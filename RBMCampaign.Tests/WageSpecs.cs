using NUnit.Framework;

namespace RBMCampaign.Tests
{
    // A regular troop's daily wage comes off the historical pay table by tier.
    public class WageSpecs
    {
        [TestCase(1, 20)]
        [TestCase(2, 30)]
        [TestCase(3, 40)]
        [TestCase(4, 60)]
        [TestCase(5, 120)]
        [TestCase(6, 240)]
        public void A_foot_soldier_is_paid_by_his_tier(int tier, int dailyWage)
        {
            Assert.That(TierBasedWageModel.WageForTier(tier, mounted: false), Is.EqualTo(dailyWage));
        }

        [TestCase(1)]
        [TestCase(3)]
        [TestCase(5)]
        public void A_horseman_draws_what_a_footman_one_tier_above_him_does(int tier)
        {
            Assert.That(TierBasedWageModel.WageForTier(tier, mounted: true),
                Is.EqualTo(TierBasedWageModel.WageForTier(tier + 1, mounted: false)));
        }

        [Test]
        public void Tier_zero_recruits_are_paid_as_tier_one_because_nobody_serves_for_nothing()
        {
            Assert.That(TierBasedWageModel.WageForTier(0, mounted: false), Is.EqualTo(20));
            Assert.That(TierBasedWageModel.WageForTier(0, mounted: true), Is.EqualTo(30));
        }

        [Test]
        public void Tiers_above_the_table_are_paid_the_top_rate()
        {
            Assert.That(TierBasedWageModel.WageForTier(9, mounted: false), Is.EqualTo(240));
            Assert.That(TierBasedWageModel.WageForTier(9, mounted: true), Is.EqualTo(480));
        }
    }
}
