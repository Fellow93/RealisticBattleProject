using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Scales the artisans' bench to the size of the town it stands in, and divides that capacity
    /// across whatever it is currently able to make.
    ///
    /// The artisans are not a shop. They are the hidden workshop every town has, standing in for
    /// every craftsman who is not one of the six named businesses -- so their output should not be a
    /// fixed rate at all. A town of two thousand souls has more hands at the bench than a town of
    /// four hundred, and those hands are shared: a town that can only get clay puts everyone on
    /// pottery, and the same town with iron, planks, wool and hides on the shelf spreads the same
    /// labour across all four trades and makes less of each.
    ///
    /// So a recipe's speed is multiplied by prosperity and divided by the number of recipes the town
    /// can currently supply. The two together are a labour pool, not a bonus: adding a recipe never
    /// adds capacity, it only re-slices it.
    /// </summary>
    /// <remarks>
    /// Applied by <see cref="RBMWorkshopModel"/>'s <c>GetEffectiveConversionSpeedOfProduction</c>
    /// override, which is handed the workshop and
    /// the recipe's declared speed but NOT which recipe is being ticked. That is exactly enough
    /// here, because the multiplier is a property of the shop and the town rather than of any one
    /// recipe -- every artisan recipe is scaled by the same figure on the same day.
    ///
    /// As an <c>AddFactor</c> so the buildings, policies and perks that already move workshop speed
    /// keep their proportional effect rather than being overwritten.
    ///
    /// "Active" means a recipe whose every input the town market can currently cover, which is the
    /// same test <c>DetermineItemRosterHasSufficientInputs</c> makes before a cycle runs -- counted
    /// in units of the input CATEGORY, since that is how a recipe consumes. A bench with no active
    /// recipe is scaled as if it had one, so the figure never collapses to the declared speed while a
    /// shop waits for an input. The count is cached per
    /// town per campaign day: the model is called once per recipe per day per workshop and again by
    /// every tooltip that shows a production figure, and recounting fifty recipes against the whole
    /// market roster on each of those calls would be a real cost for a number that moves once a day.
    /// </remarks>
    public static class ArtisanOutput
    {
        private const float OwnedWorkshopProsperityShare = 0.20f;

        private struct Bench
        {
            public int Day;
            public int Active;
            public int Total;
            public float ActiveSpeed;
        }

        // "settlement#workshopType" -> that type's reading, taken once a campaign day.
        private static readonly Dictionary<string, Bench> _activeCache = new Dictionary<string, Bench>();

        // Read by RBMWorkshopModel.GetEffectiveConversionSpeedOfProduction, which is the seam the scale
        // is applied at; the labels live here beside the math that earns them.
        internal static readonly TextObject ScaleText = new TextObject("{=RBM_ARTISAN_SCALE}Town crafts");
        internal static readonly TextObject OwnedScaleText = new TextObject("{=RBM_WORKSHOP_SCALE}Workshop share");

        internal static void ResetForNewSession()
        {
            _activeCache.Clear();
        }

        public static float Scale(Workshop workshop)
        {
            if (workshop == null || workshop.Settlement == null || workshop.Settlement.Town == null
                || workshop.WorkshopType == null)
            {
                return 1f;
            }

            Town town = workshop.Settlement.Town;
            float prosperity = town.Prosperity;
            if (prosperity <= 0f) return 1f;

            Bench bench = BenchFor(town, workshop.WorkshopType);

            // A bench with nothing it can currently make is still a bench of the same size. It used to
            // fall back to the recipe's declared speed here, which for a one-recipe shop meant the
            // whole scale switched off whenever its single input ran out -- a velvet weavery at 0.03
            // cycles a day with no cotton, and eighteen the day some arrived. The labour does not
            // leave when the shelf is bare; the input gate already stops the cycle, so the speed is
            // kept honest and the tooltip keeps saying what the shop can do. One idle recipe is the
            // smallest bench that can exist, so an empty count is read as one.
            int active = (bench.Active > 0) ? bench.Active : 1;

            // The config multiplier layers onto RBM's prosperity-driven scale, so both AI and player
            // shops -- and every tooltip and log that reads the model -- speed up or slow down together.
            float mult = RBMConfig.RBMConfig.workshopProductionMultiplier;

            if (workshop.WorkshopType.IsHidden)
            {
                int ownedCount = CountOwnedWorkshops(town);
                float share = 1f - OwnedWorkshopProsperityShare * ownedCount;
                if (share <= 0f) return 1f;
                return (prosperity * share) / active * mult;
            }

            return (prosperity * OwnedWorkshopProsperityShare) / active * mult;
        }

        private static int CountOwnedWorkshops(Town town)
        {
            Workshop[] shops = town.Workshops;
            if (shops == null) return 0;
            int count = 0;
            for (int i = 0; i < shops.Length; i++)
            {
                if (shops[i] != null && shops[i].WorkshopType != null && !shops[i].WorkshopType.IsHidden)
                    count++;
            }
            return count;
        }

        private static Bench BenchFor(Town town, WorkshopType type)
        {
            Bench bench = default(Bench);
            if (town == null || town.Settlement == null || type == null || type.Productions == null)
            {
                return bench;
            }

            string key = town.Settlement.StringId + "#" + type.StringId;
            int today = (int)CampaignTime.Now.ToDays;

            Bench cached;
            if (_activeCache.TryGetValue(key, out cached) && cached.Day == today)
            {
                return cached;
            }

            bench.Day = today;
            bench.Total = type.Productions.Count;
            foreach (WorkshopType.Production production in type.Productions)
            {
                if (CanSupply(town, production))
                {
                    bench.Active++;
                    bench.ActiveSpeed += production.ConversionSpeed;
                }
            }

            _activeCache[key] = bench;
            return bench;
        }

        /// <summary>
        /// Whether the town market holds enough of every input this recipe takes.
        /// </summary>
        private static bool CanSupply(Town town, WorkshopType.Production production)
        {
            if (production.Inputs == null || production.Inputs.Count == 0)
            {
                // An inputless recipe is always able to run, and is counted so that it takes its
                // share of the bench rather than being made free of the split.
                return true;
            }

            foreach (var input in production.Inputs)
            {
                if (input.Item1 == null)
                {
                    continue;
                }
                if (WorkshopDemand.UnitsInStore(town, input.Item1) < input.Item2)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// The day's reading of a town's bench: how much of it is at work, on how many trades, and how
        /// many cycles that is due to be worth.
        /// </summary>
        /// <remarks>
        /// Its own line because the multiplier is invisible everywhere else. The tick reads the model
        /// with <c>includeDescriptions: false</c>, so the "Town crafts" factor never reaches a log or a
        /// tooltip on the path that actually produces goods, and the shop lines that do exist answer
        /// different questions: SHOPBLOCK counts refused cycles across every workshop, SHOPIDLE names
        /// the artisan outputs a town went without. Neither says whether the bench was scaled to 4x or
        /// to 40x, which is the first thing to check when a town's output looks wrong.
        ///
        /// The due figure is the honest counterpart to SHOPBLOCK's attempted cycles: the declared
        /// speeds of the recipes the town can supply, times the multiplier. What separates the two is
        /// the profitability gate and the shop's purse, which is exactly the gap worth reading.
        /// </remarks>
        public static void LogDaily(Town town)
        {
            if (!EconomyLog.IsEnabled || town == null || town.Settlement == null || !town.IsTown)
            {
                return;
            }

            Workshop[] shops = town.Workshops;
            if (shops == null) return;

            string name = town.Settlement.Name != null ? town.Settlement.Name.ToString() : town.Settlement.StringId;
            int ownedCount = CountOwnedWorkshops(town);

            foreach (Workshop shop in shops)
            {
                if (shop == null || shop.WorkshopType == null) continue;

                Bench bench = BenchFor(town, shop.WorkshopType);
                float scale = Scale(shop);

                if (shop.WorkshopType.IsHidden)
                {
                    float artisanPct = (1f - OwnedWorkshopProsperityShare * ownedCount) * 100f;
                    EconomyLog.Log("SHOPSCALE", name,
                        "artisans  prosperity " + EconomyLog.Fmt(town.Prosperity)
                        + " × " + EconomyLog.Fmt(artisanPct) + "%"
                        + "  ·  active " + bench.Active + " of " + bench.Total + " recipes"
                        + "  ·  speed x" + EconomyLog.Fmt(scale)
                        + "  ·  " + EconomyLog.Fmt(bench.ActiveSpeed * scale) + " cycles/day due"
                        + " (declared " + EconomyLog.Fmt(bench.ActiveSpeed) + ")");
                }
                else
                {
                    string shopName = shop.WorkshopType.Name != null
                        ? shop.WorkshopType.Name.ToString()
                        : shop.WorkshopType.StringId;
                    EconomyLog.Log("SHOPSCALE", name,
                        shopName + "  prosperity " + EconomyLog.Fmt(town.Prosperity)
                        + " × " + EconomyLog.Fmt(OwnedWorkshopProsperityShare * 100f) + "%"
                        + "  ·  active " + bench.Active + " of " + bench.Total + " recipes"
                        + "  ·  speed x" + EconomyLog.Fmt(scale));
                }
            }
        }
    }
}
