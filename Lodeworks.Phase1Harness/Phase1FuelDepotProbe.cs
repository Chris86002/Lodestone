using System;
using System.Globalization;
using System.Linq;
using Lodeworks.Ksp;
using UnityEngine;

namespace Lodeworks.Phase1Harness
{
    // Opt-in flight diagnostic. Its persisted ledger is intentionally local to
    // this test part; the production mod has no Phase 1 depot gameplay.
    public sealed class Phase1FuelDepotProbe : PartModule, IPartMassModifier
    {
        [KSPField(isPersistant = true)] public double ledgerLiquidFuel;
        [KSPField(isPersistant = true)] public double ledgerOxidizer;
        [KSPField(isPersistant = true)] public float extraCargoMass;

        private string lastVesselId = string.Empty;
        private string lastOrbit = string.Empty;
        private float nextObservation;

        [KSPEvent(guiActive = true, guiName = "Probe: stock to ledger (partial)")]
        public void StockToLedger() => Transfer(50, 60);

        [KSPEvent(guiActive = true, guiName = "Probe: ledger to stock (partial)")]
        public void LedgerToStock() => Transfer(-ledgerLiquidFuel, -ledgerOxidizer);

        [KSPEvent(guiActive = true, guiName = "Probe: prepare partial reverse fixture")]
        public void PreparePartialReverse()
        {
            // Explicit diagnostic setup: creates test quantities and must never
            // be mistaken for a gameplay or bridge transaction.
            Observe("reverse fixture before (diagnostic grant follows)");
            var lf = part.Resources.Get("LiquidFuel");
            var ox = part.Resources.Get("Oxidizer");
            lf.amount = lf.maxAmount - 5;
            ox.amount = ox.maxAmount - 6;
            ledgerLiquidFuel = 10;
            ledgerOxidizer = 12;
            part.UpdateMass();
            Observe("reverse fixture ready (diagnostic grant)");
        }

        [KSPEvent(guiActive = true, guiName = "Probe: add 1 t cargo")]
        public void AddCargoMass()
        {
            Observe("cargo before");
            extraCargoMass += 1f;
            part.UpdateMass();
            Observe("cargo after");
        }

        [KSPEvent(guiActive = true, guiName = "Probe: remove 1 t cargo")]
        public void RemoveCargoMass()
        {
            Observe("cargo before");
            extraCargoMass = Math.Max(0, extraCargoMass - 1f);
            part.UpdateMass();
            Observe("cargo after");
        }

        [KSPEvent(guiActive = true, guiName = "Probe: log vessel, orbit, mass")]
        public void LogState() => Observe("manual");

        public float GetModuleMass(float defaultMass, ModifierStagingSituation situation) =>
            extraCargoMass + (float)(ledgerLiquidFuel * Density("LiquidFuel") +
                ledgerOxidizer * Density("Oxidizer"));

        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.CONSTANTLY;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (HighLogic.LoadedSceneIsFlight) Observe("OnStart " + state);
        }

        private void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null ||
                Time.realtimeSinceStartup < nextObservation) return;
            nextObservation = Time.realtimeSinceStartup + 5f;
            string vesselId = vessel.id.ToString("D");
            string orbit = OrbitText();
            if (vesselId != lastVesselId || orbit != lastOrbit)
            {
                Observe("identity/orbit changed");
                lastVesselId = vesselId;
                lastOrbit = orbit;
            }
        }

        private void Transfer(double requestedLf, double requestedOx)
        {
            if (vessel == null) return;
            Observe("bridge before");
            try
            {
                // Reverse requests cannot exceed the ledger's actual balance.
                var lf = Phase1ApiSurface.RequestStockFuel(part, "LiquidFuel", requestedLf);
                var ox = Phase1ApiSurface.RequestStockFuel(part, "Oxidizer", requestedOx);
                ledgerLiquidFuel += lf.LedgerDelta;
                ledgerOxidizer += ox.LedgerDelta;
                part.UpdateMass();
                Log("actual stock delta LF=" + F(lf.StockDelta) + " OX=" + F(ox.StockDelta) +
                    " ledger delta LF=" + F(lf.LedgerDelta) + " OX=" + F(ox.LedgerDelta));
            }
            catch (Exception error)
            {
                Log("bridge FAILED " + error);
            }
            Observe("bridge after");
        }

        private void Observe(string label)
        {
            if (part == null || vessel == null) return;
            string tanks = string.Join("; ", vessel.parts.OrderBy(p => p.flightID).Select(p =>
                p.flightID + ":" + p.partInfo.name + "[" +
                string.Join(",", p.Resources.Cast<PartResource>()
                    .Where(r => r.resourceName == "LiquidFuel" || r.resourceName == "Oxidizer")
                    .Select(r => r.resourceName + "=" + F(r.amount) + "/" + F(r.maxAmount))) + "]"));
            Log(label + " part=" + part.flightID + " vessel=" + vessel.id.ToString("D") +
                " persistent=" + vessel.persistentId + " orbit=" + OrbitText() +
                " loaded=" + vessel.loaded + " packed=" + vessel.packed +
                " stockMass+t=" + F(vessel.GetTotalMass()) +
                " ledgerLF=" + F(ledgerLiquidFuel) + " ledgerOX=" + F(ledgerOxidizer) +
                " cargoMass+t=" + F(extraCargoMass) + " tanks=" + tanks);
        }

        private string OrbitText() => vessel?.orbit == null ? "none" :
            (vessel.orbit.referenceBody?.bodyName ?? "none") + "/" +
            Math.Round(vessel.orbit.PeA / 100) * 100 + "/" +
            Math.Round(vessel.orbit.eccentricity, 3) + "/" +
            Math.Round(vessel.orbit.inclination, 2);

        private static double Density(string resource) =>
            PartResourceLibrary.Instance?.GetDefinition(resource)?.density ?? 0;

        private static string F(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
        private static void Log(string message) => Debug.Log("[Lodeworks Phase1 FuelDepot] " + message);
    }
}

