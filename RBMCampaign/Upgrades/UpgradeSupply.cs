using System.Collections.Generic;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// "Supply-town gated upgrades": a party may only upgrade its troops while a friendly or neutral
    /// town is within reach, and the upgrade is bought from that town -- value-appropriate stock leaves
    /// its market, and what the promotion cost the party reaches the townspeople who armed them, market
    /// fee and all. See <see cref="SupplyUpgradeFromTown"/>.
    ///
    /// The whole feature lives in this file plus a handful of call sites, each tagged with the comment
    /// "SupplyTown gate". Switch it off at runtime with RBMConfig.troopUpgradeRequireSupplyTown = 0;
    /// remove it outright by deleting this file, its csproj entry, and those tagged lines.
    /// </summary>
    public static class UpgradeSupply
    {
        /// <summary>
        /// The GATE and the DRAW: whether an upgrade needs a town in reach, and whether the new kit comes
        /// off that town's shelves. On only when the spoils economy is on and the gate is switched on in
        /// config.
        /// </summary>
        public static bool IsEnabled
        {
            get { return SpoilsPool.IsEnabled && RBMConfig.RBMConfig.troopUpgradeRequireSupplyTown; }
        }

        /// <summary>
        /// The PAYMENT: whether what a promotion cost reaches the town that armed the men. Deliberately
        /// NOT tied to <see cref="IsEnabled"/>.
        /// </summary>
        /// <remarks>
        /// The gate is a difficulty knob -- switch it off and armies may promote wherever they stand,
        /// taking nothing off anyone's shelves. Conservation is not a difficulty knob: whatever a player
        /// thinks of the gate, an upgrade's cost must not simply cease to exist, which is what happened on
        /// every path before this and would happen again the moment the gate was turned off. So the money
        /// follows the spoils economy alone, and finds a town to land in whether or not one was required.
        /// </remarks>
        public static bool PaymentEnabled
        {
            get { return SpoilsPool.IsEnabled; }
        }

        /// <summary>
        /// Whether <paramref name="party"/> may upgrade right now. Always true when the feature is off,
        /// so callers gate on this one call without having to know the feature exists.
        /// </summary>
        public static bool CanUpgradeNear(MobileParty party)
        {
            Town town;
            return CanUpgradeNear(party, out town);
        }

        /// <summary>
        /// As <see cref="CanUpgradeNear(MobileParty)"/>, but also hands back the town that would supply
        /// the upgrade. That town is null when the party is allowed for a reason other than a specific
        /// city -- the feature is off, a bandit, or a garrison whose faction has no city left to supply
        /// it -- so callers must null-check before buying from it. One resolution serves both the gate
        /// and the market purchase.
        /// </summary>
        public static bool CanUpgradeNear(MobileParty party, out Town town)
        {
            town = null;
            if (!IsEnabled)
            {
                return true;
            }
            // Bandits keep no friendly towns, so gating them on one would freeze their upgrades for good.
            // They upgrade wherever they roam, the way they did before this feature, with no market effect.
            if (IsBanditParty(party))
            {
                return true;
            }
            return TryGetSupplyTown(party, out town);
        }

        /// <summary>
        /// The town that would supply an upgrade. A party stationed inside a friendly/neutral settlement
        /// -- a garrison or militia holding a castle or village, or any party visiting one -- is supplied
        /// however far the nearest city lies: a town supplies itself, a castle or village is supplied
        /// from the nearest friendly city, and the party is allowed either way (true even when that
        /// search finds no city, e.g. a faction with none left, so the location gate never blocks a
        /// settled party). A party out in the field is only supplied by a friendly town within
        /// <see cref="RBMConfig.RBMConfig.troopUpgradeSupplyRadius"/> map units.
        /// </summary>
        public static bool TryGetSupplyTown(MobileParty party, out Town town)
        {
            town = null;
            if (party == null)
            {
                return false;
            }
            // Where the party is stationed. Garrisons and militia belong to their settlement even in the
            // brief windows CurrentSettlement reads null (transitions, sally-outs, old saves), so fall
            // back to their home so they are never mistaken for a party out in the field.
            Settlement current = party.CurrentSettlement;
            if (current == null && (party.IsGarrison || party.IsMilitia))
            {
                current = party.HomeSettlement;
            }
            // Stationed in a friendly/neutral settlement: supplied regardless of a city's distance. A
            // town supplies itself; a castle or village buys only from the nearest friendly city.
            if (current != null && IsFriendlyOrNeutral(party, current))
            {
                town = current.IsTown
                    ? current.Town
                    : SettlementHelper.FindNearestTownToSettlement(current,
                        MobileParty.NavigationType.Default, s => IsFriendlyOrNeutral(party, s));
                return true;
            }
            if (!IsEnabled)
            {
                return false;
            }
            // Straight-line reach rather than a pathfinding query: this runs for every AI party that
            // has a troop ready to promote, and "near a town" does not need to know the road.
            Town nearest = SettlementHelper.FindNearestTownToMobileParty(party,
                MobileParty.NavigationType.Default, s => IsFriendlyOrNeutral(party, s));
            if (nearest == null || nearest.Settlement == null)
            {
                return false;
            }
            float distance = party.GetPosition2D.Distance(nearest.Settlement.GetPosition2D);
            if (distance <= RBMConfig.RBMConfig.troopUpgradeSupplyRadius)
            {
                town = nearest;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The town a promotion is bought from. With the gate on that is the town that satisfied it -- the
        /// men were armed by the place that let them be armed at all. With the gate off there is no such
        /// town, so it falls back to the nearest not at war with the party, at any distance, and the money
        /// still has somewhere to go. Null only when the party can reach no friendly town on the map.
        /// </summary>
        public static Town ResolveMarketTown(MobileParty party)
        {
            if (party == null)
            {
                return null;
            }
            Town town;
            if (IsEnabled && TryGetSupplyTown(party, out town) && town != null)
            {
                return town;
            }
            // Also the fallback when the gate is ON but resolved nothing -- a party stationed in a castle
            // whose faction has no city left is allowed to upgrade regardless (see TryGetSupplyTown), and
            // its coin would otherwise be the one case still burnt.
            return FindNearestFriendlyTown(party);
        }

        // --- Payee-town cache ------------------------------------------------
        //
        // FindNearestFriendlyTown and FindFenceTown both sweep every town on the map by straight-line
        // distance, and each runs once per party, garrison and militia every day on the maintenance path
        // (see the remarks on FindNearestFriendlyTown). The answer only ever names a payee for a
        // promotion's coin -- a town the code already treats as crow-flies-approximate -- and it changes no
        // faster than the party moves; garrisons and militia never move at all. So the result is cached per
        // party and reused until the party has drifted PayeeRecomputeDistance from where it was taken, a
        // few days have passed (so a stationary party still notices a captured or newly-at-peace town), or
        // the cached town itself has gone invalid. A daily O(all-towns) sweep per party becomes an
        // occasional one -- and almost never for the stationary garrison/militia parties.

        // How far a party may drift from where its payee was resolved before the nearest town is re-swept.
        private const float PayeeRecomputeDistance = 10f;

        // A hard ceiling on cache age, so even a never-moving garrison re-checks its payee for war/peace
        // and capture changes every few days rather than holding one forever.
        private const double PayeeMaxCacheDays = 4.0;

        private struct PayeeCache
        {
            public Town FriendlyTown;
            public Vec2 FriendlyPos;
            public double FriendlyExpiryDay;
            public Town FenceTown;
            public Vec2 FencePos;
            public double FenceExpiryDay;
        }
        private static readonly Dictionary<MobileParty, PayeeCache> _payeeCache = new Dictionary<MobileParty, PayeeCache>();

        /// <summary>
        /// Drops the per-party payee cache. Called on every session launch (new game or load) so a departed
        /// campaign's parties and towns are not held, keyed by dead instances, for the life of the process.
        /// </summary>
        internal static void ResetForNewSession()
        {
            _payeeCache.Clear();
        }

        /// <summary>
        /// Drops a destroyed party's payee entry, so a long session does not accumulate a dead MobileParty
        /// (and the two cached Town refs behind it) per departed party. Wired from the spoils behavior's
        /// MobilePartyDestroyed handler, alongside the other party-keyed store prunes.
        /// </summary>
        internal static void OnMobilePartyDestroyed(MobileParty party, PartyBase destroyerParty)
        {
            if (party != null)
            {
                _payeeCache.Remove(party);
            }
        }

        /// <summary>
        /// The nearest town of a faction the party is not at war with, however far off it lies.
        /// </summary>
        /// <remarks>
        /// Measured straight-line rather than through <c>SettlementHelper.FindNearestTownToMobileParty</c>,
        /// which the gate above uses. That helper runs a map-distance query per town, and this is called
        /// from paths that run for every party on the map -- the daily maintenance charge above all. The
        /// two want different things besides: the gate has to know whether a town is genuinely in reach,
        /// while this only has to name a payee, and a payee picked as the crow flies is close enough even
        /// where the crow would have to cross a sea. The full sweep is cached per party -- see the payee
        /// cache above -- since a payee that moves only with the party need not be re-found every day.
        /// </remarks>
        internal static Town FindNearestFriendlyTown(MobileParty party)
        {
            if (party == null)
            {
                return null;
            }
            Vec2 pos = party.GetPosition2D;
            double now = CampaignTime.Now.ToDays;
            if (_payeeCache.TryGetValue(party, out PayeeCache cache)
                && now < cache.FriendlyExpiryDay
                && pos.Distance(cache.FriendlyPos) <= PayeeRecomputeDistance
                && (cache.FriendlyTown == null
                    || (cache.FriendlyTown.Settlement != null && IsFriendlyOrNeutral(party, cache.FriendlyTown.Settlement))))
            {
                return cache.FriendlyTown;
            }

            Town best = ComputeNearestFriendlyTown(party, pos);
            cache.FriendlyTown = best;
            cache.FriendlyPos = pos;
            cache.FriendlyExpiryDay = now + PayeeMaxCacheDays;
            _payeeCache[party] = cache;
            return best;
        }

        private static Town ComputeNearestFriendlyTown(MobileParty party, Vec2 pos)
        {
            Town best = null;
            float bestDistance = float.MaxValue;
            foreach (Town town in Town.AllTowns)
            {
                Settlement settlement = town.Settlement;
                if (settlement == null || !IsFriendlyOrNeutral(party, settlement))
                {
                    continue;
                }
                float distance = pos.Distance(settlement.GetPosition2D);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = town;
                }
            }
            return best;
        }

        /// <summary>
        /// Buys the new kit for an upgrade off the supply town: value-appropriate stock leaves the
        /// market, and what the promotion cost their lord in GOLD -- <paramref name="goldPaid"/> -- goes
        /// over to the townspeople who armed them. Soft on stock: it takes what the market has and never
        /// holds an upgrade up for want of it.
        ///
        /// Only the men bought with gold are supplied here. Men the stockpile covered armed themselves
        /// from their own spoils -- the loot and pay already in their purse -- so their promotion takes
        /// nothing off the town's shelves and puts nothing into its citizens' pockets: a spoils upgrade is
        /// the men re-arming from what they already carry, not a trade over the counter. The draw is scaled
        /// to the gold-buyer count for that reason.
        ///
        /// The stock pulled matches the slots the promotion actually improves: a mounted upgrade takes a
        /// horse, a barding upgrade takes horse armour, a body upgrade takes body armour, a new sidearm
        /// takes a one-handed weapon, and so on -- each of the tier the new gear is worth. Only when no
        /// slot reads as improved does it fall back to pulling one generic in-band item per man.
        /// </summary>
        /// <param name="goldPaid">
        /// What the promotion cost the party's lord in GOLD alone -- the spoils drawn from the men's own
        /// purses are deliberately NOT included, so no part of a spoils-covered upgrade reaches the town.
        /// The gold was billed by the caller before this runs and destroyed where it was charged, so
        /// handing it over here neither mints nor burns.
        /// </param>
        /// <remarks>
        /// NOTHING IS CHARGED HERE, as on the recruit side -- see <see cref="RecruitSupply"/>, which this
        /// now matches leg for leg. The gold was taken by a null-recipient <c>GiveGoldAction</c> on the AI
        /// path and by the party screen's own vanilla debit on the player's; the spoils were drawn from the
        /// stack's purse. This only decides where that money lands instead of vanishing.
        ///
        /// The payment leg used to be missing outright: the promotion's cost was destroyed at both call
        /// sites while the town's shelves were emptied for nothing, which was the last unconserved flow in
        /// the spoils economy. It was deleted in d347ad3 ("Remove the settlement gold-to-prosperity layer
        /// from RBMCampaign", 2026-07-20) and nothing replaced it until now.
        ///
        /// The money reaches CITIZEN WEALTH rather than the treasury -- an armourer's takings are his own
        /// -- and the town's market fee is taken out of it on the way, since a promotion outfitted off the
        /// stalls is a trade like any other. Where the kit drawn is worth more than the coin handed over,
        /// the balance is charged the fee too: the levy is on the goods that changed hands, and a
        /// spoils-discounted promotion must not be a way of walking gear past the tollhouse.
        /// </remarks>
        // TODO: a future revision may turn this into a hard gate -- no suitable stock in the town, no
        // upgrade -- rather than the soft sink it is now.
        public static void SupplyUpgradeFromTown(Town town, PartyBase buyer, CharacterObject character,
            CharacterObject upgradeTarget, int count, int goldPaid)
        {
            if (count <= 0)
            {
                return;
            }
            // A null town used to end the call right here, which burnt the promotion's whole cost -- the
            // caller has already charged it by the time this runs, so returning early destroys it exactly
            // as the old missing payment leg did. The DRAW needs a real supply town; the PAYMENT only needs
            // somewhere for the coin to land. From here on the two are resolved apart.
            Town payee = (town != null) ? town : FindFenceTown(buyer);
            // What one upgraded man's new kit is worth over his old, which is what we look to spend
            // it on in the town's stock. Zero for a cheaper-kit upgrade: nothing is bought, though
            // anything the promotion still cost is paid over all the same.
            int perManValue = SpoilsPool.GetSpoilsCostForUpgrade(buyer, character, upgradeTarget);

            // How many of the batch were bought with gold rather than covered by spoils -- only they are
            // supplied off the town. goldPaid is the gold leg alone (perManValue per man at full price), so
            // its ratio to perManValue recovers the gold-buyer count; capped at the batch and floored at
            // zero. A wholly spoils-covered upgrade (goldPaid == 0) buys nothing here and pays nothing over.
            int goldBuyers = (perManValue > 0)
                ? MathF.Max(0, MathF.Min(count, MathF.Round((float)goldPaid / perManValue)))
                : count;

            ItemRoster market = (town != null) ? town.Settlement.ItemRoster : null;
            int bought = 0;
            int wanted = 0;
            // drawnValue: the kit's worth taken, each piece counted up to its slot's need. surplus: the
            // price of pieces above their need, paid back to the town's citizens below.
            int drawnValue = 0;
            int surplus = 0;
            // The DRAW is the gated half: switch the supply-town feature off and a promotion takes nothing
            // off anyone's shelves, as it did before the feature existed. The payment below runs either
            // way -- see PaymentEnabled for why the two are not one switch.
            if (market != null && perManValue > 0 && goldBuyers > 0 && IsEnabled)
            {
                // One priced view of the stall for the whole draw: every slot of every man searches it
                // instead of walking and re-pricing the market afresh. See KitStock.
                KitStock stock = new KitStock(market, town.Settlement);
                List<SpoilsPool.SlotPurchase> slots = SpoilsPool.GetUpgradedSlots(character, upgradeTarget);
                if (slots.Count > 0)
                {
                    // Buy the actual gear the promotion adds: for each slot it improves, pull one item of
                    // that class and tier per gold-buyer man -- best effort against what the market holds.
                    wanted = slots.Count * goldBuyers;
                    foreach (SpoilsPool.SlotPurchase slot in slots)
                    {
                        for (int man = 0; man < goldBuyers; man++)
                        {
                            // The exact class first, then any gear of the same role, then a value-matched
                            // fallback that stays in category: a town short of the exact piece still arms
                            // the man in kind from what it has. See FindKitOrAnyWarGear. Priced at the
                            // town's own price, the one TakeFromStock counts.
                            int index = stock.FindKitOrAnyWarGear(slot.ItemType, slot.Value);
                            if (index < 0)
                            {
                                break; // soft sink: no war gear in band at all, the rest are outfitted off-screen
                            }
                            if (!TakeFromStock(town, stock, index, slot.Value, ref drawnValue, ref surplus))
                            {
                                break;
                            }
                            bought++;
                        }
                    }
                }
                else
                {
                    // No slot read as improved (the troops' first sets differ from the averaged price):
                    // fall back to pulling one generic in-band item per gold-buyer man, as before.
                    wanted = goldBuyers;
                    for (int man = 0; man < goldBuyers; man++)
                    {
                        int index = stock.FindKitInStock(perManValue);
                        if (index < 0)
                        {
                            break;
                        }
                        if (!TakeFromStock(town, stock, index, perManValue, ref drawnValue, ref surplus))
                        {
                            break;
                        }
                        bought++;
                    }
                }
            }

            // The part of the pieces' price above what the promotion needed goes back to the citizens whose
            // shelves they came off, as coin, instead of vanishing with the piece. Not levied: it is the
            // town's own goods turned back into its own money, not a trade.
            int returned = 0;
            if (surplus > 0 && SettlementWealth.HasCitizenPurse(town.Settlement))
            {
                returned = SettlementWealth.CreditCitizens(town.Settlement, surplus, SettlementWealth.Source.ArmsSurplus);
            }

            // The gold leg of the promotion, into the town's market purse -- the spoils leg never reaches
            // it, so a wholly spoils-covered upgrade (goldPaid == 0) pays nothing here. Independent of what
            // the draw above managed to find, exactly as the recruit price is: a picked-clean market still
            // armed the gold-buyers as best it could and the party still paid for them. The market fee
            // rides along inside RegisterPurchase.
            if (goldPaid > 0 && payee != null)
            {
                TroopMarketFeedback.RegisterPurchase(payee.Settlement, null, goldPaid, SettlementWealth.Source.Upgrade);
            }
            // The fee is on the goods that changed hands, so where the kit drawn is worth more than the
            // coin handed over -- which is the ordinary case, since spoils make the leading men free and
            // the gold cost is only the differential -- the balance is charged it too. Levy takes the fee
            // out of the market's own money, so this moves no wealth into or out of the town: only the
            // split between its citizens and its treasury. Charged against the town whose shelves were
            // emptied, so a fence sale with no draw behind it is not levied on.
            int untaxed = drawnValue - goldPaid;
            if (town != null && untaxed > 0)
            {
                TradeTariff.Levy(town.Settlement, untaxed);
            }

            // Logged whenever a supply is attempted, not only when stock was pulled: a draw that came
            // away with nothing is the case most worth seeing, and it would otherwise be silent. A
            // promotion no town at all could be found for is the one case still burnt, so it says so.
            if (SpoilsLog.IsEnabled)
            {
                string marketName = (town != null)
                    ? town.Settlement.Name.ToString()
                    : (payee != null ? payee.Settlement.Name + " (fence)" : "nowhere — cost burnt");
                SpoilsLog.Log("UPGRADE", buyer, SpoilsLog.Describe(buyer) + " supplied " + bought + "/" + wanted
                    + " item(s) worth " + drawnValue + "d for " + goldBuyers + "/" + count + "x "
                    + SpoilsLog.Describe(upgradeTarget) + " kit (gold-buyers) from " + marketName
                    + " (~" + perManValue + " each man); paid " + goldPaid + "d"
                    + (untaxed > 0 && town != null ? " (fee also charged on " + untaxed + "d of kit beyond the coin)" : "")
                    + (returned > 0 ? "; " + returned + "d over need paid back to its citizens" : "")
                    + (bought < wanted ? " — market short " + (wanted - bought) : ""));
            }
        }

        /// <summary>
        /// The last resort payee: the nearest town of ANY faction, war or no war. Null only on a map with
        /// no towns left standing.
        /// </summary>
        /// <remarks>
        /// <see cref="FindNearestFriendlyTown"/> comes back empty for a party at war with every faction
        /// holding a city -- a looter band above all, but equally a rebel clan or a kingdom down to its
        /// last castle. Those parties still promote men, and their promotions are still charged, so
        /// without a payee their coin was simply destroyed.
        ///
        /// Nobody arms a bandit over the counter, which is the point: this names a fence, not a supplier.
        /// It is used for the PAYMENT only -- no stock is drawn from a town resolved this way, so a
        /// hostile market is never emptied by the men raiding it. The money reaching those townspeople
        /// rather than vanishing is the whole of what changes.
        /// </remarks>
        private static Town FindFenceTown(PartyBase buyer)
        {
            MobileParty party = (buyer != null) ? buyer.MobileParty : null;
            if (party == null)
            {
                return null;
            }
            Vec2 pos = party.GetPosition2D;
            double now = CampaignTime.Now.ToDays;
            // A fence is the nearest town of ANY faction, so it never goes invalid by war -- only if the
            // settlement itself vanished. Existence plus the drift/age gates are all it takes to reuse.
            if (_payeeCache.TryGetValue(party, out PayeeCache cache)
                && now < cache.FenceExpiryDay
                && pos.Distance(cache.FencePos) <= PayeeRecomputeDistance
                && (cache.FenceTown == null || cache.FenceTown.Settlement != null))
            {
                return cache.FenceTown;
            }

            Town best = ComputeNearestFenceTown(pos);
            cache.FenceTown = best;
            cache.FencePos = pos;
            cache.FenceExpiryDay = now + PayeeMaxCacheDays;
            _payeeCache[party] = cache;
            return best;
        }

        private static Town ComputeNearestFenceTown(Vec2 pos)
        {
            Town best = null;
            float bestDistance = float.MaxValue;
            foreach (Town town in Town.AllTowns)
            {
                Settlement settlement = town.Settlement;
                if (settlement == null)
                {
                    continue;
                }
                float distance = pos.Distance(settlement.GetPosition2D);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = town;
                }
            }
            return best;
        }

        /// <summary>
        /// Takes one piece off the stall for a slot needing <paramref name="need"/>: its price up to the
        /// need is added to <paramref name="drawnValue"/>, anything above it to <paramref name="surplus"/>
        /// (paid back to the citizens by the caller). False only when the slot held nothing to take.
        /// </summary>
        /// <remarks>
        /// Priced through <see cref="TroopMarketFeedback.UnitPrice"/> rather than off the item's base
        /// value, so a town stripped of mail values what it has left the way it would sell it. No money
        /// moves here: the promotion's cost is paid once, in one sum, by the caller.
        ///
        /// The demand registered is a price signal, not a payment -- the same way the recruit draw feeds
        /// it. It is what teaches a garrison town's market to restock the arms its promotions keep
        /// walking off with, which the old unpaid draw never did.
        /// </remarks>
        private static bool TakeFromStock(Town town, KitStock stock, int index, int need,
            ref int drawnValue, ref int surplus)
        {
            // Priced (at the town's price, as the search ranked it) and removed from the market in one step.
            int price;
            ItemObject item = stock.TakeOne(index, out price);
            if (item == null)
            {
                return false;
            }
            int counted = MathF.Min(price, need);
            drawnValue += counted;
            surplus += price - counted;
            if (item.ItemCategory != null)
            {
                RBMTownFoodSupply.RegisterPurchaseDemand(town.MarketData, item.ItemCategory, price);
            }
            return true;
        }

        /// <summary>Looters and bandit-clan parties, which have no friendly settlements to supply from.</summary>
        private static bool IsBanditParty(MobileParty party)
        {
            return party != null && (party.IsBandit || (party.MapFaction != null && party.MapFaction.IsBanditFaction));
        }

        /// <summary>Friendly or neutral: the party's faction is not at war with the town's owner.</summary>
        /// <remarks>Shared with the maintenance draw, which picks its market the same way.</remarks>
        internal static bool IsFriendlyOrNeutral(MobileParty party, Settlement settlement)
        {
            IFaction partyFaction = party.MapFaction;
            IFaction townFaction = settlement.MapFaction;
            if (partyFaction == null || townFaction == null)
            {
                return false;
            }
            return partyFaction == townFaction || !partyFaction.IsAtWarWith(townFaction);
        }

        /// <summary>
        /// The in-stock market item to arm one kit slot with, chosen by a three-tier search that keeps a
        /// man in gear of his own kind before it broadens. -1 only when the market holds nothing the slot
        /// can use in band.
        /// </summary>
        /// <remarks>
        /// The search both the recruit and the upgrade draws walk each kit slot with. It prefers, in order:
        /// (1) the exact item class and nearest tier -- a helmet slot pulls a helmet, a bow slot a bow;
        /// (2) failing that, any item of the same FUNCTIONAL GROUP at that value -- a bow slot takes a
        /// crossbow, an arrows slot any ammunition, a sword slot any melee weapon -- so an archer whose
        /// exact bow is sold out is still armed with a launcher rather than a stray breastplate; and
        /// (3) failing that, a value-appropriate fallback that stays in the slot's BROAD CATEGORY -- a
        /// weapon broadens only to other weapons, armour only to other armour, a mount only to another
        /// mount, barding only to other barding (see <see cref="SameKitCategory"/>). A picked-over frontier
        /// market still arms its men from what it has, but a body-armour upgrade never walks off with a
        /// sidearm, nor a sword slot with a cuirass, because the exact piece ran out. Trade goods, livestock,
        /// books and food are never kit and so match no category.
        ///
        /// One O(n) pass over the stall tracks the best candidate for each tier at once and returns the
        /// highest tier that found anything, so the broadening costs no extra scans over the old two-stage
        /// search it replaces. The half-to-double value band of <see cref="FindKitInStock"/>
        /// is applied at every tier, so no tier ever spends the slot's coin on something wildly off tier.
        /// </remarks>
        /// <param name="pricedAt">
        /// When set, each candidate is valued at what this settlement's market charges for it
        /// (<see cref="TroopMarketFeedback.UnitPrice"/>) instead of its base value -- the figure the
        /// caller will be charged, so the band bounds what is actually spent. Null keeps base value.
        /// </param>
        /// <param name="maxValue">A further cap on the candidate's value, under the band's own top.</param>
        /// <remarks>
        /// A one-off search. A draw that searches the same market slot after slot holds a
        /// <see cref="KitStock"/> instead and calls its <see cref="KitStock.FindKitOrAnyWarGear"/>, which
        /// is this search, so the stall is walked and priced once rather than once per slot.
        /// </remarks>
        internal static int FindKitOrAnyWarGear(ItemRoster market, ItemObject.ItemTypeEnum itemType, int targetValue,
            Settlement pricedAt = null, int maxValue = int.MaxValue)
        {
            return new KitStock(market, pricedAt).FindKitOrAnyWarGear(itemType, targetValue, maxValue);
        }

        /// <summary>
        /// The functional group two equipment classes share, or -1 for classes that group with nothing a
        /// draw should broaden across (trade goods, books, banners). Two classes in the same group are
        /// interchangeable enough that a man missing one is still armed in role by the other: a crossbow
        /// stands in for a bow, bolts for arrows, an axe for a sword. Armour groups by wearer (all human
        /// armour together) rather than by body part, so the broadened tier of <see cref="FindKitOrAnyWarGear"/>
        /// can dress a bare slot in any spare piece; the exact-part preference lives in that search's first tier.
        /// </summary>
        private enum KitGroup
        {
            None,
            Melee,
            Thrown,
            Launcher,
            Ammunition,
            Shield,
            Armor,
            Mount,
            Barding,
        }

        private static KitGroup KitGroupOf(ItemObject.ItemTypeEnum itemType)
        {
            switch (itemType)
            {
                case ItemObject.ItemTypeEnum.OneHandedWeapon:
                case ItemObject.ItemTypeEnum.TwoHandedWeapon:
                case ItemObject.ItemTypeEnum.Polearm:
                    return KitGroup.Melee;
                case ItemObject.ItemTypeEnum.Thrown:
                    return KitGroup.Thrown;
                case ItemObject.ItemTypeEnum.Bow:
                case ItemObject.ItemTypeEnum.Crossbow:
                case ItemObject.ItemTypeEnum.Sling:
                case ItemObject.ItemTypeEnum.Pistol:
                case ItemObject.ItemTypeEnum.Musket:
                    return KitGroup.Launcher;
                case ItemObject.ItemTypeEnum.Arrows:
                case ItemObject.ItemTypeEnum.Bolts:
                case ItemObject.ItemTypeEnum.SlingStones:
                case ItemObject.ItemTypeEnum.Bullets:
                    return KitGroup.Ammunition;
                case ItemObject.ItemTypeEnum.Shield:
                    return KitGroup.Shield;
                case ItemObject.ItemTypeEnum.HeadArmor:
                case ItemObject.ItemTypeEnum.BodyArmor:
                case ItemObject.ItemTypeEnum.LegArmor:
                case ItemObject.ItemTypeEnum.HandArmor:
                case ItemObject.ItemTypeEnum.ChestArmor:
                case ItemObject.ItemTypeEnum.Cape:
                    return KitGroup.Armor;
                case ItemObject.ItemTypeEnum.Horse:
                    return KitGroup.Mount;
                case ItemObject.ItemTypeEnum.HorseHarness:
                    return KitGroup.Barding;
                default:
                    return KitGroup.None;
            }
        }

        /// <summary>Whether two equipment classes are interchangeable in role (see <see cref="KitGroupOf"/>).</summary>
        private static bool SameKitGroup(ItemObject.ItemTypeEnum a, ItemObject.ItemTypeEnum b)
        {
            KitGroup group = KitGroupOf(a);
            return group != KitGroup.None && group == KitGroupOf(b);
        }

        /// <summary>
        /// The broad category a slot's LAST-RESORT fallback is confined to: every weapon-slot class
        /// (melee, thrown, launcher, ammunition, shield) is one Weapon category, all human armour is Armor,
        /// the mount and its barding are each their own. Wider than a <see cref="KitGroup"/> -- a launcher
        /// and a sword share no group but both count as weapons -- and it is what stops an out-of-stock slot
        /// from being filled across category lines (a weapon for an armour slot, or the reverse).
        /// </summary>
        private enum KitCategory
        {
            None,
            Weapon,
            Armor,
            Mount,
            Barding,
        }

        private static KitCategory KitCategoryOf(ItemObject.ItemTypeEnum itemType)
        {
            switch (KitGroupOf(itemType))
            {
                case KitGroup.Melee:
                case KitGroup.Thrown:
                case KitGroup.Launcher:
                case KitGroup.Ammunition:
                case KitGroup.Shield:
                    return KitCategory.Weapon;
                case KitGroup.Armor:
                    return KitCategory.Armor;
                case KitGroup.Mount:
                    return KitCategory.Mount;
                case KitGroup.Barding:
                    return KitCategory.Barding;
                default:
                    return KitCategory.None;
            }
        }

        /// <summary>Whether two equipment classes share a broad category (see <see cref="KitCategoryOf"/>).</summary>
        private static bool SameKitCategory(ItemObject.ItemTypeEnum a, ItemObject.ItemTypeEnum b)
        {
            KitCategory category = KitCategoryOf(a);
            return category != KitCategory.None && category == KitCategoryOf(b);
        }

        /// <summary>
        /// A cargo animal -- a pack animal (mule / sumpter horse / pack camel) or livestock (cattle, sheep).
        /// All of these share <see cref="ItemObject.ItemTypeEnum.Horse"/> with real riding, war and noble
        /// horses, but no soldier is ever mounted on one, so a promotion's mount slot must never be filled
        /// from the baggage train's cargo animals. A real ridden mount (<see cref="HorseComponent.IsMount"/>)
        /// returns false, as does any non-horse item -- only cargo animals are excluded here.
        /// </summary>
        internal static bool IsCargoAnimal(ItemObject item)
        {
            return item != null && item.HasHorseComponent && !item.HorseComponent.IsMount;
        }

        /// <summary>
        /// Actual equipment a soldier can be armed with -- weapons, ammunition, shields, armour, mounts and
        /// barding -- as opposed to the trade goods, livestock, books and food a market also stocks. What
        /// the broadened kit search is allowed to buy: a man's kit money spent on any war gear in band, but
        /// never on a bale of wool or a cask of wine because the helmets ran out. Cargo animals (pack animals
        /// and livestock, both <see cref="ItemObject.ItemTypeEnum.Horse"/>) are excluded via <see cref="IsCargoAnimal"/>.
        /// </summary>
        internal static bool IsWarGear(ItemObject item)
        {
            if (item == null)
            {
                return false;
            }
            if (IsCargoAnimal(item))
            {
                return false;
            }
            switch (item.ItemType)
            {
                case ItemObject.ItemTypeEnum.Horse:
                case ItemObject.ItemTypeEnum.OneHandedWeapon:
                case ItemObject.ItemTypeEnum.TwoHandedWeapon:
                case ItemObject.ItemTypeEnum.Polearm:
                case ItemObject.ItemTypeEnum.Arrows:
                case ItemObject.ItemTypeEnum.Bolts:
                case ItemObject.ItemTypeEnum.SlingStones:
                case ItemObject.ItemTypeEnum.Shield:
                case ItemObject.ItemTypeEnum.Bow:
                case ItemObject.ItemTypeEnum.Crossbow:
                case ItemObject.ItemTypeEnum.Sling:
                case ItemObject.ItemTypeEnum.Thrown:
                case ItemObject.ItemTypeEnum.HeadArmor:
                case ItemObject.ItemTypeEnum.BodyArmor:
                case ItemObject.ItemTypeEnum.LegArmor:
                case ItemObject.ItemTypeEnum.HandArmor:
                case ItemObject.ItemTypeEnum.Pistol:
                case ItemObject.ItemTypeEnum.Musket:
                case ItemObject.ItemTypeEnum.Bullets:
                case ItemObject.ItemTypeEnum.ChestArmor:
                case ItemObject.ItemTypeEnum.Cape:
                case ItemObject.ItemTypeEnum.HorseHarness:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The in-stock war gear closest in worth to <paramref name="targetValue"/>, of any class, kept
        /// inside a half-to-double band so a promotion never spends its coin on something wildly off. Only
        /// war gear is kit -- trade goods, livestock and food are passed over (<see cref="IsWarGear"/>).
        /// -1 when the market holds no war gear in band. The broadened half of the search: what a slot
        /// falls back to when its own class is out of stock, and the fallback for when the slot diff comes
        /// back empty and there is no specific class to match.
        /// </summary>
        /// <remarks>
        /// Shared with <see cref="RecruitSupply"/>, which falls back the same way and passes
        /// <paramref name="pricedAt"/> and <paramref name="maxValue"/> as for <see cref="FindKitOrAnyWarGear"/>.
        /// </remarks>
        internal static int FindKitInStock(ItemRoster market, int targetValue,
            Settlement pricedAt = null, int maxValue = int.MaxValue)
        {
            return new KitStock(market, pricedAt).FindKitInStock(targetValue, maxValue);
        }

        /// <summary>
        /// One market's stall as the kit searches see it, held for the length of a draw: every slot of
        /// every man searches this rather than walking the roster and re-pricing each candidate afresh.
        /// The searches (<see cref="FindKitOrAnyWarGear(ItemObject.ItemTypeEnum, int, int)"/>,
        /// <see cref="FindKitInStock(int, int)"/>) are the ones documented on the static wrappers above,
        /// and return the same roster index those return against the market as it stands.
        /// </summary>
        /// <remarks>
        /// <para>
        /// WHY THIS IS SAFE. The draws used to call <see cref="TroopMarketFeedback.UnitPrice"/> -- a full
        /// vanilla <c>GetPrice</c> plus RBM's scarcity postfix -- on every war-gear candidate for every slot
        /// of every man, hundreds of thousands of calls a day. Each unit price is a function of the element
        /// itself and of the market state of its own vanilla <see cref="ItemCategory"/> only: vanilla prices
        /// off the category's InStoreValue / Supply / Demand, and RBM's postfix off days of supply, which is
        /// counted across that same category (workshop inputs) or on the item itself (citizen goods), both
        /// against a per-day demand table. Taking one unit changes exactly two of those inputs, both for the
        /// taken item's category: the roster's update event lowers its InStoreValue, and the caller's demand
        /// signal raises its Demand. So after a take only that category's cached prices are dropped, and
        /// they are re-priced lazily -- after the caller has registered its demand -- the next time a
        /// search reaches them. Every other price is unchanged by construction, and the search sees the
        /// same numbers the old per-slot re-pricing produced.
        /// </para>
        /// <para>
        /// ORDER. The searches break ties by roster order (first strictly-better candidate wins), and a
        /// take that empties an element moves the roster's LAST element into its slot. The view mirrors
        /// that swap exactly, then checks itself against the roster after every take; any disagreement --
        /// or any change to the roster it did not make itself, caught by the roster's version number at the
        /// start of every search -- drops the view and rebuilds it from the roster with nothing cached. A
        /// stale view can therefore cost a re-price, never a different pick.
        /// </para>
        /// <para>
        /// Pricing is lazy: an element is priced only when a search reaches it past the same filters the old
        /// loops applied before pricing, so exactly the elements the old code priced are ever priced.
        /// </para>
        /// <para>
        /// Held only for the length of one synchronous draw (or a caller's loop of draws off one market),
        /// never across ticks: nothing else runs in between that moves a price input, and the roster
        /// version check catches any stock movement regardless.
        /// </para>
        /// </remarks>
        internal sealed class KitStock
        {
            private struct Entry
            {
                public ItemObject Item;
                public ItemModifier Modifier;
                public ItemCategory Category;
                public ItemObject.ItemTypeEnum ItemType;
                public KitCategory KitCategory;
                public KitGroup KitGroup;
                public bool IsCargo;
                public bool IsWarGear;
                public int Amount;
                public bool Priced;
                public int Price;
            }

            private ItemRoster _market;
            private Settlement _pricedAt;
            private Entry[] _entries = new Entry[0];
            private int _count;
            private int _version;
            private bool _loaded;

            /// <summary>An unbound view; <see cref="Bind"/> it to a market before searching.</summary>
            internal KitStock()
            {
            }

            /// <summary>
            /// A view of <paramref name="market"/>, priced at <paramref name="pricedAt"/>'s market (null:
            /// base value), as the static searches take them.
            /// </summary>
            internal KitStock(ItemRoster market, Settlement pricedAt)
            {
                Bind(market, pricedAt);
            }

            /// <summary>
            /// Points the view at <paramref name="market"/> priced at <paramref name="pricedAt"/>. A no-op
            /// when it already is, so a caller drawing several times off one market keeps what has been
            /// priced; anything else drops the view, to be rebuilt by the next search.
            /// </summary>
            internal void Bind(ItemRoster market, Settlement pricedAt)
            {
                if (!ReferenceEquals(market, _market) || !ReferenceEquals(pricedAt, _pricedAt))
                {
                    _market = market;
                    _pricedAt = pricedAt;
                    _loaded = false;
                }
            }

            /// <summary>Rebuilds the view when it is not loaded or the roster has moved since.</summary>
            private void EnsureCurrent()
            {
                if (_loaded && _market.VersionNo == _version)
                {
                    return;
                }
                int n = _market.Count;
                if (_entries.Length < n)
                {
                    _entries = new Entry[n];
                }
                for (int i = 0; i < n; i++)
                {
                    ItemRosterElement element = _market.GetElementCopyAtIndex(i);
                    ItemObject item = element.EquipmentElement.Item;
                    Entry entry = default(Entry);
                    entry.Item = item;
                    entry.Modifier = element.EquipmentElement.ItemModifier;
                    entry.Amount = element.Amount;
                    if (item != null)
                    {
                        entry.Category = item.ItemCategory;
                        entry.ItemType = item.ItemType;
                        entry.KitCategory = KitCategoryOf(item.ItemType);
                        entry.KitGroup = KitGroupOf(item.ItemType);
                        entry.IsCargo = IsCargoAnimal(item);
                        entry.IsWarGear = IsWarGear(item);
                    }
                    _entries[i] = entry;
                }
                // Drop the references a previous, longer roster left behind.
                for (int i = n; i < _count; i++)
                {
                    _entries[i] = default(Entry);
                }
                _count = n;
                _version = _market.VersionNo;
                _loaded = true;
            }

            /// <summary>
            /// The unit price of element <paramref name="i"/>, priced on first use and cached until a take
            /// moves its category. Only valid while the view mirrors the roster (index for index), which
            /// every caller guarantees by searching first.
            /// </summary>
            private int PriceAt(int i)
            {
                if (!_entries[i].Priced)
                {
                    _entries[i].Price = (_pricedAt != null)
                        ? TroopMarketFeedback.UnitPrice(_pricedAt, _entries[i].Item, _market, i)
                        : _entries[i].Item.Value;
                    _entries[i].Priced = true;
                }
                return _entries[i].Price;
            }

            /// <summary>
            /// The static <see cref="UpgradeSupply.FindKitOrAnyWarGear(ItemRoster, ItemObject.ItemTypeEnum, int, Settlement, int)"/>
            /// against this view: same filters, same order, same tie-breaks, same result.
            /// </summary>
            internal int FindKitOrAnyWarGear(ItemObject.ItemTypeEnum itemType, int targetValue, int maxValue = int.MaxValue)
            {
                EnsureCurrent();
                int low = targetValue / 2;
                int high = MathF.Min(targetValue * 2, maxValue);
                KitCategory slotCategory = KitCategoryOf(itemType);
                KitGroup slotGroup = KitGroupOf(itemType);

                int bestExact = -1, bestExactDelta = int.MaxValue;
                int bestGroup = -1, bestGroupDelta = int.MaxValue;
                int bestFallback = -1, bestFallbackDelta = int.MaxValue;

                // A slot of no category can be armed by nothing (SameKitCategory is false for every
                // candidate), so the walk would find nothing -- and price nothing -- either way.
                if (slotCategory == KitCategory.None)
                {
                    return -1;
                }

                for (int i = 0; i < _count; i++)
                {
                    if (_entries[i].Amount <= 0 || _entries[i].Item == null || _entries[i].IsCargo)
                    {
                        continue;
                    }
                    // Category eligibility gates the whole candidate (SameKitCategory); same-group is a
                    // strict subset of it (SameKitGroup). Both read off the precomputed class.
                    if (_entries[i].KitCategory != slotCategory)
                    {
                        continue;
                    }
                    bool sameGroup = slotGroup != KitGroup.None && _entries[i].KitGroup == slotGroup;

                    int value = PriceAt(i);
                    if (value < low || value > high)
                    {
                        continue;
                    }
                    int delta = MathF.Abs(value - targetValue);

                    if (_entries[i].ItemType == itemType && delta < bestExactDelta)
                    {
                        bestExactDelta = delta;
                        bestExact = i;
                    }
                    if (sameGroup && delta < bestGroupDelta)
                    {
                        bestGroupDelta = delta;
                        bestGroup = i;
                    }
                    if (delta < bestFallbackDelta)
                    {
                        bestFallbackDelta = delta;
                        bestFallback = i;
                    }
                }

                if (bestExact >= 0)
                {
                    return bestExact;
                }
                if (bestGroup >= 0)
                {
                    return bestGroup;
                }
                return bestFallback;
            }

            /// <summary>
            /// The static <see cref="UpgradeSupply.FindKitInStock(ItemRoster, int, Settlement, int)"/> against
            /// this view: same filters, same order, same tie-breaks, same result.
            /// </summary>
            internal int FindKitInStock(int targetValue, int maxValue = int.MaxValue)
            {
                EnsureCurrent();
                int best = -1;
                int bestDelta = int.MaxValue;
                int low = targetValue / 2;
                int high = MathF.Min(targetValue * 2, maxValue);
                for (int i = 0; i < _count; i++)
                {
                    if (_entries[i].Amount <= 0 || !_entries[i].IsWarGear)
                    {
                        continue;
                    }
                    int value = PriceAt(i);
                    if (value < low || value > high)
                    {
                        continue;
                    }
                    int delta = MathF.Abs(value - targetValue);
                    if (delta < bestDelta)
                    {
                        bestDelta = delta;
                        best = i;
                    }
                }
                return best;
            }

            /// <summary>
            /// Takes one unit of the element at <paramref name="index"/> -- an index a search on this view
            /// just returned -- off the market, and hands back its item and the price it went at (priced
            /// before it left, the figure the search ranked it by). Null, with nothing taken, for an index
            /// the view does not hold.
            /// </summary>
            /// <remarks>
            /// The removal is the same <c>AddToCounts(element, -1)</c> the draws always made. The view then
            /// mirrors what the roster did (one fewer; an emptied element replaced by the roster's last),
            /// forgets the cached prices of the taken item's category -- the only prices a take moves, see
            /// the class remarks -- and verifies itself against the roster, dropping itself on any mismatch.
            /// The caller registers its demand signal after this returns; the forgotten prices are only
            /// recomputed on the next search, so they see that demand, as the old re-pricing did.
            /// </remarks>
            internal ItemObject TakeOne(int index, out int price)
            {
                price = 0;
                if (!_loaded || index < 0 || index >= _count || _entries[index].Item == null
                    || _market.VersionNo != _version)
                {
                    return null;
                }
                price = PriceAt(index);
                ItemObject item = _entries[index].Item;
                ItemCategory category = _entries[index].Category;

                _market.AddToCounts(_market.GetElementCopyAtIndex(index).EquipmentElement, -1);
                _version++; // AddToCounts bumps the roster's version exactly once

                _entries[index].Amount--;
                if (_entries[index].Amount <= 0)
                {
                    int last = _count - 1;
                    _entries[index] = _entries[last];
                    _entries[last] = default(Entry);
                    _count = last;
                }
                for (int i = 0; i < _count; i++)
                {
                    if (ReferenceEquals(_entries[i].Category, category))
                    {
                        _entries[i].Priced = false;
                    }
                }
                if (!MirrorsMarket())
                {
                    _loaded = false;
                }
                return item;
            }

            /// <summary>Whether the view still matches the roster element for element.</summary>
            private bool MirrorsMarket()
            {
                if (_market.VersionNo != _version || _market.Count != _count)
                {
                    return false;
                }
                for (int i = 0; i < _count; i++)
                {
                    ItemRosterElement element = _market.GetElementCopyAtIndex(i);
                    if (element.Amount != _entries[i].Amount
                        || !ReferenceEquals(element.EquipmentElement.Item, _entries[i].Item)
                        || !ReferenceEquals(element.EquipmentElement.ItemModifier, _entries[i].Modifier))
                    {
                        return false;
                    }
                }
                return true;
            }
        }
    }
}
