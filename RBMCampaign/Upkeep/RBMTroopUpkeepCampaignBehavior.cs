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
        }

        public override void SyncData(IDataStore dataStore)
        {
            TroopUpkeep.SyncData(dataStore);
        }
    }
}
