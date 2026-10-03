using System;
using System.Collections.Generic;

namespace Lodeworks.Sim
{
    // Pure decisions for the Phase 1 adapter probes. These are deliberately
    // independent of KSP's process-wide singleton and part prefabs.
    public static class BlueprintGate
    {
        public static bool IsAvailable(string partName, bool stockTechResearched,
            bool isSandbox, ISet<string> rewardedParts)
        {
            if (string.IsNullOrWhiteSpace(partName) || rewardedParts == null)
                return false;
            return isSandbox || (stockTechResearched && rewardedParts.Contains(partName));
        }
    }

    public readonly struct FuelBridgeResult
    {
        public FuelBridgeResult(double stockDelta, double ledgerDelta)
        {
            StockDelta = stockDelta;
            LedgerDelta = ledgerDelta;
        }

        public double StockDelta { get; }
        public double LedgerDelta { get; }
    }

    public static class FuelBridgeAccounting
    {
        // The adapter must confirm the RequestResource sign convention in-game.
        // Only the observed result, never the requested quantity, is booked.
        public static FuelBridgeResult FromRequest(double requestedStockConsumption,
            double actualRequestResult)
        {
            if (double.IsNaN(requestedStockConsumption) ||
                double.IsInfinity(requestedStockConsumption) ||
                double.IsNaN(actualRequestResult) ||
                double.IsInfinity(actualRequestResult) ||
                requestedStockConsumption == 0 ||
                (actualRequestResult != 0 &&
                Math.Sign(requestedStockConsumption) != Math.Sign(actualRequestResult)) ||
                Math.Abs(actualRequestResult) > Math.Abs(requestedStockConsumption) + 1e-8)
                throw new ArgumentOutOfRangeException(nameof(actualRequestResult));
            return new FuelBridgeResult(-actualRequestResult, actualRequestResult);
        }

        public static double MassDelta(double liquidFuelUnits, double oxidizerUnits,
            double liquidFuelDensity, double oxidizerDensity)
        {
            if (!FiniteNonnegative(liquidFuelDensity) || !FiniteNonnegative(oxidizerDensity) ||
                double.IsNaN(liquidFuelUnits) || double.IsInfinity(liquidFuelUnits) ||
                double.IsNaN(oxidizerUnits) || double.IsInfinity(oxidizerUnits))
                throw new ArgumentOutOfRangeException(nameof(liquidFuelDensity));
            return liquidFuelUnits * liquidFuelDensity + oxidizerUnits * oxidizerDensity;
        }

        private static bool FiniteNonnegative(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
    }

    public static class DepotMass
    {
        public static float AddedMass(IEnumerable<double> physicallyOwnedMasses)
        {
            if (physicallyOwnedMasses == null) throw new ArgumentNullException(nameof(physicallyOwnedMasses));
            double sum = 0;
            foreach (double mass in physicallyOwnedMasses)
            {
                if (double.IsNaN(mass) || double.IsInfinity(mass) || mass < 0)
                    throw new ArgumentOutOfRangeException(nameof(physicallyOwnedMasses));
                sum += mass;
            }
            if (double.IsInfinity(sum) || sum > float.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(physicallyOwnedMasses));
            return (float)sum;
        }
    }
}

