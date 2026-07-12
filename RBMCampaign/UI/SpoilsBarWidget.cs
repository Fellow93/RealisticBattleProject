using HarmonyLib;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
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
    /// The spoils counterpart of the party screen's troop xp bar. It fills as the men of a stack
    /// accumulate enough looted kit to cover their next upgrade; a full bar means that upgrade costs
    /// no gold at all. A stack with no upgrade left to buy still has a purse and still fills a bar,
    /// measured against the worth of the kit one of its men is already wearing.
    /// </summary>
    public class RBMTroopSpoilsBarWidget : FillBarVerticalWidget
    {
        // Coverage is a fraction, and FillBarVerticalWidget counts in whole numbers.
        private const int FillResolution = 1000;

        private static readonly Dictionary<string, CharacterObject> _troopCache = new Dictionary<string, CharacterObject>();

        private readonly BasicTooltipViewModel _tooltip;

        // A shown hint is a one-shot snapshot: BuildTooltip runs once on hover-begin and nothing polls
        // it after. Staging an upgrade from the party screen changes the stockpile (staged spoils are
        // subtracted live), so a tooltip the player is still hovering keeps stale numbers. These track
        // whether this bar's tooltip is currently on screen and the figures it was last built with, so
        // OnUpdate can replay the player's own off-and-back workaround the moment they change.
        private bool _tooltipShown;
        private long _tooltipSignature;

        public RBMTroopSpoilsBarWidget(UIContext context) : base(context)
        {
            MaxAmount = FillResolution;
            IsDirectionUpward = true;
            // The xp bar reaches its tooltip through a HintWidget bound to a view model property.
            // This widget has no view model of its own, so it drives the same tooltip type directly.
            _tooltip = new BasicTooltipViewModel(BuildTooltip);
            SpoilsLog.TraceOnce("widget-ctor", "spoils bar widget constructed");
        }

        protected override void OnHoverBegin()
        {
            base.OnHoverBegin();
            if (IsVisible)
            {
                _tooltip.ExecuteBeginHint();
                _tooltipShown = true;
                _tooltipSignature = ComputeTooltipSignature();
            }
        }

        protected override void OnHoverEnd()
        {
            base.OnHoverEnd();
            // Unconditional: a tooltip shown before the bar hid itself must still be dismissed.
            _tooltip.ExecuteEndHint();
            _tooltipShown = false;
        }

        /// <summary>
        /// Everything the tooltip prints that can change while it is on screen: the stockpile and the
        /// stack size (which the "free" upgrade counts are drawn from). Equipment values and costs are
        /// static, so this pair is enough to tell a stale tooltip from a current one.
        /// </summary>
        private long ComputeTooltipSignature()
        {
            CharacterObject character = ResolveTroop(TroopId);
            if (character == null || Campaign.Current == null)
            {
                return 0;
            }
            PartyBase party = PartyBase.MainParty;
            return ((long)SpoilsPool.GetAvailableSpoils(party, character) << 20)
                ^ (uint)SpoilsPool.GetStackSize(party, character);
        }

        private List<TooltipProperty> BuildTooltip()
        {
            List<TooltipProperty> properties = new List<TooltipProperty>();
            CharacterObject character = ResolveTroop(TroopId);
            if (character == null || Campaign.Current == null)
            {
                return properties;
            }

            PartyBase party = PartyBase.MainParty;
            properties.Add(new TooltipProperty(new TextObject("{=RBM_SPOILS_001}Spoils Stockpile").ToString(),
                SpoilsPool.GetAvailableSpoils(party, character).ToString(), 0));

            // The soft cap this stack is measured against: the days of keep it will hold before its
            // upkeep spends the surplus on food and drink. A behavioural threshold, not a hard ceiling --
            // a purse may sit above it -- so it is named plainly beside the stockpile the player reads it against.
            int cap = SpoilsPool.GetSpoilsCap(party, character);
            if (cap > 0)
            {
                properties.Add(new TooltipProperty(new TextObject("{=RBM_SPOILS_018}Spoils Reserve").ToString(),
                    cap.ToString(), 0));
            }

            // A branching troop has an upgrade cost per branch, so one number could only ever describe
            // the branch the template happens to list first. Name them all and let the stockpile speak
            // for itself against each.
            if (character.UpgradeTargets.Length > 0)
            {
                properties.Add(new TooltipProperty(new TextObject("{=RBM_SPOILS_002}Spoils per Upgrade").ToString(), "", 0,
                    false, TooltipProperty.TooltipPropertyFlags.Title));
                foreach (CharacterObject upgradeTarget in character.UpgradeTargets)
                {
                    int spoilsCost = SpoilsPool.GetSpoilsCostForUpgrade(party, character, upgradeTarget);
                    int freeUpgrades = SpoilsPool.GetFreeUpgradeCount(party, character, upgradeTarget);
                    TextObject value = new TextObject((freeUpgrades > 0)
                        ? "{=RBM_SPOILS_008}{COST}  ({FREE} free)"
                        : "{=!}{COST}");
                    value.SetTextVariable("COST", spoilsCost);
                    value.SetTextVariable("FREE", freeUpgrades);
                    properties.Add(new TooltipProperty(upgradeTarget.Name.ToString(), value.ToString(), 0));
                }
            }
            else
            {
                // The bar has no upgrade to fill toward, so it says what it is filling toward instead.
                properties.Add(new TooltipProperty(new TextObject("{=RBM_SPOILS_012}A Man's Kit Is Worth").ToString(),
                    SpoilsPool.GetEquipmentValue(character).ToString(), 0));
            }

            properties.Add(new TooltipProperty("", new TextObject("{=RBM_SPOILS_004}Holding the field earns spoils salvaged from the kit left on it, by the enemies you killed and by your own fallen. Nothing is recovered whole: armour is battered, blades are chipped, and a quiver is worth only the arrows still in it. The veterans pick first, and the further beneath a man a piece lies the likelier he is to step over it, so what they overlook falls to greener troops. What his men do not spend on their own upgrades they spend on food and drink in the settlements you stop in.").ToString(), 0, false, TooltipProperty.TooltipPropertyFlags.MultiLine));
            return properties;
        }

        /// <summary>Bound to the view model's TroopID, which is the troop's CharacterObject.StringId.</summary>
        public string TroopId { get; set; }

        /// <summary>
        /// Bound to the view model's IsUpgradableTroop, and no longer read: the bar belongs to every
        /// stack, upgradable or not. Kept because the prefab patch sets the attribute, and Gauntlet
        /// throws on a binding whose target property does not exist.
        /// </summary>
        /// <remarks>
        /// The widget owns its own IsVisible rather than binding it, because a one-way binding only
        /// pushes when the source property changes: once this widget cleared IsVisible for its own
        /// reasons, nothing would ever set it back.
        /// </remarks>
        public bool IsTroopUpgradable { get; set; }

        protected override void OnUpdate(float dt)
        {
            base.OnUpdate(dt);
            Refresh();
        }

        private void Refresh()
        {
            CharacterObject character = ResolveTroop(TroopId);
            // A troop with nowhere left to upgrade still carries a purse, and still spends it on his
            // bread and his beer, so the bar is his too. Neither IsTroopUpgradable nor an upgrade
            // target gates it: only whether there is a troop, and a spoils system to speak for.
            IsVisible = character != null && SpoilsPool.IsEnabled && Campaign.Current != null;
            if (!IsVisible)
            {
                return;
            }

            // If the player is hovering this bar when its numbers move — an upgrade staged from the
            // party screen draws the stockpile down at once — the tooltip on screen is now stale.
            // Replay their own off-and-back workaround so it shows the live figures without the cursor
            // having to leave and return, mirroring the party-screen upgrade arrows.
            if (_tooltipShown)
            {
                long signature = ComputeTooltipSignature();
                if (signature != _tooltipSignature)
                {
                    _tooltipSignature = signature;
                    MBInformationManager.HideInformations();
                    _tooltip.ExecuteBeginHint();
                }
            }

            PartyBase party = PartyBase.MainParty;
            int stockpile = SpoilsPool.GetAvailableSpoils(party, character);
            int spoilsCost = GetPrimarySpoilsCost(party, character);

            if (spoilsCost > 0)
            {
                // Mirrors the xp bar: it fills toward the next man's upgrade and saturates once the
                // whole stack is covered, rather than showing the stockpile against some arbitrary
                // ceiling.
                int stackSize = SpoilsPool.GetStackSize(party, character);
                MaxAmount = spoilsCost;
                InitialAmount = (stockpile >= spoilsCost * stackSize) ? spoilsCost : (stockpile % spoilsCost);
            }
            else
            {
                // Nothing to upgrade to, so the bar is measured against what one of his own men is
                // wearing: a full bar means the stack carries the price of a man's kit in coin.
                MaxAmount = MathF.Max(1, SpoilsPool.GetEquipmentValue(character));
                InitialAmount = MathF.Min(stockpile, MaxAmount);
            }

            SpoilsLog.TraceOnce("troop-" + character.StringId, string.Concat(
                character.StringId, " (tier ", character.Tier.ToString(), ")",
                " | equip ", SpoilsPool.GetEquipmentValue(character).ToString(),
                " | stockpile ", stockpile.ToString(), " against ", MaxAmount.ToString(),
                " | stack ", SpoilsPool.GetStackSize(party, character).ToString()));
        }

        /// <summary>
        /// The upgrade the bar measures itself against: the first branch, the same one the xp bar
        /// takes. Zero when the troop has nowhere to go, or when the branch is worth no more in kit
        /// than what he already wears.
        /// </summary>
        private static int GetPrimarySpoilsCost(PartyBase party, CharacterObject character)
        {
            return character.UpgradeTargets.Length == 0
                ? 0
                : SpoilsPool.GetSpoilsCostForUpgrade(party, character, character.UpgradeTargets[0]);
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
                SpoilsLog.Trace("UIResourceManager.WidgetFactory was null; the spoils bar widget type is not registered.");
                return;
            }
            Dictionary<string, Type> builtinTypes = AccessTools.FieldRefAccess<WidgetFactory, Dictionary<string, Type>>("_builtinTypes")(factory);
            builtinTypes[nameof(RBMTroopSpoilsBarWidget)] = typeof(RBMTroopSpoilsBarWidget);
            SpoilsLog.Trace("registered widget type " + nameof(RBMTroopSpoilsBarWidget));
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
                SpoilsLog.Trace("WidgetInfo registry not built yet; skipping widget info registration.");
                return;
            }
            if (!widgetInfos.ContainsKey(typeof(RBMTroopSpoilsBarWidget)))
            {
                widgetInfos.Add(typeof(RBMTroopSpoilsBarWidget), new WidgetInfo(typeof(RBMTroopSpoilsBarWidget)));
                SpoilsLog.Trace("registered widget info for " + nameof(RBMTroopSpoilsBarWidget));
            }
        }
    }
}
