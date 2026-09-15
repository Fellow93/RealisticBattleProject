using System.Collections.Generic;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Gives deserter parties initiative. Vanilla spawns them (via <c>DesertersCampaignBehavior</c>) as
    /// leaderless looter-bandits of the hardcoded <c>"deserters"</c> clan, and because they are
    /// <c>IsBandit &amp;&amp; LeaderHero == null</c> almost every scored AI behavior deliberately skips them:
    /// <c>AiMilitaryBehavior</c> (organized raids) bails on <c>IsBandit</c>, and <c>AiEngagePartyBehavior</c>
    /// (the "hunt an enemy party" scorer) bails on <c>LeaderHero == null</c>. The one behavior that runs for
    /// them, <c>AiLandBanditPatrollingBehavior</c>, only tells them to loiter around their spawn village with
    /// a deliberately tiny score. So today a deserter band just wanders, and only preys on anyone it happens
    /// to physically bump into.
    ///
    /// This behavior turns them into the bold marauders they ought to be: as they think, it seeds
    /// <see cref="PartyThinkParams"/> with real target-seeking scores -- chase a nearby villager convoy or
    /// caravan (<see cref="AiBehavior.GoAroundParty"/>), or march on a weakly-held village and sack it
    /// (<see cref="AiBehavior.RaidSettlement"/>) -- but only when the band actually out-matches the target
    /// (a target-relative strength gate). Same additive shape as <see cref="RBMRecruitBiasBehavior"/> and
    /// <see cref="RBMGarrisonRefillBehavior"/>: it never calls <c>SetMove*</c> itself, it only contributes
    /// candidates to the shared think params and lets vanilla's <c>AiPartyThinkBehavior</c> pick the winner
    /// and issue the order. No Harmony patch, no conflict with native AI. When nothing worth hitting is in
    /// range it adds nothing and the band falls back to vanilla's patrol.
    ///
    /// Scores are distance-weighted, so the closest viable target keeps scoring highest think after think --
    /// which is also what keeps a band committed: a village being raided sits at distance ~0 and so keeps
    /// winning, and a small commitment bonus on the current target resists abandoning a raid or chase for a
    /// marginally better one. (Feeding the raid score every think is load-bearing: <c>AiPartyThinkBehavior</c>
    /// rethinks hourly during a raid and will FINALIZE the raid if the top-scoring behavior is no longer that
    /// raid, so we must keep re-asserting it.)
    /// </summary>
    public class RBMDeserterRaiderBehavior : CampaignBehaviorBase
    {
        // The hardcoded clan every post-battle deserter party belongs to (DesertersCampaignBehavior.DeserterClan).
        private const string DeserterClanStringId = "deserters";

        // Target-relative strength gates. A band pursues only when its own EstimatedStrength beats the
        // target's by these factors. Bold, so both sit near 1: it will take on an even fight rather than
        // only sure things. Party gate is a touch above 1 because a caravan's escort can bite back; the
        // village gate is exactly "as strong as the defenders" because villages are soft loot.
        private const float PartyStrengthRatio = 1.1f;
        private const float RaidStrengthRatio = 1.0f;

        // Bold marauders roam. Party search reuses vanilla AiEngagePartyBehavior's own generous radius
        // (encounter distance * 45) so they'll set off after prey a good way away; the village search spans
        // a few average town-gaps so a strong band will march to the nearest soft village rather than wait
        // for one to wander into view.
        private const float PartySearchEncounterMultiple = 45f;
        private const float VillageSearchTownGapMultiple = 3f;

        // Base desirabilities before distance weighting. Caravans carry richer loot than a villager convoy,
        // and a raid is the marquee act of a strong band -- so raids outrank a chase of equal closeness,
        // which (via the distance weight) still lets an adjacent caravan win over a far village.
        private const float VillagerHuntBaseScore = 3f;
        private const float CaravanHuntBaseScore = 4f;
        private const float RaidBaseScore = 5f;

        // A village's mustering militia counts for this much "strength" per man in the raid gate. Militia are
        // weak, so a real band clears a normal village's levy while a beaten remnant cannot -- which is the
        // whole "if they are strong enough" gate, expressed purely relative to the target.
        private const float MilitiaStrengthPerMan = 0.6f;

        // Confidence ceiling: a raid score is scaled by how overwhelming the band is versus the defence,
        // capped here so a wildly strong band doesn't produce an absurd score that would swamp everything.
        private const float MaxRaidConfidence = 2f;

        // Keep the current objective on top: a small bonus for the target the band is already raiding or
        // chasing, so it finishes the job instead of flip-flopping to a marginally closer one each think.
        private const float CommitmentBonus = 1.4f;

        // Transient, per-session: last target we steered each party toward, for one log line per new decision
        // instead of one every think while it's still en route. Not saved.
        private readonly Dictionary<MobileParty, IMapPoint> _lastTarget = new Dictionary<MobileParty, IMapPoint>();

        public override void RegisterEvents()
        {
            CampaignEvents.AiHourlyTickEvent.AddNonSerializedListener(this, AiHourlyTick);
            // Drop a party's log-dedup entry the moment it dies, so a long session doesn't pin a dead
            // MobileParty (and its object graph) in the map for the life of the session.
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnMobilePartyDestroyed);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnMobilePartyDestroyed(MobileParty mobileParty, PartyBase destroyerParty)
        {
            if (mobileParty != null)
            {
                _lastTarget.Remove(mobileParty);
            }
        }

        private static bool IsDeserterParty(MobileParty mobileParty)
        {
            return mobileParty != null
                && mobileParty.IsActive
                && mobileParty.IsBandit
                && mobileParty.ActualClan != null
                && mobileParty.ActualClan.StringId == DeserterClanStringId;
        }

        private void AiHourlyTick(MobileParty mobileParty, PartyThinkParams p)
        {
            if (!RBMConfig.RBMConfig.deserterRaidersEnabled)
            {
                return;
            }
            if (!IsDeserterParty(mobileParty) || !mobileParty.HasLandNavigationCapability)
            {
                return;
            }
            // Deserters are never in an army; guard anyway so we never fight the army/escort logic.
            if (mobileParty.Army != null || mobileParty.BesiegedSettlement != null)
            {
                return;
            }

            float myStrength = mobileParty.Party.EstimatedStrength;
            if (myStrength <= 0f)
            {
                return;
            }

            // Best candidate this think, tracked only so the log records the target the band will actually
            // commit to (the single highest score wins in AiPartyThinkBehavior).
            IMapPoint bestTarget = null;
            float bestScore = 0f;
            string bestKind = null;

            ScoreHuntTargets(mobileParty, myStrength, p, ref bestTarget, ref bestScore, ref bestKind);
            ScoreRaidTargets(mobileParty, myStrength, p, ref bestTarget, ref bestScore, ref bestKind);

            if (bestTarget == null)
            {
                _lastTarget.Remove(mobileParty);
                return;
            }

            if (SpoilsLog.IsEnabled
                && (!_lastTarget.TryGetValue(mobileParty, out IMapPoint previous) || previous != bestTarget))
            {
                _lastTarget[mobileParty] = bestTarget;
                SpoilsLog.Log("DESERTER", mobileParty.Party,
                    PartyName(mobileParty) + " (" + mobileParty.Party.NumberOfAllMembers + " men, str "
                    + myStrength.ToString("0") + ") -> " + bestKind + " " + TargetName(bestTarget)
                    + "  ·  score " + bestScore.ToString("0.0"));
            }
        }

        /// <summary>
        /// Seeds a <see cref="AiBehavior.GoAroundParty"/> pursuit score for every nearby villager convoy or
        /// caravan the band is strong enough to run down. Only civilian, on-land, out-in-the-open parties are
        /// eligible; anything sheltering in a settlement or already in a fight is left alone.
        /// </summary>
        private void ScoreHuntTargets(MobileParty mobileParty, float myStrength, PartyThinkParams p,
            ref IMapPoint bestTarget, ref float bestScore, ref string bestKind)
        {
            float radius = Campaign.Current.Models.EncounterModel.NeededMaximumLandDistanceForEncounteringMobileParty
                * PartySearchEncounterMultiple;
            if (!(radius > 0f))
            {
                return;
            }

            LocatableSearchData<MobileParty> data = MobileParty.StartFindingLocatablesAroundPosition(
                mobileParty.Position.ToVec2(), radius);
            for (MobileParty prey = MobileParty.FindNextLocatable(ref data); prey != null;
                prey = MobileParty.FindNextLocatable(ref data))
            {
                if (prey == mobileParty || !prey.IsActive)
                {
                    continue;
                }
                // Only soft civilian prey; never other bandits/deserters, never a party in a settlement, and
                // never one already committed to a battle (our own encounter will pull it in when we arrive).
                if (!(prey.IsVillager || prey.IsCaravan) || prey.IsBandit)
                {
                    continue;
                }
                if (prey.CurrentSettlement != null || prey.MapEvent != null
                    || prey.IsCurrentlyAtSea != mobileParty.IsCurrentlyAtSea)
                {
                    continue;
                }

                float preyStrength = prey.Party.EstimatedStrength;
                if (myStrength < preyStrength * PartyStrengthRatio)
                {
                    continue;
                }

                AiHelper.GetBestNavigationTypeAndDistanceOfMobilePartyForMobileParty(
                    mobileParty, prey, out MobileParty.NavigationType navType, out float distance);
                if (navType == MobileParty.NavigationType.None || !(distance < radius))
                {
                    continue;
                }

                float distanceFactor = 1f - distance / radius;
                float baseScore = prey.IsCaravan ? CaravanHuntBaseScore : VillagerHuntBaseScore;
                float score = baseScore * distanceFactor;
                if (mobileParty.DefaultBehavior == AiBehavior.GoAroundParty && mobileParty.TargetParty == prey)
                {
                    score *= CommitmentBonus;
                }
                if (!(score > 0f))
                {
                    continue;
                }

                AIBehaviorData behaviorData = new AIBehaviorData(prey, AiBehavior.GoAroundParty, navType,
                    willGatherArmy: false, isFromPort: false, isTargetingPort: false);
                p.AddBehaviorScore((behaviorData, score));

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = prey;
                    bestKind = prey.IsCaravan ? "caravan" : "villagers";
                }
            }
        }

        /// <summary>
        /// Seeds a <see cref="AiBehavior.RaidSettlement"/> score for every nearby normal-state village whose
        /// mustering defence the band out-matches. Feeding this every think is what keeps an in-progress raid
        /// alive: the village being raided sits at distance ~0, so it keeps scoring highest and
        /// <c>AiPartyThinkBehavior</c> never swaps the raid out for the fallback patrol.
        /// </summary>
        private void ScoreRaidTargets(MobileParty mobileParty, float myStrength, PartyThinkParams p,
            ref IMapPoint bestTarget, ref float bestScore, ref string bestKind)
        {
            float radius = Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(
                mobileParty.NavigationCapability) * VillageSearchTownGapMultiple;
            if (!(radius > 0f))
            {
                return;
            }

            LocatableSearchData<Settlement> data = Settlement.StartFindingLocatablesAroundPosition(
                mobileParty.Position.ToVec2(), radius);
            for (Settlement settlement = Settlement.FindNextLocatable(ref data); settlement != null;
                settlement = Settlement.FindNextLocatable(ref data))
            {
                if (settlement == null || !settlement.IsVillage || settlement.Village == null)
                {
                    continue;
                }
                bool alreadyRaidingThis = mobileParty.DefaultBehavior == AiBehavior.RaidSettlement
                    && mobileParty.TargetSettlement == settlement;
                // Only a settled, un-contested village is a fresh raid target. The one exception is the
                // village we're already sacking -- keep scoring it so the raid isn't finalized out from
                // under us while it runs (its state is BeingRaided, not Normal).
                if (!alreadyRaidingThis)
                {
                    if (settlement.Village.VillageState != Village.VillageStates.Normal)
                    {
                        continue;
                    }
                    if (settlement.Party.MapEvent != null || settlement.SiegeEvent != null)
                    {
                        continue;
                    }
                }

                float defence = EstimateVillageDefence(settlement);
                if (myStrength < defence * RaidStrengthRatio)
                {
                    continue;
                }

                AiHelper.GetBestNavigationTypeAndAdjustedDistanceOfSettlementForMobileParty(
                    mobileParty, settlement, false, out MobileParty.NavigationType navType,
                    out float distance, out bool isFromPort);
                if (navType == MobileParty.NavigationType.None || !(distance < radius))
                {
                    continue;
                }

                float distanceFactor = 1f - distance / radius;
                float confidence = MBMath.ClampFloat(myStrength / (defence + 1f), 1f, MaxRaidConfidence);
                float score = RaidBaseScore * distanceFactor * confidence;
                if (alreadyRaidingThis)
                {
                    score *= CommitmentBonus;
                }
                if (!(score > 0f))
                {
                    continue;
                }

                AIBehaviorData behaviorData = new AIBehaviorData(settlement, AiBehavior.RaidSettlement, navType,
                    willGatherArmy: false, isFromPort, isTargetingPort: false);
                p.AddBehaviorScore((behaviorData, score));

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = settlement;
                    bestKind = "raid";
                }
            }
        }

        /// <summary>
        /// Rough defensive strength of a village for the raid gate: any garrison/militia/lord party actually
        /// standing in it, plus the levy it will muster when attacked (its <see cref="Settlement.Militia"/>
        /// count, weighted down because militia are weak). Deliberately cheap and target-relative -- it is
        /// only ever compared against the band's own strength, never reported as an absolute.
        /// </summary>
        private static float EstimateVillageDefence(Settlement settlement)
        {
            float defence = settlement.Militia * MilitiaStrengthPerMan;
            MBReadOnlyList<MobileParty> parties = settlement.Parties;
            if (parties != null)
            {
                for (int i = 0; i < parties.Count; i++)
                {
                    MobileParty party = parties[i];
                    if (party != null && (party.IsMilitia || party.IsGarrison || party.IsLordParty))
                    {
                        defence += party.Party.EstimatedStrength;
                    }
                }
            }
            return defence;
        }

        private static string PartyName(MobileParty mobileParty)
        {
            if (mobileParty == null)
            {
                return "?";
            }
            return mobileParty.Name != null ? mobileParty.Name.ToString() : mobileParty.StringId;
        }

        private static string TargetName(IMapPoint target)
        {
            if (target is MobileParty party)
            {
                return party.Name != null ? party.Name.ToString() : party.StringId;
            }
            if (target is Settlement settlement)
            {
                return settlement.Name != null ? settlement.Name.ToString() : settlement.StringId;
            }
            return "?";
        }
    }
}
