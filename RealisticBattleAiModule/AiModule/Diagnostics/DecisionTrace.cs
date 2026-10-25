using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// Decision trace, OBSERVATION ONLY: records WHO changed a formation's contested outputs (movement order, facing, arrangement,
    /// width, active behavior, team tactic) and writes DECISION / FLIP lines to the AI behavior log.
    ///
    /// Writer scope: a thread-static (kind, detail) pushed in a prefix and restored in a finalizer around FormationAI.TickOccasionally
    /// ("formAI:&lt;behavior&gt;"), TeamAIComponent.TickOccasionally ("tactic:..."), TeamAIComponent.MakeDecision ("decide:..."),
    /// ChargeGuard's Formation.Tick re-check ("chargeGuard") and ReactionMonitor's check ("react"); no scope = "player" or "other".
    ///
    /// Records (change only, against the LAST RECORDED value): move (SetMovementOrder prefix), facing (SWEPT: SetFacingOrder is a
    /// one-line wrapper the JIT inlines, a prefix would not run), arr / width (prefixes plus sweeps), beh, tactic, and notes (BEHAVIOR
    /// queues / REACT lines appended to the next DECISION line).
    ///
    /// Output (main thread, <see cref="Drain"/>): a DECISION line per formation at most every EmitSeconds, and a FLIP line on repeated
    /// Move-point reversals or order / arrangement / behavior changes within FlipWindow.
    ///
    /// THREADING / COST: hooks run on the async AI thread (and the main thread for player orders); each first reads the volatile
    /// <see cref="Active"/> and returns when the log is closed. Otherwise: per-formation State compared under its own lock, managed
    /// reads of cached fields only (no engine call), allocation only on a change, queued and formatted on the main thread.
    /// Cleared in MissionStartReset and at mission end.
    /// </summary>
    internal static class DecisionTrace
    {
        // Change thresholds against the last recorded value.
        private const float MoveMetres = 5f;
        private const float FacingDegrees = 15f;
        private const float WidthShare = 0.1f;

        // Records in between are coalesced into one DECISION line.
        private const float EmitSeconds = 0.5f;

        private const float FlipWindow = 30f;

        // Move-point jumps over FlipJumpMetres count for the move FLIP, which needs more than FlipMoveReversals (jumps back against the previous one) in the window.
        private const float FlipJumpMetres = 20f;
        private const int FlipMoveReversals = 3;

        // Order kind / arrangement / behavior changes above this in FlipWindow = FLIP.
        private const int FlipChanges = 2;

        private const float NoteMaxAge = 30f;

        private const int MaxChain = 24;
        private const int MaxNotes = 12;
        private const int MaxQueued = 20000;

        private static readonly float FacingCos = (float)Math.Cos(FacingDegrees * Math.PI / 180.0);

        // True while the AI behavior log is open.
        internal static volatile bool Active;

        internal enum Scope : byte
        {
            None,
            FormAI,
            Tactic,
            Decide,
            ChargeGuard,
            React
        }

        // The scope a prefix replaced (restored by Exit), plus a value the hook compares after.
        internal struct Frame
        {
            public bool Pushed;
            public Scope PrevKind;
            public object PrevDetail;
            public object Before;
        }

        [ThreadStatic] private static Scope _kind;
        [ThreadStatic] private static object _detail;

        // Any thread. Pair with Exit in a finally / finalizer.
        internal static Frame Enter(Scope kind, object detail)
        {
            Frame frame = default(Frame);
            if (!Active)
            {
                return frame;
            }
            frame.Pushed = true;
            frame.PrevKind = _kind;
            frame.PrevDetail = _detail;
            _kind = kind;
            _detail = detail;
            return frame;
        }

        internal static void Exit(Frame frame)
        {
            if (frame.Pushed)
            {
                _kind = frame.PrevKind;
                _detail = frame.PrevDetail;
            }
        }

        private static string WriterName(Formation formation)
        {
            switch (_kind)
            {
                case Scope.FormAI:
                    {
                        Formation owner = _detail as Formation;
                        BehaviorComponent behavior = (owner != null && owner.AI != null) ? owner.AI.ActiveBehavior : null;
                        string name = "formAI:" + ((behavior != null) ? behavior.GetType().Name : "-");
                        return (owner != null && owner != formation) ? name + "@" + TerrainCavalry.FormationName(owner) : name;
                    }
                case Scope.Tactic:
                    return "tactic:" + TacticName(_detail as TeamAIComponent);
                case Scope.Decide:
                    return "decide:" + TacticName(_detail as TeamAIComponent);
                case Scope.ChargeGuard:
                    return "chargeGuard";
                case Scope.React:
                    return "react";
                default:
                    return (formation != null && !formation.IsAIControlled) ? "player" : "other";
            }
        }

        private static string TacticName(TeamAIComponent ai)
        {
            if (ai == null)
            {
                return "-";
            }
            TacticComponent tactic = TacticSelection.CurrentTacticRef(ai);
            return (tactic != null) ? tactic.GetType().Name : "-";
        }

        // Per-formation state: any thread, under the state's lock.

        private sealed class State
        {
            public bool HasMove;
            public MovementOrder.MovementOrderEnum MoveKind;
            public Vec2 MovePoint = Vec2.Invalid;
            public Formation MoveTarget;

            public bool HasFacing;
            public FacingOrder.FacingOrderEnum FacingKind;
            public Vec2 FacingDir = Vec2.Invalid;

            public bool HasArr;
            public ArrangementOrder.ArrangementOrderEnum Arr;

            public bool HasForm;
            public FormOrder.FormOrderEnum FormKind;
            public float FormWidth;
        }

        private sealed class FormationReferenceComparer : IEqualityComparer<Formation>
        {
            public bool Equals(Formation x, Formation y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(Formation obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }

        private static readonly ConcurrentDictionary<Formation, State> States = new ConcurrentDictionary<Formation, State>(new FormationReferenceComparer());

        private static readonly Func<Formation, State> NewState = f => new State();

        private static readonly AccessTools.StructFieldRef<MovementOrder, WorldPosition> MovePositionRef = TryStructRef<MovementOrder, WorldPosition>("_position");

        private static readonly AccessTools.StructFieldRef<FacingOrder, Vec2> FacingDirectionRef = TryStructRef<FacingOrder, Vec2>("_lookAtDirection");

        private static AccessTools.StructFieldRef<T, F> TryStructRef<T, F>(string field) where T : struct
        {
            try
            {
                return AccessTools.StructFieldRefAccess<T, F>(field);
            }
            catch
            {
                return null;
            }
        }

        private static Vec2 MovePoint(ref MovementOrder order)
        {
            return (MovePositionRef != null) ? MovePositionRef(ref order).AsVec2 : Vec2.Invalid;
        }

        private static Vec2 FacingDirection(ref FacingOrder order)
        {
            return (FacingDirectionRef != null) ? FacingDirectionRef(ref order) : Vec2.Invalid;
        }

        // No records during deployment (the baseline still follows).
        private static bool Silent()
        {
            Mission mission = Mission.Current;
            return mission == null || mission.Mode == MissionMode.Deployment;
        }

        private static float Now()
        {
            Mission mission = Mission.Current;
            return (mission != null) ? mission.CurrentTime : 0f;
        }

        private enum Field : byte
        {
            Move,
            Facing,
            Arr,
            Width,
            Beh,
            Tactic,
            Note
        }

        private sealed class Rec
        {
            public Formation Formation;
            public float Time;
            public Field Field;
            public string Writer;
            public string Text;
            public string Value;
            public bool KindChanged;
            public Vec2 Point = Vec2.Invalid;
        }

        private static readonly ConcurrentQueue<Rec> Queue = new ConcurrentQueue<Rec>();

        private static int _queued;

        private static int _dropped;

        private static void Enqueue(Rec rec)
        {
            if (Interlocked.Increment(ref _queued) > MaxQueued)
            {
                Interlocked.Decrement(ref _queued);
                Interlocked.Increment(ref _dropped);
                return;
            }
            Queue.Enqueue(rec);
        }

        /// <summary>
        /// RBM's SetMovementOrder prefix, after its Charge conversion and ChargeGuard.Apply (<paramref name="input"/> = the
        /// order really handed to the formation). <paramref name="rawKind"/> = as the caller gave it, <paramref name="askedKind"/>
        /// / <paramref name="askedTarget"/> = before ChargeGuard.
        /// </summary>
        internal static void OnMovementOrder(Formation formation, MovementOrder.MovementOrderEnum rawKind, MovementOrder.MovementOrderEnum askedKind,
            Formation askedTarget, ref MovementOrder input)
        {
            if (!Active || formation == null || formation.Team == null)
            {
                return;
            }
            MovementOrder.MovementOrderEnum kind = input.OrderEnum;
            if (kind == MovementOrder.MovementOrderEnum.Invalid)
            {
                // Native applies Invalid as Stop.
                kind = MovementOrder.MovementOrderEnum.Stop;
            }
            Vec2 point = (kind == MovementOrder.MovementOrderEnum.Move) ? MovePoint(ref input) : Vec2.Invalid;
            Formation target = (kind == MovementOrder.MovementOrderEnum.ChargeToTarget) ? input.TargetFormation : null;
            State s = States.GetOrAdd(formation, NewState);
            lock (s)
            {
                if (!s.HasMove)
                {
                    MovementOrder current = formation.GetReadonlyMovementOrderReference();
                    s.MoveKind = current.OrderEnum;
                    s.MovePoint = (s.MoveKind == MovementOrder.MovementOrderEnum.Move) ? MovePoint(ref current) : Vec2.Invalid;
                    s.MoveTarget = (s.MoveKind == MovementOrder.MovementOrderEnum.ChargeToTarget) ? current.TargetFormation : null;
                    s.HasMove = true;
                }
                bool kindChanged = kind != s.MoveKind;
                bool moved = kind == MovementOrder.MovementOrderEnum.Move && point.IsValid
                    && (!s.MovePoint.IsValid || s.MovePoint.DistanceSquared(point) > MoveMetres * MoveMetres);
                bool retarget = kind == MovementOrder.MovementOrderEnum.ChargeToTarget && target != s.MoveTarget;
                if (!kindChanged && !moved && !retarget)
                {
                    return;
                }
                string before = DescribeOrder(s.MoveKind, s.MovePoint, s.MoveTarget);
                s.MoveKind = kind;
                s.MovePoint = point;
                s.MoveTarget = target;
                if (Silent())
                {
                    return;
                }
                string writer = WriterName(formation);
                bool guarded = askedKind != input.OrderEnum
                    || (askedKind == MovementOrder.MovementOrderEnum.ChargeToTarget && askedTarget != input.TargetFormation);
                if (guarded && _kind != Scope.ChargeGuard)
                {
                    writer += ">chargeGuard";
                }
                string text = before + "->" + DescribeOrder(kind, point, target);
                if (guarded)
                {
                    text += " asked=" + DescribeOrder(askedKind, Vec2.Invalid, askedTarget);
                }
                if (rawKind != askedKind)
                {
                    text += " raw=" + rawKind;
                }
                Enqueue(new Rec
                {
                    Formation = formation,
                    Time = Now(),
                    Field = Field.Move,
                    Writer = writer,
                    Text = text,
                    Value = kind.ToString(),
                    KindChanged = kindChanged,
                    Point = point
                });
            }
        }

        private static void OnArrangement(Formation formation, ArrangementOrder.ArrangementOrderEnum arr, string writerSuffix)
        {
            State s = States.GetOrAdd(formation, NewState);
            lock (s)
            {
                if (!s.HasArr)
                {
                    s.Arr = formation.ArrangementOrder.OrderEnum;
                    s.HasArr = true;
                }
                if (arr == s.Arr)
                {
                    return;
                }
                ArrangementOrder.ArrangementOrderEnum before = s.Arr;
                s.Arr = arr;
                if (Silent())
                {
                    return;
                }
                Enqueue(new Rec
                {
                    Formation = formation,
                    Time = Now(),
                    Field = Field.Arr,
                    Writer = WriterName(formation) + writerSuffix,
                    Text = before + "->" + arr,
                    Value = arr.ToString()
                });
            }
        }

        private static void OnFormOrder(Formation formation, FormOrder.FormOrderEnum kind, float width, string writerSuffix)
        {
            State s = States.GetOrAdd(formation, NewState);
            lock (s)
            {
                if (!s.HasForm)
                {
                    FormOrder current = formation.FormOrder;
                    s.FormKind = current.OrderEnum;
                    s.FormWidth = current.CustomFlankWidth;
                    s.HasForm = true;
                }
                bool changed = kind != s.FormKind
                    || (kind == FormOrder.FormOrderEnum.Custom && Math.Abs(width - s.FormWidth) > WidthShare * Math.Max(1f, Math.Abs(s.FormWidth)));
                if (!changed)
                {
                    return;
                }
                string before = DescribeForm(s.FormKind, s.FormWidth);
                s.FormKind = kind;
                s.FormWidth = width;
                if (Silent())
                {
                    return;
                }
                Enqueue(new Rec
                {
                    Formation = formation,
                    Time = Now(),
                    Field = Field.Width,
                    Writer = WriterName(formation) + writerSuffix,
                    Text = before + "->" + DescribeForm(kind, width),
                    Value = DescribeForm(kind, width)
                });
            }
        }

        /// <summary>Any thread: compares facing (and with <paramref name="all"/> arrangement and FormOrder) to the last recorded values and records a change.</summary>
        internal static void Sweep(Formation formation, string writer, bool all)
        {
            if (!Active || formation == null || formation.Team == null || formation.CountOfUnits <= 0)
            {
                return;
            }
            State s = States.GetOrAdd(formation, NewState);
            FacingOrder facing = formation.FacingOrder;
            FacingOrder.FacingOrderEnum kind = facing.OrderEnum;
            Vec2 dir = (kind == FacingOrder.FacingOrderEnum.LookAtDirection) ? FacingDirection(ref facing) : Vec2.Invalid;
            lock (s)
            {
                if (!s.HasFacing)
                {
                    s.FacingKind = kind;
                    s.FacingDir = dir;
                    s.HasFacing = true;
                }
                else if (kind != s.FacingKind || (kind == FacingOrder.FacingOrderEnum.LookAtDirection && Turned(s.FacingDir, dir)))
                {
                    string before = DescribeFacing(s.FacingKind, s.FacingDir);
                    s.FacingKind = kind;
                    s.FacingDir = dir;
                    if (!Silent())
                    {
                        Enqueue(new Rec
                        {
                            Formation = formation,
                            Time = Now(),
                            Field = Field.Facing,
                            Writer = writer ?? WriterName(formation),
                            Text = before + "->" + DescribeFacing(kind, dir),
                            Value = kind.ToString()
                        });
                    }
                }
            }
            if (all)
            {
                OnArrangement(formation, formation.ArrangementOrder.OrderEnum, "~");
                FormOrder form = formation.FormOrder;
                OnFormOrder(formation, form.OrderEnum, form.CustomFlankWidth, "~");
            }
        }

        private static bool Turned(Vec2 a, Vec2 b)
        {
            if (!a.IsValid || !b.IsValid)
            {
                return a.IsValid != b.IsValid;
            }
            float la = a.Length;
            float lb = b.Length;
            if (la < 0.0001f || lb < 0.0001f)
            {
                return false;
            }
            return a.DotProduct(b) / (la * lb) < FacingCos;
        }

        // Any thread: Sweep for the team's AI formations (player ones are swept in Drain).
        private static void SweepTeam(Team team)
        {
            if (team == null)
            {
                return;
            }
            foreach (Formation formation in team.FormationsIncludingSpecialAndEmpty)
            {
                if (formation != null && formation.IsAIControlled)
                {
                    Sweep(formation, null, true);
                }
            }
        }

        /// <summary>Any thread: "<paramref name="behavior"/> <paramref name="what"/> <paramref name="why"/>" goes onto the formation's next DECISION line.</summary>
        internal static void Note(Formation formation, string behavior, string what, string why)
        {
            if (!Active || formation == null || formation.Team == null || Silent())
            {
                return;
            }
            Enqueue(new Rec
            {
                Formation = formation,
                Time = Now(),
                Field = Field.Note,
                Text = (behavior ?? "-") + " " + (what ?? "-") + ((why != null) ? " " + why : "")
            });
        }

        private static string DescribeOrder(MovementOrder.MovementOrderEnum kind, Vec2 point, Formation target)
        {
            if (kind == MovementOrder.MovementOrderEnum.Move && point.IsValid)
            {
                return "Move(" + Pos(point) + ")";
            }
            if (kind == MovementOrder.MovementOrderEnum.ChargeToTarget)
            {
                return "ChargeToTarget(" + TerrainCavalry.FormationName(target) + ")";
            }
            return kind.ToString();
        }

        private static string DescribeFacing(FacingOrder.FacingOrderEnum kind, Vec2 dir)
        {
            if (kind == FacingOrder.FacingOrderEnum.LookAtDirection && dir.IsValid)
            {
                return "dir(" + Fmt2(dir.x) + "," + Fmt2(dir.y) + ")";
            }
            return kind.ToString();
        }

        private static string DescribeForm(FormOrder.FormOrderEnum kind, float width)
        {
            return (kind == FormOrder.FormOrderEnum.Custom) ? Fmt0(width) : kind.ToString();
        }

        private static string Pos(Vec2 p)
        {
            return Fmt0(p.x) + "," + Fmt0(p.y);
        }

        private static string Fmt0(float value)
        {
            return value.ToString("0", CultureInfo.InvariantCulture);
        }

        private static string Fmt1(float value)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string Fmt2(float value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string Clean(string text)
        {
            return (text == null) ? "-" : text.Replace('\t', ' ').Replace('|', '/');
        }

        private struct FlipEvent
        {
            public float Time;
            public string Writer;
            public string Value;
        }

        private sealed class Pending
        {
            public readonly List<Rec> Chain = new List<Rec>();
            public readonly List<Rec> Notes = new List<Rec>();
            public int ChainDropped;
            public int NotesDropped;
            public float LastEmit = float.NegativeInfinity;

            // FLIP state: [0] move reversals, [1] order kind, [2] arrangement, [3] behavior.
            public readonly List<FlipEvent>[] Flips = { new List<FlipEvent>(), new List<FlipEvent>(), new List<FlipEvent>(), new List<FlipEvent>() };
            public readonly float[] FlipQuietUntil = { float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity };
            public Vec2 LastMovePoint = Vec2.Invalid;
            public Vec2 LastJump = Vec2.Invalid;
            public string LastJumpWriter;
        }

        private static readonly string[] FlipNames = { "move", "order", "arr", "beh" };

        private static readonly Dictionary<Formation, Pending> Pendings = new Dictionary<Formation, Pending>(new FormationReferenceComparer());

        private static int _droppedReported;

        // Main thread; also sweeps player formations' facings.
        internal static void Drain(float now, bool force = false)
        {
            if (!Active)
            {
                return;
            }
            Mission mission = Mission.Current;
            if (mission != null)
            {
                foreach (Team team in mission.Teams)
                {
                    if (team == null)
                    {
                        continue;
                    }
                    foreach (Formation formation in team.FormationsIncludingSpecialAndEmpty)
                    {
                        if (formation != null && !formation.IsAIControlled)
                        {
                            Sweep(formation, null, false);
                        }
                    }
                }
            }

            Rec rec;
            while (Queue.TryDequeue(out rec))
            {
                Interlocked.Decrement(ref _queued);
                Pending p;
                if (!Pendings.TryGetValue(rec.Formation, out p))
                {
                    p = new Pending();
                    Pendings[rec.Formation] = p;
                }
                if (rec.Field == Field.Note)
                {
                    p.Notes.Add(rec);
                    if (p.Notes.Count > MaxNotes)
                    {
                        p.Notes.RemoveAt(0);
                        p.NotesDropped++;
                    }
                    continue;
                }
                if (p.Chain.Count < MaxChain)
                {
                    p.Chain.Add(rec);
                }
                else
                {
                    p.ChainDropped++;
                }
                DetectFlip(rec, p);
            }

            int dropped = Volatile.Read(ref _dropped);
            if (dropped > _droppedReported)
            {
                AiBehaviorLog.Write("# DECISION trace queue full: " + (dropped - _droppedReported).ToString(CultureInfo.InvariantCulture) + " records dropped");
                _droppedReported = dropped;
            }

            foreach (KeyValuePair<Formation, Pending> entry in Pendings)
            {
                Pending p = entry.Value;
                if (p.Chain.Count == 0 || (!force && now - p.LastEmit < EmitSeconds))
                {
                    continue;
                }
                p.LastEmit = now;
                AiBehaviorLog.Write(DecisionLine(entry.Key, p, now));
                p.Chain.Clear();
                p.ChainDropped = 0;
                p.Notes.Clear();
                p.NotesDropped = 0;
            }
        }

        private static void DetectFlip(Rec rec, Pending p)
        {
            int slot = -1;
            FlipEvent ev = new FlipEvent { Time = rec.Time, Writer = rec.Writer, Value = rec.Value };
            if (rec.Field == Field.Move)
            {
                if (rec.KindChanged)
                {
                    AddFlip(rec, p, 1, ev);
                }
                if (rec.Point.IsValid)
                {
                    if (p.LastMovePoint.IsValid)
                    {
                        Vec2 jump = rec.Point - p.LastMovePoint;
                        if (jump.LengthSquared > FlipJumpMetres * FlipJumpMetres)
                        {
                            if (p.LastJump.IsValid && p.LastJump.DotProduct(jump) < 0f)
                            {
                                ev.Writer = p.LastJumpWriter + "/" + rec.Writer;
                                ev.Value = Pos(rec.Point);
                                slot = 0;
                            }
                            p.LastJump = jump;
                            p.LastJumpWriter = rec.Writer;
                        }
                    }
                    p.LastMovePoint = rec.Point;
                }
            }
            else if (rec.Field == Field.Arr)
            {
                slot = 2;
            }
            else if (rec.Field == Field.Beh)
            {
                slot = 3;
            }
            if (slot >= 0)
            {
                AddFlip(rec, p, slot, ev);
            }
        }

        private static void AddFlip(Rec rec, Pending p, int slot, FlipEvent ev)
        {
            List<FlipEvent> list = p.Flips[slot];
            list.Add(ev);
            while (list.Count > 0 && rec.Time - list[0].Time > FlipWindow)
            {
                list.RemoveAt(0);
            }
            int limit = (slot == 0) ? FlipMoveReversals : FlipChanges;
            if (list.Count <= limit || rec.Time < p.FlipQuietUntil[slot])
            {
                return;
            }
            p.FlipQuietUntil[slot] = rec.Time + FlipWindow;
            AiBehaviorLog.Write(FlipLine(rec.Formation, slot, list, rec.Time));
            list.Clear();
        }

        private static string Head(Formation formation, float now, string kind)
        {
            Team team = formation.Team;
            StringBuilder sb = new StringBuilder();
            sb.Append("t=").Append(AiBehaviorLog.Fmt(now)).Append('\t').Append(kind);
            sb.Append('\t').Append((team != null) ? team.Side + "#" + team.TeamIndex : "-");
            sb.Append('\t').Append(formation.FormationIndex);
            string cls;
            try
            {
                cls = (formation.QuerySystem != null) ? formation.QuerySystem.MainClass.ToString() : "-";
            }
            catch
            {
                cls = "?";
            }
            sb.Append('\t').Append(cls);
            return sb.ToString();
        }

        private static string DecisionLine(Formation formation, Pending p, float now)
        {
            StringBuilder sb = new StringBuilder(Head(formation, now, "DECISION"));
            sb.Append("\tmen=").Append(formation.CountOfUnits.ToString(CultureInfo.InvariantCulture));
            Team team = formation.Team;
            sb.Append("\ttactic=").Append((team != null && team.TeamAI != null) ? TacticName(team.TeamAI) : "-");
            BehaviorComponent behavior = (formation.AI != null) ? formation.AI.ActiveBehavior : null;
            sb.Append("\tbeh=").Append((behavior != null) ? behavior.GetType().Name : "-");
            sb.Append("\tai=").Append(formation.IsAIControlled ? 1 : 0);
            sb.Append("\tchain=");
            for (int i = 0; i < p.Chain.Count; i++)
            {
                Rec r = p.Chain[i];
                if (i > 0)
                {
                    sb.Append(" | ");
                }
                sb.Append(FieldName(r.Field)).Append(':').Append(Clean(r.Writer)).Append(' ').Append(Clean(r.Text)).Append(" @").Append(Fmt1(r.Time));
            }
            if (p.ChainDropped > 0)
            {
                sb.Append(" | +").Append(p.ChainDropped.ToString(CultureInfo.InvariantCulture)).Append(" more");
            }
            sb.Append("\tnotes=");
            int written = 0;
            int old = 0;
            for (int i = 0; i < p.Notes.Count; i++)
            {
                Rec r = p.Notes[i];
                if (now - r.Time > NoteMaxAge)
                {
                    old++;
                    continue;
                }
                if (written > 0)
                {
                    sb.Append(" | ");
                }
                sb.Append(Clean(r.Text)).Append(" @").Append(Fmt1(r.Time));
                written++;
            }
            if (written == 0)
            {
                sb.Append('-');
            }
            if (p.NotesDropped + old > 0)
            {
                sb.Append(" | (").Append((p.NotesDropped + old).ToString(CultureInfo.InvariantCulture)).Append(" older dropped)");
            }
            return sb.ToString();
        }

        private static string FlipLine(Formation formation, int slot, List<FlipEvent> events, float now)
        {
            StringBuilder sb = new StringBuilder(Head(formation, now, "FLIP"));
            sb.Append("\tfield=").Append(FlipNames[slot]);
            sb.Append("\tcount=").Append(events.Count.ToString(CultureInfo.InvariantCulture)).Append("/").Append(Fmt0(FlipWindow)).Append("s");
            Dictionary<string, int> writers = new Dictionary<string, int>();
            List<string> order = new List<string>();
            for (int i = 0; i < events.Count; i++)
            {
                string w = Clean(events[i].Writer);
                int n;
                if (!writers.TryGetValue(w, out n))
                {
                    order.Add(w);
                }
                writers[w] = n + 1;
            }
            sb.Append("\twriters=");
            for (int i = 0; i < order.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append(order[i]).Append(" x").Append(writers[order[i]].ToString(CultureInfo.InvariantCulture));
            }
            sb.Append("\tseq=");
            for (int i = 0; i < events.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('>');
                }
                sb.Append(Clean(events[i].Value)).Append('@').Append(Fmt1(events[i].Time));
            }
            return sb.ToString();
        }

        private static string FieldName(Field field)
        {
            switch (field)
            {
                case Field.Move:
                    return "move";
                case Field.Facing:
                    return "facing";
                case Field.Arr:
                    return "arr";
                case Field.Width:
                    return "width";
                case Field.Beh:
                    return "beh";
                case Field.Tactic:
                    return "tactic";
                default:
                    return "note";
            }
        }

        internal static void Start()
        {
            Clear();
            Active = true;
        }

        // Main thread (MissionStartReset, OnEndMission).
        internal static void Clear()
        {
            Active = false;
            States.Clear();
            Rec dropped;
            while (Queue.TryDequeue(out dropped))
            {
            }
            Interlocked.Exchange(ref _queued, 0);
            Interlocked.Exchange(ref _dropped, 0);
            _droppedReported = 0;
            Pendings.Clear();
        }

        // FormationAI's occasional tick (private, not the tiny FormationAI.Tick wrapper).
        [HarmonyPatch(typeof(FormationAI), "TickOccasionally")]
        internal static class FormationAITickPatch
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Formation ____formation, out Frame __state)
            {
                __state = default(Frame);
                if (!Active || ____formation == null)
                {
                    return;
                }
                __state = Enter(Scope.FormAI, ____formation);
                __state.Before = (____formation.AI != null) ? ____formation.AI.ActiveBehavior : null;
            }

            private static void Finalizer(Formation ____formation, Frame __state)
            {
                if (!__state.Pushed)
                {
                    return;
                }
                try
                {
                    if (Active && ____formation != null && ____formation.Team != null)
                    {
                        BehaviorComponent after = (____formation.AI != null) ? ____formation.AI.ActiveBehavior : null;
                        BehaviorComponent before = __state.Before as BehaviorComponent;
                        if (after != before && !Silent())
                        {
                            string name = (after != null) ? after.GetType().Name : "-";
                            Enqueue(new Rec
                            {
                                Formation = ____formation,
                                Time = Now(),
                                Field = Field.Beh,
                                Writer = "formAI",
                                Text = ((before != null) ? before.GetType().Name : "-") + "->" + name,
                                Value = name
                            });
                        }
                        if (____formation.IsAIControlled)
                        {
                            Sweep(____formation, null, true);
                        }
                    }
                }
                catch
                {
                    // AI thread: never into the engine's tick.
                }
                finally
                {
                    Exit(__state);
                }
            }
        }

        [HarmonyPatch(typeof(TeamAIComponent), nameof(TeamAIComponent.TickOccasionally))]
        internal static class TeamTickPatch
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(TeamAIComponent __instance, out Frame __state)
            {
                __state = Enter(Scope.Tactic, __instance);
            }

            private static void Finalizer(Team ___Team, Frame __state)
            {
                if (!__state.Pushed)
                {
                    return;
                }
                try
                {
                    SweepTeam(___Team);
                }
                catch
                {
                }
                finally
                {
                    Exit(__state);
                }
            }
        }

        [HarmonyPatch(typeof(TeamAIComponent), "MakeDecision")]
        internal static class DecidePatch
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(TeamAIComponent __instance, out Frame __state)
            {
                __state = Enter(Scope.Decide, __instance);
                if (__state.Pushed)
                {
                    __state.Before = TacticSelection.CurrentTacticRef(__instance);
                }
            }

            private static void Finalizer(TeamAIComponent __instance, Team ___Team, Frame __state)
            {
                if (!__state.Pushed)
                {
                    return;
                }
                try
                {
                    TacticComponent before = __state.Before as TacticComponent;
                    TacticComponent after = TacticSelection.CurrentTacticRef(__instance);
                    if (Active && after != before && ___Team != null && !Silent())
                    {
                        string text = ((before != null) ? before.GetType().Name : "-") + "->" + ((after != null) ? after.GetType().Name : "-");
                        float now = Now();
                        foreach (Formation formation in ___Team.FormationsIncludingSpecialAndEmpty)
                        {
                            if (formation != null && formation.CountOfUnits > 0)
                            {
                                Enqueue(new Rec { Formation = formation, Time = now, Field = Field.Tactic, Writer = "decide", Text = text });
                            }
                        }
                    }
                    SweepTeam(___Team);
                }
                catch
                {
                }
                finally
                {
                    Exit(__state);
                }
            }
        }

        [HarmonyPatch(typeof(Formation), nameof(Formation.SetArrangementOrder))]
        internal static class ArrangementPatch
        {
            private static void Prefix(Formation __instance, ArrangementOrder order)
            {
                if (!Active)
                {
                    return;
                }
                try
                {
                    if (__instance != null && __instance.Team != null)
                    {
                        OnArrangement(__instance, order.OrderEnum, "");
                    }
                }
                catch
                {
                    // AI thread: never into the engine's tick.
                }
            }
        }

        [HarmonyPatch(typeof(Formation), nameof(Formation.SetFormOrder))]
        internal static class FormOrderPatch
        {
            private static void Prefix(Formation __instance, FormOrder order)
            {
                if (!Active)
                {
                    return;
                }
                try
                {
                    if (__instance != null && __instance.Team != null)
                    {
                        OnFormOrder(__instance, order.OrderEnum, order.CustomFlankWidth, "");
                    }
                }
                catch
                {
                    // AI thread: never into the engine's tick.
                }
            }
        }
    }
}
