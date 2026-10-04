using System;
using System.Collections.Generic;
using System.Linq;
using Contracts;
using Lodeworks.Ksp;
using UnityEngine;

namespace Lodeworks.Phase1Harness
{
    // This scenario exists only in the opt-in diagnostic package. The saved
    // reward bit belongs to the current KSP game, never to a process singleton.
    [KSPScenario(ScenarioCreationOptions.AddToAllGames,
        GameScenes.SPACECENTER, GameScenes.EDITOR, GameScenes.FLIGHT,
        GameScenes.TRACKSTATION)]
    public sealed class Phase1LockScenario : ScenarioModule
    {
        private const string ProbeName = "lwPhase1LockProbe";
        private const string Prerequisite = "basicScience";
        private readonly HashSet<string> rewards = new HashSet<string>(StringComparer.Ordinal);
        private readonly Phase1BlueprintAdapter adapter = new Phase1BlueprintAdapter(
            new Dictionary<string, string> { { ProbeName, Prerequisite } });

        private Rect window = new Rect(30, 100, 405, 215);
        private float nextReconcile;
        private bool lastReconcileSucceeded;
        private bool warnedUnavailable;
        private bool contractEventsRegistered;
        private float nextVesselObservation;

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            RegisterContractEvents();
            rewards.Clear();
            if (bool.TryParse(node.GetValue("probeRewarded"), out bool rewarded) && rewarded)
                rewards.Add(ProbeName);
            nextReconcile = 0;
            warnedUnavailable = false;
            Log("Loaded current save; reward=" + rewarded);
        }

        private void OnDestroy()
        {
            if (!contractEventsRegistered) return;
            GameEvents.Contract.onOffered.Remove(OnDiagnosticOffered);
            GameEvents.Contract.onAccepted.Remove(OnDiagnosticAccepted);
            GameEvents.Contract.onCompleted.Remove(OnDiagnosticCompleted);
            contractEventsRegistered = false;
        }

        private void RegisterContractEvents()
        {
            if (contractEventsRegistered) return;
            GameEvents.Contract.onOffered.Add(OnDiagnosticOffered);
            GameEvents.Contract.onAccepted.Add(OnDiagnosticAccepted);
            GameEvents.Contract.onCompleted.Add(OnDiagnosticCompleted);
            contractEventsRegistered = true;
        }

        private void OnDiagnosticOffered(Contract contract)
        {
            if (contract is Phase1DiagnosticContract)
                Log("Stock onOffered state=" + contract.ContractState + " funds=" + Funds());
        }

        private void OnDiagnosticAccepted(Contract contract)
        {
            if (contract is Phase1DiagnosticContract)
                Log("Stock onAccepted state=" + contract.ContractState + " funds=" + Funds());
        }

        private void OnDiagnosticCompleted(Contract contract)
        {
            if (!(contract is Phase1DiagnosticContract)) return;
            bool firstReward = rewards.Add(ProbeName);
            Reconcile();
            Log("Stock onCompleted; first blueprint reward=" + firstReward +
                " state=" + contract.ContractState + " funds=" + Funds() + " " + Snapshot());
        }

