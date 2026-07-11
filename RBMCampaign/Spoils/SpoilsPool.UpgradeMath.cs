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
        /// What an upgrade *adds* to the purse rather than spends from it: when the better troop wears
        /// cheaper kit, the men strip the dearer gear they leave behind and the surplus falls to spoils.
        /// The mirror of <see cref="GetSpoilsCostForUpgrade"/> — scaled by the same multiplier, so a gold
        /// piece the upgrade no longer costs is a gold piece of spoils gained — and never both at once,
        /// since one needs the new kit dearer and the other needs it cheaper. Per man, like the cost.
        /// </summary>
        public static int GetSpoilsCreditForUpgrade(CharacterObject character, CharacterObject upgradeTarget)
        {
            if (!IsEnabled)
            {
                return 0;
            }
            int surplus = GetEquipmentValue(character) - GetEquipmentValue(upgradeTarget);
            if (surplus <= 0)
            {
                return 0;
            }
            return MathF.Max(1, MathF.Round(surplus * RBMConfig.RBMConfig.troopUpgradeCostMultiplier));
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
        /// The share of a stack's purse that <paramref name="count"/> upgrading men carry to the troop
        /// they become. Men leave with the same fraction of the purse as they are of the stack, computed
        /// from the pool that remains once their own upgrade has been paid for; what is left stays with
        /// the men who did not upgrade. A whole stack graduating carries the whole purse.
        /// </summary>
        public static int GetCarriedSpoils(int poolAfterSpend, int count, int stackSizeBefore)
        {
            if (poolAfterSpend <= 0 || count <= 0 || stackSizeBefore <= 0)
            {
                return 0;
            }
            if (count >= stackSizeBefore)
            {
                return poolAfterSpend;
            }
            // long guards the multiply: a deep purse times a big stack can top a couple billion.
            return (int)((long)poolAfterSpend * count / stackSizeBefore);
        }

        /// <summary>
        /// Moves the leaving men's share of the old stack's purse onto the troop they upgrade into, so
        /// spoils saved up under one name are not stranded when its men graduate to the next. Call once
        /// the upgrade's own spoils spend has already been drawn from <paramref name="from"/>, passing the
        /// stack size as it stood before the men left. Returns what was carried, for logging.
        /// </summary>
        public static int CarrySpoilsOnUpgrade(PartyBase party, CharacterObject from, CharacterObject to, int count, int stackSizeBefore)
        {
            int carried = GetCarriedSpoils(GetSpoils(party, from), count, stackSizeBefore);
            if (carried > 0)
            {
                AddSpoils(party, from, -carried);
                AddSpoils(party, to, carried);
            }
            return carried;
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
            // A cheaper-kitted upgrade salvages the surplus into the purse of the men who now hold it,
            // the upgradeTarget stack, so it survives the old stack emptying out beneath them.
            int credit = GetSpoilsCreditForUpgrade(character, upgradeTarget) * count;
            AddSpoils(party, character, -spend);
            AddSpoils(party, upgradeTarget, credit);
            // The roster has already moved the men, so the old stack now holds count fewer: adding them
            // back recovers the size it upgraded from, which is how big a share the leavers carry.
            int stackSizeBefore = GetStackSize(party, character) + count;
            int carried = CarrySpoilsOnUpgrade(party, character, upgradeTarget, count, stackSizeBefore);
            if (SpoilsLog.IsEnabled && (spend > 0 || credit > 0 || carried > 0))
            {
                SpoilsLog.Log("UPGRADE", party, "player upgraded " + count + "x " + SpoilsLog.Describe(character)
                    + " -> " + SpoilsLog.Describe(upgradeTarget)
                    + "| spoils spent " + spend + " of " + (GetSpoilsCostForUpgrade(character, upgradeTarget) * count) + " needed"
                    + (credit > 0 ? ", salvaged " + credit + " into " + SpoilsLog.Describe(upgradeTarget) + "'s purse" : "")
                    + (carried > 0 ? ", carried " + carried + " of the purse along" : "")
                    + ", pool " + GetSpoils(party, character));
            }
            ClearSpoilsIfStackGone(party, character);
        }
    }
}
