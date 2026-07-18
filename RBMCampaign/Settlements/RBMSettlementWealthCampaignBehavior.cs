using TaleWorlds.CampaignSystem;

namespace RBMCampaign
{
    /// <summary>
    /// Carries the per-settlement rural and urban wealth stores through a campaign: persists them, and
    /// seeds any settlement that has none yet once the session is up. A thin shell over
    /// <see cref="SettlementWealth"/>, matching the store-plus-behaviour split the other systems use.
    /// </summary>
    public class RBMSettlementWealthCampaignBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            // Fires for a new game and a loaded save alike; SyncData has already run by then, so any
            // settlement still without a value is one this save has never carried and gets seeded.
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            SettlementWealth.InitializeAll();
            // The settlement-tooltip wrapper is installed here, once the session is up: the game registers the
            // settlement tooltip refresher at startup, so re-registering now sticks for the whole process. See
            // SettlementWealthTooltip for why this is a re-registration rather than a Harmony patch.
            SettlementWealthTooltip.Install();
        }

        public override void SyncData(IDataStore dataStore)
        {
            SettlementWealth.SyncData(dataStore);
        }
    }
}
