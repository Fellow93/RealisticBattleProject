using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;
using RC = RBMConfig.RBMConfig;

namespace RBMCampaign
{
    /// <summary>
    /// Everything the spoils system does is invisible from inside the game: pools live in a side
    /// dictionary, loot is awarded during a map event that has already closed, and the party screen
    /// only ever shows a bar. This writes it all to rbm_spoils.log next to the RBM config so the
    /// numbers can be checked after the fact.
    ///
    /// Enabled by the SpoilsLogging config flag. One file per play session, created when the
    /// campaign starts rather than at module load.
    /// </summary>
    internal static class SpoilsLog
    {
        private static readonly HashSet<string> _oncePerKey = new HashSet<string>();
        private static readonly object _fileLock = new object();
        private static bool _fileLogFailed;

        // The log file is not opened at module load. Lines emitted before a campaign starts (the
        // early hook-install traces) buffer here and are flushed into the campaign log when it is
        // opened, so a session yields a single file rather than a near-empty one from load time plus
        // the real one. If no campaign is started this session they are only ever printed to Debug.
        private static bool _fileOpened;
        private static readonly StringBuilder _pending = new StringBuilder();

        // The campaign day is printed once, as a divider, whenever it rolls over, rather than on every
        // line; this holds the last day written so a change can be spotted. -1 means "nothing yet".
        private static int _lastDayKey = -1;

        // The category of the last line written, so a blank line can be dropped in when the next line
        // belongs to a different kind of event and the two should read as separate groups.
        private static string _lastCategory;

        // Widths the three left columns are padded to so the message text starts at the same place on
        // every line and the eye can scan straight down. Category holds the longest tag ("UPGRADE").
        private const int CategoryWidth = 8;
        private static readonly string[] SeasonNames = { "Spring", "Summer", "Autumn", "Winter" };

        /// <summary>
        /// The timestamp is fixed for the span of a log so every line of a single run lands in the
        /// same file, and a fresh run gets a new name rather than overwriting the last one. It is
        /// rolled over when a campaign is started or loaded (see <see cref="StartCampaignLog"/>) so
        /// each play session stands alone.
        /// </summary>
        private static string _launchStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        public static bool IsEnabled
        {
            get { return RBMConfig.RBMConfig.spoilsLoggingEnabled; }
        }

        /// <summary>
        /// True when the log should carry the full per-stack detail. With it off the party-level summary
        /// lines still write, but the individual-soldier lines beneath them are dropped, so the log reads
        /// as what each party did rather than what each of its stacks did. Requires logging on at all.
        /// </summary>
        public static bool Verbose
        {
            get { return RBMConfig.RBMConfig.spoilsLoggingEnabled && RBMConfig.RBMConfig.spoilsVerboseLoggingEnabled; }
        }

        // Logs live under their own tree next to the config rather than loose beside it, and each
        // module's logs get their own folder: the spoils log is the campaign module's, so logs/campaign.
        private static string LogFolderPath
        {
            get { return Path.Combine(RBMConfig.Utilities.GetConfigFolderPath(), "logs", "campaign"); }
        }

        private static string LogFilePath
        {
            get { return Path.Combine(LogFolderPath, "rbm_spoils_" + _launchStamp + ".log"); }
        }

        public static void Reset()
        {
            lock (_fileLock)
            {
                _oncePerKey.Clear();
                _fileLogFailed = false;
                _fileOpened = false;
                _lastDayKey = -1;
                _lastCategory = null;
                if (!IsEnabled)
                {
                    _pending.Length = 0;
                    return;
                }
                try
                {
                    Directory.CreateDirectory(LogFolderPath);
                    File.WriteAllText(LogFilePath,
                        "RBM spoils log — " + DateTime.Now + Environment.NewLine
                        + "Columns:  time  ·  category  ·  party  ·  message"
                        + "   (campaign day is shown in the ═══ dividers, not on every line)" + Environment.NewLine);
                    _fileOpened = true;
                    // Drain anything logged before the campaign opened the file (the early traces).
                    if (_pending.Length > 0)
                    {
                        File.AppendAllText(LogFilePath, _pending.ToString());
                        _pending.Length = 0;
                    }
                }
                catch
                {
                    _fileLogFailed = true;
                }
            }
        }

