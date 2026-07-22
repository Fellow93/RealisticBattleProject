using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Gives a town a separate store for each good, instead of one undifferentiated pile.
    ///
    /// A granary is not a warehouse is not a woodyard. Grain goes in a granary, salt in a dry store,
    /// charcoal under a lean-to, and being full of one says nothing about room for another. Vanilla and
    /// RBM both modelled a town's holdings as a single number -- <c>FoodStocks</c> against one
    /// <c>FoodStocksUpperLimit</c> -- which made every food fungible with every other. A town sitting on
    /// nineteen hundred fish and sixty grain read as a full granary: no shortage to the prosperity
    /// model, none to the siege logic, none on the town screen, and a brewery that could not buy a sack
    /// of grain.
    ///
    /// So each good gets its own ceiling, and when that ceiling is reached the town simply stops buying
    /// it. Nothing is destroyed and nothing is stopped from being made -- the goods stay with whoever
    /// brought them, to be carried on to a town that still has room. A market that will not take your
    /// fish is a market you take your fish elsewhere from.
    /// </summary>
    /// <remarks>
    /// The ceiling is <see cref="StorageDays"/> of what the town's own people get through, which is the
    /// only defensible basis available: a place stores what it eats. That makes the caps fall out of
    /// <see cref="CitizenDemand"/> rather than being a second table to keep in step -- grain gets a
    /// large store because grain is half the diet, dates a small one because dates are 1.8% of it.
    ///
    /// Goods the basket does not model are NOT capped. Iron, clay, tools and war gear are bought by
    /// workshops and passing parties rather than by households, so RBM has no measure of how fast a town
    /// gets through them, and capping on a guess would throttle the workshop economy on no evidence.
    /// Better to leave them uncapped and visibly so.
    /// </remarks>
    public static class TownStorage
    {
        /// <summary>
        /// Days of its own consumption a town will hold of any one good.
        ///
        /// Two months. A town that cannot hold more than a few weeks of anything cannot be besieged
        /// meaningfully, cannot carry a bad harvest, and cannot build the surplus that lets a workshop
        /// buy cheap and a merchant carry stock -- a granary is a buffer against the year, not against
        /// the week. Sixty days is enough to ride out a season and short enough that a market still
        /// cannot become an infinite sink for whatever a caravan happens to be carrying.
        ///
        /// <see cref="RBMTownFoodSupply"/>'s reporting cap is scaled to match, so the granary a town
        /// SHOWS and the granary it can actually fill stay the same size.
        /// </summary>
        public const float StorageDays = 60f;

        /// <summary>Returned when a good has no modelled ceiling: the town will take any amount.</summary>
        public const int Uncapped = int.MaxValue;

        // What each town turned away today, by good, for the log. Diagnostics only.
        private static readonly Dictionary<Settlement, Dictionary<string, int>> _refused =
            new Dictionary<Settlement, Dictionary<string, int>>();

        /// <summary>Drops the previous session's tallies. Diagnostics only, so a session hook is enough.</summary>
        public static void Reset()
        {
            _refused.Clear();
        }

        /// <summary>
        /// How many units of this good the town has room to store, or <see cref="Uncapped"/> for a good
        /// RBM does not model the consumption of.
        /// </summary>
        public static int Capacity(Town town, ItemObject item)
        {
            if (town == null || item == null || !town.IsTown)
            {
                return Uncapped;
            }

            float daily = CitizenDemand.DailyUnits(town, item.StringId);
            if (daily <= 0f && item.IsCivilian && IsGarment(item))
            {
                // Clothing has no trade-good id to look up: it is hundreds of distinct garments sharing
                // one appetite, so they share one ceiling too, counted across the whole wardrobe.
                daily = CitizenDemand.DailyGarments(town);
            }

            if (daily <= 0f)
            {
                return Uncapped;
            }
            return MathF.Max(1, MathF.Ceiling(daily * StorageDays));
        }

        /// <summary>
        /// Units of this good the town will still accept. Zero means its store is full.
        /// </summary>
        /// <remarks>
        /// Garments are counted as a wardrobe rather than per item, matching how their ceiling is set --
        /// otherwise every distinct tunic would get a month's supply of its own and the cap would never
        /// bind.
        /// </remarks>
        public static int Headroom(Town town, ItemObject item)
        {
            int capacity = Capacity(town, item);
            if (capacity == Uncapped)
            {
                return Uncapped;
            }

            int held = IsGarment(item) ? CountGarments(town) : town.Owner.ItemRoster.GetItemNumber(item);
            int room = capacity - held;
            return (room > 0) ? room : 0;
        }

        /// <summary>
        /// Clamps an intended purchase to what the town has room for, recording anything turned away.
        /// </summary>
        public static int Accept(Settlement settlement, ItemObject item, int offered)
        {
            if (offered <= 0 || settlement == null || settlement.Town == null || !settlement.IsTown || item == null)
            {
                return offered;
            }
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return offered;
            }

            int room = Headroom(settlement.Town, item);
            if (room >= offered)
            {
                return offered;
            }

            Record(settlement, item, offered - room);
            return (room > 0) ? room : 0;
        }

        private static bool IsGarment(ItemObject item)
        {
            if (item == null || !item.IsCivilian)
            {
                return false;
            }
            ItemObject.ItemTypeEnum type = item.ItemType;
            return type == ItemObject.ItemTypeEnum.HeadArmor
                || type == ItemObject.ItemTypeEnum.BodyArmor
                || type == ItemObject.ItemTypeEnum.LegArmor
                || type == ItemObject.ItemTypeEnum.HandArmor
                || type == ItemObject.ItemTypeEnum.Cape;
        }

        private static int CountGarments(Town town)
        {
            ItemRoster roster = town.Owner.ItemRoster;
            int held = 0;
            for (int i = roster.Count - 1; i >= 0; i--)
            {
                ItemRosterElement element = roster.GetElementCopyAtIndex(i);
                if (IsGarment(element.EquipmentElement.Item))
                {
                    held += element.Amount;
                }
            }
            return held;
        }

        private static void Record(Settlement settlement, ItemObject item, int units)
        {
            if (!EconomyLog.IsEnabled || units <= 0)
            {
                return;
            }
            Dictionary<string, int> byGood;
            if (!_refused.TryGetValue(settlement, out byGood))
            {
                byGood = new Dictionary<string, int>();
                _refused[settlement] = byGood;
            }
            string key = IsGarment(item) ? "clothing" : item.StringId;
            int running;
            byGood.TryGetValue(key, out running);
            byGood[key] = running + units;
        }

        /// <summary>
        /// Writes what a town turned away today for want of room, and clears the tally.
        /// </summary>
        /// <remarks>
        /// A good appearing here every day is one the countryside makes more of than the town can use --
        /// the opposite complaint to the DEMAND line's unmet list, and just as useful. If fish is
        /// refused daily while beer goes unmet, the villages are fishing when they should be growing
        /// barley, and no amount of adjusting the town will fix it.
        /// </remarks>
        public static void FlushDaily(Settlement settlement)
        {
            Dictionary<string, int> byGood;
            if (settlement == null || !_refused.TryGetValue(settlement, out byGood))
            {
                return;
            }
            _refused.Remove(settlement);

            if (!EconomyLog.IsEnabled || byGood.Count == 0)
            {
                return;
            }

            StringBuilder line = new StringBuilder();
            int total = 0;
            foreach (KeyValuePair<string, int> pair in byGood)
            {
                total += pair.Value;
                line.Append("  ").Append(pair.Key).Append(" ").Append(pair.Value);
            }

            EconomyLog.Log("STORE", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                "turned away " + total + " units for want of room  ·" + line);
        }
    }
}
