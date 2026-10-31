using NUnit.Framework;

namespace RBMAI.Tests
{
    // Which of two cavalry groups a charging foot block keeps going for.
    public class ChargeTargetChoiceSpecs
    {
        [Test]
        public void A_second_cavalry_group_of_similar_threat_does_not_take_over()
        {
            Assert.That(ChargeTargetChoice.KeepsCurrentMounted(100f, 110f), Is.True);
        }

        [Test]
        public void A_clearly_larger_cavalry_threat_takes_over()
        {
            Assert.That(ChargeTargetChoice.KeepsCurrentMounted(100f, 130f), Is.False);
        }

        [Test]
        public void A_current_target_that_is_gone_or_harmless_is_not_kept()
        {
            Assert.That(ChargeTargetChoice.KeepsCurrentMounted(0f, 10f), Is.False);
        }

        // Whether foot can reach riders (else it holds formed facing them instead of chasing).
        [Test]
        public void Riders_far_off_and_moving_cannot_be_caught()
        {
            Assert.That(ChargeTargetChoice.Catchable(80f, false, true, 6f, false), Is.False);
        }

        [Test]
        public void Riders_in_contact_can_be_caught()
        {
            Assert.That(ChargeTargetChoice.Catchable(10f, false, true, 6f, false), Is.True);
        }

        [Test]
        public void Riders_charging_the_block_can_be_caught_from_any_distance()
        {
            Assert.That(ChargeTargetChoice.Catchable(120f, true, true, 8f, false), Is.True);
        }

        [Test]
        public void Slow_riders_near_the_block_can_be_caught()
        {
            Assert.That(ChargeTargetChoice.Catchable(35f, false, true, 1f, false), Is.True);
        }

        [Test]
        public void Slow_riders_far_off_cannot_be_caught()
        {
            Assert.That(ChargeTargetChoice.Catchable(60f, false, true, 1f, false), Is.False);
        }

        [Test]
        public void Riders_without_a_speed_sample_count_as_moving()
        {
            Assert.That(ChargeTargetChoice.Catchable(35f, false, false, 0f, false), Is.False);
        }

        [Test]
        public void Riders_charged_now_are_kept_a_little_further_out()
        {
            Assert.That(ChargeTargetChoice.Catchable(25f, false, true, 6f, true), Is.True);
            Assert.That(ChargeTargetChoice.Catchable(25f, false, true, 6f, false), Is.False);
        }
    }
}
