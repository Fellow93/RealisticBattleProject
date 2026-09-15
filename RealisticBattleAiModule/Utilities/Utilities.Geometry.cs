using RBMConfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ArmorComponent;
using static TaleWorlds.Core.ItemObject;

namespace RBMAI
{
    public static partial class Utilities
    {

        public static float sign(Vec2 p1, Vec2 p2, Vec2 p3)
        {
            return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        }

        public static bool PointInTriangle(Vec2 pt, Vec2 v1, Vec2 v2, Vec2 v3)
        {
            float d1, d2, d3;
            bool has_neg, has_pos;

            d1 = sign(pt, v1, v2);
            d2 = sign(pt, v2, v3);
            d3 = sign(pt, v3, v1);

            has_neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            has_pos = (d1 > 0) || (d2 > 0) || (d3 > 0);

            return !(has_neg && has_pos);
        }

        public static IEnumerable<Agent> CountSoldiersInPolygon(Formation formation, Vec2[] polygon)
        {
            List<Agent> enemyAgents = new List<Agent>();
            int result = 0;
            foreach (Team team in Mission.Current.Teams.ToList())
            {
                if (team.IsEnemyOf(formation.Team))
                {
                    foreach (Formation enemyFormation in team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList())
                    {
                        formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                        {
                            if (IsPointInPolygon(polygon, agent.Position.AsVec2))
                            {
                                result++;
                                enemyAgents.Add(agent);
                            }
                        });
                    }
                }
            }
            return enemyAgents;
        }

        public static bool IsPointInPolygon(Vec2[] polygon, Vec2 testPoint)
        {
            //bool result = false;
            //int j = polygon.Count() - 1;
            //for (int i = 0; i < polygon.Count(); i++)
            //{
            //    if (polygon[i].Y < testPoint.Y && polygon[j].Y >= testPoint.Y || polygon[j].Y < testPoint.Y && polygon[i].Y >= testPoint.Y)
            //    {
            //        if (polygon[i].X + (testPoint.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) * (polygon[j].X - polygon[i].X) < testPoint.X)
            //        {
            //            result = !result;
            //        }
            //    }
            //    j = i;
            //}
            //return result;
            Vec2 p1, p2;
            bool inside = false;

            if (polygon.Length < 3)
            {
                return inside;
            }

            var oldPoint = new Vec2(
                polygon[polygon.Length - 1].X, polygon[polygon.Length - 1].Y);

            for (int i = 0; i < polygon.Length; i++)
            {
                var newPoint = new Vec2(polygon[i].X, polygon[i].Y);

                if (newPoint.X > oldPoint.X)
                {
                    p1 = oldPoint;
                    p2 = newPoint;
                }
                else
                {
                    p1 = newPoint;
                    p2 = oldPoint;
                }

                if ((newPoint.X < testPoint.X) == (testPoint.X <= oldPoint.X)
                    && (testPoint.Y - (long)p1.Y) * (p2.X - p1.X)
                    < (p2.Y - (long)p1.Y) * (testPoint.X - p1.X))
                {
                    inside = !inside;
                }

                oldPoint = newPoint;
            }

            return inside;
        }
    }
}
