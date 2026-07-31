using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using RC = RBMConfig.RBMConfig;

namespace RBMCampaign
{
    /// <summary>
    /// The stabilizing half of the supply-caravan system: when a caravan runs from a genuinely wealthy
    /// town to a struggling one of the same kingdom, it also injects a capped slug of citizen wealth into
    /// the destination as a REPAYABLE INVESTMENT -- the realm's rich cities propping up a dying holding so
    /// it can afford goods and climb out of a death spiral. The injection is booked as a debt the
    /// destination owes the source, with a modest return baked in; the destination repays it only once it
    /// has recovered, and then out of its own over-accumulation.
    ///
    /// Recovery and repayment are deliberately tied to <see cref="WealthTax"/>'s existing HOARD bracket
    /// (citizen wealth above 1000× prosperity): a town only ever repays while it is hoarding, and the
    /// repayment is carved off that punitive hoard levy -- up to half of it -- before the owner and fief
    /// take their cut. So a struggling debtor is never bled, and a recovered one funds its rescuers from
    /// money it plainly did not need.
    ///
    /// Money moves only through <see cref="SettlementWealth"/> (<c>CaravanInvest</c>/<c>CaravanRepay</c>
    /// sources), never a native trade path -- the same rule the rest of the system keeps. Injection and
    /// repayment are conserved between the two pots: nothing is minted or burned, and a debtor that never
    /// recovers is simply a bad investment the source eats.
    /// </summary>
    internal static class RBMCaravanInvestment
    {
        // "Wealthy" and "struggling" are judged PER POINT OF PROSPERITY -- the same yardstick the
        // wealth-tax hoard line (1000 denars of citizen wealth per prosperity) uses -- so they track the
        // economy's real scale instead of fixed denars. A source above WealthyPerProsperity×prosperity is
        // rich enough to invest abroad; a destination below StrugglePerProsperity×prosperity is
        // cash-starved for its size.
        private const float WealthyPerProsperity = 300f;
        private const float StrugglePerProsperity = 300f;

        // ...and the destination's prosperity must also be below this share of its countryside target.
        private const float StruggleProsperityFrac = 0.9f;

        // Share of the source's excess wealth (above its wealthy line) one caravan may carry as investment.
        private const float InvestFraction = 0.2f;

        // Below this much (per point of the destination's prosperity), an injection is not worth doing.
        private const float MinInjectPerProsperity = 5f;

        // The return baked into the debt at injection: the debtor repays principal × (1 + this).
        private const float InvestReturn = 0.15f;

        // Share of a hoarding town's daily hoard levy diverted to repaying its investment debt first.
        public const float RepayShareOfHoardTax = 0.5f;

        // Leverage ceilings on the outstanding debt (return included), per point of prosperity so they
        // scale with the town rather than a fixed denar figure.
        private const float MaxOwedPerProsperity = 500f;
        private const float MaxLentPerProsperity = 1000f;

        // debtorSettlementId + "#" + creditorSettlementId  ->  outstanding owed (return included).
        private static Dictionary<string, int> _debt = new Dictionary<string, int>();

        public static bool IsEnabled
        {
            get { return RC.rbmCampaignEnabled && RC.kingdomCaravansEnabled && RC.caravanInvestmentEnabled; }
        }

        private static string Key(string debtorId, string creditorId)
        {
            return debtorId + "#" + creditorId;
        }

        /// <summary>
        /// If <paramref name="src"/> is wealthy and <paramref name="dst"/> struggling, injects capital
        /// from the source's market into the destination's and books the debt. Called from
        /// <see cref="RBMCaravanArrival"/> on arrival, before the sell leg, so the propped-up town can
        /// then afford the goods.
        /// </summary>
        public static void ApplyInjection(Settlement src, Settlement dst)
        {
            if (!IsEnabled || src == null || dst == null || src == dst)
            {
                return;
            }
            if (!Qualifies(src, dst))
            {
                return;
            }

            int inject = PlanInjection(src, dst);
            if (inject <= 0)
            {
                return;
            }

            int moved = SettlementWealth.DebitCitizens(src, inject, SettlementWealth.Source.CaravanInvest);
            if (moved <= 0)
            {
                return;
            }
            SettlementWealth.CreditCitizens(dst, moved, SettlementWealth.Source.CaravanInvest);

            int add = (int)(moved * (1f + InvestReturn));
            string key = Key(dst.StringId, src.StringId);
            _debt.TryGetValue(key, out int outstanding);
            _debt[key] = outstanding + add;

            CaravanLog.Log("INVEST", CaravanLog.Name(dst),
                "capital from " + CaravanLog.Name(src) + "  ·  injected " + moved + "d  ·  owes " + _debt[key] + "d");
        }

