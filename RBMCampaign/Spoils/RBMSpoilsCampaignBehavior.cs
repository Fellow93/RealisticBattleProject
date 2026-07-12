using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace RBMCampaign
{
    public class RBMSpoilsCampaignBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            // Fires for both a new game and a loaded save, so each play session rolls the spoils log
            // over to a fresh timestamped file with its config dumped at the top.
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, SpoilsPool.OnMapEventEnded);
            CampaignEvents.RaidCompletedEvent.AddNonSerializedListener(this, SpoilsPool.OnRaidCompleted);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, SpoilsPool.OnSettlementCaptured);
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, SpoilsPool.OnDailyTickParty);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, SpoilsPool.OnMobilePartyDestroyed);
            CampaignEvents.PlayerUpgradedTroopsEvent.AddNonSerializedListener(this, SpoilsPool.OnPlayerUpgradedTroops);
            // A stack mustered from a village or town brings a few days' maintenance in its purse.
            CampaignEvents.OnTroopRecruitedEvent.AddNonSerializedListener(this, SpoilsPool.OnTroopRecruited);
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
        }
    }
}
