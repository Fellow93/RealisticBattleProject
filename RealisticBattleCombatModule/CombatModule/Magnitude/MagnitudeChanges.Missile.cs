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
                case WeaponClass.Bolt:
                case WeaponClass.SlingStone:
                    {
                        // missileTotalDamage is expected re-based onto a thrust_damage="100" launcher
                        // (see RebaseMissileTotalDamageToRbmLauncher), so what survives is the ammo's head.
                        missileTotalDamage -= 100f;
                        missileTotalDamage *= 0.01f;
                        if (missileTotalDamage < 0f)
                        {
                            missileTotalDamage = 0f;
                        }
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

        // The engine's MissileTotalDamage is the launcher's modified thrust damage (Mission.OnAgentShootMissile's
        // damageBonus) plus the ammo's modified thrust damage. CalculateMissileMagnitude subtracts a flat 100 to
        // isolate the ammo's head, which only holds for launchers with thrust_damage="100" (all of RBM's own).
        // Re-base the total onto a 100-damage launcher so bows/crossbows/slings RBM's XML does not cover still
        // get their ammo's head. The launcher's item-modifier delta is kept, exactly as the flat -100 kept it.
        // For a thrust_damage="100" launcher this returns missileTotalDamage unchanged.
        private static float RebaseMissileTotalDamageToRbmLauncher(Agent shooter, MissionWeapon missile, float missileTotalDamage)
        {
            float launcherDamage = missileTotalDamage - missile.GetModifiedThrustDamageForCurrentUsage();
            if (launcherDamage < 0.5f)
            {
                // No launcher contribution (AddCustomMissile, e.g. RBM's siege engines): their ammo is authored
                // against the flat 100, so leave it as is.
                return missileTotalDamage;
            }
            if (shooter != null && shooter.Equipment != null)
            {
                for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                {
                    MissionWeapon launcher = shooter.Equipment[equipmentIndex];
                    if (!launcher.IsEmpty && launcher.CurrentUsageItem != null && launcher.CurrentUsageItem.IsRangedWeapon && !launcher.CurrentUsageItem.IsConsumable &&
                        Math.Abs(launcher.GetModifiedThrustDamageForCurrentUsage() - launcherDamage) < 0.5f)
                    {
                        return missileTotalDamage - launcher.CurrentUsageItem.ThrustDamage + 100f;
                    }
                }
            }
            // Launcher no longer found (dropped/swapped mid-flight): drop the launcher entirely, keep the ammo.
            return missileTotalDamage - launcherDamage + 100f;
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
                if (currentUsageItem.WeaponClass == WeaponClass.Arrow || currentUsageItem.WeaponClass == WeaponClass.Bolt || currentUsageItem.WeaponClass == WeaponClass.SlingStone)
                {
                    missileTotalDamage = RebaseMissileTotalDamageToRbmLauncher(attackInformation.AttackerAgent, weapon, missileTotalDamage);
                }
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
                length = ApplyRunningThrowPerk(in attackInformation, currentUsageItem, length, collisionData.MissileStartingBaseSpeed);
                baseMagnitude = CalculateMissileMagnitude(weapon.CurrentUsageItem.WeaponClass, weaponItem.Weight, length, missileTotalDamage, momentumRemaining, (DamageTypes)collisionData.DamageType);
                specialMagnitude = baseMagnitude;

                return false;
            }
        }
    }
}
