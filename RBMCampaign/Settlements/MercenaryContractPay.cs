using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// A mercenary company under a kingdom's pay is kept by its employer, not out of its own purse. While a
    /// clan holds a mercenary contract, its ruling clan pays it two ways each day, both through the clan
    /// finance model so they land in the Daily Gold Change beside every other clan revenue: a stipend (a flat
    /// <c>300</c> a day plus <c>2 gold per point of influence and renown</c>), and a full reimbursement of the
    /// day's troop wages -- at the DOUBLED mercenary rate the men now draw. The men themselves are the
    /// better-paid for it: a mercenary stack banks twice its wage into spoils (see <see cref="SpoilsPool"/>'s
    /// wage deposit), which is what the second wage the company lays out actually buys. Net to the company:
    /// the doubled wage it pays its men is handed straight back by the crown, and the stipend is its to keep.
    /// </summary>
    /// <remarks>
    /// Applies to every mercenary clan, player and AI alike. The doubled-wage bargain cannot bankrupt an AI
    /// company because the reimbursement travels with it -- it pays double and is paid double back, exactly
    /// as the player is; the only company left out of pocket is one whose employer cannot afford it, which is
    /// the point of the cap below.
    ///
    /// The money is conserved: the ruling clan's leader is debited exactly what the mercenary is paid, capped
    /// at what he can actually afford -- a broke employer stiffs his hired swords, and the company eats the
    /// shortfall, as a mercenary in the field would. The debit is a null-recipient <see cref="GiveGoldAction"/>
    /// (money out of the ruler), and the credit is the finance line (money into the mercenary); the two net
    /// to no coin created. This mirrors <see cref="SettlementIncomeFinanceLine"/>, whose apply pass hands over
    /// income the finance model then turns into the leader's gold.
    /// </remarks>
    public static class MercenaryContractPay
    {
        // A guaranteed 300 a day for taking the contract at all, then 2 gold more per point of the
        // company's standing (influence + renown) on top -- a small company still earns its keep, a
        // storied one is paid for the name it brings.
        private const float StipendBase = 300f;
        private const float StipendPerStandingPoint = 2f;

        /// <summary>
        /// True while a clan is a hired sword in a kingdom's service -- sworn to a liege it does not rule,
        /// under a mercenary contract rather than a vassal's oath, with a ruler other than itself to pay it.
        /// The one condition the whole feature turns on, read the same way here and at the spoils wage deposit
        /// so the two never disagree. Player and AI mercenaries alike.
        /// </summary>
        public static bool IsMercenaryClan(Clan clan)
        {
            return clan != null
                && clan.Kingdom != null
                && clan.IsUnderMercenaryService
                && clan.Kingdom.Leader != null
                && clan.Kingdom.Leader != clan.Leader;
        }

        /// <summary>
        /// Whether a party's wage counts toward the mercenary bargain: a field company the leader pays for,
        /// not a garrison or a fief's militia (kept by the settlement), a bandit rabble (paid nothing), or a
        /// caravan (its own purse). The same set the spoils wage deposit doubles, so the wage the company is
        /// charged the second time and the wage the crown reimburses are the very same men's.
        /// </summary>
        public static bool CountsForMercWage(MobileParty party)
        {
            return party != null
                && party.IsActive
                && !party.IsGarrison
                && !party.IsMilitia
                && !party.IsBandit
                && !party.IsCaravan;
        }

        /// <summary>
        /// The day's field-troop wage bill at the base rate, summed over the clan's own war parties -- the
        /// very stacks whose spoils deposit doubles. The base rate, deliberately: vanilla's wage expense has
        /// already charged this once, so the finance line charges it a second time to make the mercenary
        /// double, and the crown reimburses twice it. Summed for any mercenary clan, player or AI.
        /// </summary>
        public static int ClanFieldTroopBaseWages(Clan clan)
        {
            if (clan == null)
            {
                return 0;
            }
            PartyWageModel wageModel = Campaign.Current.Models.PartyWageModel;
            int total = 0;
            foreach (WarPartyComponent warParty in clan.WarPartyComponents)
            {
                MobileParty mobileParty = warParty?.MobileParty;
                if (!CountsForMercWage(mobileParty))
                {
                    continue;
                }
                TroopRoster roster = mobileParty.Party?.MemberRoster;
                if (roster == null)
                {
                    continue;
                }
                for (int i = 0; i < roster.Count; i++)
                {
                    TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                    if (element.Character == null || element.Character.IsHero || element.Number <= 0)
                    {
                        continue;
                    }
                    total += wageModel.GetCharacterWage(element.Character) * element.Number;
                }
            }
            return total;
        }

        /// <summary>The standing stipend the crown pays a mercenary company each day, on its influence and renown.</summary>
        private static int StipendFor(Clan clan)
        {
            float standing = MathF.Max(0f, clan.Influence + clan.Renown);
            return MathF.Round(StipendBase + StipendPerStandingPoint * standing);
        }

        // These two patches touch DefaultClanFinanceModel, whose static initializer reads
        // Game.Current.GameTextManager.FindText(...) across a score of fields. The type is
        // beforefieldinit, so the runtime may run that initializer the moment the type is first prepared
        // -- which Harmony patching one of its methods does. Left to PatchAll they would be applied at
        // module load, before any game exists, and the initializer would dereference a null Game.Current,
        // throw, and be cached as a failed type-init for the life of the process -- surfacing much later
        // as a TypeInitializationException while the map screen builds. So they are kept OFF the
        // attribute-discovered PatchAll (no [HarmonyPatch]) and applied by hand from ApplyDeferred once a
        // game is live. See WorkshopPurse for the same cctor trap on a neighbouring method.

        /// <summary>
        /// Applies the two DefaultClanFinanceModel patches, but only once a game exists so its
        /// Game.Current-reading static initializer runs safely. A no-op while <see cref="Game.Current"/>
        /// is null; RBMCampaignPatcher.DoPatching calls this on every patch pass, so it takes effect on
        /// the OnGameStart pass, by which point Game.Current is set for a new game and a loaded save alike.
        /// </summary>
        /// <remarks>
        /// Must re-apply on EVERY pass, not just the first: ApplyHarmonyPatches strips the whole
        /// rbmcampaign harmony with UnpatchAll before each DoPatching, so a game started or a save loaded
        /// after the first campaign of a process would otherwise be left with these two patches removed
        /// and never restored (a stale "already applied" flag was the bug). Re-applying is safe -- the
        /// preceding UnpatchAll clears any prior copy, so no duplicate postfix ever double-pays, and
        /// RunClassConstructor is idempotent (the cctor runs at most once per process).
        /// </remarks>
        public static void ApplyDeferred(Harmony harmony)
        {
            if (Game.Current == null)
            {
                return;
            }
            // Force the initializer to complete now, with Game.Current live, so its texts are cached once
            // and for good; after this the patching below can prepare the type without risk.
            RuntimeHelpers.RunClassConstructor(typeof(DefaultClanFinanceModel).TypeHandle);

            harmony.Patch(
                AccessTools.Method(typeof(DefaultClanFinanceModel), "AddMercenaryIncome"),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(MercenaryContractPay), nameof(SuppressVanillaMercenaryIncomePrefix))));
            harmony.Patch(
                AccessTools.Method(typeof(DefaultClanFinanceModel), "CalculateClanGoldChange"),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(MercenaryContractPay), nameof(PayMercenaryContractThroughFinancePostfix))));
        }

        /// <summary>
        /// RBM pays every mercenary contract in full (stipend + doubled-wage reimbursement), so vanilla's
        /// influence-based mercenary award must not land on top of it, or the company is paid twice. Skip
        /// <c>AddMercenaryIncome</c> for exactly the clans RBM takes over -- gated on the same condition as
        /// the pay, so a clan not under RBM's mercenary bookkeeping (feature off) keeps vanilla's award,
        /// drawn from its employer's <c>MercenaryWallet</c>, untouched.
        /// </summary>
        private static bool SuppressVanillaMercenaryIncomePrefix(Clan clan)
        {
            return !(RBMConfig.RBMConfig.rbmCampaignEnabled && IsMercenaryClan(clan));
        }

        private static void PayMercenaryContractThroughFinancePostfix(Clan clan, bool applyWithdrawals, ref ExplainedNumber __result)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || !IsMercenaryClan(clan))
            {
                return;
            }

            Contract contract = Assess(clan);

            if (applyWithdrawals)
            {
                // The authoritative once-a-day pass whose result becomes the leader's gold. Debit the
                // ruler for exactly what the mercenary is paid (a null-recipient give takes it out of
                // him), then book the pay as income; the two net to no coin created.
                int total = contract.StipendPaid + contract.WagePaid;
                if (total > 0)
                {
                    GiveGoldAction.ApplyBetweenCharacters(contract.Ruler, null, total, true);
                }
            }

            // Both passes book the same lines: the apply pass turns them into gold, the display pass only
            // makes the breakdown read what the day paid.
            Book(clan, contract, ref __result, income: true, expense: true);
        }

        /// <summary>
        /// Books the contract's lines into a display-only breakdown -- the income lines, the expense lines,
        /// or both -- so the Clan screen's separate income and expense totals can each carry their half
        /// (see <see cref="ClanFinanceTabLines"/>). Moves no coin.
        /// </summary>
        internal static void AddDisplayLines(Clan clan, ref ExplainedNumber breakdown, bool income, bool expense)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || !IsMercenaryClan(clan))
            {
                return;
            }
            Book(clan, Assess(clan), ref breakdown, income, expense);
        }

        /// <summary>What the contract owes and pays today, figured once so every booking quotes the same numbers.</summary>
        private struct Contract
        {
            public Hero Ruler;
            public int BaseWages;
            public int Stipend;
            public int WageDue;
            public int StipendPaid;
            public int WagePaid;
            public int Unpaid;
        }

        private static Contract Assess(Clan clan)
        {
            Contract c = default(Contract);
            c.Ruler = clan.Kingdom.Leader;
            c.BaseWages = ClanFieldTroopBaseWages(clan);
            c.Stipend = StipendFor(clan);
            // What the crown owes today: the full doubled wage, and the standing stipend on top. Capped
            // at what the ruler can actually afford -- stipend first, wages with whatever is left -- so a
            // poor employer pays what he can and the company eats the rest, as a stiffed mercenary would.
            c.WageDue = 2 * c.BaseWages;
            int affordable = MathF.Max(0, c.Ruler.Gold);
            c.StipendPaid = MathF.Min(c.Stipend, affordable);
            c.WagePaid = MathF.Min(c.WageDue, affordable - c.StipendPaid);
            c.Unpaid = (c.Stipend - c.StipendPaid) + (c.WageDue - c.WagePaid);
            return c;
        }

        private static void Book(Clan clan, Contract c, ref ExplainedNumber result, bool income, bool expense)
        {
            if (expense)
            {
                // The second wage the company lays out on its men, the one that makes the mercenary rate a
                // double. Vanilla already charged the first; this is the extra, and the men bank it as the
                // doubled spoils deposit (see DepositWageSpoils). Unconditional while under contract and on
                // both passes, a negative as any wage expense is -- it stands whether or not the crown pays.
                if (c.BaseWages > 0)
                {
                    result.Add(-c.BaseWages, new TextObject("{=RBM_merc_wage_cost}Mercenary troop wages"));
                }

                // Vanilla slips a mercenary bonus into the landless-clan subsistence stipend as well -- an
                // extra 40 per tier on top of the 80 every fiefless minor clan draws (CalculateClanIncomeInternal
                // line 133). That extra is a mercenary payment too, and RBM is now the company's sole paymaster,
                // so cancel it; the 80-per-tier floor is left, being a minor-clan subsistence rather than a
                // mercenary award. Vanilla excludes the player from that stipend outright, so this only ever
                // fires for AI companies, which is why it goes unlabelled -- no one reads their finance screen.
                if (clan != Clan.PlayerClan && clan.Fiefs.Count == 0)
                {
                    int mercBonus = clan.Tier * 40;
                    if (mercBonus > 0)
                    {
                        result.Add(-mercBonus);
                    }
                }
            }

            if (!income)
            {
                return;
            }

            // The net booked here is always the paid amount (StipendPaid + WagePaid), identical to the
            // ruler's debit, so the Daily Gold Change and the debit never disagree -- but how it is spelt
            // out depends on who reads the screen.
            if (clan == Clan.PlayerClan && c.Unpaid > 0)
            {
                // The player reads a finance screen, so when the crown falls short show the full pay owed as
                // income and book the unpaid remainder back as a matching shortfall line -- the breakdown
                // then reads what was earned AND what a broke employer failed to hand over, netting to the
                // paid amount all the same.
                if (c.Stipend > 0)
                {
                    result.Add(c.Stipend, new TextObject("{=RBM_merc_stipend}Mercenary stipend"));
                }
                if (c.WageDue > 0)
                {
                    result.Add(c.WageDue, new TextObject("{=RBM_merc_wage_pay}Mercenary wage pay"));
                }
                result.Add(-c.Unpaid, new TextObject("{=RBM_merc_unpaid}Employer could not pay"));
            }
            else
            {
                // Paid in full, or an AI clan whose breakdown no one reads: book only what was actually
                // handed over. For an AI clan this is what keeps its gold conserved without an informational
                // shortfall line it would never see.
                if (c.StipendPaid > 0)
                {
                    result.Add(c.StipendPaid, new TextObject("{=RBM_merc_stipend}Mercenary stipend"));
                }
                if (c.WagePaid > 0)
                {
                    result.Add(c.WagePaid, new TextObject("{=RBM_merc_wage_pay}Mercenary wage pay"));
                }
            }
        }
    }
}
