using HarmonyLib;
using System.Reflection;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    // Per-mission reset of every RBMAI static that holds an Agent or a Formation.
    //
    // Called from RBM.SubModule.OnBeforeMissionBehaviorInitialize, which native invokes at the very top of
    // Mission.AfterStart: on the main thread, before any behavior's EarlyStart, before the first agent spawns and
    // before any AI tick, so none of these collections can be read on a worker thread while they are cleared.
    // It is deliberately NOT a Harmony prefix on Mission.AfterStart: RBMAI's PatchAll only runs from
    // RBMAIPatchLogic.EarlyStart (inside AfterStart), and RBMAiPatcher.FirstPatch unpatches everything on game
    // start/load, so a prefix would miss the first mission after every load.
    //
    // Why it matters: an Agent or Formation left in a static pins its whole mission (Formation -> Team -> Mission,
    // Agent -> Mission) for the rest of the session, and a pinned Formation never runs its finalizer, which is
    // the only thing that drops Formation._simulationFormationTemp (see below).
    public static class MissionStartReset
    {
        // Native keeps one static scratch formation, Formation._simulationFormationTemp, for spawn-frame and
        // order-preview maths (GetUnitPositionWithIndexAccordingToNewOrder). It is only rebuilt when the formation
        // index changes, and only dropped from a real Formation's finalizer, so it survives into the next mission
        // whenever the previous mission's formations have not been garbage-collected yet.
        //
        // The survivor still holds an _orderPosition from the destroyed scene. The first spawn of the next mission
        // runs ResetForSimulation -> SetPositioning(null, Vec2.Forward, 1) -> BatchUnitPositions on that stale
        // WorldPosition, whose navmesh pointer is non-zero but dangling, and BatchFormationUnitPositions throws an
        // AccessViolationException (crash report 2026-09-28: Radagos' hideout re-entered after a lost duel and a reload).
        // Nulling it here makes native build a fresh one inside the current scene.
        private static readonly FieldInfo SimulationFormationTemp =
            AccessTools.Field(typeof(Formation), "_simulationFormationTemp");

        public static void Reset()
        {
            SimulationFormationTemp?.SetValue(null, null);

            // Formation-keyed behavior state.
            OverrideBehaviorAdvance.positionsStorage.Clear();
            OverrideBehaviorAdvance.waitCountStorage.Clear();
            OverrideBehaviorAdvance.advanceTimerStorage.Clear();
            OverrideBehaviorAdvance.advanceScaleStartStorage.Clear();
            OverrideBehaviorAdvance.advanceLastTickStorage.Clear();
            OverrideBehaviorAdvance.archerWaitStartStorage.Clear();
            OverrideBehaviorAdvance.lastDispersalStorage.Clear();
            OverrideBehaviorCautiousAdvance.waitCountShootingStorage.Clear();
            OverrideBehaviorCautiousAdvance.waitCountApproachingStorage.Clear();
            OverrideBehaviorCautiousAdvance.attackerNextStepTime.Clear();
            OverrideBehaviorCautiousAdvance.attackerStepTarget.Clear();
            OverrideBehaviorMountedSkirmish.rotationDirectionDictionary.Clear();
            OverrideBehaviorMountedSkirmish.orbitTargetStorage.Clear();
            OverrideBehaviorDefend.positionsStorage.Clear();
            OverrideBehaviorProtectFlank.sortieStates.Clear();
            OverrideBehaviorHoldHighGround.positionsStorage.Clear();
            OverrideMovementOrder.positionsStorage.Clear();
            AiModule.RbmBehaviors.OverrideBehaviorCharge.cavHoldPositions.Clear();
            AiModule.RbmBehaviors.OverrideBehaviorCharge.skirmisherRetreatPositions.Clear();
            AiModule.RbmBehaviors.OverrideBehaviorCharge.braceLastThreatTime.Clear();
            FormationPaceFix.tightState.Clear();
            RallyLogic.states.Clear();
            AiModule.ReinforcementAnchor.Reset();
            Utilities.significantFormationsCache.Clear();
            Utilities.formationUnitSnapshots.Clear();

            // Agent-keyed state. Some of these were only cleared from MissionCombatantsLogic.EarlyStart, i.e. field
            // battles, so town, arena and other missions without it kept the last battle's agents alive.
            Frontline.aiDecisionCooldownDict.Clear();
            Tactics.agentDamage.Clear();
            AgentAi.OnTickPatch.itemPickupDistanceStorage.Clear();
            AgentAi.OnTickPatch.bannerBearersWithHeldTarget.Clear();
            AgentAi.OnTickPatch.chargeRoutedAgents.Clear();
            AgentAi.OnTickPatch.meleePickupNextScan.Clear();
            AgentAi.OnTickPatch.riderCrowdStates.Clear();
            AgentAi.WeaponPreference.enemyClose.Clear();
            AgentAi.WeaponPreference.nextCheck.Clear();
            AgentAi.WeaponPreference.enemyCloseSince.Clear();
            AgentAi.WeaponPreference.lastEnemyCloseTime.Clear();
            AgentAi.RangedReachGate.holding.Clear();
            AgentAi.RangedReachGate.nextCheck.Clear();
            AgentAi.RangedReachGate.checkedTarget.Clear();
            AgentAi.RangedReachGate.checkedWielded.Clear();
            AgentAi.RangedReachGate.nextTargetCheck.Clear();
            AgentAi.RangedReachGate.noTargetSince.Clear();
            AgentAi.WeaponPreference.loadoutCache.Clear();
            AgentAi.AmmoPickupSafety.dangerSnapshots.Clear();
            AgentAi.ChargeDamageCallbackPatch.lastSyntheticKnockback.Clear();
            StanceLogic.agentsToChangeFormation.Clear();
            StanceLogic.agentsToDropWeapon.Clear();
            StanceLogic.agentsToDropShield.Clear();
            AgentStances.values.Clear();
            // Normally cleared by its OnRemoveBehavior; a stale one would pin the last mission and lock its input.
            PlayerExhaustionLogic.Instance = null;
        }
    }
}
