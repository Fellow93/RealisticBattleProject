using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    // Thin persistence + event shell over RBMVillageLedger: snapshots the four per-village metrics
    // once a day and records raid events, feeding the Ledger's Villages tab. Villager dispatch/arrival
    // events are recorded from the existing VillagerEscort Harmony postfixes, not here.
    public class RBMVillageLedgerCampaignBehavior : CampaignBehaviorBase
    {
        // Drops the previous campaign's history before this one's save is read -- the constructor
        // runs at OnGameStart, ahead of SyncData, matching the store-reset pattern the other
        // RBM behaviors use (see RBMSettlementWealthCampaignBehavior).
        public RBMVillageLedgerCampaignBehavior()
        {
            RBMVillageLedger.Reset();
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.VillageBeingRaided.AddNonSerializedListener(this, OnVillageBeingRaided);
            CampaignEvents.VillageLooted.AddNonSerializedListener(this, OnVillageLooted);
        }

        private void OnDailyTick()
        {
            RBMVillageLedger.RecordDailySnapshot();
        }

        private void OnVillageBeingRaided(Village village)
        {
            if (village != null)
            {
                RBMVillageLedger.AddEvent(village.Settlement, RBMVillageLedger.EvRaidStart);
            }
        }

        private void OnVillageLooted(Village village)
        {
            if (village != null)
            {
                RBMVillageLedger.AddEvent(village.Settlement, RBMVillageLedger.EvLooted);
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
            RBMVillageLedger.SyncData(dataStore);
        }
    }
}
