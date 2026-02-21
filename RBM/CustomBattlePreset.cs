using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade.CustomBattle;
using TaleWorlds.MountAndBlade.CustomBattle.CustomBattle;
using TaleWorlds.MountAndBlade.CustomBattle.CustomBattle.SelectionItem;
using TaleWorlds.ObjectSystem;

namespace RBM
{
    internal class NamedPreset
    {
        public string Name;
        public int GameTypeIndex, PlayerTypeIndex, PlayerSideIndex;
        public string MapId = "";
        public int SeasonIndex, TimeOfDayIndex, SceneLevelIndex, WallHitpointIndex;
        public bool IsSallyOut;
        public string PlayerFactionId = "", PlayerCharacterId = "";
        public int PlayerArmySize = 100;
        public int[] PlayerComposition = { 25, 25, 25, 25 };
        public string[] PlayerTroopsMeleeInfantry = new string[0];
        public string[] PlayerTroopsRangedInfantry = new string[0];
        public string[] PlayerTroopsMeleeCavalry = new string[0];
        public string[] PlayerTroopsRangedCavalry = new string[0];
        public string EnemyFactionId = "", EnemyCharacterId = "";
        public int EnemyArmySize = 100;
        public int[] EnemyComposition = { 25, 25, 25, 25 };
        public string[] EnemyTroopsMeleeInfantry = new string[0];
        public string[] EnemyTroopsRangedInfantry = new string[0];
        public string[] EnemyTroopsMeleeCavalry = new string[0];
        public string[] EnemyTroopsRangedCavalry = new string[0];
        public string[] AttackerMeleeMachineIds = { "", "", "" };
        public string[] AttackerRangedMachineIds = { "", "", "", "" };
        public string[] DefenderMachineIds = { "", "", "", "" };
    }

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
        public static string[] PlayerTroopsMeleeInfantry = new string[0];
        public static string[] PlayerTroopsRangedInfantry = new string[0];
        public static string[] PlayerTroopsMeleeCavalry = new string[0];
        public static string[] PlayerTroopsRangedCavalry = new string[0];

        // Enemy side
        public static string EnemyFactionId = "";
        public static string EnemyCharacterId = "";
        public static int EnemyArmySize = 100;
        public static int[] EnemyComposition = { 25, 25, 25, 25 };
        public static string[] EnemyTroopsMeleeInfantry = new string[0];
        public static string[] EnemyTroopsRangedInfantry = new string[0];
        public static string[] EnemyTroopsMeleeCavalry = new string[0];
        public static string[] EnemyTroopsRangedCavalry = new string[0];

        // Siege machines (empty string = empty slot)
        public static string[] AttackerMeleeMachineIds = { "", "", "" };
        public static string[] AttackerRangedMachineIds = { "", "", "", "" };
        public static string[] DefenderMachineIds = { "", "", "", "" };

        public static List<NamedPreset> Presets = new List<NamedPreset>();

        private static readonly PropertyInfo _selectedMapProp =
            typeof(MapSelectionGroupVM).GetProperty("SelectedMap");

        private static string SaveFilePath =>
            Path.Combine(RBMConfig.Utilities.GetConfigFolderPath(), "last_used_custom_battle_preset.xml");

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

                string[][] playerTroops = new string[4][];
                ReadSide(root.SelectSingleNode("PlayerSide"),
                    ref PlayerFactionId, ref PlayerCharacterId, ref PlayerArmySize, PlayerComposition,
                    playerTroops);
                PlayerTroopsMeleeInfantry  = playerTroops[0] ?? new string[0];
                PlayerTroopsRangedInfantry = playerTroops[1] ?? new string[0];
                PlayerTroopsMeleeCavalry   = playerTroops[2] ?? new string[0];
                PlayerTroopsRangedCavalry  = playerTroops[3] ?? new string[0];

