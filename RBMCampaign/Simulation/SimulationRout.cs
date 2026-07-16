using System;
using System.Collections.Generic;
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
        // A side breaks when it is being BUTCHERED -- when it has lost a much larger SHARE of the men it marched in
        // with than the enemy has. Not when it is merely outnumbered: a small party that STARTS lopsided (100 vs 25)
        // has taken no casualties yet and should not evaporate in round one, and a quality force winning against a
        // horde (80 knights vs 300 recruits) is BELOW the enemy on live headcount yet is plainly winning -- the old
        // live-headcount ratio broke on both. Casualty share reads the fight the right way round: the side bleeding
        // out faster, whatever its raw numbers, is the one that breaks. This is the gap in loss fractions at which a
        // side comes at risk of breaking -- it has lost this much MORE of itself, in proportion, than its enemy.
        private const float RoutLossGapThreshold = 0.2f;

        // ...and it must also have taken real losses of its own before it will run: a side twenty points ahead on a
        // near-bloodless field is not being butchered. This floors the beaten side's own casualty fraction -- it must
        // have bled a quarter of itself away before it will consider breaking, so a side runs only once it is plainly
        // being ground down rather than merely bloodied.
        private const float RoutMinBeatenLoss = 0.25f;

        // The chance to break in a given round, once at risk: a small base, plus a share that grows with how far past
        // the gap the butchery has gone (severity 0 at the threshold, 1 as the side is wiped out). Re-rolled each
        // round, so a hopeless stand compounds toward a near-certain break; a merely bad one may yet hold. Kept low so
        // routs stay the exception -- a beaten side more often fights on and takes its losses than breaks and runs.
        private const float RoutBaseChancePerRound = 0.03f;

        private const float RoutSeverityScale = 0.35f;

        private const float RoutMaxChancePerRound = 0.45f;

        // Vanilla's BattleState setter is internal, so ending the battle from here -- the same act vanilla's own rout
        // performs -- goes through the setter by reflection. Cached once. Going through the SETTER (not the backing
        // field) is deliberate: it is what fires OnBattleWon and finalises the event.
        private static readonly MethodInfo SetBattleState =
            typeof(MapEvent).GetProperty("BattleState")?.GetSetMethod(nonPublic: true);

        // The men each side marched in with, captured before the first simulated round wears them down -- the
        // denominator the casualty fractions are measured against. Keyed by the event, dropped when it ends.
        private static readonly Dictionary<MapEvent, ValueTuple<int, int>> _initial =
            new Dictionary<MapEvent, ValueTuple<int, int>>();

        /// <summary>A battle is over; drop its starting muster. Wired from OnMapEventEnded alongside the other cleanups.</summary>
        internal static void Forget(MapEvent mapEvent)
        {
            if (mapEvent != null)
            {
                _initial.Remove(mapEvent);
            }
        }

        /// <summary>A fresh session: the torn-down campaign's musters will never be reclaimed by MapEventEnded, so
        /// drop them all. Called from OnSessionLaunched.</summary>
        internal static void ResetForNewSession()
        {
            _initial.Clear();
        }

        // Before the round is simulated, the sides stand at whatever strength they have. The FIRST time we see a
        // battle that is the strength it marched in with -- nobody has died yet -- so that is the muster to remember.
        private static void Prefix(MapEvent __instance)
        {
            // With the equipment model off the whole overhaul stands down (see SimulationEquipmentPower.
            // SimulationEnabled): vanilla decides who wins, so there is nothing to muster and nothing to break.
            if (!SimulationEquipmentPower.SimulationEnabled || !RBMConfig.RBMConfig.simulationRoutEnabled)
            {
                return;
            }
            if (__instance == null || _initial.ContainsKey(__instance))
            {
                return;
            }
            _initial[__instance] = new ValueTuple<int, int>(
                __instance.AttackerSide.NumRemainingSimulationTroops,
                __instance.DefenderSide.NumRemainingSimulationTroops);
        }

        private static void Postfix(MapEvent __instance)
        {
            // The overhaul stands down with the equipment model off (see SimulationEquipmentPower.SimulationEnabled):
            // vanilla's own morale/annihilation resolution decides the battle, exactly as it would without RBM. The
            // dedicated rout toggle stands only this feature down, leaving equipment-aware damage in place.
            if (!SimulationEquipmentPower.SimulationEnabled || !RBMConfig.RBMConfig.simulationRoutEnabled)
            {
                return;
            }
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

            // The strength each side marched in with. The Prefix caught it before the first round; if it somehow did
            // not (a battle already under way when the patch loaded), take the current strength as the baseline -- it
            // makes this round's loss fractions zero, so nobody routs until real casualties accumulate. Never zero, so
            // the fractions cannot divide by it.
            ValueTuple<int, int> initial;
            if (!_initial.TryGetValue(__instance, out initial))
            {
                initial = new ValueTuple<int, int>(attackers, defenders);
                _initial[__instance] = initial;
            }
            int attackersInitial = Math.Max(1, initial.Item1);
            int defendersInitial = Math.Max(1, initial.Item2);

            // What SHARE of itself each side has lost. Clamped at zero because a side that gained men after the muster
            // (reinforcements attaching mid-battle) would otherwise read a negative loss.
            float attackerLoss = Math.Max(0f, (attackersInitial - attackers) / (float)attackersInitial);
            float defenderLoss = Math.Max(0f, (defendersInitial - defenders) / (float)defendersInitial);

            // The side bleeding out faster is the one that breaks -- whatever its raw numbers. A dead-even bleed breaks
            // nobody.
            MapEventSide loser;
            MapEventSide winner;
            float beatenLoss;
            float otherLoss;
            if (attackerLoss > defenderLoss)
            {
                loser = __instance.AttackerSide;
                winner = __instance.DefenderSide;
                beatenLoss = attackerLoss;
                otherLoss = defenderLoss;
            }
            else if (defenderLoss > attackerLoss)
            {
                loser = __instance.DefenderSide;
                winner = __instance.AttackerSide;
                beatenLoss = defenderLoss;
                otherLoss = attackerLoss;
            }
            else
            {
                return;
            }

            // It runs only once it is being butchered -- far enough ahead of the enemy in proportional losses AND
            // having bled enough of its own to feel it. Either test unmet, it holds.
            float gap = beatenLoss - otherLoss;
            if (gap < RoutLossGapThreshold || beatenLoss < RoutMinBeatenLoss)
            {
                return;
            }

            // How far past the breaking point the butchery has gone: 0 right at the gap threshold, 1 when the beaten
            // side is being wiped out (the enemy untouched). The chance climbs with it, re-rolled every round.
            float severity = (gap - RoutLossGapThreshold) / (1f - RoutLossGapThreshold);
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
