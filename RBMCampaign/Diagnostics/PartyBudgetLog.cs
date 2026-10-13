using System;
using System.IO;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using RC = RBMConfig.RBMConfig;

namespace RBMCampaign
{
    /// <summary>
    /// The AI lords' party budget log, under logs/partybudget next to the config: the daily wage budget
    /// <see cref="PartyWageBudget"/> sets for each AI clan's war parties, and vanilla's army check each clan leader
    /// goes through (<see cref="ArmyCheckLog"/>, read-only). One file per process launch, opened lazily
    /// on the first line, written through <see cref="BufferedLogWriter"/> (flushed hourly and before a save, closed
    /// on game end) and pruned by <see cref="LogRetention"/>. Built like <see cref="MercenaryLog"/>. Gated by its own
    /// Debug &amp; Logging toggle, <c>partyBudgetLoggingEnabled</c> (off by default), on top of <c>rbmCampaignEnabled</c>.
    ///
    /// Every line is <c>time  TAG  who  message</c>; the tags are listed in the file header and are stable.
    /// </summary>
    internal static class PartyBudgetLog
    {
        private static readonly object _fileLock = new object();
        private static bool _fileLogFailed;
        private static bool _fileOpened;
        private static BufferedLogWriter _writer;
        private static int _lastDayKey = -1;

        private const int CategoryWidth = 10;
        private const int WhoWidth = 30;
        private static readonly string[] SeasonNames = { "Spring", "Summer", "Autumn", "Winter" };
        private static readonly string _launchStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        public static bool IsEnabled
        {
            get { return RC.rbmCampaignEnabled && RC.partyBudgetLoggingEnabled; }
        }

        private static string LogFolderPath
        {
            get { return Path.Combine(RBMConfig.Utilities.GetConfigFolderPath(), "logs", "partybudget"); }
        }

        private static string LogFilePath
        {
            get { return Path.Combine(LogFolderPath, "rbm_partybudget_" + _launchStamp + ".log"); }
        }

        private static void EnsureFileOpen()
        {
            lock (_fileLock)
            {
                if (_fileOpened || _fileLogFailed)
                {
                    return;
                }
                try
                {
                    Directory.CreateDirectory(LogFolderPath);
                    LogRetention.PruneOldest(LogFolderPath, "rbm_partybudget_*.log");

                    string nl = Environment.NewLine;
                    StringBuilder header = new StringBuilder();
                    header.Append("RBM party budget log — ").Append(DateTime.Now).Append(nl);
                    header.Append(nl);
                    header.Append("Columns:  time  ·  tag  ·  clan or party  ·  message").Append(nl);
                    header.Append(nl);
                    header.Append("Daily wage budget (once a day per AI clan, in place of vanilla's gold bands):").Append(nl);
                    header.Append("  on hand = clan leader's gold + every other lord-party leader's gold - reserve ")
                        .Append(PartyWageBudget.WorkingReserve).Append(nl);
                    header.Append("  steady  = yesterday's applied daily gold change with the field parties' own flows taken out")
                        .Append(nl);
                    header.Append("            (clan leader's party wage, member-party purse top-ups and skims)").Append(nl);
                    header.Append("  budget  = max(0, steady + on hand / ").Append(PartyWageBudget.RunwayDays)
                        .Append(", on hand / ").Append(PartyWageBudget.SavingsFloorDays).Append(")   (the second term is the savings floor)").Append(nl);
                    header.Append("  share   = budget split by party size limit (clan leader's party x")
                        .Append(PartyWageBudget.LeaderPartyWeight.ToString("0.0")).Append("), floored at ")
                        .Append((PartyWageBudget.FloorShareOfSizeLimit * 100f).ToString("0"))
                        .Append("% of the size limit at the average wage; it is the party's daily wage limit").Append(nl);
                    header.Append(nl);
                    header.Append("Army check: vanilla's gates (DefaultArmyManagementCalculationModel.CanLordCreateArmy), read back; the strength floor and quota are RBM's:").Append(nl);
                    header.Append("  leader party size ratio > 0.6, food > the siege food threshold, clan influence > 100, not a mercenary,").Append(nl);
                    header.Append("  not at sea, at war with a faction holding fiefs; callable members (ratio > 0.6, food > 15 days, within ")
                        .Append("vanilla's call range, ...),").Append(nl);
                    header.Append("  at most ").Append((ArmyCallReachPatch.KingdomQuota * 100f).ToString("0"))
                        .Append("% of the kingdom's war parties in armies (vanilla 70%);").Append(nl);
                    header.Append("  leader + members' siege strength >= ").Append(ArmyStrengthFloorPatch.ArmyStrengthFloor.ToString("0"))
                        .Append(" (RBM; vanilla 1000)").Append(nl);
                    header.Append(nl);
                    header.Append("Tags:").Append(nl);
                    header.Append("  BUDGET      a clan's day: leader gold, other leaders' purses, reserve, on hand, steady income and its parts, budget").Append(nl);
                    header.Append("  SHARE       one war party under the BUDGET line above it: weight, share, limit set (and old), wage, men, size limit, affordable men").Append(nl);
                    header.Append("  KEEP        a clan with no income record yet (first day after a load or a new clan): its limits were left as they were").Append(nl);
                    header.Append("  COMPANY     a mercenary company: no wage limit (vanilla's unlimited value)").Append(nl);
                    header.Append("  ARMY-CHECK  a clan leader evaluated for raising an army, once per leader per day: result, the first gate that failed,").Append(nl);
                    header.Append("              leader ratio vs 0.6, food, influence, the parties that qualify as members and the army's strength vs the floor,").Append(nl);
                    header.Append("              the free lords passed over (out of range / short of food / under the size ratio) and the quota room").Append(nl);
                    header.Append("  PATCH       a patch that could not be applied as expected (the vanilla rule stays in force there)").Append(nl);
                    header.Append(nl);

                    _writer = BufferedLogWriter.Open(LogFilePath);
                    _writer.Write(header.ToString());
                    _writer.Flush();
                    _fileOpened = true;
                }
                catch
                {
                    _fileLogFailed = true;
                }
            }
        }

