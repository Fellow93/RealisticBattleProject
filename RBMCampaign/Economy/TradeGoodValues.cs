using System.Collections.Generic;
using HarmonyLib;
using System.Reflection;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// Reprices the trade goods. Vanilla's numbers are gameplay figures picked to make the trade
    /// minigame work; these are historically derived ones -- a period price in denars times ten,
    /// and the real mass in kilograms of one lot of the good. The two move together on purpose:
    /// a load of velvet is worth a fortune and weighs half a kilo, a load of hardwood is worth
    /// almost nothing and weighs two hundred, so what a cart can profitably carry stops being
    /// uniform across the goods list and long-haul trade starts favouring the dense luxuries the
    /// way it historically did.
    ///
    /// The table below is the single source of truth and covers goods from BOTH sources: the ones
    /// declared in SandBoxCore's <c>horses_and_others.xml</c> and the ones built in code by
    /// <c>DefaultItems.InitializeTradeGood</c>. Any item not named here -- tools, stolen goods, the
    /// trash item, and every non-Goods item in the game -- is left exactly as the game shipped it.
    ///
    /// NOTE on the code-defined goods: their StringIds come from the <c>Create(...)</c> calls in
    /// <c>DefaultItems.RegisterAll</c>, NOT from the mesh name passed to InitializeTradeGood. So
    /// iron ore is <c>iron</c> (not <c>iron_ore</c>) and the six iron/steel ingot tiers are
    /// <c>ironIngot1</c>..<c>ironIngot6</c> (not <c>crude_iron</c>/<c>steel</c>/...).
    /// </summary>
    public static class TradeGoodValues
    {
        // Value is the historical denar price x10; Weight is the real mass in kg of one trade lot.
        private static readonly Dictionary<string, (int value, float weight)> Table =
            new Dictionary<string, (int, float)>
            {
                // --- Goods declared in SandBoxCore/ModuleData/items/horses_and_others.xml ---
                { "wool",        (160,    2f) },
                { "silver",      (85,     0.85f) },
                { "jewelry",     (420,    0.025f) },
                { "salt",        (30,     1f) },
                { "spice",       (13,     1f) },
                { "cotton",      (1925,   1f) },
                { "flax",        (34,     1f) },
                { "clay",        (20,     10f) },
                { "pottery",     (100,    10f) },
                { "linen",       (170,    0.76f) },
                { "leather",     (176,    0.8f) },
                { "velvet",      (26500,  0.5f) },
                { "cheese",      (166,    15f) },
                { "butter",      (230,    8.4f) },
                { "fish",        (1140,   20f) },
                { "grape",       (275,    89f) },
                { "date_fruit",  (333,    20f) },
                { "olives",      (45,     46f) },
                { "beer",        (220,    110f) },
                { "wine",        (1330,   85f) },
                { "oil",         (270,    6.23f) },
                { "fur",         (833,    0.75f) },

                // --- Goods built in code by DefaultItems.InitializeTradeGood ---
                // StringIds taken from DefaultItems.RegisterAll, not from the mesh names.
                { "grain",       (60,     30f) },
                { "meat",        (200,    30f) },
                { "hides",       (88,     0.8f) },
                { "planks",      (10,     20f) },
                { "felt",        (250,    1f) },
                { "iron",        (1,      4f) },    // iron ore
                { "hardwood",    (11,     200f) },
                { "charcoal",    (3,      4f) },
                { "ironIngot1",  (4,      2f) },    // crude iron
                { "ironIngot2",  (11,     1f) },    // wrought iron
                { "ironIngot3",  (22,     1f) },    // iron
                { "ironIngot4",  (40,     1f) },    // steel
                { "ironIngot5",  (69,     1f) },    // fine steel
                { "ironIngot6",  (120,    1f) },    // thamaskene steel
            };

        // ItemObject.Value and ItemObject.Weight are public getters with private setters, so the
        // backing setters are resolved once here rather than per item.
        private static readonly MethodInfo ValueSetter = AccessTools.PropertySetter(typeof(ItemObject), "Value");
        private static readonly MethodInfo WeightSetter = AccessTools.PropertySetter(typeof(ItemObject), "Weight");

        /// <summary>
        /// Applies the table to one item, if it is named in it. Safe to call on anything: items
        /// outside the table, and items whose setters could not be resolved, are left untouched.
        /// </summary>
        public static void Apply(ItemObject item)
        {
            if (!RBMConfig.RBMConfig.realisticTradeGoodPrices || item == null || item.StringId == null)
            {
                return;
            }

            (int value, float weight) entry;
            if (!Table.TryGetValue(item.StringId, out entry))
            {
                return;
            }

            if (ValueSetter != null)
            {
                ValueSetter.Invoke(item, new object[] { entry.value });
            }

            if (WeightSetter != null)
            {
                WeightSetter.Invoke(item, new object[] { entry.weight });
            }
        }

        /// <summary>
        /// Covers the XML-declared goods. Deserialize is where an item's Value and Weight are read
        /// off its XML node, so a postfix here is the earliest point at which the shipped numbers
        /// exist and the last point before anything downstream (item category averages, the initial
        /// town stock seeding, the trade AI) reads them.
        /// </summary>
        [HarmonyPatch(typeof(ItemObject), "Deserialize")]
        private static class DeserializePatch
        {
            private static void Postfix(ItemObject __instance)
            {
                Apply(__instance);
            }
        }

        /// <summary>
        /// Covers the code-built goods, which never pass through Deserialize at all. This is the
        /// only place their Value and Weight are ever assigned, so a postfix on it is the exact
        /// counterpart of the one above. Together the two hooks cover both sources without needing
        /// a post-load sweep over the object manager, which would run later than the first readers
        /// of these figures and would have to guess at when "loading is done".
        /// </summary>
        [HarmonyPatch(typeof(ItemObject), "InitializeTradeGood")]
        private static class InitializeTradeGoodPatch
        {
            private static void Postfix(ItemObject item)
            {
                Apply(item);
            }
        }
    }
}
