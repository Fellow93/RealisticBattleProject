using HarmonyLib;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Roster;

namespace RBMCampaign
{
    /// <summary>
    /// Puts the player back in the fight when he sends his troops.
    ///
    /// Vanilla auto-resolve is built to spare the player: a field battle he leads himself spawns him as a man on
    /// the ground, but a battle he "sends troops" to is mustered with includePlayer=false, and the muster's own
    /// gate -- CanTroopJoinBattle -- drops the main character on that flag alone. His soldiers fight and can fall;
    /// he is simply not there. The AI lords beside him have no such shield: every one of them is mustered, swings,
    /// and can be wounded or killed in the same simulation.
    ///
    /// That asymmetry is what this undoes. Once the player is allowed into the muster he is an ordinary hero in it,
    /// the same as any lord -- SelectRandomSimulationTroop can pick him to strike (and his hero-tier kit strikes
    /// hard) or to be struck, and ApplySimulationDamageToSelectedTroop rolls his survival exactly as it already
    /// rolls every other hero's. So sending his troops now means sending himself with them, risk and weight alike.
    ///
    /// Only ever flips the includePlayer=false case, and only for the player character: a real field battle passes
    /// includePlayer=true and is untouched, and no other troop's verdict is ever changed. Gated on the simulation
    /// master switch, so with the overhaul off vanilla's spared-player auto-resolve stands exactly as it was.
    /// </summary>
    [HarmonyPatch(typeof(DefaultTroopSupplierProbabilityModel), "CanTroopJoinBattle")]
    internal static class SimulationPlayerParticipation
    {
        private static void Postfix(FlattenedTroopRosterElement troopRoster, bool includePlayer, ref bool __result)
        {
            // Already in the muster (or a real field battle, where includePlayer is true): nothing to do.
            if (__result || includePlayer)
            {
                return;
            }

            // With the overhaul off, leave vanilla's spared-player auto-resolve untouched.
            if (!SimulationEquipmentPower.SimulationEnabled)
            {
                return;
            }

            // The ONLY man vanilla drops here is the player character; his troops passed already. Let him in --
            // unless he is already out of the fight, in which case vanilla was right to leave him out.
            if (troopRoster.Troop.IsPlayerCharacter
                && !troopRoster.IsWounded && !troopRoster.IsRouted && !troopRoster.IsKilled)
            {
                __result = true;
            }
        }
    }
}
