using System.Collections.Generic;
using HarmonyLib;
using System.Reflection;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// The Naval DLC counterpart of <see cref="TradeGoodValues"/>. Kept in its own file because the
    /// two goods it reprices only exist when the Naval DLC is loaded: with the DLC absent there is
    /// nothing named <c>walrus_tusk</c> or <c>whale_oil</c> for the object manager to hand this hook,
    /// so the postfix simply never matches and the file is inert -- exactly the isolation that lets
    /// the core table stay a clean list of base-game goods.
    ///
    /// Same reasoning as the core table: value is the historical denar price x10, weight is the real
    /// mass in kilograms of one trade lot. Vanilla ships both goods at value 400/200 and weight 10;
    /// these put a lot of walrus ivory at a dense high-value luxury and a lot of whale oil at a
    /// cheap, heavy bulk good, so the same long-haul-favours-the-dense-luxury pressure the core
    /// table creates applies to the northern trade too.
    ///
    /// Both goods are declared in <c>NavalDLC/ModuleData/items.xml</c>, so like the XML-declared
    /// goods in the core table they arrive through <c>ItemObject.Deserialize</c>. The
    /// <c>InitializeTradeGood</c> hook is mirrored from the core table for symmetry and to stay
    /// correct should a future DLC build either good in code instead.
    /// </summary>
    /// <remarks>
    /// Gated on <c>rbmCampaignEnabled</c>, the same toggle the core table reads, rather than a
    /// naval-specific one: repricing these two in isolation while every other good stayed on the
    /// vanilla scale would be an inconsistency. The gate is belt-and-braces -- the class's patches
    /// are only applied from <c>RBMCampaignPatcher.DoPatching</c>, which runs only when
    /// <c>rbmCampaignEnabled</c>.
    /// </remarks>
    public static class NavalTradeGoodValues
    {
        // Value is the historical denar price x10; Weight is the real mass in kg of one trade lot.
        private static readonly Dictionary<string, (int value, float weight)> Table =
            new Dictionary<string, (int, float)>
            {
                { "walrus_tusk", (360, 5f) },      // dense high-value ivory
                { "whale_oil",   (36,  12.6f) },   // cheap, heavy bulk oil
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
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || item == null || item.StringId == null)
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
        /// Covers the XML-declared naval goods. Deserialize is where an item's Value and Weight are
        /// read off its XML node, so a postfix here is the earliest point at which the shipped
        /// numbers exist and the last point before anything downstream reads them.
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
        /// Mirrors the core table's code-built hook. Present for symmetry and forward-safety; the two
        /// goods currently in <see cref="Table"/> are XML-declared and pass through Deserialize above.
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
