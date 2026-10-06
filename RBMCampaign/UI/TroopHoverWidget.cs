using HarmonyLib;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.GauntletUI.PrefabSystem;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace RBMCampaign
{
    /// <summary>
    /// Gives each regular-troop row on the party screen the troop tooltip (the card the recruit screen and the
    /// encyclopedia show: tier, upgrade xp, wage, skills, and RBM's maintenance line and troop perks). Vanilla's
    /// party rows have none: hovering one only focuses it. Injected into both PartyTroopTuple.xml and
    /// PartyTroopTupleLeft.xml by <see cref="SpoilsBarPrefabPatch"/>, as a direct child of the row's root button.
    ///
    /// Works the way HintWidget does: it never takes input itself (disabled, DoNotAcceptEvents, zero size), and
    /// instead listens to its PARENT's events. The row button stays the hover, click and drag target, so selecting,
    /// dragging and right-clicking a row are untouched. Moving onto anything inside the row that takes events of its
    /// own (the transfer arrow, the tier/type icons, the upgrade buttons, the spoils and xp bars) ends the row's hover,
    /// so this tooltip makes way for theirs. It also hides on a click, a right-click or a drag.
    ///
    /// Shown for every non-hero troop, with or without perks. Gated on rbmCampaignEnabled and troopPerksEnabled; the
    /// prefab injection is decided once at module load, so a changed setting only adds or removes the widget after a
    /// restart, while <see cref="IsFeatureEnabled"/> is also re-checked on every hover.
    /// </summary>
    public class RBMTroopHoverTooltipWidget : Widget
    {
        private Widget _hookedParent;
        private bool _tooltipShown;

        public RBMTroopHoverTooltipWidget(UIContext context) : base(context)
        {
            IsDisabled = true;
            DoNotAcceptEvents = true;
        }

        // Not "IsEnabled": that name is Widget's own input-enable property.
        public static bool IsFeatureEnabled
        {
            get { return RBMConfig.RBMConfig.rbmCampaignEnabled && RBMConfig.RBMConfig.troopPerksEnabled; }
        }

        /// <summary>Bound to the row's TroopID, i.e. the character's StringId.</summary>
        public string TroopId { get; set; }

        protected override void OnConnectedToRoot()
        {
            base.OnConnectedToRoot();
            if (ParentWidget != null && _hookedParent == null)
            {
                _hookedParent = ParentWidget;
                _hookedParent.EventFire += OnParentEventFired;
            }
        }

        protected override void OnDisconnectedFromRoot()
        {
            HideTooltip();
            if (_hookedParent != null)
            {
                _hookedParent.EventFire -= OnParentEventFired;
                _hookedParent = null;
            }
            base.OnDisconnectedFromRoot();
        }

        private void OnParentEventFired(Widget widget, string eventName, object[] args)
        {
            switch (eventName)
            {
                case "HoverBegin":
                    ShowTooltip();
                    break;
                case "HoverEnd":
                case "MouseDown":
                case "MouseAlternateDown":
                case "DragBegin":
                    HideTooltip();
                    break;
            }
        }

        private void ShowTooltip()
        {
            if (!IsFeatureEnabled || Campaign.Current == null)
            {
                return;
            }
            CharacterObject character = ResolveTroop(TroopId);
            if (character == null || character.IsHero)
            {
                return;
            }
            InformationManager.ShowTooltip(typeof(CharacterObject), character);
            _tooltipShown = true;
        }

        private void HideTooltip()
        {
            if (_tooltipShown)
            {
                _tooltipShown = false;
                MBInformationManager.HideInformations();
            }
        }

        /// <summary>
        /// Looked up on every hover rather than cached: hovers are rare, the lookup is a dictionary read, and a
        /// cache keyed by StringId would hand a later campaign the previous campaign's objects.
        /// </summary>
        private static CharacterObject ResolveTroop(string troopId)
        {
            if (string.IsNullOrEmpty(troopId))
            {
                return null;
            }
            return MBObjectManager.Instance?.GetObject<CharacterObject>(troopId);
        }

        /// <summary>
        /// Both of Gauntlet's type registries scan assemblies once, before a module's assembly is in the
        /// AppDomain, so this type has to be added to each by hand. See
        /// RBMTroopSpoilsBarWidget.RegisterWidgetType for the full reasoning; this is the same dance.
        /// </summary>
        public static void RegisterWidgetType()
        {
            RegisterWidgetInfo();

            WidgetFactory factory = UIResourceManager.WidgetFactory;
            if (factory == null)
            {
                SpoilsLog.Trace("UIResourceManager.WidgetFactory was null; the troop hover widget type is not registered.");
                return;
            }
            Dictionary<string, Type> builtinTypes = AccessTools.FieldRefAccess<WidgetFactory, Dictionary<string, Type>>("_builtinTypes")(factory);
            builtinTypes[nameof(RBMTroopHoverTooltipWidget)] = typeof(RBMTroopHoverTooltipWidget);
            SpoilsLog.Trace("registered widget type " + nameof(RBMTroopHoverTooltipWidget));
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
            if (!widgetInfos.ContainsKey(typeof(RBMTroopHoverTooltipWidget)))
            {
                widgetInfos.Add(typeof(RBMTroopHoverTooltipWidget), new WidgetInfo(typeof(RBMTroopHoverTooltipWidget)));
                SpoilsLog.Trace("registered widget info for " + nameof(RBMTroopHoverTooltipWidget));
            }
        }
    }
}
