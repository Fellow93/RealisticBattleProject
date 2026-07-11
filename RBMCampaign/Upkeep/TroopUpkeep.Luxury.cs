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
        private static void MaybeBuyLuxury(MobileParty mobileParty, Settlement settlement)
        {
            if (!SpoilsPool.IsEnabled)
            {
                return;
            }
            PartyBase party = mobileParty.Party;
            ItemRoster market = settlement.ItemRoster;
            if (party?.MemberRoster == null || market == null)
            {
                return;
            }

            TroopRoster roster = party.MemberRoster;
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
                BuyLuxury(party, settlement, market, element, purse);
            }
        }

        /// <summary>
        /// Picks a luxury the stack can afford off the market at random and buys a single piece of it,
        /// drawing the price from its purse and leaving it in the settlement. Sets the cooldown only if a
        /// purchase actually lands, so a stack that finds nothing to its taste rolls again next hour.
        /// </summary>
        private static void BuyLuxury(PartyBase party, Settlement settlement, ItemRoster market, TroopRosterElement element, int purse)
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
                if (MathF.Max(1, market.GetElementUnitCost(i)) > purse)
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
                return;
            }

            int index = affordable[MBRandom.RandomInt(affordable.Count)];
            ItemObject chosen = market.GetItemAtIndex(index);
            int cost = MathF.Max(1, market.GetElementUnitCost(index));
            market.AddToCounts(chosen, -1);
            SpoilsPool.AddSpoils(party, element.Character, -cost);
            CreditSettlement(settlement, cost);
            SetLuxuryCooldown(party, element.Character);

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
