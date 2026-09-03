using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// The land-patrol half of the wealth-funded patrol rework: Harmony patches that turn vanilla's free,
    /// Guard-House-gated, town-only settlement patrols into a company a town or castle FUNDS out of its wealth.
    /// Each patch no-ops (leaving vanilla behaviour) unless the campaign module and the patrol toggle are on.
    /// The naval half lives in PatrolUpkeep.Naval.cs, applied by hand because its types are in the NavalDLC
    /// assembly the mod cannot reference. The daily upkeep itself is billed from the wage pass (see the patrol
    /// branch in <see cref="SpoilsPool"/>'s DepositWageSpoils), not here.
    /// </summary>
    public static class PatrolUpkeepPatches
    {
        /// <summary>
        /// Eligibility: a settlement may have a patrol if it can FUND one, not if it has a Guard House. Wealth is
        /// the gate; towns and castles both qualify. The native behaviour reads this both to spawn a patrol and,
        /// through <c>UpdateSettlementParties</c>, to remove one -- so a fief whose pot can no longer sustain a
        /// patrol stands it down of itself.
        /// </summary>
        [HarmonyPatch(typeof(DefaultSettlementPatrolModel), "CanSettlementHavePatrolParties")]
        private static class CanHavePatrolPatch
        {
            private static void Postfix(Settlement settlement, bool naval, ref bool __result)
            {
                if (!PatrolUpkeep.IsEnabled || naval)
                {
                    return;
                }
                __result = PatrolUpkeep.CanFundPatrol(settlement);
            }
        }

        /// <summary>
        /// Template: pick the patrol's company by what the budget can sustain, and hand a castle (which vanilla
        /// gives none) a real template so it can muster at all. Weak/Moderate/Strong scale with the budget, so a
        /// richer fief -- or one with a higher Guard House -- fields a better patrol.
        /// </summary>
        [HarmonyPatch(typeof(DefaultSettlementPatrolModel), "GetPartyTemplateForPatrolParty")]
        private static class TemplatePatch
        {
            private static void Postfix(Settlement settlement, bool naval, ref PartyTemplateObject __result)
            {
                if (!PatrolUpkeep.IsEnabled || naval || !PatrolUpkeep.CanFundPatrol(settlement))
                {
                    return;
                }
                PartyTemplateObject picked = PickTemplate(settlement);
                if (picked != null)
                {
                    __result = picked;
                }
            }

            private static PartyTemplateObject PickTemplate(Settlement settlement)
            {
                CultureObject culture = (settlement.OwnerClan != null) ? settlement.OwnerClan.Culture : settlement.Culture;
                if (culture == null)
                {
                    return null;
                }
                PartyTemplateObject weak = culture.SettlementPatrolPartyTemplateWeak;
                PartyTemplateObject moderate = culture.SettlementPatrolPartyTemplateModerate ?? weak;
                PartyTemplateObject strong = culture.SettlementPatrolPartyTemplateStrong ?? moderate;
                int size = PatrolUpkeep.PatrolSizeLimit(settlement);
                if (size < 12)
                {
                    return weak;
                }
                return (size < 22) ? moderate : strong;
            }
        }

        /// <summary>
        /// Size: a patrol is as large as its settlement's budget can keep, not a flat function of the Guard
        /// House level -- and a castle gets a real limit rather than vanilla's zero. Replaces the native number
        /// wholesale (a <c>Prefix</c> returning false), since vanilla builds a bare <see cref="ExplainedNumber"/>
        /// with no base to add onto and returns 0 for anything without a Guard House.
        /// </summary>
        [HarmonyPatch(typeof(DefaultPartySizeLimitModel), "CalculatePatrolPartySizeLimit")]
        private static class SizeLimitPatch
        {
            private static bool Prefix(MobileParty mobileParty, bool includeDescriptions, ref ExplainedNumber __result)
            {
                if (!PatrolUpkeep.IsEnabled || mobileParty == null || mobileParty.HomeSettlement == null)
                {
                    return true;
                }
                bool naval = mobileParty.PatrolPartyComponent != null && mobileParty.PatrolPartyComponent.IsNaval;
                __result = new ExplainedNumber(PatrolUpkeep.PatrolSizeLimit(mobileParty.HomeSettlement, naval), includeDescriptions);
                return false;
            }
        }

        /// <summary>
        /// Spawn gate: on top of eligibility, a settlement only raises a NEW patrol when its pot can arm one with
        /// a reserve to spare. Keeps a fief from mustering a company it cannot then keep.
        /// </summary>
        [HarmonyPatch(typeof(PatrolPartiesCampaignBehavior), "CanSettlementSpawnNewPartyCurrently")]
        private static class SpawnGatePatch
        {
            private static void Postfix(Settlement settlement, bool includeReason, ref TextObject reason, ref bool __result)
            {
                if (!PatrolUpkeep.IsEnabled || !__result)
                {
                    return;
                }
                if (!PatrolUpkeep.CanAffordSpawn(settlement))
                {
                    __result = false;
                    reason = includeReason ? new TextObject("{=RBM_patrol_unfunded}Cannot fund a patrol") : null;
                }
            }
        }

        /// <summary>
        /// Spawn charge &amp; cavalry: once the native party exists, bill its kit to the settlement that raised it
        /// and remount a share of its foot into cavalry to run down bandits.
        /// </summary>
        [HarmonyPatch(typeof(PatrolPartiesCampaignBehavior), "SpawnPatrolParty")]
        private static class SpawnChargePatch
        {
            private static void Postfix(Settlement settlement)
            {
                if (!PatrolUpkeep.IsEnabled || settlement == null || settlement.PatrolParty == null)
                {
                    return;
                }
                PatrolUpkeep.OnPatrolSpawned(settlement, settlement.PatrolParty.MobileParty);
            }
        }

        /// <summary>
        /// Cavalry, kept: the native replenishment rebuilds a patrol's roster from the plain template when it
        /// tops up at home, wiping the remount. Re-apply it so a patrol stays mounted for the life of the
        /// company, not just the day it musters. Naval crews are skipped inside <see cref="PatrolUpkeep.ReapplyCavalryBias"/>.
        /// </summary>
        [HarmonyPatch(typeof(PatrolPartiesCampaignBehavior), "ReplenishParty")]
        private static class ReplenishPatch
        {
            private static void Postfix(MobileParty party)
            {
                if (!PatrolUpkeep.IsEnabled)
                {
                    return;
                }
                PatrolUpkeep.ReapplyCavalryBias(party);
            }
        }
    }
}
