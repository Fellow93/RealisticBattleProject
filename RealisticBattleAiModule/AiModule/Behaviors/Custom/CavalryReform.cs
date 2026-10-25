using System;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using MathF = TaleWorlds.Library.MathF;

namespace RBMAI
{
    /// <summary>
    /// DYNAMIC REFORM (user 2026-10-23): where RBMBehaviorCavalryCharge regroups after a charge, picked from where the formations
    /// stand now. Candidates: through (beyond the target's far edge along the charge direction), left / right (off the charge
    /// line), back (behind our main infantry). Formations first: no significant enemy within HardGap (edge gap), room from enemies
    /// up to RoomGap (melee cavalry and spear/shield-heavy foot cost more), no friend standing on / heading for it (FriendlyOverlap,
    /// other reforming cavalry included); then the ride and a small pull toward our army. Terrain only filters (map edge, walkable;
    /// bad ground / woods and a slope cost with terrainTacticsEnabled). Re-checked every CheckSeconds; the point is kept unless it
    /// turned unsafe or another beats it by SwitchMargin, at most one change per ChangeSeconds.
    /// THREADING: AI thread, one instance per behavior. Cached formation data and the immutable terrain grid; the Move point is a
    /// copy of the formation's cached median with ONE SetVec2MT, built only when the point changes.
    /// </summary>
    internal sealed class CavalryReform
    {
        internal enum Kind
        {
            Through,
            Left,
            Right,
            Back
        }

        private const float HardGap = 60f;
        private const float RoomGap = 150f;
        private static readonly float[] ThroughDistances = { 120f, 160f, 200f };
        private static readonly float[] SideDistances = { 130f, 180f };
        private const float SideAlong = 30f; // side points a little beyond the target's centre
        private const float BackGap = 40f; // behind our main's rear
        private static readonly float[] BackOffsets = { 0f, -50f, 50f };
        private const float RoomWeight = 3f;
        private const float MeleeCavalryWeight = 2f;
        private const float RangedWeight = 0.6f;
        private const float InsignificantWeight = 0.4f;
        private const float RideWeight = 1f;
        private const float RideNorm = 250f;
        private const float ArmyWeight = 0.3f;
        private const float ArmyNorm = 500f;
        private const float OverlapWeight = 1f;
        private const float SlopeWeight = 0.5f;
        private const float SlopeNorm = 25f;
        private const float CheckSeconds = 2f;
        private const float ChangeSeconds = 4f;
        private const float SwitchMargin = 0.25f;

        private struct Candidate
        {
            public Kind Kind;
            public Vec2 Point;
            public Vec2 Facing;
            public float Cost;
            public float Gap;
            public Formation Nearest;
            public float Ride;
            public float Overlap;
            public float Slope;
            public string Reject;
        }

        private readonly List<Formation> _enemies = new List<Formation>();
        private readonly List<Footprint> _enemyPrints = new List<Footprint>();
        private readonly List<bool> _enemySignificant = new List<bool>();
        private readonly List<Candidate> _candidates = new List<Candidate>();

        private bool _has;
        private Candidate _current;
        private WorldPosition _world = WorldPosition.Invalid;
        private float _lastCheck = float.NegativeInfinity;
        private float _lastChange = float.NegativeInfinity;

        internal bool Holding => _has;

        internal string KindName => _has ? Name(_current.Kind) : null;

