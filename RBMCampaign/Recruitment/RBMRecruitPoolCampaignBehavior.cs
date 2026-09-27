using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// Carries each settlement's manpower pool (<see cref="RecruitPool"/>) through a campaign: persists it,
    /// refills it once a day, and writes the day's summary to the economy log. A thin shell over the
    /// store, matching the store-plus-behaviour split the other systems use.
    /// </summary>
    public class RBMRecruitPoolCampaignBehavior : CampaignBehaviorBase
    {
        /// <summary>
        /// Drops the previous campaign's pools before this one's save is read -- the store is keyed by
        /// settlement StringId, identical across campaigns, so a leak would hand B the values of A.
        /// Constructor, not OnSessionLaunched: on load, SyncData runs before RegisterEvents.
        /// </summary>
        public RBMRecruitPoolCampaignBehavior()
        {
            RecruitPool.Reset();
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        private void OnDailyTickSettlement(Settlement settlement)
        {
            RecruitPool.OnDailyTick(settlement);
        }

        private void OnDailyTick()
        {
            RecruitPool.LogDailySummary();
        }

        public override void SyncData(IDataStore dataStore)
        {
            RecruitPool.SyncData(dataStore);
        }
    }
}
