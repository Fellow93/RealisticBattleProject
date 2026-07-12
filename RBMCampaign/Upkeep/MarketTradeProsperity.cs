using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// Coin spent at a settlement's market stays there, the same way a soldier's carousing does.
    /// Buying goods off a town or village -- whether it is the player, a caravan, or a lord's party --
    /// feeds the place that sold them: a town's Prosperity, a village's Hearth, at the same
    /// <see cref="RBMConfig.RBMConfig.settlementProsperityPerGoldSpent"/> rate as local troop spending.
    /// Selling goods TO a settlement is left alone; only what a party pays out enriches the market.
    /// </summary>
    [HarmonyPatch(typeof(SellItemsAction), "Apply")]
    internal static class MarketTradeProsperity
    {
        // Signature mirrors SellItemsAction.Apply(receiverParty, payerParty, subject, number, currentSettlement).
        // The item flows receiverParty -> payerParty and the gold flows the other way, so a party BUYING
        // from a settlement is the case where the settlement is the receiverParty (it gives up the goods
        // and takes the coin).
        //
        // Runs BEFORE the sale so the price is read off the pre-trade supply. Apply removes the goods from
        // the town's roster as it runs, and GetItemPrice climbs as supply falls, so sampling it afterward
        // would price the whole lot at the depleted, dearest supply and over-credit the settlement.
        private static void Prefix(PartyBase receiverParty, PartyBase payerParty, ItemRosterElement subject, int number)
        {
            if (number <= 0 || RBMConfig.RBMConfig.settlementProsperityPerGoldSpent <= 0f)
            {
                return;
            }
            if (receiverParty == null || !receiverParty.IsSettlement)
            {
                return;
            }
            MobileParty buyer = payerParty?.MobileParty;
            if (buyer == null)
            {
                return;
            }

            Settlement settlement = receiverParty.Settlement;
            // Villages price off the town they are bound to, exactly as SellItemsAction resolves it.
            Town town = settlement.Town;
            if (town == null)
            {
                if (settlement.Village == null)
                {
                    return;
                }
                town = (settlement.Village.TradeBound != null)
                    ? settlement.Village.TradeBound.Town
                    : settlement.Village.Bound?.Town;
            }
            if (town == null)
            {
                return;
            }

            // The buyer pays the market's buy price, so isSelling is false. Priced off the pre-trade
            // supply (this is a Prefix) and multiplied out -- close enough to the gross the game charged
            // for a prosperity nudge, without simulating the per-unit price creep of a large lot.
            int gross = number * town.GetItemPrice(subject.EquipmentElement, buyer, false);
            if (gross <= 0)
            {
                return;
            }
            TroopUpkeep.CreditSettlement(settlement, gross);

            if (SpoilsLog.IsEnabled)
            {
                // Market trade fires far too often to log every transaction -- caravans and lords
                // haggle across the whole map. Throttled to the first buy per party per settlement
                // each day, the same way carousing is, enough to see the rate without flooding.
                float gain = gross * RBMConfig.RBMConfig.settlementProsperityPerGoldSpent;
                int day = (int)(CampaignTime.Now.ToHours / 24);
                SpoilsLog.LogOnce("trade-" + payerParty.Id + "-" + settlement.StringId + "-" + day, "TRADE", payerParty,
                    SpoilsLog.Describe(payerParty) + " buying at " + settlement.Name
                    + ": " + gross + " gold -> +" + gain.ToString("0.00")
                    + (settlement.Town != null ? " prosperity" : " hearth") + " (first buy of the day)");
            }
        }
    }
}
