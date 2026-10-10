using System;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// Reads and removes goods by one exact <see cref="EquipmentElement"/> slot, so what is counted and
    /// what comes off are always the same stack.
    /// </summary>
    /// <remarks>
    /// Vanilla's <c>GetItemNumber(ItemObject)</c> reads the FIRST slot holding the item under any
    /// modifier, while <c>AddToCounts(ItemObject, n)</c> removes from the UNMODIFIED slot only. Whenever
    /// a market holds the item in more than one stack the two disagree: the read can count a modified
    /// stack the remove never touches (a failed assert and a no-op, with the gold already moved), or
    /// the remove can run the plain stack past zero, where <c>ItemRosterElement.Amount</c> throws
    /// <c>MBUnderFlowException</c> mid-tick. Keying both sides by one element -- and paying on what
    /// <see cref="Take"/> reports -- closes that.
    /// </remarks>
    internal static class RosterStock
    {
        /// <summary>How many units the slot matching <paramref name="element"/> holds; 0 if none.</summary>
        internal static int Count(ItemRoster roster, EquipmentElement element)
        {
            int slot = roster.FindIndexOfElement(element);
            return slot >= 0 ? roster.GetElementNumber(slot) : 0;
        }

        /// <summary>
        /// Removes up to <paramref name="wanted"/> units from the slot matching <paramref name="element"/>
        /// and returns how many actually came off.
        /// </summary>
        internal static int Take(ItemRoster roster, EquipmentElement element, int wanted)
        {
            int slot = roster.FindIndexOfElement(element);
            if (slot < 0 || wanted <= 0)
            {
                return 0;
            }
            int take = Math.Min(wanted, roster.GetElementNumber(slot));
            if (take > 0)
            {
                roster.AddToCounts(element, -take);
            }
            return take;
        }
    }
}
