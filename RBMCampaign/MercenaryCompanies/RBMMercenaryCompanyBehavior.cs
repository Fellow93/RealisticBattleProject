using System.Collections.Generic;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Drives the mercenary companies (<see cref="MercenaryCompanies"/>, <see cref="MercenaryContracts"/>):
    /// creates the roster on the first campaign hour, keeps contracts and kingdom membership in step hourly,
    /// pays contracts and respawns lost companies daily, lets AI army leaders hire on <c>ArmyCreated</c>, and
    /// gives an unhired company something to do: recruit tavern mercenaries when understrength, otherwise hunt
    /// bandit parties it outmatches, otherwise vanilla's patrol around its home town. The roaming AI only adds
    /// scores to the shared think params (the RBMDeserterRaiderBehavior shape); it never issues a move itself.
    /// </summary>
    public class RBMMercenaryCompanyBehavior : CampaignBehaviorBase
    {
        // A company hunts only bandits it beats by this margin.
        private const float BanditStrengthRatio = 1.3f;
        // Bandit search radius, in multiples of vanilla's land encounter distance (deserters use 45).
        private const float HuntEncounterMultiple = 30f;
        // Recruit-town search radius, in average distances between neighbouring towns.
        private const float RecruitTownGapMultiple = 3f;
        // Scores run 2..5 by closeness: enough to beat vanilla's patrol, not an army call or a fight already on.
        private const float MinScore = 2f;
        private const float ScoreRange = 3f;
        private const float CommitmentBonus = 1.2f;

        // Armies created since the last hourly tick; their AI leaders consider hiring then, outside the army
        // creation call stack. Not saved: an army missed across a save simply hires no company.
        private readonly List<Army> _newArmies = new List<Army>();
        private bool _rosterChecked;

        public RBMMercenaryCompanyBehavior()
        {
            // Persisted stores are reset here, not on session launch: SyncData runs before RegisterEvents on load.
            MercenaryCompanies.Reset();
            MercenaryContracts.Reset();
            MercenaryCompanyPatches.ResetBlockLog();
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.ArmyCreated.AddNonSerializedListener(this, OnArmyCreated);
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            CampaignEvents.AiHourlyTickEvent.AddNonSerializedListener(this, OnAiHourlyTick);
            // Only to name the reason in the log; the contract itself is ended by the hourly reconcile.
            CampaignEvents.ArmyDispersed.AddNonSerializedListener(this, OnArmyDispersed);
            CampaignEvents.OnPartyLeftArmyEvent.AddNonSerializedListener(this, OnPartyLeftArmy);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnMobilePartyDestroyed);
            CampaignEvents.OnTroopsDesertedEvent.AddNonSerializedListener(this, OnTroopsDeserted);
        }

        private void OnTroopsDeserted(MobileParty party, TroopRoster deserters)
        {
            if (!MercenaryLog.IsEnabled || !MercenaryCompanies.IsCompanyParty(party) || deserters == null)
            {
                return;
            }
            List<string> types = new List<string>();
            for (int i = 0; i < deserters.Count; i++)
            {
                TroopRosterElement element = deserters.GetElementCopyAtIndex(i);
                types.Add(element.Number + " x " + element.Character?.StringId);
            }
            MercenaryLog.Log("DESERT", party, deserters.TotalManCount + " deserted [" + string.Join(", ", types) + "]"
                + " | wage " + party.TotalWage + "d, limit " + party.PaymentLimit + " (limited " + party.HasLimitedWage() + ")"
                + ", leader gold " + party.LeaderHero?.Gold + "d, morale " + party.Morale.ToString("0.0")
                + ", unpaid wages " + party.HasUnpaidWages.ToString("0.00") + ", now " + party.MemberRoster.TotalManCount + " men");
        }

        /// <summary>One STATUS line per company a day: size, wages (full and paid), contract, gold, morale, food.</summary>
        private static void LogDailyStatus()
        {
            if (!MercenaryLog.IsEnabled)
            {
                return;
            }
            foreach (Clan clan in MercenaryCompanies.LiveCompanies())
            {
                MobileParty party = MercenaryCompanies.ActiveParty(clan);
                if (party == null)
                {
                    MercenaryLog.Log("STATUS", clan, "no party, leader " + (clan.Leader != null ? clan.Leader.Name + " " + clan.Leader.HeroState
                        + (clan.Leader.IsPrisoner ? " (prisoner)" : "") : "none") + ", gold " + clan.Gold + "d");
                    continue;
                }
                bool hired = MercenaryContracts.IsHired(clan);
                MercenaryLog.Log("STATUS", party, party.MemberRoster.TotalManCount + "/" + party.Party.PartySizeLimit + " men"
                    + " (" + party.MemberRoster.TotalWounded + " wounded), wage full " + MercenaryCompanies.FullWage(party)
                    + "d, paid " + party.TotalWage + "d (" + (hired ? "full, under contract" : "half, idle") + ")"
                    + ", limit " + party.PaymentLimit + ", leader gold " + party.LeaderHero?.Gold + "d, morale "
                    + party.Morale.ToString("0.0") + ", food " + party.GetNumDaysForFoodToLast() + " days"
                    + (party.Army != null ? ", in army of " + party.Army.LeaderParty?.LeaderHero?.Name : ""));
            }
        }

        private void OnMobilePartyDestroyed(MobileParty party, PartyBase destroyer)
        {
            if (party == null)
            {
                return;
            }
            _lastRoam.Remove(party);
            if (!MercenaryCompanies.IsCompanyParty(party))
            {
                return;
            }
            MercenaryLog.Log("GONE", party, "party destroyed" + (destroyer != null ? " by " + destroyer.Name : "")
                + ", leader " + (party.ActualClan?.Leader != null ? party.ActualClan.Leader.Name + (party.ActualClan.Leader.IsPrisoner ? " captured" : party.ActualClan.Leader.IsAlive ? " free" : " dead") : "?"));
        }

        private void OnArmyDispersed(Army army, Army.ArmyDispersionReason reason, bool isPlayersArmy)
        {
            if (army == null || !MercenaryCompanies.IsEnabled)
            {
                return;
            }
            string leader = army.LeaderParty?.LeaderHero != null ? army.LeaderParty.LeaderHero.Name.ToString() : "?";
            foreach (MobileParty party in army.Parties)
            {
                if (MercenaryCompanies.IsCompanyParty(party))
                {
                    MercenaryContracts.NoteArmyEnded(party, "army of " + leader + " dispersed (" + reason + ")");
                }
            }
        }

        private void OnPartyLeftArmy(MobileParty party, Army army)
        {
            if (MercenaryCompanies.IsEnabled && MercenaryCompanies.IsCompanyParty(party))
            {
                string leader = army?.LeaderParty?.LeaderHero != null ? army.LeaderParty.LeaderHero.Name.ToString() : "?";
                MercenaryContracts.NoteArmyEnded(party, "left the army of " + leader
                    + (party.IsActive ? "" : " (party destroyed)"));
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
            MercenaryCompanies.SyncData(dataStore);
            MercenaryContracts.SyncData(dataStore);
        }

        // Set once the disband cleanup finds no company left this session (feature off).
        private bool _cleanupDone;

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            RunCleanup();
        }

        /// <summary>With the feature switched off, takes every company apart (see MercenaryCompanies.DisbandAll).</summary>
        private void RunCleanup()
        {
            if (_cleanupDone || !MercenaryCompanies.CleanupActive)
            {
                return;
            }
            _cleanupDone = MercenaryCompanies.DisbandAll();
        }

        private void OnHourlyTick()
        {
            if (!MercenaryCompanies.IsEnabled)
            {
                RunCleanup();
                return;
            }
            if (!_rosterChecked)
            {
                _rosterChecked = true;
                MercenaryCompanies.EnsureRoster();
                foreach (Clan clan in MercenaryCompanies.LiveCompanies())
                {
                    MercenaryCompanies.ApplyMercenaryIdentity(clan);
                }
            }
            if (_newArmies.Count > 0)
            {
                List<Army> armies = new List<Army>(_newArmies);
                _newArmies.Clear();
                foreach (Army army in armies)
                {
                    MercenaryContracts.TryAiHire(army);
                }
            }
            foreach (Clan clan in MercenaryCompanies.LiveCompanies())
            {
                MercenaryContracts.Reconcile(clan);
                MobileParty party = MercenaryCompanies.ActiveParty(clan);
                if (party != null && party.MapEvent == null)
                {
                    MercenaryCompanies.PurgeNonMercenaries(party);
                }
            }
        }

        private void OnDailyTick()
        {
            if (!MercenaryCompanies.IsEnabled)
            {
                return;
            }
            MercenaryContracts.PayDaily();
            MercenaryCompanies.EnsureRoster();
            MercenaryCompanies.CheckRespawns();
            foreach (Clan clan in MercenaryCompanies.LiveCompanies())
            {
                MercenaryCompanies.ApplyMercenaryIdentity(clan);
            }
            LogDailyStatus();
        }

        private void OnArmyCreated(Army army)
        {
            // Witness line for every army the event reports, the player's too: an empty log then really means
            // the event never fired.
            MercenaryLog.Log("ARMY-NEW", "", "army created: leader " + (army?.LeaderParty?.LeaderHero?.Name?.ToString() ?? "?")
                + ", kingdom " + (army?.Kingdom?.Name?.ToString() ?? "?") + ", type " + army?.ArmyType
                + ", parties " + (army?.Parties?.Count ?? 0)
                + (army?.LeaderParty == MobileParty.MainParty ? " (player's army)" : " (queued for the hiring hour)"));
            if (MercenaryCompanies.IsEnabled && army != null && army.LeaderParty != MobileParty.MainParty)
            {
                _newArmies.Add(army);
            }
        }

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            if (party == null || settlement == null || !settlement.IsTown || settlement.IsUnderSiege
                || party.MapEvent != null || !MercenaryCompanies.IsCompanyParty(party))
            {
                return;
            }
            MercenaryCompanies.RecruitTavernMercenaries(party, settlement);
        }

        private void OnAiHourlyTick(MobileParty party, PartyThinkParams p)
        {
            if (!MercenaryCompanies.IsCompanyParty(party) || party.LeaderHero == null || party.ActualClan.Kingdom != null
                || party.Army != null || party.MapEvent != null || party.BesiegedSettlement != null
                || party.IsCurrentlyAtSea || !party.HasLandNavigationCapability)
            {
                return;
            }
            bool understrength = party.Party.NumberOfAllMembers
                < party.Party.PartySizeLimit * MercenaryCompanies.RecruitTargetShare;
            IMapPoint target;
            float score;
            string kind;
            if (understrength && party.LeaderHero.Gold > MercenaryCompanies.RecruitGoldFloor
                && ScoreRecruitTown(party, p, out target, out score))
            {
                kind = "recruit at";
            }
            else if (ScoreBanditHunt(party, p, out target, out score))
            {
                kind = "hunt bandits";
            }
            else
            {
                kind = "patrol (vanilla)";
            }
            LogRoam(party, kind, target, score);
        }

        // Transient: the last roaming choice logged per company, so ROAM is written only when it changes.
        private readonly Dictionary<MobileParty, string> _lastRoam = new Dictionary<MobileParty, string>();

        private void LogRoam(MobileParty party, string kind, IMapPoint target, float score)
        {
            if (!MercenaryLog.IsEnabled)
            {
                return;
            }
            string what = kind + (target != null ? " " + target.Name : "");
            string previous;
            if (_lastRoam.TryGetValue(party, out previous) && previous == what)
            {
                return;
            }
            _lastRoam[party] = what;
            MercenaryLog.Log("ROAM", party, what + (target != null ? "  score " + score.ToString("0.0") : "")
                + " | " + party.Party.NumberOfAllMembers + "/" + party.Party.PartySizeLimit + " men, str "
                + party.Party.EstimatedStrength.ToString("0") + ", gold " + party.LeaderHero.Gold + "d");
        }

        /// <summary>The nearest town with tavern mercenaries the leader can pay for, as a GoToSettlement score.</summary>
        private static bool ScoreRecruitTown(MobileParty party, PartyThinkParams p, out IMapPoint target, out float score)
        {
            target = null;
            score = 0f;
            RecruitmentCampaignBehavior recruitment = Campaign.Current.GetCampaignBehavior<RecruitmentCampaignBehavior>();
            float radius = Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(
                MobileParty.NavigationType.Default) * RecruitTownGapMultiple;
            if (recruitment == null || !(radius > 0f))
            {
                return false;
            }
            Hero leader = party.LeaderHero;
            Settlement best = null;
            MobileParty.NavigationType bestNav = MobileParty.NavigationType.None;
            bool bestFromPort = false;
            float bestScore = 0f;
            LocatableSearchData<Settlement> data = Settlement.StartFindingLocatablesAroundPosition(party.Position.ToVec2(), radius);
            for (Settlement settlement = Settlement.FindNextLocatable(ref data); settlement != null;
                settlement = Settlement.FindNextLocatable(ref data))
            {
                // The town it is standing in has already sold it what it had (SettlementEntered).
                if (!settlement.IsTown || settlement.IsUnderSiege || settlement == party.CurrentSettlement
                    || settlement.MapFaction == null || FactionManager.IsAtWarAgainstFaction(party.MapFaction, settlement.MapFaction))
                {
                    continue;
                }
                RecruitmentCampaignBehavior.TownMercenaryData mercenaries = recruitment.GetMercenaryData(settlement.Town);
                if (mercenaries == null || mercenaries.TroopType == null || mercenaries.Number <= 0
                    || !MercenaryCompanies.IsMercenaryTroop(mercenaries.TroopType))
                {
                    continue;
                }
                // Same wage-reserve test the hire itself applies: no trip to a town where it could buy no one.
                int price = Campaign.Current.Models.PartyWageModel.GetTroopRecruitmentCost(mercenaries.TroopType, leader).RoundedResultNumber;
                if (MercenaryCompanies.AffordableRecruits(party, mercenaries.TroopType, MathF.Max(1, price)) < 1)
                {
                    continue;
                }
                AiHelper.GetBestNavigationTypeAndAdjustedDistanceOfSettlementForMobileParty(party, settlement, false,
                    out MobileParty.NavigationType navType, out float distance, out bool isFromPort);
                if (navType == MobileParty.NavigationType.None || !(distance < radius))
                {
                    continue;
                }
                float townScore = MinScore + ScoreRange * (1f - distance / radius);
                if (townScore > bestScore)
                {
                    bestScore = townScore;
                    best = settlement;
                    bestNav = navType;
                    bestFromPort = isFromPort;
                }
            }
            if (best == null || leader == null)
            {
                return false;
            }
            AIBehaviorData behaviorData = new AIBehaviorData(best, AiBehavior.GoToSettlement, bestNav,
                willGatherArmy: false, bestFromPort, isTargetingPort: false);
            if (p.TryGetBehaviorScore(in behaviorData, out float existing))
            {
                p.SetBehaviorScore(in behaviorData, existing + bestScore);
            }
            else
            {
                p.AddBehaviorScore((behaviorData, bestScore));
            }
            target = best;
            score = bestScore;
            return true;
        }

        /// <summary>The best bandit party on land it clearly outmatches, as a GoAroundParty (engage) score.</summary>
        private static bool ScoreBanditHunt(MobileParty party, PartyThinkParams p, out IMapPoint target, out float score)
        {
            target = null;
            score = 0f;
            float radius = Campaign.Current.Models.EncounterModel.NeededMaximumLandDistanceForEncounteringMobileParty
                * HuntEncounterMultiple;
            float myStrength = party.Party.EstimatedStrength;
            if (!(radius > 0f) || !(myStrength > 0f))
            {
                return false;
            }
            MobileParty best = null;
            MobileParty.NavigationType bestNav = MobileParty.NavigationType.None;
            float bestScore = 0f;
            LocatableSearchData<MobileParty> data = MobileParty.StartFindingLocatablesAroundPosition(party.Position.ToVec2(), radius);
            for (MobileParty prey = MobileParty.FindNextLocatable(ref data); prey != null; prey = MobileParty.FindNextLocatable(ref data))
            {
                if (prey == party || !prey.IsActive || !prey.IsBandit || prey.CurrentSettlement != null
                    || prey.IsCurrentlyAtSea || prey.MapEvent != null
                    || myStrength < prey.Party.EstimatedStrength * BanditStrengthRatio)
                {
                    continue;
                }
                AiHelper.GetBestNavigationTypeAndDistanceOfMobilePartyForMobileParty(party, prey,
                    out MobileParty.NavigationType navType, out float distance);
                if (navType == MobileParty.NavigationType.None || !(distance < radius))
                {
                    continue;
                }
                float preyScore = MinScore + ScoreRange * (1f - distance / radius);
                if (party.DefaultBehavior == AiBehavior.GoAroundParty && party.TargetParty == prey)
                {
                    preyScore *= CommitmentBonus;
                }
                if (preyScore > bestScore)
                {
                    bestScore = preyScore;
                    best = prey;
                    bestNav = navType;
                }
            }
            if (best == null)
            {
                return false;
            }
            AIBehaviorData behaviorData = new AIBehaviorData(best, AiBehavior.GoAroundParty, bestNav,
                willGatherArmy: false, isFromPort: false, isTargetingPort: false);
            p.AddBehaviorScore((behaviorData, bestScore));
            target = best;
            score = bestScore;
            return true;
        }
    }
}
