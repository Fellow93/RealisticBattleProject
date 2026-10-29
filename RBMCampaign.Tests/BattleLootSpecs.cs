using NUnit.Framework;

namespace RBMCampaign.Tests
{
    // After a battle the victors strip the field: veterans pick first, kit far beneath a man is likelier
    // to be stepped over, and a man can only carry so much.
    public class BattleLootSpecs
    {
        private const float HalfOverlooked = 0.5f;

        [TestCase(3, 2, 0)]
        [TestCase(6, 0, 5)]
        [TestCase(1, 3, -3)]
        public void Kit_matching_a_troops_tier_has_no_gap(int troopTier, int itemTierIndex, int gap)
        {
            Assert.That(SpoilsPool.GetTierGap(itemTierIndex, troopTier), Is.EqualTo(gap));
        }

        [TestCase(0)]
        [TestCase(-2)]
        public void A_man_never_walks_past_kit_as_good_as_his_own(int tierGap)
        {
            Assert.That(SpoilsPool.GetNoticedPieces(40, tierGap, overlookChancePerTier: 1f, roll: 0.99f), Is.EqualTo(40));
        }

        [TestCase(1, 20)]
        [TestCase(2, 10)]
        [TestCase(3, 5)]
        public void The_further_beneath_him_the_kit_the_more_of_it_he_steps_over(int tierGap, int noticed)
        {
            Assert.That(SpoilsPool.GetNoticedPieces(40, tierGap, HalfOverlooked, roll: 0.99f), Is.EqualTo(noticed));
        }

        [Test]
        public void Even_far_beneath_him_some_kit_is_still_noticed_unless_overlooking_is_certain()
        {
            Assert.That(SpoilsPool.GetNoticeFraction(5, HalfOverlooked), Is.GreaterThan(0f));
            Assert.That(SpoilsPool.GetNoticeFraction(1, 1f), Is.EqualTo(0f));
        }

        [TestCase(1.5f, 0f)]
        [TestCase(-0.2f, 1f)]
        public void An_out_of_range_overlook_setting_is_held_to_never_or_always(float overlookChance, float noticedShare)
        {
            Assert.That(SpoilsPool.GetNoticeFraction(2, overlookChance), Is.EqualTo(noticedShare));
        }

        [Test]
        public void A_lone_piece_he_would_notice_a_quarter_of_the_time_is_rolled_for_not_dropped()
        {
            Assert.That(SpoilsPool.GetNoticedPieces(1, 2, HalfOverlooked, roll: 0.2f), Is.EqualTo(1));
            Assert.That(SpoilsPool.GetNoticedPieces(1, 2, HalfOverlooked, roll: 0.3f), Is.EqualTo(0));
        }

        [Test]
        public void A_stack_carries_as_many_pieces_as_its_men_have_arms_for()
        {
            Assert.That(SpoilsPool.GetCarryCapacity(menInStack: 12, piecesPerMan: 3), Is.EqualTo(36));
            Assert.That(SpoilsPool.GetCarryCapacity(menInStack: 12, piecesPerMan: -1), Is.EqualTo(0));
        }

        [Test]
        public void Stacks_of_equal_rank_split_the_heap_by_head_count()
        {
            int[] shares = SpoilsPool.SplitAmongPeers(30, men: new[] { 10, 20 }, room: new[] { 100, 100 });
            Assert.That(shares, Is.EqualTo(new[] { 10, 20 }));
        }

        [Test]
        public void What_a_full_handed_stack_cannot_take_passes_to_its_peers()
        {
            int[] shares = SpoilsPool.SplitAmongPeers(30, men: new[] { 10, 20 }, room: new[] { 2, 100 });
            Assert.That(shares, Is.EqualTo(new[] { 2, 28 }));
        }

        [Test]
        public void No_stack_takes_more_than_its_arms_can_carry()
        {
            int[] shares = SpoilsPool.SplitAmongPeers(100, men: new[] { 10, 10 }, room: new[] { 5, 7 });
            Assert.That(shares, Is.EqualTo(new[] { 5, 7 }));
        }

        [Test]
        public void Rounding_never_leaves_a_piece_lying_while_someone_has_room()
        {
            int[] shares = SpoilsPool.SplitAmongPeers(10, men: new[] { 1, 1, 1 }, room: new[] { 50, 50, 50 });
            Assert.That(shares[0] + shares[1] + shares[2], Is.EqualTo(10));
        }
    }
}
