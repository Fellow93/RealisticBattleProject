using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Test battle presets, kept like RBM's custom battle presets: one XML file per preset in
    /// <c>Documents\Mount and Blade II Bannerlord\Configs\RBM\TestBattlePresets\</c> (so they carry over
    /// between campaigns and can be copied), plus <c>last_used_test_battle.xml</c>, rewritten on every
    /// started test battle. A preset holds the men per troop type, both sides' groups and the player's side.
    /// </summary>
    public static partial class TestBattleCommand
    {
        private const string PresetRootElement = "TestBattlePreset";
        private const string LastUsedPresetFileName = "last_used_test_battle.xml";
        private const string LastUsedPresetName = "Last used";
        private const int MaxPresetNameLength = 60;

        private static string PresetFolderPath
        {
            get { return Path.Combine(RBMConfig.Utilities.GetConfigFolderPath(), "TestBattlePresets"); }
        }

        private static string LastUsedPresetPath
        {
            get { return Path.Combine(PresetFolderPath, LastUsedPresetFileName); }
        }

        /// <summary>Every saved preset (not the last-used file), by name.</summary>
        private static List<SetupChoice> ReadPresets()
        {
            List<SetupChoice> presets = new List<SetupChoice>();
            try
            {
                if (!Directory.Exists(PresetFolderPath))
                {
                    return presets;
                }
                foreach (string path in Directory.GetFiles(PresetFolderPath, "*.xml"))
                {
                    if (string.Equals(Path.GetFileName(path), LastUsedPresetFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    SetupChoice preset = ReadPreset(path);
                    if (preset != null)
                    {
                        presets.Add(preset);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Print("[RBM test battle] reading presets from " + PresetFolderPath + " failed: " + e.Message);
            }
            return presets.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The preset in <paramref name="path"/>, or null if there is none or it can't be read.</summary>
        private static SetupChoice ReadPreset(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }
                XmlDocument document = new XmlDocument();
                document.Load(path);
                XmlNode root = document.SelectSingleNode(PresetRootElement);
                if (root == null)
                {
                    return null;
                }
                SetupChoice choice = new SetupChoice { FilePath = path };
                string name = root.Attributes?["Name"]?.Value;
                choice.Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(path) : name;
                int count;
                if (int.TryParse(ReadPresetValue(root, "CountPerTroop"), out count))
                {
                    choice.CountPerTroop = Math.Max(1, Math.Min(count, MaxCountPerTroop));
                }
                choice.AttackerGroups = ReadPresetValue(root, "AttackerGroups") ?? AllGroup;
                choice.DefenderGroups = ReadPresetValue(root, "DefenderGroups") ?? AllGroup;
                choice.PlayerIsAttacker = !string.Equals(ReadPresetValue(root, "PlayerSide"), "defender",
                    StringComparison.OrdinalIgnoreCase);
                return choice;
            }
            catch (Exception e)
            {
                Debug.Print("[RBM test battle] reading preset " + path + " failed: " + e.Message);
                return null;
            }
        }

        private static string ReadPresetValue(XmlNode root, string element)
        {
            string value = root.SelectSingleNode(element)?.InnerText;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>Saves <paramref name="choice"/> under its name, replacing a preset of the same name.</summary>
        private static bool SavePreset(SetupChoice choice)
        {
            string path = Path.Combine(PresetFolderPath, PresetFileName(choice.Name) + ".xml");
            if (!WritePreset(choice, choice.Name, path))
            {
                return false;
            }
            choice.FilePath = path;
            return true;
        }

        /// <summary>Remembers the options of a test battle that started. Called from StartTestBattle.</summary>
        private static void SaveLastUsedPreset(int countPerTroop, string attackerGroups, string defenderGroups,
            bool playerIsAttacker)
        {
            SetupChoice choice = new SetupChoice
            {
                CountPerTroop = countPerTroop,
                AttackerGroups = attackerGroups,
                DefenderGroups = defenderGroups,
                PlayerIsAttacker = playerIsAttacker,
            };
            WritePreset(choice, LastUsedPresetName, LastUsedPresetPath);
        }

        private static bool WritePreset(SetupChoice choice, string name, string path)
        {
            try
            {
                Directory.CreateDirectory(PresetFolderPath);
                XmlDocument document = new XmlDocument();
                XmlElement root = document.CreateElement(PresetRootElement);
                root.SetAttribute("Name", name);
                document.AppendChild(root);
                AppendPresetValue(document, root, "CountPerTroop", choice.CountPerTroop.ToString());
                AppendPresetValue(document, root, "AttackerGroups", choice.AttackerGroups);
                AppendPresetValue(document, root, "DefenderGroups", choice.DefenderGroups);
                AppendPresetValue(document, root, "PlayerSide", choice.PlayerIsAttacker ? "attacker" : "defender");
                document.Save(path);
                return true;
            }
            catch (Exception e)
            {
                Debug.Print("[RBM test battle] saving preset " + path + " failed: " + e.Message);
                return false;
            }
        }

        private static void AppendPresetValue(XmlDocument document, XmlElement root, string element, string value)
        {
            XmlElement child = document.CreateElement(element);
            child.InnerText = value ?? "";
            root.AppendChild(child);
        }

        private static void DeletePreset(SetupChoice preset)
        {
            try
            {
                if (preset.FilePath != null && File.Exists(preset.FilePath))
                {
                    File.Delete(preset.FilePath);
                }
            }
            catch (Exception e)
            {
                Debug.Print("[RBM test battle] deleting preset " + preset.FilePath + " failed: " + e.Message);
            }
        }

        /// <summary>The preset name as a file name: characters Windows won't take become '_'.</summary>
        private static string PresetFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            return new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        private static Tuple<bool, string> ValidatePresetName(string input)
        {
            string name = input == null ? "" : input.Trim();
            if (name.Length == 0)
            {
                return new Tuple<bool, string>(false, "Enter a name.");
            }
            if (name.Length > MaxPresetNameLength)
            {
                return new Tuple<bool, string>(false, "Use at most " + MaxPresetNameLength + " characters.");
            }
            if (string.Equals(PresetFileName(name) + ".xml", LastUsedPresetFileName, StringComparison.OrdinalIgnoreCase))
            {
                return new Tuple<bool, string>(false, "That name is kept for the last used setup.");
            }
            return new Tuple<bool, string>(true, null);
        }

        private static string SuggestPresetName(SetupChoice choice)
        {
            string name = choice.AttackerGroups.Replace(",", "+") + " vs " + choice.DefenderGroups.Replace(",", "+");
            return name.Length > MaxPresetNameLength ? name.Substring(0, MaxPresetNameLength) : name;
        }

        /// <summary>The options of a setup, one per line, for popup hints and texts.</summary>
        private static string DescribeChoice(SetupChoice choice)
        {
            return "Attacker: " + choice.AttackerGroups.Replace(",", ", ")
                + "\nDefender: " + choice.DefenderGroups.Replace(",", ", ")
                + "\nYou: " + (choice.PlayerIsAttacker ? "attacker" : "defender")
                + "\nMen per troop type: " + choice.CountPerTroop;
        }
    }
}
