using System.Collections.Generic;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>What the day's maintenance came to for one party: the whole of it, and how it was met.</summary>
    public struct MaintenanceResult
    {
        /// <summary>The full daily cost of keeping the party's stacks in the field.</summary>
        public int Total;

        /// <summary>How much of it the stacks paid out of their own spoils.</summary>
        public int Covered;

        /// <summary>What the purses could not meet, left to the party leader's gold.</summary>
        public int Shortfall;
    }

    /// <summary>
    /// The daily cost of keeping a soldier in the field, drawn against the whole worth of his kit --
    /// his gear, his horse and its harness alike. A share of that worth is spent each day mending what
    /// the march wore through and replacing what is past mending. The men pay it out of their own
    /// spoils first; whatever the purse cannot cover falls to the party leader, out of his gold.
    ///
    /// That money is spent somewhere, and it takes something with it. It goes over to the town that
    /// supplies the party -- the one it stands in, or the nearest not at war with it -- which pays the
    /// market fee on the way like any other purchase struck there (<see cref="PayMaintenanceToMarket"/>);
    /// and gear to the value of the day's wear comes off that town's shelves, matched to what the men
    /// actually wear (<see cref="DrawMaintenanceStock"/>).
    /// </summary>
    /// <remarks>
    /// Charged once per clan per day, off the clan finance model's apply pass
    /// (<see cref="MaintenanceFinanceLine"/>): every party the clan's leader keeps has its stacks' purses
    /// drained for their share, and whatever the purses cannot meet is folded into the clan's daily gold
    /// change. Routing the shortfall through the finance number rather than a separate transfer means it
    /// shows in the Daily Gold Change message and the finance breakdown, and the leader pays it through the
    /// very channel wages run through. A point of spoils is a gold piece, so the cost drains one-for-one.
    /// </remarks>
    public static partial class SpoilsPool
    {
        /// <summary>
        /// The daily cost of keeping one stack of <paramref name="number"/> men of this troop in the
        /// field: a share of the whole worth of their kit, horse and harness included. The stack's cost,
        /// not one man's, so a small troop's fraction is not rounded away. Shared by the daily charge and
        /// the recruit seed so both price a day's upkeep the same way.
        /// </summary>
        private static int DailyMaintenanceCost(CharacterObject character, int number)
        {
            float fraction = RBMConfig.RBMConfig.troopMaintenanceFraction;
            if (fraction <= 0f || number <= 0)
            {
                return 0;
            }
            return MathF.Round(fraction * GetEquipmentValueWithMount(character) * number);
        }

        /// <summary>
        /// Charges a clan the day's maintenance across every party its leader keeps: drains each stack's
        /// spoils for its share and returns the tally, whose shortfall the caller folds into the clan's
        /// daily gold change. Run once per clan per day from the finance model's apply pass, so the
        /// deduction shows in the Daily Gold Change message and the leader truly pays it. Passed
        /// <paramref name="apply"/> false it projects the same tally without touching a purse, for the
        /// finance breakdown the display pass draws.
        /// </summary>
        public static MaintenanceResult ChargeClanMaintenance(Clan clan, bool apply)
        {
            MaintenanceResult total = default(MaintenanceResult);
            if (!IsEnabled || RBMConfig.RBMConfig.troopMaintenanceFraction <= 0f || clan == null)
            {
                return total;
            }
            // The clan's own war parties -- the very ones whose wages it already pays (the game charges
            // wages off this same list). Maintenance mirrors the wage: charged for the parties the leader
            // keeps and folded into the one daily gold change he settles. Caravans keep their own purse and
            // pay their own way, so they are left out here as they are from the leader's wage bill.
            foreach (WarPartyComponent warParty in clan.WarPartyComponents)
            {
                MobileParty mobileParty = warParty?.MobileParty;
                if (mobileParty == null || !mobileParty.IsActive)
                {
                    continue;
                }
                MaintenanceResult m = ComputeMaintenance(mobileParty.Party, apply);
                total.Total += m.Total;
                total.Covered += m.Covered;
                total.Shortfall += m.Shortfall;
            }
            return total;
        }

        /// <summary>
        /// Seeds a freshly recruited stack's purse with several days' maintenance, so a man drawn from a
        /// settlement arrives with his kit in order and a little put by against the coming march rather
        /// than penniless. Added on top of whatever the stack already carries. No gold changes hands --
        /// the recruit brings the spoils with him.
        /// </summary>
        /// <summary>
        /// What a fresh stack of <paramref name="number"/> men of this troop arrives carrying: several
        /// days of their own upkeep, priced off the same daily figure the daily charge uses.
        ///
        /// Pure arithmetic, with none of <see cref="SeedRecruitMaintenance"/>'s guards about who is
        /// eligible, so the figure can be quoted without committing to seeding it.
        /// </summary>
        public static int RecruitSeedValue(CharacterObject character, int number)
        {
            int days = RBMConfig.RBMConfig.recruitMaintenanceDays;
            if (days <= 0)
            {
                return 0;
            }
            return DailyMaintenanceCost(character, number) * days;
        }

        public static void SeedRecruitMaintenance(PartyBase party, CharacterObject character, int amount)
        {
            if (!IsEnabled || party == null || character == null || character.IsHero || amount <= 0 || IsExemptParty(party))
            {
                return;
            }
            // A bandit party keeps no war-chest; it is charged no maintenance, so it is seeded none.
            if (party.MobileParty != null && party.MobileParty.IsBandit)
            {
                return;
            }
            int seed = RecruitSeedValue(character, amount);
            if (seed <= 0)
            {
                return;
            }
            AddSpoils(party, character, seed);
            if (SpoilsLog.IsEnabled && party == PartyBase.MainParty)
            {
                SpoilsLog.Log("RECRUIT", party, SpoilsLog.Describe(party) + " recruited "
                    + SpoilsLog.Describe(character) + " x" + amount + "; seeded " + seed + " spoils ("
                    + RBMConfig.RBMConfig.recruitMaintenanceDays + " days' maintenance)");
            }
        }

        /// <summary>
        /// A lord's party mustering from a village or town: the AI recruit path, which alone carries the
        /// settlement and the recruiter. Prisoners pressed into service and volunteers picked up on the
        /// road carry a null settlement and bring nothing -- only a proper muster from a settlement is
        /// seeded. The player's own muster does not come here (it fires <see cref="OnUnitRecruited"/>
        /// instead), so the main party is passed over to keep the two paths from seeding one recruit twice.
        /// </summary>
        public static void OnTroopRecruited(Hero recruiterHero, Settlement recruitmentSettlement,
            Hero recruitmentSource, CharacterObject troop, int amount)
        {
            if (recruitmentSettlement == null || !(recruitmentSettlement.IsVillage || recruitmentSettlement.IsTown))
            {
                return;
            }
            PartyBase party = recruiterHero?.PartyBelongedTo?.Party;
            if (party == null || party == PartyBase.MainParty)
            {
                return;
            }
            SeedRecruitMaintenance(party, troop, amount);
            // RecruitSupply pay: the recruit price goes over to the market town that supplies this place
            // -- its own if this is a town, its trade-bound town's if this is a village -- instead of
            // being destroyed. The man's gear left that market when he was raised, not now. Kept out of
            // SeedRecruitMaintenance so it still happens when maintenance seeding is off.
            RecruitSupply.PayRecruitPrice(recruitmentSettlement, party, recruiterHero, troop, amount);
        }

        /// <summary>
        /// The player's own muster from a settlement's notables, one man at a time into the main party --
        /// the recruit-screen path, which carries neither settlement nor party. The screen only opens
        /// inside a village or town, so the main party's current settlement stands in for the "from a
        /// settlement" gate. Prisoners pressed into service and mercenaries hired in a tavern reach this
        /// event too, but only a recruit made while the party sits in a village or town is seeded.
        /// </summary>
        public static void OnUnitRecruited(CharacterObject character, int amount)
        {
            Settlement settlement = MobileParty.MainParty?.CurrentSettlement;
            if (settlement == null || !(settlement.IsVillage || settlement.IsTown))
            {
                return;
            }
            SeedRecruitMaintenance(PartyBase.MainParty, character, amount);
            // RecruitSupply pay: as the AI path above, off the settlement the player is standing in and
            // out of the player's own purse.
            RecruitSupply.PayRecruitPrice(settlement, PartyBase.MainParty, Hero.MainHero, character, amount);
        }

        /// <summary>
        /// The day's maintenance as it would fall right now, without touching a purse or a treasury: for
        /// the finance breakdown and the party-wage tooltip, which read the coming day rather than move it.
        /// </summary>
        public static MaintenanceResult ProjectDailyMaintenance(PartyBase party)
        {
            return ComputeMaintenance(party, apply: false);
        }

        /// <summary>
        /// Writes the day's maintenance into a finance/wage breakdown as two lines -- the whole cost, then
        /// the share the men's own spoils met as an offsetting credit -- so the two net to just the coin the
        /// party is left to pay while both stay on the page. Drawn this way rather than as a single net line
        /// because an <see cref="ExplainedNumber"/> drops a zero-valued line: a stack whose spoils cover its
        /// upkeep in full would otherwise vanish from the tooltip, hiding the maintenance the player wanted
        /// to see. <paramref name="expenseSign"/> is -1 where the number counts expenses as negative (the
        /// clan finance change) and +1 where it counts costs as positive (the party wage), so the same
        /// tally reads correctly on either. Freshly built each call: an ExplainedNumber keeps the reference,
        /// so a shared TextObject would have its number overwritten by the next party.
        /// </summary>
        public static void AddMaintenanceBreakdown(ref ExplainedNumber breakdown, MaintenanceResult maintenance, float expenseSign)
        {
            if (maintenance.Total <= 0)
            {
                return;
            }
            breakdown.Add(expenseSign * maintenance.Total, new TextObject("{=RBM_SPOILS_017}Troop maintenance"));
            if (maintenance.Covered > 0)
            {
                breakdown.Add(-expenseSign * maintenance.Covered, new TextObject("{=RBM_SPOILS_020}Maintenance paid from troop spoils"));
            }
        }

        private static MaintenanceResult ComputeMaintenance(PartyBase party, bool apply)
        {
            MaintenanceResult result = default(MaintenanceResult);
            float fraction = RBMConfig.RBMConfig.troopMaintenanceFraction;
            if (!IsEnabled || fraction <= 0f || IsExemptParty(party))
            {
                return result;
            }
            TroopRoster roster = party.MemberRoster;
            if (roster == null)
            {
                return result;
            }
            // A bandit party keeps no war-chest and its leader no treasury, so there is nothing to bill.
            // Bandit troops in a lord's party still cost their keeper, so that party is charged as any other.
            if (party.MobileParty != null && party.MobileParty.IsBandit)
            {
                return result;
            }

            // How much of each stack's cost its own purse may cover turns on the party's standing in the
            // field, and is the same for every stack the party keeps, so it is read once here rather than
            // per stack. Whatever the purse is not allowed to meet falls to the party leader's gold as
            // any shortfall does, so Total stays the whole cost and only Covered/Shortfall move with it.
            float purseFraction = ContractPurseFraction(party);

            // Where the day's mending is bought and paid for. Resolved once for the party rather than per
            // stack: it costs a sweep of the town list, and every stack mends at the same market anyway.
            Settlement market = apply ? ResolveMaintenanceMarket(party.MobileParty) : null;
            ItemRoster stock = (market != null) ? market.ItemRoster : null;
            int drawnItems = 0;
            int drawnValue = 0;

            int stacksCharged = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character.IsHero || element.Number <= 0)
                {
                    continue;
                }
                // Priced off the mounted worth: a lancer's horse is a real part of what it costs to keep him.
                int cost = DailyMaintenanceCost(element.Character, element.Number);
                if (cost <= 0)
                {
                    continue;
                }
                int purseTarget = MathF.Round(cost * purseFraction);
                int fromSpoils = MathF.Min(GetSpoils(party, element.Character), purseTarget);
                if (apply && fromSpoils > 0)
                {
                    AddSpoils(party, element.Character, -fromSpoils);
                }
                result.Total += cost;
                result.Covered += fromSpoils;
                stacksCharged++;
                // What the day's wear actually took off the shelves, sized by this stack's own share of
                // the bill and matched to the gear this stack wears.
                if (stock != null)
                {
                    DrawMaintenanceStock(market, stock, element.Character, cost, ref drawnItems, ref drawnValue);
                }
                if (apply && SpoilsLog.Verbose && party == PartyBase.MainParty)
                {
                    SpoilsLog.LogVerbose("UPKEEP", party, SpoilsLog.Describe(element.Character) + " x" + element.Number
                        + ": maintenance " + cost + " (spoils " + fromSpoils + ", leader " + (cost - fromSpoils) + ")");
                }
            }

            // Only the purses are moved here; the shortfall the men cannot meet is left for the clan's
            // daily gold change to carry (see ChargeClanMaintenance), so the leader pays it once, through
            // the finance number rather than a separate transfer.
            result.Shortfall = result.Total - result.Covered;

            // Both halves were genuinely paid by the party's side -- the purses gave one, the leader's
            // gold the other -- so the whole of it is handed over to the market that supplied the gear.
            if (market != null && result.Total > 0)
            {
                PayMaintenanceToMarket(market, result.Total);
            }

            if (apply && SpoilsLog.IsEnabled && party == PartyBase.MainParty && result.Total > 0)
            {
                SpoilsLog.Log("UPKEEP", party, SpoilsLog.Describe(party) + " owed " + result.Total
                    + " maintenance across " + stacksCharged + (stacksCharged == 1 ? " stack" : " stacks")
                    + " (spoils covered " + result.Covered + ", " + result.Shortfall + " to clan gold)"
                    + (market != null
                        ? " — paid to " + market.Name + ", drew " + drawnItems + " item(s) worth " + drawnValue + "d"
                        : " — no town in reach, coin burnt"));
            }
            return result;
        }

        /// <summary>
        /// Takes the day's wear for one stack off the market: for one of the slots this troop's kit fills,
        /// a piece of that class and tier, so long as the stack's share of the bill covers it. Repairs are
        /// mostly replacements -- a strap, a shaft, a shoe, a hauberk past mending -- and this is the shelf
        /// they come off.
        /// </summary>
        /// <remarks>
        /// The stack's budget is a day's maintenance, which is a small share of its kit worth, so most days
        /// most stacks can afford one piece and no more -- a big stack of well-armoured men replaces
        /// something daily, a handful of levies replaces nothing for weeks. That is the intended shape: the
        /// money is spent either way, and what a small stack's coin bought was the smith's labour rather
        /// than a piece of kit.
        ///
        /// It stops at the first slot the budget cannot afford rather than hunting on down the list for
        /// something cheaper, which is both how <see cref="RecruitSupply"/> reads an empty purse and what
        /// keeps this to one or two market sweeps per stack -- it runs for every stack of every party on
        /// the map on the daily tick, so a full walk of eight slots against a two-hundred-line market would
        /// be felt.
        ///
        /// The starting slot is picked at random so it is not always the helmet that gets replaced: a fixed
        /// order plus a one-item budget would mean the first slot in the list is the only one an army ever
        /// wears out, and towns would be stripped of exactly one class of goods.
        ///
        /// The mount and its harness are in the list, unlike the recruit draw's, because maintenance is
        /// priced off <see cref="GetEquipmentValueWithMount"/> -- a remount is a real part of what keeping
        /// cavalry in the field costs, and the stack is being billed for it.
        /// </remarks>
        private static void DrawMaintenanceStock(Settlement market, ItemRoster stock, CharacterObject character,
            int budget, ref int items, ref int value)
        {
            if (budget <= 0)
            {
                return;
            }
            List<SlotPurchase> slots = GetKitSlots(character, includeMount: true);
            if (slots.Count == 0)
            {
                return;
            }
            int start = MBRandom.RandomInt(slots.Count);
            for (int n = 0; n < slots.Count; n++)
            {
                SlotPurchase slot = slots[(start + n) % slots.Count];
                int index = UpgradeSupply.FindKitInStock(stock, slot.ItemType, slot.Value);
                if (index < 0)
                {
                    // Nothing of that class in band on the stalls: the men mend it themselves or go without.
                    continue;
                }
                ItemObject item = stock.GetItemAtIndex(index);
                if (item == null)
                {
                    continue;
                }
                int price = TroopMarketFeedback.UnitPrice(market, item, stock, index);
                if (price > budget)
                {
                    break;
                }
                stock.AddToCounts(item, -1);
                budget -= price;
                items++;
                value += price;
                // A price signal, not a payment -- the whole day's coin is handed over in one sum by the
                // caller. This is how the town learns to restock what the armies passing through wear out.
                if (market.Town != null && item.ItemCategory != null)
                {
                    RBMTownFoodSupply.RegisterPurchaseDemand(market.Town.MarketData, item.ItemCategory, price);
                }
            }
        }

        /// <summary>
        /// Hands the day's maintenance over to the town that supplied the gear.
        /// </summary>
        /// <remarks>
        /// Paid through <see cref="TroopMarketFeedback.RegisterPurchase"/>, which is what makes this a
        /// purchase rather than a gift: the coin lands in the town's market purse, the market fee is taken
        /// out of it on the way (see <see cref="TradeTariff"/>), and the sum joins the town's recent troop
        /// trade. The whole day's bill goes over in ONE sum, not per item, so the town is paid what the
        /// army was actually charged rather than what its draw happened to find on the shelves.
        ///
        /// No category is passed with it, deliberately: the demand for what was drawn is registered piece
        /// by piece in <see cref="DrawMaintenanceStock"/>, and passing the whole sum's category here as
        /// well would count the same pressure twice.
        ///
        /// NOTHING IS CHARGED HERE. The men's purses were drained a few lines above and the shortfall is
        /// on its way onto the leader's daily gold change; this only decides where that money lands
        /// instead of vanishing. Charge and credit are the same number by construction, as on the recruit
        /// side -- see <see cref="RecruitSupply"/>, built the same way for the same reason.
        /// </remarks>
        private static void PayMaintenanceToMarket(Settlement market, int amount)
        {
            TroopMarketFeedback.RegisterPurchase(market, null, amount, SettlementWealth.Source.Maintenance);
        }

        /// <summary>
        /// The town an army mends its kit at: the one it stands in, or else the nearest town of a faction
        /// it is not at war with, however far off that lies. An army in the field is never left with
        /// nowhere to spend, so maintenance money is not destroyed anywhere on the map.
        /// </summary>
        /// <remarks>
        /// A TOWN always, never the castle or village the party happens to be sitting in. An armourer who
        /// can re-sole a hauberk is a town trade; a village has a man who shoes horses and a castle has a
        /// smith for the garrison's own gear, and neither keeps the stock an army wears through. So a party
        /// resting in either is supplied from the nearest city, as the upgrade and recruit draws are.
        ///
        /// Keeping it to towns also keeps the money and the fee together: the market fee is a town levy, so
        /// paying a village would be the one path where an army's coin arrived untariffed.
        ///
        /// The search itself is <see cref="UpgradeSupply.FindNearestFriendlyTown"/>, shared with the
        /// upgrade payment, which needs the same "a town to pay, at any distance" answer.
        /// </remarks>
        private static Settlement ResolveMaintenanceMarket(MobileParty party)
        {
            if (party == null)
            {
                return null;
            }
            Town town = UpgradeSupply.FindNearestFriendlyTown(party);
            return (town != null) ? town.Settlement : null;
        }

        /// <summary>
        /// How much of a stack's daily maintenance the men's own purse is allowed to cover, set by the
        /// party's standing in the field. An independent lord's men fund their upkeep in full; a mercenary
        /// company under a kingdom's pay meets only a part from its purses, its employer the rest; a sworn
        /// vassal's or ruler's men pay none of it from their purses, their liege bearing the whole. Whatever
        /// the purse is not allowed to meet always falls to the party leader's gold, as any shortfall does.
        /// The clan is read off the same payee chain the spoils are paid to (owner if alive, else leader);
        /// a party with no clan to answer to stands on its own and funds its upkeep in full.
        /// </summary>
        private static float ContractPurseFraction(PartyBase party)
        {
            Hero payee = GetPartyPayee(party);
            Clan clan = payee?.Clan;
            // No clan, or a clan sworn to no kingdom, answers to no liege -- its men pay their own way.
            if (clan == null || clan.Kingdom == null)
            {
                return RBMConfig.RBMConfig.independentMaintenancePurseFraction;
            }
            // Hired swords in a kingdom's service share the burden: their purses meet a part, the crown the rest.
            if (clan.IsUnderMercenaryService)
            {
                return RBMConfig.RBMConfig.mercenaryMaintenancePurseFraction;
            }
            // A sworn vassal or ruler bears the whole of it: none is met from the men's purses.
            return 0f;
        }

    }
}
