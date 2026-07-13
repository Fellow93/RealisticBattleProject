using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>What the day's maintenance came to for one party: the whole of it, and how it was met.</summary>
    public struct MaintenanceResult
    {
        /// <summary>The full daily cost of keeping the party's stacks in the field.</summary>
        public int Total;

        /// <summary>How much of it the stacks paid out of their own spoils.</summary>
        public int Covered;

        /// <summary>What the purses could not meet, left to the party leader's gold.</summary>
        public int Shortfall;
    }

    /// <summary>
    /// The daily cost of keeping a soldier in the field, drawn against the whole worth of his kit --
    /// his gear, his horse and its harness alike. A share of that worth is spent each day mending what
    /// the march wore through and replacing what is past mending. The men pay it out of their own
    /// spoils first; whatever the purse cannot cover falls to the party leader, out of his gold.
    /// </summary>
    /// <remarks>
    /// Charged once per clan per day, off the clan finance model's apply pass
    /// (<see cref="MaintenanceFinanceLine"/>): every party the clan's leader keeps has its stacks' purses
    /// drained for their share, and whatever the purses cannot meet is folded into the clan's daily gold
    /// change. Routing the shortfall through the finance number rather than a separate transfer means it
    /// shows in the Daily Gold Change message and the finance breakdown, and the leader pays it through the
    /// very channel wages run through. A point of spoils is a gold piece, so the cost drains one-for-one.
    /// </remarks>
    public static partial class SpoilsPool
    {
        /// <summary>
        /// The daily cost of keeping one stack of <paramref name="number"/> men of this troop in the
        /// field: a share of the whole worth of their kit, horse and harness included. The stack's cost,
        /// not one man's, so a small troop's fraction is not rounded away. Shared by the daily charge and
        /// the recruit seed so both price a day's upkeep the same way.
        /// </summary>
        private static int DailyMaintenanceCost(CharacterObject character, int number)
        {
            float fraction = RBMConfig.RBMConfig.troopMaintenanceFraction;
            if (fraction <= 0f || number <= 0)
            {
                return 0;
            }
            return MathF.Round(fraction * GetEquipmentValueWithMount(character) * number);
        }

        /// <summary>
        /// Charges a clan the day's maintenance across every party its leader keeps: drains each stack's
        /// spoils for its share and returns the tally, whose shortfall the caller folds into the clan's
        /// daily gold change. Run once per clan per day from the finance model's apply pass, so the
        /// deduction shows in the Daily Gold Change message and the leader truly pays it. Passed
        /// <paramref name="apply"/> false it projects the same tally without touching a purse, for the
        /// finance breakdown the display pass draws.
        /// </summary>
        public static MaintenanceResult ChargeClanMaintenance(Clan clan, bool apply)
        {
            MaintenanceResult total = default(MaintenanceResult);
            if (!IsEnabled || RBMConfig.RBMConfig.troopMaintenanceFraction <= 0f || clan == null)
            {
                return total;
            }
            // The clan's own war parties -- the very ones whose wages it already pays (the game charges
            // wages off this same list). Maintenance mirrors the wage: charged for the parties the leader
            // keeps and folded into the one daily gold change he settles. Caravans keep their own purse and
            // pay their own way, so they are left out here as they are from the leader's wage bill.
            foreach (WarPartyComponent warParty in clan.WarPartyComponents)
            {
                MobileParty mobileParty = warParty?.MobileParty;
                if (mobileParty == null || !mobileParty.IsActive)
                {
                    continue;
                }
                MaintenanceResult m = ComputeMaintenance(mobileParty.Party, apply);
                total.Total += m.Total;
                total.Covered += m.Covered;
                total.Shortfall += m.Shortfall;
            }
            return total;
        }

        /// <summary>
        /// Seeds a freshly recruited stack's purse with several days' maintenance, so a man drawn from a
        /// settlement arrives with his kit in order and a little put by against the coming march rather
        /// than penniless. Added on top of whatever the stack already carries. No gold changes hands --
        /// the recruit brings the spoils with him.
        /// </summary>
        public static void SeedRecruitMaintenance(PartyBase party, CharacterObject character, int amount)
        {
            if (!IsEnabled || party == null || character == null || character.IsHero || amount <= 0 || IsExemptParty(party))
            {
                return;
            }
            // A bandit party keeps no war-chest; it is charged no maintenance, so it is seeded none.
            if (party.MobileParty != null && party.MobileParty.IsBandit)
            {
                return;
            }
            int days = RBMConfig.RBMConfig.recruitMaintenanceDays;
            if (days <= 0)
            {
                return;
            }
            int seed = DailyMaintenanceCost(character, amount) * days;
            if (seed <= 0)
            {
                return;
            }
            AddSpoils(party, character, seed);
            if (SpoilsLog.IsEnabled && party == PartyBase.MainParty)
            {
                SpoilsLog.Log("RECRUIT", party, SpoilsLog.Describe(party) + " recruited "
                    + SpoilsLog.Describe(character) + " x" + amount + "; seeded " + seed + " spoils ("
                    + days + " days' maintenance)");
            }
        }

        /// <summary>
        /// A lord's party mustering from a village or town: the AI recruit path, which alone carries the
        /// settlement and the recruiter. Prisoners pressed into service and volunteers picked up on the
        /// road carry a null settlement and bring nothing -- only a proper muster from a settlement is
        /// seeded. The player's own muster does not come here (it fires <see cref="OnUnitRecruited"/>
        /// instead), so the main party is passed over to keep the two paths from seeding one recruit twice.
        /// </summary>
        public static void OnTroopRecruited(Hero recruiterHero, Settlement recruitmentSettlement,
            Hero recruitmentSource, CharacterObject troop, int amount)
        {
            if (recruitmentSettlement == null || !(recruitmentSettlement.IsVillage || recruitmentSettlement.IsTown))
            {
                return;
            }
            PartyBase party = recruiterHero?.PartyBelongedTo?.Party;
            if (party == null || party == PartyBase.MainParty)
            {
                return;
            }
            SeedRecruitMaintenance(party, troop, amount);
        }

        /// <summary>
        /// The player's own muster from a settlement's notables, one man at a time into the main party --
        /// the recruit-screen path, which carries neither settlement nor party. The screen only opens
        /// inside a village or town, so the main party's current settlement stands in for the "from a
        /// settlement" gate. Prisoners pressed into service and mercenaries hired in a tavern reach this
        /// event too, but only a recruit made while the party sits in a village or town is seeded.
        /// </summary>
        public static void OnUnitRecruited(CharacterObject character, int amount)
        {
            Settlement settlement = MobileParty.MainParty?.CurrentSettlement;
            if (settlement == null || !(settlement.IsVillage || settlement.IsTown))
            {
                return;
            }
            SeedRecruitMaintenance(PartyBase.MainParty, character, amount);
        }

        /// <summary>
        /// The day's maintenance as it would fall right now, without touching a purse or a treasury: for
        /// the finance breakdown and the party-wage tooltip, which read the coming day rather than move it.
        /// </summary>
        public static MaintenanceResult ProjectDailyMaintenance(PartyBase party)
        {
            return ComputeMaintenance(party, apply: false);
        }

        /// <summary>
        /// Writes the day's maintenance into a finance/wage breakdown as two lines -- the whole cost, then
        /// the share the men's own spoils met as an offsetting credit -- so the two net to just the coin the
        /// party is left to pay while both stay on the page. Drawn this way rather than as a single net line
        /// because an <see cref="ExplainedNumber"/> drops a zero-valued line: a stack whose spoils cover its
        /// upkeep in full would otherwise vanish from the tooltip, hiding the maintenance the player wanted
        /// to see. <paramref name="expenseSign"/> is -1 where the number counts expenses as negative (the
        /// clan finance change) and +1 where it counts costs as positive (the party wage), so the same
        /// tally reads correctly on either. Freshly built each call: an ExplainedNumber keeps the reference,
        /// so a shared TextObject would have its number overwritten by the next party.
        /// </summary>
        public static void AddMaintenanceBreakdown(ref ExplainedNumber breakdown, MaintenanceResult maintenance, float expenseSign)
        {
            if (maintenance.Total <= 0)
            {
                return;
            }
            breakdown.Add(expenseSign * maintenance.Total, new TextObject("{=RBM_SPOILS_017}Troop maintenance"));
            if (maintenance.Covered > 0)
            {
                breakdown.Add(-expenseSign * maintenance.Covered, new TextObject("{=RBM_SPOILS_020}Maintenance paid from troop spoils"));
            }
        }

        private static MaintenanceResult ComputeMaintenance(PartyBase party, bool apply)
        {
            MaintenanceResult result = default(MaintenanceResult);
            float fraction = RBMConfig.RBMConfig.troopMaintenanceFraction;
            if (!IsEnabled || fraction <= 0f || IsExemptParty(party))
            {
                return result;
            }
            TroopRoster roster = party.MemberRoster;
            if (roster == null)
            {
                return result;
            }
            // A bandit party keeps no war-chest and its leader no treasury, so there is nothing to bill.
            // Bandit troops in a lord's party still cost their keeper, so that party is charged as any other.
            if (party.MobileParty != null && party.MobileParty.IsBandit)
            {
                return result;
            }

            int stacksCharged = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character.IsHero || element.Number <= 0)
                {
                    continue;
                }
                // Priced off the mounted worth: a lancer's horse is a real part of what it costs to keep him.
                int cost = DailyMaintenanceCost(element.Character, element.Number);
                if (cost <= 0)
                {
                    continue;
                }
                int fromSpoils = MathF.Min(GetSpoils(party, element.Character), cost);
                if (apply && fromSpoils > 0)
                {
                    AddSpoils(party, element.Character, -fromSpoils);
                }
                result.Total += cost;
                result.Covered += fromSpoils;
                stacksCharged++;
                if (apply && SpoilsLog.Verbose && party == PartyBase.MainParty)
                {
                    SpoilsLog.LogVerbose("UPKEEP", party, SpoilsLog.Describe(element.Character) + " x" + element.Number
                        + ": maintenance " + cost + " (spoils " + fromSpoils + ", leader " + (cost - fromSpoils) + ")");
                }
            }

            // Only the purses are moved here; the shortfall the men cannot meet is left for the clan's
            // daily gold change to carry (see ChargeClanMaintenance), so the leader pays it once, through
            // the finance number rather than a separate transfer.
            result.Shortfall = result.Total - result.Covered;

            // The coin spent mending and replacing worn kit is spent somewhere: a share of the day's
            // maintenance settles into the Prosperity of the nearest fortress town -- a city or castle,
            // never a village -- scaled by the same per-gold rate as all other settlement spending.
            if (apply && result.Total > 0)
            {
                CreditMaintenanceProsperity(party, result.Total);
            }

            if (apply && SpoilsLog.IsEnabled && party == PartyBase.MainParty && result.Total > 0)
            {
                SpoilsLog.Log("UPKEEP", party, SpoilsLog.Describe(party) + " owed " + result.Total
                    + " maintenance across " + stacksCharged + (stacksCharged == 1 ? " stack" : " stacks")
                    + " (spoils covered " + result.Covered + ", " + result.Shortfall + " to clan gold)");
            }
            return result;
        }

        /// <summary>
        /// Pours a configurable share of a party's day's maintenance into the Prosperity of the nearest
        /// fortification -- the city or castle where its coin is spent mending and replacing kit -- never a
        /// village. Routed through <see cref="TroopUpkeep.CreditSettlement"/> so the same
        /// settlementProsperityPerGoldSpent rate that governs every other kind of settlement spending scales
        /// it too. A party sitting inside a fortress feeds that fortress directly; one out in the field
        /// feeds whichever city or castle lies nearest.
        /// </summary>
        private static void CreditMaintenanceProsperity(PartyBase party, int maintenanceTotal)
        {
            float fraction = RBMConfig.RBMConfig.maintenanceProsperityFraction;
            if (fraction <= 0f || maintenanceTotal <= 0)
            {
                return;
            }
            MobileParty mobileParty = party?.MobileParty;
            if (mobileParty == null)
            {
                return;
            }
            int toProsperity = MathF.Round(fraction * maintenanceTotal);
            if (toProsperity <= 0)
            {
                return;
            }
            // A garrison or a party stopped in a fortress enriches the place it sits in; a marching party
            // enriches the nearest city or castle. FindNearestFortificationToMobileParty ranges over towns
            // and castles alike but never villages, exactly the "castle or city, not village" we want.
            Settlement settlement = mobileParty.CurrentSettlement;
            if (settlement == null || !(settlement.IsTown || settlement.IsCastle))
            {
                settlement = SettlementHelper.FindNearestFortificationToMobileParty(mobileParty,
                    MobileParty.NavigationType.Default, null);
            }
            if (settlement == null)
            {
                return;
            }
            TroopUpkeep.CreditSettlement(settlement, toProsperity);

            if (SpoilsLog.IsEnabled && party == PartyBase.MainParty)
            {
                float gain = toProsperity * RBMConfig.RBMConfig.settlementProsperityPerGoldSpent;
                SpoilsLog.Log("UPKEEP", party, SpoilsLog.Describe(party) + " maintenance "
                    + maintenanceTotal + " -> " + toProsperity + " gold to " + settlement.Name
                    + " (+" + gain.ToString("0.00") + " prosperity)");
            }
        }
    }
}
