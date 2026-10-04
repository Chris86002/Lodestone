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
    // A world interval's net physical change. Positive ore carries a stable
    // provenance template; negative ore identifies the exact existing lot.
    public readonly struct SimulationMaterialDelta
    {
        public SimulationMaterialDelta(ResourceKind kind, double signedUnits,
            string? oreBatchId = null, OreBatch? producedOre = null)
        {
            if (!Enum.IsDefined(typeof(ResourceKind), kind) || !Phase2Numbers.Finite(signedUnits))
                throw new ArgumentOutOfRangeException(nameof(signedUnits));
            if (kind == ResourceKind.Ore)
            {
                OreBatch? batch = producedOre;
                if (signedUnits > 0 && (batch == null || batch.BatchId != oreBatchId))
                    throw new ArgumentException("Produced ore needs its exact provenance lot.");
                Phase2Numbers.Id(oreBatchId!, nameof(oreBatchId));
            }
            else if (oreBatchId != null || producedOre != null)
                throw new ArgumentException("Only ore carries lot metadata.");
            Kind = kind; SignedUnits = signedUnits; OreBatchId = oreBatchId; ProducedOre = producedOre;
        }
        public ResourceKind Kind { get; }
        public double SignedUnits { get; }
        public string? OreBatchId { get; }
        public OreBatch? ProducedOre { get; }
    }
    public sealed class MaterialBookSnapshot
    {
        public const int CurrentVersion = 2;
        public MaterialBookSnapshot(IEnumerable<InventorySnapshot> inventories, IEnumerable<string> receipts,
            ReservationSnapshot reservations)
        {
            Inventories = Array.AsReadOnly(inventories?.ToArray() ?? throw new ArgumentNullException(nameof(inventories)));
            Receipts = Array.AsReadOnly(receipts?.ToArray() ?? throw new ArgumentNullException(nameof(receipts)));
            Reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
            Version = CurrentVersion;
        }
        // Phase 2 pure snapshots had no reservation section. Keep their
        // round-trip contract while Phase 4 owns live save-schema migration.
        public MaterialBookSnapshot(IEnumerable<InventorySnapshot> inventories, IEnumerable<string> receipts)
            : this(inventories, receipts, new ReservationSnapshot(Array.Empty<ReservationRecord>(),
                Array.Empty<ProtectedFloor>(), Array.Empty<SlotSupply>(), Array.Empty<string>()))
        { Version = 1; }
        public int Version { get; }
        public IReadOnlyList<InventorySnapshot> Inventories { get; }
        public IReadOnlyList<string> Receipts { get; }
        public ReservationSnapshot Reservations { get; }
    }

    // Phase 2's atomic material authority. Phase 2A adds commitments/reservations;
    // these operations deliberately cannot promise future matter or capacity.
    public sealed class MaterialCommandBook
    {
        private readonly Dictionary<string, MaterialInventory> stores =
            new Dictionary<string, MaterialInventory>(StringComparer.Ordinal);
        private readonly HashSet<string> receipts = new HashSet<string>(StringComparer.Ordinal);
        private ReservationBook reservations = new ReservationBook();
        public ReservationSnapshot ReservationSnapshot => reservations.Snapshot();
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
            receipts.OrderBy(x => x, StringComparer.Ordinal), reservations.Snapshot());
        public static MaterialCommandBook Restore(MaterialBookSnapshot snapshot)
        {
            if (snapshot == null || (snapshot.Version != 1 &&
                snapshot.Version != MaterialBookSnapshot.CurrentVersion))
                throw new ArgumentException("Unsupported material snapshot.", nameof(snapshot));
            var result = new MaterialCommandBook();
            foreach (InventorySnapshot inventory in snapshot.Inventories)
                if (!result.Register(MaterialInventory.Restore(inventory)))
                    throw new ArgumentException("Duplicate endpoint ID.", nameof(snapshot));
            foreach (string id in snapshot.Receipts)
                if (!result.receipts.Add(Phase2Numbers.Id(id, nameof(snapshot))))
                    throw new ArgumentException("Duplicate command receipt.", nameof(snapshot));
            result.reservations = ReservationBook.Restore(snapshot.Reservations, result.stores);
            return result;
        }
        public ReservationResult Reserve(ReservationRecord request)
        {
            stores.TryGetValue(request.EndpointId, out MaterialInventory inventory);
            return reservations.Reserve(request, inventory);
        }
        public void SetFloor(ProtectedFloor floor) => reservations.SetFloor(floor);
        public void SetSlotSupply(SlotSupply slot) => reservations.SetSlotSupply(slot);
        public ReservationResult ReleaseCommitted(string id, string receipt, double quantity) =>
            reservations.ReleaseCommitted(id, receipt, quantity);
        public ReservationResult ConsumeEscrow(string id, string receipt, double quantity) =>
            reservations.ConsumeEscrow(id, receipt, quantity);
        public ReservationResult CommitToEscrow(string id, string receipt)
        {
            ReservationRecord? record = reservations.Find(id);
            if (record == null || !stores.TryGetValue(record.EndpointId, out MaterialInventory store))
                return new ReservationResult(ReservationStatus.Blocked, "Unknown reservation endpoint.");
            var stagedStore = MaterialInventory.Restore(store.Snapshot());
            var stagedBook = ReservationBook.Restore(reservations.Snapshot(), stores);
            ReservationResult result = stagedBook.CommitToEscrow(id, receipt, stagedStore);
            if (result.Status == ReservationStatus.Applied)
            { stores[record.EndpointId] = stagedStore; reservations = stagedBook; }
            return result;
        }
        public ReservationResult ReturnEscrow(string id, string receipt, double quantity)
        {
            ReservationRecord? record = reservations.Find(id);
            if (record == null || !stores.TryGetValue(record.EndpointId, out MaterialInventory store))
                return new ReservationResult(ReservationStatus.Blocked, "Unknown escrow endpoint.");
            var stagedStore = MaterialInventory.Restore(store.Snapshot());
            var stagedBook = ReservationBook.Restore(reservations.Snapshot(), stores);
            ReservationResult result = stagedBook.ReturnEscrow(id, receipt, quantity, stagedStore);
            if (result.Status == ReservationStatus.Applied)
            { stores[record.EndpointId] = stagedStore; reservations = stagedBook; }
            return result;
        }
        // Called only by WorldSimulator after it solves simultaneous flows for
        // one shared interval. The cursor and this inventory commit are saved
        // together in the world snapshot; it is not a player command receipt.
        internal bool ApplySimulationDeltas(string endpointId, IEnumerable<SimulationMaterialDelta> deltas)
        {
            if (!stores.TryGetValue(Phase2Numbers.Id(endpointId, nameof(endpointId)), out MaterialInventory store))
                return false;
            if (deltas == null) throw new ArgumentNullException(nameof(deltas));
            var grouped = new Dictionary<(ResourceKind Kind, string? Lot), double>();
            var templates = new Dictionary<string, OreBatch>(StringComparer.Ordinal);
            foreach (SimulationMaterialDelta delta in deltas)
            {
                var key = (delta.Kind, delta.OreBatchId);
                double next = (grouped.TryGetValue(key, out double old) ? old : 0) + delta.SignedUnits;
                if (!Phase2Numbers.Finite(next)) throw new ArgumentOutOfRangeException(nameof(deltas));
                grouped[key] = next;
                if (delta.ProducedOre != null) templates[delta.OreBatchId!] = delta.ProducedOre;
            }
            var net = grouped.Where(x => Math.Abs(x.Value) > Phase2Numbers.Epsilon).ToArray();
            foreach (var pair in net)
            {
                double amount = pair.Value;
                if (amount < 0 && -amount > reservations.AvailableMatter(store, pair.Key.Kind,
                    pair.Key.Lot) + Phase2Numbers.Epsilon) return false;
            }
            foreach (var kind in net.Select(x => x.Key.Kind).Distinct())
                if (net.Where(x => x.Key.Kind == kind).Sum(x => x.Value) >
                    reservations.AvailableCapacity(store, kind) + Phase2Numbers.Epsilon) return false;
            foreach (ProtectedFloor floor in reservations.Floors.Where(x => x.EndpointId == endpointId))
            {
                if (floor.Kind != ResourceKind.Ore) continue; // bulk has one net key
                double physical = store.Ore.Where(floor.Matches).Sum(x => x.Units);
                double claimed = reservations.Records.Where(x => x.State == ReservationState.Committed &&
                    x.Kind == ReservationKind.Matter && x.EndpointId == endpointId &&
                    x.Resource == ResourceKind.Ore && store.Ore.Any(lot =>
                        lot.BatchId == x.OreBatchId && floor.Matches(lot))).Sum(x => x.Quantity);
                double change = net.Where(x => x.Key.Kind == ResourceKind.Ore &&
                    (store.Ore.FirstOrDefault(lot => lot.BatchId == x.Key.Lot) ??
                    (x.Key.Lot != null && templates.TryGetValue(x.Key.Lot, out OreBatch template) ?
                        template : null)) is OreBatch lot && floor.Matches(lot)).Sum(x => x.Value);
                if (change < 0 && physical - claimed + change < floor.EffectiveQuantity - Phase2Numbers.Epsilon)
                    return false;
            }
            MaterialInventory staged = MaterialInventory.Restore(store.Snapshot());
            foreach (var pair in net.Where(x => x.Value < 0))
            {
                bool removed = pair.Key.Kind == ResourceKind.Ore ?
                    staged.TryRemoveOre(pair.Key.Lot!, -pair.Value, out _) :
                    staged.TryRemove(pair.Key.Kind, -pair.Value);
                if (!removed) return false;
            }
            foreach (var pair in net.Where(x => x.Value > 0))
            {
                bool added = pair.Key.Kind == ResourceKind.Ore ?
                    staged.TryAddOre(templates[pair.Key.Lot!].WithUnits(pair.Value)) :
                    staged.TryAdd(pair.Key.Kind, pair.Value);
                if (!added) return false;
            }
            stores[endpointId] = staged;
            return true;
        }
        public MaterialCommandResult RemoveStorage(string commandId, string endpointId, string storageId)
        {
            var status = DuplicateOrNew(commandId);
            if (status.Status == MaterialCommandStatus.Duplicate) return status;
            Phase2Numbers.Id(endpointId, nameof(endpointId));
            Phase2Numbers.Id(storageId, nameof(storageId));
            if (!stores.TryGetValue(endpointId, out MaterialInventory store))
                return new MaterialCommandResult(MaterialCommandStatus.Blocked, "Unknown endpoint.");
            DedicatedStorage? unit = store.Storage.FirstOrDefault(x => x.Id == storageId);
            if (unit == null)
                return new MaterialCommandResult(MaterialCommandStatus.Blocked, "Unknown storage unit.");
            MaterialInventory staged = MaterialInventory.Restore(store.Snapshot());
            if (!staged.TryRemoveStorage(storageId))
                return new MaterialCommandResult(MaterialCommandStatus.Blocked, "Stored matter needs this capacity.");
            double promised = reservations.Records.Where(x => x.State == ReservationState.Committed &&
                x.Kind == ReservationKind.IncomingCapacity && x.EndpointId == endpointId &&
                x.Resource == unit.Kind).Sum(x => x.Quantity);
            if (staged.AvailableCapacity(unit.Kind) + Phase2Numbers.Epsilon < promised)
                return new MaterialCommandResult(MaterialCommandStatus.Blocked, "Incoming capacity is promised.");
            stores[endpointId] = staged;
            receipts.Add(commandId);
            return status;
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
            {
                if (cost.Value > reservations.AvailableMatter(store, cost.Key) + Phase2Numbers.Epsilon)
                    return new MaterialCommandResult(MaterialCommandStatus.Blocked, "Reserved or protected " + cost.Key + ".");
                if (!staged.TryRemove(cost.Key, cost.Value))
                    return new MaterialCommandResult(MaterialCommandStatus.Blocked, "Insufficient " + cost.Key + ".");
            }
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
            if (units > reservations.AvailableMatter(from, kind) + Phase2Numbers.Epsilon ||
                units > reservations.AvailableCapacity(to, kind) + Phase2Numbers.Epsilon)
                return new MaterialCommandResult(MaterialCommandStatus.Blocked, "Reserved stock or capacity.");
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
            if (units > reservations.AvailableMatter(from, ResourceKind.Ore, batchId) + Phase2Numbers.Epsilon ||
                units > reservations.AvailableCapacity(to, ResourceKind.Ore) + Phase2Numbers.Epsilon)
                return new MaterialCommandResult(MaterialCommandStatus.Blocked, "Reserved ore or capacity.");
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
                ore = selectedOreBatchId == null ? staged.Ore.OrderBy(x => x.CreationSequence)
                    .ThenBy(x => x.BatchId, StringComparer.Ordinal)
                    .FirstOrDefault(x => reservations.AvailableMatter(staged, ResourceKind.Ore, x.BatchId) > 0) :
                    staged.Ore.FirstOrDefault(x => x.BatchId == selectedOreBatchId);
                if (ore == null)
                    return new RecipeCommandResult(new MaterialCommandResult(MaterialCommandStatus.Blocked, "No selected ore batch."), null);
            }
            if (recipe.Outputs.ContainsKey(ResourceKind.Ore))
                throw new ArgumentException("Recipes cannot create provenance-free ore.", nameof(recipe));
            var inputs = recipe.Inputs.Keys.ToDictionary(x => x,
                x => x == ResourceKind.Ore ? reservations.AvailableMatter(staged, x, ore!.BatchId) :
                    reservations.AvailableMatter(staged, x));
            var free = recipe.Outputs.Keys.ToDictionary(x => x, x => reservations.AvailableCapacity(staged, x));
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

