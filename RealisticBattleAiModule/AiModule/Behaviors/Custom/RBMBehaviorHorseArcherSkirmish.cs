using System;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    public class RBMBehaviorHorseArcherSkirmish : BehaviorComponent
    {
        private struct Ellipse
        {
            private readonly Vec2 _center;

            private readonly float _radius;

            private readonly float _halfLength;

            private readonly Vec2 _direction;

            public Ellipse(Vec2 center, float radius, float halfLength, Vec2 direction)
            {
                _center = center;
                _radius = radius;
                _halfLength = halfLength;
                _direction = direction;
            }

            public Vec2 GetTargetPos(Vec2 position, float distance)
            {
                Vec2 vec = _direction.LeftVec();
                Vec2 vec2 = _center + vec * _halfLength;
                Vec2 vec3 = _center - vec * _halfLength;
                Vec2 vec4 = position - _center;
                bool flag = vec4.Normalized().DotProduct(_direction) > 0f;
                Vec2 vec5 = vec4.DotProduct(vec) * vec;
                bool flag2 = vec5.Length < _halfLength;
                bool flag3 = true;
                if (flag2)
                {
                    position = _center + vec5 + _direction * (_radius * (float)(flag ? 1 : (-1)));
                }
                else
                {
                    flag3 = vec5.DotProduct(vec) > 0f;
                    Vec2 vec6 = (position - (flag3 ? vec2 : vec3)).Normalized();
                    position = (flag3 ? vec2 : vec3) + vec6 * _radius;
                }
                Vec2 vec7 = _center + vec5;
                float num = (float)Math.PI * 2f * _radius;
                while (distance > 0f)
                {
                    if (flag2 && flag)
                    {
                        float num2 = (((vec2 - vec7).Length < distance) ? (vec2 - vec7).Length : distance);
                        position = vec7 + (vec2 - vec7).Normalized() * num2;
                        position += _direction * _radius;
                        distance -= num2;
                        flag2 = false;
                        flag3 = true;
                    }
                    else if (!flag2 && flag3)
                    {
                        Vec2 v = (position - vec2).Normalized();
                        float num3 = TaleWorlds.Library.MathF.Acos(MBMath.ClampFloat(_direction.DotProduct(v), -1f, 1f));
                        float num4 = (float)Math.PI * 2f * (distance / num);
                        float num5 = ((num3 + num4 < (float)Math.PI) ? (num3 + num4) : ((float)Math.PI));
                        float num6 = (num5 - num3) / (float)Math.PI * (num / 2f);
                        Vec2 direction = _direction;
                        direction.RotateCCW(num5);
                        position = vec2 + direction * _radius;
                        distance -= num6;
                        flag2 = true;
                        flag = false;
                    }
                    else if (flag2)
                    {
                        float num7 = (((vec3 - vec7).Length < distance) ? (vec3 - vec7).Length : distance);
                        position = vec7 + (vec3 - vec7).Normalized() * num7;
                        position -= _direction * _radius;
                        distance -= num7;
                        flag2 = false;
                        flag3 = false;
                    }
                    else
                    {
                        Vec2 vec8 = (position - vec3).Normalized();
                        float num8 = TaleWorlds.Library.MathF.Acos(MBMath.ClampFloat(_direction.DotProduct(vec8), -1f, 1f));
                        float num9 = (float)Math.PI * 2f * (distance / num);
                        float num10 = ((num8 - num9 > 0f) ? (num8 - num9) : 0f);
                        float num11 = num8 - num10;
                        float num12 = num11 / (float)Math.PI * (num / 2f);
                        Vec2 vec9 = vec8;
                        vec9.RotateCCW(num11);
                        position = vec3 + vec9 * _radius;
                        distance -= num12;
                        flag2 = true;
                        flag = true;
                    }
                }
                return position;
            }
        }

        private bool _isEnemyReachable = true;
        private bool _engaging = true;

        public RBMBehaviorHorseArcherSkirmish(Formation formation)
            : base(formation)
        {
            CalculateCurrentOrder();
            base.BehaviorCoherence = 0.5f;
        }

        protected override void CalculateCurrentOrder()
        {
            WorldPosition position = base.Formation.CachedMedianPosition;
            Formation targetFormation = RBMAI.Utilities.FindSignificantEnemy(base.Formation, true, true, false, false, false, true);
            _isEnemyReachable = targetFormation != null && (!(base.Formation.Team.TeamAI is TeamAISiegeComponent) || !TeamAISiegeComponent.IsFormationInsideCastle(targetFormation, includeOnlyPositionedUnits: false));
            if (!_isEnemyReachable)
            {
                position.SetVec2(base.Formation.CachedAveragePosition);
            }
            else
            {
                bool num = (base.Formation.QuerySystem.AverageAllyPosition - base.Formation.Team.QuerySystem.AverageEnemyPosition).LengthSquared <= 3600f;
                bool engaging = _engaging;
                engaging = _engaging = num || ((!_engaging) ? ((base.Formation.CachedAveragePosition - base.Formation.QuerySystem.AverageAllyPosition).LengthSquared <= 3600f) : (!(base.Formation.QuerySystem.UnderRangedAttackRatio * 0.5f > base.Formation.QuerySystem.MakingRangedAttackRatio)));
                if (_engaging)
                {
                    if (targetFormation != null)
                    {
                        float distance = 60f;
                        if (!base.Formation.QuerySystem.IsRangedCavalryFormation)
                        {
                            distance = 30f;
                        }
                        Ellipse ellipse = new Ellipse(targetFormation.CachedMedianPosition.AsVec2, distance, targetFormation.Width * 0.5f, targetFormation.Direction);
                        position.SetVec2(ellipse.GetTargetPos(Formation.CachedAveragePosition, 35f));
                        CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(targetFormation.CachedAveragePosition);
                        //Formation.SetFacingOrder( FacingOrder.FacingOrderLookAtDirection((targetFormation.CachedMedianPosition.AsVec2 - position.AsVec2).Normalized());
                    }
                }
                else
                {
                    position = new WorldPosition(Mission.Current.Scene, new Vec3(base.Formation.QuerySystem.AverageAllyPosition.x, base.Formation.QuerySystem.AverageAllyPosition.y, base.Formation.Team.GetMedianPosition(base.Formation.Team.GetAveragePosition()).GetNavMeshZ() + 100f));
                }
            }
            if (position.GetNavMesh() == UIntPtr.Zero || !Mission.Current.IsPositionInsideBoundaries(position.AsVec2))
            {
                position = base.Formation.CachedMedianPosition;
                CurrentOrder = MovementOrder.MovementOrderMove(position);
            }
            else
            {
                CurrentOrder = MovementOrder.MovementOrderMove(position);
            }
        }

        public override void TickOccasionally()
        {
            CalculateCurrentOrder();
            Formation.SetMovementOrder(CurrentOrder);
            Formation.SetFacingOrder( CurrentFacingOrder);
        }

        protected override void OnBehaviorActivatedAux()
        {
            CalculateCurrentOrder();
            base.Formation.SetMovementOrder(base.CurrentOrder);
            base.Formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLine);
            base.Formation.SetFacingOrder( FacingOrder.FacingOrderLookAtEnemy);
            base.Formation.SetFormOrder( FormOrder.FormOrderDeep);
            base.Formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
        }

        protected override float GetAiWeight()
        {
            if (base.Formation != null && base.Formation.QuerySystem.IsCavalryFormation)
            {
                if (RBMAI.Utilities.CheckIfMountedSkirmishFormation(base.Formation, 0.6f))
                {
                    return 5f;
                }
                else
                {
                    return 0f;
                }
            }
            else if (base.Formation != null && base.Formation.QuerySystem.IsRangedCavalryFormation)
            {
                Formation enemyFormation = RBMAI.Utilities.FindSignificantEnemy(base.Formation, false, false, true, false, false);
                if (enemyFormation != null && enemyFormation.QuerySystem.IsCavalryFormation && base.Formation.CachedMedianPosition.AsVec2.Distance(enemyFormation.CachedMedianPosition.AsVec2) < 55f && enemyFormation.CountOfUnits >= base.Formation.CountOfUnits * 0.5f)
                {
                    return 1000f;
                }
                if (!_isEnemyReachable)
                {
                    return 0.01f;
                }
                return 1000f;
            }
            else
            {
                int countOfSkirmishers = 0;
                base.Formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                {
                    if (RBMAI.Utilities.CheckIfSkirmisherAgent(agent, 1))
                    {
                        countOfSkirmishers++;
                    }
                });
                if (countOfSkirmishers / base.Formation.CountOfUnits > 0.6f)
                {
                    return 1f;
                }
                else
                {
                    return 0f;
                }
            }
        }
    }
}