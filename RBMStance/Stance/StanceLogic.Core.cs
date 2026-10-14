using HarmonyLib;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.TournamentGames;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static RBMStance.PostureDamage;
using static TaleWorlds.Core.ArmorComponent;
using static TaleWorlds.Core.ItemObject;
using static TaleWorlds.MountAndBlade.Agent;

namespace RBMStance
{
    public partial class StanceLogic : MissionLogic
    {
        // UpdateAgentStats is only re-run for an agent when its stamina fraction has moved at least
        // this far from the fraction last applied to its driven properties...
        private const float StaminaRefreshThreshold = 0.05f;
        // ...and never for an agent whose stance is younger than this (seconds of mission time), so a
        // reinforcement wave is never stat-rebuilt while its spawn is still being wired up.
        private const float MinAgeForStatRefresh = 1f;

        private static float currentDt = 0f;
        private static float currentDtToUpdateStaminaHealth = 0f;

        public static MBArrayList<Agent> agentsToDropShield = new MBArrayList<Agent> { };
        public static MBArrayList<Agent> agentsToDropWeapon = new MBArrayList<Agent> { };
        public static Dictionary<Agent, FormationClass> agentsToChangeFormation = new Dictionary<Agent, FormationClass> { };

        private readonly MBArrayList<Agent> _inactiveAgentsBuffer = new MBArrayList<Agent>();
        private readonly MBArrayList<Agent> _dropShieldBuffer = new MBArrayList<Agent>();
        private readonly MBArrayList<Agent> _dropWeaponBuffer = new MBArrayList<Agent>();
        private readonly MBArrayList<Agent> _statRefreshBuffer = new MBArrayList<Agent>();

        public static void TryToDropShield(Agent victimAgent)
        {
            if (!agentsToDropShield.Contains(victimAgent))
            {
                agentsToDropShield.Add(victimAgent);
            }
        }

        // Every agent the posture system holds for the current mission.
        internal static void ClearMissionState()
        {
            AgentStances.values.Clear();
            currentDt = 0f;
            currentDtToUpdateStaminaHealth = 0f;
            agentsToDropShield.Clear();
            agentsToDropWeapon.Clear();
            agentsToChangeFormation.Clear();
        }

        // Drops the queued agents' shield (offhand) or weapon once they are not mid-release; inactive agents just leave the queue.
        private static void ProcessDropQueue(MBArrayList<Agent> queue, MBArrayList<Agent> buffer, bool offhand)
        {
            buffer.Clear();
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                if (queue[i] != null && queue[i].Mission != null && queue[i].IsActive())
                {
                    ActionCodeType currentActionType = queue[i].GetCurrentActionType(1);
                    if (
                        currentActionType == ActionCodeType.ReleaseMelee ||
                        currentActionType == ActionCodeType.ReleaseRanged ||
                        currentActionType == ActionCodeType.ReleaseThrowing ||
                        currentActionType == ActionCodeType.WeaponBash)
                    {
                        continue;
                    }
                    buffer.Add(queue[i]);
                }
                else
                {
                    buffer.Add(queue[i]);
                }
            }
            foreach (Agent agent in buffer)
            {
                if (agent != null && agent.Mission != null && agent.IsActive())
                {
                    EquipmentIndex ei = offhand ? agent.GetOffhandWieldedItemIndex() : agent.GetPrimaryWieldedItemIndex();
                    if (ei != EquipmentIndex.None)
                    {
                        agent.DropItem(ei);
                        agent.UpdateAgentProperties();
                    }
                }
                queue.Remove(agent);
            }
        }

        public static float MissionNow()
        {
            Mission mission = Mission.Current;
            return mission != null ? mission.CurrentTime : 0f;
        }

        [ThreadStatic]
        private static bool _inMeleeHitContext;

        // The strike's momentum left as MeleeHitCallback starts (below 1 once it has cut through someone); a
        // crush-through re-runs the blocked strike with it.
        [ThreadStatic]
        private static float _meleeHitMomentumRemaining;

        // Neither dealt nor took a melee or ranged hit for longer than this.
        private static bool NoRecentHits(Agent agent, float seconds)
        {
            float currentTime = MBCommon.GetTotalMissionTime();
            return currentTime - agent.LastMeleeHitTime > seconds &&
                currentTime - agent.LastRecievedMeleeHitTime > seconds &&
                currentTime - agent.LastRangedHitTime > seconds &&
                currentTime - agent.LastRecievedRangedHitTime > seconds;
        }

        private static bool QualifiesForQuickStaminaRegen(Agent agent, Stance stance)
        {
            if (stance.stamina >= PostureTuning.QuickStaminaRegenBelowShare * stance.maxStamina)
            {
                return false;
            }
            return NoRecentHits(agent, PostureTuning.QuickStaminaRegenNoHitSeconds);
        }

