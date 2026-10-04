using System;
using System.Collections.Generic;

namespace Lodeworks.Sim
{
    public enum ResourceKind { Ore, Metal, Slag, Parts, Supplies, FuelFeed, LiquidFuel, Oxidizer }
    public enum GradeBand { Poor, Normal, Rich }
    public enum ProvenanceClass { Mined, Tutorial, Debug }
    public enum StorageTier { Basic, Advanced }

    public static class Phase2Numbers
    {
        public const double Epsilon = 1e-9;
        public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public static double Nonnegative(double value, string name)
        {
            if (!Finite(value) || value < 0) throw new ArgumentOutOfRangeException(name);
            return value;
        }
        public static string Id(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A stable ID is required.", name);
            return value;
        }
    }

    // Stock resources are metadata inputs, never redefinitions of KSP resources.
    public readonly struct StockFuelMetadata
    {
        public StockFuelMetadata(double liquidFuelDensity, double oxidizerDensity,
            double liquidFuelUnitCost, double oxidizerUnitCost)
        {
            LiquidFuelDensity = Positive(liquidFuelDensity, nameof(liquidFuelDensity));
            OxidizerDensity = Positive(oxidizerDensity, nameof(oxidizerDensity));
            LiquidFuelUnitCost = Phase2Numbers.Nonnegative(liquidFuelUnitCost, nameof(liquidFuelUnitCost));
            OxidizerUnitCost = Phase2Numbers.Nonnegative(oxidizerUnitCost, nameof(oxidizerUnitCost));
        }
        private static double Positive(double value, string name) =>
            Phase2Numbers.Nonnegative(value, name) > 0 ? value : throw new ArgumentOutOfRangeException(name);
        public double LiquidFuelDensity { get; }
        public double OxidizerDensity { get; }
        public double LiquidFuelUnitCost { get; }
        public double OxidizerUnitCost { get; }
        public double Density(ResourceKind kind) => kind == ResourceKind.LiquidFuel ? LiquidFuelDensity :
            kind == ResourceKind.Oxidizer ? OxidizerDensity : throw new ArgumentException("Not stock fuel.", nameof(kind));
        public double UnitCost(ResourceKind kind) => kind == ResourceKind.LiquidFuel ? LiquidFuelUnitCost :
            kind == ResourceKind.Oxidizer ? OxidizerUnitCost : throw new ArgumentException("Not stock fuel.", nameof(kind));
    }

