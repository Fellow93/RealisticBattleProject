using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// Moves the money and the goods for one workshop cycle: what the town pays for a finished item, and
    /// what the shop pays for its materials.
    ///
    /// Vanilla wrote these two legs from two different price sides. The gate judged a cycle on the SELL
    /// price of its outputs (WCB:826) while the payment used the BUY price (WCB:847), and the payment was
    /// then clamped to a flat 1,000 that the gate never saw. It also priced ONE unit of an input and
    /// removed N of them (WCB:866-870) -- harmless in vanilla, where every recipe takes one of everything,
    /// ruinous with RBM's twenty-ingot draws.
    ///
    /// Both legs are replaced here. One valuation function, <see cref="ValueOfOutputs"/>, serves the gate
    /// and the payment, so the number a cycle is judged on is the number the town hands over.
    /// </summary>
    /// <remarks>
    /// Money conservation: every workshop debit or credit pairs with the opposite move on the town, or
    /// -- for the artisans, who settle in kind -- with nothing at all beyond the market fee on what they
    /// drew off the shelf. Nothing is minted and nothing is destroyed.
    ///
    /// Both prefixes are skip-prefixes at <see cref="Priority.First"/>, inert when
    /// <c>rbmCampaignEnabled</c> is off. The item movement is reproduced verbatim from vanilla; only the
    /// gold is RBM's.
    /// </remarks>
    public static class RBMWorkshopSettlement
    {
        /// <summary>
        /// The share of an item's sell-side value the shop is paid for it.
        /// </summary>
        /// <remarks>
        /// The sell side (<c>isSelling: true</c>) is already the lower, town-buying-from-a-seller price,
        /// and it is what vanilla's own income estimate used. Paying on it rather than on the buy price
        /// IS the wholesale discount, worth the market spread. Kept as a named constant at 1.0 so a
        /// further haircut is a one-token change if playtesting shows shops printing money.
        /// </remarks>
        public const float WholesaleShare = 1.0f;

        /// <summary>The fraction of a town's cash the most valuable single item can pull out of it.</summary>
        /// <remarks>
        /// Vanilla's 1,000 and RBM's old 10,000 both existed to stop one cycle draining a town. Any
        /// absolute number is wrong at some price scale -- that is exactly how RBM got a velvet weavery
        /// paid 1,000 for a 26,500 bolt. A fraction of the town's own gold scales itself with RBM prices
        /// forever: a poor town simply pays less per item, a rich one pays full value.
        /// </remarks>
        public const float PayoutTownShare = 0.10f;

        /// <summary>The floor under the ceiling, so a broke town still pays something for real work.</summary>
        public const int MinPayout = 500;

        /// <summary>The most a town will pay for one finished item today.</summary>
        public static int PayoutCeiling(Town town)
        {
            if (town == null)
            {
                return MinPayout;
            }
            int share = (int)(PayoutTownShare * town.Gold);
            return (share > MinPayout) ? share : MinPayout;
        }

        /// <summary>What the town pays for one finished item.</summary>
        public static int ValueOfOutput(Town town, EquipmentElement outputItem)
        {
            if (town == null || outputItem.IsEmpty)
            {
                return 0;
            }
            int price = town.GetItemPrice(outputItem, null, isSelling: true);
            int value = (int)(price * WholesaleShare);
            int ceiling = PayoutCeiling(town);
            if (value > ceiling)
            {
                value = ceiling;
            }
            // Never more than the town holds. The gate normally guarantees this, but vanilla's warehouse
            // escape lets a player cycle through without the cash test, and a town purse write is
            // clamped at zero while a workshop's is not -- the difference would be minted. Bound here,
            // at the point of payment, so every path is conserved.
            int held = town.Gold;
            if (value > held)
            {
                value = (held > 0) ? held : 0;
            }
            return value;
        }

        /// <summary>
        /// What the town will pay for a whole cycle's outputs, for the gate to judge.
        /// </summary>
        /// <remarks>
        /// The gate cannot name the individual items: vanilla picks them at random per output category
        /// inside <c>GetItemsToProduce</c>, and only their SUMMED sell-side value
        /// (<paramref name="rawOutputIncome"/>) reaches the gate. So the sum is valued the same two ways
        /// the per-item payment is -- the same wholesale share, the same ceiling -- and the tighter of
        /// them wins: <c>min(raw x share, itemCount x ceiling)</c>.
        ///
        /// This is an UPPER BOUND on the sum of the per-item payments, and exactly equal to it whenever a
        /// cycle's outputs price alike and sit within one ceiling. Both sides use the same price side and
        /// the same ceiling, so the invariant that matters holds: the gate never passes a cycle whose
        /// payments the town could not make.
        /// </remarks>
        public static int ValueOfOutputs(Town town, WorkshopType.Production production, int rawOutputIncome)
        {
            if (town == null || production.Outputs == null)
            {
                return rawOutputIncome;
            }
            int itemCount = 0;
            for (int i = 0; i < production.Outputs.Count; i++)
            {
                itemCount += production.Outputs[i].Item2;
            }

            long wholesale = (long)(rawOutputIncome * WholesaleShare);
            long ceiling = (long)itemCount * PayoutCeiling(town);
            long value = (wholesale < ceiling) ? wholesale : ceiling;
            return (value > int.MaxValue) ? int.MaxValue : (int)value;
        }

        /// <summary>
        /// The finished good onto the town's shelf, and the town's money into the shop's purse.
        /// </summary>
        /// <remarks>
        /// No market fee on this leg. A man setting his own work on his own stall has not crossed a
        /// counter, and the fee already came off the materials that made it -- taking it twice would tax
        /// the same goods on the way in and on the way out.
        /// </remarks>
        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "ProduceAnOutputToTown")]
        private static class OutputPatch
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(EquipmentElement outputItem, Workshop workshop, bool effectCapital)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || workshop == null || workshop.Settlement == null)
                {
                    return true;
                }
                Town town = workshop.Settlement.Town;
                if (town == null || town.Owner == null)
                {
                    return true;
                }

                town.Owner.ItemRoster.AddToCounts(outputItem, 1);

                if (Campaign.Current.GameStarted && RBMWorkshopCycle.SettlesInGold(workshop, effectCapital))
                {
                    int payout = ValueOfOutput(town, outputItem);
                    if (payout > 0)
                    {
                        WorkshopPurse.SetContext(WorkshopPurse.Output);
                        workshop.ChangeGold(payout);
                        WorkshopPurse.ClearContext();
                        town.ChangeGold(-payout);
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// The materials off the town's shelf, and the shop's money onto the town's counter.
        /// </summary>
        /// <remarks>
        /// Priced on the WHOLE draw, not on one unit -- fixing vanilla's mispricing at WCB:866. The price
        /// is read before the roster is touched, because a recipe that clears the last of a good leaves
        /// nothing to price afterwards.
        ///
        /// The artisans move no gold (see <c>RBMWorkshopCycle.SettlesInGold</c>) but still pay the market
        /// fee: the goods are the townspeople's already, so no price changes hands, but the stall is the
        /// town's and it takes its penny for the counter whoever is standing at it. That is the one leg
        /// of their day where anything moves, and it moves the way every other trade in the ledger does
        /// -- out of citizen wealth and into the treasury, at <see cref="TradeTariff.TariffRate"/>.
        /// </remarks>
        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "ConsumeInputFromTownMarket")]
        private static class InputPatch
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(ItemCategory productionInput, int productionInputCount, Town town,
                Workshop workshop, bool effectCapital)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || town == null || town.Owner == null
                    || productionInput == null)
                {
                    return true;
                }

                // The draw is taken and priced across every roster entry of the category, in roster
                // order, exactly as DetermineItemRosterHasSufficientInputs priced it for the gate. Vanilla
                // priced one unit at the first entry and took the whole count from that entry alone, so a
                // recipe drawing ten from two stacks could be charged for ten at the dearer price and
                // leave one stack negative. Priced this way, what the shop pays is what the gate approved.
                ItemRoster itemRoster = town.Owner.ItemRoster;
                int remaining = productionInputCount;
                int cost = 0;
                List<KeyValuePair<ItemObject, int>> taken = new List<KeyValuePair<ItemObject, int>>();
                for (int i = 0; i < itemRoster.Count && remaining > 0; i++)
                {
                    ItemObject entry = itemRoster.GetItemAtIndex(i);
                    if (entry == null || entry.ItemCategory != productionInput)
                    {
                        continue;
                    }
                    int have = itemRoster.GetElementNumber(i);
                    int take = (have < remaining) ? have : remaining;
                    if (take <= 0)
                    {
                        continue;
                    }
                    cost += town.GetItemPrice(entry) * take;
                    taken.Add(new KeyValuePair<ItemObject, int>(entry, take));
                    remaining -= take;
                }
                if (taken.Count == 0)
                {
                    return false;
                }

                if (Campaign.Current.GameStarted && cost > 0)
                {
                    if (RBMWorkshopCycle.SettlesInGold(workshop, effectCapital))
                    {
                        WorkshopPurse.SetContext(WorkshopPurse.Inputs);
                        workshop.ChangeGold(-cost);
                        WorkshopPurse.ClearContext();
                        town.ChangeGold(cost);
                    }
                    else if (workshop != null && workshop.WorkshopType != null && workshop.WorkshopType.IsHidden)
                    {
                        TradeTariff.Levy(town.Settlement, cost);
                    }
                }

                // Removed after pricing: taking a stack to zero drops its roster entry, which would shift
                // the indices the price walk depends on.
                for (int i = 0; i < taken.Count; i++)
                {
                    itemRoster.AddToCounts(taken[i].Key, -taken[i].Value);
                    CampaignEventDispatcher.Instance.OnItemConsumed(taken[i].Key, town.Owner.Settlement, taken[i].Value);
                }
                return false;
            }
        }
    }
}
