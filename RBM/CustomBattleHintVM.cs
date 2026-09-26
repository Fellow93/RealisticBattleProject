using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBM
{
    internal class CustomBattleHintVM : ViewModel
    {
        // The prefab used to carry this line as literal text, so it stayed English in
        // every language. The key names themselves are not translated on purpose.
        [DataSourceProperty]
        public string HintText =>
            new TextObject("{=RBM_CB_PRESET_HINT}Ctrl+S: Save Preset   |   Ctrl+L: Load Preset").ToString();
    }
}
