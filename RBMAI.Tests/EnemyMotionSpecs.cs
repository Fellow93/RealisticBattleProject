using NUnit.Framework;
using TaleWorlds.Library;

namespace RBMAI.Tests
{
    // The sampled formation movement every "enemy closing / coming in" check reads.
    public class EnemyMotionSpecs
    {
        [Test]
        public void A_walking_block_gives_its_walking_speed()
        {
            Vec2 v;
            Assert.That(EnemyMotion.TryStepVelocity(new Vec2(0f, 0f), new Vec2(3f, 4f), 1f, out v), Is.True);
            Assert.That(v.Length, Is.EqualTo(5f).Within(1e-4f));
        }

        [Test]
        public void A_centre_that_jumps_faster_than_a_horse_gives_no_speed()
        {
            // men moved between formations or an emptied formation refilled elsewhere
            Vec2 v;
            Assert.That(EnemyMotion.TryStepVelocity(new Vec2(0f, 0f), new Vec2(200f, 0f), 1f, out v), Is.False);
        }

        [Test]
        public void An_old_sample_gives_no_speed()
        {
            Vec2 v;
            Assert.That(EnemyMotion.TryStepVelocity(new Vec2(0f, 0f), new Vec2(10f, 0f), 6f, out v), Is.False);
        }
    }
}
