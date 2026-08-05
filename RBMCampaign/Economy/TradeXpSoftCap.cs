using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;

namespace RBMCampaign
{
    /// <summary>
    /// Damps Trade skill XP on the reworked price scale so luxury trading no longer trivialises the skill.
    ///
    /// Trade XP is vanilla's flat 0.5 XP per denar of realised profit
    /// (<see cref="DefaultSkillLevelingManager.OnTradeProfitMade(PartyBase, int)"/>). <see cref="TradeGoodValues"/>
    /// moves the whole price list onto the historical x10 scale, so the same real trade now clears ~10x the
    /// denars -- and therefore ~10x the Trade XP. The inflation is NOT uniform: basic goods barely move
    /// (their scarcity cap dropped from vanilla's 10x to 2x, and their weight caps how many can be carried),
    /// while light high-value luxuries (velvet at 0.5kg, 26,500 base) balloon by an order of magnitude. A
    /// flat rate cut would gut basic-good trading to fix the luxury path, so instead the per-transaction
    /// profit is passed through a diminishing curve: small trades are credited in full, large lumps compress.
    ///
    ///   effective = profit                          for profit &lt;= <see cref="Knee"/>
    ///   effective = Knee + (profit - Knee)^Power     above it
    ///
    /// The player's whole shop-visit profit arrives as one call (accumulated in
    /// TradeSkillCampaignBehavior.PlayerInventoryUpdated), and caravan payouts arrive as one lump per payout,
    /// so the curve acts on the natural transaction unit. It also blunts the thin-market spike sale -- one
    /// luxury unit dumped into a bare town at the 8x cap -- which still mints XP on the sell side after the
    /// money loop was closed.
    ///
    /// Gated on <see cref="RBMConfig.RBMConfig.realisticTradeGoodPrices"/>: the toggle that applies the x10
    /// repricing that causes the inflation. With vanilla-scale prices there is nothing to damp, so the curve
    /// stays off and Trade XP is left untouched.
    /// </summary>
    public static class TradeXpSoftCap
    {
        /// <summary>Profit up to this (denars) is credited in full; the curve only bites above it.</summary>
        private const int Knee = 5000;

        /// <summary>Compression exponent applied to profit above the knee. &lt;1 == diminishing returns.</summary>
        private const double Power = 0.8;

        private static bool Active =>
            RBMConfig.RBMConfig.rbmCampaignEnabled && RBMConfig.RBMConfig.realisticTradeGoodPrices;

        /// <summary>Passes the transaction profit through the diminishing curve.</summary>
        private static int Damp(int tradeProfit)
        {
            if (tradeProfit <= Knee)
            {
                return tradeProfit;
            }

            double excess = tradeProfit - Knee;
            return Knee + (int)Math.Round(Math.Pow(excess, Power));
        }

        /// <summary>Player retail trades: <c>PartyBase.MainParty</c> with the shop-visit's total profit.</summary>
        [HarmonyPatch(typeof(DefaultSkillLevelingManager), nameof(DefaultSkillLevelingManager.OnTradeProfitMade), new[] { typeof(PartyBase), typeof(int) })]
        private static class PartyTradeProfitPatch
        {
            private static void Prefix(ref int tradeProfit)
            {
                if (Active && tradeProfit > Knee)
                {
                    tradeProfit = Damp(tradeProfit);
                }
            }
        }

        /// <summary>Caravan payouts: the caravan leader with a single payout lump.</summary>
        [HarmonyPatch(typeof(DefaultSkillLevelingManager), nameof(DefaultSkillLevelingManager.OnTradeProfitMade), new[] { typeof(Hero), typeof(int) })]
        private static class HeroTradeProfitPatch
        {
            private static void Prefix(ref int tradeProfit)
            {
                if (Active && tradeProfit > Knee)
                {
                    tradeProfit = Damp(tradeProfit);
                }
            }
        }
    }
}
