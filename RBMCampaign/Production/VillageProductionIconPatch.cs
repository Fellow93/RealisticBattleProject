using System.Collections.Generic;
using HarmonyLib;
using SandBox.ViewModelCollection.Nameplate;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// Shared production-icon style resolution, so the map nameplate (<see cref="VillageProductionIconPatch"/>)
    /// and the RBM ledger pick the same sprite for a village.
    ///
    /// The nameplate brush <c>Settlement.Event.Type.Image</c> (SandBox\GUI\Brushes\Nameplates.xml) keys
    /// its style off the primary-production item's <c>StringId</c>, but ships a sprite for only a fixed
    /// set of raw goods (<see cref="StyledIds"/>). RBM's processed goods -- <c>planks</c> (lumberjack)
    /// and <c>ironIngot1</c>/crude iron (iron mine) -- are remapped onto the raw material that DOES have
    /// a style (planks -> hardwood, crude iron -> iron). <c>charcoal</c>, which the brush has no sprite
    /// for at all, would render the blank Default style whenever it wins a village's rate*Value pick
    /// (foresters, and any charcoal-heavy mine); those fall back to the best companion speciality good
    /// that the brush does ship -- so a lumberjack shows planks, a salt-and-charcoal village shows salt.
    /// </summary>
    internal static class VillageProductionIcon
    {
        // Style ids the vanilla nameplate brush ships a production sprite for. A primary good whose
        // final style id isn't here (charcoal, meat) renders the blank Default style, so the resolver
        // substitutes the best companion speciality good that IS here. Keep in sync with the
        // Settlement.Event.Type.Image styles in SandBox\GUI\Brushes\Nameplates.xml.
        private static readonly HashSet<string> StyledIds = new HashSet<string>
        {
            "cow", "camel", "butter", "cheese", "sheep", "wool", "hog", "wheat", "grain",
            "hardwood", "clay", "salt", "iron", "fish", "grape", "flax", "date_fruit",
            "olives", "cotton", "silver", "fur", "horse", "walrus_tusk", "whale_oil",
        };

        // RBM processed good -> raw-material icon style. Single source of the remap.
        public static string RemapProcessed(string productionStringId)
        {
            switch (productionStringId)
            {
                case "planks": return "hardwood";
                case "ironIngot1": return "iron";
                default: return productionStringId;
            }
        }

        // A good's final nameplate style id: RBM's processed-good remap plus vanilla's
        // camel/horse/mule normalization (SettlementNameplateEventsVM.AddPrimaryProductionIcon).
        private static string FinalStyle(string stringId)
        {
            if (string.IsNullOrEmpty(stringId))
            {
                return string.Empty;
            }
            string id = RemapProcessed(stringId);
            if (id.Contains("camel"))
            {
                return "camel";
            }
            if (id.Contains("horse") || id.Contains("mule"))
            {
                return "horse";
            }
            return id;
        }

        // The production-icon style id for a village's primary production. Reads PrimaryProduction,
        // which RBM's own getter patch already points at the reworked speciality good, then remaps and
        // normalizes it. When that resolves to a good the nameplate brush has no sprite for (charcoal),
        // falls back to the highest-weighted companion speciality good that it does. "" when there's no
        // resolvable primary production.
        public static string StyleId(Village village)
        {
            if (village == null || village.VillageType == null)
            {
                return string.Empty;
            }

            ItemObject primary = village.VillageType.PrimaryProduction;
            if (primary == null || string.IsNullOrEmpty(primary.StringId))
            {
                return string.Empty;
            }

            string style = FinalStyle(primary.StringId);
            if (StyledIds.Contains(style))
            {
                return style;
            }

            // Primary good has no sprite (charcoal): take the best companion good that does.
            foreach (ItemObject item in RBMVillageProduction.GetSpecialityItemsByWeight(village.VillageType))
            {
                string companion = FinalStyle(item.StringId);
                if (StyledIds.Contains(companion))
                {
                    return companion;
                }
            }

            // No styled speciality at all -- keep the primary's (blank) style, as vanilla would.
            return style;
        }
    }

    /// <summary>
    /// Overwrites the map-nameplate production icon with RBM's <see cref="VillageProductionIcon.StyleId"/>,
    /// which remaps processed goods and falls back from a no-sprite primary (charcoal) to a companion
    /// good the brush actually renders. Runs after vanilla's <c>AddPrimaryProductionIcon</c> so it only
    /// rewrites the icon; the underlying <c>VillageType.PrimaryProduction</c> (hover tooltip,
    /// town-management list, trade issues) still reports the real good.
    /// </summary>
    [HarmonyPatch(typeof(SettlementNameplateEventsVM), "AddPrimaryProductionIcon")]
    internal static class VillageProductionIconPatch
    {
        private static void Postfix(SettlementNameplateEventsVM __instance, Settlement ____settlement)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || __instance == null || ____settlement == null)
            {
                return;
            }

            Village village = ____settlement.Village;
            if (village == null)
            {
                return;
            }

            string style = VillageProductionIcon.StyleId(village);
            if (string.IsNullOrEmpty(style))
            {
                return;
            }

            for (int i = __instance.EventsList.Count - 1; i >= 0; i--)
            {
                SettlementNameplateEventItemVM item = __instance.EventsList[i];
                if (item != null && item.EventType == SettlementNameplateEventItemVM.SettlementEventType.Production)
                {
                    item.AdditionalParameters = style;
                    break;
                }
            }
        }
    }
}
