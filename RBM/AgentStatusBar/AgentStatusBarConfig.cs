namespace RBM.AgentStatusBar
{
    public static class AgentStatusBarConfig
    {
        public const uint AllyHealthBarFillColour = 0xFF4488CCu;  // muted steel blue
        public const uint AllyHealthBarEmptyColour = 0xFF112233u; // dark blue-gray
        public const uint EnemyHealthBarFillColour = 0xFFCC4444u;  // muted brick red
        public const uint EnemyHealthBarEmptyColour = 0xFF331111u; // dark red-gray
        public const uint AllyPostureBarFillColour = 0xFF448844u;  // muted forest green
        public const uint AllyPostureBarEmptyColour = 0xFF2A2A2Au; // dark gray
        public const uint EnemyPostureBarFillColour = 0xFF448844u; // muted forest green (same as ally)
        public const uint EnemyPostureBarEmptyColour = 0xFF2A2A2Au; // dark gray
        public const uint AllyStaminaBarFillColour = 0xFFAA9933u;  // muted amber
        public const uint AllyStaminaBarEmptyColour = 0xFF2A2A2Au; // dark gray
        public const uint EnemyStaminaBarFillColour = 0xFFAA9933u; // muted amber (same as ally)
        public const uint EnemyStaminaBarEmptyColour = 0xFF2A2A2Au; // dark gray
        public const bool AllyHealthBars = true;
        public const bool EnemyHealthBars = true;
        public const bool HeroHealthBars = true;
        public const bool HealthOnHit = true;
        public const bool HealthOnHitOnlyForMainAgent = true;
        public const bool AllyBarMatchesWithBanner = false;
        public const bool EnemyBarMatchesWithBanner = false;
        public const int DistanceCutOff = 100;
        public const float BarWidth = 900f;
        public const float BarHeight = 84f;
    }
}