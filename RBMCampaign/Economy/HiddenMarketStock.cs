using System.Collections.Generic;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Hides a share of a town's or village's market stock from the player trade screen, so a fief
    /// never shows everything it is holding for sale at once.
    ///
    /// The goods are not conjured or destroyed: the screen is handed a SHADOW roster carrying only the
    /// visible share, the player trades against that, and on a confirmed trade the net change is folded
    /// back onto the real market roster. Nothing on the campaign side -- prices, the town's own eating,
    /// caravans, AI parties -- ever sees the shadow; they keep reading the full <c>ItemRoster</c>. The
    /// only thing that changes is how much of it a shopper is shown.
    ///
    /// Mechanically the shadow is the merchant side (<c>_rosters[0]</c>) of the <see cref="InventoryLogic"/>.
    /// A drag applies to it live, and the logic's own backup (taken from the shadow) is what its Reset
    /// button reverts to -- so the reset button reverts to the halved view and cannot reveal the hidden
    /// stock. On a successful <see cref="InventoryLogic.DoneLogic"/> the delta between the shadow's
    /// opening and closing counts -- i.e. exactly what the player bought and sold -- is applied to the
    /// real roster. On cancel the real roster is simply never touched.
    /// </summary>
    public static class HiddenMarketStock
    {
        /// <summary>
        /// Share of every market stack withheld from the player's view. At 0.5 a stack of ten shows as
        /// five; a stack of one shows whole (a floor, so single items are never hidden to zero).
        /// </summary>
        private const float HiddenFraction = 0.5f;

        // Shadow merchant roster -> the real market roster it stands in for, plus the shadow's opening
        // counts. Keyed by the shadow instance, which is unique per screen open. Ephemeral: an entry is
        // consumed on a confirmed trade and dropped on cancel, and the whole map is cleared per session.
        private static readonly Dictionary<ItemRoster, Pending> _pending = new Dictionary<ItemRoster, Pending>();

        private static readonly AccessTools.FieldRef<InventoryLogic, ItemRoster[]> RostersRef =
            AccessTools.FieldRefAccess<InventoryLogic, ItemRoster[]>("_rosters");

        private struct Pending
        {
            public ItemRoster RealRoster;
            public ItemRoster OpeningShadow;
        }

        internal static void ResetForNewSession()
        {
            _pending.Clear();
        }

        /// <summary>
        /// Intercepts the settlement trade screen and swaps the real market roster for a visible-share
        /// shadow. Only the genuine town/village market is touched -- the guard on the roster identity
        /// leaves the tutorial's substitute shopping roster (and any other caller passing a roster that
        /// is not the settlement's own) alone.
        /// </summary>
        [HarmonyPatch(typeof(InventoryScreenHelper), "OpenScreenAsTrade")]
        private static class OpenScreenAsTradePatch
        {
            private static void Prefix(ref ItemRoster leftRoster, SettlementComponent settlementComponent)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || HiddenFraction <= 0f
                    || leftRoster == null || settlementComponent == null)
                {
                    return;
                }

                // Only a settlement's own market is a strategic stock worth hiding. The tutorial hands
                // OpenScreenAsTrade a hand-built roster that is not the settlement's; leave it be.
                if (settlementComponent.Owner == null || !ReferenceEquals(leftRoster, settlementComponent.Owner.ItemRoster))
                {
                    return;
                }

                ItemRoster shadow = new ItemRoster();
                for (int i = 0; i < leftRoster.Count; i++)
                {
                    ItemRosterElement element = leftRoster.GetElementCopyAtIndex(i);
                    int hidden = (int)(element.Amount * HiddenFraction);
                    int shown = element.Amount - hidden;
                    if (shown > 0)
                    {
                        shadow.AddToCounts(element.EquipmentElement, shown);
                    }
                }

                _pending[shadow] = new Pending
                {
                    RealRoster = leftRoster,
                    OpeningShadow = new ItemRoster(shadow)
                };
                leftRoster = shadow;
            }
        }

        /// <summary>
        /// On a committed trade, folds the shadow's net change back onto the real market roster. Runs
        /// only when <c>DoneLogic</c> actually succeeded (it returns false and stays open when the
        /// player cannot pay), so a rejected confirmation never writes a partial result.
        /// </summary>
        [HarmonyPatch(typeof(InventoryLogic), "DoneLogic")]
        private static class DoneLogicPatch
        {
            private static void Postfix(InventoryLogic __instance, bool __result)
            {
                if (!__result || !RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    return;
                }

                ItemRoster shadow = RostersRef(__instance)[0];
                if (shadow == null || !_pending.TryGetValue(shadow, out Pending pending))
                {
                    return;
                }
                _pending.Remove(shadow);

                ItemRoster real = pending.RealRoster;
                ItemRoster opening = pending.OpeningShadow;

                // Stacks that changed or were newly sold in: closing minus opening. A buy is negative,
                // a sell positive, a sold-in good has an opening of zero and comes through whole.
                for (int i = 0; i < shadow.Count; i++)
                {
                    ItemRosterElement element = shadow.GetElementCopyAtIndex(i);
                    int closing = shadow.GetElementNumber(i);
                    int openIndex = opening.FindIndexOfElement(element.EquipmentElement);
                    int opened = (openIndex >= 0) ? opening.GetElementNumber(openIndex) : 0;
                    int delta = closing - opened;
                    if (delta != 0)
                    {
                        real.AddToCounts(element.EquipmentElement, delta);
                    }
                }

                // Stacks bought out entirely: present at open, gone at close.
                for (int i = 0; i < opening.Count; i++)
                {
                    ItemRosterElement element = opening.GetElementCopyAtIndex(i);
                    if (shadow.FindIndexOfElement(element.EquipmentElement) < 0)
                    {
                        real.AddToCounts(element.EquipmentElement, -opening.GetElementNumber(i));
                    }
                }
            }
        }

        /// <summary>
        /// Drops the pending entry when the screen is cancelled, so a shadow that never committed does
        /// not linger. The mid-trade Reset button (<c>fromCancel == false</c>) is left untouched: it
        /// only reverts the shadow to its halved backup and must keep its entry for the eventual close.
        /// </summary>
        [HarmonyPatch(typeof(InventoryLogic), "Reset")]
        private static class ResetPatch
        {
            private static void Postfix(InventoryLogic __instance, bool fromCancel)
            {
                if (!fromCancel)
                {
                    return;
                }

                ItemRoster shadow = RostersRef(__instance)[0];
                if (shadow != null)
                {
                    _pending.Remove(shadow);
                }
            }
        }
    }
}