        /// <summary>AI thread, every reform tick: true and the Move point; false (released, logged) when no candidate is usable.</summary>
        internal bool Tick(BehaviorComponent behavior, string behaviorName, Formation target, Vec2 chargeDirection, out WorldPosition order)
        {
            order = WorldPosition.Invalid;
            Formation formation = behavior?.Formation;
            Mission mission = Mission.Current;
            if (formation == null || formation.Team == null || mission == null || target == null || target.CountOfUnits <= 0)
            {
                Release(behavior, behaviorName, "noTarget");
                return false;
            }
            float now = mission.CurrentTime;
            if (_has && now - _lastCheck < CheckSeconds)
            {
                order = _world;
                return true;
            }
            _lastCheck = now;
            ReadEnemies(mission, formation);
            Vec2 us = RBMAI.Utilities.GetFormationCenter(formation);
            Vec2 targetAt = RBMAI.Utilities.GetFormationCenter(target);
            Vec2 dir = Unit(chargeDirection);
            if (!dir.IsValid)
            {
                dir = Unit(targetAt - us);
            }
            if (!dir.IsValid || !us.IsValid || !targetAt.IsValid)
            {
                Release(behavior, behaviorName, "noGeometry");
                return false;
            }
            Formation main = FormationCohesion.TeamMain(formation.Team);
            if (main == formation || (main != null && (main.QuerySystem.IsCavalryFormation || main.QuerySystem.IsRangedCavalryFormation)))
            {
                main = null;
            }
            float pike = PikeShare(formation.Team.Side);
            BattleTerrain terrain = BattleTerrain.Current;

            string keepWhy = null;
            if (_has)
            {
                Candidate again = _current;
                Score(mission, formation, terrain, us, main, pike, true, ref again);
                if (again.Reject == null)
                {
                    _current = again;
                }
                else
                {
                    keepWhy = again.Reject;
                }
            }

            BuildCandidates(formation, target, targetAt, dir, main);
            int best = -1;
            for (int i = 0; i < _candidates.Count; i++)
            {
                Candidate c = _candidates[i];
                Score(mission, formation, terrain, us, main, pike, false, ref c);
                _candidates[i] = c;
                if (c.Reject == null && (best < 0 || c.Cost < _candidates[best].Cost))
                {
                    best = i;
                }
            }

            if (_has && keepWhy == null)
            {
                bool better = best >= 0 && _candidates[best].Cost < _current.Cost * (1f - SwitchMargin) && now - _lastChange >= ChangeSeconds;
                if (!better)
                {
                    order = _world;
                    return true;
                }
            }

            // take / change: the best candidate whose point is on the navmesh (one SetVec2MT each, usually the first)
            while (best >= 0)
            {
                Candidate pick = _candidates[best];
                WorldPosition world = formation.CachedMedianPosition;
                world.SetVec2MT(pick.Point);
                if (world.GetNavMeshMT() != UIntPtr.Zero)
                {
                    string what = !_has ? "reform take" : "reform change";
                    string why = (_has && keepWhy != null) ? " was=" + Name(_current.Kind) + "(" + keepWhy + ")" : _has ? " was=" + Name(_current.Kind) : "";
                    _has = true;
                    _current = pick;
                    _world = world;
                    _lastChange = now;
                    TerrainCavalry.NoteEvent(behavior, behaviorName, what, Describe(pick) + why);
                    order = _world;
                    return true;
                }
                pick.Reject = "noNavmesh";
                _candidates[best] = pick;
                best = -1;
                for (int i = 0; i < _candidates.Count; i++)
                {
                    if (_candidates[i].Reject == null && (best < 0 || _candidates[i].Cost < _candidates[best].Cost))
                    {
                        best = i;
                    }
                }
            }
            if (_has && keepWhy == null)
            {
                order = _world;
                return true;
            }
            if (_has)
            {
                Release(behavior, behaviorName, "none(" + keepWhy + ") " + Rejects());
            }
            else
            {
                TerrainCavalry.NoteEvent(behavior, behaviorName, "reform none", Rejects());
            }
            return false;
        }

        /// <summary>AI thread (or wherever native cancels the behavior): drops the point, logs once.</summary>
        internal void Release(BehaviorComponent behavior, string behaviorName, string why)
        {
            if (!_has)
            {
                return;
            }
            _has = false;
            _world = WorldPosition.Invalid;
            _lastCheck = float.NegativeInfinity;
            TerrainCavalry.NoteEvent(behavior, behaviorName, "reform release", "kind=" + Name(_current.Kind) + " why=" + why, false);
        }

