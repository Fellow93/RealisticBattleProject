using System;
using System.Collections.Concurrent;
using System.Globalization;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// Formation shape by ground (terrainTacticsEnabled): AI foot formations advancing (BehaviorAdvance, BehaviorCautiousAdvance)
    /// loosen in broken ground and narrow where the way ahead is narrower than their line. A final adjustment on top of the
    /// behaviors' own choice; off, before the publish, or not applicable, every helper leaves it exactly as it was.
    /// Loose: woods/rough share ahead >= LooseEnterShare turns Line into Loose (back below LooseExitShare); a shield wall is
    /// never replaced. Narrow: the narrowest walkable width ahead below NarrowEnterFactor x the normal width sets a custom
    /// width (not below MinFiles / the arrangement minimum), back once it is >= NarrowExitFactor x normal. Never for cavalry,
    /// player formations, a Charge order or under MinUnits men. Re-evaluated every EvalInterval; a shape change at most once
    /// per ShapeChangeCooldown (each change re-lays the line).
    /// THREADING: runs on the async AI thread from the behaviors' ticks: a local copy of the immutable BattleTerrain.Current,
    /// pure grid math, formation managed properties, a concurrent per-formation state (written only by its own behavior's
    /// tick) and an event queue drained on the main thread. No engine query, no WorldPosition, no MBRandom. Cleared in MissionStartReset.
    /// </summary>
    internal static class TerrainShape
    {

        public const float EvalInterval = 3f;

        public const float ShapeChangeCooldown = 10f; // per formation, any change (loose / narrow / updates / releases)

        public const float LookAhead = 60f; // m of ground ahead looked at

        public const float LooseEnterShare = 0.35f; // woods / rough share of the area + the way ahead

        public const float LooseExitShare = 0.2f;

        public const float NarrowEnterFactor = 1.1f; // narrowest walkable width ahead below this x the normal width ...

        public const float NarrowWidthFactor = 0.9f; // ... narrows to this x that width

        public const float NarrowUpdateFraction = 0.15f; // re-set only when the new width differs by more than this

        public const float NarrowExitFactor = 1.3f; // back to normal at this x the normal width

        public const int MinFiles = 12; // (and the arrangement's own minimum width)

        public const int MinUnits = 20; // fewer men (not detached) are left alone

        private const float MinRectWidth = 10f; // m, the share rectangle

        private sealed class State
        {
            public float NextEval;
            public float LastChange = float.MinValue;
            public string Behavior;
            public bool Loose;
            public bool WeSetLoose; // Restore = the arrangement it replaced
            public ArrangementOrder Restore = ArrangementOrder.ArrangementOrderLine;
            public float Share = float.NaN;
            public float WoodsShare = float.NaN;
            public bool Narrow;
            public float NarrowWidth = float.NaN;
            public float NormalWidth = float.NaN; // the behavior's own width, or the formation's before narrowing
            public float MinPassable = float.NaN;
            public float MinPassableAt = float.NaN;
            public bool SavedForm; // paths without their own width (cautious advance, foot archers): the form order before narrowing
            public FormOrder SavedFormOrder;
            public volatile string Text; // overlay text (null = nothing applied); replaced, never mutated
        }

        private static readonly ConcurrentDictionary<Formation, State> States = new ConcurrentDictionary<Formation, State>();
        /// <summary>Main thread: mission start and end.</summary>
        public static void Clear()
        {
            States.Clear();
            TerrainLog.Clear();
        }

        /// <summary>Short current-shape text for the decision overlay ("shape: loose(woods 42%)", "shape: narrow 58m"), or null.</summary>
        public static string Describe(Formation f)
        {
            State s;
            return (f != null && States.TryGetValue(f, out s) && s != null) ? s.Text : null;
        }

        // evaluation (AI thread, first in the behavior's shape section)

        /// <summary>AI thread, from the behavior's tick before its own arrangement / width code: (re-)evaluates the formation's shape. Not applicable = the state is dropped and the formation left as it is.</summary>
        internal static void Tick(Formation formation, string behaviorName)
        {
            if (formation == null)
            {
                return;
            }
            if (!RBMConfig.RBMConfig.terrainTacticsEnabled)
            {
                States.TryRemove(formation, out _);
                return;
            }
            BattleTerrain terrain = BattleTerrain.Current;
            if (terrain == null)
            {
                return;
            }
            string why = NotApplicable(formation);
            if (why != null)
            {
                Release(formation, behaviorName, why);
                return;
            }
            State s = States.GetOrAdd(formation, _ => new State());
            s.Behavior = behaviorName;
            float now = Now();
            if (now < s.NextEval)
            {
                return;
            }
            s.NextEval = now + EvalInterval;

            Vec2 centre = RBMAI.Utilities.GetFormationCenter(formation);
            Vec2 forward = formation.Direction;
            if (!centre.IsValid || !forward.IsValid || forward.Normalize() < 0.001f)
            {
                return;
            }
            float width = Math.Max(MinRectWidth, formation.Width);
            float depth = Math.Max(0f, formation.Depth);
            float woods;
            float share = terrain.WoodsOrRoughShare(centre + forward * (LookAhead * 0.5f), forward, width, depth + LookAhead, out woods);
            float at;
            float minPass = terrain.MinPassableWidthAhead(centre, forward, depth * 0.5f + LookAhead, width * 0.5f, out at);
            s.Share = share;
            s.WoodsShare = woods;
            s.MinPassable = minPass;
            s.MinPassableAt = at;

            if (now - s.LastChange < ShapeChangeCooldown)
            {
                return;
            }
            bool changed = false;

            if (!float.IsNaN(share))
            {
                if (!s.Loose && share >= LooseEnterShare)
                {
                    s.Loose = true;
                    changed = true;
                    Enqueue(formation, s, now, "loose", (woods >= share * 0.5f ? "woods" : "rough") + " >= " + Fmt2(LooseEnterShare));
                }
                else if (s.Loose && share < LooseExitShare)
                {
                    s.Loose = false;
                    changed = true;
                    Enqueue(formation, s, now, "unloose", "share < " + Fmt2(LooseExitShare));
                }
            }

            float normal = !float.IsNaN(s.NormalWidth) ? s.NormalWidth : formation.Width;
            if (!float.IsNaN(minPass) && normal > 0f)
            {
                float wanted = Math.Max(minPass * NarrowWidthFactor, MinWidth(formation));
                if (!s.Narrow)
                {
                    if (minPass < normal * NarrowEnterFactor && wanted < normal * (1f - NarrowUpdateFraction))
                    {
                        s.Narrow = true;
                        s.NarrowWidth = wanted;
                        changed = true;
                        Enqueue(formation, s, now, "narrow", "minPass < " + Fmt2(NarrowEnterFactor) + " x normal");
                    }
                }
                else if (minPass >= normal * NarrowExitFactor || wanted >= normal)
                {
                    s.Narrow = false;
                    s.NarrowWidth = float.NaN;
                    changed = true;
                    Enqueue(formation, s, now, "widen", (minPass >= normal * NarrowExitFactor) ? "minPass >= " + Fmt2(NarrowExitFactor) + " x normal" : "fits");
                }
                else if (Math.Abs(wanted - s.NarrowWidth) > s.NarrowWidth * NarrowUpdateFraction)
                {
                    s.NarrowWidth = wanted;
                    changed = true;
                    Enqueue(formation, s, now, "renarrow", "minPass moved");
                }
            }
            else if (s.Narrow && float.IsNaN(minPass))
            {
                // Every line ahead blocked: the path search routes round it, nothing to fit.
                s.Narrow = false;
                s.NarrowWidth = float.NaN;
                changed = true;
                Enqueue(formation, s, now, "widen", "noLine");
            }

            if (changed)
            {
                s.LastChange = now;
                s.Text = BuildText(s);
            }
        }

        /// <summary>Why the formation is left alone, or null.</summary>
        private static string NotApplicable(Formation formation)
        {
            if (!formation.IsAIControlled)
            {
                return "player";
            }
            if (formation.CountOfUnitsWithoutDetachedOnes < MinUnits)
            {
                return "small";
            }
            FormationQuerySystem qs = formation.QuerySystem;
            if (qs == null || qs.IsCavalryFormation || qs.IsRangedCavalryFormation)
            {
                return "mounted";
            }
            if (!qs.IsInfantryFormation && !qs.IsRangedFormation)
            {
                return "notFoot";
            }
            // ChargeGuard.IsChargeIntent: the advance to a charge counts as the charge.
            if (ChargeGuard.IsChargeIntent(formation))
            {
                return "charge";
            }
            return null;
        }

        /// <summary>Drops the state without touching the formation (its new owner decides); one release line when something was applied.</summary>
        private static void Release(Formation formation, string behaviorName, string why)
        {
            State s;
            if (!States.TryRemove(formation, out s) || s == null)
            {
                return;
            }
            if (s.Loose || s.Narrow)
            {
                s.Behavior = behaviorName;
                Enqueue(formation, s, Now(), "release", why);
            }
        }

        // application (AI thread, after Tick)

        /// <summary>While the loose rule is on: Line → Loose (the replaced arrangement is kept); when it ends: back to it if still Loose. A shield wall and every other arrangement are left alone.</summary>
        internal static void ApplyArrangement(Formation formation)
        {
            State s;
            if (formation == null || !States.TryGetValue(formation, out s) || s == null)
            {
                return;
            }
            ArrangementOrder.ArrangementOrderEnum current = formation.ArrangementOrder.OrderEnum;
            if (s.Loose)
            {
                if (current == ArrangementOrder.ArrangementOrderEnum.Line)
                {
                    s.Restore = formation.ArrangementOrder;
                    s.WeSetLoose = true;
                    formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLoose);
                }
            }
            else if (s.WeSetLoose)
            {
                s.WeSetLoose = false;
                if (current == ArrangementOrder.ArrangementOrderEnum.Loose)
                {
                    formation.SetArrangementOrder(s.Restore);
                }
            }
        }

        /// <summary>The advance's own arrangement decision while the loose rule is on: true = handled here (Loose set); false = run the normal decision (rule off, already a shield wall, or it qualifies for one).</summary>
        internal static bool DecideArrangementInstead(Formation formation)
        {
            State s;
            if (formation == null || !States.TryGetValue(formation, out s) || s == null || !s.Loose)
            {
                return false;
            }
            ArrangementOrder.ArrangementOrderEnum current = formation.ArrangementOrder.OrderEnum;
            if (current == ArrangementOrder.ArrangementOrderEnum.ShieldWall)
            {
                return false;
            }
            float shieldWallShare = RBMConfig.RBMConfig.shieldWallShare;
            // A ranged formation never takes a shield wall (IsRangedForArrangement).
            if (!RBMAI.Utilities.IsRangedForArrangement(formation) && RBMAI.Utilities.FormationRatioShieldWallEligible(formation) > shieldWallShare)
            {
                return false;
            }
            if (current != ArrangementOrder.ArrangementOrderEnum.Loose)
            {
                s.Restore = ArrangementOrder.ArrangementOrderLine;
                s.WeSetLoose = true;
                formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLoose);
            }
            return true;
        }

        /// <summary>The advance's infantry width: normalWidth narrowed while the narrow rule is on. Records it as the normal width for the rule's tests.</summary>
        internal static float AdjustWidth(Formation formation, float normalWidth)
        {
            State s;
            if (formation == null || !States.TryGetValue(formation, out s) || s == null)
            {
                return normalWidth;
            }
            s.NormalWidth = normalWidth;
            s.SavedForm = false;
            if (!s.Narrow || float.IsNaN(s.NarrowWidth))
            {
                return normalWidth;
            }
            return Math.Min(normalWidth, Math.Max(s.NarrowWidth, MinWidth(formation)));
        }

        /// <summary>Paths that set no width of their own (cautious advance, foot archers in the advance): while narrow, a custom width; when it ends, the earlier form order back.</summary>
        internal static void ApplyFormWidth(Formation formation)
        {
            State s;
            if (formation == null || !States.TryGetValue(formation, out s) || s == null)
            {
                return;
            }
            if (s.Narrow && !float.IsNaN(s.NarrowWidth))
            {
                if (!s.SavedForm)
                {
                    s.SavedFormOrder = formation.FormOrder;
                    s.SavedForm = true;
                    s.NormalWidth = formation.Width;
                }
                float w = Math.Max(s.NarrowWidth, MinWidth(formation));
                FormOrder form = formation.FormOrder;
                if (w < s.NormalWidth && (form.OrderEnum != FormOrder.FormOrderEnum.Custom || Math.Abs(form.CustomFlankWidth - w) > 0.5f))
                {
                    formation.SetFormOrder(FormOrder.FormOrderCustom(w));
                }
            }
            else if (s.SavedForm)
            {
                s.SavedForm = false;
                formation.SetFormOrder(s.SavedFormOrder);
            }
            else
            {
                s.NormalWidth = formation.Width;
            }
        }

        // shared

        /// <summary>MinFiles men wide (fewer men: all of them) at the arrangement's spacing, and the arrangement's minimum width.</summary>
        private static float MinWidth(Formation formation)
        {
            int files = Math.Max(1, Math.Min(MinFiles, formation.CountOfUnitsWithoutDetachedOnes));
            float diameter = formation.UnitDiameter;
            float width = (files - 1) * (formation.Interval + diameter) + diameter;
            float native = formation.MinimumWidth;
            return (native > width) ? native : width;
        }

        private static float Now()
        {
            Mission mission = Mission.Current;
            return (mission != null) ? mission.CurrentTime : 0f;
        }

        private static string BuildText(State s)
        {
            string text = null;
            if (s.Loose)
            {
                string kind = (!float.IsNaN(s.WoodsShare) && s.WoodsShare >= s.Share * 0.5f) ? "woods" : "rough";
                text = "loose(" + kind + " " + Pct(s.Share) + ")";
            }
            if (s.Narrow && !float.IsNaN(s.NarrowWidth))
            {
                text = ((text == null) ? "" : text + " ") + "narrow " + Fmt0(s.NarrowWidth) + "m";
            }
            return (text == null) ? null : "shape: " + text;
        }

        private static void Enqueue(Formation formation, State s, float now, string what, string why)
        {
            DecisionTrace.Note(formation, "TerrainShape", what, why);
            if (!TerrainLog.On || formation.Team == null)
            {
                return;
            }
            TerrainLog.Enqueue(new TerrainLog.Event
            {
                Tag = TerrainLog.Tag.Shape,
                Team = formation.Team,
                FormationIndex = (int)formation.FormationIndex,
                Time = now,
                Behavior = s.Behavior,
                What = what,
                Why = why,
                Extra = "share=" + Fmt2(s.Share) + " woods=" + Fmt2(s.WoodsShare) + " minPass=" + Fmt0(s.MinPassable) + " at=" + Fmt0(s.MinPassableAt)
                    + " normal=" + Fmt0(s.NormalWidth) + " narrow=" + Fmt0(s.NarrowWidth) + " width=" + Fmt0(formation.Width) + " depth=" + Fmt0(formation.Depth)
                    + " men=" + formation.CountOfUnitsWithoutDetachedOnes.ToString(CultureInfo.InvariantCulture) + " arr=" + formation.ArrangementOrder.OrderEnum
            });
        }

        private static string Pct(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : (value * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        private static string Fmt0(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0", CultureInfo.InvariantCulture);
        }

        private static string Fmt2(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
