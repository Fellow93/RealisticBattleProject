using HarmonyLib;
using RBMAI;
using SandBox.GameComponents;
using SandBox.Missions.MissionLogics;
using System;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ArmorComponent;
using static TaleWorlds.Core.ItemObject;
using static TaleWorlds.MountAndBlade.Agent;

namespace RBMCombat
{
    internal partial class DamageRework
    {
        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("CreateMeleeBlow")]
        private class CreateMeleeBlowPatch
        {
            private static void Postfix(ref Mission __instance, ref Blow __result, Agent attackerAgent, Agent victimAgent, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, CrushThroughState crushThroughState, Vec3 blowDirection, Vec3 swingDirection, bool cancelDamage)
            {

                string weaponType = "otherDamage";
                if (attackerWeapon.Item != null && attackerWeapon.CurrentUsageItem != null)
                {
                    weaponType = attackerWeapon.CurrentUsageItem.WeaponClass.ToString();
                }

                if ((attackerAgent.IsDoingPassiveAttack && collisionData.CollisionResult == CombatCollisionResult.StrikeAgent))
                {
                    if (attackerAgent.Team != victimAgent.Team)
                    {
                        __result.BlowFlag |= BlowFlags.KnockDown;
                        return;
                    }
                }

                if ((attackerAgent.IsDoingPassiveAttack && collisionData.CollisionResult == CombatCollisionResult.Blocked))
                {
                    if (attackerAgent.Team != victimAgent.Team)
                    {
                        sbyte weaponAttachBoneIndex = (sbyte)(attackerWeapon.IsEmpty ? (-1) : attackerAgent.Monster.GetBoneToAttachForItemFlags(attackerWeapon.Item.ItemFlags));
                        __result.WeaponRecord.FillAsMeleeBlow(attackerWeapon.Item, attackerWeapon.CurrentUsageItem, collisionData.AffectorWeaponSlotOrMissileIndex, weaponAttachBoneIndex);
                        __result.StrikeType = (StrikeType)collisionData.StrikeType;
                        __result.DamageType = ((!attackerWeapon.IsEmpty && true && !collisionData.IsAlternativeAttack) ? ((DamageTypes)collisionData.DamageType) : DamageTypes.Blunt);
                        __result.NoIgnore = collisionData.IsAlternativeAttack;
                        __result.AttackerStunPeriod = collisionData.AttackerStunPeriod;
                        __result.DefenderStunPeriod = collisionData.DefenderStunPeriod;
                        __result.BlowFlag = BlowFlags.None;
                        __result.GlobalPosition = collisionData.CollisionGlobalPosition;
                        __result.BoneIndex = collisionData.CollisionBoneIndex;
                        __result.Direction = blowDirection;
                        __result.SwingDirection = swingDirection;
                        __result.VictimBodyPart = collisionData.VictimHitBodyPart;
                        __result.BlowFlag |= BlowFlags.KnockBack;
                        victimAgent.RegisterBlow(__result, collisionData);
                        foreach (MissionBehavior missionBehaviour in __instance.MissionBehaviors)
                        {
                            missionBehaviour.OnRegisterBlow(attackerAgent, victimAgent, WeakGameEntity.Invalid, __result, ref collisionData, in attackerWeapon);
                        }
                        return;
                    }
                }

                if ((collisionData.CollisionResult == CombatCollisionResult.Blocked && !collisionData.AttackBlockedWithShield) || (collisionData.AttackBlockedWithShield && !collisionData.CorrectSideShieldBlock))
                {
                    switch (weaponType)
                    {
                        case "TwoHandedAxe":
                        case "OneHandedAxe":
                        case "OneHandedBastardAxe":
                        case "TwoHandedPolearm":
                        case "TwoHandedMace":
                            {
                                bool hitWithBlade = Utilities.HitWithWeaponBlade(in collisionData, in attackerWeapon);
                                if (attackerAgent.Team != victimAgent.Team && hitWithBlade)
                                {
                                    Blow newBlow = __result;
                                    sbyte weaponAttachBoneIndex = (sbyte)(attackerWeapon.IsEmpty ? (-1) : attackerAgent.Monster.GetBoneToAttachForItemFlags(attackerWeapon.Item.ItemFlags));
                                    newBlow.WeaponRecord.FillAsMeleeBlow(attackerWeapon.Item, attackerWeapon.CurrentUsageItem, collisionData.AffectorWeaponSlotOrMissileIndex, weaponAttachBoneIndex);
                                    newBlow.StrikeType = (StrikeType)collisionData.StrikeType;
                                    newBlow.DamageType = ((!attackerWeapon.IsEmpty && true && !collisionData.IsAlternativeAttack) ? ((DamageTypes)collisionData.DamageType) : DamageTypes.Blunt);
                                    newBlow.NoIgnore = collisionData.IsAlternativeAttack;
                                    newBlow.AttackerStunPeriod = collisionData.AttackerStunPeriod;
                                    newBlow.DefenderStunPeriod = collisionData.DefenderStunPeriod * 0.5f;
                                    newBlow.BlowFlag = BlowFlags.None;
                                    newBlow.GlobalPosition = collisionData.CollisionGlobalPosition;
                                    newBlow.BoneIndex = collisionData.CollisionBoneIndex;
                                    newBlow.Direction = blowDirection;
                                    newBlow.SwingDirection = swingDirection;
                                    newBlow.InflictedDamage = 0;
                                    newBlow.VictimBodyPart = collisionData.VictimHitBodyPart;
                                    newBlow.BlowFlag |= BlowFlags.NonTipThrust;
                                    victimAgent.RegisterBlow(newBlow, collisionData);
                                    foreach (MissionBehavior missionBehaviour in __instance.MissionBehaviors)
                                    {
                                        missionBehaviour.OnRegisterBlow(attackerAgent, victimAgent, WeakGameEntity.Invalid, newBlow, ref collisionData, in attackerWeapon);
                                    }
                                }
                                break;
                            }
                    }
                }

                if (collisionData.CollisionResult != CombatCollisionResult.HitWorld && collisionData.CollisionResult != CombatCollisionResult.None && victimAgent != null && attackerAgent.Team == victimAgent.Team && (__result.BlowFlag.HasAnyFlag(BlowFlags.KnockBack) || __result.BlowFlag.HasAnyFlag(BlowFlags.KnockDown)))
                {
                    __result.BlowFlag = BlowFlags.NonTipThrust;
                    return;
                }
            }
        }

        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("RegisterBlow")]
        private class RegisterBlowPatch
        {
            private static bool Prefix(ref Mission __instance, ref Agent attacker, ref Agent victim, WeakGameEntity realHitEntity, ref Blow b, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, ref CombatLogData combatLogData)
            {
                if (victim != null && victim.IsMount && collisionData.IsMissile)
                {
                    if (MissionGameModels.Current.AgentApplyDamageModel.DecideMountRearedByBlow(attacker, victim, in collisionData, attackerWeapon.CurrentUsageItem, in b))
                    {
                        b.BlowFlag |= BlowFlags.MakesRear;
                    }
                }
                if (attacker != null && attacker.IsMount && collisionData.IsHorseCharge)
                {
                    float horseBodyPartArmor = attacker.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.Chest);
                    b.SelfInflictedDamage = MBMath.ClampInt(MathF.Ceiling(MissionGameModels.Current.StrikeMagnitudeModel.ComputeRawDamage(DamageTypes.Blunt, b.BaseMagnitude / 6f, horseBodyPartArmor, 1f)), 0, 2000);
                    attacker.CreateBlowFromBlowAsReflection(in b, in collisionData, out var outBlow, out var outCollisionData);
                    attacker.RegisterBlow(outBlow, in outCollisionData);
                }

                //detect unarmed attack (exclude kicks - they use legs, not fists, so no arm recoil)
                if (attackerWeapon.IsEmpty && attacker != null && victim != null && collisionData.DamageType == (int)DamageTypes.Blunt && !collisionData.IsFallDamage && !collisionData.IsHorseCharge)
                {
                    if (!collisionData.IsAlternativeAttack)
                    {
                        float attackerArmArmor = attacker.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.ArmLeft);
                        b.SelfInflictedDamage = MBMath.ClampInt(MathF.Ceiling(MissionGameModels.Current.StrikeMagnitudeModel.ComputeRawDamage(DamageTypes.Blunt, b.BaseMagnitude / 2f, attackerArmArmor, 1f)), 0, 2000);
                        attacker.CreateBlowFromBlowAsReflection(in b, in collisionData, out var outBlow, out var outCollisionData);
                        attacker.RegisterBlow(outBlow, in outCollisionData);
                    }

                }

                if (!collisionData.AttackBlockedWithShield && !collisionData.CollidedWithShieldOnBack)
                {
                    return true;
                }
                foreach (MissionBehavior missionBehaviour in __instance.MissionBehaviors)
                {
                    missionBehaviour.OnRegisterBlow(attacker, victim, realHitEntity, b, ref collisionData, in attackerWeapon);
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("HandleBlow")]
        private class HandleBlowPatch
        {
            private static void Postfix(ref Agent __instance, ref Blow b, AgentLastHitInfo ____lastHitInfo, in AttackCollisionData collisionData)
            {
                bool isKnockBack = ((b.BlowFlag & BlowFlags.NonTipThrust) != 0) || ((b.BlowFlag & BlowFlags.KnockDown) != 0) || ((b.BlowFlag & BlowFlags.KnockBack) != 0);
                bool isBash = b.AttackType == AgentAttackType.Bash || b.AttackType == AgentAttackType.Kick;
                if ((isKnockBack || isBash) && b.InflictedDamage <= 0)
                {
                    b.InflictedDamage = 1;
                    HandleBlowAuxMethod.Invoke(__instance, new object[] { b });
                }

            }
        }
    }
}
