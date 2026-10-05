using System;
using System.Collections.Generic;
using System.Linq;
using Lodeworks.Sim;
using Xunit;

namespace Lodeworks.Sim.Tests
{
    public sealed class Phase3WorldTests
    {
        private static WorldSimulator World(double metal = 0, double parts = 0,
            double capacity = 2000, double energy = 0, double generation = 20,
            double maxTimestampUncertaintyUT = 0)
        {
            var inventory = new MaterialInventory("site");
            Assert.True(inventory.TryAdd(ResourceKind.Metal, metal));
            Assert.True(inventory.TryAdd(ResourceKind.Parts, parts));
            var book = new MaterialCommandBook(); Assert.True(book.Register(inventory));
            var world = new WorldSimulator(0, book, maxTimestampUncertaintyUT);
            world.RegisterPower(new WorldPower("site", capacity, energy, generation));
            return world;
        }
        private static double Amount(WorldSimulator world, ResourceKind kind) =>
            MaterialInventory.Restore(Assert.Single(world.Materials.Inventories)).Amount(kind);
        private static RecipeDefinition Recipe(ResourceKind input, double inputRate,
            ResourceKind output, double outputRate, double power) =>
            new RecipeDefinition(new[] { new RecipeLeg(input, inputRate) },
                new[] { new RecipeLeg(output, outputRate) }, power);
        private static void Near(double expected, double actual) =>
            Assert.True(Math.Abs(expected - actual) <= 1e-7 + 1e-9 * Math.Abs(expected),
                $"Expected {expected}, observed {actual}");

        [Fact]
        public void EmptyBatteryUsesLiveOwnedGenerationWithoutStockElectricCharge()
        {
            var world = World(metal: 10, generation: 8);
            world.RegisterConverter(new WorldConverter("shop", "site",
                Recipe(ResourceKind.Metal, .05, ResourceKind.Parts, .02, 8), 6));
            Assert.Equal(WorldAdvanceStatus.CaughtUp, world.Advance(100).Status);
            Near(5, Amount(world, ResourceKind.Metal));
            Near(2, Amount(world, ResourceKind.Parts));
            Near(0, world.GetPower("site").Energy);
        }

        [Fact]
        public void SharedPowerUsesPriorityAndEqualGroupProportions()
        {
            var high = World(metal: 100, generation: 8);
            var machine = Recipe(ResourceKind.Metal, .1, ResourceKind.Parts, .1, 8);
            var pack = Recipe(ResourceKind.Metal, .1, ResourceKind.Supplies, .1, 8);
            high.RegisterConverter(new WorldConverter("machine", "site", machine, 1));
            high.RegisterConverter(new WorldConverter("pack", "site", pack, 2));
            high.Advance(10);
            Near(1, Amount(high, ResourceKind.Parts));
            Near(0, Amount(high, ResourceKind.Supplies));

            var equal = World(metal: 100, generation: 8);
            equal.RegisterConverter(new WorldConverter("machine", "site", machine, 1));
            equal.RegisterConverter(new WorldConverter("pack", "site", pack, 1));
            equal.Advance(10);
            Near(.5, Amount(equal, ResourceKind.Parts));
            Near(.5, Amount(equal, ResourceKind.Supplies));
        }

        [Fact]
        public void SharedOreSmeltShopChainMatchesArbitraryPartitions()
        {
            WorldSimulator Build()
            {
                var world = World(generation: 20);
                var ore = new OreBatch("lot", "Kerbin", "Grasslands", .5, 0,
                    ProvenanceClass.Mined, true, 1);
                world.RegisterConverter(new WorldConverter("drill-hook", "site",
                    new RecipeDefinition(Array.Empty<RecipeLeg>(),
                        new[] { new RecipeLeg(ResourceKind.Ore, .5) }, 0), 3,
                    outputOre: ore));
                world.RegisterConverter(new WorldConverter("smelter", "site",
                    Recipe(ResourceKind.Ore, .5, ResourceKind.Metal, .1, 12), 4,
                    inputOreBatchId: "lot"));
                world.RegisterConverter(new WorldConverter("shop", "site",
                    Recipe(ResourceKind.Metal, .1, ResourceKind.Parts, .02, 8), 6));
                return world;
            }
            var whole = Build(); var split = Build();
            Assert.Equal(WorldAdvanceStatus.CaughtUp, whole.Advance(100).Status);
            foreach (double ut in new[] { 7d, 21d, 43d, 89d, 100d }) split.Advance(ut);
            Near(2, Amount(whole, ResourceKind.Parts));
            Near(Amount(whole, ResourceKind.Parts), Amount(split, ResourceKind.Parts));
            Near(Amount(whole, ResourceKind.Metal), Amount(split, ResourceKind.Metal));
            Near(Amount(whole, ResourceKind.Ore), Amount(split, ResourceKind.Ore));
        }

