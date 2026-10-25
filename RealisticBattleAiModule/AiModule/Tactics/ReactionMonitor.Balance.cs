using System.Globalization;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// ReactionMonitor's R6 (AI thread, every check): re-judge the tactic on a balance shift. Reference = the side-vs-side balance
    /// (cached FormationPower per class and men) taken when the current tactic is first seen (TacticSelection.CurrentTacticRef)
    /// and after every evaluation. A SHIFT (after BalanceMinTacticAge) is a power-ratio move by BalanceRatioShift (HUGE at
    /// BalanceHugeShift) or a decisive fall of a class the tactics lean on. Held BalanceConfirmChecks checks, it requests
    /// TacticSelection.RequestBalanceRepick (the next decision picks the highest weight without native's x1.5 bonus; never
    /// away from a fighting defensive line unless huge). Limits: BalanceSpacing, BalanceMaxSwitches, BalanceMaxEvaluations,
    /// BalanceRequestTimeout; a "blocked" request keeps the reference.
    /// </summary>
    internal sealed partial class ReactionMonitor
    {

        private const float BalanceRatioShift = 1.3f;

        private const float BalanceHugeShift = 2f;

        private const float BalanceClassMinShare = 0.12f; // of its side's power at the reference

        private const float BalanceEnemyMountedLeft = 0.3f;

        private const float BalanceEnemyRangedLeft = 0.3f;

        private const float BalanceOwnRangedLeft = 0.5f;

        private const float BalanceOwnMountedLeft = 0.5f;

        private const int BalanceConfirmChecks = 5;

        private const float BalanceMinTacticAge = 20f; // a new tactic gets to work first

        private const float BalanceSpacing = 45f;

        private const int BalanceMaxSwitches = 4;

        private const int BalanceMaxEvaluations = 8;

        private const float BalanceRequestTimeout = 15f;

        /// <summary>One side-vs-side balance (cached FormationPower per class, men). Immutable once read; AI thread.</summary>
        private sealed class Balance
        {
            public float OwnInfantry;
            public float OwnRanged;
            public float OwnCavalry;
            public float OwnHorseArchers;
            public float EnemyInfantry;
            public float EnemyRanged;
            public float EnemyCavalry;
            public float EnemyHorseArchers;
            public int OwnMen;
            public int EnemyMen;

            public float OwnTotal => OwnInfantry + OwnRanged + OwnCavalry + OwnHorseArchers;

            public float EnemyTotal => EnemyInfantry + EnemyRanged + EnemyCavalry + EnemyHorseArchers;

            public float OwnMounted => OwnCavalry + OwnHorseArchers;

            public float EnemyMounted => EnemyCavalry + EnemyHorseArchers;

            public float Ratio => (OwnTotal + 1f) / (EnemyTotal + 1f);
        }


        private Balance _balanceRef;
        private TacticComponent _balanceTactic;
        private float _balanceRefAt;
        private int _balanceChecks;
        private float _balanceLastRequest = float.NegativeInfinity;
        private int _balanceEvaluations;
        private int _balanceSwitches;
        private TacticSelection.BalanceRequest _balanceRequest;
        private bool _balanceCapLogged;

        /// <summary>Once per check.</summary>
        private void CheckBalance(Mission mission, float now)
        {
            TeamAIComponent ai = _team.TeamAI;
            if (ai == null || !TacticSelection.IsRegistered(_team))
            {
                return;
            }
            TacticComponent current = TacticSelection.CurrentTacticRef(ai);
            if (current == null)
            {
                return;
            }
            TacticSelection.BalanceRequest pending = _balanceRequest;
            if (pending != null)
            {
                if (!pending.Done)
                {
                    if (now - _balanceLastRequest < BalanceRequestTimeout)
                    {
                        return;
                    }
                    TacticSelection.CancelBalanceRepick(_team, pending);
                    LogBalance("timeout", "no decision took the request in " + Fmt0(now - _balanceLastRequest) + "s", now);
                }
                else if (pending.Switched)
                {
                    _balanceSwitches++;
                }
                _balanceRequest = null;
                // Blocked (a defensive line fighting): keep the old reference and judge again after BalanceSpacing; otherwise take a fresh reference.
                bool blocked = pending.Done && !pending.Switched && pending.Outcome != null && pending.Outcome.StartsWith("blocked");
                if (!blocked)
                {
                    _balanceRef = null;
                }
            }
            Balance b = ReadBalance(mission);
            if (_balanceRef == null || current != _balanceTactic)
            {
                _balanceRef = b;
                _balanceTactic = current;
                _balanceRefAt = now;
                _balanceChecks = 0;
                return;
            }
            if (now - _balanceRefAt < BalanceMinTacticAge)
            {
                return;
            }
            bool huge;
            string why = BalanceShift(_balanceRef, b, out huge);
            if (why == null)
            {
                _balanceChecks = 0;
                return;
            }
            if (++_balanceChecks < BalanceConfirmChecks)
            {
                return;
            }
            if (_balanceSwitches >= BalanceMaxSwitches || _balanceEvaluations >= BalanceMaxEvaluations)
            {
                if (!_balanceCapLogged)
                {
                    _balanceCapLogged = true;
                    LogBalance("cap", "switches=" + _balanceSwitches.ToString(CultureInfo.InvariantCulture) + " evaluations="
                        + _balanceEvaluations.ToString(CultureInfo.InvariantCulture) + " " + why, now);
                }
                return;
            }
            if (now - _balanceLastRequest < BalanceSpacing)
            {
                return;
            }
            _balanceChecks = 0;
            string text = why + (huge ? " huge" : "") + " " + DescribeBalance(_balanceRef, b) + " tactic=" + current.GetType().Name
                + " for=" + Fmt0(now - _balanceRefAt) + "s";
            TacticSelection.BalanceRequest request = new TacticSelection.BalanceRequest { Why = text, Huge = huge };
            if (!TacticSelection.RequestBalanceRepick(_team, request))
            {
                return;
            }
            _balanceRequest = request;
            _balanceLastRequest = now;
            _balanceEvaluations++;
            LogBalance("request", text, now);
        }

        /// <summary>Our whole side against the enemy's: cached FormationPower per class (horse archers, cavalry, foot ranged, infantry) and men.</summary>
        private Balance ReadBalance(Mission mission)
        {
            Balance b = new Balance();
            foreach (Team team in mission.Teams)
            {
                if (team == null)
                {
                    continue;
                }
                bool enemy = team.IsEnemyOf(_team);
                if (!enemy && team.Side != _team.Side)
                {
                    continue;
                }
                foreach (Formation f in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (f == null || f.CountOfUnits <= 0)
                    {
                        continue;
                    }
                    FormationQuerySystem q = f.QuerySystem;
                    float power = q.FormationPower;
                    if (float.IsNaN(power) || power < 0f)
                    {
                        power = 0f;
                    }
                    if (enemy)
                    {
                        b.EnemyMen += f.CountOfUnits;
                        if (q.IsRangedCavalryFormation)
                        {
                            b.EnemyHorseArchers += power;
                        }
                        else if (q.IsCavalryFormation)
                        {
                            b.EnemyCavalry += power;
                        }
                        else if (RBMAI.Utilities.IsRangedForArrangement(f))
                        {
                            b.EnemyRanged += power;
                        }
                        else
                        {
                            b.EnemyInfantry += power;
                        }
                    }
                    else
                    {
                        b.OwnMen += f.CountOfUnits;
                        if (q.IsRangedCavalryFormation)
                        {
                            b.OwnHorseArchers += power;
                        }
                        else if (q.IsCavalryFormation)
                        {
                            b.OwnCavalry += power;
                        }
                        else if (RBMAI.Utilities.IsRangedForArrangement(f))
                        {
                            b.OwnRanged += power;
                        }
                        else
                        {
                            b.OwnInfantry += power;
                        }
                    }
                }
            }
            return b;
        }

        /// <summary>The shift since the reference as "ratio 1.00->1.45(x1.45),enemyMounted 320->60", or null.</summary>
        private static string BalanceShift(Balance r, Balance b, out bool huge)
        {
            huge = false;
            string why = null;
            float rel = b.Ratio / r.Ratio;
            if (rel >= BalanceRatioShift || rel <= 1f / BalanceRatioShift)
            {
                why = "ratio " + Fmt2(r.Ratio) + "->" + Fmt2(b.Ratio) + "(x" + Fmt2(rel) + ")";
                huge = rel >= BalanceHugeShift || rel <= 1f / BalanceHugeShift;
            }
            why = ClassDrop(why, "enemyMounted", r.EnemyMounted, r.EnemyTotal, b.EnemyMounted, BalanceEnemyMountedLeft);
            why = ClassDrop(why, "enemyRanged", r.EnemyRanged, r.EnemyTotal, b.EnemyRanged, BalanceEnemyRangedLeft);
            why = ClassDrop(why, "ownRanged", r.OwnRanged, r.OwnTotal, b.OwnRanged, BalanceOwnRangedLeft);
            why = ClassDrop(why, "ownMounted", r.OwnMounted, r.OwnTotal, b.OwnMounted, BalanceOwnMountedLeft);
            return why;
        }

        private static string ClassDrop(string why, string name, float before, float sideTotal, float now, float left)
        {
            if (before <= 0f || before < BalanceClassMinShare * sideTotal || now > left * before)
            {
                return why;
            }
            string part = name + " " + Fmt0(before) + "->" + Fmt0(now);
            return (why == null) ? part : why + "," + part;
        }

        private static string DescribeBalance(Balance r, Balance b)
        {
            return "own=" + Fmt0(b.OwnTotal) + "(" + b.OwnMen.ToString(CultureInfo.InvariantCulture) + " men inf/rng/cav/ha "
                + Fmt0(b.OwnInfantry) + "/" + Fmt0(b.OwnRanged) + "/" + Fmt0(b.OwnCavalry) + "/" + Fmt0(b.OwnHorseArchers) + ")"
                + " enemy=" + Fmt0(b.EnemyTotal) + "(" + b.EnemyMen.ToString(CultureInfo.InvariantCulture) + " men "
                + Fmt0(b.EnemyInfantry) + "/" + Fmt0(b.EnemyRanged) + "/" + Fmt0(b.EnemyCavalry) + "/" + Fmt0(b.EnemyHorseArchers) + ")"
                + " ref own=" + Fmt0(r.OwnTotal) + "(" + r.OwnMen.ToString(CultureInfo.InvariantCulture) + ") enemy=" + Fmt0(r.EnemyTotal)
                + "(" + r.EnemyMen.ToString(CultureInfo.InvariantCulture) + ")";
        }

        /// <summary>One REACT line "R6 what id=N ... why=..." in the same columns as the other kinds.</summary>
        private void LogBalance(string what, string why, float now)
        {
            if (!Logging)
            {
                return;
            }
            int id = System.Threading.Interlocked.Increment(ref _nextId);
            EnqueueLine(_team, now, "R6\t" + what + "\tid=" + id.ToString(CultureInfo.InvariantCulture)
                + "\tvictim=-\tattacker=-\tresponder=-\tmode=-\ttta=-\tscore=-\twhy=" + why);
        }

        /// <summary>AI thread (TacticSelection's balance re-pick): why a holding line of the team is fighting (choke Line / hedgehog infantry in Contact/Charge, battle-position Line engaged, or main infantry in melee), as "choke f1 contact"; else null.</summary>
        internal static string HoldLineFighting(Team team)
        {
            if (team == null)
            {
                return null;
            }
            foreach (Formation f in team.FormationsIncludingSpecialAndEmpty)
            {
                if (f == null || f.CountOfUnits <= 0 || f.AI == null)
                {
                    continue;
                }
                BehaviorComponent active = f.AI.ActiveBehavior;
                RBMBehaviorHoldChoke choke = active as RBMBehaviorHoldChoke;
                if (choke != null && choke.CurrentRole == RBMBehaviorHoldChoke.Role.Line
                    && (choke.Charged || choke.FightState != HoldLateCharge.FightState.Holding))
                {
                    return "choke " + TerrainCavalry.FormationName(f) + " " + (choke.Charged ? "charge" : choke.FightState.ToString().ToLowerInvariant());
                }
                RBMBehaviorHedgehog hedgehog = active as RBMBehaviorHedgehog;
                if (hedgehog != null && hedgehog.CurrentRole == RBMBehaviorHedgehog.Role.Infantry
                    && (hedgehog.Engaged || hedgehog.FightState != HoldLateCharge.FightState.Holding))
                {
                    return "hedgehog " + TerrainCavalry.FormationName(f) + " " + (hedgehog.Engaged ? "charge" : hedgehog.FightState.ToString().ToLowerInvariant());
                }
                RBMBehaviorHoldPosition position = active as RBMBehaviorHoldPosition;
                if (position != null && position.CurrentRole == RBMBehaviorHoldPosition.Role.Line && position.Engaged)
                {
                    return "holdLine " + TerrainCavalry.FormationName(f) + " engaged";
                }
            }
            // The AI's own main line (aiOnly).
            Formation main = MainInfantry(team, aiOnly: true);
            if (main != null)
            {
                float melee = HoldLateCharge.MeleeShare(main);
                if (melee >= HoldLateCharge.ContactMeleeShare)
                {
                    return "main " + TerrainCavalry.FormationName(main) + " melee=" + Fmt2(melee);
                }
            }
            return null;
        }
    }
}
