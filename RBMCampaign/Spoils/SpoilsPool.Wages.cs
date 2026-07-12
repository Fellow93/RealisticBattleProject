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

            // After the day's wage has filled the purse, the day's upkeep draws it back down: the pay
            // meets the maintenance before the leader is asked to cover any shortfall out of his gold.
            ChargeMaintenance(party);
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
                // The men skim their whole share of the day's wage into their purse, cap or no cap.
                // Spoils are a closed loop now -- what lands here is spent on upgrades, food and drink,
                // never handed back to the owner as gold.
                int granted = MathF.Round(wage * RBMConfig.RBMConfig.troopWageSpoilsFraction);
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
