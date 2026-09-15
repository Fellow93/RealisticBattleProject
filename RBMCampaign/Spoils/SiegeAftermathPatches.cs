using HarmonyLib;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// The two seams where vanilla's siege aftermath and RBM's sack would otherwise contradict each
    /// other: the prosperity a stormed fief loses, and the gold its takers gain.
    /// </summary>
    public static class SiegeAftermathPatches
    {
        private static bool Active
        {
            get { return SpoilsPool.SackActive; }
        }

        /// <summary>
        /// Replaces vanilla's prosperity penalty with a flat fraction of what the fief had.
        /// </summary>
        /// <remarks>
        /// Vanilla scales the loss by the SIZE OF THE ARMY that took the place -- a logarithm of the
        /// headcount -- which reads as though a fief suffers by how many men walked through it rather
        /// than by what was decided for it. RBM's tiers make the decision the whole of it: mercy costs a
        /// tenth, pillage a fifth, devastation half, whoever took the walls.
        ///
        /// Patched at the behaviour's own private computation rather than at a model, because there is no
        /// prosperity term on <c>SiegeAftermathModel</c> at all -- that model carries only the player's
        /// trait XP. The same method feeds both the applied penalty and the menu tooltips, so the numbers
        /// the player is shown before choosing are the numbers he gets.
        /// </remarks>
        [HarmonyPatch(typeof(SiegeAftermathCampaignBehavior), "GetSiegeAftermathProsperityPenalty")]
        private static class ProsperityPenaltyPatch
        {
            private static bool Prefix(Settlement settlement, SiegeAftermathAction.SiegeAftermath aftermathType, ref float __result)
            {
                if (!Active)
                {
                    return true;
                }
                __result = SpoilsPool.ProsperityPenaltyFor(settlement, aftermathType);
                return false;
            }
        }

        /// <summary>
        /// Stops vanilla minting gold for the victors. Its figure is fifteen denars per point of
        /// prosperity destroyed, conjured from nothing and handed straight to the lords' own purses --
        /// which is exactly the money RBM's sack now takes out of the fief itself and hands to the men
        /// who took it. Left in place the two would pay for the same sack twice, once out of the
        /// settlement and once out of thin air.
        /// </summary>
        /// <remarks>
        /// Zero is safe for every reader: the applied leg divides the total by contribution percentage
        /// (a multiply and a divide by the constant 100), and the summary and tooltip lines simply print
        /// it. Nothing divides BY the gold.
        /// </remarks>
        [HarmonyPatch(typeof(SiegeAftermathCampaignBehavior), "GetSiegeAftermathArmyGoldGain")]
        private static class ArmyGoldGainPatch
        {
            private static bool Prefix(ref int __result)
            {
                if (!Active)
                {
                    return true;
                }
                __result = 0;
                return false;
            }
        }
    }
}
