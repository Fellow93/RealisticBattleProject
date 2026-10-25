using System.Globalization;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// Is an enemy formation worth reacting to (anti-bait): at least max(MinMen, MenShare x reactor men) men, or
    /// FormationPower at least PowerShare x the reactor's. AI-thread safe: cached counts/power only, no engine query.
    /// </summary>
    internal static class ThreatSignificance
    {
        internal const int MinMen = 10;

        internal const float MenShare = 0.2f;

        internal const float PowerShare = 0.25f;

        internal static bool IsSignificant(Formation reactor, Formation enemy)
        {
            if (enemy == null)
            {
                return false;
            }
            int men = enemy.CountOfUnits;
            if (men <= 0)
            {
                return false;
            }
            int ours = (reactor != null) ? reactor.CountOfUnits : 0;
            if (ours <= 0)
            {
                return men >= MinMen;
            }
            if (men >= System.Math.Max(MinMen, (int)System.Math.Ceiling(MenShare * ours)))
            {
                return true;
            }
            float ourPower = reactor.QuerySystem.FormationPower;
            return ourPower > 0f && enemy.QuerySystem.FormationPower >= PowerShare * ourPower;
        }

        internal static string Describe(Formation reactor, Formation enemy)
        {
            if (enemy == null)
            {
                return "-";
            }
            string text = TerrainCavalry.FormationName(enemy) + " men=" + enemy.CountOfUnits.ToString(CultureInfo.InvariantCulture)
                + " power=" + enemy.QuerySystem.FormationPower.ToString("0", CultureInfo.InvariantCulture);
            if (reactor == null)
            {
                return text;
            }
            return text + " vs men=" + reactor.CountOfUnits.ToString(CultureInfo.InvariantCulture)
                + " power=" + reactor.QuerySystem.FormationPower.ToString("0", CultureInfo.InvariantCulture);
        }
    }
}
