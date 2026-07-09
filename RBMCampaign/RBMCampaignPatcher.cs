using HarmonyLib;

namespace RBMCampaign
{
    public static class RBMCampaignPatcher
    {
        public static void DoPatching(ref Harmony rbmcampaignHarmony)
        {
            rbmcampaignHarmony.PatchAll();
        }
    }
}
