using System;
using System.Collections.Generic;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade.ViewModelCollection.EscapeMenu;
using TaleWorlds.ScreenSystem;

namespace RBMCampaign
{
    /// <summary>
    /// Adds an "RBM Ledger" entry to the campaign-map Escape menu (Return to the Game / Campaign
    /// Options / Options / Save / ...), opening the ledger like the Ctrl+Shift+K hotkey. The menu is
    /// a plain List&lt;EscapeMenuItemVM&gt; built by MapScreen.GetEscapeMenuItems(); inserting one item is
    /// text-only -- no icon, brush or sprite -- so unlike the map-bar nav button it cannot render
    /// invisibly. Discovered by RBMCampaignPatcher.DoPatching's PatchAll, so it is present only while
    /// the campaign module is enabled.
    /// </summary>
    [HarmonyPatch(typeof(MapScreen), "GetEscapeMenuItems")]
    public static class RBMLedgerEscapeMenuPatch
    {
        private static void Postfix(MapScreen __instance, ref List<EscapeMenuItemVM> __result)
        {
            if (__result == null)
            {
                return;
            }

            MapScreen mapScreen = __instance;
            EscapeMenuItemVM ledgerItem = new EscapeMenuItemVM(
                new TextObject("{=RBM_LEDGER_TITLE}RBM Ledger"),
                delegate
                {
                    // Close the menu first (as every other item does), then open the ledger on top of
                    // the map -- but never stack a second copy.
                    mapScreen?.CloseEscapeMenu();
                    if (Campaign.Current != null && !(ScreenManager.TopScreen is RBMLedgerScreen))
                    {
                        ScreenManager.PushScreen(new RBMLedgerScreen());
                    }
                },
                null,
                () => new Tuple<bool, TextObject>(false, null));

            // Just under "Return to the Game" so it sits near the top where it is easy to find,
            // above the save/exit block. Guarded so a non-empty list is required for the insert.
            int index = __result.Count > 0 ? 1 : 0;
            __result.Insert(index, ledgerItem);
        }
    }
}
