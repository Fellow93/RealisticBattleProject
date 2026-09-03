using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// The sea-patrol half of the wealth-funded patrol rework. The naval patrol types live in the NavalDLC
    /// assembly, which the mod does not (and cannot) reference at compile time -- so unlike the land patches
    /// (attribute-discovered by <c>PatchAll</c>) these are applied BY HAND through reflection from
    /// <see cref="RBMCampaignPatcher"/>, guarded on the types actually being present. When the DLC is absent the
    /// whole module is a silent no-op.
    ///
    /// Two things make the naval side small. First, a sea patrol is an <c>IsPatrolParty</c> mobile party like a
    /// land one, so its daily wage-and-maintenance already flows through the same PayPatrolUpkeep branch of the
    /// wage pass, and it sizes through the same shared <c>DefaultPartySizeLimitModel</c> patch -- no naval-specific
    /// upkeep or sizing code is needed here. Second, the Coastal Guard Edict is detected by its policy string id
    /// (see <see cref="PatrolUpkeep.HasCoastalEdict"/>), so making it a budget bonus needs no DLC reference. Only
    /// the eligibility and spawn OVERRIDES -- which live on NavalDLC types -- need reflection, and they are all
    /// this file adds.
    /// </summary>
    public static class PatrolUpkeepNaval
    {
        private const string NavalModelType = "NavalDLC.GameComponents.NavalSettlementPatrolModel";
        private const string NavalBehaviorType = "TaleWorlds.CampaignSystem.CampaignBehaviors.NavalPatrolPartiesCampaignBehavior";

        /// <summary>
        /// Applies the naval patches if the NavalDLC types are loaded. Called from the campaign patcher on every
        /// patch pass; the harness's UnpatchAll clears the prior copy first, so re-applying never duplicates.
        /// Any failure -- a moved type, a changed signature after a game update -- is caught and logged rather
        /// than allowed to bring the whole module's patching down, since the land half must still stand.
        /// </summary>
        public static void ApplyNaval(Harmony harmony)
        {
            try
            {
                Type navalModel = AccessTools.TypeByName(NavalModelType);
                Type navalBehavior = AccessTools.TypeByName(NavalBehaviorType);
                if (navalModel == null || navalBehavior == null)
                {
                    // No War Sails / NavalDLC in this install -- nothing to patch.
                    return;
                }

                MethodInfo canHave = AccessTools.Method(navalModel, "CanSettlementHavePatrolParties");
                if (canHave != null)
                {
                    harmony.Patch(canHave, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(PatrolUpkeepNaval), nameof(CanHavePostfix))));
                }

                MethodInfo spawnGate = AccessTools.Method(navalBehavior, "CanSettlementSpawnNewPartyCurrently");
                if (spawnGate != null)
                {
                    harmony.Patch(spawnGate, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(PatrolUpkeepNaval), nameof(SpawnGatePostfix))));
                }

                MethodInfo spawn = AccessTools.Method(navalBehavior, "SpawnPatrolParty");
                if (spawn != null)
                {
                    harmony.Patch(spawn, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(PatrolUpkeepNaval), nameof(SpawnChargePostfix))));
                }
            }
            catch (Exception e)
            {
                if (SpoilsLog.IsEnabled)
                {
                    SpoilsLog.Log("PATROL", "naval patrol patches skipped: " + e.Message);
                }
            }
        }

        /// <summary>
        /// Naval eligibility becomes wealth-based: a coastal town fields a sea patrol when it can fund one, edict
        /// or no edict. The Coastal Guard Edict is no longer the gate -- it only raises the budget.
        /// </summary>
        private static void CanHavePostfix(Settlement settlement, bool naval, ref bool __result)
        {
            if (!PatrolUpkeep.IsEnabled || !naval)
            {
                return;
            }
            __result = PatrolUpkeep.CanFundNavalPatrol(settlement);
        }

        /// <summary>Naval spawn gate: on top of eligibility, require the pot can arm the crew with a reserve to spare.</summary>
        private static void SpawnGatePostfix(Settlement settlement, bool includeReason, ref TextObject reason, ref bool __result)
        {
            if (!PatrolUpkeep.IsEnabled || !__result)
            {
                return;
            }
            if (!PatrolUpkeep.CanAffordSpawn(settlement, naval: true))
            {
                __result = false;
                reason = includeReason ? new TextObject("{=RBM_patrol_unfunded}Cannot fund a patrol") : null;
            }
        }

        /// <summary>
        /// Charges a freshly-launched sea patrol's kit to the town that raised it. A naval patrol is not stored
        /// on <c>Settlement.PatrolParty</c> (that slot is land-only), so it is found by its home settlement among
        /// the active patrol parties. The cavalry remount is skipped for it -- ships, not horses.
        /// </summary>
        private static void SpawnChargePostfix(Settlement settlement)
        {
            if (!PatrolUpkeep.IsEnabled || settlement == null)
            {
                return;
            }
            MobileParty navalPatrol = null;
            foreach (MobileParty party in MobileParty.All)
            {
                if (party != null && party.IsActive && party.IsPatrolParty
                    && party.HomeSettlement == settlement
                    && party.PatrolPartyComponent != null && party.PatrolPartyComponent.IsNaval)
                {
                    navalPatrol = party;
                    break;
                }
            }
            if (navalPatrol != null)
            {
                PatrolUpkeep.OnPatrolSpawned(settlement, navalPatrol);
            }
        }
    }
}
