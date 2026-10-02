using TaleWorlds.MountAndBlade;

namespace RBMConfig
{
    /// <summary>
    /// The kind of block a melee attack met, in the posture system's own six cases
    /// (RBMAI <c>MeleeBlowPatch</c>, the <c>MeleeHitType</c> branches), so the battle hit
    /// log (RBMCombat) and the developer stats overlay (RBMAI) count the same thing the
    /// posture math reacts to. Lives here because both modules reference RBMConfig.
    /// If MeleeBlowPatch's branch conditions change, change <see cref="Classify"/> with them.
    /// </summary>
    public enum MeleeBlockKind
    {
        None,
        ChamberBlock,
        WeaponBlock,
        WeaponParry,
        ShieldWrongSide,
        ShieldBlock,
        ShieldParry
    }

    public static class MeleeBlock
    {
        public const int KindCount = 7;

        public static MeleeBlockKind Classify(in AttackCollisionData collision)
        {
            if (collision.IsMissile)
            {
                return MeleeBlockKind.None;
            }
            CombatCollisionResult result = collision.CollisionResult;
            if (result == CombatCollisionResult.ChamberBlocked)
            {
                return MeleeBlockKind.ChamberBlock;
            }
            if (!collision.AttackBlockedWithShield)
            {
                if (result == CombatCollisionResult.Blocked)
                {
                    return MeleeBlockKind.WeaponBlock;
                }
                if (result == CombatCollisionResult.Parried)
                {
                    return MeleeBlockKind.WeaponParry;
                }
                return MeleeBlockKind.None;
            }
            // Shield: a parry from the wrong side is only a normal block, as in MeleeBlowPatch.
            if (result == CombatCollisionResult.Blocked)
            {
                return collision.CorrectSideShieldBlock ? MeleeBlockKind.ShieldBlock : MeleeBlockKind.ShieldWrongSide;
            }
            if (result == CombatCollisionResult.Parried)
            {
                return collision.CorrectSideShieldBlock ? MeleeBlockKind.ShieldParry : MeleeBlockKind.ShieldBlock;
            }
            return MeleeBlockKind.None;
        }

        /// <summary>Short label, at most 8 characters (the hit log's "what" column).</summary>
        public static string Label(MeleeBlockKind kind)
        {
            switch (kind)
            {
                case MeleeBlockKind.ChamberBlock:
                    return "chamber";

                case MeleeBlockKind.WeaponBlock:
                    return "w-block";

                case MeleeBlockKind.WeaponParry:
                    return "w-parry";

                case MeleeBlockKind.ShieldWrongSide:
                    return "sh-wrong";

                case MeleeBlockKind.ShieldBlock:
                    return "sh-block";

                case MeleeBlockKind.ShieldParry:
                    return "sh-parry";

                default:
                    return "-";
            }
        }
    }
}
