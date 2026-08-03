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
    /// A stack buys its own rations off a settlement's market and eats them out of its purse rather
    /// than the party's stores. It buys the best fare its pay stretches to, only what the stalls hold,
    /// and only what its spoils can reach.
    /// </summary>
    public static partial class TroopUpkeep
    {
        /// <summary>
        /// Every man who has eaten through his last purchase buys enough to carry him the configured
        /// span, at the rate the party would have eaten it out of the stores. He buys only what the
        /// market has and his purse can reach, so a stack that empties a starving village's stalls
        /// goes hungry sooner.
        /// </summary>
        private static int BuyFood(MobileParty mobileParty, Settlement settlement)
        {
            if (!SpoilsPool.IsEnabled || RBMConfig.RBMConfig.troopSettlementFoodDays <= 0)
            {
                return 0;
            }
            PartyBase party = mobileParty.Party;
            ItemRoster market = settlement.ItemRoster;
            if (party?.MemberRoster == null || market == null)
            {
                return 0;
            }

            int foodDays = RBMConfig.RBMConfig.troopSettlementFoodDays;
            TroopRoster roster = party.MemberRoster;
            List<FoodStall> stalls = null;
            int totalSpent = 0;
            int stacksFed = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character.IsHero || IsFed(party, element.Character))
                {
                    continue;
                }
                // Rounded up: the last few men are not left to starve over a fraction of an item.
                int wanted = (element.Number * foodDays + MenPerFoodPerDay - 1) / MenPerFoodPerDay;
                if (wanted <= 0)
                {
                    continue;
                }
                // Snapshotted once and drawn down as the stacks buy, since taking the last of an item
                // removes it from the roster and reindexes everything behind it.
                stalls = stalls ?? SnapshotFoodStalls(settlement, market);
                int spent = FeedStack(party, settlement, market, stalls, element, wanted, foodDays);
                if (spent > 0)
                {
                    totalSpent += spent;
                    stacksFed++;
                }
            }

            // The party-level line, always: what the party spent feeding itself, without naming stacks.
            if (SpoilsLog.IsEnabled && totalSpent > 0)
            {
                SpoilsLog.Log("FOOD", party, SpoilsLog.Describe(party) + " provisioned " + stacksFed
                    + (stacksFed == 1 ? " stack" : " stacks") + " in " + settlement.Name + " for " + totalSpent + " spoils");
            }
            return totalSpent;
        }

        /// <summary>One kind of food on sale: what it is, what a unit costs, how much is left.</summary>
        private struct FoodStall
        {
            public ItemObject Item;
            public int UnitSpoils;
            public int Available;
        }

        /// <remarks>
        /// The price is taken once, when the stalls are laid out, and holds for the whole party's
        /// pass. A party stripping the shelves does drive the price up, but it pays yesterday's price
        /// for today's shortage: re-pricing per stack would be more faithful and is not worth a market
        /// lookup per man. The town's next customer sees the new price.
        /// </remarks>
        private static List<FoodStall> SnapshotFoodStalls(Settlement settlement, ItemRoster market)
        {
            List<FoodStall> stalls = new List<FoodStall>();
            for (int i = 0; i < market.Count; i++)
            {
                ItemObject item = market.GetItemAtIndex(i);
                if (!item.IsFood || market.GetElementNumber(i) <= 0)
                {
                    continue;
                }
                stalls.Add(new FoodStall
                {
                    Item = item,
                    UnitSpoils = TroopMarketFeedback.UnitPrice(settlement, item, market, i),
                    Available = market.GetElementNumber(i)
                });
            }
            // Richest fare first. What a stack cannot stomach the price of, it passes over.
            stalls.Sort((a, b) => b.UnitSpoils.CompareTo(a.UnitSpoils));
            return stalls;
        }

        /// <summary>
        /// What a man will lay out for a day's rations before he calls it extravagant: a share of what
        /// he earns in a day. A recruit on two gold buys grain; a veteran on seventeen buys meat and
        /// cheese. Nothing sets a soldier's taste but his pay.
        /// </summary>
        /// <remarks>
        /// Quoted per item rather than per man-day, since that is how the market prices it: an item
        /// feeds MenPerFoodPerDay men for a day, so a man's daily share of it is that fraction.
        /// </remarks>
        private static int GetFoodPriceCeiling(CharacterObject character)
        {
            float dailyWage = Campaign.Current.Models.PartyWageModel.GetCharacterWage(character);
            return MathF.Round(dailyWage * RBMConfig.RBMConfig.troopFoodWageFraction * MenPerFoodPerDay);
        }

        /// <summary>Provisions one stack off the stalls; returns the spoils it spent, for the party tally.</summary>
        private static int FeedStack(PartyBase party, Settlement settlement, ItemRoster market, List<FoodStall> stalls, TroopRosterElement element, int wanted, int foodDays)
        {
            int budget = SpoilsPool.GetSpoils(party, element.Character);
            int spent = 0;
            int bought = 0;

            // The best fare he will pay for, then anything at all rather than go hungry. Both passes
            // walk from dearest to cheapest, so within what he can afford he always eats the best of it.
            int ceiling = GetFoodPriceCeiling(element.Character);
            BuyFromStalls(settlement, market, stalls, ceiling, budget, wanted, ref spent, ref bought);
            BuyFromStalls(settlement, market, stalls, int.MaxValue, budget, wanted, ref spent, ref bought);

            if (bought <= 0)
            {
                return 0;
            }

            // Half the food buys half the days. A stack that could only part-provision itself comes
            // back to the market sooner rather than eating a full ration out of an empty sack.
            int fedHours = MathF.Max(1, foodDays * 24 * bought / wanted);
            _fedUntilHours[SpoilsPool.Key(party, element.Character)] = NowHours + fedHours;
            SpoilsPool.AddSpoils(party, element.Character, -spent);

            if (SpoilsLog.Verbose)
            {
                SpoilsLog.LogVerbose("FOOD", party, SpoilsLog.Describe(party) + " " + SpoilsLog.Describe(element.Character) + " x" + element.Number
                    + " in " + settlement.Name + ": bought " + bought + " of " + wanted + " food for "
                    + spent + " spoils (would pay up to " + ceiling + " an item)"
                    + ", fed " + (fedHours / 24f).ToString("0.0") + " days"
                    + " (pool " + (SpoilsPool.GetSpoils(party, element.Character) + spent)
                    + " -> " + SpoilsPool.GetSpoils(party, element.Character) + ")");
            }
            return spent;
        }

        /// <summary>
        /// Takes what the stack can pay for off the stalls, dearest first, skipping anything above
        /// <paramref name="ceiling"/>. The stalls are drawn down in place so the next stack to buy
        /// sees what this one left, and the market roster is emptied to match. What the stack pays
        /// reaches the settlement through <see cref="TroopMarketFeedback"/> rather than vanishing
        /// with the food.
        /// </summary>
        private static void BuyFromStalls(Settlement settlement, ItemRoster market, List<FoodStall> stalls, int ceiling, int budget, int wanted, ref int spent, ref int bought)
        {
            for (int i = 0; i < stalls.Count && bought < wanted; i++)
            {
                FoodStall stall = stalls[i];
                if (stall.Available <= 0 || stall.UnitSpoils > ceiling)
                {
                    continue;
                }
                int take = MathF.Min(MathF.Min(wanted - bought, stall.Available), (budget - spent) / stall.UnitSpoils);
                if (take <= 0)
                {
                    continue;
                }
                market.AddToCounts(stall.Item, -take);
                stall.Available -= take;
                stalls[i] = stall;
                bought += take;
                spent += take * stall.UnitSpoils;
                TroopMarketFeedback.RegisterPurchase(settlement, stall.Item.ItemCategory, take * stall.UnitSpoils);
            }
        }
    }
}
