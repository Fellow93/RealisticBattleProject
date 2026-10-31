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
    /// Archer spots for the archer behaviors: each side's best places near its own line to shoot from (hill top, crest, or
    /// flat ground with a clear view), scored on the terrain read's background task (BattleTerrainRead.Spots.cs) and published
    /// here with a WorldPosition each (terrainTacticsEnabled). Off, before the publish or without spots, the helpers return
    /// false and the behaviors do what they did before.
    /// Use (TryHoldSpot, from the behaviors' CalculateCurrentOrder): RBMBehaviorArcherFlank takes a spot on its flank side
    /// within SpotMaxShift of its normal place; RBMBehaviorArcherSkirmish only while Shooting, a spot within SpotMaxShift with
    /// fire share >= SpotMinFire that keeps the significant enemy within SkirmishTakeRange x its range.
    /// Sticky once taken, until: a significant enemy (ThreatSignificance; cavalry/HA only with the archers SpotMountedMeleeRatio
    /// in melee) within SpotReleaseDistance, the ally infantry beyond SpotLeaveBehind, (skirmish) the enemy beyond
    /// SkirmishKeepRange or no shooting, the behavior stops being active, the toggle goes off, a friendly formation overlaps
    /// (FriendlyOverlap Take/KeepMaxShare), or the cohesion limit is exceeded (FormationCohesion); then no new spot for SpotRetakeCooldown.
    /// Claims: one spot to at most one formation of a team (ClaimKey, TryAdd), a formation holds at most one; a behavior only
    /// TAKES a spot while it is its formation's active behavior (native also calls CalculateCurrentOrder for candidates).
    /// Re-plan: TerrainArmyReplan (one job with the cavalry ground) hands ApplyReplan the worker's result, which builds/publishes
    /// the next SidePlan versions (the attacker's always, the DEFENDER's only when its own army moved or turned). A claim on an OLD version's spot moves on the AI thread to the new
    /// spot nearest it (within SpotAdoptDistance, the take rules, once per version via Claim.SeenVersion: "rebase"), else is KEPT
    /// on the old spot while every keep rule passes ("keepOld"). PruneOld drops claims of inactive behaviors and unused owner
    /// entries. "refuse" lines are deduped on the reasons (digits stripped), at most one per formation per RefusalLogSeconds.
    /// THREADING: Build / Publish / ApplyReplan / PruneOld run on the MAIN thread (Build creates and navmesh-validates
    /// each WorldPosition). TryHoldSpot / Release run on the async AI thread: volatile reads, the immutable plan, cached formation
    /// positions, concurrent-dictionary claims and a queue; no engine call, no WorldPosition construction (the cached one is
    /// copied), no MBRandom. BEHAVIOR events are queued and written by BattleTerrainLogic on the main thread. The overlay reads
    /// claims through CopyClaims. Cleared at mission end and in MissionStartReset.
    /// </summary>
    internal static class TerrainSpots
    {

        public const float SpotMaxShift = 120f; // m from the behavior's normal place / hold position

        public const float SpotEnemyMinDistance = 80f; // no spot with an enemy formation (average position) this close to it

        public const float SpotReleaseDistance = 50f; // released when an enemy significant to the holder (ThreatSignificance) comes this close

        public const float SpotMountedMeleeRatio = 0.1f; // user rule (stand vs mounted): cavalry / HA release a spot only with the archers this much in melee

        public const float SpotLeaveBehind = 200f; // released (never taken) when the ally infantry is farther

        public const float SpotMinFire = 0.6f; // skirmishers: share of the enemy's approach the spot must see

        public const float SkirmishTakeRange = 0.9f; // skirmishers take a spot only with the significant enemy within this x their effective range ...

        public const float SkirmishKeepRange = 1.1f; // ... and leave it beyond this x that range

        public const float SpotRetakeCooldown = 20f;

        public const float SpotAdoptDistance = 40f; // re-plan: an old claim moves to the nearest new spot only within this, else released


        private static readonly PlanSlot<SidePlan> Plans = new PlanSlot<SidePlan>();

        /// <summary>WorldPosition built on the main thread. Immutable.</summary>
        internal sealed class Spot
        {
            public readonly BattleTerrain.ArcherSpot Data;
            public readonly WorldPosition Position;

            internal Spot(BattleTerrain.ArcherSpot data, WorldPosition position)
            {
                Data = data;
                Position = position;
            }

            public string Id => Data.Id;
        }

        /// <summary>One side's usable spots, best first. Immutable.</summary>
        internal sealed class SidePlan
        {
            public readonly BattleSideEnum Side;
            public readonly IReadOnlyList<Spot> Spots;
            public readonly string Dropped; // spots dropped at the publish ("A3:outside A4:noNavmesh"), or null
            public readonly int Version; // 1 at the publish, + 1 per re-plan
            public readonly string Note; // publish | replan
            public readonly float LineFire; // the line's own fire share (the worker's spot bar), NaN unknown

            internal SidePlan(BattleSideEnum side, IReadOnlyList<Spot> spots, string dropped, int version = 1, string note = "publish", float lineFire = float.NaN)
            {
                Side = side;
                Spots = spots;
                Dropped = dropped;
                Version = version;
                Note = note;
                LineFire = lineFire;
            }

            public bool Contains(Spot spot)
            {
                foreach (Spot s in Spots)
                {
                    if (s == spot)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>The side's plan as the behaviors read it: null with terrainTacticsEnabled off, before the publish, or for another side. Any thread.</summary>
        public static SidePlan ForSide(BattleSideEnum side)
        {
            return Plans.ForSide(side);
        }

        public static SidePlan ForTeam(Team team)
        {
            return Plans.ForTeam(team);
        }

        /// <summary>Whatever the toggle (log and map).</summary>
        internal static SidePlan Published(BattleSideEnum side)
        {
            return Plans.Published(side);
        }

        /// <summary>MAIN thread, at the terrain publish: both sides' plans from the snapshot's spots, each with a navmesh-validated WorldPosition; spots outside the boundary or off the navmesh are dropped. Null for a side without spots.</summary>
        internal static void Build(Mission mission, BattleTerrain terrain, out SidePlan attacker, out SidePlan defender)
        {
            attacker = null;
            defender = null;
            if (mission == null || mission.Scene == null || terrain == null)
            {
                return;
            }
            attacker = BuildSide(mission, BattleSideEnum.Attacker, terrain.Spots.Attacker, 1, "publish");
            defender = BuildSide(mission, BattleSideEnum.Defender, terrain.Spots.Defender, 1, "publish");
        }

        /// <summary>MAIN thread: one side's plan (the publish's or a re-plan's) with its WorldPositions. Null without spots at the publish; a re-plan (version &gt; 1) that found none publishes an empty version, so the old spots are let go.</summary>
        private static SidePlan BuildSide(Mission mission, BattleSideEnum side, BattleTerrain.SideSpots spots, int version, string note)
        {
            if (spots == null || (spots.Spots.Count == 0 && version <= 1))
            {
                return null;
            }
            List<Spot> usable = new List<Spot>();
            StringBuilder dropped = null;
            foreach (BattleTerrain.ArcherSpot data in spots.Spots)
            {
                string why = null;
                WorldPosition position = WorldPosition.Invalid;
                if (!mission.IsPositionInsideBoundaries(data.Position))
                {
                    why = "outside";
                }
                else
                {
                    position = new WorldPosition(mission.Scene, UIntPtr.Zero, new Vec3(data.Position, data.Height), false);
                    if (position.GetNavMesh() == UIntPtr.Zero)
                    {
                        why = "noNavmesh";
                    }
                }
                if (why != null)
                {
                    dropped = (dropped == null) ? new StringBuilder() : dropped.Append(' ');
                    dropped.Append(data.Id).Append(':').Append(why);
                    continue;
                }
                usable.Add(new Spot(data, position));
            }
            return new SidePlan(side, Array.AsReadOnly(usable.ToArray()), dropped?.ToString(), version, note, spots.LineFire);
        }

        /// <summary>Main thread, at the terrain publish.</summary>
        internal static void Publish(SidePlan attacker, SidePlan defender)
        {
            Plans.Publish(attacker, defender);
        }

        /// <summary>Main thread: mission end and MissionStartReset. Also drops the claims, the queued log lines and the archer/cavalry re-plan (TerrainArmyReplan).</summary>
        public static void Clear()
        {
            Plans.Clear();
            Claims.Clear();
            Owners.Clear();
            ReleasedAt.Clear();
            AdvancingSeenAt.Clear();
            AdvanceSamples.Clear();
            LoggedRefusals.Clear();
            TerrainLog.Clear();
            RefusalLoggedAt.Clear();
            TerrainArmyReplan.Clear();
        }

        // re-plan (MAIN thread, TerrainArmyReplan)

        /// <summary>Main thread: builds and publishes a finished re-plan (WorldPositions here, as Build), then prunes old claims; SPOTS replan lines into lines. defender = publish the defender's next version (its own army moved), defenderNote = that rule's text.</summary>
        internal static void ApplyReplan(Mission mission, BattleTerrain.SpotsInfo info, bool replanDefender, string defenderNote, string reason, string ms, List<string> lines)
        {
            if (info == null || mission == null || mission.Scene == null)
            {
                return;
            }
            SidePlan oldAttacker = Plans.Attacker;
            SidePlan oldDefender = Plans.Defender;
            SidePlan attacker = Next(mission, BattleSideEnum.Attacker, info.Attacker, oldAttacker);
            SidePlan defender = replanDefender ? Next(mission, BattleSideEnum.Defender, info.Defender, oldDefender) : oldDefender;
            Plans.Publish(attacker, defender);
            int pruned = PruneOld();
            foreach (BattleSideEnum side in new[] { BattleSideEnum.Attacker, BattleSideEnum.Defender })
            {
                BattleTerrain.SideSpots data = (side == BattleSideEnum.Attacker) ? info.Attacker : info.Defender;
                SidePlan plan = (side == BattleSideEnum.Attacker) ? attacker : defender;
                string what;
                if (side == BattleSideEnum.Defender && !replanDefender)
                {
                    what = "kept v" + ((plan != null) ? plan.Version.ToString(CultureInfo.InvariantCulture) : "-") + " (" + defenderNote + ")";
                }
                else if (data != null)
                {
                    what = Describe(info, data, plan) + ((side == BattleSideEnum.Defender) ? " (" + defenderNote + ")" : "");
                }
                else
                {
                    what = "failed=" + (info.Error ?? "?") + " kept v" + ((plan != null) ? plan.Version.ToString(CultureInfo.InvariantCulture) : "-");
                }
                lines.Add("SPOTS\treplan\t" + side + "\treason=" + reason + "\twallMs=" + ms + "\tpruned=" + pruned.ToString(CultureInfo.InvariantCulture) + "\t" + what);
            }
        }

        /// <summary>Main thread: a side's next version from a re-plan's spots: the current one when the worker could not plan the side, still none when neither found any, else a new version (empty when every spot is gone, so the old ones are let go).</summary>
        private static SidePlan Next(Mission mission, BattleSideEnum side, BattleTerrain.SideSpots data, SidePlan old)
        {
            if (data == null || (old == null && data.Spots.Count == 0))
            {
                return old;
            }
            return BuildSide(mission, side, data, (old != null) ? old.Version + 1 : 2, "replan");
        }

        /// <summary>MAIN thread, after a re-plan's publish: drops claims on old spots of empty formations or inactive behaviors (they would never tick again), and owner entries no claim uses. Conditional removes only, so a concurrent take / move on the AI thread is never undone. Returns how many were dropped.</summary>
        private static int PruneOld()
        {
            SidePlan attacker = Plans.Attacker;
            SidePlan defender = Plans.Defender;
            Mission mission = Mission.Current;
            float now = (mission != null) ? mission.CurrentTime : 0f;
            int dropped = 0;
            foreach (KeyValuePair<Formation, Claim> entry in Claims)
            {
                Claim claim = entry.Value;
                if (claim == null || IsCurrent(claim.Team, claim.Spot, attacker, defender))
                {
                    continue;
                }
                Formation formation = entry.Key;
                if (formation == null || formation.CountOfUnits <= 0 || formation.AI == null || formation.AI.ActiveBehavior != claim.Behavior)
                {
                    ReleaseClaim(claim, "replan inactive", now, false);
                    dropped++;
                }
            }
            foreach (KeyValuePair<ClaimKey, Formation> entry in Owners)
            {
                ClaimKey key = entry.Key;
                if (IsCurrent(key.Team, key.Spot, attacker, defender))
                {
                    continue;
                }
                Claim claim;
                if (entry.Value != null && Claims.TryGetValue(entry.Value, out claim) && claim != null && claim.Spot == key.Spot)
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

        private static bool IsCurrent(Team team, Spot spot, SidePlan attacker, SidePlan defender)
        {
            SidePlan plan = (team == null) ? null : (team.Side == BattleSideEnum.Attacker) ? attacker : (team.Side == BattleSideEnum.Defender) ? defender : null;
            return plan != null && plan.Contains(spot);
        }

        // claims (async AI thread)

        internal sealed class Claim
        {
            public Formation Formation;
            public Team Team;
            public Spot Spot;
            public BehaviorComponent Behavior;
            public string BehaviorName;
            public float Since;
            public int SeenVersion; // plan version last matched against (taken, rebased, kept as orphan): AdoptSpot runs once per new version
        }

        private struct ClaimKey : IEquatable<ClaimKey>
        {
            public readonly Team Team;
            public readonly Spot Spot;

            public ClaimKey(Team team, Spot spot)
            {
                Team = team;
                Spot = spot;
            }

            public bool Equals(ClaimKey other)
            {
                return ReferenceEquals(Team, other.Team) && ReferenceEquals(Spot, other.Spot);
            }

            public override bool Equals(object obj)
            {
                return obj is ClaimKey && Equals((ClaimKey)obj);
            }

            public override int GetHashCode()
            {
                int a = (Team != null) ? Team.GetHashCode() : 0;
                int b = (Spot != null) ? Spot.GetHashCode() : 0;
                return (a * 397) ^ b;
            }
        }

        private static readonly ConcurrentDictionary<Formation, Claim> Claims = new ConcurrentDictionary<Formation, Claim>();
        private static readonly ConcurrentDictionary<ClaimKey, Formation> Owners = new ConcurrentDictionary<ClaimKey, Formation>();
        private static readonly ConcurrentDictionary<Formation, float> ReleasedAt = new ConcurrentDictionary<Formation, float>();
        // Per attacker team: mission time ArmyAdvancing was last true (ArmyAdvancingLatched).
        private static readonly ConcurrentDictionary<Team, float> AdvancingSeenAt = new ConcurrentDictionary<Team, float>();

        public const float AdvancingLatchSeconds = 15f; // "advancing" holds this long after the main infantry last advanced / charged (covers Regroup interludes)
        private static readonly ConcurrentDictionary<string, byte> LoggedRefusals = new ConcurrentDictionary<string, byte>();
        private static readonly ConcurrentDictionary<Formation, float> RefusalLoggedAt = new ConcurrentDictionary<Formation, float>();

        public const float RefusalLogSeconds = 10f; // at most one "refuse" line per formation per this; each set of REASONS (digits stripped) once

        /// <summary>Any thread: placed on purpose, so nobody waits for it to close up.</summary>
        internal static bool IsHolding(Formation formation)
        {
            return formation != null && Claims.ContainsKey(formation);
        }

        /// <summary>Main thread (the map): every current claim of a non-empty formation, as (formation, spot).</summary>
        internal static void CopyClaims(List<KeyValuePair<Formation, Spot>> into)
        {
            into.Clear();
            foreach (KeyValuePair<Formation, Claim> entry in Claims)
            {
                if (entry.Value != null && entry.Key != null && entry.Key.CountOfUnits > 0)
                {
                    into.Add(new KeyValuePair<Formation, Spot>(entry.Key, entry.Value.Spot));
                }
            }
        }

        /// <summary>AI thread, from a behavior's CalculateCurrentOrder: true and the spot's cached WorldPosition when the formation holds or now takes a spot. anchor = the behavior's normal place / hold position; wantedSide = Left / Right to take only spots on that side, anything else for any; ally = the ally infantry (null: no leave-behind rule); minFire = the least fire share; enemyAt / takeRange / keepRange = the skirmishers' range rule (NaN = none).</summary>
        internal static bool TryHoldSpot(BehaviorComponent behavior, string behaviorName, Vec2 anchor, FormationAI.BehaviorSide wantedSide,
            Formation ally, float minFire, Vec2 enemyAt, float takeRange, float keepRange, out WorldPosition target)
        {
            target = WorldPosition.Invalid;
            Formation formation = behavior?.Formation;
            if (formation == null || formation.Team == null)
            {
                return false;
            }
            Team team = formation.Team;
            Mission mission = Mission.Current;
            float now = (mission != null) ? mission.CurrentTime : 0f;
            bool active = formation.AI != null && formation.AI.ActiveBehavior == behavior;
            SidePlan plan = ForTeam(team);

            Claim claim;
            // User rule: while the attacker's main infantry advances or charges, archers move with the army: no spot is taken and a held one is let go (no retake cooldown).
            string advancing = (team.Side == BattleSideEnum.Attacker) ? ArmyAdvancingLatched(team, now) : null;
            if (advancing != null)
            {
                if (Claims.TryGetValue(formation, out claim) && claim != null && claim.Behavior == behavior)
                {
                    ReleaseClaim(claim, "armyAdvancing " + advancing, now, false);
                }
                return false;
            }
            if (Claims.TryGetValue(formation, out claim) && claim != null)
            {
                if (claim.Behavior != behavior)
                {
                    if (!active)
                    {
                        // Another behavior of this formation holds it; this one is only being evaluated.
                        return false;
                    }
                    ReleaseClaim(claim, "behaviorChanged", now);
                }
                else if (plan != null && active && !plan.Contains(claim.Spot))
                {
                    // A re-plan replaced the spots: move to the new spot nearest the old one (once per new version), else KEEP the old one while it is still good.
                    Claim moved = (claim.SeenVersion != plan.Version)
                        ? AdoptSpot(claim, plan, formation, team, anchor, wantedSide, ally, minFire, enemyAt, takeRange, now)
                        : null;
                    if (moved == null)
                    {
                        if (claim.SeenVersion != plan.Version)
                        {
                            Claim kept = new Claim { Formation = formation, Team = team, Spot = claim.Spot, Behavior = claim.Behavior, BehaviorName = claim.BehaviorName, Since = claim.Since, SeenVersion = plan.Version };
                            if (Claims.TryUpdate(formation, kept, claim))
                            {
                                Enqueue(team, formation, now, claim.BehaviorName, "keepOld", claim.Spot.Id, "replan v" + plan.Version.ToString(CultureInfo.InvariantCulture)
                                    + " noSpot<=" + Fmt0(SpotAdoptDistance) + "m held=" + Fmt0(now - claim.Since) + "s");
                                claim = kept;
                            }
                        }
                        string orphanWhy = KeepFailure(claim, plan, active, formation, team, ally, enemyAt, keepRange);
                        if (orphanWhy == null)
                        {
                            target = claim.Spot.Position;
                            return true;
                        }
                        ReleaseClaim(claim, orphanWhy + " (old spot, v" + plan.Version.ToString(CultureInfo.InvariantCulture) + ")", now);
                        return false;
                    }
                    else
                    {
                        string why = KeepFailure(moved, plan, active, formation, team, ally, enemyAt, keepRange);
                        if (why == null)
                        {
                            target = moved.Spot.Position;
                            return true;
                        }
                        ReleaseClaim(moved, why, now);
                        return false;
                    }
                }
                else
                {
                    string why = KeepFailure(claim, plan, active, formation, team, ally, enemyAt, keepRange);
                    if (why == null)
                    {
                        target = claim.Spot.Position;
                        return true;
                    }
                    ReleaseClaim(claim, why, now);
                    return false;
                }
            }

            if (plan == null || plan.Spots.Count == 0 || !active || formation.CountOfUnits <= 0)
            {
                return false;
            }
            float releasedAt;
            if (ReleasedAt.TryGetValue(formation, out releasedAt) && now - releasedAt < SpotRetakeCooldown)
            {
                return false;
            }
            bool logging = RBMConfig.RBMConfig.battleTerrainLoggingEnabled;
            StringBuilder refusal = null;
            foreach (Spot spot in plan.Spots)
            {
                string why = TakeFailure(spot, plan, formation, team, anchor, wantedSide, ally, minFire, enemyAt, takeRange, logging);
                if (why == null)
                {
                    if (Owners.TryAdd(new ClaimKey(team, spot), formation))
                    {
                        Claim taken = new Claim { Formation = formation, Team = team, Spot = spot, Behavior = behavior, BehaviorName = behaviorName, Since = now, SeenVersion = plan.Version };
                        Claims[formation] = taken;
                        Enqueue(team, formation, now, behaviorName, "take", spot.Id,
                            spot.Data.Kind.ToString().ToLowerInvariant() + " shift=" + Fmt0(anchor.Distance(spot.Data.Position)) + "m enemy="
                            + Fmt0(NearestEnemyDistance(team, spot.Data.Position)) + "m" + CavScoreText(team, spot));
                        target = spot.Position;
                        return true;
                    }
                    why = "claimed";
                }
                if (logging)
                {
                    refusal = (refusal == null) ? new StringBuilder() : refusal.Append(' ');
                    refusal.Append(spot.Id).Append(':').Append(why);
                }
            }
            if (refusal != null)
            {
                // Deduped on the REASONS (digits stripped) and at most one line per formation per RefusalLogSeconds.
                string text = refusal.ToString();
                string key = team.TeamIndex.ToString(CultureInfo.InvariantCulture) + "/" + ((int)formation.FormationIndex).ToString(CultureInfo.InvariantCulture)
                    + "/" + behaviorName + "/" + StripDigits(text);
                float loggedAt;
                bool recent = RefusalLoggedAt.TryGetValue(formation, out loggedAt) && now - loggedAt < RefusalLogSeconds;
                if (!recent && !LoggedRefusals.ContainsKey(key) && LoggedRefusals.TryAdd(key, 0))
                {
                    RefusalLoggedAt[formation] = now;
                    Enqueue(team, formation, now, behaviorName, "refuse", "-", text);
                }
            }
            return false;
        }

        /// <summary>AI thread: ArmyAdvancing, latched for AdvancingLatchSeconds after it was last true ("latched N s"): the attacker's infantry advances in steps with Regroup interludes that must not make archers take and drop spots.</summary>
        private static string ArmyAdvancingLatched(Team team, float now)
        {
            string advancing = ArmyAdvancing(team, now);
            if (advancing != null)
            {
                AdvancingSeenAt[team] = now;
                return advancing;
            }
            float seen;
            if (AdvancingSeenAt.TryGetValue(team, out seen) && now - seen < AdvancingLatchSeconds)
            {
                return "latched " + Fmt0(now - seen) + "s";
            }
            return null;
        }

        // A PLAYER-led main line advances when its own samples show PlayerAdvanceSpeed (m/s) toward the enemy in PlayerAdvanceSamples
        // intervals in a row, or its cached order position lies PlayerAdvanceOrderAhead (m) ahead of its front toward the enemy.
        public const float PlayerAdvanceSpeed = 1f;

        public const int PlayerAdvanceSamples = 2;

        public const float PlayerAdvanceOrderAhead = 30f;

        private const float PlayerSampleSeconds = 1f;
        private const float PlayerSampleMaxAge = 3f; // an older sample restarts the count

        /// <summary>Immutable.</summary>
        private sealed class AdvanceSample
        {
            public Formation Formation;
            public Vec2 Position;
            public float Time;
            public float Speed;
            public int Streak;
        }

        // Per attacker team: the player-led main line's last sample.
        private static readonly ConcurrentDictionary<Team, AdvanceSample> AdvanceSamples = new ConcurrentDictionary<Team, AdvanceSample>();

        /// <summary>AI thread: the team's main infantry (FormationCohesion.TeamMain) is advancing on the enemy - under a Charge order; an AI formation whose active behavior is BehaviorAdvance / CautiousAdvance / Charge; a PLAYER-led one under an Advance order, moving toward the enemy at PlayerAdvanceSpeed in PlayerAdvanceSamples samples in a row, or with its order position more than PlayerAdvanceOrderAhead ahead of its front - its behavior / order name; null otherwise. Cached formation data only.</summary>
        private static string ArmyAdvancing(Team team, float now)
        {
            Formation main = FormationCohesion.TeamMain(team);
            if (main == null)
            {
                return null;
            }
            OrderType order = main.GetReadonlyMovementOrderReference().OrderType;
            if (order == OrderType.Charge)
            {
                return "order=Charge";
            }
            if (order == OrderType.ChargeWithTarget)
            {
                return "order=ChargeWithTarget";
            }
            // Walking to its charge (ChargeGuard's advance Move).
            if (ChargeGuard.IsAdvancingToCharge(main))
            {
                return "order=chargeAdvance";
            }
            if (main.IsAIControlled)
            {
                BehaviorComponent active = (main.AI != null) ? main.AI.ActiveBehavior : null;
                if (active is BehaviorAdvance || active is BehaviorCautiousAdvance || active is BehaviorCharge)
                {
                    return active.GetType().Name;
                }
                return null;
            }
            if (order == OrderType.Advance)
            {
                return "playerLine order=Advance";
            }
            return PlayerLineAdvancing(team, main, now);
        }

        /// <summary>ArmyAdvancing for a player-led main line: its own samples / its order position (see there), or null.</summary>
        private static string PlayerLineAdvancing(Team team, Formation main, float now)
        {
            Mission mission = Mission.Current;
            Formation enemyMain;
            Vec2 forward;
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(main);
            if (mission == null || !centre.IsValid || !ReactionMonitor.ArmyForward(mission, team, main, out enemyMain, out forward))
            {
                return null;
            }
            AdvanceSample last;
            AdvanceSamples.TryGetValue(team, out last);
            if (last == null || last.Formation != main || now < last.Time || now - last.Time > PlayerSampleMaxAge)
            {
                last = new AdvanceSample { Formation = main, Position = centre, Time = now, Speed = float.NaN, Streak = 0 };
                AdvanceSamples[team] = last;
            }
            else if (now - last.Time >= PlayerSampleSeconds)
            {
                float speed = (centre - last.Position).DotProduct(forward) / (now - last.Time);
                last = new AdvanceSample { Formation = main, Position = centre, Time = now, Speed = speed, Streak = (speed >= PlayerAdvanceSpeed) ? last.Streak + 1 : 0 };
                AdvanceSamples[team] = last;
            }
            if (last.Streak >= PlayerAdvanceSamples)
            {
                return "playerLine speed=" + last.Speed.ToString("0.0", CultureInfo.InvariantCulture) + "m/s";
            }
            if (main.OrderPositionIsValid)
            {
                Vec2 order = main.OrderPosition;
                if (order.IsValid)
                {
                    float ahead = (order - centre).DotProduct(forward) - main.Depth * 0.5f;
                    if (ahead > PlayerAdvanceOrderAhead)
                    {
                        return "playerLine orderAhead=" + Fmt0(ahead) + "m";
                    }
                }
            }
            return null;
        }

        /// <summary>AI thread (or wherever native cancels a behavior): drops the formation's claim when this behavior holds it.</summary>
        internal static void Release(BehaviorComponent behavior, string why)
        {
            Formation formation = behavior?.Formation;
            Claim claim;
            if (formation == null || !Claims.TryGetValue(formation, out claim) || claim == null || claim.Behavior != behavior)
            {
                return;
            }
            Mission mission = Mission.Current;
            ReleaseClaim(claim, why, (mission != null) ? mission.CurrentTime : 0f);
        }

        /// <summary>Drops the claim; <paramref name="cooldown"/> = no new spot for SpotRetakeCooldown (false for a re-plan's release).</summary>
        private static void ReleaseClaim(Claim claim, string why, float now, bool cooldown = true)
        {
            // Conditional removes: only this claim / this owner, never one another thread just made.
            if (!((ICollection<KeyValuePair<Formation, Claim>>)Claims).Remove(new KeyValuePair<Formation, Claim>(claim.Formation, claim)))
            {
                return;
            }
            ((ICollection<KeyValuePair<ClaimKey, Formation>>)Owners).Remove(new KeyValuePair<ClaimKey, Formation>(new ClaimKey(claim.Team, claim.Spot), claim.Formation));
            if (cooldown)
            {
                ReleasedAt[claim.Formation] = now;
            }
            Enqueue(claim.Team, claim.Formation, now, claim.BehaviorName, "release", claim.Spot.Id, why + " held=" + Fmt0(now - claim.Since) + "s");
        }

        /// <summary>AI thread, a claim on a spot a re-plan replaced: the new version's spot nearest the old one within SpotAdoptDistance that the take rules allow (enemyNear left to the caller's keep rule), the claim moved there with its owner entry ("rebase"). Null when none fits or the move lost a race (the caller then releases the old claim).</summary>
        private static Claim AdoptSpot(Claim claim, SidePlan plan, Formation formation, Team team, Vec2 anchor, FormationAI.BehaviorSide wantedSide,
            Formation ally, float minFire, Vec2 enemyAt, float takeRange, float now)
        {
            Vec2 old = claim.Spot.Data.Position;
            Spot best = null;
            float bestDistance = SpotAdoptDistance;
            foreach (Spot spot in plan.Spots)
            {
                float d = spot.Data.Position.Distance(old);
                if (d > bestDistance)
                {
                    continue;
                }
                // The take rules, but an enemy near is judged by the keep rule after the move.
                string why = TakeFailure(spot, plan, formation, team, anchor, wantedSide, ally, minFire, enemyAt, takeRange, false);
                if (why == null || why == "enemyNear")
                {
                    bestDistance = d;
                    best = spot;
                }
            }
            if (best == null)
            {
                return null;
            }
            ClaimKey key = new ClaimKey(team, best);
            bool added = Owners.TryAdd(key, formation);
            Formation current;
            if (!added && !(Owners.TryGetValue(key, out current) && current == formation))
            {
                return null;
            }
            Claim moved = new Claim { Formation = formation, Team = team, Spot = best, Behavior = claim.Behavior, BehaviorName = claim.BehaviorName, Since = claim.Since, SeenVersion = plan.Version };
            if (!Claims.TryUpdate(formation, moved, claim))
            {
                if (added)
                {
                    ((ICollection<KeyValuePair<ClaimKey, Formation>>)Owners).Remove(new KeyValuePair<ClaimKey, Formation>(key, formation));
                }
                return null;
            }
            ((ICollection<KeyValuePair<ClaimKey, Formation>>)Owners).Remove(new KeyValuePair<ClaimKey, Formation>(new ClaimKey(team, claim.Spot), formation));
            Enqueue(team, formation, now, claim.BehaviorName, "rebase", best.Id, "replan v" + plan.Version.ToString(CultureInfo.InvariantCulture) + " was="
                + claim.Spot.Id + " shift=" + Fmt0(bestDistance) + "m");
            return moved;
        }

        /// <summary>Why a held spot must be left, or null to keep it.</summary>
        private static string KeepFailure(Claim claim, SidePlan plan, bool active, Formation formation, Team team, Formation ally, Vec2 enemyAt, float keepRange)
        {
            if (plan == null)
            {
                return "noPlan";
            }
            if (!active)
            {
                return "inactive";
            }
            if (formation.CountOfUnits <= 0)
            {
                return "empty";
            }
            Vec2 p = claim.Spot.Data.Position;
            Formation close;
            // User rule: enemy cavalry / horse archers near the spot release it only once the archers are in melee (the melee test walks the roster, asked only when a mounted enemy would be the nearest).
            float enemy = NearestSignificantEnemyDistance(team, p, formation, SpotMountedMeleeRatio, out close);
            if (enemy < SpotReleaseDistance)
            {
                return "enemyClose " + ThreatSignificance.Describe(formation, close) + " d=" + Fmt0(enemy) + "m";
            }
            if (ally != null && ally.CountOfUnits > 0 && ally.CachedAveragePosition.Distance(p) > SpotLeaveBehind)
            {
                return "allyAway";
            }
            if (!float.IsNaN(keepRange) && enemyAt.IsValid && enemyAt.Distance(p) > keepRange)
            {
                return "outOfRange";
            }
            // User rule: let go once a friendly formation stands on / heads onto the spot (hysteresis over the take).
            Formation friend;
            Vec2 facing = SpotFacing(formation, p, enemyAt);
            float share = FriendlyOverlap.Share(formation, p, facing, null, FriendlyOverlap.Mode.NowAndHeading, out friend);
            if (share > FriendlyOverlap.KeepMaxShare)
            {
                return FriendlyOverlap.Describe(friend, share);
            }
            // User rule: let go once the main infantry is beyond the cohesion limit + KeepSlack (FormationCohesion; not while charging / retreating / in melee).
            Formation main;
            float gap;
            float limit;
            if (!FormationCohesion.Within(formation, p, facing, FormationCohesion.Kind.Auto, null, FormationCohesion.KeepSlack, out main, out gap, out limit)
                && FormationCohesion.OrderExemption(formation) == null && !FormationCohesion.Engaged(formation))
            {
                return "cohesion " + FormationCohesion.Describe(main, gap, limit + FormationCohesion.KeepSlack);
            }
            return null;
        }

        /// <summary>Why this spot cannot be taken now, or null. explain = false gives the two formatted reasons as their bare word ("friendOverlap" / "cohesion"), skipping string formatting for callers that read only null / "enemyNear".</summary>
        /// <summary>" score=0.82 kCav=0.40 cav=-0.20" when the cavalry penalty applies, else "".</summary>
        private static string CavScoreText(Team team, Spot spot)
        {
            float kCav = ArmyComposition.KCav(team);
            return (kCav > 0f) ? " score=" + Fmt2(spot.Data.Score) + " kCav=" + Fmt2(kCav) + " cav=-" + Fmt2(ArmyComposition.SpotCavalryPenalty * kCav) : "";
        }

        private const float SpotCavalryMinImprove = 0.1f; // = BattleTerrainRead.SpotMinImprove

        private static string TakeFailure(Spot spot, SidePlan plan, Formation formation, Team team, Vec2 anchor, FormationAI.BehaviorSide wantedSide,
            Formation ally, float minFire, Vec2 enemyAt, float takeRange, bool explain)
        {
            BattleTerrain.ArcherSpot data = spot.Data;
            if ((wantedSide == FormationAI.BehaviorSide.Left || wantedSide == FormationAI.BehaviorSide.Right) && data.Side != wantedSide)
            {
                return "otherSide";
            }
            if (anchor.Distance(data.Position) > SpotMaxShift)
            {
                return "far";
            }
            if (!(data.Fire >= minFire))
            {
                return "lowFire";
            }
            // an enemy of melee cavalry: a spot out on its own must be that much better than the line (score - 0.5 x kCav vs the worker's bar)
            float kCav = ArmyComposition.KCav(team);
            if (kCav > 0f && plan != null)
            {
                float bar = (float.IsNaN(plan.LineFire) ? 0f : plan.LineFire) + SpotCavalryMinImprove;
                float penalised = data.Score - ArmyComposition.SpotCavalryPenalty * kCav;
                if (penalised < bar)
                {
                    return explain ? "cavScore(" + Fmt2(data.Score) + "-" + Fmt2(ArmyComposition.SpotCavalryPenalty * kCav) + "<" + Fmt2(bar) + ")" : "cavScore";
                }
            }
            Formation owner;
            if (Owners.TryGetValue(new ClaimKey(team, spot), out owner) && owner != formation)
            {
                Claim ownerClaim;
                bool live = owner != null && owner.CountOfUnits > 0 && Claims.TryGetValue(owner, out ownerClaim) && ownerClaim != null && ownerClaim.Spot == spot;
                if (live)
                {
                    return "claimed";
                }
                ((ICollection<KeyValuePair<ClaimKey, Formation>>)Owners).Remove(new KeyValuePair<ClaimKey, Formation>(new ClaimKey(team, spot), owner));
            }
            if (NearestEnemyDistance(team, data.Position) < SpotEnemyMinDistance)
            {
                return "enemyNear";
            }
            if (ally != null && ally.CountOfUnits > 0 && ally.CachedAveragePosition.Distance(data.Position) > SpotLeaveBehind)
            {
                return "allyFar";
            }
            if (!float.IsNaN(takeRange) && enemyAt.IsValid && enemyAt.Distance(data.Position) > takeRange)
            {
                return "outOfRange";
            }
            // User rule: not a spot where the archers would stand on a friendly formation (now or heading).
            Formation friend;
            Vec2 facing = SpotFacing(formation, data.Position, enemyAt);
            float share = FriendlyOverlap.Share(formation, data.Position, facing, null, FriendlyOverlap.Mode.NowAndHeading, out friend);
            if (share > FriendlyOverlap.TakeMaxShare)
            {
                return explain ? FriendlyOverlap.RefusalText(friend, share) : "friendOverlap";
            }
            // User rule: not a spot farther than the cohesion limit from the main infantry (FormationCohesion).
            Formation main;
            float gap;
            float limit;
            if (!FormationCohesion.Within(formation, data.Position, facing, FormationCohesion.Kind.Auto, null, 0f, out main, out gap, out limit))
            {
                return explain ? FormationCohesion.RefusalText(main, gap, limit) : "cohesion";
            }
            return null;
        }

        /// <summary>The text without its digits (the refusal log's dedupe key: the reasons, not the changing distances).</summary>
        private static string StripDigits(string text)
        {
            StringBuilder sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c < '0' || c > '9')
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>The facing the archers take on a spot (for the friendly-overlap footprint): toward the enemy they shoot at, else their own.</summary>
        private static Vec2 SpotFacing(Formation formation, Vec2 spot, Vec2 enemyAt)
        {
            if (enemyAt.IsValid && enemyAt.DistanceSquared(spot) > 1f)
            {
                return (enemyAt - spot).Normalized();
            }
            return formation.Direction;
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

        /// <summary>Metres from p to the nearest enemy formation (cached average position) significant to the reactor (ThreatSignificance), that formation in enemy; MaxValue (null) with none. A few bait men do not chase archers off a spot. Cavalry / horse archers count only while the reactor is at least mountedMeleeRatio in melee (asked at most once).</summary>
        private static float NearestSignificantEnemyDistance(Team team, Vec2 p, Formation reactor, float mountedMeleeRatio, out Formation enemy)
        {
            float best = float.MaxValue;
            enemy = null;
            Mission mission = Mission.Current;
            if (mission == null || team == null)
            {
                return best;
            }
            // -1 = not asked yet, 0 = mounted enemies count, 1 = foot only.
            int footOnly = -1;
            foreach (Team other in mission.Teams)
            {
                if (other == null || !other.IsEnemyOf(team))
                {
                    continue;
                }
                foreach (Formation e in other.FormationsIncludingEmpty)
                {
                    if (e == null || e.CountOfUnits <= 0)
                    {
                        continue;
                    }
                    float d = e.CachedAveragePosition.Distance(p);
                    if (!(d < best))
                    {
                        continue;
                    }
                    if (e.QuerySystem.IsCavalryFormation || e.QuerySystem.IsRangedCavalryFormation)
                    {
                        if (footOnly < 0)
                        {
                            footOnly = RBMAI.Utilities.FormationFightingInMelee(reactor, mountedMeleeRatio) ? 0 : 1;
                        }
                        if (footOnly == 1)
                        {
                            continue;
                        }
                    }
                    if (ThreatSignificance.IsSignificant(reactor, e))
                    {
                        best = d;
                        enemy = e;
                    }
                }
            }
            return best;
        }

        private static void Enqueue(Team team, Formation formation, float now, string behavior, string what, string spotId, string why)
        {
            TerrainLog.Behavior(team, formation, now, behavior, what, "spot", spotId, why);
        }

        /// <summary>AI thread: one BEHAVIOR line of a behavior that is not about a spot, written with spot=pointId.</summary>
        internal static void EnqueueBehavior(Team team, Formation formation, float now, string behavior, string what, string pointId, string why)
        {
            if (team == null || formation == null)
            {
                return;
            }
            Enqueue(team, formation, now, behavior, what, pointId, why);
        }

        // log text (main thread)

        /// <summary>Fields after "SPOTS\tside".</summary>
        internal static string Describe(BattleTerrain.SpotsInfo info, BattleTerrain.SideSpots side, SidePlan plan)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("applied=").Append(RBMConfig.RBMConfig.terrainTacticsEnabled ? 1 : 0);
            sb.Append("\tlineFire=").Append(Fmt2(side.LineFire));
            sb.Append("\tcand=").Append(side.Candidates.ToString(CultureInfo.InvariantCulture));
            sb.Append("\trej=room:").Append(side.RejectRoom).Append("/woods:").Append(side.RejectWoods).Append("/edge:").Append(side.RejectEdge).Append("/region:").Append(side.RejectRegion)
              .Append("/targets:").Append(side.RejectTargets).Append("/score:").Append(side.RejectScore)
              .Append("/sep:").Append(side.RejectSeparation).Append("/reach:").Append(side.RejectReach);
            sb.Append("\trays=").Append(side.Rays.ToString(CultureInfo.InvariantCulture));
            sb.Append("\tpaths=").Append(side.PathRuns.ToString(CultureInfo.InvariantCulture));
            sb.Append("\tms=").Append(Fmt2(info.Ms));
            if (info.Error != null)
            {
                sb.Append("\terror=").Append(info.Error);
            }
            sb.Append("\tspots=");
            if (side.Spots.Count == 0)
            {
                sb.Append('-');
            }
            for (int i = 0; i < side.Spots.Count; i++)
            {
                BattleTerrain.ArcherSpot s = side.Spots[i];
                if (i > 0)
                {
                    sb.Append(' ');
                }
                sb.Append(s.Id).Append('[').Append(BattleTerrain.SpotKindName(s.Kind)).Append(' ').Append(s.Source)
                  .Append(" pos=").Append(Fmt0(s.Position.x)).Append(',').Append(Fmt0(s.Position.y))
                  .Append(" gain=").Append(Fmt1(s.Gain))
                  .Append(" fire=").Append(Fmt2(s.Fire)).Append('/').Append(s.FireTargets)
                  .Append(" side=").Append(SideName(s.Side))
                  .Append(" distLine=").Append(Fmt0(s.DistLine))
                  .Append(" along=").Append(Fmt0(s.Along)).Append(" across=").Append(Fmt0(s.Across))
                  .Append(" clear=").Append(Fmt0(s.Clearance)).Append(" slope=").Append(Fmt0(s.Slope))
                  .Append(" reach=").Append(Fmt0(s.OurSeconds)).Append("s/").Append(Fmt0(s.EnemySeconds)).Append('s')
                  .Append(" score=").Append(Fmt2(s.Score))
                  .Append(" why=").Append(s.Reason).Append(']');
            }
            sb.Append("\tusable=").Append((plan != null) ? plan.Spots.Count.ToString(CultureInfo.InvariantCulture) : "0");
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

        internal static string SideName(FormationAI.BehaviorSide side)
        {
            return (side == FormationAI.BehaviorSide.Left) ? "left" : (side == FormationAI.BehaviorSide.Right) ? "right" : "middle";
        }

        private static string Fmt0(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value) || value == float.MaxValue) ? "-" : value.ToString("0", CultureInfo.InvariantCulture);
        }

        private static string Fmt1(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string Fmt2(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
