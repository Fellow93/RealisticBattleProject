using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// Makes workshops forge mostly the gear the town's own troops carry ("pre-ordered" equipment), and
    /// stops any one piece of war gear piling up in a market.
    ///
    /// A recipe outputs an <c>ItemCategory</c>, and for weapons, shields and armour that category is a
    /// tier bucket (<c>melee_weapons_3</c>, <c>shield_2</c>, <c>medium_armor</c>, ...). Per culture such a
    /// bucket holds only a handful of items -- often 1-4 bows or 2-8 shields -- while one cycle draws up to
    /// a dozen units from it, each independently, and war gear has no stock gate at all
    /// (<c>TownStorage</c> leaves it uncapped). So the same two or three items flooded every market.
    ///
    /// For each equipment unit of a cycle this picker:
    /// <list type="number">
    /// <item>with <see cref="PreOrderShare"/> (<see cref="GarmentPreOrderShare"/> for garments, which also
    /// feed civilian wardrobes) draws from the town culture's troop kits that fall in the output category,
    /// choosing a group by <see cref="MilitiaWeight"/> / <see cref="TroopTreeWeight"/> /
    /// <see cref="MercenaryWeight"/>, then an item weighted by how many of that group's troops carry it. A
    /// group with nothing uncapped in the category drops out and its share goes to the others; if every
    /// group is empty the unit falls through to the open market;</item>
    /// <item>otherwise draws on the open market, i.e. <see cref="WorkshopItemTierBias.Pick"/>.</item>
    /// </list>
    /// Both legs skip any item already at <see cref="MarketCapPerItem"/> in the town market (summed over
    /// every quality stack, plus what this cycle has already drawn). When nothing in the category is left
    /// uncapped, the unit is not produced -- the cycle's income shrinks with it, so a fully stocked shop
    /// stops passing the profit gate on its own.
    ///
    /// Groups: militia = the culture's militia troops; troop trees = everything reachable by upgrade from
    /// <c>BasicTroop</c> and <c>EliteBasicTroop</c> (recruits, and the troops garrisons are filled from);
    /// mercenaries = the culture's tavern mercenaries and the minor-faction clans of that culture, with
    /// their upgrade trees. Items flagged <c>NotMerchandise</c> are never produced, matching vanilla.
    ///
    /// Ammunition is covered too: every arrow and bolt sits in the one untiered <c>arrows</c> category, so
    /// its troop pool is all the ammo the culture's troops carry. Every other output (trade goods, tools)
    /// goes through vanilla <c>GetRandomItem</c> unchanged.
    ///
    /// Logged (economy log) as SHOPGEAR, a town's day of war gear: units made per source, units skipped at
    /// the cap by category, and the most-made items; and GEARBOOK, once per culture per session, the size
    /// of each group's troop pool. Picks are only counted when the cycle's gate lets it run, since
    /// <c>GetItemsToProduce</c> is called before the gate and a refused cycle's picks are thrown away.
    ///
    /// Runs from the daily town tick (main campaign thread). The culture tables are derived data rebuilt
    /// per campaign, so nothing is saved.
    /// </summary>
    public static class WorkshopTroopOrders
    {
        /// <summary>Share of equipment units drawn from troop kits; the rest go to the open market.</summary>
        private const float PreOrderShare = 0.8f;

        /// <summary>Lower for garments: that category also clothes the townsfolk.</summary>
        private const float GarmentPreOrderShare = 0.5f;

        private const float MilitiaWeight = 50f;
        private const float TroopTreeWeight = 40f;
        private const float MercenaryWeight = 10f;

        /// <summary>A market holding this many of one item (all quality stacks) gets no more of it.</summary>
        private const int MarketCapPerItem = 6;

        private const int Militia = 0;
        private const int TroopTrees = 1;
        private const int Mercenaries = 2;
        private const int GroupCount = 3;
        private const int OpenMarket = GroupCount; // source index for the log only

        private static readonly float[] GroupWeights = { MilitiaWeight, TroopTreeWeight, MercenaryWeight };
        private static readonly string[] SourceNames = { "militia", "troops", "mercs", "open" };
        private static readonly char[] SourceLetters = { 'M', 'T', 'C', 'O' };

        /// <summary>Per culture, per group: output category → (item, number of troops carrying it).</summary>
        private static readonly Dictionary<CultureObject, Dictionary<ItemCategory, List<(ItemObject, float)>>[]> _ordersByCulture =
            new Dictionary<CultureObject, Dictionary<ItemCategory, List<(ItemObject, float)>>[]>();

        private static HashSet<ItemCategory> _equipmentCategories;
        private static Campaign _owningCampaign;

        private static readonly Func<WorkshopsCampaignBehavior, ItemCategory, Town, EquipmentElement> VanillaGetRandomItem =
            AccessTools.MethodDelegate<Func<WorkshopsCampaignBehavior, ItemCategory, Town, EquipmentElement>>(
                AccessTools.Method(typeof(WorkshopsCampaignBehavior), "GetRandomItem"));

        /// <summary>A town's day of war gear, or one cycle's picks waiting on the gate.</summary>
        private class GearTally
        {
            public readonly int[] BySource = new int[GroupCount + 1];
            public readonly int[] ForeignBySource = new int[GroupCount + 1];
            public readonly Dictionary<ItemObject, int[]> Made = new Dictionary<ItemObject, int[]>(); // per source
            public readonly Dictionary<string, int> SkippedAtCap = new Dictionary<string, int>();

            public void Record(ItemObject item, int source, bool foreign)
            {
                BySource[source]++;
                if (foreign)
                {
                    ForeignBySource[source]++;
                }
                if (!Made.TryGetValue(item, out int[] bySource))
                {
                    bySource = new int[GroupCount + 1];
                    Made[item] = bySource;
                }
                bySource[source]++;
            }

            public void Add(GearTally other)
            {
                for (int s = 0; s < BySource.Length; s++)
                {
                    BySource[s] += other.BySource[s];
                    ForeignBySource[s] += other.ForeignBySource[s];
                }
                foreach (KeyValuePair<ItemObject, int[]> pair in other.Made)
                {
                    if (!Made.TryGetValue(pair.Key, out int[] bySource))
                    {
                        bySource = new int[GroupCount + 1];
                        Made[pair.Key] = bySource;
                    }
                    for (int s = 0; s < bySource.Length; s++)
                    {
                        bySource[s] += pair.Value[s];
                    }
                }
                foreach (KeyValuePair<string, int> pair in other.SkippedAtCap)
                {
                    SkippedAtCap.TryGetValue(pair.Key, out int n);
                    SkippedAtCap[pair.Key] = n + pair.Value;
                }
            }
        }

        private static readonly Dictionary<Settlement, GearTally> _gearDay = new Dictionary<Settlement, GearTally>();

        // The last cycle's picks, set in GetItemsToProduce and committed by the gate postfix that the same
        // tick calls right after it. Main thread, one cycle at a time, so a single slot is enough.
        private static Workshop _pendingShop;
        private static GearTally _pendingCycle;

        /// <summary>Drops the session's caches and tallies, so GEARBOOK is rewritten into each session's log.</summary>
        public static void Reset()
        {
            _owningCampaign = null;
            _ordersByCulture.Clear();
            _gearDay.Clear();
            _pendingShop = null;
            _pendingCycle = null;
        }

        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "GetItemsToProduce")]
        private static class GetItemsToProducePatch
        {
            private static bool Prefix(
                WorkshopsCampaignBehavior __instance,
                WorkshopType.Production production,
                Workshop workshop,
                ref int income,
                ref List<EquipmentElement> __result,
                Dictionary<ItemCategory, List<ItemObject>> ____itemsInCategory)
            {
                Town town = workshop?.Settlement?.Town;
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || town == null)
                {
                    return true;
                }

                EnsureCacheOwner();

                List<EquipmentElement> list = new List<EquipmentElement>();
                income = 0;
                Dictionary<ItemObject, int> stock = null; // built on the first equipment unit
                GearTally cycle = EconomyLog.IsEnabled ? new GearTally() : null;

                for (int i = 0; i < production.Outputs.Count; i++)
                {
                    ItemCategory category = production.Outputs[i].Item1;
                    int count = production.Outputs[i].Item2;
                    bool isEquipment = _equipmentCategories.Contains(category);
                    if (isEquipment && stock == null)
                    {
                        stock = CountEquipmentStock(town);
                    }

                    for (int j = 0; j < count; j++)
                    {
                        int source = OpenMarket;
                        EquipmentElement picked = isEquipment
                            ? PickEquipment(category, town, ____itemsInCategory, stock, out source)
                            : VanillaGetRandomItem(__instance, category, town);
                        if (picked.IsEmpty)
                        {
                            // Everything in the category is at the cap.
                            if (cycle != null && isEquipment)
                            {
                                cycle.SkippedAtCap.TryGetValue(category.StringId, out int skipped);
                                cycle.SkippedAtCap[category.StringId] = skipped + 1;
                            }
                            continue;
                        }
                        if (isEquipment)
                        {
                            stock.TryGetValue(picked.Item, out int held);
                            stock[picked.Item] = held + 1;
                            if (cycle != null)
                            {
                                BasicCultureObject itemCulture = picked.Item.Culture;
                                bool foreign = itemCulture != null && itemCulture.StringId != "neutral_culture"
                                    && itemCulture != town.Culture;
                                cycle.Record(picked.Item, source, foreign);
                            }
                        }
                        list.Add(picked);
                        income += town.GetItemPrice(picked, null, isSelling: true);
                    }
                }

                _pendingShop = stock != null ? workshop : null;
                _pendingCycle = stock != null ? cycle : null;
                __result = list;
                return false;
            }
        }

        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "CanNotableWorkshopProduceThisCycle")]
        private static class NotableGateCommit
        {
            private static void Postfix(Workshop workshop, bool __result)
            {
                CommitPending(workshop, __result);
            }
        }

        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "CanPlayerWorkshopProduceThisCycle")]
        private static class PlayerGateCommit
        {
            private static void Postfix(Workshop workshop, bool __result)
            {
                CommitPending(workshop, __result);
            }
        }

        /// <summary>Adds the cycle's picks to the town's day only if the gate let the cycle run.</summary>
        private static void CommitPending(Workshop workshop, bool allowed)
        {
            GearTally cycle = _pendingCycle;
            bool sameShop = _pendingShop == workshop;
            _pendingShop = null;
            _pendingCycle = null;
            if (!allowed || cycle == null || !sameShop || workshop?.Settlement == null)
            {
                return;
            }
            if (!_gearDay.TryGetValue(workshop.Settlement, out GearTally day))
            {
                day = new GearTally();
                _gearDay[workshop.Settlement] = day;
            }
            day.Add(cycle);
        }

        /// <summary>Writes a town's day of war gear as SHOPGEAR and clears it.</summary>
        public static void FlushDaily(Settlement settlement)
        {
            if (settlement == null || !_gearDay.TryGetValue(settlement, out GearTally day))
            {
                return;
            }
            _gearDay.Remove(settlement);
            if (!EconomyLog.IsEnabled)
            {
                return;
            }

            int total = 0;
            StringBuilder sources = new StringBuilder();
            for (int s = 0; s < day.BySource.Length; s++)
            {
                total += day.BySource[s];
                sources.Append(s == 0 ? "" : "  ").Append(SourceNames[s]).Append(' ').Append(day.BySource[s]);
            }

            StringBuilder line = new StringBuilder();
            line.Append("made ").Append(total).Append(" gear (").Append(sources).Append(')');

            int foreignTotal = 0;
            StringBuilder foreignSources = new StringBuilder();
            for (int s = 0; s < day.ForeignBySource.Length; s++)
            {
                if (day.ForeignBySource[s] > 0)
                {
                    foreignTotal += day.ForeignBySource[s];
                    foreignSources.Append(foreignSources.Length == 0 ? "" : "  ")
                        .Append(SourceNames[s]).Append(' ').Append(day.ForeignBySource[s]);
                }
            }
            if (foreignTotal > 0)
            {
                line.Append("  ·  foreign ").Append(foreignTotal).Append(" (").Append(foreignSources).Append(')');
            }

            if (day.SkippedAtCap.Count > 0)
            {
                int skipped = 0;
                StringBuilder byCategory = new StringBuilder();
                foreach (KeyValuePair<string, int> pair in day.SkippedAtCap)
                {
                    skipped += pair.Value;
                    byCategory.Append("  ").Append(pair.Key).Append(" x").Append(pair.Value);
                }
                line.Append("  ·  skipped at cap ").Append(skipped).Append(':').Append(byCategory);
            }

            if (day.Made.Count > 0)
            {
                List<KeyValuePair<ItemObject, int>> made = new List<KeyValuePair<ItemObject, int>>(day.Made.Count);
                foreach (KeyValuePair<ItemObject, int[]> pair in day.Made)
                {
                    int n = 0;
                    foreach (int bySource in pair.Value)
                    {
                        n += bySource;
                    }
                    made.Add(new KeyValuePair<ItemObject, int>(pair.Key, n));
                }
                made.Sort((a, b) => b.Value.CompareTo(a.Value));
                line.Append("  ·  ").Append(made.Count).Append(" distinct, most made:");
                for (int i = 0; i < made.Count && i < LoggedTopItems; i++)
                {
                    // Source letters: M militia, T troop trees, C mercenaries (contract), O open market.
                    line.Append("  ").Append(made[i].Key.StringId).Append(" x").Append(made[i].Value).Append('/');
                    int[] bySource = day.Made[made[i].Key];
                    for (int s = 0; s < bySource.Length; s++)
                    {
                        if (bySource[s] > 0)
                        {
                            line.Append(SourceLetters[s]).Append(bySource[s]);
                        }
                    }
                }
            }

            EconomyLog.Log("SHOPGEAR", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                line.ToString());
        }

        private const int LoggedTopItems = 6;

        private static EquipmentElement PickEquipment(ItemCategory category, Town town,
            Dictionary<ItemCategory, List<ItemObject>> itemsInCategory, Dictionary<ItemObject, int> stock, out int source)
        {
            Func<ItemObject, bool> isCapped = item => stock.TryGetValue(item, out int held) && held >= MarketCapPerItem;

            float preOrderShare = category == DefaultItemCategories.Garment ? GarmentPreOrderShare : PreOrderShare;
            if (town.Culture != null && MBRandom.RandomFloat < preOrderShare)
            {
                ItemObject ordered = PickPreOrdered(category, town.Culture, isCapped, out source);
                if (ordered != null)
                {
                    return WorkshopItemTierBias.WithProductionModifier(ordered);
                }
            }
            source = OpenMarket;
            return WorkshopItemTierBias.Pick(category, town, itemsInCategory, isCapped);
        }

        private static ItemObject PickPreOrdered(ItemCategory category, CultureObject culture, Func<ItemObject, bool> isCapped,
            out int group)
        {
            Dictionary<ItemCategory, List<(ItemObject, float)>>[] orders = GetOrders(culture);

            // Filter each group to its uncapped items first, so an empty or full group hands its share on.
            List<(int, float)> groups = new List<(int, float)>(GroupCount);
            List<(ItemObject, float)>[] open = new List<(ItemObject, float)>[GroupCount];
            for (int g = 0; g < GroupCount; g++)
            {
                if (!orders[g].TryGetValue(category, out List<(ItemObject, float)> items))
                {
                    continue;
                }
                open[g] = items.FindAll(entry => !isCapped(entry.Item1));
                if (open[g].Count > 0)
                {
                    groups.Add((g, GroupWeights[g]));
                }
            }

            if (groups.Count == 0)
            {
                group = OpenMarket;
                return null;
            }
            group = MBRandom.ChooseWeighted(groups);
            return MBRandom.ChooseWeighted(open[group]);
        }

        /// <summary>Market stock of every war-gear item, summed across quality-modifier stacks.</summary>
        private static Dictionary<ItemObject, int> CountEquipmentStock(Town town)
        {
            Dictionary<ItemObject, int> stock = new Dictionary<ItemObject, int>();
            ItemRoster roster = town.Owner.ItemRoster;
            for (int i = 0; i < roster.Count; i++)
            {
                ItemObject item = roster.GetItemAtIndex(i);
                if (item == null || item.ItemCategory == null || !_equipmentCategories.Contains(item.ItemCategory))
                {
                    continue;
                }
                stock.TryGetValue(item, out int held);
                stock[item] = held + roster.GetElementNumber(i);
            }
            return stock;
        }

        private static void EnsureCacheOwner()
        {
            if (_owningCampaign == Campaign.Current && _equipmentCategories != null)
            {
                return;
            }
            _owningCampaign = Campaign.Current;
            _ordersByCulture.Clear();
            _equipmentCategories = new HashSet<ItemCategory>
            {
                DefaultItemCategories.MeleeWeapons1, DefaultItemCategories.MeleeWeapons2, DefaultItemCategories.MeleeWeapons3,
                DefaultItemCategories.MeleeWeapons4, DefaultItemCategories.MeleeWeapons5,
                DefaultItemCategories.RangedWeapons1, DefaultItemCategories.RangedWeapons2, DefaultItemCategories.RangedWeapons3,
                DefaultItemCategories.RangedWeapons4, DefaultItemCategories.RangedWeapons5,
                DefaultItemCategories.Shield1, DefaultItemCategories.Shield2, DefaultItemCategories.Shield3,
                DefaultItemCategories.Shield4, DefaultItemCategories.Shield5,
                DefaultItemCategories.Garment, DefaultItemCategories.LightArmor, DefaultItemCategories.MediumArmor,
                DefaultItemCategories.HeavyArmor, DefaultItemCategories.UltraArmor,
                DefaultItemCategories.Arrows, // all ammunition, bolts included
            };
        }

        private static Dictionary<ItemCategory, List<(ItemObject, float)>>[] GetOrders(CultureObject culture)
        {
            if (_ordersByCulture.TryGetValue(culture, out Dictionary<ItemCategory, List<(ItemObject, float)>>[] orders))
            {
                return orders;
            }

            List<CharacterObject>[] troops = new List<CharacterObject>[GroupCount];
            troops[Militia] = new List<CharacterObject>
            {
                culture.MeleeMilitiaTroop, culture.MeleeEliteMilitiaTroop,
                culture.RangedMilitiaTroop, culture.RangedEliteMilitiaTroop, culture.MilitiaVeteranArcher,
            };
            troops[TroopTrees] = WalkUpgradeTrees(new List<CharacterObject> { culture.BasicTroop, culture.EliteBasicTroop });

            List<CharacterObject> mercenaryRoots = new List<CharacterObject>();
            if (culture.BasicMercenaryTroops != null)
            {
                mercenaryRoots.AddRange(culture.BasicMercenaryTroops);
            }
            foreach (Clan clan in Clan.All)
            {
                if (clan.IsMinorFaction && !clan.IsBanditFaction && clan.Culture == culture)
                {
                    mercenaryRoots.Add(clan.BasicTroop);
                }
            }
            troops[Mercenaries] = WalkUpgradeTrees(mercenaryRoots);

            orders = new Dictionary<ItemCategory, List<(ItemObject, float)>>[GroupCount];
            for (int g = 0; g < GroupCount; g++)
            {
                orders[g] = BuildGroupOrders(troops[g]);
            }
            _ordersByCulture[culture] = orders;
            LogOrderBook(culture, troops, orders);
            return orders;
        }

        /// <summary>GEARBOOK: per group, how many troops and how many producible items per category.</summary>
        private static void LogOrderBook(CultureObject culture, List<CharacterObject>[] troops,
            Dictionary<ItemCategory, List<(ItemObject, float)>>[] orders)
        {
            if (!EconomyLog.IsEnabled)
            {
                return;
            }
            StringBuilder line = new StringBuilder();
            for (int g = 0; g < GroupCount; g++)
            {
                int troopCount = 0;
                foreach (CharacterObject troop in troops[g])
                {
                    if (troop != null)
                    {
                        troopCount++;
                    }
                }
                line.Append(g == 0 ? "" : "  |  ").Append(SourceNames[g]).Append(" (").Append(troopCount).Append(" troops):");
                List<KeyValuePair<ItemCategory, List<(ItemObject, float)>>> categories =
                    new List<KeyValuePair<ItemCategory, List<(ItemObject, float)>>>(orders[g]);
                categories.Sort((a, b) => string.CompareOrdinal(a.Key.StringId, b.Key.StringId));
                foreach (KeyValuePair<ItemCategory, List<(ItemObject, float)>> pair in categories)
                {
                    line.Append(' ').Append(pair.Key.StringId).Append('=').Append(pair.Value.Count);
                }
            }
            EconomyLog.Log("GEARBOOK", culture.StringId, line.ToString());
        }

        private static List<CharacterObject> WalkUpgradeTrees(List<CharacterObject> roots)
        {
            List<CharacterObject> frontier = new List<CharacterObject>();
            HashSet<CharacterObject> seen = new HashSet<CharacterObject>();
            foreach (CharacterObject root in roots)
            {
                if (root != null && seen.Add(root))
                {
                    frontier.Add(root);
                }
            }
            for (int i = 0; i < frontier.Count; i++)
            {
                CharacterObject[] targets = frontier[i].UpgradeTargets;
                if (targets == null)
                {
                    continue;
                }
                foreach (CharacterObject target in targets)
                {
                    if (target != null && seen.Add(target))
                    {
                        frontier.Add(target);
                    }
                }
            }
            return frontier;
        }

        /// <summary>Category → items the group's troops carry, weighted by how many troops carry each.</summary>
        private static Dictionary<ItemCategory, List<(ItemObject, float)>> BuildGroupOrders(List<CharacterObject> troops)
        {
            Dictionary<ItemObject, int> carriers = new Dictionary<ItemObject, int>();
            HashSet<ItemObject> kit = new HashSet<ItemObject>();
            HashSet<CharacterObject> seen = new HashSet<CharacterObject>();
            foreach (CharacterObject troop in troops)
            {
                if (troop == null || troop.IsHero || !seen.Add(troop))
                {
                    continue;
                }
                kit.Clear();
                foreach (Equipment equipment in troop.BattleEquipments)
                {
                    for (int slot = (int)EquipmentIndex.Weapon0; slot < (int)EquipmentIndex.ArmorItemEndSlot; slot++)
                    {
                        ItemObject item = equipment[(EquipmentIndex)slot].Item;
                        if (item != null && IsProducible(item))
                        {
                            kit.Add(item);
                        }
                    }
                }
                foreach (ItemObject item in kit)
                {
                    carriers.TryGetValue(item, out int n);
                    carriers[item] = n + 1;
                }
            }

            Dictionary<ItemCategory, List<(ItemObject, float)>> byCategory = new Dictionary<ItemCategory, List<(ItemObject, float)>>();
            foreach (KeyValuePair<ItemObject, int> entry in carriers)
            {
                ItemCategory category = entry.Key.ItemCategory;
                if (!byCategory.TryGetValue(category, out List<(ItemObject, float)> items))
                {
                    items = new List<(ItemObject, float)>();
                    byCategory[category] = items;
                }
                items.Add((entry.Key, entry.Value));
            }
            return byCategory;
        }

        // Vanilla's WorkshopsCampaignBehavior.IsProducable, restricted to war gear.
        private static bool IsProducible(ItemObject item)
        {
            return !item.MultiplayerItem && !item.NotMerchandise && !item.IsCraftedByPlayer
                && item.ItemCategory != null && _equipmentCategories.Contains(item.ItemCategory);
        }
    }
}
