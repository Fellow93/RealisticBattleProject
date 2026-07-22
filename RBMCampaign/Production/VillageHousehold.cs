using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// What a village keeps of the money its convoy earns in town.
    ///
    /// Vanilla gives the countryside no household economy at all. A convoy sells its cargo in town, the
    /// takings sit in <c>PartyTradeGold</c>, and the moment it walks back through its own gate
    /// <c>VillagerCampaignBehavior.OnSettlementEntered</c> converts the whole lot into
    /// <c>TradeTaxAccumulated</c> and hands it to the owner -- the village keeps nothing, and holds no
    /// money of its own from one season to the next.
    ///
    /// Here the village keeps a fifth. The convoy carries its takings home as gold and they are split at
    /// the gate: a fifth into the village's own purse, the rest to the owner exactly as vanilla computed
    /// it. The convoy brings back money and nothing else -- it does no shopping in town.
    /// </summary>
    public static class VillageHousehold
    {
        /// <summary>
        /// Share of a convoy's takings the village keeps instead of the owner.
        ///
        /// Vanilla's <c>SettlementCommissionRateVillage</c> is 1.0 -- the owner takes a village's ENTIRE
        /// trade income and the village keeps nothing, which is why there is no slack to fund a village
        /// economy out of. Anything it holds has to come out of that, so this is a real cut to every
        /// lord's village income, the player's included. All of it goes to the village's one purse.
        /// </summary>
        public const float VillageShare = 0.2f;

        /// <summary>Carried from prefix to postfix across vanilla's homecoming bookkeeping.</summary>
        private class Homecoming
        {
            public int TradeGold;
            public int TaxBefore;
        }

        /// <summary>
        /// Keeps the village's share of the takings when a convoy reaches home.
        /// </summary>
        /// <remarks>
        /// Wrapped rather than replaced. Vanilla's method also handles the town sale and a governor perk,
        /// and it computes the owner's tax through <c>SettlementTaxModel</c>; reimplementing any of that
        /// to insert one line would put RBM on the hook for all of it. So the prefix photographs the
        /// purse and the tax ledger, vanilla does its work untouched, and the postfix reads the tax it
        /// actually charged back out of the difference -- exact by construction, whatever model or perk
        /// produced it.
        /// </remarks>
        [HarmonyPatch(typeof(VillagerCampaignBehavior), "OnSettlementEntered")]
        private static class VillagerHomecomingPatch
        {
            private static void Prefix(MobileParty mobileParty, Settlement settlement, out Homecoming __state)
            {
                __state = null;
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || mobileParty == null || !mobileParty.IsVillager
                    || settlement == null || !settlement.IsVillage || settlement.Village == null
                    || mobileParty.HomeSettlement != settlement)
                {
                    return;
                }

                __state = new Homecoming
                {
                    TradeGold = mobileParty.PartyTradeGold,
                    TaxBefore = settlement.Village.TradeTaxAccumulated
                };
            }

            private static void Postfix(MobileParty mobileParty, Settlement settlement, Homecoming __state)
            {
                if (__state == null)
                {
                    return;
                }

                // What vanilla actually charged, however it arrived at it -- at a commission rate of 1.0
                // that is the whole of the takings. Taking the share out of what was charged rather than
                // off the gross keeps this correct if a policy or perk ever moves the rate.
                int tax = settlement.Village.TradeTaxAccumulated - __state.TaxBefore;
                int kept = (int)(tax * VillageShare);
                if (kept <= 0)
                {
                    return;
                }

                settlement.Village.TradeTaxAccumulated -= kept;
                SettlementWealth.Credit(settlement, kept, SettlementWealth.Source.Homecoming);

                if (EconomyLog.IsEnabled)
                {
                    EconomyLog.Log("HOMECOME", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                        "takings " + __state.TradeGold + "d"
                        + "  ·  " + (tax - kept) + "d to owner"
                        + "  ·  kept " + kept + "d"
                        + "  ·  village purse now " + SettlementWealth.GetSettlementWealth(settlement) + "d");
                }
            }
        }
    }
}
