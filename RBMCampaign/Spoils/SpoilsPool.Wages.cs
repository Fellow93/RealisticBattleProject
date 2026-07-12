using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// The share of a stack's daily wage that comes back as spoils: what the men lay out on their own
    /// kit, mending what the last march wore through. The gold the party pays is untouched -- this
    /// only says where some of it went.
    /// </summary>
    public static partial class SpoilsPool
    {
        /// <summary>
        /// A stack's wage is not all pay: part of it is what the men lay out on their own kit, mending
        /// what the last march wore through and replacing what they cannot mend. That part comes back
        /// as spoils. The gold the party pays is untouched -- this only says where some of it went.
        /// </summary>
        /// <remarks>
        /// Applied to every party, since every party pays wages. A point of spoils is a gold piece, so
        /// half a wage deposits half its gold and no conversion is needed. What the stack does not
        /// spend on its own upgrade it carries, to spend on bread and beer instead.
        /// </remarks>
        public static void OnDailyTickParty(MobileParty mobileParty)
        {
            if (!IsEnabled || mobileParty == null)
            {
                return;
            }
            PartyBase party = mobileParty.Party;
            TroopRoster roster = party?.MemberRoster;
            if (roster == null)
            {
                return;
            }

            DepositWageSpoils(party, roster);
            // After the day's wage has landed, so a stack cannot spill coin it is about to be handed.
            SpillSurplusToGold(party);
        }

        private static void DepositWageSpoils(PartyBase party, TroopRoster roster)
        {
            if (RBMConfig.RBMConfig.troopWageSpoilsFraction <= 0f)
            {
                return;
            }
            // A bandit party keeps no war-chest and pays no wage, so there is nothing to skim into
            // spoils. Bandit troops in a lord's party are another matter -- the lord pays their wage,
            // so that stack draws its spoils like any other.
            if (party?.MobileParty != null && party.MobileParty.IsBandit)
            {
                return;
            }
            PartyWageModel wageModel = Campaign.Current.Models.PartyWageModel;
            int grantedTotal = 0;
            int stacksPaid = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character.IsHero)
                {
                    continue;
                }
                // The stack's wage, not one man's, so a small troop's half-point is not rounded away.
                int wage = wageModel.GetCharacterWage(element.Character) * element.Number;
                int granted = MathF.Round(wage * RBMConfig.RBMConfig.troopWageSpoilsFraction);
                // Wage tops a stack's purse up to its cap and no further. Past the cap the men have
                // everything their upgrades and war chest need, so any more wage-spoils would only spill
                // straight back out as minted gold on the same tick -- the treasury pays the wage, then
                // has it conjured back. Let only battlefield loot, real earned value, ever carry a purse
                // over cap and feed the spill dividend; the wage the men do not skim stays spent, as a
                // wage is, rather than doubling as new gold.
                int room = MathF.Max(0, GetSpoilsCap(party, element.Character) - GetSpoils(party, element.Character));
                granted = MathF.Min(granted, room);
                if (granted <= 0)
                {
                    continue;
                }
                if (SpoilsLog.Verbose && party == PartyBase.MainParty)
                {
                    SpoilsLog.LogVerbose("WAGE", party, SpoilsLog.Describe(element.Character) + " x" + element.Number
                        + ": wage " + wage
                        + " -> +" + granted + " spoils (pool " + GetSpoils(party, element.Character)
                        + " -> " + (GetSpoils(party, element.Character) + granted) + ")");
                }
                AddSpoils(party, element.Character, granted);
                grantedTotal += granted;
                stacksPaid++;
            }

            // The party-level line, always: the day's wage-into-spoils, without naming stacks.
            if (SpoilsLog.IsEnabled && party == PartyBase.MainParty && grantedTotal > 0)
            {
                SpoilsLog.Log("WAGE", party, SpoilsLog.Describe(party) + " drew " + grantedTotal
                    + " spoils from the day's wages across " + stacksPaid + (stacksPaid == 1 ? " stack" : " stacks"));
            }
        }
    }
}
