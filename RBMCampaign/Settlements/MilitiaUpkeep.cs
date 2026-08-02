using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// What a settlement lays out to keep its militia under arms, and where the money comes from.
    ///
    /// Militia are the one armed body in the game nobody pays at all. They appear in no clan's
    /// expenses, no wage model bills anyone for them, and they cost their settlement nothing -- vanilla
    /// grows them out of prosperity and loyalty and leaves it there. That is defensible for a levy in
    /// the abstract, but under the ledger it left a real hole: RBM credits every party's daily wage
    /// into its troops' spoils purses, so militia were accruing a full soldier's pay out of thin air.
    ///
    /// So the levy is put on the ledger, split the way the design spec splits it -- into two legs that
    /// answer two different questions:
    ///
    ///   * MAINTENANCE -- the kit a militiaman wears out standing his watch, mended and replaced at
    ///     someone's cost. A tenth of a soldier's wage. It is a real charge on the settlement, drawn
    ///     from the pot the spec names for each kind of place: a village from its own purse, buying the
    ///     gear off the town it trades with; a castle from its wealth; a town from its citizens' wealth,
    ///     the market that arms them.
    ///
    ///   * SALARY -- pay for the days a man stands a watch instead of working his trade. Under
    ///     feudalism a village's and a castle's militia draw none: they owe their service. A town's are
    ///     part-timers who are paid a quarter of a soldier's wage, and the money reaches them as
    ///     citizens -- the town's treasury paying its own townsfolk.
    ///
    /// The other nine tenths of a wage that a soldier would draw are never real money for a militiaman
    /// and are credited to no one.
    /// </summary>
    public static class MilitiaUpkeep
    {
        /// <summary>
        /// Share of a full soldier's wage a militiaman's kit costs to keep serviceable -- the
        /// maintenance leg, paid everywhere. Far below a soldier's own upkeep, deliberately: a
        /// militiaman is not marching, only standing a watch, and wears his gear out slowly.
        ///
        /// A tenth rather than a fifth. At a fifth the militia bill was the largest single charge on a
        /// town's treasury -- 27,444 denars over seven days at Danustica, more than its garrison's
        /// wages and four times its tariff income -- for men who are not soldiers and were free in
        /// vanilla.
        /// </summary>
        public const float MilitiaWageShare = 0.1f;

        /// <summary>
        /// Share of a full soldier's wage a TOWN pays its militia as salary -- the part-time watch's
        /// pay, drawn only by a city's militia and by no village's or castle's, whose service is owed
        /// under feudalism. The money is a transfer inside the town: out of the treasury, into the
        /// citizens' hands, since the militia ARE the townsfolk.
        /// </summary>
        public const float MilitiaCitySalaryShare = 0.25f;

        /// <summary>
        /// Days of a militiaman's maintenance a settlement must have in hand to keep him under arms.
        ///
        /// This is what turns the bill into a limit. A settlement does not muster the militia its
        /// prosperity would allow and then go broke keeping them: it musters what it can keep paying,
        /// and the purse is what says how many that is. Twenty days' maintenance per man is the margin
        /// -- enough that a single bad convoy does not disband the watch, little enough that a village
        /// cannot field a standing company on a harvest's takings.
        /// </summary>
        public const int MilitiaPayDaysHeld = 20;

        /// <summary>
        /// Men a settlement sheds per day while it is over what it can afford. Deliberately slow: men
        /// drift home when the pay stops, they are not dismissed on parade.
        /// </summary>
        public const float MilitiaShedPerDay = 1f;

        /// <summary>
        /// The soft cap on a settlement's militia, as a share of the manpower behind it -- a village's
        /// hearths, a fortification's prosperity. Not a wall the count cannot pass: past it, growth
        /// slows to <see cref="MilitiaOverCapGrowthFactor"/> of its rate rather than stopping, so a
        /// settlement can still creep higher on strong loyalty or a governor's perks, only slowly.
        ///
        /// A village that answers to a city keeps a smaller watch than one that answers to a castle:
        /// the castle is a garrison's seat with no standing troops of its own to spare for the fields,
        /// so its villages carry more of their own defence.
        /// </summary>
        public const float MilitiaCapVillageCity = 0.40f;
        public const float MilitiaCapVillageCastle = 0.50f;

        /// <summary>Militia soft cap for a castle, as a share of its prosperity.</summary>
        public const float MilitiaCapCastle = 0.50f;

        /// <summary>
        /// Militia soft cap for a city, as a share of its prosperity -- the highest of the three: a
        /// town is a crowd, and a crowd under threat arms a great many of its own.
        /// </summary>
        public const float MilitiaCapCity = 0.70f;

        /// <summary>What fraction of a day's growth a settlement keeps once it is over its soft cap.</summary>
        public const float MilitiaOverCapGrowthFactor = 0.10f;

        /// <summary>Times a militiaman's kit cost the funding pot must hold before it may arm a new one.</summary>
        public const int MilitiaSpawnReserveMult = 5;

        /// <summary>
        /// The same reserve for a VILLAGE, held lower than a fortification's. A village purse is small and
        /// spiky -- it swells on a convoy's return and empties again -- so demanding as many days of a
        /// kit's cost in hand as a town would gate its levy out of ever mustering. Three rather than five,
        /// alongside the quarter-price kit (<see cref="MilitiaVillageGearShare"/>), so a village that has
        /// turned a season's trade can actually arm the watch its hearths support.
        /// </summary>
        public const int MilitiaVillageSpawnReserveMult = 3;

        /// <summary>
        /// Share of a militiaman's full war-kit value a VILLAGE actually pays to arm one. A village
        /// levy is not outfitted like a soldier: the men bring their own tools and cheap arms and are
        /// given only what the muster cannot do without, so the village buys a quarter of a real kit off
        /// the town it trades with rather than a whole one. Priced at full value everywhere else -- a
        /// town or castle arms its watch properly.
        ///
        /// Applied to both the affordability gate (so the reserve it must hold is a quarter as steep) and
        /// the charge itself, keeping the two in step: at full value the ~18k Empire kit put the 5x
        /// spawn reserve (~89k) out of every village purse's reach, so no village ever fielded a growing
        /// militia at all.
        /// </summary>
        public const float MilitiaVillageGearShare = 0.25f;

        private static readonly TextObject UnaffordableText = new TextObject("{=RBM_militia_unpaid}Cannot be paid");
        private static readonly TextObject OverCapText = new TextObject("{=RBM_militia_overcap}Over muster");
        private static readonly TextObject CannotArmText = new TextObject("{=RBM_militia_unarmed}Cannot be armed");

        /// <summary>
        /// The pot that funds a settlement's militia maintenance, and against which its affordability is
        /// judged. Each kind of place pays from the purse the spec names: a village and a castle from
        /// their single settlement wealth, a town from its citizens' market money.
        /// </summary>
        private static int MaintenancePot(Settlement settlement)
        {
            if (settlement == null)
            {
                return 0;
            }
            if (settlement.IsTown)
            {
                return SettlementWealth.GetCitizenWealth(settlement);
            }
            return SettlementWealth.GetSettlementWealth(settlement);
        }

        /// <summary>
        /// What this settlement's militia costs it in maintenance each day.
        /// </summary>
        /// <remarks>
        /// Read off the militia party's own wage bill rather than priced per head. An earlier version
        /// costed every militiaman as a tier-one recruit on the grounds that most of them are, and that
        /// pricing the real roster would make the cap chase its own tail. Both halves of that were
        /// wrong. Measured over seven days, Danustica spent 27,444 denars on militia -- about twice
        /// what a tier-one roster of that size comes to -- so the cap was licensing roughly double the
        /// militia the treasury could keep, and the treasury went bankrupt on the difference.
        ///
        /// The runaway it was guarding against does not exist either, because this bills the WHOLE
        /// roster rather than an average man: shedding always lowers the bill, so the test converges
        /// instead of chasing a rising average down to zero.
        /// </remarks>
        public static int DailyMaintenanceBill(Settlement settlement)
        {
            MobileParty party = (settlement.MilitiaPartyComponent != null)
                ? settlement.MilitiaPartyComponent.MobileParty
                : null;
            if (party != null && party.IsActive)
            {
                return (int)(party.TotalWage * MilitiaWageShare);
            }

            // Militia counted but not yet mustered into a party: nothing to read a wage off, so fall
            // back to a recruit's rate for the headcount.
            return (int)(settlement.Militia * MilitiaWageShare * TierBasedWageModel.WageForTier(1, false));
        }

        /// <summary>
        /// Whether the funding pot can keep the militia the settlement currently has under arms -- twenty
        /// days of their maintenance in hand.
        /// </summary>
        public static bool CanKeepMilitia(Settlement settlement)
        {
            int bill = DailyMaintenanceBill(settlement);
            if (bill <= 0)
            {
                return true;
            }
            return MaintenancePot(settlement) >= bill * MilitiaPayDaysHeld;
        }

        /// <summary>
        /// The prosperity a fortification's militia cap is sized on. Zero for anything without a Town
        /// component, which a village's cap never reads.
        /// </summary>
        private static float Prosperity(Settlement settlement)
        {
            return settlement.Town != null ? settlement.Town.Prosperity : 0f;
        }

        /// <summary>
        /// The soft cap on this settlement's militia -- the count past which growth slows to a trickle.
        /// A village is sized on its hearths and on whether it answers to a city or a castle; a
        /// fortification on its prosperity. Zero (no cap) for anything else.
        /// </summary>
        public static float MilitiaCap(Settlement settlement)
        {
            if (settlement.IsVillage)
            {
                float hearth = settlement.Village != null ? settlement.Village.Hearth : 0f;
                Settlement bound = settlement.Village != null ? settlement.Village.TradeBound : null;
                float share = (bound != null && bound.IsCastle) ? MilitiaCapVillageCastle : MilitiaCapVillageCity;
                return hearth * share;
            }
            if (settlement.IsCastle)
            {
                return Prosperity(settlement) * MilitiaCapCastle;
            }
            if (settlement.IsTown)
            {
                return Prosperity(settlement) * MilitiaCapCity;
            }
            return 0f;
        }

        /// <summary>
        /// Holds a settlement's militia to what its manpower supports and what it can pay for.
        /// </summary>
        /// <remarks>
        /// Vanilla grows militia out of prosperity, hearths and loyalty, and nothing anywhere asks
        /// whether the place can support or afford them -- harmless while militia were free and both
        /// uncapped, wrong the moment they are neither. Two rules stack here, both pushing the day's
        /// change down rather than replacing it, so vanilla's own lines stay on the breakdown and the
        /// player can read what the militia would have been:
        ///
        ///   * A SOFT CAP on manpower. Past a share of the settlement's hearths or prosperity, the day's
        ///     growth is cut to a tenth -- a ceiling that yields to strong loyalty or a governor's
        ///     perks rather than one the count cannot pass. Only positive growth is touched; a
        ///     settlement already shedding is left to shed.
        ///
        ///   * AN AFFORDABILITY FLOOR. A settlement that cannot keep twenty days of its militia's
        ///     maintenance in the funding pot sheds men, whatever its cap says -- and this wins, because
        ///     a town too poor to arm its watch loses it even below the manpower ceiling. Applied to
        ///     every settlement, towns included: a market spent dry SHOULD start losing its militia, and
        ///     an exception would only hide that.
        /// </remarks>
        [HarmonyPatch(typeof(DefaultSettlementMilitiaModel), "CalculateMilitiaChange")]
        private static class AffordableMilitiaPatch
        {
            private static void Postfix(Settlement settlement, ref ExplainedNumber __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null)
                {
                    return;
                }

                ApplySoftCap(settlement, ref __result);

                // A settlement that cannot arm a new militiaman fields no new ones -- growth is held to
                // zero, though the men it already has are left standing (the maintenance shed below is
                // what thins those, when even their upkeep cannot be met).
                if (__result.ResultNumber > 0f && !CanAffordSpawn(settlement))
                {
                    __result.Add(-__result.ResultNumber, CannotArmText);
                }

                if (!CanKeepMilitia(settlement) && __result.ResultNumber > -MilitiaShedPerDay)
                {
                    __result.Add(-MilitiaShedPerDay - __result.ResultNumber, UnaffordableText);
                }
            }

            /// <summary>
            /// Cuts the day's growth to <see cref="MilitiaOverCapGrowthFactor"/> once the settlement is
            /// at or over its soft cap. Leaves a settlement under its cap, or one already losing men,
            /// untouched.
            /// </summary>
            private static void ApplySoftCap(Settlement settlement, ref ExplainedNumber __result)
            {
                if (__result.ResultNumber <= 0f)
                {
                    return;
                }
                float cap = MilitiaCap(settlement);
                if (cap <= 0f || settlement.Militia < cap)
                {
                    return;
                }
                __result.Add(-(1f - MilitiaOverCapGrowthFactor) * __result.ResultNumber, OverCapText);
            }
        }

        /// <summary>
        /// Pays a militia stack's upkeep -- maintenance everywhere, salary in a town -- out of the pots
        /// the spec names, and returns the maintenance a stack banks as its own kit money.
        /// </summary>
        /// <remarks>
        /// Two legs, each conserving:
        ///
        ///   * MAINTENANCE. A village debits its own purse and pays the town it trades with, the money
        ///     moving village -> town market exactly as recruit gear does (see
        ///     <see cref="RecruitSupply.GetSupplyMarket"/>); the amount it could not cover simply is not
        ///     spent. A castle or a town debits its funding pot and the stack banks that as spoils --
        ///     the militiaman's own kit money, parked in his purse the way every other soldier's is.
        ///     Nothing is credited that the pot did not pay, so no denar is invented.
        ///
        ///   * SALARY. A town moves a quarter-wage from its treasury to its citizens; a village and a
        ///     castle pay none. A pure transfer inside the settlement -- credited only what the treasury
        ///     could give.
        ///
        /// Unlike the garrison there is no owner backstop: a lord never agreed to pay these men, so an
        /// empty pot simply means an unpaid watch that day, and the affordability cap thins it over time.
        /// The caller banks exactly the maintenance this returns, so the deposit and the payment are one
        /// number by construction.
        /// </remarks>
        public static int PayMilitiaUpkeep(MobileParty militiaParty, int fullWage)
        {
            if (fullWage <= 0)
            {
                return 0;
            }

            Settlement settlement = (militiaParty.CurrentSettlement ?? militiaParty.HomeSettlement);
            if (settlement == null)
            {
                return 0;
            }

            PaySalary(settlement, fullWage);
            return PayMaintenance(settlement, fullWage);
        }

        /// <summary>
        /// The maintenance leg. Returns the amount the stack banks as its own kit money -- the paid
        /// maintenance for a castle or town, and zero for a village, whose maintenance leaves for the
        /// town market rather than sitting in the militiaman's purse.
        /// </summary>
        private static int PayMaintenance(Settlement settlement, int fullWage)
        {
            int maintenance = (int)(fullWage * MilitiaWageShare);
            if (maintenance <= 0)
            {
                return 0;
            }

            if (settlement.IsVillage)
            {
                // The village buys the gear off the town it trades with and pays that town's merchants
                // for it -- money village -> town market, mirroring the goods that went the other way.
                // Capped at the purse by Debit's own clamp, so the credit can never exceed what was
                // paid. A village with no town to trade into (a broke faction) draws nothing, and the
                // watch goes without.
                int paid = SettlementWealth.Debit(settlement, maintenance, SettlementWealth.Source.Militia);
                if (paid > 0)
                {
                    Settlement market = RecruitSupply.GetSupplyMarket(settlement);
                    if (market != null && SettlementWealth.HasCitizenPurse(market))
                    {
                        SettlementWealth.CreditCitizens(market, paid, SettlementWealth.Source.Militia);
                    }
                }
                // A village never banks militia spoils: its maintenance went to the town, not the purse.
                return 0;
            }

            // A castle from its wealth, a town from its citizens' market money. Whatever the pot pays,
            // the stack banks as spoils -- the militiaman's own kit money, the deposit equal to the
            // payment by construction.
            if (settlement.IsTown)
            {
                return SettlementWealth.DebitCitizens(settlement, maintenance, SettlementWealth.Source.Militia);
            }
            return SettlementWealth.Debit(settlement, maintenance, SettlementWealth.Source.Militia);
        }

        /// <summary>
        /// The salary leg. A town pays its part-time militia a quarter-wage out of its treasury and the
        /// money reaches its citizens; a village and a castle pay nothing, their militia owing service
        /// under feudalism. A pure transfer, credited only what the treasury could actually give.
        /// </summary>
        private static void PaySalary(Settlement settlement, int fullWage)
        {
            if (!settlement.IsTown)
            {
                return;
            }
            int salary = (int)(fullWage * MilitiaCitySalaryShare);
            if (salary <= 0)
            {
                return;
            }
            int paid = SettlementWealth.Debit(settlement, salary, SettlementWealth.Source.Militia);
            if (paid > 0)
            {
                SettlementWealth.CreditCitizens(settlement, paid, SettlementWealth.Source.Militia);
            }
        }

        // ------------------------------------------------------------------ spawn cost (the kit a new man is given)

        /// <summary>
        /// A militiaman's growth banked here as it happens but not yet paid for -- the fractional day's
        /// growth accrues until it makes a whole man, and only then is one armed. Session state, keyed by
        /// settlement, cleared per campaign; a fraction of a man lost on reload is beneath notice.
        /// </summary>
        private static readonly Dictionary<Settlement, float> _pendingGrowth = new Dictionary<Settlement, float>();

        /// <summary>Drops the accumulators before a new campaign's settlements take their place.</summary>
        public static void ResetForNewSession()
        {
            _pendingGrowth.Clear();
        }

        /// <summary>
        /// The militiaman a settlement's spawn cost is priced and armed as -- its culture's plain melee
        /// militia, the commonest of the muster. A representative rather than the exact roster mix: the
        /// design treats militia "upgrade" as abstract, so a settlement fielding dearer militia pays more
        /// only through carrying more of them, not through a per-man promotion charge.
        /// </summary>
        private static CharacterObject SpawnTroop(Settlement settlement)
        {
            return (settlement != null && settlement.Culture != null) ? settlement.Culture.MeleeMilitiaTroop : null;
        }

        /// <summary>
        /// What arming one militiaman costs -- his kit's worth, mount-less like a recruit's, and cut to
        /// <see cref="MilitiaVillageGearShare"/> for a village, whose levy is armed on the cheap. Drives
        /// both the affordability gate and the arming charge, so the two never disagree.
        /// </summary>
        private static int SpawnCostPerMan(Settlement settlement)
        {
            CharacterObject troop = SpawnTroop(settlement);
            if (troop == null)
            {
                return 0;
            }
            int full = SpoilsPool.GetEquipmentValue(troop);
            return (settlement != null && settlement.IsVillage)
                ? (int)(full * MilitiaVillageGearShare)
                : full;
        }

        /// <summary>
        /// Whether the funding pot can arm a new militiaman with a reserve to spare -- so many times his
        /// kit in hand, five for a fortification and three for a village (its purse being smaller and
        /// spikier). Read against the same pot the maintenance is drawn from, so the place that pays to
        /// keep him is the place that must be able to afford to raise him.
        /// </summary>
        public static bool CanAffordSpawn(Settlement settlement)
        {
            int per = SpawnCostPerMan(settlement);
            if (per <= 0)
            {
                return true;
            }
            int mult = (settlement != null && settlement.IsVillage)
                ? MilitiaVillageSpawnReserveMult
                : MilitiaSpawnReserveMult;
            return MaintenancePot(settlement) >= per * mult;
        }

        /// <summary>
        /// Banks the day's militia GROWTH the moment it is applied, so it can be armed later out of the
        /// village suppression window (see <see cref="ChargePendingSpawn"/>). Growth is the only militia
        /// change that happens inside a fief's DailyTick -- an escort borrowing men (see
        /// <c>VillagerEscort</c>) moves them on other events, so measuring the DailyTick delta catches
        /// muster and nothing else.
        /// </summary>
        private static void RecordGrowth(Settlement settlement, float preMilitia)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null)
            {
                return;
            }
            float delta = settlement.Militia - preMilitia;
            if (delta <= 0f)
            {
                return;
            }
            float acc;
            _pendingGrowth.TryGetValue(settlement, out acc);
            _pendingGrowth[settlement] = acc + delta;
        }

        [HarmonyPatch(typeof(Town), "DailyTick")]
        private static class TownMilitiaGrowthRecord
        {
            private static void Prefix(Town __instance, out float __state)
            {
                __state = (__instance != null && __instance.Settlement != null) ? __instance.Settlement.Militia : 0f;
            }

            private static void Postfix(Town __instance, float __state)
            {
                if (__instance != null)
                {
                    RecordGrowth(__instance.Settlement, __state);
                }
            }
        }

        [HarmonyPatch(typeof(Village), "DailyTick")]
        private static class VillageMilitiaGrowthRecord
        {
            private static void Prefix(Village __instance, out float __state)
            {
                __state = (__instance != null && __instance.Settlement != null) ? __instance.Settlement.Militia : 0f;
            }

            private static void Postfix(Village __instance, float __state)
            {
                if (__instance != null)
                {
                    RecordGrowth(__instance.Settlement, __state);
                }
            }
        }

        /// <summary>
        /// Arms every whole militiaman a settlement has grown since it last paid, out of the pots the
        /// spec names. Called from the daily settlement pass rather than the DailyTick that recorded the
        /// growth, so a village's purse write is not caught inside <c>VillageGoldStock</c>'s suppression.
        /// </summary>
        /// <remarks>
        /// Each kind of place arms its men the way it does its maintenance: a village off the town it
        /// trades with, paying that town for the gear (the recruit gear leg, reused whole); a town from
        /// its own citizens' market money; a castle from its wealth, sourcing the kit from outside its
        /// walls. Only whole men are armed -- the sub-man remainder waits in the accumulator for the next
        /// day's growth to complete it.
        /// </remarks>
        public static void ChargePendingSpawn(Settlement settlement)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null)
            {
                return;
            }
            float acc;
            if (!_pendingGrowth.TryGetValue(settlement, out acc) || acc < 1f)
            {
                return;
            }
            CharacterObject troop = SpawnTroop(settlement);
            if (troop == null)
            {
                _pendingGrowth[settlement] = 0f;
                return;
            }

            int armed = 0;
            while (acc >= 1f)
            {
                ArmOneMilitiaman(settlement, troop);
                acc -= 1f;
                armed++;
            }
            _pendingGrowth[settlement] = acc;

            if (SpoilsLog.IsEnabled && armed > 0)
            {
                SpoilsLog.Log("MILITIA", (settlement.Name != null ? settlement.Name.ToString() : settlement.StringId)
                    + " armed " + armed + " new militia at " + SpawnCostPerMan(settlement) + "d each");
            }
        }

        /// <summary>Arms one militiaman out of the settlement's funding pot, routed by kind of place.</summary>
        private static void ArmOneMilitiaman(Settlement settlement, CharacterObject troop)
        {
            if (settlement.IsVillage)
            {
                // The village buys the kit off the town it trades with and pays that town's merchants --
                // the recruit gear leg, which already does exactly this (debit village, credit town) --
                // but only a quarter of a full kit's worth, the cheap arming of a levy.
                RecruitSupply.DrawKitFromMarket(RecruitSupply.GetSupplyMarket(settlement), settlement, troop, 1,
                    MilitiaVillageGearShare);
                return;
            }
            int cost = SpoilsPool.GetEquipmentValue(troop);
            if (cost <= 0)
            {
                return;
            }
            if (settlement.IsTown)
            {
                // The townsmen arm their own watch out of the market's money.
                SettlementWealth.DebitCitizens(settlement, cost, SettlementWealth.Source.Militia);
            }
            else
            {
                // A castle has no market; the kit is sourced from outside and the coin leaves its wealth.
                SettlementWealth.Debit(settlement, cost, SettlementWealth.Source.Militia);
            }
        }
    }
}
