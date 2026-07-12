using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace RBMCampaign
{
    public class RBMTroopUpkeepCampaignBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, TroopUpkeep.OnSettlementEntered);
            CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, TroopUpkeep.OnHourlyTickParty);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, TroopUpkeep.OnMobilePartyDestroyed);
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, MilitiaUpkeep.OnDailyTickSettlement);
            CampaignEvents.OnItemProducedEvent.AddNonSerializedListener(this, ProductionUpkeep.OnItemProduced);
        }

        public override void SyncData(IDataStore dataStore)
        {
            TroopUpkeep.SyncData(dataStore);
        }
    }
}
