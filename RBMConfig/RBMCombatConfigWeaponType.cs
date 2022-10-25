namespace RBMConfig
{
	public class RBMCombatConfigWeaponType
    {
		public string weaponType;
		public float ExtraBluntFactorCut;
		public float ExtraBluntFactorPierce;
		public float ExtraBluntFactorBlunt;
		public float ExtraArmorThresholdFactorPierce;
		public float ExtraArmorThresholdFactorCut;
		public float ExtraArmorSkillDamageAbsorb;

		public RBMCombatConfigWeaponType()
        {

        }

		public RBMCombatConfigWeaponType(
			string weaponType,
			float ExtraBluntFactorCut,
			float ExtraBluntFactorPierce,
			float ExtraBluntFactorBlunt,
			float ExtraArmorThresholdFactorPierce,
			float ExtraArmorThresholdFactorCut,
			float ExtraArmorSkillDamageAbsorb)
        {
			this.weaponType = weaponType;
			this.ExtraBluntFactorCut = ExtraBluntFactorCut;
			this.ExtraBluntFactorPierce = ExtraBluntFactorPierce;
			this.ExtraBluntFactorBlunt = ExtraBluntFactorBlunt;
			this.ExtraArmorThresholdFactorPierce = ExtraArmorThresholdFactorPierce;
			this.ExtraArmorThresholdFactorCut = ExtraArmorThresholdFactorCut;
			this.ExtraArmorSkillDamageAbsorb = ExtraArmorSkillDamageAbsorb;
		}
	}
}