        [Fact]
        public void MidGapArrivalFeedsRemainingTimeAndEqualTimePhaseOrderIsStable()
        {
            var world = World(generation: 20);
            world.RegisterConverter(new WorldConverter("shop", "site",
                Recipe(ResourceKind.Metal, 1, ResourceKind.Parts, .1, 8), 6));
            world.Schedule(new WorldEvent("arrival", 100, WorldEventPhase.Arrival,
                WorldEventKind.MaterialCredit, "site", 10, ResourceKind.Metal));
            world.Schedule(new WorldEvent("player", 100, WorldEventPhase.PlayerCommand,
                WorldEventKind.MaterialDebit, "site", 2, ResourceKind.Metal));
            Assert.Equal(WorldAdvanceStatus.CaughtUp, world.Advance(105).Status);
            Near(3, Amount(world, ResourceKind.Metal));
            Near(.5, Amount(world, ResourceKind.Parts));
            Assert.Equal(2, world.Snapshot().CompletedEventIds.Count);
        }

        [Fact]
        public void ExternalReplacementUsesCaptureOrderBeforeCommandsAndKeepsPriorSnapshot()
        {
            var world = World(metal: 10);
            var before = new WorldVerifiedSnapshot("snap-0", "anchor", true,
                new[] { "Jeb" }, "Kerbin:KSC", "Kerbin", 700000, 0, 0, 0, 0, 0, 0);
            var afterDock = new WorldVerifiedSnapshot("snap-1", "anchor", true,
                new[] { "Jeb", "Val" }, "orbit:station", "Kerbin", 710000, .01, 1, 2, 3, 4, 5);
            var afterUndock = new WorldVerifiedSnapshot("snap-2", "anchor", false,
                new[] { "Jeb" }, "orbit:free-vessel", "Kerbin", 715000, .02, 1, 2, 3, 5, 5);
            // IDs deliberately sort opposite to capture order.
            Assert.True(world.Schedule(new WorldEvent("initial", 0, WorldEventPhase.ExternalReplacement,
                WorldEventKind.ExternalReplacement, "site", 0, captureSequence: 1,
                priorSnapshotId: null, replacement: before)));
            Assert.True(world.Schedule(new WorldEvent("z-dock", 50, WorldEventPhase.ExternalReplacement,
                WorldEventKind.ExternalReplacement, "site", 0, captureSequence: 2,
                priorSnapshotId: "snap-0", replacement: afterDock)));
            Assert.True(world.Schedule(new WorldEvent("a-undock", 50, WorldEventPhase.ExternalReplacement,
                WorldEventKind.ExternalReplacement, "site", 0, captureSequence: 3,
                priorSnapshotId: "snap-1", replacement: afterUndock)));
            Assert.True(world.Schedule(new WorldEvent("player", 50, WorldEventPhase.PlayerCommand,
                WorldEventKind.MaterialDebit, "site", 2, ResourceKind.Metal)));

            Assert.Equal(WorldAdvanceStatus.CaughtUp, world.Advance(0).Status);
            Assert.Contains("snap-0", world.Snapshot().CurrentSnapshotIds);
            Assert.Equal(WorldAdvanceStatus.CatchingUp, world.Advance(50, 1).Status);
            Near(50, world.CursorUT);
            // The interval ending at the external event still sees the prior verified crew/orbit snapshot.
            Assert.Contains("snap-0", world.Snapshot().CurrentSnapshotIds);
            Assert.Equal(WorldAdvanceStatus.CaughtUp, world.Advance(60).Status);
            var saved = world.Snapshot();
            Assert.Equal(new[] { "snap-0", "snap-1", "snap-2" }, saved.VerifiedSnapshots.Select(x => x.SnapshotId));
            Assert.Equal(3, saved.CaptureSequence);
            Assert.True(saved.Frontier!.BoundaryClosed);
            var restored = WorldSimulator.Restore(saved);
            Assert.Equal("snap-2", Assert.Single(restored.Snapshot().CurrentSnapshotIds));
            Assert.Equal(new[] { "Jeb" }, restored.Snapshot().VerifiedSnapshots.Single(x => x.SnapshotId == "snap-2").CrewIds);
            Near(8, Amount(restored, ResourceKind.Metal));
            Assert.False(restored.Schedule(new WorldEvent("late-external", 60,
                WorldEventPhase.ExternalReplacement, WorldEventKind.ExternalReplacement, "site", 0,
                captureSequence: 4, priorSnapshotId: "snap-2",
                replacement: new WorldVerifiedSnapshot("snap-3", "anchor", true,
                    new[] { "Jeb" }, "orbit:late", "Kerbin"))));
            Assert.Equal("late-event-behind-completed-equal-ut-frontier", restored.LastScheduleDiagnostic);
        }

