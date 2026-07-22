using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// The settlement's half of a soldier's spending. A stack buying rations or a keepsake off the
    /// stalls, or drinking its wage away in the tavern, is a customer like any other: it pays what the
    /// market asks, the coin lands in the town's purse, and the buying pressure tells the market to
    /// ask for more next time.
    /// </summary>
    /// <remarks>
    /// Before this, troop spending was a pure sink -- goods vanished off the shelf and the coin
    /// vanished with them. That left the largest gold flow on the map economically invisible in the
    /// one place it happened, while still draining that place's physical stock.
    ///
    /// Towns only, like everything in the market-scaling layer (see <see cref="RBMMarketLiquidity"/>).
    /// A castle sits on vanilla prosperity and vanilla prices, so pricing a soldier's bread off a
    /// castle's market data would charge him roughly six times a town's asking price and starve every
    /// garrison that holds one. Castles and villages keep the flat item value, and neither takes the
    /// gold or demand legs at all.
    /// </remarks>
    public static class TroopMarketFeedback
    {
        /// <summary>
        /// How much of a town's recent troop trade is carried into its treasury target. A quarter of
        /// it: measured tallies run 20k-40k on an ordinary town, against a cap of 240 x Prosperity
        /// that lands at 36k-53k for the prosperities the countryside model actually produces. At 1.0
        /// every town with any traffic at all sat pinned near its ceiling, which made the term a flat
        /// treasury bonus rather than something a busy town could stand out by.
        /// </summary>
        private const float GarrisonTradeToTreasury = 0.25f;

        /// <summary>
        /// What the tally keeps of itself each day. 0.9 is a half-life of about a week and an
        /// effective window of ten days, so an army that marches out stops paying for the town's
        /// treasury inside a fortnight rather than endowing it forever.
        /// </summary>
        private const float TallyDecayPerDay = 0.9f;

        /// <summary>
        /// The most the trade term may add, as a share of the town's prosperity-derived target. A town
        /// is a place with a countryside, not a barracks: a large enough army parked long enough could
        /// otherwise make garrison trade the dominant term in the treasury and untether town gold from
        /// the land entirely.
        /// </summary>
        private const float MaxGarrisonTradeShare = 0.5f;

        // Gold the town has taken from soldiers over the recent past, decayed daily. Keyed by
        // settlement StringId, which is stable across a campaign and identical BETWEEN campaigns --
        // hence the reset in RBMEconomyCampaignBehavior's constructor, without which campaign B would
        // read campaign A's figures. Held as int because the save system has a defined container type
        // for Dictionary<string, int> and the fractions of a denar are not worth a type definer.
        private static Dictionary<string, int> _recentTroopSpend = new Dictionary<string, int>();

        public static void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("RBM_townTroopTrade", ref _recentTroopSpend);
            if (_recentTroopSpend == null)
            {
                _recentTroopSpend = new Dictionary<string, int>();
            }
        }

        /// <summary>
        /// Drops a previous campaign's tallies. Called from the owning behaviour's CONSTRUCTOR, which
        /// runs before the save is read: resetting any later -- in RegisterEvents or on session
        /// launched -- would wipe a genuine loaded save, since SyncData has already run by then. An
        /// absent key leaves the dictionary untouched rather than nulling it, so the null guard above
        /// never catches the leak on its own.
        /// </summary>
        public static void Reset()
        {
            _recentTroopSpend.Clear();
        }

        /// <summary>
        /// What a stack actually pays for one unit. A town prices by scarcity, so an army eating a
        /// besieged city's last grain finds it priced like the last grain; anywhere else the good is
        /// worth what it is worth.
        /// </summary>
        public static int UnitPrice(Settlement settlement, ItemObject item, ItemRoster roster, int index)
        {
            Town town = settlement != null ? settlement.Town : null;
            if (item == null || town == null || !town.IsTown || !RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return MathF.Max(1, roster.GetElementUnitCost(index));
            }
            return MathF.Max(1, town.MarketData.GetPrice(item));
        }

        /// <summary>
        /// A completed purchase off the stalls: the coin into the town's purse, the pressure into the
        /// category's demand, and the sum into the trade tally.
        /// </summary>
        public static void RegisterPurchase(Settlement settlement, ItemCategory category, int goldSpent)
        {
            Town town = Receiver(settlement, goldSpent);
            if (town == null)
            {
                return;
            }
            SettlementWealth.CreditCitizens(town.Settlement, goldSpent, SettlementWealth.Source.TroopGoods);
            // A soldier at a stall is a customer like any other, so the town takes its market fee on
            // what he spends -- see TradeTariff. Levied after the coin lands, so the fee comes out of a
            // purse that has already been paid rather than out of the town's standing float.
            TradeTariff.Levy(town.Settlement, goldSpent);
            AddToTally(town, goldSpent);
            PartyTradeFlow.RegisterInflow(town.Settlement, "troop-goods", goldSpent);
            if (category == null)
            {
                // An item with no category still pays the town; there is simply no demand pool for it.
                return;
            }
            // Shared with the town's own rations and the civilian consumption pass, so the units
            // conversion that feedback needs lives in exactly one place.
            RBMTownFoodSupply.RegisterPurchaseDemand(town.MarketData, category, goldSpent);
        }

        /// <summary>
        /// Coin spent on no good at all -- taverns, dice and worse. It reaches the town's purse the
        /// same way, but there is no item and so no category whose demand it could belong to.
        /// </summary>
        public static void RegisterServiceSpend(Settlement settlement, int goldSpent)
        {
            Town town = Receiver(settlement, goldSpent);
            if (town == null)
            {
                return;
            }
            SettlementWealth.CreditCitizens(town.Settlement, goldSpent, SettlementWealth.Source.Carousing);
            // Taverns and gambling houses pay the town's fee on their takings like any other trade.
            TradeTariff.Levy(town.Settlement, goldSpent);
            AddToTally(town, goldSpent);
            PartyTradeFlow.RegisterInflow(town.Settlement, "carousing", goldSpent);
        }

        private static Town Receiver(Settlement settlement, int goldSpent)
        {
            if (goldSpent <= 0 || settlement == null || !RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return null;
            }
            Town town = settlement.Town;
            return (town != null && town.IsTown) ? town : null;
        }

        private static void AddToTally(Town town, int goldSpent)
        {
            string key = town.Settlement.StringId;
            int running;
            _recentTroopSpend.TryGetValue(key, out running);
            _recentTroopSpend[key] = running + goldSpent;
        }

        /// <summary>
        /// The treasury target's garrison-trade term: what the town has recently earned from soldiers,
        /// capped against the countryside term so it can supplement the land's contribution without
        /// replacing it.
        /// </summary>
        public static float TreasuryBonus(Town town, float prosperityTerm)
        {
            if (town == null || !town.IsTown)
            {
                return 0f;
            }
            int running;
            if (!_recentTroopSpend.TryGetValue(town.Settlement.StringId, out running) || running <= 0)
            {
                return 0f;
            }
            return MathF.Min(running * GarrisonTradeToTreasury, MathF.Max(0f, prosperityTerm) * MaxGarrisonTradeShare);
        }

        /// <summary>
        /// Ages one town's tally by a day. Called from the economy behaviour's daily settlement tick,
        /// ahead of its logging gate, since the decay has to run whether or not anyone is watching.
        /// </summary>
        public static void DecayDaily(Settlement settlement)
        {
            Town town = settlement != null ? settlement.Town : null;
            if (town == null || !town.IsTown)
            {
                return;
            }
            string key = town.Settlement.StringId;
            int running;
            if (!_recentTroopSpend.TryGetValue(key, out running) || running <= 0)
            {
                return;
            }
            int decayed = MathF.Round(running * TallyDecayPerDay);
            // Rounding alone would leave a permanent handful of denars sitting in the tally forever.
            if (decayed >= running)
            {
                decayed = running - 1;
            }
            if (decayed <= 0)
            {
                _recentTroopSpend.Remove(key);
            }
            else
            {
                _recentTroopSpend[key] = decayed;
            }
        }

        /// <summary>For the economy log: what a town is currently carrying in recent troop trade.</summary>
        public static int RecentSpend(Town town)
        {
            int running;
            return (town != null && _recentTroopSpend.TryGetValue(town.Settlement.StringId, out running)) ? running : 0;
        }
    }
}
