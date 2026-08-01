using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

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
            WealthTax.ResetForNewSession();
            MilitiaUpkeep.ResetForNewSession();
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
            // A castle earns first, then pays: income is minted before the day's upkeep and wealth tax
            // so those act on the post-income balance.
            CastleEconomy.OnDailyTick(settlement);
            AdministrativeUpkeep.OnDailyTick(settlement);
            // Arm the day's new militia, out here rather than in the DailyTick that grew them so a
            // village purse write is not caught inside VillageGoldStock's suppression window.
            MilitiaUpkeep.ChargePendingSpawn(settlement);
            WealthTax.OnDailyTick(settlement);
            // After the day's charges, so these report against the purse they left behind.
            TradeTariff.FlushDaily(settlement);
            WorkshopPurse.FlushDaily(settlement);
            WorkshopDiagnostics.FlushDaily(settlement);
            TownStorage.FlushDaily(settlement);
            RBMMarketPrices.LogDaily(settlement);
            SettlementWealth.FlushDaily(settlement);
        }

        // Whether this session began as a new campaign. Set on the new-game hook and read one hook
        // later; not serialized, so a save loaded later in the same process starts false again.
        private bool _newCampaign;

        // Whether the treasury store came back empty from the save. Recorded in SyncData on the load
        // path, before InitializeAll fills it; a save started without RBM campaign never wrote the store
        // and so loads it empty, which is the one signal that none of the new-game economy seeding ran
        // on it. Not serialized -- it describes this load, not the campaign.
        private bool _wealthStoreEmptyOnLoad;

        private void OnNewGameCreatedFollowUpEnd(CampaignGameStarter starter)
        {
            _newCampaign = true;
            SettlementWealth.SeedVillagePurses();
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            SettlementWealth.InitializeAll();

            // Deliberately NOT on the new-game hook beside the village purses, even though it is just as
            // much a new-game-only step. RBMEconomyCampaignBehavior re-seeds every town's prosperity on
            // that same event, and two listeners on one event run in behaviour registration order --
            // which would leave this reading prosperity on whichever scale happened to win. Here it is
            // unambiguously after the whole of OnNewGameCreated. Villages are safe where they are: their
            // seed reads Hearth, which nothing rewrites.
            if (_newCampaign)
            {
                SettlementWealth.SeedCitizenWealth();
            }
            // Installed here, once the session is up: the game registers the settlement tooltip refresher
            // at startup, so re-registering now sticks for the whole process. See SettlementWealthTooltip
            // for why this is a re-registration rather than a Harmony patch.
            SettlementWealthTooltip.Install();

            // A save that predates RBM campaign carried none of the store, so it loaded empty and never
            // got the new-game seeding (village purses, citizen wealth, treasuries, starting prosperity).
            // Its economy is half-built and cannot be repaired after the fact -- the seeds ride on
            // vanilla's own gold fields and there is no telling a seed from money later earned -- so all
            // that is left is to tell the player plainly.
            if (!_newCampaign && _wealthStoreEmptyOnLoad)
            {
                WarnUnseededSave();
            }
        }

        // Shown once, on loading a save that was not started with RBM campaign enabled. The save is
        // already loaded by the time this behaviour's OnSessionLaunched runs, so this is an unavoidable
        // heads-up rather than a gate: the economy cannot be seeded retroactively.
        private static void WarnUnseededSave()
        {
            InformationManager.ShowInquiry(new InquiryData(
                new TextObject("{=RBM_CAMP_001}RBM Campaign: incompatible save").ToString(),
                new TextObject("{=RBM_CAMP_002}This save was not started with RBM Campaign features enabled.{newline}{newline}The one-time economy setup (settlement treasuries, citizen wealth, village purses and starting prosperity) only runs when a campaign is CREATED with RBM Campaign on, and it cannot be applied to an existing save. This campaign will run with a half-built, unbalanced economy -- markets stuck poor, treasuries and prosperity wrong, spoils and upkeep skewed.{newline}{newline}To get the intended experience, start a NEW campaign with RBM Campaign enabled. You can keep playing this save, but the economy will not behave correctly.").ToString(),
                true, false, "OK", null, null, null),
                false, true);
        }

        public override void SyncData(IDataStore dataStore)
        {
            SettlementWealth.SyncData(dataStore);
            // The treasury store is written for every fief every session, so an empty load means the save
            // has never run under RBM campaign. Captured here, before InitializeAll seeds it on session
            // launch, so OnSessionLaunched can warn on a save that predates the module.
            if (dataStore.IsLoading)
            {
                _wealthStoreEmptyOnLoad = !SettlementWealth.HasAnyStoredWealth();
            }
            // The wealth-tax income owed but not yet paid to lords rides in the same store, so a save in
            // that window credits them on load rather than dropping coin the market already gave up.
            WealthTax.SyncData(dataStore);
        }
    }
}
