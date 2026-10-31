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

        /// <summary>One army re-plan's results (TerrainArmyReplan): the archer spots, and the cavalry ground when asked (else null).</summary>
        internal sealed class ArmyReplan
        {
            public SpotsInfo Spots;
            public CavalryInfo Cavalry;
        }

        /// <summary>TerrainArmyReplan's live re-plan of the archer spots and (cavalry) the cavalry ground on ONE frame. Any thread: pure grid work, own A* scratch, never writes to this snapshot.</summary>
        internal ArmyReplan ReplanArmyPlans(Vec2 attacker, Vec2 defender, Vec2 attackerAcross, Vec2 defenderAcross, bool cavalry)
        {
            return BattleTerrainRead.ReplanArmyPlans(this, _grid, attacker, defender, attackerAcross, defenderAcross, cavalry);
        }
    }
}
