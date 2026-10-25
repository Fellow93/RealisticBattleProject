using System.Globalization;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// Power-weighted centroid of the significant, non-retreating enemy formations near a reactor (class-weighted).
    /// Used by HoldReface and the choke bypass, foot only there. Any thread: cached data only, no engine call.
    /// </summary>
    internal static class EnemyConcentration
    {
        internal const float Range = 400f; // m, edge distance

        internal const float MeleeFootWeight = 1f;
        internal const float MeleeCavalryWeight = 0.7f;
        internal const float RangedWeight = 0.4f;
        internal const float HorseArcherWeight = 0.2f;

        internal struct Result
        {
            public Vec2 Centre;
            public float Weight;
            public int Formations;
            public Formation Main;
        }

        /// <summary>False (Centre invalid) when none is within range; footOnly ignores mounted formations.</summary>
        internal static bool Find(Mission mission, Formation reactor, float range, out Result result, bool footOnly = false)
        {
            result = default(Result);
            result.Centre = Vec2.Invalid;
            if (mission == null || reactor == null || reactor.Team == null || reactor.CountOfUnits <= 0)
            {
                return false;
            }
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(reactor);
            if (!centre.IsValid)
            {
                return false;
            }
            float x = 0f;
            float y = 0f;
            float total = 0f;
            float best = 0f;
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(reactor.Team))
                {
                    continue;
                }
                foreach (Formation e in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (e == null || e.CountOfUnits <= 0 || e.GetReadonlyMovementOrderReference().OrderType == OrderType.Retreat
                        || (footOnly && (e.QuerySystem.IsCavalryFormation || e.QuerySystem.IsRangedCavalryFormation)))
                    {
                        continue;
                    }
                    Vec2 c = RBMAI.Utilities.GetFormationCenter(e);
                    if (!c.IsValid || c.Distance(centre) - MathF.Max(e.Width, e.Depth) * 0.5f > range || !ThreatSignificance.IsSignificant(reactor, e))
                    {
                        continue;
                    }
                    float w = WeightOf(e);
                    x += c.x * w;
                    y += c.y * w;
                    total += w;
                    result.Formations++;
                    if (w > best)
                    {
                        best = w;
                        result.Main = e;
                    }
                }
            }
            if (total <= 0f)
            {
                return false;
            }
            result.Centre = new Vec2(x / total, y / total);
            result.Weight = total;
            return true;
        }

        internal static float WeightOf(Formation e)
        {
            return MathF.Max(e.QuerySystem.FormationPower, 1f) * ClassWeight(e);
        }

        internal static float ClassWeight(Formation e)
        {
            FormationQuerySystem q = e.QuerySystem;
            if (q.IsRangedCavalryFormation)
            {
                return HorseArcherWeight;
            }
            if (q.IsCavalryFormation)
            {
                return MeleeCavalryWeight;
            }
            return RBMAI.Utilities.IsRangedForArrangement(e) ? RangedWeight : MeleeFootWeight;
        }

        internal static string Describe(Result r)
        {
            if (!r.Centre.IsValid)
            {
                return "conc=-";
            }
            return "conc=" + r.Centre.x.ToString("0", CultureInfo.InvariantCulture) + "," + r.Centre.y.ToString("0", CultureInfo.InvariantCulture)
                + " n=" + r.Formations.ToString(CultureInfo.InvariantCulture)
                + " main=" + ((r.Main != null) ? TerrainCavalry.FormationName(r.Main) : "-")
                + " w=" + r.Weight.ToString("0", CultureInfo.InvariantCulture);
        }
    }
}
