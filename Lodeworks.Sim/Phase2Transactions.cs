using System;
using System.Collections.Generic;
using System.Linq;

namespace Lodeworks.Sim
{
    public enum MaterialCommandStatus { Applied, Duplicate, Blocked }
    public sealed class MaterialCommandResult
    {
        public MaterialCommandResult(MaterialCommandStatus status, string reason)
        { Status = status; Reason = reason; }
        public MaterialCommandStatus Status { get; }
        public string Reason { get; }
    }
    public sealed class RecipeCommandResult
    {
        public RecipeCommandResult(MaterialCommandResult command, RecipeResult? recipe)
        { Command = command; Recipe = recipe; }
        public MaterialCommandResult Command { get; }
        public RecipeResult? Recipe { get; }
    }
    public readonly struct MaterialCost
    {
        public MaterialCost(ResourceKind kind, double units)
        {
            if (!Enum.IsDefined(typeof(ResourceKind), kind) || kind == ResourceKind.Ore)
                throw new ArgumentException("Use a defined non-ore resource kind.", nameof(kind));
            Kind = kind; Units = Phase2Numbers.Nonnegative(units, nameof(units));
        }
        public ResourceKind Kind { get; }
        public double Units { get; }
    }
    public sealed class MaterialBookSnapshot
    {
        public const int CurrentVersion = 1;
        public MaterialBookSnapshot(IEnumerable<InventorySnapshot> inventories, IEnumerable<string> receipts)
        {
            Inventories = Array.AsReadOnly(inventories?.ToArray() ?? throw new ArgumentNullException(nameof(inventories)));
            Receipts = Array.AsReadOnly(receipts?.ToArray() ?? throw new ArgumentNullException(nameof(receipts)));
        }
        public int Version => CurrentVersion;
        public IReadOnlyList<InventorySnapshot> Inventories { get; }
        public IReadOnlyList<string> Receipts { get; }
    }

