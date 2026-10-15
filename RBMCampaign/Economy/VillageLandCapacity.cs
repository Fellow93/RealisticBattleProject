using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Gives every village a ceiling on its hearths: the land it works can feed only so many households.
    ///
    /// Vanilla's hearth growth has no ceiling at all. <c>DefaultSettlementProsperityModel.CalculateHearthChange</c>
    /// adds +4 a day below 300 hearths, +1.2 from 300 to 600, and then +0.2 a day forever, with Roads and
    /// Paths, the Irrigation daily project, governor perks and the Merciful trait stacking on top. Over a long
    /// campaign villages grew into the thousands, and everything RBM sizes on a village's hearths grew with
    /// them -- its production, its levy, its share of the town that eats its grain -- until a village could
    /// field a militia larger than a lord's party.
    ///
    /// So each village now has a LAND CAPACITY: <see cref="CapacityPerStartingHearth"/> times the hearth it
    /// started the campaign with (read from the settlements XML, see <see cref="StartingHearth"/>), raised by
    /// its bound fief's Roads and Paths (<see cref="RoadsCapacityPerLevel"/>) and, while it is the fief's
    /// running daily project, Irrigation (<see cref="IrrigationCapacityBonus"/>). A village's own XML size
    /// is the measure of its land: a hamlet on poor hills stays a hamlet, a valley village stays large.
    ///
    /// RBM authors the whole daily change rather than shaving vanilla's: the hearth CLOSES ON its capacity.
    /// Below it a village grows by <see cref="GrowthRate"/> of the room left a day; above it, it declines by
    /// <see cref="OverCapacityDeclineRate"/> of the excess a day -- the same pace both ways, so a village
    /// fills its land quickly while it has much of it to fill and settles into the ceiling over the years,
    /// and one pushed over (an old save from before this rule, a quest reward, an incident, Irrigation ending)
    /// drifts back down rather than staying. Vanilla's stepped base (+4 / +1.2 / +0.2 by size) and its Roads
    /// and Irrigation growth lines are gone: the buildings raise the capacity instead. Vanilla's MODIFIERS
    /// are kept as it computes them -- looted, Grazing Rights, the governor perks, the Empire feat, Merciful,
    /// issues -- so only the growth spine is RBM's (see <see cref="BuildHearthChange"/>).
    /// </summary>
    public static class VillageLandCapacity
    {
        /// <summary>
        /// A village's land capacity as a multiple of its starting hearth. Two: a village can double from
        /// where the world authors put it, which leaves the smallest (100 hearths) room to reach 200 and the
        /// median (about 300) room to reach 600 -- the size vanilla itself treats as a large village -- while
        /// the biggest (825) top out near 1650 instead of growing without end.
        /// </summary>
        public const float CapacityPerStartingHearth = 2.0f;

        /// <summary>
        /// Roads and Paths of the village's bound town or castle: capacity added per level, +5% / +10% / +15%
        /// at levels 1/2/3. Roads bring a market within reach, so the same land can carry more households
        /// that sell what they grow rather than eating it.
        /// </summary>
        public const float RoadsCapacityPerLevel = 0.05f;

        /// <summary>
        /// The Irrigation daily project of the bound town or castle: capacity added while it is the fief's
        /// running daily project, +15%. Water on dry fields is more land in all but name -- but only while
        /// the work goes on; once the governor turns to something else, villages that grew into the extra
        /// room drift back down to what the land carries without it.
        /// </summary>
        public const float IrrigationCapacityBonus = 0.15f;

        /// <summary>
        /// Share of the room left below capacity a village grows into per day. New households settle over
        /// years, not seasons: at half a percent a day a village closes half the gap in about 140 days (under
        /// two years of 84 days) and nine tenths of it in about five and a half years. A raided village, its
        /// hearths halved, recovers on the same curve.
        /// </summary>
        public const float GrowthRate = 0.005f;

        /// <summary>
        /// Share of the gap above capacity a village sheds per day -- the same pace as <see cref="GrowthRate"/>.
        /// Households move away over years rather than vanishing: half of the excess in about 140 days.
        /// </summary>
        public const float OverCapacityDeclineRate = 0.005f;

        /// <summary>
        /// The smallest daily decline a village over capacity takes (never more than the gap itself), so a
        /// village just over its ceiling actually reaches it instead of approaching it forever.
        /// </summary>
        public const float OverCapacityDeclineMin = 0.05f;

        /// <summary>
        /// The median starting hearth of the base game's villages (SandBox settlements.xml, 100 to 825), used
        /// for the fallback capacity of a village the XML does not describe when the XML table itself could not
        /// be read. See <see cref="Capacity"/>.
        /// </summary>
        public const float FallbackMedianStartingHearth = 305f;

        // Starting hearth per village, keyed by the village component's StringId ("village_comp_..."), as the
        // settlements XML gave it. Filled by the Village.Deserialize postfix every time the game loads the
        // Settlements XML -- for a new campaign AND for a loaded save (SandBoxManager.InitializeSandboxXMLs
        // loads it either way; the village only skips assigning the value to Hearth on a save). String-keyed,
        // so it holds no game object from a departed session.
        private static readonly Dictionary<string, float> _startingHearths = new Dictionary<string, float>();

        // Median of _startingHearths, computed on first use after each XML load; negative = not yet computed.
        private static float _medianStartingHearth = -1f;

        /// <summary>
        /// Drops the previous load's table. Deliberately NOT part of the OnSessionLaunched resets in
        /// <see cref="RBMCampaignSessionBehavior"/>: the table is filled while the Settlements XML loads, which
        /// happens BEFORE the session launches, so a reset there would wipe the very data this session needs.
        /// It is called instead from the prefix on <c>SandBoxManager.InitializeSandboxXMLs</c>, just before
        /// the XML that refills it is read.
        /// </summary>
        internal static void ResetXmlTable()
        {
            _startingHearths.Clear();
            _medianStartingHearth = -1f;
        }

        /// <summary>
        /// The hearth this village was given in the settlements XML -- the merged result of every module's
        /// file and XSLT, so another mod's override is respected -- or a negative value when the XML did not
        /// describe it (a village created at run time, or the table unreadable).
        /// </summary>
        public static float StartingHearth(Village village)
        {
            if (village == null || village.StringId == null)
            {
                return -1f;
            }
            return _startingHearths.TryGetValue(village.StringId, out float hearth) ? hearth : -1f;
        }

        /// <summary>The median starting hearth across the XML table, or <see cref="FallbackMedianStartingHearth"/> if it is empty.</summary>
        private static float MedianStartingHearth()
        {
            if (_medianStartingHearth > 0f)
            {
                return _medianStartingHearth;
            }
            if (_startingHearths.Count == 0)
            {
                return FallbackMedianStartingHearth;
            }
            List<float> values = new List<float>(_startingHearths.Values);
            values.Sort();
            int mid = values.Count / 2;
            float median = (values.Count % 2 == 1) ? values[mid] : 0.5f * (values[mid - 1] + values[mid]);
            _medianStartingHearth = (median > 0f) ? median : FallbackMedianStartingHearth;
            return _medianStartingHearth;
        }

        /// <summary>
        /// The share the bound fief's buildings add to a village's capacity: Roads and Paths
        /// (<see cref="RoadsCapacityPerLevel"/> per level) plus Irrigation while it is the running daily project
        /// (<see cref="IrrigationCapacityBonus"/>; the town and the castle project count alike). Zero for a
        /// village with no fortified bound settlement.
        /// </summary>
        public static float BuildingBonus(Village village)
        {
            Town town = (village != null && village.Bound != null) ? village.Bound.Town : null;
            if (town == null)
            {
                return 0f;
            }
            float bonus = RoadsCapacityPerLevel * BuildingEffects.Roads(town);
            if (BuildingEffects.IsDailyProjectActive(town, DefaultBuildingTypes.SettlementDailyIrrigation)
                || BuildingEffects.IsDailyProjectActive(town, DefaultBuildingTypes.CastleDailyIrrigation))
            {
                bonus += IrrigationCapacityBonus;
            }
            return bonus;
        }

        /// <summary>
        /// The village's land capacity: its starting hearth times <see cref="CapacityPerStartingHearth"/>,
        /// times (1 + <see cref="BuildingBonus"/>).
        ///
        /// FALLBACK for a village the settlements XML does not describe: capacity = the larger of its current
        /// hearth and <see cref="CapacityPerStartingHearth"/> x the median XML starting hearth (with the same
        /// building bonus). A village smaller than a doubled typical village grows toward one; a larger one is
        /// simply held where it stands -- no growth, no decline, since the gap is zero. Chosen over remembering
        /// the first hearth seen, which would have to be saved to survive a reload (re-reading it on each load
        /// would let the ceiling ratchet up with every save), and over a flat figure, which could shrink a
        /// modded village that was legitimately large. The building bonus is applied INSIDE the max, never on
        /// the current hearth, for the same reason: on top of it the ceiling would climb 15% every session.
        /// </summary>
        public static float Capacity(Village village)
        {
            if (village == null)
            {
                return 0f;
            }
            float factor = 1f + BuildingBonus(village);
            float start = StartingHearth(village);
            if (start > 0f)
            {
                return start * CapacityPerStartingHearth * factor;
            }
            return MathF.Max(village.Hearth, MedianStartingHearth() * CapacityPerStartingHearth * factor);
        }

        /// <summary>
        /// A village's day of hearth change, RBM's replacement for vanilla's
        /// <c>DefaultSettlementProsperityModel.CalculateHearthChangeInternal</c>:
        ///
        ///   * BELOW capacity, in a normal (unraided) village -- growth of <see cref="GrowthRate"/> x the room
        ///     left, on one "Land capacity (N)" line. A looted or burning village grows nothing, as in vanilla.
        ///   * AT OR ABOVE capacity, in any state -- a decline of <see cref="OverCapacityDeclineRate"/> x the
        ///     excess (at least <see cref="OverCapacityDeclineMin"/>, never more than the excess itself), on an
        ///     "Over land capacity" line.
        ///
        /// Then vanilla's modifiers in vanilla's order and on vanilla's terms: Raided (-1 while looted),
        /// Grazing Rights (-0.25), the bound fief governor's Bush Doctor / Energetic / Aid Corps (only while
        /// the village is growing, so a percentage perk never deepens a decline), the Empire feat and the
        /// Merciful governor (both only on a non-negative day, vanilla's own guard), and issue effects. Left
        /// out on purpose: the base by size and the bound fief's VillageHeartsPerDay buildings (Roads and
        /// Paths, Irrigation), which now raise the capacity instead (<see cref="BuildingBonus"/>).
        /// </summary>
        internal static ExplainedNumber BuildHearthChange(Village village, float capacity, bool includeDescriptions)
        {
            ExplainedNumber result = new ExplainedNumber(0f, includeDescriptions);
            float hearth = village.Hearth;
            bool normal = village.VillageState == Village.VillageStates.Normal;
            bool growing = normal && hearth < capacity;

            if (growing)
            {
                TextObject capacityLine = null;
                if (includeDescriptions)
                {
                    capacityLine = new TextObject("{=RBM_hearth_capacity}Land capacity ({CAPACITY})");
                    capacityLine.SetTextVariable("CAPACITY", (int)capacity);
                }
                result.Add(GrowthRate * (capacity - hearth), capacityLine);
            }
            else if (hearth > capacity)
            {
                float gap = hearth - capacity;
                result.Add(-MathF.Min(gap, MathF.Max(OverCapacityDeclineMin, gap * OverCapacityDeclineRate)),
                    includeDescriptions ? OverCapacityText : null);
            }

            if (village.VillageState == Village.VillageStates.Looted)
            {
                result.Add(-1f, RaidedText);
            }
            Clan ownerClan = village.Settlement.OwnerClan;
            if (ownerClan != null && ownerClan.Kingdom != null && ownerClan.Kingdom.ActivePolicies.Contains(DefaultPolicies.GrazingRights))
            {
                result.Add(-0.25f, DefaultPolicies.GrazingRights.Name);
            }
            if (growing && village.Bound != null)
            {
                PerkHelper.AddPerkBonusForTown(DefaultPerks.Medicine.BushDoctor, village.Bound.Town, isPrimaryBonus: true, ref result);
                PerkHelper.AddPerkBonusForTown(DefaultPerks.Athletics.Energetic, village.Bound.Town, isPrimaryBonus: false, ref result);
                PerkHelper.AddPerkBonusForTown(DefaultPerks.Steward.AidCorps, village.Bound.Town, isPrimaryBonus: false, ref result);
            }
            if (ownerClan != null && ownerClan.Culture != null
                && ownerClan.Culture.HasFeat(DefaultCulturalFeats.EmpireVillageHearthFeat) && result.ResultNumber >= 0f)
            {
                result.AddFactor(DefaultCulturalFeats.EmpireVillageHearthFeat.EffectBonus, GameTexts.FindText("str_culture"));
            }
            Hero governor = village.Bound?.Town?.Governor;
            if (governor != null && governor.CurrentSettlement?.Town == village.Bound?.Town && result.ResultNumber >= 0f)
            {
                TraitEffectHelper.ApplyTraitEffect(governor, DefaultPersonalityTraitEffects.MercyHearthGrowthEffect, ref result);
            }
            Campaign.Current.Models.IssueModel.GetIssueEffectsOfSettlement(DefaultIssueEffects.VillageHearth, village.Settlement, ref result);
            return result;
        }

        private static readonly TextObject OverCapacityText = new TextObject("{=RBM_hearth_overcapacity}Over land capacity");

        // Vanilla's own label, reused verbatim (a TaleWorlds localization key, not an RBM string).
        private static readonly TextObject RaidedText = new TextObject("{=RVas572P}Raided");

        // ------------------------------------------------------------------ starting hearth table

        /// <summary>
        /// Records each village's XML hearth as the game deserializes it. <c>Village.Deserialize</c> runs for
        /// a new campaign AND a loaded save (it only skips assigning the value on a save), on the node the game
        /// itself reads -- so whatever another mod's XML or XSLT made of a village, this sees the same figure
        /// the game would have started it at. A village that appears in more than one file is deserialized
        /// again, and the last pass wins here as it does for the game.
        /// </summary>
        [HarmonyPatch(typeof(Village), nameof(Village.Deserialize))]
        private static class VillageDeserializePatch
        {
            private static void Postfix(Village __instance, XmlNode node)
            {
                if (__instance == null || __instance.StringId == null || node == null || node.Attributes == null)
                {
                    return;
                }
                XmlAttribute attribute = node.Attributes["hearth"];
                if (attribute == null
                    || !float.TryParse(attribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float hearth)
                    || !(hearth > 0f))
                {
                    return;
                }
                _startingHearths[__instance.StringId] = hearth;
                _medianStartingHearth = -1f;
            }
        }

        /// <summary>
        /// Clears the table just before the Settlements XML that refills it is loaded, so a session never reads
        /// the figures of the one before it. See <see cref="ResetXmlTable"/>.
        /// </summary>
        [HarmonyPatch(typeof(SandBoxManager), nameof(SandBoxManager.InitializeSandboxXMLs))]
        private static class ResetOnXmlLoadPatch
        {
            private static void Prefix()
            {
                ResetXmlTable();
            }
        }

        // ------------------------------------------------------------------ hearth change

        /// <summary>
        /// Replaces the base game's hearth change with <see cref="BuildHearthChange"/>. A skipping prefix, as
        /// RBM authors the whole number; other mods' postfixes on the method still run on RBM's result. Falls
        /// through to vanilla with RBM Campaign off or for a village with no capacity to close on.
        /// </summary>
        [HarmonyPatch(typeof(DefaultSettlementProsperityModel), nameof(DefaultSettlementProsperityModel.CalculateHearthChange))]
        private static class HearthChangePatch
        {
            private static bool Prefix(Village village, bool includeDescriptions, ref ExplainedNumber __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || village == null || village.Settlement == null)
                {
                    return true;
                }
                float capacity = Capacity(village);
                if (capacity <= 0f)
                {
                    return true;
                }
                __result = BuildHearthChange(village, capacity, includeDescriptions);
                return false;
            }
        }

        /// <summary>
        /// War Sails replaces the prosperity model with a decorator (<c>NavalDLCSettlementProsperityModel</c>)
        /// that calls the base model -- RBM's number, through <see cref="HearthChangePatch"/> -- and then adds
        /// the Shipmaster's Fair Winds governor perk for villages bound to a port. That is kept while the
        /// village grows, like the other governor perks; but on a village at or over its capacity the perk would
        /// cancel or deepen the decline, so there the decorator's number is rebuilt without it.
        ///
        /// Reflected onto the DLC type by name, so RBM keeps no dependency on the optional module: without War
        /// Sails the type does not resolve, <see cref="Prepare"/> returns false and nothing is patched. The type
        /// has no static initialiser, so it is safe to patch at module load.
        /// </summary>
        [HarmonyPatch]
        private static class NavalHearthChangePatch
        {
            private const string NavalModelTypeName = "NavalDLC.GameComponents.NavalDLCSettlementProsperityModel";

            private static bool Prepare()
            {
                return TargetMethod() != null;
            }

            private static System.Reflection.MethodBase TargetMethod()
            {
                return AccessTools.Method(NavalModelTypeName + ":CalculateHearthChange");
            }

            private static void Postfix(Village village, bool includeDescriptions, ref ExplainedNumber __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || village == null || village.Settlement == null)
                {
                    return;
                }
                float capacity = Capacity(village);
                if (capacity <= 0f || village.Hearth < capacity)
                {
                    return;
                }
                __result = BuildHearthChange(village, capacity, includeDescriptions);
            }
        }
    }
}
