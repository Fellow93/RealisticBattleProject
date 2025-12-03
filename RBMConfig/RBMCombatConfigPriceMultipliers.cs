namespace RBMConfig
{
    public class RBMCombatConfigPriceMultipliers
    {
        public float ArmorPriceModifier = 1f;
        public float WeaponPriceModifier = 1f;
        public float HorsePriceModifier = 0.2f;
        public float TradePriceModifier = 1f;

        public RBMCombatConfigPriceMultipliers()
        {
        }

        public RBMCombatConfigPriceMultipliers(
            float ArmorPriceModifier,
            float WeaponPriceModifier,
            float HorsePriceModifier,
            float TradePriceModifier)
        {
            this.ArmorPriceModifier = ArmorPriceModifier;
            this.WeaponPriceModifier = WeaponPriceModifier;
            this.HorsePriceModifier = HorsePriceModifier;
            this.TradePriceModifier = TradePriceModifier;
        }
    }
}