    // Phase 2's atomic material authority. Phase 2A adds commitments/reservations;
    // these operations deliberately cannot promise future matter or capacity.
    public sealed class MaterialCommandBook
    {
        private readonly Dictionary<string, MaterialInventory> stores =
            new Dictionary<string, MaterialInventory>(StringComparer.Ordinal);
        private readonly HashSet<string> receipts = new HashSet<string>(StringComparer.Ordinal);
        public bool Register(MaterialInventory inventory)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            if (stores.ContainsKey(inventory.EndpointId)) return false;
            stores.Add(inventory.EndpointId, MaterialInventory.Restore(inventory.Snapshot()));
            return true;
        }
        public InventorySnapshot GetSnapshot(string endpointId) =>
            stores[Phase2Numbers.Id(endpointId, nameof(endpointId))].Snapshot();
        public MaterialBookSnapshot Snapshot() => new MaterialBookSnapshot(
            stores.Values.OrderBy(x => x.EndpointId, StringComparer.Ordinal).Select(x => x.Snapshot()),
            receipts.OrderBy(x => x, StringComparer.Ordinal));
        public static MaterialCommandBook Restore(MaterialBookSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Version != MaterialBookSnapshot.CurrentVersion)
                throw new ArgumentException("Unsupported material snapshot.", nameof(snapshot));
            var result = new MaterialCommandBook();
            foreach (InventorySnapshot inventory in snapshot.Inventories)
                if (!result.Register(MaterialInventory.Restore(inventory)))
                    throw new ArgumentException("Duplicate endpoint ID.", nameof(snapshot));
            foreach (string id in snapshot.Receipts)
                if (!result.receipts.Add(Phase2Numbers.Id(id, nameof(snapshot))))
                    throw new ArgumentException("Duplicate command receipt.", nameof(snapshot));
            return result;
        }
        private MaterialCommandResult DuplicateOrNew(string commandId)
        {
            Phase2Numbers.Id(commandId, nameof(commandId));
            return receipts.Contains(commandId) ?
                new MaterialCommandResult(MaterialCommandStatus.Duplicate, "Command already applied.") :
                new MaterialCommandResult(MaterialCommandStatus.Applied, string.Empty);
        }
        public MaterialCommandResult Consume(string commandId, string endpointId, IEnumerable<MaterialCost> costs)
        {
            var status = DuplicateOrNew(commandId);
            if (status.Status == MaterialCommandStatus.Duplicate) return status;
            if (costs == null) throw new ArgumentNullException(nameof(costs));
            if (!stores.TryGetValue(Phase2Numbers.Id(endpointId, nameof(endpointId)), out MaterialInventory store))
                return new MaterialCommandResult(MaterialCommandStatus.Blocked, "Unknown endpoint.");
            var grouped = new Dictionary<ResourceKind, double>();
            foreach (MaterialCost cost in costs)
            {
                double next = (grouped.TryGetValue(cost.Kind, out double old) ? old : 0) + cost.Units;
                if (!Phase2Numbers.Finite(next)) throw new ArgumentOutOfRangeException(nameof(costs));
                grouped[cost.Kind] = next;
            }
            MaterialInventory staged = MaterialInventory.Restore(store.Snapshot());
            foreach (var cost in grouped)
                if (!staged.TryRemove(cost.Key, cost.Value))
                    return new MaterialCommandResult(MaterialCommandStatus.Blocked, "Insufficient " + cost.Key + ".");
            stores[endpointId] = staged;
            receipts.Add(commandId);
            return status;
        }
        public MaterialCommandResult Transfer(string commandId, string fromId, string toId,
            ResourceKind kind, double units)
        {
            var status = DuplicateOrNew(commandId);
            if (status.Status == MaterialCommandStatus.Duplicate) return status;
            Phase2Numbers.Nonnegative(units, nameof(units));
            if (kind == ResourceKind.Ore) throw new ArgumentException("Use TransferOre for provenance.", nameof(kind));
            Phase2Numbers.Id(fromId, nameof(fromId)); Phase2Numbers.Id(toId, nameof(toId));
            if (fromId == toId || !stores.TryGetValue(fromId, out MaterialInventory from) ||
                !stores.TryGetValue(toId, out MaterialInventory to))
                return new MaterialCommandResult(MaterialCommandStatus.Blocked, "Invalid endpoints.");
            MaterialInventory source = MaterialInventory.Restore(from.Snapshot());
            MaterialInventory destination = MaterialInventory.Restore(to.Snapshot());
            if (!source.TryRemove(kind, units))
                return new MaterialCommandResult(MaterialCommandStatus.Blocked, "Insufficient source stock.");
            if (!destination.TryAdd(kind, units))
                return new MaterialCommandResult(MaterialCommandStatus.Blocked, "Destination capacity full.");
            stores[fromId] = source; stores[toId] = destination; receipts.Add(commandId);
            return status;
        }
        public MaterialCommandResult TransferOre(string commandId, string fromId, string toId,
            string batchId, double units)
        {
            var status = DuplicateOrNew(commandId);
            if (status.Status == MaterialCommandStatus.Duplicate) return status;
            Phase2Numbers.Nonnegative(units, nameof(units));
            Phase2Numbers.Id(batchId, nameof(batchId));
            Phase2Numbers.Id(fromId, nameof(fromId)); Phase2Numbers.Id(toId, nameof(toId));
            if (fromId == toId || !stores.TryGetValue(fromId, out MaterialInventory from) ||
                !stores.TryGetValue(toId, out MaterialInventory to))
                return new MaterialCommandResult(MaterialCommandStatus.Blocked, "Invalid endpoints.");
            MaterialInventory source = MaterialInventory.Restore(from.Snapshot());
            MaterialInventory destination = MaterialInventory.Restore(to.Snapshot());
            if (!source.TryRemoveOre(batchId, units, out OreBatch? lot) || lot == null)
                return new MaterialCommandResult(MaterialCommandStatus.Blocked, "Insufficient source batch.");
            if (!destination.TryAddOre(lot))
                return new MaterialCommandResult(MaterialCommandStatus.Blocked, "Destination ore capacity full.");
            stores[fromId] = source; stores[toId] = destination; receipts.Add(commandId);
            return status;
        }
        // Pure, single-inventory material commit. The supplied power budget is an
        // immutable quote input; Phase 6 must atomically commit actual owned power
        // before a live caller may use this operation.
        public RecipeCommandResult Process(string commandId, string endpointId,
            RecipeDefinition recipe, double desiredTime, double availablePower,
            string? selectedOreBatchId = null, ISet<ResourceKind>? dumpOutputs = null)
        {
            var status = DuplicateOrNew(commandId);
            if (status.Status == MaterialCommandStatus.Duplicate) return new RecipeCommandResult(status, null);
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            if (!stores.TryGetValue(Phase2Numbers.Id(endpointId, nameof(endpointId)), out MaterialInventory store))
                return new RecipeCommandResult(new MaterialCommandResult(MaterialCommandStatus.Blocked, "Unknown endpoint."), null);
            MaterialInventory staged = MaterialInventory.Restore(store.Snapshot());
            OreBatch? ore = null;
            if (recipe.Inputs.ContainsKey(ResourceKind.Ore))
            {
                ore = selectedOreBatchId == null ? staged.OldestOreForProcessing() :
                    staged.Ore.FirstOrDefault(x => x.BatchId == selectedOreBatchId);
                if (ore == null)
                    return new RecipeCommandResult(new MaterialCommandResult(MaterialCommandStatus.Blocked, "No selected ore batch."), null);
            }
            if (recipe.Outputs.ContainsKey(ResourceKind.Ore))
                throw new ArgumentException("Recipes cannot create provenance-free ore.", nameof(recipe));
            var inputs = recipe.Inputs.Keys.ToDictionary(x => x,
                x => x == ResourceKind.Ore ? ore!.Units : staged.Amount(x));
            var free = recipe.Outputs.Keys.ToDictionary(x => x, x => staged.AvailableCapacity(x));
            RecipeResult quote = RecipeMath.Evaluate(recipe, desiredTime, inputs, free,
                availablePower, dumpOutputs);
            if (quote.ActiveTime <= 0)
                return new RecipeCommandResult(new MaterialCommandResult(MaterialCommandStatus.Blocked,
                    quote.Limits.Count == 0 ? "No active recipe time." : quote.Limits[0].Kind.ToString()), quote);
            foreach (var input in quote.Consumed)
            {
                bool removed = input.Key == ResourceKind.Ore ?
                    staged.TryRemoveOre(ore!.BatchId, input.Value, out _) :
                    staged.TryRemove(input.Key, input.Value);
                if (!removed) throw new InvalidOperationException("Recipe quote and inventory diverged.");
            }
            foreach (var output in quote.Stored)
                if (!staged.TryAdd(output.Key, output.Value))
                    throw new InvalidOperationException("Recipe output capacity diverged.");
            stores[endpointId] = staged;
            receipts.Add(commandId);
            return new RecipeCommandResult(status, quote);
        }
    }
}

