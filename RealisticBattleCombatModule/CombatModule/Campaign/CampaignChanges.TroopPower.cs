using HarmonyLib;
using Helpers;
using JetBrains.Annotations;
using StoryMode.GameComponents;
using StoryMode.Missions;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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

            private sealed class NobleEntry
            {
                public readonly CultureObject Culture;

                public readonly bool IsNoble;

                public NobleEntry(CultureObject culture, bool isNoble)
                {
                    Culture = culture;
                    IsNoble = isNoble;
                }
            }

            // Whether a troop sits in his culture's noble (elite) upgrade tree. GetPower and GetBattlePower are asked
            // per spawn, per casualty and per scoreboard update (twice per troop type of the party), and walking the
            // tree allocated a dozen lists each time for an answer that only changes with the troop's culture.
            // Weakly keyed, so a template from an unloaded campaign is not held; thread-safe for the AI's threads.
            private static readonly ConditionalWeakTable<CharacterObject, NobleEntry> _nobleCache =
                new ConditionalWeakTable<CharacterObject, NobleEntry>();

            private static bool IsNobleTroop(CharacterObject troop)
            {
                if (troop == null || troop.Culture == null)
                {
                    return false;
                }
                CultureObject culture = troop.Culture;
                NobleEntry entry;
                if (_nobleCache.TryGetValue(troop, out entry) && entry.Culture == culture)
                {
                    return entry.IsNoble;
                }
                bool isNoble = false;
                CharacterObject EliteBasicTroop = culture.EliteBasicTroop;
                if (troop == EliteBasicTroop)
                {
                    isNoble = true;
                }
                else
                {
                    List<CharacterObject> cultureNobleTroopList = FillTroopListUntilTier(EliteBasicTroop, 10);
                    foreach (CharacterObject co in cultureNobleTroopList)
                    {
                        if (co == troop)
                        {
                            isNoble = true;
                        }
                    }
                }
                // An entry is never rewritten (another thread may be reading it): a character whose culture changed
                // after he was cached -- a hero, rarely -- is simply worked out afresh each time.
                if (entry == null)
                {
                    try
                    {
                        _nobleCache.Add(troop, new NobleEntry(culture, isNoble));
                    }
                    catch (System.ArgumentException)
                    {
                        // Another thread added it first; its answer is the same.
                    }
                }
                return isNoble;
            }

            [HarmonyPrefix]
            [HarmonyPatch("GetPower")]
            private static bool PrefixGetPower(ref CharacterObject __instance, ref float __result)
            {
                //return GetPowerImp(IsHero ? (HeroObject.Level / 4 + 1) : Tier, IsHero, IsMounted);
                int tier = __instance.IsHero ? (__instance.HeroObject.Level / 4 + 1) : __instance.Tier;
                bool isNoble = IsNobleTroop(__instance);
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
                bool isNoble = IsNobleTroop(__instance);
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
