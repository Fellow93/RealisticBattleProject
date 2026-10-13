using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.GauntletUI.PrefabSystem;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// A company's hire price, drawn under its name in a row of the army management party list. The row's view
    /// model (<c>ArmyManagementItemVM</c>) cannot be given a property, but it already publishes the party's
    /// <c>Clan</c> as a plain property, which Gauntlet binds like any other (it reflects every property of the
    /// view model type), so the injected widget takes <c>Clan="@Clan"</c> and prices the company itself. Empty
    /// for every row that is not a company still to hire.
    /// </summary>
    public class RBMMercenaryHirePriceWidget : TextWidget
    {
        // The price follows the party's wage bill, which only moves as men join or fall; a second is plenty.
        private const float RefreshSeconds = 1f;

        private Clan _clan;
        private float _sinceRefresh;

        public RBMMercenaryHirePriceWidget(UIContext context) : base(context)
        {
        }

        public Clan Clan
        {
            get
            {
                return _clan;
            }
            set
            {
                if (_clan != value)
                {
                    _clan = value;
                    RefreshText();
                }
            }
        }

        protected override void OnUpdate(float dt)
        {
            base.OnUpdate(dt);
            _sinceRefresh += dt;
            if (_sinceRefresh >= RefreshSeconds)
            {
                RefreshText();
            }
        }

        private void RefreshText()
        {
            _sinceRefresh = 0f;
            string text = "";
            if (_clan != null && _clan.Kingdom == null && MercenaryCompanies.IsCompany(_clan))
            {
                MobileParty party = MercenaryCompanies.ActiveParty(_clan);
                if (party != null)
                {
                    TextObject line = new TextObject("{=RBM_merc_row_price_daily}Hire {PRICE}, then {DAILY}/day");
                    line.SetTextVariable("PRICE", MercenaryCompanies.HirePrice(party).ToString("N0"));
                    line.SetTextVariable("DAILY", MercenaryCompanies.DailyPay(party).ToString("N0"));
                    text = line.ToString();
                }
            }
            if (Text != text)
            {
                Text = text;
            }
        }
    }

    /// <summary>
    /// The gold the mercenary companies in the army management cart will cost, against the player's gold, beside
    /// the screen's strength total. Empty when the screen lists no company. Reads
    /// <see cref="MercenaryArmyScreen"/>'s cart total, which the screen recomputes on every cart change.
    /// </summary>
    public class RBMMercenaryCartGoldWidget : TextWidget
    {
        public RBMMercenaryCartGoldWidget(UIContext context) : base(context)
        {
        }

        protected override void OnUpdate(float dt)
        {
            base.OnUpdate(dt);
            // Emptied rather than hidden: an empty CoverChildren text takes no room, and nothing else owns its visibility.
            bool show = MercenaryCompanies.IsEnabled && MercenaryArmyScreen.AnyCompanyListed && Hero.MainHero != null;
            if (!show)
            {
                if (!string.IsNullOrEmpty(Text))
                {
                    Text = "";
                }
                return;
            }
            int gold = Hero.MainHero.Gold;
            int cart = MercenaryArmyScreen.CartGold;
            TextObject line = cart > gold
                ? new TextObject("{=RBM_merc_cart_gold_short}Mercenary hire: {CART} / {GOLD} denars (not enough gold)")
                : new TextObject("{=RBM_merc_cart_gold}Mercenary hire: {CART} / {GOLD} denars");
            line.SetTextVariable("CART", cart.ToString("N0"));
            line.SetTextVariable("GOLD", gold.ToString("N0"));
            string text = line.ToString();
            if (Text != text)
            {
                Text = text;
            }
        }
    }

    /// <summary>
    /// Registers the two widget types with Gauntlet. Both type registries scan assemblies once, before a
    /// module's assembly is loaded, so the types are added by hand (see RBMItemWeightTextWidget for the same).
    /// </summary>
    public static class MercenaryArmyWidgets
    {
        private static readonly Type[] WidgetTypes = { typeof(RBMMercenaryHirePriceWidget), typeof(RBMMercenaryCartGoldWidget) };

        public static void RegisterWidgetTypes()
        {
            RegisterWidgetInfos();
            WidgetFactory factory = UIResourceManager.WidgetFactory;
            if (factory == null)
            {
                SpoilsLog.Trace("UIResourceManager.WidgetFactory was null; the mercenary army widgets are not registered.");
                return;
            }
            Dictionary<string, Type> builtinTypes = AccessTools.FieldRefAccess<WidgetFactory, Dictionary<string, Type>>("_builtinTypes")(factory);
            foreach (Type type in WidgetTypes)
            {
                builtinTypes[type.Name] = type;
            }
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
                return;
            }
            foreach (Type type in WidgetTypes)
            {
                if (!widgetInfos.ContainsKey(type))
                {
                    widgetInfos.Add(type, new WidgetInfo(type));
                }
            }
        }
    }
}
