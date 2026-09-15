using System.Collections.Generic;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Coin from investigating a battle site or wreckage (v1.5.0) goes through the spoils purse.
    /// </summary>
    /// <remarks>
    /// <c>BattleWreckageCampaignBehavior.ApplyWreckageInvestigationResults</c> hands out the search's
    /// rewards in stages -- troops first, then goods, then the explanations page -- and on the last stage
    /// pays <c>_lootedGoldAmount</c> with <c>GiveGoldAction.ApplyBetweenCharacters(null, MainHero, ...)</c>:
    /// a null giver, straight into player gold, bypassing every RBM hook. The coin genuinely was lost on
    /// the field, so it is still new money; what changes is who pockets it. The men who dug it up take it
    /// as spoils by tier weight and the leader's cut is what reaches the player's purse, exactly as a
    /// raid's plunder is split.
    ///
    /// Done by draining the field before vanilla's gold branch runs -- when the prefix sees the call that
    /// would pay, it grants the lump and zeroes <c>_lootedGoldAmount</c>, so vanilla neither mints nor
    /// prints its "you received N gold" line for a figure the player did not receive.
    /// </remarks>
    [HarmonyPatch(typeof(BattleWreckageCampaignBehavior), "ApplyWreckageInvestigationResults")]
    public static class BattleSiteSpoils
    {
        /// <summary>True while the results are being handed out, for <see cref="BattleSiteTroopUpkeep"/>.</summary>
        internal static bool InResults;

        private static void Finalizer()
        {
            InResults = false;
        }

        private static void Prefix(TroopRoster ____lootedTroops, ItemRoster ____lootedItems,
            List<TextObject> ____consequenceExplanations, ref int ____lootedGoldAmount)
        {
            InResults = RBMConfig.RBMConfig.rbmCampaignEnabled;
            if (!SpoilsPool.IsEnabled || ____lootedGoldAmount <= 0)
            {
                return;
            }
            // Mirror vanilla's branch order: the gold is only paid once troops and goods are cleared.
            if ((____lootedTroops != null && ____lootedTroops.Count > 0)
                || (____lootedItems != null && ____lootedItems.Count > 0)
                || ____consequenceExplanations == null || ____consequenceExplanations.Count == 0)
            {
                return;
            }
            int amount = ____lootedGoldAmount;
            ____lootedGoldAmount = 0;
            SpoilsPool.OnBattleSiteGold(PartyBase.MainParty, amount);
        }
    }

    /// <summary>
    /// Wounded men recovered from a battle site arrive with the same few days' maintenance in their
    /// purse a recruited stack gets. Vanilla hands them over through the receive-troops party screen,
    /// which raises neither recruit event RBM seeds from, so they would otherwise start with nothing.
    /// The screen's closing delegate is wrapped so only the men the player actually took are seeded:
    /// what he left on the left side is subtracted from the roster the screen opened with.
    /// </summary>
    [HarmonyPatch(typeof(PartyScreenHelper), "OpenScreenAsReceiveTroops")]
    public static class BattleSiteTroopUpkeep
    {
        private static void Prefix(TroopRoster leftMemberParty, ref PartyScreenClosedDelegate partyScreenClosedDelegate)
        {
            if (!BattleSiteSpoils.InResults || !SpoilsPool.IsEnabled || leftMemberParty == null)
            {
                return;
            }
            TroopRoster offered = leftMemberParty.CloneRosterData();
            PartyScreenClosedDelegate original = partyScreenClosedDelegate;
            partyScreenClosedDelegate = delegate (PartyBase leftOwnerParty, TroopRoster leftMemberRoster, TroopRoster leftPrisonRoster,
                PartyBase rightOwnerParty, TroopRoster rightMemberRoster, TroopRoster rightPrisonRoster, bool fromCancel)
            {
                if (!fromCancel)
                {
                    for (int i = 0; i < offered.Count; i++)
                    {
                        TroopRosterElement element = offered.GetElementCopyAtIndex(i);
                        if (element.Character == null || element.Character.IsHero)
                        {
                            continue;
                        }
                        int left = (leftMemberRoster != null) ? leftMemberRoster.GetTroopCount(element.Character) : 0;
                        int taken = element.Number - left;
                        if (taken > 0)
                        {
                            SpoilsPool.SeedRecruitMaintenance(PartyBase.MainParty, element.Character, taken);
                        }
                    }
                }
                original?.Invoke(leftOwnerParty, leftMemberRoster, leftPrisonRoster, rightOwnerParty, rightMemberRoster, rightPrisonRoster, fromCancel);
            };
        }
    }

    /// <summary>
    /// A battle site's goods are stripped off the same casualties RBM's loot pass already stripped for
    /// the victors (sites only form on battles the player was not in), so the field would yield twice.
    /// The site's goods target value is scaled by <see cref="RaidGoodsDestruction"/>'s taken fraction --
    /// the same knob that governs how much of a raid's haul survives -- so a green lord finds half of
    /// vanilla's valuables and a Nord reiver with a plunderer's eye finds nearly all of them. The target
    /// feeds both halves of the search (casualty kit and trade goods), so one scale covers the lot.
    /// </summary>
    [HarmonyPatch(typeof(BattleWreckageCampaignBehavior), "GetTradeGoodTargetValueAndRogueryXp")]
    public static class BattleSiteGoodsScale
    {
        private static void Postfix(ref int targetTradeGoodValue)
        {
            if (!RaidGoodsDestruction.IsEnabled || targetTradeGoodValue <= 0)
            {
                return;
            }
            float fraction = RaidGoodsDestruction.TakenFraction(PartyBase.MainParty);
            targetTradeGoodValue = (int)System.Math.Round(targetTradeGoodValue * fraction);
        }
    }
}
