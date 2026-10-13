using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CampaignBehaviors.BarterBehaviors;
using TaleWorlds.CampaignSystem.CampaignBehaviors.CommentBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;

namespace RBMCampaign
{
    /// <summary>
    /// Keeps vanilla's hands off the mercenary companies. Each patch is a no-op for any clan that is not one
    /// (<see cref="MercenaryCompanies.IsCompany(Clan)"/>, which is also false with the feature off).
    /// </summary>
    public static class MercenaryCompanyPatches
    {
        /// <summary>
        /// Vanilla tops a minor faction up to four heroes and gives each a party (daily, per clan). A company
        /// keeps exactly one leader and one party; RBM spawns and respawns them itself.
        /// </summary>
        [HarmonyPatch(typeof(HeroSpawnCampaignBehavior), "TrySpawnHeroesAndParties")]
        private static class NoVanillaHeroOrPartySpawn
        {
            private static bool Prefix(Clan clan)
            {
                return !MercenaryCompanies.IsCompany(clan);
            }
        }

        /// <summary>
        /// The clan's daily diplomacy barters: seeking a mercenary contract, joining a kingdom, war and peace
        /// barters, and a mercenary weighing whether to leave. A company does none of it on its own.
        /// </summary>
        [HarmonyPatch(typeof(DiplomaticBartersBehavior), "DailyTickClan")]
        private static class NoDiplomaticBarters
        {
            private static bool Prefix(Clan clan)
            {
                return !MercenaryCompanies.IsCompany(clan);
            }
        }

        /// <summary>A hired company never proposes kingdom decisions (a mercenary can, once it has 100 influence).</summary>
        [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "DailyTickClan")]
        private static class NoKingdomDecisionProposals
        {
            private static bool Prefix(Clan clan)
            {
                return !MercenaryCompanies.IsCompany(clan);
            }
        }

        /// <summary>
        /// A ruling player cannot hire a company through vanilla's minor-faction dialog (a contract with no
        /// end): companies are hired into an army from the army management screen.
        /// </summary>
        [HarmonyPatch(typeof(LordConversationsCampaignBehavior), "conversation_player_want_to_hire_mercenary_on_condition")]
        private static class NoVanillaHireDialog
        {
            private static void Postfix(ref bool __result)
            {
                if (__result && MercenaryCompanies.IsCompany(Hero.OneToOneConversationHero?.Clan))
                {
                    __result = false;
                }
            }
        }

        /// <summary>
        /// A company is paid in gold, never in influence: no influence to add it to an army (the AI leader is
        /// charged on joining, the player in the army screen's cart) or in the army's cohesion-boost cost.
        /// </summary>
        [HarmonyPatch(typeof(DefaultArmyManagementCalculationModel), "CalculatePartyInfluenceCost")]
        private static class NoInfluenceCost
        {
            private static void Postfix(MobileParty party, ref int __result)
            {
                if (MercenaryCompanies.IsCompanyParty(party))
                {
                    __result = 0;
                }
            }
        }

        /// <summary>
        /// Half pay while idle: a company out of contract pays half its party wage (see MercenaryCompanies,
        /// "Half pay when idle"). Run first among the GetTotalWage postfixes and taken as a flat line off the
        /// wage alone, so RBM's display-only maintenance lines added after it (MaintenancePartyWageLine) are not
        /// halved with it. Skipped while MercenaryCompanies.FullWage prices a contract.
        /// </summary>
        [HarmonyPatch(typeof(DefaultPartyWageModel), "GetTotalWage")]
        private static class IdleHalfPay
        {
            [HarmonyPriority(Priority.First)]
            private static void Postfix(MobileParty mobileParty, ref ExplainedNumber __result)
            {
                if (MercenaryCompanies.ReadingFullWage)
                {
                    return;
                }
                float factor = MercenaryCompanies.WageFactor(mobileParty);
                if (factor >= 1f || !(__result.ResultNumber > 0f))
                {
                    return;
                }
                // ExplainedNumber's result is base x (1 + sum of factors), and Add moves the base, so the flat cut
                // is scaled back by the factors to take exactly (1 - factor) of the finished wage.
                float scale = 1f + __result.SumOfFactors;
                if (!(scale > 0f))
                {
                    return;
                }
                __result.Add(-__result.ResultNumber * (1f - factor) / scale,
                    new TaleWorlds.Localization.TextObject("{=RBM_merc_idle_half_pay}Idle mercenary half pay"));
            }
        }

        /// <summary>
        /// A company does not buy horses or pack animals in town (vanilla's settlement-entry horse trade; RBM's
        /// LordPackTrain already leaves companies out). Its loot still sells through PartiesSellLootCampaignBehavior.
        /// </summary>
        [HarmonyPatch(typeof(PartiesBuyHorseCampaignBehavior), "OnSettlementEntered")]
        private static class NoHorseBuying
        {
            private static bool Prefix(MobileParty mobileParty)
            {
                return !MercenaryCompanies.IsCompanyParty(mobileParty);
            }
        }

        // ---- Mercenary troops only ------------------------------------------------------------------------

        // Blocked-path log lines already written, so a path vanilla retries hourly or daily is logged once.
        private static readonly HashSet<string> _blockLogged = new HashSet<string>();

