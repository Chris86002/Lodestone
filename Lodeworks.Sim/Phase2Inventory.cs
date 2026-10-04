using System;
using System.Collections.Generic;
using System.Linq;

namespace Lodeworks.Sim
{
    public sealed class DedicatedStorage
    {
        public DedicatedStorage(string id, ResourceKind kind, StorageTier tier)
        {
            Id = Phase2Numbers.Id(id, nameof(id));
            Phase2ResourceCatalog.DedicatedCapacity(kind, tier);
            Kind = kind;
            Tier = tier;
        }
        public string Id { get; }
        public ResourceKind Kind { get; }
        public StorageTier Tier { get; }
        public double Capacity => Phase2ResourceCatalog.DedicatedCapacity(Kind, Tier);
        public DedicatedStorage Upgrade(StorageTier tier) => new DedicatedStorage(Id, Kind, tier);
    }

    // A versioned pure snapshot. Phase 4 owns live ConfigNode serialization.
    public sealed class InventorySnapshot
    {
        public const int CurrentVersion = 1;
        public InventorySnapshot(string endpointId, IEnumerable<KeyValuePair<ResourceKind, double>> amounts,
            IEnumerable<OreBatch> ore, IEnumerable<DedicatedStorage> storage)
        {
            EndpointId = Phase2Numbers.Id(endpointId, nameof(endpointId));
            Amounts = Array.AsReadOnly(amounts?.ToArray() ?? throw new ArgumentNullException(nameof(amounts)));
            Ore = Array.AsReadOnly(ore?.ToArray() ?? throw new ArgumentNullException(nameof(ore)));
            Storage = Array.AsReadOnly(storage?.ToArray() ?? throw new ArgumentNullException(nameof(storage)));
        }
        public int Version => CurrentVersion;
        public string EndpointId { get; }
        public IReadOnlyList<KeyValuePair<ResourceKind, double>> Amounts { get; }
        public IReadOnlyList<OreBatch> Ore { get; }
        public IReadOnlyList<DedicatedStorage> Storage { get; }
    }

    public sealed class MaterialInventory
    {
        private readonly Dictionary<ResourceKind, double> amounts = new Dictionary<ResourceKind, double>();
        private readonly List<OreBatch> ore = new List<OreBatch>();
        private readonly Dictionary<string, DedicatedStorage> storage = new Dictionary<string, DedicatedStorage>(StringComparer.Ordinal);
        public MaterialInventory(string endpointId) { EndpointId = Phase2Numbers.Id(endpointId, nameof(endpointId)); }
        public string EndpointId { get; }
        public IReadOnlyList<OreBatch> Ore => ore.ToArray();
        public IReadOnlyList<DedicatedStorage> Storage => Array.AsReadOnly(
            storage.Values.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray());
        public double Amount(ResourceKind kind) => kind == ResourceKind.Ore ? ore.Sum(x => x.Units) :
            amounts.TryGetValue(kind, out double value) ? value : 0;
        public double IntegratedCapacity(ResourceKind kind) => Phase2ResourceCatalog.IntegratedCapacity(kind);
        public double AddedCapacity(ResourceKind kind) => storage.Values.Where(x => x.Kind == kind).Sum(x => x.Capacity);
        public double Capacity(ResourceKind kind) => IntegratedCapacity(kind) + AddedCapacity(kind);
        public double AvailableCapacity(ResourceKind kind) => Math.Max(0, Capacity(kind) - Amount(kind));

