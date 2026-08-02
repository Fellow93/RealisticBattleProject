using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Hangs a breakdown on the tavern's "Ransom your prisoners" option so the gold figure in its label is
    /// not the whole story: the men also keep the captives' stripped kit as spoils, and the leader skims
    /// his cut off that. The label already shows the man-ransom gold; this tooltip shows the spoils half.
    /// </summary>
    /// <remarks>
    /// <c>SellPrisonersCondition</c> is the option's condition delegate. A menu option's
    /// <c>MenuCallbackArgs.Tooltip</c>, set inside that delegate, is copied onto the option by
    /// <c>GameMenuOption.GetConditionsHold</c> and shown as the option's hover hint -- so a postfix that
    /// fills <c>args.Tooltip</c> is all it takes.
    ///
    /// The pot is priced over <see cref="MobilePartyHelper.GetPlayerPrisonersPlayerCanSell"/> -- the exact
    /// roster <c>SellAllTransferablePrisoners</c> hands to the sale -- so the number quoted here is the
    /// number the gather (<see cref="SpoilsPool.RansomPrisonersForSpoils"/>) will actually grant, and the
    /// cut is previewed off the very fraction that gather applies.
    /// </remarks>
    [HarmonyPatch(typeof(PlayerTownVisitCampaignBehavior), "SellPrisonersCondition")]
    public static class RansomMenuTooltip
    {
        private static void Postfix(MenuCallbackArgs args, bool __result)
        {
            if (!__result || !RBMConfig.RBMConfig.rbmCampaignEnabled || !SpoilsPool.IsEnabled)
            {
                return;
            }

            TroopRoster sellable = MobilePartyHelper.GetPlayerPrisonersPlayerCanSell();
            int pot = SpoilsPool.SumRansomGearValue(sellable);
            if (pot <= 0)
            {
                return;
            }

            int leaderCut = SpoilsPool.PreviewLeaderCut(PartyBase.MainParty, pot);
            int menShare = pot - leaderCut;

            TextObject tooltip = new TextObject("{=RBM_SPOILS_026}Their kit is stripped for spoils:{newline}Spoils to your men: {SPOILS}{newline}Your leader's cut: {CUT} gold");
            tooltip.SetTextVariable("SPOILS", menShare);
            tooltip.SetTextVariable("CUT", leaderCut);
            args.Tooltip = tooltip;
        }
    }
}
