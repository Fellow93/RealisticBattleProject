using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// A troop's wage is priced off its kit rather than its tier: a man in plate costs more to keep
    /// in the field than a levy, whatever rung of the tree they sit on. The daily wage becomes a flat
    /// share of what the man's battle gear is worth, the same equipment value every upgrade and every
    /// scrap of loot is already measured against.
    /// </summary>
    public static class GearBasedWageModel
    {
        /// <summary>
        /// Non-hero troops draw their wage through CharacterObject.TroopWage, which for them is exactly
        /// this call, so overriding it here re-bases the party's whole payment, every wage tooltip and
        /// the spoils wage-fraction in one place. Heroes never reach this path (their TroopWage is a
        /// level formula), so they keep vanilla pay. A fraction of zero leaves the tier-based wage the
        /// game hands out untouched.
        /// </summary>
        [HarmonyPatch(typeof(DefaultPartyWageModel))]
        [HarmonyPatch("GetCharacterWage")]
        private class OverrideGetCharacterWage
        {
            private static bool Prefix(CharacterObject character, ref int __result)
            {
                float fraction = RBMConfig.RBMConfig.troopWageGearFraction;
                if (fraction <= 0f || character == null || character.IsHero)
                {
                    return true;
                }

                // Raw share of the averaged battle-set value -- horse and harness included, since a
                // mounted man costs more to keep -- with no floor or cap: a barely-armed man may cost
                // nothing, a fully-plated cavalryman a great deal.
                __result = MathF.Round(SpoilsPool.GetEquipmentValueWithMount(character) * fraction);
                return false;
            }
        }
    }
}
