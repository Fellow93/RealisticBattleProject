using HarmonyLib;
using SandBox.GauntletUI.Map;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;

namespace RBMCampaign
{
    [HarmonyPatch(typeof(GauntletMapBattleSimulationView), "CreateLayout")]
    internal static class SimulationBattlePanelPatch_CreateLayout
    {
        private static SimulationBattlePanelVM _panelVM;
        private static GauntletLayer _hostLayer;
        private static ScrollbarWidget _scrollbar;
        private static int _lastEventCount;

        internal static SimulationBattlePanelVM PanelVM => _panelVM;
        internal static GauntletLayer HostLayer => _hostLayer;
        internal static ScrollbarWidget Scrollbar => _scrollbar;

        internal static int LastEventCount
        {
            get => _lastEventCount;
            set => _lastEventCount = value;
        }

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
            _scrollbar = null;
            _lastEventCount = 0;
        }

        internal static void Cleanup()
        {
            if (_panelVM != null)
            {
                _panelVM.OnFinalize();
            }
            _panelVM = null;
            _hostLayer = null;
            _scrollbar = null;
            _lastEventCount = 0;
        }
    }

    [HarmonyPatch(typeof(GauntletMapBattleSimulationView), "OnMapScreenUpdate")]
    internal static class SimulationBattlePanelPatch_Tick
    {
        private static void Postfix(float dt)
        {
            SimulationBattlePanelVM vm = SimulationBattlePanelPatch_CreateLayout.PanelVM;
            if (vm == null)
            {
                return;
            }

            vm.Tick(dt);

            int eventCount = vm.Events.Count;
            if (eventCount > SimulationBattlePanelPatch_CreateLayout.LastEventCount)
            {
                SimulationBattlePanelPatch_CreateLayout.LastEventCount = eventCount;
                ScrollToBottom();
            }
        }

        private static void ScrollToBottom()
        {
            ScrollbarWidget scrollbar = SimulationBattlePanelPatch_CreateLayout.Scrollbar;
            if (scrollbar == null)
            {
                GauntletLayer layer = SimulationBattlePanelPatch_CreateLayout.HostLayer;
                if (layer == null)
                {
                    return;
                }
                Widget root = layer.UIContext?.Root;
                if (root == null)
                {
                    return;
                }
                scrollbar = FindScrollbar(root);
                if (scrollbar == null)
                {
                    return;
                }
            }
            scrollbar.ValueFloat = scrollbar.MaxValue;
        }

        private static ScrollbarWidget FindScrollbar(Widget parent)
        {
            if (parent == null)
            {
                return null;
            }
            Widget found = parent.FindChild("ChronicleScrollbar", true);
            if (found is ScrollbarWidget sb)
            {
                return sb;
            }
            return null;
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
