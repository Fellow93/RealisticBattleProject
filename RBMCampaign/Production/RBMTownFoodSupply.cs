using System.Collections.Generic;
using System.Text;
using Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Makes a town's food stock the food actually sitting in its market, instead of an abstract
    /// number that accumulates alongside it.
    ///
    /// Vanilla keeps <see cref="Fief.FoodStocks"/> as a running total: <c>Town.DailyTick</c> adds a
    /// modelled <c>FoodChange</c> to it every day, where that change comes mostly from a flat 15 plus
    /// <c>(village.GetHearthLevel() + 1) * 6</c> per bound village -- food conjured with no goods
    /// changing hands. The grain a villager party physically hauls into the market is a separate,
    /// much smaller channel that barely matters.
    ///
    /// Here the stock is not stored at all, it is READ off the market roster, and food leaves that
    /// roster only by being eaten -- by the townspeople, priced by household off Prosperity, and by
    /// the garrison and militia, priced by head. The whole chain is physical: village Hearth ->
    /// RBMVillageProduction goods -> villager party -> town market -> eaten.
    ///
    /// Consequences worth knowing, each handled below:
    /// <list type="bullet">
    /// <item>Food-producing buildings, policies and perks would silently stop working, since nothing
    /// reads the modelled production any more. They are instead paid out as real grain.</item>
    /// <item><c>FoodChange</c> can no longer be predicted from a formula, so it is MEASURED as the
    /// day-over-day change in market food.</item>
    /// <item><c>PartyBase.IsStarving</c> is only ever set by <c>Town.DailyTick</c> from a negative
    /// stock, which a roster-backed stock can never be, so it is re-derived from an empty market.</item>
    /// </list>
    ///
    /// Castles are deliberately left on vanilla throughout: <c>Town.AllTowns</c> holds towns only, so
    /// <see cref="ItemConsumptionBehavior"/> never ticks a castle and a castle has no market channel
    /// to replace the abstract one with.
    /// </summary>
    public static class RBMTownFoodSupply
    {
        /// <summary>
        /// Share of a purchase that is fed back into the category's demand, so that buying pressure
        /// raises the price. Vanilla never does this during play -- <c>MarketData.AddDemand</c> is
        /// only reached from market seeding and the player-trade path, so demand is otherwise a pure
        /// function of prosperity and buying pressure has no price effect at all.
        ///
        /// The steady state is worth knowing exactly, because it is not obvious: <c>AddDemand</c>
        /// scales its argument by 0.15 (<c>GetDemandChangeFromValue</c>) and
        /// <c>UpdateSupplyAndDemand</c> decays demand by 0.15 a day toward the baseline, and the two
        /// cancel. Solving <c>D = 0.85D + 0.15E + 0.15VF</c> gives <c>D = E + V*F</c>: a sustained
        /// daily purchase of value <c>V</c> raises demand by the FULL <c>V*F</c>, not a fraction of
        /// it. At 1.0 this term is the price, and the baseline is a rounding error.
        /// </summary>
        private const float DemandFromPurchaseFactor = 1f;

        /// <summary>
        /// Multiplier on a town's food storage limit.
        ///
        /// Vanilla's 300 was sized against vanilla's appetite. RBM towns eat roughly ten times as
        /// much (<c>NumberOfProsperityToEatOneFood</c> at 4 rather than 40), so the same number now
        /// buys a fraction of the reserve it used to: measured at a median ration of 47 a day, 300 is
        /// SIX days of food, and 25 of 57 towns sat pinned at the limit with their markets still
        /// gaining 150 a day. A fief that cannot hold more than a week of grain cannot be besieged
        /// meaningfully, cannot carry a bad season, and shows a granary bar that is full whatever it
        /// actually has.
        ///
        /// Ten puts a typical town at about two months of supply. Applied to the whole of
        /// <c>Town.FoodStocksUpperLimit()</c> rather than to the base figure, so granary buildings
        /// keep their proportional worth instead of being swamped.
        ///
        /// Raised from five to keep step with <see cref="TownStorage.StorageDays"/>, which is what now
        /// decides how much a town can actually hold. The two must agree: this figure is only the
        /// number the granary bar, the prosperity model and the siege AI READ, and if it sat at a month
        /// while the bins held two, every town would report a full granary at half full and no shortage
        /// would ever show.
        /// </summary>
        private const float TownFoodStockScale = 10f;

        // Market food at the end of each town's last daily tick, and the day-over-day change derived
        // from it. Ephemeral -- both are rebuilt within a day of any gap -- but they hold Town
        // references, so they are cleared per session (see ResetForNewSession).
        private static readonly Dictionary<Town, int> _foodAtLastTick = new Dictionary<Town, int>();
        private static readonly Dictionary<Town, float> _measuredFoodChange = new Dictionary<Town, float>();

        // Rations the town wanted on its last tick but the market could not fill.
        private static readonly Dictionary<Town, int> _unmetRations = new Dictionary<Town, int>();

        // Per-roster-version memo for the food count. get_FoodStocks is read constantly (town UI,
        // tooltips, AI target scoring, siege checks) and the uncached form is a full roster scan.
        private static readonly Dictionary<Town, KeyValuePair<int, int>> _foodCountCache = new Dictionary<Town, KeyValuePair<int, int>>();

        internal static void ResetForNewSession()
        {
            _foodAtLastTick.Clear();
            _measuredFoodChange.Clear();
            _unmetRations.Clear();
            _foodCountCache.Clear();
        }

        /// <summary>
        /// Units of food goods in the town's market. Memoised against
        /// <see cref="ItemRoster.VersionNo"/>, which the roster bumps on every change, so the scan
        /// runs once per market mutation rather than once per read.
        /// </summary>
        public static int FoodUnitsInMarket(Town town)
        {
            ItemRoster itemRoster = town.Owner.ItemRoster;
            int version = itemRoster.VersionNo;
            if (_foodCountCache.TryGetValue(town, out KeyValuePair<int, int> cached) && cached.Key == version)
            {
                return cached.Value;
            }

            int units = 0;
            for (int i = itemRoster.Count - 1; i >= 0; i--)
            {
                ItemRosterElement element = itemRoster.GetElementCopyAtIndex(i);
                ItemObject item = element.EquipmentElement.Item;
                if (item != null && item.ItemCategory.Properties == ItemCategory.Property.BonusToFoodStores)
                {
                    units += element.Amount;
                }
            }

            _foodCountCache[town] = new KeyValuePair<int, int>(version, units);
            return units;
        }

        /// <summary>
        /// Reports a town's food stock as the food in its market rather than the stored running
        /// total, so every reader -- the town screen, siege logic, AI target scoring,
        /// <c>Settlement.IsStarving</c> -- sees the real granary.
        ///
        /// Clamped to <see cref="Town.FoodStocksUpperLimit"/> rather than reported raw, for one
        /// specific reason: <c>DefaultSettlementProsperityModel</c> pays a prosperity bonus of
        /// <c>((FoodStocks + FoodChange) - FoodStocksUpperLimit()) * 0.1</c>. A raw roster sum would
        /// hand a town sitting on 2000 grain +170 prosperity per day, which then buys more food.
        /// The clamp also keeps the stock in the 0..300 range the rest of the game was tuned for.
        /// <c>Town.DailyTick</c> still writes to the underlying auto-property; nothing reads it now.
        /// </summary>
        [HarmonyPatch(typeof(Fief), "FoodStocks", MethodType.Getter)]
        private static class FoodStocksGetterPatch
        {
            private static bool Prefix(Fief __instance, ref float __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || !(__instance is Town town) || !town.IsTown)
                {
                    return true;
                }

                __result = MathF.Min(FoodUnitsInMarket(town), town.FoodStocksUpperLimit());
                return false;
            }
        }

        /// <summary>
        /// Enlarges a town's food storage limit by <see cref="TownFoodStockScale"/>, so the reported
        /// stock can reflect what the market is really holding rather than saturating within a week's
        /// eating.
        ///
        /// Castles are left alone: their food is still vanilla's abstract figure, so a bigger granary
        /// there would only let an abstract number climb higher.
        /// </summary>
        [HarmonyPatch(typeof(Town), "FoodStocksUpperLimit")]
        private static class FoodStocksUpperLimitPatch
        {
            private static void Postfix(Town __instance, ref int __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || !__instance.IsTown)
                {
                    return;
                }

                __result = MathF.Round(__result * TownFoodStockScale);
            }
        }

        /// <summary>
        /// Reports the MEASURED day-over-day change in market food instead of the modelled one. Under
        /// a roster-backed stock the vanilla formula is not merely inaccurate but inverted: its
        /// largest positive term is the day's food sales, and a food sale is now the town EATING.
        ///
        /// Everything downstream of <c>FoodChange</c> wants a rate rather than a forecast -- the
        /// prosperity food-shortage penalty, the garrison auto-recruitment gate, AI settlement
        /// scoring, the town screen readout -- so a retrospective 24h delta serves them correctly.
        /// It also captures what no formula could: villager deliveries, caravan sales, and the player
        /// dumping grain on the market.
        /// </summary>
        [HarmonyPatch(typeof(DefaultSettlementFoodModel), "CalculateTownFoodStocksChange")]
        private static class TownFoodStocksChangePatch
        {
            private static bool Prefix(Town town, bool includeDescriptions, ref ExplainedNumber __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || town == null || !town.IsTown)
                {
                    return true;
                }

                _measuredFoodChange.TryGetValue(town, out float change);
                _unmetRations.TryGetValue(town, out int unmet);

                // Split for the tooltip: the shortfall is already folded into the stored change, so
                // it is added back before being shown as its own line.
                ExplainedNumber result = new ExplainedNumber(0f, includeDescriptions);
                result.Add(change + unmet, MarketFoodText);
                if (unmet > 0)
                {
                    result.Add(-unmet, FoodShortageText);
                }

                __result = result;
                return false;
            }
        }

        /// <summary>
        /// Reimplements <c>ItemConsumptionBehavior.MakeConsumption</c> verbatim except for one added
        /// line: a completed civilian purchase is registered against the category's demand. Vanilla
        /// buys goods out of the market every day without the buying pressure ever touching the
        /// price, so a town that eats its market dry shows no price signal for it.
        ///
        /// A prefix reimplementation rather than a postfix because the method is private, has no
        /// seam, and the per-item purchase values it needs exist only inside its loop.
        /// </summary>
        [HarmonyPatch(typeof(ItemConsumptionBehavior), "MakeConsumption")]
        private static class MakeConsumptionPatch
        {
            private static bool Prefix(Town town, Dictionary<ItemCategory, float> categoryDemand, Dictionary<ItemCategory, int> saleLog)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || town == null || !town.IsTown)
                {
                    return true;
                }

                saleLog.Clear();
                TownMarketData marketData = town.MarketData;
                ItemRoster itemRoster = town.Owner.ItemRoster;

                // The households' own shopping, bought by quantity off an explicit basket rather than
                // by gold budget: fuel, salt, crockery, timber, clothes, and whatever luxuries their
                // savings run to. See CitizenDemand. Runs first so the budget loop below sees what is
                // left on the shelf after the townspeople have been.
                CitizenDemand.BuyStaplesAndLuxuries(town, saleLog);

                for (int i = itemRoster.Count - 1; i >= 0; i--)
                {
                    ItemRosterElement element = itemRoster.GetElementCopyAtIndex(i);
                    ItemObject item = element.EquipmentElement.Item;
                    int amount = element.Amount;
                    ItemCategory category = item.GetItemCategory();

                    // Food is not bought here. The population eats by household count, not by how
                    // much gold its demand pool happens to carry -- see FeedPopulation. Leaving food
                    // in this loop would have the town eat twice over.
                    if (category.Properties == ItemCategory.Property.BonusToFoodStores)
                    {
                        continue;
                    }

                    // Nor is anything the basket already bought, for the same reason.
                    if (CitizenDemand.CoversItem(item))
                    {
                        continue;
                    }

                    float demand = categoryDemand[category];
                    float budget = Campaign.Current.Models.SettlementEconomyModel.CalculateDailySettlementBudgetForItemCategory(town, demand, category);
                    if (budget <= 0.01f)
                    {
                        continue;
                    }

                    int price = marketData.GetPrice(item);
                    float affordable = budget / price;
                    if (affordable > amount)
                    {
                        affordable = amount;
                    }
                    int bought = MBRandom.RoundRandomized(affordable);
                    if (bought > amount)
                    {
                        bought = amount;
                    }

                    itemRoster.AddToCounts(element.EquipmentElement, -bought);
                    // Vanilla writes the leftover budget back into the demand dictionary (not the
                    // rounded spend) -- kept as-is so category budgeting does not drift.
                    categoryDemand[category] = budget - affordable * price;
                    // No credit to the town. Vanilla pays the market here out of nowhere -- the
                    // townsfolk doing the buying have no purse of their own, so the sale is pure
                    // invention, and it is the single largest source of manufactured money in the
                    // economy. Under the two-purse ledger the buyer and the seller are BOTH inside
                    // citizen wealth: a townsman handing a merchant a denar moves nothing across the
                    // settlement's boundary, so the pot is unchanged and the goods are simply eaten.
                    // See SettlementWealth. The demand figure below is a price signal, not money, and
                    // still has to be registered.
                    RegisterPurchaseDemand(marketData, category, bought * price);

                    // The town's market fee on the townsfolk's own purchases. The sale itself is
                    // internal to citizen wealth -- a townsman buying off a merchant -- but the tariff
                    // on it is not: it moves a sliver into the treasury like any trade in the market.
                    // Food pays the same levy on its own leg in BuyFoodFromMarket; between the two,
                    // everything a citizen buys pays it. See TradeTariff.
                    TradeTariff.Levy(town.Settlement, bought * price);

                    saleLog.TryGetValue(category, out int logged);
                    saleLog[category] = logged + bought;
                }

                return false;
            }
        }

        /// <summary>
        /// Takes over vanilla's siege-only <c>ItemConsumptionBehavior.GetFoodFromMarket</c> slot,
        /// which runs immediately after <c>MakeConsumption</c> in the same daily tick and still has
        /// the day's sale log in hand. Three jobs, in order:
        ///
        /// 1. Pay out the food that buildings, policies and perks are supposed to produce -- as real
        ///    grain in the market. Without this the granary, Hunting Rights and Dirty Fighting would
        ///    quietly do nothing, since nothing reads modelled production any more.
        /// 2. Feed the town: households by prosperity, garrison and militia by head. Food is skipped
        ///    by <c>MakeConsumption</c> and bought here instead, so that what a town eats is set by
        ///    how many mouths it has rather than by how much gold its demand pool carries.
        /// 3. Record the day's closing food level, which is what makes the measured
        ///    <c>FoodChange</c> above possible.
        /// </summary>
        [HarmonyPatch(typeof(ItemConsumptionBehavior), "GetFoodFromMarket")]
        private static class GetFoodFromMarketPatch
        {
            private static bool Prefix(Town town, Dictionary<ItemCategory, int> saleLog)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || town == null || !town.IsTown)
                {
                    return true;
                }

                int openingFood = FoodUnitsInMarket(town);
                string delivered = DeliverModelledProduction(town);
                int afterDelivery = FoodUnitsInMarket(town);

                int wanted;
                int unmet = FeedPopulation(town, saleLog, out wanted);
                _unmetRations[town] = unmet;

                // Unmet rations are subtracted rather than merely observed. A town whose market is
                // empty buys nothing, so its roster does not move and the raw delta is 0 -- identical
                // to a day of perfect balance. Netting the shortfall off is what makes a famine read
                // as a deficit to the prosperity penalty, the AI, and the town screen.
                int closing = FoodUnitsInMarket(town);
                if (_foodAtLastTick.TryGetValue(town, out int opening))
                {
                    _measuredFoodChange[town] = closing - opening - unmet;
                }
                _foodAtLastTick[town] = closing;

                LogRations(town, openingFood, afterDelivery, closing, wanted, unmet, delivered);
                // Both halves of the day's shopping are done by now -- MakeConsumption ran the staples
                // and luxuries immediately before this, in the same tick and for this same town -- so
                // the basket tally is complete and can be reported and reset.
                CitizenDemand.ReportAndClear(town);

                return false;
            }

            /// <summary>
            /// The town's day at the table: what arrived in the market, what the mouths in it wanted,
            /// what they got, and what the granary reads afterwards. Written per town per day.
            /// </summary>
            private static void LogRations(Town town, int openingFood, int afterDelivery, int closing,
                int wanted, int unmet, string delivered)
            {
                if (!EconomyLog.IsEnabled)
                {
                    return;
                }

                int garrison = town.GarrisonParty?.Party.NumberOfAllMembers ?? 0;
                _measuredFoodChange.TryGetValue(town, out float change);

                EconomyLog.Log("FOOD", town.Settlement != null ? town.Settlement.Name.ToString() : town.StringId,
                    "market food " + openingFood + " → " + closing
                    + "  (delivered +" + (afterDelivery - openingFood)
                    + ", eaten " + (wanted - unmet)
                    + (unmet > 0 ? (", UNMET " + unmet) : "")
                    + ")  ·  wanted " + wanted
                    + "  ·  prosperity " + EconomyLog.Fmt(town.Prosperity)
                    + ", garrison " + garrison + ", militia " + EconomyLog.Fmt(town.Militia)
                    + "  ·  measured change " + EconomyLog.Fmt(change)
                    + "  stock " + EconomyLog.Fmt(town.FoodStocks) + "/" + town.FoodStocksUpperLimit()
                    + (town.IsUnderSiege ? "  ·  UNDER SIEGE" : "")
                    + (string.IsNullOrEmpty(delivered) ? "" : ("  ·  delivered: " + delivered)));
            }
        }

        /// <summary>
        /// Re-sets the starvation flag that <c>Town.DailyTick</c> can no longer raise. It derives the
        /// flag from <c>FoodStocks &lt; 0</c>, and a market-backed stock floors at zero, so
        /// <c>PartyBase.IsStarving</c> would never fire again -- costing the prosperity food-shortage
        /// penalty and the loyalty hit that scales with <c>Party.DaysStarving</c>.
        ///
        /// The equivalent condition is unmet rations, not an empty market: a town with 10 grain and
        /// 700 mouths went hungry just as surely as one with none, and it will read as starving here
        /// a day before its market actually empties.
        ///
        /// <c>Settlement.IsStarving</c> needs no help: it reads <c>Town.FoodStocks &lt;= 0f</c>, which
        /// the getter patch already answers correctly.
        ///
        /// The starvation CLOCK needs the same treatment for the same reason, and it is the half that
        /// actually bites: the loyalty penalty in <c>DefaultSettlementLoyaltyModel</c> triggers on
        /// <c>DaysStarving &gt; 14</c>, and <c>DaysStarving</c> is measured from
        /// <c>PartyBase._lastEatingTime</c>. <c>Town.DailyTick</c> stamps that to now whenever
        /// <c>FoodStocks &gt; 0</c> -- so under a market-backed stock a partial famine reset its own
        /// clock every day for as long as a single grain sat unsold, and could never reach fourteen.
        /// The prefix remembers the stamp and the postfix puts it back on a day rations went unmet, so
        /// the clock runs from the last day the town actually fed everyone.
        /// </summary>
        [HarmonyPatch(typeof(Town), "DailyTick")]
        private static class DailyTickStarvationPatch
        {
            // PartyBase._lastEatingTime -- private, and there is no setter that does not mean "fed".
            private static readonly AccessTools.FieldRef<PartyBase, CampaignTime> LastEatingTime =
                AccessTools.FieldRefAccess<PartyBase, CampaignTime>("_lastEatingTime");

            private static void Prefix(Town __instance, out CampaignTime __state)
            {
                __state = LastEatingTime(__instance.Owner);
            }

            private static void Postfix(Town __instance, CampaignTime __state)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || !__instance.IsTown)
                {
                    return;
                }

                if (_unmetRations.TryGetValue(__instance, out int unmet) && unmet > 0)
                {
                    __instance.Owner.RemainingFoodPercentage = -100;
                    LastEatingTime(__instance.Owner) = __state;
                }
            }
        }

        /// <summary>
        /// Food goods a smuggler might turn up with, for Dirty Fighting's delivery. Only grain and
        /// meat exist as code-created items on <see cref="DefaultItems"/>; the rest are XML and are
        /// resolved by id, the same way <see cref="RBMVillageProduction"/> resolves its output.
        /// </summary>
        private static readonly string[] SmuggledFoodIds = { "grain", "meat", "fish", "cheese", "butter", "grape", "olives", "date_fruit", "beer" };

        /// <summary>
        /// Pays modelled food production into the market as real goods, so those effects survive the
        /// move to a physical food stock. Each source delivers what its own description says it is
        /// rather than a blanket grain payout:
        ///
        /// <list type="bullet">
        /// <item><see cref="BuildingEffectEnum.FoodProduction"/> -- arable land worked for the
        /// settlement, so grain. In vanilla the only source is the castle Farmlands building, which
        /// <c>DefaultBuildingModel.CanAddBuildingTypeToTown</c> gates to castles, so this branch pays
        /// nothing for a town today; it is here for modded or RBM-added town farm buildings.</item>
        /// <item>Hunting Rights -- the kingdom opening its game reserves to commoners, so meat.</item>
        /// <item>Dirty Fighting -- the perk reads "{VALUE} random food item will be smuggled to the
        /// besieged governed settlement", so it delivers a randomly chosen food good.</item>
        /// </list>
        /// </summary>
        /// <summary>
        /// Returns what it delivered, good by good, for the economy log -- or an empty string when it
        /// delivered nothing. Building, policy and perk food is the one channel that adds food out of
        /// nowhere, so it is worth being able to see it separately from what the villages hauled in.
        /// </summary>
        private static string DeliverModelledProduction(Town town)
        {
            StringBuilder delivered = EconomyLog.IsEnabled ? new StringBuilder() : null;

            if (!town.IsUnderSiege)
            {
                ExplainedNumber arable = new ExplainedNumber(0f);
                town.AddEffectOfBuildings(BuildingEffectEnum.FoodProduction, ref arable);
                DeliverGood(town, DefaultItems.Grain, arable.ResultNumber, "buildings", delivered);
            }
            else
            {
                // Flat Add (2f), so it needs no base to scale off -- unlike an AddFactor perk, which
                // would have to be folded into a running total to mean anything.
                ExplainedNumber smuggled = new ExplainedNumber(0f);
                PerkHelper.AddPerkBonusForTown(DefaultPerks.Roguery.DirtyFighting, town, ref smuggled);
                DeliverGood(town, RandomSmuggledFood(), smuggled.ResultNumber, "smuggled", delivered);
            }

            Kingdom kingdom = town.Settlement.OwnerClan?.Kingdom;
            if (kingdom != null && kingdom.HasPolicy(DefaultPolicies.HuntingRights))
            {
                DeliverGood(town, DefaultItems.Meat, HuntingRightsGame, "hunting rights", delivered);
            }

            return (delivered != null) ? delivered.ToString() : "";
        }

        /// <summary>Units of game per day the Hunting Rights policy is worth, as in vanilla.</summary>
        private const float HuntingRightsGame = 2f;

        private static ItemObject RandomSmuggledFood()
        {
            string id = SmuggledFoodIds[MBRandom.RandomInt(SmuggledFoodIds.Length)];
            return Game.Current.ObjectManager.GetObject<ItemObject>(id) ?? DefaultItems.Grain;
        }

        private static void DeliverGood(Town town, ItemObject item, float amount, string source, StringBuilder delivered)
        {
            if (item == null)
            {
                return;
            }

            int units = MBRandom.RoundRandomized(amount);
            if (units > 0)
            {
                town.Owner.ItemRoster.AddToCounts(item, units);
                if (delivered != null)
                {
                    if (delivered.Length > 0)
                    {
                        delivered.Append(", ");
                    }
                    delivered.Append(units).Append(" ").Append(item.StringId).Append(" (").Append(source).Append(")");
                }
            }
        }

        /// <summary>
        /// Buys the town's rations out of the market: the townspeople by household, the garrison and
        /// militia by head.
        ///
        /// Prosperity stands in for the number of households, so the civilian ration is
        /// <c>Prosperity / NumberOfProsperityToEatOneFood</c> -- the same term vanilla's food model
        /// used, and the same one RBM's <c>= 4</c> override sharpens. Deliberately NOT the civilian
        /// gold budget in <c>MakeConsumption</c>: that is denominated in gold, so as food prices rose
        /// the town would buy fewer units and quietly eat less, when what a rising price should mean
        /// is that feeding the same households costs more.
        ///
        /// Militia are counted alongside the garrison because they are townsfolk under arms who still
        /// have to be fed; vanilla's model never charged for them at all.
        ///
        /// The consumption math mirrors vanilla's <c>CalculateTownFoodChangeInternal</c> term for
        /// term, including the perk order (Gourmet and Master of Warcraft fold into their own
        /// sub-totals before the sum, Triage Tent and the building effects apply to the total), so
        /// the only thing that changed is that the result is now a shopping list rather than a
        /// number subtracted from an abstract store.
        /// </summary>
        private static int FeedPopulation(Town town, Dictionary<ItemCategory, int> saleLog, out int wanted)
        {
            SettlementFoodModel foodModel = Campaign.Current.Models.SettlementFoodModel;

            ExplainedNumber households = new ExplainedNumber(town.Prosperity / foodModel.NumberOfProsperityToEatOneFood);
            float men = (town.GarrisonParty?.Party.NumberOfAllMembers ?? 0) + town.Militia;
            ExplainedNumber soldiers = new ExplainedNumber(men / foodModel.NumberOfMenOnGarrisonToEatOneFood);
            ExplainedNumber rations = new ExplainedNumber(0f);

            if (town.IsUnderSiege)
            {
                PerkHelper.AddPerkBonusForTown(DefaultPerks.Steward.Gourmet, town, ref soldiers);
                PerkHelper.AddPerkBonusForTown(DefaultPerks.Medicine.TriageTent, town, ref rations);
            }
            PerkHelper.AddPerkBonusForTown(DefaultPerks.Steward.MasterOfWarcraft, town, ref households);

            rations.Add(households.ResultNumber);
            rations.Add(soldiers.ResultNumber);
            town.AddEffectOfBuildings(BuildingEffectEnum.FoodConsumption, ref rations);

            int units = MBRandom.RoundRandomized(rations.ResultNumber);

            // The town's standing administration eats a fixed ration on top of the modelled population,
            // provisioned out of the treasury exactly like the garrison's -- see AdministrativeUpkeep,
            // which pays the same staff's wage. A floor, so it is fed even by a town of no prosperity
            // and no garrison, which is why it is added after the early return below rather than folded
            // into the model total.
            int adminUnits = AdministrativeUpkeep.TownDailyFood;
            wanted = (units + adminUnits > 0) ? units + adminUnits : 0;
            if (wanted <= 0)
            {
                return 0;
            }

            // Who eats decides who pays. The rations are bought in two groups because a townsman
            // buying his own bread moves nothing across the settlement's boundary, while a soldier's or
            // an official's is provisioned FOR him out of the fief's treasury -- see BuyFoodFromMarket.
            //
            // Split by the pre-building shares rather than by recomputing each leg through the perk
            // and building chain, so the day's total ration is exactly what it was before the split.
            // The food balance is calibrated on that total and must not move.
            float soldierPart = soldiers.ResultNumber;
            float bothParts = households.ResultNumber + soldierPart;
            int soldierUnits = (units > 0 && bothParts > 0f) ? MBRandom.RoundRandomized(units * soldierPart / bothParts) : 0;
            if (soldierUnits > units)
            {
                soldierUnits = units;
            }
            int civilianUnits = units - soldierUnits;
            int provisionedUnits = soldierUnits + adminUnits;

            int unmet = 0;
            if (civilianUnits > 0)
            {
                // The households eat a basket -- half bread, a sixth beer, and so on -- rather than
                // whatever is cheapest. See CitizenDemand. What the market cannot supply in the shape
                // they wanted, they make up with whatever food is left and cheapest, which is what a
                // hungry household actually does and what keeps the day's ration count unchanged.
                int unshaped = CitizenDemand.BuyRation(town, civilianUnits, saleLog);
                if (unshaped > 0)
                {
                    unmet += BuyFoodFromMarket(town, unshaped, saleLog, provisioned: false);
                }
            }
            if (provisionedUnits > 0)
            {
                unmet += BuyFoodFromMarket(town, provisionedUnits, saleLog, provisioned: true);
            }
            return unmet;
        }

        /// <summary>
        /// Pays a garrison's grocer's bill: the fief's treasury first, and whatever it cannot cover
        /// charged to the owner, exactly as <see cref="GarrisonUpkeep"/> splits the same garrison's
        /// wages. Returns the total that changed hands, which is what the market is owed.
        /// </summary>
        /// <remarks>
        /// The owner backstop is what stops a poor fief from starving the men holding it the moment
        /// its treasury runs dry. A lord who wants a garrison somewhere pays to feed it if the place
        /// cannot; the alternative -- taking the food anyway -- would mint the difference, and refusing
        /// the food would starve a garrison over a bookkeeping shortfall.
        /// </remarks>
        private static int PayForGarrisonFood(Settlement settlement, int cost)
        {
            int fromTreasury = SettlementWealth.Debit(settlement, cost, SettlementWealth.Source.GarrisonFood);
            int remainder = cost - fromTreasury;
            if (remainder > 0)
            {
                Hero owner = (settlement.OwnerClan != null) ? settlement.OwnerClan.Leader : null;
                if (owner != null)
                {
                    int fromOwner = (owner.Gold < remainder) ? owner.Gold : remainder;
                    if (fromOwner > 0)
                    {
                        owner.ChangeHeroGold(-fromOwner);
                        return fromTreasury + fromOwner;
                    }
                }
            }
            return fromTreasury;
        }

        /// <summary>
        /// Buys up to <paramref name="amount"/> units of food goods out of the town's market.
        /// Deliberately NOT vanilla's private <c>GetFoodFromMarketInternal</c>, which confiscates the
        /// goods for free -- acceptable for a one-off siege emergency, wrong as a daily mechanism.
        /// This settles at the market price through the same steps as a civilian purchase in
        /// <c>MakeConsumption</c>: price the item, pull it, credit the town, register the demand, log
        /// the units.
        ///
        /// Returns the amount it could NOT fill. That figure is the town going hungry, and nothing
        /// else records it -- an empty market leaves the roster unchanged, so a purchase that buys
        /// nothing is indistinguishable from a day of perfect balance unless the shortfall is
        /// reported back.
        ///
        /// CHEAPEST FIRST, for the same reason <see cref="VillagerDelivery"/> sells food cheapest
        /// first: a ration is a ration, so paying the fish price for one when grain is on the shelf
        /// buys the town nothing and costs it the difference. Walking the roster tail-first -- which
        /// is only a safe-removal idiom, not an ordering -- had towns eating 1,140-denar fish ahead of
        /// 60-denar grain, burning a treasury that §5.1 of the design doc sizes on the assumption that
        /// a day's rations cost roughly a day's rations.
        ///
        /// Sorting means the removal order no longer matches the roster order, so the purchase is
        /// planned into a list first and then executed against EquipmentElement rather than index --
        /// the same reason <c>SellCargo</c> builds its lots up front.
        /// </summary>
        private static int BuyFoodFromMarket(Town town, int amount, Dictionary<ItemCategory, int> saleLog, bool provisioned)
        {
            ItemRoster itemRoster = town.Owner.ItemRoster;
            TownMarketData marketData = town.MarketData;

            List<FoodLot> lots = new List<FoodLot>();
            for (int i = itemRoster.Count - 1; i >= 0; i--)
            {
                ItemRosterElement element = itemRoster.GetElementCopyAtIndex(i);
                ItemObject item = element.EquipmentElement.Item;
                if (item == null || item.ItemCategory.Properties != ItemCategory.Property.BonusToFoodStores || element.Amount <= 0)
                {
                    continue;
                }

                lots.Add(new FoodLot
                {
                    Element = element.EquipmentElement,
                    Category = item.ItemCategory,
                    Amount = element.Amount,
                    Price = marketData.GetPrice(item),
                    RosterOrder = lots.Count
                });
            }

            // Cheapest per unit first; ties keep the reverse-roster order so the result is stable.
            lots.Sort(delegate (FoodLot a, FoodLot b)
            {
                return (a.Price != b.Price) ? a.Price.CompareTo(b.Price) : a.RosterOrder.CompareTo(b.RosterOrder);
            });

            int civilianSpend = 0;
            int provisionedSpend = 0;
            foreach (FoodLot lot in lots)
            {
                if (amount <= 0)
                {
                    break;
                }

                int taken = (lot.Amount >= amount) ? amount : lot.Amount;
                amount -= taken;
                itemRoster.AddToCounts(lot.Element, -taken);

                int cost = taken * lot.Price;
                if (provisioned)
                {
                    // The fief buys this ration for men who do not buy their own. Real money leaves the
                    // treasury and reaches the merchants who sold it, which is the whole point: until
                    // now feeding a garrison CREDITED the town, so a bigger garrison made a town richer.
                    //
                    // Tallied on what actually reached the market rather than on the asking price: a
                    // broke treasury with no owner to fall back on buys the food short, and the fee is
                    // owed on the trade that happened.
                    provisionedSpend += SettlementWealth.CreditCitizens(town.Settlement,
                        PayForGarrisonFood(town.Settlement, cost), SettlementWealth.Source.GarrisonFood);
                }
                else
                {
                    // Civilian rations pay nobody: the townsman handing a merchant a denar is a move
                    // inside citizen wealth, not across the settlement's boundary. But the town still
                    // takes its market fee on the sale, the same one every other trade pays -- see the
                    // levy below.
                    civilianSpend += cost;
                }

                RegisterPurchaseDemand(marketData, lot.Category, cost);

                saleLog.TryGetValue(lot.Category, out int logged);
                saleLog[lot.Category] = logged + taken;
            }

            // The market fee on the day's bread, whoever it was for. The townsfolk's own purchase is
            // internal to citizen wealth and the fee on it is not -- it moves a sliver into the treasury,
            // like any trade struck in the market. The garrison's and the administration's rations are a
            // trade in the same market and pay the same fee, even though the buyer is the fief itself:
            // the levy is on the trade, not on the trader. See TradeTariff.
            //
            // Only one of the two is ever non-zero in a single call -- the two legs are bought
            // separately -- but they are summed rather than levied apart so the fee is rounded once.
            TradeTariff.Levy(town.Settlement, civilianSpend + provisionedSpend);

            return amount;
        }

        /// <summary>A stack of market food priced for this purchase, held apart from the roster so
        /// buying out of price order cannot disturb the iteration.</summary>
        private struct FoodLot
        {
            public EquipmentElement Element;
            public ItemCategory Category;
            public int Amount;
            public int Price;
            public int RosterOrder;
        }

        /// <summary>
        /// Feeds a completed purchase back into its category's demand, so buying pressure raises the
        /// price. Shared by the civilian channel, the population's rations, and soldiers buying off
        /// the same stalls (<see cref="TroopMarketFeedback"/>) -- the units conversion below is the
        /// reason this is shared rather than reimplemented per channel.
        ///
        /// The division by <see cref="RBMProsperityEquilibrium.VanillaProsperityScale"/> is a UNITS
        /// conversion, not a tuning knob. <paramref name="purchaseValue"/> is gold, and in vanilla
        /// that was directly comparable to demand, because there demand IS the gold budget. RBM split
        /// the two apart: the price-side demand this modifies now lives on the household scale
        /// (<c>RBMMarketLiquidity.EstimatedDemandPatch</c>), a twentieth of the gold scale, so feeding
        /// raw gold into it overstates the feedback by exactly that factor.
        ///
        /// Left unconverted it did not merely distort the price, it WAS the price: a town buying ~50
        /// grain at ~300d fed 15,000 into a baseline of about 18, some eight hundred times larger.
        /// Prices then tracked purchase volume rather than scarcity, which is a positive feedback --
        /// the better fed a town became, the more it paid -- and it silently nullified the whole
        /// point of putting the baseline back on the household scale.
        /// </summary>
        internal static void RegisterPurchaseDemand(TownMarketData marketData, ItemCategory category, int purchaseValue)
        {
            if (purchaseValue <= 0)
            {
                return;
            }

            float feedback = purchaseValue * DemandFromPurchaseFactor / RBMProsperityEquilibrium.VanillaProsperityScale;
            marketData.AddDemand(category, feedback);
        }

        private static readonly TextObject MarketFoodText = new TextObject("{=RBM_FOOD_MARKET}Market food");
        private static readonly TextObject FoodShortageText = new TextObject("{=RBM_FOOD_SHORTAGE}Unmet rations");
    }
}
