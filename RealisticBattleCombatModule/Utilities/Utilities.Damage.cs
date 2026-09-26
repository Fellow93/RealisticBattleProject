using RBMConfig;
using TaleWorlds.Core;
using static TaleWorlds.Core.ArmorComponent;

namespace RBMCombat
{
    public static partial class Utilities
    {
        // Body lives in RBMConfig.SkillDamage so RBMAI's crush-through estimate uses the same table.
        public static float GetSkillBasedDamage(float magnitude, bool isPassiveUsage, string weaponType, DamageTypes damageType, float effectiveSkill, float skillModifier, StrikeType strikeType, float weaponWeight)
        {
            return SkillDamage.GetSkillBasedDamage(magnitude, isPassiveUsage, weaponType, damageType, effectiveSkill, skillModifier, strikeType, weaponWeight);
        }

        // Body lives in RBMConfig.BlowDamage so RBMAI's crush-through estimate uses the same armor math.
        public static float RBMComputeDamage(string weaponType, DamageTypes damageType, float magnitude, float armorEffectiveness, float absorbedDamageRatio, out float penetratedDamage, out float bluntTraumaAfterArmor, float weaponDamageFactor = 1f, BasicCharacterObject player = null, bool isPlayerVictim = false, ArmorMaterialTypes armorMaterial = ArmorMaterialTypes.None)
        {
            return BlowDamage.RBMComputeDamage(weaponType, damageType, magnitude, armorEffectiveness, absorbedDamageRatio, out penetratedDamage, out bluntTraumaAfterArmor, weaponDamageFactor, player, isPlayerVictim, armorMaterial);
        }
    }
}
