using HarmonyLib;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.GauntletUI.PrefabSystem;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace RBMCampaign
{
    /// <summary>
    /// The per-man maintenance line under the wage in the party screen's selected-troop panel. The
    /// panel shows a stack's daily wage (<c>CurrentCharacterWageLbl</c>); this sits beneath it with the
    /// stack's daily upkeep, so the two costs of keeping a soldier read together.
    ///
    /// The panel's view model (<c>PartyVM</c>) cannot simply be given a maintenance property: Gauntlet
    /// builds its bindable property table by reflecting the concrete view model type, so a property not
    /// compiled into that class does not exist to a binding. What the selected troop's row DOES publish
    /// is its <c>TroopID</c> (the character's StringId, the same binding the spoils bar takes), so this
    /// widget takes that and looks the maintenance up itself. Mirrors <see cref="RBMItemWeightTextWidget"/>.
    /// </summary>
    public class RBMTroopMaintenanceTextWidget : RichTextWidget
    {
        private const string CoinIcon = "<img src=\"General\\Icons\\Coin@2x\" extend=\"6\">";

        private static readonly Dictionary<string, CharacterObject> _troopCache = new Dictionary<string, CharacterObject>();

        private string _troopId;

        public RBMTroopMaintenanceTextWidget(UIContext context) : base(context)
        {
        }

        /// <summary>Bound to the selected troop's TroopID, i.e. the character's StringId.</summary>
        public string TroopId
        {
            get
            {
                return _troopId;
            }
            set
            {
                if (_troopId != value)
                {
                    _troopId = value;
                    RefreshText();
                }
            }
        }

        private void RefreshText()
        {
            CharacterObject character = Resolve(_troopId);
            int perMan = (character != null) ? SpoilsPool.GetDailyMaintenancePerMan(character) : 0;
            // A hero, a zero-kit troop, or maintenance switched off leaves nothing to show; blank it so
            // the line reads empty rather than "Maintenance: 0".
            if (perMan <= 0)
            {
                Text = "";
                return;
            }
            Text = new TextObject("{=RBM_SPOILS_023}Maintenance: {AMOUNT}")
                .SetTextVariable("AMOUNT", perMan)
                .ToString() + CoinIcon;
        }

        private static CharacterObject Resolve(string troopId)
        {
            if (string.IsNullOrEmpty(troopId))
            {
                return null;
            }
            CharacterObject character;
            if (!_troopCache.TryGetValue(troopId, out character))
            {
                character = MBObjectManager.Instance?.GetObject<CharacterObject>(troopId);
                _troopCache[troopId] = character;
            }
            return character;
        }

        /// <summary>
        /// Both of Gauntlet's type registries scan assemblies once, before a module's assembly is in
        /// the AppDomain, so this type has to be added to each by hand. See
        /// RBMTroopSpoilsBarWidget.RegisterWidgetType for the full reasoning; this is the same dance.
        /// </summary>
        public static void RegisterWidgetType()
        {
            RegisterWidgetInfo();

            WidgetFactory factory = UIResourceManager.WidgetFactory;
            if (factory == null)
            {
                SpoilsLog.Trace("UIResourceManager.WidgetFactory was null; the maintenance label widget type is not registered.");
                return;
            }
            Dictionary<string, Type> builtinTypes = AccessTools.FieldRefAccess<WidgetFactory, Dictionary<string, Type>>("_builtinTypes")(factory);
            builtinTypes[nameof(RBMTroopMaintenanceTextWidget)] = typeof(RBMTroopMaintenanceTextWidget);
            SpoilsLog.Trace("registered widget type " + nameof(RBMTroopMaintenanceTextWidget));
        }

        [HarmonyPatch(typeof(WidgetInfo))]
        [HarmonyPatch("Refresh")]
        private class ReRegisterAfterWidgetInfoRefresh
        {
            private static void Postfix()
            {
                RegisterWidgetInfo();
            }
        }

        private static void RegisterWidgetInfo()
        {
            Dictionary<Type, WidgetInfo> widgetInfos =
                AccessTools.Field(typeof(WidgetInfo), "_widgetInfos").GetValue(null) as Dictionary<Type, WidgetInfo>;
            if (widgetInfos == null)
            {
                // CollectWidgetTypes has not run yet; it will pick the type up on its own.
                return;
            }
            if (!widgetInfos.ContainsKey(typeof(RBMTroopMaintenanceTextWidget)))
            {
                widgetInfos.Add(typeof(RBMTroopMaintenanceTextWidget), new WidgetInfo(typeof(RBMTroopMaintenanceTextWidget)));
                SpoilsLog.Trace("registered widget info for " + nameof(RBMTroopMaintenanceTextWidget));
            }
        }
    }
}
