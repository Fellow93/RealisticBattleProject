using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// The mounted FORMED APPROACH shared by RBMBehaviorCavalryCharge and RBMBehaviorReact (USER RULE 2026-10-30): beyond GoDistance the
    /// charge is a Move in Line to the target's near edge (LeadEdge), the ChargeToTarget goes by GoReason and is kept until the gap stays
    /// above KeepDistance for KeepSeconds without melee. AI thread except Target (any thread).
    /// </summary>
    internal static class FormedApproach
    {
        internal const float GoDistance = 35f; // m edge gap (Gap); user 2026-10-30, was 65

        /// <summary>The approach's edge gap, the same for every caller: both formations' nominal rectangles (FootprintMode.Current), not their scattered men.</summary>
        internal static float Gap(Formation own, Formation target)
        {
            return FormationGeometry.Gap(own, FootprintMode.Current, target, FootprintMode.Current);
        }
        internal const float KeepDistance = 110f; // m
        internal const float KeepSeconds = 2f;
        internal const float ClashLeadSeconds = 4f;
        internal const float LeadMaxSeconds = 2f; // the Move point leads a moving target by its velocity, at most this long
        internal const float FacingTurnCos = 0.985f; // ~10 degrees

        /// <summary>The go rule's log text, null while the formed approach holds. gap is the caller's edge gap, ownMelee its own melee test.</summary>
        internal static string GoReason(Formation own, Formation target, float gap, Vec2 centre, Vec2 targetCentre, bool ownMelee)
        {
            if (gap <= GoDistance)
            {
                return "gap=" + TerrainCavalry.Fmt0(gap) + "m";
            }
            if (ownMelee)
            {
                return "melee gap=" + TerrainCavalry.Fmt0(gap) + "m";
            }
            // user rule 2026-10-30: a target already fighting is no reason to go early (in a cavalry swirl it always is)
            if (ChargeTargetChoice.IsMounted(target))
            {
                // a cavalry clash: both closing, go early so they do not meet in a Move
                float closing = EnemyMotion.ClosingSpeed(own, targetCentre) + EnemyMotion.ClosingSpeed(target, centre);
                if (closing > 0f && closing * ClashLeadSeconds >= gap)
                {
                    return "clash closing=" + TerrainCavalry.Fmt1(closing) + " gap=" + TerrainCavalry.Fmt0(gap) + "m";
                }
            }
            return null;
        }

        /// <summary>The Move point: the target's near edge along u (own centre -> target centre), led by its velocity up to LeadMaxSeconds,
        /// never back toward us. False when the centres coincide.</summary>
        internal static bool TryLeadEdge(Formation own, Formation target, Footprint shape, Vec2 centre, Vec2 targetCentre, float gap, out Vec2 edge, out Vec2 u)
        {
            u = targetCentre - centre;
            if (u.Normalize() < 0.01f)
            {
                edge = Vec2.Invalid;
                return false;
            }
            edge = targetCentre - u * FormationGeometry.ExtentAlong(shape, u);
            Vec2 velocity;
            float speed = own.QuerySystem.MovementSpeedMaximum;
            if (EnemyMotion.TryVelocity(target, out velocity) && speed > 0f && !float.IsNaN(speed))
            {
                Vec2 lead = velocity * MathF.Min(LeadMaxSeconds, gap / speed);
                // a target riding at us is met where it is, not led back toward us
                float along = Vec2.DotProduct(lead, u);
                if (along < 0f)
                {
                    lead -= u * along;
                }
                edge += lead;
            }
            return true;
        }

        /// <summary>Any thread: the target an AI formation rides at in a formed approach (its order is a Move), else null.</summary>
        internal static Formation Target(Formation formation)
        {
            return global::RBMBehaviorCavalryCharge.FormedApproachTarget(formation) ?? RBMBehaviorReact.FormedApproachTarget(formation);
        }
    }
}
