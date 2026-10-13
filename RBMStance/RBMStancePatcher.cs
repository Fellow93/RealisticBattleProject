using HarmonyLib;

namespace RBMStance
{
    /// <summary>
    /// Bootstrap for RBM Stance: the posture and stamina system and AI kick / shield bash / weapon bash. Its own module
    /// and its own Harmony instance (com.rbmstance), patched by RBM.SubModule.ApplyHarmonyPatches whenever
    /// rbmStanceEnabled is on -- independent of RBM AI (it no longer lives in RBMAI) and of RBM Combat. Off = none of
    /// its patches installed and none of its mission behaviors added. The posture/stamina/kick sub-options are read at
    /// run time.
    ///
    /// Applied after RBMCombat's: several targets are shared (Mission.CreateMeleeBlow, MeleeHitCallback,
    /// OnAgentShootMissile, Agent.EquipItemsFromSpawnEquipment, OnWieldedItemIndexChange,
    /// AgentStatCalculateModel.SetAiRelatedProperties) and the stance patches carry [HarmonyAfter("com.rbmcombat")]
    /// so they keep running after RBMCombat's, as they did while RBMAI (patched per mission) hosted them.
    ///
    /// Plain PatchAll at module load, like RBMCombat: the original bodies of the targets RBMCombat does not already
    /// patch (Agent.WieldInitialWeapons, Agent.OnWeaponReloadPhaseChange, MissionState.LoadMission/OnDeactivate,
    /// TournamentRound.EndMatch, MissionMainAgentController.ControlTick) read no static of a type whose initializer
    /// needs the game loaded (see the cctor notes in RBMCampaignPatcher), so recompiling them early is safe.
    /// </summary>
    public static class RBMStancePatcher
    {
        /// <summary>Whether the module is switched on: the RBM Stance setting alone.</summary>
        public static bool Enabled
        {
            get { return RBMConfig.RBMConfig.rbmStanceEnabled; }
        }

        public static void DoPatching(ref Harmony rbmstanceHarmony)
        {
            rbmstanceHarmony.PatchAll(typeof(RBMStancePatcher).Assembly);
        }
    }
}
