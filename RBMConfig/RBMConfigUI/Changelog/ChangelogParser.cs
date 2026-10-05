using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TaleWorlds.Localization;
using TaleWorlds.ModuleManager;

namespace RBMConfig
{
    // One "## vX.Y.Z (changes since ...)" block of CHANGELOG.md.
    internal sealed class ChangelogEntry
    {
        // First word of the header ("v4.5.3"); also the key stored as the last seen version.
        public string Version;

        // Rest of the header ("(changes since v4.5.2)"), may be empty.
        public string Subtitle;

        public readonly List<ChangelogLine> Lines = new List<ChangelogLine>();
    }

    internal enum ChangelogLineKind
    {
        Section,
        Bullet,
        Paragraph
    }

    internal sealed class ChangelogLine
    {
        public ChangelogLineKind Kind;

        // Bullet nesting: 0 = top level, capped at 2.
        public int Depth;

        // Markdown text with the inline markers (**bold**, `code`, links) still in.
        public string Text;
    }

    // Reads the CHANGELOG.md the build copies into the RBM module folder. Only the subset the file actually
    // uses is understood: "## " starts a version, "### " (or deeper) a section, "- "/"* " a bullet (indented
    // = nested), other lines a paragraph or, right after a bullet, its continuation. Everything before the
    // first "## " (the "# Changelog" title) is skipped.
    internal static class ChangelogParser
    {
        public const string FileName = "CHANGELOG.md";

        // A translation can ship its own CHANGELOG.md in its language folder (ModuleData/Languages/<folder>/,
        // next to the language_data.xml whose id is the active game language). The module-root file is the
        // fallback, so nothing changes for languages without one.
        public static string GetFilePath()
        {
            string moduleDir = ModuleHelper.GetModuleFullPath("RBM");
            string englishPath = Path.Combine(moduleDir, FileName);
            string translatedPath = FindTranslatedFile(moduleDir);
            return translatedPath != null && IsUpToDate(translatedPath, englishPath) ? translatedPath : englishPath;
        }

        // A translation is used only while its newest entry is the English file's newest entry. One that lags
        // behind would hide the newer releases, and the unseen badge (which compares the newest version against
        // the last seen one) would stop firing for that language. One that parses to nothing falls back too.
        private static bool IsUpToDate(string translatedPath, string englishPath)
        {
            try
            {
                List<ChangelogEntry> translated = Parse(File.ReadAllText(translatedPath, Encoding.UTF8));
                if (translated.Count == 0)
                {
                    return false;
                }
                if (!File.Exists(englishPath))
                {
                    return true;
                }
                List<ChangelogEntry> english = Parse(File.ReadAllText(englishPath, Encoding.UTF8));
                return english.Count == 0
                    || string.Equals(translated[0].Version, english[0].Version, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception e)
            {
                TaleWorlds.Library.Debug.Print("[RBM] Could not compare the translated changelog: " + e.Message);
                return false;
            }
        }

        private static string FindTranslatedFile(string moduleDir)
        {
            try
            {
                string language = MBTextManager.ActiveTextLanguage;
                string languagesDir = Path.Combine(moduleDir, "ModuleData", "Languages");
                if (string.IsNullOrEmpty(language) || !Directory.Exists(languagesDir))
                {
                    return null;
                }
                string marker = "id=\"" + language + "\"";
                foreach (string dir in Directory.GetDirectories(languagesDir))
                {
                    string candidate = Path.Combine(dir, FileName);
                    string languageData = Path.Combine(dir, "language_data.xml");
                    if (File.Exists(candidate) && File.Exists(languageData)
                        && File.ReadAllText(languageData, Encoding.UTF8).Contains(marker))
                    {
                        return candidate;
                    }
                }
            }
            catch (Exception e)
            {
                TaleWorlds.Library.Debug.Print("[RBM] Could not look for a translated changelog: " + e.Message);
            }
            return null;
        }

        public static List<ChangelogEntry> Parse(string text)
        {
            List<ChangelogEntry> entries = new List<ChangelogEntry>();
            if (string.IsNullOrEmpty(text))
            {
                return entries;
            }

            ChangelogEntry current = null;
            // The last bullet or paragraph, which a following plain line continues (markdown lazy continuation).
            ChangelogLine open = null;
            string[] rows = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string row in rows)
            {
                string trimmed = row.Trim().TrimStart('﻿');
                if (trimmed == "##" || trimmed.StartsWith("## "))
                {
                    open = null;
                    string header = trimmed.Substring(2).Trim();
                    if (header.Length == 0)
                    {
                        current = null;
                        continue;
                    }
                    int space = header.IndexOf(' ');
                    current = new ChangelogEntry
                    {
                        Version = ToPlainText(space < 0 ? header : header.Substring(0, space)),
                        Subtitle = space < 0 ? string.Empty : ToPlainText(header.Substring(space + 1).Trim())
                    };
                    entries.Add(current);
                    continue;
                }
                if (current == null)
                {
                    continue;
                }
                if (trimmed.Length == 0 || IsRule(trimmed))
                {
                    open = null;
                    continue;
                }
                if (trimmed[0] == '#')
                {
                    open = null;
                    string title = trimmed.TrimStart('#').Trim();
                    if (title.Length > 0)
                    {
                        current.Lines.Add(new ChangelogLine { Kind = ChangelogLineKind.Section, Text = title });
                    }
                    continue;
                }
                if (trimmed.Length > 1 && (trimmed[0] == '-' || trimmed[0] == '*' || trimmed[0] == '+') && trimmed[1] == ' ')
                {
                    int indent = 0;
                    while (indent < row.Length && (row[indent] == ' ' || row[indent] == '\t'))
                    {
                        indent += row[indent] == '\t' ? 4 : 1;
                    }
                    open = new ChangelogLine
                    {
                        Kind = ChangelogLineKind.Bullet,
                        Depth = Math.Min(indent / 2, 2),
                        Text = trimmed.Substring(2).Trim()
                    };
                    current.Lines.Add(open);
                    continue;
                }
                if (open != null)
                {
                    open.Text += " " + trimmed;
                    continue;
                }
                open = new ChangelogLine { Kind = ChangelogLineKind.Paragraph, Text = trimmed };
                current.Lines.Add(open);
            }
            return entries;
        }

