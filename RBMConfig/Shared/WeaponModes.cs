using TaleWorlds.Core;

namespace RBMConfig
{
    /// <summary>
    /// The prototype sword modes (half-sword and mordhau, on the Broad Two Hander, western_2hsword_t4; the figures in
    /// the comments below were tuned on the Highland Broadsword, which had them first) keep weapon_class TwoHandedSword,
    /// so the Two Handed skill and everything keyed by class still treats them as the two-handed sword. Their DAMAGE is
    /// scored as another weapon type, picked here from the usage's weapon description. Every live-combat lookup of a
    /// weapon's damage type (RBMCombat's blow and shield damage, armor wear, RBMAI's posture table) goes through
    /// <see cref="GetDamageWeaponType"/>, so the two modes stay in step everywhere. Exception: the posture table gives
    /// the mordhau its own, clumsier MORDHAU rows (PostureDamageTable.cs) rather than the two-handed mace's. Lives in RBMConfig because RBMAI does
    /// not reference RBMCombat. Auto-resolve and item tiers read usage 0 only and never see either mode.
    /// </summary>
    public static class WeaponModes
    {
        /// <summary>Weapon description of the half-sword mode (staff thrusts, left hand on the blade).</summary>
        public const string HalfSwordDescriptionId = "TwoHandedSwordHalfSword";

        /// <summary>Weapon description of the mordhau mode (sword held reversed by the blade, hilt as the head).</summary>
        public const string MordhauDescriptionId = "TwoHandedSwordMordhau";

        /// <summary>Damage type of the half-sword: its own row in the weapon-type config (sword factors, its own pierce
        /// armor threshold) and the two-handed sword's rows everywhere else.</summary>
        public const string HalfSwordDamageType = "HalfSword";

        /// <summary>Damage type of the mordhau: scored as a two-handed mace (guard and pommel are the head; every
        /// hard-coded switch lists it next to "TwoHandedMace"), with its own row in the weapon-type config for a weaker
        /// blunt trauma (<see cref="MordhauBluntTraumaFactor"/>).</summary>
        public const string MordhauDamageType = "Mordhau";

        /// <summary>
        /// Default ExtraBluntFactorBlunt of the Mordhau config row: a multiplier on the 0.7 blunt-trauma base, which is
        /// what a blunt blow that does not get through the armor deals. A guard and pommel are no mace head: 0.57 keeps
        /// the mordhau a little above the sword's own cut through armor (Broad Two Hander, 150 skill: guard 33 / 27,
        /// pommel 30.5 / 25 at armor 40 / 60, against the cut's 30 / 23); 0.56 would put the pommel level with the cut.
        /// Unarmored hits are unaffected (they penetrate fully). Chosen over the magnitude because the two-handed mace's
        /// skill floor (15 x (1 + skill modifier) x 4.6) would stop a lower magnitude well above this.
        /// </summary>
        public const float MordhauBluntTraumaFactor = 0.57f;

        /// <summary>
        /// Swing damage factor of the mordhau's striking head (guard and pommel), in place of the blade's cutting factor
        /// (it only sets the armor penetration threshold, see BlowDamage). 0.8 = the median of the five vanilla two-handed
        /// maces under RBM's mace heads: peasant_maul_t1 and peasant_maul_t1_2 (mace_head_24, a wooden mallet, 0.3),
        /// sturgia_mace_2_t4 (mace_head_37, 0.8), sturgia_mace_1_t3 (mace_head_38, 0.9), aserai_mace_5_t4 (mace_head_34,
        /// 0.9). It is also the lowest of the three steel heads, which fits a guard and pommel that were never shaped to
        /// strike. Fixed: the blade's item modifier (sharpness, rust) does not change it. Returned for the mordhau by
        /// RBMCombat's Utilities.getSwingDamageFactor, so the blow, the shield hit and the tooltip all use it.
        /// </summary>
        public const float MordhauSwingDamageFactor = 0.8f;

