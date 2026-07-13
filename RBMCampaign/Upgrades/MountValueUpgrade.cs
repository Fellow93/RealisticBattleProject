using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// "Charge the mount in gold instead of the baggage train": a troop that upgrades into a mounted
    /// tier no longer needs a horse item sitting in the party's inventory, and one is no longer consumed
    /// on the upgrade. Instead the horse and its harness are priced into the upgrade's gold/spoils cost
    /// (see <see cref="SpoilsPool.GetUpgradeEquipmentValue"/>, read by the cost and salvage math).
    ///
    /// The whole feature is this file plus that one helper switch and its two call sites. Switch it off
    /// at runtime with RBMConfig.troopUpgradeChargeMountValue = 0; remove it outright by deleting this
    /// file and its csproj entry, and reverting the helper to <see cref="SpoilsPool.GetEquipmentValue"/>.
    /// </summary>
    public static class MountValueUpgrade
    {
        /// <summary>On only when the spoils economy is on and the toggle is switched on in config.</summary>
        public static bool IsEnabled
        {
            get { return SpoilsPool.IsEnabled && RBMConfig.RBMConfig.troopUpgradeChargeMountValue; }
        }

        /// <summary>
        /// Nulls out an upgrade target's required-item category so the native upgrade flow stops treating
        /// a horse as a consumable. Every reader of this property is upgrade-side -- the party-screen
        /// validation, the item consumption, the arrow's availability cap, the model's item check and the
        /// requirement tooltips -- so one null makes them all behave as "no item required" at once.
        /// </summary>
        [HarmonyPatch(typeof(CharacterObject), "UpgradeRequiresItemFromCategory", MethodType.Getter)]
        private class DropUpgradeItemRequirement
        {
            private static void Postfix(ref ItemCategory __result)
            {
                if (__result != null && IsEnabled)
                {
                    __result = null;
                }
            }
        }
    }
}
