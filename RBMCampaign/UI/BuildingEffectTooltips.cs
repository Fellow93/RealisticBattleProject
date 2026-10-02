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
    /// So a plain "RBM:" block is appended to the effect text the town management screen builds, per
    /// building type, naming the RBM effects at all three levels at once. It hangs off
    /// <see cref="BuildingType.GetExplanationAtLevel"/> -- the single method every one of those surfaces
    /// reads (the project list's current and next-level lines both route through it) -- so one postfix
    /// covers them all, and a building RBM does not touch is left exactly as vanilla wrote it.
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
                string extra = Describe(__instance);
                if (extra == null)
                {
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
        /// Drops the <see cref="DeadEffects"/> lines from vanilla's per-level effect text. Each line is rendered
        /// exactly as vanilla renders it and cut out of the finished text, so nothing else is rebuilt.
        /// </summary>
        private static void StripDeadEffectLines(BuildingType type, int level, ref TextObject text)
        {
            string s = text.ToString();
            bool changed = false;
            foreach (BuildingEffectEnum effect in DeadEffects)
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
        /// The RBM line for a building type, or null when RBM adds nothing to it. Written at all three
        /// levels together (the "+10/20/30%" idiom) because a player choosing what to build is choosing a
        /// whole ladder, not the next rung.
        /// </summary>
        private static string Describe(BuildingType type)
        {
            if (type == DefaultBuildingTypes.SettlementFortifications || type == DefaultBuildingTypes.CastleFortifications)
            {
                return "RBM: siege defence +10/20/30% · garrison & militia maintenance -0/5/10%";
            }
            if (type == DefaultBuildingTypes.SettlementBarracks || type == DefaultBuildingTypes.CastleBarracks)
            {
                return "RBM: cost of arming garrison & militia recruits -5/10/15% · garrison and militia intake +1/2/3 per day when the treasury can fund them · garrison soft size +20/40/60 men (recruits get dearer from the recruit pool later) · militia soft cap +2/3/5% of the fief's manpower";
            }
            if (type == DefaultBuildingTypes.SettlementTrainingFields || type == DefaultBuildingTypes.CastleTrainingFields)
            {
                return "RBM: garrison promotions -5/10/15% · garrison and militia gain +10/20/30 experience a day (replaces the 1/2/3 above) · militia soft cap +1/2/3% of the fief's manpower";
            }
            if (type == DefaultBuildingTypes.SettlementGuardHouse)
            {
                return "RBM: tariff on caravan and traveller trade +0.3/0.6/1.0 percentage points · convicts kept at work on the fief's building projects";
            }
            if (type == DefaultBuildingTypes.CastleGuardHouse)
            {
                return "RBM: tariff on caravan and traveller trade +0.3/0.6/1.0 percentage points · convicts kept at work on the fief's building projects · militia soft cap +2/3/5% of the castle's manpower (replaces the militia per day above; the barracks owns intake)";
            }
            if (type == DefaultBuildingTypes.CastleCastallansOffice)
            {
                return "RBM: 10/20/30% of garrison recruits enlist as the culture's elite soldier rather than its common one · upkeep of the mounted garrison -10/20/30% · recruit pool max +10/20/30%";
            }
            if (type == DefaultBuildingTypes.CastleCraftmansQuarters)
            {
                return "RBM: the castle's daily income from its lands +10/20/30%";
            }
            if (type == DefaultBuildingTypes.CastleFarmlands)
            {
                return "RBM: the castle's own food production +10/20/30% (replaces the flat 6/12/18 food a day)";
            }
            if (type == DefaultBuildingTypes.SettlementTaxOffice)
            {
                return "RBM: wealth tax and minting cuts +5/10/15%, to the fief and its lord alike";
            }
            if (type == DefaultBuildingTypes.SettlementMarketplace)
            {
                return "RBM: tariff on every trade in the settlement +10/20/30%";
            }
            if (type == DefaultBuildingTypes.SettlementWarehouse || type == DefaultBuildingTypes.CastleGranary)
            {
                return "RBM: the granary holds 40/50/60 days of the fief's own eating (30 days with no granary), replacing the fixed food limit; a town's market refuses food beyond it";
            }
            if (type == DefaultBuildingTypes.SettlementMason || type == DefaultBuildingTypes.CastleMason)
            {
                return "RBM: construction efficiency +5/10/15% · labour ceiling +10/20/30% (replaces construction per day)";
            }
            if (type == DefaultBuildingTypes.SettlementWaterworks)
            {
                return "RBM: everything else the town has built is worth +10/20/30% more prosperity";
            }
            if (type == DefaultBuildingTypes.SettlementRoadsAndPaths || type == DefaultBuildingTypes.CastleRoadsAndPaths)
            {
                return "RBM: bound villages produce +5/10/15% more goods · bound villages' recruit pool growth +10/20/30%";
            }
            if (type == DefaultBuildingTypes.SettlementDailyTrainMilitia)
            {
                return "RBM: recruit pool growth +25% and militia soft cap +3% of the town's manpower while running";
            }
            if (type == DefaultBuildingTypes.SettlementDailyHousing)
            {
                return "RBM: recruit pool growth +25% while running";
            }
            if (type == DefaultBuildingTypes.CastleDailyRaiseTroops)
            {
                return "RBM: recruit pool growth +50% and militia soft cap +3% of the castle's manpower while running";
            }
            return null;
        }
    }
}
