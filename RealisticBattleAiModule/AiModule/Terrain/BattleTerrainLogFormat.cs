using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RBMAI
{
    /// <summary>Formats a BattleTerrain snapshot into the terrain log's TERRAIN / TERRAIN_* lines. Pure formatting; fields are documented in docs/terrain-log-format.md.</summary>
    internal static class BattleTerrainLogFormat
    {
        /// <summary>The TERRAIN line's fields (after "t=..\tTERRAIN\tsrc=..\t").</summary>
        public static string Base(BattleTerrain t)
        {
            BattleTerrain.SideInfo atk = t.Attacker;
            BattleTerrain.SideInfo def = t.Defender;
            StringBuilder sb = new StringBuilder();
            sb.Append("scene=").Append(t.Scene ?? "-");
            sb.Append("\tmen=").Append(atk.Men).Append('/').Append(def.Men);
            sb.Append(" dist=").Append(Fmt(t.Distance));
            sb.Append(" dz=").Append(Fmt(t.HeightDifference));
            sb.Append("\tfrontage atk=").Append(Fmt(atk.Frontage)).Append(" def=").Append(Fmt(def.Frontage));
            sb.Append("\tmap=").Append(Fmt(t.MapWidth)).Append('x').Append(Fmt(t.MapLength));
            sb.Append(" bounds=").Append(t.Bounds);
            sb.Append(" step=").Append(Fmt(t.GridStep)).Append(" cells=").Append(t.GridCells);
            sb.Append("\tfield=").Append(Fmt(t.FieldWidth));
            sb.Append(' ').Append(Area(t.Field));
            sb.Append("\tbuildMs=").Append(t.BuildMs)
              .Append(" clearMs=").Append(Fmt(t.ClearMs))
              .Append(" edgeMs=").Append(Fmt(t.EdgeMs))
              .Append(" mainMs=").Append(t.MainMs)
              .Append(" rayMs=").Append(t.RayMs)
              .Append(" bgMs=").Append(t.BackgroundMs)
              .Append(" wallMs=").Append(t.WallMs);
            sb.Append("\tgatherT=").Append(Fmt(t.ComputedAt)).Append(" publishT=").Append(Fmt(t.PublishedAt));
            return sb.ToString();
        }

        /// <summary>The TERRAIN_* lines, in writing order.</summary>
        public static List<string> Groups(BattleTerrain t)
        {
            return new List<string> { Space(t), Open(t), Mounted(t), Sight(t), Water(t) };
        }

        /// <summary>The TERRAIN_WATER line: source of the grid's deep water / bridges versus the blocker's grid (m2).</summary>
        private static string Water(BattleTerrain t)
        {
            Line line = new Line("TERRAIN_WATER");
            BattleTerrainRead.WaterRead w = t.WaterRead;
            if (w == null)
            {
                line.Add("src", "-");
                return line.Text();
            }
            float cell = t.GridStep * t.GridStep;
            float blockerCell = w.BlockerStep * w.BlockerStep;
            bool blocker = w.BlockerStep > 0f;
            line.Add("src", w.Source).Add("step", t.GridStep).Add("blockerStep", blocker ? w.BlockerStep : float.NaN);
            line.Add("deepM2", Round(w.DeepCells * cell)).Add("blockerDeepM2", blocker ? Round(w.BlockerDeepCells * blockerCell) : float.NaN)
                .Add("probeDeepM2", Round(w.ProbeDeepCells * cell));
            line.Add("bridgeM2", Round(w.BridgeCells * cell)).Add("blockerBridgeM2", blocker ? Round(w.BlockerBridgeCells * blockerCell) : float.NaN)
                .Add("probeBridgeM2", Round(w.ProbeBridgeCells * cell));
            line.Add("deepCells", w.DeepCells).Add("bridgeCells", w.BridgeCells).Add("blockerBridgeCells", w.BlockerBridgeCells)
                .Add("probeBridgeCells", w.ProbeBridgeCells);
            line.Add("deepDisagree", w.DeepDisagree).Add("deckMissing", w.DeckMissing).Add("deckFacesOff", w.DeckFacesOff)
                .Add("deckShallow", w.DeckShallow.ToString(CultureInfo.InvariantCulture) + "/" + w.DeckShallowBridge.ToString(CultureInfo.InvariantCulture));
            return line.Text();
        }

        /// <summary>The FEATURES line, then one FEATURE line per feature (kind order, most important first).</summary>
        public static List<string> Features(BattleTerrain t)
        {
            List<string> lines = new List<string>();
            BattleTerrain.FeatureMapInfo m = t.FeatureMap;
            Line head = new Line("FEATURES");
            foreach (BattleTerrain.FeatureKind kind in (BattleTerrain.FeatureKind[])System.Enum.GetValues(typeof(BattleTerrain.FeatureKind)))
            {
                if (kind == BattleTerrain.FeatureKind.Woods)
                {
                    head.Add(BattleTerrain.FeatureKindName(kind), WoodsCount(t.Woods));
                    continue;
                }
                head.Add(BattleTerrain.FeatureKindName(kind), t.Of(kind).Count);
            }
            head.Add("total", t.Features.Count).Add("paths", m.Paths).Add("expanded", m.Expanded).Add("stepMs", StepMs(m.StepMs));
            head.Add("droppedNearEdge", Counts(m.DroppedNearEdge));
            lines.Add(head.Finish(m));
            foreach (BattleTerrain.Feature feature in t.Features)
            {
                lines.Add(Feature(feature));
            }
            return lines;
        }

        /// <summary>One FEATURE line: common fields then the kind's own.</summary>
        private static string Feature(BattleTerrain.Feature x)
        {
            Line line = new Line("FEATURE");
            line.Add("id", x.Id).Add("kind", BattleTerrain.FeatureKindName(x.Kind));
            line.Add("pos", Point(x.Position));
            line.Add("area", Round(x.Area));
            line.Add("size", (float.IsNaN(x.Length) && float.IsNaN(x.Width)) ? "-" : Fmt(Round(x.Length)) + "x" + Fmt(Round(x.Width)));
            line.Add("dAtk", Relation(x.Attacker)).Add("dDef", Relation(x.Defender));
            line.Add("between", Flag(x.Between)).Add("side", x.Side.ToString().ToLowerInvariant());
            line.Add("imp", x.Importance);
            if (x is BattleTerrain.HillFeature hill)
            {
                line.Add("h", hill.Height).Add("crest", hill.Crest);
                line.Add("tAtk", hill.AttackerSeconds).Add("tDef", hill.DefenderSeconds);
                line.Add("first", ArmyName(hill.First));
            }
            else if (x is BattleTerrain.CrestFeature crest)
            {
                line.Add("at", crest.At).Add("rise", crest.Rise).Add("h", crest.Height);
            }
            else if (x is BattleTerrain.ChokeFeature choke)
            {
                line.Add("ratio", choke.Ratio).Add("at", choke.At).Add("src", ChokeSources(choke.Sources));
                line.Add("exitOf", ArmyName(choke.ExitOf));
                line.Add("type", choke.Type.ToString().ToLowerInvariant()).Add("boundedBy", choke.BoundedBy.ToString().ToLowerInvariant());
                line.Add("width", choke.Width).Add("length", choke.Length);
                line.Add("line", choke.LineStart.IsValid && choke.LineEnd.IsValid ? Point(choke.LineStart) + ".." + Point(choke.LineEnd) : "-");
                line.Add("mouths", choke.MouthA.IsValid && choke.MouthB.IsValid ? Point(choke.MouthA) + ".." + Point(choke.MouthB) : "-");
                line.Add("sides", ChokeSides(choke));
                line.Add("separates", Flag(choke.Separates)).Add("onRoute", Flag(choke.OnRoute));
                line.Add("wet", choke.WetShare).Add("water", choke.WaterId ?? "-").Add("ford", choke.FordId ?? "-");
            }
            else if (x is BattleTerrain.OpenFieldFeature open)
            {
                line.Add("maxClear", open.MaxClear).Add("armies", ArmyName(open.Armies));
                AreaFields(line, open);
            }
            else if (x is BattleTerrain.FlankFeature flank)
            {
                line.Add("army", ArmyName(flank.Army)).Add("own", flank.OwnLeft ? "left" : "right");
                line.Add("end", EndName(flank.End)).Add("endDist", flank.EndDistance).Add("spare", flank.Spare);
                line.Add("enemyRoute", Flank(flank.EnemyRoute));
            }
            else if (x is BattleTerrain.ObstacleFeature obstacle)
            {
                line.Add("cover", obstacle.Cover.ToString().ToLowerInvariant());
                if (obstacle.Source != BattleTerrain.AreaSource.None)
                {
                    line.Add("src", obstacle.Source.ToString().ToLowerInvariant());
                }
                if (!float.IsNaN(obstacle.MeanSlope))
                {
                    line.Add("slope", obstacle.MeanSlope);
                }
                if (!float.IsNaN(obstacle.RegionRadius))
                {
                    line.Add("radius", obstacle.RegionRadius);
                }
                AreaFields(line, obstacle);
            }
            else if (x is BattleTerrain.WaterFeature water)
            {
                line.Add("touchesField", Flag(water.TouchesField)).Add("crosses", Flag(water.CrossesAxis)).Add("crossAt", water.CrossAt);
                AreaFields(line, water);
            }
            else if (x is BattleTerrain.FordFeature ford)
            {
                line.Add("onPath", Flag(ford.OnPath)).Add("atDeep", Flag(ford.AtDeepWater)).Add("water", ford.WaterId ?? "-");
                AreaFields(line, ford);
            }
            return line.Text();
        }

        /// <summary>"N (map:a density:b both:c)" (+ " rays:d" when any): the woods count by source.</summary>
        private static string WoodsCount(IReadOnlyList<BattleTerrain.ObstacleFeature> woods)
        {
            int map = 0, density = 0, both = 0, rays = 0;
            foreach (BattleTerrain.ObstacleFeature w in woods)
            {
                switch (w.Source)
                {
                    case BattleTerrain.AreaSource.Map:
                        map++;
                        break;
                    case BattleTerrain.AreaSource.Density:
                        density++;
                        break;
                    case BattleTerrain.AreaSource.Both:
                        both++;
                        break;
                    default:
                        rays++;
                        break;
                }
            }
            return woods.Count.ToString(CultureInfo.InvariantCulture) + " (map:" + map.ToString(CultureInfo.InvariantCulture)
                + " density:" + density.ToString(CultureInfo.InvariantCulture) + " both:" + both.ToString(CultureInfo.InvariantCulture)
                + ((rays > 0) ? " rays:" + rays.ToString(CultureInfo.InvariantCulture) : "") + ")";
        }

        /// <summary>An area feature's nearest point to the A->D segment and that distance.</summary>
        private static void AreaFields(Line line, BattleTerrain.AreaFeature x)
        {
            line.Add("axisDist", x.AxisDistance).Add("near", Point(x.ClosestPoint));
        }

        /// <summary>"x,y" rounded to whole metres.</summary>
        private static string Point(TaleWorlds.Library.Vec2 p)
        {
            return Fmt(Round(p.x)) + "," + Fmt(Round(p.y));
        }

        /// <summary>"distance m@bearing deg" from one army ("230m@-12deg"); "-" when missing.</summary>
        private static string Relation(BattleTerrain.FeatureRelation r)
        {
            return (r == null) ? "-" : Fmt(Round(r.Distance)) + "m@" + Fmt((float)System.Math.Round(r.Bearing)) + "deg";
        }

        /// <summary>"atk" | "def" | "both" | "none".</summary>
        private static string ArmyName(BattleTerrain.ArmySet armies)
        {
            bool attacker = armies.HasFlag(BattleTerrain.ArmySet.Attacker);
            bool defender = armies.HasFlag(BattleTerrain.ArmySet.Defender);
            return attacker ? (defender ? "both" : "atk") : (defender ? "def" : "none");
        }

        /// <summary>"corridor+exitAtk+..." from the flags.</summary>
        private static string ChokeSources(BattleTerrain.ChokeSource sources)
        {
            List<string> names = new List<string>();
            if ((sources & BattleTerrain.ChokeSource.Corridor) != 0) names.Add("corridor");
            if ((sources & BattleTerrain.ChokeSource.MidCorridor) != 0) names.Add("mid");
            if ((sources & BattleTerrain.ChokeSource.ExitAttacker) != 0) names.Add("exitAtk");
            if ((sources & BattleTerrain.ChokeSource.ExitDefender) != 0) names.Add("exitDef");
            if ((sources & BattleTerrain.ChokeSource.Profile) != 0) names.Add("profile");
            return (names.Count > 0) ? string.Join("+", names) : "-";
        }

        /// <summary>"atk:A def:B opposite" (sides 0 = A, 1 = B, '-' unknown) then same | opposite | unknown.</summary>
        private static string ChokeSides(BattleTerrain.ChokeFeature choke)
        {
            return "atk:" + SideLetter(choke.AttackerSide) + " def:" + SideLetter(choke.DefenderSide) + " " + choke.Sides.ToString().ToLowerInvariant();
        }

        private static string SideLetter(int side)
        {
            return (side == 0) ? "A" : (side == 1) ? "B" : "-";
        }

        /// <summary>"name:ms,..." in step order; "-" when missing.</summary>
        private static string StepMs(IReadOnlyDictionary<string, float> steps)
        {
            if (steps == null || steps.Count == 0)
            {
                return "-";
            }
            return string.Join(",", steps.Select(p => p.Key + ":" + Fmt(p.Value)));
        }

        /// <summary>"name:count,..." in insertion order; "-" when missing.</summary>
        private static string Counts(IReadOnlyDictionary<string, int> counts)
        {
            if (counts == null || counts.Count == 0)
            {
                return "-";
            }
            return string.Join(",", counts.Select(p => p.Key + ":" + p.Value.ToString(CultureInfo.InvariantCulture)));
        }

        private static string Open(BattleTerrain t)
        {
            BattleTerrain.SideInfo atk = t.Attacker;
            BattleTerrain.SideInfo def = t.Defender;
            BattleTerrain.OpenInfo o = t.Open;
            Line line = new Line("TERRAIN_OPEN");
            line.Add("clearAtkLine", atk.ClearLine).Add("clearDefLine", def.ClearLine);
            line.Add("corridorWidth", o.CorridorWidth).Add("corridorFront", o.CorridorFront);
            line.Add("exitAtk", Exit(atk)).Add("exitDef", Exit(def));
            line.Add("midCorridorWidth", o.MidCorridorWidth).Add("midCorridorFront", o.MidCorridorFront);
            return line.Finish(o);
        }

        private static string Space(BattleTerrain t)
        {
            BattleTerrain.SideInfo atk = t.Attacker;
            BattleTerrain.SideInfo def = t.Defender;
            BattleTerrain.SpaceInfo s = t.Space;
            Line line = new Line("TERRAIN_SPACE");
            line.Add("forceAtk", Force(atk)).Add("forceDef", Force(def));
            line.Add("speedAtk", Fmt(atk.FootSpeed) + "/" + Fmt(atk.MountedSpeed)).Add("speedDef", Fmt(def.FootSpeed) + "/" + Fmt(def.MountedSpeed));
            line.Add("snapAtk", atk.HasSnap ? Fmt(atk.SnapDistance) : "-").Add("snapDef", def.HasSnap ? Fmt(def.SnapDistance) : "-");
            line.Add("widths", Widths(s.WidthProfile));
            line.Add("flankAtkL", Flank(atk.FlankLeft)).Add("flankAtkR", Flank(atk.FlankRight));
            line.Add("flankDefL", Flank(def.FlankLeft)).Add("flankDefR", Flank(def.FlankRight));
            line.Add("sameGround", Same(s.SameGround));
            return line.Finish(s);
        }

        private static string Mounted(BattleTerrain t)
        {
            Line line = new Line("TERRAIN_MOUNTED");
            line.Add("runupAtk", RunUp(t.Attacker.RunUpOntoEnemy)).Add("runupDef", RunUp(t.Defender.RunUpOntoEnemy));
            return line.Finish(t.Mounted);
        }

        private static string Sight(BattleTerrain t)
        {
            BattleTerrain.SightInfo s = t.Sight;
            Line line = new Line("TERRAIN_SIGHT");
            line.Add("fireDef", t.Defender.FireAny).Add("fireAtk", t.Attacker.FireAny);
            if (s.RayFailed > 0)
            {
                line.Add("rayFailed", s.RayFailed);
            }
            return line.Finish(s);
        }

        /// <summary>"out=.. clutter=.. deep=.. slope=.. >15=.. >25=..".</summary>
        private static string Area(BattleTerrain.AreaStats a)
        {
            if (a == null)
            {
                return "-";
            }
            return "out=" + Fmt(a.OutShare)
                + " clutter=" + Fmt(a.Clutter)
                + " deep=" + Fmt(a.DeepShare)
                + " slope=" + Fmt(a.SlopeMean)
                + " >15=" + Fmt(a.SteepShare)
                + " >25=" + Fmt(a.HardShare);
        }

        /// <summary>"men/foot f/mounted m/ranged r".</summary>
        private static string Force(BattleTerrain.SideInfo side)
        {
            return side.Men + "/" + side.Foot + "f/" + side.Mounted + "m/" + side.Ranged + "r";
        }

        /// <summary>"reach/len m/ratio x/time s"; "0/-/-/-" unreachable; "-" nothing to measure.</summary>
        private static string Flank(BattleTerrain.FlankRoute route)
        {
            if (route == null)
            {
                return "-";
            }
            if (!route.Reachable)
            {
                return "0/-/-/-";
            }
            return "1/" + Fmt(route.Length) + "m/" + Fmt(route.Ratio) + "x/" + Fmt(route.Seconds) + "s";
        }

        /// <summary>"width m/front x": an army's exit, or "-".</summary>
        private static string Exit(BattleTerrain.SideInfo side)
        {
            if (float.IsNaN(side.ExitWidth))
            {
                return "-";
            }
            return Fmt(side.ExitWidth) + "m/" + Fmt(side.ExitFront) + "x";
        }

        /// <summary>"max/min(end) bumps maxBumps/minBumps".</summary>
        private static string RunUp(BattleTerrain.RunUp runUp)
        {
            if (runUp == null)
            {
                return "-";
            }
            return Fmt(runUp.Max) + "/" + Fmt(runUp.Min) + "(" + EndName(runUp.MinEnd) + ") bumps "
                + runUp.MaxBumps.ToString(CultureInfo.InvariantCulture) + "/" + runUp.MinBumps.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>"fraction:total,..." for every width line ("0:200,0.05:212,..."); "-" when missing.</summary>
        private static string Widths(IReadOnlyList<BattleTerrain.WidthLine> profile)
        {
            if (profile == null)
            {
                return "-";
            }
            StringBuilder sb = new StringBuilder();
            foreach (BattleTerrain.WidthLine w in profile)
            {
                sb.Append((sb.Length > 0) ? "," : "").Append(Fmt(w.Fraction)).Append(':').Append(Fmt((float)System.Math.Round(w.Total)));
            }
            return sb.ToString();
        }

        private static string EndName(BattleTerrain.End end)
        {
            return (end == BattleTerrain.End.None) ? "-" : end.ToString().ToLowerInvariant();
        }

        private static string Same(bool? sameGround)
        {
            return sameGround.HasValue ? (sameGround.Value ? "1" : "0") : "-";
        }

        /// <summary>Rounded to a whole number (m², metres); NaN and infinity stay as they are.</summary>
        private static float Round(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? value : (float)System.Math.Round(value);
        }

        /// <summary>The terrain log's own number format: up to two decimals, invariant; NaN and ∞ as "-".</summary>
        private static string Fmt(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string Flag(bool value)
        {
            return value ? "1" : "0";
        }

        /// <summary>One TERRAIN_* line: KIND then tab-separated name=value fields, then the group's error and ms.</summary>
        private sealed class Line
        {
            private readonly StringBuilder _sb;

            public Line(string kind)
            {
                _sb = new StringBuilder(kind);
            }

            public Line Add(string name, string value)
            {
                _sb.Append('\t').Append(name).Append('=').Append(string.IsNullOrEmpty(value) ? "-" : value);
                return this;
            }

            public Line Add(string name, float value)
            {
                return Add(name, Fmt(value));
            }

            public Line Add(string name, int value)
            {
                return Add(name, value.ToString(CultureInfo.InvariantCulture));
            }

            /// <summary>The line as it stands (no error / ms): for lines that are not a group's.</summary>
            public string Text()
            {
                return _sb.ToString();
            }

            public string Finish(BattleTerrain.Group group)
            {
                if (group.Error != null)
                {
                    Add("error", group.Error);
                }
                Add("ms", group.Ms);
                return _sb.ToString();
            }
        }
    }
}
