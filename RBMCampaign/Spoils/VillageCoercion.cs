using System;
using System.Collections.Generic;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// Forcing supplies out of a village that does NOT resist (v1.5.0's no-war coercion path) is wired
    /// like a raid instead of being a mint.
    /// </summary>
    /// <remarks>
    /// Since v1.5.0, <c>VillageHostileActionCampaignBehavior</c> forks each hostile action: if the village
    /// resists there is a real raid <c>MapEvent</c>, and every RBM raid seam (purse drain, spoils split,
    /// goods destruction) runs. If it does not resist, vanilla goes straight to
    /// <c>village_force_supplies_ended_successfully_on_consequence</c>, which pays the player hearth-scaled
    /// gold with <c>GiveGoldAction.ApplyBetweenCharacters(null, MainHero, ...)</c> -- a null giver, i.e.
    /// coin minted from nobody -- and hands over freshly minted production goods, raising no raid event.
    /// Under RBM that made coercion the cheapest money printer in the game and drained nothing from the
    /// village.
    ///
    /// This marks the consequence for its duration (prefix + finalizer, the same pattern
    /// <see cref="RansomFunding"/> uses), and while marked:
    /// <list type="bullet">
    /// <item>the gold hand-off is charged to the village purse and clamped to what it holds; with the
    /// spoils economy on, the coin goes into the party's spoils purses with the leader's cut, exactly
    /// like a raid's, and the mint is cancelled; with it off, the clamped amount still reaches the player
    /// directly, conserved;</item>
    /// <item>the goods roster is scaled by <see cref="RaidGoodsDestruction"/>'s taken fraction, so the two
    /// hostile-action paths yield consistently.</item>
    /// </list>
    /// Forced volunteers are left alone: that path mints no coin and no goods.
    /// </remarks>
    public static class VillageCoercion
    {
        // The village currently being coerced, or null outside the consequence.
        private static Settlement _village;

        [HarmonyPatch(typeof(VillageHostileActionCampaignBehavior), "village_force_supplies_ended_successfully_on_consequence")]
        private static class MarkCoercionPatch
        {
            private static void Prefix()
            {
                _village = RBMConfig.RBMConfig.rbmCampaignEnabled ? Settlement.CurrentSettlement : null;
            }

            private static void Finalizer()
            {
                _village = null;
            }
        }

        /// <summary>
        /// The coin leg. Fires only on the exact mint: null giver, the player as recipient, coercion in
        /// progress. Everything else is left to vanilla.
        /// </summary>
        [HarmonyPatch(typeof(GiveGoldAction), "ApplyInternal")]
        private static class FundCoercionPatch
        {
            private static bool Prefix(Hero giverHero, PartyBase giverParty, Hero recipientHero, ref int goldAmount)
            {
                Settlement village = _village;
                if (village == null || village.Village == null
                    || giverHero != null || giverParty != null
                    || recipientHero != Hero.MainHero
                    || goldAmount <= 0)
                {
                    return true;
                }

                int drained = SettlementWealth.Debit(village, goldAmount, SettlementWealth.Source.Raid);
                if (drained < 1)
                {
                    // A broke village hands over nothing, however hard it was squeezed.
                    goldAmount = 0;
                    return false;
                }

                if (SpoilsPool.IsEnabled && RBMConfig.RBMConfig.troopRaidSpoilsMultiplier > 0f)
                {
                    SpoilsPool.OnVillageCoerced(village, PartyBase.MainParty, drained);
                    return false;
                }

                // Spoils economy off: the player is paid directly, but only what the village had.
                goldAmount = drained;
                return true;
            }
        }

        /// <summary>The goods leg: waste the same share of the haul a raid would.</summary>
        [HarmonyPatch(typeof(InventoryScreenHelper), "OpenScreenAsLoot", new Type[] { typeof(Dictionary<PartyBase, ItemRoster>) })]
        private static class ScaleCoercedGoodsPatch
        {
            private static void Prefix(Dictionary<PartyBase, ItemRoster> itemRostersToLoot)
            {
                if (_village == null || !RaidGoodsDestruction.IsEnabled || itemRostersToLoot == null)
                {
                    return;
                }
                float fraction = RaidGoodsDestruction.TakenFraction(PartyBase.MainParty);
                if (fraction >= 1f)
                {
                    return;
                }
                foreach (KeyValuePair<PartyBase, ItemRoster> entry in itemRostersToLoot)
                {
                    ItemRoster roster = entry.Value;
                    if (roster == null)
                    {
                        continue;
                    }
                    // Backwards: removing an element shifts the indices above it.
                    for (int i = roster.Count - 1; i >= 0; i--)
                    {
                        ItemRosterElement element = roster.GetElementCopyAtIndex(i);
                        int count = element.Amount;
                        int keep = (int)Math.Round(count * fraction);
                        if (keep < count)
                        {
                            roster.AddToCounts(element.EquipmentElement, keep - count);
                        }
                    }
                }
            }
        }
    }
}
