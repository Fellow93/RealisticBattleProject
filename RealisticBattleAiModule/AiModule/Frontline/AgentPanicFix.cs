using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.MountAndBlade.Formation;
using static TaleWorlds.MountAndBlade.MovementOrder;

namespace RBMAI
{
    public class AgentPanicFix : MissionLogic
    {
        public override void OnAgentPanicked(Agent affectedAgent)
        {
            affectedAgent.ClearTargetFrame();
        }

        public override void OnAgentControllerSetToPlayer(Agent agent)
        {
            agent.ClearTargetFrame();
        }
    }
}
