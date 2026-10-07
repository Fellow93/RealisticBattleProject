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
    /// Split across files: TroopUpkeep.cs holds the shared state, the carousing, and the food
    /// consumption patch; TroopUpkeep.Food.cs holds the buying of rations off a settlement's market;
    /// TroopUpkeep.FoodForecast.cs holds the days-of-food estimate that accounts for rations lapsing.
    /// </remarks>
    public static partial class TroopUpkeep
    {
        // Keyed the same way as the spoils pool, since it is the same granularity: one entry per
        // stack, saved flat as partyId#charId. The hour the stack's men run out of the food they last bought.
        private static readonly SpoilsPool.StackStore _fedUntilHours = new SpoilsPool.StackStore();

        // Same key, same granularity: the hour before which a stack that has just splurged on a luxury
        // will not splurge again, so the indulgence stays an occasional treat rather than a daily habit.
        private static readonly SpoilsPool.StackStore _luxuryCooldownUntilHours = new SpoilsPool.StackStore();

        /// <summary>
        /// Drops the previous campaign's rations and cooldowns. Called from
        /// <see cref="RBMTroopUpkeepCampaignBehavior"/>'s CONSTRUCTOR for the same reason
        /// <see cref="SpoilsPool.Reset"/> is: it is the only hook that runs before the save is read.
        /// </summary>
        public static void Reset()
        {
            _fedUntilHours.Clear();
            _luxuryCooldownUntilHours.Clear();
            ResetUnfedProfiles();
        }

        public static void SyncData(IDataStore dataStore)
        {
            _fedUntilHours.Sync(dataStore, "RBM_troopFedUntilHours");
            _luxuryCooldownUntilHours.Sync(dataStore, "RBM_troopLuxuryCooldown");
            if (!dataStore.IsSaving)
            {
                // The rosters and the ration store were both just replaced; no profile built before can stand.
                ResetUnfedProfiles();
            }
            SpoilsLog.Log("SAVE", (dataStore.IsSaving ? "saved " : "loaded ") + _fedUntilHours.Count + " fed-until entries");
        }

        private static int NowHours
        {
            get { return (int)CampaignTime.Now.ToHours; }
        }

        /// <summary>The most of a stack's over-cap surplus that carousing can spend in a single hour.</summary>
        /// <remarks>
        /// Two per cent, down from twenty-five. This is an HOURLY rate applied multiplicatively, which
        /// is far harsher than it reads: a quarter an hour is <c>0.75^24</c>, so 99.9% of a surplus was
        /// gone inside one day. Loot and plunder never accumulated at all -- they arrived and were drunk
        /// before the next dawn, which is what made carousing spike after every battle and made it 98%
        /// of everything entering a town.
        ///
        /// At two per cent a surplus sheds about 38% over a day and takes the better part of a week to
        /// clear. That is still a stack spending down its winnings noticeably faster than its wage, but
        /// over the span a soldier would plausibly do it.
        /// </remarks>
        private const float MaxSurplusFunFractionPerHour = 0.02f;

        /// <summary>
        /// The ceiling on what one man can spend on fun in a day, per tier of his own: a recruit
        /// cannot drink like a veteran however deep his purse. Multiplied by tier + 1, so the lowest
        /// tier still has a floor rather than a ceiling of nothing.
        /// </summary>
        /// <remarks>
        /// The fraction-of-surplus limit above is a rate, not a bound: a purse far enough over its cap
        /// still shed a fortune per hour, because a share of a large enough number is a large number.
        /// One stack was measured drinking 110,289 in a single hour, which is not a soldier spending his
        /// pay but an accounting sink wearing the costume of one -- and once carousing began paying the
        /// town it stopped being an internal matter.
        ///
        /// Twenty-five, down from two hundred. At two hundred it was not a ceiling at all: a man of
        /// tier t was allowed 200(t+1) a day against a wage of roughly 50t, so it sat four times over
        /// anything the wage leg could produce and only ever bound in the extreme. A 200-man garrison of
        /// tier 3 was licensed 160,000 denars a day. Sized now to sit just above the wage-driven rate
        /// rather than multiples over it, so it actually bounds the surplus drain -- which is the job it
        /// was added for.
        /// </remarks>
        private const int MaxFunPerManPerDayPerTier = 25;

        /// <summary>Men per food item per day: the rate the game's own consumption model eats at.</summary>
        private static int MenPerFoodPerDay
        {
            get { return Campaign.Current.Models.MobilePartyFoodConsumptionModel.NumberOfMenOnMapToEatOneFood; }
        }

        public static bool IsFed(PartyBase party, CharacterObject character)
        {
            int fedUntil;
            return _fedUntilHours.TryGetValue(party, character, out fedUntil)
                && fedUntil > (_forecastHours ?? NowHours);
        }

        /// <summary>
        /// The share of a party's mouths that still eat out of its stores. Men carrying food they
        /// bought for themselves do not, so a party whose stacks are all provisioned consumes nothing.
        /// Heroes never buy their own rations and always count as unfed.
        /// </summary>
        /// <remarks>
        /// Asked on every read of a party's food change -- the AI's hourly planning for every party and
        /// army member, the forecast below once per ration lapse, the map bar several times a second --
        /// so the per-stack walk is done once into an <see cref="UnfedProfile"/> and reused until the
        /// roster or the party's rations change. Judging it at an hour is then a sum over the provisioned
        /// stacks alone, with the same integers the walk would have produced.
        /// </remarks>
        public static float GetUnfedManFraction(MobileParty mobileParty)
        {
            PartyBase party = mobileParty?.Party;
            if (party == null || party.MemberRoster == null)
            {
                return 1f;
            }
            UnfedProfile profile = GetUnfedProfile(party);
            int fedMen = 0;
            if (profile.FedUntil.Length > 0)
            {
                // The clock IsFed would judge each stack by, read once: it cannot move within this call.
                int hour = _forecastHours ?? NowHours;
                for (int i = 0; i < profile.FedUntil.Length; i++)
                {
                    if (profile.FedUntil[i] > hour)
                    {
                        fedMen += profile.Men[i];
                    }
                }
            }
            int unfed = profile.Total - fedMen;
            return profile.Total <= 0 ? 1f : (float)unfed / profile.Total;
        }

        /// <summary>
        /// One party's stacks as the unfed fraction sees them, independent of the hour: every man on the
        /// roster, and the non-hero stacks holding a ration entry with the hour it lapses and their size.
        /// </summary>
        /// <remarks>
        /// Its inputs are exactly the roster's characters and counts (both change only through the
        /// roster's own mutators, every one of which bumps <c>TroopRoster.VersionNo</c>), whether each
        /// character is a hero (fixed for a CharacterObject), and the party's ration entries (every change
        /// to which re-stamps the party's bucket, see <see cref="SpoilsPool.StackStore"/>). The roster
        /// instance is held too, so a profile is never matched against a different roster that happens to
        /// share a version number. Immutable once built, so a reader on any thread sees a whole one.
        /// </remarks>
        private sealed class UnfedProfile
        {
            internal static readonly int[] None = new int[0];

            internal TroopRoster Roster;
            internal int RosterVersion;
            internal int FedStamp;
            internal int Total;
            internal int[] FedUntil = None;
            internal int[] Men = None;
        }

        private sealed class UnfedProfileHolder
        {
            internal volatile UnfedProfile Current;
        }

        // Weakly keyed, so a destroyed party's profile goes with it; thread-safe, so a food-change read
        // off the main thread cannot corrupt it. Replaced wholesale on reset and load.
        private static System.Runtime.CompilerServices.ConditionalWeakTable<PartyBase, UnfedProfileHolder> _unfedProfiles =
            new System.Runtime.CompilerServices.ConditionalWeakTable<PartyBase, UnfedProfileHolder>();

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PartyBase, UnfedProfileHolder>.CreateValueCallback _newProfileHolder =
            party => new UnfedProfileHolder();

        private static void ResetUnfedProfiles()
        {
            _unfedProfiles = new System.Runtime.CompilerServices.ConditionalWeakTable<PartyBase, UnfedProfileHolder>();
        }

        private static UnfedProfile GetUnfedProfile(PartyBase party)
        {
            TroopRoster roster = party.MemberRoster;
            int version = roster.VersionNo;
            int stamp = _fedUntilHours.GetStamp(party);
            UnfedProfileHolder holder = _unfedProfiles.GetValue(party, _newProfileHolder);
            UnfedProfile profile = holder.Current;
            if (profile != null && profile.Roster == roster && profile.RosterVersion == version && profile.FedStamp == stamp)
            {
                return profile;
            }
            profile = BuildUnfedProfile(party, roster, version, stamp);
            holder.Current = profile;
            return profile;
        }

        private static UnfedProfile BuildUnfedProfile(PartyBase party, TroopRoster roster, int version, int stamp)
        {
            UnfedProfile profile = new UnfedProfile
            {
                Roster = roster,
                RosterVersion = version,
                FedStamp = stamp
            };
            SpoilsPool.StackStore.Bucket bucket = _fedUntilHours.GetBucket(party);
            List<int> fedUntil = null;
            List<int> men = null;
            int total = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                total += element.Number;
                int until;
                if (bucket == null || element.Character.IsHero
                    || !SpoilsPool.StackStore.TryGetValue(bucket, element.Character, out until))
                {
                    continue;
                }
                if (fedUntil == null)
                {
                    fedUntil = new List<int>();
                    men = new List<int>();
                }
                fedUntil.Add(until);
                men.Add(element.Number);
            }
            profile.Total = total;
            if (fedUntil != null)
            {
                profile.FedUntil = fedUntil.ToArray();
                profile.Men = men.ToArray();
            }
            return profile;
        }

        /// <summary>
        /// A garrison never leaves the settlement it holds, and it does not provision itself: the town
        /// feeds it off the market as part of its own rations. Militia are the same, and neither draws
        /// on a party's food stores in the first place. This is for parties that arrive somewhere.
        /// </summary>
        private static bool IsVisitor(MobileParty mobileParty)
        {
            return mobileParty != null && !mobileParty.IsGarrison && !mobileParty.IsMilitia;
        }

        /// <summary>
        /// Who may spend their purse on the town's taverns and stalls.
        ///
        /// This used to be visitors alone, on the grounds that a garrison standing in one place forever
        /// would be a permanent faucet of prosperity fed by nothing. That objection was correct, and it
        /// dissolved the moment the fief began paying a quarter of its own garrison's wages: the coin a
        /// garrison spends is now the town's own money coming back over the counter, not money invented
        /// to hand it. Letting it spend is what closes that loop -- treasury to the men, the men to the
        /// market -- and leaving it shut would simply destroy the wage instead.
        ///
        /// Militia are in on the same terms. They used to be excluded because nobody paid them at all,
        /// which made their purse the very faucet this gate existed to stop; now the settlement pays
        /// them a fifth of a wage out of its own treasury and their purse holds nothing but that, so
        /// their spending is the same short loop as the garrison's -- treasury to the men, the men to
        /// the market -- only smaller. See <see cref="MilitiaUpkeep"/>.
        /// </summary>
        private static bool SpendsLocally(MobileParty mobileParty)
        {
            return mobileParty != null;
        }

        public static void OnSettlementEntered(MobileParty mobileParty, Settlement settlement, Hero hero)
        {
            if (IsVisitor(mobileParty))
            {
                GrantProvisioningXp(mobileParty, BuyFood(mobileParty, settlement));
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
            int provisioned = 0;
            if (IsVisitor(mobileParty))
            {
                provisioned += BuyFood(mobileParty, settlement);
            }
            if (SpendsLocally(mobileParty))
            {
                // Carousing is deliberately left out: drinking pay away is not thrift, so it earns no
                // stewardship. Only the food and the luxuries the men lay in for themselves count.
                SpendOnFun(mobileParty, settlement);
                provisioned += MaybeBuyLuxury(mobileParty, settlement);
            }
            GrantProvisioningXp(mobileParty, provisioned);
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
                int cap = SpoilsPool.GetSpoilsCap(party, element.Character, element.Number);
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

            // A floating word above the settlement when the drink is worth remarking on -- who is
            // carousing here and what it cost them. Only shows while the settlement is inspected; the
            // event is otherwise a cheap no-op walk of the nearby nameplates.
            RBMMapNotifications.RaiseSpoilsDrunk(settlement, mobileParty, spentTotal);

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
                _fedUntilHours.Remove(party, character);
                _luxuryCooldownUntilHours.Remove(party, character);
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
            if (_fedUntilHours.TryGetValue(from, character, out fedUntil))
            {
                int existing;
                _fedUntilHours.TryGetValue(to, character, out existing);
                if (fedUntil > existing)
                {
                    _fedUntilHours.Set(to, character, fedUntil);
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
            // Only this party's own entries, so walk its bucket rather than every ration on the map.
            string partyId = SpoilsPool.StackStore.PartyId(party);
            SpoilsPool.StackStore.Bucket bucket = _fedUntilHours.GetBucket(partyId);
            if (bucket == null)
            {
                return;
            }
            List<string> orphans = null;
            foreach (string charId in bucket.Values.Keys)
            {
                CharacterObject character = MBObjectManager.Instance.GetObject<CharacterObject>(charId);
                if (character == null || party.MemberRoster.FindIndexOfTroop(character) < 0)
                {
                    if (orphans == null)
                    {
                        orphans = new List<string>();
                    }
                    orphans.Add(charId);
                }
            }
            if (orphans != null)
            {
                foreach (string charId in orphans)
                {
                    _fedUntilHours.Remove(partyId, charId);
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
            int fed = 0;
            int luxury = 0;
            foreach (string id in partyIds)
            {
                fed += _fedUntilHours.RemoveParty(id);
                luxury += _luxuryCooldownUntilHours.RemoveParty(id);
            }
            if (fed > 0 || luxury > 0)
            {
                SpoilsLog.Log("POOL", "pruned " + fed + " ration and " + luxury
                    + " luxury entries from exempt (villager) parties");
            }
        }

        public static void OnMobilePartyDestroyed(MobileParty party, PartyBase destroyer)
        {
            // The party's own buckets, dropped whole: no scan of anyone else's rations.
            string partyId = SpoilsPool.StackStore.PartyId(party.Party);
            _fedUntilHours.RemoveParty(partyId);
            _luxuryCooldownUntilHours.RemoveParty(partyId);
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
                // Take the fed men's share off the BASE rather than adding a factor. ExplainedNumber sums its
                // factors, and vanilla then applies the food perks (Spartan -0.2, Warrior's Diet -0.1, ...) as
                // factors on this same number, so a "provisioned" factor of e.g. -0.9 plus -0.3 of perks went
                // past -1, flipped the sign and was clamped to the model's -0.01 floor: a well-provisioned party
                // with those perks ate almost nothing. Shrinking the base lets the perks scale what is left.
                // Only members are covered by the unfed fraction; the prisoners' NumberOfPrisoners/2 share of
                // the base is untouched, so they keep eating from the stores.
                int members = (party?.Party != null) ? party.Party.NumberOfAllMembers : 0;
                if (members > 0)
                {
                    // The base is negative (consumption), so the fed men's portion is added back as a positive.
                    __result.Add(members * (1f - unfed) / MenPerFoodPerDay, _ownRations);
                }
            }
        }
    }
}
