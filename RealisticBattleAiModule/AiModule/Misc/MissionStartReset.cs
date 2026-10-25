using HarmonyLib;
using System.Reflection;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    // Per-mission reset of every RBMAI static that holds an Agent or a Formation (a static pins its whole mission).
    // Called from RBM.SubModule.OnBeforeMissionBehaviorInitialize: main thread, top of Mission.AfterStart, before any
    // spawn or AI tick. NOT a Harmony prefix: RBMAI's PatchAll runs only inside AfterStart and FirstPatch unpatches on
    // game start/load, so a prefix would miss the first mission after every load.
    public static class MissionStartReset
    {
        // Native's static scratch Formation._simulationFormationTemp is only dropped by a real Formation's finalizer, so it can
        // survive into the next mission holding a dangling _orderPosition from the destroyed scene; the first spawn then
        // throws an AccessViolationException in BatchFormationUnitPositions. Nulling it makes native build a fresh one.
        private static readonly FieldInfo SimulationFormationTemp =
            AccessTools.Field(typeof(Formation), "_simulationFormationTemp");

        // A class reading a game member by name in its static initializer throws TypeInitializationException once a game update
        // renames it; Reset runs in EVERY mission, so those clears go through Guarded (logged, the rest still runs).
        private static void Guarded(string step, System.Action clear)
        {
            try
            {
                clear();
            }
            catch (System.Exception e)
            {
                AiTickGuard.Report("MissionStartReset " + step, e, null);
            }
        }

        public static void Reset()
        {
            // AI tick guard failure counts (writes the last mission's totals to the rgl log first).
            AiTickGuard.Clear();
            SimulationFormationTemp?.SetValue(null, null);

            // Formation-keyed behavior state.
            OverrideBehaviorAdvance.positionsStorage.Clear();
            OverrideBehaviorAdvance.waitCountStorage.Clear();
            OverrideBehaviorAdvance.advanceTimerStorage.Clear();
            OverrideBehaviorAdvance.advanceScaleStartStorage.Clear();
            OverrideBehaviorAdvance.advanceLastTickStorage.Clear();
            OverrideBehaviorAdvance.archerWaitStartStorage.Clear();
            OverrideBehaviorAdvance.archerWaitHoldStorage.Clear();
            OverrideBehaviorAdvance.lastDispersalStorage.Clear();
            OverrideBehaviorCautiousAdvance.waitCountShootingStorage.Clear();
            OverrideBehaviorCautiousAdvance.waitCountApproachingStorage.Clear();
            OverrideBehaviorCautiousAdvance.attackerNextStepTime.Clear();
            OverrideBehaviorCautiousAdvance.attackerStepTarget.Clear();
            OverrideBehaviorMountedSkirmish.rotationDirectionDictionary.Clear();
            OverrideBehaviorMountedSkirmish.orbitTargetStorage.Clear();
            OverrideBehaviorMountedSkirmish.velocityCache.Clear();
            OverrideBehaviorMountedSkirmish.mountedThreatCache.Clear();
            OverrideBehaviorDefend.positionsStorage.Clear();
            OverrideBehaviorProtectFlank.sortieStates.Clear();
            OverrideBehaviorProtectFlank.closePostUntil.Clear();
            // Flank guards' cohesion-pull log latch.
            FormationCohesion.Clear();
            OverrideBehaviorHoldHighGround.commits.Clear();
            OverrideMovementOrder.positionsStorage.Clear();
            AiModule.RbmBehaviors.OverrideBehaviorCharge.cavHoldPositions.Clear();
            AiModule.RbmBehaviors.OverrideBehaviorCharge.skirmisherRetreatPositions.Clear();
            AiModule.RbmBehaviors.OverrideBehaviorCharge.braceLastThreatTime.Clear();
            FormationPaceFix.tightState.Clear();
            RallyLogic.states.Clear();
            AiModule.ReinforcementAnchor.Reset();
            Utilities.significantFormationsCache.Clear();
            Utilities.formationUnitSnapshots.Clear();
            Utilities.fightingInMeleeCache.Clear();
            HoldLateCharge.ClearMeleeShareCache();
            Utilities.ClearRangedArrangement();
            Utilities.ClearAttackJoinLatches();

            // Agent-keyed state (also drops the Agents its states hold). Cleared here, not only in field battles' EarlyStart.
            Frontline.aiDecisionCooldownDict.Clear();
            // Step-gate per-mission counters (per-agent state is in the AIDecisionState entries cleared above).
            Frontline.ResetObstacleCounters();
            Tactics.agentDamage.Clear();
            // Tactic- and Team-keyed: per-battle tactic multipliers, registered teams.
            Guarded("TacticSelection", TacticSelection.Clear);
            // Latched army scale and bandit-leader flags (set in EarlyStart, after this).
            ArmyScale.Clear();
            // Team-keyed (a Team pins its Mission too).
            Tactics.TacticDwell.tacticSince.Clear();
            AgentAi.OnTickPatch.itemPickupDistanceStorage.Clear();
            AgentAi.OnTickPatch.chargeRoutedAgents.Clear();
            AgentAi.OnTickPatch.meleePickupNextScan.Clear();
            AgentAi.OnTickPatch.riderCrowdStates.Clear();
            AgentAi.WeaponPreference.enemyClose.Clear();
            AgentAi.WeaponPreference.nextCheck.Clear();
            AgentAi.WeaponPreference.enemyCloseSince.Clear();
            AgentAi.WeaponPreference.lastEnemyCloseTime.Clear();
            AgentAi.WeaponPreference.throwWindow.Clear();
            AgentAi.RangedReachGate.holding.Clear();
            AgentAi.RangedReachGate.nextCheck.Clear();
            AgentAi.RangedReachGate.checkedTarget.Clear();
            AgentAi.RangedReachGate.checkedWielded.Clear();
            AgentAi.RangedReachGate.nextTargetCheck.Clear();
            AgentAi.RangedReachGate.noTargetSince.Clear();
            AgentAi.WeaponPreference.loadoutCache.Clear();
            AgentAi.AmmoPickupSafety.dangerSnapshots.Clear();
            AgentAi.ChargeDamageCallbackPatch.lastSyntheticKnockback.Clear();
            OrderReactionHold.Clear();
            ChargeGuard.Clear();
            // Off until AiBehaviorLogic.AfterStart reopens the log.
            DecisionTrace.Clear();
            // Worker-thread readers; the blocker sets the grid again in EarlyStart.
            DeepWaterSlots.Clear();
            // AI-thread readers; republished after deployment.
            BattleTerrain.Clear();
            TerrainTacticFactors.Clear();
            Guarded("TerrainFlankPlan", TerrainFlankPlan.Clear);
            TerrainSpots.Clear();
            TerrainCavalry.Clear();
            TerrainShape.Clear();
            TerrainChokeHold.Clear();
            TerrainBattlePosition.Clear();
            TerrainHedgehog.Clear();
            TerrainApproach.Clear();
            Guarded("ReactionMonitor", ReactionMonitor.Clear);
            // Formation marker smoothing cache (holds the Mission). Guarded: its patch class reads a Formation field by name.
            Guarded("FormationMarkers", FormationMarkerPatches.Clear);
            // Per-battle diagnostic counters of the AGENTS lines.
            System.Threading.Interlocked.Exchange(ref OverrideParallelFormationMovement.CallCount, 0L);
            System.Threading.Interlocked.Exchange(ref OverrideParallelFormationMovement.SetCount, 0L);
            System.Threading.Interlocked.Exchange(ref FormationCatchUpGate.CapCount, 0L);
            // The posture/stamina statics are RBMStance's own (RBMStance.StanceReset), reset from the same SubModule hook.
        }
    }
}
