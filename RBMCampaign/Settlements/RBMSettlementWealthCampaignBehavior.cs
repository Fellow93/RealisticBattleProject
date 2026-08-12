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
            // The garrison's maintenance leg -- its wage is billed through the finance model (see
            // GarrisonUpkeep.GarrisonWageSharePatch); this is the kit-value half, charged here so it lands
            // on the post-income treasury with the day's other upkeep.
            GarrisonUpkeep.ChargeMaintenance(settlement);
            // Arm the day's new militia, out here rather than in the DailyTick that grew them so a
            // village purse write is not caught inside VillageGoldStock's suppression window. The refund
            // for a watch shed to the affordability floor rides alongside, out of the same window, returning
            // its recovered kit value to the funding pot.
            MilitiaUpkeep.ChargePendingSpawn(settlement);
            MilitiaUpkeep.RefundPendingDecline(settlement);
            // Grow the garrison off the fief's wealth, after its upkeep and its militia are paid, so it
            // recruits from genuine surplus and the base local defence (militia) is funded first.
            GarrisonRecruitCost.GrowGarrison(settlement);
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

        // The one durable proof that a campaign was CREATED with RBM campaign on and so got its
        // new-game economy seeding. Stamped true on the new-game hook and serialized, it rides in the
        // save for good -- a save started without RBM campaign never wrote it and loads it false, no
        // matter how many times it is later opened under RBM. This is the only provenance signal that
        // survives: every other trace of the seeding (village purses, citizen wealth, treasuries,
        // prosperity) rides on vanilla's own fields, which a later load re-touches and so cannot be
        // told from a save that always had them.
        private bool _campaignSeeded;

        private void OnNewGameCreatedFollowUpEnd(CampaignGameStarter starter)
        {
            _newCampaign = true;
            _campaignSeeded = true;
            SettlementWealth.SeedVillagePurses();
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            SettlementWealth.InitializeAll();

            // Deliberately NOT on the new-game hook beside the village purses, even though both are just as
            // much new-game-only steps. RBMEconomyCampaignBehavior re-seeds every town's prosperity on that
            // same event, and two listeners on one event run in behaviour registration order -- which would
            // leave these reading prosperity on whichever scale happened to win. Here they are unambiguously
            // after the whole of OnNewGameCreated, so prosperity is final before either is read.
            //
            //   * CITIZEN WEALTH reads prosperity directly.
            //   * INITIAL MILITIA is seeded at each fortification's growth-curve equilibrium (see
            //     MilitiaUpkeep.EquilibriumMilitia), which reads prosperity for its intake -- so it carries
            //     the same ordering dependency and must run here, after the prosperity re-seed, not on the
            //     new-game hook where it would race it. Villages are safe either way (their seed reads
            //     Hearth, which nothing rewrites), but the whole seed is kept together here for clarity.
            if (_newCampaign)
            {
                SettlementWealth.SeedCitizenWealth();
                MilitiaUpkeep.SeedInitialMilitia();
            }
            // Installed here, once the session is up: the game registers the settlement tooltip refresher
            // at startup, so re-registering now sticks for the whole process. See SettlementWealthTooltip
            // for why this is a re-registration rather than a Harmony patch.
            SettlementWealthTooltip.Install();

            // A loaded save with no seed stamp was never created as an RBM campaign, so none of the
            // new-game seeding (village purses, citizen wealth, treasuries, starting prosperity) ran on
            // it. Its economy is half-built and cannot be repaired after the fact -- the seeds ride on
            // vanilla's own gold fields and there is no telling a seed from money later earned -- so all
            // that is left is to tell the player plainly. (_newCampaign guards a brand-new game, whose
            // stamp is only set moments earlier on the new-game hook.)
            if (!_newCampaign && !_campaignSeeded)
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
            // The provenance stamp. A save that predates this key -- one never created as an RBM campaign,
            // or made before the stamp existed -- has no entry to read, so the load leaves the field at
            // its fresh-instance false, and OnSessionLaunched warns. A campaign created under RBM wrote
            // true on its new-game hook and carries it here for the life of the save.
            dataStore.SyncData("RBM_campaignSeeded", ref _campaignSeeded);
            // The wealth-tax income owed but not yet paid to lords rides in the same store, so a save in
            // that window credits them on load rather than dropping coin the market already gave up.
            WealthTax.SyncData(dataStore);
        }
    }
}
