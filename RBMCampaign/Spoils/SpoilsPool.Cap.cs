using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// A stack holds only so many days' keep in its purse: a set number of days' worth of its wage and
    /// its field maintenance together. This ceiling governs how much a stack keeps before its upkeep
    /// spends the rest on food and drink.
    /// </summary>
    public static partial class SpoilsPool
    {
        /// <summary>
        /// The most a stack can usefully hold: a configured number of days' keep -- its daily wage and
        /// its daily field maintenance together, times the day count. Priced the same for every tier;
        /// a veteran's dearer wage and kit already make his days' keep the deeper purse, so no separate
        /// war chest is needed. A top-tier troop with no upgrade to save for is held to the same rule.
        /// </summary>
        public static int GetSpoilsCap(PartyBase party, CharacterObject character)
        {
            int stackSize = GetStackSize(party, character);
            if (stackSize <= 0)
            {
                return 0;
            }
            int days = RBMConfig.RBMConfig.troopSpoilsCapDays;
            if (days <= 0)
            {
                return 0;
            }
            // Both priced for the whole stack, not one man, so a small troop's fraction is not rounded
            // away -- the same granularity the daily wage skim and the maintenance charge use.
            int dailyWage = Campaign.Current.Models.PartyWageModel.GetCharacterWage(character) * stackSize;
            int dailyMaintenance = DailyMaintenanceCost(character, stackSize);
            return (dailyWage + dailyMaintenance) * days;
        }

        /// <summary>
        /// Who a party's spoils gold is paid to: its owner if one is alive, else the hero leading it.
        /// Null when no one can be paid.
        /// </summary>
        public static Hero GetPartyPayee(PartyBase party)
        {
            if (party == null)
            {
                return null;
            }
            Hero payee = (party.Owner != null && party.Owner.IsAlive) ? party.Owner : party.LeaderHero;
            return (payee != null && payee.IsAlive) ? payee : null;
        }

        /// <summary>
        /// A companion collects spoils like a soldier but holds no purse: his share is paid straight into the
        /// party's gold (its <see cref="GetPartyPayee"/>) and no leader cut is skimmed from it -- he is the clan's
        /// own, not a hired sword. The payee himself is never a companion by this test, so the leader keeps
        /// working exactly as before. False when there is no living payee to pay, so companions collect only where
        /// there is someone to receive it.
        /// </summary>
        public static bool IsCompanionStack(CharacterObject character, Hero payee)
        {
            return character != null && character.IsHero
                && payee != null && character.HeroObject != null && character.HeroObject != payee;
        }
    }
}
