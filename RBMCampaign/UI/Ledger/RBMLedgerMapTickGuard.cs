using HarmonyLib;
using SandBox.View;
using TaleWorlds.ScreenSystem;

namespace RBMCampaign
{
    // Crash guard for the RBM Ledger.
    //
    // The ledger is a bare ScreenBase (RBMLedgerScreen), not a GameStateScreen. Pushing it does NOT
    // change the active game state, so MapState stays active and keeps running its map-mode tick every
    // frame behind the ledger -- including SandBoxViewVisualManager.OnTick, which fans out to
    // NavalDLC.View's NavalMobilePartyVisualManager and its TWParallel ship-visual job. With the map
    // no longer rendering, the frame loop runs uncapped (every mouse move pumps more frames), the naval
    // job storms, and NavalDLC trips a native lifetime race -- an AccessViolation deep in
    // NavalMobilePartyVisual.UpdateEntityPosition. Freezing campaign time does NOT help: the gate on
    // that tick is "MapState is the active state", not the campaign clock (dt), and the visual job runs
    // on real frame time regardless.
    //
    // Every vanilla full-screen map overlay (Party, Kingdom, Inventory, Clan, ...) sidesteps this by
    // being a GameStateScreen whose state deactivates MapState. Rather than refactor the ledger into a
    // GameState, we simply skip the map visual tick while the ledger is the top screen. Skipping is
    // purely cosmetic here -- the map is hidden anyway -- and normal ticking resumes the instant the
    // ledger is popped. Inert whenever the ledger is not open (returns true), so it costs nothing in
    // normal play and needs no War Sails / NavalDLC assembly reference.
    [HarmonyPatch(typeof(SandBoxViewVisualManager), "OnTick")]
    internal static class RBMLedgerMapTickGuard
    {
        private static bool Prefix()
        {
            // false = skip the original map visual tick this frame.
            return !(ScreenManager.TopScreen is RBMLedgerScreen);
        }
    }
}
