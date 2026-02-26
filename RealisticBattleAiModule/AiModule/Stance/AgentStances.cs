using System.Collections.Generic;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    public static class AgentStances
    {
        public static Dictionary<Agent, Stance> values = new Dictionary<Agent, Stance> { };
        public static StanceVisualLogic postureVisual = null;
    }
}