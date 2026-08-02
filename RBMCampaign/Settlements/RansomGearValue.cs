using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;

namespace RBMCampaign
{
    /// <summary>
    /// Adds a captive's full kit -- armour, weapons, mount and harness -- to the price he ransoms for,
    /// on top of vanilla's recruitment-cost-plus-noble-bonus figure.
    ///
    /// A ransomed man is stripped before he is handed back: his gear stays with the captor and the buyer
    /// pays for it. Vanilla's <see cref="DefaultRansomValueCalculationModel"/> priced only the man, never
    /// the war-worth of what he was wearing, so a fully armoured knight fetched the same as a peasant of
    /// the same recruitment cost. Here every prisoner is worth his kit as well.
    /// </summary>
    /// <remarks>
    /// The value flows through the one model the whole game reads, so the sale payout
    /// (<c>SellPrisonersAction</c>), the party-screen ransom column, and the prisoner barterables all
    /// quote the same enriched figure. How the two halves are FUNDED differs, and
    /// <see cref="RansomFunding"/> is where that is enforced: the man himself is bought out of the town's
    /// citizen purse as vanilla's ransom always was, but the gear -- a real good the town keeps when it
    /// takes the prisoner -- is minted to the seller rather than drawn from citizens. The town ends up
    /// with the prisoner and his kit; the party ends up with the kit's worth; no citizen's purse is
    /// emptied to pay for goods that walked in on the prisoner's back.
    ///
    /// Kit value comes from <see cref="SpoilsPool.GetEquipmentValueWithMount"/>, the same averaged
    /// battle-set price the spoils economy meters wages and upgrades against, so a prisoner's ransom and
    /// his upkeep agree on what his gear is worth.
    /// </remarks>
    [HarmonyPatch(typeof(DefaultRansomValueCalculationModel), "PrisonerRansomValue")]
    public static class RansomGearValue
    {
        private static void Postfix(CharacterObject prisoner, ref int __result)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || prisoner == null)
            {
                return;
            }

            int gearValue = SpoilsPool.GetEquipmentValueWithMount(prisoner);
            if (gearValue > 0)
            {
                __result += gearValue;
            }
        }
    }
}
