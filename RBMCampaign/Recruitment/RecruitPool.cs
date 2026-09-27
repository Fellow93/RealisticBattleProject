using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// A settlement's MANPOWER -- the men of fighting age it can spare, as a finite pool that refills
    /// slowly. Every NEW soldier the settlement produces draws one man from it: a notable's volunteer
    /// slot filled with a fresh recruit, or a man armed into the garrison. Nothing else touches it --
    /// men moved between a garrison and a lord, prisoners, the defence muster arming volunteers who
    /// already exist, and the militia (armed locals, not soldiers) are not new men.
    ///
    /// Sized off the settlement's population: Prosperity for a town or a castle, Hearth for a village.
    /// <list type="bullet">
    /// <item>GROWTH -- <see cref="DailyGrowthPerPoint"/> a day per point.</item>
    /// <item>CEILING -- <see cref="MaxPerPoint"/> per point.</item>
    /// <item>RESERVE -- the garrison may only recruit while the pool stands above
    /// <see cref="GarrisonReservePerPoint"/> per point, so there are always some men left to come
    /// forward as volunteers. Volunteers may spend the pool down to nothing.</item>
    /// <item>DIMINISHING RETURNS -- a volunteer costs 1, but a garrison man costs more the larger the garrison
    /// already is (<see cref="GetGarrisonManCost"/>).</item>
    /// </list>
    /// A garrison already larger than this would allow is not shrunk -- it simply stops growing.
    /// </summary>
    public static class RecruitPool
    {
        /// <summary>Men a day the pool regains, per point of prosperity (fortification) or hearth (village).</summary>
        public const float DailyGrowthPerPoint = 0.03f;

        /// <summary>Most men the pool holds, per point of prosperity or hearth.</summary>
        public const float MaxPerPoint = 0.2f;

        /// <summary>Men per point the garrison leaves in the pool for volunteers -- it does not recruit below this.</summary>
        public const float GarrisonReservePerPoint = 0.07f;

        /// <summary>
        /// Diminishing returns on garrison size: a new garrison man costs 1 + (garrison / this)² from the pool,
        /// so each recruit costs 2 at 150 men, 5 at 300, 10 at 450. Growth slows steeply as the garrison swells
        /// rather than stopping at a hard cap.
        /// </summary>
        public const float GarrisonCostSoftSize = 150f;

        /// <summary>
        /// The same for a castle, smaller: a castle has no notables drawing volunteers from its pool and a
        /// higher prosperity scale than a town, so without this its whole, faster refill went to the garrison.
        /// Costs 2 at 100 men, 5 at 200, 10 at 300.
        /// </summary>
        public const float CastleGarrisonCostSoftSize = 100f;

        // Settlement.StringId -> men in the pool. Persisted; reset in the behaviour's constructor (see
        // RBMRecruitPoolCampaignBehavior). A settlement with no entry -- a new game, or a save made before
        // this existed -- starts full.
        private static Dictionary<string, float> _pool = new Dictionary<string, float>();

        // The day's tallies for the one summary line in the economy log. Not persisted.
        private static int _dayVolunteers;
        private static int _dayVolunteersRefused;
        private static int _dayGarrison;

        public static bool IsEnabled
        {
            get { return RBMConfig.RBMConfig.rbmCampaignEnabled; }
        }

        public static void Reset()
        {
            _pool = new Dictionary<string, float>();
            _dayVolunteers = 0;
            _dayVolunteersRefused = 0;
            _dayGarrison = 0;
        }

        public static void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("RBM_settlementRecruitPool", ref _pool);
            if (_pool == null)
            {
                _pool = new Dictionary<string, float>();
            }
        }

        /// <summary>The population figure the pool is sized off: prosperity for a town or castle, hearth for a village.</summary>
        private static float BaseOf(Settlement settlement)
        {
            if (settlement == null)
            {
                return 0f;
            }
            if ((settlement.IsTown || settlement.IsCastle) && settlement.Town != null)
            {
                return Math.Max(0f, settlement.Town.Prosperity);
            }
            if (settlement.IsVillage && settlement.Village != null)
            {
                return Math.Max(0f, settlement.Village.Hearth);
            }
            return 0f;
        }

        // One method each for growth and ceiling, so a building's multiplier has a single place to go.

        /// <summary>
        /// Building share of the day's refill, as a fraction: a fortification's manpower daily project, or for
        /// a village its bound town or castle's Roads and Paths. See <see cref="BuildingEffects"/>.
        /// </summary>
        public static float GetGrowthBonus(Settlement settlement)
        {
            if (settlement == null)
            {
                return 0f;
            }
            if (settlement.IsVillage)
            {
                Settlement bound = settlement.Village?.Bound;
                return BuildingEffects.RoadsVillageRecruitGrowthBonus(bound?.Town);
            }
            return BuildingEffects.RecruitPoolDailyProjectGrowthBonus(settlement.Town);
        }

        /// <summary>Building share of the ceiling, as a fraction: a castle's Castellan's Office.</summary>
        public static float GetMaxBonus(Settlement settlement)
        {
            if (settlement == null || !settlement.IsCastle)
            {
                return 0f;
            }
            return BuildingEffects.CastellanRecruitPoolMaxBonus(settlement.Town);
        }

        /// <summary>Men the settlement's pool regains today.</summary>
        public static float GetDailyGrowth(Settlement settlement)
        {
            return BaseOf(settlement) * DailyGrowthPerPoint * (1f + GetGrowthBonus(settlement));
        }

        /// <summary>Most men the settlement's pool can hold.</summary>
        public static float GetMax(Settlement settlement)
        {
            return BaseOf(settlement) * MaxPerPoint * (1f + GetMaxBonus(settlement));
        }

        /// <summary>The garrison size at which a new man costs the pool double: 150 town / 100 castle, plus Barracks.</summary>
        public static float GetGarrisonSoftSize(Settlement settlement)
        {
            float soft = settlement != null && settlement.IsCastle ? CastleGarrisonCostSoftSize : GarrisonCostSoftSize;
            return soft + BuildingEffects.BarracksGarrisonSoftSizeBonus(settlement?.Town);
        }

        /// <summary>The floor garrison recruitment will not take the pool below.</summary>
        public static float GetGarrisonReserve(Settlement settlement)
        {
            return BaseOf(settlement) * GarrisonReservePerPoint;
        }

        /// <summary>
        /// Men in the settlement's pool, never above its present ceiling (prosperity can fall under a
        /// full pool). A settlement seen for the first time starts full.
        /// </summary>
        public static float Get(Settlement settlement)
        {
            if (settlement == null)
            {
                return 0f;
            }
            float max = GetMax(settlement);
            float value;
            if (!_pool.TryGetValue(settlement.StringId, out value) || value > max)
            {
                value = max;
                _pool[settlement.StringId] = value;
            }
            return value;
        }

        /// <summary>
        /// Pool points one new garrison man costs at the garrison's present size: 1 + (men / soft size)²,
        /// soft size 150 for a town and 100 for a castle, plus Barracks (see <see cref="GetGarrisonSoftSize"/>).
        /// A settlement with no garrison yet pays 1.
        /// </summary>
        public static float GetGarrisonManCost(Settlement settlement)
        {
            int men = settlement?.Town?.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;
            float ratio = men / GetGarrisonSoftSize(settlement);
            return 1f + ratio * ratio;
        }

        /// <summary>
        /// Whole men the garrison may still take today without breaking the volunteer reserve, at the
        /// present per-man cost. The cost rises as men join, so the spawn loop re-checks this man by man.
        /// </summary>
        public static int GarrisonAvailable(Settlement settlement)
        {
            if (!IsEnabled)
            {
                return int.MaxValue;
            }
            int n = (int)Math.Floor((Get(settlement) - GetGarrisonReserve(settlement)) / GetGarrisonManCost(settlement));
            return n > 0 ? n : 0;
        }

        /// <summary>Whole men left in the pool to step forward as volunteers.</summary>
        private static int VolunteerAvailable(Settlement settlement)
        {
            int n = (int)Math.Floor(Get(settlement));
            return n > 0 ? n : 0;
        }

        /// <summary>The pool as the UI shows it: "current/max (+growth/day)", whole men and one decimal of growth.</summary>
        public static string FormatPool(Settlement settlement)
        {
            TextObject text = new TextObject("{=RBM_pool_value}{CUR}/{MAX} (+{GROWTH}/day)");
            text.SetTextVariable("CUR", (int)Get(settlement));
            text.SetTextVariable("MAX", (int)GetMax(settlement));
            text.SetTextVariable("GROWTH", GetDailyGrowth(settlement).ToString("0.0", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        /// <summary>
        /// "Garrison growth paused below N" when a fortification's pool is down to its volunteer reserve
        /// (the garrison cannot take even one man); null otherwise, and always for a village.
        /// </summary>
        public static string FormatGarrisonPaused(Settlement settlement)
        {
            if (!IsEnabled || settlement == null || !(settlement.IsTown || settlement.IsCastle)
                || GarrisonAvailable(settlement) >= 1)
            {
                return null;
            }
            // The pool must clear the volunteer reserve by one man's present cost.
            TextObject text = new TextObject("{=RBM_pool_paused}Garrison growth paused below {RES}");
            text.SetTextVariable("RES", (int)Math.Ceiling(GetGarrisonReserve(settlement) + GetGarrisonManCost(settlement)));
            return text.ToString();
        }

        /// <summary>
        /// What the settlement's buildings add to its pool, e.g. "+25% growth, +20% max, garrison soft size
        /// +40"; null when they add nothing.
        /// </summary>
        public static string FormatBuildingBonus(Settlement settlement)
        {
            if (!IsEnabled || settlement == null)
            {
                return null;
            }
            List<string> parts = new List<string>();
            int growth = (int)Math.Round(GetGrowthBonus(settlement) * 100f);
            if (growth > 0)
            {
                TextObject text = new TextObject("{=RBM_pool_bonus_growth}+{PCT}% growth");
                text.SetTextVariable("PCT", growth);
                parts.Add(text.ToString());
            }
            int max = (int)Math.Round(GetMaxBonus(settlement) * 100f);
            if (max > 0)
            {
                TextObject text = new TextObject("{=RBM_pool_bonus_max}+{PCT}% max");
                text.SetTextVariable("PCT", max);
                parts.Add(text.ToString());
            }
            if (settlement.IsTown || settlement.IsCastle)
            {
                int soft = (int)BuildingEffects.BarracksGarrisonSoftSizeBonus(settlement.Town);
                if (soft > 0)
                {
                    TextObject text = new TextObject("{=RBM_pool_bonus_soft}garrison soft size +{MEN}");
                    text.SetTextVariable("MEN", soft);
                    parts.Add(text.ToString());
                }
            }
            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        /// <summary>"N.N per man": what the next garrison recruit costs the pool. Null for a village.</summary>
        public static string FormatGarrisonManCost(Settlement settlement)
        {
            if (!IsEnabled || settlement == null || !(settlement.IsTown || settlement.IsCastle))
            {
                return null;
            }
            TextObject text = new TextObject("{=RBM_pool_cost_value}{COST} per man");
            text.SetTextVariable("COST", GetGarrisonManCost(settlement).ToString("0.0", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        /// <summary>Takes one new garrison soldier's cost out of the pool (see <see cref="GetGarrisonManCost"/>).</summary>
        public static void ConsumeGarrison(Settlement settlement)
        {
            if (!IsEnabled || settlement == null)
            {
                return;
            }
            _pool[settlement.StringId] = Math.Max(0f, Get(settlement) - GetGarrisonManCost(settlement));
            _dayGarrison++;
        }

        /// <summary>The day's refill, clamped to the ceiling. Towns, castles and villages; nothing else has a pool.</summary>
        public static void OnDailyTick(Settlement settlement)
        {
            if (!IsEnabled || settlement == null
                || !(settlement.IsTown || settlement.IsCastle || settlement.IsVillage))
            {
                return;
            }
            float max = GetMax(settlement);
            float value = Get(settlement) + GetDailyGrowth(settlement);
            _pool[settlement.StringId] = value > max ? max : value;
        }

        /// <summary>
        /// One economy-log line for the day just ended: new men drawn, volunteers turned away for want
        /// of them, and how full the pools stand by settlement kind.
        /// </summary>
        public static void LogDailySummary()
        {
            if (EconomyLog.IsEnabled)
            {
                float townPool = 0f, townMax = 0f, castlePool = 0f, castleMax = 0f, villagePool = 0f, villageMax = 0f;
                foreach (Settlement s in Settlement.All)
                {
                    if (s.IsTown) { townPool += Get(s); townMax += GetMax(s); }
                    else if (s.IsCastle) { castlePool += Get(s); castleMax += GetMax(s); }
                    else if (s.IsVillage) { villagePool += Get(s); villageMax += GetMax(s); }
                }
                EconomyLog.Log("MANPOWER", "(world)",
                    "new volunteers " + _dayVolunteers + " (" + _dayVolunteersRefused + " refused, pool empty)"
                    + "  ·  new garrison men " + _dayGarrison
                    + "  ·  pools: towns " + (int)townPool + "/" + (int)townMax
                    + ", castles " + (int)castlePool + "/" + (int)castleMax
                    + ", villages " + (int)villagePool + "/" + (int)villageMax);
            }
            _dayVolunteers = 0;
            _dayVolunteersRefused = 0;
            _dayGarrison = 0;
        }

        /// <summary>
        /// Charges the pool for each volunteer slot the daily roll FILLS, and empties again any slot it
        /// filled that the pool could not pay for.
        /// </summary>
        /// <remarks>
        /// A before/after comparison per notable rather than a rewrite of the native method, which fills
        /// empty slots and promotes filled ones in one pass and then re-sorts the array. Counting occupied
        /// slots is immune to both: a promotion and the sort leave the count alone, so the rise in the
        /// count is exactly the number of new men. An over-budget fill is undone by clearing one slot
        /// holding a troop whose count rose, weakest first (a fresh fill is the basic recruit; a
        /// promotion is a tier up), then sliding the gap to the tail as the native sort would.
        ///
        /// Runs FIRST among postfixes so <see cref="RecruitSupply"/>'s kit draw, which diffs the same call,
        /// only ever sees the men who were actually allowed through.
        /// </remarks>
        [HarmonyPatch(typeof(RecruitmentCampaignBehavior), "UpdateVolunteersOfNotablesInSettlement")]
        private static class VolunteerSpawnPatch
        {
            // Each notable's slots as they stood before the roll. Buffers are reused across calls: this
            // runs for every settlement every day.
            private static readonly List<Hero> _notables = new List<Hero>();
            private static readonly List<CharacterObject[]> _before = new List<CharacterObject[]>();

            private static void Prefix(Settlement settlement)
            {
                _notables.Clear();
                if (!IsEnabled || settlement == null || settlement.Notables == null)
                {
                    return;
                }
                foreach (Hero notable in settlement.Notables)
                {
                    CharacterObject[] slots = (notable != null) ? notable.VolunteerTypes : null;
                    if (slots == null)
                    {
                        continue;
                    }
                    int k = _notables.Count;
                    if (k == _before.Count || _before[k].Length != slots.Length)
                    {
                        if (k == _before.Count)
                        {
                            _before.Add(new CharacterObject[slots.Length]);
                        }
                        else
                        {
                            _before[k] = new CharacterObject[slots.Length];
                        }
                    }
                    Array.Copy(slots, _before[k], slots.Length);
                    _notables.Add(notable);
                }
            }

            [HarmonyPriority(Priority.First)]
            private static void Postfix(Settlement settlement)
            {
                if (!IsEnabled || settlement == null)
                {
                    return;
                }
                for (int k = 0; k < _notables.Count; k++)
                {
                    CharacterObject[] after = _notables[k].VolunteerTypes;
                    if (after == null)
                    {
                        continue;
                    }
                    CharacterObject[] before = _before[k];
                    int newFills = Occupied(after) - Occupied(before);
                    if (newFills <= 0)
                    {
                        continue;
                    }
                    int allowed = Math.Min(newFills, VolunteerAvailable(settlement));
                    if (allowed > 0)
                    {
                        _pool[settlement.StringId] = Math.Max(0f, Get(settlement) - allowed);
                        _dayVolunteers += allowed;
                    }
                    int refused = newFills - allowed;
                    for (int r = 0; r < refused; r++)
                    {
                        if (!ClearOneNewFill(before, after))
                        {
                            break;
                        }
                        _dayVolunteersRefused++;
                    }
                    if (refused > 0)
                    {
                        CompactNullsToTail(after);
                    }
                }
                _notables.Clear();
            }

            private static int Occupied(CharacterObject[] slots)
            {
                int n = 0;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] != null)
                    {
                        n++;
                    }
                }
                return n;
            }

            private static int CountOf(CharacterObject[] slots, CharacterObject troop)
            {
                int n = 0;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == troop)
                    {
                        n++;
                    }
                }
                return n;
            }

            /// <summary>Empties one slot holding a troop there are more of than before the roll, weakest first.</summary>
            private static bool ClearOneNewFill(CharacterObject[] before, CharacterObject[] after)
            {
                int pick = -1;
                for (int i = 0; i < after.Length; i++)
                {
                    CharacterObject troop = after[i];
                    if (troop == null || CountOf(after, troop) <= CountOf(before, troop))
                    {
                        continue;
                    }
                    if (pick < 0 || troop.Level < after[pick].Level)
                    {
                        pick = i;
                    }
                }
                if (pick < 0)
                {
                    return false;
                }
                after[pick] = null;
                return true;
            }

            /// <summary>Slides empty slots to the end, keeping the order of the rest -- the shape the native sort leaves.</summary>
            private static void CompactNullsToTail(CharacterObject[] slots)
            {
                int write = 0;
                for (int read = 0; read < slots.Length; read++)
                {
                    if (slots[read] != null)
                    {
                        slots[write++] = slots[read];
                    }
                }
                for (; write < slots.Length; write++)
                {
                    slots[write] = null;
                }
            }
        }
    }
}
