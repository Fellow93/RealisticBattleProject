using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBM
{
    internal class CustomBattleHintVM : ViewModel
    {
        // The hotkey strip the custom-battle overlay prints. Gauntlet never resolves a {=id} marker
        // inside a prefab's Text=, so the prefab binds this instead.
        [DataSourceProperty]
        public string HotkeyHint =>
            new TextObject("{=RBM_CB_HOTKEY_HINT}Ctrl+S: Save Preset   |   Ctrl+L: Load Preset").ToString();
    }
}
