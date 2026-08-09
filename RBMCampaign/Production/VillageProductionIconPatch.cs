using System;
using HarmonyLib;
using SandBox.ViewModelCollection.Nameplate;

namespace RBMCampaign
{
    /// <summary>
    /// Remaps the map-nameplate production icon for RBM's processed village goods onto the raw material
    /// the game actually ships a sprite for. The nameplate brush (<c>Settlement.Event.Type.Image</c>,
    /// in SandBox\GUI\Brushes\Nameplates.xml) keys its style off the primary-production item's
    /// <c>StringId</c>, but has no style for <c>planks</c> or <c>ironIngot1</c> (crude iron) -- the goods
    /// RBM's reworked table makes primary for lumberjack and iron-mine villages -- so those nameplates
    /// render the blank Default style. Swap the icon id for the raw material that DOES have a style:
    /// planks -> hardwood, crude iron -> iron (iron ore).
    ///
    /// This is icon-only: the constructor overload taking a string is used solely for the production
    /// icon, and the underlying <c>VillageType.PrimaryProduction</c> (hover tooltip, town-management
    /// list, trade issues) still reports the real good.
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

            switch (productionIconId)
            {
                case "planks":
                    productionIconId = "hardwood";
                    break;
                case "ironIngot1":
                    productionIconId = "iron";
                    break;
            }
        }
    }
}
