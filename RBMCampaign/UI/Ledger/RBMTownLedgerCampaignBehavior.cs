using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;

namespace RBMCampaign
{
    // Thin persistence + event shell over RBMTownLedger: snapshots the seven per-town metrics once a
    // day and records siege/capture events, feeding the Ledger's Towns tab. Market-buy flow (party and
    // caravan spending) is recorded from the SellItemsAction patch nested in RBMTownLedger; villager
    // deliveries from the VillagerDelivery hook -- neither runs here.
    public class RBMTownLedgerCampaignBehavior : CampaignBehaviorBase
    {
        // Drops the previous campaign's history before this one's save is read (ctor runs at OnGameStart,
        // ahead of SyncData), matching the store-reset pattern the other RBM behaviors use.
        public RBMTownLedgerCampaignBehavior()
        {
            RBMTownLedger.Reset();
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.OnSiegeEventStartedEvent.AddNonSerializedListener(this, OnSiegeStarted);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
        }

        private void OnDailyTick()
        {
            RBMTownLedger.RecordDailySnapshot();
        }

        private void OnSiegeStarted(SiegeEvent siegeEvent)
        {
            Settlement s = siegeEvent != null ? siegeEvent.BesiegedSettlement : null;
            if (s != null && s.IsTown)
            {
                RBMTownLedger.AddEvent(s, RBMTownLedger.EvSiege);
            }
        }

        private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner,
            Hero capturerHero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (settlement != null && settlement.IsTown)
            {
                RBMTownLedger.AddEvent(settlement, RBMTownLedger.EvCaptured);
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
            RBMTownLedger.SyncData(dataStore);
        }
    }
}
