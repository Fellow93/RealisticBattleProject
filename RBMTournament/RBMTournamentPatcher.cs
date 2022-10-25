using HarmonyLib;

namespace RBMTournament
{
    public static class RBMTournamentPatcher
    {
        public static void DoPatching(ref Harmony rbmtournamentHarmony)
        {
            rbmtournamentHarmony.PatchAll();
        }
    }
}
