using System.Globalization;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// What a charging foot formation fights: its foot target, unless an enemy mounted formation is a much larger threat to it.
    /// Threat = FormationPower x class weight / (1 + edge gap / ThreatFalloff); riders neither in contact nor charging this block
    /// x PassingFactor (foot cannot catch them). A mounted formation takes over at SwitchRatio x the foot target's threat and keeps
    /// it down to KeepRatio. AI-thread safe: cached formation data and FormationGeometry only.
    /// </summary>
    internal static class ChargeTargetChoice
    {
        private const float ThreatFalloff = 40f; // m
        private const float EngagedGap = 15f; // m
        private const float PassingFactor = 0.4f;
        private const float HorseArcherWeight = 0.5f;
        private const float RangedFootWeight = 0.7f;
        internal const float SwitchRatio = 2f;
        internal const float KeepRatio = 1.25f;

        // Foot reaches riders in contact, charging it, or slow and near; riders charged now are kept to KeepCatchGap.
        internal const float KeepCatchGap = 30f; // m
        internal const float SlowRiderSpeed = 2f; // m/s, sampled (EnemyMotion)
        internal const float SlowRiderGap = 40f; // m

        /// <summary>Foot can reach the riders: edge gap within EngagedGap (KeepCatchGap for the current target), charging it, or slow and near.</summary>
        internal static bool Catchable(float gap, bool chargingUs, bool hasSpeed, float speed, bool current)
        {
            if (chargingUs || gap <= (current ? KeepCatchGap : EngagedGap))
            {
                return true;
            }
            return hasSpeed && speed < SlowRiderSpeed && gap <= SlowRiderGap;
        }

        internal static bool Catchable(Formation own, Formation riders, bool current)
        {
            float gap = FormationGeometry.Gap(own, FootprintMode.Current, riders, FootprintMode.ScatterAware);
            bool hasSpeed = EnemyMotion.TryVelocity(riders, out Vec2 velocity);
            return Catchable(gap, ChargingAt(riders, own), hasSpeed, hasSpeed ? velocity.Length : 0f, current);
        }

        internal static bool IsMounted(Formation formation)
        {
            FormationQuerySystem q = formation.QuerySystem;
            return q.IsCavalryFormation || q.IsRangedCavalryFormation;
        }

        internal static float Threat(Formation own, Formation enemy)
        {
            if (own == null || enemy == null || enemy.CountOfUnits <= 0)
            {
                return 0f;
            }
            float gap = FormationGeometry.Gap(own, FootprintMode.Current, enemy, FootprintMode.ScatterAware);
            if (gap == float.MaxValue)
            {
                return 0f;
            }
            FormationQuerySystem q = enemy.QuerySystem;
            float weight = 1f;
            if (q.IsRangedCavalryFormation)
            {
                weight = HorseArcherWeight;
            }
            else if (!q.IsCavalryFormation && q.IsRangedFormation)
            {
                weight = RangedFootWeight;
            }
            if ((q.IsCavalryFormation || q.IsRangedCavalryFormation) && gap > EngagedGap && !ChargingAt(enemy, own))
            {
                weight *= PassingFactor;
            }
            return q.FormationPower * weight / (1f + gap / ThreatFalloff);
        }

        private static bool ChargingAt(Formation enemy, Formation own)
        {
            return AiModule.RbmBehaviors.OverrideBehaviorCharge.ChargeTargetOf(enemy) == own;
        }

        /// <summary>Another mounted formation replaces the one charged now only when it is a clearly larger threat.</summary>
        internal static bool KeepsCurrentMounted(float currentThreat, float bestThreat)
        {
            return currentThreat > 0f && bestThreat < KeepRatio * currentThreat;
        }

        /// <summary>The most threatening significant enemy mounted formation, null with none; <paramref name="current"/> (if mounted) kept by KeepsCurrentMounted.</summary>
        internal static Formation MostThreateningMounted(Formation own, out float threat, Formation current = null)
        {
            threat = 0f;
            Mission mission = Mission.Current;
            if (own == null || own.Team == null || mission == null)
            {
                return null;
            }
            Formation best = null;
            float currentThreat = 0f;
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(own.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (enemy == null || enemy.CountOfUnits <= 0 || !IsMounted(enemy) || !ThreatSignificance.IsSignificant(own, enemy))
                    {
                        continue;
                    }
                    float t = Threat(own, enemy);
                    if (enemy == current)
                    {
                        currentThreat = t;
                    }
                    if (t > threat)
                    {
                        threat = t;
                        best = enemy;
                    }
                }
            }
            // two riding groups of near-equal threat swapped every tick (log 2026-10-30 07-13: the block marched back and forth)
            if (best != null && best != current && KeepsCurrentMounted(currentThreat, threat))
            {
                threat = currentThreat;
                return current;
            }
            return best;
        }

        /// <summary>
        /// Riders may be picked: AI-controlled, and not a hold's local charge (USER RULE: the hedgehog square and the choke line
        /// wait for cavalry and charge infantry only).
        /// </summary>
        internal static bool RidersAllowed(Formation own)
        {
            if (own == null || own.Team == null || !own.IsAIControlled)
            {
                return false;
            }
            TacticComponent tactic = TacticSelection.CurrentTacticRef(own.Team.TeamAI);
            if (tactic is RBMTacticHedgehog hedgehog && hedgehog.IsLocalChargeOf(own))
            {
                return false;
            }
            return !(tactic is RBMTacticHoldChoke choke && choke.IsLocalChargeOf(own));
        }

        /// <summary><paramref name="footTarget"/>, or a mounted formation whose threat dominates it; <paramref name="current"/> = the formation charged now (hysteresis).</summary>
        internal static Formation Prefer(Formation own, Formation footTarget, Formation current)
        {
            if (!RidersAllowed(own))
            {
                return footTarget;
            }
            Formation mounted = MostThreateningMounted(own, out float mountedThreat, current);
            if (mounted == null || mounted == footTarget)
            {
                return footTarget;
            }
            if (footTarget == null)
            {
                return mounted;
            }
            float ratio = mounted == current ? KeepRatio : SwitchRatio;
            return mountedThreat >= ratio * Threat(own, footTarget) ? mounted : footTarget;
        }

        internal static string Describe(Formation own, Formation enemy)
        {
            return TerrainCavalry.FormationName(enemy) + " threat=" + Threat(own, enemy).ToString("0", CultureInfo.InvariantCulture);
        }
    }
}
