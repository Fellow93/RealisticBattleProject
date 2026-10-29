using NUnit.Framework;

namespace RBMCampaign.Tests
{
    // How a town's stock (in days of supply) turns into a price markup.
    public class MarketPriceSpecs
    {
        private const float Basic = RBMMarketPrices.BasicCap;
        private const float Medium = RBMMarketPrices.MediumCap;
        private const float Luxury = RBMMarketPrices.MaxFactor;

        [TestCase(Basic, 15f)]
        [TestCase(Medium, 15f)]
        [TestCase(Luxury, 15f)]
        [TestCase(Luxury, 120f)]
        public void A_comfortably_stocked_market_sells_at_the_base_price(float tierCap, float daysOfStock)
        {
            Assert.That(RBMMarketPrices.ScarcityFactor(daysOfStock, tierCap), Is.EqualTo(1f).Within(0.001f));
        }

        [TestCase(Basic)]
        [TestCase(Medium)]
        [TestCase(Luxury)]
        public void A_bare_shelf_charges_the_goods_tier_ceiling_and_no_more(float tierCap)
        {
            Assert.That(RBMMarketPrices.ScarcityFactor(0.5f, tierCap), Is.EqualTo(tierCap).Within(0.001f));
            Assert.That(RBMMarketPrices.ScarcityFactor(0f, tierCap), Is.EqualTo(tierCap).Within(0.001f));
        }

        // The calibration points the curve was tuned against (RBMMarketPrices.CeilingDays docs).
        [TestCase(Luxury, 10f, 1.3f)]
        [TestCase(Luxury, 5f, 1.9f)]
        [TestCase(Luxury, 2f, 3.4f)]
        [TestCase(Luxury, 1f, 5.2f)]
        [TestCase(Medium, 1f, 3.0f)]
        [TestCase(Basic, 10f, 1.1f)]
        [TestCase(Basic, 2f, 1.5f)]
        [TestCase(Basic, 1f, 1.7f)]
        public void A_short_market_charges_the_documented_markup(float tierCap, float daysOfStock, float markup)
        {
            Assert.That(RBMMarketPrices.ScarcityFactor(daysOfStock, tierCap), Is.EqualTo(markup).Within(0.06f));
        }

        [TestCase(Basic)]
        [TestCase(Medium)]
        [TestCase(Luxury)]
        public void Price_never_drops_as_the_stock_runs_down(float tierCap)
        {
            float previous = RBMMarketPrices.ScarcityFactor(30f, tierCap);
            for (float days = 29.5f; days >= 0f; days -= 0.5f)
            {
                float price = RBMMarketPrices.ScarcityFactor(days, tierCap);
                Assert.That(price, Is.GreaterThanOrEqualTo(previous), "at " + days + " days");
                previous = price;
            }
        }

        [TestCase(Luxury, 0.2f)]
        [TestCase(Luxury, 3f)]
        [TestCase(Basic, 7f)]
        [TestCase(Medium, 15f)]
        public void The_AI_price_signal_agrees_with_the_shop_price_wherever_there_is_a_shortage(float tierCap, float daysOfStock)
        {
            Assert.That(RBMMarketPrices.SignalFactor(daysOfStock, tierCap),
                Is.EqualTo(RBMMarketPrices.ScarcityFactor(daysOfStock, tierCap)).Within(0.0001f));
        }

        [Test]
        public void A_glutted_market_reads_cheap_to_the_AI_but_never_below_the_signal_floor()
        {
            Assert.That(RBMMarketPrices.SignalFactor(60f, Luxury), Is.LessThan(1f));
            Assert.That(RBMMarketPrices.SignalFactor(1000000f, Luxury), Is.EqualTo(RBMMarketPrices.SignalFloor).Within(0.0001f));
        }
    }
}
