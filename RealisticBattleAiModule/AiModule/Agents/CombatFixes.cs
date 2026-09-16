using HarmonyLib;
using Helpers;
using JetBrains.Annotations;
using SandBox.GameComponents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.MountAndBlade.ArrangementOrder;

namespace RBMAI
{
    public static partial class AgentAi
    {
        public static bool IsActivelyAttacking(Agent agent)
        {
            switch (agent.AttackDirection)
            {
                case Agent.UsageDirection.AttackDown:
                case Agent.UsageDirection.AttackLeft:
                case Agent.UsageDirection.AttackRight:
                case Agent.UsageDirection.AttackEnd:
                case Agent.UsageDirection.AttackAny:
                    {
                        return true;
                    }
            }
            Agent.ActionCodeType currentActionType = agent.GetCurrentActionType(1);
            if (
                currentActionType == Agent.ActionCodeType.ReadyMelee ||
                currentActionType == Agent.ActionCodeType.ReleaseRanged ||
                currentActionType == Agent.ActionCodeType.ReleaseThrowing)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// True when the unit carries a melee weapon it can hold together with a shield. A troop whose
        /// only melee weapon is two-handed (e.g. imperial "Flame") cannot raise the shield without
        /// sheathing its weapon, so forcing a shield stance on it would leave it fighting bare-handed.
        /// </summary>
        private static bool HasShieldCompatibleMeleeWeapon(Agent unit)
        {
            MissionEquipment equipment = unit.Equipment;
            if (equipment == null)
            {
                return false;
            }
            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
            {
                MissionWeapon weapon = equipment[i];
                if (weapon.IsEmpty)
                {
                    continue;
                }
                WeaponComponentData usage = weapon.CurrentUsageItem;
                if (usage != null && usage.IsMeleeWeapon && !usage.IsShield &&
                    !usage.WeaponFlags.HasAnyFlag(WeaponFlags.NotUsableWithOneHand))
                {
                    return true;
                }
            }
            return false;
        }

        [HarmonyPatch(typeof(ArrangementOrder))]
        [HarmonyPatch("GetShieldDirectionOfUnit")]
        internal class HoldTheDoor
        {
            private static void Postfix(ref Agent.UsageDirection __result, Formation formation, Agent unit, ArrangementOrderEnum orderEnum)
            {
                if (unit.IsDetachedFromFormation)
                {
                    __result = Agent.UsageDirection.None;
                    return;
                }
                if (Mission.Current != null && Mission.Current.IsSiegeBattle && unit.Team != null && unit.IsActive() &&
                    unit.Team.IsAttacker && !unit.IsRangedCached && unit.HasShieldCached && !IsActivelyAttacking(unit) &&
                    HasShieldCompatibleMeleeWeapon(unit))
                {
                    if (__result == Agent.UsageDirection.None)
                    {
                        __result = Agent.UsageDirection.DefendUp;
                    }
                }
                bool test = true;
                switch (orderEnum)
                {
                    case ArrangementOrderEnum.ShieldWall:
                        if (unit.Formation.FiringOrder.OrderEnum != FiringOrder.RangedWeaponUsageOrderEnum.HoldYourFire)
                        {
                            bool hasRanged = unit.Equipment.HasAnyWeaponWithFlags(WeaponFlags.HasString);
                            bool hasTwoHanded = unit.Equipment.HasAnyWeaponWithFlags(WeaponFlags.NotUsableWithOneHand);
                            if (hasRanged || hasTwoHanded)
                            {
                                test = false;
                            }
                        }
                        if (test)
                        {
                            if (((IFormationUnit)unit).FormationRankIndex == 0)
                            {
                                __result = Agent.UsageDirection.DefendDown;
                                return;
                            }
                            if (formation.Arrangement.GetNeighborUnitOfLeftSide(unit) == null)
                            {
                                __result = Agent.UsageDirection.DefendLeft;
                                return;
                            }
                            if (formation.Arrangement.GetNeighborUnitOfRightSide(unit) == null)
                            {
                                __result = Agent.UsageDirection.DefendRight;
                                return;
                            }
                            __result = Agent.UsageDirection.AttackEnd;
                            return;
                        }
                        __result = Agent.UsageDirection.None;
                        return;

                    case ArrangementOrderEnum.Circle:
                    case ArrangementOrderEnum.Square:
                        if (unit.Formation.FiringOrder.OrderEnum != FiringOrder.RangedWeaponUsageOrderEnum.HoldYourFire)
                        {
                            bool hasRanged = unit.Equipment.HasAnyWeaponWithFlags(WeaponFlags.HasString);
                            bool hasTwoHanded = unit.Equipment.HasAnyWeaponWithFlags(WeaponFlags.NotUsableWithOneHand);
                            if (hasRanged || hasTwoHanded)
                            {
                                test = false;
                            }
                        }
                        if (test)
                        {
                            if (((IFormationUnit)unit).FormationRankIndex == 0)
                            {
                                __result = Agent.UsageDirection.DefendDown;
                                return;
                            }
                            __result = Agent.UsageDirection.AttackEnd;
                            return;
                        }
                        __result = Agent.UsageDirection.None;
                        return;

                    default:
                        //__result = Agent.UsageDirection.None;
                        return;
                }
            }
        }

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("UpdateLastAttackAndHitTimes")]
        internal class UpdateLastAttackAndHitTimesFix
        {
            private static readonly PropertyInfo _lastRangedHitTime = typeof(Agent).GetProperty("LastRecievedRangedHitTime");
            private static readonly PropertyInfo _lastRangedAttackTime = typeof(Agent).GetProperty("LastRangedHitTime");
            private static readonly PropertyInfo _lastMeleeHitTime = typeof(Agent).GetProperty("LastRecievedMeleeHitTime");
            private static readonly PropertyInfo _lastMeleeAttackTime = typeof(Agent).GetProperty("LastMeleeHitTime");

