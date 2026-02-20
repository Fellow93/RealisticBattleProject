using System;
using System.IO;
using System.Reflection;
using System.Xml;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using TaleWorlds.MountAndBlade.CustomBattle;
using TaleWorlds.MountAndBlade.CustomBattle.CustomBattle;
using TaleWorlds.MountAndBlade.CustomBattle.CustomBattle.SelectionItem;

namespace RBM
{
    internal static class CustomBattlePreset
    {
        // Game type
        public static int GameTypeIndex;
        public static int PlayerTypeIndex;
        public static int PlayerSideIndex;

        // Map
        public static string MapId = "";
        public static int SeasonIndex;
        public static int TimeOfDayIndex;
        public static int SceneLevelIndex;
        public static int WallHitpointIndex;
        public static bool IsSallyOut;

        // Player side
        public static string PlayerFactionId = "";
        public static string PlayerCharacterId = "";
        public static int PlayerArmySize = 100;
        public static int[] PlayerComposition = { 25, 25, 25, 25 };

        // Enemy side
        public static string EnemyFactionId = "";
        public static string EnemyCharacterId = "";
        public static int EnemyArmySize = 100;
        public static int[] EnemyComposition = { 25, 25, 25, 25 };

        // Siege machines (empty string = empty slot)
        public static string[] AttackerMeleeMachineIds = { "", "", "" };
        public static string[] AttackerRangedMachineIds = { "", "", "", "" };
        public static string[] DefenderMachineIds = { "", "", "", "" };

        private static readonly PropertyInfo _selectedMapProp =
            typeof(MapSelectionGroupVM).GetProperty("SelectedMap");

        private static string SaveFilePath =>
            Path.Combine(RBMConfig.Utilities.GetConfigFolderPath(), "custom_battle_preset.xml");

        // ------------------------------------------------------------------ //
        //  Load / Save                                                         //
        // ------------------------------------------------------------------ //

        public static void LoadPreset()
        {
            try
            {
                if (!File.Exists(SaveFilePath)) return;

                var doc = new XmlDocument();
                doc.Load(SaveFilePath);
                var root = doc.SelectSingleNode("CustomBattlePreset");
                if (root == null) return;

                GameTypeIndex = ReadInt(root, "GameTypeIndex");
                PlayerTypeIndex = ReadInt(root, "PlayerTypeIndex");
                PlayerSideIndex = ReadInt(root, "PlayerSideIndex");
                MapId = ReadStr(root, "MapId");
                SeasonIndex = ReadInt(root, "SeasonIndex");
                TimeOfDayIndex = ReadInt(root, "TimeOfDayIndex");
                SceneLevelIndex = ReadInt(root, "SceneLevelIndex");
                WallHitpointIndex = ReadInt(root, "WallHitpointIndex");
                IsSallyOut = ReadBool(root, "IsSallyOut");

                ReadSide(root.SelectSingleNode("PlayerSide"),
                    ref PlayerFactionId, ref PlayerCharacterId, ref PlayerArmySize, PlayerComposition);
                ReadSide(root.SelectSingleNode("EnemySide"),
                    ref EnemyFactionId, ref EnemyCharacterId, ref EnemyArmySize, EnemyComposition);

                ReadMachineIds(root, "AttackerMeleeMachines", AttackerMeleeMachineIds);
                ReadMachineIds(root, "AttackerRangedMachines", AttackerRangedMachineIds);
                ReadMachineIds(root, "DefenderMachines", DefenderMachineIds);
            }
            catch (Exception) { }
        }