    public static class Phase2ResourceCatalog
    {
        public static double IntegratedCapacity(ResourceKind kind) => kind switch
        {
            ResourceKind.Ore => 100, ResourceKind.Metal => 100, ResourceKind.Slag => 0,
            ResourceKind.Parts => 20, ResourceKind.Supplies => 20, ResourceKind.FuelFeed => 100,
            ResourceKind.LiquidFuel => 45, ResourceKind.Oxidizer => 55,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        public static double DedicatedCapacity(ResourceKind kind, StorageTier tier)
        {
            if (tier != StorageTier.Basic && tier != StorageTier.Advanced)
                throw new ArgumentOutOfRangeException(nameof(tier));
            double basic = kind switch
            {
                ResourceKind.Ore => 400, ResourceKind.Metal => 400, ResourceKind.Slag => 400,
                ResourceKind.Parts => 200, ResourceKind.Supplies => 200, ResourceKind.FuelFeed => 400,
                ResourceKind.LiquidFuel => 2000, ResourceKind.Oxidizer => 2000,
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            return tier == StorageTier.Basic ? basic : 4 * basic;
        }
        public static double Density(ResourceKind kind, StockFuelMetadata fuel) => kind switch
        {
            ResourceKind.Ore => 0.010, ResourceKind.Metal => 0.010, ResourceKind.Slag => 0.010,
            ResourceKind.Parts => 0.005, ResourceKind.Supplies => 0.002,
            ResourceKind.FuelFeed => 0.005, ResourceKind.LiquidFuel => fuel.LiquidFuelDensity,
            ResourceKind.Oxidizer => fuel.OxidizerDensity,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        public static double Mass(ResourceKind kind, double units, StockFuelMetadata fuel) =>
            Phase2Numbers.Nonnegative(units, nameof(units)) * Density(kind, fuel);
        public static double MixtureDensity(double liquidFuelUnits, double oxidizerUnits, StockFuelMetadata fuel)
        {
            double lf = Phase2Numbers.Nonnegative(liquidFuelUnits, nameof(liquidFuelUnits));
            double ox = Phase2Numbers.Nonnegative(oxidizerUnits, nameof(oxidizerUnits));
            double total = lf + ox;
            if (!Phase2Numbers.Finite(total) || total <= 0) throw new ArgumentOutOfRangeException(nameof(liquidFuelUnits));
            return (lf * fuel.LiquidFuelDensity + ox * fuel.OxidizerDensity) / total;
        }
    }

    public sealed class OreBatch
    {
        public OreBatch(string batchId, string originBody, string originBiome, double grade,
            double units, ProvenanceClass provenance, bool campaignEligible, long creationSequence)
        {
            BatchId = Phase2Numbers.Id(batchId, nameof(batchId));
            OriginBody = Phase2Numbers.Id(originBody, nameof(originBody));
            OriginBiome = Phase2Numbers.Id(originBiome, nameof(originBiome));
            if (!Phase2Numbers.Finite(grade) || grade < 0 || grade > 1)
                throw new ArgumentOutOfRangeException(nameof(grade));
            if (creationSequence < 0) throw new ArgumentOutOfRangeException(nameof(creationSequence));
            if (!Enum.IsDefined(typeof(ProvenanceClass), provenance)) throw new ArgumentOutOfRangeException(nameof(provenance));
            if (provenance != ProvenanceClass.Mined && campaignEligible)
                throw new ArgumentException("Only mined ore may be campaign eligible.", nameof(campaignEligible));
            Grade = grade;
            Units = Phase2Numbers.Nonnegative(units, nameof(units));
            Provenance = provenance;
            CampaignEligible = campaignEligible;
            CreationSequence = creationSequence;
        }
        public string BatchId { get; }
        public string OriginBody { get; }
        public string OriginBiome { get; }
        public double Grade { get; }
        public double Units { get; }
        public ProvenanceClass Provenance { get; }
        public bool CampaignEligible { get; }
        public long CreationSequence { get; }
        public GradeBand Band => Grade < 0.40 ? GradeBand.Poor : Grade < 0.70 ? GradeBand.Normal : GradeBand.Rich;
        public OreBatch WithUnits(double units) => new OreBatch(BatchId, OriginBody, OriginBiome, Grade,
            units, Provenance, CampaignEligible, CreationSequence);
        public bool CanStackWith(OreBatch other) => other != null &&
            OriginBody == other.OriginBody && OriginBiome == other.OriginBiome &&
            Band == other.Band && Provenance == other.Provenance &&
            CampaignEligible == other.CampaignEligible;
        public OreBatch Stack(OreBatch other)
        {
            if (!CanStackWith(other)) throw new ArgumentException("Incompatible ore provenance or grade band.", nameof(other));
            double total = Units + other.Units;
            if (!Phase2Numbers.Finite(total) || total <= 0) throw new ArgumentOutOfRangeException(nameof(other));
            // Retain the oldest stable lot identity. Campaign filters operate on the band.
            OreBatch first = CreationSequence < other.CreationSequence ||
                (CreationSequence == other.CreationSequence &&
                 string.CompareOrdinal(BatchId, other.BatchId) <= 0) ? this : other;
            return new OreBatch(first.BatchId, OriginBody, OriginBiome,
                (Grade * Units + other.Grade * other.Units) / total, total,
                Provenance, CampaignEligible, first.CreationSequence);
        }
    }
}

