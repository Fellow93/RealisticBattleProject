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
    /// town is within reach, and the upgrade is treated as buying the new kit from that town -- the
    /// worth of the upgrade settles into the town's prosperity and value-appropriate stock leaves its
    /// market.
    ///
    /// The whole feature lives in this file plus a handful of call sites, each tagged with the comment
    /// "SupplyTown gate". Switch it off at runtime with RBMConfig.troopUpgradeRequireSupplyTown = 0;
    /// remove it outright by deleting this file, its csproj entry, and those tagged lines.
    /// </summary>
    public static class UpgradeSupply
    {
        /// <summary>On only when the spoils economy is on and the gate is switched on in config.</summary>
        public static bool IsEnabled
        {
            get { return SpoilsPool.IsEnabled && RBMConfig.RBMConfig.troopUpgradeRequireSupplyTown; }
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
        /// Buys the new kit for an upgrade off the supply town: value-appropriate stock leaves the
        /// market and the worth of the promotion settles into the town's prosperity, the same
        /// buy-then-credit path the food and luxury spending already run. A soft sink -- it buys what
        /// the market has and never holds an upgrade up for want of stock.
        /// </summary>
        // TODO: a future revision may turn this into a hard gate -- no suitable stock in the town, no
        // upgrade -- rather than the soft sink it is now.
        public static void SupplyUpgradeFromTown(Town town, PartyBase buyer, CharacterObject character, CharacterObject upgradeTarget, int count)
        {
            if (town == null || count <= 0)
            {
                return;
            }
            // What one upgraded man's new kit is worth over his old, which is both what we look to spend
            // it on in stock and what the town is paid for supplying it. Zero for a cheaper-kit upgrade:
            // nothing is bought and nothing changes hands.
            int perManValue = SpoilsPool.GetSpoilsCostForUpgrade(character, upgradeTarget);
            if (perManValue <= 0)
            {
                return;
            }
            TroopUpkeep.CreditSettlement(town.Settlement, perManValue * count);

            ItemRoster market = town.Settlement.ItemRoster;
            int bought = 0;
            if (market != null)
            {
                for (int man = 0; man < count; man++)
                {
                    int index = FindKitInStock(market, perManValue);
                    if (index < 0)
                    {
                        break; // soft sink: nothing suitable left, the rest of the batch is outfitted off-screen
                    }
                    market.AddToCounts(market.GetItemAtIndex(index), -1);
                    bought++;
                }
            }
            // Logged whenever a supply lands, not only when stock was pulled: the prosperity credit
            // above happens even to a bare market, and the shortfall is worth seeing on its own line.
            if (SpoilsLog.IsEnabled)
            {
                SpoilsLog.Log("UPGRADE", buyer, SpoilsLog.Describe(buyer) + " supplied " + bought + "/" + count
                    + "x " + SpoilsLog.Describe(upgradeTarget) + " kit from " + town.Settlement.Name
                    + " (~" + perManValue + " each), " + (perManValue * count) + " into its prosperity"
                    + (bought < count ? " — market short " + (count - bought) : ""));
            }
        }

        /// <summary>Looters and bandit-clan parties, which have no friendly settlements to supply from.</summary>
        private static bool IsBanditParty(MobileParty party)
        {
            return party != null && (party.IsBandit || (party.MapFaction != null && party.MapFaction.IsBanditFaction));
        }

        /// <summary>Friendly or neutral: the party's faction is not at war with the town's owner.</summary>
        private static bool IsFriendlyOrNeutral(MobileParty party, Settlement settlement)
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
        /// The in-stock market item closest in worth to <paramref name="targetValue"/>, kept inside a
        /// half-to-double band so a promotion never spends its coin on something wildly off. Food is
        /// never kit. -1 when the market holds nothing in band.
        /// </summary>
        private static int FindKitInStock(ItemRoster market, int targetValue)
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
                if (item == null || item.IsFood)
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
