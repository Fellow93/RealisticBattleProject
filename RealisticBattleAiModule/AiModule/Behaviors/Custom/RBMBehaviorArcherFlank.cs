using System;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    internal class RBMBehaviorArcherFlank : BehaviorComponent
    {

        public FormationAI.BehaviorSide FlankSide = FormationAI.BehaviorSide.Middle;

        /// <summary>AI thread.</summary>
        private readonly CoverSupport _glue = new CoverSupport("ArcherFlank", "spot");

        private bool _coverFacing;

        /// <summary>AI thread.</summary>
        private readonly FormationCohesion.Pull _cohesion = new FormationCohesion.Pull();

        /// <summary>Last ordered place. AI thread.</summary>
        private Vec2 _flankPoint = Vec2.Invalid;

        private const float FlankPointRebuild = 5f; // m

        /// <summary>Width as a Line of the current file count (the real width changes ~2.3x on Line / Loose, which would move the place). AI thread.</summary>
        private static float NominalLineWidth(Formation formation)
        {
            float diameter = formation.UnitDiameter;
            float interval = formation.Interval;
            float step = diameter + interval;
            if (step < 0.1f)
            {
                return formation.Width;
            }
            float files = MathF.Max(1f, (float)MathF.Round((formation.Width + interval) / step));
            float lineInterval = Formation.GetDefaultUnitInterval(formation.CalculateHasSignificantNumberOfMounted && formation.RidingOrder != RidingOrder.RidingOrderDismount,
                ArrangementOrder.GetUnitSpacingOf(ArrangementOrder.ArrangementOrderEnum.Line));
            return MathF.Max(0f, files * (diameter + lineInterval) - lineInterval);
        }

        internal const float CavalryBehindGap = 5f; // m behind the main's rear at kCav 1

        /// <summary>AI thread.</summary>
        private readonly CavalryBehindPlace _cavPlace = new CavalryBehindPlace("ArcherFlank", "flank");

        /// <summary>Front-centre place: the anchor's front-centre + side x lateral, slid by kCav toward its rear (lateral x (1 - k), back k x (depth + CavalryBehindGap)).</summary>
        internal static Vec2 FlankPlace(Formation ally, Vec2 side, float lateral, float kCav)
        {
            Vec2 back = ally.Direction;
            back = (back.IsValid && back.LengthSquared > 0.01f) ? back.Normalized() * (kCav * (ally.Depth + CavalryBehindGap)) : Vec2.Zero;
            return ally.CurrentPosition + side * (lateral * (1f - kCav)) - back;
        }

        private const float ApproachRangeMargin = 10f; // m beyond the missile range: still approaching
        private const float ApproachMoveMin = 10f; // m to the order point: walking

        /// <summary>
        /// AI thread, user rule 2026-10-30: an attacker's archers still out of shooting range face where the army advances - the anchor's
        /// facing, else toward their own move point - not the enemy. False otherwise (the caller keeps its enemy facing).
        /// </summary>
        internal static bool ApproachFacing(Formation archers, Formation anchor, Vec2 movePoint, out Vec2 facing)
        {
            facing = Vec2.Invalid;
            if (archers?.Team == null || archers.Team.Side != BattleSideEnum.Attacker || !archers.IsAIControlled)
            {
                return false;
            }
            Vec2 at = archers.CachedAveragePosition;
            Vec2 enemyAt = archers.QuerySystem.Team.MedianTargetFormationPosition.AsVec2;
            if (!at.IsValid || !enemyAt.IsValid || at.Distance(enemyAt) <= archers.QuerySystem.MaximumMissileRange + archers.Depth * 0.5f + ApproachRangeMargin)
            {
                return false;
            }
            Vec2 dir = (anchor != null && anchor != archers) ? anchor.Direction : Vec2.Invalid;
            if (!dir.IsValid || dir.LengthSquared < 0.01f)
            {
                if (!movePoint.IsValid || movePoint.Distance(at) <= ApproachMoveMin)
                {
                    return false;
                }
                dir = movePoint - at;
            }
            facing = dir.Normalized();
            return true;
        }

        private Vec2 StablePoint(Vec2 point)
        {
            if (!_flankPoint.IsValid || _flankPoint.DistanceSquared(point) > FlankPointRebuild * FlankPointRebuild)
            {
                _flankPoint = point;
            }
            return _flankPoint;
        }

        public RBMBehaviorArcherFlank(Formation formation)
            : base(formation)
        {
            _behaviorSide = formation.AI.Side;
            CalculateCurrentOrder();
            base.BehaviorCoherence = 0.5f;
            base.NavmeshlessTargetPositionPenalty = 1f;
        }

        protected override float GetAiWeight()
        {
            // the archer stand owns the defender's archer anchor (no stand-off, no cover steps)
            return RBMBehaviorArcherStand.Owns(base.Formation) ? 0f : 1000f;
        }

        protected override void OnBehaviorActivatedAux()
        {
            CalculateCurrentOrder();
            base.Formation.SetMovementOrder(base.CurrentOrder);
            // Line / Loose by the situation for a ranged formation (Utilities.ChooseRangedArrangement)
            if (!RBMAI.Utilities.TryApplyRangedArrangement(base.Formation, ArrangementOrder.ArrangementOrderEnum.Loose))
            {
                base.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLoose);
            }
            base.Formation.SetFacingOrder( FacingOrder.FacingOrderLookAtEnemy);
            base.Formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
            // ~4 ranks for foot archers (TrySetRangedWidth), else native Wide
            if (!RBMAI.Utilities.TrySetRangedWidth(base.Formation))
            {
                base.Formation.SetFormOrder(FormOrder.FormOrderWide);
            }
        }

        protected override void CalculateCurrentOrder()
        {
            _approachFacing = false;
            // user rule: defender archers approached by the enemy seek cover behind / beside their infantry
            if (CoverOrder())
            {
                return;
            }
            // SupportFire: once the frontlines connect, one place behind / beside the engaged main
            if (SupportOrder())
            {
                return;
            }
            WorldPosition position = base.Formation.CachedMedianPosition;
            Vec2 averagePosition = base.Formation.CachedAveragePosition;

            float flankRange = 10f;

            // position anchor (main melee infantry, else the strongest other AI foot formation); never itself
            Formation allyFormation = ArmyComposition.AnchorFor(base.Formation);

            WorldPosition medianTargetFormationPosition = base.Formation.QuerySystem.Team.MedianTargetFormationPosition;
            Vec2 enemyDirection = (medianTargetFormationPosition.AsVec2 - averagePosition).Normalized();
            // no infantry to flank: stand off 150 m on OUR side of the enemy (enemyDirection points from us to them)
            Vec2 noAllyStandOff = medianTargetFormationPosition.AsVec2 - enemyDirection * 150f;
            // backing off can leave the map: hold in place then
            WorldPosition standOffProbe = position;
            standOffProbe.SetVec2(noAllyStandOff);
            if (!Mission.Current.IsPositionInsideBoundaries(noAllyStandOff) || standOffProbe.GetNavMesh() == UIntPtr.Zero)
            {
                noAllyStandOff = position.AsVec2;
            }

            bool flankSide = _behaviorSide == FormationAI.BehaviorSide.Right || FlankSide == FormationAI.BehaviorSide.Right
                || _behaviorSide == FormationAI.BehaviorSide.Left || FlankSide == FormationAI.BehaviorSide.Left;
            // an enemy of melee cavalry: EITHER behind the main melee infantry OR the flank place (kCav, hysteresis)
            Vec2 behindMain = flankSide
                ? _cavPlace.Place(this, allyFormation, medianTargetFormationPosition.AsVec2, base.Formation.QuerySystem.MaximumMissileRange * 0.5f + base.Formation.Depth * 0.5f)
                : Vec2.Invalid;
            if (behindMain.IsValid)
            {
                Vec2 calcPosition = StablePoint(behindMain);
                position.SetVec2(calcPosition);
                if (!Mission.Current.IsPositionInsideBoundaries(calcPosition) || position.GetNavMesh() == UIntPtr.Zero)
                {
                    position.SetVec2((allyFormation != null) ? allyFormation.CurrentPosition - allyFormation.Direction * 10f : noAllyStandOff);
                }
            }
            else if (_behaviorSide == FormationAI.BehaviorSide.Right || FlankSide == FormationAI.BehaviorSide.Right)
            {
                if (allyFormation != null)
                {
                    Vec2 calcPosition = StablePoint(FlankPlace(allyFormation, allyFormation.Direction.RightVec().Normalized(), allyFormation.Width * 0.5f + NominalLineWidth(base.Formation) * 0.5f + flankRange, 0f));

                    position.SetVec2(calcPosition);
                    if (!Mission.Current.IsPositionInsideBoundaries(calcPosition) || position.GetNavMesh() == UIntPtr.Zero)
                    {
                        calcPosition = allyFormation.CurrentPosition;
                        calcPosition -= allyFormation.Direction * 5f;
                        position.SetVec2(calcPosition);
                    }
                }
                else
                {
                    position.SetVec2(noAllyStandOff);
                }
            }
            else if (_behaviorSide == FormationAI.BehaviorSide.Left || FlankSide == FormationAI.BehaviorSide.Left)
            {
                if (allyFormation != null)
                {
                    Vec2 calcPosition = StablePoint(FlankPlace(allyFormation, allyFormation.Direction.LeftVec().Normalized(), allyFormation.Width * 0.5f + NominalLineWidth(base.Formation) * 0.5f + flankRange, 0f));
                    position.SetVec2(calcPosition);
                    if (!Mission.Current.IsPositionInsideBoundaries(calcPosition) || position.GetNavMesh() == UIntPtr.Zero)
                    {
                        calcPosition = allyFormation.CurrentPosition;
                        calcPosition -= allyFormation.Direction * 10f;
                        position.SetVec2(calcPosition);
                    }
                }
                else
                {
                    position.SetVec2(noAllyStandOff);
                }
            }
            else
            {
                if (allyFormation != null)
                {
                    position = allyFormation.CachedMedianPosition;
                }
                else
                {
                    position.SetVec2(noAllyStandOff);
                }
            }

            // archer spot (TerrainSpots): held until the enemy closes in or the infantry leaves it behind
            FormationAI.BehaviorSide spotSide = (_behaviorSide == FormationAI.BehaviorSide.Right || FlankSide == FormationAI.BehaviorSide.Right) ? FormationAI.BehaviorSide.Right
                : (_behaviorSide == FormationAI.BehaviorSide.Left || FlankSide == FormationAI.BehaviorSide.Left) ? FormationAI.BehaviorSide.Left
                : FormationAI.BehaviorSide.BehaviorSideNotSet;
            bool onSpot = false;
            if (allyFormation != null && spotSide != FormationAI.BehaviorSide.BehaviorSideNotSet)
            {
                WorldPosition spotPosition;
                if (TerrainSpots.TryHoldSpot(this, "ArcherFlank", position.AsVec2, spotSide, allyFormation, 0f, Vec2.Invalid, float.NaN, float.NaN, out spotPosition))
                {
                    position = spotPosition;
                    onSpot = true;
                }
            }
            else
            {
                TerrainSpots.Release(this, (allyFormation == null) ? "noAlly" : "noFlankSide");
            }
            Formation formation = base.Formation;
            if (!onSpot && formation.AI != null && formation.AI.ActiveBehavior == this)
            {
                // user rule: never more than the cohesion limit from the main infantry; cached, rebuilt when the place moves > 5 m
                Vec2 point;
                string what;
                string why;
                position = _cohesion.Resolve(formation, position, enemyDirection, FormationCohesion.Kind.Auto, null, out point, out what, out why);
                if (what != null)
                {
                    TerrainSpots.EnqueueBehavior(formation.Team, formation, Mission.Current.CurrentTime, "ArcherFlank", what, "cohesion", why);
                }
            }
            base.CurrentOrder = MovementOrder.MovementOrderMove(position);
            Vec2 approach;
            _approachFacing = ApproachFacing(formation, allyFormation, position.AsVec2, out approach);
            Vec2 wanted = _approachFacing ? approach : enemyDirection;
            float angle = CurrentFacingOrder.GetDirection(Formation).AngleBetween(wanted);
            if (angle > 0.2f || angle < -0.2f)
            {
                base.CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(wanted);
            }
        }

        /// <summary>AI thread: CurrentFacingOrder is the approach facing (ApproachFacing), applied in TickOccasionally.</summary>
        private bool _approachFacing;

        public override void TickOccasionally()
        {
            CalculateCurrentOrder();
            base.Formation.SetMovementOrder(base.CurrentOrder);
            if (_glue.Covering || _glue.Supporting || _approachFacing)
            {
                // face the threat / engaged enemy / the way the army advances; back to LookAtEnemy when that ends
                base.Formation.SetFacingOrder(CurrentFacingOrder);
                _coverFacing = true;
            }
            else if (_coverFacing)
            {
                _coverFacing = false;
                base.Formation.SetFacingOrder(FacingOrder.FacingOrderLookAtEnemy);
            }
            // under cover / in support fire a fixed post (Line unless fire / ground / cavalry say otherwise)
            bool post = _glue.Covering || _glue.Supporting;
            RBMAI.Utilities.TryApplyRangedArrangement(base.Formation, post ? ArrangementOrder.ArrangementOrderEnum.Line : ArrangementOrder.ArrangementOrderEnum.Loose, post);
        }

        /// <summary>The support-fire state for the decision layer ("support behind"), else null. Any thread.</summary>
        internal string SupportLabel => _glue.Support.Label;

        /// <summary>AI thread.</summary>
        private bool SupportOrder()
        {
            Formation formation = base.Formation;
            Mission mission = Mission.Current;
            if (formation == null || mission == null)
            {
                return false;
            }
            FormationAI.BehaviorSide side = (_behaviorSide == FormationAI.BehaviorSide.Right || FlankSide == FormationAI.BehaviorSide.Right) ? FormationAI.BehaviorSide.Right
                : (_behaviorSide == FormationAI.BehaviorSide.Left || FlankSide == FormationAI.BehaviorSide.Left) ? FormationAI.BehaviorSide.Left
                : FormationAI.BehaviorSide.BehaviorSideNotSet;
            WorldPosition target;
            Vec2 facing;
            switch (_glue.SupportStep(this, formation, null, Vec2.Invalid, Vec2.Invalid, side, mission.CurrentTime, out target, out facing))
            {
                case CoverSupport.Step.Off:
                    return false;
                case CoverSupport.Step.Ended:
                    base.CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                    return false;
                case CoverSupport.Step.Started:
                    TerrainSpots.Release(this, "support");
                    _cohesion.Reset();
                    break;
            }
            base.CurrentOrder = MovementOrder.MovementOrderMove(target);
            base.CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(facing);
            return true;
        }

        /// <summary>AI thread.</summary>
        private bool CoverOrder()
        {
            Formation formation = base.Formation;
            Mission mission = Mission.Current;
            if (formation == null || mission == null)
            {
                return false;
            }
            WorldPosition target;
            Vec2 facing;
            switch (_glue.CoverStep(this, formation, false, mission.CurrentTime, out target, out facing))
            {
                case CoverSupport.Step.Off:
                    return false;
                case CoverSupport.Step.Ended:
                    base.CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                    return false;
                case CoverSupport.Step.Started:
                    TerrainSpots.Release(this, "cover");
                    break;
            }
            base.CurrentOrder = MovementOrder.MovementOrderMove(target);
            base.CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(facing);
            return true;
        }

        public override void OnBehaviorCanceled()
        {
            TerrainSpots.Release(this, "behaviorEnded");
            _glue.ReleaseCover(base.Formation, "behaviorEnded");
            _glue.ReleaseSupport(base.Formation, "behaviorEnded");
            _coverFacing = false;
            _cohesion.Reset();
            _flankPoint = Vec2.Invalid;
            base.OnBehaviorCanceled();
        }

        public override TextObject GetBehaviorString()
        {
            string name = GetType().Name;
            return GameTexts.FindText("str_formation_ai_sergeant_instruction_behavior_text", name);
        }
    }

    /// <summary>ENEMY MELEE CAVALRY (kCav), user decision: an either/or archer place with hysteresis - behind the team's main melee infantry from kCav
    /// ArmyComposition.CavalryBehindOn, the behavior's normal place under CavalryBehindOff; losing the main keeps the last behind point HoldSeconds. One instance per behavior, AI thread.</summary>
    internal sealed class CavalryBehindPlace
    {
        private const float On = ArmyComposition.CavalryBehindOn;
        private const float Off = ArmyComposition.CavalryBehindOff;
        internal const float HoldSeconds = 6f;

        private readonly string _behavior;
        private readonly string _logPoint;
        private bool _on;
        private Vec2 _point = Vec2.Invalid;
        private float _lostAt = float.NaN;

        internal CavalryBehindPlace(string behavior, string logPoint)
        {
            _behavior = behavior;
            _logPoint = logPoint;
        }

        /// <summary>The behind-main place (front-centre) while the choice holds, else Vec2.Invalid. Pulled toward enemyAt by up to CavalryBehindGap to stay within reach.</summary>
        internal Vec2 Place(BehaviorComponent owner, Formation ally, Vec2 enemyAt, float reach)
        {
            Formation formation = owner?.Formation;
            Team team = formation?.Team;
            if (team == null)
            {
                return Vec2.Invalid;
            }
            float kCav = ArmyComposition.KCav(team);
            bool want = _on ? kCav > Off : kCav >= On;
            bool allyIsMain = ally != null && ally == FormationCohesion.TeamMain(team);
            float now = Mission.Current.CurrentTime;
            if (want && allyIsMain)
            {
                Vec2 p = RBMBehaviorArcherFlank.FlankPlace(ally, Vec2.Zero, 0f, 1f);
                if (enemyAt.IsValid)
                {
                    float d = p.Distance(enemyAt);
                    if (d > reach && d > 0.1f)
                    {
                        p += (enemyAt - p) * (MathF.Min(d - reach, RBMBehaviorArcherFlank.CavalryBehindGap) / d);
                    }
                }
                if (!_on)
                {
                    Log(owner, "behindMain on kCav=" + ArmyComposition.F2(kCav) + ">=" + ArmyComposition.F2(On) + " behind " + TerrainCavalry.FormationName(ally));
                }
                _on = true;
                _point = p;
                _lostAt = float.NaN;
                return p;
            }
            if (!_on)
            {
                return Vec2.Invalid;
            }
            if (want && _point.IsValid)
            {
                // the main is gone: the last point a few seconds, not one jump out to the flank
                if (float.IsNaN(_lostAt))
                {
                    _lostAt = now;
                }
                if (now - _lostAt < HoldSeconds)
                {
                    return _point;
                }
            }
            _on = false;
            _point = Vec2.Invalid;
            _lostAt = float.NaN;
            Log(owner, "behindMain off " + (want ? "mainLost" : "kCav=" + ArmyComposition.F2(kCav) + "<=" + ArmyComposition.F2(Off)));
            return Vec2.Invalid;
        }

        private void Log(BehaviorComponent owner, string why)
        {
            Formation formation = owner.Formation;
            if (formation?.AI != null && formation.AI.ActiveBehavior == owner)
            {
                TerrainSpots.EnqueueBehavior(formation.Team, formation, Mission.Current.CurrentTime, _behavior, "kCavPlace", _logPoint, why);
                ArmyComposition.Note(formation.Team, formation, ArmyComposition.KindGate, "kCavPlace", _behavior + " " + why);
            }
        }
    }
}