using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// A stack sitting on more spoils than it will ever need for its kit sometimes indulges: it buys a
    /// luxury off the settlement's market -- jewellery, velvet, furs, wine, a fine garment -- purely to
    /// have it. The
    /// coin leaves the purse for the settlement, the way carousing does, and the good itself is a
    /// personal keepsake rather than party loot, so it cannot be turned back into gold at the next town.
    /// </summary>
    public static partial class TroopUpkeep
    {
        /// <summary>
        /// Rolls each over-cap stack for an indulgence. Only stacks with coin to spare over their ceiling
        /// take part, only one purchase per stack per cooldown, and only now and then even then.
        /// </summary>
        private static int MaybeBuyLuxury(MobileParty mobileParty, Settlement settlement)
        {
            if (!SpoilsPool.IsEnabled)
            {
                return 0;
            }
            PartyBase party = mobileParty.Party;
            ItemRoster market = settlement.ItemRoster;
            if (party?.MemberRoster == null || market == null)
            {
                return 0;
            }

            int totalSpent = 0;
            TroopRoster roster = party.MemberRoster;
            // As with food, only the player's own party earns a floating word above the market naming the
            // bauble it bought; every other party indulges silently.
            List<(ItemObject Item, int Count)> playerPurchases = (mobileParty == MobileParty.MainParty)
                ? new List<(ItemObject, int)>() : null;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                CharacterObject character = element.Character;
                if (character.IsHero)
                {
                    continue;
                }
                int purse = SpoilsPool.GetSpoils(party, character);
                // Nothing over the cap, nothing to fritter on a bauble.
                if (purse <= SpoilsPool.GetSpoilsCap(party, character))
                {
                    continue;
                }
                if (IsLuxuryOnCooldown(party, character))
                {
                    continue;
                }
                if (MBRandom.RandomFloat >= RBMConfig.RBMConfig.troopLuxurySpendChance)
                {
                    continue;
                }
                totalSpent += BuyLuxury(party, settlement, market, element, purse, playerPurchases);
            }

            // One bubble for the hour's indulgences: which keepsakes the men bought, and what they cost.
            if (playerPurchases != null && totalSpent > 0 && playerPurchases.Count > 0)
            {
                RBMMapNotifications.RaiseSoldiersBoughtLuxury(settlement, mobileParty, playerPurchases, totalSpent);
            }
            return totalSpent;
        }

        /// <summary>
        /// Picks a luxury the stack can afford off the market at random and buys a single piece of it,
        /// drawing the price from its purse and leaving it in the settlement. Sets the cooldown only if a
        /// purchase actually lands, so a stack that finds nothing to its taste rolls again next hour.
        /// </summary>
        private static int BuyLuxury(PartyBase party, Settlement settlement, ItemRoster market, TroopRosterElement element, int purse, List<(ItemObject Item, int Count)> purchases = null)
        {
            List<int> affordable = null;
            for (int i = 0; i < market.Count; i++)
            {
                ItemObject item = market.GetItemAtIndex(i);
                // Trade goods and equipment both qualify -- a fine garment is as much an indulgence as
                // a cask of wine. Food is the one trade good we never treat as a keepsake.
                if (item == null || item.IsFood || item.ItemCategory == null)
                {
                    continue;
                }
                // A luxury proper: the market wants it more as an indulgence than as a staple.
                if (item.ItemCategory.LuxuryDemand <= item.ItemCategory.BaseDemand || market.GetElementNumber(i) <= 0)
                {
                    continue;
                }
                if (TroopMarketFeedback.UnitPrice(settlement, item, market, i) > purse)
                {
                    continue;
                }
                if (affordable == null)
                {
                    affordable = new List<int>();
                }
                affordable.Add(i);
            }
            if (affordable == null)
            {
                return 0;
            }

            int index = affordable[MBRandom.RandomInt(affordable.Count)];
            ItemObject chosen = market.GetItemAtIndex(index);
            int cost = TroopMarketFeedback.UnitPrice(settlement, chosen, market, index);
            market.AddToCounts(chosen, -1);
            SpoilsPool.AddSpoils(party, element.Character, -cost);
            TroopMarketFeedback.RegisterPurchase(settlement, chosen.ItemCategory, cost);
            SetLuxuryCooldown(party, element.Character);
            purchases?.Add((chosen, 1));

            if (SpoilsLog.Verbose)
            {
                SpoilsLog.Log("LUX", party, SpoilsLog.Describe(party) + " " + SpoilsLog.Describe(element.Character) + " x" + element.Number
                    + " indulged in " + chosen.Name + " at " + settlement.Name + " for " + cost
                    + " spoils (pool " + purse + " -> " + SpoilsPool.GetSpoils(party, element.Character)
                    + ", cap " + SpoilsPool.GetSpoilsCap(party, element.Character) + ")");
            }
            else if (SpoilsLog.IsEnabled)
            {
                SpoilsLog.Log("LUX", party, SpoilsLog.Describe(party) + " indulged in " + chosen.Name
                    + " at " + settlement.Name + " for " + cost + " spoils");
            }
            return cost;
        }

        private static bool IsLuxuryOnCooldown(PartyBase party, CharacterObject character)
        {
            int until;
            return _luxuryCooldownUntilHours.TryGetValue(SpoilsPool.Key(party, character), out until) && until > NowHours;
        }

        private static void SetLuxuryCooldown(PartyBase party, CharacterObject character)
        {
            _luxuryCooldownUntilHours[SpoilsPool.Key(party, character)] = NowHours + RBMConfig.RBMConfig.troopLuxuryCooldownDays * 24;
        }
    }
}
