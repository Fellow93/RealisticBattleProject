using HarmonyLib;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.ExtraWidgets;
using TaleWorlds.GauntletUI.PrefabSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace RBMCampaign
{
    /// <summary>
    /// The gear counterpart of the party screen's troop xp bar. It fills as the men of a stack
    /// accumulate enough looted kit to cover their next upgrade; a full bar means that upgrade
    /// costs no gold at all.
    /// </summary>
    public class RBMTroopGearBarWidget : FillBarVerticalWidget
    {
        // Coverage is a fraction, and FillBarVerticalWidget counts in whole numbers.
        private const int FillResolution = 1000;

        private static readonly Dictionary<string, CharacterObject> _troopCache = new Dictionary<string, CharacterObject>();

        private readonly BasicTooltipViewModel _tooltip;

        public RBMTroopGearBarWidget(UIContext context) : base(context)
        {
            MaxAmount = FillResolution;
            IsDirectionUpward = true;
            // The xp bar reaches its tooltip through a HintWidget bound to a view model property.
            // This widget has no view model of its own, so it drives the same tooltip type directly.
            _tooltip = new BasicTooltipViewModel(BuildTooltip);
            GearLog.TraceOnce("widget-ctor", "gear bar widget constructed");
        }

        protected override void OnHoverBegin()
        {
            base.OnHoverBegin();
            if (IsVisible)
            {
                _tooltip.ExecuteBeginHint();
            }
        }

        protected override void OnHoverEnd()
        {
            base.OnHoverEnd();
            // Unconditional: a tooltip shown before the bar hid itself must still be dismissed.
            _tooltip.ExecuteEndHint();
        }

        private List<TooltipProperty> BuildTooltip()
        {
            List<TooltipProperty> properties = new List<TooltipProperty>();
            CharacterObject character = ResolveTroop(TroopId);
            if (character == null || character.UpgradeTargets.Length == 0 || Campaign.Current == null)
            {
                return properties;
            }

            PartyBase party = PartyBase.MainParty;
            properties.Add(new TooltipProperty(new TextObject("{=RBM_GEAR_001}Gear Stockpile").ToString(),
                GearPool.GetAvailableGear(party, character).ToString(), 0));

            // A branching troop has an upgrade cost per branch, so one number could only ever describe
            // the branch the template happens to list first. Name them all and let the stockpile speak
            // for itself against each.
            properties.Add(new TooltipProperty(new TextObject("{=RBM_GEAR_002}Gear per Upgrade").ToString(), "", 0,
                false, TooltipProperty.TooltipPropertyFlags.Title));
            foreach (CharacterObject upgradeTarget in character.UpgradeTargets)
            {
                int gearCost = GearPool.GetGearCostForUpgrade(character, upgradeTarget);
                int freeUpgrades = GearPool.GetFreeUpgradeCount(party, character, upgradeTarget);
                TextObject value = new TextObject((freeUpgrades > 0)
                    ? "{=RBM_GEAR_008}{COST}  ({FREE} free)"
                    : "{=!}{COST}");
                value.SetTextVariable("COST", gearCost);
                value.SetTextVariable("FREE", freeUpgrades);
                properties.Add(new TooltipProperty(upgradeTarget.Name.ToString(), value.ToString(), 0));
            }

            properties.Add(new TooltipProperty("", new TextObject("{=RBM_GEAR_004}Holding the field earns gear salvaged from the kit left on it, by the enemies you killed and by your own fallen. Nothing is recovered whole: armour is battered, blades are chipped, and a quiver is worth only the arrows still in it. A soldier takes only kit of his own tier or better, and the veterans pick first, so what they pass over falls to greener troops. The stockpile outfits men one at a time: those it covers upgrade for free, and the rest pay gold for what it cannot reach.").ToString(), 0, false, TooltipProperty.TooltipPropertyFlags.MultiLine));
            return properties;
        }

        /// <summary>Bound to the view model's TroopID, which is the troop's CharacterObject.StringId.</summary>
        public string TroopId { get; set; }

        /// <summary>
        /// Bound to the view model's IsUpgradableTroop. The widget owns its own IsVisible rather
        /// than binding it, because a one-way binding only pushes when the source property changes:
        /// once this widget cleared IsVisible for its own reasons, nothing would ever set it back.
        /// </summary>
        public bool IsTroopUpgradable { get; set; }

        protected override void OnUpdate(float dt)
        {
            base.OnUpdate(dt);
            Refresh();
        }

        private void Refresh()
        {
            CharacterObject character = ResolveTroop(TroopId);
            bool hasUpgrade = character != null && character.UpgradeTargets.Length > 0;
            IsVisible = IsTroopUpgradable && hasUpgrade && GearPool.IsEnabled && Campaign.Current != null;
            if (!IsVisible)
            {
                return;
            }

            PartyBase party = PartyBase.MainParty;
            CharacterObject upgradeTarget = character.UpgradeTargets[0];
            int gearCost = GearPool.GetGearCostForUpgrade(character, upgradeTarget);
            int stockpile = GearPool.GetAvailableGear(party, character);
            int stackSize = GearPool.GetStackSize(party, character);

            // Mirrors the xp bar: it fills toward the next man's upgrade and saturates once the whole
            // stack is covered, rather than showing the stockpile against some arbitrary ceiling.
            if (gearCost > 0)
            {
                MaxAmount = gearCost;
                InitialAmount = (stockpile >= gearCost * stackSize) ? gearCost : (stockpile % gearCost);
            }
            else
            {
                MaxAmount = FillResolution;
                InitialAmount = 0;
            }

            GearLog.TraceOnce("troop-" + character.StringId, string.Concat(
                character.StringId, " (tier ", character.Tier.ToString(), ") -> ", upgradeTarget.StringId,
                " | equip ", GearPool.GetEquipmentValue(character).ToString(),
                " -> ", GearPool.GetEquipmentValue(upgradeTarget).ToString(),
                " | stockpile ", stockpile.ToString(), "/", gearCost.ToString(), " per man",
                " | stack ", stackSize.ToString(),
                " | free ", GearPool.GetFreeUpgradeCount(party, character, upgradeTarget).ToString(),
                " | nextManGold ", character.GetUpgradeGoldCost(party, 0).ToString()));
        }

        private static CharacterObject ResolveTroop(string troopId)
        {
            if (string.IsNullOrEmpty(troopId))
            {
                return null;
            }
            CharacterObject character;
            if (!_troopCache.TryGetValue(troopId, out character))
            {
                character = MBObjectManager.Instance.GetObject<CharacterObject>(troopId);
                _troopCache[troopId] = character;
            }
            return character;
        }

        /// <summary>
        /// Both of Gauntlet's type registries scan assemblies once, before a module's assembly is
        /// in the AppDomain, so this type has to be added to each by hand.
        ///
        /// WidgetFactory._builtinTypes resolves the prefab tag; without it an unknown tag silently
        /// degrades to a plain invisible Widget. WidgetInfo._widgetInfos is read by the Widget base
        /// constructor with an unguarded indexer, so without it construction throws
        /// KeyNotFoundException. It also records that OnUpdate is overridden, which is what makes
        /// Gauntlet tick this widget at all.
        /// </summary>
        public static void RegisterWidgetType()
        {
            RegisterWidgetInfo();

            WidgetFactory factory = UIResourceManager.WidgetFactory;
            if (factory == null)
            {
                GearLog.Trace("UIResourceManager.WidgetFactory was null; the gear bar widget type is not registered.");
                return;
            }
            Dictionary<string, Type> builtinTypes = AccessTools.FieldRefAccess<WidgetFactory, Dictionary<string, Type>>("_builtinTypes")(factory);
            builtinTypes[nameof(RBMTroopGearBarWidget)] = typeof(RBMTroopGearBarWidget);
            GearLog.Trace("registered widget type " + nameof(RBMTroopGearBarWidget));
        }

        /// <summary>
        /// WidgetInfo.Refresh throws the registry away and rebuilds it by scanning assemblies, which
        /// would drop this type again and crash the next Widget construction.
        /// </summary>
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
                GearLog.Trace("WidgetInfo registry not built yet; skipping widget info registration.");
                return;
            }
            if (!widgetInfos.ContainsKey(typeof(RBMTroopGearBarWidget)))
            {
                widgetInfos.Add(typeof(RBMTroopGearBarWidget), new WidgetInfo(typeof(RBMTroopGearBarWidget)));
                GearLog.Trace("registered widget info for " + nameof(RBMTroopGearBarWidget));
            }
        }
    }
}
