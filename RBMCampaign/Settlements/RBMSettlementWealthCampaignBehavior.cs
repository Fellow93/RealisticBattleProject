using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// Carries the settlement and citizen purses through a campaign: persists the treasury store, seeds
    /// anything that has none yet, and puts both on the settlement tooltip. A thin shell over
    /// <see cref="SettlementWealth"/>, matching the store-plus-behaviour split the other systems use.
    /// </summary>
    public class RBMSettlementWealthCampaignBehavior : CampaignBehaviorBase
    {
        /// <summary>
        /// Drops the previous campaign's treasuries before this one's save is read. See
        /// <see cref="SettlementWealth.Reset"/> for why this belongs in the constructor and nowhere else.
        /// </summary>
        public RBMSettlementWealthCampaignBehavior()
        {
            SettlementWealth.Reset();
        }

        public override void RegisterEvents()
        {
            // Fires for a new game and a loaded save alike; SyncData has already run by then, so any
            // settlement still without a treasury is one this save has never carried and gets seeded.
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);

            // A village's purse is vanilla's own gold field, which always holds a value, so it can only
            // be sized where we know that value is vanilla's seed and not something a campaign earned:
            // the new-game path. The same hook the economy behaviour seeds prosperity on, late enough
            // that hearths are built and linked.
            CampaignEvents.OnNewGameCreatedPartialFollowUpEndEvent.AddNonSerializedListener(this, OnNewGameCreatedFollowUpEnd);

            // The standing administration draws its wage once a day. Food for a town's staff rides with
            // the daily food pass instead; only the wage and a village's small ration are paid here.
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
        }

        private void OnDailyTickSettlement(Settlement settlement)
        {
            AdministrativeUpkeep.OnDailyTick(settlement);
            WealthTax.OnDailyTick(settlement);
            // After the day's charges, so these report against the purse they left behind.
            TradeTariff.FlushDaily(settlement);
            WorkshopPurse.FlushDaily(settlement);
            WorkshopDiagnostics.FlushDaily(settlement);
            TownStorage.FlushDaily(settlement);
            SettlementWealth.FlushDaily(settlement);
        }

        private void OnNewGameCreatedFollowUpEnd(CampaignGameStarter starter)
        {
            SettlementWealth.SeedVillagePurses();
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            SettlementWealth.InitializeAll();
            // Installed here, once the session is up: the game registers the settlement tooltip refresher
            // at startup, so re-registering now sticks for the whole process. See SettlementWealthTooltip
            // for why this is a re-registration rather than a Harmony patch.
            SettlementWealthTooltip.Install();
        }

        public override void SyncData(IDataStore dataStore)
        {
            SettlementWealth.SyncData(dataStore);
        }
    }
}
