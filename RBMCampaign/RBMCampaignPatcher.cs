using HarmonyLib;

namespace RBMCampaign
{
    public static class RBMCampaignPatcher
    {
        public static void DoPatching(ref Harmony rbmcampaignHarmony)
        {
            rbmcampaignHarmony.PatchAll();
            RBMTroopSpoilsBarWidget.RegisterWidgetType();
            UpgradeLimitWidgets.RegisterWidgetTypes();
            // Drop any nameplate view-models left subscribed to RBM's map-bubble events by a previous
            // session, so a save reload does not pin the old map's nameplates in memory. See RBMMapNotifications.
            RBMMapNotifications.Reset();
            // The DefaultClanFinanceModel patches are held out of PatchAll: touching that type before a
            // game exists forces its Game.Current-reading static initializer to throw and poisons it for
            // the process. Apply them by hand instead -- a no-op until Game.Current is live, so it lands
            // on the OnGameStart pass (this runs on every patch pass). See MercenaryContractPay.
            MercenaryContractPay.ApplyDeferred(rbmcampaignHarmony);
            // The sea-patrol patches target NavalDLC types the mod cannot reference, so they are applied by
            // hand and guarded on those types being present -- a silent no-op without the DLC. The land patrol
            // patches ride the PatchAll above like everything else. See PatrolUpkeep.Naval.cs.
            PatrolUpkeepNaval.ApplyNaval(rbmcampaignHarmony);
        }
    }
}