        /// <summary>
        /// Whether an injection from <paramref name="src"/> to <paramref name="dst"/> would actually happen
        /// right now -- the dispatcher uses this to decide whether to send a pure relief caravan to a
        /// struggling town that has no goods-trade route.
        /// </summary>
        public static bool WouldInvest(Settlement src, Settlement dst)
        {
            return IsEnabled && src != null && dst != null && src != dst
                && Qualifies(src, dst) && PlanInjection(src, dst) > 0;
        }

        /// <summary>A town's prosperity, or 0 for anything that is not a town (no market to judge).</summary>
        private static float Prosperity(Settlement settlement)
        {
            return (settlement != null && settlement.Town != null) ? settlement.Town.Prosperity : 0f;
        }

        private static bool Qualifies(Settlement src, Settlement dst)
        {
            float srcProsperity = Prosperity(src);
            float dstProsperity = Prosperity(dst);
            if (srcProsperity < 1f || dstProsperity < 1f)
            {
                return false;
            }

            // Source rich for its size, destination cash-starved for its size -- judged per prosperity.
            if (SettlementWealth.GetCitizenWealth(src) <= WealthyPerProsperity * srcProsperity)
            {
                return false;
            }
            if (SettlementWealth.GetCitizenWealth(dst) >= StrugglePerProsperity * dstProsperity)
            {
                return false;
            }

            // ...and the destination's prosperity is below the fraction of what its countryside supports.
            float target = RBMProsperityEquilibrium.TargetProsperity(dst);
            if (target > 0f && dst.Town.Prosperity >= StruggleProsperityFrac * target)
            {
                return false;
            }

            if (OutstandingByDebtor(dst.StringId) >= MaxOwedPerProsperity * dstProsperity)
            {
                return false;
            }
            if (OutstandingByCreditor(src.StringId) >= MaxLentPerProsperity * srcProsperity)
            {
                return false;
            }
            return true;
        }

        private static int PlanInjection(Settlement src, Settlement dst)
        {
            float srcProsperity = Prosperity(src);
            float dstProsperity = Prosperity(dst);
            if (srcProsperity < 1f || dstProsperity < 1f)
            {
                return 0;
            }

            // The source's wealth above its wealthy line, and the destination's gap up to its struggle
            // line -- both per prosperity, so the figures track the economy's scale.
            int excess = SettlementWealth.GetCitizenWealth(src) - (int)(WealthyPerProsperity * srcProsperity);
            if (excess <= 0)
            {
                return 0;
            }
            int deficit = (int)(StrugglePerProsperity * dstProsperity) - SettlementWealth.GetCitizenWealth(dst);
            if (deficit <= 0)
            {
                return 0;
            }
            int inject = Math.Min(deficit, (int)(excess * InvestFraction));

            // Keep the resulting debt (principal × (1 + return)) within the debtor's remaining room.
            int debtorRoom = (int)(MaxOwedPerProsperity * dstProsperity) - OutstandingByDebtor(dst.StringId);
            if (debtorRoom <= 0)
            {
                return 0;
            }
            int maxInjectForRoom = (int)(debtorRoom / (1f + InvestReturn));
            inject = Math.Min(inject, maxInjectForRoom);

            // Not worth doing below a minimum scaled to the destination's size.
            if (inject < (int)(MinInjectPerProsperity * dstProsperity))
            {
                return 0;
            }
            return inject;
        }

