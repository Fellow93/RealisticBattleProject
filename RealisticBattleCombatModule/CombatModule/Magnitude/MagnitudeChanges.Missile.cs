using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.CampaignSystem.ViewModelCollection.Inventory;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace RBMCombat
{
    public static partial class MagnitudeChanges
    {
        public static float CalculateMissileMagnitude(WeaponClass weaponClass, float weaponWeight, float missileSpeed, float missileTotalDamage, float momentumRemaining, DamageTypes damageType)
        {
            float baseMagnitude = 0f;
            switch (weaponClass)
            {
                case WeaponClass.Boulder:
                case WeaponClass.Stone:
                    {
                        missileTotalDamage *= 0.01f;
                        break;
                    }
                case WeaponClass.ThrowingAxe:
                case WeaponClass.ThrowingKnife:
                case WeaponClass.Dagger:
                case WeaponClass.Javelin:
                case WeaponClass.OneHandedPolearm:
                case WeaponClass.LowGripPolearm:
                    {
                        missileSpeed -= Utilities.throwableCorrectionSpeed;
                        if (missileSpeed < 5.0f)
                        {
                            missileSpeed = 5f;
                        }
                        break;
                    }
                case WeaponClass.Arrow:
                    {
                        missileTotalDamage -= 100f;
                        missileTotalDamage *= 0.01f;
                        break;
                    }
                case WeaponClass.Bolt:
                    {
                        missileTotalDamage -= 100f;
                        missileTotalDamage *= 0.01f;
                        break;
                    }
                case WeaponClass.SlingStone:
                    {
                        missileTotalDamage -= 100f;
                        missileTotalDamage *= 0.01f;
                        break;
                    }
            }

            float physicalDamage = ((missileSpeed * missileSpeed) * (weaponWeight)) / 2;
            float momentumDamage = (missileSpeed * weaponWeight);
            switch (weaponClass)
            {
                case WeaponClass.Boulder:
                case WeaponClass.Stone:
                    {
                        physicalDamage = (missileSpeed * missileSpeed * (weaponWeight) * 0.5f);
                        break;
                    }
                case WeaponClass.ThrowingAxe:
                case WeaponClass.ThrowingKnife:
                case WeaponClass.Dagger:
                    {
                        missileSpeed -= 0f; //5f
                        break;
                    }
                case WeaponClass.Javelin:
                case WeaponClass.OneHandedPolearm:
                case WeaponClass.LowGripPolearm:
                    {
                        if (physicalDamage > (weaponWeight) * 300f)
                        {
                            physicalDamage = (weaponWeight) * 300f;
                        }
                        break;
                    }
                case WeaponClass.Arrow:
                    {
                        if (physicalDamage > (weaponWeight) * 2250f)
                        {
                            physicalDamage = (weaponWeight) * 2250f;
                        }
                        break;
                    }
                case WeaponClass.Bolt:
                    {
                        if (physicalDamage > (weaponWeight) * 2500f)
                        {
                            physicalDamage = (weaponWeight) * 2500f;
                        }
                        break;
                    }
                case WeaponClass.SlingStone:
                    {
                        // Sling stones are heavier than arrows but slower; cap is slightly below arrow
                        // to reflect the lower penetration potential of a blunt projectile.
                        if (physicalDamage > (weaponWeight) * 3000f)
                        {
                            physicalDamage = (weaponWeight) * 3000f;
                        }
                        break;
                    }
            }

            baseMagnitude = physicalDamage * missileTotalDamage * momentumRemaining;

            if (weaponClass == WeaponClass.Javelin)
            {
                missileTotalDamage = 0f;
                //baseMagnitude = (physicalDamage * momentumRemaining + (missileTotalDamage * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                if (damageType == DamageTypes.Pierce)
                {
                    baseMagnitude = (physicalDamage * momentumRemaining) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                }
                else if (damageType == DamageTypes.Cut)
                {
                    baseMagnitude = (physicalDamage * momentumRemaining);
                }
                else
                {
                    baseMagnitude = (physicalDamage * momentumRemaining) * 0.5f;
                }
            }

            if (weaponClass == WeaponClass.ThrowingAxe)
            {
                baseMagnitude = physicalDamage * momentumRemaining;
            }
            if (weaponClass == WeaponClass.ThrowingKnife ||
                weaponClass == WeaponClass.Dagger)
            {
                baseMagnitude = (physicalDamage * momentumRemaining) * RBMConfig.RBMConfig.ThrustMagnitudeModifier * 0.6f;
            }

            if (weaponClass == WeaponClass.OneHandedPolearm ||
                weaponClass == WeaponClass.LowGripPolearm)
            {
                baseMagnitude = (physicalDamage * momentumRemaining) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
            }
            if (weaponClass == WeaponClass.Arrow ||
                weaponClass == WeaponClass.Bolt ||
                weaponClass == WeaponClass.SlingStone)
            {
                baseMagnitude = physicalDamage * missileTotalDamage * momentumRemaining;
            }
            return baseMagnitude;
        }

        [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
        [HarmonyPatch("ComputeBlowMagnitudeMissile")]
        private class ComputeBlowMagnitudeMissilePatch
        {
            private static bool Prefix(in AttackInformation attackInformation, in AttackCollisionData collisionData, float momentumRemaining, in Vec2 victimVelocity, out float baseMagnitude, out float specialMagnitude)
            {
                MissionWeapon weapon = attackInformation.AttackerWeapon;
                Vec3 missileVelocity = collisionData.MissileVelocity;

                float missileTotalDamage = collisionData.MissileTotalDamage;

                WeaponComponentData currentUsageItem = weapon.CurrentUsageItem;
                ItemObject weaponItem;
                if (weapon.AmmoWeapon.Item != null)
                {
                    weaponItem = weapon.AmmoWeapon.Item;
                }
                else
                {
                    weaponItem = weapon.Item;
                }

                float length;
                if (!attackInformation.IsVictimAgentNull)
                {
                    length = (victimVelocity.ToVec3() - missileVelocity).Length;
                }
                else
                {
                    length = missileVelocity.Length;
                }
                baseMagnitude = CalculateMissileMagnitude(weapon.CurrentUsageItem.WeaponClass, weaponItem.Weight, length, missileTotalDamage, momentumRemaining, (DamageTypes)collisionData.DamageType);
                specialMagnitude = baseMagnitude;

                return false;
            }
        }
    }
}
