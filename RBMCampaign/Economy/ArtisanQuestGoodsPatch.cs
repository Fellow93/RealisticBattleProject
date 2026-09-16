using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace RBMCampaign
{
    /// <summary>
    /// The artisan "Overpriced Goods" issue picks its requested good from a fixed vanilla list
    /// (cow, sheep, wool, iron, leather, hardwood). Under RBM's village production rework, raw
    /// iron ore (<c>iron</c>) and <c>hardwood</c> are no longer produced anywhere, so a quest that
    /// asked for them could never be fulfilled from the market. Swap them for the goods RBM's
    /// villages actually turn out: crude iron (<c>ironIngot1</c>) and <c>planks</c>. The price-index
    /// spawn gate and the amount/reward formulas then run against those items instead.
    /// </summary>
    [HarmonyPatch(typeof(ArtisanOverpricedGoodsIssueBehavior), "PossibleRequestedItems", MethodType.Getter)]
    internal static class ArtisanQuestGoodsPatch
    {
        private static void Postfix(ref IEnumerable<ItemObject> __result)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || __result == null)
            {
                return;
            }
            __result = Remap(__result);
        }

        private static IEnumerable<ItemObject> Remap(IEnumerable<ItemObject> items)
        {
            foreach (ItemObject item in items)
            {
                if (item == null)
                {
                    continue;
                }
                ItemObject replacement = null;
                switch (item.StringId)
                {
                    case "iron": replacement = MBObjectManager.Instance.GetObject<ItemObject>("ironIngot1"); break;
                    case "hardwood": replacement = MBObjectManager.Instance.GetObject<ItemObject>("planks"); break;
                }
                yield return replacement ?? item;
            }
        }
    }
}
