using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;

namespace RBMCampaign
{
    /// <summary>
    /// A troop's daily wage is a flat base value multiplied by its tier: each rung of the tree costs
    /// that much more to keep in the field. The base is configurable; a base of zero leaves the
    /// vanilla per-tier wage the game hands out untouched.
    /// </summary>
    public static class TierBasedWageModel
    {
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
                int wageBase = RBMConfig.RBMConfig.troopWageTierBase;
                if (wageBase <= 0 || character == null || character.IsHero)
                {
                    return true;
                }

                // Base pay per tier, so a tier-3 man costs three times a tier-1's keep. Tier-0 rabble
                // draw nothing, which is fine -- they carry no kit worth paying for.
                __result = wageBase * character.Tier;
                return false;
            }
        }
    }
}
