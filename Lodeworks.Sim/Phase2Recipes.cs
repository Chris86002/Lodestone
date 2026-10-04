using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Lodeworks.Sim
{
    public readonly struct RecipeLeg
    {
        public RecipeLeg(ResourceKind kind, double unitsPerSecond)
        {
            if (!Enum.IsDefined(typeof(ResourceKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            Kind = kind;
            UnitsPerSecond = Phase2Numbers.Nonnegative(unitsPerSecond, nameof(unitsPerSecond));
        }
        public ResourceKind Kind { get; }
        public double UnitsPerSecond { get; }
    }

    public sealed class RecipeDefinition
    {
        public RecipeDefinition(IEnumerable<RecipeLeg> inputs, IEnumerable<RecipeLeg> outputs, double powerPerSecond)
        {
            Inputs = Aggregate(inputs, nameof(inputs));
            Outputs = Aggregate(outputs, nameof(outputs));
            PowerPerSecond = Phase2Numbers.Nonnegative(powerPerSecond, nameof(powerPerSecond));
        }
        public IReadOnlyDictionary<ResourceKind, double> Inputs { get; }
        public IReadOnlyDictionary<ResourceKind, double> Outputs { get; }
        public double PowerPerSecond { get; }
        private static IReadOnlyDictionary<ResourceKind, double> Aggregate(IEnumerable<RecipeLeg> legs, string name)
        {
            if (legs == null) throw new ArgumentNullException(name);
            var result = new Dictionary<ResourceKind, double>();
            foreach (RecipeLeg leg in legs)
            {
                if (leg.UnitsPerSecond == 0) continue;
                double next = (result.TryGetValue(leg.Kind, out double old) ? old : 0) + leg.UnitsPerSecond;
                if (!Phase2Numbers.Finite(next)) throw new ArgumentOutOfRangeException(name);
                result[leg.Kind] = next;
            }
            return new ReadOnlyDictionary<ResourceKind, double>(result);
        }
    }

    public enum RecipeLimitKind { InputShortage, OutputFull, PowerShortage }
    public readonly struct RecipeLimit
    {
        public RecipeLimit(RecipeLimitKind kind, ResourceKind? resource) { Kind = kind; Resource = resource; }
        public RecipeLimitKind Kind { get; }
        public ResourceKind? Resource { get; }
    }
    public sealed class RecipeResult
    {
        public RecipeResult(double activeTime, IDictionary<ResourceKind, double> consumed,
            IDictionary<ResourceKind, double> stored, IDictionary<ResourceKind, double> dumped,
            IEnumerable<RecipeLimit> limits, double powerUsed)
        {
            ActiveTime = activeTime;
            Consumed = new ReadOnlyDictionary<ResourceKind, double>(new Dictionary<ResourceKind, double>(consumed));
            Stored = new ReadOnlyDictionary<ResourceKind, double>(new Dictionary<ResourceKind, double>(stored));
            Dumped = new ReadOnlyDictionary<ResourceKind, double>(new Dictionary<ResourceKind, double>(dumped));
            Limits = Array.AsReadOnly(limits.ToArray());
            PowerUsed = powerUsed;
        }
        public double ActiveTime { get; }
        public IReadOnlyDictionary<ResourceKind, double> Consumed { get; }
        public IReadOnlyDictionary<ResourceKind, double> Stored { get; }
        public IReadOnlyDictionary<ResourceKind, double> Dumped { get; }
        public IReadOnlyList<RecipeLimit> Limits { get; }
        public double PowerUsed { get; }
    }

    public static class RecipeMath
    {
        public static RecipeResult Evaluate(RecipeDefinition recipe, double desiredTime,
            IReadOnlyDictionary<ResourceKind, double> availableInputs,
            IReadOnlyDictionary<ResourceKind, double> freeOutputCapacity,
            double availablePower, ISet<ResourceKind>? dumpOutputs = null)
        {
            if (recipe == null || availableInputs == null || freeOutputCapacity == null)
                throw new ArgumentNullException(nameof(recipe));
            Phase2Numbers.Nonnegative(desiredTime, nameof(desiredTime));
            Phase2Numbers.Nonnegative(availablePower, nameof(availablePower));
            ValidatePool(availableInputs);
            ValidatePool(freeOutputCapacity);
            if (recipe.Inputs.Count == 0 && recipe.Outputs.Count == 0 && recipe.PowerPerSecond == 0)
                return new RecipeResult(0, new Dictionary<ResourceKind, double>(),
                    new Dictionary<ResourceKind, double>(), new Dictionary<ResourceKind, double>(),
                    Array.Empty<RecipeLimit>(), 0);
            double limit = desiredTime;
            var constraints = new List<(double Time, RecipeLimit Limit)>();
            foreach (var input in recipe.Inputs)
            {
                double time = Value(availableInputs, input.Key) / input.Value;
                constraints.Add((time, new RecipeLimit(RecipeLimitKind.InputShortage, input.Key)));
                limit = Math.Min(limit, time);
            }
            foreach (var output in recipe.Outputs)
            {
                if (dumpOutputs != null && dumpOutputs.Contains(output.Key)) continue;
                double time = Value(freeOutputCapacity, output.Key) / output.Value;
                constraints.Add((time, new RecipeLimit(RecipeLimitKind.OutputFull, output.Key)));
                limit = Math.Min(limit, time);
            }
            if (recipe.PowerPerSecond > 0)
            {
                double time = availablePower / recipe.PowerPerSecond;
                constraints.Add((time, new RecipeLimit(RecipeLimitKind.PowerShortage, null)));
                limit = Math.Min(limit, time);
            }
            if (!Phase2Numbers.Finite(limit) || limit < 0) throw new InvalidOperationException("Invalid recipe limit.");
            var consumed = new Dictionary<ResourceKind, double>();
            var stored = new Dictionary<ResourceKind, double>();
            var dumped = new Dictionary<ResourceKind, double>();
            foreach (var input in recipe.Inputs) consumed[input.Key] = input.Value * limit;
            foreach (var output in recipe.Outputs)
            {
                double produced = output.Value * limit;
                double capacity = Value(freeOutputCapacity, output.Key);
                double saved = dumpOutputs != null && dumpOutputs.Contains(output.Key) ? Math.Min(produced, capacity) : produced;
                stored[output.Key] = saved;
                if (produced > saved) dumped[output.Key] = produced - saved;
            }
            // Reasons describe the final limiting boundary, not every candidate.
            var reasons = limit < desiredTime - Phase2Numbers.Epsilon ?
                constraints.Where(x => Math.Abs(x.Time - limit) <= Phase2Numbers.Epsilon)
                    .Select(x => x.Limit).ToArray() : Array.Empty<RecipeLimit>();
            return new RecipeResult(limit, consumed, stored, dumped, reasons, recipe.PowerPerSecond * limit);
        }
        private static void ValidatePool(IReadOnlyDictionary<ResourceKind, double> pool)
        {
            foreach (var pair in pool) Phase2Numbers.Nonnegative(pair.Value, nameof(pool));
        }
        private static double Value(IReadOnlyDictionary<ResourceKind, double> pool, ResourceKind kind) =>
            pool.TryGetValue(kind, out double value) ? value : 0;

        public static RecipeDefinition Smelt(double grade, double originYield, double originHardness,
            double crewFactor, double rateMultiplier)
        {
            if (!Phase2Numbers.Finite(grade) || grade < 0 || grade > 1 ||
                !Phase2Numbers.Finite(originYield) || originYield < 0 || originYield > 1)
                throw new ArgumentOutOfRangeException(nameof(grade));
            double factor = Factor(crewFactor, rateMultiplier);
            double metal = 0.50 * grade * originYield * factor;
            return new RecipeDefinition(new[] { new RecipeLeg(ResourceKind.Ore, 0.50 * factor) },
                new[] { new RecipeLeg(ResourceKind.Metal, metal),
                    new RecipeLeg(ResourceKind.Slag, 0.50 * factor - metal) },
                12 * Phase2Numbers.Nonnegative(originHardness, nameof(originHardness)) * factor);
        }
        public static RecipeDefinition Machine(double crewFactor, double rateMultiplier)
        {
            double factor = Factor(crewFactor, rateMultiplier);
            return new RecipeDefinition(new[] { new RecipeLeg(ResourceKind.Metal, 0.05 * factor) },
                new[] { new RecipeLeg(ResourceKind.Parts, 0.02 * factor) }, 8 * factor);
        }
        public static RecipeDefinition Pack(double crewFactor, double rateMultiplier)
        {
            double factor = Factor(crewFactor, rateMultiplier);
            return new RecipeDefinition(new[] { new RecipeLeg(ResourceKind.Metal, 0.02 * factor) },
                new[] { new RecipeLeg(ResourceKind.Supplies, 0.05 * factor) }, 4 * factor);
        }
        private static double Factor(double crew, double multiplier)
        {
            Phase2Numbers.Nonnegative(crew, nameof(crew));
            Phase2Numbers.Nonnegative(multiplier, nameof(multiplier));
            double factor = crew * multiplier;
            if (!Phase2Numbers.Finite(factor)) throw new ArgumentOutOfRangeException(nameof(multiplier));
            return factor;
        }
    }
}

