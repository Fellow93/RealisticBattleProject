using System;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
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
                int ourStart = vm.TooltipPropertyList.Count;
                Append(vm, args);
                MoveUnderInformation(vm.TooltipPropertyList, ourStart);
                SettlementTroopTooltip.GroupTroopTypes(vm, args.Length > 0 ? args[0] as Settlement : null);
            };
            _installedWrapper = wrapper;
            InformationManager.RegisterTooltip<Settlement, PropertyBasedTooltipVM>(wrapper, registry.MovieName);
        }

        /// <summary>
        /// Moves our lines (everything from <paramref name="ourStart"/> on) up to the end of the tooltip's
        /// "Information" section, where prosperity, loyalty and the bound villages are.
        /// </summary>
        /// <remarks>
        /// Appended at the very end they came after the troop roster that the extended (Alt) view lists,
        /// which for a big garrison runs the tooltip off the bottom of the screen and took our lines with
        /// it. They also landed below the "press Alt" and parley hints. Both refreshers -- vanilla's
        /// TooltipRefresherCollection.RefreshSettlementTooltip and War Sails' -- open that section with a
        /// spacer and a "str_information" header and end it at the next spacer (TextHeight -1), which opens
        /// the troop roster, the parties list or the DEV shop list. When the section is missing (the
        /// settlement's information is hidden from the player) the lines stay at the end as before.
        /// </remarks>
        private static void MoveUnderInformation(MBBindingList<TooltipProperty> list, int ourStart)
        {
            if (list.Count <= ourStart)
            {
                return;
            }
            string header = GameTexts.FindText("str_information").ToString();
            int headerIndex = -1;
            for (int i = 0; i < ourStart; i++)
            {
                if (list[i].DefinitionLabel == header)
                {
                    headerIndex = i;
                    break;
                }
            }
            if (headerIndex < 0)
            {
                return;
            }
            int target = ourStart;
            for (int i = headerIndex + 1; i < ourStart; i++)
            {
                if (list[i].TextHeight == -1)
                {
                    target = i;
                    break;
                }
            }
            if (target == ourStart)
            {
                return;
            }
            var ours = new List<TooltipProperty>();
            for (int i = ourStart; i < list.Count; i++)
            {
                ours.Add(list[i]);
            }
            for (int i = list.Count - 1; i >= ourStart; i--)
            {
                list.RemoveAt(i);
            }
            for (int i = 0; i < ours.Count; i++)
            {
                list.Insert(target + i, ours[i]);
            }
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
            bool exact = propertyBasedTooltipVM.IsExtended;
            if (settlement.IsCastle)
            {
                propertyBasedTooltipVM.AddProperty(new TextObject("{=RBM_wealth_castle}Castle wealth").ToString(),
                    FormatWealth(SettlementWealth.GetSettlementWealth(settlement), exact), 0);
                AppendConstruction(propertyBasedTooltipVM, settlement);
                AppendRecruitPool(propertyBasedTooltipVM, settlement);
                return;
            }
            if (settlement.IsTown)
            {
                propertyBasedTooltipVM.AddProperty(new TextObject("{=RBM_wealth_citizen}Citizen wealth").ToString(),
                    FormatWealth(SettlementWealth.GetCitizenWealth(settlement), exact), 0);
            }
            propertyBasedTooltipVM.AddProperty(new TextObject("{=RBM_wealth_settlement}Settlement wealth").ToString(),
                FormatWealth(SettlementWealth.GetSettlementWealth(settlement), exact), 0);
            if (settlement.IsTown)
            {
                AppendConstruction(propertyBasedTooltipVM, settlement);
            }
            AppendRecruitPool(propertyBasedTooltipVM, settlement);
        }

        /// <summary>
        /// A purse as the hover shows it. At a glance it is only a rough figure -- rounded to its leading
        /// two digits, midpoints up: 47,312 reads "~47,000", 12,550 reads "~13,000" and 1,234,567 reads
        /// "~1,200,000" -- and holding the extend key (vanilla's "more info" Alt, which re-runs the whole
        /// refresher, see PropertyBasedTooltipVM.OnIsExtendedChanged) gives the exact sum.
        /// </summary>
        private static string FormatWealth(int amount, bool exact)
        {
            if (exact || amount == 0)
            {
                return amount.ToString("N0", CultureInfo.InvariantCulture);
            }
            // Under 100 the two leading digits are the whole number, so the magnitude bottoms out at 1.
            long magnitude = (long)Math.Pow(10, Math.Max(0, Math.Floor(Math.Log10(Math.Abs((long)amount))) - 1));
            long rounded = (long)Math.Round((double)Math.Abs((long)amount) / magnitude, MidpointRounding.AwayFromZero) * magnitude;
            return "~" + (amount < 0 ? "-" : string.Empty) + rounded.ToString("N0", CultureInfo.InvariantCulture);
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
                Construction.LastDailySpend(settlement).ToString("N0", CultureInfo.InvariantCulture), 0);
            propertyBasedTooltipVM.AddProperty(new TextObject("{=RBM_construction_reserve}Construction reserve").ToString(),
                settlement.Town.BoostBuildingProcess.ToString("N0", CultureInfo.InvariantCulture), 0);
        }
    }
}
