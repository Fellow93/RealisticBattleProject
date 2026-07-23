using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;

namespace RBMCampaign
{
    /// <summary>
    /// A troop's daily wage comes off the historical pay table: a foot soldier's keep roughly doubles
    /// every couple of rungs, and a horseman draws what a footman one tier above him does. The figures
    /// are the medieval daily rates in pence at ten gold to the penny; a wage base of zero leaves the
    /// vanilla per-tier wage the game hands out untouched.
    /// </summary>
    public static class TierBasedWageModel
    {
        // Daily gold for tiers 1-6. Tier 0 rabble are paid as tier 1 -- nobody serves for nothing.
        private static readonly int[] FootWage = { 20, 30, 40, 60, 120, 240 };
        private static readonly int[] CavalryWage = { 30, 40, 60, 120, 240, 480 };

        /// <summary>
        /// The table itself, for callers who need a rate without a CharacterObject to hand.
        /// </summary>
        public static int WageForTier(int tier, bool mounted)
        {
            int index = tier - 1;
            if (index < 0)
            {
                index = 0;
            }
            else if (index >= FootWage.Length)
            {
                index = FootWage.Length - 1;
            }

            return mounted ? CavalryWage[index] : FootWage[index];
        }

        /// <summary>
        /// Non-hero troops draw their wage through CharacterObject.TroopWage, which for them is exactly
        /// this call, so overriding it here re-bases the party's whole payment, every wage tooltip and
        /// the spoils wage-fraction in one place. Heroes never reach this path, so they keep vanilla pay.
        /// </summary>
        [HarmonyPatch(typeof(DefaultPartyWageModel))]
        [HarmonyPatch("GetCharacterWage")]
        private class OverrideGetCharacterWage
        {
            private static bool Prefix(CharacterObject character, ref int __result)
            {
                if (RBMConfig.RBMConfig.troopWageTierBase <= 0 || character == null || character.IsHero)
                {
                    return true;
                }

                __result = WageForTier(character.Tier, character.IsMounted);
                return false;
            }
        }
    }
}
