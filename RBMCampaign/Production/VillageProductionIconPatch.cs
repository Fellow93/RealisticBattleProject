using System;
using HarmonyLib;
using SandBox.ViewModelCollection.Nameplate;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// Shared production-icon id resolution, so the map nameplate (<see cref="VillageProductionIconPatch"/>)
    /// and the RBM ledger pick the same sprite for a village.
    ///
    /// The nameplate brush <c>Settlement.Event.Type.Image</c> (SandBox\GUI\Brushes\Nameplates.xml) keys
    /// its style off the primary-production item's <c>StringId</c>, but ships no style for RBM's processed
    /// village goods -- <c>planks</c> (lumberjack) and <c>ironIngot1</c>/crude iron (iron mine) -- so those
    /// would render the blank Default style. Remap them onto the raw material that DOES have a style:
    /// planks -> hardwood, crude iron -> iron (iron ore).
    /// </summary>
    internal static class VillageProductionIcon
    {
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

        // The nameplate/ledger production-icon style id for a village's primary production: vanilla's
        // camel/horse/mule normalization (SettlementNameplateEventsVM.AddPrimaryProductionIcon) plus RBM's
        // remap. "" when there's no resolvable primary production. Reads PrimaryProduction, which RBM's own
        // getter patch already points at the reworked speciality good.
        public static string StyleId(Village village)
        {
            ItemObject primary = village != null && village.VillageType != null
                ? village.VillageType.PrimaryProduction
                : null;
            if (primary == null || string.IsNullOrEmpty(primary.StringId))
            {
                return string.Empty;
            }

            string id = RemapProcessed(primary.StringId);
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
    }

    /// <summary>
    /// Remaps the map-nameplate production icon for RBM's processed village goods onto the raw material
    /// the game actually ships a sprite for. The constructor overload taking a string is used solely for
    /// the production icon, and the underlying <c>VillageType.PrimaryProduction</c> (hover tooltip,
    /// town-management list, trade issues) still reports the real good.
    /// </summary>
    [HarmonyPatch(typeof(SettlementNameplateEventItemVM), MethodType.Constructor, new Type[] { typeof(string) })]
    internal static class VillageProductionIconPatch
    {
        private static void Prefix(ref string productionIconId)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || string.IsNullOrEmpty(productionIconId))
            {
                return;
            }

            productionIconId = VillageProductionIcon.RemapProcessed(productionIconId);
        }
    }
}
