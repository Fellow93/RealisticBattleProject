using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.ViewModelCollection.Party;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// The "Choose the prisoners to be ransomed" flow opens the party screen in Ransom mode, whose bottom
    /// trade label quotes only the man-ransom gold. That is half the deal: the captives' kit is also
    /// stripped for spoils, and the leader skims his cut off it -- the same as the "Ransom your prisoners"
    /// menu option's tooltip already shows (<see cref="RansomMenuTooltip"/>). This appends those two lines
    /// to the label so the individual-selection screen tells the whole story too.
    /// </summary>
    /// <remarks>
    /// <c>PartyVM.OnPartyGoldChanged</c> rebuilds <c>GoldChangeText</c> from the running ransom total every
    /// time a prisoner is moved, so a postfix that re-derives the spoils off the current selection keeps the
    /// figure live as prisoners are picked. In Ransom mode the player's prisoners start on the RIGHT
    /// (<c>PrisonerRosters[1]</c>) and the ones being sold accumulate on the empty broker LEFT
    /// (<c>PrisonerRosters[0]</c>) -- see <see cref="PartyScreenHelper.OpenScreenAsRansom"/> -- so
    /// <c>PrisonerRosters[0]</c> is exactly the roster the sale will strip, priced by the same
    /// <see cref="SpoilsPool.SumRansomGearValue"/> / <see cref="SpoilsPool.PreviewLeaderCut"/> pair the
    /// gather and the menu tooltip use, so all three always agree.
    ///
    /// <c>____currentMode</c> injects <c>PartyVM._currentMode</c> (an underscore-prefixed field, hence four
    /// leading underscores) so the append fires only on the ransom screen and never on the normal, loot,
    /// donate or manage variants that share this method.
    /// </remarks>
    [HarmonyPatch(typeof(PartyVM), "OnPartyGoldChanged")]
    public static class RansomScreenSpoilsLabel
    {
        private static void Postfix(PartyVM __instance, PartyScreenHelper.PartyScreenMode ____currentMode)
        {
            if (____currentMode != PartyScreenHelper.PartyScreenMode.Ransom
                || !RBMConfig.RBMConfig.rbmCampaignEnabled || !SpoilsPool.IsEnabled)
            {
                return;
            }

            PartyScreenLogic logic = __instance.PartyScreenLogic;
            if (logic == null || logic.PrisonerRosters == null || logic.PrisonerRosters.Length < 1)
            {
                return;
            }

            TroopRoster sold = logic.PrisonerRosters[0];
            int pot = SpoilsPool.SumRansomGearValue(sold);
            if (pot <= 0)
            {
                return;
            }

            int leaderCut = SpoilsPool.PreviewLeaderCut(PartyBase.MainParty, pot);
            int menShare = pot - leaderCut;

            TextObject label = new TextObject("{=RBM_SPOILS_027}{BASE}{newline}Spoils to your men: {SPOILS}{newline}Your leader's cut: {CUT} gold");
            label.SetTextVariable("BASE", __instance.GoldChangeText);
            label.SetTextVariable("SPOILS", menShare);
            label.SetTextVariable("CUT", leaderCut);
            __instance.GoldChangeText = label.ToString();
        }
    }
}
