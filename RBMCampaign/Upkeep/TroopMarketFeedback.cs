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
            // Log-only and keyed the same way, so a previous campaign's part-day would otherwise show
            // up as one bogus TAVERN line in the new one.
            _carousedGold.Clear();
            _carousedUnits.Clear();
            _carousedGoods.Clear();
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
        /// <param name="source">
        /// The ledger line the coin arrives under. Defaults to the stack's own upkeep, which is what
        /// nearly every purchase here is; recruit kit passes its own so the daily ledger can tell a
        /// settlement outfitting its musters apart from one feeding a passing army.
        /// </param>
        public static void RegisterPurchase(Settlement settlement, ItemCategory category, int goldSpent,
            string source = SettlementWealth.Source.TroopGoods)
        {
            // The coin lands wherever the settlement keeps its money -- a castle's market and a village's
            // single purse included, which used to burn it. The market fee rides along inside.
            if (!CreditLocalPurse(settlement, goldSpent, source))
            {
                return;
            }

            Town town = Receiver(settlement, goldSpent);
            if (town == null)
            {
                // Paid, but not a town: no demand pool to feed and no trade tally to keep, both of which
                // are town-scale machinery. See CreditLocalPurse.
                return;
            }
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
        /// What a soldier's carousing buys that actually comes off a shelf, and in what proportion.
        /// Beer above all, because that is what a tavern is; wine for the men who can afford it, and
        /// the rest is what is put in front of them to eat.
        /// </summary>
        /// <remarks>
        /// Grape is table fruit here, not a wine input, and it puts the taverns in direct competition
        /// with the wineries for the same vineyard crop -- which is the correct tension. A wine region
        /// with a garrison in it should find its presses short.
        /// </remarks>
        private static readonly KeyValuePair<string, float>[] TavernFare =
        {
            new KeyValuePair<string, float>("beer", 0.38f),
            new KeyValuePair<string, float>("wine", 0.18f),
            new KeyValuePair<string, float>("meat", 0.18f),
            new KeyValuePair<string, float>("cheese", 0.11f),
            new KeyValuePair<string, float>("fish", 0.08f),
            new KeyValuePair<string, float>("grape", 0.07f),
        };

        /// <summary>
        /// The share of carousing money that buys something physical rather than paying for the house.
        ///
        /// The other half is the tavern itself -- the keeper's labour, the room, the fire, the dice, the
        /// company. That part is a service: it pays the town exactly the same but consumes nothing off
        /// a shelf, which is why carousing could not simply be turned into a purchase wholesale.
        /// </summary>
        private const float CarousingGoodsShare = 0.5f;

        // What the taverns took and what they poured, per town, aggregated over the day's hourly
        // rounds and emitted by DecayDaily. Ephemeral and log-only.
        private static readonly Dictionary<string, int> _carousedGold = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _carousedUnits = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _carousedGoods = new Dictionary<string, int>();

        /// <summary>
        /// A stack drinking its wage away. The coin reaches the town's purse whole, but only part of it
        /// buys anything: the rest pays for the house.
        /// </summary>
        /// <remarks>
        /// Carousing used to be pure service -- the largest single inflow a town had, and it drained
        /// nothing off the market. A garrison could drink in a town for a month and not lower its beer
        /// by a barrel, which made soldiers the one customer whose money arrived without demand
        /// following it. Now half of it is spent over a counter on beer, wine, meat, cheese and fish,
        /// at the market's own prices, and those goods leave.
        ///
        /// The gold is UNCHANGED by the split, deliberately. The town is credited the full sum once,
        /// pays the fee on the full sum once, and tallies it once, exactly as before; all that is new is
        /// that some of it now removes stock and registers demand. So this cannot move any balance in
        /// the ledger -- it only decides whether the money took something with it.
        ///
        /// Fare the market has not got falls back to the house rather than going unspent. Men who find
        /// the beer gone do not take their coin home; they drink what there is, and pay the keeper for
        /// the privilege. That fallback is also what keeps the gold invariant above exactly true.
        /// </remarks>
        public static void RegisterServiceSpend(Settlement settlement, int goldSpent)
        {
            // Every settlement with a purse keeps what its taverns take -- castles and villages have
            // drinking houses too, and their takings used to be destroyed.
            if (!CreditLocalPurse(settlement, goldSpent, SettlementWealth.Source.Carousing))
            {
                return;
            }

            Town town = Receiver(settlement, goldSpent);
            if (town == null)
            {
                // Paid, but the fare is not bought: pricing it would need market data, and a castle's or
                // village's is on vanilla's scale where a loaf costs six times a town's. So outside a
                // town the whole sum stays with the house rather than being priced wrongly.
                return;
            }
            AddToTally(town, goldSpent);
            PartyTradeFlow.RegisterInflow(town.Settlement, "carousing", goldSpent);

            PourDrinks(town, MathF.Round(goldSpent * CarousingGoodsShare), goldSpent);
        }

        /// <summary>
        /// Takes the fare the drinkers got through off the market, spending up to
        /// <paramref name="budget"/> denars across <see cref="TavernFare"/> at market prices.
        /// </summary>
        /// <remarks>
        /// Bought by GOLD rather than by quantity, unlike the households' basket in
        /// <see cref="CitizenDemand"/> -- and that difference is right rather than an inconsistency. A
        /// household needs a fixed number of meals however dear they are; a soldier has a fixed number
        /// of coins and drinks whatever they buy. So a town short of beer sells the same men less of it
        /// at a higher price, which is exactly the pressure a scarce good should feel.
        ///
        /// No money moves here. The gold was credited whole by the caller, and both sides of this
        /// counter are inside citizen wealth anyway -- the soldier paid the tavern, the tavern pays its
        /// supplier, and neither crosses the settlement's boundary. Only the goods leave.
        /// </remarks>
        private static void PourDrinks(Town town, int budget, int goldSpent)
        {
            int poured = 0;
            int spentOnGoods = 0;

            if (budget > 0)
            {
                ItemRoster roster = town.Owner.ItemRoster;
                foreach (KeyValuePair<string, float> fare in TavernFare)
                {
                    int share = MathF.Round(budget * fare.Value);
                    if (share <= 0)
                    {
                        continue;
                    }

                    ItemObject item = Game.Current.ObjectManager.GetObject<ItemObject>(fare.Key);
                    if (item == null)
                    {
                        continue;
                    }

                    int price = MathF.Max(1, town.MarketData.GetPrice(item));
                    int wanted = share / price;
                    if (wanted <= 0)
                    {
                        continue;
                    }

                    int available = roster.GetItemNumber(item);
                    int taken = (available < wanted) ? available : wanted;
                    if (taken <= 0)
                    {
                        continue;
                    }

                    int cost = taken * price;
                    roster.AddToCounts(item, -taken);
                    poured += taken;
                    spentOnGoods += cost;

                    RBMTownFoodSupply.RegisterPurchaseDemand(town.MarketData, item.ItemCategory, cost);
                }
            }

            if (!EconomyLog.IsEnabled)
            {
                return;
            }

            string key = town.Settlement.StringId;
            int running;
            _carousedGold.TryGetValue(key, out running);
            _carousedGold[key] = running + goldSpent;
            _carousedUnits.TryGetValue(key, out running);
            _carousedUnits[key] = running + poured;
            _carousedGoods.TryGetValue(key, out running);
            _carousedGoods[key] = running + spentOnGoods;
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

        /// <summary>
        /// Puts a soldier's coin into whichever purse the settlement he spent it in actually keeps, and
        /// reports whether it found one.
        /// </summary>
        /// <remarks>
        /// This exists because the town gate above is about PRICING, not about who gets paid, and the two
        /// were conflated. Castles and villages sit on vanilla prosperity and vanilla prices, so a
        /// soldier's bread must not be costed off their market data -- but that is no reason for his
        /// money to cease to exist when he hands it over, which is exactly what happened: every spend
        /// path deducted the spoils unconditionally and then dropped the coin on the floor unless the
        /// settlement was a town.
        ///
        /// It was not a rounding error. A castle pays its garrison's wages and its militia's stipend out
        /// of its own treasury by real debits, every hour, into purses that then incinerated the money --
        /// so a castle's treasury drained into nothing at exactly the rate it paid its men.
        ///
        /// A town or castle takes it into the market, which is where a shopkeeper's takings belong. A
        /// village has no market -- one purse only -- so it goes there. See <see cref="SettlementWealth"/>.
        /// </remarks>
        private static bool CreditLocalPurse(Settlement settlement, int goldSpent, string source)
        {
            if (goldSpent <= 0 || settlement == null || !RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return false;
            }

            if (SettlementWealth.HasCitizenPurse(settlement))
            {
                SettlementWealth.CreditCitizens(settlement, goldSpent, source);
            }
            else if (settlement.IsVillage)
            {
                SettlementWealth.Credit(settlement, goldSpent, source);
            }
            else
            {
                return false;
            }

            // Towns take a market fee; a village has no market to charge one on, and Levy gates on that
            // itself.
            TradeTariff.Levy(settlement, goldSpent);
            return true;
        }

        /// <summary>
        /// A surgeon's fee. Reaches the settlement like any other purchase, which it did not before: the
        /// healing path deducted the coin from the soldier's purse and credited nobody at all, in every
        /// settlement including towns, despite its own description saying otherwise.
        /// </summary>
        /// <remarks>
        /// Registered as a service rather than a purchase -- no goods leave a shelf, and unlike carousing
        /// it buys no fare either, so it does not go through <see cref="RegisterServiceSpend"/>. A
        /// bonesetter's time is the whole of what is bought.
        /// </remarks>
        public static void RegisterSurgery(Settlement settlement, int goldSpent)
        {
            if (!CreditLocalPurse(settlement, goldSpent, SettlementWealth.Source.Surgery))
            {
                return;
            }
            Town town = Receiver(settlement, goldSpent);
            if (town != null)
            {
                AddToTally(town, goldSpent);
                PartyTradeFlow.RegisterInflow(settlement, "surgery", goldSpent);
            }
        }

        /// <summary>
        /// What a lord pays to muster a man. In a town it reaches the CITIZENS who armed him, reimbursing
        /// the wealth they fronted for his kit when he first stepped forward (the gear leg debits them; see
        /// <see cref="RecruitSupply.DrawKitFromMarket"/>) -- their profit on the man is the bounty over his
        /// gear. In a village, which keeps a single purse, it lands there, reimbursing what the village paid
        /// its trade town for the kit. Vanilla destroyed the whole recruit price; this is where it goes.
        /// </summary>
        /// <remarks>
        /// Deliberately untariffed. A tariff is a market fee: <see cref="TradeTariff.Levy"/> takes its cut
        /// OUT of citizen wealth and moves it to the treasury. The recruit price is the citizens' own
        /// reimbursement for gear they already paid for -- levying it would tax them on the recovery of
        /// their own outlay, which is why this credits them directly rather than through
        /// <see cref="CreditLocalPurse"/>.
        ///
        /// The goods a recruit walks off with are handled where they leave, when he is first raised -- and
        /// in a town his citizens are debited their value there -- so nothing is registered as demand here.
        /// </remarks>
        public static void RegisterRecruitPay(Settlement settlement, int goldSpent)
        {
            if (goldSpent <= 0 || settlement == null || !RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return;
            }
            // A town credits the citizens who armed him; a village its single purse. Castles never reach
            // here (PayRecruitPrice gates on town/village), so the split is exactly these two.
            if (SettlementWealth.HasCitizenPurse(settlement))
            {
                SettlementWealth.CreditCitizens(settlement, goldSpent, SettlementWealth.Source.Recruit);
            }
            else
            {
                SettlementWealth.Credit(settlement, goldSpent, SettlementWealth.Source.Recruit);
            }

            Town town = Receiver(settlement, goldSpent);
            if (town != null)
            {
                AddToTally(town, goldSpent);
                PartyTradeFlow.RegisterInflow(settlement, "recruit", goldSpent);
            }
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
        /// <remarks>
        /// DIAGNOSTIC ONLY as of the gold controller being switched off. It used to raise the target
        /// the controller regulated towards, which was the only way a garrison town could keep the
        /// money its garrison brought -- coin handed to a town already at target was destroyed within
        /// days, so income without a target move netted to nothing. There is no target to raise any
        /// more; soldier spending is simply kept, because nothing takes it away.
        ///
        /// It survives because the shadow target it feeds is still the yardstick the LIQUID drift line
        /// is measured against, and a yardstick that ignored garrison trade would read every garrison
        /// town as runaway. Retire this, its tally and its decay together with that line.
        /// </remarks>
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
            FlushTavernLog(town, key);

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

        /// <summary>
        /// Writes the day's takings at one town's taverns and clears the tally.
        /// </summary>
        /// <remarks>
        /// Aggregated over the day rather than written per round, because carousing ticks hourly for
        /// every stack of every party in the settlement -- logged raw it would drown every other line
        /// in the file.
        ///
        /// The number to read is the gap between what was spent on fare and what the fare share was
        /// meant to be. A town whose taverns take a fortune and pour almost nothing has no drink to
        /// sell, and the shortfall is silently reverting to the house.
        /// </remarks>
        private static void FlushTavernLog(Town town, string key)
        {
            int gold;
            if (!_carousedGold.TryGetValue(key, out gold) || gold <= 0)
            {
                return;
            }

            int units;
            int onGoods;
            _carousedUnits.TryGetValue(key, out units);
            _carousedGoods.TryGetValue(key, out onGoods);

            _carousedGold.Remove(key);
            _carousedUnits.Remove(key);
            _carousedGoods.Remove(key);

            int fareBudget = MathF.Round(gold * CarousingGoodsShare);
            EconomyLog.Log("TAVERN", town.Settlement.Name != null ? town.Settlement.Name.ToString() : key,
                "took " + gold + "d"
                + "  ·  fare " + onGoods + "d of " + fareBudget + "d budgeted → " + units + " units"
                + "  ·  house " + (gold - onGoods) + "d"
                + (onGoods < fareBudget ? "  ·  SHORT ON DRINK" : ""));
        }

        /// <summary>For the economy log: what a town is currently carrying in recent troop trade.</summary>
        public static int RecentSpend(Town town)
        {
            int running;
            return (town != null && _recentTroopSpend.TryGetValue(town.Settlement.StringId, out running)) ? running : 0;
        }
    }
}
