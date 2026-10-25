using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// Reads a team AI's active tactic and every option with its current GetTacticWeight (for the logs and developer views). The weight
    /// is invoked through reflection so it includes RBM's patches (TacticSelection multiplier, terrain factor): the final weight native
    /// ranks, before its x1.5 for the current tactic. Read only.
    ///
    /// MAIN THREAD ONLY: GetTacticWeight reads formations and agents the AI thread also writes.
    /// </summary>
    internal static class TacticWeightProbe
    {
        private static readonly FieldInfo _fCurrentTactic =
            AccessTools.Field(typeof(TeamAIComponent), "_currentTactic");

        private static readonly FieldInfo _fAvailableTactics =
            AccessTools.Field(typeof(TeamAIComponent), "_availableTactics");

        // Invoke dispatches to the override and through RBM's Harmony patches.
        private static readonly MethodInfo _mGetTacticWeight =
            AccessTools.Method(typeof(TacticComponent), "GetTacticWeight");

        // Null without a team AI, a tactic or the field (a game update).
        public static TacticComponent CurrentTactic(Team team)
        {
            if (team == null || team.TeamAI == null || _fCurrentTactic == null)
            {
                return null;
            }
            return _fCurrentTactic.GetValue(team.TeamAI) as TacticComponent;
        }

        // Weight is NaN if GetTacticWeight threw. Null without team AI / options / handles.
        public static List<KeyValuePair<TacticComponent, float>> Evaluate(Team team)
        {
            if (team == null || team.TeamAI == null || _fAvailableTactics == null || _mGetTacticWeight == null)
            {
                return null;
            }
            List<TacticComponent> options = _fAvailableTactics.GetValue(team.TeamAI) as List<TacticComponent>;
            if (options == null || options.Count == 0)
            {
                return null;
            }
            List<KeyValuePair<TacticComponent, float>> weights = new List<KeyValuePair<TacticComponent, float>>(options.Count);
            foreach (TacticComponent option in options)
            {
                if (option == null)
                {
                    continue;
                }
                float weight;
                try
                {
                    weight = (float)_mGetTacticWeight.Invoke(option, null);
                }
                catch
                {
                    weight = float.NaN;
                }
                weights.Add(new KeyValuePair<TacticComponent, float>(option, weight));
            }
            return weights;
        }

        /// <summary>NaN weights count 0, as TacticSelection's postfix makes them for the real choice.</summary>
        public static float SafeWeight(float weight)
        {
            return float.IsNaN(weight) ? 0f : weight;
        }

        public static string Name(TacticComponent tactic)
        {
            return (tactic != null) ? tactic.GetType().Name : "-";
        }

        /// <summary>Final weight split into army fit x TacticSelection multiplier x terrain.</summary>
        internal struct RankedTactic
        {
            public TacticComponent Tactic;
            public float Final; // NaN when GetTacticWeight threw; before native's x1.5 for the current tactic
            public bool Registered; // false: not registered by TacticSelection (Multiplier is then 1)
            public float Multiplier; // culture x commander x jitter
            public float Terrain; // the factor inside Final; 1 when not applied
            // Final / (Multiplier x Terrain); NaN when Final is NaN or the divisor ~0.
            public float Fit;
        }

        private static readonly System.Comparison<KeyValuePair<TacticComponent, float>> ByWeightDescending =
            (a, b) => SafeWeight(b.Value).CompareTo(SafeWeight(a.Value));

        // Best first. Null without options or on reflection failure. Main thread only.
        public static List<RankedTactic> Rank(Team team)
        {
            List<KeyValuePair<TacticComponent, float>> weights;
            try
            {
                weights = Evaluate(team);
            }
            catch
            {
                weights = null;
            }
            if (weights == null || weights.Count == 0)
            {
                return null;
            }
            weights.Sort(ByWeightDescending);
            List<RankedTactic> ranked = new List<RankedTactic>(weights.Count);
            foreach (KeyValuePair<TacticComponent, float> entry in weights)
            {
                RankedTactic row = new RankedTactic();
                row.Tactic = entry.Key;
                row.Final = entry.Value;
                row.Registered = TacticSelection.TryGetMultiplier(entry.Key, out row.Multiplier);
                row.Terrain = TacticSelection.TerrainFactorFor(entry.Key);
                float divisor = row.Multiplier * row.Terrain;
                row.Fit = (float.IsNaN(row.Final) || divisor <= 1e-6f) ? float.NaN : row.Final / divisor;
                ranked.Add(row);
            }
            return ranked;
        }

        public static string ShortName(TacticComponent tactic)
        {
            string name = Name(tactic);
            if (name.StartsWith("RBMTactic"))
            {
                return name.Substring("RBMTactic".Length);
            }
            if (name.StartsWith("Tactic"))
            {
                return name.Substring("Tactic".Length);
            }
            return name;
        }

        /// <summary>Two decimals, three below 0.1 so the near-zero weights still rank visibly.</summary>
        public static string FormatWeight(float value)
        {
            return value.ToString((value > -0.1f && value < 0.1f) ? "0.000" : "0.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        // First field of TacticSelection.DescribeTeam. Null while unregistered; "" when registered without a culture.
        public static string ReadCulture(Team team)
        {
            string described = TacticSelection.DescribeTeam(team);
            const string prefix = "culture=";
            if (described == null)
            {
                return null;
            }
            if (!described.StartsWith(prefix))
            {
                return "";
            }
            int end = described.IndexOf('\t');
            string culture = (end > prefix.Length) ? described.Substring(prefix.Length, end - prefix.Length) : described.Substring(prefix.Length);
            return (culture == "-") ? "" : culture;
        }
    }
}