        /// <summary>
        /// Repays this town's investment debt out of its daily hoard levy, capped at <paramref name="cap"/>
        /// (half the levy). Called from <see cref="WealthTax"/>'s hoarding branch, before the owner and
        /// fief take their cut. Returns how much was actually repaid.
        /// </summary>
        public static int RepayFromHoardTax(Settlement debtor, int cap)
        {
            if (!IsEnabled || debtor == null || cap <= 0 || _debt.Count == 0)
            {
                return 0;
            }

            // Eligible creditors: a valid settlement not at war with the debtor. Prune debts whose
            // creditor no longer exists (forgiven). Debts to an enemy simply wait for peace.
            List<KeyValuePair<string, int>> eligible = new List<KeyValuePair<string, int>>();
            long totalOwed = 0;
            List<string> dead = null;
            foreach (KeyValuePair<string, int> pair in _debt)
            {
                SplitKey(pair.Key, out string debtorId, out string creditorId);
                if (debtorId != debtor.StringId || pair.Value <= 0)
                {
                    continue;
                }
                Settlement creditor = RBMCaravanRegister.FindSettlement(creditorId);
                if (creditor == null)
                {
                    (dead ?? (dead = new List<string>())).Add(pair.Key);
                    continue;
                }
                if (debtor.MapFaction != null && creditor.MapFaction != null
                    && debtor.MapFaction.IsAtWarWith(creditor.MapFaction))
                {
                    continue;
                }
                eligible.Add(new KeyValuePair<string, int>(creditorId, pair.Value));
                totalOwed += pair.Value;
            }
            if (dead != null)
            {
                foreach (string k in dead)
                {
                    _debt.Remove(k);
                }
            }
            if (eligible.Count == 0 || totalOwed <= 0)
            {
                return 0;
            }

            int repay = (int)Math.Min(cap, totalOwed);
            int moved = SettlementWealth.DebitCitizens(debtor, repay, SettlementWealth.Source.CaravanRepay);
            if (moved <= 0)
            {
                return 0;
            }

            int distributed = 0;
            for (int i = 0; i < eligible.Count; i++)
            {
                string creditorId = eligible[i].Key;
                int owed = eligible[i].Value;
                int share = (i == eligible.Count - 1)
                    ? (moved - distributed)
                    : (int)((long)moved * owed / totalOwed);
                if (share > owed)
                {
                    share = owed;
                }
                if (share <= 0)
                {
                    continue;
                }

                Settlement creditor = RBMCaravanRegister.FindSettlement(creditorId);
                if (creditor == null)
                {
                    continue;
                }
                SettlementWealth.CreditCitizens(creditor, share, SettlementWealth.Source.CaravanRepay);

                string key = Key(debtor.StringId, creditorId);
                _debt.TryGetValue(key, out int outstanding);
                outstanding -= share;
                if (outstanding <= 0)
                {
                    _debt.Remove(key);
                }
                else
                {
                    _debt[key] = outstanding;
                }
                distributed += share;

                CaravanLog.Log("REPAY", CaravanLog.Name(debtor),
                    "to " + CaravanLog.Name(creditor) + "  ·  repaid " + share + "d  ·  owes " + Math.Max(0, outstanding) + "d");
            }

            // Any coin taken but not distributed (integer rounding) goes back to the debtor's market.
            int leftover = moved - distributed;
            if (leftover > 0)
            {
                SettlementWealth.CreditCitizens(debtor, leftover, SettlementWealth.Source.CaravanRepay);
            }

            return distributed;
        }

        private static int OutstandingByDebtor(string debtorId)
        {
            int total = 0;
            foreach (KeyValuePair<string, int> pair in _debt)
            {
                SplitKey(pair.Key, out string d, out string _);
                if (d == debtorId)
                {
                    total += pair.Value;
                }
            }
            return total;
        }

        private static int OutstandingByCreditor(string creditorId)
        {
            int total = 0;
            foreach (KeyValuePair<string, int> pair in _debt)
            {
                SplitKey(pair.Key, out string _, out string c);
                if (c == creditorId)
                {
                    total += pair.Value;
                }
            }
            return total;
        }

        private static void SplitKey(string key, out string debtorId, out string creditorId)
        {
            int hash = key.IndexOf('#');
            if (hash < 0)
            {
                debtorId = key;
                creditorId = null;
                return;
            }
            debtorId = key.Substring(0, hash);
            creditorId = key.Substring(hash + 1);
        }

        public static void Reset()
        {
            _debt.Clear();
        }

        public static void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("RBM_caravanInvestDebt", ref _debt);
            if (_debt == null)
            {
                _debt = new Dictionary<string, int>();
            }
        }
    }
}