        private void ReadEnemies(Mission mission, Formation formation)
        {
            _enemies.Clear();
            _enemyPrints.Clear();
            _enemySignificant.Clear();
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(formation.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                {
                    Footprint print;
                    if (enemy == null || enemy.CountOfUnits <= 0 || !FormationGeometry.TryGet(enemy, FootprintMode.ScatterAware, out print))
                    {
                        continue;
                    }
                    _enemies.Add(enemy);
                    _enemyPrints.Add(print);
                    _enemySignificant.Add(ThreatSignificance.IsSignificant(formation, enemy));
                }
            }
        }

        private void BuildCandidates(Formation formation, Formation target, Vec2 targetAt, Vec2 dir, Formation main)
        {
            _candidates.Clear();
            Footprint targetPrint;
            if (!FormationGeometry.TryGet(target, FootprintMode.ScatterAware, out targetPrint))
            {
                targetPrint = Footprint.Circle(targetAt, MathF.Max(target.Width, target.Depth) * 0.5f);
            }
            Vec2 far = targetPrint.Centre + dir * targetPrint.ExtentAlong(dir);
            foreach (float d in ThroughDistances)
            {
                Add(Kind.Through, far + dir * d, targetAt);
            }
            Vec2 left = new Vec2(-dir.y, dir.x);
            float across = targetPrint.ExtentAlong(left);
            foreach (float d in SideDistances)
            {
                Add(Kind.Left, targetPrint.Centre + dir * SideAlong + left * (across + d), targetAt);
                Add(Kind.Right, targetPrint.Centre + dir * SideAlong - left * (across + d), targetAt);
            }
            if (main != null)
            {
                Vec2 mainAt = RBMAI.Utilities.GetFormationCenter(main);
                Vec2 facing = Unit(main.Direction);
                if (mainAt.IsValid && facing.IsValid)
                {
                    Vec2 side = new Vec2(-facing.y, facing.x);
                    Vec2 rear = mainAt - facing * (MathF.Max(main.Depth, 1f) * 0.5f + BackGap);
                    foreach (float o in BackOffsets)
                    {
                        _candidates.Add(new Candidate { Kind = Kind.Back, Point = rear + side * o, Facing = facing });
                    }
                }
            }
        }

        private void Add(Kind kind, Vec2 point, Vec2 targetAt)
        {
            Vec2 facing = Unit(targetAt - point);
            _candidates.Add(new Candidate { Kind = kind, Point = point, Facing = facing });
        }

