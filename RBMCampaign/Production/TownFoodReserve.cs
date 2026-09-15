using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Keeps a share of a town's market food as a strategic reserve that outside forces cannot buy.
    /// The townsfolk, garrison and militia still eat from the whole stock (that is not a market sale --
    /// see <see cref="RBMTownFoodSupply"/>), and the player is never restricted here; the reserve bites
    /// only on food LEAVING the town to a passing mobile party -- a marching army, an AI lord's party,
    /// or a caravan buying grain to resell elsewhere.
    ///
    /// Every one of those goes through <see cref="SellItemsAction"/> with the settlement as the seller,
    /// which is the single choke this gates. The player's own trade never uses that action (it runs
    /// through <c>InventoryLogic</c>), so the reserve and the player-facing hidden stock
    /// (<see cref="HiddenMarketStock"/>) stay cleanly separate.
    ///
    /// The reserve floor is half of the town's food, anchored once a day: the first time an outsider
    /// tries to buy food that day the floor is fixed at <see cref="ReserveFraction"/> of what the market
    /// then holds, and every sale that day is clamped to the surplus above it. A fixed floor is what
    /// makes it a real reserve rather than a moving target -- half of a shrinking number is never empty,
    /// so without the anchor a run of buyers would nibble the stock toward zero. Villager deliveries
    /// during the day are free to be bought; the next day the floor re-anchors to the new stock.
    /// </summary>
    public static class TownFoodReserve
    {
        /// <summary>Share of a town's food held back from outside buyers.</summary>
        private const float ReserveFraction = 0.5f;

        // Per-town reserve floor, anchored on the campaign day it was taken. Ephemeral -- re-derived
        // within a day of any gap -- but holds Town references, so cleared per session.
        private static readonly Dictionary<Town, DailyFloor> _floors = new Dictionary<Town, DailyFloor>();

        private struct DailyFloor
        {
            public int Day;
            public int Floor;
        }

        internal static void ResetForNewSession()
        {
            _floors.Clear();
        }

        private static int ReserveFloor(Town town, int currentFood)
        {
            int day = (int)CampaignTime.Now.ToDays;
            if (_floors.TryGetValue(town, out DailyFloor cached) && cached.Day == day)
            {
                return cached.Floor;
            }

            int floor = (int)MathF.Round(currentFood * ReserveFraction);
            _floors[town] = new DailyFloor { Day = day, Floor = floor };
            return floor;
        }

        /// <summary>
        /// Gates a settlement's food sale to an outside party against the town's reserve. Clamps the
        /// sale to whatever surplus sits above the floor, and blocks it outright once the floor is
        /// reached. Anything that is not a town selling food goods to a non-player mobile party falls
        /// straight through untouched.
        /// </summary>
        [HarmonyPatch(typeof(SellItemsAction), "Apply")]
        private static class ApplyPatch
        {
            // Apply's first parameter is the seller (it forwards to ApplyInternal's sellerParty), the
            // second the buyer -- the names on Apply itself read backwards.
            private static bool Prefix(PartyBase receiverParty, PartyBase payerParty, ItemRosterElement subject, ref int number)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || ReserveFraction <= 0f || number <= 0)
                {
                    return true;
                }

                PartyBase seller = receiverParty;
                if (seller == null || !seller.IsSettlement || seller.Settlement == null || !seller.Settlement.IsTown)
                {
                    return true;
                }

                MobileParty buyer = payerParty?.MobileParty;
                if (buyer == null || buyer.IsMainParty)
                {
                    return true;
                }

                ItemObject item = subject.EquipmentElement.Item;
                if (item == null || item.ItemCategory == null
                    || item.ItemCategory.Properties != ItemCategory.Property.BonusToFoodStores)
                {
                    return true;
                }

                Town town = seller.Settlement.Town;
                int current = RBMTownFoodSupply.FoodUnitsInMarket(town);
                int available = current - ReserveFloor(town, current);
                if (available <= 0)
                {
                    return false;
                }
                if (number > available)
                {
                    number = available;
                }
                return true;
            }
        }
    }
}
