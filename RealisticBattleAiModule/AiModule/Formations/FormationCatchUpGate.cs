using HarmonyLib;
using System.Reflection;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    // Native HumanAIComponent.ParallelUpdateFormationMovement clears ShouldCatchUpWithFormation for EVERY
    // man once the formation's DeviationOfPositionsExcludeFarAgents exceeds AverageMaxUnlimitedSpeed * 3
    // (~11 m). A 500-man line spawns at 16-22 m, so nobody paces: everyone runs at his own top speed, the
    // fast outrun the slow, and the deviation never drops back under the gate (logged 2026-09-09: 15 m and
    // catchUp=0 on all 500 for the whole approach). CachedMovementSpeed is only ever applied through that
    // flag, so FormationPaceFix is inert while the gate is tripped.
    //
    // The flag itself is written on the native worker thread, and RBM must not touch that path when a
    // MissionLibrary mod is loaded (see Frontline/FormationMovement.cs). So instead this MAIN-THREAD postfix
    // on Formation.CacheFormationIntegrityData caps the deviation native reads to just under its own gate,
    // for AI-controlled non-cavalry formations under a Move order. Native's per-agent test then runs as
    // designed: a man within 2x the threshold of his slot paces, a man farther away sprints to close.
    //
    // Other readers of the value: BehaviorRegroup's weight (slightly lower when capped -- acceptable, Advance
    // does the pacing now), CacheMovementSpeedOfUnits' 0.5x restriction-drop check (unaffected, cap is above
    // it), BehaviorAdvance's shield-wall re-form check (0.5x, unaffected), and Agent.SetFormationIntegrityData.
    [HarmonyPatch(typeof(Formation), "CacheFormationIntegrityData")]
    internal static class FormationCatchUpGate
    {
        private static readonly MethodInfo IntegritySetter =
            AccessTools.PropertySetter(typeof(Formation), "CachedFormationIntegrityData");

        private static readonly object[] _setterArgs = new object[1];

        // Native gate is 3.0x; sit a little under it so float noise cannot re-trip it.
        private const float GateFactor = 2.9f;

        internal static long CapCount;

        [HarmonyPostfix]
        private static void Postfix(Formation __instance)
        {
            if (IntegritySetter == null || __instance == null || !__instance.IsAIControlled)
            {
                return;
            }
            FormationQuerySystem qs = __instance.QuerySystem;
            if (qs == null || qs.IsCavalryFormation || qs.IsRangedCavalryFormation)
            {
                return;
            }
            if (__instance.GetReadonlyMovementOrderReference().OrderEnum != MovementOrder.MovementOrderEnum.Move)
            {
                return;
            }
            if (__instance.ArrangementOrder.OrderEnum == ArrangementOrder.ArrangementOrderEnum.Column)
            {
                return;
            }

            Formation.FormationIntegrityDataGroup data = __instance.CachedFormationIntegrityData;
            float cap = data.AverageMaxUnlimitedSpeedExcludeFarAgents * GateFactor;
            if (cap <= 0f || data.DeviationOfPositionsExcludeFarAgents <= cap)
            {
                return;
            }

            _setterArgs[0] = new Formation.FormationIntegrityDataGroup(
                data.AverageVelocityExcludeFarAgents,
                cap,
                MathF.Max(data.MaxDeviationOfPositionExcludeFarAgents, cap),
                data.AverageMaxUnlimitedSpeedExcludeFarAgents);
            IntegritySetter.Invoke(__instance, _setterArgs);
            CapCount++;
        }
    }
}
