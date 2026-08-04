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
    /// score sits in a moderate band (below urgent siege/defense and below the 8f "good enough" cutoff),
    /// so a weak lord still breaks off to react to a real threat instead of stubbornly marching home.
    ///
    /// The move itself is executed by vanilla's own <c>SetPartyAiAction</c> when our score wins, and the
    /// actual troop transfer is entirely vanilla's -- we only steer the party toward a settlement where
    /// that transfer's own conditions (own clan, wage room, garrison above its floor) will fire.
    /// </summary>
    public class RBMGarrisonRefillBehavior : CampaignBehaviorBase
    {
        // "Badly depleted": trigger only below this fraction of the party's *affordable* size limit.
        // FindPartySizeNormalLimit already caps the target at what the lord can pay for, so a broke
        // lord reads as "at his limit" and is left alone rather than sent on a pointless round-trip.
        private const float DepletedFractionOfAffordableLimit = 0.4f;

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

        public override void RegisterEvents()
        {
            CampaignEvents.AiHourlyTickEvent.AddNonSerializedListener(this, AiHourlyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
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
            if (mobileParty.PartySizeRatio >= affordableLimit * DepletedFractionOfAffordableLimit)
            {
                return;
            }

            Settlement best = null;
            float bestCandidateScore = float.MinValue;
            int bestSurplus = 0;
            float bestDistanceDays = 0f;
            MobileParty.NavigationType bestNavType = MobileParty.NavigationType.None;
            bool bestIsFromPort = false;
            bool bestIsTargetingPort = false;

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
                    best = settlement;
                    bestSurplus = surplus;
                    bestDistanceDays = distanceAsDays;
                    bestNavType = navType;
                    bestIsFromPort = isFromPort;
                    bestIsTargetingPort = isTargetingPort;
                }
            }

            if (best == null)
            {
                _lastDivertTarget.Remove(mobileParty);
                return;
            }

            // Moderate, deficit-scaled score (~2..5): beats idle wandering, but stays under urgent
            // siege/defense and the 8f terminal cutoff so threats always override the refill trip.
            float depletion = MBMath.ClampFloat(1f - mobileParty.PartySizeRatio / affordableLimit, 0f, 1f);
            float score = 2f + 3f * depletion;

            AddGoToSettlementScore(p, best, score, bestNavType, bestIsFromPort, bestIsTargetingPort);

            // One DIVERT line per new decision, not one per hourly re-score of the same trip.
            if (GarrisonRefillLog.IsEnabled
                && (!_lastDivertTarget.TryGetValue(mobileParty, out Settlement previous) || previous != best))
            {
                _lastDivertTarget[mobileParty] = best;
                GarrisonRefillLog.Log("DIVERT", PartyName(mobileParty),
                    "-> " + GarrisonRefillLog.Name(best)
                    + "  ·  party " + mobileParty.Party.NumberOfRegularMembers + "/" + mobileParty.Party.PartySizeLimit
                    + " (ratio " + mobileParty.PartySizeRatio.ToString("0.00")
                    + " of affordable " + affordableLimit.ToString("0.00") + ")"
                    + "  ·  garrison surplus " + bestSurplus
                    + "  ·  " + bestDistanceDays.ToString("0.0") + "d out"
                    + "  ·  score " + score.ToString("0.0"));
            }
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
            private static void Prefix(Settlement settlement, out int __state)
            {
                __state = settlement?.Town?.GarrisonParty?.Party.NumberOfRegularMembers ?? 0;
            }

            private static void Postfix(MobileParty mobileParty, Settlement settlement, int __state)
            {
                if (!GarrisonRefillLog.IsEnabled || mobileParty == null || settlement == null)
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
    }
}
