using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// A stack resting in a settlement pays the local surgeons out of its own purse to mend its wounded
    /// faster than they would knit on the march. The coin leaves for the settlement the way carousing and
    /// provisioning do, and a man is only ever patched up a little at a time, so a bad convalescence still
    /// takes a stay in town rather than an hour -- it just costs the stack the kit it was saving for. This
    /// runs on top of the game's own free daily healing, so a stack with an empty purse still recovers, only
    /// slower.
    /// </summary>
    public static partial class TroopUpkeep
    {
        /// <summary>
        /// Mends a share of each wounded stack that can pay for it. Heroes heal by their own rules rather
        /// than a soldier count, so they are left to the game; a stack with none down, an empty purse, or a
        /// fee it cannot meet pays and mends nothing, so healing can never drive the pool negative.
        /// </summary>
        private static void HealWounded(MobileParty mobileParty, Settlement settlement)
        {
            if (!SpoilsPool.IsEnabled || RBMConfig.RBMConfig.troopSpoilsHealGoldPerTier <= 0)
            {
                return;
            }
            PartyBase party = mobileParty.Party;
            TroopRoster roster = party.MemberRoster;
            int spentTotal = 0;
            int healedTotal = 0;

            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                CharacterObject character = element.Character;
                if (character.IsHero || element.WoundedNumber <= 0)
                {
                    continue;
                }
                int purse = SpoilsPool.GetSpoils(party, character);
                if (purse <= 0)
                {
                    continue;
                }
                // A surgeon charges more to patch up a valuable veteran than a raw recruit, so the fee
                // scales with the man's tier -- and his richer purse can bear it.
                int costPerMan = MathF.Max(1, RBMConfig.RBMConfig.troopSpoilsHealGoldPerTier * character.Tier);
                // Only a little of the wounded is mended each hour, so even a deep purse buys a faster
                // recovery rather than an instant one. A dead-zero rate is a hard off-switch; above it, at
                // least one man mends so a small stack whose share rounds to nothing still makes progress.
                float healRate = RBMConfig.RBMConfig.troopSpoilsHealFractionPerHour;
                if (healRate <= 0f)
                {
                    continue;
                }
                int perHourCap = MathF.Max(1, MathF.Round(element.WoundedNumber * healRate));
                int heal = MathF.Min(element.WoundedNumber, perHourCap);
                heal = MathF.Min(heal, purse / costPerMan);
                if (heal <= 0)
                {
                    continue;
                }
                int cost = heal * costPerMan;
                // Count unchanged, wounded down: the men stay on the roster, just no longer hurt. Because the
                // stack's size does not change, the roster does not reindex, so iterating it here is safe.
                roster.AddToCounts(character, 0, false, -heal);
                SpoilsPool.AddSpoils(party, character, -cost);
                spentTotal += cost;
                healedTotal += heal;
            }

            if (spentTotal > 0)
            {
                CreditSettlement(settlement, spentTotal);
            }
            if (healedTotal > 0 && SpoilsLog.IsEnabled)
            {
                // Hourly, so once a day per party per settlement is enough to see the rate without flooding.
                SpoilsLog.LogOnce("heal-" + party.Id + "-" + settlement.StringId + "-" + (NowHours / 24), "HEAL", party,
                    SpoilsLog.Describe(party) + " paying for care in " + settlement.Name
                    + ": " + healedTotal + " mended this hour for " + spentTotal + " spoils");
            }
        }
    }
}
