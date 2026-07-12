using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;

namespace RBMCampaign
{
    /// <summary>
    /// Surfaces the day's troop maintenance -- what the party leader is left to pay once his men have
    /// met what they can out of their own spoils -- as its own line in the clan finance breakdown, the
    /// tooltip over the denars and the clan finance screen alike, so the coin is accounted for beside
    /// wages rather than vanishing from the treasury unexplained. A projection of what the next daily
    /// tick will bill, the same way every other line projects the day to come.
    /// </summary>
    public static class MaintenanceFinanceLine
    {
        [HarmonyPatch(typeof(DefaultClanFinanceModel), "CalculateClanGoldChange")]
        private class ShowMaintenanceLine
        {
            /// <summary>
            /// Drawn only when the model is asked to explain the day, never when it is asked to apply it.
            /// The leader's share leaves his purse through GiveGoldAction on the daily tick, wholly outside
            /// this model, so adding it on the apply path too would pay it twice. On the display path it
            /// changes no gold, only what the breakdown reads.
            /// </summary>
            private static void Postfix(Clan clan, bool applyWithdrawals, ref ExplainedNumber __result)
            {
                if (applyWithdrawals || !SpoilsPool.IsEnabled)
                {
                    return;
                }
                if (RBMConfig.RBMConfig.troopMaintenanceFraction <= 0f)
                {
                    return;
                }
                // Only the player reads a finance breakdown; an AI clan's leaders pay their maintenance all
                // the same, but no tooltip line need be drawn for it.
                if (clan == null || clan != Clan.PlayerClan)
                {
                    return;
                }
                // Maintenance runs on every party on the daily tick and bills that party's own payee, so
                // walk every active party and gather the ones whose leader belongs to this clan, off the
                // same payee rule the tick uses, so the line matches the gold that actually leaves.
                int total = 0;
                int covered = 0;
                int paid = 0;
                foreach (MobileParty mobileParty in MobileParty.All)
                {
                    if (mobileParty == null || !mobileParty.IsActive)
                    {
                        continue;
                    }
                    Hero payer = SpoilsPool.GetPartyPayee(mobileParty.Party);
                    if (payer == null || payer.Clan != clan)
                    {
                        continue;
                    }
                    MaintenanceResult m = SpoilsPool.ProjectDailyMaintenance(mobileParty.Party);
                    total += m.Total;
                    covered += m.Covered;
                    paid += m.Shortfall;
                }
                // Only the leader-paid remainder is clan gold; what the spoils met never passed through the
                // treasury. Shown only when it costs the clan something -- the per-party wage tooltip carries
                // the fuller picture for a party whose men cover their own upkeep.
                if (paid > 0)
                {
                    __result.Add(-paid, SpoilsPool.BuildMaintenanceLineText(total, covered));
                }
            }
        }
    }
}
