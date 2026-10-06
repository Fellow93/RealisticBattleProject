using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;

namespace RBMCombat
{
    /// <summary>
    /// The battle effect of troop perks (RBMConfig.TroopPerks, listed in ModuleData/rbm_troop_perks.xml): vanilla
    /// answers "does this character have perk X" for a non-hero with a flat false, so these postfixes answer true
    /// for the listed (troop, perk) pairs, and every perk check that goes through CharacterObject.GetPerkValue --
    /// PerkHelper.AddPerkBonusForCharacter / AddEpicPerkBonusForCharacter, the direct checks in the agent stat,
    /// damage and strike models, troop max hit points (Agent sets BaseHealthLimit from MaxHitPoints()) and RBM's own
    /// perk code -- then applies the perk's real vanilla effect. Only while a mission is running, so no campaign-map
    /// model ever sees a troop perk. Gated on rbmCombatEnabled (this module is only patched when it is on) and
    /// troopPerksEnabled.
    ///
    /// Both overloads are patched: v1.5 added GetPerkValue(perk, environment, isPrimaryEffect, out effectValue),
    /// which most of the agent stat model now calls, and which also checks that the effect applies in the agent's
    /// battle environment (land or sea). The postfix mirrors Hero.GetPerkValue's version of that.
    ///
    /// Inlining: GetPerkValue(PerkObject) is a tiny non-virtual method, the kind the JIT may inline into a caller,
    /// which would bypass the detour for that caller. Harmony 2.4's MonoMod core disables inlining of a method it
    /// detours (TryDisableInlining), and RBM applies its patches at module load, before any campaign or mission
    /// code that calls it has been compiled, so this is not expected to bite. PerkHelper.AddPerkBonusForCharacter and
    /// AddEpicPerkBonusForCharacter(WithSkill) are deliberately NOT patched as a fallback: their bonus math
    /// (CalculateContextualPerkData, AddToStat) is private, and a second grant there would double-count whenever this
    /// patch does work. If in-game testing ever shows a listed perk not applying to a troop, inlining into that
    /// specific caller is the first suspect.
    ///
    /// Troop Perk Logging (troopPerkLoggingEnabled, RBM Debug &amp; Logging) shows which listed perks the game really
    /// asks about: while <see cref="TroopPerkLogLogic"/> runs, each grant (and each environment refusal of a listed
    /// pair) is handed to <see cref="TroopPerkLog.Record"/>, and one file per mission lands in logs/troopperks. Off,
    /// the cost here is one bool read on a grant.
    /// </summary>
    public static class TroopPerkEffects
    {
        [HarmonyPatch(typeof(CharacterObject), nameof(CharacterObject.GetPerkValue), new[] { typeof(PerkObject) })]
        private class GetPerkValuePatch
        {
            private static void Postfix(CharacterObject __instance, PerkObject perk, ref bool __result)
            {
                if (__result || !RBMConfig.TroopPerks.HasAny)
                {
                    return;
                }
                if (RBMConfig.TroopPerks.AppliesInMission(__instance, perk))
                {
                    __result = true;
                    if (TroopPerkLog.Recording)
                    {
                        TroopPerkLog.Record(__instance, perk, false, BattleEnvironment.None, false, true);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(CharacterObject), nameof(CharacterObject.GetPerkValue),
            new[] { typeof(PerkObject), typeof(BattleEnvironment), typeof(bool), typeof(float) },
            new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out })]
        private class GetPerkValueWithEffectPatch
        {
            private static void Postfix(CharacterObject __instance, PerkObject perk, BattleEnvironment environment,
                bool isPrimaryEffect, ref float effectValue, ref bool __result)
            {
                if (__result || !RBMConfig.TroopPerks.HasAny)
                {
                    return;
                }
                if (!RBMConfig.TroopPerks.AppliesInMission(__instance, perk))
                {
                    return;
                }
                if (!perk.ApplicableInEnvironment(environment, isPrimaryEffect))
                {
                    // A listed pair asked about where its effect does not apply: logged as such, answered no.
                    if (TroopPerkLog.Recording)
                    {
                        TroopPerkLog.Record(__instance, perk, true, environment, isPrimaryEffect, false);
                    }
                    return;
                }
                effectValue = isPrimaryEffect ? perk.GetPrimaryBonus(environment) : perk.GetSecondaryBonus(environment);
                __result = true;
                if (TroopPerkLog.Recording)
                {
                    TroopPerkLog.Record(__instance, perk, true, environment, isPrimaryEffect, true);
                }
            }
        }
    }
}
