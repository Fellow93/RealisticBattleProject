using HarmonyLib;

namespace RBMAutoResolve
{
    /// <summary>
    /// Bootstrap for the Detailed Auto Resolve (the equipment-aware campaign battle simulation). Its own module and
    /// its own Harmony instance (com.rbmautoresolve), patched by RBM.SubModule.ApplyHarmonyPatches whenever
    /// simulationEquipmentEnabled is on -- independent of RBM Campaign and RBM Combat, both of which it reads only
    /// as data (Combat picks the armour equation it prices kit with).
    ///
    /// Plain PatchAll: none of its targets (DefaultCombatSimulationModel, MapEvent, MapEventSide,
    /// DefaultTroopSupplierProbabilityModel, GauntletMapBattleSimulationView, the War Sails combat model) has a static
    /// initializer that reads the game texts, so patching them at module load is safe (see the
    /// GameTextInitializedTypes note in RBMCampaignPatcher). The War Sails patches skip themselves without the DLC
    /// (their Prepare finds no target).
    /// </summary>
    public static class RBMAutoResolvePatcher
    {
        /// <summary>Whether the module is switched on: the Detailed Auto Resolve setting alone.</summary>
        public static bool Enabled
        {
            get { return RBMConfig.RBMConfig.simulationEquipmentEnabled; }
        }

        public static void DoPatching(ref Harmony rbmautoresolveHarmony)
        {
            rbmautoresolveHarmony.PatchAll(typeof(RBMAutoResolvePatcher).Assembly);
        }
    }
}
