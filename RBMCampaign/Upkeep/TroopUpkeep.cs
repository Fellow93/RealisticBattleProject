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
using TaleWorlds.ObjectSystem;

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

        // Same key, same granularity: the hour before which a stack that has just splurged on a luxury
        // will not splurge again, so the indulgence stays an occasional treat rather than a daily habit.
        private static Dictionary<string, int> _luxuryCooldownUntilHours = new Dictionary<string, int>();

        /// <summary>
        /// Drops the previous campaign's rations and cooldowns. Called from
        /// <see cref="RBMTroopUpkeepCampaignBehavior"/>'s CONSTRUCTOR for the same reason
        /// <see cref="SpoilsPool.Reset"/> is: it is the only hook that runs before the save is read.
        /// </summary>
        public static void Reset()
        {
            _fedUntilHours.Clear();
            _luxuryCooldownUntilHours.Clear();
        }

        public static void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("RBM_troopFedUntilHours", ref _fedUntilHours);
            if (_fedUntilHours == null)
            {
                _fedUntilHours = new Dictionary<string, int>();
            }
            dataStore.SyncData("RBM_troopLuxuryCooldown", ref _luxuryCooldownUntilHours);
            if (_luxuryCooldownUntilHours == null)
            {
                _luxuryCooldownUntilHours = new Dictionary<string, int>();
            }
            SpoilsLog.Log("SAVE", (dataStore.IsSaving ? "saved " : "loaded ") + _fedUntilHours.Count + " fed-until entries");
        }

        private static int NowHours
        {
            get { return (int)CampaignTime.Now.ToHours; }
        }

        /// <summary>The most of a stack's over-cap surplus that carousing can spend in a single hour.</summary>
        private const float MaxSurplusFunFractionPerHour = 0.25f;

        /// <summary>
        /// The ceiling on what one man can spend on fun in a day, per tier of his own: a recruit
        /// cannot drink like a veteran however deep his purse. Multiplied by tier + 1, so the lowest
        /// tier still has a floor rather than a ceiling of nothing.
        /// </summary>
        /// <remarks>
        /// The fraction-of-surplus limit above is a rate, not a bound: a purse far enough over its cap
        /// still shed a fortune per hour, because a quarter of a large enough number is a large
        /// number. One stack was measured drinking 110,289 in a single hour, which is not a soldier
        /// spending his pay but an accounting sink wearing the costume of one -- and once carousing
        /// began paying the town it stopped being an internal matter.
        ///
        /// Sized to sit about three times the wage-driven rate a man of that tier drinks at, so a
        /// stack over its cap still spends visibly harder than one saving up, and no stack spends like
        /// a treasury emptying. A man of tier t earns roughly 50t a day and carouses about 75t of it,
        /// against a ceiling of 200(t+1).
        /// </remarks>
        private const int MaxFunPerManPerDayPerTier = 200;

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
            if (settlement == null)
            {
                return;
            }
            // Paid healing draws on a stack's own finite purse and only mends men who are actually down, so
            // it is not the standing faucet of prosperity that free provisioning and carousing would be: a
            // garrison or militia holding a settlement mends its wounded there like any party passing through.
            HealWounded(mobileParty, settlement);
            if (!IsVisitor(mobileParty))
            {
                return;
            }
            BuyFood(mobileParty, settlement);
            SpendOnFun(mobileParty, settlement);
            MaybeBuyLuxury(mobileParty, settlement);
        }

        /// <summary>
        /// Taverns, dice and worse. A stack spends against what it earns in a day for every day it
        /// idles in a settlement -- more than it earns, at the default, so an idle garrison town eats
        /// the savings its men marched in with. A stack whose purse is over its cap has nothing left to
        /// save for, so it blows the excess on fun far faster than its wage alone -- and the further
        /// over the cap it sits, the harder it spends, the surplus bite scaling by how many times over
        /// its ceiling the purse stands. A stack with an empty purse spends nothing: the pool is never
        /// driven negative, so carousing cannot put a soldier in debt.
        ///
        /// Over everything sits a hard per-man ceiling by tier (<see cref="MaxFunPerManPerDayPerTier"/>):
        /// the surplus drain is a rate, and a rate on a large enough purse is still a fortune per hour.
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
                float funFraction = RBMConfig.RBMConfig.troopSettlementFunWageFraction;
                int spend = MathF.Round(dailyWage / 24f * funFraction);
                // Over the cap the men have nothing left to save for, so the excess above it is drunk
                // away on top of the wage bite -- and the further over the cap they sit, the harder they
                // spend: the surplus bite scales by how many times over its ceiling the purse stands, so
                // a purse well over cap empties far faster than one only just above it.
                int cap = SpoilsPool.GetSpoilsCap(party, element.Character);
                int surplus = purse - cap;
                if (surplus > 0)
                {
                    if (cap <= 0)
                    {
                        // Nothing to save for at all: the whole surplus is fair game.
                        spend += surplus;
                    }
                    else
                    {
                        float overRatio = (float)purse / cap;
                        int surplusBite = MathF.Round(surplus / 24f * funFraction * overRatio);
                        // However many times over the cap the purse stands, no more than this share of the
                        // surplus is blown in a single hour, so a stack sitting far over cap still drains
                        // gradually rather than dumping its whole purse into prosperity the hour it arrives.
                        surplusBite = MathF.Min(surplusBite, MathF.Round(surplus * MaxSurplusFunFractionPerHour));
                        spend += surplusBite;
                    }
                }
                // The hard ceiling, applied to wage and surplus together: whatever a man's purse holds
                // and however far over his cap it sits, there is only so much drinking he can do in an
                // hour. This is what bounds the surplus drain, which the fraction above does not.
                int tierCeiling = MathF.Max(1, MaxFunPerManPerDayPerTier * (element.Character.Tier + 1) * element.Number / 24);
                spend = MathF.Min(spend, tierCeiling);
                spend = MathF.Min(purse, spend);
                if (spend <= 0)
                {
                    continue;
                }
                SpoilsPool.AddSpoils(party, element.Character, -spend);
                spentTotal += spend;
            }

            // Once for the party rather than per stack: it is the same taverns taking the coin, and
            // there is no good changing hands whose category the demand could belong to.
            TroopMarketFeedback.RegisterServiceSpend(settlement, spentTotal);

            if (spentTotal > 0 && SpoilsLog.IsEnabled)
            {
                // Hourly, so once a day per party per settlement is enough to see the rate without
                // flooding -- the party is in the key so stacks from different parties do not collide.
                SpoilsLog.LogOnce("fun-" + party.Id + "-" + settlement.StringId + "-" + (NowHours / 24), "FUN", party,
                    SpoilsLog.Describe(party) + " carousing in " + settlement.Name
                    + ": " + spentTotal + " spoils this hour");
            }
        }

        /// <summary>A stack's rations die with the stack, the way its spoils do.</summary>
        public static void ClearIfStackGone(PartyBase party, CharacterObject character)
        {
            if (party.MemberRoster.FindIndexOfTroop(character) < 0)
            {
                string key = SpoilsPool.Key(party, character);
                _fedUntilHours.Remove(key);
                _luxuryCooldownUntilHours.Remove(key);
            }
        }

        /// <summary>
        /// Rations move with the men when they transfer to another party, the way their purse does. The
        /// receiving stack keeps the later of the two provisionings, and the source's entry is dropped if
        /// its men all marched off. Paired with <see cref="SpoilsPool.TransferSpoils"/>.
        /// </summary>
        public static void TransferFedState(PartyBase from, PartyBase to, CharacterObject character)
        {
            int fedUntil;
            if (_fedUntilHours.TryGetValue(SpoilsPool.Key(from, character), out fedUntil))
            {
                int existing;
                _fedUntilHours.TryGetValue(SpoilsPool.Key(to, character), out existing);
                if (fedUntil > existing)
                {
                    _fedUntilHours[SpoilsPool.Key(to, character)] = fedUntil;
                }
            }
            ClearIfStackGone(from, character);
        }

        /// <summary>
        /// Drops ration entries for stacks that have left <paramref name="party"/> by a path that never
        /// cleared them, the food-side twin of <see cref="SpoilsPool.PruneOrphans"/>.
        /// </summary>
        public static void PruneOrphans(PartyBase party)
        {
            if (party == null || party.MemberRoster == null)
            {
                return;
            }
            string prefix = party.Id + "#";
            List<string> orphans = null;
            foreach (string key in _fedUntilHours.Keys)
            {
                if (!key.StartsWith(prefix))
                {
                    continue;
                }
                string charId = key.Substring(prefix.Length);
                CharacterObject character = MBObjectManager.Instance.GetObject<CharacterObject>(charId);
                if (character == null || party.MemberRoster.FindIndexOfTroop(character) < 0)
                {
                    if (orphans == null)
                    {
                        orphans = new List<string>();
                    }
                    orphans.Add(key);
                }
            }
            if (orphans != null)
            {
                foreach (string key in orphans)
                {
                    _fedUntilHours.Remove(key);
                }
            }
        }

        /// <summary>
        /// Drops the ration and luxury-cooldown entries of parties now exempt from the system, the
        /// food-side twin of <see cref="SpoilsPool.PruneExemptParties"/>. Called with the exempt party
        /// ids the spoils sweep already gathered, so the party list is walked once for both stores.
        /// </summary>
        public static void PruneExemptParties(HashSet<string> partyIds)
        {
            int fed = SpoilsPool.RemoveEntriesForParties(_fedUntilHours, partyIds);
            int luxury = SpoilsPool.RemoveEntriesForParties(_luxuryCooldownUntilHours, partyIds);
            if (fed > 0 || luxury > 0)
            {
                SpoilsLog.Log("POOL", "pruned " + fed + " ration and " + luxury
                    + " luxury entries from exempt (villager) parties");
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
            stale.Clear();
            foreach (string key in _luxuryCooldownUntilHours.Keys)
            {
                if (SpoilsPool.KeyBelongsToParty(key, party.Party))
                {
                    stale.Add(key);
                }
            }
            foreach (string key in stale)
            {
                _luxuryCooldownUntilHours.Remove(key);
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
                // The base is negative, so a negative factor shrinks how much is eaten. The unfed fraction
                // is measured over the member roster only, but the base also feeds the party's prisoners
                // (NumberOfPrisoners/2, see DefaultMobilePartyFoodConsumptionModel). Scaling the whole base
                // by the members' provisioning would stop feeding the prisoners too, so their portion is
                // added back at the share the factor just removed from it.
                __result.AddFactor(unfed - 1f, _ownRations);
                int prisonerFood = (party?.Party != null) ? party.Party.NumberOfPrisoners / 2 : 0;
                if (prisonerFood > 0)
                {
                    // Prisoners eat regardless of how the soldiers are provisioned. AddFactor multiplied the
                    // base -- prisoners included -- by unfed, so restore prisonerBase * (1 - unfed), the part
                    // of their ration the members' factor wrongly cancelled. Negative: it is consumption.
                    float prisonerBase = -(float)prisonerFood / MenPerFoodPerDay;
                    __result.Add(prisonerBase * (1f - unfed), _ownRations);
                }
            }
        }
    }
}
