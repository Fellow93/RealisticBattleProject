using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// Gives the artisans' six inputless recipes something to work with.
    ///
    /// Vanilla's <c>artisans</c> shop has six productions that declare no inputs at all: tier-1 melee
    /// weapons and arrows at 1.5 cycles a day, bows and shields at 1.2, garments and light armour at
    /// 0.8. They cost nothing and consume nothing, so every town in Calradia manufactures blades,
    /// arrows, shields and clothing out of the air, every day, forever, whatever its countryside grows
    /// or its mines dig. Nothing throttles them and nothing ever can, because there is no shelf they
    /// draw on to run dry.
    ///
    /// That is the last unmetered source in the town economy. <see cref="WorkshopPurse"/> closed the
    /// gold half of the artisans' books; this closes the goods half. Once these recipes have a bill of
    /// materials they pass through the same <c>DetermineItemRosterHasSufficientInputs</c> check as every
    /// other recipe, which means a town with no iron on the shelf stops forging, and a town whose
    /// villages raise no sheep stops weaving. Arms production becomes a thing the map has to supply.
    ///
    /// And second, it moves every woodworking recipe in the game off hardwood and onto planks -- see
    /// <see cref="TryMoveWoodToPlanks"/> -- because RBM's lumberjacks cut planks and no village
    /// anywhere makes hardwood, which had quietly left a quarter of the artisans' bench and the whole
    /// wood workshop unable to run at all.
    /// </summary>
    /// <remarks>
    /// Done in code rather than as an XML override because the merge rules leave no middle ground.
    /// <c>Production</c> nodes carry no id, so a partial <c>spworkshops.xml</c> would have its nodes
    /// APPENDED to the vanilla ones -- the inputless recipes would survive alongside the new ones and
    /// the change would do nothing. The only alternative, <c>_replaceWhileMerging="true"</c> on the
    /// WorkshopType, means copying all thirty-two of the artisans' recipes into RBM and thereafter
    /// silently masking anything TaleWorlds changes in the twenty-six this does not touch. Rewriting
    /// productions in place is the smaller and the more honest change.
    ///
    /// The rewrite works because <c>MBReadOnlyList&lt;T&gt;</c> is only read-only by name -- it derives
    /// from <c>List&lt;T&gt;</c>, and the <c>Productions</c> getter hands back the backing list itself,
    /// so an indexed assignment lands on the real object with no reflection. <c>Production</c> is a
    /// struct whose constructor allocates its own input and output lists, so a rebuilt copy shares
    /// nothing with the one it replaces.
    ///
    /// Idempotent by construction: it only ever touches a production that has NO inputs, so a second
    /// run finds nothing left to do. That matters because <c>WorkshopType</c> objects belong to the
    /// object manager rather than to a campaign, and this runs on every session launch.
    /// </remarks>
    public static class WorkshopRecipes
    {
        /// <summary>
        /// What each inputless output is made of, keyed by the output category's id. A recipe producing
        /// something absent from this table is left as vanilla wrote it.
        /// </summary>
        /// <remarks>
        /// One count of each: planks for everything the shop cuts, wool for everything it weaves, and
        /// no iron anywhere. Iron is what separates these six from the tiers above them -- a tier-1
        /// blade costs planks where tier 2 and 3 cost iron, and arrows likewise -- so the entry rung of
        /// each ladder is the cheap rung, made of the thing the forests grow rather than the thing the
        /// mines have to dig. The point of the change is not that tier 1 should be dear; it is that it
        /// should not be free.
        ///
        /// The wood is PLANKS rather than hardwood, and that is not a cosmetic choice. RBM's village
        /// production makes planks -- the lumberjack's speciality, with charcoal -- and makes hardwood
        /// nowhere at all, so a hardwood cost would be a cost no town could ever meet. It also puts
        /// planks to work: vanilla has the wood workshop saw hardwood into planks at 2 a day and then
        /// nothing anywhere consumes them, so they are a good with a source, a price and no purpose.
        /// Now the arms trade eats them.
        /// </remarks>
        private static Dictionary<string, List<ItemCategory>> BuildRecipeTable()
        {
            return new Dictionary<string, List<ItemCategory>>
            {
                { "melee_weapons",  new List<ItemCategory> { DefaultItemCategories.Planks } },
                { "arrows",         new List<ItemCategory> { DefaultItemCategories.Planks } },
                { "ranged_weapons", new List<ItemCategory> { DefaultItemCategories.Planks } },
                { "shield",         new List<ItemCategory> { DefaultItemCategories.Planks } },
                { "garment",        new List<ItemCategory> { DefaultItemCategories.Wool } },
                { "light_armor",    new List<ItemCategory> { DefaultItemCategories.Wool } },
            };
        }

        /// <summary>
        /// Walks every workshop type, moves its woodwork onto planks, and gives each inputless recipe
        /// its bill of materials.
        /// </summary>
        public static void Apply()
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return;
            }

            Dictionary<string, List<ItemCategory>> recipes = BuildRecipeTable();

            foreach (WorkshopType type in WorkshopType.All)
            {
                if (type == null || type.Productions == null)
                {
                    continue;
                }

                for (int i = 0; i < type.Productions.Count; i++)
                {
                    WorkshopType.Production production = type.Productions[i];
                    if (production.Inputs == null || production.Outputs == null)
                    {
                        continue;
                    }

                    if (production.Inputs.Count > 0)
                    {
                        WorkshopType.Production onPlanks;
                        if (TryMoveWoodToPlanks(production, out onPlanks))
                        {
                            type.Productions[i] = onPlanks;
                        }
                        // Anything else that already costs something is vanilla's business, and skipping
                        // it is also half of what makes a second call a no-op.
                        continue;
                    }

                    List<ItemCategory> inputs = FindInputsFor(production, recipes);
                    if (inputs == null)
                    {
                        continue;
                    }

                    WorkshopType.Production replacement = new WorkshopType.Production(production.ConversionSpeed);
                    foreach (var output in production.Outputs)
                    {
                        replacement.AddOutput(output.Item1, output.Item2);
                    }
                    foreach (ItemCategory input in inputs)
                    {
                        replacement.AddInput(input);
                    }

                    // MBReadOnlyList is a List underneath and Productions is the backing list itself.
                    type.Productions[i] = replacement;
                }
            }
        }

        /// <summary>
        /// Rebuilds a recipe with planks in place of hardwood, or reports that it needed no change.
        /// </summary>
        /// <remarks>
        /// Fifteen of the artisans' recipes and the whole of <c>wood_WorkshopType</c> are fed on
        /// hardwood: the upper weapon and shield tiers, all five grades of horse equipment, and the
        /// sawmill itself. RBM's villages make no hardwood, so under the mod every one of those is a
        /// recipe that can never run -- the wood workshop idle in every town that has one, and no shield
        /// above tier 1 ever made anywhere in Calradia. Moving them all onto planks puts them back to
        /// work on the good the lumberjacks actually cut.
        ///
        /// The sawmill's own <c>hardwood -&gt; planks</c> line is the one exception, left exactly as it
        /// is. Rewriting it would make planks out of planks, which is a loop rather than a recipe; it
        /// simply never runs now, and the eleven other lines of that workshop -- bows and shields -- go
        /// on working. So the wood workshop stops being a sawmill and becomes what its bench actually
        /// does, which is fine.
        ///
        /// A hardwood output is left alone throughout. Nothing in the game has one, but if something
        /// ever does, it is a source of the good, not a consumer, and turning it into a source of planks
        /// is not this method's business.
        /// </remarks>
        private static bool TryMoveWoodToPlanks(WorkshopType.Production production,
            out WorkshopType.Production replacement)
        {
            replacement = production;

            bool usesWood = false;
            foreach (var input in production.Inputs)
            {
                if (input.Item1 == DefaultItemCategories.Wood)
                {
                    usesWood = true;
                    break;
                }
            }
            if (!usesWood)
            {
                return false;
            }

            // The sawmill line. Substituting here would have it turn planks into planks.
            foreach (var output in production.Outputs)
            {
                if (output.Item1 == DefaultItemCategories.Planks)
                {
                    return false;
                }
            }

            WorkshopType.Production rebuilt = new WorkshopType.Production(production.ConversionSpeed);
            foreach (var output in production.Outputs)
            {
                rebuilt.AddOutput(output.Item1, output.Item2);
            }
            foreach (var input in production.Inputs)
            {
                ItemCategory category = (input.Item1 == DefaultItemCategories.Wood)
                    ? DefaultItemCategories.Planks
                    : input.Item1;
                rebuilt.AddInput(category, input.Item2);
            }

            replacement = rebuilt;
            return true;
        }

        /// <summary>
        /// The materials for a recipe, or null if none of its outputs is one this rewrites. Keyed off
        /// the FIRST output that matches, since none of the six produces more than one category.
        /// </summary>
        private static List<ItemCategory> FindInputsFor(WorkshopType.Production production,
            Dictionary<string, List<ItemCategory>> recipes)
        {
            foreach (var output in production.Outputs)
            {
                if (output.Item1 == null)
                {
                    continue;
                }
                List<ItemCategory> inputs;
                if (recipes.TryGetValue(output.Item1.StringId, out inputs))
                {
                    return inputs;
                }
            }
            return null;
        }
    }
}
