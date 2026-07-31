using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using RC = RBMConfig.RBMConfig;

namespace RBMCampaign
{
    /// <summary>
    /// The dispatch brain of the supply-caravan system: it reads which of a kingdom's towns are drowning
    /// in a good and which of its other towns are short of it, and puts a caravan on the road between
    /// them. Only towns of the SAME kingdom are ever matched -- the search is binned by
    /// <see cref="IFaction"/> and never looks across a bin -- so no caravan crosses a border. Run on a
    /// cadence by <see cref="RBMCaravanBehavior"/> (currently every two days).
    /// </summary>
    internal static class RBMCaravanDispatch
    {
        // Days of its own supply above which a town's stock of a good counts as a surplus worth moving.
        private const float SurplusDays = 40f;

        // Days of supply below which a town counts as short of a good and worth supplying.
        private const float ShortageDays = 5f;

        // Days of supply the source is left after loading a caravan, so moving its surplus cannot itself
        // tip it into shortage. Must stay below SurplusDays.
        private const float KeepDays = 20f;

        // A caravan smaller than this is not worth the road; larger than this is one cart's load.
        private const int MinCaravanLoad = 20;
        private const int MaxCaravanLoad = 200;

        // Roughly how many units one pack animal is added to carry. Weight only slows a caravan, so this
        // is a comfort figure, not a hard capacity.
        private const int UnitsPerPackAnimal = 8;

        // Per-pass dispatch budget SCALES WITH KINGDOM SIZE: a realm may send up to this many caravans per
        // town each pass, counted separately for goods and for relief (investment-only). There is
        // deliberately no global cap -- every kingdom trades on its own, so bigger realms move more.
        private const int CaravansPerTownPerPass = 2;

        // A destination whose market holds less than this cannot be trusted to pay for a caravan's load.
        private const int MinBuyerWealth = 1000;

        // Days of food a fresh caravan is stocked with, so its guards do not go hungry and desert on the
        // road. Consumed as the party travels; whatever is left dies with the caravan.
        private const int FoodDays = 5;

        // A caravan bundles every good that shares its source→destination route, up to these limits, so
        // one trip can carry several goods rather than a single one.
        private const int MaxGoodsPerCaravan = 6;
        private const int MaxCaravanTotalUnits = 400;

