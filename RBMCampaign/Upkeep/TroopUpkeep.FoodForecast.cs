using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// How many days a party's stores will last, counting the men whose own rations run out.
    /// </summary>
    /// <remarks>
    /// Vanilla divides the stores by today's consumption and calls it the answer. That is wrong the
    /// moment <see cref="ProvisionedMenEatTheirOwnFood"/> is in play: a party whose stacks all bought
    /// their own food eats next to nothing out of its stores -- the model's 0.01 a day floor -- so 31
    /// food "lasts" thousands of days, when in truth the rations lapse inside
    /// <c>troopSettlementFoodDays</c> and the whole party is back on the stores after that. The player
    /// saw it on the food tooltip, and the AI read the same figure when deciding whether it had the food
    /// to join an army or sit down to a siege.
    ///
    /// The forecast walks forward over the hours the stacks' rations lapse, eating each stretch at the
    /// rate the party will actually eat during it. That rate is never re-derived here: the clock the fed
    /// check reads is moved forward and the game's own <see cref="MobileParty.FoodChange"/> is asked
    /// again, so perks, prisoners and the floor all come out exactly as the consumption patch applies
    /// them, and the two cannot drift apart. It assumes nobody buys more food on the way, which is the
    /// question being asked -- how long until the stores run out if the party does not resupply.
    /// </remarks>
    public static partial class TroopUpkeep
    {
        // The hour IsFed judges rations against while a forecast is running, instead of now. Null the
        // rest of the time. Thread-static so a forecast on one thread can never make another thread's
        // live consumption read a future clock.
        [System.ThreadStatic]
        private static int? _forecastHours;

        /// <summary>
        /// Days until <paramref name="food"/> runs out for <paramref name="mobileParty"/>, with the
        /// party's own rations lapsing on schedule. Null when no stack is carrying rations (or the
        /// system is off), meaning vanilla's straight division is already the right answer and should
        /// stand untouched.
        /// </summary>
        public static float? ForecastDaysOfFood(MobileParty mobileParty, float food)
        {
            // The same gate as the consumption patch: when it is not shrinking consumption there is
            // nothing to correct.
            if (!SpoilsPool.IsEnabled || RBMConfig.RBMConfig.troopSettlementFoodDays <= 0)
            {
                return null;
            }
            List<int> expiries = GetRationExpiries(mobileParty);
            if (expiries == null)
            {
                return null;
            }

            // Once the last rations are eaten the whole party is on the stores for good. If even that
            // does not draw them down, the food never runs out and vanilla's figure is as good as any.
            float fullRate = -FoodChangeAtHour(mobileParty, expiries[expiries.Count - 1]);
            if (fullRate <= 0f)
            {
                return null;
            }
            if (food <= 0f)
            {
                return 0f;
            }

            float remaining = food;
            float days = 0f;
            int from = NowHours;
            foreach (int until in expiries)
            {
                // The rate for the stretch up to the next lapse, judged at its start: the stacks whose
                // rations outlast it are still off the stores for all of it.
                float rate = -FoodChangeAtHour(mobileParty, from);
                float span = (until - from) / 24f;
                if (rate > 0f)
                {
                    if (rate * span >= remaining)
                    {
                        return days + remaining / rate;
                    }
                    remaining -= rate * span;
                }
                days += span;
                from = until;
            }
            return days + remaining / fullRate;
        }

        /// <summary>
        /// The distinct future hours at which one of the party's stacks runs out of its own rations,
        /// earliest first. Null when none is carrying any. Heroes never buy rations and are skipped the
        /// way <see cref="GetUnfedManFraction"/> skips them.
        /// </summary>
        private static List<int> GetRationExpiries(MobileParty mobileParty)
        {
            PartyBase party = mobileParty?.Party;
            if (party == null || party.MemberRoster == null)
            {
                return null;
            }
            int now = NowHours;
            List<int> expiries = null;
            TroopRoster roster = party.MemberRoster;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                int fedUntil;
                if (element.Character.IsHero
                    || !_fedUntilHours.TryGetValue(SpoilsPool.Key(party, element.Character), out fedUntil)
                    || fedUntil <= now)
                {
                    continue;
                }
                if (expiries == null)
                {
                    expiries = new List<int>();
                }
                // Stacks usually provision together in one settlement, so most share an hour; one
                // stretch per distinct hour keeps the model calls to a handful.
                if (!expiries.Contains(fedUntil))
                {
                    expiries.Add(fedUntil);
                }
            }
            expiries?.Sort();
            return expiries;
        }

        /// <summary>The party's daily food change as it will stand at <paramref name="hours"/>.</summary>
        private static float FoodChangeAtHour(MobileParty mobileParty, int hours)
        {
            int? saved = _forecastHours;
            _forecastHours = hours;
            try
            {
                return mobileParty.FoodChange;
            }
            finally
            {
                _forecastHours = saved;
            }
        }

        /// <summary>
        /// The days-of-food figure the AI plans armies and sieges with, and the map bar and recruitment
        /// screen warn with. Left alone for any party with no rations of its own.
        /// </summary>
        [HarmonyPatch(typeof(MobileParty))]
        [HarmonyPatch("GetNumDaysForFoodToLast")]
        private class ForecastNumDaysForFoodToLast
        {
            private static void Postfix(MobileParty __instance, ref int __result)
            {
                // The same food vanilla counts: the stores, plus the part-eaten item only the main
                // party keeps track of.
                float food = __instance.ItemRoster.TotalFood;
                if (__instance == MobileParty.MainParty)
                {
                    food += __instance.Party.RemainingFoodPercentage * 0.01f;
                }
                float? days = ForecastDaysOfFood(__instance, food);
                if (days.HasValue)
                {
                    // Truncated like vanilla's; clamped so a pathological rate cannot overflow the cast.
                    __result = (int)MathF.Min(days.Value, 1000000f);
                }
            }
        }

        /// <summary>
        /// The main party's food tooltip on the map bar -- the "days until food runs out" the player
        /// actually reads. It divides by the explained food change itself rather than asking
        /// GetNumDaysForFoodToLast, so it needs its own correction.
        /// </summary>
        [HarmonyPatch(typeof(CampaignUIHelper))]
        [HarmonyPatch("GetPartyFoodTooltip")]
        private class ForecastPartyFoodTooltip
        {
            // Only patched once a game's texts are loaded. GetPartyFoodTooltip reads one of
            // CampaignUIHelper's static fields directly, so compiling Harmony's replacement runs the
            // class's static initializer -- and that calls GameTexts.FindText, which throws before
            // Game.Initialize. ApplyHarmonyPatches also runs at module load and on the main menu, and a
            // failed static initializer is never retried: the type stays broken for the whole session
            // and the map screen crashes on the next load. OnGameStart re-patches with the texts in place.
            private static bool Prepare()
            {
                return Game.Current?.GameTextManager != null;
            }

            private static void Postfix(MobileParty mainParty, List<TooltipProperty> __result)
            {
                if (mainParty == null || __result == null)
                {
                    return;
                }
                // The figure the tooltip itself quotes above the line.
                float? days = ForecastDaysOfFood(mainParty, mainParty.Food > 0f ? mainParty.Food : 0f);
                if (!days.HasValue)
                {
                    return;
                }
                // Found by its label rather than position, so a line another mod appends cannot make
                // this overwrite the wrong one. Rounded up, as vanilla's GetDaysUntilNoFood rounds it.
                string label = GameTexts.FindText("str_total_days_until_no_food").ToString();
                for (int i = __result.Count - 1; i >= 0; i--)
                {
                    if (__result[i].DefinitionLabel == label)
                    {
                        __result[i].ValueLabel = MathF.Ceiling(days.Value).ToString();
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// The army food tooltip: one days-left line per party, then their average. Vanilla divides each
        /// party's food by its current change, so a provisioned party reads as near-inexhaustible and
        /// drags the army's average with it.
        /// </summary>
        [HarmonyPatch(typeof(CampaignUIHelper))]
        [HarmonyPatch("GetArmyFoodTooltip")]
        private class ForecastArmyFoodTooltip
        {
            private static void Postfix(Army army, List<TooltipProperty> __result)
            {
                if (army?.LeaderParty == null || __result == null)
                {
                    return;
                }
                // Vanilla lays the per-party lines straight after the first plain separator, in the order
                // of army.Parties, then a rundown separator and the average. Walked in that order and
                // checked by name, so a list shaped any other way is left exactly as vanilla built it.
                int cursor = __result.FindIndex(p => p.PropertyModifier == (int)TooltipProperty.TooltipPropertyFlags.DefaultSeperator);
                if (cursor < 0)
                {
                    return;
                }
                cursor++;
                bool changed = false;
                double totalDays = 0.0;
                foreach (MobileParty party in army.Parties)
                {
                    if (!army.DoesLeaderPartyAndAttachedPartiesContain(party))
                    {
                        continue;
                    }
                    if (cursor >= __result.Count || __result[cursor].DefinitionLabel != party.Name.ToString())
                    {
                        return;
                    }
                    float? days = ForecastDaysOfFood(party, party.Food > 0f ? party.Food : 0f);
                    if (days.HasValue)
                    {
                        __result[cursor].ValueLabel = MathF.Ceiling(days.Value).ToString();
                        totalDays += days.Value;
                        changed = true;
                    }
                    else
                    {
                        // Vanilla's own per-party term for the average.
                        totalDays += MathF.Max(party.Food / -party.FoodChange, 0f);
                    }
                    cursor++;
                }
                // Past the rundown separator, the average line.
                int average = cursor + 1;
                if (changed && average < __result.Count && army.LeaderPartyAndAttachedPartiesCount > 0)
                {
                    __result[average].ValueLabel = MathF.Ceiling(totalDays / army.LeaderPartyAndAttachedPartiesCount).ToString();
                }
            }
        }
    }
}
