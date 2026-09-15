using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// Measures the money parties put into a settlement's market, and take out of it, by trading.
    ///
    /// This exists to settle one question the ledger turns on. A town pays the countryside for its food
    /// every season and the basket only returns about a fourteenth of it; the rest reaches the owner as
    /// village tax. Whether that is a leak depends entirely on how much of it lords hand back over the
    /// counter -- and they do hand some back, since a party buying food routes through
    /// <c>SellItemsAction</c> into <c>GiveGoldAction.ApplyForCharacterToSettlement</c>, real gold out of
    /// a hero's own purse and into the settlement's. What is not known is the FRACTION. Payroll --
    /// party and garrison wages -- is deducted from clan gold and credited to nobody, so it is a true
    /// sink; purchases are not. The split between them is what sizes the rest of the work.
    ///
    /// So rather than argue it, every trade a party makes with a settlement is weighed and totalled by
    /// who was on the other side of it. Diagnostics only: nothing here changes a denar.
    /// </summary>
    public static class PartyTradeFlow
    {
        /// <summary>A settlement's trading with parties over one day, by counterparty.</summary>
        private class DayTally
        {
            public readonly Dictionary<string, int> In = new Dictionary<string, int>();
            public int Out;
            public int Trades;
        }

        private static readonly Dictionary<Settlement, DayTally> _tallies = new Dictionary<Settlement, DayTally>();

        /// <summary>
        /// Records money reaching a settlement's market by a route that is not counter trade.
        ///
        /// RBM's own soldier spending -- rations, luxuries, and the carousing and healing that buy no
        /// item at all -- credits the town's purse directly rather than through
        /// <c>SellItemsAction</c>, so the patch below is blind to it. Left out, the tally would
        /// undercount exactly the flow it exists to measure: that coin is a lord's wage bill coming
        /// back over a tavern counter, which is clan gold returning by another door.
        /// </summary>
        public static void RegisterInflow(Settlement settlement, string kind, int gold)
        {
            if (!EconomyLog.IsEnabled || settlement == null || gold <= 0)
            {
                return;
            }

            DayTally tally;
            if (!_tallies.TryGetValue(settlement, out tally))
            {
                tally = new DayTally();
                _tallies[settlement] = tally;
            }
            tally.Trades++;
            int running;
            tally.In.TryGetValue(kind, out running);
            tally.In[kind] = running + gold;
        }

        /// <summary>Drops the previous session's tallies. Pure diagnostics, so a session hook is enough.</summary>
        public static void Reset()
        {
            _tallies.Clear();
        }

        /// <summary>
        /// Writes down a settlement's day of counter trade and clears it. Called from the daily
        /// settlement tick, so each settlement gets one line rather than one per item stack -- the food
        /// behaviour alone calls the trade action once per item type per party, which at transaction
        /// granularity would bury the log it is meant to inform.
        /// </summary>
        public static void FlushDaily(Settlement settlement)
        {
            DayTally tally;
            if (settlement == null || !_tallies.TryGetValue(settlement, out tally))
            {
                return;
            }
            _tallies.Remove(settlement);

            if (!EconomyLog.IsEnabled || (tally.Out == 0 && tally.In.Count == 0))
            {
                return;
            }

            int inTotal = 0;
            foreach (KeyValuePair<string, int> kv in tally.In)
            {
                inTotal += kv.Value;
            }

            string breakdown = "";
            foreach (KeyValuePair<string, int> kv in tally.In)
            {
                breakdown += "  " + kv.Key + " " + kv.Value + "d";
            }

            EconomyLog.Log("COUNTER", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                "parties paid in " + inTotal + "d  ·  settlement paid out " + tally.Out + "d"
                + "  ·  net " + (inTotal - tally.Out) + "d"
                + "  ·  " + tally.Trades + " trades  ·" + breakdown);
        }

        /// <summary>
        /// Weighs the settlement's purse either side of a trade. Reading the gold difference rather than
        /// recomputing the price keeps this exact through every branch vanilla takes -- caravan against
        /// hero, the commission split-off, the no-buyer case -- without restating any of that logic.
        /// </summary>
        [HarmonyPatch(typeof(SellItemsAction), "ApplyInternal")]
        private static class SettlementTradeFlowPatch
        {
            private static void Prefix(PartyBase sellerParty, PartyBase buyerParty, out object __state)
            {
                __state = null;
                if (!EconomyLog.IsEnabled || !RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    return;
                }

                Settlement settlement = SettlementSideOf(sellerParty, buyerParty);
                if (settlement == null || settlement.SettlementComponent == null)
                {
                    return;
                }

                __state = new object[] { settlement, settlement.SettlementComponent.Gold };
            }

            private static void Postfix(PartyBase sellerParty, PartyBase buyerParty, object __state)
            {
                object[] state = __state as object[];
                if (state == null)
                {
                    return;
                }

                Settlement settlement = (Settlement)state[0];
                int delta = settlement.SettlementComponent.Gold - (int)state[1];
                if (delta == 0)
                {
                    return;
                }

                DayTally tally;
                if (!_tallies.TryGetValue(settlement, out tally))
                {
                    tally = new DayTally();
                    _tallies[settlement] = tally;
                }
                tally.Trades++;

                if (delta < 0)
                {
                    tally.Out += -delta;
                    return;
                }

                // Whoever was NOT the settlement is the party that just paid.
                PartyBase counterparty = (sellerParty != null && sellerParty.IsSettlement) ? buyerParty : sellerParty;
                string kind = KindOf(counterparty);
                int running;
                tally.In.TryGetValue(kind, out running);
                tally.In[kind] = running + delta;
            }
        }

        private static Settlement SettlementSideOf(PartyBase sellerParty, PartyBase buyerParty)
        {
            if (sellerParty != null && sellerParty.IsSettlement)
            {
                return sellerParty.Settlement;
            }
            if (buyerParty != null && buyerParty.IsSettlement)
            {
                return buyerParty.Settlement;
            }
            return null;
        }

        /// <summary>
        /// Who paid, in the terms the question is asked in. "lord" is the one that matters -- that is
        /// clan gold coming back over the counter, the flow whose size decides whether payroll is the
        /// only real sink in the economy.
        /// </summary>
        private static string KindOf(PartyBase party)
        {
            MobileParty mobileParty = party != null ? party.MobileParty : null;
            if (mobileParty == null)
            {
                return "other";
            }
            if (mobileParty.IsMainParty)
            {
                return "player";
            }
            if (mobileParty.IsLordParty)
            {
                return "lord";
            }
            if (mobileParty.IsCaravan)
            {
                return "caravan";
            }
            if (mobileParty.IsVillager)
            {
                return "villager";
            }
            if (mobileParty.IsGarrison || mobileParty.IsMilitia)
            {
                return "garrison";
            }
            if (mobileParty.IsBandit)
            {
                return "bandit";
            }
            return "other";
        }
    }
}
