using HarmonyLib;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement.Categories;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// Keeps RBM supply caravans off the clan screen's caravan list. They are owned by the source town's
    /// clan leader but pay their owner nothing by design, so a town-owning player saw a row of caravans
    /// earning 0 and read it as broken caravan income.
    /// </summary>
    [HarmonyPatch(typeof(ClanPartiesVM), nameof(ClanPartiesVM.RefreshPartiesList))]
    internal static class SupplyCaravanClanListPatch
    {
        private static void Postfix(ClanPartiesVM __instance)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return;
            }

            bool removed = false;
            for (int i = __instance.Caravans.Count - 1; i >= 0; i--)
            {
                ClanPartyItemVM item = __instance.Caravans[i];
                if (item.Party != null && RBMCaravanRegister.IsManaged(item.Party.MobileParty))
                {
                    __instance.Caravans.RemoveAt(i);
                    removed = true;
                }
            }

            if (!removed)
            {
                return;
            }

            GameTexts.SetVariable("CURRENT", __instance.Caravans.Count);
            __instance.CaravansText = GameTexts.FindText("str_clan_caravans").ToString();
            __instance.RefreshTotalExpense();
        }
    }
}
