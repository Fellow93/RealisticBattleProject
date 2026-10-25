using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// Cavalry ground sense for the cavalry behaviors: each side's charge lanes onto the enemy line and the bad-ground mask, scored
    /// on the terrain read's background task (BattleTerrainRead.Cavalry.cs) and published here with WorldPositions for the lane
    /// run-up points (terrainTacticsEnabled). Off, before the publish or with nothing for the side, every helper returns false / 1.
    /// Use (AI thread): RBMBehaviorCavalryCharge target pick (TargetFactor: bad-ground targets x TargetBadGroundPenalty, lane targets
    /// x TargetLaneBonus); charge start (TryTakeLane: ride to a lane's run-up point first; CheckLaneApproach ends it).
    /// Claims: one formation per lane per team (ClaimKey, TryAdd); a stale owner (empty, holding something else, behavior not
    /// active) loses it.
    /// Use-time checks: a lane only while it still leads onto the target as it stands NOW (LaneFit: aim near the target's current
    /// front, charge direction within LaneStaleAngle of the bearing to its centre); a failed check refuses / releases it ("stale ...").
    /// Friendly overlap (FriendlyOverlap; friends where they stand AND are heading): a run-up point is not taken when more than
    /// TakeMaxShare of the riders' footprint (front-centre at the point, facing the target, kept in Hold.Facing) overlaps a friendly
    /// formation, and is let go above KeepMaxShare.
    /// Re-plan: Tick (MAIN thread) asks its TerrainPlanRefresh, then runs BattleTerrain.ReplanCavalry on a background Task (generation
    /// token) and publishes both sides' next SidePlan versions (bad-ground mask kept). A lane approach of an OLD version, on the AI
    /// thread at the holder's next tick, moves to the new lane onto the same target nearest its old run-up point (LaneAdoptShift,
    /// take rules, Rebase) else is released and the formation charges from where it is. PruneOld drops claims of old points.
    /// THREADING: Build / Publish / Tick / FinishReplan / PruneOld run on the MAIN thread (Build creates and navmesh-checks the
    /// WorldPositions). Every other helper runs on the async AI thread: volatile reads of immutable plans, array reads (IsBadGround),
    /// cached formation positions, concurrent-dictionary claims and a queue; no engine call, no WorldPosition construction (the cached
    /// ones are copied), no MBRandom. BEHAVIOR events are queued and written on the main thread; the overlay reads holds through
    /// CopyHolds. Cleared at mission end and in MissionStartReset.
    /// </summary>
    internal static class TerrainCavalry
    {

        // Target pick (lower score wins): bad-ground targets x TargetBadGroundPenalty, targets a lane runs onto x TargetLaneBonus.
        public const float TargetBadGroundPenalty = 3f;

        public const float TargetLaneBonus = 0.85f;

        // A lane runs onto a formation when its aim is within max(LaneAimMatchMin, half its width + LaneAimMatchMargin) of its centre.
        public const float LaneAimMatchMin = 60f;

        public const float LaneAimMatchMargin = 30f;

        public const float LaneKeepAimFactor = 1.5f; // approach ends when the target is farther than this x the match distance from the aim

        // A lane's run-up point is used only within LaneMaxReach of the formation, at least LaneMinRun from the target
        // (a shorter run is no run-up; closer charges at once) and at most LaneMaxBack farther from it than the formation already is.
        public const float LaneMaxReach = 150f;

        public const float LaneMinRun = 90f;

        public const float LaneMaxBack = 60f;

        public const float LaneThreatDistance = 60f; // no lane is taken, and an approach ends, with an enemy formation this close

        public const float LaneArrive = 30f; // + a quarter of the formation's width

        public const float LaneApproachTimeout = 15f; // s, whatever happens

        public const float LaneRetakeCooldown = 10f;

        // Stale lane: aim farther than LaneStaleFrontMargin + half the target's width from its CURRENT front (cached centre + Direction x depth/2) ...
        public const float LaneStaleFrontMargin = 40f;

        public const float LaneStaleAngle = 30f; // ... or charge direction (aim - run-up point) this many degrees off the bearing run-up point -> target centre

        private static readonly float LaneStaleCos = (float)Math.Cos(LaneStaleAngle * Math.PI / 180.0);

        public const float LaneAdoptShift = 60f; // re-plan: old lane approach moves to a new run-up point only this close to its old one


        private static volatile SidePlan _attacker;
        private static volatile SidePlan _defender;
        private static volatile BattleTerrain.CavalryInfo _ground;


        private static readonly TerrainPlanRefresh Refresh = new TerrainPlanRefresh("cavalry");
        private static int _generation; // bumped in Clear: an older re-plan is dropped
        private static Task<BattleTerrain.CavalryInfo> _replan;
        private static int _replanGeneration;
        private static string _replanReason;
        private static long _replanStartTicks;

        /// <summary>FromAim in metres; World built on the main thread. Immutable.</summary>
        internal sealed class LaneEntry
        {
            public readonly float FromAim;
            public readonly Vec2 Position;
            public readonly WorldPosition World;

            internal LaneEntry(float fromAim, Vec2 position, WorldPosition world)
            {
                FromAim = fromAim;
                Position = position;
                World = world;
            }
        }

        /// <summary>Entries = usable run-up points, start first. Immutable.</summary>
        internal sealed class Lane
        {
            public readonly BattleTerrain.ChargeLane Data;
            public readonly IReadOnlyList<LaneEntry> Entries;

            internal Lane(BattleTerrain.ChargeLane data, IReadOnlyList<LaneEntry> entries)
            {
                Data = data;
                Entries = entries;
            }

            public string Id => Data.Id;
        }

        /// <summary>One side's usable cavalry ground. Immutable.</summary>
        internal sealed class SidePlan
        {
            public readonly BattleSideEnum Side;
            public readonly IReadOnlyList<Lane> Lanes;
            public readonly string Dropped; // snapshot items dropped at the publish ("AL2@150:noNavmesh"), or null
            public readonly int Version; // 1 at the publish, + 1 per re-plan
            public readonly string Note; // publish | replan

            internal SidePlan(BattleSideEnum side, IReadOnlyList<Lane> lanes, string dropped, int version = 1, string note = "publish")
            {
                Side = side;
                Lanes = lanes;
                Dropped = dropped;
                Version = version;
                Note = note;
            }

            public bool Contains(Lane lane)
            {
                foreach (Lane l in Lanes)
                {
                    if (l == lane)
                    {
                        return true;
                    }
                }
                return false;
            }

            public bool ContainsPoint(object point)
            {
                Lane lane = point as Lane;
                return lane != null && Contains(lane);
            }
        }

        /// <summary>The side's plan as the behaviors read it: null with terrainTacticsEnabled off, before the publish, or for another side. Any thread.</summary>
        public static SidePlan ForSide(BattleSideEnum side)
        {
            // None for a side without terrain plans (ArmyScale).
            if (!RBMConfig.RBMConfig.terrainTacticsEnabled || ArmyScale.NoPlans(side))
            {
                return null;
            }
            return Published(side);
        }

        public static SidePlan ForTeam(Team team)
        {
            return (team != null) ? ForSide(team.Side) : null;
        }

        /// <summary>Whatever the toggle (log and map).</summary>
        internal static SidePlan Published(BattleSideEnum side)
        {
            return (side == BattleSideEnum.Attacker) ? _attacker : (side == BattleSideEnum.Defender) ? _defender : null;
        }

        /// <summary>Bad ground under p (steep, wet / deep, no navmesh, cramped among obstacles, as a share of the cells around it). False with terrainTacticsEnabled off, before the publish or outside the map. Any thread: a volatile read and array reads.</summary>
        public static bool IsBadGround(Vec2 p)
        {
            if (!RBMConfig.RBMConfig.terrainTacticsEnabled)
            {
                return false;
            }
            BattleTerrain.CavalryInfo ground = _ground;
            return ground != null && ground.IsBadGround(p);
        }

        /// <summary>MAIN thread, at the terrain publish: both sides' plans from the snapshot, each lane run-up point with a navmesh-checked WorldPosition; points outside the boundary or off the navmesh are dropped.</summary>
        internal static void Build(Mission mission, BattleTerrain terrain, out SidePlan attacker, out SidePlan defender)
        {
            attacker = null;
            defender = null;
            if (mission == null || mission.Scene == null || terrain == null)
            {
                return;
            }
            attacker = BuildSide(mission, terrain, BattleSideEnum.Attacker, terrain.Cavalry.Attacker, 1, "publish");
            defender = BuildSide(mission, terrain, BattleSideEnum.Defender, terrain.Cavalry.Defender, 1, "publish");
        }

        /// <summary>MAIN thread: one side's plan (the publish's or a re-plan's) with its WorldPositions; null without data.</summary>
        private static SidePlan BuildSide(Mission mission, BattleTerrain terrain, BattleSideEnum side, BattleTerrain.SideCavalry data, int version, string note)
        {
            if (data == null)
            {
                return null;
            }
            StringBuilder dropped = null;
            Action<string, string> drop = (id, why) =>
            {
                dropped = (dropped == null) ? new StringBuilder() : dropped.Append(' ');
                dropped.Append(id).Append(':').Append(why);
            };
            List<Lane> lanes = new List<Lane>();
            foreach (BattleTerrain.ChargeLane lane in data.Lanes)
            {
                List<LaneEntry> entries = new List<LaneEntry>();
                foreach (float fromAim in lane.Entries)
                {
                    Vec2 p = lane.Aim + lane.Direction * fromAim;
                    string why;
                    WorldPosition world = TerrainBattlePosition.MakeWorld(mission, terrain, p, out why);
                    if (why != null)
                    {
                        drop(lane.Id + "@" + fromAim.ToString("0", CultureInfo.InvariantCulture), why);
                        continue;
                    }
                    entries.Add(new LaneEntry(fromAim, p, world));
                }
                if (entries.Count > 0)
                {
                    lanes.Add(new Lane(lane, Array.AsReadOnly(entries.ToArray())));
                }
            }
            return new SidePlan(side, Array.AsReadOnly(lanes.ToArray()), dropped?.ToString(), version, note);
        }

        /// <summary>Main thread, at the terrain publish.</summary>
        internal static void Publish(SidePlan attacker, SidePlan defender, BattleTerrain.CavalryInfo ground)
        {
            _attacker = attacker;
            _defender = defender;
            _ground = (ground != null && ground.BadMask != null) ? ground : null;
        }

        /// <summary>Main thread: mission end and MissionStartReset. Also drops the holds, claims, queued events and the re-plan state (a running re-plan is abandoned by generation).</summary>
        public static void Clear()
        {
            _attacker = null;
            _defender = null;
            _ground = null;
            Holds.Clear();
            Owners.Clear();
            LaneCooldownUntil.Clear();
            LoggedKeys.Clear();
            UseEvent ignored;
            while (UseEvents.TryDequeue(out ignored))
            {
            }
            _generation++;
            if (_replan != null)
            {
                // Observe a late fault so it is never reported as an unobserved task exception.
                _replan.ContinueWith(t => { var unused = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                _replan = null;
            }
            _replanReason = null;
            Refresh.Clear();
        }

        // re-plan (MAIN thread)

        /// <summary>MAIN thread, every tick after the publish: finishes a running re-plan or starts one when TerrainPlanRefresh says it is due. Adds the log lines' fields to lines; runs whether or not the log is on. Nothing with terrainTacticsEnabled off or without a published plan.</summary>
        internal static void Tick(Mission mission, BattleTerrain terrain, float now, List<string> lines)
        {
            if (mission == null || terrain == null)
            {
                return;
            }
            if (_replan != null)
            {
                FinishReplan(mission, terrain, lines);
                return;
            }
            // No re-plan work when neither side uses the cavalry ground (ArmyScale).
            if (!RBMConfig.RBMConfig.terrainTacticsEnabled || (_attacker == null && _defender == null)
                || (ArmyScale.NoPlans(BattleSideEnum.Attacker) && ArmyScale.NoPlans(BattleSideEnum.Defender)))
            {
                return;
            }
            TerrainPlanRefresh.Armies armies;
            string note;
            string reason = Refresh.Check(mission, now, out armies, out note);
            if (note != null)
            {
                lines.Add("CAVALRY\t" + note);
            }
            if (reason == null)
            {
                return;
            }
            Refresh.Started(now, armies);
            _replanGeneration = _generation;
            _replanStartTicks = DateTime.UtcNow.Ticks;
            _replanReason = reason;
            Vec2 attacker = armies.Attacker;
            Vec2 defender = armies.Defender;
            Vec2 attackerAcross = armies.AttackerAcross;
            Vec2 defenderAcross = armies.DefenderAcross;
            // Background: the read's FindCavalry over the immutable snapshot only (no engine call).
            _replan = Task.Run(() => terrain.ReplanCavalry(attacker, defender, attackerAcross, defenderAcross));
        }

        /// <summary>Main thread: builds and publishes a finished re-plan (WorldPositions here, as Build), then prunes old claims.</summary>
        private static void FinishReplan(Mission mission, BattleTerrain terrain, List<string> lines)
        {
            Task<BattleTerrain.CavalryInfo> task = _replan;
            if (!task.IsCompleted)
            {
                return;
            }
            _replan = null;
            if (_replanGeneration != _generation)
            {
                return;
            }
            string ms = ((DateTime.UtcNow.Ticks - _replanStartTicks) / (double)TimeSpan.TicksPerMillisecond).ToString("0", CultureInfo.InvariantCulture);
            if (task.IsFaulted || task.IsCanceled)
            {
                Exception e = (task.Exception != null) ? task.Exception.GetBaseException() : new OperationCanceledException("cavalry re-plan canceled");
                lines.Add("CAVALRY\treplan\tfailed=" + e.GetType().Name + ":" + BattleTerrainRead.Sanitize(e.Message) + "\treason=" + _replanReason + "\twallMs=" + ms);
                return;
            }
            BattleTerrain.CavalryInfo info = task.Result;
            if (info == null || mission.Scene == null)
            {
                return;
            }
            SidePlan oldAttacker = _attacker;
            SidePlan oldDefender = _defender;
            // A side the worker could not plan keeps its current version.
            SidePlan attacker = (info.Attacker != null)
                ? BuildSide(mission, terrain, BattleSideEnum.Attacker, info.Attacker, (oldAttacker != null) ? oldAttacker.Version + 1 : 1, "replan") : oldAttacker;
            SidePlan defender = (info.Defender != null)
                ? BuildSide(mission, terrain, BattleSideEnum.Defender, info.Defender, (oldDefender != null) ? oldDefender.Version + 1 : 1, "replan") : oldDefender;
            _attacker = attacker;
            _defender = defender;
            int pruned = PruneOld();
            foreach (BattleSideEnum side in new[] { BattleSideEnum.Attacker, BattleSideEnum.Defender })
            {
                BattleTerrain.SideCavalry data = (side == BattleSideEnum.Attacker) ? info.Attacker : info.Defender;
                SidePlan plan = (side == BattleSideEnum.Attacker) ? attacker : defender;
                lines.Add("CAVALRY\treplan\t" + side + "\treason=" + _replanReason + "\twallMs=" + ms + "\tpruned=" + pruned.ToString(CultureInfo.InvariantCulture) + "\t"
                    + ((data != null) ? Describe(info, data, plan) : "failed=" + (info.Error ?? "?") + " kept v" + ((plan != null) ? plan.Version.ToString(CultureInfo.InvariantCulture) : "-")));
            }
        }

        /// <summary>MAIN thread, after a re-plan's publish: drops claims on points of no current version that no hold uses, and holds of old points whose formation is empty or whose behavior is no longer active. Conditional removes only, so a concurrent take / move on the AI thread is never undone. Returns how many were dropped.</summary>
        private static int PruneOld()
        {
            SidePlan attacker = _attacker;
            SidePlan defender = _defender;
            int dropped = 0;
            float now = Now();
            foreach (KeyValuePair<Formation, Hold> entry in Holds)
            {
                Hold hold = entry.Value;
                if (hold == null || IsCurrent(hold.Team, hold.Point, attacker, defender))
                {
                    continue;
                }
                Formation formation = entry.Key;
                if (formation == null || formation.CountOfUnits <= 0 || formation.AI == null || formation.AI.ActiveBehavior != hold.Behavior)
                {
                    ReleaseHold(hold, "replan inactive", now);
                    dropped++;
                }
            }
            foreach (KeyValuePair<ClaimKey, Formation> entry in Owners)
            {
                ClaimKey key = entry.Key;
                if (IsCurrent(key.Team, key.Point, attacker, defender))
                {
                    continue;
                }
                Hold hold;
                if (entry.Value != null && Holds.TryGetValue(entry.Value, out hold) && hold != null && hold.Point == key.Point)
                {
                    continue;
                }
                if (((ICollection<KeyValuePair<ClaimKey, Formation>>)Owners).Remove(entry))
                {
                    dropped++;
                }
            }
            return dropped;
        }

        private static bool IsCurrent(Team team, object point, SidePlan attacker, SidePlan defender)
        {
            SidePlan plan = (team == null) ? null : (team.Side == BattleSideEnum.Attacker) ? attacker : (team.Side == BattleSideEnum.Defender) ? defender : null;
            return plan != null && plan.ContainsPoint(point);
        }

        // holds and claims (async AI thread)

        internal enum HoldKind
        {
            Lane
        }

        /// <summary>A formation's lane approach. Immutable once added.</summary>
        internal sealed class Hold
        {
            public Formation Formation;
            public Team Team;
            public HoldKind Kind;
            public object Point; // the Lane
            public string Id;
            public Vec2 Position;
            public Vec2 Facing; // friendly-overlap footprint, same at take and keep
            public LaneEntry Entry;
            public BehaviorComponent Behavior;
            public string BehaviorName;
            public float Since;
        }

        private struct ClaimKey : IEquatable<ClaimKey>
        {
            public readonly Team Team;
            public readonly object Point;

            public ClaimKey(Team team, object point)
            {
                Team = team;
                Point = point;
            }

            public bool Equals(ClaimKey other)
            {
                return ReferenceEquals(Team, other.Team) && ReferenceEquals(Point, other.Point);
            }

            public override bool Equals(object obj)
            {
                return obj is ClaimKey && Equals((ClaimKey)obj);
            }

            public override int GetHashCode()
            {
                int a = (Team != null) ? Team.GetHashCode() : 0;
                int b = (Point != null) ? Point.GetHashCode() : 0;
                return (a * 397) ^ b;
            }
        }

        private static readonly ConcurrentDictionary<Formation, Hold> Holds = new ConcurrentDictionary<Formation, Hold>();
        private static readonly ConcurrentDictionary<ClaimKey, Formation> Owners = new ConcurrentDictionary<ClaimKey, Formation>();
        private static readonly ConcurrentDictionary<Formation, float> LaneCooldownUntil = new ConcurrentDictionary<Formation, float>();
        private static readonly ConcurrentDictionary<string, byte> LoggedKeys = new ConcurrentDictionary<string, byte>();
        private static readonly ConcurrentQueue<UseEvent> UseEvents = new ConcurrentQueue<UseEvent>();

        internal sealed class UseEvent
        {
            public Team Team;
            public int FormationIndex;
            public float Time;
            public string Behavior;
            public string What;
            public string PointId;
            public string Why;
        }

        /// <summary>Main thread (terrain log).</summary>
        internal static bool TryDequeueUse(out UseEvent e)
        {
            return UseEvents.TryDequeue(out e);
        }

        /// <summary>Main thread (the map): every current hold of a non-empty formation whose behavior is still its active one.</summary>
        internal static void CopyHolds(List<Hold> into)
        {
            into.Clear();
            foreach (KeyValuePair<Formation, Hold> entry in Holds)
            {
                Hold hold = entry.Value;
                if (hold != null && entry.Key != null && entry.Key.CountOfUnits > 0 && entry.Key.AI != null && entry.Key.AI.ActiveBehavior == hold.Behavior)
                {
                    into.Add(hold);
                }
            }
        }

        internal static Hold HeldBy(BehaviorComponent behavior, HoldKind kind)
        {
            Formation formation = behavior?.Formation;
            Hold hold;
            if (formation == null || !Holds.TryGetValue(formation, out hold) || hold == null || hold.Behavior != behavior || hold.Kind != kind)
            {
                return null;
            }
            return hold;
        }

        /// <summary>AI thread (or wherever native cancels a behavior): drops the formation's hold when this behavior has it.</summary>
        internal static void Release(BehaviorComponent behavior, string why)
        {
            Formation formation = behavior?.Formation;
            Hold hold;
            if (formation == null || !Holds.TryGetValue(formation, out hold) || hold == null || hold.Behavior != behavior)
            {
                return;
            }
            ReleaseHold(hold, why, Now());
        }

        private static void ReleaseHold(Hold hold, string why, float now)
        {
            // Conditional removes: never undo a hold another thread just made.
            if (!((ICollection<KeyValuePair<Formation, Hold>>)Holds).Remove(new KeyValuePair<Formation, Hold>(hold.Formation, hold)))
            {
                return;
            }
            ((ICollection<KeyValuePair<ClaimKey, Formation>>)Owners).Remove(new KeyValuePair<ClaimKey, Formation>(new ClaimKey(hold.Team, hold.Point), hold.Formation));
            if (hold.Kind == HoldKind.Lane)
            {
                LaneCooldownUntil[hold.Formation] = now + LaneRetakeCooldown;
            }
            Enqueue(hold.Team, hold.Formation, now, hold.BehaviorName, "release" + KindName(hold.Kind), hold.Id, why + " held=" + Fmt0(now - hold.Since) + "s");
        }

        /// <summary>True when another formation of the team really uses the point; a stale owner loses it here.</summary>
        private static bool ClaimedByOther(Team team, object point, Formation formation, float now)
        {
            ClaimKey key = new ClaimKey(team, point);
            Formation owner;
            if (!Owners.TryGetValue(key, out owner) || owner == formation)
            {
                return false;
            }
            Hold ownerHold = null;
            bool holds = owner != null && Holds.TryGetValue(owner, out ownerHold) && ownerHold != null && ownerHold.Point == point;
            if (holds && owner.CountOfUnits > 0 && owner.AI != null && owner.AI.ActiveBehavior == ownerHold.Behavior)
            {
                return true;
            }
            if (holds)
            {
                ReleaseHold(ownerHold, "stale", now);
            }
            ((ICollection<KeyValuePair<ClaimKey, Formation>>)Owners).Remove(new KeyValuePair<ClaimKey, Formation>(key, owner));
            return false;
        }

        private static bool Take(BehaviorComponent behavior, string behaviorName, HoldKind kind, object point, string id, Vec2 position, Vec2 facing, LaneEntry entry, float now, string why)
        {
            Formation formation = behavior.Formation;
            Team team = formation.Team;
            Hold old;
            if (Holds.TryGetValue(formation, out old) && old != null)
            {
                ReleaseHold(old, "replaced", now);
            }
            ClaimKey key = new ClaimKey(team, point);
            if (!Owners.TryAdd(key, formation))
            {
                Formation current;
                if (!(Owners.TryGetValue(key, out current) && current == formation))
                {
                    return false;
                }
            }
            Holds[formation] = new Hold
            {
                Formation = formation,
                Team = team,
                Kind = kind,
                Point = point,
                Id = id,
                Position = position,
                Facing = facing,
                Entry = entry,
                Behavior = behavior,
                BehaviorName = behaviorName,
                Since = now
            };
            Enqueue(team, formation, now, behaviorName, "take" + KindName(kind), id, why);
            return true;
        }

        /// <summary>AI thread: moves a hold of an OLD plan version onto a point of the current one (same kind, behavior and Since; claim moved with it). Null (old hold released, "replan claimed") when another formation really uses the new point; null without a release when the hold changed meanwhile.</summary>
        private static Hold Rebase(Hold old, object point, string id, Vec2 position, Vec2 facing, LaneEntry entry, float now, string why)
        {
            Formation formation = old.Formation;
            Team team = old.Team;
            ClaimKey key = new ClaimKey(team, point);
            if (ClaimedByOther(team, point, formation, now))
            {
                ReleaseHold(old, "replan claimed(" + id + ")", now);
                return null;
            }
            bool added = Owners.TryAdd(key, formation);
            Formation current;
            if (!added && !(Owners.TryGetValue(key, out current) && current == formation))
            {
                ReleaseHold(old, "replan claimed(" + id + ")", now);
                return null;
            }
            Hold next = new Hold
            {
                Formation = formation,
                Team = team,
                Kind = old.Kind,
                Point = point,
                Id = id,
                Position = position,
                Facing = facing,
                Entry = entry,
                Behavior = old.Behavior,
                BehaviorName = old.BehaviorName,
                Since = old.Since
            };
            if (!Holds.TryUpdate(formation, next, old))
            {
                if (added)
                {
                    ((ICollection<KeyValuePair<ClaimKey, Formation>>)Owners).Remove(new KeyValuePair<ClaimKey, Formation>(key, formation));
                }
                return null;
            }
            ((ICollection<KeyValuePair<ClaimKey, Formation>>)Owners).Remove(new KeyValuePair<ClaimKey, Formation>(new ClaimKey(team, old.Point), formation));
            Enqueue(team, formation, now, old.BehaviorName, "rebase" + KindName(old.Kind), id, why + " was=" + old.Id + " shift=" + Fmt0(old.Position.Distance(position)) + "m");
            return next;
        }

        /// <summary>A re-plan replaced the hold's point. False without a plan.</summary>
        private static bool IsOldVersion(Hold hold, SidePlan plan)
        {
            return hold != null && plan != null && !plan.ContainsPoint(hold.Point);
        }

        private static bool IsActive(BehaviorComponent behavior)
        {
            Formation formation = behavior.Formation;
            return formation != null && formation.AI != null && formation.AI.ActiveBehavior == behavior;
        }

        // target pick (RBMBehaviorCavalryCharge.AcquireTarget)

        /// <summary>AI thread: the factor for a charge target's score (lower wins): TargetBadGroundPenalty on bad ground, x TargetLaneBonus when a lane runs onto it; 1 with the toggle off or no plan. note = "badGround", "lane AL1", both, or null.</summary>
        internal static float TargetFactor(Team team, Formation candidate, out string note)
        {
            note = null;
            SidePlan plan = ForTeam(team);
            if (plan == null || candidate == null)
            {
                return 1f;
            }
            float factor = 1f;
            Vec2 median = candidate.CachedMedianPosition.AsVec2;
            if (IsBadGround(median))
            {
                factor *= TargetBadGroundPenalty;
                note = "badGround";
            }
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(candidate);
            float match = AimMatch(candidate);
            foreach (Lane lane in plan.Lanes)
            {
                if (lane.Data.Aim.Distance(centre) <= match)
                {
                    factor *= TargetLaneBonus;
                    note = (note == null ? "" : note + "+") + "lane " + lane.Id;
                    break;
                }
            }
            return factor;
        }

        /// <summary>AI thread: one BEHAVIOR "target" line per formation and text (the target pick changed by the terrain, a bad-ground target down-weighted).</summary>
        internal static void NoteTarget(BehaviorComponent behavior, string behaviorName, string what, string text)
        {
            Formation formation = behavior?.Formation;
            if (formation == null || formation.Team == null || !RBMConfig.RBMConfig.battleTerrainLoggingEnabled || !IsActive(behavior))
            {
                return;
            }
            string key = formation.Team.TeamIndex.ToString(CultureInfo.InvariantCulture) + "/" + ((int)formation.FormationIndex).ToString(CultureInfo.InvariantCulture)
                + "/" + what + "/" + text;
            if (LoggedKeys.TryAdd(key, 0))
            {
                Enqueue(formation.Team, formation, Now(), behaviorName, what, "-", text);
            }
        }

        /// <summary>AI thread: one BEHAVIOR line every call (no de-duplication) while the behavior is active (or always: requireActive false), for rare decisions (a chase given up, a reform point).</summary>
        internal static void NoteEvent(BehaviorComponent behavior, string behaviorName, string what, string text, bool requireActive = true)
        {
            Formation formation = behavior?.Formation;
            if (formation == null || formation.Team == null || (requireActive && !IsActive(behavior)))
            {
                return;
            }
            // Enqueue notes the decision trace and drops the line with the log off
            Enqueue(formation.Team, formation, Now(), behaviorName, what, "-", text);
        }

        /// <summary>"f3:Infantry".</summary>
        internal static string FormationName(Formation formation)
        {
            return (formation == null) ? "-" : "f" + ((int)formation.FormationIndex).ToString(CultureInfo.InvariantCulture) + ":" + formation.RepresentativeClass;
        }

        private static float AimMatch(Formation target)
        {
            return Math.Max(LaneAimMatchMin, target.Width * 0.5f + LaneAimMatchMargin);
        }

        // charge lanes (RBMBehaviorCavalryCharge)

        /// <summary>AI thread, when a charge starts: true and the lane's run-up point (its cached WorldPosition is entry.World) when the formation takes a lane onto the target.</summary>
        internal static bool TryTakeLane(BehaviorComponent behavior, string behaviorName, Formation target, out Lane lane, out LaneEntry entry)
        {
            lane = null;
            entry = null;
            Formation formation = behavior?.Formation;
            if (formation == null || formation.Team == null || target == null || target.CountOfUnits <= 0 || formation.CountOfUnits <= 0)
            {
                return false;
            }
            Team team = formation.Team;
            SidePlan plan = ForTeam(team);
            if (plan == null || plan.Lanes.Count == 0 || !IsActive(behavior))
            {
                return false;
            }
            float now = Now();
            float until;
            if (LaneCooldownUntil.TryGetValue(formation, out until) && now < until)
            {
                return false;
            }
            Vec2 us = RBMAI.Utilities.GetFormationCenter(formation);
            Vec2 targetAt = RBMAI.Utilities.GetFormationCenter(target);
            float usToTarget = us.Distance(targetAt);
            if (usToTarget < LaneMinRun)
            {
                return false;
            }
            float match = AimMatch(target);
            StringBuilder refusal = null;
            bool logging = RBMConfig.RBMConfig.battleTerrainLoggingEnabled;
            int staleCode;
            float staleGap, staleLimit, staleAngle;
            Formation friend;
            float friendShare;
            foreach (Lane candidate in plan.Lanes)
            {
                string why = null;
                LaneEntry best = null;
                if (candidate.Data.Aim.Distance(targetAt) > match)
                {
                    why = "aimOff";
                }
                else
                {
                    // The run-up point nearest the formation's own distance to the aim (ride onto the lane, not back up it).
                    float wanted = us.Distance(candidate.Data.Aim);
                    float bestGap = float.MaxValue;
                    foreach (LaneEntry e in candidate.Entries)
                    {
                        if (e.Position.Distance(targetAt) < LaneMinRun)
                        {
                            continue;
                        }
                        float gap = Math.Abs(e.FromAim - wanted);
                        if (gap < bestGap)
                        {
                            bestGap = gap;
                            best = e;
                        }
                    }
                    if (best == null)
                    {
                        why = "short";
                    }
                    else if (best.Position.Distance(us) > LaneMaxReach)
                    {
                        why = "far";
                    }
                    else if (best.Position.Distance(targetAt) > usToTarget + LaneMaxBack)
                    {
                        why = "behind";
                    }
                    else if ((staleCode = LaneFit(candidate, best, target, out staleGap, out staleLimit, out staleAngle)) != 0)
                    {
                        why = "stale(" + StaleText(staleCode, staleGap, staleLimit, staleAngle) + ")";
                    }
                    else if (NearestEnemyDistance(team, best.Position) < LaneThreatDistance)
                    {
                        why = "enemyNear";
                    }
                    else if ((friendShare = FriendlyOverlap.Share(formation, best.Position, Bearing(best.Position, targetAt), null, FriendlyOverlap.Mode.NowAndHeading, out friend))
                        > FriendlyOverlap.TakeMaxShare)
                    {
                        // User rule: not a run-up point where the riders would form on a friendly formation.
                        why = FriendlyOverlap.RefusalText(friend, friendShare);
                    }
                    else if (ClaimedByOther(team, candidate, formation, now))
                    {
                        why = "claimed";
                    }
                }
                if (why == null)
                {
                    string text = "target=" + FormationName(target) + " run=" + Fmt0(best.Position.Distance(targetAt)) + "m shift=" + Fmt0(us.Distance(best.Position))
                        + "m len=" + Fmt0(candidate.Data.Length) + " drop=" + Fmt1(candidate.Data.Drop);
                    if (Take(behavior, behaviorName, HoldKind.Lane, candidate, candidate.Id, best.Position, Bearing(best.Position, targetAt), best, now, text))
                    {
                        lane = candidate;
                        entry = best;
                        return true;
                    }
                    why = "claimed";
                }
                if (logging)
                {
                    refusal = (refusal == null) ? new StringBuilder() : refusal.Append(' ');
                    refusal.Append(candidate.Id).Append(':').Append(why);
                }
            }
            if (refusal != null)
            {
                NoteTarget(behavior, behaviorName, "refuseLane", refusal.ToString());
            }
            return false;
        }

        /// <summary>AI thread, every tick of a lane approach: true to keep riding to the run-up point; false (lane released, why logged) when it is over: arrived, timeout, enemy close, target gone or moved off the lane, lane stale (LaneFit), inactive, no plan.</summary>
        internal static bool CheckLaneApproach(BehaviorComponent behavior, Formation target, bool inMelee)
        {
            Hold hold = HeldBy(behavior, HoldKind.Lane);
            if (hold == null)
            {
                return false;
            }
            string why = null;
            Formation formation = behavior.Formation;
            float now = Now();
            SidePlan plan = ForTeam(formation.Team);
            if (IsOldVersion(hold, plan))
            {
                // A re-plan replaced the lane: ride on to the nearest new lane onto the same target, else charge from here.
                hold = AdoptLane(hold, plan, target, now);
                if (hold == null)
                {
                    return false;
                }
            }
            Lane lane = hold.Point as Lane;
            int staleCode;
            float staleGap, staleLimit, staleAngle;
            Vec2 us = RBMAI.Utilities.GetFormationCenter(formation);
            if (plan == null)
            {
                why = "noPlan";
            }
            else if (!IsActive(behavior))
            {
                why = "inactive";
            }
            else if (target == null || target.CountOfUnits <= 0)
            {
                why = "targetGone";
            }
            else if (lane == null || lane.Data.Aim.Distance(RBMAI.Utilities.GetFormationCenter(target)) > AimMatch(target) * LaneKeepAimFactor)
            {
                why = "targetMoved";
            }
            else if ((staleCode = LaneFit(lane, hold.Entry, target, out staleGap, out staleLimit, out staleAngle)) != 0)
            {
                // The lane no longer leads onto the target as it stands now: charge it straight from here.
                why = "stale " + StaleText(staleCode, staleGap, staleLimit, staleAngle);
            }
            else if (us.Distance(hold.Position) <= LaneArrive + formation.Width * 0.25f)
            {
                why = "arrived";
            }
            else if (inMelee)
            {
                why = "melee";
            }
            else if (now - hold.Since > LaneApproachTimeout)
            {
                why = "timeout";
            }
            else if (NearestEnemyDistance(formation.Team, us) < LaneThreatDistance)
            {
                why = "enemyClose";
            }
            else
            {
                // User rule: a friendly formation now stands on / heads onto the run-up point: charge from here.
                Formation friend;
                float share = FriendlyOverlap.Share(formation, hold.Position, hold.Facing, null, FriendlyOverlap.Mode.NowAndHeading, out friend);
                if (share > FriendlyOverlap.KeepMaxShare)
                {
                    why = FriendlyOverlap.Describe(friend, share);
                }
            }
            if (why == null)
            {
                return true;
            }
            ReleaseHold(hold, why, now);
            return false;
        }

        /// <summary>AI thread, a lane approach whose lane a re-plan replaced: the new version's run-up point nearest the old one (within LaneAdoptShift) on a lane still running onto the target (aim within the keep distance, LaneFit, at least LaneMinRun, no enemy within LaneThreatDistance), the hold moved there (Rebase); else the hold is released ("replan ...") and null returned.</summary>
        private static Hold AdoptLane(Hold hold, SidePlan plan, Formation target, float now)
        {
            if (target == null || target.CountOfUnits <= 0)
            {
                ReleaseHold(hold, "replan targetGone", now);
                return null;
            }
            Vec2 targetAt = RBMAI.Utilities.GetFormationCenter(target);
            float keep = AimMatch(target) * LaneKeepAimFactor;
            Lane bestLane = null;
            LaneEntry best = null;
            float bestShift = LaneAdoptShift;
            float gap, limit, angle;
            Formation friend;
            foreach (Lane lane in plan.Lanes)
            {
                if (lane.Data.Aim.Distance(targetAt) > keep)
                {
                    continue;
                }
                foreach (LaneEntry e in lane.Entries)
                {
                    float shift = e.Position.Distance(hold.Position);
                    if (shift > bestShift || e.Position.Distance(targetAt) < LaneMinRun || LaneFit(lane, e, target, out gap, out limit, out angle) != 0
                        || NearestEnemyDistance(hold.Team, e.Position) < LaneThreatDistance
                        || FriendlyOverlap.Share(hold.Formation, e.Position, Bearing(e.Position, targetAt), null, FriendlyOverlap.Mode.NowAndHeading, out friend) > FriendlyOverlap.TakeMaxShare)
                    {
                        continue;
                    }
                    bestShift = shift;
                    best = e;
                    bestLane = lane;
                }
            }
            if (best == null)
            {
                ReleaseHold(hold, "replan noLane(v" + plan.Version.ToString(CultureInfo.InvariantCulture) + ")", now);
                return null;
            }
            return Rebase(hold, bestLane, bestLane.Id, best.Position, Bearing(best.Position, targetAt), best, now, "replan v" + plan.Version.ToString(CultureInfo.InvariantCulture));
        }

        // shared

        private static float Now()
        {
            Mission mission = Mission.Current;
            return (mission != null) ? mission.CurrentTime : 0f;
        }

        /// <summary>Unit vector from to to (the riders' facing at a point: toward the target); invalid when they coincide.</summary>
        private static Vec2 Bearing(Vec2 from, Vec2 to)
        {
            Vec2 d = to - from;
            return (d.LengthSquared > 1f) ? d.Normalized() : Vec2.Invalid;
        }

        /// <summary>MaxValue with none.</summary>
        private static float NearestEnemyDistance(Team team, Vec2 p)
        {
            float best = float.MaxValue;
            Mission mission = Mission.Current;
            if (mission == null || team == null)
            {
                return best;
            }
            foreach (Team other in mission.Teams)
            {
                if (other == null || !other.IsEnemyOf(team))
                {
                    continue;
                }
                foreach (Formation enemy in other.FormationsIncludingEmpty)
                {
                    if (enemy == null || enemy.CountOfUnits <= 0)
                    {
                        continue;
                    }
                    float d = enemy.CachedAveragePosition.Distance(p);
                    if (d < best)
                    {
                        best = d;
                    }
                }
            }
            return best;
        }

        /// <summary>AI thread: does the lane still lead onto the target as it stands now? 0 = yes; 1 = its aim is more than LaneStaleFrontMargin + half the target's width from the target's current front (cached centre + Direction x depth/2); 2 = its charge direction from entry (null: not tested) is more than LaneStaleAngle off the bearing to the target's centre. Cached data only, no allocation.</summary>
        private static int LaneFit(Lane lane, LaneEntry entry, Formation target, out float gap, out float limit, out float angle)
        {
            gap = float.NaN;
            limit = float.NaN;
            angle = float.NaN;
            if (lane == null || target == null)
            {
                return 0;
            }
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(target);
            if (!centre.IsValid)
            {
                return 0;
            }
            Vec2 front = centre;
            Vec2 facing = target.Direction;
            if (facing.IsValid && facing.LengthSquared > 0.01f)
            {
                front = centre + facing.Normalized() * (target.Depth * 0.5f);
            }
            limit = LaneStaleFrontMargin + target.Width * 0.5f;
            gap = lane.Data.Aim.Distance(front);
            if (gap > limit)
            {
                return 1;
            }
            if (entry == null)
            {
                return 0;
            }
            Vec2 bearing = centre - entry.Position;
            if (bearing.Normalize() < 1f)
            {
                return 0;
            }
            Vec2 charge = -lane.Data.Direction;
            float dot = Vec2.DotProduct(charge, bearing);
            if (dot >= LaneStaleCos)
            {
                return 0;
            }
            angle = (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, dot))) * 180.0 / Math.PI);
            return 2;
        }

        /// <summary>Log text for a LaneFit result: "aim 85m>62m" / "angle 41deg>30deg".</summary>
        private static string StaleText(int code, float gap, float limit, float angle)
        {
            return (code == 1) ? "aim " + Fmt0(gap) + "m>" + Fmt0(limit) + "m"
                : (code == 2) ? "angle " + Fmt0(angle) + "deg>" + Fmt0(LaneStaleAngle) + "deg"
                : "noTarget";
        }

        private static void Enqueue(Team team, Formation formation, float now, string behavior, string what, string pointId, string why)
        {
            DecisionTrace.Note(formation, behavior, what, why);
            if (!RBMConfig.RBMConfig.battleTerrainLoggingEnabled)
            {
                return;
            }
            UseEvents.Enqueue(new UseEvent
            {
                Team = team,
                FormationIndex = (int)formation.FormationIndex,
                Time = now,
                Behavior = behavior,
                What = what,
                PointId = pointId,
                Why = why
            });
        }

        private static string KindName(HoldKind kind)
        {
            return "Lane";
        }

        // log text (main thread)

        /// <summary>Fields after "CAVALRY\tside".</summary>
        internal static string Describe(BattleTerrain.CavalryInfo info, BattleTerrain.SideCavalry side, SidePlan plan)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("applied=").Append(RBMConfig.RBMConfig.terrainTacticsEnabled ? 1 : 0);
            sb.Append("\tbadField=").Append(Fmt2(info.BadShareField)).Append("\tbadMap=").Append(Fmt2(info.BadShareMap));
            sb.Append("\tlanesTried=").Append(side.LanesTried.ToString(CultureInfo.InvariantCulture))
              .Append("/short:").Append(side.LanesShort.ToString(CultureInfo.InvariantCulture));
            sb.Append("\tlaneEdge=trimmed:").Append(side.LanesEdgeTrimmed.ToString(CultureInfo.InvariantCulture))
              .Append("/dropped:").Append(side.LanesEdgeDropped.ToString(CultureInfo.InvariantCulture));
            sb.Append("\tms=").Append(Fmt2(info.Ms));
            if (info.Error != null)
            {
                sb.Append("\terror=").Append(info.Error);
            }
            sb.Append("\tlanes=");
            if (side.Lanes.Count == 0)
            {
                sb.Append('-');
            }
            for (int i = 0; i < side.Lanes.Count; i++)
            {
                BattleTerrain.ChargeLane l = side.Lanes[i];
                if (i > 0)
                {
                    sb.Append(' ');
                }
                sb.Append(l.Id).Append("[start=").Append(Fmt0(l.Start.x)).Append(',').Append(Fmt0(l.Start.y))
                  .Append(" aim=").Append(Fmt0(l.Aim.x)).Append(',').Append(Fmt0(l.Aim.y))
                  .Append(" off=").Append(Fmt2(l.Offset))
                  .Append(" len=").Append(Fmt0(l.Length)).Append(" end=").Append(l.End.ToString().ToLowerInvariant())
                  .Append(" bumps=").Append(l.Bumps.ToString(CultureInfo.InvariantCulture))
                  .Append(" drop=").Append(Fmt1(l.Drop))
                  .Append(" wet=").Append(Fmt0(l.WetMetres)).Append(" bad=").Append(Fmt0(l.BadMetres))
                  .Append(" entries=").Append(l.Entries.Count.ToString(CultureInfo.InvariantCulture))
                  .Append(" score=").Append(Fmt2(l.Score)).Append(']');
            }
            sb.Append("\tusable=lanes:").Append((plan != null) ? plan.Lanes.Count.ToString(CultureInfo.InvariantCulture) : "0");
            if (plan != null && plan.Dropped != null)
            {
                sb.Append("\tdropped=").Append(plan.Dropped);
            }
            if (plan != null)
            {
                sb.Append("\tv=").Append(plan.Version.ToString(CultureInfo.InvariantCulture)).Append('(').Append(plan.Note).Append(')');
            }
            return sb.ToString();
        }

        internal static string Fmt0(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value) || value == float.MaxValue) ? "-" : value.ToString("0", CultureInfo.InvariantCulture);
        }

        internal static string Fmt1(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string Fmt2(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
