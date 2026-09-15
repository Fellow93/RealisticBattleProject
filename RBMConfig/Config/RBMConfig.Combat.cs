using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace RBMConfig
{
    public static partial class RBMConfig
    {
        public static float ThrustMagnitudeModifier = 0.05f;
        public static float OneHandedThrustDamageBonus = 20f;
        public static float TwoHandedThrustDamageBonus = 20f;

        //RBMAI
        public static bool hitStopEnabled = true;

        public static bool postureEnabled = true;
        public static bool staminaEnabled = true;

        public static float playerPostureMultiplier = 1f;
        public static bool postureGUIEnabled = true;
        public static bool vanillaCombatAi = false;
        public static bool keepBattleEnabled = false;

        // Frontline -- the per-agent melee jostling system in RBMAI/AiModule/Frontline/FrontlinePositioning.cs.
        // Only the mindset/decision block is gated by frontlineEnabled; the cavalry and ranged free-charge
        // gates and the facing postfix in the same file are always on.
        public static bool frontlineEnabled = true;
        public static int frontlineMinFormationSize = 25;
        public static float frontlineDecisionTimerMax = 2f;
        public static float frontlineAttackWeight = 1f;
        public static float frontlineBackStepWeight = 1f;
        public static float frontlineFindAllyWeight = 1f;
        public static float frontlineFlankWeight = 1f;

        //RBMCombat
        public static bool realisticArrowArc = false;

        public static bool armorStatusUIEnabled = true;

        public static float armorMultiplier = 2f;
        public static bool armorPenetrationMessage = false;
        public static bool betterArrowVisuals = true;
        public static bool passiveShoulderShields = false;
        public static bool troopOverhaulActive = true;
        public static string realisticRangedReload = "2";
        public static float maceBluntModifier = 1f;
        public static float armorThresholdModifier = 1f;
        public static float bluntTraumaBonus = 0f;

        public static bool sneakAttackInstaKill = false;

        public static RBMCombatConfigPriceMultipliers priceMultipliers = new RBMCombatConfigPriceMultipliers();
        public static List<RBMCombatConfigWeaponType> weaponTypesFactors = new List<RBMCombatConfigWeaponType>();
    }
}
