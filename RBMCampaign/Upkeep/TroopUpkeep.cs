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
    ///
    /// Split across two files: TroopUpkeep.cs holds the shared state, the carousing, and the food
    /// consumption patch; TroopUpkeep.Food.cs holds the buying of rations off a settlement's market.
    /// </remarks>
    public static partial class TroopUpkeep
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
        /// Taverns, dice and worse. A stack spends against what it earns in a day for every day it
        /// idles in a settlement -- more than it earns, at the default, so an idle garrison town eats
        /// the savings its men marched in with. A stack with an empty purse spends nothing: the pool
        /// is never driven negative, so carousing cannot put a soldier in debt.
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
                    * RBMConfig.RBMConfig.troopSettlementFunWageFraction));
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
                SpoilsLog.LogOnce("fun-" + settlement.StringId + "-" + (NowHours / 24), "FUN", party,
                    SpoilsLog.Describe(party) + " carousing in " + settlement.Name
                    + ": " + spentTotal + " spoils this hour");
            }
        }

        /// <summary>Coin spent in a settlement stays there. A point of spoils is a gold piece.</summary>
        private static void CreditSettlement(Settlement settlement, int spoilsSpent)
        {
            float gain = spoilsSpent * RBMConfig.RBMConfig.settlementProsperityPerGoldSpent;
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
}
