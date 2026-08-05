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

        /// <summary>
        /// The nearest town of a faction the party is not at war with, however far off it lies.
        /// </summary>
        /// <remarks>
        /// Measured straight-line rather than through <c>SettlementHelper.FindNearestTownToMobileParty</c>,
        /// which the gate above uses. That helper runs a map-distance query per town, and this is called
        /// from paths that run for every party on the map -- the daily maintenance charge above all. The
        /// two want different things besides: the gate has to know whether a town is genuinely in reach,
        /// while this only has to name a payee, and a payee picked as the crow flies is close enough even
        /// where the crow would have to cross a sea.
        /// </remarks>
        internal static Town FindNearestFriendlyTown(MobileParty party)
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
                float distance = party.GetPosition2D.Distance(settlement.GetPosition2D);
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
            int drawnValue = 0;
            // The DRAW is the gated half: switch the supply-town feature off and a promotion takes nothing
            // off anyone's shelves, as it did before the feature existed. The payment below runs either
            // way -- see PaymentEnabled for why the two are not one switch.
            if (market != null && perManValue > 0 && goldBuyers > 0 && IsEnabled)
            {
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
                            // The right class and tier first, then any war gear at that value: a town short
                            // of the exact piece still arms the man from what it has. See FindKitOrAnyWarGear.
                            int index = FindKitOrAnyWarGear(market, slot.ItemType, slot.Value);
                            if (index < 0)
                            {
                                break; // soft sink: no war gear in band at all, the rest are outfitted off-screen
                            }
                            if (!TakeFromStock(town, market, index, ref drawnValue))
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
                        int index = FindKitInStock(market, perManValue);
                        if (index < 0)
                        {
                            break;
                        }
                        if (!TakeFromStock(town, market, index, ref drawnValue))
                        {
                            break;
                        }
                        bought++;
                    }
                }
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
            Town best = null;
            float bestDistance = float.MaxValue;
            foreach (Town town in Town.AllTowns)
            {
                Settlement settlement = town.Settlement;
                if (settlement == null)
                {
                    continue;
                }
                float distance = party.GetPosition2D.Distance(settlement.GetPosition2D);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = town;
                }
            }
            return best;
        }

        /// <summary>
        /// Takes one piece off the stall and reports what it was worth, adding it to
        /// <paramref name="drawnValue"/>. False only when the slot held nothing to take.
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
        private static bool TakeFromStock(Town town, ItemRoster market, int index, ref int drawnValue)
        {
            ItemObject item = market.GetItemAtIndex(index);
            if (item == null)
            {
                return false;
            }
            int price = TroopMarketFeedback.UnitPrice(town.Settlement, item, market, index);
            market.AddToCounts(item, -1);
            drawnValue += price;
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
        /// The in-stock market item of the right class and tier if the market has one, and otherwise ANY
        /// war gear of about that worth. -1 only when the market holds no war gear at all in band.
        /// </summary>
        /// <remarks>
        /// This is the two-stage search both the recruit and the upgrade draws walk each kit slot with:
        /// find the nearest part of the same class first -- a body upgrade pulling body armour, a horse
        /// upgrade a horse -- and, when that class is out of stock, broaden to whatever war gear the town
        /// does hold at that value rather than leaving the slot bare and outfitting the man off-screen. A
        /// picked-over frontier market still arms its men from what it has, spending the kit's worth on
        /// spare shields or helmets when the exact piece is gone. Trade goods and food are never kit --
        /// see <see cref="IsWarGear"/>.
        /// </remarks>
        internal static int FindKitOrAnyWarGear(ItemRoster market, ItemObject.ItemTypeEnum itemType, int targetValue)
        {
            int index = FindKitInStock(market, itemType, targetValue);
            if (index < 0)
            {
                index = FindKitInStock(market, targetValue);
            }
            return index;
        }

        /// <summary>
        /// Actual equipment a soldier can be armed with -- weapons, ammunition, shields, armour, mounts and
        /// barding -- as opposed to the trade goods, livestock, books and food a market also stocks. What
        /// the broadened kit search is allowed to buy: a man's kit money spent on any war gear in band, but
        /// never on a bale of wool or a cask of wine because the helmets ran out.
        /// </summary>
        internal static bool IsWarGear(ItemObject item)
        {
            if (item == null)
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
        /// The in-stock market item of class <paramref name="itemType"/> closest in worth to
        /// <paramref name="targetValue"/>, kept inside a half-to-double band so a promotion never spends
        /// its coin on something wildly off tier. -1 when the market holds nothing of that class in band.
        /// Matching the class is what makes a horse upgrade pull a horse and a body upgrade pull body armour.
        /// </summary>
        /// <remarks>Shared with <see cref="RecruitSupply"/>, which draws a recruit's kit the same way.</remarks>
        internal static int FindKitInStock(ItemRoster market, ItemObject.ItemTypeEnum itemType, int targetValue)
        {
            int best = -1;
            int bestDelta = int.MaxValue;
            int low = targetValue / 2;
            int high = targetValue * 2;
            for (int i = 0; i < market.Count; i++)
            {
                if (market.GetElementNumber(i) <= 0)
                {
                    continue;
                }
                ItemObject item = market.GetItemAtIndex(i);
                if (item == null || item.ItemType != itemType)
                {
                    continue;
                }
                int value = item.Value;
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
        /// The in-stock war gear closest in worth to <paramref name="targetValue"/>, of any class, kept
        /// inside a half-to-double band so a promotion never spends its coin on something wildly off. Only
        /// war gear is kit -- trade goods, livestock and food are passed over (<see cref="IsWarGear"/>).
        /// -1 when the market holds no war gear in band. The broadened half of the search: what a slot
        /// falls back to when its own class is out of stock, and the fallback for when the slot diff comes
        /// back empty and there is no specific class to match.
        /// </summary>
        /// <remarks>Shared with <see cref="RecruitSupply"/>, which falls back the same way.</remarks>
        internal static int FindKitInStock(ItemRoster market, int targetValue)
        {
            int best = -1;
            int bestDelta = int.MaxValue;
            int low = targetValue / 2;
            int high = targetValue * 2;
            for (int i = 0; i < market.Count; i++)
            {
                if (market.GetElementNumber(i) <= 0)
                {
                    continue;
                }
                ItemObject item = market.GetItemAtIndex(i);
                if (!IsWarGear(item))
                {
                    continue;
                }
                int value = item.Value;
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
    }
}
