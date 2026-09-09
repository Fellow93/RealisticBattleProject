using System.Collections.Concurrent;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    public static class AgentStances
    {
        // Written on the main thread (agent spawn/wield/tick) but read from the parallel formation-movement
        // job (Frontline.OverrideFormation), so this must be a concurrent map: a plain Dictionary being
        // resized on the main thread while a worker walks it is a torn-bucket crash.
        public static ConcurrentDictionary<Agent, Stance> values = new ConcurrentDictionary<Agent, Stance>();
        public static StanceVisualLogic postureVisual = null;
    }
}