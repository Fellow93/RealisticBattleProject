using System.Collections.Generic;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Teaches AI lords the one thing vanilla's settlement-visit scoring never knows: that RBM makes
    /// recruiting <see cref="RecruitSupply.RecruitsFree">free</see> in a lord's own clan fiefs, and free
    /// for a kingdom ruler anywhere in his realm. Vanilla <c>AiVisitSettlementBehavior</c> scores "should
    /// I recruit here" off each volunteer's daily WAGE and a flat money-limit; it never looks at the
    /// recruit gold cost at all, so a lord treats a stranger's town and his own free fief alike and simply
    /// picks the nearest bodies. He leaves free soldiers standing.
    ///
    /// Same shape as <see cref="RBMGarrisonRefillBehavior"/>: a pure additive bias on the AI "think" loop.
    /// It drops a moderate <c>GoToSettlement</c> score into the same <see cref="PartyThinkParams"/> every
    /// vanilla movement-scorer feeds, for the best free-recruit settlement that actually has volunteers,
    /// and lets the single highest-scoring behavior win as usual. No Harmony patching and no forced
    /// movement: the score sits in a gentle band (below the garrison-refill trip, well under the 8f "good
    /// enough" cutoff and urgent siege/defense), so it tips a lord toward his own fief when he's choosing
    /// to top up anyway, without ever overriding a real fight.
    ///
    /// The actual recruiting is entirely vanilla's <c>RecruitmentCampaignBehavior</c> once he arrives; we
    /// only steer him to a settlement where RBM's own pricing will quote him zero.
    /// </summary>
    public class RBMRecruitBiasBehavior : CampaignBehaviorBase
    {
        // "Understrength": nudge only while below this fraction of the party's *affordable* size limit.
        // FindPartySizeNormalLimit already caps the target at what the lord can pay for, so a lord at his
        // wage ceiling reads as "full" and is left alone rather than sent to muster men he can't keep.
        private const float EagerFractionOfAffordableLimit = 0.9f;

        // Don't route across the map for recruits -- mustering is routine, not worth a long march.
        private const float MaxTravelDays = 6f;

        // Each in-game day of travel is "worth" this many available volunteers when choosing between a
        // nearer thin town and a farther full one -- keeps recruiting local. Lower than the garrison
        // behavior's 30 because volunteer counts are far smaller than a garrison's surplus.
        private const float VolunteersPerTravelDay = 10f;

        // Don't divert for scraps: a free settlement must offer at least this many recruitable volunteers
        // (about one notable's worth) before it's worth steering toward.
        private const int MinVolunteers = 4;

        // Recruit slots vanilla reads per notable when the recruiter shares the settlement's faction --
        // which a lord's own fiefs and a ruler's realm always do, so the same-faction count applies.
        private const int RecruitSlotsPerNotable = 4;

        // Transient, per-session: last target we steered each party toward, so the log gets one line per
        // new decision instead of one every hour a party is still en route. Not saved.
        private readonly Dictionary<MobileParty, Settlement> _lastTarget = new Dictionary<MobileParty, Settlement>();

        // How often the expensive part -- the settlement sweep with per-candidate map pathfinding -- may
        // run per party. Between scans we re-apply the cached target's score every hour (PartyThinkParams
        // is rebuilt each tick, so a skipped hour would drop the bias entirely) without touching the
        // navmesh again. Cuts the hot path from ~24 full scans/day to 4 per eligible party, while the
        // injected score -- being purely deficit-scaled -- still tracks the party's real strength hourly.
        private const float RescanIntervalHours = 6f;

        // Per-session cache of the last full scan's result per party: the chosen target plus the nav data
        // needed to re-add its score, the volunteer/distance figures for the log, and the campaign hour at
        // which the scan may run again. Not saved -- an empty map after load just rescans once per party.
        private struct CachedTarget
        {
            public Settlement Settlement;
            public MobileParty.NavigationType NavType;
            public bool IsFromPort;
            public bool IsTargetingPort;
            public int Volunteers;
            public float DistanceDays;
            public double NextScanHour;
        }
        private readonly Dictionary<MobileParty, CachedTarget> _cache = new Dictionary<MobileParty, CachedTarget>();

        public override void RegisterEvents()
        {
            CampaignEvents.AiHourlyTickEvent.AddNonSerializedListener(this, AiHourlyTick);
            // Both maps are keyed by MobileParty and fed for a large fraction of parties, so drop a party's
            // entries the moment it is destroyed -- otherwise a long session accumulates a dead MobileParty
            // (and the object graph behind it) per departed lord, growing the managed heap for the life of
            // the session.
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnMobilePartyDestroyed);
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
            _lastTarget.Remove(mobileParty);
        }

        private void AiHourlyTick(MobileParty mobileParty, PartyThinkParams p)
        {
            // The whole point is the free-recruit price, so this only makes sense when that pricing is on.
            // With RecruitSupply off, recruiting costs the vanilla rate everywhere and there's no free
            // fief to prefer -- leave the AI to vanilla's own scoring.
            if (!RecruitSupply.IsEnabled || !IsEligibleParty(mobileParty))
            {
                return;
            }

            Hero leader = mobileParty.LeaderHero;
            if (leader.Clan == null)
            {
                return;
            }

            // Understrength *and able to grow*: below the eager threshold of the affordable limit, and not
            // already over the wage limit (recruiting is free but upkeep is not, so never send a lord to
            // muster men he can't pay to keep).
            if (mobileParty.IsWageLimitExceeded())
            {
                return;
            }
            float affordableLimit = PartyBaseHelper.FindPartySizeNormalLimit(mobileParty);
            if (mobileParty.PartySizeRatio >= affordableLimit * EagerFractionOfAffordableLimit)
            {
                return;
            }

            // Throttle the expensive work. The full sweep pathfinds to every free-recruit settlement, so
            // run it only every RescanIntervalHours, or when the previously chosen target has gone invalid
            // (besieged, captured, no longer free). Between scans we still re-add the cached score each
            // hour -- p is rebuilt every tick, so a missed hour would drop the bias entirely.
            double nowHours = CampaignTime.Now.ToHours;
            if (!_cache.TryGetValue(mobileParty, out CachedTarget cached)
                || nowHours >= cached.NextScanHour
                || !IsCachedTargetStillValid(cached.Settlement, leader))
            {
                cached = ScanForBestTarget(mobileParty, leader);
                cached.NextScanHour = nowHours + RescanIntervalHours;
                _cache[mobileParty] = cached;
            }

            if (cached.Settlement == null)
            {
                _lastTarget.Remove(mobileParty);
                return;
            }

            // Gentle, deficit-scaled score (~1.5..3.5): tips a lord toward his own free fief when he's
            // topping up anyway, but stays under the garrison-refill trip, urgent siege/defense and the
            // 8f terminal cutoff so any real threat still overrides it. Distance-independent, so it stays
            // accurate every hour even while a cached target is reused between scans.
            float depletion = MBMath.ClampFloat(1f - mobileParty.PartySizeRatio / affordableLimit, 0f, 1f);
            float score = 1.5f + 2f * depletion;

            AddGoToSettlementScore(p, cached.Settlement, score, cached.NavType, cached.IsFromPort, cached.IsTargetingPort);

            if (SpoilsLog.IsEnabled
                && (!_lastTarget.TryGetValue(mobileParty, out Settlement previous) || previous != cached.Settlement))
            {
                _lastTarget[mobileParty] = cached.Settlement;
                SpoilsLog.Log("RECRUITAI", mobileParty.Party,
                    PartyName(mobileParty) + " -> " + cached.Settlement.Name
                    + "  ·  party " + mobileParty.Party.NumberOfRegularMembers + "/" + mobileParty.Party.PartySizeLimit
                    + " (ratio " + mobileParty.PartySizeRatio.ToString("0.00")
                    + " of affordable " + affordableLimit.ToString("0.00") + ")"
                    + "  ·  free volunteers " + cached.Volunteers
                    + "  ·  " + cached.DistanceDays.ToString("0.0") + "d out"
                    + "  ·  score " + score.ToString("0.0"));
            }
        }

        /// <summary>
        /// Cheap single-settlement re-check of a cached target between full scans, so a party is never left
        /// steering toward a fief that has since been besieged, captured, or lost its free-recruit status.
        /// A null target (the last scan found nowhere worth going) counts as still valid -- it just means
        /// "keep doing nothing until the next scheduled scan" rather than rescanning every hour.
        /// </summary>
        private static bool IsCachedTargetStillValid(Settlement settlement, Hero leader)
        {
            if (settlement == null)
            {
                return true;
            }
            if (!(settlement.IsTown || settlement.IsVillage))
            {
                return false;
            }
            if (settlement.Party.MapEvent != null || settlement.SiegeEvent != null)
            {
                return false;
            }
            return RecruitSupply.RecruitsFree(settlement, leader);
        }

        /// <summary>
        /// The expensive part: sweep the lord's free-recruit settlements (his own clan's fiefs, or his
        /// whole realm if he's the ruler), count volunteers, pathfind the travel distance, and pick the
        /// best. Returns the chosen target and the nav data needed to re-apply its score; Settlement stays
        /// null when nothing qualifies.
        /// </summary>
        private static CachedTarget ScanForBestTarget(MobileParty mobileParty, Hero leader)
        {
            CachedTarget result = default(CachedTarget);
            float bestCandidateScore = float.MinValue;

            // A ruler recruits free across his whole realm; everyone else only in his own clan's fiefs.
            // Iterate the tighter set per role, but still gate each on RecruitsFree as the authority so
            // this can never disagree with what RecruitSupply will actually quote.
            bool isRuler = leader.Clan.Kingdom != null && leader.Clan.Kingdom.Leader == leader;
            IEnumerable<Settlement> candidates = isRuler ? leader.Clan.Kingdom.Settlements : leader.Clan.Settlements;

            foreach (Settlement settlement in candidates)
            {
                if (settlement == null || !(settlement.IsTown || settlement.IsVillage))
                {
                    continue;
                }
                if (!RecruitSupply.RecruitsFree(settlement, leader))
                {
                    continue;
                }
                // Skip anything under attack; siege/defense scoring owns those, and we don't want to walk
                // a party into a besieged town just to recruit.
                if (settlement.Party.MapEvent != null || settlement.SiegeEvent != null)
                {
                    continue;
                }

                int volunteers = CountAvailableVolunteers(settlement);
                if (volunteers < MinVolunteers)
                {
                    continue;
                }

                GetBestNavigationData(mobileParty, settlement, out MobileParty.NavigationType navType,
                    out float distanceAsDays, out bool isFromPort, out bool isTargetingPort);
                if (navType == MobileParty.NavigationType.None || distanceAsDays > MaxTravelDays)
                {
                    continue;
                }

                float candidateScore = volunteers - distanceAsDays * VolunteersPerTravelDay;
                if (candidateScore > bestCandidateScore)
                {
                    bestCandidateScore = candidateScore;
                    result.Settlement = settlement;
                    result.Volunteers = volunteers;
                    result.DistanceDays = distanceAsDays;
                    result.NavType = navType;
                    result.IsFromPort = isFromPort;
                    result.IsTargetingPort = isTargetingPort;
                }
            }

            return result;
        }

        /// <summary>
        /// Recruitable volunteers standing at <paramref name="settlement"/>, counted the way vanilla's own
        /// recruit scorer counts them (<c>GetApproximateVolunteersCanBeRecruitedDataFromSettlement</c>):
        /// the first few slots of each living notable's roster. Same-faction slot count, since a lord's own
        /// fiefs and a ruler's realm are always his own faction.
        /// </summary>
        private static int CountAvailableVolunteers(Settlement settlement)
        {
            if (settlement.Notables == null)
            {
                return 0;
            }
            int count = 0;
            foreach (Hero notable in settlement.Notables)
            {
                if (notable == null || !notable.IsAlive)
                {
                    continue;
                }
                CharacterObject[] volunteers = notable.VolunteerTypes;
                if (volunteers == null)
                {
                    continue;
                }
                int slots = volunteers.Length < RecruitSlotsPerNotable ? volunteers.Length : RecruitSlotsPerNotable;
                for (int i = 0; i < slots; i++)
                {
                    if (volunteers[i] != null)
                    {
                        count++;
                    }
                }
            }
            return count;
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
            // Leave the player's own clan parties to the player; and don't disturb parties already
            // committed to an army, a battle, a siege, or disbanding.
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
        /// Adds (or accumulates onto) a GoToSettlement score for <paramref name="settlement"/> -- the same
        /// shape as vanilla's AddBehaviorTupleWithScore so it slots into the think loop cleanly.
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
    }
}
