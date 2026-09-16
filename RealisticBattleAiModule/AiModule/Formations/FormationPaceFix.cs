using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    // Native Formation.CacheMovementSpeedOfUnits averages GetMaximumForwardUnlimitedSpeed() (a
    // creation-time constant that is blind to encumbrance/wounds/terrain) and only applies the
    // arrangement's run restriction while the line is still tight
    // (DeviationOfPositionsExcludeFarAgents < avgSpeed * 0.5). The moment the line goes ragged the
    // restriction is DROPPED, so the formation frame speeds up exactly when it should slow down and
    // the stragglers can never close -- a runaway.
    //
    // This postfix recomputes the cached speed for AI-controlled, non-cavalry formations that are
    // currently running BehaviorAdvance:
    //   * per-unit effective speed = GetMaximumForwardUnlimitedSpeed() * AgentDrivenProperties.MaxSpeedMultiplier
    //   * take the 20th percentile (the slow tail, i.e. the men actually holding the line back),
    //     min'd with the mean as a safety net
    //   * multiply by the arrangement run restriction UNCONDITIONALLY
    //
    // Call site is main-thread only (Formation.TickOccasionally timer), so the shared scratch list is safe.
    [HarmonyPatch(typeof(Formation), "CacheMovementSpeedOfUnits")]
    internal static class FormationPaceFix
    {
        private static readonly MethodInfo CachedMovementSpeedSetter =
            AccessTools.PropertySetter(typeof(Formation), "CachedMovementSpeed");

        private static readonly List<float> _speedScratch = new List<float>(256);
        private static readonly object[] _setterArgs = new object[1];

        // The run restriction exists to let a line re-form; a line that IS formed does not need it. Native had
        // this backwards (restriction only while tight, dropped when ragged -> runaway). Here a tight line runs
        // at the slow tail's full speed and a ragged one is restricted until it closes up. Hysteresis on the
        // deviation so the pace does not flap at the boundary (logged tight lines sit at 0.05-0.5 m,
        // re-forming ones at 3-10 m).
        private const float TightDeviation = 1.5f;
        private const float RaggedDeviation = 3.5f;

        private static readonly Dictionary<Formation, bool> _tightState = new Dictionary<Formation, bool>();
        private static Mission _mission;

        private static bool IsTight(Formation formation)
        {
            if (_mission != Mission.Current)
            {
                _mission = Mission.Current;
                _tightState.Clear();
            }
            float deviation = formation.CachedFormationIntegrityData.DeviationOfPositionsExcludeFarAgents;
            bool tight;
            if (!_tightState.TryGetValue(formation, out tight))
            {
                tight = deviation < TightDeviation;
            }
            else if (tight)
            {
                tight = deviation < RaggedDeviation;
            }
            else
            {
                tight = deviation < TightDeviation;
            }
            _tightState[formation] = tight;
            return tight;
        }

        [HarmonyPostfix]
        private static void Postfix(Formation __instance)
        {
            if (CachedMovementSpeedSetter == null || __instance == null)
            {
                return;
            }
            if (!__instance.IsAIControlled)
            {
                return;
            }
            FormationQuerySystem qs = __instance.QuerySystem;
            if (qs == null || qs.IsCavalryFormation || qs.IsRangedCavalryFormation)
            {
                return;
            }
            // Regroup is deliberately NOT paced: regrouping men run at full speed (AgentSpeedAndParams).
            BehaviorComponent activeBehavior = __instance.AI?.ActiveBehavior;
            if (activeBehavior == null || !(activeBehavior is BehaviorAdvance))
            {
                return;
            }

            // Native prefers walk restriction when present; leave that branch alone.
            __instance.ArrangementOrder.GetMovementSpeedRestriction(out var runRestriction, out var walkRestriction);
            if (walkRestriction.HasValue)
            {
                return;
            }
            float restriction = runRestriction ?? 1f;
            if (IsTight(__instance))
            {
                // Formed line: Line 0.8 / Loose 0.9 / Circle 0.5 -> 1.0, ShieldWall & Square 0.3 -> 0.6.
                float relaxed = restriction * 2f;
                restriction = (relaxed > 1f) ? 1f : relaxed;
            }

            _speedScratch.Clear();
            float sum = 0f;
            foreach (Agent agent in __instance.GetUnitsWithoutDetachedOnes())
            {
                if (agent == null || !agent.IsActive())
                {
                    continue;
                }
                float baseSpeed = agent.GetMaximumForwardUnlimitedSpeed();
                AgentDrivenProperties props = agent.AgentDrivenProperties;
                float mult = (props != null) ? props.MaxSpeedMultiplier : 1f;
                if (mult <= 0f)
                {
                    mult = 1f;
                }
                float eff = baseSpeed * mult;
                if (eff <= 0f)
                {
                    continue;
                }
                _speedScratch.Add(eff);
                sum += eff;
            }

            int count = _speedScratch.Count;
            if (count == 0)
            {
                return;
            }

            _speedScratch.Sort();
            int idx = (int)(count * 0.2f);
            if (idx >= count)
            {
                idx = count - 1;
            }
            float quantile = _speedScratch[idx];
            float mean = sum / count;
            float chosen = (quantile < mean) ? quantile : mean;

            float result = chosen * restriction;
            if (result < 0.1f)
            {
                result = 0.1f;
            }

            _setterArgs[0] = result;
            CachedMovementSpeedSetter.Invoke(__instance, _setterArgs);
        }
    }
}