        public static void Log(string tag, Clan clan, string message)
        {
            Log(tag, clan != null ? Who(clan) : "", message);
        }

        public static void Log(string tag, MobileParty party, string message)
        {
            Log(tag, party != null ? Who(party) : "", message);
        }

        public static void Log(string tag, string who, string message)
        {
            if (!IsEnabled)
            {
                return;
            }
            string line = DateTime.Now.ToString("HH:mm:ss") + "  "
                + (tag ?? "").PadRight(CategoryWidth) + "  "
                + Clip(who ?? "", WhoWidth).PadRight(WhoWidth) + "  "
                + message;
            lock (_fileLock)
            {
                string divider = DayDividerIfChanged();
                WriteToFile(divider != null ? Environment.NewLine + divider + Environment.NewLine + line : line);
            }
        }

        public static string Who(Clan clan)
        {
            if (clan == null)
            {
                return "?";
            }
            return (clan.Name != null ? clan.Name.ToString() : "?") + " [" + clan.StringId + "]";
        }

        public static string Who(MobileParty party)
        {
            if (party == null)
            {
                return "?";
            }
            if (party.LeaderHero != null && party.LeaderHero.Name != null)
            {
                return party.LeaderHero.Name.ToString();
            }
            return party.Name != null ? party.Name.ToString() : party.StringId;
        }

        private static string Clip(string text, int width)
        {
            return text.Length <= width ? text : text.Substring(0, width);
        }

        private static string DayDividerIfChanged()
        {
            if (!SpoilsLog.CampaignClockReady())
            {
                return null;
            }
            try
            {
                CampaignTime now = CampaignTime.Now;
                int year = now.GetYear;
                int dayOfYear = now.GetDayOfYear;
                int dayKey = year * 1000 + dayOfYear;
                if (dayKey == _lastDayKey)
                {
                    return null;
                }
                _lastDayKey = dayKey;
                int season = Math.Max(0, Math.Min(SeasonNames.Length - 1, (int)now.GetSeasonOfYear));
                return string.Format("════════ campaign {0}-{1:000}  ·  {2} ════════", year, dayOfYear + 1, SeasonNames[season]);
            }
            catch
            {
                return null;
            }
        }

        private static void WriteToFile(string message)
        {
            if (_fileLogFailed)
            {
                return;
            }
            EnsureFileOpen();
            lock (_fileLock)
            {
                if (!_fileOpened)
                {
                    return;
                }
                if (_writer == null || !_writer.WriteLine(message))
                {
                    _fileLogFailed = true;
                }
            }
        }
    }
}