        public static void SavePreset()
        {
            try
            {
                Directory.CreateDirectory(RBMConfig.Utilities.GetConfigFolderPath());

                var doc = new XmlDocument();
                var root = doc.CreateElement("CustomBattlePreset");
                doc.AppendChild(root);

                Elem(doc, root, "GameTypeIndex", GameTypeIndex.ToString());
                Elem(doc, root, "PlayerTypeIndex", PlayerTypeIndex.ToString());
                Elem(doc, root, "PlayerSideIndex", PlayerSideIndex.ToString());
                Elem(doc, root, "MapId", MapId);
                Elem(doc, root, "SeasonIndex", SeasonIndex.ToString());
                Elem(doc, root, "TimeOfDayIndex", TimeOfDayIndex.ToString());
                Elem(doc, root, "SceneLevelIndex", SceneLevelIndex.ToString());
                Elem(doc, root, "WallHitpointIndex", WallHitpointIndex.ToString());
                Elem(doc, root, "IsSallyOut", IsSallyOut ? "1" : "0");

                WriteSide(doc, root, "PlayerSide",
                    PlayerFactionId, PlayerCharacterId, PlayerArmySize, PlayerComposition);
                WriteSide(doc, root, "EnemySide",
                    EnemyFactionId, EnemyCharacterId, EnemyArmySize, EnemyComposition);

                WriteMachineIds(doc, root, "AttackerMeleeMachines", AttackerMeleeMachineIds);
                WriteMachineIds(doc, root, "AttackerRangedMachines", AttackerRangedMachineIds);
                WriteMachineIds(doc, root, "DefenderMachines", DefenderMachineIds);

                doc.Save(SaveFilePath);
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------ //
        //  VM capture                                                          //
        // ------------------------------------------------------------------ //

        public static void SaveFromVM(CustomBattleVM vm)
        {
            try
            {
                GameTypeIndex = vm.GameTypeSelectionGroup.GameTypeSelection.SelectedIndex;
                PlayerTypeIndex = vm.GameTypeSelectionGroup.PlayerTypeSelection.SelectedIndex;
                PlayerSideIndex = vm.GameTypeSelectionGroup.PlayerSideSelection.SelectedIndex;

                if (vm.MapSelectionGroup.SelectedMap is MapItemVM mapItem)
                    MapId = mapItem.MapId;
                SeasonIndex = vm.MapSelectionGroup.SeasonSelection.SelectedIndex;
                TimeOfDayIndex = vm.MapSelectionGroup.TimeOfDaySelection.SelectedIndex;
                SceneLevelIndex = vm.MapSelectionGroup.SceneLevelSelection.SelectedIndex;
                WallHitpointIndex = vm.MapSelectionGroup.WallHitpointSelection.SelectedIndex;
                IsSallyOut = vm.MapSelectionGroup.IsSallyOutSelected;

                ReadSideFromVM(vm.PlayerSide,
                    ref PlayerFactionId, ref PlayerCharacterId, ref PlayerArmySize, PlayerComposition);
                ReadSideFromVM(vm.EnemySide,
                    ref EnemyFactionId, ref EnemyCharacterId, ref EnemyArmySize, EnemyComposition);

                ReadMachinesFromVM(vm.AttackerMeleeMachines, AttackerMeleeMachineIds);
                ReadMachinesFromVM(vm.AttackerRangedMachines, AttackerRangedMachineIds);
                ReadMachinesFromVM(vm.DefenderMachines, DefenderMachineIds);
            }
            catch (Exception) { }
        }

        private static void ReadSideFromVM(CustomBattleSideVM side,
            ref string factionId, ref string charId, ref int armySize, int[] comp)
        {
            factionId = side.FactionSelectionGroup.SelectedItem?.Faction?.StringId ?? "";
            charId = side.SelectedCharacter?.StringId ?? "";
            armySize = side.CompositionGroup.ArmySize;
            comp[0] = side.CompositionGroup.MeleeInfantryComposition.CompositionValue;
            comp[1] = side.CompositionGroup.RangedInfantryComposition.CompositionValue;
            comp[2] = side.CompositionGroup.MeleeCavalryComposition.CompositionValue;
            comp[3] = side.CompositionGroup.RangedCavalryComposition.CompositionValue;
        }

        private static void ReadMachinesFromVM(
            MBBindingList<CustomBattleSiegeMachineVM> machines, string[] ids)
        {
            for (int i = 0; i < ids.Length && i < machines.Count; i++)
                ids[i] = machines[i].SiegeEngineType?.StringId ?? "";
        }

        // ------------------------------------------------------------------ //
        //  VM restore                                                          //
        // ------------------------------------------------------------------ //

        public static void ApplyToVM(CustomBattleVM vm)
        {
            try
            {
                if (string.IsNullOrEmpty(MapId)) return;

                // 1. Game type first — triggers map list rebuild via callback
                if (vm.GameTypeSelectionGroup.GameTypeSelection.SelectedIndex != GameTypeIndex)
                    vm.GameTypeSelectionGroup.GameTypeSelection.SelectedIndex = GameTypeIndex;
                vm.GameTypeSelectionGroup.PlayerTypeSelection.SelectedIndex = PlayerTypeIndex;
                vm.GameTypeSelectionGroup.PlayerSideSelection.SelectedIndex = PlayerSideIndex;

                // 3. Map — find by MapId, set both index and backing property
                var mapSel = vm.MapSelectionGroup.MapSelection;
                int mapIdx = -1;
                for (int i = 0; i < mapSel.ItemList.Count; i++)
                {
                    if (mapSel.ItemList[i].MapId == MapId) { mapIdx = i; break; }
                }
                if (mapIdx >= 0)
                {
                    mapSel.SelectedIndex = mapIdx;
                    _selectedMapProp?.SetValue(vm.MapSelectionGroup, mapSel.ItemList[mapIdx],
                        BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                }

                // 4. Season / time / level / walls
                vm.MapSelectionGroup.SeasonSelection.SelectedIndex = SeasonIndex;
                vm.MapSelectionGroup.TimeOfDaySelection.SelectedIndex = TimeOfDayIndex;
                vm.MapSelectionGroup.SceneLevelSelection.SelectedIndex = SceneLevelIndex;
                vm.MapSelectionGroup.WallHitpointSelection.SelectedIndex = WallHitpointIndex;

                // 5. Sally out
                if (IsSallyOut != vm.MapSelectionGroup.IsSallyOutSelected)
                    vm.MapSelectionGroup.ExecuteSallyOutChange();

                // 6–9. Sides
                ApplySide(vm.PlayerSide,
                    PlayerFactionId, PlayerCharacterId, PlayerArmySize, PlayerComposition);
                ApplySide(vm.EnemySide,
                    EnemyFactionId, EnemyCharacterId, EnemyArmySize, EnemyComposition);

                // 11. Siege machines
                ApplyMachines(vm.AttackerMeleeMachines, AttackerMeleeMachineIds);
                ApplyMachines(vm.AttackerRangedMachines, AttackerRangedMachineIds);
                ApplyMachines(vm.DefenderMachines, DefenderMachineIds);
            }
            catch (Exception) { }
        }

        private static void ApplySide(CustomBattleSideVM side,
            string factionId, string charId, int armySize, int[] comp)
        {
            // Faction
            var factions = side.FactionSelectionGroup.Factions;
            for (int i = 0; i < factions.Count; i++)
            {
                if (factions[i].Faction?.StringId == factionId)
                {
                    side.FactionSelectionGroup.SelectFaction(i);
                    break;
                }
            }

            // Character — must come after faction because faction change rebuilds the character list
            var charList = side.CharacterSelectionGroup.ItemList;
            for (int i = 0; i < charList.Count; i++)
            {
                if (charList[i].Character?.StringId == charId)
                {
                    side.CharacterSelectionGroup.SelectedIndex = i;
                    break;
                }
            }

            // Army size (clamped)
            side.CompositionGroup.ArmySize = Math.Max(side.CompositionGroup.MinArmySize,
                Math.Min(side.CompositionGroup.MaxArmySize, armySize));

            // Composition percentages
            side.CompositionGroup.MeleeInfantryComposition.CompositionValue = comp[0];
            side.CompositionGroup.RangedInfantryComposition.CompositionValue = comp[1];
            side.CompositionGroup.MeleeCavalryComposition.CompositionValue = comp[2];
            side.CompositionGroup.RangedCavalryComposition.CompositionValue = comp[3];
        }

        private static void ApplyMachines(
            MBBindingList<CustomBattleSiegeMachineVM> machines, string[] ids)
        {
            for (int i = 0; i < machines.Count && i < ids.Length; i++)
            {
                string id = ids[i];
                if (string.IsNullOrEmpty(id))
                {
                    machines[i].SetMachineType(null);
                }
                else
                {
                    var type = MBObjectManager.Instance.GetObject<SiegeEngineType>(id);
                    if (type != null)
                        machines[i].SetMachineType(type);
                }
            }
        }

        // ------------------------------------------------------------------ //
        //  XML helpers                                                         //
        // ------------------------------------------------------------------ //

        private static int ReadInt(XmlNode n, string tag, int def = 0)
        {
            var c = n?.SelectSingleNode(tag);
            return c != null && int.TryParse(c.InnerText, out int v) ? v : def;
        }

        private static string ReadStr(XmlNode n, string tag, string def = "")
        {
            return n?.SelectSingleNode(tag)?.InnerText ?? def;
        }

        private static bool ReadBool(XmlNode n, string tag, bool def = false)
        {
            var c = n?.SelectSingleNode(tag);
            if (c == null) return def;
            return c.InnerText == "1" || c.InnerText == "true";
        }

        private static void ReadSide(XmlNode n,
            ref string faction, ref string character, ref int army, int[] comp)
        {
            if (n == null) return;
            faction = ReadStr(n, "FactionId");
            character = ReadStr(n, "CharacterId");
            army = ReadInt(n, "ArmySize", 100);
            comp[0] = ReadInt(n, "CompositionMeleeInfantry", 25);
            comp[1] = ReadInt(n, "CompositionRangedInfantry", 25);
            comp[2] = ReadInt(n, "CompositionMeleeCavalry", 25);
            comp[3] = ReadInt(n, "CompositionRangedCavalry", 25);
        }

        private static void ReadMachineIds(XmlNode root, string tag, string[] ids)
        {
            var n = root?.SelectSingleNode(tag);
            if (n == null) return;
            var nodes = n.SelectNodes("Machine");
            for (int i = 0; i < nodes.Count && i < ids.Length; i++)
                ids[i] = nodes[i].InnerText ?? "";
        }

        private static void Elem(XmlDocument doc, XmlNode parent, string name, string val)
        {
            var e = doc.CreateElement(name);
            e.InnerText = val ?? "";
            parent.AppendChild(e);
        }

        private static void WriteSide(XmlDocument doc, XmlNode root, string tag,
            string faction, string character, int army, int[] comp)
        {
            var n = doc.CreateElement(tag);
            root.AppendChild(n);
            Elem(doc, n, "FactionId", faction);
            Elem(doc, n, "CharacterId", character);
            Elem(doc, n, "ArmySize", army.ToString());
            Elem(doc, n, "CompositionMeleeInfantry", comp[0].ToString());
            Elem(doc, n, "CompositionRangedInfantry", comp[1].ToString());
            Elem(doc, n, "CompositionMeleeCavalry", comp[2].ToString());
            Elem(doc, n, "CompositionRangedCavalry", comp[3].ToString());
        }

        private static void WriteMachineIds(XmlDocument doc, XmlNode root, string tag, string[] ids)
        {
            var n = doc.CreateElement(tag);
            root.AppendChild(n);
            foreach (var id in ids)
                Elem(doc, n, "Machine", id);
        }
    }
}
