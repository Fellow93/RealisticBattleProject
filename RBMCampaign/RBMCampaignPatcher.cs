using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;

namespace RBMCampaign
{
    public static class RBMCampaignPatcher
    {
        // Game types whose static initializers read Game.Current.GameTextManager / GameTexts.FindText,
        // both null until a Game has been initialized. Patching a method prepares its declaring type,
        // and under Mono (Linux/Proton) that runs the initializer on the spot -- so patching any of
        // these at module load or the main menu throws a TypeInitializationException out of PatchAll
        // and takes the game down before the menu. The Windows CLR is lazier and only trips on some
        // methods (see MercenaryContractPay), which is why this went unnoticed there. Every patch on
        // these types is therefore held back until a game is live; they are campaign-only patches, so
        // nothing is lost by their absence at the menu.
        private static readonly HashSet<Type> GameTextInitializedTypes = new HashSet<Type>
        {
            typeof(DefaultClanFinanceModel),
            typeof(DefaultSettlementMilitiaModel),
            typeof(CampaignUIHelper),
        };

        public static void DoPatching(ref Harmony rbmcampaignHarmony)
        {
            // Game.Initialize sets up the game texts before any submodule's OnGameStart, so on that
            // pass (this runs on every patch pass) the initializers are safe to run.
            bool gameLive = Game.Current != null;
            if (gameLive)
            {
                // Run them now, deliberately, rather than leave it to whichever patch prepares the type first.
                foreach (Type type in GameTextInitializedTypes)
                {
                    RuntimeHelpers.RunClassConstructor(type.TypeHandle);
                }
            }
            // PatchAll, one class at a time, minus the held-back ones while no game exists.
            foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(RBMCampaignPatcher).Assembly))
            {
                if (!gameLive && TargetsGameTextInitializedType(type))
                {
                    continue;
                }
                rbmcampaignHarmony.CreateClassProcessor(type).Patch();
            }
            RBMTroopSpoilsBarWidget.RegisterWidgetType();
            UpgradeLimitWidgets.RegisterWidgetTypes();
            // Drop any nameplate view-models left subscribed to RBM's map-bubble events by a previous
            // session, so a save reload does not pin the old map's nameplates in memory. See RBMMapNotifications.
            RBMMapNotifications.Reset();
            // The hand-applied DefaultClanFinanceModel patches, deferred for the same reason as above:
            // a no-op until Game.Current is live, so they land on the OnGameStart pass. See MercenaryContractPay.
            MercenaryContractPay.ApplyDeferred(rbmcampaignHarmony);
            ClanFinanceTabLines.ApplyDeferred(rbmcampaignHarmony);
        }

        private static bool TargetsGameTextInitializedType(Type patchClass)
        {
            List<HarmonyMethod> attributes = HarmonyMethodExtensions.GetFromType(patchClass);
            if (attributes == null || attributes.Count == 0)
            {
                return false;
            }
            Type target = HarmonyMethod.Merge(attributes).declaringType;
            return target != null && GameTextInitializedTypes.Contains(target);
        }
    }
}
