using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.TownManagement;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// The seams that hand construction over to <see cref="Construction"/>: what a building costs, who
    /// ticks it, what the screen reports, and how much the owner may pledge to the reserve.
    /// </summary>
    public static class ConstructionPatches
    {
        private static bool Active
        {
            get { return RBMConfig.RBMConfig.rbmCampaignEnabled; }
        }

        /// <summary>
        /// Reprices every building project. Vanilla's costs are denominated in a currency of nothing --
        /// points that arrive free out of prosperity -- so with a point now worth a coin they are
        /// trivial: a town would raise its walls out of a week's tax. Multiplied up, a project is the
        /// years-long, treasury-draining undertaking it ought to be.
        /// </summary>
        /// <remarks>
        /// Patched at the ROOT figure rather than at any one reader, so the same number reaches
        /// <c>Building.GetConstructionCost</c>, <c>BuildingHelper.CheckIfBuildingIsComplete</c>, the
        /// days-to-complete estimate and every tooltip. Progress already banked is not rescaled -- an old
        /// save simply finds its half-built projects a smaller fraction of the way along.
        /// </remarks>
        [HarmonyPatch(typeof(BuildingType), "GetProductionCost")]
        private static class ProductionCostPatch
        {
            private static void Postfix(ref int __result)
            {
                if (!Active || __result <= 0)
                {
                    return;
                }
                int multiplier = RBMConfig.RBMConfig.buildingCostMultiplier;
                if (multiplier > 1)
                {
                    __result *= multiplier;
                }
            }
        }

        /// <summary>
        /// Takes the building tick off vanilla. Its version adds free points and burns a flat 500 of the
        /// reserve a day whether or not the work needed it; ours runs from the RBM settlement pass, after
        /// the day's other bills, and pays for what it builds. Everything else in vanilla's daily
        /// settlement pass -- wall repair, the AI's project picks -- is untouched.
        /// </summary>
        [HarmonyPatch(typeof(BuildingsCampaignBehavior), "TickCurrentBuildingForTown")]
        private static class TickPatch
        {
            private static bool Prefix()
            {
                return !Active;
            }
        }

        /// <summary>
        /// Reports the day's FUNDED work rather than what prosperity alone would have given away.
        /// </summary>
        /// <remarks>
        /// The result is replaced outright rather than adjusted: vanilla's lines describe a different
        /// quantity (free labour out of population) and leaving them in would double-count. The bonuses
        /// they carried -- governor perks, the Battanian feat, market production goods -- return in phase
        /// 2 as factors on funded work, never as free points.
        /// </remarks>
        [HarmonyPatch(typeof(DefaultBuildingConstructionModel), "CalculateDailyConstructionPower")]
        private static class DailyPowerPatch
        {
            private static void Postfix(Town town, bool includeDescriptions, ref ExplainedNumber __result)
            {
                if (!Active)
                {
                    return;
                }
                __result = Construction.Project(town, includeDescriptions);
            }
        }

        [HarmonyPatch(typeof(DefaultBuildingConstructionModel), "CalculateDailyConstructionPowerWithoutBoost")]
        private static class DailyPowerWithoutBoostPatch
        {
            private static void Postfix(Town town, ref int __result)
            {
                if (!Active)
                {
                    return;
                }
                // There is no such thing as construction without the reserve any more -- the reserve IS
                // the funding -- so this reports the same figure. It is only ever used as a divisor in
                // BuildingHelper.GetDaysToComplete, so it must never come back zero.
                int projected = (int)Construction.Project(town, false).ResultNumber;
                __result = (projected > 0) ? projected : 1;
            }
        }

        /// <summary>
        /// Stops vanilla treating the reserve as a per-day boost with a fixed price.
        /// </summary>
        /// <remarks>
        /// The bonus is zero because the reserve no longer adds a lump of work on top of a free baseline;
        /// it funds all the work there is.
        ///
        /// The COST is not zeroed, which the plan called for: <c>BuildingHelper.GetDaysToComplete</c>
        /// divides an int by it, so a zero there is a hard crash on the town management screen. Returning
        /// a figure no reserve can reach takes the same branch out of play with none of the risk, and the
        /// estimate falls back to dividing the remaining cost by the day's real funded output.
        /// </remarks>
        [HarmonyPatch(typeof(DefaultBuildingConstructionModel), "GetBoostAmount")]
        private static class BoostAmountPatch
        {
            private static void Postfix(ref int __result)
            {
                if (Active)
                {
                    __result = 0;
                }
            }
        }

        [HarmonyPatch(typeof(DefaultBuildingConstructionModel), "GetBoostCost")]
        private static class BoostCostPatch
        {
            private static void Postfix(ref int __result)
            {
                if (Active)
                {
                    __result = int.MaxValue;
                }
            }
        }

        /// <summary>
        /// Lets the owner pledge as much as the work can actually absorb.
        /// </summary>
        /// <remarks>
        /// Vanilla's flat 10,000 ceiling was sized against projects costing a few thousand points. With
        /// costs multiplied up and the reserve the only thing funding them, a lord willing to pay for a
        /// wall out of his own pocket has to be able to pay for it -- ten days of the fief's full labour
        /// ceiling, or his whole purse, whichever is less.
        ///
        /// Patched on the property SETTER rather than at either site that assigns it, because the VM
        /// writes it from both its constructor and <c>ExecuteConfirm</c>, and this catches every write
        /// with one seam.
        /// </remarks>
        /// <summary>
        /// Rewrites the reserve panel's explanation. Vanilla prints "+BOOST construction for COST gold a
        /// day" from the two figures neutralised above, which would now read as +0 for two billion.
        /// </summary>
        [HarmonyPatch(typeof(TownManagementReserveControlVM), "UpdateReserveText")]
        private static class ReserveTextPatch
        {
            private static void Postfix(TownManagementReserveControlVM __instance, Settlement ____settlement)
            {
                if (!Active || ____settlement == null || ____settlement.Town == null)
                {
                    return;
                }
                Town town = ____settlement.Town;
                TextObject text = new TextObject("{=!}The reserve pays for all building work at one denar per point of construction. Up to {CAP} points of labour are available a day; the fief adds {SHARE}% of its treasury to the reserve daily.");
                text.SetTextVariable("CAP", (int)Construction.DailyCapacity(town));
                text.SetTextVariable("SHARE", (RBMConfig.RBMConfig.constructionBudgetShare * 100f).ToString("0.#"));
                __instance.ReserveBonusText = text.ToString();
            }
        }

        [HarmonyPatch(typeof(TownManagementReserveControlVM), "set_MaxReserveAmount")]
        private static class MaxReservePatch
        {
            private static void Prefix(ref int value, Settlement ____settlement)
            {
                if (!Active || ____settlement == null || ____settlement.Town == null)
                {
                    return;
                }
                float capacity = Construction.DailyCapacity(____settlement.Town) * 10f;
                int raised = (capacity > 2000000000f) ? 2000000000 : (int)capacity;
                if (raised <= value)
                {
                    return;
                }
                int gold = (Hero.MainHero != null) ? Hero.MainHero.Gold : 0;
                value = (raised < gold) ? raised : gold;
            }
        }
    }
}
