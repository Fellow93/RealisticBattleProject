using System;
using System.Collections.Concurrent;
using System.Globalization;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// WEAR DOWN FIRST (k = kHA x kMount > 0): a cavalry-heavy, horse-archer-led army's MELEE cavalry waits while the horse archers work, then commits once any of
    ///  - enemyLoss: the enemy side lost at least Lerp(0, WearDownLossFull, k) of its weighted men since the first snapshot, arrivals added (Snapshot.StartEnemyStrength);
    ///  - footEngaged: an enemy formation significant to one of our foot formations is at least InMelee in melee within EngagedGap of it;
    ///  - haAmmo: our horse-archer formations' riders with ammunition (men-weighted RangedCavalryUnitRatio) under WearDownAmmoOpen, or none left;
    ///  - time: Lerp(0, WearDownTimeFull, k) s since first contact (one of ours InMelee in melee or a significant enemy within ContactGap of one);
    ///  - k fell to 0 after the team was held (also when our melee cavalry became a token force: nothing to hold).
    /// Latched open per team. k = 0 is never held. Tuning in ArmyComposition.
    /// THREADING: AI thread; cached formation data; per-team state under its own lock, re-checked at most once per CheckSeconds. Cleared in ArmyComposition.Clear.
    /// </summary>
    internal static class CavalryWearDown
    {
        internal const float ContactGap = 150f; // m edge gap: the time clock starts
        private const float CheckSeconds = 1f;

        private sealed class State
        {
            public bool Open;
            public bool WasHeld;
            public bool Held;
            public float JoinedAt = float.NaN;
            public float CheckedAt = float.NegativeInfinity;
            public string Text;
        }

        private static readonly ConcurrentDictionary<Team, State> States = new ConcurrentDictionary<Team, State>();

        internal static void Clear()
        {
            States.Clear();
        }

        /// <summary>AI thread: true while the team's melee cavalry waits; text = the hold's numbers, or the open reason once open (null when never held).</summary>
        internal static bool Holds(Team team, out string text)
        {
            text = null;
            Mission mission = Mission.Current;
            if (team == null || mission == null)
            {
                return false;
            }
            State state = States.GetOrAdd(team, t => new State());
            lock (state)
            {
                if (state.Open)
                {
                    text = state.Text;
                    return false;
                }
                float now = MBCommon.GetTotalMissionTime();
                if (now - state.CheckedAt < CheckSeconds && now >= state.CheckedAt)
                {
                    text = state.Text;
                    return state.Held;
                }
                state.CheckedAt = now;
                ArmyComposition.Snapshot snap = ArmyComposition.For(team);
                float kHA = ArmyComposition.KHA(snap);
                float kMount = ArmyComposition.KMount(snap);
                // only a cavalry-heavy, horse-archer-led army waits; both ramps scale the wait; token melee cavalry: nothing to hold
                bool realCavalry = snap != null && snap.OwnReal(ArmyComposition.Category.Cavalry);
                float k = realCavalry ? kHA * kMount : 0f;
                if (k <= 0f)
                {
                    if (state.WasHeld)
                    {
                        return OpenWith(team, state, !realCavalry ? "cavalryToken" : (kHA <= 0f) ? "kHA=0" : "kMount=0", out text);
                    }
                    state.Held = false;
                    state.Text = null;
                    return false;
                }
                // the clock starts at real contact, not at deployment (an all-mounted side reads "joined" from the start)
                if (float.IsNaN(state.JoinedAt) && InContact(mission, team))
                {
                    state.JoinedAt = now;
                }
                float lossNeed = ArmyComposition.Lerp(0f, ArmyComposition.WearDownLossFull, k);
                float loss = (snap.StartEnemyStrength > 0f) ? 1f - snap.EnemyStrength / snap.StartEnemyStrength : 0f;
                if (loss >= lossNeed)
                {
                    return OpenWith(team, state, "enemyLoss=" + ArmyComposition.F2(loss) + ">=" + ArmyComposition.F2(lossNeed), out text);
                }
                string engaged = FootEngaged(team);
                if (engaged != null)
                {
                    return OpenWith(team, state, "footEngaged " + engaged, out text);
                }
                float ammo;
                int haMen = HorseArcherAmmo(team, out ammo);
                if (haMen <= 0)
                {
                    return OpenWith(team, state, "noHorseArchers", out text);
                }
                if (ammo < ArmyComposition.WearDownAmmoOpen)
                {
                    return OpenWith(team, state, "haAmmo=" + ArmyComposition.F2(ammo), out text);
                }
                float timeNeed = ArmyComposition.Lerp(0f, ArmyComposition.WearDownTimeFull, k);
                float since = float.IsNaN(state.JoinedAt) ? 0f : now - state.JoinedAt;
                if (!float.IsNaN(state.JoinedAt) && since >= timeNeed)
                {
                    return OpenWith(team, state, "time=" + Fmt0(since) + "s>=" + Fmt0(timeNeed) + "s", out text);
                }
                state.WasHeld = true;
                state.Held = true;
                state.Text = "k=" + ArmyComposition.F2(k) + " kHA=" + ArmyComposition.F2(kHA) + " kMount=" + ArmyComposition.F2(kMount) + " loss=" + ArmyComposition.F2(loss) + "/" + ArmyComposition.F2(lossNeed)
                    + " haAmmo=" + ArmyComposition.F2(ammo) + " joined=" + (float.IsNaN(state.JoinedAt) ? "-" : Fmt0(since) + "/" + Fmt0(timeNeed) + "s");
                text = state.Text;
                return true;
            }
        }

        private static bool OpenWith(Team team, State state, string reason, out string text)
        {
            if (!state.WasHeld)
            {
                reason += " (never held)";
            }
            state.Open = true;
            state.Held = false;
            state.Text = reason;
            text = reason;
            return false;
        }

        /// <summary>Any of our formations at least InMelee in melee, or a significant enemy within ContactGap (edge gap, ScatterAware) of one.</summary>
        private static bool InContact(Mission mission, Team team)
        {
            MBList<Formation> own = team.FormationsIncludingEmpty;
            for (int i = 0; i < own.Count; i++)
            {
                Formation f = own[i];
                if (f == null || f.CountOfUnits <= 0)
                {
                    continue;
                }
                if (HoldLateCharge.MeleeShare(f) >= ArmyComposition.InMelee || RBMAI.Utilities.NearestSignificantEnemyGap(f) <= ContactGap)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>"fN:Class on fM:Class" for the first significant enemy formation in melee with one of our foot formations, else null.</summary>
        private static string FootEngaged(Team team)
        {
            Formation foot;
            Formation e = ArmyComposition.EnemyOnOurFoot(team, false, 0f, ArmyComposition.InMelee, false, out foot);
            return (e != null) ? TerrainCavalry.FormationName(e) + " on " + TerrainCavalry.FormationName(foot) : null;
        }

        /// <summary>Horse-archer men (troop type) in our horse-archer formations; ammo = their share still with ammunition (ArmyComposition.AmmoShare, men-weighted).</summary>
        private static int HorseArcherAmmo(Team team, out float ammo)
        {
            int men = 0;
            float armed = 0f;
            MBList<Formation> own = team.FormationsIncludingEmpty;
            for (int i = 0; i < own.Count; i++)
            {
                Formation f = own[i];
                if (f == null || f.CountOfUnits <= 0 || !ArmyComposition.IsHorseArchers(f))
                {
                    continue;
                }
                int shooters = ArmyComposition.MenOf(f).Shooters;
                men += shooters;
                armed += shooters * ArmyComposition.AmmoShare(f);
            }
            ammo = (men > 0) ? armed / men : 0f;
            return men;
        }

        private static string Fmt0(float value)
        {
            return ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// The one cavalry commit gate, read by RBMBehaviorCavalryCharge's weight and by the native BehaviorCharge weight postfix for the same formation:
    ///  - WearDown: CavalryWearDown holds the team's melee cavalry (troop type) - RBM's charge weighs WearDownHeldWeight, native's charge 0;
    ///  - StayHome: kWeak > 0 (our cavalry outmatched by the enemy's melee cavalry) - both x max(1 - kWeak, WeakWeightFloor);
    ///  - Commit: otherwise (factor 1).
    /// Exempt: player formations and the Embolon wedge (never gated), a formation in melee (no wear-down hold), enemy melee cavalry on our foot (no stay-home).
    /// AI thread, cached data only.
    /// </summary>
    internal static class CavalryCommit
    {
        internal const float WearDownHeldWeight = 0.01f; // tiny, not 0: any behavior the tactic gives wins, with none the formation guards
        internal const float WeakWeightFloor = 0.1f;

        internal enum Gate
        {
            Commit,
            WearDown,
            StayHome
        }

        internal struct Verdict
        {
            public Gate Gate;
            /// <summary>StayHome's weight factor; 1 otherwise.</summary>
            public float Factor;
            /// <summary>CavalryWearDown's numbers while held, its open reason once open, "selfMelee"; null when it never applied.</summary>
            public string WearText;
            public float KWeak;
            /// <summary>"home" / "engaged" while kWeak > 0, else null.</summary>
            public string WeakKind;
            public string WeakText;
        }

        internal static Verdict For(Formation formation)
        {
            Verdict v = new Verdict { Gate = Gate.Commit, Factor = 1f };
            if (formation?.Team == null || !formation.IsAIControlled || IsTacticWedge(formation))
            {
                return v;
            }
            Team team = formation.Team;
            if (ArmyComposition.CategoryOf(formation) == ArmyComposition.Category.Cavalry)
            {
                string text;
                bool held = CavalryWearDown.Holds(team, out text);
                if (held && HoldLateCharge.MeleeShare(formation) >= ArmyComposition.InMelee)
                {
                    held = false;
                    text = "selfMelee";
                }
                v.WearText = text;
                if (held)
                {
                    v.Gate = Gate.WearDown;
                    return v;
                }
            }
            v.KWeak = ArmyComposition.KWeak(team);
            if (v.KWeak > 0f)
            {
                Formation engaged = ArmyComposition.EnemyOnOurFoot(team, true, ArmyComposition.InMelee, 0f, true, out _);
                if (engaged != null)
                {
                    v.WeakKind = "engaged";
                    v.WeakText = "engaged " + TerrainCavalry.FormationName(engaged);
                }
                else
                {
                    v.Gate = Gate.StayHome;
                    v.Factor = MathF.Max(1f - v.KWeak, WeakWeightFloor);
                    v.WeakKind = "home";
                    v.WeakText = "home weight x" + ArmyComposition.F2(v.Factor);
                }
            }
            return v;
        }

        internal static bool IsTacticWedge(Formation formation)
        {
            TeamAIComponent ai = formation?.Team?.TeamAI;
            RBMTacticEmbolon embolon = (ai != null) ? TacticSelection.CurrentTacticRef(ai) as RBMTacticEmbolon : null;
            return embolon != null && embolon.Wedge == formation;
        }
    }
}
