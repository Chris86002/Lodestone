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

        [KSPEvent(guiActive = true, guiName = "Probe: other tank to ledger")]
        public void OtherTankToLedger() => TransferOnOtherTank(50, 60);

        [KSPEvent(guiActive = true, guiName = "Probe: ledger to other tank")]
        public void LedgerToOtherTank() => TransferOnOtherTank(-ledgerLiquidFuel, -ledgerOxidizer);

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

        [KSPEvent(guiActive = true, guiName = "Probe: check orbit clearance")]
        public void CheckOrbitClearance()
        {
            Observe("clearance observation");
            var orbit = vessel?.orbit;
            var body = orbit?.referenceBody;
            if (orbit == null || body == null || body.bodyName == "Sun" || body.pqsController == null ||
                double.IsNaN(body.pqsController.radiusMax) ||
                double.IsInfinity(body.pqsController.radiusMax) ||
                body.pqsController.radiusMax < body.Radius)
            {
                Log("diagnostic clearance=REFUSE terrain/reference cannot be verified");
                return;
            }
            double terrainHeight = body.pqsController.radiusMax - body.Radius;
            double atmosphereHeight = body.atmosphere ? body.atmosphereDepth : 0;
            double minimumPe = Math.Max(terrainHeight, atmosphereHeight) + 10000;
            bool bound = orbit.eccentricity >= 0 && orbit.eccentricity < 1;
            bool safe = bound && orbit.eccentricity <= 0.05 && orbit.inclination <= 10 &&
                orbit.PeA >= minimumPe;
            Log("diagnostic clearance=" + (safe ? "SAFE" : "REFUSE") +
                " body=" + body.bodyName + " peM=" + F(orbit.PeA) +
                " minimumPeM=" + F(minimumPe) +
                " atmosphereM=" + F(atmosphereHeight) +
                " maxTerrainM=" + F(terrainHeight) +
                " ecc=" + F(orbit.eccentricity) + " incDeg=" + F(orbit.inclination));
        }

        [KSPEvent(guiActive = true, guiName = "Probe: dock nearest test port")]
        public void DockNearestTestPort()
        {
            if (part == null || vessel == null) return;
            var own = part.Modules.OfType<ModuleDockingNode>().FirstOrDefault();
            if (own == null) { Log("dock unavailable: no local docking node"); return; }
            var other = FlightGlobals.Vessels
                .Where(v => v != vessel && v.loaded && v.parts != null)
                .SelectMany(v => v.parts.SelectMany(p => p.Modules.OfType<ModuleDockingNode>()
                    .Select(node => new { Vessel = v, Node = node })))
                .OrderBy(x => (vessel.GetWorldPos3D() - x.Vessel.GetWorldPos3D()).magnitude)
                .FirstOrDefault();
            if (other == null) { Log("dock unavailable: no loaded remote docking node"); return; }
            double distance = (vessel.GetWorldPos3D() - other.Vessel.GetWorldPos3D()).magnitude;
            if (distance > 200) { Log("dock unavailable: nearest port distance=" + F(distance) + "m"); return; }
            Observe("before diagnostic dock");
            Log("diagnostic DockToVessel target=" + other.Vessel.id.ToString("D") +
                " persistent=" + other.Vessel.persistentId + " distance=" + F(distance) + "m");
            try { own.DockToVessel(other.Node); }
            catch (Exception error) { Log("diagnostic DockToVessel FAILED " + error); }
        }

        [KSPEvent(guiActive = true, guiName = "Probe: undock test port")]
        public void UndockTestPort()
        {
            if (part == null || vessel == null) return;
            var own = part.Modules.OfType<ModuleDockingNode>().FirstOrDefault();
            if (own == null) { Log("undock unavailable: no local docking node"); return; }
            Observe("before diagnostic undock");
            try { own.Undock(); }
            catch (Exception error) { Log("diagnostic Undock FAILED " + error); }
        }

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
            => TransferOnPart(part, requestedLf, requestedOx, "selected tank");

        private void TransferOnOtherTank(double requestedLf, double requestedOx)
        {
            var other = vessel?.parts?.Where(p => p != part &&
                    p.Resources.Get("LiquidFuel") != null &&
                    p.Resources.Get("Oxidizer") != null)
                .OrderBy(p => p.flightID).FirstOrDefault();
            if (other == null) { Log("other tank unavailable on connected vessel"); return; }
            TransferOnPart(other, requestedLf, requestedOx, "other tank");
        }

        private void TransferOnPart(Part selectedTank, double requestedLf,
            double requestedOx, string selection)
        {
            if (vessel == null || selectedTank == null || selectedTank.vessel != vessel) return;
            Log("bridge selected=" + selection + " part=" + selectedTank.flightID);
            Observe("bridge before " + selection);
            try
            {
                // Reverse requests cannot exceed the ledger's actual balance.
                var lf = Phase1ApiSurface.RequestStockFuel(selectedTank, "LiquidFuel", requestedLf);
                var ox = Phase1ApiSurface.RequestStockFuel(selectedTank, "Oxidizer", requestedOx);
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
            Observe("bridge after " + selection);
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

