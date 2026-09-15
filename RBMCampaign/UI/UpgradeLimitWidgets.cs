using HarmonyLib;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.GauntletUI.PrefabSystem;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// The clan-screen control for a party's daily upgrade-gold cap: a slider paired with an "unlimited"
    /// checkbox, mirroring the native party wage limit beside it. Both widgets subclass their native base
    /// (a slider, a toggle button) and source their state themselves from <see cref="PartyUpgradeBudget"/>
    /// rather than from a view-model property.
    ///
    /// Why not a view-model property: Gauntlet builds a view model's bindable-property table by reflecting
    /// the concrete type, so a property RBM did not compile into <c>ClanFinanceExpenseItemVM</c> does not
    /// exist to a binding. What the panel DOES publish, one level up on <c>ClanPartyItemVM</c>, is the
    /// party itself (<c>Party="@Party"</c>), and Gauntlet passes non-string binding values through
    /// untouched -- so each widget takes the real <see cref="PartyBase"/> and reads/writes the cap directly.
    /// This is the write-back counterpart of the read-only pattern used by the spoils bar and the
    /// maintenance line (see <see cref="RBMTroopSpoilsBarWidget"/>).
    ///
    /// The right panel is a single instance rebound as the selected party changes, so each widget watches
    /// its bound <see cref="Party"/> and reloads the moment it points at a different party -- distinguishing
    /// that reload from a genuine user edit (same party, value moved) by a per-party snapshot.
    /// </summary>
    public static class UpgradeLimitWidgets
    {
        /// <summary>
        /// Both of Gauntlet's type registries scan assemblies once, before RBM's assembly is in the
        /// AppDomain, so these types have to be added to each by hand. See
        /// <see cref="RBMTroopSpoilsBarWidget.RegisterWidgetType"/> for the full reasoning; this is the
        /// same dance for two types at once.
        /// </summary>
        public static void RegisterWidgetTypes()
        {
            RegisterWidgetInfos();

            WidgetFactory factory = UIResourceManager.WidgetFactory;
            if (factory == null)
            {
                SpoilsLog.Trace("UIResourceManager.WidgetFactory was null; the upgrade-limit widget types are not registered.");
                return;
            }
            Dictionary<string, Type> builtinTypes = AccessTools.FieldRefAccess<WidgetFactory, Dictionary<string, Type>>("_builtinTypes")(factory);
            builtinTypes[nameof(RBMUpgradeLimitSliderWidget)] = typeof(RBMUpgradeLimitSliderWidget);
            builtinTypes[nameof(RBMUpgradeLimitToggleWidget)] = typeof(RBMUpgradeLimitToggleWidget);
            SpoilsLog.Trace("registered the upgrade-limit widget types");
        }

        [HarmonyPatch(typeof(WidgetInfo))]
        [HarmonyPatch("Refresh")]
        private class ReRegisterAfterWidgetInfoRefresh
        {
            private static void Postfix()
            {
                RegisterWidgetInfos();
            }
        }

        private static void RegisterWidgetInfos()
        {
            Dictionary<Type, WidgetInfo> widgetInfos =
                AccessTools.Field(typeof(WidgetInfo), "_widgetInfos").GetValue(null) as Dictionary<Type, WidgetInfo>;
            if (widgetInfos == null)
            {
                // CollectWidgetTypes has not run yet; it will pick the types up on its own.
                return;
            }
            if (!widgetInfos.ContainsKey(typeof(RBMUpgradeLimitSliderWidget)))
            {
                widgetInfos.Add(typeof(RBMUpgradeLimitSliderWidget), new WidgetInfo(typeof(RBMUpgradeLimitSliderWidget)));
            }
            if (!widgetInfos.ContainsKey(typeof(RBMUpgradeLimitToggleWidget)))
            {
                widgetInfos.Add(typeof(RBMUpgradeLimitToggleWidget), new WidgetInfo(typeof(RBMUpgradeLimitToggleWidget)));
            }
        }
    }

    /// <summary>
    /// The slider half of the upgrade-budget control. Edits the finite cap in <see cref="PartyUpgradeBudget"/>
    /// while it is enforced, greys itself out and shows "Unlimited" while it is not. Two child text widgets,
    /// found by id, carry the title and the current value.
    /// </summary>
    public class RBMUpgradeLimitSliderWidget : SliderWidget
    {
        // Ids of the two label children this widget owns and drives (see the injected prefab block).
        private const string TitleLabelId = "RBMUpgradeCapTitle";
        private const string ValueLabelId = "RBMUpgradeCapValue";

        private string _loadedForParty;
        private int _lastValue;
        private TextWidget _titleLabel;
        private TextWidget _valueLabel;
        private bool _titleSet;

        public RBMUpgradeLimitSliderWidget(UIContext context) : base(context)
        {
        }

        /// <summary>The party this row is showing, pushed by the <c>Party="@Party"</c> binding.</summary>
        public PartyBase Party { get; set; }

        protected override void OnUpdate(float dt)
        {
            base.OnUpdate(dt);

            PartyBase party = Party;
            if (party == null)
            {
                return;
            }
            string id = party.Id;
            bool unlimited = PartyUpgradeBudget.IsUnlimited(party);

            if (_loadedForParty != id)
            {
                // Rebound to a different party: load its stored cap. Unlock first so the programmatic set
                // is not swallowed by a lock left on from the previous (unlimited) party.
                _loadedForParty = id;
                Locked = false;
                ValueInt = PartyUpgradeBudget.GetFiniteCap(party);
                _lastValue = ValueInt;
            }
            else if (!unlimited && ValueInt != _lastValue)
            {
                // Same party, value moved under the player's drag: persist it.
                _lastValue = ValueInt;
                PartyUpgradeBudget.SetFiniteCap(party, ValueInt);
            }

            // While unlimited, the cap is not enforced, so the slider is greyed and frozen -- exactly as the
            // native wage slider does under its own "unlimited" checkbox.
            IsDisabled = unlimited;
            Locked = unlimited;

            UpdateLabels(unlimited);
        }

        private void UpdateLabels(bool unlimited)
        {
            if (_titleLabel == null)
            {
                _titleLabel = FindChild(TitleLabelId, true) as TextWidget;
            }
            if (_titleLabel != null && !_titleSet)
            {
                _titleLabel.Text = new TextObject("{=RBM_UPGCAP_TITLE}Upgrade Budget / Day").ToString();
                _titleSet = true;
            }

            if (_valueLabel == null)
            {
                _valueLabel = FindChild(ValueLabelId, true) as TextWidget;
            }
            if (_valueLabel != null)
            {
                _valueLabel.Text = unlimited
                    ? new TextObject("{=RBM_UPGCAP_UNLIMITED}Unlimited").ToString()
                    : ValueInt.ToString();
            }
        }
    }

    /// <summary>
    /// The "unlimited" checkbox half of the control. Reflects and toggles the enforced flag in
    /// <see cref="PartyUpgradeBudget"/>; its child label, found by id, reads "Unlimited".
    /// </summary>
    public class RBMUpgradeLimitToggleWidget : ButtonWidget
    {
        private const string LabelId = "RBMUpgradeCapUnlimitedLabel";

        private string _loadedForParty;
        private bool _lastSelected;
        private TextWidget _label;
        private bool _labelSet;

        public RBMUpgradeLimitToggleWidget(UIContext context) : base(context)
        {
        }

        /// <summary>The party this row is showing, pushed by the <c>Party="@Party"</c> binding.</summary>
        public PartyBase Party { get; set; }

        protected override void OnUpdate(float dt)
        {
            base.OnUpdate(dt);

            PartyBase party = Party;
            if (party == null)
            {
                return;
            }
            string id = party.Id;

            if (_loadedForParty != id)
            {
                // Rebound to a different party: show its state. IsSelected == "unlimited".
                _loadedForParty = id;
                IsSelected = PartyUpgradeBudget.IsUnlimited(party);
                _lastSelected = IsSelected;
            }
            else if (IsSelected != _lastSelected)
            {
                // The base ButtonWidget flips IsSelected on click; persist the new state.
                _lastSelected = IsSelected;
                PartyUpgradeBudget.SetUnlimited(party, IsSelected);
            }

            if (_label == null)
            {
                _label = FindChild(LabelId, true) as TextWidget;
            }
            if (_label != null && !_labelSet)
            {
                _label.Text = new TextObject("{=RBM_UPGCAP_UNLIMITED}Unlimited").ToString();
                _labelSet = true;
            }
        }
    }
}
