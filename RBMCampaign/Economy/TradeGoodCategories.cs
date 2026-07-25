using HarmonyLib;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// Gives charcoal and the six iron grades an <see cref="ItemCategory"/> each, so a workshop
    /// recipe can name the grade it wants.
    ///
    /// A recipe's inputs and outputs are written in CATEGORIES, not items -- <c>WorkshopType</c>
    /// resolves <c>input_item="ironIngot2"</c> through <c>GetObject&lt;ItemCategory&gt;</c> and
    /// <c>output="ItemCategory.ironIngot2"</c> through the presumed-object reader. Vanilla files
    /// every ingot from crude iron to thamaskene steel under the one <c>iron</c> category, and
    /// charcoal under <c>hardwood</c> alongside the logs, so until now there was nothing for those
    /// names to resolve to: an unresolved INPUT is dropped with only a debug line, leaving the
    /// recipe cheaper than written, and an unresolved OUTPUT quietly mints an empty category and
    /// produces into it. Seven of RBM's artisan recipes were written against grades and were
    /// running on neither.
    ///
    /// With the categories registered and the items moved into them, a recipe that asks for wrought
    /// iron gets wrought iron, and a forge with nothing but crude iron on the shelf stops forging
    /// mail rather than silently forging it out of the wrong metal.
    /// </summary>
    /// <remarks>
    /// Registration rides <c>MBSubModuleBase.InitializeSubModuleGameObjects</c>, the hook TaleWorlds
    /// added for exactly this -- the Naval DLC registers walrus tusk and whale oil the same way. It
    /// is called from <c>Game.InitializeDefaultGameObjects</c> immediately after
    /// <c>DefaultItemCategories</c> is built and before <c>DefaultItems</c>, the Items XML and (far
    /// later) the WorkshopTypes XML, so every reader downstream sees the full set. No Harmony patch
    /// is needed for the registration itself.
    ///
    /// Membership is a different matter: <c>ItemObject.ItemCategory</c> has a private setter and the
    /// seven items are built in code by <c>DefaultItems</c>, never deserialized, so the only place
    /// their category is ever assigned is the <c>InitializeTradeGood</c> call that creates them.
    /// Hence the prefix below, which swaps the argument on its way in. <see cref="TradeGoodValues"/>
    /// already postfixes the same method to reprice these same goods, for the same reason.
    ///
    /// Deliberately NOT gated on a config toggle. The recipes that name these categories ship in
    /// RBM's WorkshopTypes XML, which the engine loads whatever the campaign module's settings say,
    /// so a toggle could only ever produce the half-state this exists to remove: categories the
    /// recipes name but no item belongs to.
    /// </remarks>
    public static class TradeGoodCategories
    {
        // Item StringId -> the id of the category made for it. They are the same string on purpose:
        // ItemObject and ItemCategory are separate object-manager type records with independent
        // StringId spaces, and a grade's category is not usefully named anything else.
        private static readonly string[] Ids =
        {
            "charcoal",
            "ironIngot1",
            "ironIngot2",
            "ironIngot3",
            "ironIngot4",
            "ironIngot5",
            "ironIngot6",
        };

        /// <summary>
        /// Registers the seven categories on the current game. Idempotent: <c>RegisterPresumedObject</c>
        /// hands back the existing object if one is already filed under the id, and re-initialising it
        /// writes the same values.
        /// </summary>
        public static void Register(Game game)
        {
            if (game == null || game.ObjectManager == null)
            {
                return;
            }

            foreach (string id in Ids)
            {
                ItemCategory category = game.ObjectManager.RegisterPresumedObject(new ItemCategory(id));
                if (category == null)
                {
                    continue;
                }

                // Vanilla's own numbers for the category each good is leaving: iron is (10, 20) and
                // hardwood is (10, 10). Demand is per category, so splitting one category into seven
                // without repeating its figures would leave the grades demanded at nothing.
                bool isCharcoal = id == "charcoal";
                category.InitializeObject(
                    isTradeGood: true,
                    baseDemand: 10,
                    luxuryDemand: isCharcoal ? 10 : 20);
            }
        }

        /// <summary>
        /// Puts each of the seven goods into its own category as it is created.
        /// </summary>
        /// <remarks>
        /// A prefix rather than a postfix because <c>InitializeTradeGood</c> assigns the category from
        /// its argument, so amending the argument is the whole change; a postfix would have to reach
        /// past a private setter to undo what the method just did.
        ///
        /// The lookup is by the ITEM's StringId and the category is fetched by the same string, which
        /// is safe in both directions: an item not in the list keeps the category vanilla passed, and
        /// a category that somehow failed to register leaves the item where it was rather than
        /// nulling its category.
        /// </remarks>
        [HarmonyPatch(typeof(ItemObject), "InitializeTradeGood")]
        private static class InitializeTradeGoodPatch
        {
            private static void Prefix(ItemObject item, ref ItemCategory category)
            {
                if (item == null || item.StringId == null || Game.Current == null)
                {
                    return;
                }

                bool wanted = false;
                foreach (string id in Ids)
                {
                    if (id == item.StringId)
                    {
                        wanted = true;
                        break;
                    }
                }
                if (!wanted)
                {
                    return;
                }

                ItemCategory own = Game.Current.ObjectManager.GetObject<ItemCategory>(item.StringId);
                if (own != null)
                {
                    category = own;
                }
            }
        }
    }
}
