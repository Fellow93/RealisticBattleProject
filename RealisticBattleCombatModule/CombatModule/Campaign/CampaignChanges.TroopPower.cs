using HarmonyLib;
using Helpers;
using JetBrains.Annotations;
using RBMAI;
using StoryMode.GameComponents;
using StoryMode.Missions;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.CampaignSystem.ComponentInterfaces.CombatXpModel;
using static TaleWorlds.CampaignSystem.MapEvents.MapEvent;

namespace RBMCombat
{
    internal partial class CampaignChanges
    {
        public static List<CharacterObject> FillTroopListUntilTier(CharacterObject starterTroop, int tier)
        {
            List<CharacterObject> troops = new List<CharacterObject>();
            if (starterTroop != null)
            {
                List<CharacterObject> lastUpgradeTargets = new List<CharacterObject>();

                troops.Add(starterTroop);

                lastUpgradeTargets.Clear();
                lastUpgradeTargets.Add(starterTroop);

                for (int i = 1; i < tier; i++)
                {
                    List<CharacterObject> newUpgradeTargets = new List<CharacterObject>();
                    foreach (CharacterObject co in lastUpgradeTargets)
                    {
                        if (co != null && co.UpgradeTargets != null)
                        {
                            troops.AddRange(co.UpgradeTargets);
                            newUpgradeTargets.AddRange(co.UpgradeTargets);
                        }
                    }
                    lastUpgradeTargets = newUpgradeTargets;
                }
            }

            return troops;
        }

        [HarmonyPatch(typeof(DefaultVolunteerModel))]
        [HarmonyPatch("GetBasicVolunteer")]
        private static class DefaultVolunteerModelPatch
        {
            private static bool Prefix(Hero sellerHero, ref CharacterObject __result)
            {
                float randomF = MBRandom.RandomFloat;
                if (randomF < 0.15f)
                {
                    __result = sellerHero.Culture.EliteBasicTroop;
                    return false;
                }
                else
                {
                    __result = sellerHero.Culture.BasicTroop;
                    return false;
                }
            }
        }

        [HarmonyPatch(typeof(CharacterObject))]
        private class OverrideCharacterObject
        {
            [HarmonyPrefix]
            [HarmonyPatch("GetPowerImp")]
            private static bool PrefixGetPowerImp(ref float __result, int tier, bool isHero = false, bool isMounted = false)
            {
                bool isNoble = false;
                float origPower = (float)((2 + tier) * (8 + tier)) * 0.02f * (isHero ? 1.5f : (isMounted ? 1.2f : 1f));
                float modifiedTier = (tier - 1) * 3f;
                modifiedTier = MathF.Clamp(modifiedTier, 1f, modifiedTier);
                __result = (float)((2f + modifiedTier) * (8f + modifiedTier)) * 0.02f * (isHero ? 1.5f : 1f) * (isMounted ? 1.5f : 1f) * (isNoble ? 1.5f : 1f);
                return false;
            }

            public static float CustomGetPowerImp(int tier, bool isHero = false, bool isMounted = false, bool isNoble = false)
            {
                return (float)((2f + tier) * (8f + tier)) * 0.02f * (isHero ? 1.5f : 1f) * (isMounted ? 1.5f : 1f) * (isNoble ? 1.5f : 1f);
            }

            [HarmonyPrefix]
            [HarmonyPatch("GetPower")]
            private static bool PrefixGetPower(ref CharacterObject __instance, ref float __result)
            {
                //return GetPowerImp(IsHero ? (HeroObject.Level / 4 + 1) : Tier, IsHero, IsMounted);
                int tier = __instance.IsHero ? (__instance.HeroObject.Level / 4 + 1) : __instance.Tier;
                bool isNoble = false;
                if (__instance != null && __instance.Culture != null)
                {
                    CharacterObject EliteBasicTroop = __instance.Culture.EliteBasicTroop;
                    if (__instance == EliteBasicTroop)
                    {
                        isNoble = true;
                    }
                    else
                    {
                        List<CharacterObject> cultureNobleTroopList = FillTroopListUntilTier(__instance.Culture.EliteBasicTroop, 10);
                        foreach (CharacterObject co in cultureNobleTroopList)
                        {
                            if (co == __instance)
                            {
                                isNoble = true;
                            }
                        }
                    }
                }
                //float origPower = (float)((2 + tier) * (8 + tier)) * 0.02f * (__instance.IsHero ? 1.5f : (__instance.IsMounted ? 1.2f : 1f));
                float modifiedTier = (tier - 1) * 3f;
                modifiedTier = MathF.Clamp(modifiedTier, 1f, modifiedTier);
                __result = (float)((2f + modifiedTier) * (8f + modifiedTier)) * 0.02f * (__instance.IsHero ? 1.5f : 1f) * (__instance.IsMounted ? 1.5f : 1f) * (isNoble ? 1.5f : 1f);
                return false;
            }