        /// <summary>Forgets which blocks were logged; called when a campaign is started or loaded.</summary>
        internal static void ResetBlockLog()
        {
            _blockLogged.Clear();
        }

        private static void LogBlockOnce(MobileParty party, string key, string message)
        {
            if (MercenaryLog.IsEnabled && _blockLogged.Add(party.StringId + "|" + key))
            {
                MercenaryLog.Log("BLOCK", party, message);
            }
        }

        /// <summary>
        /// A company never takes a settlement notable's volunteers (the culture's regular recruits). Its tavern
        /// hiring, in the same <c>CheckRecruiting</c>, is left alone: vanilla's lord-party branch only buys a tavern
        /// troop of the Mercenary occupation.
        /// </summary>
        [HarmonyPatch(typeof(RecruitmentCampaignBehavior), "RecruitVolunteersFromNotable")]
        private static class NoNotableVolunteers
        {
            private static bool Prefix(MobileParty mobileParty, Settlement settlement)
            {
                if (!MercenaryCompanies.IsCompanyParty(mobileParty))
                {
                    return true;
                }
                // Vanilla asks on every town entry and every hour inside; logged once per company per session.
                LogBlockOnce(mobileParty, "volunteers",
                    "notable volunteers skipped (first at " + settlement?.Name + "; companies hire only tavern mercenaries;"
                    + " logged once per company per session)");
                return false;
            }
        }

        /// <summary>A company recruits a prisoner only if he is a mercenary troop.</summary>
        [HarmonyPatch(typeof(RecruitPrisonersCampaignBehavior), "RecruitPrisonersAi")]
        private static class NoNonMercenaryPrisonerRecruits
        {
            private static bool Prefix(MobileParty mobileParty, CharacterObject troop, int num)
            {
                if (!MercenaryCompanies.IsCompanyParty(mobileParty) || MercenaryCompanies.IsMercenaryTroop(troop))
                {
                    return true;
                }
                LogBlockOnce(mobileParty, "prisoner|" + troop?.StringId,
                    "prisoner recruitment of " + num + " x " + troop?.Name + " (" + troop?.Occupation + ") skipped: not a mercenary troop");
                return false;
            }
        }

        /// <summary>
        /// A company troop never upgrades into a non-mercenary one. Every upgrade path -- vanilla's AI
        /// (PartyUpgraderCampaignBehavior), RBM's reimplementation (SpoilsUpgradePatches) -- divides the stack's XP
        /// by this cost and skips a target that comes to 0 men, so an unreachable cost closes the branch.
        /// (In vanilla and under RBM's troop overhaul every tavern mercenary tree stays Mercenary; this guards
        /// other data.)
        /// </summary>
        [HarmonyPatch(typeof(DefaultPartyTroopUpgradeModel), "GetXpCostForUpgrade")]
        private static class NoUpgradeOutOfMercenaries
        {
            private const int Unreachable = 100000000;

            private static void Postfix(PartyBase party, CharacterObject upgradeTarget, ref int __result)
            {
                if (upgradeTarget != null && !MercenaryCompanies.IsMercenaryTroop(upgradeTarget)
                    && MercenaryCompanies.IsCompanyParty(party?.MobileParty))
                {
                    __result = Unreachable;
                }
            }
        }

        /// <summary>
        /// Leaving a kingdom makes peace with each of its enemies, one "made peace" log entry (and, against
        /// the player's realm, a map notice) apiece -- every time a contract ends. The peace itself still
        /// happens; only the comment on it is skipped for a company.
        /// </summary>
        [HarmonyPatch(typeof(CommentOnMakePeaceBehavior), "OnMakePeace")]
        private static class NoPeaceNoticeForCompanies
        {
            private static bool Prefix(IFaction faction1, IFaction faction2)
            {
                if (MercenaryCompanies.Disbanding
                    && (MercenaryCompanies.HasCompanyId(faction1 as Clan) || MercenaryCompanies.HasCompanyId(faction2 as Clan)))
                {
                    return false;
                }
                return !(MercenaryCompanies.IsCompany(faction1) || MercenaryCompanies.IsCompany(faction2));
            }
        }

        /// <summary>
        /// The disband cleanup (feature switched off) destroys each company quietly: no "X clan was destroyed"
        /// pop-up (SandBox's DefaultNotificationsCampaignBehavior) and no clan-destroyed log entry. Only while
        /// MercenaryCompanies.Disbanding is set; a company destroyed in play (its leader killed) is announced as
        /// vanilla announces any clan.
        /// </summary>
        [HarmonyPatch(typeof(SandBox.CampaignBehaviors.DefaultNotificationsCampaignBehavior), "OnClanDestroyed")]
        private static class NoDisbandPopup
        {
            private static bool Prefix(Clan destroyedClan)
            {
                return !(MercenaryCompanies.Disbanding && MercenaryCompanies.HasCompanyId(destroyedClan));
            }
        }

        [HarmonyPatch(typeof(CommentOnClanDestroyedBehavior), "OnClanDestroyed")]
        private static class NoDisbandLogEntry
        {
            private static bool Prefix(Clan destroyedClan)
            {
                return !(MercenaryCompanies.Disbanding && MercenaryCompanies.HasCompanyId(destroyedClan));
            }
        }
    }
}
