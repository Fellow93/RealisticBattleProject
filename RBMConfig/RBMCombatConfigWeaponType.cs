namespace RBMConfig
{
    public class RBMCombatConfigWeaponType
    {
        public string weaponType;
        public float ExtraBluntFactorCut;
        public float ExtraBluntFactorPierce;
        // Blunt blows: a multiplier on the fixed 0.7 trauma base (1 = the old hardcoded value), unlike the
        // Cut/Pierce factors, which are the trauma base itself.
        public float ExtraBluntFactorBlunt;
        public float ExtraArmorThresholdFactorPierce;
        public float ExtraArmorThresholdFactorCut;
        public float ExtraArmorThresholdFactorBlunt;
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
            float ExtraArmorThresholdFactorBlunt,
            float ExtraArmorSkillDamageAbsorb)
        {
            this.weaponType = weaponType;
            this.ExtraBluntFactorCut = ExtraBluntFactorCut;
            this.ExtraBluntFactorPierce = ExtraBluntFactorPierce;
            this.ExtraBluntFactorBlunt = ExtraBluntFactorBlunt;
            this.ExtraArmorThresholdFactorPierce = ExtraArmorThresholdFactorPierce;
            this.ExtraArmorThresholdFactorCut = ExtraArmorThresholdFactorCut;
            this.ExtraArmorThresholdFactorBlunt = ExtraArmorThresholdFactorBlunt;
            this.ExtraArmorSkillDamageAbsorb = ExtraArmorSkillDamageAbsorb;
        }
    }
}
