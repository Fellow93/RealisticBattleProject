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
                // v1.5.x: vanilla also stamps the contact times here. FormationQuerySystem.IsUnderRangedAttack
                // reads only those, so without this a formation counts as under fire only from shield-blocked
                // missiles. The public helper uses Mission.CurrentTime, the clock the Formation readers use.
                __instance.UpdateLastRecievedContactTimes(isMissile);
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
                        __instance.RiderAgent.UpdateLastRecievedContactTimes(isMissile);
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Crash guard (report 303BD0): when a siege-engine AI releases its user, vanilla calls
        /// AIDefendGameObjectEnable, which dereferences HumanAIComponent. An agent flipped to AI control
        /// by another mod without AgentHumanAILogic adding the component NREs here, killing the mission.
        /// Drop only the defend step for such agents; the rest of the method runs as vanilla.
        /// Reached from StopUsingGameObjectMT on worker threads, so this must stay read-only.
        /// </summary>
        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("AfterStoppedUsingMissionObject")]
        internal class AfterStoppedUsingMissionObjectGuard
        {
            private static void Prefix(Agent __instance, ref Agent.StopUsingGameObjectFlags flags)
            {
                if (__instance.HumanAIComponent == null)
                {
                    flags &= ~Agent.StopUsingGameObjectFlags.DefendAfterStoppingUsingGameObject;
                }
            }
        }

        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("ChargeDamageCallback")]
        [UsedImplicitly]
        [MBCallback]
        internal class ChargeDamageCallbackPatch
        {
            /// <summary>
            /// A charge knockdown/knockback is a short shock rout, not a morale rout: the victim flees but
            /// stays in his formation, and the HumanAIComponent tick rallies him once he is clear of melee.
            /// CanPanic() still gates it (Leadership.LoyaltyAndHonor, siege-ladder exemption).
            /// Deliberately NOT routed through Panic(): vanilla's flee path (Mission.OnAgentFleeing ->
            /// Agent.OnFleeing) removes the agent from his formation, so a rallied agent came back
            /// formationless - charging on his own and ignoring orders - and it also fires the
            /// flee-contagion morale wave and its perks, which a charge shock should not trigger.
            /// </summary>
            private static void PanicFromCharge(Agent victim)
            {
                CommonAIComponent ai = victim.CommonAIComponent;
                if (ai != null && !ai.IsRetreating && !ai.IsPanicked && ai.CanPanic())
                {
                    // The formation's retreat-position cache reuses a flee point found within 20 m, so a charge that
                    // shocks a dozen men in one pass runs the flee-position search once, not once per man (vanilla's
                    // MovementOrder retreat does the same). It needs a formation to cache on.
                    ai.Retreat(useCachingSystem: victim.Formation != null);
                    OnTickPatch.chargeRoutedAgents.Add(victim);
                }
            }

            // Victim -> mission time of his last synthetic knockback. A horse ploughing through a packed formation
            // bumps the same men several times a second, and each synthetic blow replays the hit animation and runs
            // OnRegisterBlow on every mission behavior; one per victim per cooldown is all the stagger shows anyway.
            public static readonly Dictionary<Agent, float> lastSyntheticKnockback = new Dictionary<Agent, float>();
            private const float SyntheticKnockbackCooldown = 0.5f;
            // A friendly bump slower than this (relative m/s) is a rider easing through his own infantry, not a
            // collision; knocking those men back stalled every shoulder-to-shoulder formation a horseman passed.
            private const float FriendlyKnockbackMinSpeed = 3f;

            private static bool OnSyntheticKnockbackCooldown(Agent victim, float now)
            {
                return lastSyntheticKnockback.TryGetValue(victim, out float last) && now - last < SyntheticKnockbackCooldown;
            }

            // A 0-damage knockback blow: the charge staggers the victim even where vanilla's knockback model or its
            // friendly early-out leaves the horse passing through him untouched.
            private static void RegisterSyntheticKnockback(Mission mission, ref AttackCollisionData collisionData, Blow blow, Agent attacker, Agent victim, float now)
            {
                lastSyntheticKnockback[victim] = now;
                blow.BaseMagnitude = 0;
                blow.MovementSpeedDamageModifier = collisionData.MovementSpeedDamageModifier;
                blow.InflictedDamage = 0;
                blow.SelfInflictedDamage = 0;
                blow.AbsorbedByArmor = 0;
                blow.DamageCalculated = true;
                blow.BlowFlag |= BlowFlags.KnockBack;
                MissionWeapon attackerWeapon = default(MissionWeapon);
                victim.RegisterBlow(blow, collisionData);
                foreach (MissionBehavior missionBehaviour in mission.MissionBehaviors)
                {
                    missionBehaviour.OnRegisterBlow(attacker, victim, WeakGameEntity.Invalid, blow, ref collisionData, in attackerWeapon);
                }
            }

            private static void Postfix(ref AttackCollisionData collisionData, Blow blow, Agent attacker, Agent victim, Mission __instance)
            {
                Agent rider = attacker.RiderAgent;
                if (rider != null)
                {
                    // One native read and at most one write, instead of two read-modify-write round trips.
                    Agent.EventControlFlag flags = rider.EventControlFlags;
                    Agent.EventControlFlag newFlags = (flags & ~Agent.EventControlFlag.DoubleTapToDirectionMask) | Agent.EventControlFlag.DoubleTapToDirectionUp;
                    if (newFlags != flags)
                    {
                        rider.EventControlFlags = newFlags;
                    }
                }
                // Vanilla has already registered the charge blow by the time this postfix runs, so the charge
                // may have killed the victim. Retreat() and RegisterBlow on a removed agent reach native code.
                if (victim == null || !victim.IsActive())
                {
                    return;
                }
                if (attacker.RiderAgent != null && victim.Character != null && Mission.Current != null && (Mission.Current.IsFieldBattle || Mission.Current.IsSallyOutBattle) && attacker.IsEnemyOf(victim))
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
                        float now = __instance.CurrentTime;
                        if (!OnSyntheticKnockbackCooldown(victim, now))
                        {
                            RegisterSyntheticKnockback(__instance, ref collisionData, blow, attacker, victim, now);
                        }
                    }
                }
                if (attacker.RiderAgent != null && !attacker.IsEnemyOf(victim) && victim.CurrentMortalityState != Agent.MortalityState.Invulnerable)
                {
                    float now = __instance.CurrentTime;
                    if (!OnSyntheticKnockbackCooldown(victim, now) &&
                        (attacker.Velocity.AsVec2 - victim.Velocity.AsVec2).LengthSquared > FriendlyKnockbackMinSpeed * FriendlyKnockbackMinSpeed)
                    {
                        RegisterSyntheticKnockback(__instance, ref collisionData, blow, attacker, victim, now);
                    }
                }
                return;
            }
        }
    }
}
