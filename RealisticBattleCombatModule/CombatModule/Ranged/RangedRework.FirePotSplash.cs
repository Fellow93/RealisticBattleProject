using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMCombat
{
    public partial class RangedRework
    {
        /// <summary>
        /// Steeper splash fall-off for fire pots (any Burning area missile: the hand-thrown `pot`, the siege
        /// engines' pot projectiles, War Sails' naval pots).
        ///
        /// Native Mission.MissileAreaDamageCallback deals full damage out to an inner radius (1.6 m big / 1.0 m
        /// small, x0.8 under 22 m/s) and then only falls to 1/9 at the edge (2.8 m / 1.2 m), so a pot still hurts
        /// a whole file of men. Here a burning splash keeps full damage only over the inner FullDamageFraction of
        /// the radius and then drops cubically to zero at the edge. Native's halving of slow (hand-thrown)
        /// splashes is kept. Stones and other area missiles keep the native curve.
        ///
        /// The factor is a local (num8) that native multiplies into each victim's InflictedDamage, so this
        /// transpiler overwrites it right after the missile weapon is read, once per splash victim.
        /// </summary>
        [HarmonyPatch(typeof(Mission), "MissileAreaDamageCallback")]
        internal class FirePotSplashFalloff
        {
            private const float FullDamageFraction = 0.3f;
            private const float FalloffExponent = 3f;

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
                MethodInfo sqrt = AccessTools.Method(typeof(MathF), nameof(MathF.Sqrt), new[] { typeof(float) });
                MethodInfo setBone = AccessTools.Method(typeof(AttackCollisionData), nameof(AttackCollisionData.SetCollisionBoneIndexForAreaDamage));
                MethodInfo getWeapon = AccessTools.PropertyGetter(typeof(Mission.Missile), nameof(Mission.Missile.Weapon));
                MethodInfo falloff = AccessTools.Method(typeof(FirePotSplashFalloff), nameof(Falloff));

                int distanceLocal = -1;
                int factorLocal = -1;
                for (int i = 0; i < codes.Count - 1; i++)
                {
                    // float num6 = MathF.Sqrt(num4);  -> distance from the blast to the victim's nearest bone
                    if (distanceLocal < 0 && codes[i].Calls(sqrt) && codes[i + 1].IsStloc())
                    {
                        distanceLocal = codes[i + 1].LocalIndex();
                    }
                    // num8 *= num3; attackCollisionData.SetCollisionBoneIndexForAreaDamage(...)  -> the store just
                    // before that call's two argument loads is the finished fall-off factor.
                    if (factorLocal < 0 && codes[i].Calls(setBone) && i >= 3 && codes[i - 3].IsStloc())
                    {
                        factorLocal = codes[i - 3].LocalIndex();
                    }
                    // MissionWeapon attackerWeapon = _missilesDictionary[...].Weapon;
                    if (distanceLocal >= 0 && factorLocal >= 0 && codes[i].Calls(getWeapon) && codes[i + 1].IsStloc())
                    {
                        int weaponLocal = codes[i + 1].LocalIndex();
                        codes.InsertRange(i + 2, new[]
                        {
                            CodeInstruction.LoadLocal(factorLocal),
                            CodeInstruction.LoadLocal(distanceLocal),
                            new CodeInstruction(OpCodes.Ldarg_S, (byte)5), // isBigExplosion
                            new CodeInstruction(OpCodes.Ldarg_1),          // ref AttackCollisionData collisionDataInput
                            CodeInstruction.LoadLocal(weaponLocal),
                            new CodeInstruction(OpCodes.Call, falloff),
                            CodeInstruction.StoreLocal(factorLocal),
                        });
                        break;
                    }
                }

                return codes;
            }

            private static float Falloff(float nativeFactor, float distance, bool isBigExplosion, ref AttackCollisionData collisionData, MissionWeapon weapon)
            {
                WeaponComponentData usage = weapon.IsEmpty ? null : weapon.CurrentUsageItem;
                if (usage == null || !usage.WeaponFlags.HasAnyFlag(WeaponFlags.Burning))
                {
                    return nativeFactor;
                }

                // Same radii and slow-missile halving as native.
                float radius = isBigExplosion ? 2.8f : 1.2f;
                float fullRadius = radius * FullDamageFraction;
                float t = MBMath.ClampFloat((distance - fullRadius) / (radius - fullRadius), 0f, 1f);
                float factor = (float)System.Math.Pow(1f - t, FalloffExponent);
                if (collisionData.MissileVelocity.LengthSquared < 484f)
                {
                    factor *= 0.5f;
                }
                return factor;
            }
        }
    }
}
