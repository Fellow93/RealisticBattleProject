using HarmonyLib;
using JetBrains.Annotations;
using NetworkMessages.FromServer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ItemObject;
using static TaleWorlds.MountAndBlade.Mission;

namespace RBMCombat
{
    public partial class RangedRework
    {
        // Bow/crossbow/sling MissileSpeed in the item data is the XML draw weight (sling: length) and is never written.
        // The engine reads a launch speed in two places only: the per-agent WeaponStatsData handed over in
        // Agent.WeaponEquipped (RangedRework.EquipWield sets it there, the AI aims with it) and the shot below, whose
        // prefix sets the velocity and whose transpiler hands native AddMissile the same speed as baseSpeed.
        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("OnAgentShootMissile")]
        [UsedImplicitly]
        [MBCallback]
        private class OverrideOnAgentShootMissile
        {
            // Launch speed of the shot in progress, 0 when RBM did not set one. Main thread only.
            private static int _shotSpeed;

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                // float baseSpeed = shooterAgent.Equipment[...].GetModifiedMissileSpeedForCurrentUsage();
                MethodInfo original = AccessTools.Method(typeof(MissionWeapon), nameof(MissionWeapon.GetModifiedMissileSpeedForCurrentUsage));
                MethodInfo replacement = AccessTools.Method(typeof(OverrideOnAgentShootMissile), nameof(ShotBaseSpeed));
                foreach (CodeInstruction code in instructions)
                {
                    if (code.Calls(original))
                    {
                        code.opcode = OpCodes.Call;
                        code.operand = replacement;
                    }
                    yield return code;
                }
            }

            private static int ShotBaseSpeed(ref MissionWeapon launcher)
            {
                return _shotSpeed > 0 ? _shotSpeed : launcher.GetModifiedMissileSpeedForCurrentUsage();
            }

