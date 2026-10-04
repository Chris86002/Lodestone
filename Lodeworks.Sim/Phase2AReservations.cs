using System;
using System.Collections.Generic;
using System.Linq;

namespace Lodeworks.Sim
{
    public enum ReservationKind { Matter, IncomingCapacity, Slot }
    public enum ReservationState { Committed, Escrowed, Released, Consumed, Quarantined }
    public enum ReservationStatus { Applied, Duplicate, Blocked, Conflict }

    public sealed class ReservationResult
    {
        public ReservationResult(ReservationStatus status, string reason) { Status = status; Reason = reason; }
        public ReservationStatus Status { get; }
        public string Reason { get; }
    }

    public sealed class OreFilter
    {
        public OreFilter(string? body = null, string? biome = null, GradeBand? band = null,
            bool? campaignEligible = null)
        {
            if (band.HasValue && !Enum.IsDefined(typeof(GradeBand), band.Value))
                throw new ArgumentOutOfRangeException(nameof(band));
            Body = body; Biome = biome; Band = band; CampaignEligible = campaignEligible;
        }
        public string? Body { get; }
        public string? Biome { get; }
        public GradeBand? Band { get; }
        public bool? CampaignEligible { get; }
        public bool Matches(OreBatch batch) => batch != null &&
            (Body == null || Body == batch.OriginBody) &&
            (Biome == null || Biome == batch.OriginBiome) &&
            (!Band.HasValue || Band.Value == batch.Band) &&
            (!CampaignEligible.HasValue || CampaignEligible.Value == batch.CampaignEligible);
    }

    public sealed class ProtectedFloor
    {
        public ProtectedFloor(string id, string endpointId, ResourceKind kind, double quantity,
            OreFilter? oreFilter = null, double? objectiveRemaining = null, double assigned = 0)
        {
            Id = Phase2Numbers.Id(id, nameof(id));
            EndpointId = Phase2Numbers.Id(endpointId, nameof(endpointId));
            if (!Enum.IsDefined(typeof(ResourceKind), kind) ||
                (kind != ResourceKind.Ore && oreFilter != null)) throw new ArgumentException("Invalid floor resource/filter.");
            Kind = kind; Quantity = Phase2Numbers.Nonnegative(quantity, nameof(quantity));
            OreFilter = oreFilter;
            if (objectiveRemaining.HasValue) Phase2Numbers.Nonnegative(objectiveRemaining.Value, nameof(objectiveRemaining));
            ObjectiveRemaining = objectiveRemaining;
            Assigned = Phase2Numbers.Nonnegative(assigned, nameof(assigned));
        }
        public string Id { get; }
        public string EndpointId { get; }
        public ResourceKind Kind { get; }
        public double Quantity { get; }
        public OreFilter? OreFilter { get; }
        public double? ObjectiveRemaining { get; }
        public double Assigned { get; }
        public double EffectiveQuantity => ObjectiveRemaining.HasValue ?
            Math.Min(Quantity, Math.Max(0, ObjectiveRemaining.Value - Assigned)) : Quantity;
        public bool Matches(OreBatch batch) => OreFilter == null || OreFilter.Matches(batch);
    }

    public sealed class SlotSupply
    {
        public SlotSupply(string endpointId, string slotKey, double capacity, double occupied)
        {
            EndpointId = Phase2Numbers.Id(endpointId, nameof(endpointId));
            SlotKey = Phase2Numbers.Id(slotKey, nameof(slotKey));
            Capacity = Phase2Numbers.Nonnegative(capacity, nameof(capacity));
            Occupied = Phase2Numbers.Nonnegative(occupied, nameof(occupied));
            if (Occupied > Capacity) throw new ArgumentOutOfRangeException(nameof(occupied));
        }
        public string EndpointId { get; }
        public string SlotKey { get; }
        public double Capacity { get; }
        public double Occupied { get; }
    }

