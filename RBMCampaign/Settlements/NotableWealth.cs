using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// Stops a notable's purse being a furnace, and returns what it used to burn to the market.
    ///
    /// Every notable holds a purse pinned to a thousand-denar band around 10,000, and vanilla keeps it
    /// there with a converter that runs nightly: above 10,500 the surplus is turned into standing at
    /// 500 gold a point, and below 4,500 standing is sold back at the same rate. The upward leg is
    /// <c>GiveGoldAction.ApplyBetweenCharacters(notable, null, …)</c> -- recipient <c>null</c>, which
    /// <c>ApplyInternal</c> passes over without crediting anybody. The money is not moved. It is
    /// destroyed.
    ///
    /// That matters because everything feeding the purse came out of the town. A named workshop's day is
    /// a net withdrawal from citizen wealth -- vanilla credits the shop <c>min(1000, price)</c> for each
    /// output and bills it for one input, and every trade-good recipe in the file turns one input into
    /// two dearer outputs -- and the owner then draws a fifth of the accumulated capital into his purse.
    /// A caravan buys and sells against <c>Town.Gold</c> on both legs, and its owner draws a fifth of
    /// the float above its working capital the same way. Both withdrawals are honest transfers; neither
    /// creates or destroys anything. The destruction is entirely at the converter, one step later, and
    /// it is therefore the only place that needs fixing to close the whole leak.
    ///
    /// So the surplus is credited to the market instead of being annihilated, and the refill leg is paid
    /// for out of that same market rather than conjured. A notable is a citizen; his money is the town's
    /// money, sitting in a named pocket.
    /// </summary>
    /// <remarks>
    /// A replacing prefix rather than a redirect on <c>GiveGoldAction.ApplyInternal</c>, because two RBM
    /// prefixes already fire on the null-participant case there (<see cref="NativeTradeConservation"/>
    /// funding a stalled trade, <see cref="RansomFunding"/> funding a ransom) and each is deliberately
    /// gated on its own narrow marker. A third would have to distinguish itself from both by inspecting
    /// the caller. Reimplementing eleven lines of arithmetic here is cheaper and cannot collide.
    ///
    /// The arithmetic is vanilla's, kept in the same shape and with the same integer truncation, so the
    /// standing a notable gains for a given purse is unchanged to the point. Only the counterparty moves.
    ///
    /// <c>NotablePowerManagementBehavior</c> is safe to reach through <c>PatchAll</c>: it is a
    /// <c>CampaignBehaviorBase</c> whose only statics are <c>private const int</c>, with no type
    /// initializer to trip. This is deliberately NOT patched at
    /// <c>DefaultClanFinanceModel.CalculateHeroIncomeFromAssets</c>, which is where the withdrawals
    /// themselves happen -- that class carries the <c>Game.Current</c>-reading cctor trap documented in
    /// <see cref="WorkshopPurse"/>, and the withdrawals are conserving anyway, so there is nothing to fix
    /// there.
    ///
    /// KNOWN RESIDUAL -- alley income. <c>CalculateHeroIncomeFromAssets</c> adds a flat 30 a day per
    /// owned alley with no counterparty debit: a mint. Before this change the converter destroyed it
    /// again, so it was net zero on the world's money; now it is credited to the market, which makes it
    /// a small net faucet of 30 a day per alley -- 60 to 120 a town, against the 12,000-plus a day this
    /// closes. Fixing it means either charging the townspeople for the racket (which is what an alley
    /// is) or excluding it here; both are gameplay decisions rather than plumbing, so it is left named
    /// rather than quietly patched. It belongs in the "still open" table of
    /// <c>docs/economy-money-flows.md</c> until then.
    /// </remarks>
    public static class NotableWealth
    {
        // Vanilla's own three constants, from NotablePowerManagementBehavior. Named rather than inlined
        // so the band and the exchange rate can be checked against the game source at a glance.
        private const int GoldLimitForNotablesToStartGainingPower = 10000;

        private const int GoldLimitForNotablesToStartLosingPower = 5000;

        private const int GoldNeededToGainOnePower = 500;

        /// <summary>
        /// Puts money into the settlement a notable lives in, preferring the market he trades in and
        /// falling back to the settlement's own purse where there is no market -- a village.
        /// </summary>
        /// <remarks>
        /// Villages reach this only by way of a player gift, being otherwise unable to earn a denar:
        /// they hold no workshop, caravan or alley. Their money is sent to the treasury rather than the
        /// village purse because vanilla clamps a village's <c>Gold</c> back to 1,000 every tick, which
        /// would swallow the credit whole.
        /// </remarks>
        private static int Give(Settlement settlement, int amount)
        {
            if (settlement == null || amount <= 0)
            {
                return 0;
            }
            return SettlementWealth.HasCitizenPurse(settlement)
                ? SettlementWealth.CreditCitizens(settlement, amount, SettlementWealth.Source.NotableWealth)
                : SettlementWealth.Credit(settlement, amount, SettlementWealth.Source.NotableWealth);
        }

        /// <summary>
        /// Takes money from the same pocket <see cref="Give"/> fills, and reports what it could actually
        /// find. The return value is the authority, not the balance read before it.
        /// </summary>
        private static int Take(Settlement settlement, int amount)
        {
            if (settlement == null || amount <= 0)
            {
                return 0;
            }
            return SettlementWealth.HasCitizenPurse(settlement)
                ? SettlementWealth.DebitCitizens(settlement, amount, SettlementWealth.Source.NotableWealth)
                : SettlementWealth.Debit(settlement, amount, SettlementWealth.Source.NotableWealth);
        }

        [HarmonyPatch(typeof(NotablePowerManagementBehavior), "BalanceGoldAndPowerOfNotable")]
        private static class ConverterRoutingPatch
        {
            private static bool Prefix(Hero notable)
            {
                if (notable == null)
                {
                    return false;
                }
                Settlement settlement = notable.CurrentSettlement;
                if (settlement == null)
                {
                    // Nowhere to route to. Do nothing at all rather than fall through to vanilla, which
                    // would destroy the surplus -- a notable between settlements is not a reason to
                    // burn money. He converts again tomorrow, wherever he is standing.
                    return false;
                }

                if (notable.Gold > GoldLimitForNotablesToStartGainingPower + GoldNeededToGainOnePower)
                {
                    int lots = (notable.Gold - GoldLimitForNotablesToStartGainingPower)
                        / GoldNeededToGainOnePower;
                    int amount = lots * GoldNeededToGainOnePower;
                    if (amount > 0)
                    {
                        notable.ChangeHeroGold(-amount);
                        // Credit what the purse would take. A market cannot refuse money, so this is a
                        // formality today -- but it is read rather than assumed, so that a future cap on
                        // citizen wealth cannot silently vanish the difference.
                        int taken = Give(settlement, amount);
                        if (taken < amount)
                        {
                            notable.ChangeHeroGold(amount - taken);
                            lots = taken / GoldNeededToGainOnePower;
                        }
                        notable.AddPower(lots);
                    }
                    return false;
                }

                if (notable.Gold < GoldLimitForNotablesToStartLosingPower - GoldNeededToGainOnePower
                    && notable.Power > 0f)
                {
                    int lots = (GoldLimitForNotablesToStartLosingPower - notable.Gold)
                        / GoldNeededToGainOnePower;
                    int paid = Take(settlement, lots * GoldNeededToGainOnePower);
                    // Standing is sold in whole points, so anything the market could find beyond the
                    // last full point goes straight back rather than being pocketed unpaid for.
                    int fundedLots = paid / GoldNeededToGainOnePower;
                    int used = fundedLots * GoldNeededToGainOnePower;
                    if (paid > used)
                    {
                        Give(settlement, paid - used);
                    }
                    if (fundedLots > 0)
                    {
                        notable.ChangeHeroGold(used);
                        notable.AddPower(-fundedLots);
                    }
                }
                return false;
            }
        }
    }
}
