using System;
using System.Collections.Generic;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Makes a badly depleted AI lord party head for one of its own clan's fortifications that is
    /// holding a surplus garrison, so vanilla <c>GarrisonTroopsCampaignBehavior.OnSettlementEntered</c>
    /// tops the party back up from that garrison on arrival.
    ///
    /// This is a pure, additive bias on the AI "think" loop -- it drops a <c>GoToSettlement</c> score
    /// into the same <see cref="PartyThinkParams"/> every vanilla movement-scorer feeds, and lets the
    /// single highest-scoring behavior win as usual. No Harmony patching, and no forced movement: the
    /// score is a strong, deficit-scaled pull that outbids idle wandering and settlement visits (and, for
    /// a badly depleted lord, clears the 8f "good enough" cutoff), yet still sits well under urgent
    /// siege/defense scores, so a weak lord breaks off to react to a real threat instead of marching home.
    ///
    /// The move itself is executed by vanilla's own <c>SetPartyAiAction</c> when our score wins, and the
    /// actual troop transfer is entirely vanilla's -- we only steer the party toward a settlement where
    /// that transfer's own conditions (own clan, wage room, garrison above its floor) will fire.
    /// </summary>
    public class RBMGarrisonRefillBehavior : CampaignBehaviorBase
    {
        // "Understrength": trigger while below this fraction of the party's *affordable* size limit.
        // FindPartySizeNormalLimit already caps the target at what the lord can pay for, so a broke
        // lord reads as "at his limit" and is left alone rather than sent on a pointless round-trip.
        // With LordPartyWageLimit restoring a vanilla-scale affordable size, this can sit high: a lord
        // three-quarters full still has real room, so send him home to top up rather than waiting until
        // he is nearly wiped out (the old 0.4 fired only under ~24% of the *raw* size cap).
        private const float DepletedFractionOfAffordableLimit = 0.75f;

        // Vanilla's army-eligibility gate (DefaultArmyManagementCalculationModel: leader and every called
        // member need PartySizeRatio > 0.6). Never divert a lord who already clears it -- he is army
        // material, and a pull home would compete with the kingdom's gather-army decision.
        private const float ArmyEligiblePartySizeRatio = 0.6f;

        // Vanilla's hard garrison floors (GarrisonTroopsCampaignBehavior). A garrison at or below its
        // floor never releases troops, so anything above it is the surplus we can actually draw on.
        private const int MinGarrisonForTown = 125;
        private const int MinGarrisonForCastle = 75;

        // Don't divert for a trickle: a garrison must sit at least this many men above its floor before
        // the trip is worth it, or weak lords cross the map to pick up a handful of troops.
        private const int MinGarrisonSurplus = 10;

        // Each in-game day of travel is "worth" this many surplus garrison troops when choosing between
        // a nearer small garrison and a farther large one -- keeps refills local rather than cross-map.
        private const float SurplusPerTravelDay = 30f;

        // Don't bother routing a lord more than this many days out just to refill.
        private const float MaxTravelDays = 8f;

        // Transient, per-session: the last target we steered each party toward, so the DIVERT log gets
        // one line per new decision instead of one every hour the party is still en route. Not saved --
        // an empty map after load just means the next tick's decisions re-log once, which is harmless.
        private readonly Dictionary<MobileParty, Settlement> _lastDivertTarget = new Dictionary<MobileParty, Settlement>();

        // How often the expensive part -- the clan-fief sweep with per-candidate map pathfinding -- may
        // run per party. Between scans we re-apply the cached target's score every hour (PartyThinkParams
        // is rebuilt each tick, so a skipped hour would drop the bias entirely) without touching the
        // navmesh again. The injected score is purely deficit-scaled, so it stays accurate hourly even
        // while a cached target is reused.
        private const float RescanIntervalHours = 6f;

        // Per-session cache of the last full scan's result per party: the chosen fortification plus the
        // nav data needed to re-add its score, the surplus/distance figures for the log, and the campaign
        // hour at which the scan may run again. Not saved -- an empty map after load just rescans once.
        private struct CachedTarget
        {
            public Settlement Settlement;
            public MobileParty.NavigationType NavType;
            public bool IsFromPort;
            public bool IsTargetingPort;
            public int Surplus;
            public float DistanceDays;
            public double NextScanHour;
        }
        private readonly Dictionary<MobileParty, CachedTarget> _cache = new Dictionary<MobileParty, CachedTarget>();

        public override void RegisterEvents()
        {
            CampaignEvents.AiHourlyTickEvent.AddNonSerializedListener(this, AiHourlyTick);
            // Both maps are keyed by MobileParty, so drop a party's entries when it is destroyed -- else a
            // long session accumulates a dead MobileParty (and its object graph) per departed lord.
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnMobilePartyDestroyed);
            // Diagnostics only: why AI armies end. Nothing else records an army's creation or dispersal.
            CampaignEvents.ArmyCreated.AddNonSerializedListener(this, OnArmyCreated);
            CampaignEvents.ArmyDispersed.AddNonSerializedListener(this, OnArmyDispersed);
        }

        // Transient, per-session: campaign hour each army was created at, for the ARMY-END age figure.
        private readonly Dictionary<Army, double> _armyCreatedHour = new Dictionary<Army, double>();

        private static readonly AccessTools.FieldRef<Army, int> ArmyInactivityCounter =
            AccessTools.FieldRefAccess<Army, int>("_inactivityCounter");

        private void OnArmyCreated(Army army)
        {
            // The creation hour only feeds the ARMY-END age figure, so nothing is tracked while the log is off
            // (an army created before the log was switched on just reports its age as "?").
            if (army == null || army.LeaderParty == null || !GarrisonRefillLog.IsEnabled)
            {
                return;
            }
            _armyCreatedHour[army] = CampaignTime.Now.ToHours;
            try
            {
                GarrisonRefillLog.Log("ARMY-NEW", PartyName(army.LeaderParty), DescribeArmy(army));
            }
            catch
            {
            }
        }

        private void OnArmyDispersed(Army army, Army.ArmyDispersionReason reason, bool isPlayersArmy)
        {
            if (army == null)
            {
                return;
            }
            double createdHour;
            bool hasAge = _armyCreatedHour.TryGetValue(army, out createdHour);
            _armyCreatedHour.Remove(army);
            if (!GarrisonRefillLog.IsEnabled || army.LeaderParty == null)
            {
                return;
            }
            try
            {
                MobileParty leader = army.LeaderParty;
                GarrisonRefillLog.Log("ARMY-END", PartyName(leader),
                    "reason " + reason
                    + "  ·  age " + (hasAge ? (CampaignTime.Now.ToHours - createdHour).ToString("0") + "h" : "?")
                    + "  ·  cohesion " + army.Cohesion.ToString("0")
                    + "  ·  idle " + ArmyInactivityCounter(army) + "h"
                    + "  ·  waiting " + army.IsWaitingForArmyMembers()
                    + "  ·  leader " + leader.DefaultBehavior
                    + " -> " + GarrisonRefillLog.Name(leader.TargetSettlement)
                    + (leader.IsCurrentlyAtSea ? " (at sea)" : "")
                    + "  ·  " + DescribeArmy(army)
                    + "  ·  " + DescribeSiegeOptions(leader));
            }
            catch
            {
            }
        }

        private static string DescribeArmy(Army army)
        {
            int men = 0;
            int starving = 0;
            int minFoodDays = int.MaxValue;
            foreach (MobileParty party in army.Parties)
            {
                men += party.MemberRoster.TotalManCount;
                if (party.Party.IsStarving)
                {
                    starving++;
                }
                minFoodDays = Math.Min(minFoodDays, party.GetNumDaysForFoodToLast());
            }
            return army.ArmyType + " vs " + (army.AiBehaviorObject != null && army.AiBehaviorObject.Name != null ? army.AiBehaviorObject.Name.ToString() : "?")
                + "  ·  parties " + army.Parties.Count + " (attached " + army.LeaderPartyAndAttachedPartiesCount + ")"
                + "  ·  men " + men
                + "  ·  strength " + army.LeaderParty.GetTotalLandStrengthWithFollowers().ToString("0")
                + "  ·  starving " + starving
                + "  ·  min food " + (minFoodDays == int.MaxValue ? "?" : minFoodDays + "d");
        }

        // How many enemy fortifications still clear the siege strength gate for this leader right now, and
        // the best score among them. Zero passing targets at dispersal = the army lost its only objective.
        private static string DescribeSiegeOptions(MobileParty leader)
        {
            float strength = leader.GetTotalLandStrengthWithFollowers();
            int enemyForts = 0;
            int passing = 0;
            float bestScore = 0f;
            Settlement best = null;
            foreach (Settlement settlement in Settlement.All)
            {
                if (!settlement.IsFortification || !settlement.MapFaction.IsAtWarWith(leader.MapFaction))
                {
                    continue;
                }
                enemyForts++;
                float score = Campaign.Current.Models.TargetScoreCalculatingModel.GetTargetScoreForFaction(settlement, Army.ArmyTypes.Besieger, leader, strength);
                if (score > 0f)
                {
                    passing++;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = settlement;
                    }
                }
            }
            return "siege targets " + passing + "/" + enemyForts
                + (best != null ? " (best " + GarrisonRefillLog.Name(best) + " " + bestScore.ToString("0.00") + ")" : "");
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnMobilePartyDestroyed(MobileParty mobileParty, PartyBase destroyerParty)
        {
            if (mobileParty == null)
            {
                return;
            }
            _cache.Remove(mobileParty);
            _lastDivertTarget.Remove(mobileParty);
        }

        private void AiHourlyTick(MobileParty mobileParty, PartyThinkParams p)
        {
            if (!IsEligibleParty(mobileParty))
            {
                return;
            }

            Hero leader = mobileParty.LeaderHero;

            // Understrength *and able to grow*: below the depleted threshold of the affordable limit,
            // and not already over the wage limit (the same gate the garrison transfer itself checks,
            // so we never send a lord who couldn't keep the troops anyway).
            if (mobileParty.IsWageLimitExceeded())
            {
                return;
            }
            float affordableLimit = PartyBaseHelper.FindPartySizeNormalLimit(mobileParty);
            if (mobileParty.PartySizeRatio >= MathF.Min(affordableLimit * DepletedFractionOfAffordableLimit, ArmyEligiblePartySizeRatio))
            {
                return;
            }

            // Throttle the expensive work. The full sweep pathfinds to every clan fortification, so run it
            // only every RescanIntervalHours, or when the previously chosen target has gone invalid (under
            // attack, captured, garrison drained below its surplus floor). Between scans we still re-add
            // the cached score each hour -- p is rebuilt every tick, so a missed hour would drop the bias.
            double nowHours = CampaignTime.Now.ToHours;
            if (!_cache.TryGetValue(mobileParty, out CachedTarget cached)
                || nowHours >= cached.NextScanHour
                || !IsCachedTargetStillValid(cached.Settlement))
            {
                cached = ScanForBestTarget(mobileParty, leader);
                cached.NextScanHour = nowHours + RescanIntervalHours;
                _cache[mobileParty] = cached;
            }

            if (cached.Settlement == null)
            {
                _lastDivertTarget.Remove(mobileParty);
                return;
            }

            // Deficit-scaled score (~5..9): a strong pull home that a badly depleted lord (top of the band)
            // pushes past the 8f "good enough" cutoff, so he actually breaks off idle wandering/visiting to
            // go refill -- yet urgent siege/defense scores (far higher) still override it, so he never
            // marches home in the teeth of a threat. Distance-independent, so it stays accurate every hour
            // even while a cached target is reused.
            float depletion = MBMath.ClampFloat(1f - mobileParty.PartySizeRatio / affordableLimit, 0f, 1f);
            float score = 5f + 4f * depletion;

            AddGoToSettlementScore(p, cached.Settlement, score, cached.NavType, cached.IsFromPort, cached.IsTargetingPort);

            // One DIVERT line per new decision, not one per hourly re-score of the same trip.
            if (GarrisonRefillLog.IsEnabled
                && (!_lastDivertTarget.TryGetValue(mobileParty, out Settlement previous) || previous != cached.Settlement))
            {
                _lastDivertTarget[mobileParty] = cached.Settlement;
                GarrisonRefillLog.Log("DIVERT", PartyName(mobileParty),
                    "-> " + GarrisonRefillLog.Name(cached.Settlement)
                    + "  ·  party " + mobileParty.Party.NumberOfRegularMembers + "/" + mobileParty.Party.PartySizeLimit
                    + " (ratio " + mobileParty.PartySizeRatio.ToString("0.00")
                    + " of affordable " + affordableLimit.ToString("0.00") + ")"
                    + "  ·  garrison surplus " + cached.Surplus
                    + "  ·  " + cached.DistanceDays.ToString("0.0") + "d out"
                    + "  ·  score " + score.ToString("0.0"));
            }
        }

        /// <summary>
        /// Cheap single-settlement re-check of a cached target between full scans, so a party is never left
        /// heading for a fortification that has since been besieged, captured, or drained below its surplus
        /// floor. A null target (the last scan found nothing worth the trip) counts as still valid -- it
        /// just means "keep doing nothing until the next scheduled scan" rather than rescanning every hour.
        /// </summary>
        private static bool IsCachedTargetStillValid(Settlement settlement)
        {
            if (settlement == null)
            {
                return true;
            }
            if (!settlement.IsFortification || settlement.Town == null)
            {
                return false;
            }
            if (settlement.Party.MapEvent != null || settlement.SiegeEvent != null)
            {
                return false;
            }
            int garrison = settlement.Town.GarrisonParty?.Party.NumberOfRegularMembers ?? 0;
            int floor = settlement.IsTown ? MinGarrisonForTown : MinGarrisonForCastle;
            return garrison - floor >= MinGarrisonSurplus;
        }

        /// <summary>
        /// The expensive part: sweep the lord's own clan fortifications, measure each garrison's surplus,
        /// pathfind the travel distance, and pick the best. Returns the chosen target and the nav data
        /// needed to re-apply its score; Settlement stays null when nothing qualifies.
        /// </summary>
        private static CachedTarget ScanForBestTarget(MobileParty mobileParty, Hero leader)
        {
            CachedTarget result = default(CachedTarget);
            float bestCandidateScore = float.MinValue;

            // Own clan only -- vanilla's take-from-garrison gate is LeaderHero.Clan == OwnerClan, so a
            // same-kingdom fief owned by another clan would path the lord there for nothing.
            foreach (Settlement settlement in leader.Clan.Settlements)
            {
                if (!settlement.IsFortification || settlement.Town == null)
                {
                    continue;
                }
                // Skip anything under attack; siege/defense scoring owns those decisions, and we don't
                // want to walk a weak party into a besieged town.
                if (settlement.Party.MapEvent != null || settlement.SiegeEvent != null)
                {
                    continue;
                }

                int garrison = settlement.Town.GarrisonParty?.Party.NumberOfRegularMembers ?? 0;
                int floor = settlement.IsTown ? MinGarrisonForTown : MinGarrisonForCastle;
                int surplus = garrison - floor;
                if (surplus < MinGarrisonSurplus)
                {
                    continue;
                }

                GetBestNavigationData(mobileParty, settlement, out MobileParty.NavigationType navType,
                    out float distanceAsDays, out bool isFromPort, out bool isTargetingPort);
                if (navType == MobileParty.NavigationType.None || distanceAsDays > MaxTravelDays)
                {
                    continue;
                }

                float candidateScore = surplus - distanceAsDays * SurplusPerTravelDay;
                if (candidateScore > bestCandidateScore)
                {
                    bestCandidateScore = candidateScore;
                    result.Settlement = settlement;
                    result.Surplus = surplus;
                    result.DistanceDays = distanceAsDays;
                    result.NavType = navType;
                    result.IsFromPort = isFromPort;
                    result.IsTargetingPort = isTargetingPort;
                }
            }

            return result;
        }

        private static string PartyName(MobileParty mobileParty)
        {
            if (mobileParty == null)
            {
                return "?";
            }
            if (mobileParty.LeaderHero != null && mobileParty.LeaderHero.Name != null)
            {
                return mobileParty.LeaderHero.Name.ToString();
            }
            return mobileParty.Name != null ? mobileParty.Name.ToString() : mobileParty.StringId;
        }

        private static bool IsEligibleParty(MobileParty mobileParty)
        {
            if (mobileParty == null || !mobileParty.IsLordParty || mobileParty.LeaderHero == null)
            {
                return false;
            }
            // Leave the player's own clan parties to the player; and don't disturb parties that are
            // already committed to an army, a battle, a siege, or disbanding.
            if (mobileParty.LeaderHero.Clan == Clan.PlayerClan)
            {
                return false;
            }
            if (mobileParty.Army != null || mobileParty.MapEvent != null
                || mobileParty.BesiegedSettlement != null || mobileParty.IsDisbanding)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Mirrors vanilla <c>AiVisitSettlementBehavior.GetBestNavigationDataForVisitingSettlement</c>:
        /// picks the cheaper of a land approach and (for naval-capable parties) a port approach.
        /// </summary>
        private static void GetBestNavigationData(MobileParty mobileParty, Settlement settlement,
            out MobileParty.NavigationType bestNavigationType, out float distanceAsDays,
            out bool isFromPort, out bool isTargetingPort)
        {
            bestNavigationType = MobileParty.NavigationType.None;
            float bestNavigationDistance = float.MaxValue;
            isTargetingPort = false;
            bool landIsFromPort = false;

            if (!settlement.HasPort || settlement.SiegeEvent == null
                || settlement.SiegeEvent.IsBlockadeActive || !mobileParty.HasNavalNavigationCapability)
            {
                AiHelper.GetBestNavigationTypeAndAdjustedDistanceOfSettlementForMobileParty(
                    mobileParty, settlement, false, out bestNavigationType, out bestNavigationDistance,
                    out landIsFromPort);
            }
            isFromPort = landIsFromPort;

            if (mobileParty.HasNavalNavigationCapability && settlement.HasPort && settlement.IsFortification)
            {
                AiHelper.GetBestNavigationTypeAndAdjustedDistanceOfSettlementForMobileParty(
                    mobileParty, settlement, true, out MobileParty.NavigationType portNavType,
                    out float portDistance, out bool portIsFromPort);
                if (portDistance < bestNavigationDistance)
                {
                    bestNavigationType = portNavType;
                    bestNavigationDistance = portDistance;
                    isFromPort = portIsFromPort;
                    isTargetingPort = true;
                }
            }

            distanceAsDays = bestNavigationDistance
                / (Campaign.Current.EstimatedAverageLordPartySpeed * (float)CampaignTime.HoursInDay);
        }

        /// <summary>
        /// Adds (or accumulates onto) a GoToSettlement score for <paramref name="settlement"/> -- the
        /// same shape as vanilla's AddBehaviorTupleWithScore so it slots into the think loop cleanly.
        /// </summary>
        private static void AddGoToSettlementScore(PartyThinkParams p, Settlement settlement, float score,
            MobileParty.NavigationType navigationType, bool isFromPort, bool isTargetingPort)
        {
            AIBehaviorData behaviorData = new AIBehaviorData(settlement, AiBehavior.GoToSettlement,
                navigationType, willGatherArmy: false, isFromPort, isTargetingPort);
            if (p.TryGetBehaviorScore(in behaviorData, out float existing))
            {
                p.SetBehaviorScore(in behaviorData, existing + score);
            }
            else
            {
                p.AddBehaviorScore((behaviorData, score));
            }
        }

        /// <summary>
        /// Verification postfix on vanilla's garrison-to-party transfer, so the log captures how many
        /// men a garrison actually released to an arriving lord -- the payoff of a DIVERT. This fires
        /// for every vanilla take (RBM-routed or an ordinary lord walking into his surplus town), which
        /// is exactly the full picture we want when confirming the feature. Read-only; gated by the log.
        /// </summary>
        [HarmonyPatch(typeof(GarrisonTroopsCampaignBehavior), "TakeTroopsFromGarrison")]
        private class LogTakeTroopsFromGarrison
        {
            // -1 = the log was off when the transfer started, so there is nothing to compare against.
            private static void Prefix(Settlement settlement, out int __state)
            {
                __state = GarrisonRefillLog.IsEnabled
                    ? (settlement?.Town?.GarrisonParty?.Party.NumberOfRegularMembers ?? 0)
                    : -1;
            }

            private static void Postfix(MobileParty mobileParty, Settlement settlement, int __state)
            {
                if (__state < 0 || !GarrisonRefillLog.IsEnabled || mobileParty == null || settlement == null)
                {
                    return;
                }
                int after = settlement.Town?.GarrisonParty?.Party.NumberOfRegularMembers ?? 0;
                int moved = __state - after;
                if (moved <= 0)
                {
                    return;
                }
                GarrisonRefillLog.Log("REFILL", PartyName(mobileParty),
                    "took " + moved + " from " + GarrisonRefillLog.Name(settlement) + " garrison"
                    + "  ·  garrison " + __state + " -> " + after
                    + "  ·  party now " + mobileParty.Party.NumberOfRegularMembers + "/" + mobileParty.Party.PartySizeLimit);
            }
        }

        /// <summary>
        /// Lets an AI lord visiting his own clan's fortification fill up from its surplus garrison in one visit.
        ///
        /// Vanilla's party/garrison transfer splits the combined men in proportion to the two "ideal" sizes, and
        /// under RBM the garrison's ideal (wealth-driven) is large, so a lord only walks off with a slice of what
        /// he could hold -- lords sat at ~0.5 PartySizeRatio beside garrisons of 400+, under the 0.6 vanilla
        /// requires to lead or join an army. After vanilla's own transfer this tops the lord up to his vanilla
        /// size-with-food-and-wage limit, never taking the garrison below vanilla's floor (125 town / 75 castle).
        /// The men move through vanilla's own TakeTroopsFromGarrison, so the REFILL log above records them too.
        /// </summary>
        [HarmonyPatch(typeof(GarrisonTroopsCampaignBehavior), "ManageGarrisonForParty")]
        private class TopUpLordFromSurplusGarrison
        {
            private static readonly Func<GarrisonTroopsCampaignBehavior, MobileParty, int> CalculatePartyLimit =
                AccessTools.MethodDelegate<Func<GarrisonTroopsCampaignBehavior, MobileParty, int>>(
                    AccessTools.Method(typeof(GarrisonTroopsCampaignBehavior), "CalculateMobilePartySizeLimitWithFoodAndWage"));

            private static readonly Action<GarrisonTroopsCampaignBehavior, MobileParty, Settlement, int, bool> TakeTroops =
                AccessTools.MethodDelegate<Action<GarrisonTroopsCampaignBehavior, MobileParty, Settlement, int, bool>>(
                    AccessTools.Method(typeof(GarrisonTroopsCampaignBehavior), "TakeTroopsFromGarrison"));

            private static void Prefix(Settlement settlement, out int __state)
            {
                __state = settlement?.Town?.GarrisonParty?.Party.NumberOfRegularMembers ?? 0;
            }

            private static void Postfix(GarrisonTroopsCampaignBehavior __instance, MobileParty mobileParty, Settlement settlement, int __state)
            {
                if (!IsEligibleParty(mobileParty) || settlement?.Town?.GarrisonParty == null
                    || mobileParty.LeaderHero.Clan != settlement.OwnerClan || mobileParty.IsWageLimitExceeded())
                {
                    return;
                }
                int garrison = settlement.Town.GarrisonParty.Party.NumberOfRegularMembers;
                // Vanilla just handed men TO the garrison (it was below its ideal): don't take them straight back.
                if (garrison > __state)
                {
                    return;
                }
                int floor = settlement.IsTown ? MinGarrisonForTown : MinGarrisonForCastle;
                int room = CalculatePartyLimit(__instance, mobileParty) - mobileParty.Party.NumberOfRegularMembers;
                int take = Math.Min(room, garrison - floor);
                if (take > 0)
                {
                    TakeTroops(__instance, mobileParty, settlement, take, false);
                }
            }
        }
    }
}
