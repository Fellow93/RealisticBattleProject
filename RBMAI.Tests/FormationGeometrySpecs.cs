using NUnit.Framework;

namespace RBMAI.Tests
{
    // A scattered formation's box (its men's bounds) is capped to 2x its own size, placed on the bulk of its men.
    public class FormationGeometrySpecs
    {
        [Test]
        public void A_box_within_the_cap_is_unchanged()
        {
            float offset = FormationGeometry.CappedOffset(20f, 5f, 30f, out float half);
            Assert.That(half, Is.EqualTo(20f));
            Assert.That(offset, Is.EqualTo(0f));
        }

        [Test]
        public void An_oversized_box_shrinks_to_the_cap()
        {
            FormationGeometry.CappedOffset(60f, 0f, 30f, out float half);
            Assert.That(half, Is.EqualTo(30f));
        }

        [Test]
        public void The_capped_box_sits_on_the_bulk_of_the_men()
        {
            Assert.That(FormationGeometry.CappedOffset(60f, 12f, 30f, out _), Is.EqualTo(12f));
        }

        [Test]
        public void The_capped_box_stays_inside_the_men_box()
        {
            // bulk near one end: the capped box ends at that end instead of sticking out past the men
            Assert.That(FormationGeometry.CappedOffset(60f, 45f, 30f, out _), Is.EqualTo(30f));
            Assert.That(FormationGeometry.CappedOffset(60f, -45f, 30f, out _), Is.EqualTo(-30f));
        }
    }
}
