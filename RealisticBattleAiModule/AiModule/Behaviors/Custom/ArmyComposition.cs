using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// Army composition per team (own = the team's side, enemy = every enemy team) by MEN x class weight (user decision: RBM troop power
    /// makes tier-5 riders ~8x a tier-2 recruit, so power shares misread normal armies): smoothed category shares the composition ramps
    /// read, token flags per category.
    ///
    /// PLAN: every composition decision is made in one place, the snapshot build (For, at most once per RefreshSeconds): the "infantry leads"
    /// verdict (FormationCohesion.TeamMain needs it), the main, the foot position anchor without a main (strongest qualifying AI foot formation;
    /// never mounted, never a token category), the archer line holder, the k ramps and the FootRangedEngage band. Every latch is carried from the
    /// previous snapshot; one diff (LogPlan) writes the COMPEVENT lines for what changed. Readers get up to RefreshSeconds of lag.
    ///
    /// Categories by TROOP TYPE (user rule: ammo never changes a man's category): the formation's logical class counts (the troop's
    /// DefaultFormationClass through Mission.GetAgentTroopClass, kept by the formation on join / leave) - melee infantry, archers (foot
    /// ranged, skirmishers included), melee cavalry, horse archers (light cavalry included). A formation's category = its majority
    /// (mounted when at least half its men are); a rider without a horse counts as foot of his kind (MenOf). "Dry" is a STATE read from ammo (AmmoShare / IsDry).
    ///
    /// THREADING: AI-thread safe (cached formation data only). A snapshot is rebuilt on read at most once per RefreshSeconds and
    /// published as an immutable object; a concurrent rebuild only repeats the work. Team / Formation-keyed ConcurrentDictionaries,
    /// cleared in Clear (MissionStartReset). Events go to a queue the terrain log drains on the main thread (COMPEVENT).
    /// </summary>
    internal static class ArmyComposition
    {
        // ---------------------------------------------------------------- tuning: every number of the composition rules (k = 0 keeps the old rule)

        internal const float RefreshSeconds = 1f;
        internal const float SmoothSeconds = 10f; // EMA time constant
        internal const float ReseedGrowth = 1.2f; // side men above this x the lowest since the last seed: no smoothing (reinforcements, refills)
        internal const float SwitchRatio = 1.2f; // x power to replace a held foot anchor
        internal const float MaxCavalryStrengthRatio = 10f;

        // weight of one man by category (user decision D1)
        internal const float WeightInfantry = 1f;
        internal const float WeightArchers = 1f;
        internal const float WeightCavalry = 1.5f;
        internal const float WeightHorseArchers = 1.3f;

        // TOKEN force (user rule: a handful of a category never drives an army-level role or ramp): weighted share under TokenEnterShare
        // and fewer than TokenEnterMen men; it stops being token at TokenLeaveShare or TokenLeaveMen (casualties do not flicker it).
        internal const float TokenEnterShare = 0.10f;
        internal const float TokenLeaveShare = 0.15f;
        internal const int TokenEnterMen = 60;
        internal const int TokenLeaveMen = 80;

        /// <summary>Dry: under this share of a formation's archer / horse-archer men still has ammunition.</summary>
        internal const float DryAmmoShare = 0.2f;

        // anchor needs this share of the team's foot men (and FormationCohesion.MinMainMen)
        internal const float AnchorMinFootShare = 0.15f;

        // Infantry leads (FormationCohesion's main): the team's smoothed infantry share of its weighted men reaches the first, kept down to the second; never a token force.
        internal const float MainEnterStrengthShare = 0.25f;
        internal const float MainKeepStrengthShare = 0.15f;

        // composition ramps on men shares (0 for a normal enemy: >= ~40 % melee infantry and <= ~30 % mounted by men; with the class weights the
        // worst normal case reads eFoot 0.35 / eCav 0.39, hence the starts). Token enemy categories count as 0.
        internal const float KFootStart = 0.7f, KFootFull = 0.9f; // of 1 - EnemyMeleeFoot
        internal const float KCavStart = 0.4f, KCavFull = 0.6f; // of EnemyMeleeCavalry
        internal const float KWeakStart = 0.2f, KWeakFull = 0.6f; // of 1 - CavalryStrengthRatio
        // own-army ramps (0 for a normal army: <= ~30 % mounted men reads mnt <= 0.39)
        internal const float KMountStart = 0.45f, KMountFull = 0.75f; // of the non-token Mounted share
        internal const float KHaStart = 0.3f, KHaFull = 0.7f; // of HorseArcherOfMounted
        internal const float MountedArmyStart = 0.6f, MountedArmyShare = 0.9f; // FullScaleAttack's mounted-army floor ramp, of the team's mounted share

        // either / or bands with hysteresis
        internal const float FootRangedOn = 0.5f, FootRangedOff = 0.3f; // kFoot band: enemy foot archers count as an engaging enemy
        internal const float CavalryBehindOn = 0.5f, CavalryBehindOff = 0.35f; // kCav band: archers behind the main melee infantry (CavalryBehindPlace)
        internal const float FollowJoinHoldSeconds = 10f; // FollowJoined: joined at least this long
        internal const float FollowUnjoinSeconds = 5f; // not joined this long before the gate closes again

        // shared thresholds
        internal const float InMelee = 0.1f; // a formation's melee share that counts as fighting
        internal const float EngagedGap = 30f; // m edge gap: a formation that close to one in melee is engaged with it
        internal const float WeakShrink = 0.6f; // kWeak: ProtectFlank sortie ranges / time and the React cavalry leash x (1 - this x kWeak)

        // the other end of each blend (at k = 1)
        internal const float CavalryLimitFactor = 0.6f; // kCav: FormationCohesion limits x this (an enemy of melee cavalry rides into gaps)
        internal const float SpotCavalryPenalty = 0.5f; // kCav: off an archer spot's score at take time
        internal const float ActiveSortieGrowth = 1.8f; // kMount: defender ProtectFlank sortie ranges / time x Lerp(1, this, kMount)
        internal const float ActiveHomeLeash = 250f; // m from the position anchor while the sorties are grown
        internal const float OutshotSecondsNoFoot = 20f; // kFoot: HoldLateCharge.OutshotSeconds
        internal const float BehindRangedGapNoFoot = 60f; // kFoot: HoldLateCharge.BehindRangedGap
        internal const float WearDownLossFull = 0.2f; // k: CavalryWearDown opens at enemy loss Lerp(0, this, k)
        internal const float WearDownTimeFull = 90f; // s, k: ... or Lerp(0, this, k) s after first contact
        internal const float WearDownAmmoOpen = 0.3f; // ... or our horse archers' ammo under this

        internal enum Category
        {
            Infantry = 0,
            Archers = 1,
            Cavalry = 2,
            HorseArchers = 3
        }

        internal const int CategoryCount = 4;
        private static readonly string[] CategoryNames = { "inf", "arc", "cav", "ha" };
        internal static readonly float[] CategoryWeights = { WeightInfantry, WeightArchers, WeightCavalry, WeightHorseArchers };

        /// <summary>Men of a formation by troop type.</summary>
        internal struct Men
        {
            public int Infantry;
            public int Archers;
            public int Cavalry;
            public int HorseArchers;
            /// <summary>Riders of a mounted troop type without a horse now, already moved to Infantry / Archers (not part of Total twice).</summary>
            public int Dismounted;

            public int Total => Infantry + Archers + Cavalry + HorseArchers;
            public int Mounted => Cavalry + HorseArchers;
            public int Shooters => Archers + HorseArchers;

            /// <summary>Men x class weight (Weight*), summed in category order.</summary>
            public float Weighted
            {
                get
                {
                    float sum = 0f;
                    for (int c = 0; c < CategoryCount; c++)
                    {
                        sum += Of((Category)c) * CategoryWeights[c];
                    }
                    return sum;
                }
            }

            public int Of(Category c)
            {
                switch (c)
                {
                    case Category.Infantry: return Infantry;
                    case Category.Archers: return Archers;
                    case Category.Cavalry: return Cavalry;
                    default: return HorseArchers;
                }
            }
        }

        /// <summary>Any thread: the formation's men by troop type (logical class counts; the slot's class when the counts are empty), with dismounted
        /// riders counted by what they are now: a melee cavalryman without a horse as infantry, a horse archer as an archer (Dismount).</summary>
        internal static Men MenOf(Formation f)
        {
            Men m = TroopMenOf(f);
            Dismount(f, ref m);
            return m;
        }

        /// <summary>Mounted men now = the formation's cached physical mount share (QuerySystem *UnitRatioReadOnly: HasMount-based, cached field reads, no
        /// evaluation) x its men; it only lowers the troop-type count. Ammo never moves anyone (the physical ranged split is ammo-based, so it is not read).</summary>
        private static void Dismount(Formation f, ref Men m)
        {
            int troopMounted = m.Mounted;
            if (troopMounted <= 0)
            {
                return;
            }
            FormationQuerySystem q = f.QuerySystem;
            if (q == null)
            {
                return;
            }
            float inf = q.InfantryUnitRatioReadOnly, rng = q.RangedUnitRatioReadOnly, cav = q.CavalryUnitRatioReadOnly, ha = q.RangedCavalryUnitRatioReadOnly;
            float attached = inf + rng + cav + ha;
            // not evaluated yet (all 0) or garbage: keep the troop types
            if (!(attached > 0.5f) || float.IsNaN(attached))
            {
                return;
            }
            // normalised over the attached men (detached ones are in CountOfUnits but in no ratio)
            int mountedNow = (int)Math.Round((cav + ha) / attached * f.CountOfUnits);
            int dismounted = troopMounted - Math.Max(0, mountedNow);
            if (dismounted <= 0)
            {
                return;
            }
            int cavalryOff = (int)Math.Round((float)dismounted * m.Cavalry / troopMounted);
            cavalryOff = Math.Min(cavalryOff, m.Cavalry);
            int horseArchersOff = Math.Min(dismounted - cavalryOff, m.HorseArchers);
            m.Cavalry -= cavalryOff;
            m.Infantry += cavalryOff;
            m.HorseArchers -= horseArchersOff;
            m.Archers += horseArchersOff;
            m.Dismounted = cavalryOff + horseArchersOff;
        }

        private static Men TroopMenOf(Formation f)
        {
            Men m = default(Men);
            int count = (f != null) ? f.CountOfUnits : 0;
            if (count <= 0)
            {
                return m;
            }
            m.Infantry = Math.Max(0, f.GetCountOfUnitsBelongingToLogicalClass(FormationClass.Infantry));
            m.Archers = Math.Max(0, f.GetCountOfUnitsBelongingToLogicalClass(FormationClass.Ranged));
            m.Cavalry = Math.Max(0, f.GetCountOfUnitsBelongingToLogicalClass(FormationClass.Cavalry));
            m.HorseArchers = Math.Max(0, f.GetCountOfUnitsBelongingToLogicalClass(FormationClass.HorseArcher));
            if (m.Total <= 0)
            {
                switch (f.FormationIndex.DefaultClass())
                {
                    case FormationClass.Ranged: m.Archers = count; break;
                    case FormationClass.Cavalry: m.Cavalry = count; break;
                    case FormationClass.HorseArcher: m.HorseArchers = count; break;
                    default: m.Infantry = count; break;
                }
            }
            return m;
        }

        private static Category CategoryOf(Men m)
        {
            int mounted = m.Mounted;
            if (mounted > 0 && mounted * 2 >= m.Total)
            {
                return (m.HorseArchers > m.Cavalry) ? Category.HorseArchers : Category.Cavalry;
            }
            return (m.Archers > m.Infantry) ? Category.Archers : Category.Infantry;
        }

        /// <summary>Any thread: the formation's category by troop type (majority; mounted when at least half its men are).</summary>
        internal static Category CategoryOf(Formation f)
        {
            return CategoryOf(MenOf(f));
        }

        /// <summary>Mounted by troop type (melee cavalry or horse archers) less the riders who lost their horse; ammo never changes it.</summary>
        internal static bool IsMounted(Formation f)
        {
            Category c = CategoryOf(f);
            return c == Category.Cavalry || c == Category.HorseArchers;
        }

        internal static bool IsHorseArchers(Formation f)
        {
            return CategoryOf(f) == Category.HorseArchers;
        }

        /// <summary>Foot archers by troop type (a dry archer formation stays archers).</summary>
        internal static bool IsArchers(Formation f)
        {
            return CategoryOf(f) == Category.Archers;
        }

        internal static bool IsMeleeInfantryType(Formation f)
        {
            return f != null && f.CountOfUnits > 0 && CategoryOf(f) == Category.Infantry;
        }

        /// <summary>Share of the formation's archer + horse-archer men that still has a ranged weapon with ammunition (cached ranged ratios); 1 without shooters.</summary>
        internal static float AmmoShare(Formation f)
        {
            Men m = MenOf(f);
            if (m.Shooters <= 0)
            {
                return 1f;
            }
            FormationQuerySystem q = f.QuerySystem;
            float armed = (q.RangedUnitRatio + q.RangedCavalryUnitRatio) * f.CountOfUnits;
            return Math.Min(1f, armed / m.Shooters);
        }

        /// <summary>The one dry rule: a shooters' ammunition share under DryAmmoShare.</summary>
        internal static bool IsDryAmmo(float ammo)
        {
            return ammo < DryAmmoShare;
        }

        /// <summary>Dry state: an archer / horse-archer formation whose shooters with ammunition are under DryAmmoShare.</summary>
        internal static bool IsDry(Formation f)
        {
            return IsDry(f, out _);
        }

        /// <summary>As IsDry; ammo = AmmoShare for an archer / horse-archer formation, NaN otherwise.</summary>
        internal static bool IsDry(Formation f, out float ammo)
        {
            ammo = float.NaN;
            if (f == null || f.CountOfUnits <= 0)
            {
                return false;
            }
            Category c = CategoryOf(f);
            if (c != Category.Archers && c != Category.HorseArchers)
            {
                return false;
            }
            ammo = AmmoShare(f);
            return IsDryAmmo(ammo);
        }

        // Snapshot array index: Category for own, CategoryCount + Category for enemy
        private const int Own = 0;
        private const int Enemy = CategoryCount;

        /// <summary>One team's composition. Shares are of side weighted men (0..1), smoothed; men are raw.</summary>
        internal sealed class Snapshot
        {
            public float Time { get; internal set; }
            /// <summary>Shares by category: 0..3 own, 4..7 enemy.</summary>
            internal readonly float[] Shares = new float[2 * CategoryCount];
            /// <summary>Raw men by category: 0..3 own, 4..7 enemy.</summary>
            internal readonly int[] MenBy = new int[2 * CategoryCount];
            public float Infantry => Shares[Own + (int)Category.Infantry];
            public float Ranged => Shares[Own + (int)Category.Archers];
            public float Cavalry => Shares[Own + (int)Category.Cavalry];
            public float HorseArchers => Shares[Own + (int)Category.HorseArchers];
            public float Mounted { get; internal set; }
            /// <summary>Horse archers' share of our mounted strength.</summary>
            public float HorseArcherOfMounted { get; internal set; }
            public float EnemyMeleeFoot => Shares[Enemy + (int)Category.Infantry];
            public float EnemyRanged => Shares[Enemy + (int)Category.Archers];
            public float EnemyMeleeCavalry => Shares[Enemy + (int)Category.Cavalry];
            public float EnemyHorseArchers => Shares[Enemy + (int)Category.HorseArchers];
            /// <summary>Our non-token mounted strength / the enemy's non-token melee cavalry strength (smoothed weighted men), capped at MaxCavalryStrengthRatio.</summary>
            public float CavalryStrengthRatio { get; internal set; }
            /// <summary>Raw weighted men.</summary>
            public float OwnStrength { get; internal set; }
            public float EnemyStrength { get; internal set; }
            /// <summary>Enemy weighted men at the first snapshot, raised by every later enemy arrival (CavalryWearDown's losses).</summary>
            public float StartEnemyStrength { get; internal set; }
            public int OwnMen { get; internal set; }
            public int InfantryMen => MenBy[Own + (int)Category.Infantry];
            public int RangedMen => MenBy[Own + (int)Category.Archers];
            public int CavalryMen => MenBy[Own + (int)Category.Cavalry];
            public int HorseArcherMen => MenBy[Own + (int)Category.HorseArchers];
            public int EnemyMen { get; internal set; }
            public int EnemyMeleeFootMen => MenBy[Enemy + (int)Category.Infantry];
            public int EnemyRangedMen => MenBy[Enemy + (int)Category.Archers];
            public int EnemyMeleeCavalryMen => MenBy[Enemy + (int)Category.Cavalry];
            public int EnemyHorseArcherMen => MenBy[Enemy + (int)Category.HorseArchers];
            /// <summary>Riders without a horse, already counted as infantry / archers (log only).</summary>
            public int OwnDismounted { get; internal set; }
            public int EnemyDismounted { get; internal set; }
            /// <summary>This team alone (not the side), raw: non-token mounted weighted men / its weighted men; NaN without men.</summary>
            public float TeamMountedShare { get; internal set; } = float.NaN;
            /// <summary>Bit (1 &lt;&lt; Category) set: that category is a token force (own / enemy side).</summary>
            public int OwnToken { get; internal set; }
            public int EnemyToken { get; internal set; }
            /// <summary>ArmyScale says no plans for the side: every k is 0.</summary>
            public bool Limited { get; internal set; }
            public string ScaleReason { get; internal set; }
            /// <summary>"f1:Ranged=0.85 f3:HorseArcher=0.10(dry)" for the team's archer / horse-archer formations; "-" without.</summary>
            public string DryText { get; internal set; }

            // smoothed weighted men by category: 0..3 own, 4..7 enemy; TeamS: this team alone
            internal readonly float[] S = new float[2 * CategoryCount];
            internal readonly float[] TeamS = new float[CategoryCount];
            internal int MinOwnMen, MinEnemyMen, SeedGeneration;

            // ---- the plan (decided in Build)

            /// <summary>This team's smoothed infantry share of its weighted men (the main rule's input).</summary>
            public float TeamInfantryShare { get; internal set; }
            /// <summary>The one main-body verdict: infantry may lead (FormationCohesion.TeamMain); never while own infantry is a token force.</summary>
            public bool InfantryLeads { get; internal set; }
            /// <summary>FormationCohesion.TeamMain at the build.</summary>
            public Formation Main { get; internal set; }
            /// <summary>No main and not Limited: the no-main roles apply.</summary>
            public bool NoMain { get; internal set; }
            /// <summary>The position anchor without a main (null with a main or without a candidate).</summary>
            public Formation FootAnchor { get; internal set; }
            /// <summary>"main", "noMain", "none(...)".</summary>
            public string AnchorReason { get; internal set; } = "-";
            /// <summary>The defender's archer anchor without a main (RBMBehaviorArcherStand stands it; Limited sides too); null otherwise.</summary>
            public Formation LineHolder { get; internal set; }
            public string LineReason { get; internal set; } = "-";
            public float KFoot { get; internal set; }
            public float KCav { get; internal set; }
            public float KWeak { get; internal set; }
            public float KMount { get; internal set; }
            public float KHA { get; internal set; }
            public bool FootRangedEngage { get; internal set; }

            public bool IsOwnToken(Category c)
            {
                return (OwnToken & (1 << (int)c)) != 0;
            }

            public bool IsEnemyToken(Category c)
            {
                return (EnemyToken & (1 << (int)c)) != 0;
            }

            /// <summary>Present and not a token force.</summary>
            public bool OwnReal(Category c)
            {
                return MenBy[Own + (int)c] > 0 && !IsOwnToken(c);
            }
        }

        private static int _reseedGeneration;

        /// <summary>Main thread (terrain publish): the next snapshot of every team is reseeded (no smoothing).</summary>
        internal static void RequestReseed()
        {
            Interlocked.Increment(ref _reseedGeneration);
        }

        private static readonly ConcurrentDictionary<Team, Snapshot> Snapshots = new ConcurrentDictionary<Team, Snapshot>();
        private static readonly ConcurrentDictionary<Formation, bool> DryLatch = new ConcurrentDictionary<Formation, bool>();
        private static readonly ConcurrentDictionary<BattleSideEnum, byte> ScaleLogged = new ConcurrentDictionary<BattleSideEnum, byte>();
        /// <summary>Team -> mission time of its last Build that threw.</summary>
        private static readonly ConcurrentDictionary<Team, float> FailedAt = new ConcurrentDictionary<Team, float>();

        internal static void Clear()
        {
            Snapshots.Clear();
            FailedAt.Clear();
            DryLatch.Clear();
            ScaleLogged.Clear();
            MountedFloorLogged.Clear();
            FollowJoinedLatch.Clear();
            NoteStates.Clear();
            ReapplySeen.Clear();
            LogEvent dropped;
            while (LogEvents.TryDequeue(out dropped))
            {
            }
            Interlocked.Exchange(ref _queued, 0);
            CavalryWearDown.Clear();
        }

        /// <summary>k = clamp((x - start) / (full - start), 0, 1); a falling ramp when full &lt; start.</summary>
        public static float Ramp(float x, float start, float full)
        {
            if (float.IsNaN(x) || full == start)
            {
                return (x >= full) ? 1f : 0f;
            }
            float k = (x - start) / (full - start);
            return (k <= 0f) ? 0f : ((k >= 1f) ? 1f : k);
        }

        /// <summary>AI thread: the team's snapshot, rebuilt when older than RefreshSeconds; null without a team / mission.</summary>
        internal static Snapshot For(Team team)
        {
            if (team == null || Mission.Current == null)
            {
                return null;
            }
            float now = MBCommon.GetTotalMissionTime();
            Snapshot old;
            bool has = Snapshots.TryGetValue(team, out old);
            if (has && now - old.Time < RefreshSeconds && now >= old.Time)
            {
                return old;
            }
            // a failed build waits RefreshSeconds like a good one (no rebuild on every call)
            float failedAt;
            if (FailedAt.TryGetValue(team, out failedAt) && now - failedAt < RefreshSeconds && now >= failedAt)
            {
                return null;
            }
            Snapshot snap;
            try
            {
                snap = Build(team, has ? old : null, now);
            }
            catch (Exception e)
            {
                FailedAt[team] = now;
                AiTickGuard.Report("ArmyComposition.Build", e, null);
                return null;
            }
            Snapshots[team] = snap;
            if (FollowBand(snap) && !(has && FollowBand(old)))
            {
                WakeFollow(team);
            }
            // with a main too: token mounted escorts ride with it
            RBMBehaviorArcherStand.Wake(team);
            return snap;
        }

        /// <summary>Any thread, read only: the team's last built snapshot (never builds); false before the first.</summary>
        internal static bool TryPeek(Team team, out Snapshot snapshot)
        {
            snapshot = null;
            return team != null && Snapshots.TryGetValue(team, out snapshot) && snapshot != null;
        }

        /// <summary>FollowCavalry is awake (WeightFactor 1) only while kMount > 0: kMount = 0 leaves FindBestBehavior exactly as before.</summary>
        internal static bool FollowBand(Snapshot s)
        {
            return KMount(s) > 0f;
        }

        /// <summary>AI thread: wakes (WeightFactor 1) the dormant FollowCavalry behaviors of the team's AI melee foot.</summary>
        private static void WakeFollow(Team team)
        {
            MBList<Formation> formations = team.FormationsIncludingEmpty;
            for (int i = 0; i < formations.Count; i++)
            {
                Formation f = formations[i];
                if (f == null || f.CountOfUnits <= 0 || f.AI == null || !RBMBehaviorFollowCavalry.IsCandidate(f))
                {
                    continue;
                }
                RBMBehaviorFollowCavalry follow = f.AI.GetBehavior<RBMBehaviorFollowCavalry>();
                if (follow != null && follow.WeightFactor <= 0f)
                {
                    follow.WeightFactor = 1f;
                }
            }
        }

        private static Snapshot Build(Team team, Snapshot previous, float now)
        {
            Snapshot s = new Snapshot { Time = now };
            float[] raw = new float[2 * CategoryCount];
            int[] men = s.MenBy;
            StringBuilder dry = null;
            int ownDismounted = 0, enemyDismounted = 0;
            float[] teamRaw = new float[CategoryCount];
            foreach (Team other in Mission.Current.Teams)
            {
                if (other == null)
                {
                    continue;
                }
                bool enemy = other.IsEnemyOf(team);
                if (!enemy && other.Side != team.Side)
                {
                    continue;
                }
                foreach (Formation f in other.FormationsIncludingSpecialAndEmpty)
                {
                    if (f == null || f.CountOfUnits <= 0)
                    {
                        continue;
                    }
                    Men m = MenOf(f);
                    int offset = enemy ? Enemy : Own;
                    if (enemy)
                    {
                        enemyDismounted += m.Dismounted;
                    }
                    else
                    {
                        ownDismounted += m.Dismounted;
                    }
                    if (other == team)
                    {
                        for (int c = 0; c < CategoryCount; c++)
                        {
                            teamRaw[c] += m.Of((Category)c) * CategoryWeights[c];
                        }
                    }
                    for (int c = 0; c < CategoryCount; c++)
                    {
                        int n = m.Of((Category)c);
                        men[offset + c] += n;
                        raw[offset + c] += n * CategoryWeights[c];
                    }
                    if (other == team && m.Shooters > 0)
                    {
                        Category cat = CategoryOf(m);
                        if (cat == Category.Archers || cat == Category.HorseArchers)
                        {
                            float ammo = AmmoShare(f);
                            bool isDry = IsDryAmmo(ammo);
                            dry = dry ?? new StringBuilder(64);
                            if (dry.Length > 0)
                            {
                                dry.Append(' ');
                            }
                            dry.Append(TerrainCavalry.FormationName(f)).Append('=').Append(F2(ammo)).Append(isDry ? "(dry)" : "");
                            bool was;
                            DryLatch.TryGetValue(f, out was);
                            if (was != isDry)
                            {
                                DryLatch[f] = isDry;
                                Note(team, f, KindState, "dry", (isDry ? "enter" : "exit") + " ammo=" + F2(ammo) + (isDry ? "<" : ">=") + F2(DryAmmoShare)
                                    + " shooters=" + m.Shooters.ToString(CultureInfo.InvariantCulture) + " men=" + f.CountOfUnits.ToString(CultureInfo.InvariantCulture));
                            }
                        }
                    }
                }
            }
            s.DryText = (dry != null) ? dry.ToString() : "-";
            s.OwnDismounted = ownDismounted;
            s.EnemyDismounted = enemyDismounted;
            s.OwnMen = Sum(men, Own);
            s.EnemyMen = Sum(men, Enemy);
            s.OwnStrength = Sum(raw, Own);
            s.EnemyStrength = Sum(raw, Enemy);

            ArmyScale.SideScale scale = ArmyScale.For(team.Side);
            s.Limited = scale != null && scale.NoPlans;
            s.ScaleReason = (scale != null) ? scale.Reason : "unread";
            if (s.Limited && ScaleLogged.TryAdd(team.Side, 1))
            {
                Note(team, null, KindState, "scale", "suppressed side=" + team.Side + " mode=" + scale.Reason + " men=" + s.OwnMen.ToString(CultureInfo.InvariantCulture)
                    + ": every k 0, no FollowCavalry / wearDown / initiative" + (scale.ReactionsOff ? ", no-infantry battle-joined rule off" : ""));
            }

            int generation = Volatile.Read(ref _reseedGeneration);
            string reseedWhy = null;
            if (previous == null)
            {
                reseedWhy = "first";
            }
            else if (previous.SeedGeneration != generation)
            {
                reseedWhy = "publish";
            }
            else if (s.OwnMen > ReseedGrowth * previous.MinOwnMen)
            {
                reseedWhy = "ownGrew";
            }
            else if (s.EnemyMen > ReseedGrowth * previous.MinEnemyMen)
            {
                reseedWhy = "enemyGrew";
            }
            float a = 1f;
            if (reseedWhy != null)
            {
                s.MinOwnMen = s.OwnMen;
                s.MinEnemyMen = s.EnemyMen;
                s.SeedGeneration = generation;
                if (previous != null && s.OwnMen + s.EnemyMen > 0)
                {
                    Note(team, null, KindState, "reseed", reseedWhy + " own=" + previous.MinOwnMen.ToString(CultureInfo.InvariantCulture) + "->" + s.OwnMen.ToString(CultureInfo.InvariantCulture)
                        + " enemy=" + previous.MinEnemyMen.ToString(CultureInfo.InvariantCulture) + "->" + s.EnemyMen.ToString(CultureInfo.InvariantCulture));
                }
            }
            else
            {
                s.MinOwnMen = Math.Min(previous.MinOwnMen, s.OwnMen);
                s.MinEnemyMen = Math.Min(previous.MinEnemyMen, s.EnemyMen);
                s.SeedGeneration = previous.SeedGeneration;
                float dt = now - previous.Time;
                a = (dt > 0f) ? 1f - (float)Math.Exp(-dt / SmoothSeconds) : 0f;
            }
            bool smooth = previous != null && reseedWhy == null;
            for (int i = 0; i < s.S.Length; i++)
            {
                s.S[i] = smooth ? previous.S[i] + (raw[i] - previous.S[i]) * a : raw[i];
            }
            for (int c = 0; c < CategoryCount; c++)
            {
                s.TeamS[c] = smooth ? previous.TeamS[c] + (teamRaw[c] - previous.TeamS[c]) * a : teamRaw[c];
            }

            // enemy arrivals are not losses: every rise of the enemy's strength raises the start
            if (previous == null || previous.StartEnemyStrength <= 0f)
            {
                s.StartEnemyStrength = s.EnemyStrength;
            }
            else
            {
                float rise = s.EnemyStrength - previous.EnemyStrength;
                s.StartEnemyStrength = previous.StartEnemyStrength + Math.Max(0f, rise);
                if (rise >= 1f)
                {
                    Note(team, null, KindState, "startEnemy", "raised +" + F2(rise) + " -> " + F2(s.StartEnemyStrength) + " (enemy arrivals)");
                }
            }

            Derive(s, previous?.OwnToken ?? 0, previous?.EnemyToken ?? 0);
            const int cav = (int)Category.Cavalry, ha = (int)Category.HorseArchers;
            float teamTotal = Sum(teamRaw, 0);
            if (teamTotal > 0f)
            {
                s.TeamMountedShare = ((s.IsOwnToken(Category.Cavalry) ? 0f : teamRaw[cav]) + (s.IsOwnToken(Category.HorseArchers) ? 0f : teamRaw[ha])) / teamTotal;
            }

            BuildPlan(team, s, previous);
            LogPlan(team, previous, s);
            return s;
        }

        /// <summary>Pure: shares, mounted / horse-archer shares, token bits (hysteresis from the previous bits) and the cavalry strength ratio from s.S and s.MenBy.</summary>
        internal static void Derive(Snapshot s, int previousOwnToken, int previousEnemyToken)
        {
            float own = Sum(s.S, Own);
            float foe = Sum(s.S, Enemy);
            for (int c = 0; c < CategoryCount; c++)
            {
                s.Shares[Own + c] = Share(s.S[Own + c], own);
                s.Shares[Enemy + c] = Share(s.S[Enemy + c], foe);
            }
            const int cav = (int)Category.Cavalry, ha = (int)Category.HorseArchers;
            s.Mounted = s.Cavalry + s.HorseArchers;
            s.HorseArcherOfMounted = Share(s.S[Own + ha], s.S[Own + cav] + s.S[Own + ha]);
            s.OwnToken = Tokens(previousOwnToken, s.S, Own, own, s.MenBy);
            s.EnemyToken = Tokens(previousEnemyToken, s.S, Enemy, foe, s.MenBy);
            float ownMounted = (s.IsOwnToken(Category.Cavalry) ? 0f : s.S[Own + cav]) + (s.IsOwnToken(Category.HorseArchers) ? 0f : s.S[Own + ha]);
            float enemyCavalry = s.IsEnemyToken(Category.Cavalry) ? 0f : s.S[Enemy + cav];
            s.CavalryStrengthRatio = (enemyCavalry > 0f) ? Math.Min(ownMounted / enemyCavalry, MaxCavalryStrengthRatio) : ((ownMounted > 0f) ? MaxCavalryStrengthRatio : 0f);
        }

        /// <summary>Pure: the five k ramps and the FootRangedEngage band (held = it was on).</summary>
        internal static void ComputeRamps(Snapshot s, bool footRangedHeld)
        {
            s.KFoot = ComputeKFoot(s);
            s.KCav = ComputeKCav(s);
            s.KWeak = ComputeKWeak(s);
            s.KMount = ComputeKMount(s);
            s.KHA = ComputeKHA(s);
            s.FootRangedEngage = footRangedHeld ? s.KFoot > FootRangedOff : s.KFoot >= FootRangedOn;
        }

        /// <summary>Pure, the one main-body verdict: infantry leads at a share of MainEnterStrengthShare, kept down to MainKeepStrengthShare, never as a token force.</summary>
        internal static bool LeadsVerdict(bool held, bool infantryToken, float infantryShare)
        {
            return !infantryToken && infantryShare >= (held ? MainKeepStrengthShare : MainEnterStrengthShare);
        }

        /// <summary>The decisions: k ramps, FootRangedEngage band, infantry-leads verdict, main, foot anchor, archer line holder. Latches come from previous.</summary>
        private static void BuildPlan(Team team, Snapshot s, Snapshot previous)
        {
            ComputeRamps(s, previous != null && previous.FootRangedEngage);

            float teamTotal = Sum(s.TeamS, 0);
            s.TeamInfantryShare = Share(s.TeamS[(int)Category.Infantry], teamTotal);
            s.InfantryLeads = LeadsVerdict(previous != null && previous.InfantryLeads, s.IsOwnToken(Category.Infantry), s.TeamInfantryShare);

            s.Main = FormationCohesion.TeamMainWith(team, s.InfantryLeads);
            s.NoMain = s.Main == null && !s.Limited;
            if (s.Main != null)
            {
                s.AnchorReason = "main";
            }
            else
            {
                Formation held = previous?.FootAnchor;
                Formation best = StrongestAnchor(team, s);
                bool heldValid = best != null && held != null && held != best && held.Team == team && held.CountOfUnits > 0 && IsAnchorCandidate(held, s)
                    && (held.IsAIControlled || !best.IsAIControlled);
                float heldPower = heldValid ? held.QuerySystem.FormationPower : 0f;
                float bestPower = (best != null) ? best.QuerySystem.FormationPower : 0f;
                bool outpowered = heldValid && bestPower >= SwitchRatio * heldPower;
                bool keep = KeepsAnchor(heldValid, heldPower, bestPower, outpowered && HeldInContact(held));
                s.FootAnchor = keep ? held : best;
                s.AnchorReason = (s.FootAnchor != null) ? ((keep && outpowered) ? "keptInContact" : "noMain") : NoAnchorReason(s);
            }

            // user rule: infantry that does not lead never holds the line; the defender's archer anchor stands (RBMBehaviorArcherStand), small battles too
            if (s.Main != null)
            {
                s.LineReason = "main";
            }
            else if (team.Side != BattleSideEnum.Defender)
            {
                s.LineReason = "attacker";
            }
            else if (s.FootAnchor == null)
            {
                s.LineReason = "noAnchor";
            }
            else if (!IsArchers(s.FootAnchor))
            {
                s.LineReason = "anchorNotArchers";
            }
            else
            {
                s.LineHolder = s.FootAnchor;
                s.LineReason = "archerAnchor";
            }
        }

        /// <summary>A valid held foot anchor stays while the best is under SwitchRatio x its power, or whatever the best while it is in contact (no mid-fight swap).</summary>
        internal static bool KeepsAnchor(bool heldValid, float heldPower, float bestPower, bool heldInContact)
        {
            return heldValid && (heldInContact || bestPower < SwitchRatio * heldPower);
        }

        /// <summary>AI thread: in melee, its archer stand fighting, or a significant enemy within the stand's join gap.</summary>
        private static bool HeldInContact(Formation held)
        {
            if (RBMAI.Utilities.FormationFightingInMelee(held, InMelee))
            {
                return true;
            }
            RBMBehaviorArcherStand stand = held.AI?.GetBehavior<RBMBehaviorArcherStand>();
            return (stand != null && stand.Fighting) || RBMAI.Utilities.NearestSignificantEnemyGap(held) <= RBMBehaviorArcherStand.JoinGap;
        }

        private static string NoAnchorReason(Snapshot s)
        {
            if (s.InfantryMen + s.RangedMen <= 0)
            {
                return "none(noFoot)";
            }
            if ((s.InfantryMen <= 0 || s.IsOwnToken(Category.Infantry)) && (s.RangedMen <= 0 || s.IsOwnToken(Category.Archers)))
            {
                return s.IsOwnToken(Category.Infantry) ? (s.IsOwnToken(Category.Archers) ? "none(token inf arc)" : "none(token inf)") : "none(token arc)";
            }
            return "none(small)";
        }

        /// <summary>Token bits with hysteresis.</summary>
        internal static int Tokens(int previous, float[] smoothed, int offset, float total, int[] men)
        {
            int bits = 0;
            for (int c = 0; c < CategoryCount; c++)
            {
                int n = men[offset + c];
                float share = Share(smoothed[offset + c], total);
                bool was = (previous & (1 << c)) != 0;
                bool token = n > 0 && (was ? (share < TokenLeaveShare && n < TokenLeaveMen) : (share < TokenEnterShare && n < TokenEnterMen));
                if (token)
                {
                    bits |= 1 << c;
                }
            }
            return bits;
        }

        // ---------------------------------------------------------------- plan diff (COMPEVENT)

        /// <summary>One COMPEVENT per decision that changed since the previous snapshot.</summary>
        private static void LogPlan(Team team, Snapshot was, Snapshot now)
        {
            int wasOwn = was?.OwnToken ?? 0, wasEnemy = was?.EnemyToken ?? 0;
            for (int c = 0; c < CategoryCount; c++)
            {
                LogToken(team, now, false, c, (wasOwn & (1 << c)) != 0, now.IsOwnToken((Category)c));
                LogToken(team, now, true, c, (wasEnemy & (1 << c)) != 0, now.IsEnemyToken((Category)c));
            }
            bool leadsWas = was != null && was.InfantryLeads;
            if (now.InfantryLeads != leadsWas && (was != null || now.InfantryLeads))
            {
                Note(team, null, KindState, "infantryLeads", (now.InfantryLeads ? "on share=" : "off share=") + F2(now.TeamInfantryShare)
                    + (now.InfantryLeads ? ">=" + F2(MainEnterStrengthShare) : (now.IsOwnToken(Category.Infantry) ? " tokenInfantry" : "<" + F2(MainKeepStrengthShare))));
            }
            if (now.FootRangedEngage != (was != null && was.FootRangedEngage))
            {
                Note(team, null, KindState, "footRangedEngage", (now.FootRangedEngage ? "on" : "off") + " kFoot=" + F2(now.KFoot)
                    + (now.FootRangedEngage ? ">=" + F2(FootRangedOn) : "<=" + F2(FootRangedOff)));
            }
            Formation mainWas = was?.Main;
            if (now.Main != mainWas)
            {
                Formation f = now.Main ?? mainWas;
                Note(team, f, KindRole, "main", ((now.Main != null) ? "gained " : "lost ") + TerrainCavalry.FormationName(mainWas) + "->" + TerrainCavalry.FormationName(now.Main)
                    + (now.InfantryLeads ? " leads" : " noLead") + " share=" + F2(now.TeamInfantryShare) + " men=" + ((f != null) ? f.CountOfUnits : 0).ToString(CultureInfo.InvariantCulture)
                    + " minMen=" + FormationCohesion.MinMainMen(team).ToString(CultureInfo.InvariantCulture));
            }
            // the anchor while a main exists is the main: no line
            if (now.AnchorReason != "main" && (now.FootAnchor != was?.FootAnchor || now.AnchorReason != was?.AnchorReason))
            {
                Note(team, now.FootAnchor, KindRole, "anchor", TerrainCavalry.FormationName(was?.FootAnchor) + "->" + TerrainCavalry.FormationName(now.FootAnchor)
                    + " reason=" + now.AnchorReason + ((now.FootAnchor != null) ? " men=" + now.FootAnchor.CountOfUnits.ToString(CultureInfo.InvariantCulture)
                    + " power=" + F2(now.FootAnchor.QuerySystem.FormationPower) : ""));
            }
            if (now.LineHolder != was?.LineHolder)
            {
                Note(team, now.LineHolder ?? was?.LineHolder, KindRole, "line", "archerStand " + TerrainCavalry.FormationName(was?.LineHolder) + "->" + TerrainCavalry.FormationName(now.LineHolder)
                    + " reason=" + now.LineReason + ((now.LineHolder != null) ? " men=" + now.LineHolder.CountOfUnits.ToString(CultureInfo.InvariantCulture)
                    + " ammo=" + F2(AmmoShare(now.LineHolder)) : ""));
            }
        }

        private static void LogToken(Team team, Snapshot s, bool enemy, int c, bool was, bool token)
        {
            int n = s.MenBy[(enemy ? Enemy : Own) + c];
            if (token == was || (n <= 0 && !was))
            {
                return;
            }
            float share = s.Shares[(enemy ? Enemy : Own) + c];
            Note(team, null, KindState, "token", (enemy ? "enemy " : "own ") + CategoryNames[c] + (token ? " on" : " off") + " share=" + F2(share) + " men="
                + n.ToString(CultureInfo.InvariantCulture) + (token ? " (<" + F2(TokenEnterShare) + " and <" + TokenEnterMen.ToString(CultureInfo.InvariantCulture) + ")"
                : " (>=" + F2(TokenLeaveShare) + " or >=" + TokenLeaveMen.ToString(CultureInfo.InvariantCulture) + " or gone)"));
        }

        private static bool HasEnemy(Snapshot s)
        {
            return s != null && !s.Limited && s.EnemyMen > 0 && s.EnemyStrength > 0f;
        }

        /// <summary>Enemy without (much) melee foot: Ramp(1 - EnemyMeleeFoot, 0.7, 0.9), token enemy infantry = none; 0 without a snapshot / enemy / under ArmyScale NoPlans.</summary>
        internal static float KFoot(Snapshot s)
        {
            return (s != null) ? s.KFoot : 0f;
        }

        /// <summary>Enemy with melee cavalry: Ramp(EnemyMeleeCavalry, 0.4, 0.6), token enemy cavalry = none.</summary>
        internal static float KCav(Snapshot s)
        {
            return (s != null) ? s.KCav : 0f;
        }

        /// <summary>Our cavalry outmatched by the enemy's melee cavalry: Ramp(1 - CavalryStrengthRatio, 0.2, 0.6) x kCav; 0 when our mounted categories are all token / absent.</summary>
        internal static float KWeak(Snapshot s)
        {
            return (s != null) ? s.KWeak : 0f;
        }

        /// <summary>Our army is cavalry-heavy: Ramp(non-token mounted share, 0.45, 0.75); 0 without a snapshot / under NoPlans.</summary>
        internal static float KMount(Snapshot s)
        {
            return (s != null) ? s.KMount : 0f;
        }

        /// <summary>Our mounted strength is mostly horse archers: Ramp(HorseArcherOfMounted, 0.3, 0.7); 0 with token / no horse archers or under NoPlans.</summary>
        internal static float KHA(Snapshot s)
        {
            return (s != null) ? s.KHA : 0f;
        }

        private static float ComputeKFoot(Snapshot s)
        {
            return HasEnemy(s) ? Ramp(1f - (s.IsEnemyToken(Category.Infantry) ? 0f : s.EnemyMeleeFoot), KFootStart, KFootFull) : 0f;
        }

        private static float ComputeKCav(Snapshot s)
        {
            return HasEnemy(s) ? Ramp(s.IsEnemyToken(Category.Cavalry) ? 0f : s.EnemyMeleeCavalry, KCavStart, KCavFull) : 0f;
        }

        private static float ComputeKWeak(Snapshot s)
        {
            if (!HasEnemy(s) || !(s.OwnReal(Category.Cavalry) || s.OwnReal(Category.HorseArchers)))
            {
                return 0f;
            }
            return Ramp(1f - s.CavalryStrengthRatio, KWeakStart, KWeakFull) * ComputeKCav(s);
        }

        private static float RealMounted(Snapshot s)
        {
            return (s.IsOwnToken(Category.Cavalry) ? 0f : s.Cavalry) + (s.IsOwnToken(Category.HorseArchers) ? 0f : s.HorseArchers);
        }

        private static float ComputeKMount(Snapshot s)
        {
            return (!s.Limited && s.OwnMen > 0) ? Ramp(RealMounted(s), KMountStart, KMountFull) : 0f;
        }

        private static float ComputeKHA(Snapshot s)
        {
            return (!s.Limited && s.OwnReal(Category.HorseArchers)) ? Ramp(s.HorseArcherOfMounted, KHaStart, KHaFull) : 0f;
        }

        /// <summary>Any thread, read only: the team's weighted, token-aware, dismount-corrected mounted share from its last snapshot; NaN before one / without men.</summary>
        internal static float PeekTeamMountedShare(Team team)
        {
            Snapshot s;
            return TryPeek(team, out s) ? s.TeamMountedShare : float.NaN;
        }

        private static readonly ConcurrentDictionary<Team, float> MountedFloorLogged = new ConcurrentDictionary<Team, float>();

        /// <summary>Any thread: COMPEVENT mountedFloor when the FullScaleAttack all-mounted floor's k moves by 0.1 or crosses 0.</summary>
        internal static void NoteMountedFloor(Team team, float k, float share, string source, float floor)
        {
            if (team == null)
            {
                return;
            }
            float last;
            bool has = MountedFloorLogged.TryGetValue(team, out last);
            if (has ? (Math.Abs(k - last) < 0.1f && (k > 0f) == (last > 0f)) : k <= 0f)
            {
                return;
            }
            MountedFloorLogged[team] = k;
            Note(team, null, KindTactic, "mountedFloor", "FullScaleAttack k=" + F2(k) + " mnt=" + F2(share) + " src=" + source + " floor=" + F2(floor));
        }

        internal static float KFoot(Team team) { return KFoot(SafeFor(team)); }
        internal static float KCav(Team team) { return KCav(SafeFor(team)); }
        internal static float KWeak(Team team) { return KWeak(SafeFor(team)); }
        internal static float KMount(Team team) { return KMount(SafeFor(team)); }
        internal static float KHA(Team team) { return KHA(SafeFor(team)); }

        /// <summary>AI thread: the own category is a token force (false when absent).</summary>
        internal static bool IsToken(Team team, Category c)
        {
            Snapshot s = SafeFor(team);
            return s != null && s.IsOwnToken(c);
        }

        /// <summary>AI thread: the enemy's foot (melee infantry and archers) is absent or token forces only ("only mounted left" tests); false without a snapshot.</summary>
        internal static bool EnemyFootTokenOnly(Team team)
        {
            Snapshot s = SafeFor(team);
            return s != null && (s.EnemyMeleeFootMen <= 0 || s.IsEnemyToken(Category.Infantry)) && (s.EnemyRangedMen <= 0 || s.IsEnemyToken(Category.Archers));
        }

        /// <summary>AI thread: a formation of an enemy token category (for "does X exist" tests).</summary>
        internal static bool IsTokenEnemyFormation(Team team, Formation enemy)
        {
            if (enemy == null)
            {
                return false;
            }
            Category c = CategoryOf(enemy);
            Snapshot s = SafeFor(team);
            return s != null && s.IsEnemyToken(c);
        }

        /// <summary>AI thread: ArmyScale NoPlans for the team's side (small / tiny / bandit / siege scene): the composition roles are off.</summary>
        internal static bool Limited(Team team)
        {
            return ArmyScale.NoPlans(team);
        }

        /// <summary>AI thread, cached data: the first enemy formation significant to one of our foot formations (foot) within EngagedGap of it (edge gap,
        /// ScatterAware), our foot at least ownMelee and the enemy at least enemyMelee in melee (0 = not checked); cavalryOnly: enemy melee cavalry (troop type)
        /// only; includeSpecial: the special formations too (FormationsIncludingSpecialAndEmpty). Null without one.</summary>
        internal static Formation EnemyOnOurFoot(Team team, bool includeSpecial, float ownMelee, float enemyMelee, bool cavalryOnly, out Formation foot)
        {
            foot = null;
            Mission mission = Mission.Current;
            if (team == null || mission == null)
            {
                return null;
            }
            MBList<Formation> own = includeSpecial ? team.FormationsIncludingSpecialAndEmpty : team.FormationsIncludingEmpty;
            for (int i = 0; i < own.Count; i++)
            {
                Formation f = own[i];
                if (f == null || f.CountOfUnits <= 0 || IsMounted(f) || (ownMelee > 0f && HoldLateCharge.MeleeShare(f) < ownMelee))
                {
                    continue;
                }
                Footprint footShape;
                if (!FormationGeometry.TryGet(f, FootprintMode.ScatterAware, out footShape))
                {
                    continue;
                }
                foreach (Team other in mission.Teams)
                {
                    if (other == null || !other.IsEnemyOf(team))
                    {
                        continue;
                    }
                    MBList<Formation> enemies = includeSpecial ? other.FormationsIncludingSpecialAndEmpty : other.FormationsIncludingEmpty;
                    for (int j = 0; j < enemies.Count; j++)
                    {
                        Formation e = enemies[j];
                        if (e == null || e.CountOfUnits <= 0 || (cavalryOnly && CategoryOf(e) != Category.Cavalry) || !ThreatSignificance.IsSignificant(f, e))
                        {
                            continue;
                        }
                        Footprint shape;
                        if (!FormationGeometry.TryGet(e, FootprintMode.ScatterAware, out shape) || FormationGeometry.Gap(footShape, shape) > EngagedGap)
                        {
                            continue;
                        }
                        if (enemyMelee <= 0f || HoldLateCharge.MeleeShare(e) >= enemyMelee)
                        {
                            foot = f;
                            return e;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>AI thread: the no-main roles apply (the archers' initiative): the plan has no main and is not Limited.</summary>
        /// <summary>AI thread: the side's weighted strength is at least the enemy's (false without a snapshot or enemy).</summary>
        internal static bool NotWeaker(Team team)
        {
            Snapshot s = SafeFor(team);
            return s != null && s.EnemyStrength > 0f && s.OwnStrength >= s.EnemyStrength;
        }

        internal static bool NoMainRoles(Team team)
        {
            Snapshot s = SafeFor(team);
            return s != null && s.NoMain;
        }

        /// <summary>AI thread: the plan's infantry-leads verdict (FormationCohesion's main rule); true without a plan.</summary>
        internal static bool InfantryLeads(Team team)
        {
            Snapshot s = SafeFor(team);
            return s == null || s.InfantryLeads;
        }

        /// <summary>AI thread: the defender's archer stand holder (no main, archer anchor; Limited sides too), else null.</summary>
        internal static Formation LineHolder(Team team)
        {
            Formation f = SafeFor(team)?.LineHolder;
            return (f != null && f.CountOfUnits > 0) ? f : null;
        }

        private static Snapshot SafeFor(Team team)
        {
            try
            {
                return For(team);
            }
            catch (Exception e)
            {
                AiTickGuard.Report("ArmyComposition.For", e, null);
                return null;
            }
        }

        /// <summary>AI thread: enemy foot ranged formations count as an engaging enemy for the team's holds (kFoot on at 0.5, off at 0.3).</summary>
        internal static bool FootRangedEngage(Team team)
        {
            Snapshot s = SafeFor(team);
            return s != null && s.FootRangedEngage;
        }

        private sealed class FollowJoinState
        {
            public bool Joined;
            public float JoinedAt = float.NaN;
            public float FalseSince = float.NaN;
        }

        private static readonly ConcurrentDictionary<Team, FollowJoinState> FollowJoinedLatch = new ConcurrentDictionary<Team, FollowJoinState>();

        /// <summary>AI thread: whether the team's foot may follow its cavalry (RBMBehaviorFollowCavalry): the attacker always; the defender while its
        /// live battle-position hold has engaged (RBMBehaviorHoldPosition.TeamLineHold, so a recall closes it again), else while its battle is joined
        /// (Utilities.HasBattleBeenJoined from its main). Not latched: on for at least FollowJoinHoldSeconds, off after FollowUnjoinSeconds of false.</summary>
        internal static bool FollowJoined(Team team)
        {
            if (team == null)
            {
                return false;
            }
            if (team.Side != BattleSideEnum.Defender)
            {
                return true;
            }
            FollowJoinState state = FollowJoinedLatch.GetOrAdd(team, t => new FollowJoinState());
            float now = MBCommon.GetTotalMissionTime();
            lock (state)
            {
                bool engaged;
                bool raw = RBMBehaviorHoldPosition.TeamLineHold(team, out engaged)
                    ? engaged
                    : RBMAI.Utilities.HasBattleBeenJoined(team, FormationCohesion.TeamMain(team), state.Joined);
                if (raw)
                {
                    state.FalseSince = float.NaN;
                    if (!state.Joined)
                    {
                        state.Joined = true;
                        state.JoinedAt = now;
                    }
                    return true;
                }
                if (!state.Joined)
                {
                    return false;
                }
                if (float.IsNaN(state.FalseSince))
                {
                    state.FalseSince = now;
                }
                if (now - state.JoinedAt >= FollowJoinHoldSeconds && now - state.FalseSince >= FollowUnjoinSeconds)
                {
                    state.Joined = false;
                    state.FalseSince = float.NaN;
                    return false;
                }
                return true;
            }
        }

        /// <summary>"kFoot=0.00 kCav=0.00 kWeak=0.00".</summary>
        internal static string DescribeK(Team team)
        {
            Snapshot s = SafeFor(team);
            return "kFoot=" + F2(KFoot(s)) + " kCav=" + F2(KCav(s)) + " kWeak=" + F2(KWeak(s));
        }

        /// <summary>"kMount=0.00 kHA=0.00".</summary>
        internal static string DescribeOwnK(Team team)
        {
            Snapshot s = SafeFor(team);
            return "kMount=" + F2(KMount(s)) + " kHA=" + F2(KHA(s));
        }

        internal static float Lerp(float a, float b, float k)
        {
            return a + (b - a) * k;
        }

        private static float Share(float part, float total)
        {
            return (total > 0f) ? part / total : 0f;
        }

        // one side's CategoryCount values from offset, summed in category order
        private static float Sum(float[] values, int offset)
        {
            float sum = 0f;
            for (int c = 0; c < CategoryCount; c++)
            {
                sum += values[offset + c];
            }
            return sum;
        }

        private static int Sum(int[] values, int offset)
        {
            int sum = 0;
            for (int c = 0; c < CategoryCount; c++)
            {
                sum += values[offset + c];
            }
            return sum;
        }

        /// <summary>The strongest formation passing the test, AI-controlled ones first, other than the two excluded.</summary>
        internal static Formation Strongest(List<Formation> formations, Func<Formation, bool> test, Formation exclude1, Formation exclude2)
        {
            Formation best = null;
            foreach (Formation f in formations)
            {
                if (f != exclude1 && f != exclude2 && test(f) && Stronger(f, best))
                {
                    best = f;
                }
            }
            return best;
        }

        /// <summary>As Strongest over the team's formations with men.</summary>
        internal static Formation Strongest(Team team, Func<Formation, bool> test)
        {
            Formation best = null;
            MBList<Formation> formations = team.FormationsIncludingEmpty;
            for (int i = 0; i < formations.Count; i++)
            {
                Formation f = formations[i];
                if (f != null && f.CountOfUnits > 0 && test(f) && Stronger(f, best))
                {
                    best = f;
                }
            }
            return best;
        }

        private static bool Stronger(Formation f, Formation best)
        {
            return best == null || (f.IsAIControlled && !best.IsAIControlled)
                || (f.IsAIControlled == best.IsAIControlled && f.QuerySystem.FormationPower > best.QuerySystem.FormationPower);
        }

        /// <summary>Position anchor candidate: AI foot (infantry / archers by type) of a non-token category, at least MinMainMen men and AnchorMinFootShare of the team's foot men; never dry (it would charge at once and drag its escort along).</summary>
        private static bool IsAnchorCandidate(Formation f, Snapshot s)
        {
            if (f == null || f.Team == null || !f.IsAIControlled || f.CountOfUnits < FormationCohesion.MinMainMen(f.Team) || IsDry(f))
            {
                return false;
            }
            Category c = CategoryOf(f);
            if (c != Category.Infantry && c != Category.Archers)
            {
                return false;
            }
            return !s.IsOwnToken(c) && f.CountOfUnits >= AnchorMinFootShare * (s.InfantryMen + s.RangedMen);
        }

        private static Formation StrongestAnchor(Team team, Snapshot s)
        {
            Formation best = null;
            MBList<Formation> formations = team.FormationsIncludingEmpty;
            for (int i = 0; i < formations.Count; i++)
            {
                Formation f = formations[i];
                if (f != null && f.CountOfUnits > 0 && IsAnchorCandidate(f, s) && Stronger(f, best))
                {
                    best = f;
                }
            }
            return best;
        }

        /// <summary>AI thread: FormationCohesion.TeamMain, else the plan's foot anchor; never mounted, never token foot, null without one (the all-mounted paths apply).</summary>
        internal static Formation PositionAnchor(Team team)
        {
            if (team == null)
            {
                return null;
            }
            Formation main = FormationCohesion.TeamMain(team);
            if (main != null)
            {
                return main;
            }
            Formation anchor = SafeFor(team)?.FootAnchor;
            return (anchor != null && anchor.CountOfUnits > 0) ? anchor : null;
        }

        /// <summary>AI thread: the formation's position anchor, null when that is the formation itself (it acts alone).</summary>
        internal static Formation AnchorFor(Formation formation)
        {
            Formation anchor = PositionAnchor(formation?.Team);
            return (anchor == formation) ? null : anchor;
        }

        /// <summary>"inf=0.62 rng=0.20 cav=0.10 ha=0.08 mnt=0.18 haOfMnt=0.40 eFoot=0.55 eRng=0.15 eCav=0.20 eHa=0.10 cavRatio=0.90".</summary>
        internal static string Describe(Snapshot s)
        {
            if (s == null)
            {
                return "-";
            }
            StringBuilder sb = new StringBuilder(160);
            for (int c = 0; c < CategoryCount; c++)
            {
                sb.Append(ShareLabels[Own + c]).Append(F2(s.Shares[Own + c]));
            }
            sb.Append(" mnt=").Append(F2(s.Mounted));
            sb.Append(" haOfMnt=").Append(F2(s.HorseArcherOfMounted));
            for (int c = 0; c < CategoryCount; c++)
            {
                sb.Append(ShareLabels[Enemy + c]).Append(F2(s.Shares[Enemy + c]));
            }
            sb.Append(" cavRatio=").Append(F2(s.CavalryStrengthRatio));
            return sb.ToString();
        }

        private static readonly string[] ShareLabels = { "inf=", " rng=", " cav=", " ha=", " eFoot=", " eRng=", " eCav=", " eHa=" };

        /// <summary>"own:inf,ha enemy:-" token categories.</summary>
        internal static string DescribeTokens(Snapshot s)
        {
            if (s == null)
            {
                return "-";
            }
            return "own:" + TokenList(s.OwnToken) + " enemy:" + TokenList(s.EnemyToken);
        }

        private static string TokenList(int bits)
        {
            if (bits == 0)
            {
                return "-";
            }
            StringBuilder sb = new StringBuilder(16);
            for (int c = 0; c < CategoryCount; c++)
            {
                if ((bits & (1 << c)) != 0)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(',');
                    }
                    sb.Append(CategoryNames[c]);
                }
            }
            return sb.ToString();
        }

        internal static string Describe(Team team)
        {
            try
            {
                return Describe(For(team));
            }
            catch (Exception e)
            {
                AiTickGuard.Report("ArmyComposition.Describe", e, null);
                return "-";
            }
        }

        /// <summary>Main thread (logs): Describe of the cached snapshot only, "-" before the first.</summary>
        internal static string DescribePeek(Team team)
        {
            Snapshot s;
            return TryPeek(team, out s) ? Describe(s) : "-";
        }

        /// <summary>Largest change of any share or k between two snapshots (NaN when either is null).</summary>
        internal static float MaxChange(Snapshot a, Snapshot b)
        {
            if (a == null || b == null)
            {
                return float.NaN;
            }
            float d = Math.Abs(a.HorseArcherOfMounted - b.HorseArcherOfMounted);
            for (int i = 0; i < a.Shares.Length; i++)
            {
                d = Math.Max(d, Math.Abs(a.Shares[i] - b.Shares[i]));
            }
            d = Math.Max(d, Math.Abs(KFoot(a) - KFoot(b)));
            d = Math.Max(d, Math.Abs(KCav(a) - KCav(b)));
            d = Math.Max(d, Math.Abs(KWeak(a) - KWeak(b)));
            d = Math.Max(d, Math.Abs(KMount(a) - KMount(b)));
            d = Math.Max(d, Math.Abs(KHA(a) - KHA(b)));
            return d;
        }

        internal static string F2(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- event log (COMPEVENT)

        // COMPEVENT kinds: who does what / the army's state / a gate on a behavior / a tactic-level choice
        internal const string KindRole = "role";
        internal const string KindState = "state";
        internal const string KindGate = "gate";
        internal const string KindTactic = "tactic";

        internal sealed class LogEvent
        {
            public Team Team;
            public float Time;
            public string Kind;
            public string What;
            public string Formation;
            public string Text;
        }

        private sealed class NoteState
        {
            public string Text;
            public float Time = float.NegativeInfinity;
            public int Suppressed;
        }

        private const int MaxQueuedLines = 4000;
        internal const float NoteRepeatSeconds = 30f; // the same text for one key is written again only after this
        internal const float NoteMinGap = 0.5f; // a different text this soon after the last for one key is counted, not written

        private static readonly ConcurrentQueue<LogEvent> LogEvents = new ConcurrentQueue<LogEvent>();
        private static readonly ConcurrentDictionary<string, NoteState> NoteStates = new ConcurrentDictionary<string, NoteState>();
        private static int _queued;

        /// <summary>Any thread: one COMPEVENT line (kind, what, formation, text) plus a DecisionTrace note; with behavior also the formation's BEHAVIOR line
        /// (behavior, what, text; not deduped: callers write on a change). Per team / kind / what / formation the same text is deduped for NoteRepeatSeconds
        /// and a burst within NoteMinGap is counted ("suppressed=N" on the next line). Only with the terrain log on.</summary>
        internal static void Note(Team team, Formation formation, string kind, string what, string text, string behavior = null)
        {
            if (team == null || text == null)
            {
                return;
            }
            Mission mission = Mission.Current;
            float now = (mission != null) ? mission.CurrentTime : 0f;
            if (behavior != null && formation != null)
            {
                // notes the decision trace too
                TerrainBattlePosition.Enqueue(team, formation, now, behavior, what, "-", text);
            }
            else if (formation != null && DecisionTrace.Active)
            {
                DecisionTrace.Note(formation, "Composition", what, text);
            }
            if (!RBMConfig.RBMConfig.battleTerrainLoggingEnabled)
            {
                return;
            }
            string name = (formation != null) ? TerrainCavalry.FormationName(formation) : "-";
            string key = team.TeamIndex.ToString(CultureInfo.InvariantCulture) + "|" + kind + "|" + what + "|" + name;
            NoteState state = NoteStates.GetOrAdd(key, k => new NoteState());
            int suppressed;
            lock (state)
            {
                if (state.Text == text && now - state.Time < NoteRepeatSeconds && now >= state.Time)
                {
                    return;
                }
                if (now - state.Time < NoteMinGap && now >= state.Time)
                {
                    state.Suppressed++;
                    return;
                }
                suppressed = state.Suppressed;
                state.Suppressed = 0;
                state.Text = text;
                state.Time = now;
            }
            if (Interlocked.Increment(ref _queued) > MaxQueuedLines)
            {
                Interlocked.Decrement(ref _queued);
                return;
            }
            LogEvents.Enqueue(new LogEvent
            {
                Team = team,
                Time = now,
                Kind = kind,
                What = what,
                Formation = name,
                Text = (suppressed > 0) ? text + " suppressed=" + suppressed.ToString(CultureInfo.InvariantCulture) : text
            });
        }

        /// <summary>Main thread (terrain log).</summary>
        internal static bool TryDequeue(out LogEvent e)
        {
            if (LogEvents.TryDequeue(out e))
            {
                Interlocked.Decrement(ref _queued);
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- tactic re-apply on a main change (T5)

        private sealed class SeenMain
        {
            public bool HasMain;
            public Formation Line;
            public float At = float.NegativeInfinity;
        }

        internal const float ReapplyMinSeconds = 5f;

        private static readonly ConcurrentDictionary<TacticComponent, SeenMain> ReapplySeen = new ConcurrentDictionary<TacticComponent, SeenMain>();

        private static readonly AccessTools.FieldRef<TacticComponent, bool> IsTacticReapplyNeededRef = TryReapplyNeededRef();

        private static AccessTools.FieldRef<TacticComponent, bool> TryReapplyNeededRef()
        {
            try
            {
                return AccessTools.FieldRefAccess<TacticComponent, bool>("IsTacticReapplyNeeded");
            }
            catch
            {
                return null;
            }
        }

        /// <summary>AI thread, before the team AI's TickOccasionally: when the plan's main appears or disappears, or its archer line holder changes, under the
        /// current tactic (any tactic: an attack tactic's archers only get their no-main behaviors on a re-apply, a defend tactic's Defend assigns the line),
        /// the tactic re-applies once (native IsTacticReapplyNeeded), at most every ReapplyMinSeconds.</summary>
        [HarmonyPatch(typeof(TeamAIComponent), nameof(TeamAIComponent.TickOccasionally))]
        internal static class MainChangeReapplyPatch
        {
            private static void Prefix(TeamAIComponent __instance, Team ___Team)
            {
                Mission mission = Mission.Current;
                if (___Team == null || mission == null || !mission.IsFieldBattle || IsTacticReapplyNeededRef == null)
                {
                    return;
                }
                TacticComponent tactic = TacticSelection.CurrentTacticRef(__instance);
                Snapshot plan = SafeFor(___Team);
                if (tactic == null || plan == null)
                {
                    return;
                }
                bool hasMain = plan.Main != null;
                Formation line = plan.LineHolder;
                float now = mission.CurrentTime;
                SeenMain seen;
                if (!ReapplySeen.TryGetValue(tactic, out seen))
                {
                    ReapplySeen[tactic] = new SeenMain { HasMain = hasMain, Line = line, At = now };
                    return;
                }
                if (seen.HasMain == hasMain && seen.Line == line)
                {
                    return;
                }
                if (now - seen.At < ReapplyMinSeconds)
                {
                    return;
                }
                Note(___Team, plan.Main ?? line, KindTactic, "reapply", tactic.GetType().Name + " main=" + (hasMain ? TerrainCavalry.FormationName(plan.Main) : "-")
                    + " line=" + TerrainCavalry.FormationName(seen.Line) + "->" + TerrainCavalry.FormationName(line) + " infShare=" + F2(plan.TeamInfantryShare));
                seen.HasMain = hasMain;
                seen.Line = line;
                seen.At = now;
                IsTacticReapplyNeededRef(tactic) = true;
            }
        }
    }
}