        [Fact]
        public void ReplacementBacklogRoundTripsAndPartitioningPreservesJournalState()
        {
            WorldSimulator Build()
            {
                var world = World(metal: 10);
                var first = new WorldVerifiedSnapshot("first", "anchor", true,
                    new[] { "Jeb" }, "surface", null);
                var second = new WorldVerifiedSnapshot("second", "anchor", true,
                    new[] { "Jeb", "Val" }, "orbit", "Kerbin", 700000, 0, 0, 0, 0, 0, 50);
                world.Schedule(new WorldEvent("replace-1", 50, WorldEventPhase.ExternalReplacement,
                    WorldEventKind.ExternalReplacement, "site", 0, captureSequence: 1,
                    replacement: first));
                world.Schedule(new WorldEvent("replace-2", 75, WorldEventPhase.ExternalReplacement,
                    WorldEventKind.ExternalReplacement, "site", 0, captureSequence: 2,
                    priorSnapshotId: "first", replacement: second));
                return world;
            }
            var whole = Build();
            Assert.Equal(WorldAdvanceStatus.CaughtUp, whole.Advance(100).Status);
            var bounded = Build();
            Assert.Equal(WorldAdvanceStatus.CatchingUp, bounded.Advance(100, 1).Status);
            var saved = bounded.Snapshot();
            Assert.Equal(2, saved.CaptureSequence);
            var restored = WorldSimulator.Restore(saved);
            Assert.Equal(WorldAdvanceStatus.CaughtUp, restored.Advance(100).Status);
            Assert.Equal(whole.Snapshot().VerifiedSnapshots.Select(x => x.Signature),
                restored.Snapshot().VerifiedSnapshots.Select(x => x.Signature));
            Assert.Equal(whole.Snapshot().CurrentSnapshotIds, restored.Snapshot().CurrentSnapshotIds);
            Assert.Equal(whole.Snapshot().Frontier!.Phase, restored.Snapshot().Frontier!.Phase);

            var partitioned = Build();
            partitioned.Advance(25); partitioned.Advance(50); partitioned.Advance(74); partitioned.Advance(100);
            Assert.Equal(whole.Snapshot().CurrentSnapshotIds, partitioned.Snapshot().CurrentSnapshotIds);
            Assert.Equal(whole.Snapshot().CompletedEventIds.OrderBy(x => x),
                partitioned.Snapshot().CompletedEventIds.OrderBy(x => x));
        }

