using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace Lodeworks.Sim
{
    public enum WorldEventPhase { Arrival = 0, Maintenance = 1, ExternalReplacement = 2, PlayerCommand = 3, Route = 4, Campaign = 5 }
    public enum WorldEventKind { MaterialCredit, MaterialDebit, PowerCredit, SetGeneration, SetConverterEnabled, ExternalReplacement }
    public enum WorldTimestampQuality { Exact, ObservedBounded, Unknown }
    public enum WorldAdvanceStatus { CaughtUp, CatchingUp, Blocked, RequiresRestore }
    public enum WorldPowerPriority
    {
        Maintenance = 0, Construction = 1, OreDrill = 2, Smelter = 3,
        FuelFeedPump = 4, Refinery = 5, Shop = 6
    }

    public sealed class WorldPower
    {
        public WorldPower(string endpointId, double capacity, double energy, double generationPerSecond)
        {
            EndpointId = Phase2Numbers.Id(endpointId, nameof(endpointId));
            Capacity = Phase2Numbers.Nonnegative(capacity, nameof(capacity));
            Energy = Phase2Numbers.Nonnegative(energy, nameof(energy));
            GenerationPerSecond = Phase2Numbers.Nonnegative(generationPerSecond, nameof(generationPerSecond));
            if (energy > capacity + Phase2Numbers.Epsilon) throw new ArgumentOutOfRangeException(nameof(energy));
        }
        public string EndpointId { get; }
        public double Capacity { get; }
        public double Energy { get; }
        public double GenerationPerSecond { get; }
        public WorldPower With(double? energy = null, double? generation = null) =>
            new WorldPower(EndpointId, Capacity, energy ?? Energy, generation ?? GenerationPerSecond);
    }

    // This describes a pure continuous flow. Phase 5 owns extraction and its
    // depletion law; a source with no inputs is only a test/future caller hook.
    public sealed class WorldConverter
    {
        public WorldConverter(string id, string endpointId, RecipeDefinition recipe, int powerPriority,
            bool enabled = true, string? inputOreBatchId = null, OreBatch? outputOre = null,
            IEnumerable<ResourceKind>? dumpOutputs = null)
        {
            Id = Phase2Numbers.Id(id, nameof(id));
            EndpointId = Phase2Numbers.Id(endpointId, nameof(endpointId));
            Recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
            if (powerPriority < 0) throw new ArgumentOutOfRangeException(nameof(powerPriority));
            if (inputOreBatchId != null) Phase2Numbers.Id(inputOreBatchId, nameof(inputOreBatchId));
            if (recipe.Outputs.ContainsKey(ResourceKind.Ore) != (outputOre != null))
                throw new ArgumentException("Ore production needs a provenance template.");
            if (recipe.Inputs.Keys.Intersect(recipe.Outputs.Keys).Any())
                throw new ArgumentException("A converter cannot consume its own output resource.");
            if (inputOreBatchId != null && !recipe.Inputs.ContainsKey(ResourceKind.Ore))
                throw new ArgumentException("Only an ore input can select a lot.");
            ResourceKind[] dumping = (dumpOutputs ?? Array.Empty<ResourceKind>()).Distinct().ToArray();
            if (dumping.Any(x => !recipe.Outputs.ContainsKey(x)))
                throw new ArgumentException("Dump selection must name a recipe output.");
            PowerPriority = powerPriority; Enabled = enabled;
            InputOreBatchId = inputOreBatchId; OutputOre = outputOre;
            DumpOutputs = Array.AsReadOnly(dumping);
        }
        public string Id { get; }
        public string EndpointId { get; }
        public RecipeDefinition Recipe { get; }
        public int PowerPriority { get; }
        public bool Enabled { get; }
        public string? InputOreBatchId { get; }
        public OreBatch? OutputOre { get; }
        public IReadOnlyList<ResourceKind> DumpOutputs { get; }
        public WorldConverter WithEnabled(bool enabled) => new WorldConverter(Id, EndpointId, Recipe,
            PowerPriority, enabled, InputOreBatchId, OutputOre, DumpOutputs);
    }

    public sealed class WorldEvent
    {
        public WorldEvent(string id, double ut, WorldEventPhase phase, WorldEventKind kind,
            string endpointId, double value, ResourceKind? resource = null,
            string? oreBatchId = null, OreBatch? ore = null, string? converterId = null,
            long captureSequence = 0, double? observedUT = null,
            WorldTimestampQuality timestampQuality = WorldTimestampQuality.Exact,
            double? uncertaintyStartUT = null, string? priorSnapshotId = null,
            WorldVerifiedSnapshot? replacement = null)
        {
            Id = Phase2Numbers.Id(id, nameof(id));
            UT = Phase2Numbers.Nonnegative(ut, nameof(ut));
            if (!Enum.IsDefined(typeof(WorldEventPhase), phase) ||
                !Enum.IsDefined(typeof(WorldEventKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            Phase = phase; Kind = kind;
            EndpointId = Phase2Numbers.Id(endpointId, nameof(endpointId));
            Value = Phase2Numbers.Nonnegative(value, nameof(value));
            if (captureSequence < 0) throw new ArgumentOutOfRangeException(nameof(captureSequence));
            if (!Enum.IsDefined(typeof(WorldTimestampQuality), timestampQuality))
                throw new ArgumentOutOfRangeException(nameof(timestampQuality));
            CaptureSequence = captureSequence;
            ObservedUT = Phase2Numbers.Nonnegative(observedUT ?? ut, nameof(observedUT));
            TimestampQuality = timestampQuality;
            UncertaintyStartUT = Phase2Numbers.Nonnegative(uncertaintyStartUT ?? ut, nameof(uncertaintyStartUT));
            if (UncertaintyStartUT > ObservedUT || ObservedUT + Phase2Numbers.Epsilon < ut)
                throw new ArgumentException("Observed and uncertainty times must bound the effective event time.");
            if (timestampQuality == WorldTimestampQuality.Exact &&
                (Math.Abs(ObservedUT - ut) > Phase2Numbers.Epsilon || Math.Abs(UncertaintyStartUT - ut) > Phase2Numbers.Epsilon))
                throw new ArgumentException("Exact timestamps must have a zero-width time interval.");
            if (timestampQuality == WorldTimestampQuality.ObservedBounded &&
                Math.Abs(ObservedUT - ut) > Phase2Numbers.Epsilon)
                throw new ArgumentException("Bounded observations are applied at observedUT, never backdated.");
            if (timestampQuality == WorldTimestampQuality.Unknown &&
                Math.Abs(UncertaintyStartUT - ut) > Phase2Numbers.Epsilon)
                throw new ArgumentException("Unknown-time events are held at uncertaintyStartUT.");
            if (priorSnapshotId != null) Phase2Numbers.Id(priorSnapshotId, nameof(priorSnapshotId));
            PriorSnapshotId = priorSnapshotId;
            Replacement = replacement;
            Resource = resource; OreBatchId = oreBatchId; Ore = ore; ConverterId = converterId;
            if (kind == WorldEventKind.MaterialCredit || kind == WorldEventKind.MaterialDebit)
            {
                if (resource == null || !Enum.IsDefined(typeof(ResourceKind), resource.Value))
                    throw new ArgumentException("Material event needs a resource.");
                if (resource == ResourceKind.Ore && (oreBatchId == null ||
                    (kind == WorldEventKind.MaterialCredit && (ore == null || ore.BatchId != oreBatchId))))
                    throw new ArgumentException("Ore event needs its exact lot and credit provenance.");
                if (resource != ResourceKind.Ore && (oreBatchId != null || ore != null))
                    throw new ArgumentException("Only ore events carry lot metadata.");
            }
            if (kind == WorldEventKind.SetConverterEnabled)
                Phase2Numbers.Id(converterId!, nameof(converterId));
            if (kind == WorldEventKind.ExternalReplacement &&
                (phase != WorldEventPhase.ExternalReplacement || replacement == null || captureSequence <= 0))
                throw new ArgumentException("External replacement needs its explicit phase, payload, and capture sequence.");
            if (kind != WorldEventKind.ExternalReplacement &&
                (phase == WorldEventPhase.ExternalReplacement || replacement != null || priorSnapshotId != null || captureSequence != 0))
                throw new ArgumentException("Only external replacement events carry journal metadata.");
        }
        public string Id { get; }
        public double UT { get; }
        public WorldEventPhase Phase { get; }
        public WorldEventKind Kind { get; }
        public string EndpointId { get; }
        public double Value { get; }
        public ResourceKind? Resource { get; }
        public string? OreBatchId { get; }
        public OreBatch? Ore { get; }
        public string? ConverterId { get; }
        public long CaptureSequence { get; }
        public double ObservedUT { get; }
        public WorldTimestampQuality TimestampQuality { get; }
        public double UncertaintyStartUT { get; }
        public string? PriorSnapshotId { get; }
        public WorldVerifiedSnapshot? Replacement { get; }
        public string Signature
        {
            get
            {
                string[] fields = { UT.ToString("R", CultureInfo.InvariantCulture), Phase.ToString(),
                    Kind.ToString(), EndpointId, Value.ToString("R", CultureInfo.InvariantCulture),
                    Resource?.ToString() ?? "", OreBatchId ?? "", ConverterId ?? "",
                    CaptureSequence.ToString(CultureInfo.InvariantCulture),
                    ObservedUT.ToString("R", CultureInfo.InvariantCulture), TimestampQuality.ToString(),
                    UncertaintyStartUT.ToString("R", CultureInfo.InvariantCulture), PriorSnapshotId ?? "",
                    Replacement?.Signature ?? "",
                    Ore?.BatchId ?? "", Ore?.OriginBody ?? "", Ore?.OriginBiome ?? "",
                    Ore?.Grade.ToString("R", CultureInfo.InvariantCulture) ?? "",
                    Ore?.Units.ToString("R", CultureInfo.InvariantCulture) ?? "",
                    Ore?.Provenance.ToString() ?? "", Ore?.CampaignEligible.ToString() ?? "",
                    Ore?.CreationSequence.ToString(CultureInfo.InvariantCulture) ?? "" };
                return string.Concat(fields.Select(x => x.Length.ToString(CultureInfo.InvariantCulture) + ":" + x));
            }
        }
    }

    // Immutable external observations are journal data, not mutable KSP objects.
    // Phase 4 captures these on the main thread and replaces them atomically on save switch.
    public sealed class WorldVerifiedSnapshot
    {
        public WorldVerifiedSnapshot(string snapshotId, string entityId, bool valid,
            IEnumerable<string> crewIds, string? locationId, string? orbitBodyId,
            double? semiMajorAxis = null, double? eccentricity = null, double? inclination = null,
            double? longitudeAscendingNode = null, double? argumentOfPeriapsis = null,
            double? meanAnomalyAtEpoch = null, double? epochUT = null)
        {
            SnapshotId = Phase2Numbers.Id(snapshotId, nameof(snapshotId));
            EntityId = Phase2Numbers.Id(entityId, nameof(entityId));
            Valid = valid;
            CrewIds = Array.AsReadOnly((crewIds ?? throw new ArgumentNullException(nameof(crewIds)))
                .Select(x => Phase2Numbers.Id(x, nameof(crewIds))).Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal).ToArray());
            LocationId = locationId;
            OrbitBodyId = orbitBodyId;
            if (locationId != null) Phase2Numbers.Id(locationId, nameof(locationId));
            if (orbitBodyId != null) Phase2Numbers.Id(orbitBodyId, nameof(orbitBodyId));
            SemiMajorAxis = FiniteNullable(semiMajorAxis, nameof(semiMajorAxis));
            Eccentricity = FiniteNullable(eccentricity, nameof(eccentricity));
            Inclination = FiniteNullable(inclination, nameof(inclination));
            LongitudeAscendingNode = FiniteNullable(longitudeAscendingNode, nameof(longitudeAscendingNode));
            ArgumentOfPeriapsis = FiniteNullable(argumentOfPeriapsis, nameof(argumentOfPeriapsis));
            MeanAnomalyAtEpoch = FiniteNullable(meanAnomalyAtEpoch, nameof(meanAnomalyAtEpoch));
            EpochUT = FiniteNullable(epochUT, nameof(epochUT));
            if (new[] { SemiMajorAxis, Eccentricity, Inclination, LongitudeAscendingNode,
                ArgumentOfPeriapsis, MeanAnomalyAtEpoch, EpochUT }.Any(x => x.HasValue) && orbitBodyId == null)
                throw new ArgumentException("Orbital elements require a reference body.");
        }
        private static double? FiniteNullable(double? value, string name)
        {
            if (value.HasValue && !Phase2Numbers.Finite(value.Value)) throw new ArgumentOutOfRangeException(name);
            return value;
        }
        public string SnapshotId { get; }
        public string EntityId { get; }
        public bool Valid { get; }
        public IReadOnlyList<string> CrewIds { get; }
        public string? LocationId { get; }
        public string? OrbitBodyId { get; }
        public double? SemiMajorAxis { get; }
        public double? Eccentricity { get; }
        public double? Inclination { get; }
        public double? LongitudeAscendingNode { get; }
        public double? ArgumentOfPeriapsis { get; }
        public double? MeanAnomalyAtEpoch { get; }
        public double? EpochUT { get; }
        public string Signature => string.Concat(new[] { SnapshotId, EntityId, Valid.ToString(),
            string.Concat(CrewIds.Select(x => x.Length.ToString(CultureInfo.InvariantCulture) + ":" + x)),
            LocationId ?? "", OrbitBodyId ?? "",
            SemiMajorAxis?.ToString("R", CultureInfo.InvariantCulture) ?? "",
            Eccentricity?.ToString("R", CultureInfo.InvariantCulture) ?? "",
            Inclination?.ToString("R", CultureInfo.InvariantCulture) ?? "",
            LongitudeAscendingNode?.ToString("R", CultureInfo.InvariantCulture) ?? "",
            ArgumentOfPeriapsis?.ToString("R", CultureInfo.InvariantCulture) ?? "",
            MeanAnomalyAtEpoch?.ToString("R", CultureInfo.InvariantCulture) ?? "",
            EpochUT?.ToString("R", CultureInfo.InvariantCulture) ?? "" }
            .Select(x => x.Length.ToString(CultureInfo.InvariantCulture) + ":" + x));
    }

    public sealed class WorldPhaseFrontier
    {
        public WorldPhaseFrontier(double ut, WorldEventPhase phase, long captureSequence, string eventId,
            bool boundaryClosed = false)
        {
            if (!Enum.IsDefined(typeof(WorldEventPhase), phase)) throw new ArgumentOutOfRangeException(nameof(phase));
            if (captureSequence < 0) throw new ArgumentOutOfRangeException(nameof(captureSequence));
            UT = Phase2Numbers.Nonnegative(ut, nameof(ut)); Phase = phase;
            CaptureSequence = captureSequence; EventId = Phase2Numbers.Id(eventId, nameof(eventId));
            BoundaryClosed = boundaryClosed;
        }
        public double UT { get; }
        public WorldEventPhase Phase { get; }
        public long CaptureSequence { get; }
        public string EventId { get; }
        public bool BoundaryClosed { get; }
    }

    public sealed class WorldEventReceipt
    {
        public WorldEventReceipt(string id, string signature)
        { Id = Phase2Numbers.Id(id, nameof(id)); Signature = signature ?? throw new ArgumentNullException(nameof(signature)); }
        public string Id { get; }
        public string Signature { get; }
    }
    public sealed class WorldScheduleRejection
    {
        public WorldScheduleRejection(string eventId, string signature, string reason)
        {
            EventId = Phase2Numbers.Id(eventId, nameof(eventId));
            Signature = signature ?? throw new ArgumentNullException(nameof(signature));
            Reason = Phase2Numbers.Id(reason, nameof(reason));
        }
        public string EventId { get; }
        public string Signature { get; }
        public string Reason { get; }
    }
    public sealed class WorldDumpTotal
    {
        public WorldDumpTotal(string converterId, ResourceKind resource, double units)
        {
            ConverterId = Phase2Numbers.Id(converterId, nameof(converterId));
            if (!Enum.IsDefined(typeof(ResourceKind), resource)) throw new ArgumentOutOfRangeException(nameof(resource));
            Resource = resource; Units = Phase2Numbers.Nonnegative(units, nameof(units));
        }
        public string ConverterId { get; }
        public ResourceKind Resource { get; }
        public double Units { get; }
    }

    public sealed class WorldSnapshot
    {
        public const int CurrentVersion = 2;
        public WorldSnapshot(double cursorUT, double targetUT, MaterialBookSnapshot materials,
            IEnumerable<WorldPower> power, IEnumerable<WorldConverter> converters,
            IEnumerable<WorldEvent> pendingEvents, IEnumerable<WorldEventReceipt> completedEvents,
            IEnumerable<WorldDumpTotal> dumped, long captureSequence = 0,
            WorldPhaseFrontier? frontier = null, IEnumerable<WorldVerifiedSnapshot>? verifiedSnapshots = null,
            IEnumerable<string>? currentSnapshotIds = null, double maxTimestampUncertaintyUT = 0,
            IEnumerable<WorldScheduleRejection>? rejectedEvents = null)
        {
            CursorUT = Phase2Numbers.Nonnegative(cursorUT, nameof(cursorUT));
            TargetUT = Phase2Numbers.Nonnegative(targetUT, nameof(targetUT));
            if (targetUT < cursorUT) throw new ArgumentOutOfRangeException(nameof(targetUT));
            Materials = materials ?? throw new ArgumentNullException(nameof(materials));
            Power = Array.AsReadOnly(power?.ToArray() ?? throw new ArgumentNullException(nameof(power)));
            Converters = Array.AsReadOnly(converters?.ToArray() ?? throw new ArgumentNullException(nameof(converters)));
            PendingEvents = Array.AsReadOnly(pendingEvents?.ToArray() ?? throw new ArgumentNullException(nameof(pendingEvents)));
            CompletedEvents = Array.AsReadOnly(completedEvents?.ToArray() ?? throw new ArgumentNullException(nameof(completedEvents)));
            Dumped = Array.AsReadOnly(dumped?.ToArray() ?? throw new ArgumentNullException(nameof(dumped)));
            if (captureSequence < 0) throw new ArgumentOutOfRangeException(nameof(captureSequence));
            CaptureSequence = captureSequence; Frontier = frontier;
            MaxTimestampUncertaintyUT = Phase2Numbers.Nonnegative(maxTimestampUncertaintyUT, nameof(maxTimestampUncertaintyUT));
            VerifiedSnapshots = Array.AsReadOnly((verifiedSnapshots ?? Array.Empty<WorldVerifiedSnapshot>()).ToArray());
            CurrentSnapshotIds = Array.AsReadOnly((currentSnapshotIds ?? Array.Empty<string>()).ToArray());
            RejectedEvents = Array.AsReadOnly((rejectedEvents ?? Array.Empty<WorldScheduleRejection>()).ToArray());
        }
        public int Version => CurrentVersion;
        public double CursorUT { get; }
        public double TargetUT { get; }
        public MaterialBookSnapshot Materials { get; }
        public IReadOnlyList<WorldPower> Power { get; }
        public IReadOnlyList<WorldConverter> Converters { get; }
        public IReadOnlyList<WorldEvent> PendingEvents { get; }
        public IReadOnlyList<WorldEventReceipt> CompletedEvents { get; }
        public IReadOnlyList<string> CompletedEventIds => Array.AsReadOnly(CompletedEvents.Select(x => x.Id).ToArray());
        public IReadOnlyList<WorldDumpTotal> Dumped { get; }
        public long CaptureSequence { get; }
        public WorldPhaseFrontier? Frontier { get; }
        public IReadOnlyList<WorldVerifiedSnapshot> VerifiedSnapshots { get; }
        public IReadOnlyList<string> CurrentSnapshotIds { get; }
        public double MaxTimestampUncertaintyUT { get; }
        public IReadOnlyList<WorldScheduleRejection> RejectedEvents { get; }
    }

    public sealed class WorldAdvanceResult
    {
        public WorldAdvanceResult(WorldAdvanceStatus status, double cursorUT, double targetUT, string reason)
        { Status = status; CursorUT = cursorUT; TargetUT = targetUT; Reason = reason; }
        public WorldAdvanceStatus Status { get; }
        public double CursorUT { get; }
        public double TargetUT { get; }
        public string Reason { get; }
    }

    // One pure clock owner. Every endpoint shares its material inventory and
    // owned power among converters over each solved open interval.
    public sealed class WorldSimulator
    {
        private MaterialCommandBook materials;
        private readonly Dictionary<string, WorldPower> power = new Dictionary<string, WorldPower>(StringComparer.Ordinal);
        private readonly Dictionary<string, WorldConverter> converters = new Dictionary<string, WorldConverter>(StringComparer.Ordinal);
        private readonly Dictionary<string, WorldEvent> events = new Dictionary<string, WorldEvent>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> completed = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<WorldScheduleRejection> rejectedEvents = new List<WorldScheduleRejection>();
        private readonly Dictionary<string, WorldVerifiedSnapshot> verifiedSnapshots = new Dictionary<string, WorldVerifiedSnapshot>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> currentSnapshotIds = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<(string Id, ResourceKind Kind), double> dumped =
            new Dictionary<(string Id, ResourceKind Kind), double>();
        private double cursorUT;
        private double targetUT;
        private long captureSequence;
        private WorldPhaseFrontier? frontier;
        private readonly double maxTimestampUncertaintyUT;
        public string LastScheduleDiagnostic { get; private set; } = string.Empty;
        public WorldSimulator(double startUT, MaterialCommandBook materials, double maxTimestampUncertaintyUT = 0)
        {
            cursorUT = targetUT = Phase2Numbers.Nonnegative(startUT, nameof(startUT));
            this.maxTimestampUncertaintyUT = Phase2Numbers.Nonnegative(maxTimestampUncertaintyUT, nameof(maxTimestampUncertaintyUT));
            this.materials = materials == null ? throw new ArgumentNullException(nameof(materials)) :
                MaterialCommandBook.Restore(materials.Snapshot());
        }
        public double CursorUT => cursorUT;
        public double TargetUT => targetUT;
        public bool CatchingUp => cursorUT < targetUT || events.Values.Any(x => x.UT <= cursorUT);
        public MaterialBookSnapshot Materials => materials.Snapshot();
        public WorldPower GetPower(string endpointId) => power[Phase2Numbers.Id(endpointId, nameof(endpointId))];
        public double GetDumped(string converterId, ResourceKind kind) =>
            dumped.TryGetValue((Phase2Numbers.Id(converterId, nameof(converterId)), kind), out double value) ? value : 0;
        public void RegisterPower(WorldPower state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            materials.GetSnapshot(state.EndpointId);
            if (power.ContainsKey(state.EndpointId)) throw new ArgumentException("Duplicate power endpoint.");
            power.Add(state.EndpointId, state);
        }
        public void RegisterConverter(WorldConverter converter)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            if (!power.ContainsKey(converter.EndpointId)) throw new ArgumentException("Unknown power endpoint.");
            if (converters.ContainsKey(converter.Id)) throw new ArgumentException("Duplicate converter ID.");
            converters.Add(converter.Id, converter);
            if (HasMaterialCycle(converter.EndpointId))
            {
                converters.Remove(converter.Id);
                throw new ArgumentException("Cyclic converter dependencies require a later explicit flow policy.");
            }
        }
        private bool HasMaterialCycle(string endpointId)
        {
            WorldConverter[] local = converters.Values.Where(x => x.EndpointId == endpointId).ToArray();
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            bool Walk(WorldConverter current)
            {
                if (visiting.Contains(current.Id)) return true;
                if (visited.Contains(current.Id)) return false;
                visiting.Add(current.Id);
                foreach (WorldConverter downstream in local.Where(x => x.Id != current.Id &&
                    current.Recipe.Outputs.Keys.Except(current.DumpOutputs)
                        .Intersect(x.Recipe.Inputs.Keys).Any()))
                    if (Walk(downstream)) return true;
                visiting.Remove(current.Id); visited.Add(current.Id);
                return false;
            }
            return local.Any(Walk);
        }
        public bool Schedule(WorldEvent item)
        {
            LastScheduleDiagnostic = string.Empty;
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (completed.TryGetValue(item.Id, out string signature))
            {
                if (signature != item.Signature)
                { ConsumeCaptureSequence(item); return Reject(item, "conflicting-reused-completed-event-id"); }
                return false;
            }
            if (events.TryGetValue(item.Id, out WorldEvent existing))
            {
                if (existing.Signature != item.Signature)
                { ConsumeCaptureSequence(item); return Reject(item, "conflicting-reused-pending-event-id"); }
                return false;
            }
            // Consume every newly observed external sequence even when quarantined, so a rejected
            // observation cannot make a later sequence appear to reuse an older capture number.
            if (item.Kind == WorldEventKind.ExternalReplacement)
            {
                if (item.CaptureSequence <= captureSequence) return Reject(item, "non-monotonic-capture-sequence");
                captureSequence = item.CaptureSequence;
            }
            if (!power.ContainsKey(item.EndpointId)) return Reject(item, "unknown-event-endpoint");
            if (item.UT < cursorUT) return Reject(item, "event-before-cursor");
            if (frontier != null && Math.Abs(item.UT - frontier.UT) <= Phase2Numbers.Epsilon &&
                CompareFrontier(item, frontier) <= 0)
                return Reject(item, "late-event-behind-completed-equal-ut-frontier");
            if (item.Kind == WorldEventKind.ExternalReplacement)
            {
                if (item.Replacement == null || (item.PriorSnapshotId != null && !HasPriorSnapshot(item.PriorSnapshotId,
                    item.Replacement.EntityId)))
                    return Reject(item, "unknown-or-mismatched-prior-snapshot");
                if (verifiedSnapshots.TryGetValue(item.Replacement.SnapshotId, out WorldVerifiedSnapshot existingSnapshot) &&
                    existingSnapshot.Signature != item.Replacement.Signature)
                    return Reject(item, "conflicting-snapshot-id");
                if (events.Values.Any(x => x.Kind == WorldEventKind.ExternalReplacement &&
                    x.Replacement?.SnapshotId == item.Replacement.SnapshotId &&
                    x.Replacement.Signature != item.Replacement.Signature))
                    return Reject(item, "conflicting-pending-snapshot-id");
            }
            events.Add(item.Id, item);
            return true;
        }
        private bool Reject(WorldEvent item, string reason)
        {
            LastScheduleDiagnostic = reason;
            if (!rejectedEvents.Any(x => x.EventId == item.Id && x.Signature == item.Signature))
                rejectedEvents.Add(new WorldScheduleRejection(item.Id, item.Signature, reason));
            return false;
        }
        private void ConsumeCaptureSequence(WorldEvent item)
        {
            if (item.Kind == WorldEventKind.ExternalReplacement && item.CaptureSequence > captureSequence)
                captureSequence = item.CaptureSequence;
        }
        private bool HasPriorSnapshot(string snapshotId, string entityId) =>
            (verifiedSnapshots.TryGetValue(snapshotId, out WorldVerifiedSnapshot prior) && prior.EntityId == entityId) ||
            events.Values.Any(x => x.Kind == WorldEventKind.ExternalReplacement &&
                x.Replacement?.SnapshotId == snapshotId && x.Replacement.EntityId == entityId);
        private static int CompareFrontier(WorldEvent item, WorldPhaseFrontier frontier)
        {
            if (frontier.BoundaryClosed) return -1;
            int phase = item.Phase.CompareTo(frontier.Phase);
            if (phase != 0) return phase;
            int sequence = item.Phase == WorldEventPhase.ExternalReplacement ?
                item.CaptureSequence.CompareTo(frontier.CaptureSequence) : 0;
            return sequence != 0 ? sequence : string.CompareOrdinal(item.Id, frontier.EventId);
        }
        public WorldSnapshot Snapshot() => new WorldSnapshot(cursorUT, targetUT, materials.Snapshot(),
            power.Values.OrderBy(x => x.EndpointId, StringComparer.Ordinal),
            converters.Values.OrderBy(x => x.Id, StringComparer.Ordinal),
            events.Values.OrderBy(x => x.UT).ThenBy(x => x.Phase).ThenBy(x => x.Id, StringComparer.Ordinal),
            completed.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new WorldEventReceipt(x.Key, x.Value)),
            dumped.OrderBy(x => x.Key.Id, StringComparer.Ordinal).ThenBy(x => x.Key.Kind)
                .Select(x => new WorldDumpTotal(x.Key.Id, x.Key.Kind, x.Value)), captureSequence, frontier,
            verifiedSnapshots.Values.OrderBy(x => x.SnapshotId, StringComparer.Ordinal),
            currentSnapshotIds.Values.OrderBy(x => x, StringComparer.Ordinal), maxTimestampUncertaintyUT,
            rejectedEvents.OrderBy(x => x.EventId, StringComparer.Ordinal).ThenBy(x => x.Signature, StringComparer.Ordinal));
        public static WorldSimulator Restore(WorldSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Version != WorldSnapshot.CurrentVersion)
                throw new ArgumentException("Unsupported world snapshot.", nameof(snapshot));
            var world = new WorldSimulator(snapshot.CursorUT, MaterialCommandBook.Restore(snapshot.Materials),
                snapshot.MaxTimestampUncertaintyUT);
            world.targetUT = snapshot.TargetUT;
            world.frontier = snapshot.Frontier;
            foreach (WorldVerifiedSnapshot verified in snapshot.VerifiedSnapshots)
                if (world.verifiedSnapshots.ContainsKey(verified.SnapshotId))
                    throw new ArgumentException("Duplicate verified snapshot.", nameof(snapshot));
                else world.verifiedSnapshots.Add(verified.SnapshotId, verified);
            foreach (string id in snapshot.CurrentSnapshotIds)
            {
                if (!world.verifiedSnapshots.TryGetValue(id, out WorldVerifiedSnapshot verified) ||
                    world.currentSnapshotIds.ContainsKey(verified.EntityId))
                    throw new ArgumentException("Invalid current verified snapshot.", nameof(snapshot));
                world.currentSnapshotIds.Add(verified.EntityId, id);
            }
            foreach (WorldScheduleRejection rejection in snapshot.RejectedEvents)
            {
                if (world.rejectedEvents.Any(x => x.EventId == rejection.EventId && x.Signature == rejection.Signature))
                    throw new ArgumentException("Duplicate rejected event record.", nameof(snapshot));
                world.rejectedEvents.Add(rejection);
            }
            foreach (WorldPower state in snapshot.Power) world.RegisterPower(state);
            foreach (WorldConverter converter in snapshot.Converters) world.RegisterConverter(converter);
            foreach (WorldEventReceipt receipt in snapshot.CompletedEvents)
                if (world.completed.ContainsKey(receipt.Id))
                    throw new ArgumentException("Duplicate event receipt.", nameof(snapshot));
                else world.completed.Add(receipt.Id, receipt.Signature);
            foreach (WorldEvent item in snapshot.PendingEvents.OrderBy(x => x.UT).ThenBy(x => x.Phase)
                .ThenBy(x => x.Phase == WorldEventPhase.ExternalReplacement ? x.CaptureSequence : 0)
                .ThenBy(x => x.Id, StringComparer.Ordinal))
                if (!world.Schedule(item)) throw new ArgumentException("Duplicate pending event.", nameof(snapshot));
            world.captureSequence = Math.Max(world.captureSequence, snapshot.CaptureSequence);
            foreach (WorldDumpTotal total in snapshot.Dumped)
            {
                if (!world.converters.ContainsKey(total.ConverterId) ||
                    world.dumped.ContainsKey((total.ConverterId, total.Resource)))
                    throw new ArgumentException("Invalid dumped-output total.", nameof(snapshot));
                world.dumped.Add((total.ConverterId, total.Resource), total.Units);
            }
            return world;
        }
        public WorldAdvanceResult AdvanceForFrame(double requestedUT, double maxMilliseconds = 4)
        {
            if (!Phase2Numbers.Finite(maxMilliseconds) || maxMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxMilliseconds));
            var timer = Stopwatch.StartNew();
            WorldAdvanceResult result;
            do
            {
                result = Advance(requestedUT, 1);
            } while (result.Status == WorldAdvanceStatus.CatchingUp && timer.Elapsed.TotalMilliseconds < maxMilliseconds);
            return result;
        }
        public WorldAdvanceResult Advance(double requestedUT, int maxBoundaries = int.MaxValue)
        {
            Phase2Numbers.Nonnegative(requestedUT, nameof(requestedUT));
            if (maxBoundaries <= 0) throw new ArgumentOutOfRangeException(nameof(maxBoundaries));
            if (requestedUT < cursorUT)
                return Result(WorldAdvanceStatus.RequiresRestore, "UT rollback requires a saved world snapshot.");
            targetUT = Math.Max(targetUT, requestedUT);
            for (int boundary = 0; boundary < maxBoundaries; boundary++)
            {
                WorldEvent? unsafeTime = events.Values.Where(x =>
                    (x.TimestampQuality == WorldTimestampQuality.Unknown ||
                    (x.TimestampQuality == WorldTimestampQuality.ObservedBounded &&
                     x.ObservedUT - x.UncertaintyStartUT > maxTimestampUncertaintyUT + Phase2Numbers.Epsilon)) &&
                    x.UncertaintyStartUT <= cursorUT)
                    .OrderBy(x => x.UncertaintyStartUT).ThenBy(x => x.CaptureSequence).ThenBy(x => x.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (unsafeTime != null)
                    return Result(WorldAdvanceStatus.Blocked, "Event time uncertainty requires reconciliation at UT " +
                        unsafeTime.UncertaintyStartUT.ToString("R", CultureInfo.InvariantCulture) + ": " + unsafeTime.Id);
                WorldEvent? due = events.Values.Where(x => x.UT <= cursorUT)
                    .OrderBy(x => x.UT).ThenBy(x => x.Phase)
                    .ThenBy(x => x.Phase == WorldEventPhase.ExternalReplacement ? x.CaptureSequence : 0)
                    .ThenBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
                if (due != null)
                {
                    if (due.TimestampQuality == WorldTimestampQuality.Unknown)
                        return Result(WorldAdvanceStatus.Blocked, "Unknown event time requires reconciliation: " + due.Id);
                    if (due.TimestampQuality == WorldTimestampQuality.ObservedBounded &&
                        due.ObservedUT - due.UncertaintyStartUT > maxTimestampUncertaintyUT + Phase2Numbers.Epsilon)
                        return Result(WorldAdvanceStatus.Blocked, "Event timestamp uncertainty exceeds the configured bound: " + due.Id);
                    if (!ApplyEvent(due)) return Result(WorldAdvanceStatus.Blocked, "Event blocked: " + due.Id);
                    events.Remove(due.Id); completed.Add(due.Id, due.Signature);
                    frontier = new WorldPhaseFrontier(cursorUT, due.Phase, due.CaptureSequence, due.Id);
                    continue;
                }
                if (cursorUT >= targetUT)
                {
                    CloseFrontier();
                    return Result(WorldAdvanceStatus.CaughtUp, string.Empty);
                }
                double nextEvent = targetUT;
                foreach (WorldEvent pending in events.Values)
                {
                    bool unsafePending = pending.TimestampQuality == WorldTimestampQuality.Unknown ||
                        (pending.TimestampQuality == WorldTimestampQuality.ObservedBounded &&
                         pending.ObservedUT - pending.UncertaintyStartUT > maxTimestampUncertaintyUT + Phase2Numbers.Epsilon);
                    nextEvent = Math.Min(nextEvent, unsafePending ? pending.UncertaintyStartUT : pending.UT);
                }
                if (nextEvent <= cursorUT) throw new InvalidOperationException("Nonprogressing event boundary.");
                var flows = SolveFlows();
                double step = Math.Min(nextEvent - cursorUT, TimeToBoundary(flows));
                if (step <= 0 || !Phase2Numbers.Finite(step))
                    throw new InvalidOperationException("Nonprogressing world interval.");
                CommitInterval(flows, step);
                cursorUT = Math.Min(nextEvent, cursorUT + step);
            }
            bool caughtUp = cursorUT >= targetUT && !events.Values.Any(x => x.UT <= cursorUT);
            if (caughtUp && targetUT == cursorUT) CloseFrontier();
            return Result(caughtUp ? WorldAdvanceStatus.CaughtUp : WorldAdvanceStatus.CatchingUp, string.Empty);
        }
        private void CloseFrontier()
        {
            if (targetUT == cursorUT && (frontier == null || frontier.UT != cursorUT ||
                !frontier.BoundaryClosed))
                frontier = new WorldPhaseFrontier(cursorUT, WorldEventPhase.Campaign, long.MaxValue, "boundary-closed", true);
        }
        private WorldAdvanceResult Result(WorldAdvanceStatus status, string reason) =>
            new WorldAdvanceResult(status, cursorUT, targetUT, reason);
        private bool ApplyEvent(WorldEvent item)
        {
            if (item.Kind == WorldEventKind.MaterialCredit || item.Kind == WorldEventKind.MaterialDebit)
                return materials.ApplySimulationDeltas(item.EndpointId, new[] {
                    new SimulationMaterialDelta(item.Resource!.Value,
                        item.Kind == WorldEventKind.MaterialCredit ? item.Value : -item.Value,
                        item.OreBatchId, item.Ore) });
            WorldPower state = power[item.EndpointId];
            if (item.Kind == WorldEventKind.PowerCredit)
            {
                if (state.Energy + item.Value > state.Capacity + Phase2Numbers.Epsilon) return false;
                power[item.EndpointId] = state.With(energy: Math.Min(state.Capacity, state.Energy + item.Value));
            }
            else if (item.Kind == WorldEventKind.SetGeneration)
                power[item.EndpointId] = state.With(generation: item.Value);
            else if (item.Kind == WorldEventKind.SetConverterEnabled)
            {
                if (!converters.TryGetValue(item.ConverterId!, out WorldConverter converter) ||
                    converter.EndpointId != item.EndpointId) return false;
                converters[item.ConverterId!] = converter.WithEnabled(item.Value > 0);
            }
            else if (item.Kind == WorldEventKind.ExternalReplacement)
            {
                WorldVerifiedSnapshot replacement = item.Replacement!;
                if (currentSnapshotIds.TryGetValue(replacement.EntityId, out string currentId))
                {
                    if (item.PriorSnapshotId != currentId) return false;
                }
                else if (item.PriorSnapshotId != null) return false;
                if (verifiedSnapshots.TryGetValue(replacement.SnapshotId, out WorldVerifiedSnapshot existing) &&
                    existing.Signature != replacement.Signature) return false;
                verifiedSnapshots[replacement.SnapshotId] = replacement;
                currentSnapshotIds[replacement.EntityId] = replacement.SnapshotId;
            }
            return true;
        }

        private sealed class Flow
        {
            public Flow(WorldConverter converter, string? oreInput)
            { Converter = converter; OreInput = oreInput; Fraction = converter.Enabled ? 1 : 0; }
            public WorldConverter Converter { get; }
            public string? OreInput { get; }
            public double Fraction { get; set; }
            public Dictionary<ResourceKind, double> StoredDumpFraction { get; } =
                new Dictionary<ResourceKind, double>();
        }
        private List<Flow> SolveFlows()
        {
            var flows = converters.Values.OrderBy(x => x.Id, StringComparer.Ordinal).Select(converter =>
            {
                InventorySnapshot snapshot = materials.GetSnapshot(converter.EndpointId);
                string? lot = converter.InputOreBatchId;
                if (converter.Recipe.Inputs.ContainsKey(ResourceKind.Ore) && lot == null)
                    lot = snapshot.Ore.OrderBy(x => x.CreationSequence).ThenBy(x => x.BatchId, StringComparer.Ordinal)
                        .FirstOrDefault()?.BatchId ?? converters.Values.Where(x => x.EndpointId == converter.EndpointId)
                        .Select(x => x.OutputOre?.BatchId).Where(x => x != null)
                        .OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault();
                return new Flow(converter, lot);
            }).ToList();
            foreach (Flow flow in flows.Where(x => x.Converter.Recipe.Inputs.ContainsKey(ResourceKind.Ore) && x.OreInput == null))
                flow.Fraction = 0;
            // Fractions only fall. A bounded fixed point protects every zero-stock,
            // full-capacity and empty-battery boundary without stepping through seconds.
            for (int iteration = 0; iteration < flows.Count * 4 + 4; iteration++)
            {
                bool changed = false;
                foreach (string endpoint in power.Keys.OrderBy(x => x, StringComparer.Ordinal))
                {
                    var group = flows.Where(x => x.Converter.EndpointId == endpoint).ToList();
                    WorldPower battery = power[endpoint];
                    if (battery.Energy <= Phase2Numbers.Epsilon)
                        changed |= Limit(group, x => x.Converter.Recipe.PowerPerSecond,
                            battery.GenerationPerSecond);
                    MaterialInventory inventory = MaterialInventory.Restore(materials.GetSnapshot(endpoint));
                    ReservationBook book = ReservationView();
                    ConfigureDumpStorage(group, inventory, book);
                    foreach (ResourceKind kind in Enum.GetValues(typeof(ResourceKind)))
                    {
                        if (kind == ResourceKind.Ore)
                        {
                            var lots = inventory.Ore.Select(x => x.BatchId).Concat(group.Select(x => x.OreInput))
                                .Concat(group.Select(x => x.Converter.OutputOre?.BatchId)).Where(x => x != null)
                                .Distinct().OrderBy(x => x, StringComparer.Ordinal);
                            foreach (string lot in lots!)
                                if (book.AvailableMatter(inventory, kind, lot) <= Phase2Numbers.Epsilon)
                                    changed |= LimitNet(group, kind, lot, false);
                        }
                        else if (book.AvailableMatter(inventory, kind) <= Phase2Numbers.Epsilon)
                            changed |= LimitNet(group, kind, null, false);
                        if (book.AvailableCapacity(inventory, kind) <= Phase2Numbers.Epsilon)
                            changed |= LimitNet(group, kind, null, true);
                    }
                    foreach (ProtectedFloor floor in book.Floors.Where(x => x.EndpointId == endpoint &&
                        x.Kind == ResourceKind.Ore))
                        if (FloorSlack(floor, inventory, book) <= Phase2Numbers.Epsilon)
                            changed |= Limit(group, x => FloorInput(x, floor, inventory, group),
                                group.Sum(x => x.Fraction * FloorOutput(x, floor)));
                }
                if (!changed) return flows;
            }
            throw new InvalidOperationException("World flow constraints did not converge.");
        }
        private ReservationBook ReservationView() => ReservationBook.Restore(materials.ReservationSnapshot,
            materials.Snapshot().Inventories.ToDictionary(x => x.EndpointId,
                x => MaterialInventory.Restore(x), StringComparer.Ordinal));
        private static double Input(Flow flow, ResourceKind kind, string? lot) =>
            flow.Converter.Recipe.Inputs.TryGetValue(kind, out double rate) &&
            (kind != ResourceKind.Ore || lot == null || flow.OreInput == lot) ? rate : 0;
        private static double Output(Flow flow, ResourceKind kind, string? lot) =>
            flow.Converter.Recipe.Outputs.TryGetValue(kind, out double rate) &&
            (kind != ResourceKind.Ore || lot == null || flow.Converter.OutputOre?.BatchId == lot) ?
                rate * (flow.Converter.DumpOutputs.Contains(kind) ?
                    flow.StoredDumpFraction[kind] : 1) : 0;
        private static double Rate(IReadOnlyDictionary<ResourceKind, double> rates, ResourceKind kind) =>
            rates.TryGetValue(kind, out double value) ? value : 0;
        private static void ConfigureDumpStorage(List<Flow> group, MaterialInventory inventory,
            ReservationBook book)
        {
            foreach (ResourceKind kind in group.SelectMany(x => x.Converter.DumpOutputs).Distinct())
            {
                double produced = group.Where(x => x.Converter.DumpOutputs.Contains(kind))
                    .Sum(x => x.Fraction * Rate(x.Converter.Recipe.Outputs, kind));
                double consumed = group.Sum(x => x.Fraction *
                    Rate(x.Converter.Recipe.Inputs, kind));
                double factor = book.AvailableCapacity(inventory, kind) > Phase2Numbers.Epsilon ||
                    produced <= 0 ? 1 : Math.Min(1, consumed / produced);
                foreach (Flow flow in group.Where(x => x.Converter.DumpOutputs.Contains(kind)))
                    flow.StoredDumpFraction[kind] = factor;
            }
        }
        private static double FloorInput(Flow flow, ProtectedFloor floor,
            MaterialInventory inventory, List<Flow> group)
        {
            OreBatch? lot = inventory.Ore.FirstOrDefault(x => x.BatchId == flow.OreInput) ??
                group.Select(x => x.Converter.OutputOre).FirstOrDefault(x => x?.BatchId == flow.OreInput);
            return lot != null && floor.Matches(lot) ? Input(flow, ResourceKind.Ore, flow.OreInput) : 0;
        }
        private static double FloorOutput(Flow flow, ProtectedFloor floor) =>
            flow.Converter.OutputOre != null && floor.Matches(flow.Converter.OutputOre) ?
                Output(flow, ResourceKind.Ore, flow.Converter.OutputOre.BatchId) : 0;
        private static double FloorSlack(ProtectedFloor floor, MaterialInventory inventory, ReservationBook book)
        {
            double physical = inventory.Ore.Where(floor.Matches).Sum(x => x.Units);
            double claimed = book.Records.Where(x => x.State == ReservationState.Committed &&
                x.Kind == ReservationKind.Matter && x.EndpointId == inventory.EndpointId &&
                x.Resource == ResourceKind.Ore && inventory.Ore.Any(lot =>
                    lot.BatchId == x.OreBatchId && floor.Matches(lot))).Sum(x => x.Quantity);
            return Math.Max(0, physical - claimed - floor.EffectiveQuantity);
        }
        private static bool LimitNet(List<Flow> flows, ResourceKind kind, string? lot, bool full)
        {
            double supply = flows.Sum(x => x.Fraction * (full ? Input(x, kind, lot) : Output(x, kind, lot)));
            return Limit(flows, x => full ? Output(x, kind, lot) : Input(x, kind, lot), supply);
        }
        private static bool Limit(List<Flow> flows, Func<Flow, double> demand, double available)
        {
            double wanted = flows.Sum(x => x.Fraction * demand(x));
            if (wanted <= available + 1e-12) return false;
            double left = Math.Max(0, available);
            bool changed = false;
            foreach (var priority in flows.Where(x => x.Fraction > 0 && demand(x) > 0)
                .GroupBy(x => x.Converter.PowerPriority).OrderBy(x => x.Key))
            {
                double groupDemand = priority.Sum(x => x.Fraction * demand(x));
                double factor = groupDemand <= left ? 1 : left / groupDemand;
                if (factor < 1)
                {
                    foreach (Flow flow in priority) flow.Fraction *= factor;
                    changed = true;
                }
                left = Math.Max(0, left - groupDemand * factor);
            }
            return changed;
        }
        private double TimeToBoundary(List<Flow> flows)
        {
            double time = double.PositiveInfinity;
            foreach (string endpoint in power.Keys)
            {
                WorldPower battery = power[endpoint];
                var group = flows.Where(x => x.Converter.EndpointId == endpoint).ToArray();
                double draw = group.Sum(x => x.Fraction * x.Converter.Recipe.PowerPerSecond);
                double netPower = battery.GenerationPerSecond - draw;
                if (netPower < -1e-12 && battery.Energy > Phase2Numbers.Epsilon)
                    time = Math.Min(time, battery.Energy / -netPower);
                if (netPower > 1e-12 && battery.Energy < battery.Capacity - Phase2Numbers.Epsilon)
                    time = Math.Min(time, (battery.Capacity - battery.Energy) / netPower);
                MaterialInventory inventory = MaterialInventory.Restore(materials.GetSnapshot(endpoint));
                ReservationBook book = ReservationView();
                foreach (ResourceKind kind in Enum.GetValues(typeof(ResourceKind)))
                {
                    double net = group.Sum(x => x.Fraction * (Output(x, kind, null) - Input(x, kind, x.OreInput)));
                    if (net > 1e-12 && book.AvailableCapacity(inventory, kind) > Phase2Numbers.Epsilon)
                        time = Math.Min(time, book.AvailableCapacity(inventory, kind) / net);
                    if (kind != ResourceKind.Ore && net < -1e-12 &&
                        book.AvailableMatter(inventory, kind) > Phase2Numbers.Epsilon)
                        time = Math.Min(time, book.AvailableMatter(inventory, kind) / -net);
                    if (kind == ResourceKind.Ore)
                        foreach (string lot in inventory.Ore.Select(x => x.BatchId)
                            .Concat(group.Select(x => x.OreInput)).Where(x => x != null)
                            .Select(x => x!).Distinct())
                        {
                            double lotNet = group.Sum(x => x.Fraction *
                                (Output(x, kind, lot) - Input(x, kind, lot)));
                            if (lotNet < -1e-12 && book.AvailableMatter(inventory, kind, lot) > Phase2Numbers.Epsilon)
                                time = Math.Min(time, book.AvailableMatter(inventory, kind, lot) / -lotNet);
                        }
                }
                foreach (ProtectedFloor floor in book.Floors.Where(x => x.EndpointId == endpoint &&
                    x.Kind == ResourceKind.Ore))
                {
                    double rate = group.Sum(x => x.Fraction *
                        (FloorOutput(x, floor) - FloorInput(x, floor, inventory, group.ToList())));
                    double slack = FloorSlack(floor, inventory, book);
                    if (rate < -1e-12 && slack > Phase2Numbers.Epsilon)
                        time = Math.Min(time, slack / -rate);
                }
            }
            return time;
        }
        private void CommitInterval(List<Flow> flows, double duration)
        {
            // Stage the entire material world before publishing cursor/power.
            MaterialCommandBook staged = MaterialCommandBook.Restore(materials.Snapshot());
            foreach (string endpoint in power.Keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                var deltas = new List<SimulationMaterialDelta>();
                foreach (Flow flow in flows.Where(x => x.Converter.EndpointId == endpoint && x.Fraction > 0))
                {
                    double active = duration * flow.Fraction;
                    foreach (var leg in flow.Converter.Recipe.Inputs)
                        deltas.Add(new SimulationMaterialDelta(leg.Key, -leg.Value * active,
                            leg.Key == ResourceKind.Ore ? flow.OreInput : null));
                    foreach (var leg in flow.Converter.Recipe.Outputs)
                    {
                        double stored = Output(flow, leg.Key, null) * active;
                        if (stored > 0)
                            deltas.Add(new SimulationMaterialDelta(leg.Key, stored,
                                leg.Key == ResourceKind.Ore ? flow.Converter.OutputOre!.BatchId : null,
                                leg.Key == ResourceKind.Ore ? flow.Converter.OutputOre : null));
                    }
                }
                if (!staged.ApplySimulationDeltas(endpoint, deltas))
                    throw new InvalidOperationException("Solved world material interval failed to commit.");
            }
            foreach (string endpoint in power.Keys.ToArray())
            {
                WorldPower state = power[endpoint];
                double draw = flows.Where(x => x.Converter.EndpointId == endpoint)
                    .Sum(x => x.Fraction * x.Converter.Recipe.PowerPerSecond);
                double energy = Math.Max(0, Math.Min(state.Capacity,
                    state.Energy + (state.GenerationPerSecond - draw) * duration));
                power[endpoint] = state.With(energy: energy);
            }
            foreach (Flow flow in flows.Where(x => x.Fraction > 0))
                foreach (ResourceKind kind in flow.Converter.DumpOutputs)
                {
                    double amount = (Rate(flow.Converter.Recipe.Outputs, kind) -
                        Output(flow, kind, null)) * flow.Fraction * duration;
                    if (amount <= 0) continue;
                    var key = (flow.Converter.Id, kind);
                    dumped[key] = (dumped.TryGetValue(key, out double old) ? old : 0) + amount;
                }
            materials = staged;
        }
    }
}

