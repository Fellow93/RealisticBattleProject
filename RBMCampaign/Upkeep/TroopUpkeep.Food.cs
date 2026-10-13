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
        /// Days of rations an AI lord's war party carries per man, when that is more than the
        /// configured <c>troopSettlementFoodDays</c>.
        /// </summary>
        /// <remarks>
        /// A stack used to buy exactly the configured 20 days and buy nothing more until it had eaten
        /// every bit of it, so over a campaign its rations ran a sawtooth from 20 down to 0 -- about ten
        /// days on average. The party's own stores added little on top: vanilla's food buying divides
        /// them by today's consumption, which <see cref="ProvisionedMenEatTheirOwnFood"/> shrinks to the
        /// unprovisioned men, so while any rations are left it judges the stores ample and leaves them
        /// thin. Lords therefore sat at 10-13 days and kept failing the army gates (leader over
        /// <c>NeededFoodsInDaysThresholdForSiege</c> = 12 days, each called member over 15). A lord on
        /// campaign now carries a month and tops it back up long before it runs low.
        /// </remarks>
        private const int LordRationDays = 30;

        /// <summary>
        /// A lord's stack tops its rations back up to <see cref="LordRationDays"/> at any settlement it
        /// stops in once fewer than this many days are left, instead of waiting until they are gone.
        /// </summary>
        private const int LordRationTopUpBelowDays = 20;

        /// <summary>
        /// AI lords' war parties, mercenary companies and the player clan's companion-led parties among
        /// them. Not the player's own party, and not caravans, villagers, garrisons or bandits: they keep
        /// the configured span and buy again only once it is eaten.
        /// </summary>
        private static bool CarriesCampaignRations(MobileParty mobileParty)
        {
            return mobileParty != null && mobileParty.IsLordParty && !mobileParty.IsMainParty;
        }

        /// <summary>
        /// Every man who has eaten through his last purchase buys enough to carry him the configured
        /// span, at the rate the party would have eaten it out of the stores. He buys only what the
        /// market has and his purse can reach, so a stack that empties a starving village's stalls
        /// goes hungry sooner. An AI lord's men carry a longer span and top it up before it runs out
        /// (<see cref="LordRationDays"/>, <see cref="LordRationTopUpBelowDays"/>), paying only for the
        /// days they are short.
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
            // How many hours of rations a stack still holding some must be down to before it buys
            // more. Zero for everyone but lords: they buy only once the last purchase is eaten.
            int topUpBelowHours = 0;
            if (CarriesCampaignRations(mobileParty))
            {
                foodDays = MathF.Max(foodDays, LordRationDays);
                topUpBelowHours = MathF.Min(LordRationTopUpBelowDays, foodDays) * 24;
            }
            int targetHours = foodDays * 24;
            int now = NowHours;
            TroopRoster roster = party.MemberRoster;
            List<FoodStall> stalls = null;
            int totalSpent = 0;
            int stacksFed = 0;
            // Only the player's own party earns a floating word above the market naming what it bought,
            // so the tally of items is gathered only for it. Every other party feeds itself silently.
            List<(ItemObject Item, int Count)> playerPurchases = (mobileParty == MobileParty.MainParty)
                ? new List<(ItemObject, int)>() : null;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character.IsHero)
                {
                    continue;
                }
                // The hours of rations the stack still carries. Outside a lord's party any at all is
                // enough to pass the market by, exactly as IsFed judges it.
                int remainingHours = 0;
                if (_fedUntilHours.TryGetValue(party, element.Character, out int fedUntil) && fedUntil > now)
                {
                    remainingHours = fedUntil - now;
                }
                if (remainingHours > 0 && remainingHours >= topUpBelowHours)
                {
                    continue;
                }
                // Only the hours it is short of the full span: a top-up never pays twice for food the
                // men are already carrying. For a stack with nothing left this is the full span.
                int neededHours = targetHours - remainingHours;
                if (neededHours <= 0)
                {
                    continue;
                }
                // Rounded up: the last few men are not left to starve over a fraction of an item.
                int perItemHours = MenPerFoodPerDay * 24;
                int wanted = (int)(((long)element.Number * neededHours + perItemHours - 1) / perItemHours);
                if (wanted <= 0)
                {
                    continue;
                }
                // An empty purse buys nothing whatever the stalls hold, so it is turned away before the
                // stalls are laid out and priced: a party whose hungry stacks are all broke never prices
                // the market at all.
                int budget = SpoilsPool.GetSpoils(party, element.Character);
                if (budget <= 0)
                {
                    continue;
                }
                // Snapshotted once and drawn down as the stacks buy, since taking the last of an item
                // removes it from the roster and reindexes everything behind it.
                stalls = stalls ?? SnapshotFoodStalls(settlement, market);
                int spent = FeedStack(party, settlement, market, stalls, element, wanted, now + remainingHours, neededHours, budget, playerPurchases);
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

            // One bubble for the whole provisioning pass: which fare the men laid in, and what it cost.
            if (playerPurchases != null && totalSpent > 0 && playerPurchases.Count > 0)
            {
                RBMMapNotifications.RaiseTroopsBoughtFood(settlement, mobileParty, playerPurchases, totalSpent);
            }
            return totalSpent;
        }

        /// <summary>One kind of food on sale: what it is, what a unit costs, how much is left.</summary>
        private struct FoodStall
        {
            // The exact stack, modifier included: priced off it and bought out of it, never out of a
            // sibling stack of the same item.
            public EquipmentElement Element;
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
                    Element = market.GetElementCopyAtIndex(i).EquipmentElement,
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

        /// <summary>
        /// Provisions one stack off the stalls out of <paramref name="budget"/>, its purse as the caller
        /// just read it; returns the spoils it spent, for the party tally. <paramref name="wanted"/> items
        /// buy <paramref name="neededHours"/> of rations on top of <paramref name="fedFromHour"/>, the
        /// hour the stack's current rations run out (now, for a stack carrying none).
        /// </summary>
        private static int FeedStack(PartyBase party, Settlement settlement, ItemRoster market, List<FoodStall> stalls, TroopRosterElement element, int wanted, int fedFromHour, int neededHours, int budget, List<(ItemObject Item, int Count)> purchases = null)
        {
            int spent = 0;
            int bought = 0;

            // The best fare he will pay for, then anything at all rather than go hungry. Both passes
            // walk from dearest to cheapest, so within what he can afford he always eats the best of it.
            int ceiling = GetFoodPriceCeiling(element.Character);
            BuyFromStalls(settlement, market, stalls, ceiling, budget, wanted, ref spent, ref bought, purchases);
            BuyFromStalls(settlement, market, stalls, int.MaxValue, budget, wanted, ref spent, ref bought, purchases);

            if (bought <= 0)
            {
                return 0;
            }

            // Half the food buys half the days. A stack that could only part-provision itself comes
            // back to the market sooner rather than eating a full ration out of an empty sack.
            int fedHours = MathF.Max(1, (int)((long)neededHours * bought / wanted));
            _fedUntilHours.Set(party, element.Character, fedFromHour + fedHours);
            SpoilsPool.AddSpoils(party, element.Character, -spent);

            if (SpoilsLog.Verbose)
            {
                SpoilsLog.LogVerbose("FOOD", party, SpoilsLog.Describe(party) + " " + SpoilsLog.Describe(element.Character) + " x" + element.Number
                    + " in " + settlement.Name + ": bought " + bought + " of " + wanted + " food for "
                    + spent + " spoils (would pay up to " + ceiling + " an item)"
                    + ", fed " + ((fedFromHour + fedHours - NowHours) / 24f).ToString("0.0") + " days"
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
        private static void BuyFromStalls(Settlement settlement, ItemRoster market, List<FoodStall> stalls, int ceiling, int budget, int wanted, ref int spent, ref int bought, List<(ItemObject Item, int Count)> purchases = null)
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
                // Paid on what actually came off, so the stack is never charged for food it did not get.
                take = RosterStock.Take(market, stall.Element, take);
                if (take <= 0)
                {
                    stall.Available = 0;
                    stalls[i] = stall;
                    continue;
                }
                stall.Available -= take;
                stalls[i] = stall;
                bought += take;
                spent += take * stall.UnitSpoils;
                TroopMarketFeedback.RegisterPurchase(settlement, stall.Item.ItemCategory, take * stall.UnitSpoils);
                purchases?.Add((stall.Item, take));
            }
        }
    }
}