        [Fact]
        public void UnknownAndOverBoundObservationTimesPinCursorAtUncertaintyStart()
        {
            var unknown = World(metal: 10);
            unknown.Schedule(new WorldEvent("unknown", 40, WorldEventPhase.ExternalReplacement,
                WorldEventKind.ExternalReplacement, "site", 0, captureSequence: 1, observedUT: 60,
                timestampQuality: WorldTimestampQuality.Unknown, uncertaintyStartUT: 40,
                replacement: new WorldVerifiedSnapshot("unknown-snap", "anchor", true,
                    new[] { "Jeb" }, "orbit", "Kerbin")));
            var result = unknown.Advance(100);
            Assert.Equal(WorldAdvanceStatus.Blocked, result.Status);
            Near(40, result.CursorUT);
            Near(100, result.TargetUT);
            Near(10, Amount(unknown, ResourceKind.Metal));
            Assert.Single(unknown.Snapshot().PendingEvents);

            var overBound = World(metal: 10);
            overBound.Schedule(new WorldEvent("wide", 60, WorldEventPhase.ExternalReplacement,
                WorldEventKind.ExternalReplacement, "site", 0, captureSequence: 1, observedUT: 60,
                timestampQuality: WorldTimestampQuality.ObservedBounded, uncertaintyStartUT: 40,
                replacement: new WorldVerifiedSnapshot("wide-snap", "anchor", true,
                    new[] { "Jeb" }, "orbit", "Kerbin")));
            Assert.Equal(WorldAdvanceStatus.Blocked, overBound.Advance(100).Status);
            Near(40, overBound.CursorUT);
            Assert.Empty(overBound.Snapshot().VerifiedSnapshots);
        }

        [Fact]
        public void BoundedTimestampRequiresExplicitMeasuredToleranceAndAppliesAtObservation()
        {
            var conservative = World();
            var observation = new WorldEvent("bounded", 60, WorldEventPhase.ExternalReplacement,
                WorldEventKind.ExternalReplacement, "site", 0, captureSequence: 1, observedUT: 60,
                timestampQuality: WorldTimestampQuality.ObservedBounded, uncertaintyStartUT: 59.5,
                replacement: new WorldVerifiedSnapshot("bounded-snap", "anchor", true,
                    new[] { "Jeb" }, "orbit", "Kerbin"));
            Assert.True(conservative.Schedule(observation));
            Assert.Equal(WorldAdvanceStatus.Blocked, conservative.Advance(100).Status);
            Near(59.5, conservative.CursorUT);

            // A measured cadence may opt into a finite bound; events still apply at observedUT, never at interval start.
            var measured = World(maxTimestampUncertaintyUT: 1);
            Assert.True(measured.Schedule(observation));
            Assert.Equal(WorldAdvanceStatus.CaughtUp, measured.Advance(100).Status);
            Near(100, measured.CursorUT);
            Assert.Equal("bounded-snap", Assert.Single(measured.Snapshot().CurrentSnapshotIds));
        }

        [Fact]
        public void EqualUtFrontierAllowsUncommittedCausalEventButRejectsLateEarlierPhase()
        {
            var world = World();
            var first = new WorldVerifiedSnapshot("one", "anchor", true,
                Array.Empty<string>(), "orbit", "Kerbin");
            Assert.True(world.Schedule(new WorldEvent("external-1", 50, WorldEventPhase.ExternalReplacement,
                WorldEventKind.ExternalReplacement, "site", 0, captureSequence: 1, replacement: first)));
            Assert.Equal(WorldAdvanceStatus.CatchingUp, world.Advance(50, 1).Status);
            Assert.True(world.Schedule(new WorldEvent("external-2", 50, WorldEventPhase.ExternalReplacement,
                WorldEventKind.ExternalReplacement, "site", 0, captureSequence: 2, priorSnapshotId: "one",
                replacement: new WorldVerifiedSnapshot("two", "anchor", true,
                    Array.Empty<string>(), "orbit2", "Kerbin"))));
            Assert.Equal(WorldAdvanceStatus.CaughtUp, world.Advance(50).Status);
            Assert.False(world.Schedule(new WorldEvent("external-late", 50, WorldEventPhase.ExternalReplacement,
                WorldEventKind.ExternalReplacement, "site", 0, captureSequence: 3, priorSnapshotId: "two",
                replacement: new WorldVerifiedSnapshot("three", "anchor", true,
                    Array.Empty<string>(), "orbit3", "Kerbin"))));
            Assert.Contains("frontier", world.LastScheduleDiagnostic);
            Assert.False(world.Schedule(new WorldEvent("late-campaign", 50, WorldEventPhase.Campaign,
                WorldEventKind.MaterialDebit, "site", 1, ResourceKind.Metal)));
            Assert.Contains("frontier", world.LastScheduleDiagnostic);
        }

