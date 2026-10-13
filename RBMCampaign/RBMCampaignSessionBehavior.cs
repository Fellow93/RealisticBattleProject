using RBMAutoResolve;
using RBMConfig.Logging;
using TaleWorlds.CampaignSystem;

namespace RBMCampaign
{
    /// <summary>
    /// RBM Campaign's session-wide housekeeping: clears the module's static caches when a session launches and pushes
    /// every buffered log to disk once an hour and before a save. It used to ride on RBMSimulationCampaignBehavior,
    /// which moved to its own module (RBMAutoResolve) and is registered only while the auto-resolve is on.
    ///
    /// Registered by RBM.SubModule under rbmCampaignEnabled, in the slot that behavior held (after the spoils and
    /// upkeep behaviors, before the rest), so every OnSessionLaunched that ran after these resets still does --
    /// RBMSettlementWealthCampaignBehavior's seed counts on the hearth cache being clean already.
    /// </summary>
    public class RBMCampaignSessionBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);

            // Every campaign log writes into a buffered file handle (BufferedLogWriter) instead of reopening its
            // file per line. Push the buffers to disk once an in-game hour and before every save, so the files can
            // be read while playing and a crash loses at most an hour of lines. Game end and module unload close the
            // files (RBM.SubModule). The auto-resolve log also flushes itself, for when this module is off.
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, BufferedLogWriter.FlushAll);
            CampaignEvents.OnBeforeSaveEvent.AddNonSerializedListener(this, BufferedLogWriter.FlushAll);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            // The strategic power model prices a troop through the auto-resolve's kit, hit-point and perk caches,
            // whether or not the auto-resolve itself is on -- and with it off, its own behavior (which clears them) is
            // not registered. So clear them here as well, before the strategic caches built on them. Twice is free.
            RBMSimulationCampaignBehavior.ResetForNewSession();

            // The strategic power model wants a clean slate for a stronger reason still: nothing reclaims its caches
            // at all. They are keyed by CharacterObject and MobileParty and no event empties them, so a lord's
            // measured power -- and the troop, party and hero instances it is filed under -- would outlive the
            // campaign it was taken from and sit there for the life of the process. Worse than the leak: a hero's
            // measurement is only re-taken when it is a day old, and the day it is stamped with comes from whichever
            // campaign was running when it was taken. Load a save at an earlier date and that stamp is in the future,
            // so the entry never looks stale -- and the lord is priced, indefinitely, off the harness he was wearing
            // in a campaign the player has left.
            StrategicTroopPower.ResetForNewSession();

            // Same reasoning, smaller stakes: the town food caches are keyed by Town and would
            // otherwise hold a departed campaign's settlements for the life of the process.
            RBMTownFoodSupply.ResetForNewSession();
            CitizenDemand.ResetForNewSession();
            TownFoodReserve.ResetForNewSession();
            HiddenMarketStock.ResetForNewSession();
            RBMProsperityEquilibrium.ResetForNewSession();
            RBMMarketPrices.ResetForNewSession();
            WorkshopDemand.ResetForNewSession();
            RBMItemWeightTextWidget.ResetForNewSession();

            // The upgrade supply-town resolver caches a payee town per party, keyed by MobileParty
            // identity; clear it so a departed campaign's parties and towns are not held for the life
            // of the process.
            UpgradeSupply.ResetForNewSession();
        }
    }
}
