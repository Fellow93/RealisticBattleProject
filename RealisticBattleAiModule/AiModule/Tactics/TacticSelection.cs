using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// How a field-battle team picks its tactic. Every tactic is registered for every culture (Tactics.EarlyStartPatch); its own
    /// GetTacticWeight grades composition fit, and a Priority.Last postfix multiplies by culture x commander x jitter (fixed at
    /// registration) and, with terrainTacticsEnabled, the ground's factor (TerrainTacticFactors; 1 before the publish).
    /// The postfix runs on the async AI thread: it only reads the dictionary and the volatile immutable terrain factors, no
    /// allocation, no engine call. Instances not registered here (sieges, naval, teams RBM did not set up) are left alone.
    /// Dictionaries are cleared in MissionStartReset.
    /// </summary>
    public static class TacticSelection
    {

        public const float SignatureCultureBonus = 1.5f;
        public const float OffCultureFactor = 0.8f; // tactic with signature cultures, side is none of them
        public const float ValorStep = 0.1f; // per Valor level (-2..2): aggressive x(1 + step*v), defensive x(1 - step*v)
        public const float CalculatingStep = 0.1f; // per Calculating level: maneuver x(1 + step*c)
        public const float SkillFactorBase = 0.85f; // maneuver tactics at Tactics skill 0
        public const float SkillFactorRange = 0.3f; // added at SkillFactorCap and above
        public const float SkillFactorCap = 250f; // Tactics skill
        public const float JitterMin = 0.9f; // per-battle random factor per option, uniform
        public const float JitterMax = 1.1f;


        public const float UnfitWeight = 0.05f; // RBM tactic the army is not fit for at all
        public const float SplitInfantryMinRatio = 0.35f; // infantry share of the army
        public const float SplitInfantryMinCount = 45f;
        public const float SplitInfantrySuperiorityStart = 0.9f; // our infantry / enemy infantry where the weight starts to rise
        public const float SplitInfantrySuperioritySpan = 0.6f; // span to full
        public const float SplitArchersMinRatio = 0.12f; // ranged share
        public const float SplitArchersMinCount = 30f;
        public const float SplitArchersFullRatio = 0.35f;

        public const float RemnantShare = 0.5f; // cavalry-remnant rout: power ratio down to this share of the start ratio

        [Flags]
        private enum Style
        {
            None = 0,
            Aggressive = 1,
            Maneuver = 2,
            Defensive = 4
        }

        private sealed class Profile
        {
            public readonly string[] Cultures;
            public readonly Style Style;

            public Profile(Style style, params string[] cultures)
            {
                Style = style;
                Cultures = cultures;
            }
        }

        /// <summary>Read-only after the static constructor, so safe to read from any thread.</summary>
        private static readonly Dictionary<Type, Profile> Profiles = new Dictionary<Type, Profile>
        {
            { typeof(TacticFullScaleAttack), new Profile(Style.Aggressive) },
            { typeof(RBMTacticEmbolon), new Profile(Style.Aggressive | Style.Maneuver, "empire") },
            { typeof(RBMTacticAttackSplitSkirmishers), new Profile(Style.Maneuver, "aserai", "darshi") },
            { typeof(RBMTacticAttackSplitInfantry), new Profile(Style.Maneuver, "sturgia", "nord") },
            { typeof(RBMTacticAttackSplitArchers), new Profile(Style.Maneuver, "battania") },
            { typeof(TacticRangedHarrassmentOffensive), new Profile(Style.Maneuver, "khuzait") },
            { typeof(RBMTacticDefendSplitInfantry), new Profile(Style.Maneuver | Style.Defensive, "sturgia", "nord") },
            { typeof(RBMTacticDefendSplitArchers), new Profile(Style.Maneuver | Style.Defensive, "battania") },
            { typeof(RBMTacticHoldChoke), new Profile(Style.Defensive | Style.Maneuver, "sturgia", "nord") },
            { typeof(RBMTacticReservePlug), new Profile(Style.Defensive | Style.Maneuver, "empire") },
            { typeof(RBMTacticHedgehog), new Profile(Style.Defensive, "battania") },
            { typeof(TacticDefensiveEngagement), new Profile(Style.Defensive) },
            { typeof(TacticDefensiveLine), new Profile(Style.Defensive) },
        };

        /// <summary>One registered option's factors, fixed at registration.</summary>
        public sealed class Weighting
        {
            public float Culture;
            public float Valor;
            public float Calculating;
            public float Skill;
            public float Jitter;
            public float Total;
            public BattleSideEnum Side; // picks the terrain factors (TerrainTacticFactors.ForSide)
        }

        /// <summary>The side's commander as read at registration. Options is written and read on the main thread only.</summary>
        private sealed class TeamState
        {
            public string Culture;
            public string Commander;
            public bool HasCommander; // false (e.g. the player's side in a custom battle): Tactics skill is neutral, not 0
            public int Valor;
            public int Calculating;
            public int Skill;
            public volatile bool Routed; // set by the MakeDecision prefix (AI thread) the first time the cavalry-remnant rout fires
            public readonly List<KeyValuePair<TacticComponent, Weighting>> Options = new List<KeyValuePair<TacticComponent, Weighting>>();
        }

        /// <summary>Cavalry-remnant routs (team, mission time, remaining power), queued on the AI thread for the AI behavior log.</summary>
        private static readonly ConcurrentQueue<KeyValuePair<Team, Vec2>> RoutEvents = new ConcurrentQueue<KeyValuePair<Team, Vec2>>();

        private static readonly ConcurrentDictionary<TacticComponent, Weighting> Weights = new ConcurrentDictionary<TacticComponent, Weighting>();

        // The MakeDecision prefix acts only for these.
        private static readonly ConcurrentDictionary<Team, TeamState> Teams = new ConcurrentDictionary<Team, TeamState>();

        private static readonly AccessTools.FieldRef<TeamAIComponent, TacticComponent> CurrentTacticField =
            TryFieldRef<TeamAIComponent, TacticComponent>("_currentTactic");

        /// <summary>The team AI's current tactic (native's private field), null without one. Any thread.</summary>
        internal static TacticComponent CurrentTacticRef(TeamAIComponent ai)
        {
            return (ai != null && CurrentTacticField != null) ? CurrentTacticField(ai) : null;
        }

        /// <summary>Null when a game update renamed the field (FieldRefAccess would throw inside this static initializer and poison the class). Callers treat null as "no current tactic / no options".</summary>
        private static AccessTools.FieldRef<T, F> TryFieldRef<T, F>(string field) where T : class
        {
            try
            {
                return AccessTools.FieldRefAccess<T, F>(field);
            }
            catch
            {
                return null;
            }
        }


        public const float TerrainRepickMargin = 1.1f; // best weight ÷ current's (native's x1.5 current-tactic bonus is skipped once)

        /// <summary>Set on the main thread at the terrain publish, taken by the MakeDecision prefix.</summary>
        private static readonly ConcurrentDictionary<Team, byte> TerrainRepick = new ConcurrentDictionary<Team, byte>();

        /// <summary>Queued on the AI thread, drained on the main thread for the terrain log.</summary>
        private static readonly ConcurrentQueue<RepickEvent> RepickEvents = new ConcurrentQueue<RepickEvent>();

        private static readonly AccessTools.FieldRef<TeamAIComponent, List<TacticComponent>> AvailableTacticsField =
            TryFieldRef<TeamAIComponent, List<TacticComponent>>("_availableTactics");

        /// <summary>Null when the field cannot be read.</summary>
        private static List<TacticComponent> AvailableTacticsRef(TeamAIComponent ai)
        {
            return (ai != null && AvailableTacticsField != null) ? AvailableTacticsField(ai) : null;
        }

        /// <summary>protected internal virtual; Invoke dispatches to the override and through RBM's weight patches.</summary>
        private static readonly MethodInfo GetTacticWeightMethod = AccessTools.Method(typeof(TacticComponent), "GetTacticWeight");

        /// <summary>The private setter native's own switch goes through: OnCancel on the old tactic, OnApply and a tick on the new.</summary>
        private static readonly MethodInfo CurrentTacticSetter = AccessTools.PropertySetter(typeof(TeamAIComponent), "CurrentTactic");

        internal sealed class RepickEvent
        {
            public Team Team;
            public float Time;
            public string From;
            public string To;
            public float FromWeight;
            public float ToWeight;
            public bool Switched;
            public bool FlankReapply; // kept, but the flank plan changed a side choice: re-applied
            public string Reason; // null = the terrain re-pick; "balance switched|kept|blocked(...) ..." = R6
        }


        public const float BalanceRepickMargin = 1.1f; // best weight ÷ current's

        /// <summary>R6 request. Written by the monitor and the MakeDecision prefix, both in the team's AI tick: no lock.</summary>
        internal sealed class BalanceRequest
        {
            public string Why;
            public bool Huge; // may switch away from a defensive hold whose line is fighting
            public volatile bool Done;
            public bool Switched;
            public string Outcome;
        }

        // Set by the monitor, taken by the MakeDecision prefix (both AI thread).
        private static readonly ConcurrentDictionary<Team, BalanceRequest> BalanceRepick = new ConcurrentDictionary<Team, BalanceRequest>();


        // Set by any AI-thread caller, taken by the MakeDecision prefix.
        private static readonly ConcurrentDictionary<Team, string> ReasonRepick = new ConcurrentDictionary<Team, string>();

        /// <summary>AI thread: the team's next decision re-judges its tactic at once (ReasonRepickOnce: best option without the x1.5 bonus, switched at BalanceRepickMargin, TacticDwell restarted), logged REPICK reason=... False for a team RBM did not set up.</summary>
        internal static bool RequestRepick(Team team, string reason)
        {
            if (!IsRegistered(team))
            {
                return false;
            }
            ReasonRepick[team] = reason ?? "-";
            return true;
        }

        /// <summary>The team was set up by RBM (its tactic decisions run through MakeDecisionPatch). Any thread.</summary>
        internal static bool IsRegistered(Team team)
        {
            return team != null && Teams.ContainsKey(team);
        }

        /// <summary>AI thread (R6): next decision runs BalanceRepickOnce. False for a team RBM did not set up.</summary>
        internal static bool RequestBalanceRepick(Team team, BalanceRequest request)
        {
            if (request == null || !IsRegistered(team))
            {
                return false;
            }
            BalanceRepick[team] = request;
            return true;
        }

        /// <summary>AI thread (R6): drops the request when it is still this one.</summary>
        internal static void CancelBalanceRepick(Team team, BalanceRequest request)
        {
            if (team != null && request != null)
            {
                ((ICollection<KeyValuePair<Team, BalanceRequest>>)BalanceRepick).Remove(new KeyValuePair<Team, BalanceRequest>(team, request));
            }
        }

        internal static void ClearBalanceRepicks()
        {
            BalanceRepick.Clear();
        }

        public static void Clear()
        {
            Weights.Clear();
            Teams.Clear();
            TerrainRepick.Clear();
            BalanceRepick.Clear();
            ReasonRepick.Clear();
            KeyValuePair<Team, Vec2> ignored;
            while (RoutEvents.TryDequeue(out ignored))
            {
            }
            RepickEvent ignoredRepick;
            while (RepickEvents.TryDequeue(out ignoredRepick))
            {
            }
        }

        /// <summary>Main thread, once per battle at the terrain publish. Returns how many teams were marked.</summary>
        public static int RequestTerrainRepick(Mission mission)
        {
            int marked = 0;
            if (mission == null)
            {
                return 0;
            }
            foreach (Team team in mission.Teams)
            {
                // A side without terrain plans (ArmyScale) has no terrain re-pick.
                if (team != null && team.HasTeamAi && Teams.ContainsKey(team) && !ArmyScale.NoPlans(team))
                {
                    TerrainRepick[team] = 1;
                    marked++;
                }
            }
            return marked;
        }

        /// <summary>Main thread (terrain log).</summary>
        internal static bool TryDequeueRepick(out RepickEvent e)
        {
            return RepickEvents.TryDequeue(out e);
        }

        /// <summary>Main thread (AI behavior log). x = mission time, y = remaining power ratio.</summary>
        public static bool TryDequeueRout(out Team team, out float time, out float remainingPower)
        {
            KeyValuePair<Team, Vec2> e;
            if (RoutEvents.TryDequeue(out e))
            {
                team = e.Key;
                time = e.Value.x;
                remainingPower = e.Value.y;
                return true;
            }
            team = null;
            time = 0f;
            remainingPower = 0f;
            return false;
        }

        /// <summary>Composition inputs behind the RBM tactics' weights, for the AI behavior log. Main thread.</summary>
        public static string DescribeComposition(Team team)
        {
            TeamQuerySystem q = team?.QuerySystem;
            if (q == null)
            {
                return "-";
            }
            StringBuilder sb = new StringBuilder();
            float ourInfantry = q.InfantryRatio * q.MemberCount;
            float enemyInfantry = q.EnemyInfantryRatio * q.EnemyUnitCount;
            sb.Append("men=").Append(q.MemberCount).Append('/').Append(q.EnemyUnitCount);
            sb.Append(" inf=").Append(AiBehaviorLog.Fmt(q.InfantryRatio)).Append('(').Append((int)ourInfantry)
              .Append(" vs ").Append((int)enemyInfantry).Append(')');
            float t;
            sb.Append(" splitInf=").Append(TryGetInfantrySuperiority(team, out t) ? AiBehaviorLog.Fmt(t) : "unfit");
            sb.Append(" rng=").Append(AiBehaviorLog.Fmt(q.RangedRatio)).Append('(').Append((int)(q.RangedRatio * q.MemberCount)).Append(')');
            float fit;
            sb.Append(" splitArch=").Append(TryGetArcherFit(team, out fit) ? AiBehaviorLog.Fmt(fit) : "unfit");
            sb.Append(" cav=").Append(AiBehaviorLog.Fmt(q.CavalryRatio));
            sb.Append(" ha=").Append(AiBehaviorLog.Fmt(q.RangedCavalryRatio));
            TeamState state;
            if (Teams.TryGetValue(team, out state))
            {
                foreach (KeyValuePair<TacticComponent, Weighting> option in state.Options)
                {
                    RBMTacticEmbolon embolon = option.Key as RBMTacticEmbolon;
                    if (embolon != null)
                    {
                        sb.Append(" wedgeRiders=").Append((int)embolon.LastCavCount)
                          .Append(" barded=").Append(AiBehaviorLog.Fmt(embolon.LastHeavyShare));
                    }
                }
            }
            sb.Append(" rem=").Append(AiBehaviorLog.Fmt(q.RemainingPowerRatio));
            ArmyScale.SideScale scale = ArmyScale.For(team.Side);
            sb.Append(" scale=").Append((scale != null) ? scale.Reason : "unread");
            if (team.TeamAI != null && team.Side == BattleSideEnum.Defender)
            {
                sb.Append(" defApplicable=").Append(team.TeamAI.IsDefenseApplicable ? 1 : 0);
            }
            return sb.ToString();
        }

        /// <summary>Main thread (MissionCombatantsLogic.EarlyStart), so MBRandom is fine here.</summary>
        public static void Register(Team team, TacticComponent tactic, IBattleCombatant leader)
        {
            if (team == null || tactic == null)
            {
                return;
            }
            TeamState state = Teams.GetOrAdd(team, t => ReadCommander(leader));

            Profile profile;
            Profiles.TryGetValue(tactic.GetType(), out profile);
            Style style = (profile != null) ? profile.Style : Style.None;

            Weighting w = new Weighting();
            w.Culture = 1f;
            if (profile != null && profile.Cultures.Length > 0)
            {
                w.Culture = (state.Culture != null && Array.IndexOf(profile.Cultures, state.Culture) >= 0) ? SignatureCultureBonus : OffCultureFactor;
            }
            w.Valor = 1f;
            if ((style & Style.Aggressive) != 0)
            {
                w.Valor *= 1f + ValorStep * state.Valor;
            }
            if ((style & Style.Defensive) != 0)
            {
                w.Valor *= 1f - ValorStep * state.Valor;
            }
            w.Calculating = 1f;
            w.Skill = 1f;
            if ((style & Style.Maneuver) != 0)
            {
                w.Calculating = 1f + CalculatingStep * state.Calculating;
                w.Skill = state.HasCommander ? SkillFactorBase + SkillFactorRange * MBMath.ClampFloat(state.Skill / SkillFactorCap, 0f, 1f) : 1f;
            }
            w.Jitter = MBRandom.RandomFloatRanged(JitterMin, JitterMax);
            w.Total = w.Culture * w.Valor * w.Calculating * w.Skill * w.Jitter;
            w.Side = team.Side;

            Weights[tactic] = w;
            state.Options.Add(new KeyValuePair<TacticComponent, Weighting>(tactic, w));
        }

        private static TeamState ReadCommander(IBattleCombatant leader)
        {
            TeamState state = new TeamState();
            state.Culture = leader?.BasicCulture?.StringId;
            BasicCharacterObject general = leader?.General;
            state.Commander = general?.StringId;
            state.HasCommander = general != null;
            try
            {
                // Traits exist only for campaign heroes; custom battle generals are plain BasicCharacterObjects.
                if (general is CharacterObject co && co.HeroObject != null)
                {
                    state.Valor = MBMath.ClampInt(co.HeroObject.GetTraitLevel(DefaultTraits.Valor), -2, 2);
                    state.Calculating = MBMath.ClampInt(co.HeroObject.GetTraitLevel(DefaultTraits.Calculating), -2, 2);
                }
            }
            catch
            {
                state.Valor = 0;
                state.Calculating = 0;
            }
            try
            {
                state.Skill = (general != null) ? general.GetSkillValue(DefaultSkills.Tactics) : 0;
            }
            catch
            {
                state.Skill = 0;
            }
            return state;
        }

        public static bool TryGetMultiplier(TacticComponent tactic, out float multiplier)
        {
            Weighting w;
            if (tactic != null && Weights.TryGetValue(tactic, out w))
            {
                multiplier = w.Total;
                return true;
            }
            multiplier = 1f;
            return false;
        }

        /// <summary>The terrain factor applied to this option's weight (TerrainTacticFactors' total for its type and side); 1 when off, before the publish, or unregistered. Any thread: one dictionary read, one volatile read, an immutable object.</summary>
        public static float TerrainFactorFor(TacticComponent tactic)
        {
            Weighting w;
            if (tactic == null || !Weights.TryGetValue(tactic, out w))
            {
                return 1f;
            }
            return TerrainFactorFor(tactic, w);
        }

        private static float TerrainFactorFor(TacticComponent tactic, Weighting w)
        {
            // No terrain factor for a side without terrain plans (ArmyScale.NoPlans).
            if (!RBMConfig.RBMConfig.terrainTacticsEnabled || ArmyScale.NoPlans(w.Side))
            {
                return 1f;
            }
            TerrainTacticFactors.SideFactors factors = TerrainTacticFactors.ForSide(w.Side);
            return (factors != null) ? factors.TotalFor(tactic.GetType()) : 1f;
        }

        /// <summary>One line per team for the AI behavior log: the commander and each option's factors. Main thread.</summary>
        public static string DescribeTeam(Team team)
        {
            TeamState state;
            if (team == null || !Teams.TryGetValue(team, out state))
            {
                return null;
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("culture=").Append(state.Culture ?? "-");
            sb.Append("\tcommander=").Append(state.Commander ?? "-");
            sb.Append("\tvalor=").Append(state.Valor);
            sb.Append("\tcalculating=").Append(state.Calculating);
            sb.Append("\ttactics=").Append(state.Skill);
            sb.Append("\toptions=");
            for (int i = 0; i < state.Options.Count; i++)
            {
                Weighting w = state.Options[i].Value;
                if (i > 0)
                {
                    sb.Append(' ');
                }
                sb.Append(state.Options[i].Key.GetType().Name)
                  .Append(":c").Append(AiBehaviorLog.Fmt(w.Culture))
                  .Append("/v").Append(AiBehaviorLog.Fmt(w.Valor))
                  .Append("/k").Append(AiBehaviorLog.Fmt(w.Calculating))
                  .Append("/s").Append(AiBehaviorLog.Fmt(w.Skill))
                  .Append("/j").Append(AiBehaviorLog.Fmt(w.Jitter))
                  .Append("=x").Append(AiBehaviorLog.Fmt(w.Total));
            }
            return sb.ToString();
        }


        /// <summary>
        /// False when the army has too little infantry to split. Otherwise t in [0, 1]: how far our infantry outnumbers
        /// the enemy's (0 at SplitInfantrySuperiorityStart, 1 at Start + Span).
        /// </summary>
        public static bool TryGetInfantrySuperiority(Team team, out float t)
        {
            t = 0f;
            TeamQuerySystem q = team?.QuerySystem;
            if (q == null)
            {
                return false;
            }
            float ourInfantry = q.InfantryRatio * q.MemberCount;
            if (q.InfantryRatio < SplitInfantryMinRatio || ourInfantry < SplitInfantryMinCount)
            {
                return false;
            }
            float enemyInfantry = q.EnemyInfantryRatio * q.EnemyUnitCount;
            // No enemy infantry reading counts as parity: the first decisions see the enemy's cached infantry share still at 0.
            float superiority = (enemyInfantry > 0f) ? ourInfantry / enemyInfantry : 1f;
            t = MBMath.ClampFloat((superiority - SplitInfantrySuperiorityStart) / SplitInfantrySuperioritySpan, 0f, 1f);
            return true;
        }

        /// <summary>False when the army has too few archers to split. Otherwise fit in [0, 1], full at SplitArchersFullRatio.</summary>
        public static bool TryGetArcherFit(Team team, out float fit)
        {
            fit = 0f;
            TeamQuerySystem q = team?.QuerySystem;
            if (q == null)
            {
                return false;
            }
            if (q.RangedRatio < SplitArchersMinRatio || q.RangedRatio * q.MemberCount < SplitArchersMinCount)
            {
                return false;
            }
            fit = MBMath.ClampFloat(q.RangedRatio / SplitArchersFullRatio, 0f, 1f);
            return true;
        }


        /// <summary>Applies the registration multiplier, then the terrain factor. Priority.Last, after RBM's own GetTacticWeight postfixes. AI thread: dictionary and volatile reads of immutable data; no allocation, no engine call.</summary>
        [HarmonyPatch]
        private static class TacticWeightPatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (Type type in Profiles.Keys)
                {
                    MethodInfo method = AccessTools.Method(type, "GetTacticWeight");
                    if (method != null)
                    {
                        yield return method;
                    }
                }
            }

            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(TacticComponent __instance, ref float __result)
            {
                Weighting w;
                if (__instance == null || !Weights.TryGetValue(__instance, out w))
                {
                    return;
                }
                if (float.IsNaN(__result))
                {
                    __result = 0f;
                }
                __result *= w.Total * TerrainFactorFor(__instance, w);
            }
        }

        /// <summary>Once per team per tactic decision (every 5 s, async AI thread), RBM-registered teams only: the cavalry-remnant rout, the one terrain re-pick (TerrainRepickOnce), a requested balance / reason re-pick (not in the same decision as the terrain one), and following a newer flank plan (TerrainFlankPlan.FollowPlan).</summary>
        [HarmonyPatch(typeof(TeamAIComponent), "MakeDecision")]
        private static class MakeDecisionPatch
        {
            [HarmonyPrefix]
            private static void Prefix(TeamAIComponent __instance, Team ___Team)
            {
                TeamState state;
                if (___Team == null || !Teams.TryGetValue(___Team, out state))
                {
                    return;
                }
                RoutCavalryRemnant(___Team, state);
                byte ignored;
                BalanceRequest balance;
                string reason;
                if (TerrainRepick.TryRemove(___Team, out ignored))
                {
                    TerrainRepickOnce(__instance, ___Team);
                }
                else if (ReasonRepick.TryRemove(___Team, out reason))
                {
                    ReasonRepickOnce(__instance, ___Team, reason);
                }
                else if (BalanceRepick.TryRemove(___Team, out balance))
                {
                    BalanceRepickOnce(__instance, ___Team, balance);
                }
                // A newer flank plan version re-applies the current tactic when it changes one of its side choices.
                TerrainFlankPlan.FollowPlan(CurrentTacticRef(__instance), ___Team);
            }

            /// <summary>The first decision after the ground is read: the highest-weight option (terrain included, no current-tactic bonus) replaces the current tactic when it beats it by TerrainRepickMargin, through native's private setter, and TacticDwell restarts. Otherwise the tactic is kept and re-applied only when the flank plan changes one of its side choices. AI thread, once per team per battle.</summary>
            private static void TerrainRepickOnce(TeamAIComponent ai, Team team)
            {
                RepickResult r;
                if (!RepickOnce(ai, team, TerrainRepickMargin, false, out r))
                {
                    return;
                }
                // Kept: its side choices predate the ground read; re-applied once (IsTacticReapplyNeeded) only when the flank plan changes one of them.
                bool flankReapply = !r.Switched && TerrainFlankPlan.RequestReapplyIfChanged(r.Current, team);
                EnqueueRepick(team, r, flankReapply, null);
            }

            private struct RepickResult
            {
                public Mission Mission;
                public TacticComponent Current;
                public TacticComponent Best;
                public float BestWeight;
                public float CurrentWeight;
                public bool Switched;
                public string Blocked; // HoldLineFighting's reason when holdLineBlocks kept a better option out
            }

            /// <summary>The re-picks' shared core: the highest-weight option (EvaluateOptions) replaces the current tactic when it beats it by margin, through native's private setter, and TacticDwell restarts. holdLineBlocks: not away from a DEFENSIVE tactic while its line fights (ReactionMonitor.HoldLineFighting). False (nothing done) without a mission, tactic, options or the reflection. AI thread.</summary>
            private static bool RepickOnce(TeamAIComponent ai, Team team, float margin, bool holdLineBlocks, out RepickResult result)
            {
                result = default(RepickResult);
                Mission mission = Mission.Current;
                TacticComponent current = CurrentTacticRef(ai);
                List<TacticComponent> options = AvailableTacticsRef(ai);
                if (mission == null || current == null || options == null || options.Count == 0
                    || GetTacticWeightMethod == null || CurrentTacticSetter == null)
                {
                    return false;
                }
                TacticComponent best;
                float bestWeight;
                float currentWeight;
                EvaluateOptions(options, current, out best, out bestWeight, out currentWeight);
                bool better = best != null && best != current && bestWeight > 0f && bestWeight >= currentWeight * margin;
                string blocked = null;
                if (better && holdLineBlocks && IsDefensive(current))
                {
                    blocked = ReactionMonitor.HoldLineFighting(team);
                }
                bool switched = better && blocked == null;
                if (switched)
                {
                    CurrentTacticSetter.Invoke(ai, new object[] { best });
                    Tactics.TacticDwell.tacticSince[team] = mission.CurrentTime;
                }
                result.Mission = mission;
                result.Current = current;
                result.Best = best;
                result.BestWeight = bestWeight;
                result.CurrentWeight = currentWeight;
                result.Switched = switched;
                result.Blocked = blocked;
                return true;
            }

            /// <summary>The REPICK line's event; reason null = the terrain re-pick.</summary>
            private static void EnqueueRepick(Team team, RepickResult r, bool flankReapply, string reason)
            {
                RepickEvents.Enqueue(new RepickEvent
                {
                    Team = team,
                    Time = r.Mission.CurrentTime,
                    From = r.Current.GetType().Name,
                    To = (r.Best != null) ? r.Best.GetType().Name : "-",
                    FromWeight = r.CurrentWeight,
                    ToWeight = r.BestWeight,
                    Switched = r.Switched,
                    FlankReapply = flankReapply,
                    Reason = reason
                });
            }

            /// <summary>Every option's weight as native's MakeDecision reads it (through RBM's patches) WITHOUT the x1.5 current-tactic bonus; the best and the current one's. AI thread.</summary>
            private static void EvaluateOptions(List<TacticComponent> options, TacticComponent current, out TacticComponent best, out float bestWeight,
                out float currentWeight)
            {
                best = null;
                bestWeight = float.NegativeInfinity;
                currentWeight = 0f;
                foreach (TacticComponent option in options)
                {
                    float weight;
                    try
                    {
                        weight = (float)GetTacticWeightMethod.Invoke(option, null);
                    }
                    catch
                    {
                        continue;
                    }
                    if (float.IsNaN(weight))
                    {
                        weight = 0f;
                    }
                    if (option == current)
                    {
                        currentWeight = weight;
                    }
                    if (weight > bestWeight)
                    {
                        bestWeight = weight;
                        best = option;
                    }
                }
            }

            /// <summary>A balance re-pick (ReactionMonitor R6): the highest-weight option replaces the current tactic when it beats it by BalanceRepickMargin (the terrain re-pick's mechanism). Not away from a DEFENSIVE tactic while its line fights (ReactionMonitor.HoldLineFighting) unless the shift is huge. Each outcome goes back on the request and into a REPICK line. AI thread.</summary>
            private static void BalanceRepickOnce(TeamAIComponent ai, Team team, BalanceRequest request)
            {
                RepickResult r;
                if (!RepickOnce(ai, team, BalanceRepickMargin, !request.Huge, out r))
                {
                    request.Outcome = "noOptions";
                    request.Done = true;
                    return;
                }
                request.Switched = r.Switched;
                request.Outcome = r.Switched ? "switched" : (r.Blocked != null) ? "blocked(" + r.Blocked + ")" : "kept";
                request.Done = true;
                EnqueueRepick(team, r, false, "balance " + request.Outcome + " " + (request.Why ?? "-"));
            }

            /// <summary>A re-pick for a named reason (RequestRepick, e.g. the choke bypass): as the balance re-pick, one REPICK line "reason switched|kept". AI thread.</summary>
            private static void ReasonRepickOnce(TeamAIComponent ai, Team team, string reason)
            {
                RepickResult r;
                if (!RepickOnce(ai, team, BalanceRepickMargin, false, out r))
                {
                    return;
                }
                EnqueueRepick(team, r, false,
                    (reason.Split(' ')[0]) + " " + (r.Switched ? "switched" : "kept") + " " + (reason.IndexOf(' ') >= 0 ? reason.Substring(reason.IndexOf(' ') + 1) : "-"));
            }

            private static bool IsDefensive(TacticComponent tactic)
            {
                Profile profile;
                return tactic != null && Profiles.TryGetValue(tactic.GetType(), out profile) && (profile.Style & Style.Defensive) != 0;
            }

            private static void RoutCavalryRemnant(Team team, TeamState state)
            {
                Mission mission = Mission.Current;
                // RemainingPowerRatio is our power vs the ENEMY's, so a small band facing a big army would rout at t~0: a REMNANT also has to have lost ground since the start (ratio at most RemnantShare x TotalPowerRatio).
                float remaining = team.QuerySystem.RemainingPowerRatio;
                if (mission == null || remaining >= 0.1f || remaining > RemnantShare * team.QuerySystem.TotalPowerRatio)
                {
                    return;
                }
                BattleSideEnum side = team.Side;
                foreach (Team sideTeam in mission.Teams)
                {
                    if (sideTeam.Side != side)
                    {
                        continue;
                    }
                    foreach (Agent agent in sideTeam.ActiveAgents)
                    {
                        if (!agent.HasMount)
                        {
                            return;
                        }
                    }
                }
                foreach (Team sideTeam in mission.Teams)
                {
                    if (sideTeam.Side != side)
                    {
                        continue;
                    }
                    foreach (Agent agent in sideTeam.ActiveAgents)
                    {
                        agent.SetMorale(0f);
                    }
                }
                // Logged once per team (the rule re-fires every decision while riders remain).
                if (!state.Routed)
                {
                    state.Routed = true;
                    RoutEvents.Enqueue(new KeyValuePair<Team, Vec2>(team, new Vec2(mission.CurrentTime, team.QuerySystem.RemainingPowerRatio)));
                }
            }
        }
    }
}