            private static bool Prefix(Agent shooterAgent, EquipmentIndex weaponIndex, Vec3 position, ref Vec3 velocity, Mat3 orientation, bool hasRigidBody, bool isPrimaryWeaponShot, int forcedMissileIndex, Mission __instance)
            {
                _shotSpeed = 0;
                MissionWeapon missionWeapon = shooterAgent.Equipment[weaponIndex];
                // The first usage's class, what GetWeaponStatsData()[0].WeaponClass held, without building the
                // stats of every usage.
                WeaponClass weaponClass = missionWeapon.GetWeaponComponentDataForUsage(0).WeaponClass;
                Vec3 engineLaunchVelocity = velocity;

                // AI javelin/throwing-axe throws used to be pitched down here after the AI had aimed (axes a flat 5
                // deg, javelins by the shooter's receding speed, and 1.4/2 m/s off the upward speed with no target
                // agent). Removed 2026-10-01: the engine aims with the throw speed RBMCombat's WeaponEquipped prefix
                // hands it, so the tweaks only moved throws off target. AI throws are traced as THROW/LAND lines.
                WeaponClass thrownClass = weaponClass;
                if (thrownClass == WeaponClass.Javelin || thrownClass == WeaponClass.ThrowingAxe || thrownClass == WeaponClass.ThrowingKnife || thrownClass == WeaponClass.Stone)
                {
                    MissileAimTrace.BeginThrow(shooterAgent, thrownClass, position, engineLaunchVelocity, velocity, ItemObject.GetAirFrictionConstant(thrownClass, missionWeapon.CurrentUsageItem.WeaponFlags));
                    // The throw leaves at RBM's speed (WeaponEquipped prefix), so Running Throw counts only what the run adds.
                    // A copy of one piece, as vanilla's shot does: the last throw may already have emptied the slot.
                    MissionWeapon thrownPiece = missionWeapon;
                    thrownPiece.Amount = 1;
                    _shotSpeed = RBMConfig.MissileBallistics.GetThrowSpeed(shooterAgent, thrownPiece);
                }

                if ((weaponClass == WeaponClass.Bow) || (weaponClass == WeaponClass.Crossbow) || (weaponClass == WeaponClass.Sling))
                {
                    float ammoWeight;
                    if (missionWeapon.AmmoWeapon.Item != null && missionWeapon.Item != null && !missionWeapon.AmmoWeapon.IsEmpty && missionWeapon.AmmoWeapon.Amount > 0)
                    {
                        float ammoWeightSum = missionWeapon.AmmoWeapon.GetWeight();
                        float ammoCount = missionWeapon.AmmoWeapon.Amount;
                        ammoWeight = ammoWeightSum / ammoCount;
                    }
                    else
                    {
                        ammoWeight = 0.07f;
                    }

                    int msModifier = 0;
                    if (missionWeapon.ItemModifier != null)
                    {
                        msModifier = missionWeapon.ItemModifier.ModifyHitPoints(50) - 50;
                    }

                    int calculatedMissileSpeed;
                    if (weaponClass == WeaponClass.Sling)
                    {
                        // Slings factor in the shooter's skill and equipment weight on every shot. Same function as
                        // the equip patch (what the engine aims with) and RBMAI's reach gate; it adds the modifier
                        // bonus itself.
                        calculatedMissileSpeed = RBMConfig.MissileBallistics.GetSlingSpeed(shooterAgent, missionWeapon, missionWeapon.CurrentUsageItem);
                    }
                    else
                    {
                        calculatedMissileSpeed = Utilities.calculateMissileSpeed(ammoWeight, missionWeapon.CurrentUsageItem.ItemUsage, missionWeapon.CurrentUsageItem.MissileSpeed + msModifier);
                    }

                    // A modded launcher whose draw weight plus a negative modifier is <= 0 gives sqrt(negative) = NaN,
                    // cast to int.MinValue. That would go into AddMissile's baseSpeed, so keep the engine's shot.
                    if (calculatedMissileSpeed <= 0)
                    {
                        return true;
                    }

                    Vec3 engineVelocity = velocity;
                    Vec3 shooterAgentVelocity = new Vec3(shooterAgent.Velocity, -1);
                    Vec3 myVelocity = new Vec3(velocity, -1);

                    myVelocity.Normalize();

                    float shooterAgentSpeed = Vec3.DotProduct(shooterAgentVelocity, myVelocity);

                    Vec3 modifierVec = shooterAgentVelocity + myVelocity;

                    velocity.x = myVelocity.x * (calculatedMissileSpeed + shooterAgentSpeed);
                    velocity.y = myVelocity.y * (calculatedMissileSpeed + shooterAgentSpeed);
                    velocity.z = myVelocity.z * (calculatedMissileSpeed + shooterAgentSpeed);

                    // The drag the missile flies with is its ammo's (a sling's stone is AirFrictionBullet, not the
                    // sling's AirFrictionStone).
                    float flightFriction = !missionWeapon.AmmoWeapon.IsEmpty && missionWeapon.AmmoWeapon.CurrentUsageItem != null
                        ? ItemObject.GetAirFrictionConstant(missionWeapon.AmmoWeapon.CurrentUsageItem.WeaponClass, missionWeapon.AmmoWeapon.CurrentUsageItem.WeaponFlags)
                        : ItemObject.GetAirFrictionConstant(missionWeapon.CurrentUsageItem.AmmoClass, (WeaponFlags)0);
                    MissileAimTrace.BeginShot(shooterAgent, weaponClass, position, engineVelocity, velocity, calculatedMissileSpeed, ammoWeight, flightFriction);

                    _shotSpeed = calculatedMissileSpeed;
                }

                //if (shooterAgent != null && !shooterAgent.IsAIControlled && !BannerlordConfig.DisplayTargetingReticule && (wsd[0].WeaponClass == (int)WeaponClass.Bow || wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                if (shooterAgent != null && !shooterAgent.IsAIControlled && RBMConfig.RBMConfig.rbmCombatEnabled && RBMConfig.RBMConfig.realisticArrowArc && (weaponClass == WeaponClass.Bow || weaponClass == WeaponClass.Crossbow))
                {

                    double rotRad = 0.083141f;
                    float vecLength = velocity.Length;
                    double currentRad = (double)Math.Acos(MBMath.ClampFloat(velocity.z / vecLength, -1f, 1f));
                    float newZ = velocity.Length * ((float)Math.Cos(currentRad - rotRad));
                    velocity.z = newZ;
                }

                // A NaN/Infinity or zero launch velocity is handed straight to native AddMissile, which
                // access-violates on it; fire the engine's own shot instead.
                if (!velocity.IsValid || velocity.LengthSquared < 1f)
                {
                    velocity = engineLaunchVelocity;
                }

                return true;
            }

            private static void Postfix(ref Agent shooterAgent, EquipmentIndex weaponIndex, Vec3 position, Vec3 velocity, Mat3 orientation, bool hasRigidBody, bool isPrimaryWeaponShot, int forcedMissileIndex, Mission __instance)
            {
                _shotSpeed = 0;
                MissionWeapon missionWeapon = shooterAgent.Equipment[weaponIndex];
                WeaponClass weaponClass = missionWeapon.GetWeaponComponentDataForUsage(0).WeaponClass;

                if (shooterAgent != null && Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.Siege && !shooterAgent.IsMainAgent && (weaponClass == WeaponClass.Javelin || weaponClass == WeaponClass.ThrowingAxe) && shooterAgent.Team?.Side == BattleSideEnum.Defender)
                {
                    for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                    {
                        if (shooterAgent.Equipment[equipmentIndex].IsAnyConsumable() && shooterAgent.Equipment[equipmentIndex].Amount <= 2)
                        {
                            shooterAgent.SetWeaponAmountInSlot(equipmentIndex, shooterAgent.Equipment[equipmentIndex].ModifiedMaxAmount, enforcePrimaryItem: true);
                        }
                    }
                }
                MissileAimTrace.CommitShot(__instance);
            }
        }
    }
}
