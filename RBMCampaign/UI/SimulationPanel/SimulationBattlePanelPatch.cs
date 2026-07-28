using HarmonyLib;
using SandBox.GauntletUI.Map;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace RBMCampaign
{
    [HarmonyPatch(typeof(GauntletMapBattleSimulationView), "CreateLayout")]
    internal static class SimulationBattlePanelPatch_CreateLayout
    {
        private static SimulationBattlePanelVM _panelVM;
        private static GauntletLayer _hostLayer;

        internal static SimulationBattlePanelVM PanelVM => _panelVM;

        private static void Postfix(GauntletMapBattleSimulationView __instance,
            GauntletLayer ____layerAsGauntletLayer)
        {
            if (!SimulationEquipmentPower.SimulationEnabled)
            {
                return;
            }

            MapEvent mapEvent = MobileParty.MainParty?.MapEvent;
            if (mapEvent == null || ____layerAsGauntletLayer == null)
            {
                return;
            }

            _panelVM = new SimulationBattlePanelVM(mapEvent);
            _hostLayer = ____layerAsGauntletLayer;
            _hostLayer.LoadMovie("SimulationBattlePanel", _panelVM);
        }

        internal static void Cleanup()
        {
            if (_panelVM != null)
            {
                _panelVM.OnFinalize();
            }
            _panelVM = null;
            _hostLayer = null;
        }
    }

    [HarmonyPatch(typeof(GauntletMapBattleSimulationView), "OnMapScreenUpdate")]
    internal static class SimulationBattlePanelPatch_Tick
    {
        private static void Postfix(float dt)
        {
            SimulationBattlePanelVM vm = SimulationBattlePanelPatch_CreateLayout.PanelVM;
            if (vm != null)
            {
                vm.Tick(dt);
            }
        }
    }

    [HarmonyPatch(typeof(GauntletMapBattleSimulationView), "OnFinalize")]
    internal static class SimulationBattlePanelPatch_Finalize
    {
        private static void Prefix()
        {
            SimulationBattlePanelPatch_CreateLayout.Cleanup();
        }
    }
}
