using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;

namespace RBMCampaign
{
    /// <summary>
    /// Pins every garrison's and militia's effective morale to a fixed 50.
    ///
    /// Vanilla garrison/militia morale swings with settlement starvation and unpaid wages
    /// (<c>DefaultPartyMoraleModel.GetEffectivePartyMorale</c>): a starving or unpaid fief drops the
    /// garrison below the desertion threshold of 10 (<c>DefaultPartyDesertionModel</c>,
    /// <c>GetMoraleThresholdForTroopDesertion</c>), so it bleeds men on top of whatever the wage-cap
    /// already sheds. Under the RBM ledger a fief that cannot afford its defenders should pressure its
    /// own treasury (see <see cref="GarrisonUpkeep"/> and <see cref="GarrisonWageLimit"/>), not lose
    /// troops to morale.
    ///
    /// Overwriting the model result (rather than the <c>MobileParty.Morale</c> getter) covers every
    /// consumer at once -- the getter reads <c>ResultNumber</c> off this same call, and the party-screen
    /// tooltip reads it with <c>includeDescription</c>. 50 sits above the desertion threshold and is the
    /// vanilla base value, so it reads as a neutral, steady defender.
    ///
    /// A prefix that skips vanilla for exactly those parties: the result is a bare 50 (no lines beyond
    /// the base), so running vanilla's full computation -- starvation, wages, perks, traits, recent
    /// events -- only to throw it away was pure cost, paid on every morale read of every garrison and
    /// militia (the desertion model, AI, nameplates). Every other party runs vanilla untouched.
    /// </summary>
    [HarmonyPatch(typeof(DefaultPartyMoraleModel), nameof(DefaultPartyMoraleModel.GetEffectivePartyMorale))]
    internal static class GarrisonMorale
    {
        private static bool Prefix(MobileParty mobileParty, bool includeDescription, ref ExplainedNumber __result)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || mobileParty == null
                || (!mobileParty.IsGarrison && !mobileParty.IsMilitia))
            {
                return true;
            }

            __result = new ExplainedNumber(50f, includeDescription);
            return false;
        }
    }
}
