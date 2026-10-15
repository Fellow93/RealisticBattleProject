using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Tells the player what a building is actually worth under RBM.
    ///
    /// Vanilla writes a building's effects out of its own effect table, one line per
    /// <c>BuildingEffectEnum</c>, and that table is still true as far as it goes -- a Barracks still cuts
    /// garrison wages, a Warehouse still helps the workshops. But most of what a building does in RBM is
    /// not in that table at all: the wall that decides a siege, the lodgings that decide how fast a
    /// garrison fills, the mason yard that decides whether anything gets built. A player reading the
    /// vanilla text would be choosing his projects on a fraction of the facts.
    ///
    /// So the RBM effects are appended to the effect text the town management screen builds, one line
    /// per effect with the value at the level asked for, in the same shape as vanilla's own lines. It
    /// hangs off <see cref="BuildingType.GetExplanationAtLevel"/> -- the single method every one of those
    /// surfaces reads (the project list's "L-I/L-II/L-III" current and next-level lines both route through
    /// it, each with its own level) -- so one postfix covers them all, and a building RBM does not touch
    /// is left exactly as vanilla wrote it. Vanilla lines for effects RBM removed or replaced are cut.
    /// </summary>
    internal static class BuildingEffectTooltips
    {
        [HarmonyPatch(typeof(BuildingType), "GetExplanationAtLevel")]
        private static class ExplanationPatch
        {
            private static void Postfix(BuildingType __instance, int level, ref TextObject __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || __instance == null || level < 1 || level > 3)
                {
                    return;
                }
                StripDeadEffectLines(__instance, level, ref __result);
                string extra = Describe(__instance, level);
                if (extra == null)
                {
                    return;
                }
                if (__result.ToString().Length == 0)
                {
                    __result = new TextObject("{=!}" + extra);
                    return;
                }

                // Composed through text variables rather than by string concatenation, so nothing in the
                // vanilla text is re-parsed as markup on the way through.
                TextObject wrapper = new TextObject("{=!}{BASE}\n{RBM}");
                wrapper.SetTextVariable("BASE", __result);
                wrapper.SetTextVariable("RBM", new TextObject("{=!}" + extra));
                __result = wrapper;
            }
        }

        /// <summary>
        /// Vanilla effects that do nothing under RBM, whose effect lines are removed from the building text:
        /// garrison capacity (RBM lifts the garrison size limit, see GarrisonPartySize) and garrison auto
        /// recruitment (RBM's own garrison growth replaces vanilla's, see GarrisonRecruitCost).
        /// </summary>
        private static readonly BuildingEffectEnum[] DeadEffects =
        {
            BuildingEffectEnum.GarrisonCapacity,
            BuildingEffectEnum.GarrisonAutoRecruitment,
        };

        /// <summary>
        /// Vanilla effects one building type has that RBM replaces with its own line: the castle Guard
        /// House's militia a day (MilitiaUpkeep skips it), Farmlands' flat food (RBMTownFoodSupply takes it
        /// back), the Mason's construction a day (Construction) and the Warehouse/Granary food limit
        /// (RBMTownFoodSupply.FoodStocksUpperLimitPatch). Empty for every other type.
        /// </summary>
        private static BuildingEffectEnum[] ReplacedEffects(BuildingType type)
        {
            if (type == DefaultBuildingTypes.CastleGuardHouse)
            {
                return new[] { BuildingEffectEnum.Militia };
            }
            if (type == DefaultBuildingTypes.CastleFarmlands)
            {
                return new[] { BuildingEffectEnum.FoodProduction };
            }
            if (type == DefaultBuildingTypes.SettlementMason || type == DefaultBuildingTypes.CastleMason)
            {
                return new[] { BuildingEffectEnum.ConstructionPerDay };
            }
            if (type == DefaultBuildingTypes.SettlementWarehouse || type == DefaultBuildingTypes.CastleGranary)
            {
                return new[] { BuildingEffectEnum.FoodStock };
            }
            return new BuildingEffectEnum[0];
        }

        /// <summary>
        /// Drops the <see cref="DeadEffects"/> and <see cref="ReplacedEffects"/> lines from vanilla's per-level
        /// effect text. Each line is rendered exactly as vanilla renders it and cut out of the finished text,
        /// so nothing else is rebuilt.
        /// </summary>
        private static void StripDeadEffectLines(BuildingType type, int level, ref TextObject text)
        {
            string s = text.ToString();
            bool changed = false;
            foreach (BuildingEffectEnum effect in DeadEffects.Concat(ReplacedEffects(type)))
            {
                float value = type.GetBaseBuildingEffectAmount(effect, level);
                if (value == 0f)
                {
                    continue;
                }
                TextObject lineText = GameTexts.FindText("str_building_effect_explanation", effect.ToString());
                lineText.SetTextVariable("BONUS_AMOUNT", value);
                lineText.SetTextVariable("BONUS_AMOUNT_PERCENT", value * 100f);
                string line = lineText.ToString();
                if (string.IsNullOrEmpty(line) || !s.Contains(line))
                {
                    continue;
                }
                s = s.Replace(line + "\n", string.Empty).Replace("\n" + line, string.Empty).Replace(line, string.Empty);
                changed = true;
            }
            if (changed)
            {
                TextObject stripped = new TextObject("{=!}{BASE}");
                stripped.SetTextVariable("BASE", s);
                text = stripped;
            }
        }

        /// <summary>
        /// Replaces vanilla's one-line building descriptions where they promise something RBM took away (a
        /// bigger garrison limit, auto recruitment) or leave out what the building now does for the recruit
        /// pool. Explanation has a private setter and the building types are rebuilt for every campaign, so
        /// this is called on each session launch (RBMSettlementWealthCampaignBehavior).
        /// </summary>
        public static void ApplyDescriptions()
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return;
            }
            SetExplanation(DefaultBuildingTypes.SettlementFortifications, new TextObject("{=rbm_bdesc_fort_town}Better fortifications and higher walls around the town. Strengthens the defence in a siege and lowers the upkeep of the garrison and militia."));
            SetExplanation(DefaultBuildingTypes.CastleFortifications, new TextObject("{=rbm_bdesc_fort_castle}Better fortifications and higher walls around the keep. Strengthens the defence in a siege and lowers the upkeep of the garrison and militia."));
            TextObject barracks = new TextObject("{=rbm_bdesc_barracks}Lodgings for garrison troops. Each level lets the garrison take in more men a day and grow larger before recruits get scarce, and lowers garrison wages and the cost of arming recruits.");
            SetExplanation(DefaultBuildingTypes.SettlementBarracks, barracks);
            SetExplanation(DefaultBuildingTypes.CastleBarracks, barracks);
            SetExplanation(DefaultBuildingTypes.CastleCastallansOffice, new TextObject("{=rbm_bdesc_castellan}A castellan who keeps the rolls of the valley's families. Each level enlarges the recruit pool, brings in more elite recruits and lowers garrison wages."));
            SetExplanation(DefaultBuildingTypes.SettlementDailyTrainMilitia, new TextObject("{=rbm_bdesc_trainmilitia}Schedule drills for commoners, increasing militia recruitment and the growth of the recruit pool."));
            SetExplanation(DefaultBuildingTypes.CastleDailyRaiseTroops, new TextObject("{=rbm_bdesc_raisetroops}Call up the men of the castle's lands, increasing militia recruitment and the growth of the recruit pool."));
            SetExplanation(DefaultBuildingTypes.SettlementDailyHousing, new TextObject("{=rbm_bdesc_housing}Construct housing so that more folks can settle, increasing population and the growth of the recruit pool."));
            TextObject roads = new TextObject("{=rbm_bdesc_roads}Increase village production, village hearth growth and the growth of the bound villages' recruit pools.");
            SetExplanation(DefaultBuildingTypes.SettlementRoadsAndPaths, roads);
            SetExplanation(DefaultBuildingTypes.CastleRoadsAndPaths, roads);
        }

        private static void SetExplanation(BuildingType type, TextObject text)
        {
            if (type != null)
            {
                Traverse.Create(type).Property("Explanation").SetValue(text);
            }
        }

        /// <summary>
        /// The RBM lines for a building type at one level (1-3), one effect per line in vanilla's own
        /// shape, or null when RBM adds nothing to it. Only that level's values: the project list shows
        /// the current and the next level side by side, each through its own call, so the comparison
        /// reads off the two numbers.
        /// </summary>
        private static string Describe(BuildingType type, int level)
        {
            List<string> lines = new List<string>();
            if (type == DefaultBuildingTypes.SettlementFortifications || type == DefaultBuildingTypes.CastleFortifications)
            {
                Line(lines, "{=RBM_BLDL_SIEGE_DEFENCE}+{VALUE}% siege defence", Pick(level, 10, 20, 30));
                Line(lines, "{=RBM_BLDL_FORT_UPKEEP}-{VALUE}% garrison and militia upkeep", Pick(level, 0, 5, 10));
            }
            else if (type == DefaultBuildingTypes.SettlementBarracks || type == DefaultBuildingTypes.CastleBarracks)
            {
                Line(lines, "{=RBM_BLDL_INTAKE}+{VALUE} garrison and militia recruits a day, if the treasury can pay", Pick(level, 1, 2, 3));
                Line(lines, "{=RBM_BLDL_ARMING_COST}-{VALUE}% cost of arming new recruits", Pick(level, 5, 10, 15));
                Line(lines, "{=RBM_BLDL_GARRISON_SOFT}+{VALUE} garrison size before recruits get scarce", Pick(level, 20, 40, 60));
                Line(lines, "{=RBM_BLDL_MILITIA_CAP}+{VALUE}% of manpower to the militia cap", Pick(level, 2, 3, 5));
            }
            else if (type == DefaultBuildingTypes.SettlementTrainingFields || type == DefaultBuildingTypes.CastleTrainingFields)
            {
                Line(lines, "{=RBM_BLDL_DRILL_XP}+{VALUE} daily experience from drill for garrison and militia", Pick(level, 10, 20, 30));
                Line(lines, "{=RBM_BLDL_PROMOTION_COST}-{VALUE}% garrison promotion cost", Pick(level, 5, 10, 15));
                Line(lines, "{=RBM_BLDL_MILITIA_CAP}+{VALUE}% of manpower to the militia cap", Pick(level, 1, 2, 3));
            }
            else if (type == DefaultBuildingTypes.SettlementGuardHouse || type == DefaultBuildingTypes.CastleGuardHouse)
            {
                LineText(lines, "{=RBM_BLDL_TRAVEL_TARIFF}+{VALUE} percentage points tariff on caravan and traveller trade", level == 1 ? "0.3" : level == 2 ? "0.6" : "1.0");
                lines.Add(new TextObject("{=RBM_BLDL_CONVICTS}Convicts work on the building projects").ToString());
                if (type == DefaultBuildingTypes.CastleGuardHouse)
                {
                    Line(lines, "{=RBM_BLDL_MILITIA_CAP}+{VALUE}% of manpower to the militia cap", Pick(level, 2, 3, 5));
                }
            }
            else if (type == DefaultBuildingTypes.CastleCastallansOffice)
            {
                Line(lines, "{=RBM_BLDL_ELITE_RECRUITS}{VALUE}% of garrison recruits join as elite troops", Pick(level, 10, 20, 30));
                Line(lines, "{=RBM_BLDL_MOUNTED_UPKEEP}-{VALUE}% mounted garrison upkeep", Pick(level, 10, 20, 30));
                Line(lines, "{=RBM_BLDL_POOL_MAX}+{VALUE}% recruit pool size", Pick(level, 10, 20, 30));
            }
            else if (type == DefaultBuildingTypes.CastleCraftmansQuarters)
            {
                Line(lines, "{=RBM_BLDL_LAND_INCOME}+{VALUE}% income from the castle's lands", Pick(level, 10, 20, 30));
            }
            else if (type == DefaultBuildingTypes.CastleFarmlands)
            {
                Line(lines, "{=RBM_BLDL_FOOD_PRODUCTION}+{VALUE}% castle food production", Pick(level, 10, 20, 30));
            }
            else if (type == DefaultBuildingTypes.SettlementTaxOffice)
            {
                Line(lines, "{=RBM_BLDL_WEALTH_TAX}+{VALUE}% wealth tax and minting income", Pick(level, 5, 10, 15));
            }
            else if (type == DefaultBuildingTypes.SettlementMarketplace)
            {
                Line(lines, "{=RBM_BLDL_MARKET_TARIFF}+{VALUE}% tariff on every trade in the town", Pick(level, 10, 20, 30));
            }
            else if (type == DefaultBuildingTypes.SettlementWarehouse || type == DefaultBuildingTypes.CastleGranary)
            {
                Line(lines, "{=RBM_BLDL_FOOD_DAYS}Stores {VALUE} days of food (30 without it)", Pick(level, 40, 50, 60));
            }
            else if (type == DefaultBuildingTypes.SettlementMason || type == DefaultBuildingTypes.CastleMason)
            {
                Line(lines, "{=RBM_BLDL_CONSTRUCTION_EFF}+{VALUE}% construction efficiency", Pick(level, 5, 10, 15));
                Line(lines, "{=RBM_BLDL_CONSTRUCTION_CAP}+{VALUE}% daily construction limit", Pick(level, 10, 20, 30));
            }
            else if (type == DefaultBuildingTypes.SettlementWaterworks)
            {
                Line(lines, "{=RBM_BLDL_WATERWORKS}+{VALUE}% prosperity from the town's other buildings", Pick(level, 10, 20, 30));
            }
            else if (type == DefaultBuildingTypes.SettlementRoadsAndPaths || type == DefaultBuildingTypes.CastleRoadsAndPaths)
            {
                Line(lines, "{=RBM_BLDL_VILLAGE_POOL}+{VALUE}% recruit pool growth in bound villages", Pick(level, 10, 20, 30));
            }
            else if (type == DefaultBuildingTypes.SettlementDailyTrainMilitia)
            {
                Line(lines, "{=RBM_BLDL_POOL_GROWTH}+{VALUE}% recruit pool growth", 25);
                Line(lines, "{=RBM_BLDL_MILITIA_CAP}+{VALUE}% of manpower to the militia cap", 3);
            }
            else if (type == DefaultBuildingTypes.SettlementDailyHousing)
            {
                Line(lines, "{=RBM_BLDL_POOL_GROWTH}+{VALUE}% recruit pool growth", 25);
            }
            else if (type == DefaultBuildingTypes.CastleDailyRaiseTroops)
            {
                Line(lines, "{=RBM_BLDL_POOL_GROWTH}+{VALUE}% recruit pool growth", 50);
                Line(lines, "{=RBM_BLDL_MILITIA_CAP}+{VALUE}% of manpower to the militia cap", 3);
            }
            return lines.Count == 0 ? null : string.Join("\n", lines);
        }

        private static int Pick(int level, int l1, int l2, int l3)
        {
            return level == 1 ? l1 : level == 2 ? l2 : l3;
        }

        /// <summary>Adds one effect line with its value, or nothing when the value is zero at this level.</summary>
        private static void Line(List<string> lines, string template, int value)
        {
            if (value != 0)
            {
                LineText(lines, template, value.ToString());
            }
        }

        private static void LineText(List<string> lines, string template, string value)
        {
            TextObject text = new TextObject(template);
            text.SetTextVariable("VALUE", value);
            lines.Add(text.ToString());
        }
    }
}
