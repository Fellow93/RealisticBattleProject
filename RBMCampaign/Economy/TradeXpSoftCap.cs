using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

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

        /// <summary>
        /// Caps the Trade XP a player's workshops can mint in a single day.
        /// </summary>
        /// <remarks>
        /// Workshop production XP does NOT come through <c>OnTradeProfitMade</c> and so the diminishing
        /// curve above never touched it. It is a separate path: every output a shop sends to town calls
        /// <see cref="SkillLevelingManager.OnProductionProducedToWarehouse"/>, which grants
        /// <c>WorkshopModel.GetTradeXpPerWarehouseProduction</c> = base value x0.1 of that one unit.
        ///
        /// Two RBM changes make that a firehose. The whole price list moved to the historical x10 scale,
        /// so each unit's base value -- and therefore its XP -- is an order of magnitude larger; and RBM
        /// runs many more production cycles per day than vanilla (RBMWorkshopCycle relaxes the
        /// profit floor, ArtisanOutput scales a named shop's speed by town prosperity), so a prosperous
        /// velvet weavery clears a stack of high-value cycles on the FIRST daily tick and hands the whole
        /// pile of XP over at once. The observed result was 50+ Trade XP from one tick of one shop.
        ///
        /// The curve above is the wrong tool here: it acts per transaction, and this is a burst of many
        /// small per-unit grants, each under the knee. So instead the grants are summed against a flat
        /// daily budget and the day's total is clamped -- a shop still earns its owner a decent, steady
        /// Trade income (it is an expensive investment) without turning the first day of ownership into a
        /// free skill level. The grant is taken over entirely so a partial award at the cap edge is exact.
        ///
        /// Gated on <see cref="RBMConfig.RBMConfig.rbmCampaignEnabled"/> rather than the price toggle,
        /// because the dominant amplifier is the cycle throughput, which is campaign-gated, not the
        /// repricing. The per-day tally is plain process state: it resets when the map day rolls over, and
        /// a save/reload simply starts the running day fresh.
        /// </remarks>
        [HarmonyPatch(typeof(SkillLevelingManager), nameof(SkillLevelingManager.OnProductionProducedToWarehouse))]
        private static class WorkshopProductionXpCap
        {
            /// <summary>Most Trade XP a player's workshops may grant in one map day. Tunable.</summary>
            private const float PerDayCap = 20f;

            private static int _day = -1;
            private static float _grantedToday;

            private static bool Prefix(EquipmentElement production)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || Campaign.Current == null)
                {
                    return true;
                }

                int today = (int)CampaignTime.Now.ToDays;
                if (today != _day)
                {
                    _day = today;
                    _grantedToday = 0f;
                }

                float xp = Campaign.Current.Models.WorkshopModel.GetTradeXpPerWarehouseProduction(production);
                if (xp <= 0f)
                {
                    return false;
                }

                float remaining = PerDayCap - _grantedToday;
                if (remaining <= 0f)
                {
                    return false;
                }

                float grant = (xp < remaining) ? xp : remaining;
                _grantedToday += grant;
                Hero.MainHero.AddSkillXp(DefaultSkills.Trade, grant);
                return false;
            }
        }
    }
}
