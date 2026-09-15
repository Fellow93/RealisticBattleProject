using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace RBMCampaign
{
    /// <summary>
    /// Drives the intra-kingdom supply-caravan system: a weekly dispatch pass that puts caravans on the
    /// road, cleanup when one is destroyed, and the save of the register that remembers them. The arrival
    /// sale and vanilla-suppression are Harmony patches (<see cref="RBMCaravanArrival"/>) and need no
    /// wiring here. A thin shell over <see cref="RBMCaravanRegister"/> and <see cref="RBMCaravanDispatch"/>,
    /// matching the store-plus-behaviour split the other RBM systems use.
    /// </summary>
    public class RBMCaravanBehavior : CampaignBehaviorBase
    {
        /// <summary>
        /// Drops the previous campaign's caravans before this one's save is read. In the constructor for
        /// the same reason every RBM store resets there -- see <see cref="SpoilsPool.Reset"/>.
        /// </summary>
        public RBMCaravanBehavior()
        {
            RBMCaravanRegister.Reset();
            RBMCaravanInvestment.Reset();
        }

        // The dispatcher runs on this cadence rather than every day, so caravans go out in modest waves.
        private const int DispatchIntervalDays = 2;
        private int _daysSinceDispatch;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnMobilePartyDestroyed);
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            CaravanLog.StartCampaignLog();
        }

        // Every DispatchIntervalDays the dispatcher looks for a valid surplus→shortage route within a
        // kingdom and sends a caravan if it finds one; a pass with no valid target simply sends nothing.
        private void OnDailyTick()
        {
            if (++_daysSinceDispatch < DispatchIntervalDays)
            {
                return;
            }
            _daysSinceDispatch = 0;
            RBMCaravanDispatch.RunDispatch();
        }

        private void OnMobilePartyDestroyed(MobileParty party, PartyBase destroyer)
        {
            RBMCaravanRegister.OnMobilePartyDestroyed(party, destroyer);
        }

        public override void SyncData(IDataStore dataStore)
        {
            RBMCaravanRegister.SyncData(dataStore);
            RBMCaravanInvestment.SyncData(dataStore);
        }
    }
}
