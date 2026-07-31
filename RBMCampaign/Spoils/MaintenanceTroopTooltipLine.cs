using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Adds a per-man "Maintenance" line to the troop tooltip, directly under the wage line vanilla
    /// already shows, so a soldier's daily upkeep reads beside his daily pay wherever his troop card is
    /// hovered -- the party screen above all. Display only: it reports the figure the daily charge prices
    /// upkeep at (see <see cref="SpoilsPool.GetDailyMaintenancePerMan"/>) and touches no purse. Off when
    /// the spoils economy or maintenance itself is off.
    /// </summary>
    public static class MaintenanceTroopTooltipLine
    {
        private const string CoinIcon = "{=!}<img src=\"General\\Icons\\Coin@2x\" extend=\"6\">";

        [HarmonyPatch(typeof(TooltipRefresherCollection), "RefreshCharacterTooltip")]
        private class AddMaintenanceLine
        {
            private static void Postfix(PropertyBasedTooltipVM propertyBasedTooltipVM, object[] args)
            {
                if (propertyBasedTooltipVM == null || !SpoilsPool.IsEnabled
                    || RBMConfig.RBMConfig.troopMaintenanceFraction <= 0f)
                {
                    return;
                }
                CharacterObject character = (args != null && args.Length > 0) ? args[0] as CharacterObject : null;
                if (character == null)
                {
                    return;
                }
                int perMan = SpoilsPool.GetDailyMaintenancePerMan(character);
                if (perMan <= 0)
                {
                    return;
                }

                // Built exactly as vanilla builds the wage line just above it (RefreshCharacterTooltip), so
                // the two read as a matched pair: "{Label}: {N} {coin}".
                GameTexts.SetVariable("LEFT", new TextObject("{=RBM_SPOILS_022}Maintenance"));
                GameTexts.SetVariable("STR1", perMan);
                GameTexts.SetVariable("STR2", CoinIcon);
                GameTexts.SetVariable("RIGHT", GameTexts.FindText("str_STR1_space_STR2"));
                string line = GameTexts.FindText("str_LEFT_colon_RIGHT_wSpaceAfterColon").ToString();
                TooltipProperty property = new TooltipProperty("", line, 0);

                // Slot it right under the wage line when there is one, so it sits with the pay rather than at
                // the foot of the skills. The wage line is the only property whose value opens with the
                // localized wage label; for the rare wageless troop, fall back to the first blank spacer
                // (the gap before "Skills") so it still lands above the skill list.
                MBBindingList<TooltipProperty> list = propertyBasedTooltipVM.TooltipPropertyList;
                int insertAt = list.Count;
                string wageLabel = GameTexts.FindText("str_wage").ToString();
                int firstBlank = -1;
                for (int i = 0; i < list.Count; i++)
                {
                    string value = list[i].ValueLabel;
                    if (!string.IsNullOrEmpty(value) && value.StartsWith(wageLabel))
                    {
                        insertAt = i + 1;
                        firstBlank = -1;
                        break;
                    }
                    if (firstBlank < 0 && string.IsNullOrEmpty(value) && string.IsNullOrEmpty(list[i].DefinitionLabel))
                    {
                        firstBlank = i;
                    }
                }
                if (firstBlank >= 0)
                {
                    insertAt = firstBlank;
                }
                list.Insert(insertAt, property);
            }
        }
    }
}
