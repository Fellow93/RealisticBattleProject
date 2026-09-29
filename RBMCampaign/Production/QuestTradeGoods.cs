using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace RBMCampaign
{
    /// <summary>
    /// Points vanilla's trade-good quests at goods RBM's markets actually carry.
    ///
    /// RBM's lumberjacks produce <c>planks</c> and its iron mines <c>ironIngot1</c> (crude iron);
    /// <c>hardwood</c> only comes out of the smithy refining recipe and <c>iron</c> (ore) is not made
    /// at all. Two vanilla notable quests still ask for the raw goods by id, so they would send the
    /// player after something no market stocks:
    /// <list type="bullet">
    /// <item><c>ArtisanCantSellProductsAtAFairPriceIssue</c> -- the delivery pool lists hardwood. The
    /// artisan hands over a fixed 6-60 lots, so planks drop straight in.</item>
    /// <item><c>ArtisanOverpricedGoodsIssue</c> -- the requested-item pool lists both. This one asks
    /// for <c>10000 / Value x difficulty</c> lots, so a like-for-like swap onto RBM's cheap, heavy
    /// goods would demand up to 1,000 planks (20 t) or 2,500 crude iron (5 t) -- more than any market
    /// holds or a party can carry. Iron ore becomes <c>tools</c> instead (48 denars, 1 kg: at most
    /// ~208 lots, in line with wool and leather), and hardwood is dropped, as no dense wood good
    /// exists to stand in for it.</item>
    /// </list>
    /// <c>EscortMerchantCaravanIssue</c> only checks that <c>hardwood</c> exists as an object, which
    /// it still does, so it needs nothing.
    /// </summary>
    /// <remarks>
    /// Issues already rolled in a save keep the good they were rolled with; only new ones change.
    /// </remarks>
    internal static class QuestTradeGoods
    {
        private static ItemObject Get(string stringId)
        {
            return MBObjectManager.Instance.GetObject<ItemObject>(stringId);
        }

        [HarmonyPatch(typeof(ArtisanOverpricedGoodsIssueBehavior), "PossibleRequestedItems", MethodType.Getter)]
        private static class OverpricedGoodsRequestedItemsPatch
        {
            private static void Postfix(ref IEnumerable<ItemObject> __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || __result == null)
                {
                    return;
                }

                List<ItemObject> swapped = new List<ItemObject>();
                foreach (ItemObject item in __result)
                {
                    switch (item?.StringId)
                    {
                        case "hardwood":
                            continue;
                        case "iron":
                            swapped.Add(Get("tools") ?? item);
                            break;
                        default:
                            swapped.Add(item);
                            break;
                    }
                }
                __result = swapped;
            }
        }

        [HarmonyPatch(typeof(ArtisanCantSellProductsAtAFairPriceIssueBehavior.ArtisanCantSellProductsAtAFairPriceIssue),
            MethodType.Constructor, new[] { typeof(Hero) })]
        private static class CantSellProductsDeliveryItemPatch
        {
            private static void Postfix(ref ItemObject ____rawMaterialsToBeDelivered)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || ____rawMaterialsToBeDelivered?.StringId != "hardwood")
                {
                    return;
                }

                ____rawMaterialsToBeDelivered = Get("planks") ?? ____rawMaterialsToBeDelivered;
            }
        }
    }
}
