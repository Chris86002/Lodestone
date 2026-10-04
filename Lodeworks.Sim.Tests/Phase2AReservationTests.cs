using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Lodeworks.Sim.Tests
{
    public sealed class Phase2AReservationTests
    {
        private static readonly StockFuelMetadata Fuel = new StockFuelMetadata(0.005, 0.005, 0.8, 0.18);
        private static ReservationRecord Matter(string id, string owner, string endpoint,
            ResourceKind kind, double quantity, string? lot = null, string ownerType = "construction",
            IEnumerable<string>? overrides = null) => new ReservationRecord(id, owner, ownerType,
                endpoint, ReservationKind.Matter, quantity, 100, 1, kind, oreBatchId: lot,
                authorizedFloorIds: overrides);
        private static MaterialInventory MetalSite(double units)
        {
            var site = new MaterialInventory("site");
            Assert.True(site.TryAdd(ResourceKind.Metal, units));
            return site;
        }

        [Fact]
        public void ExistingSixtyOfHundredClaimBlocksLaterEightyExport()
        {
            var book = new MaterialCommandBook(); book.Register(MetalSite(100));
            Assert.Equal(ReservationStatus.Applied,
                book.Reserve(Matter("build", "first-shop", "site", ResourceKind.Metal, 60)).Status);
            Assert.Equal(MaterialCommandStatus.Blocked,
                book.Transfer("export", "site", "ksc", ResourceKind.Metal, 80).Status);
            Assert.Equal(ReservationStatus.Blocked,
                book.Reserve(Matter("route", "route-1", "site", ResourceKind.Metal, 80,
                    ownerType: "export")).Status);
            Assert.Equal(100, MaterialInventory.Restore(book.GetSnapshot("site")).Amount(ResourceKind.Metal));
            Assert.Equal(ReservationStatus.Applied, book.ReleaseCommitted("build", "cancel-build", 60).Status);
            Assert.Equal(ReservationStatus.Applied,
                book.Reserve(Matter("route", "route-1", "site", ResourceKind.Metal, 80,
                    ownerType: "export")).Status);
        }

        [Fact]
        public void ReverseChronologicalClaimWinsWithoutStealingExistingPromise()
        {
            var site = MetalSite(100); var reservations = new ReservationBook();
            Assert.Equal(ReservationStatus.Applied,
                reservations.Reserve(Matter("export", "route", "site", ResourceKind.Metal, 80), site).Status);
            Assert.Equal(ReservationStatus.Blocked,
                reservations.Reserve(Matter("build", "shop", "site", ResourceKind.Metal, 60), site).Status);
            Assert.Equal(20, reservations.AvailableMatter(site, ResourceKind.Metal));
        }

        [Fact]
        public void OverlappingBroadAndRichOreFloorsProtectMatchingLotsWithoutSummingFloors()
        {
            var site = new MaterialInventory("site");
            site.TryAddOre(new OreBatch("rich", "Mun", "Crater", 0.8, 80, ProvenanceClass.Mined, true, 1));
            site.TryAddOre(new OreBatch("poor", "Mun", "Crater", 0.3, 20, ProvenanceClass.Mined, true, 2));
            var book = new ReservationBook();
            book.SetFloor(new ProtectedFloor("all", "site", ResourceKind.Ore, 60, new OreFilter(campaignEligible: true)));
            book.SetFloor(new ProtectedFloor("rich", "site", ResourceKind.Ore, 40,
                new OreFilter(body: "Mun", band: GradeBand.Rich, campaignEligible: true)));
            Assert.Equal(40, book.AvailableMatter(site, ResourceKind.Ore, "rich"));
            Assert.Equal(20, book.AvailableMatter(site, ResourceKind.Ore, "poor"));
            Assert.Equal(ReservationStatus.Applied,
                book.Reserve(Matter("rich-claim", "ship", "site", ResourceKind.Ore, 40, "rich"), site).Status);
            Assert.Equal(0, book.AvailableMatter(site, ResourceKind.Ore, "poor"));
            Assert.Equal(0, book.AvailableMatter(site, ResourceKind.Ore, "rich"));
        }

        [Fact]
        public void QuotaFloorIsBoundedByAlreadyAssignedCargoAndExplicitExportCanUseIt()
        {
            var site = new MaterialInventory("site");
            site.TryAddOre(new OreBatch("lot", "Mun", "Crater", 0.8, 100, ProvenanceClass.Mined, true, 1));
            var book = new ReservationBook();
            book.SetFloor(new ProtectedFloor("quota", "site", ResourceKind.Ore, 80,
                new OreFilter(campaignEligible: true), objectiveRemaining: 100, assigned: 70));
            Assert.Equal(70, book.AvailableMatter(site, ResourceKind.Ore, "lot"));
            Assert.Equal(ReservationStatus.Blocked,
                book.Reserve(Matter("bad", "route", "site", ResourceKind.Ore, 80, "lot",
                    ownerType: "export", overrides: new[] { "quota" }), site).Status);
            Assert.Equal(ReservationStatus.Applied,
                book.Reserve(Matter("good", "route", "site", ResourceKind.Ore, 80, "lot",
                    ownerType: "campaignExport", overrides: new[] { "quota" }), site).Status);
            Assert.Equal(0, book.AvailableMatter(site, ResourceKind.Ore, "lot"));
        }

        [Fact]
        public void IncomingCapacityAndSlotPromisesDoNotAddMatterOrCapacity()
        {
            var site = new MaterialInventory("depot");
            site.TryAdd(ResourceKind.LiquidFuel, 40);
            var book = new ReservationBook();
            var incoming = new ReservationRecord("incoming", "tug-a", "arrival", "depot",
                ReservationKind.IncomingCapacity, 4, 10, 1, ResourceKind.LiquidFuel);
            Assert.Equal(ReservationStatus.Applied, book.Reserve(incoming, site).Status);
            Assert.Equal(45, site.Capacity(ResourceKind.LiquidFuel));
            Assert.Equal(40, site.Amount(ResourceKind.LiquidFuel));
            Assert.Equal(1, book.AvailableCapacity(site, ResourceKind.LiquidFuel));
            Assert.Equal(ReservationStatus.Blocked, book.Reserve(new ReservationRecord("second", "tug-b",
                "arrival", "depot", ReservationKind.IncomingCapacity, 2, 11, 1,
                ResourceKind.LiquidFuel), site).Status);
            book.SetSlotSupply(new SlotSupply("depot", "berth", 2, 0));
            Assert.Equal(ReservationStatus.Applied, book.Reserve(new ReservationRecord("berth-a", "tug-a",
                "parking", "depot", ReservationKind.Slot, 1, 10, 1, slotKey: "berth"), null).Status);
            Assert.Equal(ReservationStatus.Applied, book.Reserve(new ReservationRecord("berth-b", "tug-b",
                "parking", "depot", ReservationKind.Slot, 1, 10, 1, slotKey: "berth"), null).Status);
            Assert.Equal(0, book.AvailableSlot("depot", "berth"));
        }

        [Fact]
        public void EscrowRemovesStockOnceAndPartialReturnAndConsumptionConserveOwnership()
        {
            var book = new MaterialCommandBook(); book.Register(MetalSite(100));
            Assert.Equal(ReservationStatus.Applied,
                book.Reserve(Matter("build", "shop", "site", ResourceKind.Metal, 60)).Status);
            Assert.Equal(ReservationStatus.Applied, book.CommitToEscrow("build", "escrow-1").Status);
            Assert.Equal(40, MaterialInventory.Restore(book.GetSnapshot("site")).Amount(ResourceKind.Metal));
            var active = ReservationBook.Restore(book.ReservationSnapshot,
                new Dictionary<string, MaterialInventory> { ["site"] = MaterialInventory.Restore(book.GetSnapshot("site")) });
            Assert.Equal(0.6, active.EscrowMass(Fuel), 12);
            Assert.Equal(ReservationStatus.Duplicate, book.CommitToEscrow("build", "escrow-1").Status);
            Assert.Equal(ReservationStatus.Blocked, book.CommitToEscrow("build", "escrow-again").Status);
            Assert.Equal(ReservationStatus.Applied, book.ReturnEscrow("build", "return-20", 20).Status);
            Assert.Equal(60, MaterialInventory.Restore(book.GetSnapshot("site")).Amount(ResourceKind.Metal));
            Assert.Equal(ReservationStatus.Applied, book.ConsumeEscrow("build", "consume-40", 40).Status);
            Assert.Equal(ReservationState.Consumed,
                Assert.Single(book.ReservationSnapshot.Records).State);
            Assert.Equal(60, MaterialInventory.Restore(book.GetSnapshot("site")).Amount(ResourceKind.Metal));
            Assert.Equal(0, ReservationBook.Restore(book.ReservationSnapshot,
                new Dictionary<string, MaterialInventory> { ["site"] = MaterialInventory.Restore(book.GetSnapshot("site")) }).EscrowMass(Fuel));
        }

        [Fact]
        public void ReturningEscrowCannotConsumePromisedIncomingCapacity()
        {
            var book = new MaterialCommandBook(); book.Register(MetalSite(100));
            Assert.Equal(ReservationStatus.Applied,
                book.Reserve(Matter("build", "shop", "site", ResourceKind.Metal, 80)).Status);
            Assert.Equal(ReservationStatus.Applied, book.CommitToEscrow("build", "escrow").Status);
            Assert.Equal(ReservationStatus.Applied, book.Reserve(new ReservationRecord("incoming", "tug",
                "arrival", "site", ReservationKind.IncomingCapacity, 80, 10, 1,
                ResourceKind.Metal)).Status);
            Assert.Equal(ReservationStatus.Blocked, book.ReturnEscrow("build", "return", 20).Status);
            Assert.Equal(20, MaterialInventory.Restore(book.GetSnapshot("site")).Amount(ResourceKind.Metal));
            Assert.Equal(ReservationStatus.Applied, book.ReleaseCommitted("incoming", "release", 80).Status);
            Assert.Equal(ReservationStatus.Applied, book.ReturnEscrow("build", "return", 20).Status);
            Assert.Equal(40, MaterialInventory.Restore(book.GetSnapshot("site")).Amount(ResourceKind.Metal));
        }

        [Fact]
        public void RemovingStorageCannotInvalidateIncomingCapacityPromise()
        {
            var site = MetalSite(100);
            Assert.True(site.TryAddStorage(new DedicatedStorage("bin", ResourceKind.Metal, StorageTier.Basic)));
            var book = new MaterialCommandBook(); book.Register(site);
            Assert.Equal(ReservationStatus.Applied, book.Reserve(new ReservationRecord("incoming", "tug",
                "arrival", "site", ReservationKind.IncomingCapacity, 300, 10, 1,
                ResourceKind.Metal)).Status);
            Assert.Equal(MaterialCommandStatus.Blocked, book.RemoveStorage("remove", "site", "bin").Status);
            Assert.Single(book.GetSnapshot("site").Storage);
            Assert.Equal(ReservationStatus.Applied, book.ReleaseCommitted("incoming", "release", 300).Status);
            Assert.Equal(MaterialCommandStatus.Applied, book.RemoveStorage("remove", "site", "bin").Status);
            Assert.Empty(book.GetSnapshot("site").Storage);
        }

        [Fact]
        public void MaterialTransferAndRecipeCannotBypassPromises()
        {
            var source = MetalSite(100);
            var dest = new MaterialInventory("dest");
            var book = new MaterialCommandBook(); book.Register(source); book.Register(dest);
            book.Reserve(Matter("shop", "shop-job", "site", ResourceKind.Metal, 80));
            Assert.Equal(MaterialCommandStatus.Blocked,
                book.Transfer("move", "site", "dest", ResourceKind.Metal, 30).Status);
            Assert.Equal(MaterialCommandStatus.Blocked,
                book.Consume("cost", "site", new[] { new MaterialCost(ResourceKind.Metal, 30) }).Status);
            var machine = RecipeMath.Machine(1, 1);
            var result = book.Process("process", "site", machine, 1000, 10000);
            Assert.Equal(MaterialCommandStatus.Applied, result.Command.Status);
            Assert.Equal(20, result.Recipe!.Consumed[ResourceKind.Metal]);
            Assert.Equal(80, MaterialInventory.Restore(book.GetSnapshot("site")).Amount(ResourceKind.Metal));
        }

        [Fact]
        public void DuplicateReservationAndReleaseReceiptsAreIdempotent()
        {
            var site = MetalSite(100); var book = new ReservationBook();
            var request = Matter("claim", "shop", "site", ResourceKind.Metal, 60);
            Assert.Equal(ReservationStatus.Applied, book.Reserve(request, site).Status);
            Assert.Equal(ReservationStatus.Duplicate, book.Reserve(request, site).Status);
            Assert.Equal(ReservationStatus.Conflict,
                book.Reserve(Matter("claim", "other", "site", ResourceKind.Metal, 60), site).Status);
            Assert.Equal(ReservationStatus.Applied, book.ReleaseCommitted("claim", "release-20", 20).Status);
            Assert.Equal(ReservationStatus.Duplicate, book.ReleaseCommitted("claim", "release-20", 20).Status);
            Assert.Equal(60, book.AvailableMatter(site, ResourceKind.Metal));
            Assert.Equal(40, book.Find("claim")!.Quantity);
        }

        [Fact]
        public void RestoreQuarantinesConflictingClaimsWithoutDeletingStock()
        {
            var site = MetalSite(100);
            var original = new ReservationSnapshot(new[] {
                Matter("first", "shop", "site", ResourceKind.Metal, 60),
                Matter("second", "route", "site", ResourceKind.Metal, 80)
            }, Array.Empty<ProtectedFloor>(), Array.Empty<SlotSupply>(), Array.Empty<string>());
            var restored = ReservationBook.Restore(original,
                new Dictionary<string, MaterialInventory> { ["site"] = site });
            Assert.Single(restored.Conflicts);
            Assert.Single(restored.Records);
            Assert.Equal("second", Assert.Single(restored.QuarantinedRecords).Id);
            Assert.Equal(100, site.Amount(ResourceKind.Metal));
            Assert.Equal(40, restored.AvailableMatter(site, ResourceKind.Metal));
            var again = ReservationBook.Restore(restored.Snapshot(),
                new Dictionary<string, MaterialInventory> { ["site"] = site });
            Assert.Single(again.QuarantinedRecords);
        }

        [Fact]
        public void PhaseTwoSnapshotRestoresWithoutInventingReservations()
        {
            var old = new MaterialBookSnapshot(new[] { MetalSite(25).Snapshot() },
                new[] { "already-paid" });
            Assert.Equal(1, old.Version);
            var restored = MaterialCommandBook.Restore(old);
            Assert.Empty(restored.ReservationSnapshot.Records);
            Assert.Equal(25, MaterialInventory.Restore(restored.GetSnapshot("site")).Amount(ResourceKind.Metal));
            Assert.Equal(MaterialCommandStatus.Duplicate,
                restored.Consume("already-paid", "site", new[] { new MaterialCost(ResourceKind.Metal, 5) }).Status);
        }
    }
}

