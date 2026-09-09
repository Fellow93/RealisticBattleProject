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
            // Regroup is the "close the line up" interlude Advance keeps flipping into; pace it too.
            BehaviorComponent activeBehavior = __instance.AI?.ActiveBehavior;
            if (activeBehavior == null || !(activeBehavior is BehaviorAdvance || activeBehavior is BehaviorRegroup))
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
