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
    /// A Harmony patch on TooltipRefresherCollection.RefreshSettlementTooltip is not enough. SandBox.View
    /// registers it as the Settlement refresher (SandBoxViewSubModule.RegisterTooltipTypes, from
    /// OnSubModuleLoad), and every hover looks the refresher up again in InformationManager.RegisteredTypes
    /// (TooltipBaseVM.InvokeRefreshData), so a patch on that method would fire as long as it stays registered.
    /// But War Sails re-registers Settlement with its own NavalTooltipRefresherCollection.RefreshSettlementTooltip
    /// (NavalDLCViewSubModule.RegisterTooltipTypes), a full replacement that never calls vanilla's, so with the
    /// DLC active a patch on vanilla's method never runs. An early patch was also seen to crash the campaign load.
    /// So rather than patching, we re-register the Settlement tooltip with a wrapper that calls whatever refresher
    /// is registered (vanilla's or War Sails') and then adds our lines. The modules register theirs once at
    /// startup, so re-registering after the session is up (see <see cref="RBMSettlementWealthCampaignBehavior"/>)
    /// sticks for the whole process.
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

            // A castle holds a single pool -- its own wealth -- with no market and so no citizen purse;
            // a village likewise has one purse and no market. Only a town shows both a citizen-wealth
            // line and a settlement line. See SettlementWealth.HasMarket.
            if (settlement.IsCastle)
            {
                propertyBasedTooltipVM.AddProperty(new TextObject("{=RBM_wealth_castle}Castle wealth").ToString(),
                    SettlementWealth.GetSettlementWealth(settlement).ToString(), 0);
                AppendConstruction(propertyBasedTooltipVM, settlement);
                AppendRecruitPool(propertyBasedTooltipVM, settlement);
                return;
            }
            if (settlement.IsTown)
            {
                propertyBasedTooltipVM.AddProperty(new TextObject("{=RBM_wealth_citizen}Citizen wealth").ToString(),
                    SettlementWealth.GetCitizenWealth(settlement).ToString(), 0);
            }
            propertyBasedTooltipVM.AddProperty(new TextObject("{=RBM_wealth_settlement}Settlement wealth").ToString(),
                SettlementWealth.GetSettlementWealth(settlement).ToString(), 0);
            if (settlement.IsTown)
            {
                AppendConstruction(propertyBasedTooltipVM, settlement);
            }
            AppendRecruitPool(propertyBasedTooltipVM, settlement);
        }

        /// <summary>
        /// The settlement's manpower: men left to come forward as new soldiers, its ceiling and daily refill,
        /// and -- for a fortification -- a note when the pool is down to the reserve the garrison leaves for
        /// volunteers. See <see cref="RecruitPool"/>.
        /// </summary>
        private static void AppendRecruitPool(PropertyBasedTooltipVM propertyBasedTooltipVM, Settlement settlement)
        {
            if (!RecruitPool.IsEnabled)
            {
                return;
            }
            propertyBasedTooltipVM.AddProperty(new TextObject("{=RBM_pool_label}Recruit pool").ToString(),
                RecruitPool.FormatPool(settlement), 0);
            string bonus = RecruitPool.FormatBuildingBonus(settlement);
            if (bonus != null)
            {
                propertyBasedTooltipVM.AddProperty(new TextObject("{=RBM_pool_bonus_label}Recruit pool buildings").ToString(), bonus, 0);
            }
            string cost = RecruitPool.FormatGarrisonManCost(settlement);
            if (cost != null)
            {
                propertyBasedTooltipVM.AddProperty(new TextObject("{=RBM_pool_cost_label}Garrison recruit cost").ToString(), cost, 0);
            }
            string paused = RecruitPool.FormatGarrisonPaused(settlement);
            if (paused != null)
            {
                // Empty definition + value: vanilla's shape for a single free-text line.
                propertyBasedTooltipVM.AddProperty(string.Empty, paused, 0);
            }
        }

        /// <summary>
        /// What the fief's building site cost its reserve on the last building day, and what the reserve
        /// still holds. See <see cref="Construction"/>.
        /// </summary>
        private static void AppendConstruction(PropertyBasedTooltipVM propertyBasedTooltipVM, Settlement settlement)
        {
            if (settlement.Town == null)
            {
                return;
            }
            propertyBasedTooltipVM.AddProperty(new TextObject("{=RBM_construction_spend}Construction spend (daily)").ToString(),
                Construction.LastDailySpend(settlement).ToString(), 0);
            propertyBasedTooltipVM.AddProperty(new TextObject("{=RBM_construction_reserve}Construction reserve").ToString(),
                settlement.Town.BoostBuildingProcess.ToString(), 0);
        }
    }
}
