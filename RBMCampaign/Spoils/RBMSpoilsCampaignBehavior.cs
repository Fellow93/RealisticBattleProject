using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
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
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, SpoilsPool.OnMobilePartyDestroyed);
            CampaignEvents.PlayerUpgradedTroopsEvent.AddNonSerializedListener(this, SpoilsPool.OnPlayerUpgradedTroops);
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            SpoilsLog.StartCampaignLog();
        }

        // A daily sweep of every party's purses. The party-screen transfer hook moves a purse when the
        // player marches troops off, but a stack can also leave by paths that touch no purse -- a dismissal,
        // a donation, an AI handover -- leaving its spoils orphaned under a stack that is no longer there.
        // Destroying a party collects its orphans, but a party that outlives its men (the player's, a
        // garrison) would hoard them forever, so every party is reconciled against its roster once a day.
        // Cheap: each PruneOrphans is a single dict pass that short-circuits on the party's key prefix.
        private void OnDailyTick()
        {
            if (!SpoilsPool.IsEnabled)
            {
                return;
            }
            foreach (MobileParty party in MobileParty.All)
            {
                if (party?.Party != null)
                {
                    SpoilsPool.PruneOrphans(party.Party);
                }
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
            SpoilsPool.SyncData(dataStore);
        }
    }
}