            private static bool Prefix(ref Agent __instance, Agent attackerAgent, bool isMissile)
            {
                float currentTime = MBCommon.GetTotalMissionTime();
                if (isMissile)
                {
                    //__instance.LastRecievedRangedHitTime = currentTime;
                    _lastRangedHitTime.SetValue(__instance, currentTime, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                }
                else
                {
                    //LastRecievedMeleeHitTime = currentTime;
                    _lastMeleeHitTime.SetValue(__instance, currentTime, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                }
                if (attackerAgent != __instance && attackerAgent != null)
                {
                    if (isMissile)
                    {
                        //attackerAgent.LastRangedHitTime = currentTime;
                        _lastRangedAttackTime.SetValue(attackerAgent, currentTime, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                    }
                    else
                    {
                        //attackerAgent.LastMeleeHitTime = currentTime;
                        _lastMeleeAttackTime.SetValue(attackerAgent, currentTime, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                    }
                }

                if (!__instance.IsHuman)
                {
                    if (__instance.RiderAgent != null)
                    {
                        if (isMissile)
                        {
                            //__instance.LastRecievedRangedHitTime = currentTime;
                            _lastRangedHitTime.SetValue(__instance.RiderAgent, currentTime, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                        }
                        else
                        {
                            //LastRecievedMeleeHitTime = currentTime;
                            _lastMeleeHitTime.SetValue(__instance.RiderAgent, currentTime, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                        }
                    }
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("IsInWater")]
        internal class IsInWaterFix
        {
            private static bool Prefix(ref Agent __instance, ref bool __result)
            {
                if (Mission.Current != null && Mission.Current.IsFieldBattle)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("OnAgentShootMissile")]
        [UsedImplicitly]
        [MBCallback]
        internal class OverrideOnAgentShootMissile
        {
            //private static int _oldMissileSpeed;
            private static bool Prefix(Agent shooterAgent, EquipmentIndex weaponIndex, Vec3 position, ref Vec3 velocity, Mat3 orientation, bool hasRigidBody, bool isPrimaryWeaponShot, int forcedMissileIndex, Mission __instance)
            {
                MissionWeapon missionWeapon = shooterAgent.Equipment[weaponIndex];
                WeaponStatsData[] wsd = missionWeapon.GetWeaponStatsData();

                if (!RBMConfig.RBMConfig.rbmCombatEnabled && (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle && !shooterAgent.IsMainAgent && (wsd[0].WeaponClass == (int)WeaponClass.Javelin || wsd[0].WeaponClass == (int)WeaponClass.ThrowingAxe)))
                {
                    //float shooterSpeed = shooterAgent.MovementVelocity.Normalize();
                    if (!shooterAgent.HasMount)
                    {
                        velocity.z = velocity.z - 1.4f;
                    }
                    else
                    {
                        velocity.z = velocity.z - 2f;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("ChargeDamageCallback")]
        [UsedImplicitly]
        [MBCallback]
        internal class ChargeDamageCallbackPatch
        {
            /// <summary>
            /// Route a charge-induced rout through vanilla's panic path instead of calling Retreat()
            /// directly. Panic() respects CanPanic() (BattleMoraleModel.CanPanicDueToMorale, so
            /// Leadership.LoyaltyAndHonor and the siege-ladder exemption apply) and is turned into the
            /// actual retreat by MissionAgentPanicHandler on the next pre-tick, which also fires
            /// Mission.OnAgentFleeing so the flee-contagion morale wave and its perks run.
            /// </summary>
            private static void PanicFromCharge(Agent victim)
            {
                CommonAIComponent ai = victim.CommonAIComponent;
                if (ai != null && !ai.IsRetreating && !ai.IsPanicked && ai.CanPanic())
                {
                    ai.Panic();
                }
            }

            private static void Postfix(ref AttackCollisionData collisionData, Blow blow, Agent attacker, Agent victim, Mission __instance)
            {
                if (attacker.RiderAgent != null)
                {
                    attacker.RiderAgent.EventControlFlags &= ~Agent.EventControlFlag.DoubleTapToDirectionMask;
                    attacker.RiderAgent.EventControlFlags |= Agent.EventControlFlag.DoubleTapToDirectionUp;
                }
                if (attacker.RiderAgent != null && victim != null && victim.Character != null && Mission.Current != null && (Mission.Current.IsFieldBattle || Mission.Current.IsSallyOutBattle) && attacker.IsEnemyOf(victim))
                {
                    bool isKnockDown = blow.BlowFlag.HasFlag(BlowFlags.KnockDown);
                    bool isKnockBack = blow.BlowFlag.HasFlag(BlowFlags.KnockBack);
                    int victimTier = victim.Character.GetBattleTier();

                    Vec2 blowDirection = blow.Direction.AsVec2.Normalized();
                    Vec2 victimDirection = victim.LookDirection.AsVec2.Normalized();
                    float dot = Vec2.DotProduct(blowDirection, victimDirection);
                    bool isChargedFromBack = dot > 0f;

                    if (isKnockDown)
                    {
                        if (isChargedFromBack)
                        {
                            bool shouldPanic = MBRandom.RandomInt(4) != 0;
                            if (shouldPanic)
                            {
                                PanicFromCharge(victim);
                            }
                        }
                        else
                        {
                            bool shouldPanic = MBRandom.RandomInt(3) == 0;
                            if (shouldPanic)
                            {
                                PanicFromCharge(victim);
                            }
                        }
                    }
                    else if (isKnockBack)
                    {
                        if (isChargedFromBack)
                        {
                            bool shouldPanic = MBRandom.RandomInt(3) == 0;
                            if (shouldPanic)
                            {
                                PanicFromCharge(victim);
                            }
                        }
                        else
                        {
                            int sumModifiers = Math.Max(3, 1 + victimTier);
                            bool shouldPanic = MBRandom.RandomInt(sumModifiers) == 0;
                            if (shouldPanic)
                            {
                                PanicFromCharge(victim);
                            }
                        }
                    }
                    if (!isKnockBack && !isKnockDown)
                    {
                        blow.BaseMagnitude = 0;
                        blow.MovementSpeedDamageModifier = collisionData.MovementSpeedDamageModifier;
                        blow.InflictedDamage = 0;
                        blow.SelfInflictedDamage = 0;
                        blow.AbsorbedByArmor = 0;
                        blow.DamageCalculated = true;
                        blow.BlowFlag |= BlowFlags.KnockBack;
                        WeakGameEntity invalid = WeakGameEntity.Invalid;
                        Blow b = blow;
                        MissionWeapon attackerWeapon = default(MissionWeapon);
                        victim.RegisterBlow(blow, collisionData);
                        foreach (MissionBehavior missionBehaviour in __instance.MissionBehaviors)
                        {
                            missionBehaviour.OnRegisterBlow(attacker, victim, WeakGameEntity.Invalid, blow, ref collisionData, in attackerWeapon);
                        }
                    }
                }
                if (attacker.RiderAgent != null && !attacker.IsEnemyOf(victim) && victim.CurrentMortalityState != Agent.MortalityState.Invulnerable)
                {
                    blow.BaseMagnitude = 0;
                    blow.MovementSpeedDamageModifier = collisionData.MovementSpeedDamageModifier;
                    blow.InflictedDamage = 0;
                    blow.SelfInflictedDamage = 0;
                    blow.AbsorbedByArmor = 0;
                    blow.DamageCalculated = true;
                    blow.BlowFlag |= BlowFlags.KnockBack;
                    WeakGameEntity invalid = WeakGameEntity.Invalid;
                    Blow b = blow;
                    MissionWeapon attackerWeapon = default(MissionWeapon);
                    victim.RegisterBlow(blow, collisionData);
                    foreach (MissionBehavior missionBehaviour in __instance.MissionBehaviors)
                    {
                        missionBehaviour.OnRegisterBlow(attacker, victim, WeakGameEntity.Invalid, blow, ref collisionData, in attackerWeapon);
                    }
                }
                return;
            }
        }
    }
}