        private static bool QualifiesForQuickPostureRegen(Agent agent, Stance stance, float now)
        {
            if (now - stance.lastPostureEventTime <= PostureTuning.QuickPostureRegenQuietSeconds)
            {
                return false;
            }
            return NoRecentHits(agent, PostureTuning.QuickPostureRegenQuietSeconds);
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow blow)
        {
            base.OnAgentRemoved(affectedAgent, affectorAgent, agentState, blow);
            if (!Mission.Current.IsFieldBattle)
            {
                return;
            }
            //if horse is killed and has certain speed it affect units around
            if (affectedAgent != null && !affectedAgent.IsHuman)
            {
                Agent horse = affectedAgent;
                float speed = horse.MovementVelocity.Length;
                if (speed >= 5f)
                {
                    List<Agent> list = new List<Agent>();
                    Vec2 searchPosition = horse.Position.AsVec2 + horse.GetMovementDirection().Normalized();
                    AgentProximityMap.ProximityMapSearchStruct searchStruct = AgentProximityMap.BeginSearch(Mission.Current, searchPosition, 0f, extendRangeByBiggestAgentCollisionPadding: true);
                    while (searchStruct.LastFoundAgent != null)
                    {
                        Agent lastFoundAgent = searchStruct.LastFoundAgent;
                        // IsHuman: a riderless horse also has HasMount == false, and act_stagger_backward_3
                        // is not in as_horse — the action resolves to -1 and the engine CTDs.
                        if (lastFoundAgent.IsHuman && lastFoundAgent.CurrentMortalityState != Agent.MortalityState.Invulnerable && !lastFoundAgent.HasMount && CanForceReaction(lastFoundAgent))
                        {
                            list.Add(lastFoundAgent);
                        }
                        AgentProximityMap.FindNext(Mission.Current, ref searchStruct);
                    }
                    foreach (Agent agent in list)
                    {
                        agent.SetActionChannel(0, ActionIndexCache.act_stagger_backward_3, actionSpeed: 1f);
                    }
                }
            }
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (RBMConfig.RBMConfig.postureEnabled)
            {
                currentDtToUpdateStaminaHealth += dt;
                currentDt += dt;

                if (currentDt >= PostureTuning.RegenUpdatePeriodSeconds)
                {
                    float regenScale = Math.Min(currentDt, PostureTuning.RegenUpdatePeriodSeconds * PostureTuning.RegenMaxPeriodsPerUpdate)
                        / PostureTuning.RegenUpdatePeriodSeconds;
                    _inactiveAgentsBuffer.Clear();
                    _statRefreshBuffer.Clear();
                    // Stat refresh is AI work: only while AI ticking is on, and only stamina feeds
                    // AgentDrivenProperties (StanceStatsPatch reads it under postureEnabled && staminaEnabled).
                    bool canRefreshStats = RBMConfig.RBMConfig.staminaEnabled && Mission.Current.AllowAiTicking;
                    float now = Mission.Current.CurrentTime;
                    foreach (KeyValuePair<Agent, Stance> entry in AgentStances.values)
                    {
                        if (entry.Key == null)
                        {
                            continue;
                        }
                        // An agent whose Mission is null has already been cleared (Agent.Clear ran);
                        // it must be evicted, never read below.
                        if (entry.Key.Mission == null || !entry.Key.IsActive())
                        {
                            _inactiveAgentsBuffer.Add(entry.Key);
                            continue;
                        }
                        if (entry.Key.IsPlayerControlled)
                        {
                            PushPlayerStanceBars(entry.Value);
                        }
                        if (entry.Value.posture < entry.Value.maxPosture)
                        {
                            if (RBMConfig.RBMConfig.postureGUIEnabled)
                            {
                                if (AgentStances.postureVisual != null && AgentStances.postureVisual._dataSource.ShowEnemyStatus && AgentStances.postureVisual.affectedAgent == entry.Key)
                                {
                                    AgentStances.postureVisual._dataSource.EnemyPosture = (int)entry.Value.posture;
                                    AgentStances.postureVisual._dataSource.EnemyPostureMax = (int)entry.Value.maxPosture;

                                    AgentStances.postureVisual._dataSource.EnemyStamina = (int)entry.Value.stamina;
                                    AgentStances.postureVisual._dataSource.EnemyStaminaMax = (int)entry.Value.maxStamina;
                                }
                            }
                            if (now - entry.Value.lastPostureDamageTime >= PostureTuning.PostureRegenPauseSeconds)
                            {
                                bool quickRegen = QualifiesForQuickPostureRegen(entry.Key, entry.Value, now);
                                entry.Value.TickPostureRegen((quickRegen ? PostureTuning.QuickRegenMultiplier : 1f) * regenScale);
                            }
                        }
                        if (RBMConfig.RBMConfig.staminaEnabled)
                        {
                            if (entry.Value.stamina < entry.Value.maxStamina)
                            {
                                bool quickRegen = QualifiesForQuickStaminaRegen(entry.Key, entry.Value);
                                entry.Value.TickStaminaRegen((quickRegen ? PostureTuning.QuickRegenMultiplier : 1f) * regenScale);
                            }

                            float staminaLevel = entry.Value.stamina / entry.Value.maxStamina;
                            if (currentDtToUpdateStaminaHealth > PostureTuning.StaminaHealthRegenIntervalSeconds)
                            {
                                if (staminaLevel > PostureTuning.StaminaHealthRegenAboveShare)
                                {
                                    entry.Key.Health = Math.Min(entry.Key.HealthLimit, entry.Key.Health + PostureTuning.StaminaHealthRegenAmount);
                                }
                            }

                            // Event-driven driven-properties refresh: only when the stamina fraction
                            // has drifted from what was last applied, and never on a just-built agent.
                            if (canRefreshStats
                                && entry.Key.IsHuman
                                && now - entry.Value.createdAt >= MinAgeForStatRefresh
                                && Math.Abs(staminaLevel - entry.Value.lastAppliedStaminaFraction) >= StaminaRefreshThreshold)
                            {
                                entry.Value.lastAppliedStaminaFraction = staminaLevel;
                                _statRefreshBuffer.Add(entry.Key);
                            }
                        }
                    }
                    // Run outside the dictionary walk: UpdateAgentStats can re-enter the wield patches
                    // that add stance entries.
                    foreach (Agent agent in _statRefreshBuffer)
                    {
                        if (agent.Mission != null && agent.IsActive())
                        {
                            agent.UpdateAgentStats();
                        }
                    }
                    _statRefreshBuffer.Clear();
                    if (currentDtToUpdateStaminaHealth > PostureTuning.StaminaHealthRegenIntervalSeconds)
                    {
                        currentDtToUpdateStaminaHealth -= PostureTuning.StaminaHealthRegenIntervalSeconds;
                    }
                    foreach (Agent agent in _inactiveAgentsBuffer)
                    {
                        Stance removedStance;
                        AgentStances.values.TryRemove(agent, out removedStance);
                    }

                    foreach (KeyValuePair<Agent, FormationClass> entry in agentsToChangeFormation)
                    {
                        if (entry.Key != null && entry.Key.Mission != null && entry.Key.IsActive() && entry.Key.Team != null)
                        {
                            entry.Key.Formation = entry.Key.Team.GetFormation(entry.Value);
                            entry.Key.DisableScriptedMovement();
                        }
                    }
                    agentsToChangeFormation.Clear();

                    ProcessDropQueue(agentsToDropShield, _dropShieldBuffer, offhand: true);
                    ProcessDropQueue(agentsToDropWeapon, _dropWeaponBuffer, offhand: false);

                    currentDt = 0f;
                }
            }
        }

