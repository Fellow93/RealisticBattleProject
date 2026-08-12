using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Grows and pays for a fief's garrison out of the settlement's own WEALTH -- the settlement economy,
    /// not the field army, is what fills a castle's or a city's walls.
    ///
    /// Vanilla hands a fortification one free man off its notables' volunteers each day and bills the
    /// OWNER clan for it (<c>AutoRecruitmentExpenses</c>). RBM turns that around: the volunteer drip is
    /// switched off entirely (those volunteers stay in their lists for the player to recruit), and the
    /// garrison instead grows from the fief's treasury on a wealth-driven curve. A rich fief fields a
    /// deep garrison quickly; a broke frontier castle grows none until its books are in order. The lord's
    /// budget is not in the picture here -- a bonus feeding extra spawn rate on top of wealth is a later
    /// iteration; this pass is wealth-only.
    ///
    /// The curve, once a day per fortification:
    /// <list type="bullet">
    /// <item>BASIC REQUIREMENT -- the treasury must hold <see cref="GarrisonReserveDays"/>× the whole
    /// settlement's daily bill (garrison wage + maintenance, militia, administration, walls). Below that
    /// reserve, growth is zero: a fief that can barely cover its own running costs does not recruit.</item>
    /// <item>RATE -- <c>growth = wealth / (spawnCost × <see cref="GarrisonSpawnReserveMult"/>)</c>, one man
    /// for every few times his kit-value in the treasury, clamped to <see cref="GarrisonSpawnDailyMax"/> a
    /// day so even a flush fief fills over days rather than in one, and to the garrison's own size ceiling.</item>
    /// <item>SPEND -- each man is armed straight into the garrison roster and the treasury pays his kit's
    /// worth, stopping the moment arming the next would break the per-man reserve. In a town the coin
    /// reaches the armourers (citizen wealth); a castle, having no market, sources the kit from outside
    /// and the coin leaves the ledger.</item>
    /// </list>
    /// </summary>
    public static class GarrisonRecruitCost
    {
        /// <summary>Times the settlement's whole daily bill a fief must hold in reserve to recruit at all.</summary>
        public const int GarrisonReserveDays = 15;

        /// <summary>Times a man's equipment cost the treasury must hold to arm him -- also the divisor in the growth rate.</summary>
        public const int GarrisonSpawnReserveMult = 5;

        /// <summary>Most men a fief will recruit into its garrison in a single day, however rich it is.</summary>
        public const int GarrisonSpawnDailyMax = 4;

        /// <summary>
        /// Days of the garrison's OWN wage-and-maintenance bill the treasury must hold to sustain it. Below
        /// this the fief cannot carry a garrison this size and it is trimmed toward an affordable one (the
        /// men are still paid -- the wage bill falls back on the owner clan). Kept well under the recruit
        /// reserve (<see cref="GarrisonReserveDays"/>× the whole settlement's bill) so a dead band sits
        /// between: below the recruit reserve a fief stops growing, only below THIS does it reduce.
        /// </summary>
        public const int GarrisonKeepDays = 7;

        /// <summary>Men a fief trims from an over-strength garrison a day -- a slow reduction, not a collapse.</summary>
        public const int GarrisonShedPerDay = 2;

        public static bool IsEnabled
        {
            get { return SpoilsPool.IsEnabled && RBMConfig.RBMConfig.rbmCampaignEnabled; }
        }

        /// <summary>The fief's own treasury -- Pot B for a castle or a town alike.</summary>
        private static int FiefWealth(Settlement settlement)
        {
            return SettlementWealth.GetSettlementWealth(settlement);
        }

        /// <summary>
        /// The whole settlement's standing daily cost: the garrison (wage + maintenance), the militia, the
        /// administration and the walls. The basic-requirement reserve is a multiple of this -- a fief must
        /// be able to cover everything it already pays for, with room to spare, before it spends on new men.
        /// </summary>
        private static int FullDailyBill(Settlement settlement)
        {
            return GarrisonUpkeep.EstimateDailyBill(settlement)
                + MilitiaUpkeep.DailyMaintenanceBill(settlement)
                + AdministrativeUpkeep.EstimateDailyBill(settlement);
        }

        /// <summary>The representative garrison recruit -- the settlement culture's basic soldier.</summary>
        private static CharacterObject SpawnTroop(Settlement settlement)
        {
            return (settlement != null && settlement.Culture != null) ? settlement.Culture.BasicTroop : null;
        }

        /// <summary>What arming one garrison recruit costs -- his kit's full worth. Drives both the rate and the charge.</summary>
        private static int SpawnCost(Settlement settlement)
        {
            CharacterObject troop = SpawnTroop(settlement);
            return troop != null ? SpoilsPool.GetEquipmentValue(troop) : 0;
        }

        /// <summary>
        /// Switches off vanilla's volunteer-fed garrison auto-recruit. RBM grows the garrison from wealth
        /// instead (see <see cref="GrowGarrison"/>), so skipping this leaves the notables' volunteers in
        /// their lists for the player to recruit and takes the owner clan off the recruitment bill.
        /// </summary>
        [HarmonyPatch(typeof(GarrisonRecruitmentCampaignBehavior), "TickAutoRecruitmentGarrisonChange")]
        private static class GarrisonAutoRecruitDisablePatch
        {
            private static bool Prefix(Town town)
            {
                // Only take over when the wealth system is on and this is a real fortification; otherwise
                // let vanilla's volunteer auto-recruit run untouched.
                return !(IsEnabled && town != null && town.Settlement != null);
            }
        }

        /// <summary>
        /// Switches off vanilla's base garrison change too, so <see cref="GrowGarrison"/> is the SINGLE
        /// source of garrison growth. This path is near-inert in practice (a small rebellion-only bump),
        /// but leaving it would trickle men in outside the wealth gate; closing it keeps every garrison
        /// recruit answerable to the fief's treasury.
        /// </summary>
        [HarmonyPatch(typeof(GarrisonRecruitmentCampaignBehavior), "TickGarrisonChangeForTown")]
        private static class GarrisonBaseChangeDisablePatch
        {
            private static bool Prefix(Town town)
            {
                return !(IsEnabled && town != null && town.Settlement != null);
            }
        }

        /// <summary>
        /// Grows a fief's garrison for the day off its wealth. Called from the daily settlement pass, after
        /// the day's upkeep is paid, so recruiting spends genuine surplus and lands outside any village
        /// suppression window (garrisons are towns and castles only, so none applies, but the pass is the
        /// consistent home for a settlement-wealth write).
        /// </summary>
        public static void GrowGarrison(Settlement settlement)
        {
            GrowthCalc c = Compute(settlement);
            if (!c.Valid || c.Final == 0)
            {
                return;
            }

            if (c.Final > 0)
            {
                CharacterObject troop = SpawnTroop(settlement);
                if (troop == null)
                {
                    return;
                }
                int spawnCost = c.SpawnCost;

                int armed = 0;
                for (int i = 0; i < c.Final; i++)
                {
                    // Stop the moment arming the next man would break the per-man reserve -- the same floor
                    // the rate is built on, enforced man by man as the treasury drains.
                    if (FiefWealth(settlement) < spawnCost * GarrisonSpawnReserveMult)
                    {
                        break;
                    }
                    ArmOneGarrisonTroop(settlement, troop, spawnCost);
                    armed++;
                }

                if (EconomyLog.IsEnabled && armed > 0)
                {
                    EconomyLog.Log("GARRISON", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                        "recruited " + armed + "x " + (troop.Name != null ? troop.Name.ToString() : troop.StringId)
                        + " at " + spawnCost + "d each  ·  treasury now " + FiefWealth(settlement) + "d");
                }
                return;
            }

            // Final < 0: the fief's treasury cannot sustain a garrison this size, so it is trimmed down.
            // The men are still paid in full -- the wage bill falls back on the owner clan (see
            // GarrisonUpkeep) -- there are simply too many of them for the fief's own wealth to carry.
            int shed = ShedGarrison(settlement, -c.Final);
            if (EconomyLog.IsEnabled && shed > 0)
            {
                EconomyLog.Log("GARRISON", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                    "over-strength — " + shed + " stood down  ·  treasury " + FiefWealth(settlement) + "d");
            }
        }

        /// <summary>
        /// Removes up to <paramref name="count"/> non-hero men from the garrison, rawest levies (lowest
        /// tier) first -- a garrison larger than its fief can sustain is trimmed toward an affordable size.
        /// No kit value is refunded: the men stand down and go home, and nothing is minted back into a
        /// treasury that is already drained. Returns how many actually left.
        /// </summary>
        private static int ShedGarrison(Settlement settlement, int count)
        {
            MobileParty garrison = (settlement != null && settlement.Town != null) ? settlement.Town.GarrisonParty : null;
            if (garrison == null || garrison.MemberRoster == null || count <= 0)
            {
                return 0;
            }
            TroopRoster roster = garrison.MemberRoster;
            int removed = 0;
            while (removed < count)
            {
                int idx = -1;
                int lowestTier = int.MaxValue;
                for (int i = 0; i < roster.Count; i++)
                {
                    TroopRosterElement e = roster.GetElementCopyAtIndex(i);
                    if (e.Character == null || e.Character.IsHero || e.Number <= 0)
                    {
                        continue;
                    }
                    if (e.Character.Tier < lowestTier)
                    {
                        lowestTier = e.Character.Tier;
                        idx = i;
                    }
                }
                if (idx < 0)
                {
                    break;
                }
                roster.AddToCounts(roster.GetElementCopyAtIndex(idx).Character, -1);
                removed++;
            }
            return removed;
        }

        /// <summary>
        /// Arms one recruit into the garrison roster and pays his kit's worth out of the fief's treasury,
        /// creating the garrison party first if the fief has none yet (the way vanilla does).
        /// </summary>
        private static void ArmOneGarrisonTroop(Settlement settlement, CharacterObject troop, int cost)
        {
            if (settlement.Town.GarrisonParty == null)
            {
                settlement.AddGarrisonParty();
            }
            MobileParty garrison = settlement.Town.GarrisonParty;
            if (garrison == null || garrison.MemberRoster == null)
            {
                return;
            }
            garrison.MemberRoster.AddToCounts(troop, 1);

            int paid = SettlementWealth.Debit(settlement, cost, SettlementWealth.Source.GarrisonRecruit);
            // In a town the coin reaches the armourers who kitted him; a castle sources the gear from
            // outside its walls and the coin leaves the ledger.
            if (paid > 0 && settlement.IsTown)
            {
                SettlementWealth.CreditCitizens(settlement, paid, SettlementWealth.Source.GarrisonRecruit);
            }
        }

        /// <summary>
        /// The day's garrison-growth figure for a fortification, decomposed so the tick and the tooltip
        /// read from one source: the wealth rate, its daily cap, the size ceiling, and whether the reserve
        /// holds it at zero.
        /// </summary>
        private struct GrowthCalc
        {
            public bool Valid;          // the wealth system drives this fortification's garrison
            public int SpawnCost;       // kit value of one recruit
            public int Reserve;         // full daily bill × GarrisonReserveDays -- the recruit gate
            public bool Held;           // treasury below the recruit reserve, so no growth
            public int Rate;            // wealth / (SpawnCost × mult), before any cap
            public int Capped;          // rate clamped to the daily maximum
            public int Headroom;        // room left under the garrison's size ceiling
            public int AfterHeadroom;   // capped, clamped to headroom -- the grow amount
            public int KeepThreshold;   // garrison's own daily bill × GarrisonKeepDays -- the shed floor
            public int Shed;            // men leaving today when too poor to keep the garrison
            public int Final;           // signed net: >0 recruit, <0 shed, 0 hold
        }

        private static GrowthCalc Compute(Settlement settlement)
        {
            GrowthCalc c = new GrowthCalc();
            c.Headroom = int.MaxValue;
            if (!IsEnabled || settlement == null || settlement.Town == null
                || !(settlement.IsTown || settlement.IsCastle))
            {
                return c;
            }
            c.SpawnCost = SpawnCost(settlement);
            if (c.SpawnCost <= 0)
            {
                return c;
            }
            c.Valid = true;

            int wealth = FiefWealth(settlement);
            c.Reserve = FullDailyBill(settlement) * GarrisonReserveDays;
            c.Held = wealth < c.Reserve;

            c.Rate = wealth / (c.SpawnCost * GarrisonSpawnReserveMult);
            c.Capped = c.Rate > GarrisonSpawnDailyMax ? GarrisonSpawnDailyMax : c.Rate;

            int manCount = 0;
            MobileParty garrison = settlement.Town.GarrisonParty;
            if (garrison != null && garrison.Party != null && garrison.MemberRoster != null)
            {
                manCount = garrison.MemberRoster.TotalManCount;
                int headroom = garrison.Party.PartySizeLimit - manCount;
                c.Headroom = headroom > 0 ? headroom : 0;
            }
            c.AfterHeadroom = c.Capped < c.Headroom ? c.Capped : c.Headroom;
            if (c.AfterHeadroom < 0)
            {
                c.AfterHeadroom = 0;
            }

            int garrisonBill = GarrisonUpkeep.EstimateDailyBill(settlement);
            c.KeepThreshold = garrisonBill * GarrisonKeepDays;

            if (!c.Held)
            {
                // Enough reserve over the whole settlement's bill: recruit.
                c.Final = c.AfterHeadroom;
            }
            else if (garrisonBill > 0 && wealth < c.KeepThreshold && manCount > 0)
            {
                // The garrison costs more than the fief's treasury can sustain (it cannot hold even a week
                // of the garrison's own bill), so it is trimmed toward an affordable size. A dead band sits
                // between here and the recruit reserve, where the garrison simply holds.
                c.Shed = GarrisonShedPerDay < manCount ? GarrisonShedPerDay : manCount;
                c.Final = -c.Shed;
            }
            // else: between the shed floor and the recruit reserve -- hold, Final stays 0.
            return c;
        }

        /// <summary>
        /// The net garrison change the fief will apply today: positive to recruit (gated on the reserve,
        /// rate-and-size-capped), negative to shed when too poor to keep the garrison, zero when holding in
        /// the dead band. Shared by <see cref="GrowGarrison"/> and the settlement UI so the tick and the
        /// tooltip never disagree.
        /// </summary>
        public static int ProjectedGrowth(Settlement settlement)
        {
            return Compute(settlement).Final;
        }

        /// <summary>
        /// Makes the settlement UI's "garrison change" figure -- the number on the town overlay and its
        /// hover tooltip, and the clan/kingdom settlement lists -- report the wealth system rather than
        /// vanilla's now-disabled volunteer projection. Every one of those surfaces reads this one helper,
        /// so replacing it here fixes them all at once.
        /// </summary>
        [HarmonyPatch(typeof(Helpers.SettlementHelper), "GetGarrisonChangeExplainedNumber")]
        private static class GarrisonChangeTooltipPatch
        {
            private static bool Prefix(Town town, ref ExplainedNumber __result)
            {
                if (!IsEnabled || town == null || town.Settlement == null)
                {
                    return true; // leave vanilla's projection in place
                }
                __result = GetGarrisonChangeExplained(town);
                return false;
            }
        }

        /// <summary>
        /// Lays out the garrison-change breakdown the UI shows: the daily intake the treasury funds (its
        /// per-day cap named in the label when it binds), then the size ceiling and the reserve hold as
        /// their own lines, then vanilla's desertion on the end. The lines net to what
        /// <see cref="GrowGarrison"/> will actually apply.
        /// </summary>
        private static ExplainedNumber GetGarrisonChangeExplained(Town town)
        {
            ExplainedNumber en = new ExplainedNumber(0f, includeDescriptions: true);
            GrowthCalc c = Compute(town != null ? town.Settlement : null);

            if (c.Valid && c.Final < 0)
            {
                // The garrison is larger than the fief can sustain -- the men are paid, there are simply too
                // many of them for the treasury -- so it is trimmed toward an affordable size. The upkeep
                // reserve it is failing to hold is named so the player sees the figure that stops the trim.
                TextObject shedText = new TextObject("{=rbm_garr_shed}Over-strength — reducing (upkeep reserve {KEEP})");
                shedText.SetTextVariable("KEEP", c.KeepThreshold);
                en.Add(c.Final, shedText);
            }
            else if (c.Valid && c.Capped > 0)
            {
                // The daily intake, after the per-day cap (named in the label when it actually trims the
                // wealth rate) but before the size ceiling and the reserve, which follow.
                TextObject recruit;
                if (c.Rate > c.Capped)
                {
                    recruit = new TextObject("{=rbm_garr_rate_capped}Wealth recruitment (max {MAX}/day)");
                    recruit.SetTextVariable("MAX", GarrisonSpawnDailyMax);
                }
                else
                {
                    recruit = new TextObject("{=rbm_garr_rate}Wealth recruitment");
                }
                en.Add(c.Capped, recruit);

                // Fewer than the cap when the garrison is near its size ceiling.
                if (c.Capped > c.AfterHeadroom)
                {
                    en.Add(c.AfterHeadroom - c.Capped, new TextObject("{=rbm_garr_full}Garrison near capacity"));
                }

                // Held at zero until the treasury clears its reserve (its whole daily bill, many times over).
                if (c.Held && c.AfterHeadroom > 0)
                {
                    TextObject resText = new TextObject("{=rbm_garr_res}Held — treasury below reserve ({RES})");
                    resText.SetTextVariable("RES", c.Reserve);
                    en.Add(0 - c.AfterHeadroom, resText);
                }
            }

            // Desertion, as vanilla layers it on top of the change.
            if (town != null && town.GarrisonParty != null && Campaign.Current != null)
            {
                int desert = Campaign.Current.Models.PartyDesertionModel.GetTroopsToDesert(town.GarrisonParty).TotalManCount;
                if (desert > 0)
                {
                    en.Add(-desert, new TextObject("{=ojBJ3aTO}Desertion"));
                }
            }
            return en;
        }
    }
}
