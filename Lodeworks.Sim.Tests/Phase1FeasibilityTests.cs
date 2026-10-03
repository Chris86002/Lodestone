using System;
using System.Collections.Generic;
using Xunit;

namespace Lodeworks.Sim.Tests
{
    public sealed class Phase1FeasibilityTests
    {
        [Fact]
        public void BlueprintDecisionIsSaveSpecificAndSandboxBypassesCareerGates()
        {
            var saveA = new HashSet<string> { "lw-depot-core" };
            var saveB = new HashSet<string>();
            Assert.True(BlueprintGate.IsAvailable("lw-depot-core", true, false, saveA));
            Assert.False(BlueprintGate.IsAvailable("lw-depot-core", true, false, saveB));
            Assert.False(BlueprintGate.IsAvailable("lw-depot-core", false, false, saveA));
            Assert.True(BlueprintGate.IsAvailable("lw-depot-core", false, true, saveB));
            Assert.True(BlueprintGate.IsAvailable("lw-depot-core", true, true, saveB));
        }

        [Fact]
        public void PartialBridgeBooksOnlyActualTransferAndConservesMass()
        {
            var lf = FuelBridgeAccounting.FromRequest(100, 31);
            var ox = FuelBridgeAccounting.FromRequest(100, 19);
            Assert.Equal(-31, lf.StockDelta);
            Assert.Equal(31, lf.LedgerDelta);
            Assert.Equal(0, FuelBridgeAccounting.MassDelta(
                lf.StockDelta + lf.LedgerDelta, ox.StockDelta + ox.LedgerDelta,
                0.005, 0.005));
            var reverse = FuelBridgeAccounting.FromRequest(-50, -12);
            Assert.Equal(12, reverse.StockDelta);
            Assert.Equal(-12, reverse.LedgerDelta);
            Assert.Equal(0, FuelBridgeAccounting.FromRequest(50, 0).LedgerDelta);
        }

        [Fact]
        public void BadBridgeResultsCannotMintFuel()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FuelBridgeAccounting.FromRequest(10, -2));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FuelBridgeAccounting.FromRequest(10, 11));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FuelBridgeAccounting.FromRequest(10, double.NaN));
        }

        [Fact]
        public void DepotMassCountsOnlyPhysicalMatterAndRejectsInvalidValues()
        {
            Assert.Equal(6.5f, DepotMass.AddedMass(new[] { 2.0, 3.5, 1.0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DepotMass.AddedMass(new[] { 1.0, -1.0 }));
        }
    }
}

