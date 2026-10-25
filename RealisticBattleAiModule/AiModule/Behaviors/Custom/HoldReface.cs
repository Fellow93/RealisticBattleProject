using System.Globalization;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// A holding line turns IN PLACE to face the enemy's main concentration (user rule). One instance per holding behavior:
    /// HoldPosition's Line role (any angle; Archers follow) and HoldChoke's Line role (only within MaxOffPlan of the gap's facing).
    ///
    /// RULE: line not in melee, concentration of enemy FOOT (EnemyConcentration footOnly) farther than MinDistance and its bearing
    /// more than TurnDegrees off the facing (pending while > TurnDegrees - HysteresisDegrees) for DwellSeconds: face that bearing;
    /// at most one turn per MinTurnSeconds. A plan facing change of KeepTurnDegrees or more goes back to the plan; a smaller one
    /// keeps the turn.
    ///
    /// TurnedPlace: the archer / reserve places rotate with the line round its pivot.
    ///
    /// TURNING IN PLACE: the Move point is the FRONT-centre, so the line pivots round the block centre (plan point - plan facing x
    /// depth / 2) and the new front = pivot + new facing x depth / 2. A front point off walkable ground refuses the turn. Copy of
    /// the plan's cached WorldPosition with ONE SetVec2MT (native caches the order position by its Vec2), rebuilt on > RebuildDistance
    /// and RebuildSeconds apart.
    ///
    /// Log: BEHAVIOR what "reface" / "unface".
    ///
    /// THREADING: AI thread: cached data, immutable terrain snapshot, one SetVec2MT on a fresh copy; no engine query, no MBRandom.
    /// </summary>
    internal sealed class HoldReface
    {

        internal const float TurnDegrees = 35f;
        internal const float HysteresisDegrees = 15f; // pending while above TurnDegrees minus this
        internal const float DwellSeconds = 5f;
        internal const float MinTurnSeconds = 10f;
        internal const float MinDistance = 40f; // m, concentration to the line's centre
        internal const float KeepTurnDegrees = 35f; // a smaller plan facing change keeps a local turn
        private const float RebuildDistance = 5f; // m
        private const float RebuildSeconds = 2f;

        private int _planKey = int.MinValue;
        private Vec2 _planFacing = Vec2.Invalid;
        private Vec2 _facing = Vec2.Invalid;
        private bool _turned;
        private float _pendingSince = float.NaN;
        private float _turnedAt = float.NegativeInfinity;

        private WorldPosition _world = WorldPosition.Invalid;
        private Vec2 _worldPoint = Vec2.Invalid;
        private Vec2 _worldPlanPoint = Vec2.Invalid;
        private float _builtAt = float.NegativeInfinity;

        /// <summary>Invalid before the first Update.</summary>
        internal Vec2 Facing => _turned ? _facing : _planFacing;

        internal void Reset()
        {
            _planKey = int.MinValue;
            _planFacing = Vec2.Invalid;
            _facing = Vec2.Invalid;
            _turned = false;
            _pendingSince = float.NaN;
            _turnedAt = float.NegativeInfinity;
            _world = WorldPosition.Invalid;
            _worldPoint = Vec2.Invalid;
            _worldPlanPoint = Vec2.Invalid;
            _builtAt = float.NegativeInfinity;
        }

        /// <summary>AI thread, every order calculation. planKey = plan version; mayTurn = holding at its point, not charged, active. maxOffPlanDegrees: 180 = any. Returns "reface" / "unface" with text in why, else null.</summary>
        internal string Update(Mission mission, Formation line, int planKey, Vec2 planFacing, float now, bool mayTurn, float maxOffPlanDegrees, out string why)
        {
            why = null;
            if (!planFacing.IsValid || planFacing.LengthSquared < 0.01f)
            {
                return null;
            }
            planFacing = planFacing.Normalized();
            string what = null;
            if (_turned && _planKey != int.MinValue && planKey != _planKey && _planFacing.IsValid && Degrees(planFacing, _planFacing) < KeepTurnDegrees)
            {
                // a new version facing about the same way: the local turn stands
                _planKey = planKey;
                _planFacing = planFacing;
                _turned = Degrees(_facing, planFacing) > 1f;
            }
            else if (planKey != _planKey || !_planFacing.IsValid || Vec2.DotProduct(planFacing, _planFacing) < 0.9998f)
            {
                if (_turned && _planKey != int.MinValue)
                {
                    what = "unface";
                    why = "plan v" + planKey.ToString(CultureInfo.InvariantCulture) + ": facing " + Dir(_facing) + "->" + Dir(planFacing);
                }
                _planKey = planKey;
                _planFacing = planFacing;
                _facing = planFacing;
                _turned = false;
                _pendingSince = float.NaN;
            }
            if (!mayTurn || mission == null || line == null || line.CountOfUnits <= 0)
            {
                _pendingSince = float.NaN;
                return what;
            }
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(line);
            EnemyConcentration.Result conc;
            if (!centre.IsValid || !EnemyConcentration.Find(mission, line, EnemyConcentration.Range, out conc, true))
            {
                _pendingSince = float.NaN;
                return what;
            }
            Vec2 to = conc.Centre - centre;
            float distance = to.Normalize();
            Vec2 facing = Facing;
            float off = Degrees(to, facing);
            float need = float.IsNaN(_pendingSince) ? TurnDegrees : TurnDegrees - HysteresisDegrees;
            if (distance <= MinDistance || off <= need)
            {
                _pendingSince = float.NaN;
                return what;
            }
            float offPlan = Degrees(to, planFacing);
            if (offPlan > maxOffPlanDegrees)
            {
                _pendingSince = float.NaN;
                return what;
            }
            if (float.IsNaN(_pendingSince))
            {
                _pendingSince = now;
                return what;
            }
            if (now - _pendingSince < DwellSeconds || now - _turnedAt < MinTurnSeconds)
            {
                return what;
            }
            // melee last (it walks the men's hit times): a fighting line never turns
            float melee = HoldLateCharge.MeleeShare(line);
            if (melee >= HoldLateCharge.ContactMeleeShare)
            {
                _pendingSince = float.NaN;
                return what;
            }
            Vec2 old = facing;
            _facing = to;
            _turned = offPlan > 1f;
            _turnedAt = now;
            _pendingSince = float.NaN;
            why = "facing " + Dir(old) + "->" + Dir(to) + " turn=" + Fmt0(off) + "deg plan+" + Fmt0(offPlan) + "deg dist=" + Fmt0(distance) + "m melee="
                + melee.ToString("0.00", CultureInfo.InvariantCulture) + " " + EnemyConcentration.Describe(conc);
            return "reface";
        }

        /// <summary>AI thread: the plan's own point while not turned (or the turned front is off walkable ground), else the turned point (cached).</summary>
        internal WorldPosition Point(WorldPosition planWorld, Vec2 planPoint, Vec2 planFacing, float depth, float now, out Vec2 point)
        {
            point = planPoint;
            if (!_turned || !planPoint.IsValid || !planFacing.IsValid || planFacing.LengthSquared < 0.01f || !_facing.IsValid)
            {
                return planWorld;
            }
            float half = MathF.Max(depth, 1f) * 0.5f;
            Vec2 pivot = planPoint - planFacing.Normalized() * half;
            Vec2 front = pivot + _facing * half;
            if (_world.IsValid && _worldPlanPoint.IsValid && _worldPlanPoint.DistanceSquared(planPoint) < 0.25f
                && (_worldPoint.Distance(front) <= RebuildDistance || now - _builtAt < RebuildSeconds))
            {
                point = _worldPoint;
                return _world;
            }
            BattleTerrain terrain = BattleTerrain.Current;
            if (terrain != null && !terrain.IsWalkable(front))
            {
                // keep the facing, stand at the plan's own point (the turned front would be off the ground)
                return planWorld;
            }
            WorldPosition target = planWorld;
            target.SetVec2MT(front);
            _world = target;
            _worldPoint = front;
            _worldPlanPoint = planPoint;
            _builtAt = now;
            point = front;
            return target;
        }

        /// <summary>Archer / cavalry reserve place of a turned line: the plan's place rotated round the line's pivot; the plan's own when not turned or the turned one is off walkable ground / near the edge. ONE SetVec2MT on a copy, cached. One per behavior, AI thread.</summary>
        internal sealed class TurnedPlace
        {
            private const float EdgeMin = 20f;

            private WorldPosition _world = WorldPosition.Invalid;
            private Vec2 _point = Vec2.Invalid;
            private Vec2 _source = Vec2.Invalid;
            private float _builtAt = float.NegativeInfinity;
            private bool _turned;

            internal void Reset()
            {
                _world = WorldPosition.Invalid;
                _point = Vec2.Invalid;
                _source = Vec2.Invalid;
                _builtAt = float.NegativeInfinity;
                _turned = false;
            }

            /// <summary>what = "turnPlace" / "unturnPlace" on a change, else null.</summary>
            internal WorldPosition Resolve(WorldPosition planWorld, Vec2 planPoint, Vec2 pivot, Vec2 planFacing, Vec2 facing, float now,
                out Vec2 point, out string what, out string why)
            {
                point = planPoint;
                what = null;
                why = null;
                Vec2 target = Vec2.Invalid;
                string refused = null;
                float turn = 0f;
                if (planPoint.IsValid && pivot.IsValid && planFacing.IsValid && facing.IsValid && planFacing.LengthSquared > 0.01f && facing.LengthSquared > 0.01f)
                {
                    Vec2 a = planFacing.Normalized();
                    Vec2 b = facing.Normalized();
                    float cos = MBMath.ClampFloat(Vec2.DotProduct(a, b), -1f, 1f);
                    if (cos < 0.9998f)
                    {
                        float sin = a.x * b.y - a.y * b.x;
                        Vec2 rel = planPoint - pivot;
                        target = pivot + new Vec2(rel.x * cos - rel.y * sin, rel.x * sin + rel.y * cos);
                        turn = Degrees(a, b);
                        BattleTerrain terrain = BattleTerrain.Current;
                        if (terrain != null && (!terrain.IsWalkable(target) || terrain.EdgeDistanceAt(target) < EdgeMin))
                        {
                            refused = "offGround";
                            target = Vec2.Invalid;
                        }
                    }
                }
                if (!target.IsValid)
                {
                    if (_turned)
                    {
                        _turned = false;
                        what = "unturnPlace";
                        why = (refused != null) ? refused + " turn=" + Fmt0(turn) + "deg" : "line faces the plan's way";
                    }
                    return planWorld;
                }
                if (!_turned)
                {
                    _turned = true;
                    what = "turnPlace";
                    why = "line turned " + Fmt0(turn) + "deg: place " + Fmt0(planPoint.Distance(target)) + "m round the line's pivot to " + Fmt0(target.x) + "," + Fmt0(target.y);
                }
                if (_world.IsValid && _source.IsValid && _source.DistanceSquared(planPoint) < 0.25f
                    && (_point.Distance(target) <= RebuildDistance || now - _builtAt < RebuildSeconds))
                {
                    point = _point;
                    return _world;
                }
                WorldPosition built = planWorld;
                built.SetVec2MT(target);
                _world = built;
                _point = target;
                _source = planPoint;
                _builtAt = now;
                point = target;
                return built;
            }
        }

        private static float Degrees(Vec2 a, Vec2 b)
        {
            float dot = MBMath.ClampFloat(Vec2.DotProduct(a, b), -1f, 1f);
            return (float)(System.Math.Acos(dot) * 180.0 / System.Math.PI);
        }

        private static string Dir(Vec2 v)
        {
            return v.IsValid ? v.x.ToString("0.00", CultureInfo.InvariantCulture) + "," + v.y.ToString("0.00", CultureInfo.InvariantCulture) : "-";
        }

        private static string Fmt0(float value)
        {
            return value.ToString("0", CultureInfo.InvariantCulture);
        }
    }
}