        /// <summary>
        /// A dispatch pass. Bins every town by its kingdom; within each kingdom it works DESTINATION-first:
        /// it takes each struggling town (neediest first) and fills a caravan with everything one source
        /// can supply of that town's shortages -- so a town short of many goods gets a single dense caravan
        /// per supplying source, rather than one thin caravan per good.
        /// </summary>
        public static void RunDispatch()
        {
            if (!RC.rbmCampaignEnabled || !RC.kingdomCaravansEnabled || Campaign.Current == null)
            {
                return;
            }

            Dictionary<IFaction, List<Town>> byFaction = new Dictionary<IFaction, List<Town>>();
            foreach (Town town in Town.AllTowns)
            {
                if (town == null || !town.IsTown || town.Settlement == null)
                {
                    continue;
                }
                IFaction faction = town.Settlement.MapFaction;
                if (faction == null)
                {
                    continue;
                }
                if (!byFaction.TryGetValue(faction, out List<Town> list))
                {
                    list = new List<Town>();
                    byFaction[faction] = list;
                }
                list.Add(town);
            }

            foreach (KeyValuePair<IFaction, List<Town>> realm in byFaction)
            {
                List<Town> towns = realm.Value;
                if (towns.Count < 2)
                {
                    // A realm needs at least two towns to run a caravan between.
                    continue;
                }

                // Per-kingdom budgets, scaled to the realm's size and reset each pass. No global limit --
                // every kingdom runs its own caravans independently.
                int budget = towns.Count * CaravansPerTownPerPass;
                int goodsDispatched = 0;
                int reliefDispatched = 0;

                // The deepest-surplus source for each good, and how much it has to give this pass. Snapshot
                // once; the per-good remaining spare is drawn down as caravans are committed so one source
                // is never over-promised across several destinations.
                Dictionary<string, Town> bestSource = new Dictionary<string, Town>();
                Dictionary<string, int> srcRemaining = new Dictionary<string, int>(); // "srcId#goodId" -> units left
                foreach (string goodId in CitizenDemand.ModelledGoods)
                {
                    ItemObject good = RBMCaravanRegister.FindItem(goodId);
                    if (good == null)
                    {
                        continue;
                    }
                    Town src = null;
                    float srcDays = 0f;
                    int srcSpare = 0;
                    foreach (Town town in towns)
                    {
                        float days = RBMMarketPrices.DaysOfSupply(town, good);
                        if (days >= SurplusDays && days > srcDays && !Busy(town.Settlement))
                        {
                            int spare = SpareUnits(town, good, days);
                            if (spare >= MinCaravanLoad)
                            {
                                src = town;
                                srcDays = days;
                                srcSpare = spare;
                            }
                        }
                    }
                    if (src != null)
                    {
                        bestSource[goodId] = src;
                        srcRemaining[src.Settlement.StringId + "#" + goodId] = srcSpare;
                    }
                }

                // Neediest towns first, so the per-pass cap serves the most-starved markets.
                Dictionary<Town, int> neediness = new Dictionary<Town, int>();
                foreach (Town town in towns)
                {
                    neediness[town] = Neediness(town);
                }
                List<Town> dests = new List<Town>(towns);
                dests.Sort((a, b) => neediness[b].CompareTo(neediness[a]));

                HashSet<Town> served = new HashSet<Town>();
                foreach (Town dst in dests)
                {
                    if (goodsDispatched >= budget)
                    {
                        break;
                    }
                    if (Busy(dst.Settlement) || SettlementWealth.GetCitizenWealth(dst.Settlement) < MinBuyerWealth)
                    {
                        continue;
                    }

                    // Group this town's shortages by which source supplies them, so one caravan per source
                    // carries everything that source can bring.
                    Dictionary<Town, List<RBMCaravanRegister.GoodLot>> bySource = new Dictionary<Town, List<RBMCaravanRegister.GoodLot>>();
                    Dictionary<Town, int> bySourceUnits = new Dictionary<Town, int>();

                    foreach (string goodId in CitizenDemand.ModelledGoods)
                    {
                        ItemObject good = RBMCaravanRegister.FindItem(goodId);
                        if (good == null)
                        {
                            continue;
                        }
                        float days = RBMMarketPrices.DaysOfSupply(dst, good);
                        if (days < 0f || days > ShortageDays)
                        {
                            continue; // not short of this good
                        }
                        if (!bestSource.TryGetValue(goodId, out Town src) || src == dst)
                        {
                            continue; // no one in the realm has it to spare
                        }
                        int room = EffectiveHeadroom(dst, good);
                        if (room <= 0)
                        {
                            continue;
                        }

                        string rk = src.Settlement.StringId + "#" + goodId;
                        srcRemaining.TryGetValue(rk, out int remaining);
                        if (remaining < MinCaravanLoad)
                        {
                            continue; // source already committed elsewhere this pass
                        }

                        bySourceUnits.TryGetValue(src, out int already);
                        int qty = Math.Min(remaining, Math.Min(room, MaxCaravanLoad));
                        qty = Math.Min(qty, MaxCaravanTotalUnits - already); // keep the whole caravan reasonable
                        if (qty < MinCaravanLoad)
                        {
                            continue;
                        }

                        if (!bySource.TryGetValue(src, out List<RBMCaravanRegister.GoodLot> lots))
                        {
                            lots = new List<RBMCaravanRegister.GoodLot>();
                            bySource[src] = lots;
                        }
                        if (lots.Count >= MaxGoodsPerCaravan)
                        {
                            continue;
                        }
                        lots.Add(new RBMCaravanRegister.GoodLot(goodId, qty));
                        bySourceUnits[src] = already + qty;
                        srcRemaining[rk] = remaining - qty;
                    }

                    // One caravan per source supplying this town, up to the goods budget.
                    foreach (KeyValuePair<Town, List<RBMCaravanRegister.GoodLot>> pair in bySource)
                    {
                        if (goodsDispatched >= budget)
                        {
                            break;
                        }
                        if (SpawnCaravan(pair.Key, dst, pair.Value))
                        {
                            goodsDispatched++;
                            served.Add(dst);
                        }
                    }
                }

                // Relief runs, on their OWN budget: a struggling town that got no goods caravan still gets
                // an investment caravan from a wealthy town of the realm -- capital even when there is
                // nothing to trade.
                if (RC.caravanInvestmentEnabled)
                {
                    foreach (Town dst in dests)
                    {
                        if (reliefDispatched >= budget)
                        {
                            break;
                        }
                        if (served.Contains(dst) || Busy(dst.Settlement))
                        {
                            continue;
                        }
                        Town investor = FindInvestor(towns, dst);
                        if (investor != null && SpawnCaravan(investor, dst, new List<RBMCaravanRegister.GoodLot>()))
                        {
                            reliefDispatched++;
                            served.Add(dst);
                        }
                    }
                }
            }
        }

