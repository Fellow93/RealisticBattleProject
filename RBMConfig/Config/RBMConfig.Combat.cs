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
        // AI kick / shield bash / weapon bash (RBMAI AiModule/Agents/AiKickBash.cs). Gates the whole feature:
        // the AI attempts, their posture/stamina costs, the victim's loss and the knockdown rules. Off = vanilla.
        public static bool aiKickBashEnabled = true;

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
        public static bool betterArrowVisuals = true;
        // Thickness multiplier for the realistic in-flight arrow/bolt mesh (betterArrowVisuals); 1 = true to size.
        public static float arrowThicknessScale = 1f;
        public static bool passiveShoulderShields = false;
        // A melee blow landing on the arm that holds a shield is blocked by the shield even when its bearer is not
        // blocking (RBMCombat MeleeHitCallbackPatch in Ranged/RangedRework.Collision.cs). Player and AI alike.
        public static bool passiveShieldBlockEnabled = true;
        public static bool troopOverhaulActive = true;
        public static string realisticRangedReload = "2";
        // When true, AI agents follow realisticRangedReload too (otherwise that setting is player-only).
        public static bool rangedReloadAffectsAi = false;
        // Player-only dotted trajectory preview while drawing a bow/crossbow/sling (RBMCombat RangedAimArcView), plus
        // the camera assist that lifts and tilts the third-person camera so the predicted landing point of a high shot
        // stays on screen (RBMCombat RangedAimCamera). Works with or without rbmCombatEnabled; when off the view is
        // not even added to the mission and nothing is patched.
        public static bool rangedAimArcEnabled = false;
        public static float maceBluntModifier = 1f;
        public static float armorThresholdModifier = 1f;
        public static float bluntTraumaBonus = 0f;

        public static bool sneakAttackInstaKill = false;

        public static RBMCombatConfigPriceMultipliers priceMultipliers = new RBMCombatConfigPriceMultipliers();
        public static List<RBMCombatConfigWeaponType> weaponTypesFactors = new List<RBMCombatConfigWeaponType>();
    }
}