            [HarmonyPrefix]
            [HarmonyPatch("GetBattlePower")]
            private static bool PrefixGetBattlePower(ref CharacterObject __instance, ref float __result)
            {
                int tier = __instance.IsHero ? (__instance.HeroObject.Level / 4 + 1) : __instance.Tier;
                bool isNoble = false;
                if (__instance != null && __instance.Culture != null)
                {
                    CharacterObject EliteBasicTroop = __instance.Culture.EliteBasicTroop;
                    if (__instance == EliteBasicTroop)
                    {
                        isNoble = true;
                    }
                    else
                    {
                        List<CharacterObject> cultureNobleTroopList = FillTroopListUntilTier(__instance.Culture.EliteBasicTroop, 10);
                        foreach (CharacterObject co in cultureNobleTroopList)
                        {
                            if (co == __instance)
                            {
                                isNoble = true;
                            }
                        }
                    }
                }
                float modifiedTier = (tier - 1) * 3f;
                modifiedTier = MathF.Clamp(modifiedTier, 1f, modifiedTier);
                //__result = (float)((2f + modifiedTier) * (8f + modifiedTier)) * 0.02f * (__instance.IsHero ? 1.5f : 1f) * (__instance.IsMounted ? 1.5f : 1f) * (isNoble ? 1.5f : 1f);

                __result = MathF.Max(1f + 0.5f * (__instance.GetPower() - CustomGetPowerImp(0, __instance.IsHero, __instance.IsMounted, isNoble)), 1f);
                return false;
            }
        }

        [HarmonyPatch(typeof(DefaultMilitaryPowerModel))]
        public class OverrideDefaultMilitaryPowerModel
        {
            public static float GetTroopPowerBasedOnContextForXPAttacker(CharacterObject troop, MapEvent.BattleTypes battleType = MapEvent.BattleTypes.None, BattleSideEnum battleSideEnum = BattleSideEnum.None, bool isSimulation = false)
            {
                int tier = (troop.IsHero ? (troop.HeroObject.Level / 4 + 1) : troop.Tier);
                var modifiedTier = tier * 1f;
                if (battleType == BattleTypes.Siege || battleType == BattleTypes.SiegeOutside || battleType == BattleTypes.SallyOut)
                {
                    return (float)((2f + modifiedTier) * (8f + modifiedTier)) * 0.02f * (troop.IsHero ? 1.25f : 1f);
                }
                return (float)((2f + modifiedTier) * (8f + modifiedTier)) * 0.02f * 1f;
            }

            public static float GetTroopPowerBasedOnContextForXPVictim(CharacterObject troop, MapEvent.BattleTypes battleType = MapEvent.BattleTypes.None, BattleSideEnum battleSideEnum = BattleSideEnum.None, bool isSimulation = false)
            {
                int tier = (troop.IsHero ? (troop.HeroObject.Level / 4 + 1) : troop.Tier);
                var modifiedTier = tier * 1f;
                if (battleType == BattleTypes.Siege || battleType == BattleTypes.SiegeOutside || battleType == BattleTypes.SallyOut)
                {
                    return (float)((2f + modifiedTier) * (8f + modifiedTier)) * 0.02f * (troop.IsHero ? 1.5f : 1f);
                }
                return (float)((2f + modifiedTier) * (8f + modifiedTier)) * 0.02f * (troop.IsHero ? 1.5f : (troop.IsMounted ? 1.5f : 1f));
            }
        }
    }
}