        // The engine registers no blow for a missile stopped by a shield, so OnAgentHit only sees body hits;
        // shield hits are charged in OnMissileHit. The last shield charge is kept so the same missile's
        // pass-through or pilum wound right after it costs nothing more.
        private const float ShieldChargeBodyHitWindowSeconds = 0.5f;
        private int _shieldChargedMissileIndex = -1;
        private Agent _shieldChargedVictim;
        private float _shieldChargedTime;

        private static readonly AccessTools.FieldRef<TaleWorlds.MountAndBlade.Mission, Dictionary<int, TaleWorlds.MountAndBlade.Mission.Missile>> MissilesByIndex =
            AccessTools.FieldRefAccess<TaleWorlds.MountAndBlade.Mission, Dictionary<int, TaleWorlds.MountAndBlade.Mission.Missile>>("_missilesDictionary");

        private static bool MissileCostsPosture(WeaponClass weaponClass)
        {
            switch (weaponClass)
            {
                case WeaponClass.Bow:
                case WeaponClass.Crossbow:
                case WeaponClass.Arrow:
                case WeaponClass.Bolt:
                case WeaponClass.ThrowingKnife:
                case WeaponClass.Javelin:
                case WeaponClass.ThrowingAxe:
                    return true;
                default:
                    return false;
            }
        }

        // Same gate the melee patch uses: enemy hits on an active human with a stance only.
        private static bool TryGetMissilePostureTarget(Agent victim, Agent attacker, out Stance stance)
        {
            stance = null;
            if (!RBMConfig.RBMConfig.postureEnabled || victim == null || !victim.IsActive() || !victim.IsHuman
                || attacker == null || attacker.IsFriendOf(victim))
            {
                return false;
            }
            return AgentStances.values.TryGetValue(victim, out stance) && stance != null;
        }

