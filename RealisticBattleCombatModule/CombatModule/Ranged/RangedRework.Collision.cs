using HarmonyLib;
using JetBrains.Annotations;
using NetworkMessages.FromServer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
        [HarmonyPatch(typeof(Mission))]
        internal class HandleMissileCollisionReactionPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("HandleMissileCollisionReaction")]
            private static bool Prefix(ref Mission __instance, ref Dictionary<int, Missile> ____missilesDictionary, int missileIndex, ref MissileCollisionReaction collisionReaction, MatrixFrame attachLocalFrame, Agent attackerAgent, Agent attachedAgent, bool attachedToShield, sbyte attachedBoneIndex, MissionObject attachedMissionObject, Vec3 bounceBackVelocity, Vec3 bounceBackAngularVelocity, int forcedSpawnIndex, bool isAttachedFrameLocal)
            {
                Missile missile;
                ____missilesDictionary.TryGetValue(missileIndex, out missile);
                if (missile != null)
                {
                    MissileAimTrace.Land(__instance, missileIndex, missile, attachedAgent, attachedToShield);
                }
                MissionObjectId missionObjectId = new MissionObjectId(-1, createdAtRuntime: true);
                // Big battles with heavy missile volume can hand us a missile whose entity the engine never created
                // (reported NRE on Entity.Remove below, 1000-unit battle). Vanilla has the same unguarded line; every
                // branch here needs the entity, so skip the visual handling and still notify network and behaviors.
                switch (missile?.Entity == null ? MissileCollisionReaction.Invalid : collisionReaction)
                {
                    case MissileCollisionReaction.BecomeInvisible:
                        missile.Entity.Remove(81);
                        break;

                    case MissileCollisionReaction.Stick:
                        EquipmentIndex shieldIndex = EquipmentIndex.None;
                        if (attachedAgent != null && attachedToShield)
                        {
                            shieldIndex = FindShieldToStickInto(attachedAgent);
                            if (shieldIndex == EquipmentIndex.None)
                            {
                                // Shield hit but the agent holds no shield any more: nothing to attach to, and a missile
                                // prepared for drop but never attached is left stranded in the world. Vanish it instead,
                                // the same way vanilla's BecomeInvisible does (before PrepareMissileWeaponForDrop).
                                missile.Entity.Remove(81);
                                break;
                            }
                        }
                        missile.Entity.SetVisibilityExcludeParents(visible: true);
                        if (attachedAgent != null)
                        {
                            __instance.PrepareMissileWeaponForDrop(missileIndex);
                            if (attachedToShield)
                            {
                                attachedAgent.AttachWeaponToWeapon(shieldIndex, missile.Weapon, missile.Entity, ref attachLocalFrame);
                            }
                            else
                            {
                                attachedAgent.AttachWeaponToBone(missile.Weapon, missile.Entity, attachedBoneIndex, ref attachLocalFrame);
                            }
                        }
                        else
                        {
                            Vec3 velocity = Vec3.Zero;
                            missionObjectId = __instance.SpawnWeaponAsDropFromMissile(missileIndex, attachedMissionObject, in attachLocalFrame, WeaponSpawnFlags.AsMissile | WeaponSpawnFlags.WithStaticPhysics, in velocity, in velocity, forcedSpawnIndex);
                        }
                        break;

                    case MissileCollisionReaction.BounceBack:
                        missile.Entity.SetVisibilityExcludeParents(visible: true);
                        missionObjectId = __instance.SpawnWeaponAsDropFromMissile(missileIndex, null, in attachLocalFrame, WeaponSpawnFlags.AsMissile | WeaponSpawnFlags.WithPhysics, in bounceBackVelocity, in bounceBackAngularVelocity, forcedSpawnIndex);
                        break;
                }
                bool flag = collisionReaction != MissileCollisionReaction.PassThrough;
                if (GameNetwork.IsServerOrRecorder)
                {
                    GameNetwork.BeginBroadcastModuleEvent(); ;
                    GameNetwork.WriteMessage(new HandleMissileCollisionReaction(missileIndex, collisionReaction, attachLocalFrame, isAttachedFrameLocal, attackerAgent.Index, attachedAgent?.Index ?? (-1), attachedToShield, attachedBoneIndex, attachedMissionObject?.Id ?? MissionObjectId.Invalid, bounceBackVelocity, bounceBackAngularVelocity, missionObjectId.Id));
                    GameNetwork.EndBroadcastModuleEvent(GameNetwork.EventBroadcastFlags.AddToMissionRecord);
                }
                else if (GameNetwork.IsClientOrReplay && flag)
                {
                    __instance.RemoveMissileAsClient(missileIndex);
                }
                foreach (MissionBehavior missionBehavior in __instance.MissionBehaviors)
                {
                    missionBehavior.OnMissileCollisionReaction(collisionReaction, attackerAgent, attachedAgent, attachedBoneIndex);
                }
                return false;
            }

            // The wielded off-hand shield, else the first shield in the weapon slots (a shield on the back), else None.
            private static EquipmentIndex FindShieldToStickInto(Agent agent)
            {
                if (!agent.WieldedOffhandWeapon.IsEmpty)
                {
                    return agent.GetOffhandWieldedItemIndex();
                }
                if (agent.Equipment != null)
                {
                    for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                    {
                        if (!agent.Equipment[equipmentIndex].IsEmpty && agent.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Shield)
                        {
                            return equipmentIndex;
                        }
                    }
                }
                return EquipmentIndex.None;
            }
        }

        [UsedImplicitly]
        [MBCallback]
        [HarmonyPatch(typeof(Mission))]
        internal class MeleeHitCallbackPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("MeleeHitCallback")]
            private static bool Prefix(ref Mission __instance, ref AttackCollisionData collisionData, Agent attacker, Agent victim, GameEntity realHitEntity, ref float inOutMomentumRemaining, ref MeleeCollisionReaction colReaction, CrushThroughState crushThroughState, Vec3 blowDir, Vec3 swingDir, ref HitParticleResultData hitParticleResultData, bool crushedThroughWithoutAgentCollision)
            {
                if (collisionData.CollidedWithShieldOnBack)
                {
                    AttackCollisionData acd = AttackCollisionData.GetAttackCollisionDataForDebugPurpose(true, collisionData.CorrectSideShieldBlock, collisionData.IsAlternativeAttack, collisionData.IsColliderAgent, collisionData.CollidedWithShieldOnBack,
                        collisionData.IsMissile, collisionData.MissileBlockedWithWeapon, collisionData.MissileHasPhysics, collisionData.EntityExists, collisionData.ThrustTipHit, collisionData.MissileGoneUnderWater, collisionData.MissileGoneOutOfBorder,
                        CombatCollisionResult.Blocked, collisionData.AffectorWeaponSlotOrMissileIndex, collisionData.StrikeType, collisionData.DamageType, collisionData.CollisionBoneIndex,
                        collisionData.VictimHitBodyPart, collisionData.AttackBoneIndex, collisionData.AttackDirection, collisionData.PhysicsMaterialIndex, collisionData.CollisionHitResultFlags, collisionData.AttackProgress, collisionData.CollisionDistanceOnWeapon,
                        collisionData.AttackerStunPeriod, collisionData.DefenderStunPeriod, collisionData.MissileTotalDamage, collisionData.MissileStartingBaseSpeed, collisionData.ChargeVelocity, collisionData.FallSpeed, collisionData.WeaponRotUp,
                        collisionData.WeaponBlowDir, collisionData.CollisionGlobalPosition, collisionData.MissileVelocity, collisionData.MissileStartingPosition, collisionData.VictimAgentCurVelocity, collisionData.CollisionGlobalNormal);
                    acd.BaseMagnitude = collisionData.BaseMagnitude;
                    acd.MovementSpeedDamageModifier = collisionData.MovementSpeedDamageModifier;
                    acd.SelfInflictedDamage = collisionData.SelfInflictedDamage;
                    acd.InflictedDamage = collisionData.InflictedDamage;
                    acd.AbsorbedByArmor = collisionData.AbsorbedByArmor;
                    collisionData = acd;
                }
                return true;
            }
        }

        [UsedImplicitly]
        [MBCallback]
        [HarmonyPatch(typeof(Mission))]
        internal class MissileHitCallbackPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("MissileHitCallback")]
            private static bool Prefix(ref Mission __instance, ref Dictionary<int, Missile> ____missilesDictionary, ref AttackCollisionData collisionData, Vec3 missileStartingPosition, Vec3 missilePosition, Vec3 missileAngularVelocity, Vec3 movementVelocity, MatrixFrame attachGlobalFrame, MatrixFrame affectedShieldGlobalFrame, int numDamagedAgents, Agent attacker, Agent victim, GameEntity hitEntity)
            {
                Missile missile;
                if (____missilesDictionary.TryGetValue(collisionData.AffectorWeaponSlotOrMissileIndex, out missile))
                {
                    if (collisionData.CollidedWithShieldOnBack)
                    {
                        if (missile.Weapon.HasAllUsagesWithAnyWeaponFlag(WeaponFlags.MultiplePenetration) || missile.Weapon.HasAllUsagesWithAnyWeaponFlag(WeaponFlags.CanPenetrateShield) ||
                            missile.Weapon.HasAllUsagesWithAnyWeaponFlag(WeaponFlags.AffectsArea) || missile.Weapon.HasAllUsagesWithAnyWeaponFlag(WeaponFlags.AffectsAreaBig))
                        {
                            return true;
                        }

                        // Turning this into a shield block sends vanilla down its AttackBlockedWithShield branch, which for a
                        // CanPenetrateShield missile indexes the victim's off-hand slot - EquipmentIndex.None when the shield is
                        // on the back. Ask the damage model for the flags vanilla will use (Impale, War Sails' Crew of Spears,
                        // other mods' models) and leave those hits alone.
                        WeaponFlags decidedFlags = missile.Weapon.CurrentUsageItem.WeaponFlags;
                        MissionGameModels.Current.AgentApplyDamageModel.DecideMissileWeaponFlags(attacker, missile.Weapon, ref decidedFlags);
                        if (decidedFlags.HasAnyFlag(WeaponFlags.CanPenetrateShield))
                        {
                            return true;
                        }

                        // The engine passes a null attacker once the shooter has been removed from the mission (e.g. a
                        // routed horse archer fading out while its arrow is still in flight).
                        if (attacker?.Character != null)
                        {
                            TaleWorlds.CampaignSystem.CharacterObject characterObject = attacker.Character as TaleWorlds.CampaignSystem.CharacterObject;
                            if (characterObject != null)
                            {
                                if (characterObject.HeroObject != null)
                                {
                                    if (characterObject.HeroObject.GetPerkValue(DefaultPerks.Throwing.Impale))
                                    {
                                        return true;
                                    }
                                }
                            }
                        }

                        AttackCollisionData acd = AttackCollisionData.GetAttackCollisionDataForDebugPurpose(true, collisionData.CorrectSideShieldBlock, collisionData.IsAlternativeAttack, collisionData.IsColliderAgent, collisionData.CollidedWithShieldOnBack,
                            collisionData.IsMissile, collisionData.MissileBlockedWithWeapon, collisionData.MissileHasPhysics, collisionData.EntityExists, collisionData.ThrustTipHit, collisionData.MissileGoneUnderWater, collisionData.MissileGoneOutOfBorder,
                            CombatCollisionResult.Blocked, collisionData.AffectorWeaponSlotOrMissileIndex, collisionData.StrikeType, collisionData.DamageType, collisionData.CollisionBoneIndex,
                            collisionData.VictimHitBodyPart, collisionData.AttackBoneIndex, collisionData.AttackDirection, collisionData.PhysicsMaterialIndex, collisionData.CollisionHitResultFlags, collisionData.AttackProgress, collisionData.CollisionDistanceOnWeapon,
                            collisionData.AttackerStunPeriod, collisionData.DefenderStunPeriod, collisionData.MissileTotalDamage, collisionData.MissileStartingBaseSpeed, collisionData.ChargeVelocity, collisionData.FallSpeed, collisionData.WeaponRotUp,
                            collisionData.WeaponBlowDir, collisionData.CollisionGlobalPosition, collisionData.MissileVelocity, collisionData.MissileStartingPosition, collisionData.VictimAgentCurVelocity, collisionData.CollisionGlobalNormal);
                        acd.BaseMagnitude = collisionData.BaseMagnitude;
                        acd.MovementSpeedDamageModifier = collisionData.MovementSpeedDamageModifier;
                        acd.SelfInflictedDamage = collisionData.SelfInflictedDamage;
                        acd.InflictedDamage = collisionData.InflictedDamage;
                        acd.AbsorbedByArmor = collisionData.AbsorbedByArmor;

                        collisionData = acd;
                    }
                }
                return true;
            }

            // This postfix runs inside the engine's missile-hit callback, and the engine goes on to attach or drop
            // the missile against that same shield once it returns. Removing the shield right here pulled the entity
            // out from under that step (reported crashes on missiles passing through a shield on the back), so the
            // removal is queued for the next mission tick, the way vanilla defers weapon removal from callbacks.
            // Callers queue only on the hit that takes the shield from above 0 to 0: until the tick runs, the 0-HP
            // shield stays in the slot and a second missile in the same frame must not queue it again.
            private static void RemoveBrokenShieldNextTick(Mission mission, Agent victim, EquipmentIndex equipmentIndex)
            {
                mission.AddTickActionMT(Mission.MissionTickAction.RemoveEquippedWeapon, victim, (int)equipmentIndex, 0);
            }

            [HarmonyPostfix]
            [HarmonyPatch("MissileHitCallback")]
            private static void Postfix(ref Mission __instance, ref Dictionary<int, Missile> ____missilesDictionary, ref AttackCollisionData collisionData, Vec3 missileStartingPosition, Vec3 missilePosition, Vec3 missileAngularVelocity, Vec3 movementVelocity, MatrixFrame attachGlobalFrame, MatrixFrame affectedShieldGlobalFrame, int numDamagedAgents, Agent attacker, Agent victim, GameEntity hitEntity)
            {
                Missile missile;
                if (____missilesDictionary.TryGetValue(collisionData.AffectorWeaponSlotOrMissileIndex, out missile))
                {
                    if (missile.Weapon.HasAllUsagesWithAnyWeaponFlag(WeaponFlags.MultiplePenetration) || missile.Weapon.HasAllUsagesWithAnyWeaponFlag(WeaponFlags.CanPenetrateShield) ||
                            missile.Weapon.HasAllUsagesWithAnyWeaponFlag(WeaponFlags.AffectsArea) || missile.Weapon.HasAllUsagesWithAnyWeaponFlag(WeaponFlags.AffectsAreaBig))
                    {
                        if (collisionData.CollidedWithShieldOnBack)
                        {
                            if (victim != null && collisionData.IsMissile)
                            {
                                for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.ExtraWeaponSlot; equipmentIndex++)
                                {
                                    if (victim.Equipment != null && !victim.Equipment[equipmentIndex].IsEmpty)
                                    {
                                        if (victim.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Shield)
                                        {
                                            int previousHitPoints = victim.Equipment[equipmentIndex].HitPoints;
                                            int num = MathF.Max(0, previousHitPoints - collisionData.InflictedDamage);
                                            victim.ChangeWeaponHitPoints(equipmentIndex, (short)num);
                                            if (num == 0 && previousHitPoints > 0)
                                            {
                                                RemoveBrokenShieldNextTick(__instance, victim, equipmentIndex);
                                            }
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        return;
                    }
                }
                if (collisionData.AttackBlockedWithShield && collisionData.CollidedWithShieldOnBack)
                {
                    if (victim != null && collisionData.CollidedWithShieldOnBack && collisionData.IsMissile)
                    {
                        for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                        {
                            if (victim.Equipment != null && !victim.Equipment[equipmentIndex].IsEmpty)
                            {
                                if (victim.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Shield)
                                {
                                    int previousHitPoints = victim.Equipment[equipmentIndex].HitPoints;
                                    int num = MathF.Max(0, previousHitPoints - collisionData.InflictedDamage);
                                    victim.ChangeWeaponHitPoints(equipmentIndex, (short)num);
                                    if (num == 0 && previousHitPoints > 0)
                                    {
                                        RemoveBrokenShieldNextTick(__instance, victim, equipmentIndex);
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
