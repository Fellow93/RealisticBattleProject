using System;
using System.Reflection;
using TaleWorlds.CampaignSystem;

namespace RBMConfig.Logging
{
    /// <summary>
    /// Whether the campaign clock can be read yet, for the date stamp every campaign log puts on its lines. Shared
    /// by the RBMCampaign logs and the auto-resolve's SimulationLog (RBMAutoResolve), which is why it lives here and
    /// not in either module (it used to be SpoilsLog.CampaignClockReady).
    /// </summary>
    public static class CampaignLogClock
    {
        // The campaign whose clock CampaignClockReady last found ready. Once a campaign's clock is up it stays
        // up, so the reflective MapTimeTracker read is skipped for every later line of that campaign. Weak, so
        // a finished campaign is not kept alive by the log.
        private static WeakReference<Campaign> _clockReadyCampaign;

        // Campaign.MapTimeTracker is internal; read it to tell whether CampaignTime.Now is safe yet.
        private static readonly PropertyInfo MapTimeTrackerProperty =
            typeof(Campaign).GetProperty("MapTimeTracker", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// True once CampaignTime.Now and its date getters can be read without throwing. Shared by
        /// every campaign log's date stamp.
        /// </summary>
        public static bool CampaignClockReady()
        {
            // DaysInYear is zero until CampaignTime.Initialize has run the time model; GetYear divides
            // by the tick count derived from it, so a line written before then would divide by zero.
            Campaign current = Campaign.Current;
            if (current == null || CampaignTime.DaysInYear <= 0)
            {
                return false;
            }
            // Already found ready for this campaign: skip the reflective read below on every later line.
            WeakReference<Campaign> cached = _clockReadyCampaign;
            Campaign cachedCampaign;
            if (cached != null && cached.TryGetTarget(out cachedCampaign) && ReferenceEquals(cachedCampaign, current))
            {
                return true;
            }
            // CampaignTime.Now dereferences Campaign.MapTimeTracker, which stays null on a new campaign
            // until OnInitialize creates it. The callers' catch would swallow the NRE, but a first-chance
            // exception on every early line stops an attached debugger, so check it up front.
            bool ready = MapTimeTrackerProperty == null || MapTimeTrackerProperty.GetValue(current) != null;
            if (ready)
            {
                _clockReadyCampaign = new WeakReference<Campaign>(current);
            }
            return ready;
        }
    }
}
