using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
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
    /// Applied on <c>GetEffectiveConversionSpeedOfProduction</c>, which is handed the workshop and
    /// the recipe's declared speed but NOT which recipe is being ticked. That is exactly enough
    /// here, because the multiplier is a property of the shop and the town rather than of any one
    /// recipe -- every artisan recipe is scaled by the same figure on the same day.
    ///
    /// As an <c>AddFactor</c> so the buildings, policies and perks that already move workshop speed
    /// keep their proportional effect rather than being overwritten.
    ///
    /// "Active" means a recipe whose every input the town market can currently cover, which is the
    /// same test <c>DetermineItemRosterHasSufficientInputs</c> makes before a cycle runs -- counted
    /// in units of the input CATEGORY, since that is how a recipe consumes. The count is cached per
    /// town per campaign day: the model is called once per recipe per day per workshop and again by
    /// every tooltip that shows a production figure, and recounting fifty recipes against the whole
    /// market roster on each of those calls would be a real cost for a number that moves once a day.
    /// </remarks>
    public static class ArtisanOutput
    {
        // The hidden every-town workshop. Named shops keep their declared rates.
        private const string ArtisansTypeId = "artisans";

        // A day's reading of one town's bench: the recipes it could supply, how many recipes the shop
        // has in total, and the declared speeds of the supplied ones added up. The last two are for
        // the log alone -- the multiplier needs only the count -- but they come free from the same
        // walk, and taking them later would mean walking every recipe against the market a second
        // time on a day the answer is already known.
        private struct Bench
        {
            public int Day;
            public int Active;
            public int Total;
            public float ActiveSpeed;
        }

        // Settlement id -> that town's reading, taken once a campaign day.
        private static readonly Dictionary<string, Bench> _activeCache = new Dictionary<string, Bench>();

        private static readonly TextObject ScaleText = new TextObject("{=RBM_ARTISAN_SCALE}Town crafts");

        internal static void ResetForNewSession()
        {
            _activeCache.Clear();
        }

        /// <summary>
        /// The multiplier on every artisan recipe in this town: prosperity split across the recipes
        /// the town can currently feed. 1 (no change) for a town whose artisans can make nothing,
        /// where there is nothing to scale anyway.
        /// </summary>
        public static float Scale(Workshop workshop)
        {
            if (workshop == null || workshop.Settlement == null || workshop.Settlement.Town == null)
            {
                return 1f;
            }

            Town town = workshop.Settlement.Town;
            Bench bench = BenchFor(town, workshop.WorkshopType);
            if (bench.Active <= 0)
            {
                return 1f;
            }

            float prosperity = town.Prosperity;
            if (prosperity <= 0f)
            {
                return 1f;
            }

            return prosperity / bench.Active;
        }

        private static Bench BenchFor(Town town, WorkshopType type)
        {
            Bench bench = default(Bench);
            if (town == null || town.Settlement == null || type == null || type.Productions == null)
            {
                return bench;
            }

            string key = town.Settlement.StringId;
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

            Workshop artisans = null;
            Workshop[] shops = town.Workshops;
            if (shops != null)
            {
                foreach (Workshop shop in shops)
                {
                    if (shop != null && shop.WorkshopType != null && shop.WorkshopType.StringId == ArtisansTypeId)
                    {
                        artisans = shop;
                        break;
                    }
                }
            }
            if (artisans == null)
            {
                return;
            }

            Bench bench = BenchFor(town, artisans.WorkshopType);
            float scale = Scale(artisans);
            string name = town.Settlement.Name != null ? town.Settlement.Name.ToString() : town.Settlement.StringId;

            EconomyLog.Log("SHOPSCALE", name,
                "artisans  prosperity " + EconomyLog.Fmt(town.Prosperity)
                + "  ·  active " + bench.Active + " of " + bench.Total + " recipes"
                + "  ·  speed x" + EconomyLog.Fmt(scale)
                + "  ·  " + EconomyLog.Fmt(bench.ActiveSpeed * scale) + " cycles/day due"
                + " (declared " + EconomyLog.Fmt(bench.ActiveSpeed) + ")");
        }

        [HarmonyPatch(typeof(DefaultWorkshopModel), "GetEffectiveConversionSpeedOfProduction")]
        private static class ConversionSpeedPatch
        {
            private static void Postfix(Workshop workshop, ref ExplainedNumber __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled
                    || workshop == null
                    || workshop.WorkshopType == null
                    || workshop.WorkshopType.StringId != ArtisansTypeId)
                {
                    return;
                }

                float scale = Scale(workshop);
                if (scale == 1f)
                {
                    return;
                }

                __result.AddFactor(scale - 1f, ScaleText);
            }
        }
    }
}
