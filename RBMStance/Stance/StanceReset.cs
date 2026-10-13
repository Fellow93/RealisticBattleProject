namespace RBMStance
{
    // Per-mission reset of every RBMStance static that holds an Agent or a mission behavior.
    //
    // Called unconditionally from RBM.SubModule.OnBeforeMissionBehaviorInitialize (next to RBMAI's MissionStartReset),
    // which native invokes at the very top of Mission.AfterStart: on the main thread, before any behavior's EarlyStart,
    // before the first agent spawns and before any AI tick, so nothing here can be read on a worker thread while it is
    // cleared (AgentStances.values is also read by RBMAI's Frontline job). Unconditional so that switching the module
    // off mid-session still releases the previous mission's agents. Deliberately not a Harmony patch: a patch would not
    // run with the module off, which is exactly when stale state must still be dropped.
    //
    // An Agent left in a static pins its whole mission (Agent -> Mission) for the rest of the session.
    public static class StanceReset
    {
        public static void Reset()
        {
            StanceLogic.agentsToChangeFormation.Clear();
            StanceLogic.agentsToDropWeapon.Clear();
            StanceLogic.agentsToDropShield.Clear();
            AgentStances.values.Clear();
            // Normally cleared by their OnRemoveBehavior; a stale one would pin the last mission (and, for the
            // exhaustion logic, lock its input).
            AgentStances.postureVisual = null;
            PlayerExhaustionLogic.Instance = null;
            HitStopLogic.ResetStatics();
            KickBash.AiKickBashComponent.ResetStatics();
        }
    }
}
