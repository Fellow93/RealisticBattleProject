using HarmonyLib;
using TaleWorlds.CampaignSystem.GameComponents;

namespace RBMCampaign
{
    /// <summary>
    /// Takes morale out of a simulated blow.
    ///
    /// Vanilla's <see cref="DefaultCombatSimulationModel"/> ends every <c>SimulateHit</c> by running the blow through
    /// <c>CalculateSimulationMoraleEffects</c>, which multiplies its damage by a factor drawn from the two sides'
    /// morale relative to a baseline of fifty:
    ///
    ///   factor = 1 + ( min(strikerMorale - 50, 0) - max(struckMorale - 50, 0) ) * 0.005
    ///
    /// -- an asymmetric penalty that can only ever WEAKEN the striker: his own low morale softens his blows, and a
    /// high-morale defender softens them too, up to a combined -50% at the extremes. That factor is applied on top of
    /// the tier-power ratio the equipment model replaces (see <see cref="SimulationEquipmentPower"/>), and it prices
    /// a blow on how the fight is going rather than on what the man is carrying.
    ///
    /// RBM's auto-resolve prices a blow on the striker's actual kit against the struck man's actual armour, and does
    /// not want that number then bent by a morale multiplier. Whether a side breaks and routs is untouched -- that is
    /// tracked outside SimulateHit, in the simulation loop, and it still decides who leaves the field. This removes
    /// only morale's thumb on the DAMAGE of an individual blow, by skipping the vanilla method that applies it.
    /// </summary>
    [HarmonyPatch(typeof(DefaultCombatSimulationModel), "CalculateSimulationMoraleEffects")]
    internal static class SimulationMorale
    {
        // Returning false skips the original body entirely, so the morale AddFactor never touches the blow. The
        // method is void, so there is nothing to substitute -- the damage simply passes through unmodified. But only
        // while the equipment model is actually pricing blows: with it off (see SimulationEquipmentPower.
        // SimulationEnabled) the battle is meant to be vanilla's own, morale and all, so let the original run.
        private static bool Prefix()
        {
            return !SimulationEquipmentPower.SimulationEnabled;
        }
    }
}