    public sealed class ReservationRecord
    {
        public ReservationRecord(string id, string ownerId, string ownerType, string endpointId,
            ReservationKind kind, double quantity, double createdUt, int priority,
            ResourceKind? resource = null, string? slotKey = null, string? oreBatchId = null,
            ReservationState state = ReservationState.Committed, OreBatch? escrowOre = null,
            string receipt = "", IEnumerable<string>? authorizedFloorIds = null)
        {
            Id = Phase2Numbers.Id(id, nameof(id)); OwnerId = Phase2Numbers.Id(ownerId, nameof(ownerId));
            OwnerType = Phase2Numbers.Id(ownerType, nameof(ownerType));
            EndpointId = Phase2Numbers.Id(endpointId, nameof(endpointId));
            if (!Enum.IsDefined(typeof(ReservationKind), kind) ||
                !Enum.IsDefined(typeof(ReservationState), state)) throw new ArgumentOutOfRangeException(nameof(kind));
            if (!Phase2Numbers.Finite(createdUt) || createdUt < 0) throw new ArgumentOutOfRangeException(nameof(createdUt));
            if ((kind == ReservationKind.Slot) != (slotKey != null) ||
                (kind == ReservationKind.Slot) == resource.HasValue ||
                (oreBatchId != null && resource != ResourceKind.Ore) ||
                (resource == ResourceKind.Ore && kind == ReservationKind.Matter && oreBatchId == null))
                throw new ArgumentException("Reservation target is inconsistent.");
            if (resource.HasValue && !Enum.IsDefined(typeof(ResourceKind), resource.Value))
                throw new ArgumentOutOfRangeException(nameof(resource));
            if (state == ReservationState.Escrowed && kind != ReservationKind.Matter)
                throw new ArgumentException("Only matter can enter escrow.");
            Quantity = Phase2Numbers.Nonnegative(quantity, nameof(quantity));
            if (state == ReservationState.Committed && Quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity));
            if (escrowOre != null && (resource != ResourceKind.Ore ||
                escrowOre.BatchId != oreBatchId || Math.Abs(escrowOre.Units - Quantity) > Phase2Numbers.Epsilon))
                throw new ArgumentException("Escrow ore must match its lot and quantity.");
            Kind = kind; CreatedUt = createdUt; Priority = priority; Resource = resource;
            SlotKey = slotKey; OreBatchId = oreBatchId; State = state; EscrowOre = escrowOre;
            Receipt = receipt ?? string.Empty;
            AuthorizedFloorIds = Array.AsReadOnly((authorizedFloorIds ?? Array.Empty<string>())
                .Select(x => Phase2Numbers.Id(x, nameof(authorizedFloorIds)))
                .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        }
        public string Id { get; }
        public string OwnerId { get; }
        public string OwnerType { get; }
        public string EndpointId { get; }
        public ReservationKind Kind { get; }
        public double Quantity { get; }
        public double CreatedUt { get; }
        public int Priority { get; }
        public ResourceKind? Resource { get; }
        public string? SlotKey { get; }
        public string? OreBatchId { get; }
        public ReservationState State { get; }
        public OreBatch? EscrowOre { get; }
        public string Receipt { get; }
        public IReadOnlyList<string> AuthorizedFloorIds { get; }
        public ReservationRecord With(double quantity, ReservationState state, string receipt,
            OreBatch? escrowOre = null) => new ReservationRecord(Id, OwnerId, OwnerType, EndpointId,
                Kind, quantity, CreatedUt, Priority, Resource, SlotKey, OreBatchId, state, escrowOre,
                receipt, AuthorizedFloorIds);
    }