        // "---", "***" and the like: a markdown horizontal rule, drawn as nothing.
        private static bool IsRule(string trimmed)
        {
            if (trimmed.Length < 3)
            {
                return false;
            }
            foreach (char c in trimmed)
            {
                if (c != '-' && c != '*' && c != '_' && c != ' ')
                {
                    return false;
                }
            }
            return true;
        }

        // Gauntlet rich text: "**x**" becomes a span in the brush's "Bold" style. An unpaired "**" stays as typed.
        public static string ToRichText(string markdown)
        {
            string[] parts = Clean(markdown).Split(new[] { "**" }, StringSplitOptions.None);
            int markers = parts.Length - 1;
            int pairedMarkers = markers - markers % 2;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    if (i > pairedMarkers)
                    {
                        sb.Append("**");
                    }
                    else
                    {
                        sb.Append(i % 2 == 1 ? "<span style=\"Bold\">" : "</span>");
                    }
                }
                sb.Append(parts[i]);
            }
            return sb.ToString();
        }

        public static string ToPlainText(string markdown)
        {
            return Clean(markdown).Replace("**", string.Empty);
        }

        // Strips the markdown the widgets cannot show and keeps the rich-text parser safe: it reads every '<' as
        // the start of a tag and throws on a malformed one, so a literal '<' is swapped for a look-alike.
        private static string Clean(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
            {
                return string.Empty;
            }
            return StripLinks(markdown).Replace("`", string.Empty)
                .Replace("→", "->")
                .Replace('<', '‹');
        }

        // "[text](url)" -> "text". (RBMConfig does not reference System.dll, so no Regex.)
        private static string StripLinks(string text)
        {
            int open = text.IndexOf('[');
            if (open < 0)
            {
                return text;
            }
            StringBuilder sb = new StringBuilder(text.Length);
            int pos = 0;
            while (open >= 0)
            {
                int close = text.IndexOf("](", open + 1, StringComparison.Ordinal);
                int end = close < 0 ? -1 : text.IndexOf(')', close + 2);
                if (end < 0)
                {
                    break;
                }
                sb.Append(text, pos, open - pos);
                sb.Append(text, open + 1, close - open - 1);
                pos = end + 1;
                open = text.IndexOf('[', pos);
            }
            sb.Append(text, pos, text.Length - pos);
            return sb.ToString();
        }
    }
}
