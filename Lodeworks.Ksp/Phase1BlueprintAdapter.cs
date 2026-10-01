using System;
using System.Collections.Generic;
using KSP.UI.Screens;
using Lodeworks.Sim;

namespace Lodeworks.Ksp
{
    /// <summary>
    /// Stock API feasibility adapter for campaign-only Lodeworks parts. Reward
    /// parts must be configured with a private, absent techRequired ID so that
    /// stock R&D has no node at which to buy them. The separate stock prerequisite
    /// is checked here before granting the part as experimental.
    /// </summary>
    internal sealed class Phase1BlueprintAdapter
    {
        internal const string LockedTechId = "lodeworksBlueprintLocked";

        private readonly Dictionary<string, string> stockPrerequisites;
        private readonly HashSet<string> controlledParts;

        internal Phase1BlueprintAdapter(IDictionary<string, string> stockPrerequisites)
        {
            if (stockPrerequisites == null) throw new ArgumentNullException(nameof(stockPrerequisites));
            this.stockPrerequisites = new Dictionary<string, string>(StringComparer.Ordinal);
            controlledParts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in stockPrerequisites)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    throw new ArgumentException("A reward part needs an internal name.", nameof(stockPrerequisites));
                this.stockPrerequisites.Add(pair.Key, pair.Value ?? string.Empty);
                controlledParts.Add(pair.Key);
            }
        }

        /// <summary>
        /// Reconcile after R&D has loaded the current save, on a reward, and after
        /// stock research changes. The scenario supplies its saved entitlements;
        /// this adapter never treats process-global memory as campaign authority.
        /// Returns false for a missing/misconfigured part or unavailable R&D.
        /// </summary>
        internal bool Reconcile(ISet<string> rewardedParts, bool sandbox)
        {
            if (rewardedParts == null) throw new ArgumentNullException(nameof(rewardedParts));
            if (ResearchAndDevelopment.Instance == null || PartLoader.Instance == null)
                return false;
            // A tech-tree mod must not turn this sentinel into a purchasable node.
            if (ResearchAndDevelopment.Instance.GetTechState(LockedTechId) != null)
                return false;

            foreach (string name in controlledParts)
            {
                AvailablePart part = PartLoader.getPartInfoByName(name);
                if (part == null || part.TechRequired != LockedTechId)
                    return false;
            }

            bool changed = false;
            foreach (string name in controlledParts)
            {
                AvailablePart part = PartLoader.getPartInfoByName(name);
                string stockTech = stockPrerequisites[name];
                bool techReady = stockTech.Length == 0 ||
                    ResearchAndDevelopment.GetTechnologyState(stockTech) == RDTech.State.Available;
                bool shouldGrant = BlueprintGate.IsAvailable(name, techReady, sandbox, rewardedParts);
                bool isGranted = ResearchAndDevelopment.IsExperimentalPart(part);
                bool isAvailable = ResearchAndDevelopment.PartTechAvailable(part) &&
                    ResearchAndDevelopment.PartModelPurchased(part);
                if (shouldGrant && !isAvailable)
                {
                    ResearchAndDevelopment.AddExperimentalPart(part);
                    changed = true;
                }
                else if (!shouldGrant && isGranted)
                {
                    ResearchAndDevelopment.RemoveExperimentalPart(part);
                    changed = true;
                }
            }

            if (changed && EditorPartList.Instance != null)
                EditorPartList.Instance.Refresh();
            return true;
        }
    }
}
