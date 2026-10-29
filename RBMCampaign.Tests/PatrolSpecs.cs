using NUnit.Framework;

namespace RBMCampaign.Tests
{
    // A town or castle keeps a patrol out of its own funds: the Guard House, the Coastal Guard Edict and the
    // Patrols perk make it bigger and better, and the fief never raises or keeps one it cannot afford.
    public class PatrolSpecs
    {
        [Test]
        public void A_fief_with_nothing_to_boost_its_patrol_has_a_boost_of_one()
        {
            Assert.That(PatrolUpkeep.Boost(guardHouseLevel: 0, coastalEdict: false, patrolsPerk: false), Is.EqualTo(1f));
        }

        [Test]
        public void Each_Guard_House_level_adds_half_and_the_edict_and_perk_a_quarter_each()
        {
            Assert.That(PatrolUpkeep.Boost(2, false, false), Is.EqualTo(2f).Within(0.0001f));
            Assert.That(PatrolUpkeep.Boost(0, true, false), Is.EqualTo(1.25f).Within(0.0001f));
            Assert.That(PatrolUpkeep.Boost(3, true, true), Is.EqualTo(3f).Within(0.0001f));
        }

        [TestCase(1f, 1)]
        [TestCase(1.25f, 1)]
        [TestCase(1.5f, 2)]
        [TestCase(2f, 3)]
        [TestCase(2.5f, 4)]
        [TestCase(3f, 4)]
        public void A_bigger_boost_buys_a_better_patrol_template_up_to_level_four(float boost, int level)
        {
            Assert.That(PatrolUpkeep.TemplateLevel(boost), Is.EqualTo(level));
        }

        [Test]
        public void The_template_level_survives_float_rounding_of_the_boost()
        {
            Assert.That(PatrolUpkeep.TemplateLevel(PatrolUpkeep.Boost(1, false, false)), Is.EqualTo(2));
        }

        [TestCase(1f, 20)]
        [TestCase(2f, 40)]
        [TestCase(3.5f, 60)]
        [TestCase(0.25f, 12)]
        public void A_patrol_is_at_most_twenty_men_per_boost_capped_at_sixty_and_never_below_twelve(float boost, int maxMen)
        {
            Assert.That(PatrolUpkeep.MaxSize(boost), Is.EqualTo(maxMen));
        }

        [Test]
        public void The_patrol_is_as_big_as_its_daily_budget_pays_for()
        {
            // 15,000 pot x 1% x boost 2 = 300 a day, at 10 a man = 30 men; boost 2 allows up to 40.
            Assert.That(PatrolUpkeep.SizeAtPot(pot: 15000, budgetFraction: 0.01f, boost: 2f, perMan: 10), Is.EqualTo(30));
        }

        [Test]
        public void The_boost_scales_the_budget_as_well_as_the_ceiling()
        {
            Assert.That(PatrolUpkeep.SizeAtPot(20000, 0.01f, 1.5f, 10), Is.EqualTo(30));
        }

        [Test]
        public void A_rich_fief_is_held_to_its_ceiling_and_a_poor_one_still_fields_the_smallest_patrol()
        {
            Assert.That(PatrolUpkeep.SizeAtPot(10000000, 0.01f, 1f, 10), Is.EqualTo(20));
            Assert.That(PatrolUpkeep.SizeAtPot(100, 0.01f, 1f, 10), Is.EqualTo(PatrolUpkeep.MinSize));
            Assert.That(PatrolUpkeep.SizeAtPot(30000, 0.01f, 1f, perMan: 0), Is.EqualTo(PatrolUpkeep.MinSize));
        }

        [Test]
        public void The_keep_reserve_is_a_minimal_patrols_pay_for_fifteen_days()
        {
            Assert.That(PatrolUpkeep.KeepFloor(perManDaily: 10), Is.EqualTo(12 * 10 * 15));
        }

        [Test]
        public void A_fief_keeps_its_patrol_only_while_its_averaged_funds_hold_the_keep_reserve()
        {
            Assert.That(PatrolUpkeep.HoldsKeepReserve(averagePot: 1800, keepFloor: 1800), Is.True);
            Assert.That(PatrolUpkeep.HoldsKeepReserve(averagePot: 1799, keepFloor: 1800), Is.False);
        }

        [Test]
        public void A_fief_with_no_priced_patrol_keeps_none()
        {
            Assert.That(PatrolUpkeep.HoldsKeepReserve(averagePot: 1000000, keepFloor: 0), Is.False);
        }

        [Test]
        public void A_patrol_manned_from_the_garrison_is_raised_on_twice_the_keep_reserve()
        {
            Assert.That(PatrolUpkeep.HoldsRaiseReserve(averagePot: 3600, keepFloor: 1800, kitPaid: 0, isCastle: false), Is.True);
            Assert.That(PatrolUpkeep.HoldsRaiseReserve(averagePot: 3599, keepFloor: 1800, kitPaid: 0, isCastle: false), Is.False);
        }

        [Test]
        public void A_town_buying_men_also_needs_three_times_their_kit_and_a_castle_once()
        {
            Assert.That(PatrolUpkeep.HoldsRaiseReserve(3600 + 3000, 1800, kitPaid: 1000, isCastle: false), Is.True);
            Assert.That(PatrolUpkeep.HoldsRaiseReserve(3600 + 2999, 1800, kitPaid: 1000, isCastle: false), Is.False);
            Assert.That(PatrolUpkeep.HoldsRaiseReserve(3600 + 1000, 1800, kitPaid: 1000, isCastle: true), Is.True);
        }

        [Test]
        public void A_man_is_bought_when_the_funds_hold_five_times_his_price_and_still_pay_for_the_patrol()
        {
            Assert.That(PatrolUpkeep.CanAffordMan(pot: 5000, cost: 1000, keepFloor: 1800, sizeAfterPrice: 20, headCount: 20), Is.True);
        }

        [Test]
        public void No_man_is_bought_when_the_funds_hold_less_than_five_times_his_price()
        {
            Assert.That(PatrolUpkeep.CanAffordMan(4999, 1000, 1800, 20, 20), Is.False);
        }

        [Test]
        public void No_man_is_bought_whose_price_would_leave_the_funds_under_the_keep_reserve()
        {
            Assert.That(PatrolUpkeep.CanAffordMan(pot: 5000, cost: 1000, keepFloor: 4001, sizeAfterPrice: 20, headCount: 20), Is.False);
        }

        [Test]
        public void No_man_is_bought_that_the_next_trim_would_send_home()
        {
            Assert.That(PatrolUpkeep.CanAffordMan(pot: 50000, cost: 1000, keepFloor: 1800, sizeAfterPrice: 19, headCount: 20), Is.False);
        }

        [Test]
        public void A_man_with_no_price_is_never_bought()
        {
            Assert.That(PatrolUpkeep.CanAffordMan(50000, 0, 1800, 20, 20), Is.False);
        }

        [TestCase(120, 70)]
        [TestCase(50, 0)]
        [TestCase(30, 0)]
        public void A_patrol_never_takes_the_garrison_below_fifty_healthy_men(int healthy, int spare)
        {
            Assert.That(PatrolUpkeep.GarrisonSpare(healthy), Is.EqualTo(spare));
        }

        [Test]
        public void The_averaged_funds_move_a_seventh_of_the_way_to_the_live_funds_each_day()
        {
            Assert.That(PatrolUpkeep.StepAveragePot(average: 7000, livePot: 14000), Is.EqualTo(8000));
            Assert.That(PatrolUpkeep.StepAveragePot(average: 7000, livePot: 0), Is.EqualTo(6000));
        }
    }
}
