using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// What an upgrade costs a stack in spoils, and how far the purse reaches across a batch of men.
    /// Spoils are spent one man at a time, so the leading men go free and only the rest pay.
    /// </summary>
    public static partial class SpoilsPool
    {
        /// <summary>
        /// What the better kit is worth over the old, which is exactly what the upgrade costs in gold.
        /// A point of spoils is a gold piece: the two prices are the same number because they are the
        /// same price, paid out of different pockets. Zero means the upgrade needs no spoils, so
        /// callers must not divide by it.
        /// </summary>
        public static int GetSpoilsCostForUpgrade(CharacterObject character, CharacterObject upgradeTarget)
        {
            if (!IsEnabled)
            {
                return 0;
            }
            int delta = GetEquipmentValue(upgradeTarget) - GetEquipmentValue(character);
            if (delta <= 0)
            {
                return 0;
            }
            return MathF.Max(1, MathF.Round(delta * RBMConfig.RBMConfig.troopUpgradeCostMultiplier));
        }

        /// <summary>
        /// How many men the stockpile can outfit, as a fraction. Two and a half means two upgrade
        /// free and the third pays half price.
        /// </summary>
        public static float GetCoveredMen(PartyBase party, CharacterObject character, CharacterObject upgradeTarget)
        {
            int spoilsCost = GetSpoilsCostForUpgrade(character, upgradeTarget);
            return spoilsCost <= 0 ? 0f : (float)GetAvailableSpoils(party, character) / spoilsCost;
        }

        /// <summary>
        /// Of <paramref name="count"/> men upgrading, how many the gold has to pay for. Spoils are spent
        /// one man at a time rather than smeared across the stack, so the first men go free and only
        /// what the stockpile cannot reach is charged.
        /// </summary>
        public static float GetUnpaidMen(PartyBase party, CharacterObject character, CharacterObject upgradeTarget, int count)
        {
            return MathF.Max(0f, count - MathF.Min(GetCoveredMen(party, character, upgradeTarget), (float)count));
        }

        /// <summary>Whole men the stockpile outfits outright, capped at the stack.</summary>
        public static int GetFreeUpgradeCount(PartyBase party, CharacterObject character, CharacterObject upgradeTarget)
        {
            int spoilsCost = GetSpoilsCostForUpgrade(character, upgradeTarget);
            if (spoilsCost <= 0)
            {
                return 0;
            }
            return MathF.Min(GetAvailableSpoils(party, character) / spoilsCost, GetStackSize(party, character));
        }

        /// <summary>Spoils drawn down by upgrading <paramref name="count"/> men, never more than the stockpile holds.</summary>
        public static int GetBatchSpoilsSpend(PartyBase party, CharacterObject character, CharacterObject upgradeTarget, int count)
        {
            int spoilsCost = GetSpoilsCostForUpgrade(character, upgradeTarget);
            if (spoilsCost <= 0 || count <= 0)
            {
                return 0;
            }
            return MathF.Min(GetAvailableSpoils(party, character), spoilsCost * count);
        }

        /// <summary>
        /// Fires once per upgraded stack when the player commits the party screen, never when the
        /// screen is cancelled. The screen already worked out what each man cost as it went, so the
        /// spoils it reserved is simply drawn down here rather than recomputed.
        /// </summary>
        public static void OnPlayerUpgradedTroops(CharacterObject character, CharacterObject upgradeTarget, int count)
        {
            PartyBase party = PartyBase.MainParty;
            int spend = PartyScreenStagedUpgrades.ConsumeStagedSpoils(party, character);
            if (SpoilsLog.IsEnabled && spend > 0)
            {
                SpoilsLog.Log("UPGRADE", party, "player upgraded " + count + "x " + SpoilsLog.Describe(character)
                    + " -> " + SpoilsLog.Describe(upgradeTarget)
                    + "| spoils spent " + spend + " of " + (GetSpoilsCostForUpgrade(character, upgradeTarget) * count) + " needed"
                    + ", pool " + GetSpoils(party, character) + " -> " + (GetSpoils(party, character) - spend));
            }
            AddSpoils(party, character, -spend);
            ClearSpoilsIfStackGone(party, character);
        }
    }
}