        public bool TryAddStorage(DedicatedStorage unit)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (storage.ContainsKey(unit.Id)) return false;
            storage.Add(unit.Id, unit);
            return true;
        }
        public bool TryRemoveStorage(string id)
        {
            Phase2Numbers.Id(id, nameof(id));
            if (!storage.TryGetValue(id, out DedicatedStorage unit)) return false;
            if (Amount(unit.Kind) > Capacity(unit.Kind) - unit.Capacity + Phase2Numbers.Epsilon) return false;
            return storage.Remove(id);
        }
        public bool TryUpgradeStorage(string id, StorageTier tier)
        {
            Phase2Numbers.Id(id, nameof(id));
            if (!storage.TryGetValue(id, out DedicatedStorage unit) || tier <= unit.Tier) return false;
            storage[id] = unit.Upgrade(tier);
            return true;
        }
        public bool TryAdd(ResourceKind kind, double units)
        {
            if (kind == ResourceKind.Ore) throw new ArgumentException("Ore requires a provenance batch.", nameof(kind));
            Phase2Numbers.Nonnegative(units, nameof(units));
            double next = Amount(kind) + units;
            if (!Phase2Numbers.Finite(next) || next > Capacity(kind) + Phase2Numbers.Epsilon) return false;
            amounts[kind] = Math.Min(Capacity(kind), next);
            return true;
        }
        public bool TryRemove(ResourceKind kind, double units)
        {
            if (kind == ResourceKind.Ore) throw new ArgumentException("Ore requires a batch ID.", nameof(kind));
            Phase2Numbers.Nonnegative(units, nameof(units));
            double left = Amount(kind) - units;
            if (left < -Phase2Numbers.Epsilon) return false;
            amounts[kind] = Math.Max(0, left);
            return true;
        }
        public bool TryAddOre(OreBatch batch)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            if (Amount(ResourceKind.Ore) + batch.Units > Capacity(ResourceKind.Ore) + Phase2Numbers.Epsilon) return false;
            int index = ore.FindIndex(x => x.CanStackWith(batch));
            if (index >= 0)
            {
                if (batch.Units > 0) ore[index] = ore[index].Stack(batch);
                return true;
            }
            if (ore.Any(x => x.BatchId == batch.BatchId)) return false;
            ore.Add(batch);
            return true;
        }
        public bool TryRemoveOre(string batchId, double units, out OreBatch? taken)
        {
            Phase2Numbers.Id(batchId, nameof(batchId));
            Phase2Numbers.Nonnegative(units, nameof(units));
            taken = null;
            int index = ore.FindIndex(x => x.BatchId == batchId);
            if (index < 0 || ore[index].Units + Phase2Numbers.Epsilon < units) return false;
            OreBatch old = ore[index];
            taken = old.WithUnits(units);
            double remaining = Math.Max(0, old.Units - units);
            if (remaining == 0) ore.RemoveAt(index); else ore[index] = old.WithUnits(remaining);
            return true;
        }
        // Phase 2A will exclude committed lots. Debug/tutorial ore can still be processed;
        // campaign eligibility is a separate provenance flag.
        public OreBatch? OldestOreForProcessing() => ore.Where(x => x.Units > 0)
            .OrderBy(x => x.CreationSequence).ThenBy(x => x.BatchId, StringComparer.Ordinal).FirstOrDefault();
        public double PhysicalMass(StockFuelMetadata fuel)
        {
            double total = Amount(ResourceKind.Ore) * Phase2ResourceCatalog.Density(ResourceKind.Ore, fuel);
            foreach (var amount in amounts)
                total += Phase2ResourceCatalog.Mass(amount.Key, amount.Value, fuel);
            if (!Phase2Numbers.Finite(total)) throw new InvalidOperationException("Inventory mass overflow.");
            return total;
        }
        public InventorySnapshot Snapshot() => new InventorySnapshot(EndpointId,
            amounts.OrderBy(x => x.Key), ore.ToArray(), Storage);
        public static MaterialInventory Restore(InventorySnapshot snapshot)
        {
            if (snapshot == null || snapshot.Version != InventorySnapshot.CurrentVersion)
                throw new ArgumentException("Unsupported inventory snapshot.", nameof(snapshot));
            var result = new MaterialInventory(snapshot.EndpointId);
            foreach (DedicatedStorage unit in snapshot.Storage)
                if (!result.TryAddStorage(unit)) throw new ArgumentException("Duplicate storage ID.", nameof(snapshot));
            var seenKinds = new HashSet<ResourceKind>();
            foreach (var pair in snapshot.Amounts)
            {
                if (!seenKinds.Add(pair.Key)) throw new ArgumentException("Duplicate resource kind.", nameof(snapshot));
                if (!result.TryAdd(pair.Key, pair.Value)) throw new ArgumentException("Invalid stock amount or capacity.", nameof(snapshot));
            }
            foreach (OreBatch batch in snapshot.Ore)
                if (!result.TryAddOre(batch)) throw new ArgumentException("Invalid ore batch or capacity.", nameof(snapshot));
            return result;
        }
    }
}