                string[][] enemyTroops = new string[4][];
                ReadSide(root.SelectSingleNode("EnemySide"),
                    ref EnemyFactionId, ref EnemyCharacterId, ref EnemyArmySize, EnemyComposition,
                    enemyTroops);
                EnemyTroopsMeleeInfantry  = enemyTroops[0] ?? new string[0];
                EnemyTroopsRangedInfantry = enemyTroops[1] ?? new string[0];
                EnemyTroopsMeleeCavalry   = enemyTroops[2] ?? new string[0];
                EnemyTroopsRangedCavalry  = enemyTroops[3] ?? new string[0];

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
                    PlayerFactionId, PlayerCharacterId, PlayerArmySize, PlayerComposition,
                    PlayerTroopsMeleeInfantry, PlayerTroopsRangedInfantry,
                    PlayerTroopsMeleeCavalry, PlayerTroopsRangedCavalry);
                WriteSide(doc, root, "EnemySide",
                    EnemyFactionId, EnemyCharacterId, EnemyArmySize, EnemyComposition,
                    EnemyTroopsMeleeInfantry, EnemyTroopsRangedInfantry,
                    EnemyTroopsMeleeCavalry, EnemyTroopsRangedCavalry);

                WriteMachineIds(doc, root, "AttackerMeleeMachines", AttackerMeleeMachineIds);
                WriteMachineIds(doc, root, "AttackerRangedMachines", AttackerRangedMachineIds);
                WriteMachineIds(doc, root, "DefenderMachines", DefenderMachineIds);

                doc.Save(SaveFilePath);
            }
            catch (Exception) { }
        }

        public static void SavePresetToFile(NamedPreset preset, string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var doc = new XmlDocument();
                var root = doc.CreateElement("CustomBattlePreset");
                root.SetAttribute("Name", preset.Name);
                doc.AppendChild(root);

                Elem(doc, root, "GameTypeIndex", preset.GameTypeIndex.ToString());
                Elem(doc, root, "PlayerTypeIndex", preset.PlayerTypeIndex.ToString());
                Elem(doc, root, "PlayerSideIndex", preset.PlayerSideIndex.ToString());
                Elem(doc, root, "MapId", preset.MapId);
                Elem(doc, root, "SeasonIndex", preset.SeasonIndex.ToString());
                Elem(doc, root, "TimeOfDayIndex", preset.TimeOfDayIndex.ToString());
                Elem(doc, root, "SceneLevelIndex", preset.SceneLevelIndex.ToString());
                Elem(doc, root, "WallHitpointIndex", preset.WallHitpointIndex.ToString());
                Elem(doc, root, "IsSallyOut", preset.IsSallyOut ? "1" : "0");

                WriteSide(doc, root, "PlayerSide",
                    preset.PlayerFactionId, preset.PlayerCharacterId, preset.PlayerArmySize, preset.PlayerComposition,
                    preset.PlayerTroopsMeleeInfantry, preset.PlayerTroopsRangedInfantry,
                    preset.PlayerTroopsMeleeCavalry, preset.PlayerTroopsRangedCavalry);
                WriteSide(doc, root, "EnemySide",
                    preset.EnemyFactionId, preset.EnemyCharacterId, preset.EnemyArmySize, preset.EnemyComposition,
                    preset.EnemyTroopsMeleeInfantry, preset.EnemyTroopsRangedInfantry,
                    preset.EnemyTroopsMeleeCavalry, preset.EnemyTroopsRangedCavalry);

                WriteMachineIds(doc, root, "AttackerMeleeMachines", preset.AttackerMeleeMachineIds);
                WriteMachineIds(doc, root, "AttackerRangedMachines", preset.AttackerRangedMachineIds);
                WriteMachineIds(doc, root, "DefenderMachines", preset.DefenderMachineIds);

                doc.Save(path);
            }
            catch (Exception) { }
        }

        public static NamedPreset LoadPresetFromFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var doc = new XmlDocument();
                doc.Load(path);
                var root = doc.SelectSingleNode("CustomBattlePreset");
                if (root == null) return null;

                var p = new NamedPreset();
                p.Name = root.Attributes?["Name"]?.Value ?? Path.GetFileNameWithoutExtension(path);
                p.GameTypeIndex = ReadInt(root, "GameTypeIndex");
                p.PlayerTypeIndex = ReadInt(root, "PlayerTypeIndex");
                p.PlayerSideIndex = ReadInt(root, "PlayerSideIndex");
                p.MapId = ReadStr(root, "MapId");
                p.SeasonIndex = ReadInt(root, "SeasonIndex");
                p.TimeOfDayIndex = ReadInt(root, "TimeOfDayIndex");
                p.SceneLevelIndex = ReadInt(root, "SceneLevelIndex");
                p.WallHitpointIndex = ReadInt(root, "WallHitpointIndex");
                p.IsSallyOut = ReadBool(root, "IsSallyOut");

                string[][] playerTroops = new string[4][];
                ReadSide(root.SelectSingleNode("PlayerSide"),
                    ref p.PlayerFactionId, ref p.PlayerCharacterId, ref p.PlayerArmySize, p.PlayerComposition,
                    playerTroops);
                p.PlayerTroopsMeleeInfantry  = playerTroops[0] ?? new string[0];
                p.PlayerTroopsRangedInfantry = playerTroops[1] ?? new string[0];
                p.PlayerTroopsMeleeCavalry   = playerTroops[2] ?? new string[0];
                p.PlayerTroopsRangedCavalry  = playerTroops[3] ?? new string[0];

                string[][] enemyTroops = new string[4][];
                ReadSide(root.SelectSingleNode("EnemySide"),
                    ref p.EnemyFactionId, ref p.EnemyCharacterId, ref p.EnemyArmySize, p.EnemyComposition,
                    enemyTroops);
                p.EnemyTroopsMeleeInfantry  = enemyTroops[0] ?? new string[0];
                p.EnemyTroopsRangedInfantry = enemyTroops[1] ?? new string[0];
                p.EnemyTroopsMeleeCavalry   = enemyTroops[2] ?? new string[0];
                p.EnemyTroopsRangedCavalry  = enemyTroops[3] ?? new string[0];

                ReadMachineIds(root, "AttackerMeleeMachines", p.AttackerMeleeMachineIds);
                ReadMachineIds(root, "AttackerRangedMachines", p.AttackerRangedMachineIds);
                ReadMachineIds(root, "DefenderMachines", p.DefenderMachineIds);

                return p;
            }
            catch (Exception) { return null; }
        }

        public static NamedPreset CaptureFromVM(CustomBattleVM vm, string name)
        {
            var p = new NamedPreset { Name = name };
            try
            {
                p.GameTypeIndex = vm.GameTypeSelectionGroup.GameTypeSelection.SelectedIndex;
                p.PlayerTypeIndex = vm.GameTypeSelectionGroup.PlayerTypeSelection.SelectedIndex;
                p.PlayerSideIndex = vm.GameTypeSelectionGroup.PlayerSideSelection.SelectedIndex;

                if (vm.MapSelectionGroup.SelectedMap is MapItemVM mapItem)
                    p.MapId = mapItem.MapId;
                p.SeasonIndex = vm.MapSelectionGroup.SeasonSelection.SelectedIndex;
                p.TimeOfDayIndex = vm.MapSelectionGroup.TimeOfDaySelection.SelectedIndex;
                p.SceneLevelIndex = vm.MapSelectionGroup.SceneLevelSelection.SelectedIndex;
                p.WallHitpointIndex = vm.MapSelectionGroup.WallHitpointSelection.SelectedIndex;
                p.IsSallyOut = vm.MapSelectionGroup.IsSallyOutSelected;

                string[][] playerTroops = new string[4][];
                ReadSideFromVM(vm.PlayerSide,
                    ref p.PlayerFactionId, ref p.PlayerCharacterId, ref p.PlayerArmySize, p.PlayerComposition,
                    playerTroops);
                p.PlayerTroopsMeleeInfantry  = playerTroops[0] ?? new string[0];
                p.PlayerTroopsRangedInfantry = playerTroops[1] ?? new string[0];
                p.PlayerTroopsMeleeCavalry   = playerTroops[2] ?? new string[0];
                p.PlayerTroopsRangedCavalry  = playerTroops[3] ?? new string[0];

                string[][] enemyTroops = new string[4][];
                ReadSideFromVM(vm.EnemySide,
                    ref p.EnemyFactionId, ref p.EnemyCharacterId, ref p.EnemyArmySize, p.EnemyComposition,
                    enemyTroops);
                p.EnemyTroopsMeleeInfantry  = enemyTroops[0] ?? new string[0];
                p.EnemyTroopsRangedInfantry = enemyTroops[1] ?? new string[0];
                p.EnemyTroopsMeleeCavalry   = enemyTroops[2] ?? new string[0];
                p.EnemyTroopsRangedCavalry  = enemyTroops[3] ?? new string[0];

                ReadMachinesFromVM(vm.AttackerMeleeMachines, p.AttackerMeleeMachineIds);
                ReadMachinesFromVM(vm.AttackerRangedMachines, p.AttackerRangedMachineIds);
                ReadMachinesFromVM(vm.DefenderMachines, p.DefenderMachineIds);
            }
            catch (Exception) { }
            return p;
        }

        public static void ApplyNamedToVM(NamedPreset preset, CustomBattleVM vm)
        {
            try
            {
                if (string.IsNullOrEmpty(preset.MapId)) return;

                if (vm.GameTypeSelectionGroup.GameTypeSelection.SelectedIndex != preset.GameTypeIndex)
                    vm.GameTypeSelectionGroup.GameTypeSelection.SelectedIndex = preset.GameTypeIndex;
                vm.GameTypeSelectionGroup.PlayerTypeSelection.SelectedIndex = preset.PlayerTypeIndex;
                vm.GameTypeSelectionGroup.PlayerSideSelection.SelectedIndex = preset.PlayerSideIndex;

                var mapSel = vm.MapSelectionGroup.MapSelection;
                int mapIdx = -1;
                for (int i = 0; i < mapSel.ItemList.Count; i++)
                {
                    if (mapSel.ItemList[i].MapId == preset.MapId) { mapIdx = i; break; }
                }
                if (mapIdx >= 0)
                {
                    mapSel.SelectedIndex = mapIdx;
                    _selectedMapProp?.SetValue(vm.MapSelectionGroup, mapSel.ItemList[mapIdx],
                        BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                }

                vm.MapSelectionGroup.SeasonSelection.SelectedIndex = preset.SeasonIndex;
                vm.MapSelectionGroup.TimeOfDaySelection.SelectedIndex = preset.TimeOfDayIndex;
                vm.MapSelectionGroup.SceneLevelSelection.SelectedIndex = preset.SceneLevelIndex;
                vm.MapSelectionGroup.WallHitpointSelection.SelectedIndex = preset.WallHitpointIndex;

                if (preset.IsSallyOut != vm.MapSelectionGroup.IsSallyOutSelected)
                    vm.MapSelectionGroup.ExecuteSallyOutChange();

                ApplySide(vm.PlayerSide,
                    preset.PlayerFactionId, preset.PlayerCharacterId, preset.PlayerArmySize, preset.PlayerComposition,
                    preset.PlayerTroopsMeleeInfantry, preset.PlayerTroopsRangedInfantry,
                    preset.PlayerTroopsMeleeCavalry, preset.PlayerTroopsRangedCavalry);
                ApplySide(vm.EnemySide,
                    preset.EnemyFactionId, preset.EnemyCharacterId, preset.EnemyArmySize, preset.EnemyComposition,
                    preset.EnemyTroopsMeleeInfantry, preset.EnemyTroopsRangedInfantry,
                    preset.EnemyTroopsMeleeCavalry, preset.EnemyTroopsRangedCavalry);

                ApplyMachines(vm.AttackerMeleeMachines, preset.AttackerMeleeMachineIds);
                ApplyMachines(vm.AttackerRangedMachines, preset.AttackerRangedMachineIds);
                ApplyMachines(vm.DefenderMachines, preset.DefenderMachineIds);
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

                string[][] playerTroops = new string[4][];
                ReadSideFromVM(vm.PlayerSide,
                    ref PlayerFactionId, ref PlayerCharacterId, ref PlayerArmySize, PlayerComposition,
                    playerTroops);
                PlayerTroopsMeleeInfantry  = playerTroops[0] ?? new string[0];
                PlayerTroopsRangedInfantry = playerTroops[1] ?? new string[0];
                PlayerTroopsMeleeCavalry   = playerTroops[2] ?? new string[0];
                PlayerTroopsRangedCavalry  = playerTroops[3] ?? new string[0];

                string[][] enemyTroops = new string[4][];
                ReadSideFromVM(vm.EnemySide,
                    ref EnemyFactionId, ref EnemyCharacterId, ref EnemyArmySize, EnemyComposition,
                    enemyTroops);
                EnemyTroopsMeleeInfantry  = enemyTroops[0] ?? new string[0];
                EnemyTroopsRangedInfantry = enemyTroops[1] ?? new string[0];
                EnemyTroopsMeleeCavalry   = enemyTroops[2] ?? new string[0];
                EnemyTroopsRangedCavalry  = enemyTroops[3] ?? new string[0];

                ReadMachinesFromVM(vm.AttackerMeleeMachines, AttackerMeleeMachineIds);
                ReadMachinesFromVM(vm.AttackerRangedMachines, AttackerRangedMachineIds);
                ReadMachinesFromVM(vm.DefenderMachines, DefenderMachineIds);
            }
            catch (Exception) { }
        }

        private static void ReadSideFromVM(CustomBattleSideVM side,
            ref string factionId, ref string charId, ref int armySize, int[] comp,
            string[][] troops)
        {
            factionId = side.FactionSelectionGroup.SelectedItem?.Faction?.StringId ?? "";
            charId = side.SelectedCharacter?.StringId ?? "";
            armySize = side.CompositionGroup.ArmySize;
            comp[0] = side.CompositionGroup.MeleeInfantryComposition.CompositionValue;
            comp[1] = side.CompositionGroup.RangedInfantryComposition.CompositionValue;
            comp[2] = side.CompositionGroup.MeleeCavalryComposition.CompositionValue;
            comp[3] = side.CompositionGroup.RangedCavalryComposition.CompositionValue;
            troops[0] = ReadSelectedTroopIds(side.CompositionGroup.MeleeInfantryComposition.TroopTypes);
            troops[1] = ReadSelectedTroopIds(side.CompositionGroup.RangedInfantryComposition.TroopTypes);
            troops[2] = ReadSelectedTroopIds(side.CompositionGroup.MeleeCavalryComposition.TroopTypes);
            troops[3] = ReadSelectedTroopIds(side.CompositionGroup.RangedCavalryComposition.TroopTypes);
        }

        private static string[] ReadSelectedTroopIds(MBBindingList<CustomBattleTroopTypeVM> troopTypes)
        {
            var ids = new List<string>();
            foreach (var t in troopTypes)
                if (t.IsSelected && !string.IsNullOrEmpty(t.Character?.StringId))
                    ids.Add(t.Character.StringId);
            return ids.ToArray();
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
                    PlayerFactionId, PlayerCharacterId, PlayerArmySize, PlayerComposition,
                    PlayerTroopsMeleeInfantry, PlayerTroopsRangedInfantry,
                    PlayerTroopsMeleeCavalry, PlayerTroopsRangedCavalry);
                ApplySide(vm.EnemySide,
                    EnemyFactionId, EnemyCharacterId, EnemyArmySize, EnemyComposition,
                    EnemyTroopsMeleeInfantry, EnemyTroopsRangedInfantry,
                    EnemyTroopsMeleeCavalry, EnemyTroopsRangedCavalry);

                // 11. Siege machines
                ApplyMachines(vm.AttackerMeleeMachines, AttackerMeleeMachineIds);
                ApplyMachines(vm.AttackerRangedMachines, AttackerRangedMachineIds);
                ApplyMachines(vm.DefenderMachines, DefenderMachineIds);
            }
            catch (Exception) { }
        }

        private static void ApplySide(CustomBattleSideVM side,
            string factionId, string charId, int armySize, int[] comp,
            string[] troopsMI, string[] troopsRI, string[] troopsMC, string[] troopsRC)
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

            // Troop type selections — faction change populates TroopTypes, so apply after faction
            ApplyTroopSelection(side.CompositionGroup.MeleeInfantryComposition.TroopTypes, troopsMI);
            ApplyTroopSelection(side.CompositionGroup.RangedInfantryComposition.TroopTypes, troopsRI);
            ApplyTroopSelection(side.CompositionGroup.MeleeCavalryComposition.TroopTypes, troopsMC);
            ApplyTroopSelection(side.CompositionGroup.RangedCavalryComposition.TroopTypes, troopsRC);
        }

        private static void ApplyTroopSelection(MBBindingList<CustomBattleTroopTypeVM> troopTypes, string[] ids)
        {
            if (ids == null || ids.Length == 0 || troopTypes.Count == 0) return;

            bool anyMatched = false;
            foreach (var t in troopTypes)
            {
                string tId = t.Character?.StringId ?? "";
                bool shouldSelect = false;
                foreach (var id in ids)
                    if (id == tId) { shouldSelect = true; break; }
                t.IsSelected = shouldSelect;
                if (shouldSelect) anyMatched = true;
            }

            // If nothing matched (e.g. faction changed since last save), leave at least one selected
            if (!anyMatched)
                troopTypes[0].IsSelected = true;
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
            ref string faction, ref string character, ref int army, int[] comp,
            string[][] troops)
        {
            if (n == null) return;
            faction = ReadStr(n, "FactionId");
            character = ReadStr(n, "CharacterId");
            army = ReadInt(n, "ArmySize", 100);
            comp[0] = ReadInt(n, "CompositionMeleeInfantry", 25);
            comp[1] = ReadInt(n, "CompositionRangedInfantry", 25);
            comp[2] = ReadInt(n, "CompositionMeleeCavalry", 25);
            comp[3] = ReadInt(n, "CompositionRangedCavalry", 25);
            troops[0] = ReadStringList(n, "TroopsMeleeInfantry");
            troops[1] = ReadStringList(n, "TroopsRangedInfantry");
            troops[2] = ReadStringList(n, "TroopsMeleeCavalry");
            troops[3] = ReadStringList(n, "TroopsRangedCavalry");
        }

        private static string[] ReadStringList(XmlNode n, string tag)
        {
            var parent = n?.SelectSingleNode(tag);
            if (parent == null) return new string[0];
            var nodes = parent.SelectNodes("Troop");
            var result = new List<string>();
            foreach (XmlNode node in nodes)
                if (!string.IsNullOrEmpty(node.InnerText))
                    result.Add(node.InnerText);
            return result.ToArray();
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
            string faction, string character, int army, int[] comp,
            string[] troopsMI, string[] troopsRI, string[] troopsMC, string[] troopsRC)
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
            WriteStringList(doc, n, "TroopsMeleeInfantry", troopsMI);
            WriteStringList(doc, n, "TroopsRangedInfantry", troopsRI);
            WriteStringList(doc, n, "TroopsMeleeCavalry", troopsMC);
            WriteStringList(doc, n, "TroopsRangedCavalry", troopsRC);
        }

        private static void WriteStringList(XmlDocument doc, XmlNode parent, string tag, string[] ids)
        {
            var n = doc.CreateElement(tag);
            parent.AppendChild(n);
            if (ids == null) return;
            foreach (var id in ids)
                if (!string.IsNullOrEmpty(id))
                    Elem(doc, n, "Troop", id);
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
