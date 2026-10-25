using System.Collections.Generic;
using TaleWorlds.Library;

namespace RBMAI
{
    /// <summary>Entry points of the background live re-plans (pure grid work over the snapshot's grid). A PathSearch's scratch arrays must never be shared between threads.</summary>
    internal sealed partial class BattleTerrain
    {
        /// <summary>TerrainFlankPlan's live re-plan. Any thread: pure grid work with its own A* scratch.</summary>
        internal List<FlankFeature> ReplanFlanks(Vec2 attacker, Vec2 defender, float attackerFrontage, float defenderFrontage, float attackerRide, float defenderRide)
        {
            return BattleTerrainRead.ReplanFlanks(_grid, attacker, defender, attackerFrontage, defenderFrontage, attackerRide, defenderRide);
        }

        /// <summary>TerrainCavalry's live re-plan. Any thread: pure grid work, own A* scratch, never writes to this snapshot.</summary>
        internal CavalryInfo ReplanCavalry(Vec2 attacker, Vec2 defender, Vec2 attackerAcross, Vec2 defenderAcross)
        {
            return BattleTerrainRead.ReplanCavalry(this, _grid, attacker, defender, attackerAcross, defenderAcross);
        }

        /// <summary>TerrainSpots' live re-plan. Same rules as ReplanCavalry.</summary>
        internal SpotsInfo ReplanSpots(Vec2 attacker, Vec2 defender, Vec2 attackerAcross, Vec2 defenderAcross)
        {
            return BattleTerrainRead.ReplanSpots(this, _grid, attacker, defender, attackerAcross, defenderAcross);
        }
    }
}