        [Fact]
        public void ReloadReplacesAbandonedJournalAndCaptureSequence()
        {
            var world = World();
            WorldSnapshot loadedSave = world.Snapshot();
            var abandoned = new WorldVerifiedSnapshot("abandoned", "anchor", true,
                Array.Empty<string>(), "orbit-abandoned", "Kerbin");
            Assert.True(world.Schedule(new WorldEvent("abandoned-event", 10,
                WorldEventPhase.ExternalReplacement, WorldEventKind.ExternalReplacement, "site", 0,
                captureSequence: 1, replacement: abandoned)));
            Assert.Equal(WorldAdvanceStatus.CaughtUp, world.Advance(10).Status);

            // Restore is a replacement of the timeline, not a merge with observations from the abandoned save.
            var reloaded = WorldSimulator.Restore(loadedSave);
            var currentTimeline = new WorldVerifiedSnapshot("current", "anchor", true,
                new[] { "Jeb" }, "surface", null);
            Assert.True(reloaded.Schedule(new WorldEvent("current-event", 10,
                WorldEventPhase.ExternalReplacement, WorldEventKind.ExternalReplacement, "site", 0,
                captureSequence: 1, replacement: currentTimeline)));
            Assert.Equal(WorldAdvanceStatus.CaughtUp, reloaded.Advance(10).Status);
            Assert.Equal(new[] { "current" }, reloaded.Snapshot().VerifiedSnapshots.Select(x => x.SnapshotId));
            Assert.DoesNotContain("abandoned-event", reloaded.Snapshot().CompletedEventIds);
        }

        [Fact]
        public void JournalDuplicatePayloadIsNoOpAndConflictingReuseIsDiagnosed()
        {
            var world = World();
            var event1 = new WorldEvent("same", 10, WorldEventPhase.Arrival,
                WorldEventKind.MaterialCredit, "site", 1, ResourceKind.Metal);
            Assert.True(world.Schedule(event1));
            Assert.False(world.Schedule(event1));
            Assert.False(world.Schedule(new WorldEvent("same", 10,
                WorldEventPhase.Arrival, WorldEventKind.MaterialCredit, "site", 2, ResourceKind.Metal)));
            Assert.Equal("conflicting-reused-pending-event-id", world.LastScheduleDiagnostic);

            var replacement = new WorldVerifiedSnapshot("same-snapshot", "anchor", true,
                Array.Empty<string>(), "orbit-a", "Kerbin");
            Assert.True(world.Schedule(new WorldEvent("external-one", 10,
                WorldEventPhase.ExternalReplacement, WorldEventKind.ExternalReplacement, "site", 0,
                captureSequence: 1, replacement: replacement)));
            Assert.False(world.Schedule(new WorldEvent("external-two", 10,
                WorldEventPhase.ExternalReplacement, WorldEventKind.ExternalReplacement, "site", 0,
                captureSequence: 2, priorSnapshotId: "same-snapshot",
                replacement: new WorldVerifiedSnapshot("same-snapshot", "anchor", true,
                    Array.Empty<string>(), "orbit-b", "Kerbin"))));
            Assert.Equal("conflicting-pending-snapshot-id", world.LastScheduleDiagnostic);
            var saved = WorldSimulator.Restore(world.Snapshot()).Snapshot();
            Assert.Equal(2, saved.RejectedEvents.Count);
            Assert.Equal(2, saved.CaptureSequence);
            Assert.Contains(saved.RejectedEvents, x => x.Reason == "conflicting-reused-pending-event-id");
            Assert.Contains(saved.RejectedEvents, x => x.Reason == "conflicting-pending-snapshot-id");
        }

