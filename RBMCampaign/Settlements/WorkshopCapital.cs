using HarmonyLib;
using TaleWorlds.CampaignSystem.GameComponents;

namespace RBMCampaign
{
    /// <summary>
    /// Raises the working capital a workshop is founded with, from vanilla's 10,000 to 60,000.
    ///
    /// Capital is a shop's own purse: it buys inputs from it, and pays its wages and overhead out of it.
    /// Now that <see cref="WorkshopProductionMargin"/> lets a named shop run many more cycles a day, it
    /// draws inputs and pays payroll (150/cycle) far faster than before, so a larger float keeps a busy
    /// shop from draining into the <c>shop-broke</c> gate (Capital &lt; inputCost) that
    /// <see cref="WorkshopDiagnostics"/> counts.
    /// </summary>
    /// <remarks>
    /// Patches the model getter, which is where every consumer reads the figure:
    /// <list type="bullet">
    /// <item><c>Workshop.Initialize</c> seeds a new shop's <c>Capital</c> from it -- the intended effect.</item>
    /// <item><c>ChangeOwnerOfWorkshopAction</c> resets capital to it whenever a shop changes hands, so
    /// existing shops climb to the new float as they turn over rather than all at once. Shops already
    /// running on an old save keep their current Capital until then.</item>
    /// <item><c>GetCostForPlayer</c> adds <c>InitialCapital / 5</c> to the price of buying a workshop, so
    /// the player now pays ~10,000 more for one -- but receives a shop holding 60,000 rather than 10,000,
    /// which is the better side of that trade.</item>
    /// </list>
    /// <c>Workshop.ProfitMade</c> and the per-shop <c>InitialCapital</c> snapshot read the workshop's own
    /// stored field, set once at founding, so they stay self-consistent and are unaffected here.
    ///
    /// Only applied when <c>rbmCampaignEnabled</c>: RBMCampaign's <c>PatchAll</c> runs under that toggle.
    /// </remarks>
    [HarmonyPatch(typeof(DefaultWorkshopModel), "InitialCapital", MethodType.Getter)]
    public static class WorkshopCapital
    {
        // Was 10,000. The purse a workshop is founded with.
        private const int FoundingCapital = 60000;

        private static void Postfix(ref int __result)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return;
            }
            __result = FoundingCapital;
        }

        /// <summary>
        /// Keeps the low-capital line at half the founding float, as it is in vanilla (5,000 of 10,000).
        /// </summary>
        /// <remarks>
        /// Below this line vanilla stops charging the daily overhead to the shop and charges the owner's
        /// treasury instead, and the clan-screen card turns its capital row into a warning. Left at
        /// 5,000 against a 60,000 float the warning came at a twelfth of the purse rather than half of
        /// it, so a shop could bleed most of its capital before the player was told anything.
        ///
        /// Read by <c>HandlePlayerWorkshopExpense</c>, <c>DefaultClanFinanceModel.AddPlayerExpenseForWorkshops</c>
        /// and <c>ClanFinanceWorkshopItemVM.GetCurrentCapitalProperty</c>, all live, so the three agree.
        /// A shop from an old save that still holds vanilla's 10,000 sits under the new line and will
        /// have its 100/day overhead billed to its owner until production lifts it above 30,000.
        /// </remarks>
        [HarmonyPatch(typeof(DefaultWorkshopModel), "CapitalLowLimit", MethodType.Getter)]
        private static class LowLimit
        {
            private const int LowCapital = FoundingCapital / 2;

            private static void Postfix(ref int __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    return;
                }
                __result = LowCapital;
            }
        }
    }
}
