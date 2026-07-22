using System;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Appends the two purses to the settlement hover tooltip on the campaign map, so the flows built on
    /// top of them can be watched settlement by settlement rather than only in the log.
    /// </summary>
    /// <remarks>
    /// The settlement tooltip is not driven by a live method call the way a Harmony patch would catch it:
    /// the game captures a delegate to TooltipRefresherCollection.RefreshSettlementTooltip once, in
    /// SandBox.View's OnSubModuleLoad, and every hover fires through that captured delegate. A patch
    /// applied later never routes through it, and one applied early enough to be captured runs while the
    /// campaign map is still loading and crashes the load. So rather than patching, we re-register the
    /// Settlement tooltip with a wrapper that calls whatever refresher was registered and then adds our
    /// lines. Registration happens once at startup, so re-registering after the session is up
    /// (see <see cref="RBMSettlementWealthCampaignBehavior"/>) sticks for the whole process.
    /// </remarks>
    public static class SettlementWealthTooltip
    {
        private static Action<PropertyBasedTooltipVM, object[]> _installedWrapper;

        /// <summary>
        /// Wraps the currently-registered Settlement tooltip refresher with ours. Safe to call again each
        /// session: if our wrapper is already the registered refresher it does nothing, so it never nests.
        /// </summary>
        public static void Install()
        {
            var registered = InformationManager.RegisteredTypes;
            if (registered == null || !registered.TryGetValue(typeof(Settlement), out InformationManager.TooltipRegistry registry))
            {
                return;
            }
            var original = registry.OnRefreshData as Action<PropertyBasedTooltipVM, object[]>;
            if (original == null || original == _installedWrapper)
            {
                return;
            }

            Action<PropertyBasedTooltipVM, object[]> chained = original;
            Action<PropertyBasedTooltipVM, object[]> wrapper = delegate (PropertyBasedTooltipVM vm, object[] args)
            {
                chained(vm, args);
                Append(vm, args);
            };
            _installedWrapper = wrapper;
            InformationManager.RegisterTooltip<Settlement, PropertyBasedTooltipVM>(wrapper, registry.MovieName);
        }

        private static void Append(PropertyBasedTooltipVM propertyBasedTooltipVM, object[] args)
        {
            Settlement settlement = args.Length > 0 ? args[0] as Settlement : null;
            if (settlement == null || !(settlement.IsVillage || settlement.IsTown || settlement.IsCastle))
            {
                return;
            }

            // A blank line sets these off from the settlement's own stats above them.
            propertyBasedTooltipVM.AddProperty(string.Empty, string.Empty, -1);

            // A village has one purse and no market, so showing it a citizen-wealth line would be
            // showing it a permanent zero. See SettlementWealth.HasMarket.
            if (!settlement.IsVillage)
            {
                propertyBasedTooltipVM.AddProperty(new TextObject("{=RBM_wealth_citizen}Citizen wealth").ToString(),
                    SettlementWealth.GetCitizenWealth(settlement).ToString(), 0);
            }
            propertyBasedTooltipVM.AddProperty(new TextObject("{=RBM_wealth_settlement}Settlement wealth").ToString(),
                SettlementWealth.GetSettlementWealth(settlement).ToString(), 0);
        }
    }
}
