using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// The standing overhead every settlement carries before a single soldier or builder: the clerks,
    /// wardens, gate-watch and market officials who keep the place running. A small fixed staff that
    /// eats and draws a wage whatever else the settlement is doing.
    ///
    /// It is deliberately not scaled to prosperity. A town does not run on twice the bureaucracy when
    /// it doubles in size; the baseline is a floor, the fixed cost of being a place at all, and it is
    /// the same cost for a thriving town and a struggling one. That makes it bite hardest exactly where
    /// it should -- on a settlement whose income has collapsed and which now cannot even cover its own
    /// administration.
    ///
    /// Food comes off the settlement's stores like any other ration, and the wage is paid from the
    /// settlement's own purse. In a town the wage reaches the townsfolk who hold the offices, so it
    /// stays inside citizen wealth; in a village there is no such pot, so the wage simply leaves the
    /// purse into the untracked household economy the way any village spending does.
    /// </summary>
    public static class AdministrativeUpkeep
    {
        /// <summary>Roughly sixty officials to a town; the headcount is the wage and food below, not a count kept anywhere.</summary>
        public const int TownDailyFood = 3;
        public const int TownDailySalary = 300;

        /// <summary>Roughly twenty to a village -- a headman, his reeve, a few wardens.</summary>
        public const int VillageDailyFood = 1;
        public const int VillageDailySalary = 100;

        /// <summary>
        /// A settlement's daily administration: the wage from the purse, and for a village its food off
        /// the store. A town's administrative FOOD is bought with the rest of its rations in
        /// <see cref="RBMTownFoodSupply"/>, where the market and the treasury payment already live; only
        /// the wage is paid here.
        /// </summary>
        public static void OnDailyTick(Settlement settlement)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null)
            {
                return;
            }

            if (settlement.IsTown)
            {
                PayWage(settlement, TownDailySalary, toCitizens: true);
            }
            else if (settlement.IsVillage)
            {
                ConsumeVillageFood(settlement, VillageDailyFood);
                PayWage(settlement, VillageDailySalary, toCitizens: false);
            }
            else if (settlement.IsCastle)
            {
                // A castle earns its own income now (see CastleEconomy), so it carries its own
                // administration too: the keep's clerks and wardens, and the standing cost of the
                // walls that are its whole purpose. A castle has one pool and no market for a wage to
                // circulate back into, so both simply leave it, the way a village's spending does.
                PayWage(settlement, CastleEconomy.AdminDailySalary, toCitizens: false);
                PayWallsUpkeep(settlement);
            }
        }

        /// <summary>
        /// The daily cost of keeping a castle's walls sound -- masons, mortar, the gate-works -- scaled
        /// to how high the fortifications have been built (<c>Town.GetWallLevel</c>). Unlike the
        /// administration's wage this leaves the economy: it is paid out to tradesmen the ledger does
        /// not track, the way any coin a settlement spends on its own fabric does. A castle's keep is
        /// its reason to exist, so its walls are a cost it always carries.
        /// </summary>
        private static void PayWallsUpkeep(Settlement settlement)
        {
            if (settlement.Town == null)
            {
                return;
            }
            int wallLevel = settlement.Town.GetWallLevel();
            int cost = wallLevel * CastleEconomy.WallUpkeepPerLevel;
            if (cost <= 0)
            {
                return;
            }

            int paid = SettlementWealth.Debit(settlement, cost, SettlementWealth.Source.Maintenance);
            if (EconomyLog.IsEnabled && paid > 0)
            {
                EconomyLog.Log("WALLS", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                    "upkeep " + paid + "/" + cost + "d (wall lvl " + wallLevel + ")"
                    + (paid < cost ? "  ·  purse short" : "")
                    + "  ·  purse now " + SettlementWealth.GetSettlementWealth(settlement) + "d");
            }
        }

        /// <summary>
        /// Pays the administration's wage out of the settlement's purse, capped at what it holds.
        /// </summary>
        /// <remarks>
        /// In a town the offices are held by townsfolk, so the wage lands back in citizen wealth and the
        /// town's total is unchanged -- the treasury simply hands it to the people. A village has one
        /// purse and no citizen pot, so the wage leaves it: the money is now in the reeve's hands, which
        /// this economy does not track, the same as any coin a village spends on itself.
        /// </remarks>
        private static void PayWage(Settlement settlement, int wage, bool toCitizens)
        {
            int paid = SettlementWealth.Debit(settlement, wage, SettlementWealth.Source.Admin);
            if (paid <= 0)
            {
                return;
            }
            if (toCitizens)
            {
                SettlementWealth.CreditCitizens(settlement, paid, SettlementWealth.Source.Admin);
            }

            if (EconomyLog.IsEnabled)
            {
                EconomyLog.Log("ADMIN", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                    "wage " + paid + "/" + wage + "d"
                    + (paid < wage ? "  ·  purse short" : "")
                    + "  ·  purse now " + SettlementWealth.GetSettlementWealth(settlement) + "d");
            }
        }

        /// <summary>
        /// Eats the administration's ration out of a village's store -- the food it grew, before the
        /// convoy carries the rest to town. Cheapest food first, matching how the village sells and the
        /// town buys, so a village is never left holding grain while its clerks ate the fish.
        /// </summary>
        private static void ConsumeVillageFood(Settlement settlement, int amount)
        {
            if (settlement.Village == null || amount <= 0)
            {
                return;
            }
            ItemRoster roster = settlement.Village.Owner.ItemRoster;

            while (amount > 0)
            {
                int cheapestIndex = -1;
                int cheapestValue = int.MaxValue;
                for (int i = 0; i < roster.Count; i++)
                {
                    ItemObject item = roster.GetElementCopyAtIndex(i).EquipmentElement.Item;
                    if (item == null || item.ItemCategory == null
                        || item.ItemCategory.Properties != ItemCategory.Property.BonusToFoodStores
                        || roster.GetElementNumber(i) <= 0)
                    {
                        continue;
                    }
                    if (item.Value < cheapestValue)
                    {
                        cheapestValue = item.Value;
                        cheapestIndex = i;
                    }
                }
                if (cheapestIndex < 0)
                {
                    // Nothing left to eat. A village with no food in store is a real state -- a bad
                    // harvest, a raid -- and the administration simply goes short like everyone else.
                    return;
                }

                int take = roster.GetElementNumber(cheapestIndex);
                if (take > amount)
                {
                    take = amount;
                }
                roster.AddToCounts(roster.GetElementCopyAtIndex(cheapestIndex).EquipmentElement, -take);
                amount -= take;
            }
        }
    }
}
