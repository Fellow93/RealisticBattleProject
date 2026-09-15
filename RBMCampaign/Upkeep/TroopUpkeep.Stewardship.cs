using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// The stewardship a leader earns from housekeeping rather than the field: men who provision
    /// themselves out of their own purses, a baggage train that carries food for a long march, and
    /// enough spare horses to put the footmen in the saddle. None of it is fighting, all of it is the
    /// quiet business of keeping an army supplied and moving -- which is what stewardship is.
    /// </summary>
    /// <remarks>
    /// Provisioning is granted where the spending happens, off the food and luxury spend the upkeep
    /// tick already tallies; the food-reserve and mounted-footmen grants are read once a day off the
    /// party's stores and its baggage train. All three go to the party leader, and each is gated by its
    /// own config rate so any one can be turned off without the others.
    /// </remarks>
    public static partial class TroopUpkeep
    {
        /// <summary>Days of stores at which each further step of food-reserve stewardship is earned.</summary>
        private static readonly int[] FoodReserveDayTiers = { 10, 20, 30, 40, 50 };

        /// <summary>The hero who earns for a party's housekeeping: the one who leads it, if a live hero does.</summary>
        private static Hero StewardOf(MobileParty mobileParty)
        {
            Hero leader = mobileParty?.LeaderHero;
            return (leader != null && leader.IsAlive) ? leader : null;
        }

        private static void AddStewardXp(Hero hero, float xp)
        {
            if (hero == null || xp <= 0f)
            {
                return;
            }
            hero.AddSkillXp(DefaultSkills.Steward, xp);
        }

        /// <summary>
        /// Stewardship for a camp that feeds and indulges itself out of the men's own purses, scaled by
        /// what they laid out. Called off the food and luxury spend the upkeep tick already gathered, so
        /// carousing -- which is not thrift -- never reaches it.
        /// </summary>
        public static void GrantProvisioningXp(MobileParty mobileParty, int spoilsSpent)
        {
            if (spoilsSpent <= 0)
            {
                return;
            }
            float rate = RBMConfig.RBMConfig.stewardProvisioningXpPerSpoils;
            if (rate <= 0f)
            {
                return;
            }
            AddStewardXp(StewardOf(mobileParty), spoilsSpent * rate);
        }

        /// <summary>
        /// The once-a-day grants that read off a party's standing state rather than an event: the depth
        /// of its food stores and the spare horses its footmen could ride.
        /// </summary>
        public static void OnDailyTickParty(MobileParty mobileParty)
        {
            Hero steward = StewardOf(mobileParty);
            if (steward == null)
            {
                return;
            }
            AddStewardXp(steward, FoodReserveXp(mobileParty));
            AddStewardXp(steward, MountedFootmenXp(mobileParty));
        }

        /// <summary>
        /// Stewardship for the depth of a party's larder: one step per threshold of days its stores
        /// would last at the present rate of eating, times its size per hundred men. A party that is not
        /// eating into its stores at all -- fed faster than it consumes -- is as well-supplied as one
        /// could be, so it clears every threshold.
        /// </summary>
        private static float FoodReserveXp(MobileParty mobileParty)
        {
            float perTier = RBMConfig.RBMConfig.stewardFoodReserveXpPerTier;
            if (perTier <= 0f)
            {
                return 0f;
            }
            int men = (mobileParty.MemberRoster != null) ? mobileParty.MemberRoster.TotalManCount : 0;
            if (men <= 0)
            {
                return 0f;
            }
            float change = mobileParty.FoodChange;
            float days;
            if (change < 0f)
            {
                days = mobileParty.Food / -change;
            }
            else
            {
                // Not depleting: effectively bottomless if there is anything in the sacks at all, and
                // nothing to reward if the party is marching on empty stores it just is not eating yet.
                days = mobileParty.Food > 0f ? float.MaxValue : 0f;
            }
            int tiers = 0;
            for (int i = 0; i < FoodReserveDayTiers.Length; i++)
            {
                if (days >= FoodReserveDayTiers[i])
                {
                    tiers++;
                }
            }
            if (tiers <= 0)
            {
                return 0f;
            }
            // Per tier, per hundred men: victualling a host for a span is a greater feat than a warband.
            return tiers * perTier * (men / 100f);
        }

        /// <summary>
        /// Stewardship for every spare mount the baggage train carries that a footman could ride --
        /// rideable horses in the item roster, capped at how many men are on foot, which is exactly the
        /// count the map-speed model turns into its "footmen on horses" bonus. Linear in that count, so
        /// it rises with the size of the train a leader keeps his infantry horsed from.
        /// </summary>
        private static float MountedFootmenXp(MobileParty mobileParty)
        {
            float perHorse = RBMConfig.RBMConfig.stewardMountedFootmanXpPerHorse;
            if (perHorse <= 0f)
            {
                return 0f;
            }
            PartyBase party = mobileParty.Party;
            if (party == null)
            {
                return 0f;
            }
            int spareMounts = (mobileParty.ItemRoster != null) ? mobileParty.ItemRoster.NumberOfMounts : 0;
            int usable = MathF.Min(party.NumberOfMenWithoutHorse, spareMounts);
            if (usable <= 0)
            {
                return 0f;
            }
            return usable * perHorse;
        }
    }
}
