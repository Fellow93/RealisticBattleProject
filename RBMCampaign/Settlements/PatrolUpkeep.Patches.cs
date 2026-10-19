using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// The Harmony half of the settlement-funded patrols (<see cref="PatrolUpkeep"/>): steers vanilla's
    /// <c>PatrolPartiesCampaignBehavior</c> and the patrol and party-size models instead of replacing them, so
    /// the patrol AI, dialogs, UI and save type stay vanilla's. Every patch changes nothing unless
    /// <see cref="PatrolUpkeep.IsEnabled"/>, and every one leaves naval patrols (War Sails) to their own rules --
    /// except their daily pay, billed from the wage pass (see <see cref="SpoilsPool"/>).
    ///
    /// NavalDLC's <c>NavalSettlementPatrolModel</c> and <c>NavalDLCPartySizeLimitModel</c> pass every land query
    /// down to the default models, so patching those covers both setups.
    /// </summary>
    public static class PatrolUpkeepPatches
    {
        private static readonly TextObject UnfundedText = new TextObject("{=RBM_patrol_unfunded}Cannot fund a patrol");
        private static readonly TextObject SavingText = new TextObject("{=RBM_patrol_saving}Saving to raise a patrol");
        private static readonly TextObject NoMenText = new TextObject("{=RBM_patrol_nomen}Too few men to raise a patrol");

        /// <summary>
        /// The keep-gate. A town or castle may have a land patrol while its funds can keep one, Guard House or
        /// not; vanilla reads this to spawn one and, through <c>UpdateSettlementParties</c>, to stand an existing
        /// one down the day it turns false.
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
                __result = PatrolUpkeep.CanKeepPatrol(settlement);
            }
        }

        /// <summary>
        /// Quality: RBM's <c>patrol_party_&lt;culture&gt;_template_level_N</c> for the fief's boost, in place of
        /// vanilla's Guard-House pick -- which returns nothing for a town without one or a castle.
        /// </summary>
        [HarmonyPatch(typeof(DefaultSettlementPatrolModel), "GetPartyTemplateForPatrolParty")]
        private static class TemplatePatch
        {
            private static void Postfix(Settlement settlement, bool naval, ref PartyTemplateObject __result)
            {
                if (!PatrolUpkeep.IsEnabled || naval || !PatrolUpkeep.IsEligible(settlement))
                {
                    return;
                }
                PartyTemplateObject picked = PatrolUpkeep.PickTemplate(settlement, __result);
                if (picked != null)
                {
                    __result = picked;
                }
            }
        }

        /// <summary>
        /// Training time. Vanilla reads the Guard House level and throws on a fief without one, which now includes
        /// every castle and any town that never built it: those take <see cref="PatrolUpkeep.NoGuardHouseSpawnDays"/>.
        /// A Guard House keeps vanilla's 10 days less two per level above the first.
        /// </summary>
        [HarmonyPatch(typeof(DefaultSettlementPatrolModel), "GetPatrolPartySpawnDuration")]
        private static class SpawnDurationPatch
        {
            private static bool Prefix(Settlement settlement, bool naval, ref CampaignTime __result)
            {
                if (!PatrolUpkeep.IsEnabled || naval || settlement == null || PatrolUpkeep.GuardHouseLevel(settlement) > 0)
                {
                    return true;
                }
                __result = CampaignTime.Days(PatrolUpkeep.NoGuardHouseSpawnDays);
                return false;
            }
        }

        /// <summary>
        /// Size: a land patrol is as large as its fief's funds can keep (see <see cref="PatrolUpkeep.ExplainSize"/>),
        /// with the breakdown on the party-size tooltip. Replaces vanilla's 10 + 5 × Guard House level outright
        /// (vanilla gives 0 to a castle). Naval patrols keep War Sails' template size.
        /// ⚠️ The plain query (no descriptions) runs inside the map's parallel movement tick, so it only reads the
        /// main-thread cache (<see cref="PatrolUpkeep.CachedPartyLimit"/>); only the tooltip computes the breakdown.
        /// </summary>
        [HarmonyPatch(typeof(DefaultPartySizeLimitModel), "CalculatePatrolPartySizeLimit")]
        private static class SizeLimitPatch
        {
            private static bool Prefix(MobileParty mobileParty, bool includeDescriptions, ref ExplainedNumber __result)
            {
                if (!PatrolUpkeep.IsManagedPatrol(mobileParty))
                {
                    return true;
                }
                __result = includeDescriptions
                    ? PatrolUpkeep.ExplainPartySize(mobileParty, true)
                    : new ExplainedNumber(PatrolUpkeep.CachedPartyLimit(mobileParty));
                return false;
            }
        }

        /// <summary>
        /// The spawn-gate, and honest status text. On top of the keep-gate a fief only raises a NEW patrol once it
        /// can man the whole of it (garrison men, then men bought under the garrison's rules) and its pot holds
        /// the bought men's kit several times over. Vanilla names a failed keep-gate "No Guard House", which no
        /// longer says anything; the clan and kingdom fief lists show these reasons instead.
        /// </summary>
        [HarmonyPatch(typeof(PatrolPartiesCampaignBehavior), "CanSettlementSpawnNewPartyCurrently")]
        private static class SpawnGatePatch
        {
            private static void Postfix(Settlement settlement, bool includeReason, ref TextObject reason, ref bool __result)
            {
                if (!PatrolUpkeep.IsEnabled || settlement == null)
                {
                    return;
                }
                if (!__result)
                {
                    // Vanilla's first test is the keep-gate; when that is what failed, say so in RBM's words.
                    if (includeReason && PatrolUpkeep.IsEligible(settlement) && !PatrolUpkeep.CanKeepPatrol(settlement))
                    {
                        reason = UnfundedText;
                    }
                    return;
                }
                if (!PatrolUpkeep.CanRaisePatrol(settlement, out bool shortOfMen))
                {
                    __result = false;
                    reason = includeReason ? (shortOfMen ? NoMenText : SavingText) : null;
                }
            }
        }

        /// <summary>
        /// Speed: a land patrol marches <see cref="PatrolUpkeep.SpeedBonus"/> faster, a labelled line on its speed
        /// tooltip. On the default model's land base speed, which NavalDLC's speed model calls for every party on
        /// land (at sea it computes its own), so this covers both setups.
        /// </summary>
        [HarmonyPatch(typeof(DefaultPartySpeedCalculatingModel), "CalculateBaseSpeed")]
        private static class SpeedPatch
        {
            private static void Postfix(MobileParty mobileParty, ref ExplainedNumber __result)
            {
                PatrolUpkeep.AddSpeedBonus(mobileParty, ref __result);
            }
        }

        /// <summary>
        /// Manning and raising cost: once vanilla has created the patrol from its template, man it for real at the
        /// size its fief can keep -- garrison men first, the rest bought and paid for under the garrison's rules
        /// (see <see cref="PatrolUpkeep.OnPatrolSpawned"/>).
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
        /// Paid replenishment: replaces vanilla's free clear-and-rebuild of a land patrol at home with
        /// <see cref="PatrolUpkeep.Replenish"/>, which keeps the men it has and mans the missing ones from the
        /// garrison, buying what the garrison cannot spare. Vanilla's
        /// hourly roll still decides when. Naval patrols keep vanilla's.
        /// With the rework off this does nothing: a patrol vanilla would never have raised (a castle's, or a town's
        /// without a Guard House) has no template and vanilla's refill would throw -- the main module's
        /// always-installed <c>RBM.PatrolReplenishGuard</c> skips that call, RBM Campaign on or off.
        /// </summary>
        [HarmonyPatch(typeof(PatrolPartiesCampaignBehavior), "ReplenishParty")]
        private static class ReplenishPatch
        {
            private static bool Prefix(MobileParty party)
            {
                if (!PatrolUpkeep.IsEnabled || party == null || party.PatrolPartyComponent == null
                    || party.PatrolPartyComponent.IsNaval)
                {
                    return true;
                }
                PatrolUpkeep.Replenish(party);
                return false;
            }
        }

        /// <summary>
        /// A land patrol reaching home ends a stalled refill, so vanilla's hourly refill roll gets to try again while
        /// it rests there, and sends any men over its fief's size back to the garrison (see
        /// <see cref="PatrolUpkeep.OnPatrolEnteredHome"/>).
        /// </summary>
        [HarmonyPatch(typeof(PatrolPartiesCampaignBehavior), "SettlementEntered")]
        private static class EnteredHomePatch
        {
            private static void Postfix(MobileParty party, Settlement settlement)
            {
                if (PatrolUpkeep.IsEnabled && party != null && party.IsPatrolParty && settlement != null
                    && settlement == party.HomeSettlement)
                {
                    PatrolUpkeep.OnPatrolEnteredHome(party);
                }
            }
        }

        /// <summary>
        /// No patrols conjured at campaign start. Vanilla raises one for every qualifying fief at new-game index 50,
        /// for free and before RBM has seeded the treasuries and citizen wealth -- reading a fief's purse that early
        /// would seed it off pre-campaign figures for good. Patrols are raised through the daily queue instead, paid
        /// for, a training period after the campaign opens.
        /// </summary>
        [HarmonyPatch(typeof(PatrolPartiesCampaignBehavior), "OnNewGameCreated")]
        private static class NewGamePatch
        {
            private static bool Prefix()
            {
                return !PatrolUpkeep.IsEnabled;
            }
        }

        /// <summary>
        /// Just before vanilla destroys a fief's land patrol (its keep-gate failed, or the fief changed hands), its
        /// men go back into the garrison unless the fief was taken by force (see
        /// <see cref="PatrolUpkeep.OnStandingDown"/>), and the stand-down is logged.
        /// </summary>
        [HarmonyPatch(typeof(PatrolPartiesCampaignBehavior), "RemoveSettlementParties")]
        private static class DisbandPatch
        {
            private static void Prefix(Settlement settlement)
            {
                if (PatrolUpkeep.IsEnabled)
                {
                    PatrolUpkeep.OnStandingDown(settlement);
                }
            }
        }

        /// <summary>
        /// Tells <see cref="PatrolUpkeep.OnStandingDown"/> how a fief is changing hands while vanilla's owner-change
        /// listener stands its patrol down (vanilla removes it whatever the reason). The finalizer clears the mark
        /// even if the listener throws.
        /// </summary>
        [HarmonyPatch(typeof(PatrolPartiesCampaignBehavior), "OnSettlementOwnerChangedEvent")]
        private static class OwnerChangedPatch
        {
            private static void Prefix(ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
            {
                PatrolUpkeep.BeginOwnerChange(detail);
            }

            private static void Finalizer()
            {
                PatrolUpkeep.EndOwnerChange();
            }
        }

        /// <summary>
        /// Security: vanilla only counts a patrol toward a fief's security with a town Guard House, so a castle's or
        /// a Guard-House-less town's patrol gets vanilla's level-0 figure (see <see cref="PatrolUpkeep.AddSecurityBonus"/>).
        /// NavalDLC's security model starts from the default one, so this covers both setups.
        /// </summary>
        [HarmonyPatch(typeof(DefaultSettlementSecurityModel), "CalculateSecurityChange")]
        private static class SecurityPatch
        {
            private static void Postfix(Town town, ref ExplainedNumber __result)
            {
                PatrolUpkeep.AddSecurityBonus(town, ref __result);
            }
        }

        /// <summary>Logs a patrol lost in the field, and drops its pay tally.</summary>
        [HarmonyPatch(typeof(PatrolPartiesCampaignBehavior), "MobilePartyDestroyed")]
        private static class DestroyedLogPatch
        {
            private static void Postfix(MobileParty party, PartyBase destroyerParty)
            {
                if (PatrolUpkeep.IsEnabled && party != null && party.IsPatrolParty)
                {
                    PatrolUpkeep.OnPatrolDestroyed(party, destroyerParty);
                }
            }
        }
    }
}
