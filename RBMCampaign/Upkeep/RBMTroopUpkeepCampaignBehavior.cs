using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace RBMCampaign
{
    public class RBMTroopUpkeepCampaignBehavior : CampaignBehaviorBase
    {
        /// <summary>Clears the last campaign's stores before this one's save is read.</summary>
        public RBMTroopUpkeepCampaignBehavior()
        {
            TroopUpkeep.Reset();
        }

        public override void RegisterEvents()
        {
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, TroopUpkeep.OnSettlementEntered);
            CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, TroopUpkeep.OnHourlyTickParty);
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, TroopUpkeep.OnDailyTickParty);
            // A field party out of food buries a share of its wounded each day. See FiefStarvation.
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, FiefStarvation.OnDailyTickParty);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, TroopUpkeep.OnMobilePartyDestroyed);
        }

        public override void SyncData(IDataStore dataStore)
        {
            TroopUpkeep.SyncData(dataStore);
        }
    }
}
