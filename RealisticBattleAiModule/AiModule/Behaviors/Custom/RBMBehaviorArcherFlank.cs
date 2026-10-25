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
        private readonly CoverSupport _glue = new CoverSupport(CoverSupport.LogQueue.Spots, "ArcherFlank");

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
            return 1000f;
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

            // main melee infantry (AI- or player-led), else the nearest other own infantry; never itself
            Formation allyFormation = FormationCohesion.MainInfantry(base.Formation)
                ?? RBMAI.Utilities.FindSignificantAlly(base.Formation, true, false, false, false, false);

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

            if (_behaviorSide == FormationAI.BehaviorSide.Right || FlankSide == FormationAI.BehaviorSide.Right)
            {
                if (allyFormation != null)
                {
                    Vec2 calcPosition = StablePoint(allyFormation.CurrentPosition + allyFormation.Direction.RightVec().Normalized() * (allyFormation.Width * 0.5f + NominalLineWidth(base.Formation) * 0.5f + flankRange));

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
                    Vec2 calcPosition = StablePoint(allyFormation.CurrentPosition + allyFormation.Direction.LeftVec().Normalized() * (allyFormation.Width * 0.5f + NominalLineWidth(base.Formation) * 0.5f + flankRange));
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
            float angle = CurrentFacingOrder.GetDirection(Formation).AngleBetween(enemyDirection);
            if (angle > 0.2f || angle < -0.2f)
            {
                base.CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(enemyDirection);
            }
        }

        public override void TickOccasionally()
        {
            CalculateCurrentOrder();
            base.Formation.SetMovementOrder(base.CurrentOrder);
            if (_glue.Covering || _glue.Supporting)
            {
                // face the threat / engaged enemy; back to LookAtEnemy when that ends
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
}