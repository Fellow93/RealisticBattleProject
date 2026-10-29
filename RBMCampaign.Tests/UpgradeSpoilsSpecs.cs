using NUnit.Framework;

namespace RBMCampaign.Tests
{
    // When men upgrade, they take their share of the old stack's spoils purse to the troop they become.
    public class UpgradeSpoilsSpecs
    {
        [Test]
        public void A_whole_stack_upgrading_carries_the_whole_purse()
        {
            Assert.That(SpoilsPool.GetCarriedSpoils(poolAfterSpend: 1234, count: 10, stackSizeBefore: 10), Is.EqualTo(1234));
        }

        [Test]
        public void Upgrading_men_carry_the_same_share_of_the_purse_as_they_are_of_the_stack()
        {
            Assert.That(SpoilsPool.GetCarriedSpoils(poolAfterSpend: 1000, count: 3, stackSizeBefore: 10), Is.EqualTo(300));
        }

        [Test]
        public void A_partial_share_is_rounded_down_so_the_men_staying_behind_keep_the_odd_coin()
        {
            Assert.That(SpoilsPool.GetCarriedSpoils(poolAfterSpend: 100, count: 1, stackSizeBefore: 3), Is.EqualTo(33));
        }

        [TestCase(0, 3, 10)]
        [TestCase(-50, 3, 10)]
        [TestCase(1000, 0, 10)]
        [TestCase(1000, 3, 0)]
        public void Nothing_is_carried_from_an_empty_purse_or_by_no_men(int pool, int count, int stackSizeBefore)
        {
            Assert.That(SpoilsPool.GetCarriedSpoils(pool, count, stackSizeBefore), Is.EqualTo(0));
        }

        [Test]
        public void A_deep_purse_in_a_big_stack_does_not_overflow()
        {
            Assert.That(SpoilsPool.GetCarriedSpoils(poolAfterSpend: 2000000000, count: 900, stackSizeBefore: 1000),
                Is.EqualTo(1800000000));
        }

        [Test]
        public void Upgrading_men_never_carry_more_than_the_purse_holds()
        {
            for (int stack = 1; stack <= 40; stack++)
            {
                for (int count = 1; count <= stack; count++)
                {
                    int carried = SpoilsPool.GetCarriedSpoils(997, count, stack);
                    Assert.That(carried, Is.InRange(0, 997), count + " of " + stack);
                }
            }
        }
    }
}
