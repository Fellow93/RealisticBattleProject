using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Surfaces the day's surplus spoils -- what your veterans hand up as gold once their own upgrades
    /// are provisioned -- as its own line in the clan finance breakdown, the tooltip over your denars
    /// and the clan finance screen alike, so the coin is accounted for beside wages and tariffs rather
    /// than appearing from nowhere. A projection of what the next daily tick will spill from the pools
    /// as they stand, the same way every other line projects the day to come.
    /// </summary>
    public static class SpoilsFinanceLine
    {
        private static readonly TextObject _spoilsIncomeText = new TextObject("{=RBM_CON_069}Troop Spoils");

        [HarmonyPatch(typeof(DefaultClanFinanceModel), "CalculateClanGoldChange")]
        private class ShowSpoilsSpillLine
        {
            /// <summary>
            /// Drawn only when the model is asked to explain the day, never when it is asked to apply it.
            /// The spill reaches the purse through GiveGoldAction on the daily tick, wholly outside this
            /// model, so adding it on the apply path too would pay it twice -- once minted, once through
            /// the finance net. On the display path it changes no gold, only what the breakdown reads.
            /// </summary>
            private static void Postfix(Clan clan, bool applyWithdrawals, ref ExplainedNumber __result)
            {
                if (applyWithdrawals || !SpoilsPool.IsEnabled)
                {
                    return;
                }
                if (RBMConfig.RBMConfig.troopSpoilsGoldSpillFraction <= 0f)
                {
                    return;
                }
                // Only the player reads a finance breakdown; an AI clan's spill is real gold all the same,
                // but it needs no tooltip line drawn for it.
                if (clan == null || clan != Clan.PlayerClan)
                {
                    return;
                }
                int projected = 0;
                foreach (WarPartyComponent warParty in clan.WarPartyComponents)
                {
                    MobileParty mobileParty = warParty.MobileParty;
                    if (mobileParty != null && mobileParty.IsActive)
                    {
                        projected += SpoilsPool.ProjectDailySpill(mobileParty.Party);
                    }
                }
                if (projected > 0)
                {
                    __result.Add(projected, _spoilsIncomeText);
                }
            }
        }
    }
}
