using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Lodeworks.Sim.Tests
{
    public sealed class Phase2InventoryTests
    {
        private static readonly StockFuelMetadata Fuel = new StockFuelMetadata(0.005, 0.005, 0.8, 0.18);

        [Fact]
        public void StockMixtureMassUsesVerifiedMetadataAndRejectsBadValues()
        {
            var asymmetric = new StockFuelMetadata(0.004, 0.006, 0.8, 0.18);
            Assert.Equal(0.0051, Phase2ResourceCatalog.MixtureDensity(9, 11, asymmetric), 12);
            Assert.Equal(0.102, Phase2ResourceCatalog.Mass(ResourceKind.LiquidFuel, 9, asymmetric) +
                Phase2ResourceCatalog.Mass(ResourceKind.Oxidizer, 11, asymmetric), 12);
            Assert.Throws<ArgumentOutOfRangeException>(() => new StockFuelMetadata(double.NaN, 0.005, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Phase2ResourceCatalog.MixtureDensity(0, 0, Fuel));
            Assert.Throws<ArgumentOutOfRangeException>(() => Phase2ResourceCatalog.Mass(ResourceKind.Metal, double.PositiveInfinity, Fuel));
        }

        [Fact]
        public void GradeBandsAndProvenanceDoNotCrossStackBoundaries()
        {
            var poor = new OreBatch("a", "Mun", "Midlands", 0.39, 20, ProvenanceClass.Mined, true, 1);
            var normal = new OreBatch("b", "Mun", "Midlands", 0.40, 20, ProvenanceClass.Mined, true, 2);
            var rich = new OreBatch("c", "Mun", "Midlands", 0.70, 20, ProvenanceClass.Mined, true, 3);
            Assert.Equal(GradeBand.Poor, poor.Band);
            Assert.Equal(GradeBand.Normal, normal.Band);
            Assert.Equal(GradeBand.Rich, rich.Band);
            Assert.False(poor.CanStackWith(normal));
            Assert.False(normal.CanStackWith(rich));
            Assert.False(normal.CanStackWith(new OreBatch("d", "Minmus", "Midlands", 0.5, 2, ProvenanceClass.Mined, true, 4)));
            Assert.False(normal.CanStackWith(new OreBatch("e", "Mun", "Midlands", 0.5, 2, ProvenanceClass.Debug, false, 4)));
            var merged = normal.Stack(new OreBatch("f", "Mun", "Midlands", 0.60, 20, ProvenanceClass.Mined, true, 5));
            Assert.Equal(0.50, merged.Grade, 12);
            Assert.Equal(40, merged.Units);
            Assert.Equal("b", merged.BatchId);
            var inventory = new MaterialInventory("ore-site");
            Assert.True(inventory.TryAddOre(normal));
            Assert.True(inventory.TryAddOre(new OreBatch("f", "Mun", "Midlands", 0.60, 20,
                ProvenanceClass.Mined, true, 5)));
            Assert.Equal(2, inventory.Ore.Count); // do not erase later batch identity
            Assert.Throws<ArgumentException>(() => new OreBatch("debug", "Mun", "Midlands", 0.5, 1, ProvenanceClass.Debug, true, 1));
        }

        [Fact]
        public void DedicatedStorageOnlyChangesItsOwnResourceAndNeverGrantsContents()
        {
            var inventory = new MaterialInventory("site-a");
            Assert.Equal(100, inventory.Capacity(ResourceKind.Metal));
            Assert.Equal(20, inventory.Capacity(ResourceKind.Parts));
            Assert.True(inventory.TryAddStorage(new DedicatedStorage("metal-bin", ResourceKind.Metal, StorageTier.Basic)));
            Assert.Equal(500, inventory.Capacity(ResourceKind.Metal));
            Assert.Equal(20, inventory.Capacity(ResourceKind.Parts));
            Assert.Equal(0, inventory.Amount(ResourceKind.Metal));
            Assert.True(inventory.TryAdd(ResourceKind.Metal, 450));
            Assert.False(inventory.TryRemoveStorage("metal-bin"));
            Assert.True(inventory.TryUpgradeStorage("metal-bin", StorageTier.Advanced));
            Assert.Equal(1700, inventory.Capacity(ResourceKind.Metal));
            Assert.Equal(450, MaterialInventory.Restore(inventory.Snapshot()).Amount(ResourceKind.Metal));
            Assert.False(inventory.TryAdd(ResourceKind.Parts, 21));
        }

        [Fact]
        public void RecipeDuplicateLegsLimitAtAggregatedStockAndOutputFull()
        {
            var recipe = new RecipeDefinition(
                new[] { new RecipeLeg(ResourceKind.Metal, 2), new RecipeLeg(ResourceKind.Metal, 3),
                    new RecipeLeg(ResourceKind.Metal, 0) },
                new[] { new RecipeLeg(ResourceKind.Parts, 1), new RecipeLeg(ResourceKind.Parts, 1) }, 0);
            var input = new Dictionary<ResourceKind, double> { [ResourceKind.Metal] = 20 };
            var free = new Dictionary<ResourceKind, double> { [ResourceKind.Parts] = 12 };
            var result = RecipeMath.Evaluate(recipe, 10, input, free, 0);
            Assert.Equal(4, result.ActiveTime);
            Assert.Equal(20, result.Consumed[ResourceKind.Metal]);
            Assert.Equal(8, result.Stored[ResourceKind.Parts]);
            Assert.Equal(RecipeLimitKind.InputShortage, Assert.Single(result.Limits).Kind);
            Assert.Equal(20, input[ResourceKind.Metal]); // quote did not mutate caller pool
            free[ResourceKind.Parts] = 3;
            result = RecipeMath.Evaluate(recipe, 10, input, free, 0);
            Assert.Equal(1.5, result.ActiveTime);
            Assert.Equal(RecipeLimitKind.OutputFull, Assert.Single(result.Limits).Kind);
        }

        [Fact]
        public void SlagDumpChangesOnlyItsOutputLimitAndTracksDiscardedMass()
        {
            var smelt = RecipeMath.Smelt(0.60, 0.75, 1, 1, 1);
            var input = new Dictionary<ResourceKind, double> { [ResourceKind.Ore] = 5 };
            var free = new Dictionary<ResourceKind, double> { [ResourceKind.Metal] = 10, [ResourceKind.Slag] = 0 };
            var blocked = RecipeMath.Evaluate(smelt, 10, input, free, 120);
            Assert.Equal(0, blocked.ActiveTime);
            Assert.Equal(RecipeLimitKind.OutputFull, Assert.Single(blocked.Limits).Kind);
            var dumped = RecipeMath.Evaluate(smelt, 10, input, free, 120,
                new HashSet<ResourceKind> { ResourceKind.Slag });
            Assert.Equal(10, dumped.ActiveTime);
            Assert.Equal(5, dumped.Consumed[ResourceKind.Ore]);
            Assert.Equal(2.25, dumped.Stored[ResourceKind.Metal], 12);
            Assert.Equal(2.75, dumped.Dumped[ResourceKind.Slag], 12);
            Assert.Empty(dumped.Limits);
            Assert.Equal(5, dumped.Stored[ResourceKind.Metal] + dumped.Dumped[ResourceKind.Slag], 12);
        }

        [Fact]
        public void RateMultiplierAndCrewFactorApplyOnceAndPowerCanLimit()
        {
            var machine = RecipeMath.Machine(0.5, 2);
            var result = RecipeMath.Evaluate(machine, 10,
                new Dictionary<ResourceKind, double> { [ResourceKind.Metal] = 10 },
                new Dictionary<ResourceKind, double> { [ResourceKind.Parts] = 10 }, 40);
            Assert.Equal(5, result.ActiveTime);
            Assert.Equal(0.25, result.Consumed[ResourceKind.Metal], 12);
            Assert.Equal(0.10, result.Stored[ResourceKind.Parts], 12);
            Assert.Equal(RecipeLimitKind.PowerShortage, Assert.Single(result.Limits).Kind);
            Assert.Equal(40, result.PowerUsed);
            Assert.Equal(0, RecipeMath.Evaluate(RecipeMath.Machine(0, 1), 10,
                new Dictionary<ResourceKind, double>(), new Dictionary<ResourceKind, double>(), 100).ActiveTime);
        }

        [Fact]
        public void StarterKitGrantAndRoundTripCannotCreateExtraOrRepairToken()
        {
            var kits = new StarterKitBook();
            Assert.True(kits.GrantOnce("hab-1", true));
            Assert.False(kits.GrantOnce("hab-1", true));
            Assert.Equal(9, Assert.Single(kits.Receipts).Tokens.Count);
            Assert.Equal(StarterPieceLocation.InstalledInHab,
                kits.Find("hab-1", StarterPieceKind.TransportTerminal)!.Location);
            Assert.True(kits.TryMove("hab-1", StarterPieceKind.OreDrill,
                StarterPieceLocation.Packed, StarterPieceLocation.Placed));
            Assert.True(kits.TryRecordWear("hab-1", StarterPieceKind.OreDrill, 0.4));
            Assert.True(kits.TryMove("hab-1", StarterPieceKind.OreDrill,
                StarterPieceLocation.Placed, StarterPieceLocation.Packed));
            Assert.False(kits.TryRecordWear("hab-1", StarterPieceKind.OreDrill, 1));
            var reloaded = StarterKitBook.Restore(kits.Receipts);
            Assert.False(reloaded.GrantOnce("hab-1", false));
            Assert.Equal(0.4, reloaded.Find("hab-1", StarterPieceKind.OreDrill)!.Condition);
        }

        [Fact]
        public void TransfersRollBackOnFullDestinationAndReceiptsSurviveReload()
        {
            var a = new MaterialInventory("a");
            var b = new MaterialInventory("b");
            Assert.True(a.TryAdd(ResourceKind.Metal, 80));
            Assert.True(b.TryAdd(ResourceKind.Metal, 90));
            var book = new MaterialCommandBook();
            Assert.True(book.Register(a)); Assert.True(book.Register(b));
            Assert.Equal(MaterialCommandStatus.Blocked,
                book.Transfer("x", "a", "b", ResourceKind.Metal, 20).Status);
            Assert.Equal(80, MaterialInventory.Restore(book.GetSnapshot("a")).Amount(ResourceKind.Metal));
            Assert.Equal(90, MaterialInventory.Restore(book.GetSnapshot("b")).Amount(ResourceKind.Metal));
            Assert.Equal(MaterialCommandStatus.Applied,
                book.Transfer("x", "a", "b", ResourceKind.Metal, 10).Status);
            var reloaded = MaterialCommandBook.Restore(book.Snapshot());
            Assert.Equal(MaterialCommandStatus.Duplicate,
                reloaded.Transfer("x", "a", "b", ResourceKind.Metal, 10).Status);
            Assert.Equal(70, MaterialInventory.Restore(reloaded.GetSnapshot("a")).Amount(ResourceKind.Metal));
            Assert.Equal(100, MaterialInventory.Restore(reloaded.GetSnapshot("b")).Amount(ResourceKind.Metal));
        }

        [Fact]
        public void OreTransferPreservesOriginAndEligibilityAndDoesNotMintOnRetry()
        {
            var a = new MaterialInventory("a");
            var b = new MaterialInventory("b");
            Assert.True(a.TryAddOre(new OreBatch("mun-ore", "Mun", "Crater", 0.8, 60,
                ProvenanceClass.Mined, true, 7)));
            var book = new MaterialCommandBook();
            book.Register(a); book.Register(b);
            Assert.Equal(MaterialCommandStatus.Applied,
                book.TransferOre("move-1", "a", "b", "mun-ore", 25).Status);
            Assert.Equal(MaterialCommandStatus.Duplicate,
                book.TransferOre("move-1", "a", "b", "mun-ore", 25).Status);
            var moved = Assert.Single(MaterialInventory.Restore(book.GetSnapshot("b")).Ore);
            Assert.Equal("Mun", moved.OriginBody);
            Assert.Equal(GradeBand.Rich, moved.Band);
            Assert.True(moved.CampaignEligible);
            Assert.Equal(25, moved.Units);
            Assert.Equal(35, MaterialInventory.Restore(book.GetSnapshot("a")).Amount(ResourceKind.Ore));
        }

        [Fact]
        public void ProcessingCommitsAllMaterialLegsOnceAndRetainsOreProvenanceUntilConsumed()
        {
            var inventory = new MaterialInventory("site");
            inventory.TryAddOre(new OreBatch("debug-ore", "Mun", "Crater", 0.6, 5,
                ProvenanceClass.Debug, false, 1));
            var book = new MaterialCommandBook(); book.Register(inventory);
            var smelt = RecipeMath.Smelt(0.6, 0.75, 1, 1, 1);
            var noDump = book.Process("smelt", "site", smelt, 10, 120);
            Assert.Equal(MaterialCommandStatus.Blocked, noDump.Command.Status);
            Assert.Equal(5, MaterialInventory.Restore(book.GetSnapshot("site")).Amount(ResourceKind.Ore));
            var run = book.Process("smelt", "site", smelt, 10, 120,
                dumpOutputs: new HashSet<ResourceKind> { ResourceKind.Slag });
            Assert.Equal(MaterialCommandStatus.Applied, run.Command.Status);
            Assert.Equal(2.25, MaterialInventory.Restore(book.GetSnapshot("site")).Amount(ResourceKind.Metal), 12);
            Assert.Equal(0, MaterialInventory.Restore(book.GetSnapshot("site")).Amount(ResourceKind.Ore));
            Assert.Equal(2.75, run.Recipe!.Dumped[ResourceKind.Slag], 12);
            Assert.Equal(MaterialCommandStatus.Duplicate,
                MaterialCommandBook.Restore(book.Snapshot()).Process("smelt", "site", smelt, 10, 120).Command.Status);
        }

        [Fact]
        public void ProcessingChoosesOldestAvailableLotEvenWhenCampaignIneligible()
        {
            var inventory = new MaterialInventory("site");
            inventory.TryAddOre(new OreBatch("later", "Mun", "Crater", 0.8, 1,
                ProvenanceClass.Mined, true, 2));
            inventory.TryAddOre(new OreBatch("earlier", "Minmus", "Flats", 0.3, 1,
                ProvenanceClass.Debug, false, 1));
            Assert.Equal("earlier", inventory.OldestOreForProcessing()!.BatchId);
        }

        [Fact]
        public void MaterialCostAggregatesDuplicateLegsAndFailsAtomically()
        {
            var inventory = new MaterialInventory("site");
            inventory.TryAdd(ResourceKind.Metal, 100);
            inventory.TryAdd(ResourceKind.Parts, 5);
            var book = new MaterialCommandBook(); book.Register(inventory);
            Assert.Equal(MaterialCommandStatus.Blocked, book.Consume("build", "site", new[] {
                new MaterialCost(ResourceKind.Metal, 30), new MaterialCost(ResourceKind.Metal, 30),
                new MaterialCost(ResourceKind.Parts, 6) }).Status);
            Assert.Equal(100, MaterialInventory.Restore(book.GetSnapshot("site")).Amount(ResourceKind.Metal));
            Assert.Equal(MaterialCommandStatus.Applied, book.Consume("build", "site", new[] {
                new MaterialCost(ResourceKind.Metal, 30), new MaterialCost(ResourceKind.Metal, 30),
                new MaterialCost(ResourceKind.Parts, 5) }).Status);
            Assert.Equal(40, MaterialInventory.Restore(book.GetSnapshot("site")).Amount(ResourceKind.Metal));
            Assert.Equal(MaterialCommandStatus.Duplicate, book.Consume("build", "site",
                new[] { new MaterialCost(ResourceKind.Metal, 60) }).Status);
        }

        [Fact]
        public void InvalidNumbersAndSnapshotCapacityFailClosed()
        {
            var inventory = new MaterialInventory("site");
            Assert.Throws<ArgumentOutOfRangeException>(() => inventory.TryAdd(ResourceKind.Metal, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RecipeLeg(ResourceKind.Metal, double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => RecipeMath.Evaluate(
                RecipeMath.Pack(1, 1), double.NaN,
                new Dictionary<ResourceKind, double>(), new Dictionary<ResourceKind, double>(), 0));
            var invalid = new InventorySnapshot("site",
                new[] { new KeyValuePair<ResourceKind, double>(ResourceKind.Parts, 21) },
                Array.Empty<OreBatch>(), Array.Empty<DedicatedStorage>());
            Assert.Throws<ArgumentException>(() => MaterialInventory.Restore(invalid));
        }
    }
}