        /// <summary>
        /// A campaign launching -- a new game or a loaded save -- rolls the log over to a fresh
        /// timestamped file so each play session stands in its own log rather than appending to the
        /// one opened when the module first loaded, and records the config the session runs under at
        /// the top so a log can be read back without guessing which settings produced it.
        /// </summary>
        public static void StartCampaignLog()
        {
            lock (_fileLock)
            {
                _launchStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            }
            Reset();
            LogConfig();
        }

        /// <summary>
        /// Dumps every RBM config value the session runs under as one pretty-printed JSON object. The
        /// spoils numbers only make sense against the multipliers that produced them, and those live
        /// in a file the reader of the log may never have opened, so they are copied in here. Written
        /// as a raw block rather than through <see cref="Log"/> so the JSON is not broken up by a
        /// timestamp on every line.
        /// </summary>
        private static void LogConfig()
        {
            if (!IsEnabled)
            {
                return;
            }

            string json = Obj(1,
                Field("configVersion", RC.CONFIG_VERSION),
                Member("modules", Obj(2,
                    Field("rbmAiEnabled", RC.rbmAiEnabled),
                    Field("rbmCombatEnabled", RC.rbmCombatEnabled),
                    Field("rbmCampaignEnabled", RC.rbmCampaignEnabled),
                    Field("rbmTournamentEnabled", RC.rbmTournamentEnabled),
                    Field("developerMode", RC.developerMode))),
                Member("campaign", Obj(2,
                    Field("troopUpgradeCostMultiplier", RC.troopUpgradeCostMultiplier),
                    Field("troopUpgradeSpoilsLootMultiplier", RC.troopUpgradeSpoilsLootMultiplier),
                    Field("troopUpgradeRequireSupplyTown", RC.troopUpgradeRequireSupplyTown),
                    Field("troopUpgradeSupplyRadius", RC.troopUpgradeSupplyRadius),
                    Field("troopLootPiecesPerMan", RC.troopLootPiecesPerMan),
                    Field("troopLootOverlookChancePerTier", RC.troopLootOverlookChancePerTier),
                    Field("troopWageSpoilsFraction", RC.troopWageSpoilsFraction),
                    Field("troopWageTierBase", RC.troopWageTierBase),
                    Field("troopSettlementFoodDays", RC.troopSettlementFoodDays),
                    Field("troopFoodWageFraction", RC.troopFoodWageFraction),
                    Field("troopSettlementFunWageFraction", RC.troopSettlementFunWageFraction),
                    Field("settlementProsperityPerGoldSpent", RC.settlementProsperityPerGoldSpent),
                    Field("troopRaidSpoilsMultiplier", RC.troopRaidSpoilsMultiplier),
                    Field("troopSpoilsWarChestGoldPerTier", RC.troopSpoilsWarChestGoldPerTier),
                    Field("troopLuxuryCooldownDays", RC.troopLuxuryCooldownDays),
                    Field("troopLuxurySpendChance", RC.troopLuxurySpendChance),
                    Field("troopFallenSpoilsCaptureFraction", RC.troopFallenSpoilsCaptureFraction),
                    Field("spoilsLoggingEnabled", RC.spoilsLoggingEnabled),
                    Field("spoilsVerboseLoggingEnabled", RC.spoilsVerboseLoggingEnabled))),
                Member("ai", Obj(2,
                    Field("hitStopEnabled", RC.hitStopEnabled),
                    Field("postureEnabled", RC.postureEnabled),
                    Field("staminaEnabled", RC.staminaEnabled),
                    Field("playerPostureMultiplier", RC.playerPostureMultiplier),
                    Field("postureGUIEnabled", RC.postureGUIEnabled),
                    Field("vanillaCombatAi", RC.vanillaCombatAi),
                    Field("keepBattleEnabled", RC.keepBattleEnabled))),
                Member("combat", Obj(2,
                    Field("armorMultiplier", RC.armorMultiplier),
                    Field("armorThresholdModifier", RC.armorThresholdModifier),
                    Field("maceBluntModifier", RC.maceBluntModifier),
                    Field("bluntTraumaBonus", RC.bluntTraumaBonus),
                    Field("thrustMagnitudeModifier", RC.ThrustMagnitudeModifier),
                    Field("realisticRangedReload", RC.realisticRangedReload),
                    Field("realisticArrowArc", RC.realisticArrowArc),
                    Field("betterArrowVisuals", RC.betterArrowVisuals),
                    Field("passiveShoulderShields", RC.passiveShoulderShields),
                    Field("troopOverhaulActive", RC.troopOverhaulActive),
                    Field("sneakAttackInstaKill", RC.sneakAttackInstaKill),
                    Field("armorStatusUIEnabled", RC.armorStatusUIEnabled),
                    Field("armorPenetrationMessage", RC.armorPenetrationMessage),
                    Member("priceMultipliers", Obj(3,
                        Field("armor", RC.priceMultipliers.ArmorPriceModifier),
                        Field("weapon", RC.priceMultipliers.WeaponPriceModifier),
                        Field("horse", RC.priceMultipliers.HorsePriceModifier),
                        Field("trade", RC.priceMultipliers.TradePriceModifier))),
                    Field("weaponTypeFactorCount", RC.weaponTypesFactors.Count))));

            string block = ("----- RBM config -----" + "\n" + json).Replace("\n", Environment.NewLine);
            Debug.Print("[RBM][Spoils] config:" + Environment.NewLine + block);
            WriteToFile(block);
        }

