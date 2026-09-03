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

            // The main party never dies, so nothing else ever reclaims the purses of stacks that leave it
            // by a path that doesn't route through an upgrade, a transfer, or destruction -- a dismissal or
            // a donation. Sweep those orphans here, once a day, for the one immortal party; every other
            // party's orphans are collected when it is eventually destroyed (OnMobilePartyDestroyed).
            if (mobileParty == MobileParty.MainParty)
            {
                PruneOrphans(party);
            }

            DepositWageSpoils(party, roster);
        }

        private static void DepositWageSpoils(PartyBase party, TroopRoster roster)
        {
            // A bandit party keeps no war-chest and pays no wage, so there is nothing to skim into
            // spoils. Bandit troops in a lord's party are another matter -- the lord pays their wage,
            // so that stack draws its spoils like any other.
            if (party?.MobileParty != null && party.MobileParty.IsBandit)
            {
                return;
            }
            // A garrison is pulled out of the spoils economy entirely: its wages are the fief's to pay
            // (see GarrisonUpkeep) and its promotions are billed straight to the fief's treasury (see
            // SpoilsUpgradePatches), so it keeps no purse of its own to skim a wage into.
            if (party?.MobileParty != null && party.MobileParty.IsGarrison)
            {
                return;
            }
            PartyWageModel wageModel = Campaign.Current.Models.PartyWageModel;
            bool isMilitia = party?.MobileParty != null && party.MobileParty.IsMilitia;
            // A settlement patrol under the wealth-funded rework is billed like a field troop -- a full wage
            // into the purse, then kit-value maintenance out of it -- but the home settlement is the payer, not
            // a leader, so the whole of it happens inside PatrolUpkeep. With the rework off a patrol falls
            // through to the ordinary deposit below, as it did before (vanilla never billed anyone for it).
            bool isPatrol = PatrolUpkeep.IsEnabled && party?.MobileParty != null && party.MobileParty.IsPatrolParty;
            // A mercenary company's men are kept at double pay while the contract holds, so a stack under it
            // banks twice its wage into spoils. This is the second wage the company is charged for through the
            // finance model (see MercenaryContractPay) and the crown then reimburses, so the extra deposit is
            // backed by real coin, not minted. Player and AI mercenaries alike; read the payee clan off the
            // same chain the spoils are paid to.
            bool mercDouble = !isMilitia
                && MercenaryContractPay.CountsForMercWage(party?.MobileParty)
                && MercenaryContractPay.IsMercenaryClan(GetPartyPayee(party)?.Clan);
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
                if (isMilitia)
                {
                    // Militia are billed like a field troop -- a reduced wage into the purse, then kit-value
                    // maintenance out of it -- but the settlement is the payer, not the leader, so the whole
                    // of it happens inside MilitiaUpkeep rather than being banked by the caller here.
                    MilitiaUpkeep.PayMilitiaUpkeep(party.MobileParty, element.Character, element.Number, wage);
                    continue;
                }
                if (isPatrol)
                {
                    // Same shape as militia -- wage into the purse, maintenance out of it -- with the home
                    // settlement as payer, so the wage a patrol banks is one a fief actually paid for.
                    PatrolUpkeep.PayPatrolUpkeep(party.MobileParty, element.Character, element.Number, wage);
                    continue;
                }
                // The men skim their whole day's wage into their purse, cap or no cap. Spoils are a
                // closed loop now -- what lands here is spent on upgrades, food and drink, never handed
                // back to the owner as gold.
                int granted = wage;
                if (mercDouble)
                {
                    // Twice the wage, banked. The extra half is the crown's, paid through the leader.
                    granted = wage * 2;
                }
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
