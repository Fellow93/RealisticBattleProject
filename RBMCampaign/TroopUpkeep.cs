using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// What a soldier does with his spoils when he reaches a settlement. He buys food off the market
    /// and eats it out of his own purse rather than the party's stores, and he drinks and gambles the
    /// rest away. Both leave their mark on the place he spent it in.
    /// </summary>
    /// <remarks>
    /// Spoils is the stack's purse, not just its kit stockpile: wage flows in uncapped, and an
    /// upgrade is only one of the things it can be spent on. A garrison that sits in a town long
    /// enough will drink its way out of ever affording better armour.
    /// </remarks>
    public static class TroopUpkeep
    {
        // Keyed the same way as the spoils pool, since it is the same granularity: one entry per
        // stack. The hour the stack's men run out of the food they last bought.
        private static Dictionary<string, int> _fedUntilHours = new Dictionary<string, int>();

        public static void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("RBM_troopFedUntilHours", ref _fedUntilHours);
            if (_fedUntilHours == null)
            {
                _fedUntilHours = new Dictionary<string, int>();
            }
            SpoilsLog.Log("SAVE", (dataStore.IsSaving ? "saved " : "loaded ") + _fedUntilHours.Count + " fed-until entries");
        }

        private static int NowHours
        {
            get { return (int)CampaignTime.Now.ToHours; }
        }

        /// <summary>Men per food item per day: the rate the game's own consumption model eats at.</summary>
        private static int MenPerFoodPerDay
        {
            get { return Campaign.Current.Models.MobilePartyFoodConsumptionModel.NumberOfMenOnMapToEatOneFood; }
        }

        public static bool IsFed(PartyBase party, CharacterObject character)
        {
            int fedUntil;
            return _fedUntilHours.TryGetValue(SpoilsPool.Key(party, character), out fedUntil) && fedUntil > NowHours;
        }

        /// <summary>
        /// The share of a party's mouths that still eat out of its stores. Men carrying food they
        /// bought for themselves do not, so a party whose stacks are all provisioned consumes nothing.
        /// Heroes never buy their own rations and always count as unfed.
        /// </summary>
        public static float GetUnfedManFraction(MobileParty mobileParty)
        {
            PartyBase party = mobileParty?.Party;
            if (party == null || party.MemberRoster == null)
            {
                return 1f;
            }
            int total = 0;
            int unfed = 0;
            TroopRoster roster = party.MemberRoster;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                total += element.Number;
                if (element.Character.IsHero || !IsFed(party, element.Character))
                {
                    unfed += element.Number;
                }
            }
            return total <= 0 ? 1f : (float)unfed / total;
        }

        /// <summary>
        /// A garrison never leaves the settlement it holds, so it would provision and carouse in it
        /// forever, standing as a permanent faucet of prosperity fed by nothing. Militia are the same,
        /// and neither draws on a party's food stores in the first place. This is for parties that
        /// arrive somewhere.
        /// </summary>
        private static bool IsVisitor(MobileParty mobileParty)
        {
            return mobileParty != null && !mobileParty.IsGarrison && !mobileParty.IsMilitia;
        }

        public static void OnSettlementEntered(MobileParty mobileParty, Settlement settlement, Hero hero)
        {
            if (IsVisitor(mobileParty))
            {
                BuyFood(mobileParty, settlement);
            }
        }

        /// <summary>
        /// Carousing is paid for by the hour, so a party that stops for a night leaves less behind
        /// than one that winters in the place. Provisioning is not: a stack buys food once and buys no
        /// more until it has eaten what it bought, however long it stays.
        /// </summary>
        public static void OnHourlyTickParty(MobileParty mobileParty)
        {
            Settlement settlement = mobileParty?.CurrentSettlement;
            if (settlement == null || !IsVisitor(mobileParty))
            {
                return;
            }
            BuyFood(mobileParty, settlement);
            SpendOnFun(mobileParty, settlement);
        }

        /// <summary>
        /// Every man who has eaten through his last purchase buys enough to carry him the configured
        /// span, at the rate the party would have eaten it out of the stores. He buys only what the
        /// market has and his purse can reach, so a stack that empties a starving village's stalls
        /// goes hungry sooner.
        /// </summary>
        private static void BuyFood(MobileParty mobileParty, Settlement settlement)
        {
            if (!SpoilsPool.IsEnabled || RBMConfig.RBMConfig.troopSettlementFoodDays <= 0)
            {
                return;
            }
            PartyBase party = mobileParty.Party;
            ItemRoster market = settlement.ItemRoster;
            if (party?.MemberRoster == null || market == null)
            {
                return;
            }

            int foodDays = RBMConfig.RBMConfig.troopSettlementFoodDays;
            TroopRoster roster = party.MemberRoster;
            List<FoodStall> stalls = null;
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
                stalls = stalls ?? SnapshotFoodStalls(market);
                FeedStack(party, settlement, market, stalls, element, wanted, foodDays);
            }
        }

        /// <summary>One kind of food on sale: what it is, what a unit costs, how much is left.</summary>
        private struct FoodStall
        {
            public ItemObject Item;
            public int UnitSpoils;
            public int Available;
        }

        private static List<FoodStall> SnapshotFoodStalls(ItemRoster market)
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
                    UnitSpoils = MathF.Max(1, MathF.Round(market.GetElementUnitCost(i) * SpoilsPool.SpoilsPerGold)),
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
            return MathF.Round(dailyWage * RBMConfig.RBMConfig.troopFoodWageFraction
                * SpoilsPool.SpoilsPerGold * MenPerFoodPerDay);
        }

        private static void FeedStack(PartyBase party, Settlement settlement, ItemRoster market, List<FoodStall> stalls, TroopRosterElement element, int wanted, int foodDays)
        {
            int budget = SpoilsPool.GetSpoils(party, element.Character);
            int spent = 0;
            int bought = 0;

            // The best fare he will pay for, then anything at all rather than go hungry. Both passes
            // walk from dearest to cheapest, so within what he can afford he always eats the best of it.
            int ceiling = GetFoodPriceCeiling(element.Character);
            BuyFromStalls(market, stalls, ceiling, budget, wanted, ref spent, ref bought);
            BuyFromStalls(market, stalls, int.MaxValue, budget, wanted, ref spent, ref bought);

            if (bought <= 0)
            {
                return;
            }

            // Half the food buys half the days. A stack that could only part-provision itself comes
            // back to the market sooner rather than eating a full ration out of an empty sack.
            int fedHours = MathF.Max(1, foodDays * 24 * bought / wanted);
            _fedUntilHours[SpoilsPool.Key(party, element.Character)] = NowHours + fedHours;
            SpoilsPool.AddSpoils(party, element.Character, -spent);
            CreditSettlement(settlement, spent);

            if (SpoilsLog.IsEnabled && party == PartyBase.MainParty)
            {
                SpoilsLog.Log("FOOD", SpoilsLog.Describe(element.Character) + " x" + element.Number
                    + " in " + settlement.Name + ": bought " + bought + " of " + wanted + " food for "
                    + spent + " spoils (would pay up to " + ceiling + " an item)"
                    + ", fed " + (fedHours / 24f).ToString("0.0") + " days"
                    + " (pool " + (SpoilsPool.GetSpoils(party, element.Character) + spent)
                    + " -> " + SpoilsPool.GetSpoils(party, element.Character) + ")");
            }
        }

        /// <summary>
        /// Takes what the stack can pay for off the stalls, dearest first, skipping anything above
        /// <paramref name="ceiling"/>. The stalls are drawn down in place so the next stack to buy
        /// sees what this one left, and the market roster is emptied to match.
        /// </summary>
        private static void BuyFromStalls(ItemRoster market, List<FoodStall> stalls, int ceiling, int budget, int wanted, ref int spent, ref int bought)
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
            }
        }

        /// <summary>
        /// Taverns, dice and worse. A stack spends a share of what it earns in a day for every day it
        /// idles in a settlement, and a stack with an empty purse spends nothing -- the pool is never
        /// driven negative, so carousing cannot put a soldier in debt.
        /// </summary>
        private static void SpendOnFun(MobileParty mobileParty, Settlement settlement)
        {
            if (!SpoilsPool.IsEnabled || RBMConfig.RBMConfig.troopSettlementFunWageFraction <= 0f)
            {
                return;
            }
            PartyBase party = mobileParty.Party;
            PartyWageModel wageModel = Campaign.Current.Models.PartyWageModel;
            TroopRoster roster = party.MemberRoster;
            int spentTotal = 0;

            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character.IsHero)
                {
                    continue;
                }
                int purse = SpoilsPool.GetSpoils(party, element.Character);
                if (purse <= 0)
                {
                    continue;
                }
                // An hour's worth of the day's wage. Veterans earn more and so drink better.
                float dailyWage = wageModel.GetCharacterWage(element.Character) * element.Number;
                int spend = MathF.Min(purse, MathF.Round(dailyWage / 24f
                    * RBMConfig.RBMConfig.troopSettlementFunWageFraction * SpoilsPool.SpoilsPerGold));
                if (spend <= 0)
                {
                    continue;
                }
                SpoilsPool.AddSpoils(party, element.Character, -spend);
                spentTotal += spend;
            }

            if (spentTotal > 0)
            {
                CreditSettlement(settlement, spentTotal);
            }
            if (spentTotal > 0 && SpoilsLog.IsEnabled && party == PartyBase.MainParty)
            {
                // Hourly, so once a day per settlement is enough to see the rate without flooding.
                SpoilsLog.LogOnce("fun-" + settlement.StringId + "-" + (NowHours / 24), "FUN",
                    SpoilsLog.Describe(party) + " carousing in " + settlement.Name
                    + ": " + spentTotal + " spoils this hour");
            }
        }

        /// <summary>
        /// Coin spent in a settlement stays there. Spoils are worth more than the gold they stand for,
        /// so the spend is converted back before the town is credited, or a place would grow rich in
        /// proportion to how steeply this mod discounts upgrades.
        /// </summary>
        private static void CreditSettlement(Settlement settlement, int spoilsSpent)
        {
            float gold = spoilsSpent / SpoilsPool.SpoilsPerGold;
            float gain = gold * RBMConfig.RBMConfig.settlementProsperityPerGoldSpent;
            if (gain <= 0f)
            {
                return;
            }
            if (settlement.Town != null)
            {
                settlement.Town.Prosperity += gain;
            }
            else if (settlement.Village != null)
            {
                settlement.Village.Hearth += gain;
            }
        }

        /// <summary>A stack's rations die with the stack, the way its spoils do.</summary>
        public static void ClearIfStackGone(PartyBase party, CharacterObject character)
        {
            if (party.MemberRoster.FindIndexOfTroop(character) < 0)
            {
                _fedUntilHours.Remove(SpoilsPool.Key(party, character));
            }
        }

        public static void OnMobilePartyDestroyed(MobileParty party, PartyBase destroyer)
        {
            List<string> stale = new List<string>();
            foreach (string key in _fedUntilHours.Keys)
            {
                if (SpoilsPool.KeyBelongsToParty(key, party.Party))
                {
                    stale.Add(key);
                }
            }
            foreach (string key in stale)
            {
                _fedUntilHours.Remove(key);
            }
        }

        /// <summary>
        /// The party eats only for the men who are not carrying rations of their own. Patched on the
        /// base consumption rather than the final figure so the vanilla perks still apply, and so the
        /// floor the model puts under a party's consumption still holds.
        /// </summary>
        [HarmonyPatch(typeof(DefaultMobilePartyFoodConsumptionModel))]
        [HarmonyPatch("CalculateDailyBaseFoodConsumptionf")]
        private class ProvisionedMenEatTheirOwnFood
        {
            private static readonly TextObject _ownRations = new TextObject("{=RBM_SPOILS_011}Provisioned from their own purse");

            private static void Postfix(MobileParty party, ref ExplainedNumber __result)
            {
                if (!SpoilsPool.IsEnabled || RBMConfig.RBMConfig.troopSettlementFoodDays <= 0)
                {
                    return;
                }
                float unfed = GetUnfedManFraction(party);
                if (unfed >= 1f)
                {
                    return;
                }
                // The base is negative, so a negative factor shrinks how much is eaten.
                __result.AddFactor(unfed - 1f, _ownRations);
            }
        }
    }

    public class RBMTroopUpkeepCampaignBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, TroopUpkeep.OnSettlementEntered);
            CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, TroopUpkeep.OnHourlyTickParty);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, TroopUpkeep.OnMobilePartyDestroyed);
        }

        public override void SyncData(IDataStore dataStore)
        {
            TroopUpkeep.SyncData(dataStore);
        }
    }
}