        [Fact]
        public void FullOutputResumesAfterDrainAndPreservesMaterial()
        {
            var world = World(metal: 100, parts: 20);
            world.RegisterConverter(new WorldConverter("shop", "site",
                Recipe(ResourceKind.Metal, 1, ResourceKind.Parts, 1, 8), 6));
            world.Schedule(new WorldEvent("drain", 100, WorldEventPhase.Arrival,
                WorldEventKind.MaterialDebit, "site", 10, ResourceKind.Parts));
            world.Advance(105);
            Near(95, Amount(world, ResourceKind.Metal));
            Near(15, Amount(world, ResourceKind.Parts));
        }

        [Fact]
        public void FullOutputCanBeDrainedByAnotherConverterInSameInterval()
        {
            var world = World(metal: 100, parts: 20);
            world.RegisterConverter(new WorldConverter("shop", "site",
                Recipe(ResourceKind.Metal, 1, ResourceKind.Parts, 1, 8), 6));
            world.RegisterConverter(new WorldConverter("packer", "site",
                Recipe(ResourceKind.Parts, 1, ResourceKind.Supplies, .1, 8), 6,
                enabled: false));
            world.Schedule(new WorldEvent("start-packer", 100, WorldEventPhase.PlayerCommand,
                WorldEventKind.SetConverterEnabled, "site", 1, converterId: "packer"));
            world.Advance(105);
            Near(95, Amount(world, ResourceKind.Metal));
            Near(20, Amount(world, ResourceKind.Parts));
            Near(.5, Amount(world, ResourceKind.Supplies));
        }

        [Fact]
        public void BatteryEmptyBoundaryMatchesPartitionedAdvance()
        {
            WorldSimulator Build()
            {
                var world = World(metal: 100, capacity: 20, energy: 20, generation: 4);
                world.RegisterConverter(new WorldConverter("shop", "site",
                    Recipe(ResourceKind.Metal, 1, ResourceKind.Parts, .1, 8), 6));
                return world;
            }
            var whole = Build(); var split = Build();
            whole.Advance(10);
            split.Advance(2); split.Advance(7); split.Advance(10);
            Near(.75, Amount(whole, ResourceKind.Parts));
            Near(Amount(whole, ResourceKind.Parts), Amount(split, ResourceKind.Parts));
            Near(0, whole.GetPower("site").Energy);
        }

        [Fact]
        public void ReservationRemainsUnusableDuringWorldCatchup()
        {
            var site = new MaterialInventory("site"); site.TryAdd(ResourceKind.Metal, 100);
            var book = new MaterialCommandBook(); book.Register(site);
            Assert.Equal(ReservationStatus.Applied, book.Reserve(new ReservationRecord("build", "shop",
                "construction", "site", ReservationKind.Matter, 60, 0, 1,
                ResourceKind.Metal)).Status);
            var world = new WorldSimulator(0, book);
            world.RegisterPower(new WorldPower("site", 0, 0, 100));
            world.RegisterConverter(new WorldConverter("machine", "site",
                Recipe(ResourceKind.Metal, 1, ResourceKind.Parts, .1, 1), 6));
            world.Advance(100);
            Near(60, Amount(world, ResourceKind.Metal));
            Near(4, Amount(world, ResourceKind.Parts));
        }

