using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
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
    /// Applied to every party on the daily tick, alongside the wage that fills the purse -- the wage is
    /// deposited first, so the day's pay can meet the day's upkeep before the leader is asked for a
    /// penny. A point of spoils is a gold piece, so the cost drains one-for-one. The leader's share is
    /// paid to a null receiver, the same way an upgrade's gold cost is spent, so no coin is minted --
    /// it only leaves his treasury.
    /// </remarks>
    public static partial class SpoilsPool
    {
        /// <summary>
        /// Charges the day's maintenance to a party: drains each stack's spoils for its share and bills
        /// the leader for whatever the purses fall short of. Returns the day's tally.
        /// </summary>
        public static MaintenanceResult ChargeMaintenance(PartyBase party)
        {
            return ComputeMaintenance(party, apply: true);
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
        /// The breakdown line both the clan finance tooltip and the party-wage tooltip draw: names the
        /// cost as maintenance and says how much of it the men's own spoils met, so the gold figure
        /// beside it reads as what the party is left to pay. Freshly built each call -- an ExplainedNumber
        /// keeps the reference, so a shared instance would have its numbers overwritten by the next party.
        /// </summary>
        public static TextObject BuildMaintenanceLineText(int total, int covered)
        {
            return new TextObject("{=RBM_SPOILS_017}Troop maintenance ({COVERED} of {TOTAL} met by spoils)")
                .SetTextVariable("COVERED", covered)
                .SetTextVariable("TOTAL", total);
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
                // The stack's cost, not one man's, so a small troop's fraction is not rounded away. Priced
                // off the mounted worth: a lancer's horse is a real part of what it costs to keep him.
                int cost = MathF.Round(fraction * GetEquipmentValueWithMount(element.Character) * element.Number);
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

            result.Shortfall = result.Total - result.Covered;
            if (apply && result.Shortfall > 0)
            {
                // The men could not meet the day's upkeep out of their purses; their keeper makes up the
                // difference from his own gold. Paid to a null receiver -- the coin leaves the field, not
                // moves to another purse -- the mirror of how an upgrade's gold cost is spent.
                Hero payer = GetPartyPayee(party);
                if (payer != null && payer.IsAlive)
                {
                    GiveGoldAction.ApplyBetweenCharacters(payer, null, result.Shortfall, true);
                }
            }

            if (apply && SpoilsLog.IsEnabled && party == PartyBase.MainParty && result.Total > 0)
            {
                SpoilsLog.Log("UPKEEP", party, SpoilsLog.Describe(party) + " paid " + result.Total
                    + " maintenance across " + stacksCharged + (stacksCharged == 1 ? " stack" : " stacks")
                    + " (spoils covered " + result.Covered + ", leader paid " + result.Shortfall + ")");
            }
            return result;
        }
    }
}
