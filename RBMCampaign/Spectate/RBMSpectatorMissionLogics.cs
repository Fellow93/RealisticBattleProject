using System;
using System.Reflection;
using SandBox.Missions.MissionLogics;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace RBMCampaign
{
    /// <summary>
    /// The spawn handler, told which battle it is spawning.
    ///
    /// SandBoxMissionSpawnHandler finds its battle by asking for MapEvent.PlayerMapEvent, and in a spectated battle
    /// there is no such thing -- the player is not a party to the fight and never will be. Left alone it would set
    /// _mapEvent to null and then walk into it on AfterStart, asking a null event how many men it had involved.
    ///
    /// So: let the base do its work (it also finds the spawn logic for us), then hand it the event it could not find
    /// for itself. Everything downstream of that field -- the headcounts, the horses, the wave settings -- is the
    /// base class's own AfterStart, unchanged and inherited.
    /// </summary>
    internal sealed class RBMSpectatorSpawnHandler : SandBoxBattleMissionSpawnHandler
    {
        private readonly MapEvent _watchedEvent;

        public RBMSpectatorSpawnHandler(MapEvent watchedEvent)
        {
            _watchedEvent = watchedEvent;
        }

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            _mapEvent = _watchedEvent;
        }
    }

    /// <summary>
    /// Generals and captains, minus the one step that assumes a player is standing on the field.
    ///
    /// The class itself is load-bearing and must NOT be dropped: it is what sets Team.GeneralAgent and
    /// Formation.Captain, and RBM's own AgentStatCalculateModel reads those to spread a captain's perk aura over his
    /// men. Without it every troop in the watched battle would quietly fight with different stats than the same
    /// troop in a battle the player fights -- which would defeat the whole point of watching.
    ///
    /// But its OnDeploymentFinished reaches for Mission.InitialPlayerAgent and calls a method on it, guarded only by
    /// "the general is not the player". In a spectated battle the player agent is null, and null is not the general,
    /// so the guard opens and the deref lands. Everything that hook does is player housekeeping -- putting the player
    /// in the general's formation and letting him command it remotely -- and there is no player to house. The real
    /// work (OnTeamDeployed above) has already run for both teams.
    /// </summary>
    internal sealed class RBMSpectatorGeneralsAndCaptainsAssignmentLogic : SandboxGeneralsAndCaptainsAssignmentLogic
    {
        public RBMSpectatorGeneralsAndCaptainsAssignmentLogic(TextObject attackerGeneralName, TextObject defenderGeneralName)
            : base(attackerGeneralName, defenderGeneralName)
        {
        }

        public override void OnDeploymentFinished()
        {
        }
    }

    /// <summary>
    /// Says "the deployment is over" out loud, because in this mission nobody else will.
    ///
    /// A normal battle has a DeploymentMissionController, and that is what calls Mission.OnDeploymentFinished when
    /// the player presses Begin. We cannot have one -- DeploymentMissionController dereferences
    /// Mission.InitialPlayerAgent unconditionally, and there is no player agent here -- so the call would simply
    /// never happen, and three separate things downstream of it would never happen either:
    ///
    ///   RBM's AgentStatCalculateModel.InitializeAgentStatsAfterDeploymentFinished, which the mission arms off this
    ///   signal and which every reinforcement arriving later depends on for its stats.
    ///
    ///   TeamAIGeneral.OnDeploymentFinished, which is where each formation's AI is initialised. Without it the
    ///   formations have no tactical brain and the battle is two crowds standing in a field.
    ///
    ///   RTSCamera's own OnDeploymentFinished, which is where it settles what its camera is looking at.
    ///
    /// The troops themselves spawn regardless -- the spawn logic has a no-deployment-controller path -- so the
    /// signal is all that is missing. Wait for both sides to be down (IsInitialSpawnOver, which only goes true after
    /// each side's deployment-over fired OnTeamDeployed) and then fire it by hand, once.
    ///
    /// The RTSCamera handoff rides on this same tick, and the ORDER matters -- see SetCommandMode below.
    /// </summary>
    internal sealed class RBMSpectatorDeploymentFinisher : MissionLogic
    {
        private DefaultBattleMissionAgentSpawnLogic _spawn;
        private bool _done;

        private static FieldInfo _commandModeField;
        private static bool _commandModeLookedUp;

        private bool _commandModeChanged;
        private bool _previousCommandMode;

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            _spawn = Mission.GetMissionBehavior<DefaultBattleMissionAgentSpawnLogic>();
        }

        public override void OnMissionTick(float dt)
        {
            if (_done || _spawn == null || !_spawn.IsInitialSpawnOver)
            {
                return;
            }
            _done = true;

            // LATE, and not a moment earlier. RTSCamera's free-camera logic reads this flag twice, and wants opposite
            // answers each time.
            //
            // On team-deployed it asks "is this a commanded battle with no player agent?" -- and if so it possesses a
            // troop to stand in for the player. We want NO proxy: a possessed trooper is a real man pulled out of the
            // line, with the player's stats and none of the AI's orders. So the flag stays false through deployment
            // and that branch never opens: nobody is possessed, InitialPlayerAgent stays null, and the battle is
            // genuinely agentless.
            //
            // On deployment-finished it asks the reverse: with the flag still false it would try to snap the camera
            // onto the player's agent -- the one that does not exist. So it must be true by then.
            //
            // Between the two moments is exactly here.
            SetCommandMode(true);

            Mission.Current.OnDeploymentFinished();
        }

        public override void OnRemoveBehavior()
        {
            if (_commandModeChanged)
            {
                WriteCommandMode(_previousCommandMode);
                _commandModeChanged = false;
            }
            // The watched battle ends here. RBMSpectatorMission.IsSpectating would shut on its own anyway -- it reads
            // the live mission, and the next one will not have this behaviour in it -- but leaving a finished battle
            // named as the watched one is untidy and keeps a MapEvent alive for no reason.
            RBMSpectatorMission.EndWatching();
            base.OnRemoveBehavior();
        }

        private void SetCommandMode(bool value)
        {
            FieldInfo field = CommandModeField();
            if (field == null)
            {
                return;
            }
            try
            {
                _previousCommandMode = (bool)field.GetValue(null);
                field.SetValue(null, value);
                _commandModeChanged = true;
            }
            catch (Exception)
            {
                _commandModeChanged = false;
            }
        }

        private static void WriteCommandMode(bool value)
        {
            FieldInfo field = CommandModeField();
            if (field == null)
            {
                return;
            }
            try
            {
                field.SetValue(null, value);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// RTSCamera's CommandMode flag, found by reflection and never by reference.
        ///
        /// RBM must load and run on a machine that has never heard of RTSCamera, so it cannot carry a compile-time
        /// dependency on RTSCamera.dll -- one missing assembly and every RBM type that touches it fails to load.
        /// Reflection costs one lookup, cached, and degrades to doing nothing at all.
        /// </summary>
        private static FieldInfo CommandModeField()
        {
            if (_commandModeLookedUp)
            {
                return _commandModeField;
            }
            _commandModeLookedUp = true;

            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type type = assembly.GetType("RTSCamera.CampaignGame.Behavior.CommandBattleBehavior", false);
                    if (type != null)
                    {
                        _commandModeField = type.GetField("CommandMode", BindingFlags.Public | BindingFlags.Static);
                        break;
                    }
                }
            }
            catch (Exception)
            {
                _commandModeField = null;
            }
            return _commandModeField;
        }
    }
}