        /// <summary>The first town of the realm able to invest in <paramref name="dst"/> right now, or null.</summary>
        private static Town FindInvestor(List<Town> towns, Town dst)
        {
            foreach (Town s in towns)
            {
                if (s == dst || Busy(s.Settlement))
                {
                    continue;
                }
                if (RBMCaravanInvestment.WouldInvest(s.Settlement, dst.Settlement))
                {
                    return s;
                }
            }
            return null;
        }

        /// <summary>How many modelled goods a town is currently short of -- its priority as a destination.</summary>
        private static int Neediness(Town town)
        {
            int count = 0;
            foreach (string goodId in CitizenDemand.ModelledGoods)
            {
                ItemObject good = RBMCaravanRegister.FindItem(goodId);
                if (good == null)
                {
                    continue;
                }
                float days = RBMMarketPrices.DaysOfSupply(town, good);
                if (days >= 0f && days <= ShortageDays)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Puts one caravan on the road carrying a bundle of goods: creates the native party with the
        /// cargo, takes the same counts out of the source market (no payment now -- see the buy-leg note),
        /// and points it at the destination. Returns whether it went.
        /// </summary>
        private static bool SpawnCaravan(Town src, Town dst, List<RBMCaravanRegister.GoodLot> goods)
        {
            // No goods => a relief caravan: it carries nothing and exists only to deliver the investment
            // injection to a struggling town that has no goods-trade route.
            bool relief = (goods == null || goods.Count == 0);

            Hero owner = (src.Settlement.OwnerClan != null) ? src.Settlement.OwnerClan.Leader : null;
            if (owner == null || owner.Clan == null || owner.IsDead)
            {
                return false;
            }

            CultureObject culture = src.Settlement.Culture;
            if (culture == null || culture.CaravanPartyTemplates == null)
            {
                return false;
            }
            PartyTemplateObject template = culture.CaravanPartyTemplates
                .GetRandomElementWithPredicate<PartyTemplateObject>(x => x.ShipHulls.Count == 0);
            if (template == null)
            {
                return false;
            }

            // The cargo the caravan is created carrying: every good in the bundle, plus pack animals enough
            // to move the lot. A relief caravan travels empty -- its errand is the injection, not trade.
            ItemRoster cargo = new ItemRoster();
            if (!relief)
            {
                int totalUnits = 0;
                foreach (RBMCaravanRegister.GoodLot lot in goods)
                {
                    ItemObject good = RBMCaravanRegister.FindItem(lot.GoodId);
                    if (good == null || lot.Qty <= 0)
                    {
                        continue;
                    }
                    cargo.AddToCounts(new EquipmentElement(good), lot.Qty);
                    totalUnits += lot.Qty;
                }
                if (totalUnits <= 0)
                {
                    return false;
                }
                ItemObject pack = CheapestPackAnimal();
                if (pack != null)
                {
                    cargo.AddToCounts(new EquipmentElement(pack), Math.Max(2, totalUnits / UnitsPerPackAnimal));
                }
            }

            // Hand the errand to the creation postfix so the caravan is registered before its own
            // source-entry event fires (which our OnSettlementEntered patch then suppresses).
            RBMCaravanRegister.Pending = new RBMCaravanRegister.Order
            {
                SourceId = src.Settlement.StringId,
                DestId = dst.Settlement.StringId,
                Goods = relief ? new List<RBMCaravanRegister.GoodLot>() : goods,
                State = RBMCaravanRegister.StateSpawning
            };

            MobileParty caravan = CaravanPartyComponent.CreateCaravanParty(
                owner, src.Settlement, template, isInitialSpawn: false, caravanLeader: null, caravanItems: cargo, isElite: false);

            // Belt and braces: the postfix should have bound it, but guarantee it and clear the handoff.
            if (caravan != null && !RBMCaravanRegister.IsManaged(caravan))
            {
                RBMCaravanRegister.BindPending(caravan.StringId);
            }
            RBMCaravanRegister.Pending = null;

            if (caravan == null || !RBMCaravanRegister.IsManaged(caravan))
            {
                return false;
            }

            // Buy leg: the goods are already on the caravan (added at creation); take the same counts out
            // of the source's market stock. NO money is paid at the source now -- this is citizens buying
            // from citizens, so the source is paid only when the caravan returns with the destination's
            // coin, and the source's tariff is taken then, out of that money. Paying or taxing the source
            // here would charge a market for income it has not yet received. A relief caravan takes nothing.
            if (!relief)
            {
                foreach (RBMCaravanRegister.GoodLot lot in goods)
                {
                    ItemObject good = RBMCaravanRegister.FindItem(lot.GoodId);
                    if (good != null && lot.Qty > 0)
                    {
                        src.Settlement.ItemRoster.AddToCounts(new EquipmentElement(good), -lot.Qty);
                    }
                }
            }

            // Feed the guards for the outbound leg so they do not starve on the road.
            StockFood(caravan, goods);

            RBMCaravanRegister.SetState(caravan.StringId, RBMCaravanRegister.StateEnRoute);
            caravan.SetMoveGoToSettlement(dst.Settlement, MobileParty.NavigationType.Default, false);

            CaravanLog.Log("DISPATCH", CaravanLog.Name(src.Settlement),
                "→ " + CaravanLog.Name(dst.Settlement)
                + (relief ? "  ·  relief (investment only)" : "  ·  took " + RBMCaravanRegister.DescribeGoods(goods)));

            return true;
        }

        /// <summary>Units a town can spare of a good while keeping <see cref="KeepDays"/> for itself.</summary>
        private static int SpareUnits(Town town, ItemObject good, float days)
        {
            if (days <= KeepDays)
            {
                return 0;
            }
            int stock = town.Owner.ItemRoster.GetItemNumber(good);
            float spareFraction = 1f - (KeepDays / days);
            return (int)(stock * spareFraction);
        }

        /// <summary>Room a town has for more of a good, less what caravans already in flight will bring.</summary>
        private static int EffectiveHeadroom(Town town, ItemObject good)
        {
            int headroom = TownStorage.Headroom(town, good);
            if (headroom == TownStorage.Uncapped)
            {
                return MaxCaravanLoad;
            }
            int inFlight = RBMCaravanRegister.InFlightQty(town.Settlement.StringId, good.StringId);
            int room = headroom - inFlight;
            return (room > 0) ? room : 0;
        }

        /// <summary>A settlement caught in a battle or under siege cannot spawn or receive a caravan cleanly.</summary>
        private static bool Busy(Settlement settlement)
        {
            return settlement == null || settlement.Party == null
                || settlement.Party.MapEvent != null || settlement.SiegeEvent != null;
        }

        private static ItemObject CheapestPackAnimal()
        {
            ItemObject cheapest = null;
            float best = float.MaxValue;
            foreach (ItemObject item in Items.All)
            {
                if (item != null && item.ItemCategory == DefaultItemCategories.PackAnimal
                    && !item.NotMerchandise && item.Value < best)
                {
                    cheapest = item;
                    best = item.Value;
                }
            }
            return cheapest;
        }

        /// <summary>
        /// Adds <see cref="FoodDays"/> of provisions to a caravan so its guards stay fed and do not desert.
        /// The food is a cheap staple that is never the good being traded (so the sale and the homecoming,
        /// which act only on the traded good, leave it alone); the party eats it as it travels and whatever
        /// is left dies with the caravan. Called at dispatch for the outbound leg and again at the
        /// destination for the return leg.
        /// </summary>
        internal static void StockFood(MobileParty caravan, List<RBMCaravanRegister.GoodLot> goods)
        {
            if (caravan == null)
            {
                return;
            }
            ItemObject food = FoodBufferItem(goods);
            if (food == null)
            {
                return;
            }

            // A safe over-estimate of daily consumption (roughly a food per eight mouths a day), so five
            // days' worth is always plenty for a caravan-sized party.
            int men = caravan.MemberRoster.TotalManCount;
            int perDay = Math.Max(1, men / 8);
            int units = perDay * FoodDays;
            if (units > 0)
            {
                caravan.ItemRoster.AddToCounts(new EquipmentElement(food), units);
            }
        }

        /// <summary>The cheapest staple food to provision a caravan with, never one of the goods it carries.</summary>
        private static ItemObject FoodBufferItem(List<RBMCaravanRegister.GoodLot> goods)
        {
            HashSet<string> carried = new HashSet<string>();
            if (goods != null)
            {
                foreach (RBMCaravanRegister.GoodLot lot in goods)
                {
                    carried.Add(lot.GoodId);
                }
            }
            string[] candidates = { "grain", "meat", "fish", "cheese", "dates" };
            foreach (string id in candidates)
            {
                if (carried.Contains(id))
                {
                    continue;
                }
                ItemObject item = RBMCaravanRegister.FindItem(id);
                if (item != null)
                {
                    return item;
                }
            }
            return null;
        }
    }
}
