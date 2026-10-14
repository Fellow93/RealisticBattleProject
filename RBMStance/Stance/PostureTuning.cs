namespace RBMStance
{
    // Not config settings; tune in game.
    public static class PostureTuning
    {
        // Share of max posture a break resets to at once; the second after the breaker's own landed hit.
        public const float PostureBreakResetShare = 0.75f;
        public const float LandedHitBreakResetShare = 0.33f;

        public const float PostureRegenPauseSeconds = 1.2f;

        // No hits and no posture exchange (blocks, parries) for this long before quick regen.
        public const float QuickPostureRegenQuietSeconds = 10f;

        // Quick stamina regen: below this share of max and no hits for this long.
        public const float QuickStaminaRegenBelowShare = 0.7f;
        public const float QuickStaminaRegenNoHitSeconds = 10f;

        public const float QuickRegenMultiplier = 3f;

        // Regen runs about every update period: the tick count per period, scaled by the time actually elapsed,
        // which counts for at most this many periods (a hitch gives no burst).
        public const float RegenUpdatePeriodSeconds = 0.5f;
        public const int RegenTicksPerUpdate = 30;
        public const float RegenMaxPeriodsPerUpdate = 2f;

        // Every interval, a man above the stamina share regains this much health.
        public const float StaminaHealthRegenIntervalSeconds = 10f;
        public const float StaminaHealthRegenAboveShare = 0.85f;
        public const float StaminaHealthRegenAmount = 0.9f;

        // A break that overshoots by this share of max posture also drops the weapon/shield.
        public const float DropOverkillShare = 0.5f;

        // Perfect parry: defender pays the matching block's cost times the factor, then regains the refund share of max.
        public const float PerfectParryWeaponFactor = 0.25f;
        public const float PerfectParryShieldFactor = 0.5f;
        public const float PerfectParryWeaponRefund = 0.05f;
        public const float PerfectParryShieldRefund = 0.025f;
        // Share of the crush-through damage a weapon parry break still lets through.
        public const float PerfectParryCrushThroughFactor = 0.5f;

        // Multipliers on the parry result.
        public const float ChamberDefenderFactor = 0.5f;
        public const float ChamberAttackerFactor = 1.5f;

        public const float LandedHitAttackerFactor = 0.2f;

        public const float ClosingSpeedPostureFactor = 4f;

        // Applied to the attacker/defender skill ratio; 0.5 = square root, so skill can't outweigh the weapon table.
        public const float SkillRatioExponent = 0.5f;

        // Most posture a missile stopped by a raised shield costs (before Arrow Catcher).
        public const float ShieldMissilePostureCap = 2f;
        public const float ShieldThrownPostureCap = 6f;
    }
}