        [Fact]
        public void TwoOreConsumersCannotCrossOneOverlappingProtectedFloor()
        {
            var site = new MaterialInventory("site");
            site.TryAddOre(new OreBatch("a", "Kerbin", "Grasslands", .5, 50,
                ProvenanceClass.Mined, true, 1));
            site.TryAddOre(new OreBatch("b", "Kerbin", "Grasslands", .5, 50,
                ProvenanceClass.Mined, true, 2));
            var book = new MaterialCommandBook(); book.Register(site);
            book.SetFloor(new ProtectedFloor("quota", "site", ResourceKind.Ore, 80,
                new OreFilter(body: "Kerbin", campaignEligible: true)));
            var world = new WorldSimulator(0, book);
            world.RegisterPower(new WorldPower("site", 0, 0, 100));
            var recipe = Recipe(ResourceKind.Ore, 1, ResourceKind.Metal, .1, 1);
            world.RegisterConverter(new WorldConverter("smelt-a", "site", recipe, 4,
                inputOreBatchId: "a"));
            world.RegisterConverter(new WorldConverter("smelt-b", "site", recipe, 4,
                inputOreBatchId: "b"));
            Assert.Equal(WorldAdvanceStatus.CaughtUp, world.Advance(100).Status);
            Near(80, Amount(world, ResourceKind.Ore));
            Near(2, Amount(world, ResourceKind.Metal));
        }

        [Fact]
        public void OptionalOutputStoresWhenRoomExistsAndCountsOnlyOverflowAsDumped()
        {
            WorldSimulator Build(bool slagBin, double initialSlag = 0)
            {
                var site = new MaterialInventory("site");
                site.TryAdd(ResourceKind.Metal, 10);
                if (slagBin) site.TryAddStorage(new DedicatedStorage("slag-bin", ResourceKind.Slag,
                    StorageTier.Basic));
                if (initialSlag > 0) Assert.True(site.TryAdd(ResourceKind.Slag, initialSlag));
                var book = new MaterialCommandBook(); book.Register(site);
                var world = new WorldSimulator(0, book);
                world.RegisterPower(new WorldPower("site", 0, 0, 10));
                world.RegisterConverter(new WorldConverter("machine", "site",
                    new RecipeDefinition(new[] { new RecipeLeg(ResourceKind.Metal, 1) },
                        new[] { new RecipeLeg(ResourceKind.Parts, .1),
                            new RecipeLeg(ResourceKind.Slag, 1) }, 1), 6,
                    dumpOutputs: new[] { ResourceKind.Slag }));
                return world;
            }
            var noBin = Build(false); noBin.Advance(5);
            Near(0, Amount(noBin, ResourceKind.Slag));
            Near(5, noBin.GetDumped("machine", ResourceKind.Slag));
            var restored = WorldSimulator.Restore(noBin.Snapshot());
            restored.Advance(10);
            Near(10, restored.GetDumped("machine", ResourceKind.Slag));
            var withBin = Build(true); withBin.Advance(10);
            Near(10, Amount(withBin, ResourceKind.Slag));
            Near(0, withBin.GetDumped("machine", ResourceKind.Slag));
            var nearlyFull = Build(true, 395); nearlyFull.Advance(10);
            Near(400, Amount(nearlyFull, ResourceKind.Slag));
            Near(5, nearlyFull.GetDumped("machine", ResourceKind.Slag));
        }

        [Fact]
        public void BoundedCatchupSnapshotResumesWithoutReplayingArrival()
        {
            WorldSimulator Build()
            {
                var world = World(generation: 20);
                world.RegisterConverter(new WorldConverter("shop", "site",
                    Recipe(ResourceKind.Metal, 1, ResourceKind.Parts, .1, 8), 6));
                world.Schedule(new WorldEvent("arrival", 50, WorldEventPhase.Arrival,
                    WorldEventKind.MaterialCredit, "site", 80, ResourceKind.Metal));
                return world;
            }
            var reference = Build(); reference.Advance(100);
            var bounded = Build();
            Assert.Equal(WorldAdvanceStatus.CatchingUp, bounded.Advance(100, 1).Status);
            Near(50, bounded.CursorUT);
            var saved = bounded.Snapshot();
            Assert.Equal(100, saved.TargetUT);
            var restored = WorldSimulator.Restore(saved);
            Assert.Equal(WorldAdvanceStatus.CaughtUp, restored.Advance(100).Status);
            Near(Amount(reference, ResourceKind.Parts), Amount(restored, ResourceKind.Parts));
            Near(Amount(reference, ResourceKind.Metal), Amount(restored, ResourceKind.Metal));
            Assert.False(restored.Schedule(new WorldEvent("arrival", 50, WorldEventPhase.Arrival,
                WorldEventKind.MaterialCredit, "site", 80, ResourceKind.Metal)));
            Assert.False(restored.Schedule(new WorldEvent("arrival", 100,
                WorldEventPhase.Arrival, WorldEventKind.MaterialCredit, "site", 80, ResourceKind.Metal)));
            Assert.Equal("conflicting-reused-completed-event-id", restored.LastScheduleDiagnostic);
        }

