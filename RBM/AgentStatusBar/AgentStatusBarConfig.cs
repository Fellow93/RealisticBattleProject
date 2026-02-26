namespace RBM.AgentStatusBar
{
    public static class AgentStatusBarConfig
    {
        public const uint AllyHealthBarFillColour = 4294901760u;  // 0xFFFF0000 red
        public const uint AllyHealthBarEmptyColour = 4278190080u; // 0xFF000000 black
        public const uint EnemyHealthBarFillColour = 4294901760u;  // 0xFFFF0000 red
        public const uint EnemyHealthBarEmptyColour = 4278190080u; // 0xFF000000 black
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