        /// <summary>Fills Cost (lower wins) or Reject. keep = the held point (FriendlyOverlap's keep share).</summary>
        private void Score(Mission mission, Formation formation, BattleTerrain terrain, Vec2 us, Formation main, float pike, bool keep, ref Candidate c)
        {
            c.Reject = null;
            c.Nearest = null;
            c.Gap = float.MaxValue;
            c.Slope = float.NaN;
            Vec2 p = c.Point;
            if (!p.IsValid || !c.Facing.IsValid)
            {
                c.Reject = "invalid";
                return;
            }
            if (terrain != null)
            {
                if (terrain.NearEdge(p) || !terrain.IsWalkable(p))
                {
                    c.Reject = "ground";
                    return;
                }
                if (RBMConfig.RBMConfig.terrainTacticsEnabled)
                {
                    if (terrain.Cavalry.IsBadGround(p))
                    {
                        c.Reject = "badGround";
                        return;
                    }
                    c.Slope = terrain.SlopeAt(p);
                }
            }
            else if (!mission.IsPositionInsideBoundaries(p))
            {
                c.Reject = "outside";
                return;
            }
            Footprint own = FormationGeometry.AtFront(formation, p, c.Facing);
            float room = 0f;
            for (int i = 0; i < _enemies.Count; i++)
            {
                Formation enemy = _enemies[i];
                float gap = FormationGeometry.Gap(own, _enemyPrints[i]);
                bool significant = _enemySignificant[i];
                if (gap < c.Gap)
                {
                    c.Gap = gap;
                    c.Nearest = enemy;
                }
                if (significant && gap < HardGap)
                {
                    c.Reject = "enemy(" + TerrainCavalry.FormationName(enemy) + ")";
                    return;
                }
                if (gap >= RoomGap)
                {
                    continue;
                }
                FormationQuerySystem q = enemy.QuerySystem;
                float w = q.IsCavalryFormation ? MeleeCavalryWeight
                    : q.IsRangedCavalryFormation || q.IsRangedFormation ? RangedWeight
                    : 1f + pike;
                if (!significant)
                {
                    w *= InsignificantWeight;
                }
                room += w * (1f - gap / RoomGap);
            }
            Formation friend;
            c.Overlap = FriendlyOverlap.Share(formation, p, c.Facing, null, FriendlyOverlap.Mode.NowAndHeading, out friend);
            if (c.Overlap > (keep ? FriendlyOverlap.KeepMaxShare : FriendlyOverlap.TakeMaxShare))
            {
                c.Reject = FriendlyOverlap.RefusalText(friend, c.Overlap);
                return;
            }
            c.Ride = us.Distance(p);
            float cost = RoomWeight * room + RideWeight * c.Ride / RideNorm + OverlapWeight * c.Overlap;
            if (main != null)
            {
                Vec2 mainAt = RBMAI.Utilities.GetFormationCenter(main);
                if (mainAt.IsValid)
                {
                    cost += ArmyWeight * MathF.Min(1f, mainAt.Distance(p) / ArmyNorm);
                }
            }
            if (!float.IsNaN(c.Slope))
            {
                cost += SlopeWeight * MathF.Min(1f, c.Slope / SlopeNorm);
            }
            c.Cost = cost;
        }

        /// <summary>The enemy side's spear/shield share of its melee infantry (TerrainHedgehog's read), 0 unknown.</summary>
        private static float PikeShare(BattleSideEnum own)
        {
            BattleSideEnum enemy = (own == BattleSideEnum.Attacker) ? BattleSideEnum.Defender : BattleSideEnum.Attacker;
            TerrainHedgehog.SidePlan plan = TerrainHedgehog.Published(enemy);
            float share = (plan != null) ? plan.PikeShare : 0f;
            return (float.IsNaN(share) || share < 0f) ? 0f : MathF.Min(1f, share);
        }

        private string Describe(Candidate c)
        {
            return "kind=" + Name(c.Kind) + " gap=" + Fmt0(c.Gap) + "m(enemy " + TerrainCavalry.FormationName(c.Nearest) + ") ride=" + Fmt0(c.Ride)
                + "m overlap=" + c.Overlap.ToString("0.##", CultureInfo.InvariantCulture)
                + " terrain=" + (float.IsNaN(c.Slope) ? "-" : "slope" + Fmt0(c.Slope))
                + " cost=" + c.Cost.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private string Rejects()
        {
            if (!RBMConfig.RBMConfig.battleTerrainLoggingEnabled)
            {
                return "";
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (Candidate c in _candidates)
            {
                if (c.Reject != null)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(' ');
                    }
                    sb.Append(Name(c.Kind)).Append(':').Append(c.Reject);
                }
            }
            return sb.ToString();
        }

        private static string Name(Kind kind)
        {
            switch (kind)
            {
                case Kind.Through:
                    return "through";
                case Kind.Left:
                    return "left";
                case Kind.Right:
                    return "right";
                default:
                    return "back";
            }
        }

        private static string Fmt0(float value)
        {
            return TerrainCavalry.Fmt0(value);
        }

        private static Vec2 Unit(Vec2 v)
        {
            if (!v.IsValid || v.LengthSquared < 0.0001f)
            {
                return Vec2.Invalid;
            }
            return v.Normalized();
        }
    }
}
