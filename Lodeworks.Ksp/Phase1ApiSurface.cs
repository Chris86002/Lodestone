using System;
using Contracts;
using Lodeworks.Sim;

namespace Lodeworks.Ksp
{
    // Compile-time probes against the installed KSP assemblies. No constructor
    // mutates the current game. Later phases supply persisted scenario records.
    internal static class Phase1ApiSurface
    {
        internal static bool ContractAvailable() => ContractSystem.Instance != null;

        internal static bool StockTechAvailable(AvailablePart part) =>
            part != null && ResearchAndDevelopment.Instance != null &&
            ResearchAndDevelopment.PartTechAvailable(part);

        internal static double CurrentFunds() => Funding.Instance?.Funds ?? 0;

        // This method group pins the stock mutation signature without changing
        // funds before Phase 4 supplies durable transaction receipts.
        internal static Action<double, TransactionReasons>? FundsWriter(Funding funding) =>
            funding == null ? null : new Action<double, TransactionReasons>(funding.AddFunds);

        // Diagnostic only: the loaded/docked and selected-tank constraints must
        // be confirmed before exposing this to a player command.
        internal static FuelBridgeResult RequestStockFuel(Part selectedTank,
            string resourceName, double unitsToConsume)
        {
            if (selectedTank == null || selectedTank.vessel == null ||
                (resourceName != "LiquidFuel" && resourceName != "Oxidizer") ||
                unitsToConsume == 0 || double.IsNaN(unitsToConsume) ||
                double.IsInfinity(unitsToConsume))
                throw new ArgumentException("Invalid selected tank or fuel request.");
            double actual = selectedTank.RequestResource(resourceName, unitsToConsume);
            return FuelBridgeAccounting.FromRequest(unitsToConsume, actual);
        }

        internal static string VesselReference(Vessel vessel) =>
            vessel?.id.ToString("D") ?? string.Empty;

        internal static bool IsBoundOrbit(Vessel vessel) =>
            vessel != null && vessel.orbit != null &&
            vessel.orbit.referenceBody != null &&
            vessel.orbit.eccentricity >= 0 && vessel.orbit.eccentricity < 1;

        internal static double OrbitPeriapsis(Vessel vessel) =>
            vessel?.orbit?.PeA ?? double.NaN;

        internal static double ResourceDensity(string name)
        {
            var definition = PartResourceLibrary.Instance?.GetDefinition(name);
            return definition?.density ?? double.NaN;
        }
    }

    // The stock interface accepts a per-part delta. Actual packed and unloaded
    // propagation must be checked in-game before the depot feature is accepted.
    public sealed class Phase1DepotMassProbe : PartModule, IPartMassModifier
    {
        [KSPField(isPersistant = true)]
        public float probeCargoMass;

        public float GetModuleMass(float defaultMass, ModifierStagingSituation situation) =>
            DepotMass.AddedMass(new[] { (double)probeCargoMass });

        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.CONSTANTLY;
    }
}
