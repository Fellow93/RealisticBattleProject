namespace RBMConfig
{
    /// <summary>
    /// Weapon-type keys (WeaponClass names plus RBM's "unarmedAttack"/"otherDamage") as the shared damage math in
    /// <see cref="BlowDamage"/> and <see cref="SkillDamage"/> reads them.
    /// </summary>
    public static class WeaponTypeKeys
    {
        /// <summary>
        /// The class whose rules a weapon type follows. No item in the game or RBM is a LowGripPolearm or a Pick;
        /// another mod's are priced as a one-handed spear and a mace, so they use those classes' rows. Before they
        /// fell to the default case, where their thrusts (scaled by ThrustMagnitudeModifier like every thrust) missed
        /// the thrust bonus and hit for a twentieth, with the armor-ignoring default factors. Every other type is
        /// returned unchanged.
        /// </summary>
        public static string NormalizeWeaponType(string weaponType)
        {
            switch (weaponType)
            {
                case "LowGripPolearm":
                    return "OneHandedPolearm";
                case "Pick":
                    return "Mace";
                default:
                    return weaponType;
            }
        }
    }
}
