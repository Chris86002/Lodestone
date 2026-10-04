using Contracts;
using UnityEngine;

namespace Lodeworks.Phase1Harness
{
    // Generated only by the opt-in scenario button. This is a stock contract
    // lifecycle probe, not a campaign contract or a Phase 15 implementation.
    public sealed class Phase1DiagnosticContract : Contract
    {
        internal static bool AllowGeneration;
        // Generation is opt-in, but an offered contract must remain valid after the
        // one-shot generation switch is reset so stock can present and reload it.
        public override bool MeetRequirements() => true;
        protected override bool Generate()
        {
            if (!AllowGeneration) return false;
            AddParameter(new Phase1DiagnosticParameter(), null);
            SetExpiry(10, 10);
            SetDeadlineDays(10f, null);
            SetFunds(100, 100, null);
            SetScience(1, null);
            SetReputation(1, null);
            Log("Generate state=" + ContractState);
            return true;
        }

        protected override string GetTitle() => "Lodeworks Phase 1 contract diagnostic";
        protected override string GetDescription() =>
            "Opt-in test of stock offer, acceptance, reload, completion, and reward order.";
        protected override string GetSynopsys() => "Accept, save/reload, then finish with the diagnostic panel.";
        protected override string GetNotes() => "Diagnostic contract; do not use for a normal career.";

        protected override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            Log("OnLoad state=" + ContractState + " funds=" + Funds());
        }

        protected override void OnSave(ConfigNode node)
        {
            Log("OnSave state=" + ContractState + " funds=" + Funds());
            base.OnSave(node);
        }

        protected override void OnOffered()
        {
            base.OnOffered();
            Log("OnOffered state=" + ContractState + " funds=" + Funds());
        }

        protected override void OnAccepted()
        {
            base.OnAccepted();
            Log("OnAccepted state=" + ContractState + " funds=" + Funds());
        }

        protected override void AwardCompletion()
        {
            Log("AwardCompletion before funds=" + Funds());
            base.AwardCompletion();
            Log("AwardCompletion after funds=" + Funds());
        }

        protected override void OnCompleted()
        {
            Log("OnCompleted before base funds=" + Funds());
            base.OnCompleted();
            Log("OnCompleted after base funds=" + Funds());
        }

        private static string Funds() => Funding.Instance?.Funds.ToString("G17") ?? "unavailable";
        private static void Log(string message) =>
            Debug.Log("[Lodeworks Phase1 Contract] " + message);
    }

    public sealed class Phase1DiagnosticParameter : ContractParameter
    {
        public void CompleteFromDiagnosticPanel() => SetComplete();
        protected override string GetTitle() => "Complete from the Lodeworks Phase 1 panel";
        protected override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            Debug.Log("[Lodeworks Phase1 Contract] parameter OnLoad state=" + State);
        }
        protected override void OnSave(ConfigNode node)
        {
            Debug.Log("[Lodeworks Phase1 Contract] parameter OnSave state=" + State);
            base.OnSave(node);
        }
    }
}