        /// <summary>
        /// Half-sword thrust: factor on the arm strength (the effective mass the arms put behind the point, 5 for any
        /// two-handed thrust) in RBMCombat's Utilities.CalculateThrustMagnitudeForTwoHandedWeapon. With the left hand on
        /// the blade both hands push along the blade's axis, so more of the body drives the point than through a handle.
        /// 1.08: a small extra push, about 7% more thrust energy than the normal grip on the Broad Two Hander at 150
        /// Two Handed (214 against 199). The overhead stab gets the same factor. Its speed is the
        /// sword thrust's: the thrust speed in RBM's formula only feeds the magnitude, not the animation, so a slower
        /// "deliberate" speed there would only have weakened it. Applied in MagnitudeChanges.Melee.cs and the tooltip
        /// (MagnitudeChanges.StatCalcs.cs).
        /// Paired with the HalfSword config row's pierce armor threshold 3.75 (RBMConfig/Utilities.cs, sword 3.5), which
        /// keeps the half-sword "the normal thrust and a little more" against armor (Broad Two Hander, 150 skill: 86 / 34
        /// at armor 40 / 60 against the normal thrust's 80 / 31). The push sets the armor-60 value (pure blunt trauma
        /// there), the threshold the armor-40 one.
        /// </summary>
        public const float HalfSwordThrustForceFactor = 1.08f;

        /// <summary>
        /// Half-sword thrust animation: factor on the usage's ThrustSpeed (the stat the engine plays the thrust at), set
        /// by RBMCombat's MordhauGrip.Apply from crafting's thrust speed for the design, so a slower, deliberate thrust.
        /// RBM's sword thrust magnitude computes its own speed from weight and inertia, so this does not change damage.
        /// Its other weaknesses: a shorter hand reach (RBMXML/RBM_item_usage_sets.xml) and no cuts.
        /// </summary>
        public const float HalfSwordThrustSpeedFactor = 0.85f;

        /// <summary>
        /// Mordhau swing speed and handling (the engine's defend speed), as factors on the PHYSICAL values RBMCombat's
        /// MordhauGrip computes with crafting's own formulas from the inertia about the hand on the blade. Those formulas
        /// are dominated by fixed arm terms, so moving the balance point ~0.5 m from the hand only costs ~6% swing speed
        /// and ~4% handling; these factors cover what they cannot express, the awkwardness of swinging a sword held by
        /// its blade. Target: the mordhau 20% slower than the normal grip in both. On the Highland Broadsword: swing
        /// 106 normal, 100 physical, x0.85 = 85; handling 93 normal, 89 physical, x0.83 = 74. On the Broad Two Hander
        /// (longer blade, hand further from the balance point): swing 106 / 97 / 82, handling 92 / 86 / 71.
        /// </summary>
        public const float MordhauSwingSpeedFactor = 0.85f;

        /// <inheritdoc cref="MordhauSwingSpeedFactor"/>
        public const float MordhauHandlingFactor = 0.83f;

        public static bool IsHalfSword(WeaponComponentData usage)
        {
            return usage != null && usage.WeaponDescriptionId == HalfSwordDescriptionId;
        }

        public static bool IsMordhau(WeaponComponentData usage)
        {
            return usage != null && usage.WeaponDescriptionId == MordhauDescriptionId;
        }

        /// <summary>True for a usage whose damage type is not its weapon class.</summary>
        public static bool HasOwnDamageType(WeaponComponentData usage)
        {
            return IsHalfSword(usage) || IsMordhau(usage);
        }

        /// <summary>
        /// The weapon type a usage's damage is computed as: "HalfSword" for the half-sword, "Mordhau" for the
        /// mordhau, else its weapon class name. Null for no usage.
        /// </summary>
        public static string GetDamageWeaponType(WeaponComponentData usage)
        {
            if (usage == null)
            {
                return null;
            }
            if (IsHalfSword(usage))
            {
                return HalfSwordDamageType;
            }
            if (IsMordhau(usage))
            {
                return MordhauDamageType;
            }
            return usage.WeaponClass.ToString();
        }
    }
}
