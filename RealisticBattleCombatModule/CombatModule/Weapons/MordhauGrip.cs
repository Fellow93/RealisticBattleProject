using HarmonyLib;
using System.Globalization;
using System.Reflection;
using System.Text;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace RBMCombat
{
    /// <summary>
    /// PROTOTYPE mordhau (murder-stroke) mode: a two-handed sword held reversed by the blade near its tip and swung so
    /// the guard and pommel are the striking head. The usage itself is XML (the <c>TwoHandedSwordMordhau</c> weapon
    /// description, last in the TwoHandedSword template, <c>rotated_in_hand</c>); this does the two things XML cannot:
    ///
    /// - Moves the hand up the blade. <c>rotated_in_hand</c> only flips the sword about the grip, so the right hand would
    ///   still close on the handle with the blade pointing back past the wrist. The frame origin is moved along the
    ///   weapon axis so the hand sits on the blade, <see cref="HandOnBladeShare"/> of the way from the blade's base to
    ///   its tip. The rotation (the flip) is kept as crafting set it.
    /// - Makes the swing Blunt. A crafted usage takes its damage type from the blade piece (Cut here), and a weapon
    ///   description has no attribute to override it.
    ///
    /// The question this test answers is whether the engine's melee collision follows the per-usage hand frame (hits
    /// land at the hilt end, close to the man) or ignores it (hits as if the sword were held normally). The usage's
    /// frame reaches the engine per usage in WeaponStatsData.WeaponFrame (MissionWeapon.GetWeaponStatsDataForUsage);
    /// the managed hit math reads GetRealWeaponLength (= WeaponLength + Frame.u . Frame.origin, i.e. hand to TIP),
    /// which for this frame is the short stub of blade behind the hand. The Field Battle Logging setting writes a
    /// MORDHAU line per collision to logs/battles (see BattleHitLogic) with what is needed to tell the cases apart.
    ///
    /// Applied by <see cref="Apply"/> from RBM.SubModule (OnGameInitializationFinished and every
    /// OnMissionBehaviorInitialize, both under rbmCombatEnabled). It is computed from the item's design, never from the
    /// current origin, so running it again changes nothing.
    ///
    /// Damage: the mode is scored as a two-handed mace with a weaker blunt trauma (damage type "Mordhau",
    /// RBMConfig.WeaponModes.GetDamageWeaponType / MordhauBluntTraumaFactor). Its swing
    /// magnitude uses the reversed sword as a lever (<see cref="TryGetSwingLever"/>, MagnitudeChanges.Melee.cs and the
    /// tooltip's MagnitudeChanges.StatCalcs.cs), and its damage factor is WeaponModes.MordhauSwingDamageFactor.
    /// </summary>
    public static class MordhauGrip
    {
        public const string WeaponDescriptionId = RBMConfig.WeaponModes.MordhauDescriptionId;

        /// <summary>
        /// Where the right hand closes on the blade, as a share of the blade's length from its base (the guard end)
        /// toward the tip. 0.85 = 15% of the blade left sticking out behind the fist. The tuning constant of the test.
        /// </summary>
        public const float HandOnBladeShare = 0.85f;

        private static readonly PropertyInfo SwingDamageTypeProperty = AccessTools.Property(typeof(WeaponComponentData), "SwingDamageType");
        private static readonly PropertyInfo SwingSpeedProperty = AccessTools.Property(typeof(WeaponComponentData), "SwingSpeed");
        private static readonly PropertyInfo HandlingProperty = AccessTools.Property(typeof(WeaponComponentData), "Handling");
        private static readonly PropertyInfo ThrustSpeedProperty = AccessTools.Property(typeof(WeaponComponentData), "ThrustSpeed");

        // ---- Crafting's speed formulas, mirrored (TaleWorlds.Core Crafting.CraftingStats: CalculateSwingSpeed,
        // SimulateSwingLayer, CalculateAgility, and SetWeaponData's conversions). Crafting computed the mordhau usage's
        // SwingSpeed and Handling for the hand on the handle; held by the blade, the centre of mass sits about 0.5 m from
        // the hand instead, so both are recomputed from the inertia about the new hand (same formulas for both grips),
        // then scaled by WeaponModes.MordhauSwingSpeedFactor / MordhauHandlingFactor for the grip's awkwardness, which
        // these arm-dominated formulas cannot express. Keep in step with the decompiled source.

        /// <summary>Crafting's swing speed (rad/s; SwingSpeed = Floor(x * 4.5454545)) for a given inertia about the shoulder.</summary>
        private static float CraftingSwingSpeed(float inertiaAroundShoulder, float reach, WeaponFlags flags)
        {
            double num = 1.0 * (double)inertiaAroundShoulder + 0.9;
            double num2 = 170.0;
            double num3 = 90.0;
            double num4 = 27.0;
            double num5 = 15.0;
            double num6 = 7.0;
            if (flags.HasAllFlags(WeaponFlags.MeleeWeapon | WeaponFlags.NotUsableWithOneHand))
            {
                if (flags.HasAnyFlag(WeaponFlags.WideGrip))
                {
                    num += 1.5;
                    num6 *= 4.0;
                    num5 *= 1.7;
                    num3 *= 1.3;
                    num2 *= 1.15;
                }
                else
                {
                    num += 1.0;
                    num6 *= 2.4;
                    num5 *= 1.3;
                    num3 *= 1.35;
                    num2 *= 1.15;
                }
            }
            num4 = System.Math.Max(1.0, num4 - num);
            num5 = System.Math.Max(1.0, num5 - num);
            num6 = System.Math.Max(1.0, num6 - num);
            double reachTerm = 3.9 * (double)reach * (flags.HasAllFlags(WeaponFlags.MeleeWeapon | WeaponFlags.WideGrip) ? 1.0 : 0.3);
            double t = 0.33 * (SimulateSwingLayer(1.5, 200.0, num4, 2.0 + num, reachTerm)
                + SimulateSwingLayer(1.5, num2, num5, 1.0 + num, reachTerm)
                + SimulateSwingLayer(1.5, num3, num6, 0.5 + num, reachTerm));
            return (float)(20.8 / t);
        }

        private static double SimulateSwingLayer(double angleSpan, double usablePower, double maxUsableTorque, double inertia, double reachTerm)
        {
            double angle = 0.0;
            double speed = 0.01;
            double time = 0.0;
            while (angle < angleSpan)
            {
                double torque = usablePower / speed;
                if (torque > maxUsableTorque)
                {
                    torque = maxUsableTorque;
                }
                torque -= speed * reachTerm;
                speed += 0.009999999776482582 * torque / inertia;
                angle += speed * 0.009999999776482582;
                time += 0.009999999776482582;
                if (time > 1000.0)
                {
                    break;   // degenerate input; crafting itself never reaches this
                }
            }
            return time;
        }

        /// <summary>Crafting's handling (CalculateAgility) for a given inertia about the grip.</summary>
        private static int CraftingHandling(float inertiaAroundGrip, WeaponFlags flags)
        {
            float g = inertiaAroundGrip;
            if (flags.HasAllFlags(WeaponFlags.MeleeWeapon | WeaponFlags.NotUsableWithOneHand))
            {
                g *= 0.5f;
                g += 0.9f;
            }
            else if (flags.HasAllFlags(WeaponFlags.MeleeWeapon | WeaponFlags.WideGrip))
            {
                g *= 0.4f;
                g += 1f;
            }
            else
            {
                g += 0.7f;
            }
            return MathF.Round(100f * MathF.Pow(1f / g, 0.55f));
        }

        /// <summary>
        /// Crafting's ThrustSpeed (CalculateThrustSpeed + SimulateThrustLayer, SetWeaponData's Floor(x * 11.764706)) for
        /// the normal hand position: mass term 1.8 + weight + 0.2 x inertia about the grip.
        /// </summary>
        private static int CraftingThrustSpeed(float weight, float inertiaAroundGrip, WeaponFlags flags)
        {
            double num = 1.8 + (double)weight + (double)inertiaAroundGrip * 0.2;
            double num2 = 170.0;
            double num3 = 90.0;
            double num4 = 24.0;
            double num5 = 15.0;
            if (flags.HasAllFlags(WeaponFlags.MeleeWeapon | WeaponFlags.NotUsableWithOneHand) && !flags.HasAnyFlag(WeaponFlags.WideGrip))
            {
                num += 0.6;
                num5 *= 1.9;
                num4 *= 1.1;
                num3 *= 1.2;
                num2 *= 1.05;
            }
            else if (flags.HasAllFlags(WeaponFlags.MeleeWeapon | WeaponFlags.NotUsableWithOneHand | WeaponFlags.WideGrip))
            {
                num += 0.9;
                num5 *= 2.1;
                num4 *= 1.2;
                num3 *= 1.2;
                num2 *= 1.05;
            }
            double t = 0.33 * (SimulateThrustLayer(0.6, 250.0, 48.0, 4.0 + num)
                + SimulateThrustLayer(0.6, num2, num4, 2.0 + num)
                + SimulateThrustLayer(0.6, num3, num5, 0.5 + num));
            return MathF.Floor((float)(3.8500000000000005 / t) * 11.764706f);
        }

        private static double SimulateThrustLayer(double distance, double usablePower, double maxUsableForce, double mass)
        {
            double travelled = 0.0;
            double speed = 0.01;
            double time = 0.0;
            while (travelled < distance)
            {
                double force = usablePower / speed;
                if (force > maxUsableForce)
                {
                    force = maxUsableForce;
                }
                speed += 0.01 * force / mass;
                travelled += speed * 0.01;
                time += 0.01;
                if (time > 1000.0)
                {
                    break;   // degenerate input; crafting itself never reaches this
                }
            }
            return time;
        }

        /// <summary>
        /// SwingSpeed and Handling crafting would give a usage whose hand sits <paramref name="centerOfMass"/> metres
        /// from the centre of mass with <paramref name="reach"/> metres of weapon ahead of it: inertia about the shoulder
        /// = I_cm + m(0.5 + d)^2 (arm 0.5 m), about the grip = I_cm + m d^2, with I_cm = TotalInertia.
        /// </summary>
        private static void CraftingSpeeds(float weight, float inertiaAroundCm, float centerOfMass, float reach, WeaponFlags flags, out int swingSpeed, out int handling)
        {
            float shoulder = inertiaAroundCm + weight * (0.5f + centerOfMass) * (0.5f + centerOfMass);
            float grip = inertiaAroundCm + weight * centerOfMass * centerOfMass;
            swingSpeed = MathF.Floor(CraftingSwingSpeed(shoulder, reach, flags) * 4.5454545f);
            handling = CraftingHandling(grip, flags);
        }

        /// <summary>What the last <see cref="Apply"/> did, one line per adjusted usage; written into the battle log header.</summary>
        public static string LastApplySummary { get; private set; } = "  mordhau: not applied yet";

        public static bool IsMordhau(WeaponComponentData usage)
        {
            return RBMConfig.WeaponModes.IsMordhau(usage);
        }

        /// <summary>
        /// Weapon-local position (m, along the weapon axis from the crafted grip point, + toward the tip) where the
        /// mordhau hand sits. Blade base = WeaponLength - blade length, the same split RBM's handle-hit test uses.
        /// </summary>
        public static bool TryGetHandPosition(ItemObject item, WeaponComponentData usage, out float handZ, out float bladeLength)
        {
            handZ = 0f;
            bladeLength = 0f;
            if (item == null || usage == null || item.WeaponDesign == null || item.WeaponDesign.UsedPieces == null
                || item.WeaponDesign.UsedPieces.Length == 0 || item.WeaponDesign.UsedPieces[0] == null
                || !item.WeaponDesign.UsedPieces[0].IsValid)
            {
                return false;
            }
            float length = usage.WeaponLength * 0.01f;
            bladeLength = item.WeaponDesign.UsedPieces[0].ScaledBladeLength;
            if (bladeLength <= 0f || bladeLength > length)
            {
                return false;
            }
            handZ = length - (1f - HandOnBladeShare) * bladeLength;
            return true;
        }

        /// <summary>
        /// The reversed sword as a lever for the swing magnitude (CombatStatCalculator.CalculateStrikeMagnitudeForSwing),
        /// in metres from the mordhau hand toward the hilt, which is the striking end:
        /// - leverLength = hand to the pommel end = handZ + HandToBottomLength (the pommel end sits at z = -HandToBottomLength),
        /// - centerOfMass = hand to the centre of mass = handZ - CenterOfMass (CenterOfMass is z of the centre of mass).
        /// CalculateStrikeMagnitudeForSwing takes the inertia about the centre of mass (crafting's TotalInertia is that:
        /// CalculateWeaponInertia sums each piece about the centre of mass), so it needs no parallel-axis shift; the
        /// lever arm enters through the centre-of-mass distance it is given.
        /// The usage's own GetRealWeaponLength is no use here: for this frame it is the stub of blade behind the fist.
        /// </summary>
        public static bool TryGetSwingLever(ItemObject item, WeaponComponentData usage, out float leverLength, out float centerOfMass)
        {
            leverLength = 0f;
            centerOfMass = 0f;
            if (!TryGetHandPosition(item, usage, out float handZ, out _))
            {
                return false;
            }
            leverLength = handZ + item.WeaponDesign.HandToBottomLength;
            centerOfMass = handZ - usage.CenterOfMass;
            return leverLength > 0f;
        }

        /// <summary>
        /// Where on the lever a collision was, as a share of <paramref name="leverLength"/> (the impactPointAsPercent of
        /// CalculateStrikeMagnitudeForSwing). The engine reports CollisionDistanceOnWeapon from the hand along the
        /// weapon's axis. Which way that axis points for a flipped hand frame is not established, so the distance is
        /// taken unsigned: everything further from the hand than the stub of blade behind the fist (about 0.13 m) is on
        /// the hilt side anyway. The engine's own impact share (vanilla clamps the distance to the usage's real length)
        /// saturates at the stub, which is why the raw distance is used.
        /// </summary>
        public static float GetImpactPointOnLever(float collisionDistanceOnWeapon, float leverLength)
        {
            if (leverLength <= 0f)
            {
                return 1f;
            }
            return MBMath.ClampFloat(System.Math.Abs(collisionDistanceOnWeapon), 0f, leverLength) / leverLength;
        }

        /// <summary>
        /// Every mordhau usage of every item: hand moved up the blade, swing made Blunt, slower swing and guard. Also
        /// every half-sword usage: slower thrust (<see cref="ApplyHalfSword"/>). Idempotent.
        /// </summary>
        public static void Apply()
        {
            StringBuilder sb = new StringBuilder();
            int count = 0;
            int halfSwordCount = 0;
            MBObjectManager manager = MBObjectManager.Instance;
            MBReadOnlyList<ItemObject> items = manager != null ? manager.GetObjectTypeList<ItemObject>() : null;
            if (items != null)
            {
                foreach (ItemObject item in items)
                {
                    if (item == null || item.WeaponComponent == null)
                    {
                        continue;
                    }
                    MBReadOnlyList<WeaponComponentData> usages = item.Weapons;
                    for (int i = 0; i < usages.Count; i++)
                    {
                        WeaponComponentData usage = usages[i];
                        if (RBMConfig.WeaponModes.IsHalfSword(usage))
                        {
                            ApplyHalfSword(item, usage, i, usages.Count, sb);
                            halfSwordCount++;
                            continue;
                        }
                        if (!IsMordhau(usage))
                        {
                            continue;
                        }
                        DamageTypes swingBefore = usage.SwingDamageType;
                        string frameNote;
                        if (TryGetHandPosition(item, usage, out float handZ, out float bladeLength))
                        {
                            // Same construction as Crafting's own hand base (use_center_of_mass_as_hand_base):
                            // origin = R * (-Up * handZ), so weapon point z sits at hand-space (handZ - z) * up after the flip.
                            Mat3 rotation = usage.Frame.rotation;
                            Vec3 toHand = -Vec3.Up * handZ;
                            usage.SetFrame(new MatrixFrame(in rotation, rotation.TransformToParent(in toHand)));
                            frameNote = "L=" + F(usage.WeaponLength * 0.01f) + " blade=" + F(bladeLength)
                                + " pommelEnd=" + F(-item.WeaponDesign.HandToBottomLength)
                                + " handZ=" + F(handZ)
                                + " frame.u=(" + F(usage.Frame.rotation.u.x) + "," + F(usage.Frame.rotation.u.y) + "," + F(usage.Frame.rotation.u.z) + ")"
                                + " frame.origin=(" + F(usage.Frame.origin.x) + "," + F(usage.Frame.origin.y) + "," + F(usage.Frame.origin.z) + ")"
                                + " realLength=" + F(usage.GetRealWeaponLength())
                                + " lever=" + F(handZ + item.WeaponDesign.HandToBottomLength)
                                + " leverCoM=" + F(handZ - usage.CenterOfMass);
                        }
                        else
                        {
                            frameNote = "frame NOT moved (no crafted design / blade length)";
                        }
                        if (usage.SwingDamageType != DamageTypes.Blunt && SwingDamageTypeProperty != null)
                        {
                            SwingDamageTypeProperty.SetValue(usage, DamageTypes.Blunt);
                        }
                        // Unwieldy: swing speed and handling (the engine's defend speed) from the inertia about the hand
                        // on the blade. Inputs are the design's (weight, inertia, centre of mass, lengths), never the
                        // usage's current speeds, so a second Apply sets the same values.
                        int swingSpeedBefore = usage.SwingSpeed;
                        int handlingBefore = usage.Handling;
                        string speedNote;
                        if (TryGetSwingLever(item, usage, out float leverLength, out float leverCenterOfMass)
                            && SwingSpeedProperty != null && HandlingProperty != null)
                        {
                            // The normal grip through the same formulas: should match the crafted values (a check, not used).
                            CraftingSpeeds(item.Weight, usage.TotalInertia, usage.CenterOfMass, usage.WeaponLength * 0.01f, usage.WeaponFlags, out int normalSwing, out int normalHandling);
                            CraftingSpeeds(item.Weight, usage.TotalInertia, leverCenterOfMass, leverLength, usage.WeaponFlags, out int physicalSwing, out int physicalHandling);
                            // Physical value, then the grip-awkwardness factor (RBMConfig.WeaponModes); both from the
                            // design, so Apply stays idempotent.
                            int finalSwing = MathF.Round(physicalSwing * RBMConfig.WeaponModes.MordhauSwingSpeedFactor);
                            int finalHandling = MathF.Round(physicalHandling * RBMConfig.WeaponModes.MordhauHandlingFactor);
                            SwingSpeedProperty.SetValue(usage, finalSwing);
                            HandlingProperty.SetValue(usage, finalHandling);
                            speedNote = " swingSpeed " + swingSpeedBefore + "->" + usage.SwingSpeed + " (normal-grip formula " + normalSwing + ", physical " + physicalSwing + ")"
                                + " handling " + handlingBefore + "->" + usage.Handling + " (normal-grip formula " + normalHandling + ", physical " + physicalHandling + ")";
                        }
                        else
                        {
                            speedNote = " swingSpeed/handling NOT recomputed (no lever) " + swingSpeedBefore + "/" + handlingBefore;
                        }
                        count++;
                        sb.Append("  mordhau: ").Append(item.StringId).Append(" usage ").Append(i).Append("/").Append(usages.Count)
                          .Append(" ").Append(frameNote)
                          .Append(" swing ").Append(swingBefore).Append("->").Append(usage.SwingDamageType)
                          .Append(speedNote)
                          .Append("\n");
                    }
                }
            }
            if (count == 0)
            {
                sb.Append("  mordhau: no item has a ").Append(WeaponDescriptionId).Append(" usage (check the XML)\n");
            }
            if (halfSwordCount == 0)
            {
                sb.Append("  half-sword: no item has a ").Append(RBMConfig.WeaponModes.HalfSwordDescriptionId).Append(" usage (check the XML)\n");
            }
            LastApplySummary = sb.ToString();
        }

        /// <summary>
        /// Prototype half-sword usage: a slower, deliberate thrust. ThrustSpeed (sent to the engine in WeaponStatsData,
        /// it sets the thrust animation's speed) = crafting's thrust speed for the design x
        /// WeaponModes.HalfSwordThrustSpeedFactor. From the design, never the current value, so idempotent. RBM's
        /// thrust magnitude for swords computes its own speed from weight and inertia, so this does not change damage.
        /// </summary>
        private static void ApplyHalfSword(ItemObject item, WeaponComponentData usage, int index, int usageCount, StringBuilder sb)
        {
            int before = usage.ThrustSpeed;
            int crafted = CraftingThrustSpeed(item.Weight, usage.TotalInertia + item.Weight * usage.CenterOfMass * usage.CenterOfMass, usage.WeaponFlags);
            if (ThrustSpeedProperty != null)
            {
                ThrustSpeedProperty.SetValue(usage, MathF.Round(crafted * RBMConfig.WeaponModes.HalfSwordThrustSpeedFactor));
            }
            sb.Append("  half-sword: ").Append(item.StringId).Append(" usage ").Append(index).Append("/").Append(usageCount)
              .Append(" itemUsage=").Append(usage.ItemUsage)
              .Append(" thrustSpeed ").Append(before).Append("->").Append(usage.ThrustSpeed)
              .Append(" (crafting formula ").Append(crafted).Append(")\n");
        }

        private static string F(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
