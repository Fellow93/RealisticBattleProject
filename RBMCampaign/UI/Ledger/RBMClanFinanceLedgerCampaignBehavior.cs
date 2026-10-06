using TaleWorlds.CampaignSystem;

namespace RBMCampaign
{
    // Thin persistence + clock shell over RBMClanFinanceLedger, feeding the Ledger's Clan finances tab. The
    // flows themselves are recorded by the Harmony hooks nested in RBMClanFinanceLedger; this only starts
    // tracking on session launch, closes a finished day every campaign hour (so a day on which no gold moved
    // still ends on time), and saves the record.
    public class RBMClanFinanceLedgerCampaignBehavior : CampaignBehaviorBase
    {
        // Drops the previous campaign's record before this one's save is read (ctor runs at OnGameStart,
        // ahead of SyncData), matching the store-reset pattern the other RBM behaviors use.
        public RBMClanFinanceLedgerCampaignBehavior()
        {
            RBMClanFinanceLedger.Reset();
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            RBMClanFinanceLedger.RollIfNeeded();
        }

        private void OnHourlyTick()
        {
            RBMClanFinanceLedger.RollIfNeeded();
        }

        public override void SyncData(IDataStore dataStore)
        {
            RBMClanFinanceLedger.SyncData(dataStore);
        }
    }
}