    public sealed class ReservationSnapshot
    {
        public const int CurrentVersion = 6;
        public ReservationSnapshot(IEnumerable<ReservationRecord> records, IEnumerable<ProtectedFloor> floors,
            IEnumerable<SlotSupply> slots, IEnumerable<string> receipts)
        {
            Records = Array.AsReadOnly(records?.ToArray() ?? throw new ArgumentNullException(nameof(records)));
            Floors = Array.AsReadOnly(floors?.ToArray() ?? throw new ArgumentNullException(nameof(floors)));
            Slots = Array.AsReadOnly(slots?.ToArray() ?? throw new ArgumentNullException(nameof(slots)));
            Receipts = Array.AsReadOnly(receipts?.ToArray() ?? throw new ArgumentNullException(nameof(receipts)));
        }
        public int Version => CurrentVersion;
        public IReadOnlyList<ReservationRecord> Records { get; }
        public IReadOnlyList<ProtectedFloor> Floors { get; }
        public IReadOnlyList<SlotSupply> Slots { get; }
        public IReadOnlyList<string> Receipts { get; }
    }

    public sealed class ReservationBook
    {
        private readonly Dictionary<string, ReservationRecord> records = new Dictionary<string, ReservationRecord>(StringComparer.Ordinal);
        private readonly Dictionary<string, ProtectedFloor> floors = new Dictionary<string, ProtectedFloor>(StringComparer.Ordinal);
        private readonly Dictionary<string, SlotSupply> slots = new Dictionary<string, SlotSupply>(StringComparer.Ordinal);
        private readonly HashSet<string> receipts = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> conflicts = new List<string>();
        private readonly List<ReservationRecord> quarantined = new List<ReservationRecord>();
        public IReadOnlyList<ReservationRecord> Records => Array.AsReadOnly(records.Values.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray());
        public IReadOnlyList<ReservationRecord> QuarantinedRecords => Array.AsReadOnly(quarantined.ToArray());
        public IReadOnlyList<ProtectedFloor> Floors => Array.AsReadOnly(floors.Values.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray());
        public IReadOnlyList<string> Conflicts => Array.AsReadOnly(conflicts.ToArray());
        public ReservationRecord? Find(string id) => records.TryGetValue(id, out ReservationRecord record) ? record : null;
        private static string SlotId(string endpointId, string slotKey) => endpointId + "\u001f" + slotKey;
        private static ReservationResult Applied() => new ReservationResult(ReservationStatus.Applied, string.Empty);
        private static ReservationResult Blocked(string reason) => new ReservationResult(ReservationStatus.Blocked, reason);
        private ReservationResult CheckNew(ReservationRecord request)
        {
            if (!records.TryGetValue(request.Id, out ReservationRecord old)) return Applied();
            bool same = old.OwnerId == request.OwnerId && old.OwnerType == request.OwnerType &&
                old.EndpointId == request.EndpointId && old.Kind == request.Kind &&
                old.Resource == request.Resource && old.SlotKey == request.SlotKey &&
                old.OreBatchId == request.OreBatchId && old.Quantity == request.Quantity &&
                old.CreatedUt == request.CreatedUt && old.Priority == request.Priority &&
                old.AuthorizedFloorIds.SequenceEqual(request.AuthorizedFloorIds);
            return new ReservationResult(same ? ReservationStatus.Duplicate : ReservationStatus.Conflict,
                same ? "Reservation already exists." : "Reservation ID has conflicting ownership or quantity.");
        }
        public void SetFloor(ProtectedFloor floor)
        {
            if (floor == null) throw new ArgumentNullException(nameof(floor));
            floors[floor.Id] = floor;
        }
        public void SetSlotSupply(SlotSupply supply)
        {
            if (supply == null) throw new ArgumentNullException(nameof(supply));
            slots[SlotId(supply.EndpointId, supply.SlotKey)] = supply;
        }
        private static bool Ignored(string id, ISet<string>? allowedFloorIds) =>
            allowedFloorIds != null && allowedFloorIds.Contains(id);
        public double AvailableMatter(MaterialInventory inventory, ResourceKind kind,
            string? oreBatchId = null, ISet<string>? allowedFloorIds = null)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            if (kind != ResourceKind.Ore)
            {
                double claimed = records.Values.Where(x => x.State == ReservationState.Committed &&
                    x.Kind == ReservationKind.Matter && x.EndpointId == inventory.EndpointId && x.Resource == kind)
                    .Sum(x => x.Quantity);
                double floor = floors.Values.Where(x => x.EndpointId == inventory.EndpointId && x.Kind == kind &&
                    !Ignored(x.Id, allowedFloorIds)).Select(x => x.EffectiveQuantity).DefaultIfEmpty(0).Max();
                return Math.Max(0, inventory.Amount(kind) - claimed - floor);
            }
            Phase2Numbers.Id(oreBatchId!, nameof(oreBatchId));
            OreBatch? selected = inventory.Ore.FirstOrDefault(x => x.BatchId == oreBatchId);
            if (selected == null) return 0;
            double selectedClaimed = records.Values.Where(x => x.State == ReservationState.Committed &&
                x.Kind == ReservationKind.Matter && x.EndpointId == inventory.EndpointId &&
                x.OreBatchId == oreBatchId).Sum(x => x.Quantity);
            double available = Math.Max(0, selected.Units - selectedClaimed);
            foreach (ProtectedFloor floor in floors.Values.Where(x => x.EndpointId == inventory.EndpointId &&
                x.Kind == ResourceKind.Ore && !Ignored(x.Id, allowedFloorIds) && x.Matches(selected)))
            {
                double physical = inventory.Ore.Where(floor.Matches).Sum(x => x.Units);
                double claimed = records.Values.Where(x => x.State == ReservationState.Committed &&
                    x.Kind == ReservationKind.Matter && x.EndpointId == inventory.EndpointId &&
                    x.OreBatchId != null && inventory.Ore.Any(lot => lot.BatchId == x.OreBatchId && floor.Matches(lot)))
                    .Sum(x => x.Quantity);
                available = Math.Min(available, Math.Max(0, physical - claimed - floor.EffectiveQuantity));
            }
            return available;
        }
        public double AvailableCapacity(MaterialInventory inventory, ResourceKind kind)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            double claimed = records.Values.Where(x => x.State == ReservationState.Committed &&
                x.Kind == ReservationKind.IncomingCapacity && x.EndpointId == inventory.EndpointId &&
                x.Resource == kind).Sum(x => x.Quantity);
            return Math.Max(0, inventory.AvailableCapacity(kind) - claimed);
        }
        public double AvailableSlot(string endpointId, string slotKey)
        {
            if (!slots.TryGetValue(SlotId(Phase2Numbers.Id(endpointId, nameof(endpointId)),
                Phase2Numbers.Id(slotKey, nameof(slotKey))), out SlotSupply supply)) return 0;
            double claimed = records.Values.Where(x => x.State == ReservationState.Committed &&
                x.Kind == ReservationKind.Slot && x.EndpointId == endpointId && x.SlotKey == slotKey)
                .Sum(x => x.Quantity);
            return Math.Max(0, supply.Capacity - supply.Occupied - claimed);
        }
        public ReservationResult Reserve(ReservationRecord request, MaterialInventory? inventory)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.State != ReservationState.Committed) return Blocked("Only new committed claims may be reserved.");
            ReservationResult prior = CheckNew(request);
            if (prior.Status != ReservationStatus.Applied) return prior;
            if (request.AuthorizedFloorIds.Count > 0)
            {
                if (request.Kind != ReservationKind.Matter ||
                    (request.OwnerType != "campaignExport" && request.OwnerType != "manualOverride"))
                    return Blocked("Floor override needs an explicit authorized owner type.");
                foreach (string id in request.AuthorizedFloorIds)
                    if (!floors.TryGetValue(id, out ProtectedFloor floor) ||
                        floor.EndpointId != request.EndpointId || floor.Kind != request.Resource)
                        return Blocked("Unknown or unrelated floor override.");
            }
            var allowed = request.AuthorizedFloorIds.Count == 0 ? null :
                new HashSet<string>(request.AuthorizedFloorIds, StringComparer.Ordinal);
            double available = request.Kind == ReservationKind.Slot ?
                AvailableSlot(request.EndpointId, request.SlotKey!) : inventory == null ||
                inventory.EndpointId != request.EndpointId ? -1 :
                request.Kind == ReservationKind.Matter ?
                    AvailableMatter(inventory, request.Resource!.Value, request.OreBatchId, allowed) :
                    AvailableCapacity(inventory, request.Resource!.Value);
            if (request.Quantity > available + Phase2Numbers.Epsilon)
                return Blocked("Insufficient unclaimed matter, capacity, or slot supply.");
            records.Add(request.Id, request);
            return Applied();
        }
        private ReservationResult CheckReceipt(string receipt)
        {
            Phase2Numbers.Id(receipt, nameof(receipt));
            return receipts.Contains(receipt) ? new ReservationResult(ReservationStatus.Duplicate, "Receipt already applied.") : Applied();
        }
        public ReservationResult CommitToEscrow(string id, string receipt, MaterialInventory inventory)
        {
            ReservationResult prior = CheckReceipt(receipt);
            if (prior.Status != ReservationStatus.Applied) return prior;
            if (!records.TryGetValue(Phase2Numbers.Id(id, nameof(id)), out ReservationRecord record) ||
                record.State != ReservationState.Committed || record.Kind != ReservationKind.Matter ||
                record.EndpointId != inventory.EndpointId) return Blocked("No committed matter claim.");
            OreBatch? lot = null;
            bool removed = record.Resource == ResourceKind.Ore ?
                inventory.TryRemoveOre(record.OreBatchId!, record.Quantity, out lot) :
                inventory.TryRemove(record.Resource!.Value, record.Quantity);
            if (!removed) return Blocked("Claimed stock is missing; no escrow created.");
            records[id] = record.With(record.Quantity, ReservationState.Escrowed, receipt, lot);
            receipts.Add(receipt);
            return Applied();
        }
        public ReservationResult ReleaseCommitted(string id, string receipt, double quantity)
        {
            ReservationResult prior = CheckReceipt(receipt);
            if (prior.Status != ReservationStatus.Applied) return prior;
            Phase2Numbers.Nonnegative(quantity, nameof(quantity));
            if (!records.TryGetValue(Phase2Numbers.Id(id, nameof(id)), out ReservationRecord record) ||
                record.State != ReservationState.Committed || quantity <= 0 ||
                quantity > record.Quantity + Phase2Numbers.Epsilon) return Blocked("No releasable claim.");
            double left = Math.Max(0, record.Quantity - quantity);
            records[id] = record.With(left, left == 0 ? ReservationState.Released : ReservationState.Committed, receipt);
            receipts.Add(receipt);
            return Applied();
        }
        public ReservationResult ReturnEscrow(string id, string receipt, double quantity, MaterialInventory inventory)
        {
            ReservationResult prior = CheckReceipt(receipt);
            if (prior.Status != ReservationStatus.Applied) return prior;
            Phase2Numbers.Nonnegative(quantity, nameof(quantity));
            if (!records.TryGetValue(Phase2Numbers.Id(id, nameof(id)), out ReservationRecord record) ||
                record.State != ReservationState.Escrowed || record.EndpointId != inventory.EndpointId ||
                quantity <= 0 || quantity > record.Quantity + Phase2Numbers.Epsilon)
                return Blocked("No returnable escrow.");
            if (quantity > AvailableCapacity(inventory, record.Resource!.Value) + Phase2Numbers.Epsilon)
                return Blocked("Capacity is promised to another owner.");
            bool added = record.Resource == ResourceKind.Ore ?
                inventory.TryAddOre(record.EscrowOre!.WithUnits(quantity)) :
                inventory.TryAdd(record.Resource!.Value, quantity);
            if (!added) return Blocked("No room to return escrow.");
            double left = Math.Max(0, record.Quantity - quantity);
            records[id] = record.With(left, left == 0 ? ReservationState.Released : ReservationState.Escrowed,
                receipt, left > 0 ? record.EscrowOre?.WithUnits(left) : null);
            receipts.Add(receipt);
            return Applied();
        }
        public ReservationResult ConsumeEscrow(string id, string receipt, double quantity)
        {
            ReservationResult prior = CheckReceipt(receipt);
            if (prior.Status != ReservationStatus.Applied) return prior;
            Phase2Numbers.Nonnegative(quantity, nameof(quantity));
            if (!records.TryGetValue(Phase2Numbers.Id(id, nameof(id)), out ReservationRecord record) ||
                record.State != ReservationState.Escrowed || quantity <= 0 ||
                quantity > record.Quantity + Phase2Numbers.Epsilon) return Blocked("No consumable escrow.");
            double left = Math.Max(0, record.Quantity - quantity);
            records[id] = record.With(left, left == 0 ? ReservationState.Consumed : ReservationState.Escrowed,
                receipt, left > 0 ? record.EscrowOre?.WithUnits(left) : null);
            receipts.Add(receipt);
            return Applied();
        }
        public double EscrowMass(StockFuelMetadata fuel) => records.Values.Where(x => x.State == ReservationState.Escrowed)
            .Sum(x => Phase2ResourceCatalog.Mass(x.Resource!.Value, x.Quantity, fuel));
        public ReservationSnapshot Snapshot() => new ReservationSnapshot(Records.Concat(QuarantinedRecords), Floors, slots.Values,
            receipts.OrderBy(x => x, StringComparer.Ordinal));
        public static ReservationBook Restore(ReservationSnapshot snapshot,
            IReadOnlyDictionary<string, MaterialInventory> inventories)
        {
            if (snapshot == null || snapshot.Version != ReservationSnapshot.CurrentVersion || inventories == null)
                throw new ArgumentException("Invalid reservation snapshot.", nameof(snapshot));
            var book = new ReservationBook();
            foreach (ProtectedFloor floor in snapshot.Floors)
            {
                if (book.floors.ContainsKey(floor.Id)) book.conflicts.Add("Duplicate floor " + floor.Id);
                else book.SetFloor(floor);
            }
            foreach (SlotSupply slot in snapshot.Slots)
            {
                string key = SlotId(slot.EndpointId, slot.SlotKey);
                if (book.slots.ContainsKey(key)) book.conflicts.Add("Duplicate slot " + key);
                else book.SetSlotSupply(slot);
            }
            foreach (ReservationRecord record in snapshot.Records.OrderBy(x => x.CreatedUt)
                .ThenByDescending(x => x.Priority).ThenBy(x => x.Id, StringComparer.Ordinal))
            {
                if (record.State == ReservationState.Quarantined)
                {
                    book.quarantined.Add(record);
                    book.conflicts.Add(record.Receipt);
                    continue;
                }
                if (book.records.ContainsKey(record.Id))
                {
                    string reason = "Duplicate reservation " + record.Id;
                    book.quarantined.Add(record.With(record.Quantity, ReservationState.Quarantined, reason));
                    book.conflicts.Add(reason);
                    continue;
                }
                if (record.State == ReservationState.Committed)
                {
                    inventories.TryGetValue(record.EndpointId, out MaterialInventory inventory);
                    ReservationResult result = book.Reserve(record, inventory);
                    if (result.Status != ReservationStatus.Applied)
                    {
                        string reason = "Quarantined " + record.Id + ": " + result.Reason;
                        book.quarantined.Add(record.With(record.Quantity, ReservationState.Quarantined, reason));
                        book.conflicts.Add(reason);
                        continue;
                    }
                }
                else if (record.State == ReservationState.Escrowed && record.Resource == ResourceKind.Ore &&
                    record.EscrowOre == null)
                {
                    string reason = "Quarantined " + record.Id + ": missing escrow lot";
                    book.quarantined.Add(record.With(record.Quantity, ReservationState.Quarantined, reason));
                    book.conflicts.Add(reason);
                }
                else book.records.Add(record.Id, record);
            }
            foreach (string receipt in snapshot.Receipts)
                if (!book.receipts.Add(Phase2Numbers.Id(receipt, nameof(snapshot))))
                    book.conflicts.Add("Duplicate receipt " + receipt);
            return book;
        }
    }
}

