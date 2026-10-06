using System;
using System.Collections.Generic;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    // One row of the Clan gold tab: a kind of one-off gold, its signed total over the window, and on how
    // many days it occurred; hover for the day-by-day amounts. The tab's Net row uses the same shape.
    public class RBMLedgerClanGoldRowVM : ViewModel
    {
        public RBMLedgerClanGoldRowVM(string kindName, string total, string days, BasicTooltipViewModel hint)
        {
            KindName = kindName;
            Total = total;
            Days = days;
            Hint = hint;
        }

        [DataSourceProperty] public string KindName { get; }
        [DataSourceProperty] public string Total { get; }
        [DataSourceProperty] public string Days { get; }
        [DataSourceProperty] public BasicTooltipViewModel Hint { get; }
    }

    /// <summary>
    /// Projects <see cref="ClanEventGoldLedger"/> onto the RBM Ledger's Clan gold tab: one row per kind of
    /// one-off gold seen in the last <see cref="ClanEventGoldLedger.HistoryDays"/> days, signed (income
    /// positive, drains negative), plus a Net row. Read live each time the screen opens; nothing here is
    /// stored. This is the ONLY place that gold is reported -- it is past event gold and never belongs in
    /// the clan finance projection.
    /// </summary>
    internal static class RBMLedgerClanGold
    {
        public static MBBindingList<RBMLedgerClanGoldRowVM> BuildRows(out RBMLedgerClanGoldRowVM net)
        {
            var rows = new MBBindingList<RBMLedgerClanGoldRowVM>();
            int window = ClanEventGoldLedger.HistoryDays;
            int today = ClanEventGoldLedger.CurrentDay;
            // Signed net per day offset (0 = today), for the Net row and its hover.
            int[] netByOffset = new int[window];
            bool[] anyByOffset = new bool[window];

            foreach (EventGoldKind kind in (EventGoldKind[])Enum.GetValues(typeof(EventGoldKind)))
            {
                int sign = ClanEventGoldLedger.IsDrain(kind) ? -1 : 1;
                long total = 0L;
                int days = 0;
                var lines = new List<KeyValuePair<int, int>>();
                for (int offset = 0; offset < window; offset++)
                {
                    int gold = ClanEventGoldLedger.GetDay(kind, today - offset);
                    if (gold <= 0)
                    {
                        continue;
                    }
                    int signed = sign * gold;
                    total += signed;
                    days++;
                    netByOffset[offset] += signed;
                    anyByOffset[offset] = true;
                    lines.Add(new KeyValuePair<int, int>(offset, signed));
                }
                if (days == 0)
                {
                    continue;
                }
                string name = KindName(kind);
                rows.Add(new RBMLedgerClanGoldRowVM(name, Signed(total), days.ToString(), BuildHint(name, lines, window)));
            }

            long netTotal = 0L;
            int netDays = 0;
            var netLines = new List<KeyValuePair<int, int>>();
            for (int offset = 0; offset < window; offset++)
            {
                if (!anyByOffset[offset])
                {
                    continue;
                }
                netTotal += netByOffset[offset];
                netDays++;
                netLines.Add(new KeyValuePair<int, int>(offset, netByOffset[offset]));
            }
            string netName = new TextObject("{=RBM_LEDGER_CG_NET}Net").ToString();
            net = new RBMLedgerClanGoldRowVM(netName, Signed(netTotal), netDays.ToString(), BuildHint(netName, netLines, window));
            return rows;
        }

        // The day-by-day amounts of one row, newest first; null when the row has none (no tooltip).
        private static BasicTooltipViewModel BuildHint(string name, List<KeyValuePair<int, int>> lines, int window)
        {
            if (lines.Count == 0)
            {
                return null;
            }
            var sb = new System.Text.StringBuilder();
            sb.Append(new TextObject("{=RBM_LEDGER_CG_HINT_HDR}{NAME}, last {DAYS} days")
                .SetTextVariable("NAME", name).SetTextVariable("DAYS", window).ToString());
            foreach (KeyValuePair<int, int> l in lines)
            {
                string day = l.Key == 0
                    ? new TextObject("{=RBM_LEDGER_TODAY}Today").ToString()
                    : "-" + l.Key + "d";
                sb.Append('\n').Append(day).Append(": ").Append(Signed(l.Value));
            }
            string text = sb.ToString();
            return new BasicTooltipViewModel(() => text);
        }

        private static string Signed(long gold)
        {
            return gold > 0 ? "+" + gold : gold.ToString();
        }

        private static string KindName(EventGoldKind kind)
        {
            switch (kind)
            {
                case EventGoldKind.LeaderCut: return new TextObject("{=RBM_LEDGER_CG_LEADER_CUT}Leader's cut of spoils").ToString();
                case EventGoldKind.CompanionSpoils: return new TextObject("{=RBM_LEDGER_CG_COMPANION}Companions' share of spoils").ToString();
                case EventGoldKind.Minting: return new TextObject("{=RBM_LEDGER_CG_MINTING}Mint cuts").ToString();
                case EventGoldKind.UpgradeGold: return new TextObject("{=RBM_LEDGER_CG_UPGRADE}Promotions paid in gold").ToString();
                case EventGoldKind.BloodMoney: return new TextObject("{=RBM_LEDGER_CG_BLOOD_MONEY}Blood money").ToString();
                case EventGoldKind.ShipScrap: return new TextObject("{=RBM_LEDGER_CG_SHIP_SCRAP}Ship scrap").ToString();
                default: return kind.ToString();
            }
        }
    }
}
