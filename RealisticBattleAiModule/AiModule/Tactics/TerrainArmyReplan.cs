using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// The live re-plan of the army-relative plans that share one frame: the archer spots (TerrainSpots) and the cavalry ground
    /// (TerrainCavalry). One TerrainPlanRefresh decides when, one background job computes both from the armies' centres now.
    /// Per family: the DEFENDER's spots only when its own army moved / turned (user rule); the cavalry ground only while it has
    /// a plan for a side. MAIN thread only (BattleTerrainLogic's tick); cleared through TerrainSpots.Clear.
    /// </summary>
    internal static class TerrainArmyReplan
    {
        private static readonly TerrainPlanRefresh Refresh = new TerrainPlanRefresh("armyPlans");
        private static readonly ReplanJob<BattleTerrain.ArmyReplan> Job = new ReplanJob<BattleTerrain.ArmyReplan>("army re-plan canceled");
        private static bool _defender;
        private static string _defenderNote;
        private static bool _cavalry;

        internal static void Clear()
        {
            Job.Clear();
            Refresh.Clear();
            _defender = false;
            _defenderNote = null;
            _cavalry = false;
        }

        /// <summary>MAIN thread, every tick after the publish: finishes a running re-plan or starts one when due. Adds the log lines' fields to lines; runs whether or not the log is on.</summary>
        internal static void Tick(Mission mission, BattleTerrain terrain, float now, List<string> lines)
        {
            if (mission == null || terrain == null)
            {
                return;
            }
            if (Job.Running)
            {
                Finish(mission, terrain, lines);
                return;
            }
            // also with no spot for either side at the publish (there may be some later)
            if (!TerrainPlans.AnyActive)
            {
                return;
            }
            TerrainPlanRefresh.Armies armies;
            string note;
            string reason = Refresh.Check(mission, now, out armies, out note);
            if (note != null)
            {
                lines.Add("SPOTS\t" + note);
            }
            if (reason == null)
            {
                return;
            }
            Refresh.Started(now, armies);
            _defender = Refresh.SideMoved(BattleSideEnum.Defender, armies, out _defenderNote);
            if (_defender)
            {
                Refresh.SidePlanned(BattleSideEnum.Defender, armies);
            }
            _cavalry = TerrainCavalry.HasPlan;
            bool cavalry = _cavalry;
            Vec2 attacker = armies.Attacker;
            Vec2 defender = armies.Defender;
            Vec2 attackerAcross = armies.AttackerAcross;
            Vec2 defenderAcross = armies.DefenderAcross;
            // background: the read's FindSpots / FindCavalry over the immutable snapshot only
            Job.Start(reason, () => terrain.ReplanArmyPlans(attacker, defender, attackerAcross, defenderAcross, cavalry));
        }

        private static void Finish(Mission mission, BattleTerrain terrain, List<string> lines)
        {
            BattleTerrain.ArmyReplan result;
            string ms;
            string failure;
            ReplanJob<BattleTerrain.ArmyReplan>.Outcome outcome = Job.TryFinish(out result, out ms, out failure);
            if (outcome == ReplanJob<BattleTerrain.ArmyReplan>.Outcome.Failed)
            {
                lines.Add("SPOTS\treplan\tfailed=" + failure + "\treason=" + Job.Reason + "\twallMs=" + ms);
                if (_cavalry)
                {
                    lines.Add("CAVALRY\treplan\tfailed=" + failure + "\treason=" + Job.Reason + "\twallMs=" + ms);
                }
                return;
            }
            if (outcome != ReplanJob<BattleTerrain.ArmyReplan>.Outcome.Done || result == null)
            {
                return;
            }
            // each family on its own: a spots failure must not skip the cavalry ground (as the separate ticks did)
            try
            {
                TerrainSpots.ApplyReplan(mission, result.Spots, _defender, _defenderNote, Job.Reason, ms, lines);
            }
            catch (Exception e)
            {
                AiTickGuard.Report("TerrainSpots.ApplyReplan", e, Job.Reason);
            }
            if (_cavalry)
            {
                try
                {
                    TerrainCavalry.ApplyReplan(mission, terrain, result.Cavalry, Job.Reason, ms, lines);
                }
                catch (Exception e)
                {
                    AiTickGuard.Report("TerrainCavalry.ApplyReplan", e, Job.Reason);
                }
            }
        }
    }
}
