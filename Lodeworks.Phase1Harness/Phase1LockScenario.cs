using System;
using System.Collections.Generic;
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

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            rewards.Clear();
            if (bool.TryParse(node.GetValue("probeRewarded"), out bool rewarded) && rewarded)
                rewards.Add(ProbeName);
            nextReconcile = 0;
            warnedUnavailable = false;
            Log("Loaded current save; reward=" + rewarded);
        }

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
        }

        private void Reconcile()
        {
            bool sandbox = HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX;
            try
            {
                lastReconcileSucceeded = adapter.Reconcile(rewards, sandbox);
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
            GUILayout.Label("Save through KSP, then reload to test persistence.");
            GUI.DragWindow();
        }

        private static string Snapshot()
        {
            if (ResearchAndDevelopment.Instance == null || PartLoader.Instance == null)
                return "Stock state: R&D or PartLoader unavailable";
            AvailablePart part = PartLoader.getPartInfoByName(ProbeName);
            if (part == null)
                return "Stock state: diagnostic PART missing";
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