        // --- Tiny JSON pretty-printer, enough for the flat config object above. ---

        /// <summary>Wraps members in braces, each on its own line indented to <paramref name="level"/>.</summary>
        private static string Obj(int level, params string[] members)
        {
            string pad = new string(' ', level * 2);
            string closePad = new string(' ', (level - 1) * 2);
            return "{\n" + pad + string.Join(",\n" + pad, members) + "\n" + closePad + "}";
        }

        /// <summary>A key whose value is an already-formatted object or array from <see cref="Obj"/>.</summary>
        private static string Member(string key, string rawValue)
        {
            return "\"" + key + "\": " + rawValue;
        }

        private static string Field(string key, bool value)
        {
            return "\"" + key + "\": " + (value ? "true" : "false");
        }

        private static string Field(string key, int value)
        {
            return "\"" + key + "\": " + value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Field(string key, float value)
        {
            return "\"" + key + "\": " + value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Field(string key, string value)
        {
            return "\"" + key + "\": \"" + (value ?? "") + "\"";
        }

        public static void Log(string category, PartyBase party, string message)
        {
            Emit(category, PartyToken(party), message);
        }

        public static void Log(string category, string message)
        {
            Emit(category, "", message);
        }

        /// <summary>A per-stack detail line, written only when verbose logging is on.</summary>
        public static void LogVerbose(string category, PartyBase party, string message)
        {
            if (!Verbose)
            {
                return;
            }
            Emit(category, PartyToken(party), message);
        }

        public static void LogVerbose(string category, string message)
        {
            if (!Verbose)
            {
                return;
            }
            Emit(category, "", message);
        }

        public static void LogOnce(string key, string category, PartyBase party, string message)
        {
            LogOnceInternal(key, category, PartyToken(party), message);
        }

        /// <summary>For lines that would otherwise repeat every frame or every troop refresh.</summary>
        public static void LogOnce(string key, string category, string message)
        {
            LogOnceInternal(key, category, "", message);
        }

        private static void LogOnceInternal(string key, string category, string party, string message)
        {
            if (!IsEnabled)
            {
                return;
            }
            bool isFirst;
            lock (_fileLock)
            {
                isFirst = _oncePerKey.Add(key);
            }
            if (isFirst)
            {
                Emit(category, party, message);
            }
        }

        /// <summary>
        /// The one place a line is composed and written. Prints a day divider when the campaign date
        /// rolls over and a blank line when the kind of event changes, then the line itself as aligned
        /// columns. All of it happens under the file lock so the divider and its lines cannot interleave
        /// with a write racing in from the prefab loading thread.
        /// </summary>
        private static void Emit(string category, string party, string message)
        {
            if (!IsEnabled)
            {
                return;
            }
            string line = FormatLine(category, party, message);
            lock (_fileLock)
            {
                StringBuilder block = new StringBuilder();
                string divider = DayDividerIfChanged();
                if (divider != null)
                {
                    // A dated divider already sets the group apart; it carries its own blank lines.
                    block.Append(Environment.NewLine).Append(divider).Append(Environment.NewLine)
                        .Append(Environment.NewLine);
                }
                else if (_lastCategory != null && _lastCategory != category)
                {
                    block.Append(Environment.NewLine);
                }
                block.Append(line);
                _lastCategory = category;
                WriteToFile(block.ToString());
            }
            Debug.Print("[RBM][Spoils] " + line);
            if (!RBMConfig.RBMConfig.developerMode)
            {
                return;
            }
            try
            {
                // The earliest lines run from OnSubModuleLoad, before the message log exists.
                InformationManager.DisplayMessage(new InformationMessage("[RBM] " + line));
            }
            catch
            {
            }
        }

        /// <summary>
        /// The wall clock, to line a line up against a crash or a screenshot, then the fixed-width
        /// category and party columns so the message text starts at the same place every time. Detail
        /// lines that arrive already indented keep their indent inside the message column.
        /// </summary>
        private static string FormatLine(string category, string party, string message)
        {
            string wallClock = DateTime.Now.ToString("HH:mm:ss");
            string cat = (category ?? "").PadRight(CategoryWidth);
            string pty = string.IsNullOrEmpty(party) ? "    " : party;
            return wallClock + "  " + cat + "  " + pty + "  " + message;
        }

        /// <summary>
        /// Which party a line is about, as a fixed four-character token so it forms a clean column. The
        /// log carries every party in the world and the player usually only wants his own, so an AI
        /// lord's line is tagged rather than dropped and can still be grepped out.
        /// </summary>
        private static string PartyToken(PartyBase party)
        {
            if (Campaign.Current == null || party == null)
            {
                return "----";
            }
            return (party == PartyBase.MainParty) ? "MAIN" : " AI ";
        }

        /// <summary>
        /// A dated divider line the first time a line is written on a new campaign day, or null on the
        /// same day as the last one. Pulls the date out of every line and into one heading per day.
        /// </summary>
        /// <remarks>
        /// CampaignTime.Now reads Campaign.Current, null before a campaign exists (the early UI lines),
        /// and can throw while the time system is still mid-init; both cases simply yield no divider.
        /// </remarks>
        private static string DayDividerIfChanged()
        {
            if (Campaign.Current == null)
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
                // Bannerlord's year is four 21-day seasons; derive the season rather than lean on an
                // API property that may not be present, and clamp so an out-of-range day cannot throw.
                int season = Math.Max(0, Math.Min(3, (dayOfYear - 1) / 21));
                return string.Format("════════ campaign {0}-{1:000}  ·  {2} ════════", year, dayOfYear, SeasonNames[season]);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Shorthand for the party screen and prefab plumbing, which is all one category.</summary>
        public static void Trace(string message)
        {
            Log("UI", message);
        }

        public static void TraceOnce(string key, string message)
        {
            LogOnce(key, "UI", message);
        }

        public static string Describe(PartyBase party)
        {
            if (party == null)
            {
                return "<null party>";
            }
            if (party == PartyBase.MainParty)
            {
                return "MainParty";
            }
            return party.Name != null ? party.Name.ToString() : party.Id;
        }

        public static string Describe(CharacterObject character)
        {
            if (character == null)
            {
                return "<null troop>";
            }
            return character.StringId + " (T" + character.Tier + ")";
        }

        /// <summary>
        /// Prefabs load on a loading thread while the campaign starts on the main one, so these
        /// writes genuinely race. A transient sharing violation must not silently end the log.
        /// </summary>
        private static void WriteToFile(string message)
        {
            if (_fileLogFailed)
            {
                return;
            }
            lock (_fileLock)
            {
                if (!_fileOpened)
                {
                    // No campaign log yet: hold the line until StartCampaignLog opens the file and flushes.
                    _pending.Append(message).Append(Environment.NewLine);
                    return;
                }
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        File.AppendAllText(LogFilePath, message + Environment.NewLine);
                        return;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(2);
                    }
                    catch
                    {
                        _fileLogFailed = true;
                        return;
                    }
                }
            }
        }
    }
}
