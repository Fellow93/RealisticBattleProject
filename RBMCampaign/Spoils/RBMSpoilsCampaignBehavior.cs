using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

namespace RBMCampaign
{
    public class RBMSpoilsCampaignBehavior : CampaignBehaviorBase
    {
        /// <summary>Clears the last campaign's purses before this one's save is read.</summary>
        public RBMSpoilsCampaignBehavior()
        {
            SpoilsPool.Reset();
            // Same reset-before-load ordering: drop the previous campaign's per-party upgrade caps so a new
            // game starts uncapped and only a real save repopulates them.
            PartyUpgradeBudget.Reset();
            // And the previous campaign's event-gold record, for the same reason.
            ClanEventGoldLedger.Reset();
        }

        public override void RegisterEvents()
        {
            // Fires for both a new game and a loaded save, so each play session rolls the spoils log
            // over to a fresh timestamped file with its config dumped at the top.
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, SpoilsPool.OnMapEventEnded);
            CampaignEvents.RaidCompletedEvent.AddNonSerializedListener(this, SpoilsPool.OnRaidCompleted);
            // Snapshots the besieging parties of every besieged fief (so the sack at capture can pay them
            // all after the camp is gone) and bleeds a besieged castle's treasury a little each day.
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, SpoilsPool.OnBesiegedFortificationDailyTick);
            // The sack of a stormed fief hangs off the aftermath its conqueror chose; the owner change is
            // only half the handshake, since the two fire in either order (see OnSettlementCaptured).
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, SpoilsPool.OnSettlementCaptured);
            CampaignEvents.OnSiegeAftermathAppliedEvent.AddNonSerializedListener(this, SpoilsPool.OnSiegeAftermathApplied);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, SpoilsPool.OnHourlyTickSackSweep);
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, SpoilsPool.OnDailyTickParty);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, SpoilsPool.OnMobilePartyDestroyed);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, PartyUpgradeBudget.OnMobilePartyDestroyed);
            // Drop the destroyed party's cached supply-town payee too (keyed by MobileParty), so a long
            // session does not retain a dead party per its last upgrade-maintenance charge.
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, UpgradeSupply.OnMobilePartyDestroyed);
            CampaignEvents.PlayerUpgradedTroopsEvent.AddNonSerializedListener(this, SpoilsPool.OnPlayerUpgradedTroops);
            // A stack mustered from a village or town brings a few days' maintenance in its purse.
            // OnTroopRecruited is the AI/action path (carries the settlement); OnUnitRecruited is the
            // player's recruit screen (one man at a time into the main party, no settlement arg).
            CampaignEvents.OnTroopRecruitedEvent.AddNonSerializedListener(this, SpoilsPool.OnTroopRecruited);
            CampaignEvents.OnUnitRecruitedEvent.AddNonSerializedListener(this, SpoilsPool.OnUnitRecruited);
            // An executed captive (player execution or a v1.5 blood feud) is stripped of his kit like a
            // ransomed one; must run BEFORE the kill, while his captor party is still known.
            CampaignEvents.BeforeHeroKilledEvent.AddNonSerializedListener(this, SpoilsPool.OnBeforeHeroKilled);
            // A new campaign's armies -- lords', garrisons, militias, caravans and, with an advanced start,
            // the player's -- were raised without a recruit event; give them the recruit seed once character
            // creation is done.
            CampaignEvents.OnCharacterCreationIsOverEvent.AddNonSerializedListener(this, OnCharacterCreationIsOver);
            // A caravan's guards are hired as it is formed, whoever forms it, and a rebellion's lord parties
            // (and a minor faction's respawn) are handed their template's men; the roster is full by now. A
            // major-clan lord respawned in play opens with no men -- he levies them from a garrison afterwards,
            // purse and all (LordRespawn) -- and a player-clan party with only its leader, so those seed
            // nothing. A new game's lord parties get more men after this; SeedStartingArmies tops those up.
            CampaignEvents.MobilePartyCreated.AddNonSerializedListener(this, OnMobilePartyCreated);
        }

        private void OnMobilePartyCreated(MobileParty mobileParty)
        {
            if (mobileParty != null && (mobileParty.IsCaravan || mobileParty.IsLordParty))
            {
                SpoilsPool.SeedGrowthSince(mobileParty.Party, null);
            }
        }

        /// <summary>
        /// Raised ten times (index 0..9) from v1.5. The advanced start fills the main party at index 8, so
        /// the seed waits for the last pass, which also keeps it to one run per new game. A loaded save never
        /// comes through here.
        /// </summary>
        private void OnCharacterCreationIsOver(int index)
        {
            if (index != 9)
            {
                return;
            }
            SpoilsPool.SeedStartingArmies();
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            SpoilsLog.StartCampaignLog();
            // Sweep out any purse a save made before villagers were exempted left on a villager party;
            // its owner can no longer spend or prune it, so it would otherwise linger for the save's life.
            SpoilsPool.PruneExemptParties();
        }

        public override void SyncData(IDataStore dataStore)
        {
            SpoilsPool.SyncData(dataStore);
            PartyUpgradeBudget.SyncData(dataStore);
            ClanEventGoldLedger.SyncData(dataStore);
        }
    }
}
