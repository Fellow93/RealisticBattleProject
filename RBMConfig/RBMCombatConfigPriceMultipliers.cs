namespace RBMConfig
{
	public class RBMCombatConfigPriceMultipliers
	{
		public float ArmorPriceModifier;
		public float WeaponPriceModifier;
		public float HorsePriceModifier;
		public float TradePriceModifier;

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