        private static string Funds() => Funding.Instance?.Funds.ToString("G17") ?? "unavailable";

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            node.AddValue("probeRewarded", rewards.Contains(ProbeName));
            Log("Saved current save; reward=" + rewards.Contains(ProbeName));
        }

        private void Update()
        {
            if (HighLogic.CurrentGame == null || Time.realtimeSinceStartup < nextReconcile)
                return;
            nextReconcile = Time.realtimeSinceStartup + 2f;
            Reconcile();
            if (HighLogic.LoadedScene == GameScenes.TRACKSTATION &&
                Time.realtimeSinceStartup >= nextVesselObservation)
            {
                nextVesselObservation = Time.realtimeSinceStartup + 10f;
                foreach (Vessel testVessel in FlightGlobals.Vessels)
                    Log("Tracking vessel=" + testVessel.vesselName +
                        " id=" + testVessel.id.ToString("D") +
                        " persistent=" + testVessel.persistentId +
                        " loaded=" + testVessel.loaded +
                        " packed=" + testVessel.packed +
                        " totalMass+t=" + testVessel.GetTotalMass().ToString("G17"));
            }
        }

        private void Reconcile()
        {
            bool sandbox = HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX;
            try
            {
                // Stock sandbox does not create an R&D singleton. Its normal
                // part catalogue already ignores tech and purchase gates.
                // Verify the diagnostic PART instead of calling career R&D.
                if (sandbox)
                {
                    AvailablePart? probe = PartLoader.Instance == null ? null :
                        PartLoader.getPartInfoByName(ProbeName);
                    lastReconcileSucceeded = probe != null &&
                        probe.TechRequired == Phase1BlueprintAdapter.LockedTechId;
                }
                else
                {
                    lastReconcileSucceeded = adapter.Reconcile(rewards, false);
                }
                if (!lastReconcileSucceeded && !warnedUnavailable)
                {
                    warnedUnavailable = true;
                    Log("Reconciliation unavailable. Check R&D, the probe PART, and the sentinel tech ID.");
                }
                if (lastReconcileSucceeded)
                    warnedUnavailable = false;
            }
            catch (Exception error)
            {
                lastReconcileSucceeded = false;
                if (!warnedUnavailable)
                {
                    warnedUnavailable = true;
                    Debug.LogError("[Lodeworks Phase1 Harness] Reconcile failed: " + error);
                }
            }
        }

        private void OnGUI()
        {
            if (HighLogic.CurrentGame == null ||
                (HighLogic.LoadedScene != GameScenes.SPACECENTER &&
                 HighLogic.LoadedScene != GameScenes.EDITOR))
                return;
            window = GUILayout.Window(194805, window, DrawWindow,
                "Lodeworks Phase 1 lock probe");
        }

        private void DrawWindow(int id)
        {
            Game.Modes mode = HighLogic.CurrentGame.Mode;
            GUILayout.Label("Save: " + HighLogic.SaveFolder + "   Mode: " + mode);
            GUILayout.Label("Saved reward: " + rewards.Contains(ProbeName) +
                "   Reconcile: " + (lastReconcileSucceeded ? "ready" : "waiting / failed"));
            GUILayout.Label(Snapshot());
            if (GUILayout.Button("Grant test reward in this save"))
            {
                rewards.Add(ProbeName);
                Reconcile();
                Log("Grant clicked. " + Snapshot());
            }
            if (GUILayout.Button("Revoke test reward in this save"))
            {
                rewards.Remove(ProbeName);
                Reconcile();
                Log("Revoke clicked. " + Snapshot());
            }
            if (GUILayout.Button("Log current stock state"))
                Log(Snapshot());
            GUILayout.Space(5);
            GUILayout.Label("Contract diagnostic: " + ContractSnapshot());
            if (GUILayout.Button("Offer diagnostic stock contract")) OfferDiagnosticContract();
            if (GUILayout.Button("Finish accepted diagnostic contract")) FinishDiagnosticContract();
            GUILayout.Label("Save through KSP, then reload to test persistence.");
            GUI.DragWindow();
        }

        private static string ContractSnapshot()
        {
            if (ContractSystem.Instance == null) return "stock ContractSystem unavailable";
            var contract = ContractSystem.Instance.Contracts
                .OfType<Phase1DiagnosticContract>().FirstOrDefault();
            return contract == null ? "none; mode=" + HighLogic.CurrentGame.Mode :
                contract.ContractState + "; funds=" + Funds();
        }

        private static void OfferDiagnosticContract()
        {
            if (HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX ||
                ContractSystem.Instance == null)
            {
                Log("Offer unavailable in mode=" + HighLogic.CurrentGame.Mode +
                    "; ContractSystem=" + (ContractSystem.Instance != null));
                return;
            }
            Contract contract;
            Phase1DiagnosticContract.AllowGeneration = true;
            try
            {
                contract = ContractSystem.Instance.GenerateContract(174805,
                    Contract.ContractPrestige.Trivial, typeof(Phase1DiagnosticContract));
            }
            finally
            {
                Phase1DiagnosticContract.AllowGeneration = false;
            }
            if (contract != null && contract.Offer())
                ContractSystem.Instance.Contracts.Add(contract);
            Log("GenerateContract returned=" + (contract?.ContractState.ToString() ?? "null") +
                " funds=" + Funds());
        }

        private static void FinishDiagnosticContract()
        {
            var contract = ContractSystem.Instance?.Contracts
                .OfType<Phase1DiagnosticContract>()
                .FirstOrDefault(c => c.ContractState == Contract.State.Active);
            var parameter = contract?.GetParameter(typeof(Phase1DiagnosticParameter))
                as Phase1DiagnosticParameter;
            if (parameter == null)
            {
                Log("Finish refused: no active diagnostic contract");
                return;
            }
            Log("Before parameter completion state=" + contract!.ContractState +
                " funds=" + Funds());
            parameter.CompleteFromDiagnosticPanel();
            Log("After parameter completion state=" + contract.ContractState +
                " funds=" + Funds());
        }

        private static string Snapshot()
        {
            if (PartLoader.Instance == null)
                return "Stock state: PartLoader unavailable";
            AvailablePart part = PartLoader.getPartInfoByName(ProbeName);
            if (part == null)
                return "Stock state: diagnostic PART missing";
            if (HighLogic.CurrentGame != null &&
                HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX)
                return "Sandbox: part present; stock R&D not used";
            if (ResearchAndDevelopment.Instance == null)
                return "Stock state: career/science R&D unavailable";
            bool science = ResearchAndDevelopment.GetTechnologyState(Prerequisite) ==
                RDTech.State.Available;
            return "Science=" + science + " Experimental=" +
                ResearchAndDevelopment.IsExperimentalPart(part) + " Tech=" +
                ResearchAndDevelopment.PartTechAvailable(part) + " Purchased=" +
                ResearchAndDevelopment.PartModelPurchased(part);
        }

        private static void Log(string message) =>
            Debug.Log("[Lodeworks Phase1 Harness] " + message);
    }
}

