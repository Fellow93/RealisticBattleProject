using System.Collections.Generic;
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
        /// A party whose stacks' wage someone else pays (militia, a settlement patrol: their settlement), so the wage pass
        /// hands each stack's wage to the biller instead of banking it from nothing.
        /// </summary>
        internal interface IWageBiller
        {
            bool Bills(MobileParty party);
            void BillStack(MobileParty party, CharacterObject character, int number, int wage);
            void FinishParty(MobileParty party);
        }

        private static readonly List<IWageBiller> _wageBillers = new List<IWageBiller>();

        /// <summary>Registers a biller once; called from <see cref="RBMCampaignSessionBehavior"/> at session launch.</summary>
        internal static void RegisterWageBiller(IWageBiller biller)
        {
            if (biller != null && !_wageBillers.Contains(biller))
            {
                _wageBillers.Add(biller);
            }
        }

        private static IWageBiller WageBillerFor(MobileParty party)
        {
            if (party == null)
            {
                return null;
            }
            for (int i = 0; i < _wageBillers.Count; i++)
            {
                if (_wageBillers[i].Bills(party))
                {
                    return _wageBillers[i];
                }
            }
            return null;
        }

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
            // A garrison banks its wage like any other troop. The fief pays that wage out of its treasury
            // (owner backstop -- see GarrisonUpkeep), so the purse holds real coin, and what the men do
            // not spend on their own promotions they drink and spend where they stand, which hands it back
            // to the settlement that paid it (TroopUpkeep / TroopMarketFeedback.CreditLocalPurse).
            // Kit maintenance is the one leg still billed to the fief directly rather than the purse
            // (GarrisonUpkeep.ChargeMaintenance); the garrison is no WarPartyComponent, so the clan
            // maintenance pass never touches it and nothing is charged twice.
            PartyWageModel wageModel = Campaign.Current.Models.PartyWageModel;
            // A party its settlement pays (militia: MilitiaUpkeep; a settlement patrol: its home fief, see
            // PatrolUpkeep) has its wage billed rather than banked from nothing here. With no biller, the
            // plain deposit below.
            IWageBiller biller = WageBillerFor(party?.MobileParty);
            // A mercenary company's men are kept at double pay while the contract holds, so a stack under it
            // banks twice its wage into spoils. This is the second wage the company is charged for through the
            // finance model (see MercenaryContractPay) and the crown then reimburses, so the extra deposit is
            // backed by real coin, not minted. Player and AI mercenaries alike; read the payee clan off the
            // same chain the spoils are paid to.
            bool mercDouble = biller == null
                && MercenaryContractPay.CountsForMercWage(party?.MobileParty)
                && MercenaryContractPay.IsMercenaryClan(GetPartyPayee(party)?.Clan);
            // An idle mercenary company pays its men half wages (the cut is made on the party's total wage, which
            // this per-stack sum does not read), so its stacks bank half: they keep exactly what they are paid.
            float companyWageFactor = MercenaryCompanies.WageFactor(party?.MobileParty);
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
                if (companyWageFactor < 1f)
                {
                    wage = MathF.Round(wage * companyWageFactor);
                }
                if (biller != null)
                {
                    biller.BillStack(party.MobileParty, element.Character, element.Number, wage);
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

            if (biller != null)
            {
                biller.FinishParty(party.MobileParty);
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
