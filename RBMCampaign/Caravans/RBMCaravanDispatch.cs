using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Naval;
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
        private const float SurplusDays = 30f;

        // Days of supply below which a town counts as short of a good and worth supplying.
        private const float ShortageDays = 10f;

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

        // Minimum days of food a caravan is stocked with, so even a short hop keeps a cushion; the actual
        // amount is sized to the leg's estimated travel time (see StockFood). Consumed as the party travels;
        // whatever is left dies with the caravan.
        private const int FoodDays = 5;

        // A ceiling on provisioning so a route the land estimate reads as near-unreachable cannot pile on an
        // absurd, caravan-slowing mountain of food. Real routes are well under this.
        private const int MaxFoodDays = 60;

        // The estimated road time is multiplied by this before provisioning, so a slow, heavily-laden, or
        // waylaid caravan still never runs dry before it arrives. Food is cheap and dies with the caravan, so
        // erring high costs nothing but a little carried weight.
        private const float FoodDaysSafetyFactor = 1.5f;

        // A caravan bundles every good that shares its source→destination route, up to these limits, so
        // one trip can carry several goods rather than a single one.
        private const int MaxGoodsPerCaravan = 6;
        private const int MaxCaravanTotalUnits = 400;

        // A sea route must beat the land route's travel TIME by at least this factor before a caravan is put
        // to ship. The margin keeps caravans off the water for break-even crossings, so the embark/disembark
        // transitions and the detour to a port are only paid when the sea genuinely saves time. There is no
        // margin on the island case -- if there is no road at all, the sea is the only way and always wins.
        private const float SeaRouteTimeMargin = 0.9f;

        // Days added to a sea route's estimate for BOARDING, which the raw travel-time math ignores. From a
        // PORT source the caravan's fleet is planted at the quay (RouteBetween sets the anchor on the ship it
        // is standing beside), so embarking is instant and only the ~2h disembark at the far port remains --
        // a rounding-error tenth of a day. From a NON-port source the party must first march to a coast and
        // summon its fleet, which the transition model charges up to ~2 days; budgeting that stops a caravan
        // from trekking inland-to-water for a crossing whose sailing saving is smaller than the summon costs.
        // Per travelled leg.
        private const float SeaRouteDockBoardingDays = 0.1f;
        private const float SeaRouteOpenBoardingDays = 2f;

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

            // Don't put a caravan on a route it can never finish: if there is no road between the two towns
            // (an island town, say) and no workable sea crossing either, skip this pairing entirely rather
            // than strand a party. When a land route exists this is a cheap true and we spawn as normal.
            if (!CanDeliver(src.Settlement, dst.Settlement, culture))
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

            // Point it at the destination first (which also grants any ship a sea route needs), so the food
            // estimate below sees the real leg and navigation the caravan will actually travel.
            RBMCaravanRegister.SetState(caravan.StringId, RBMCaravanRegister.StateEnRoute);
            RouteBetween(caravan, src.Settlement, dst.Settlement, culture);

            // Feed the guards for the whole outbound leg so they do not starve or slow on the road.
            StockFood(caravan, dst.Settlement, goods);

            CaravanLog.Log("DISPATCH", CaravanLog.Name(src.Settlement),
                "→ " + CaravanLog.Name(dst.Settlement)
                + (relief ? "  ·  relief (investment only)" : "  ·  took " + RBMCaravanRegister.DescribeGoods(goods)));

            return true;
        }

        /// <summary>
        /// Whether a caravan from <paramref name="from"/> could actually reach <paramref name="to"/>. Land
        /// is the default and the fallback: any road route makes this true. Only when there is no road at
        /// all does water come into play, and then only if this culture fields ships and the destination
        /// has a port to land at and a combined land+sea route exists. Without the Naval DLC no culture has
        /// ship hulls and no town has a port, so this is purely the land test -- the feature stays dormant.
        /// </summary>
        private static bool CanDeliver(Settlement from, Settlement to, CultureObject culture)
        {
            var dist = Campaign.Current.Models.MapDistanceModel;
            if (dist.PathExistBetweenPoints(from.GatePosition, to.GatePosition, MobileParty.NavigationType.Default))
            {
                return true; // a road exists -- always preferred
            }
            return to.HasPort
                && culture != null && culture.AvailableShipHulls != null && culture.AvailableShipHulls.Count > 0
                && dist.PathExistBetweenPoints(from.GatePosition, to.PortPosition, MobileParty.NavigationType.All);
        }

        /// <summary>
        /// Points a caravan at a settlement, choosing sea over land when the crossing is genuinely faster.
        /// The two routes are weighed in travel TIME, not raw distance: a caravan's ship is slower per unit
        /// distance than its land march (vanilla seeds ~3.53 against ~4.2), so a sea route only wins when it
        /// is enough shorter to beat the slower hull. When the sea wins -- or there is simply no road at all,
        /// an island crossing -- the caravan is given a ship, keeps its land access so it is a hybrid that
        /// still walks the land legs, and is moved with <see cref="MobileParty.NavigationType.All"/> targeting
        /// the port. Otherwise it takes the road. If a ship cannot be granted it falls back to a plain land
        /// order; <see cref="CanDeliver"/> is expected to have already screened out truly unreachable pairs.
        /// </summary>
        internal static void RouteBetween(MobileParty caravan, Settlement from, Settlement to, CultureObject culture)
        {
            if (PreferSeaRoute(from, to, culture) && EnsureNaval(caravan, culture))
            {
                // Board at the source's own dock. A transferred ship starts with an INVALID anchor -- its
                // fleet is "nowhere" -- which the transition model charges ~2 days to summon before the party
                // can embark. The caravan is standing in its port source, so plant the anchor at that port:
                // the fleet is already at the quay and embarking is instant (distance ~0 => zero transition).
                if (from != null && from.HasPort && caravan.Anchor != null)
                {
                    caravan.Anchor.Settlement = from;
                }
                caravan.SetMoveGoToSettlement(to, MobileParty.NavigationType.All, true);
                CaravanLog.Log("SEA", CaravanLog.Name(to), "by ship from " + CaravanLog.Name(from));
                return;
            }
            caravan.SetMoveGoToSettlement(to, MobileParty.NavigationType.Default, false);
        }

        /// <summary>
        /// Whether a caravan from <paramref name="from"/> should sail to <paramref name="to"/> rather than
        /// march. True when the sea route's estimated travel TIME is meaningfully shorter than the land
        /// route's (by <see cref="SeaRouteTimeMargin"/>), or when there is no land route at all -- an island
        /// town -- in which case the sea is the only way in and wins outright. Requires the destination to
        /// have a port to land at and the culture to field ships; without the Naval DLC no culture has hulls
        /// and no town has a port, so this is always false and every caravan takes the road (the feature
        /// stays dormant). Distances come straight from the <see cref="MapDistanceModel"/> -- party-agnostic,
        /// so no ship need be granted just to measure -- and each is timed at the average caravan land/naval
        /// speed the game seeds, splitting the sea route's own land and water legs by the model's landRatio,
        /// plus a fixed boarding cost for the embark/disembark the travel math cannot see (cheap from a port
        /// source, dear from an inland one -- see the SeaRoute*BoardingDays constants).
        /// </summary>
        private static bool PreferSeaRoute(Settlement from, Settlement to, CultureObject culture)
        {
            if (from == null || to == null || !to.HasPort
                || culture == null || culture.AvailableShipHulls == null || culture.AvailableShipHulls.Count == 0)
            {
                return false; // no port to land at, or no ships to sail -- land only
            }

            var dist = Campaign.Current.Models.MapDistanceModel;
            float unreachable = Campaign.MapDiagonal * 5f; // the model returns ~this when no path of that type exists

            // Land: a plain road march, source gate to destination gate.
            float landDist = dist.GetDistance(from, to, false, false, MobileParty.NavigationType.Default, out float _);
            bool hasLand = landDist < unreachable;

            // Sea: a hybrid land+sea route landing at the destination's port. Try leaving from the source's
            // gate, and -- if the source itself has a port -- from that port too, keeping whichever is shorter
            // (mirrors how the game weighs the same route for a real naval party).
            float seaLandRatio;
            float seaDist = dist.GetDistance(from, to, false, true, MobileParty.NavigationType.All, out seaLandRatio);
            if (from.HasPort)
            {
                float fromPortDist = dist.GetDistance(from, to, true, true, MobileParty.NavigationType.All, out float fromPortRatio);
                if (fromPortDist < seaDist)
                {
                    seaDist = fromPortDist;
                    seaLandRatio = fromPortRatio;
                }
            }
            bool hasSea = seaDist < unreachable;

            if (!hasSea)
            {
                return false; // cannot reach by water -- march (CanDeliver blocked us if there is no land route either)
            }
            if (!hasLand)
            {
                return true; // an island crossing: the water is the only way in
            }

            // Time each route. The sea route's distance is split into its land leg and its water leg by
            // landRatio, then each leg is timed at its own speed -- the water leg is genuinely slower, which
            // is exactly what makes "shorter distance" and "shorter time" diverge and is the point of this.
            float hoursPerDay = (float)CampaignTime.HoursInDay;
            float landSpeed = Campaign.Current.EstimatedAverageCaravanPartySpeed;
            float navalSpeed = Campaign.Current.EstimatedAverageCaravanPartyNavalSpeed;
            if (landSpeed <= 0f || navalSpeed <= 0f)
            {
                return false; // speeds not seeded yet -- play it safe and march
            }

            float landDays = landDist / (landSpeed * hoursPerDay);
            float seaLandLeg = seaDist * seaLandRatio;
            float seaWaterLeg = seaDist - seaLandLeg;
            // Sailing time plus the fixed boarding cost the travel math alone does not see. Boarding is cheap
            // from a port source (the caravan embarks at its own quay) and dear from an inland one (walk to a
            // coast, summon the fleet), so the crossing must clear a higher bar to be worth marching to.
            float boardingDays = from.HasPort ? SeaRouteDockBoardingDays : SeaRouteOpenBoardingDays;
            float seaDays = (seaLandLeg / landSpeed + seaWaterLeg / navalSpeed) / hoursPerDay + boardingDays;

            return seaDays < landDays * SeaRouteTimeMargin;
        }

        /// <summary>
        /// Ensures a caravan can cross water: if it has no ship yet it is given one from the culture's hulls
        /// and its land access is (re)confirmed, so it becomes a hybrid that sails the crossings and walks
        /// the rest. Returns whether the party ended up naval-capable. The ship is transferred for free --
        /// no gold moves -- and dies with the caravan when it dissolves.
        /// </summary>
        private static bool EnsureNaval(MobileParty caravan, CultureObject culture)
        {
            if (caravan == null)
            {
                return false;
            }
            if (caravan.HasNavalNavigationCapability)
            {
                return true; // already carries a ship (e.g. granted on the outbound leg)
            }
            ShipHull hull = (culture != null && culture.AvailableShipHulls != null && culture.AvailableShipHulls.Count > 0)
                ? culture.AvailableShipHulls[0] : null;
            if (hull == null)
            {
                return false;
            }
            ChangeShipOwnerAction.ApplyByTransferring(caravan.Party, new Ship(hull));
            caravan.SetLandNavigationAccess(true); // keep the land fallback -- a hybrid, not a water-only party
            return caravan.HasNavalNavigationCapability;
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
        /// Provisions a caravan with enough food to last the whole leg to <paramref name="target"/> so its
        /// guards stay fed and do not starve or slow on the road. The amount is sized to the leg's estimated
        /// travel time (with a safety margin, floored at <see cref="FoodDays"/> and capped at
        /// <see cref="MaxFoodDays"/>), not a flat few days -- a long haul is stocked for the long haul. The
        /// food is a cheap staple that is never the good being traded (so the sale and the homecoming, which
        /// act only on the traded good, leave it alone); the party eats it as it travels and whatever is left
        /// dies with the caravan. Called at dispatch for the outbound leg, at the destination for the return
        /// leg, and on a re-route for the new leg.
        /// </summary>
        internal static void StockFood(MobileParty caravan, Settlement target, List<RBMCaravanRegister.GoodLot> goods)
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

            // A safe over-estimate of daily consumption (roughly a food per eight mouths a day), times the
            // days the leg is expected to take, so the caravan carries enough to reach the far end.
            int men = caravan.MemberRoster.TotalManCount;
            int perDay = Math.Max(1, men / 8);
            int units = perDay * EstimateTravelDays(caravan, target);
            if (units > 0)
            {
                caravan.ItemRoster.AddToCounts(new EquipmentElement(food), units);
            }
        }

        /// <summary>
        /// How many days of food to stock for the leg from the caravan's current position to
        /// <paramref name="target"/>: the estimated travel time (distance at the caravan's own navigation
        /// over the average caravan speed) scaled by <see cref="FoodDaysSafetyFactor"/>, floored at
        /// <see cref="FoodDays"/> and capped at <see cref="MaxFoodDays"/>. Falls back to the floor if the
        /// distance or speed cannot be read.
        /// </summary>
        private static int EstimateTravelDays(MobileParty caravan, Settlement target)
        {
            if (caravan == null || target == null || Campaign.Current == null)
            {
                return FoodDays;
            }
            MobileParty.NavigationType nav = caravan.HasNavalNavigationCapability
                ? MobileParty.NavigationType.All
                : MobileParty.NavigationType.Default;
            bool isPort = caravan.HasNavalNavigationCapability && target.HasPort;
            float d = Campaign.Current.Models.MapDistanceModel.GetDistance(caravan, target, isPort, nav, out float _);
            float speed = Campaign.Current.EstimatedAverageCaravanPartySpeed;
            if (speed <= 0f || d <= 0f || float.IsNaN(d) || float.IsInfinity(d))
            {
                return FoodDays;
            }
            float travelDays = d / (speed * (float)CampaignTime.HoursInDay);
            int provisioned = (int)Math.Ceiling(travelDays * FoodDaysSafetyFactor);
            return Math.Max(FoodDays, Math.Min(provisioned, MaxFoodDays));
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