        [Fact]
        public void RollbackRequiresLoadedSnapshotAndDoesNotMutateCurrentWorld()
        {
            var world = World(metal: 10);
            var saved = world.Snapshot();
            world.Advance(100);
            Assert.Equal(WorldAdvanceStatus.RequiresRestore, world.Advance(50).Status);
            Near(100, world.CursorUT);
            var loaded = WorldSimulator.Restore(saved);
            Near(0, loaded.CursorUT);
        }

        [Fact]
        public void BlockedArrivalKeepsCursorAndPendingEventForARecoveryDecision()
        {
            var world = World(parts: 20);
            world.Schedule(new WorldEvent("arrival", 50, WorldEventPhase.Arrival,
                WorldEventKind.MaterialCredit, "site", 1, ResourceKind.Parts));
            var result = world.Advance(100);
            Assert.Equal(WorldAdvanceStatus.Blocked, result.Status);
            Near(50, result.CursorUT);
            Near(100, result.TargetUT);
            Near(20, Amount(world, ResourceKind.Parts));
            Assert.Single(world.Snapshot().PendingEvents);
        }

        [Fact]
        public void EventAtTargetStillCountsAsUnfinishedCatchupUntilCommitted()
        {
            var world = World();
            world.Schedule(new WorldEvent("arrival", 50, WorldEventPhase.Arrival,
                WorldEventKind.MaterialCredit, "site", 1, ResourceKind.Metal));
            Assert.Equal(WorldAdvanceStatus.CatchingUp, world.Advance(50, 1).Status);
            Assert.True(world.CatchingUp);
            Near(0, Amount(world, ResourceKind.Metal));
            Assert.Equal(WorldAdvanceStatus.CaughtUp, world.Advance(50).Status);
            Assert.False(world.CatchingUp);
            Near(1, Amount(world, ResourceKind.Metal));
        }

        [Fact]
        public void ConverterCycleCannotCreateGoodsFromEmptyBuffers()
        {
            var world = World();
            world.RegisterConverter(new WorldConverter("a", "site",
                Recipe(ResourceKind.Metal, 1, ResourceKind.Parts, 1, 1), 1));
            Assert.Throws<ArgumentException>(() => world.RegisterConverter(new WorldConverter("b", "site",
                Recipe(ResourceKind.Parts, 1, ResourceKind.Metal, 1, 1), 1)));
            Assert.Equal(WorldAdvanceStatus.CaughtUp, world.Advance(10).Status);
            Near(0, Amount(world, ResourceKind.Parts));
        }

        [Fact]
        public void ProductionCannotFillPromisedIncomingCapacity()
        {
            var site = new MaterialInventory("site"); site.TryAdd(ResourceKind.Metal, 10);
            var book = new MaterialCommandBook(); book.Register(site);
            Assert.Equal(ReservationStatus.Applied, book.Reserve(new ReservationRecord(
                "incoming", "tug", "arrival", "site", ReservationKind.IncomingCapacity,
                18, 0, 1, ResourceKind.Parts)).Status);
            var world = new WorldSimulator(0, book);
            world.RegisterPower(new WorldPower("site", 0, 0, 10));
            world.RegisterConverter(new WorldConverter("shop", "site",
                Recipe(ResourceKind.Metal, 1, ResourceKind.Parts, 1, 1), 6));
            world.Advance(10);
            Near(2, Amount(world, ResourceKind.Parts));
            Near(8, Amount(world, ResourceKind.Metal));
        }
    }
}