        private static void ApplyMissilePosture(Agent victim, Agent attacker, Stance stance, WeaponClass weaponClass, float postureLoss, in AttackCollisionData collisionData)
        {
            if (RBMConfig.RBMConfig.staminaEnabled)
            {
                bool heavy = weaponClass == WeaponClass.Javelin || weaponClass == WeaponClass.ThrowingAxe;
                stance.ReduceStamina(postureLoss * (heavy ? 3f : 2f));
            }

            if (postureLoss > 0f)
            {
                float now = MissionNow();
                stance.posture = Math.Max(0f, stance.posture - postureLoss);
                stance.MarkPostureDamage(now);

                //ranged posture break
                if (stance.posture <= 0f)
                {
                    // health is already reduced here but Die() hasn't run yet; don't animate a dying agent
                    if (victim.Health >= 1f)
                    {
                        ForceStaggerAnimation(victim, collisionData, 0.85f, false);
                    }
                    stance.BreakPosture(now, PostureTuning.PostureBreakResetShare);
                }
            }

            AddPostureDamageVisual(attacker, victim);
        }

        public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent, in MissionWeapon affectorWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
        {
            base.OnAgentHit(affectedAgent, affectorAgent, in affectorWeapon, in blow, in attackCollisionData);
            if (!blow.IsMissile || attackCollisionData.AttackBlockedWithShield || affectorWeapon.CurrentUsageItem == null)
            {
                return;
            }
            WeaponClass weaponClass = affectorWeapon.CurrentUsageItem.WeaponClass;
            if (!MissileCostsPosture(weaponClass) || !TryGetMissilePostureTarget(affectedAgent, affectorAgent, out Stance stance))
            {
                return;
            }
            if (blow.WeaponRecord.AffectorWeaponSlotOrMissileIndex == _shieldChargedMissileIndex && affectedAgent == _shieldChargedVictim
                && MissionNow() - _shieldChargedTime <= ShieldChargeBodyHitWindowSeconds)
            {
                return;
            }
            ApplyMissilePosture(affectedAgent, affectorAgent, stance, weaponClass, Math.Max(0f, blow.InflictedDamage), in attackCollisionData);
        }

        // Thrown weapons get the higher shield cap; arrows, bolts and sling stones the minimal one.
        private static float ShieldMissilePostureCap(WeaponClass weaponClass)
        {
            switch (weaponClass)
            {
                case WeaponClass.Bow:
                case WeaponClass.Crossbow:
                case WeaponClass.Arrow:
                case WeaponClass.Bolt:
                case WeaponClass.Sling:
                case WeaponClass.SlingStone:
                    return PostureTuning.ShieldMissilePostureCap;
                case WeaponClass.ThrowingKnife:
                case WeaponClass.Javelin:
                case WeaponClass.ThrowingAxe:
                    return PostureTuning.ShieldThrownPostureCap;
                default:
                    return 0f;
            }
        }

        // Posture lost = the damage done to the shield, capped per missile type. Raised shield only; Arrow Catcher scales it (RBMConfig.ArrowCatcher).
        public override void OnMissileHit(Agent attacker, Agent victim, bool isCanceled, AttackCollisionData collisionData)
        {
            base.OnMissileHit(attacker, victim, isCanceled, collisionData);
            if (isCanceled || !collisionData.AttackBlockedWithShield || collisionData.CollidedWithShieldOnBack)
            {
                return;
            }
            if (!TryGetMissilePostureTarget(victim, attacker, out Stance stance) || Mission == null)
            {
                return;
            }
            int missileIndex = collisionData.AffectorWeaponSlotOrMissileIndex;
            if (!MissilesByIndex(Mission).TryGetValue(missileIndex, out TaleWorlds.MountAndBlade.Mission.Missile missile))
            {
                return;
            }
            WeaponComponentData usage = missile.Weapon.CurrentUsageItem;
            float cap = usage == null ? 0f : ShieldMissilePostureCap(usage.WeaponClass);
            if (cap <= 0f)
            {
                return;
            }
            _shieldChargedMissileIndex = missileIndex;
            _shieldChargedVictim = victim;
            _shieldChargedTime = MissionNow();
            float postureLoss = Math.Min(cap, Math.Max(0f, collisionData.InflictedDamage)) * RBMConfig.ArrowCatcher.GetPostureFactor(victim);
            ApplyMissilePosture(victim, attacker, stance, usage.WeaponClass, postureLoss, in collisionData);
        }
    }
}
