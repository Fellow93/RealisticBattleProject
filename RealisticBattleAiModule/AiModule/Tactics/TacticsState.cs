using HarmonyLib;
using SandBox.Missions.MissionLogics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.GauntletUI.Mission.Singleplayer;
using TaleWorlds.MountAndBlade.ViewModelCollection.HUD.FormationMarker;
using static TaleWorlds.Core.ItemObject;

namespace RBMAI
{
    public static partial class Tactics
    {
        public class AgentDamageDone
        {
            public float damageDone = 0f;
            public FormationClass initialClass = FormationClass.Unset;
            public bool isAttacker = false;
        }

        public static ConcurrentDictionary<Agent, AgentDamageDone> agentDamage = new ConcurrentDictionary<Agent, AgentDamageDone>();

        private static bool? _missionLibraryLoaded;

        /// <summary>
        /// True when the "MissionLibrary" shared assembly is loaded. It is shipped by RTSCamera, RTSCamera.CommandSystem
        /// AND BattleMiniMap (all by the same author) and carries the code that reads the formation arrangement grid
        /// (LineFormation._units2D) by reflection and relies on its invariant -- that _units2D[i,j].FormationFileIndex
        /// == i and .FormationRankIndex == j for every unit (see CheckFormationArrangementIntegrity). Any RBM mid-battle
        /// formation reassignment transiently breaks that invariant; those mods then dereference a stale grid slot ->
        /// native TickMission faults with an AccessViolationException (looks like a freeze). Keying on the shared
        /// MissionLibrary (rather than each mod's name) covers all three at once -- confirmed both RTSCamera and
        /// BattleMiniMap reproduce the freeze with RBM.
        /// When present, RBM's split tactics do their membership move ONCE then hold stable (see the
        /// _membershipSplitDone / doReassign latch), and the ManageFormationCounts prefix skips its class-reshuffle so
        /// native keeps the grid consistent.
        /// Detected via the loaded assemblies (not the module list) and cached: the answer cannot change mid-run.
        /// </summary>
        internal static bool IsFormationReshufflingUnsafe
        {
            get
            {
                if (_missionLibraryLoaded == null)
                {
                    try
                    {
                        _missionLibraryLoaded = AppDomain.CurrentDomain.GetAssemblies()
                            .Any(a => a.GetName().Name.Equals("MissionLibrary", StringComparison.OrdinalIgnoreCase));
                    }
                    catch
                    {
                        _missionLibraryLoaded = false;
                    }
                }
                return _missionLibraryLoaded.Value;
            }
        }

        internal static void ClassifyMountedAgent(Agent agent, List<Agent> skirmishers, List<Agent> melee)
        {
            bool isMountedSkirmisher = false;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
            {
                if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
                {
                    if (agent.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Thrown && agent.MountAgent != null)
                    {
                        isMountedSkirmisher = true;
                        break;
                    }
                }
            }
            if (isMountedSkirmisher)
            {
                skirmishers.Add(agent);
            }
            else
            {
                melee.Add(agent);
            }
        }
    }
}
