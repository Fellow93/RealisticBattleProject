using TaleWorlds.CampaignSystem;
using TaleWorlds.InputSystem;
using TaleWorlds.ScreenSystem;

namespace RBMCampaign
{
    // Campaign-map hotkey opener for the RBM Ledger. Polled every frame from
    // RBM.SubModule.OnApplicationTick while Mission.Current == null (i.e. on the map / menus).
    // Ctrl+Shift+K is safe from vanilla map-nav clashes: MapScreen.TickNavigationInput bails
    // whenever Ctrl or Shift is held, so none of its game-keys fire under this chord.
    public static class RBMLedgerHotkey
    {
        public static void CheckHotkey()
        {
            if (Campaign.Current == null)
            {
                return;
            }

            bool ctrl = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
            bool shift = Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift);

            if (ctrl && shift && Input.IsKeyPressed(InputKey.K))
            {
                // Don't stack a second ledger if it's already the top screen.
                if (ScreenManager.TopScreen is RBMLedgerScreen)
                {
                    return;
                }
                ScreenManager.PushScreen(new RBMLedgerScreen());
            }
        }
    }
}
