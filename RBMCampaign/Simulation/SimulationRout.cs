using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// A beaten side breaks and runs, instead of being butchered to the last man.
    ///
    /// Vanilla auto-resolve already HAS a rout -- <see cref="MapEvent.CalculateWinner"/> ends the battle and calls
    /// <c>Route()</c> on the losing side, which sends its survivors off as fugitives (counted at a tenth of a
    /// casualty's weight; the men live) rather than killing them. But the only mid-battle trigger for it is a side's
    /// <c>GetSideMorale()</c> falling to approximately zero -- and that morale is a strength-weighted average of each
    /// party's STANDING campaign morale (<c>MobileParty.Morale</c>: food, wages, recent events). It does not move as
    /// men fall during the simulated fight. So the gate essentially never trips, and every auto-resolved battle grinds
    /// on until one side's <c>NumRemainingSimulationTroops</c> reaches nought -- annihilation, not a rout.
    ///
    /// This adds the missing trigger: once a side is beaten badly enough on the field -- its remaining fighting
    /// strength fallen below a fraction of the enemy's -- it may break, with a chance that climbs the more lopsided
    /// the fight becomes and is re-rolled every round, so a hopeless position gives way sooner. When it does, the
    /// beaten side is routed through vanilla's OWN <c>Route()</c> and the battle is ended for the other side, exactly
    /// as vanilla's morale rout would have -- so the fugitives survive, the pursuit and the reward books all behave,
    /// and nothing here reimplements what the game already does. It only decides WHEN the break happens.
    ///
    /// Sieges are left to vanilla: a storming assault has its own state machine and its defenders cannot flee a wall
    /// (see <c>MapEventSide.OnTroopRouted</c>, which refuses to rout a siege defender), so forcing a break here would
    /// be both wrong and fragile. This fires on field battles, raids and hideouts.
    /// </summary>
    [HarmonyPatch(typeof(MapEvent), "SimulateBattleRound")]
    internal static class SimulationRout
    {
        // A side is only AT RISK of breaking once it is this far down on the enemy in remaining fighting men -- below
        // 35% of the winner's numbers. NumRemainingSimulationTroops is the live survivor count vanilla itself uses to
        // detect a wipe; troop QUALITY is already expressed in it, since better troops wear the enemy's count down
        // faster and lose their own slower, so by the time the ratio is this lopsided the stronger army has shown it.
        private const float RoutStrengthThreshold = 0.35f;

        // The chance to break in a given round, once at risk: a small base, plus a share that grows with how far past
        // the threshold the rout has gone (severity 0 at the threshold, 1 as the side is wiped out). Re-rolled each
        // round, so a hopeless stand compounds toward a near-certain break; a merely bad one may yet hold.
        private const float RoutBaseChancePerRound = 0.06f;

        private const float RoutSeverityScale = 0.55f;

        private const float RoutMaxChancePerRound = 0.6f;

        // Vanilla's BattleState setter is internal, so ending the battle from here -- the same act vanilla's own rout
        // performs -- goes through the setter by reflection. Cached once. Going through the SETTER (not the backing
        // field) is deliberate: it is what fires OnBattleWon and finalises the event.
        private static readonly MethodInfo SetBattleState =
            typeof(MapEvent).GetProperty("BattleState").GetSetMethod(nonPublic: true);

        private static void Postfix(MapEvent __instance)
        {
            // Only a battle still being fought, and never a siege (see the class note).
            if (__instance == null || __instance.BattleState != BattleState.None || __instance.IsSiegeAssault)
            {
                return;
            }
            if (SetBattleState == null)
            {
                return;
            }

            int attackers = __instance.AttackerSide.NumRemainingSimulationTroops;
            int defenders = __instance.DefenderSide.NumRemainingSimulationTroops;

            // If either side is already gone, vanilla's own CalculateWinner has the battle -- nothing to break.
            if (attackers <= 0 || defenders <= 0)
            {
                return;
            }

            // The weaker side is the one that might break; the stronger is who it loses to. A dead-even field breaks
            // nobody.
            MapEventSide loser;
            MapEventSide winner;
            int loserCount;
            int winnerCount;
            if (attackers < defenders)
            {
                loser = __instance.AttackerSide;
                winner = __instance.DefenderSide;
                loserCount = attackers;
                winnerCount = defenders;
            }
            else if (defenders < attackers)
            {
                loser = __instance.DefenderSide;
                winner = __instance.AttackerSide;
                loserCount = defenders;
                winnerCount = attackers;
            }
            else
            {
                return;
            }

            float ratio = (float)loserCount / (float)winnerCount;
            if (ratio >= RoutStrengthThreshold)
            {
                return;
            }

            // How far past the breaking point the rout has gone: 0 right at the threshold, 1 as the loser is wiped out.
            float severity = (RoutStrengthThreshold - ratio) / RoutStrengthThreshold;
            float chance = RoutBaseChancePerRound + severity * RoutSeverityScale;
            if (chance > RoutMaxChancePerRound)
            {
                chance = RoutMaxChancePerRound;
            }

            if (MBRandom.RandomFloat >= chance)
            {
                return;
            }

            // The break. Vanilla's own Route() sends the survivors off as fugitives; then the battle is ended for the
            // winner, exactly as CalculateWinner's morale rout does -- BattleState's setter fires OnBattleWon and the
            // event finalises through the ordinary path.
            loser.Route();
            BattleState result = (winner == __instance.AttackerSide)
                ? BattleState.AttackerVictory
                : BattleState.DefenderVictory;
            SetBattleState.Invoke(__instance, new object[] { result });
        }
    }
}
