using HarmonyLib;
using Helpers;
using SandBox.GameComponents;
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
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.CampaignSystem.ComponentInterfaces.CombatXpModel;
using static TaleWorlds.CampaignSystem.MapEvents.MapEvent;

namespace RBMCombat
{
    internal class CampaignChanges
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

        [HarmonyPatch(typeof(DefaultPartyHealingModel))]
        private class OverrideDefaultPartyHealingModel
        {
            [HarmonyPrefix]
            [HarmonyPatch("GetSurvivalChance")]
            private static bool PrefixGetAiWeight(ref float __result, PartyBase party, CharacterObject character, DamageTypes damageType, PartyBase enemyParty = null)
            {
                if ((character.IsHero && CampaignOptions.BattleDeath == CampaignOptions.Difficulty.VeryEasy) || (character.IsPlayerCharacter && CampaignOptions.BattleDeath == CampaignOptions.Difficulty.Easy))
                {
                    __result = 1f;
                    return false;
                }
                ExplainedNumber stat = new ExplainedNumber(1f);
                if (party != null && party.MobileParty != null)
                {
                    MobileParty mobileParty = party.MobileParty;
                    SkillHelper.AddSkillBonusForParty(DefaultSkills.Medicine, DefaultSkillEffects.SurgeonSurvivalBonus, mobileParty, ref stat);
                    if (enemyParty?.MobileParty != null && enemyParty.MobileParty.HasPerk(DefaultPerks.Medicine.DoctorsOath))
                    {
                        SkillHelper.AddSkillBonusForParty(DefaultSkills.Medicine, DefaultSkillEffects.SurgeonSurvivalBonus, enemyParty.MobileParty, ref stat);
                    }
                    stat.Add((float)character.Level * 0.02f);
                    if (!character.IsHero && party.MapEvent != null && character.Tier < 3)
                    {
                        PerkHelper.AddPerkBonusForParty(DefaultPerks.Medicine.PhysicianOfPeople, party.MobileParty, isPrimaryBonus: false, ref stat);
                    }
                    if (character.IsHero)
                    {
                        stat.Add(character.GetTotalArmorSum() * 0.01f);
                        stat.Add(character.Age * -0.01f);
                        stat.AddFactor(50f);
                        //stat.AddFactor(49f);
                        //stat.Add(stat.ResultNumber * 50f - stat.ResultNumber);
                    }
                    ExplainedNumber stat2 = new ExplainedNumber(1f / stat.ResultNumber);
                    if (character.IsHero)
                    {
                        if (party.IsMobile && party.MobileParty.HasPerk(DefaultPerks.Medicine.CheatDeath, checkSecondaryRole: true))
                        {
                            stat2.AddFactor(DefaultPerks.Medicine.CheatDeath.SecondaryBonus, DefaultPerks.Medicine.CheatDeath.Name);
                        }
                        if (character.HeroObject.Clan == Clan.PlayerClan)
                        {
                            float clanMemberDeathChanceMultiplier = Campaign.Current.Models.DifficultyModel.GetClanMemberDeathChanceMultiplier();
                            if (!clanMemberDeathChanceMultiplier.ApproximatelyEqualsTo(0f))
                            {
                                stat2.AddFactor(clanMemberDeathChanceMultiplier, GameTexts.FindText("str_game_difficulty"));
                            }
                        }
                    }
                    __result = 1f - MBMath.ClampFloat(stat2.ResultNumber, 0f, 1f);
                    return false;
                }
                if (stat.ResultNumber.ApproximatelyEqualsTo(0f))
                {
                    __result = 0f;
                    return false;
                }
                __result = 1f - 1f / stat.ResultNumber;
                return false;
            }
        }

        [HarmonyPatch(typeof(StoryModeGenericXpModel))]
        [HarmonyPatch("GetXpMultiplier")]
        private class AddSkillXpPatch
        {
            private static bool Prefix(StoryModeGenericXpModel __instance, Hero hero, ref float __result)
            {
                __result = 1f;
                return false;
            }
        }

        [HarmonyPatch(typeof(DefaultCombatXpModel))]
        [HarmonyPatch("GetXpMultiplierFromShotDifficulty")]
        private class GetXpMultiplierFromShotDifficultyPatch
        {
            private static bool Prefix(DefaultCombatXpModel __instance, float shotDifficulty, ref float __result)
            {
                if (shotDifficulty > 14.4f)
                {
                    shotDifficulty = 14.4f;
                }
                __result = MBMath.Lerp(1.25f, 3.0f, (shotDifficulty - 1f) / 13.4f);
                return false;
            }
        }

        [HarmonyPatch(typeof(StoryMode.Extensions.Extensions))]
        [HarmonyPatch("IsTrainingField")]
        private class IsTrainingFieldPatch
        {
            private static bool Prefix(ref bool __result)
            {
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(DefaultCombatXpModel))]
        private class GetXpFromHitPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("GetXpFromHit")]
            private static bool PrefixGetXpFromHit(ref DefaultCombatXpModel __instance, CharacterObject attackerTroop, CharacterObject captain, CharacterObject attackedTroop, PartyBase party, int damage, bool isFatal, MissionTypeEnum missionType, out int xpAmount)
            {
                if (missionType == MissionTypeEnum.Battle || missionType == MissionTypeEnum.PracticeFight || missionType == MissionTypeEnum.Tournament || missionType == MissionTypeEnum.SimulationBattle)
                {
                    float victimTroopPower = 0f;
                    float attackerTroopPower = 0f;
                    if (party?.MapEvent != null)
                    {
                        victimTroopPower = OverrideDefaultMilitaryPowerModel.GetTroopPowerBasedOnContextForXPVictim(attackedTroop, party.MapEvent?.EventType ?? MapEvent.BattleTypes.None, party.Side, missionType == MissionTypeEnum.SimulationBattle);
                        attackerTroopPower = OverrideDefaultMilitaryPowerModel.GetTroopPowerBasedOnContextForXPAttacker(attackerTroop, party.MapEvent?.EventType ?? MapEvent.BattleTypes.None, party.Side, missionType == MissionTypeEnum.SimulationBattle);
                    }
                    else
                    {
                        victimTroopPower = OverrideDefaultMilitaryPowerModel.GetTroopPowerBasedOnContextForXPVictim(attackedTroop);
                        attackerTroopPower = OverrideDefaultMilitaryPowerModel.GetTroopPowerBasedOnContextForXPAttacker(attackerTroop);
                    }
                    float rawXpNum = 0;

                    rawXpNum = 0.4f * (victimTroopPower + 0.5f) * (attackerTroopPower + 0.5f) * (float)(30);

                    float xpModifier;
                    switch (missionType)
                    {
                        case MissionTypeEnum.NoXp:
                            xpModifier = 0f;
                            break;

                        default:
                            xpModifier = 1f;
                            break;
                            //case MissionTypeEnum.Battle:
                            //    xpModifier = 1f;
                            //    break;
                            //case MissionTypeEnum.SimulationBattle:
                            //    xpModifier = 1f;
                            //    break;
                            //case MissionTypeEnum.Tournament:
                            //    xpModifier = 1f;
                            //    break;
                            //case MissionTypeEnum.PracticeFight:
                            //    xpModifier = 1f;
                            //    break;
                    }
                    //rawXpNum = rawXpNum * xpModifier * levelDiffModifier;
                    rawXpNum = rawXpNum * xpModifier;
                    ExplainedNumber xpToGain = new ExplainedNumber(rawXpNum);
                    if (party != null)
                    {
                        MethodInfo method = typeof(DefaultCombatXpModel).GetMethod("GetBattleXpBonusFromPerks", BindingFlags.NonPublic | BindingFlags.Instance);
                        method.DeclaringType.GetMethod("GetBattleXpBonusFromPerks");
                        method.Invoke(__instance, new object[] { party, xpToGain, attackerTroop });
                    }
                    if (captain != null && captain.IsHero && captain.GetPerkValue(DefaultPerks.Leadership.InspiringLeader))
                    {
                        xpToGain.AddFactor(DefaultPerks.Leadership.InspiringLeader.SecondaryBonus, DefaultPerks.Leadership.InspiringLeader.Name);
                    }
                    xpAmount = MathF.Round(xpToGain.ResultNumber);
                    return false;
                }
                xpAmount = 0;
                return true;
            }
        }

        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("CreateMeleeBlow")]
        private class CreateMeleeBlowPatch
        {
            private static void Postfix(ref Mission __instance, ref Blow __result, Agent attackerAgent, Agent victimAgent, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, CrushThroughState crushThroughState, Vec3 blowDirection, Vec3 swingDirection, bool cancelDamage)
            {
                if (Campaign.Current != null)
                {
                    if (victimAgent != null && victimAgent.Character != null && victimAgent.Character.IsHero)
                    {
                        if (collisionData.CollisionResult == CombatCollisionResult.Blocked || collisionData.CollisionResult == CombatCollisionResult.Parried || collisionData.CollisionResult == CombatCollisionResult.ChamberBlocked)
                        {
                            CharacterObject affectedCharacter = (CharacterObject)victimAgent.Character;
                            Hero heroObject = affectedCharacter.HeroObject;

                            CharacterObject affectorCharacter = (CharacterObject)attackerAgent.Character;

                            float experience = 1f;
                            Campaign.Current.Models.CombatXpModel.GetXpFromHit(heroObject.CharacterObject, null, affectorCharacter, heroObject.PartyBelongedTo?.Party, (int)collisionData.InflictedDamage, false, CombatXpModel.MissionTypeEnum.Battle, out var xpAmount);
                            if (collisionData.CollisionResult == CombatCollisionResult.Blocked && collisionData.AttackBlockedWithShield)
                            {
                                experience = xpAmount * 0.8f;
                            }
                            if (collisionData.CollisionResult == CombatCollisionResult.Parried || collisionData.CollisionResult == CombatCollisionResult.ChamberBlocked)
                            {
                                experience = xpAmount * 1.2f;
                            }
                            WeaponComponentData parryWeapon = victimAgent.WieldedWeapon.CurrentUsageItem;
                            if (parryWeapon != null)
                            {
                                SkillObject skillForWeapon = Campaign.Current.Models.CombatXpModel.GetSkillForWeapon(parryWeapon, false);
                                float num2 = ((skillForWeapon == DefaultSkills.Bow) ? 0.5f : 1f);
                                affectedCharacter.HeroObject.AddSkillXp(skillForWeapon, experience);
                            }
                            else
                            {
                                heroObject.AddSkillXp(DefaultSkills.Athletics, MBRandom.RoundRandomized(experience));
                            }
                            if (victimAgent.HasMount)
                            {
                                float num3 = 0.1f;
                                float speedBonusFromMovement = collisionData.MovementSpeedDamageModifier;
                                if (speedBonusFromMovement > 0f)
                                {
                                    num3 *= 1f + speedBonusFromMovement;
                                }
                                if (num3 > 0f)
                                {
                                    heroObject.AddSkillXp(DefaultSkills.Riding, MBRandom.RoundRandomized(num3 * experience));
                                }
                            }
                            else
                            {
                                float num5 = 0.2f;
                                float speedBonusFromMovement = collisionData.MovementSpeedDamageModifier;
                                if (speedBonusFromMovement > 0f)
                                {
                                    num5 += 1.5f * speedBonusFromMovement;
                                }
                                if (num5 > 0f)
                                {
                                    heroObject.AddSkillXp(DefaultSkills.Athletics, num5 * experience);
                                }
                            }
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(TrainingFieldMissionController))]
        [HarmonyPatch("BowInTrainingAreaUpdate")]
        private static class BowInTrainingAreaUpdatePatch
        {
            private static int lastBreakeableCount = -1;
            private static bool shouldCount = false;

            private static void Postfix(int ____trainingProgress, TutorialArea ____activeTutorialArea, int ____trainingSubTypeIndex)
            {
                if (____trainingProgress == 1)
                {
                    lastBreakeableCount = -1;
                }
                if (____trainingProgress == 4)
                {
                    if (lastBreakeableCount == -1)
                    {
                        lastBreakeableCount = ____activeTutorialArea.GetBrokenBreakableCount(____trainingSubTypeIndex);
                    }
                    else
                    {
                        if (lastBreakeableCount != ____activeTutorialArea.GetBrokenBreakableCount(____trainingSubTypeIndex))
                        {
                            lastBreakeableCount = ____activeTutorialArea.GetBrokenBreakableCount(____trainingSubTypeIndex);
                            shouldCount = true;
                        }
                    }
                }
                if (shouldCount && ____trainingProgress == 4)
                {
                    shouldCount = false;
                    EquipmentIndex ei = Mission.Current.MainAgent.GetWieldedItemIndex(Agent.HandIndex.MainHand);
                    if (ei != EquipmentIndex.None)
                    {
                        CharacterObject playerCharacter = (CharacterObject)CharacterObject.PlayerCharacter;
                        if (playerCharacter != null)
                        {
                            if (Mission.Current.MainAgent.WieldedWeapon.CurrentUsageItem != null)
                            {
                                WeaponComponentData wieldedWeapon = Mission.Current.MainAgent.WieldedWeapon.CurrentUsageItem;
                                SkillObject skillForWeapon = Campaign.Current.Models.CombatXpModel.GetSkillForWeapon(wieldedWeapon, false);
                                if (skillForWeapon != null)
                                {
                                    playerCharacter.HeroObject.AddSkillXp(skillForWeapon, 50);
                                    if (Mission.Current.MainAgent.HasMount)
                                    {
                                        playerCharacter.HeroObject.AddSkillXp(DefaultSkills.Riding, 25);
                                    }
                                    else
                                    {
                                        playerCharacter.HeroObject.AddSkillXp(DefaultSkills.Athletics, 25);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(TrainingFieldMissionController))]
        [HarmonyPatch("BowTrainingEndedSuccessfully")]
        private static class BowTrainingEndedSuccessfullyPatch
        {
            private static void Postfix(int ____trainingProgress, TutorialArea ____activeTutorialArea, int ____trainingSubTypeIndex)
            {
                EquipmentIndex ei = Mission.Current.MainAgent.GetWieldedItemIndex(Agent.HandIndex.MainHand);
                if (ei != EquipmentIndex.None)
                {
                    CharacterObject playerCharacter = (CharacterObject)CharacterObject.PlayerCharacter;
                    if (playerCharacter != null)
                    {
                        if (Mission.Current.MainAgent.WieldedWeapon.CurrentUsageItem != null)
                        {
                            WeaponComponentData wieldedWeapon = Mission.Current.MainAgent.WieldedWeapon.CurrentUsageItem;
                            SkillObject skillForWeapon = Campaign.Current.Models.CombatXpModel.GetSkillForWeapon(wieldedWeapon, false);
                            if (skillForWeapon != null)
                            {
                                playerCharacter.HeroObject.AddSkillXp(skillForWeapon, 500);
                                if (Mission.Current.MainAgent.HasMount)
                                {
                                    playerCharacter.HeroObject.AddSkillXp(DefaultSkills.Riding, 250);
                                }
                                else
                                {
                                    playerCharacter.HeroObject.AddSkillXp(DefaultSkills.Athletics, 250);
                                }
                            }
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(TrainingFieldMissionController))]
        [HarmonyPatch("MountedTrainingUpdate")]
        private static class MountedTrainingUpdatePatch
        {
            private static int lastBreakeableCount = -1;
            private static bool shouldCount = false;

            private static void Postfix(int ____trainingProgress, TutorialArea ____activeTutorialArea, int ____trainingSubTypeIndex)
            {
                if (____trainingProgress == 1)
                {
                    lastBreakeableCount = -1;
                }
                if (____trainingProgress == 4)
                {
                    if (lastBreakeableCount == -1)
                    {
                        lastBreakeableCount = ____activeTutorialArea.GetBrokenBreakableCount(____trainingSubTypeIndex);
                    }
                    else
                    {
                        if (lastBreakeableCount != ____activeTutorialArea.GetBrokenBreakableCount(____trainingSubTypeIndex))
                        {
                            lastBreakeableCount = ____activeTutorialArea.GetBrokenBreakableCount(____trainingSubTypeIndex);
                            shouldCount = true;
                        }
                    }
                }
                if (shouldCount && ____trainingProgress == 4)
                {
                    shouldCount = false;
                    EquipmentIndex ei = Mission.Current.MainAgent.GetWieldedItemIndex(Agent.HandIndex.MainHand);
                    if (ei != EquipmentIndex.None)
                    {
                        CharacterObject playerCharacter = (CharacterObject)CharacterObject.PlayerCharacter;
                        if (playerCharacter != null)
                        {
                            if (Mission.Current.MainAgent.WieldedWeapon.CurrentUsageItem != null)
                            {
                                WeaponComponentData wieldedWeapon = Mission.Current.MainAgent.WieldedWeapon.CurrentUsageItem;
                                SkillObject skillForWeapon = Campaign.Current.Models.CombatXpModel.GetSkillForWeapon(wieldedWeapon, false);
                                if (skillForWeapon != null)
                                {
                                    playerCharacter.HeroObject.AddSkillXp(skillForWeapon, 50);
                                    if (Mission.Current.MainAgent.HasMount)
                                    {
                                        playerCharacter.HeroObject.AddSkillXp(DefaultSkills.Riding, 25);
                                    }
                                    else
                                    {
                                        playerCharacter.HeroObject.AddSkillXp(DefaultSkills.Athletics, 25);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(TrainingFieldMissionController))]
        [HarmonyPatch("MountedTrainingEndedSuccessfully")]
        private static class MountedTrainingEndedSuccessfullyPatch
        {
            private static void Postfix(int ____trainingProgress, TutorialArea ____activeTutorialArea, int ____trainingSubTypeIndex, float ____timeScore)
            {
                int brokenBreakableCount = ____activeTutorialArea.GetBrokenBreakableCount(____trainingSubTypeIndex);
                int breakablesCount = ____activeTutorialArea.GetBreakablesCount(____trainingSubTypeIndex);
                float missFactor = (float)brokenBreakableCount / (float)breakablesCount;
                if (missFactor >= 1f)
                {
                    missFactor = 1.25f;
                }
                float defaultTime = 80f;
                float timeFactor = defaultTime / ____timeScore;
                EquipmentIndex ei = Mission.Current.MainAgent.GetWieldedItemIndex(Agent.HandIndex.MainHand);
                if (ei != EquipmentIndex.None)
                {
                    CharacterObject playerCharacter = (CharacterObject)CharacterObject.PlayerCharacter;
                    if (playerCharacter != null)
                    {
                        if (Mission.Current.MainAgent.WieldedWeapon.CurrentUsageItem != null)
                        {
                            WeaponComponentData wieldedWeapon = Mission.Current.MainAgent.WieldedWeapon.CurrentUsageItem;
                            SkillObject skillForWeapon = Campaign.Current.Models.CombatXpModel.GetSkillForWeapon(wieldedWeapon, false);
                            if (skillForWeapon != null)
                            {
                                playerCharacter.HeroObject.AddSkillXp(skillForWeapon, 1000 * missFactor * timeFactor);
                                if (Mission.Current.MainAgent.HasMount)
                                {
                                    playerCharacter.HeroObject.AddSkillXp(DefaultSkills.Riding, 500 * missFactor * timeFactor);
                                }
                                else
                                {
                                    playerCharacter.HeroObject.AddSkillXp(DefaultSkills.Athletics, 500 * missFactor * timeFactor);
                                }
                            }
                        }
                    }
                }
            }
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

        [HarmonyPatch(typeof(PerkHelper))]
        private class OverrideAddPerkBonusForParty
        {
            private static void AddToStat(ref ExplainedNumber stat, SkillEffect.EffectIncrementType effectIncrementType, float number, TextObject text)
            {
                switch (effectIncrementType)
                {
                    case SkillEffect.EffectIncrementType.Add:
                        stat.Add(number, text);
                        break;

                    case SkillEffect.EffectIncrementType.AddFactor:
                        stat.AddFactor(number * 0.01f, text);
                        break;
                }
            }

            [HarmonyPrefix]
            [HarmonyPatch("AddPerkBonusForParty")]
            private static bool PrefixAddPerkBonusForParty(PerkObject perk, MobileParty party, bool isPrimaryBonus, ref ExplainedNumber stat)
            {
                Hero hero = party?.LeaderHero;
                if (hero == null)
                {
                    return false;
                }
                bool flag = isPrimaryBonus && perk.PrimaryRole == SkillEffect.PerkRole.PartyLeader;
                bool flag2 = !isPrimaryBonus && perk.SecondaryRole == SkillEffect.PerkRole.PartyLeader;
                if ((flag || flag2) && hero.GetPerkValue(perk))
                {
                    float num = (flag ? perk.PrimaryBonus : perk.SecondaryBonus);
                    if (flag)
                    {
                        AddToStat(ref stat, perk.PrimaryIncrementType, num, perk.Name);
                    }
                    else
                    {
                        AddToStat(ref stat, perk.SecondaryIncrementType, num, perk.Name);
                    }
                }
                flag = isPrimaryBonus && perk.PrimaryRole == SkillEffect.PerkRole.ClanLeader;
                flag2 = !isPrimaryBonus && perk.SecondaryRole == SkillEffect.PerkRole.ClanLeader;
                if ((flag || flag2) && hero.Clan.Leader != null && hero.Clan.Leader.GetPerkValue(perk))
                {
                    if (flag)
                    {
                        AddToStat(ref stat, perk.PrimaryIncrementType, perk.PrimaryBonus, perk.Name);
                    }
                    else
                    {
                        AddToStat(ref stat, perk.SecondaryIncrementType, perk.SecondaryBonus, perk.Name);
                    }
                }
                flag = isPrimaryBonus && perk.PrimaryRole == SkillEffect.PerkRole.PartyMember;
                flag2 = !isPrimaryBonus && perk.SecondaryRole == SkillEffect.PerkRole.PartyMember;
                if (flag || flag2)
                {
                    if (hero.Clan != Clan.PlayerClan)
                    {
                        if (hero.GetPerkValue(perk))
                        {
                            AddToStat(ref stat, flag ? perk.PrimaryIncrementType : perk.SecondaryIncrementType, flag ? perk.PrimaryBonus : perk.SecondaryBonus, perk.Name);
                        }
                    }
                    else
                    {
                        foreach (TroopRosterElement item in party.MemberRoster.GetTroopRoster())
                        {
                            if (item.Character.IsHero && item.Character.GetPerkValue(perk))
                            {
                                AddToStat(ref stat, flag ? perk.PrimaryIncrementType : perk.SecondaryIncrementType, flag ? perk.PrimaryBonus : perk.SecondaryBonus, perk.Name);
                            }
                        }
                    }
                }
                if (hero.Clan != Clan.PlayerClan)
                {
                    return false;
                }
                flag = isPrimaryBonus && perk.PrimaryRole == SkillEffect.PerkRole.Engineer;
                flag2 = !isPrimaryBonus && perk.SecondaryRole == SkillEffect.PerkRole.Engineer;
                if (flag || flag2)
                {
                    Hero effectiveEngineer = party.EffectiveEngineer;
                    if (effectiveEngineer != null && effectiveEngineer.GetPerkValue(perk))
                    {
                        if (flag)
                        {
                            AddToStat(ref stat, perk.PrimaryIncrementType, perk.PrimaryBonus, perk.Name);
                        }
                        else
                        {
                            AddToStat(ref stat, perk.SecondaryIncrementType, perk.SecondaryBonus, perk.Name);
                        }
                    }
                }
                flag = isPrimaryBonus && perk.PrimaryRole == SkillEffect.PerkRole.Scout;
                flag2 = !isPrimaryBonus && perk.SecondaryRole == SkillEffect.PerkRole.Scout;
                if (flag || flag2)
                {
                    Hero effectiveScout = party.EffectiveScout;
                    if (effectiveScout != null && effectiveScout.GetPerkValue(perk))
                    {
                        if (flag)
                        {
                            AddToStat(ref stat, perk.PrimaryIncrementType, perk.PrimaryBonus, perk.Name);
                        }
                        else
                        {
                            AddToStat(ref stat, perk.SecondaryIncrementType, perk.SecondaryBonus, perk.Name);
                        }
                    }
                }
                flag = isPrimaryBonus && perk.PrimaryRole == SkillEffect.PerkRole.Surgeon;
                flag2 = !isPrimaryBonus && perk.SecondaryRole == SkillEffect.PerkRole.Surgeon;
                if (flag || flag2)
                {
                    Hero effectiveSurgeon = party.EffectiveSurgeon;
                    if (effectiveSurgeon != null && effectiveSurgeon.GetPerkValue(perk))
                    {
                        if (flag)
                        {
                            AddToStat(ref stat, perk.PrimaryIncrementType, perk.PrimaryBonus, perk.Name);
                        }
                        else
                        {
                            AddToStat(ref stat, perk.SecondaryIncrementType, perk.SecondaryBonus, perk.Name);
                        }
                    }
                }
                flag = isPrimaryBonus && perk.PrimaryRole == SkillEffect.PerkRole.Quartermaster;
                flag2 = !isPrimaryBonus && perk.SecondaryRole == SkillEffect.PerkRole.Quartermaster;
                if (!(flag || flag2))
                {
                    return false;
                }
                Hero effectiveQuartermaster = party.EffectiveQuartermaster;
                if (effectiveQuartermaster != null && effectiveQuartermaster.GetPerkValue(perk))
                {
                    if (flag)
                    {
                        AddToStat(ref stat, perk.PrimaryIncrementType, perk.PrimaryBonus, perk.Name);
                    }
                    else
                    {
                        AddToStat(ref stat, perk.SecondaryIncrementType, perk.SecondaryBonus, perk.Name);
                    }
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(SandboxAgentStatCalculateModel))]
        private class OverrideGetEffectiveMaxHealth
        {
            [HarmonyPrefix]
            [HarmonyPatch("GetEffectiveMaxHealth")]
            private static bool PrefixGetEffectiveMaxHealth(ref SandboxAgentStatCalculateModel __instance, ref float __result, Agent agent)
            {
                float baseHealthLimit = agent.BaseHealthLimit;
                ExplainedNumber stat = new ExplainedNumber(baseHealthLimit);
                if (agent.IsHuman && !agent.IsHero)
                {
                    CharacterObject characterObject = agent.Character as CharacterObject;
                    IAgentOriginBase agentOriginBase = agent?.Origin;
                    MobileParty mobileParty = ((PartyBase)(agentOriginBase?.BattleCombatant))?.MobileParty;
                    //CharacterObject partyLeader = mobileParty?.LeaderHero?.CharacterObject;
                    CharacterObject captain = agent?.Formation?.Captain?.Character as CharacterObject;
                    if (characterObject != null && captain != null)
                    {
                        if (captain.GetPerkValue(DefaultPerks.TwoHanded.ThickHides))
                        {
                            PerkHelper.AddPerkBonusForParty(DefaultPerks.TwoHanded.ThickHides, mobileParty, isPrimaryBonus: false, ref stat);
                        }
                        if (captain.GetPerkValue(DefaultPerks.Polearm.HardyFrontline))
                        {
                            PerkHelper.AddPerkBonusForParty(DefaultPerks.Polearm.HardyFrontline, mobileParty, isPrimaryBonus: true, ref stat);
                        }
                        if (characterObject.IsRanged)
                        {
                            if (captain.GetPerkValue(DefaultPerks.Crossbow.PickedShots))
                            {
                                PerkHelper.AddPerkBonusForParty(DefaultPerks.Crossbow.PickedShots, mobileParty, isPrimaryBonus: false, ref stat);
                            }
                        }
                        if (!agent.HasMount)
                        {
                            if (captain.GetPerkValue(DefaultPerks.Athletics.WellBuilt))
                            {
                                PerkHelper.AddPerkBonusForParty(DefaultPerks.Athletics.WellBuilt, mobileParty, isPrimaryBonus: false, ref stat);
                            }
                            if (characterObject.IsInfantry)
                            {
                                if (captain.GetPerkValue(DefaultPerks.Polearm.HardKnock))
                                {
                                    PerkHelper.AddPerkBonusForParty(DefaultPerks.Polearm.HardKnock, mobileParty, isPrimaryBonus: false, ref stat);
                                }
                                if (captain.GetPerkValue(DefaultPerks.OneHanded.UnwaveringDefense))
                                {
                                    PerkHelper.AddPerkBonusForParty(DefaultPerks.OneHanded.UnwaveringDefense, mobileParty, isPrimaryBonus: false, ref stat);
                                }
                            }
                        }
                        if (captain.GetPerkValue(DefaultPerks.Medicine.MinisterOfHealth))
                        {
                            int num = MathF.Max(__instance.GetEffectiveSkill(agent, DefaultSkills.Medicine) - 200, 0) / 10;
                            if (num > 0)
                            {
                                stat.Add(num);
                            }
                        }
                    }
                }
                else if (!agent.IsHuman)
                {
                    Agent riderAgent = agent.RiderAgent;
                    if (riderAgent != null)
                    {
                        CharacterObject character = riderAgent?.Character as CharacterObject;
                        IAgentOriginBase agentOriginBase = agent?.Origin;
                        MobileParty mobileParty = ((PartyBase)(agentOriginBase?.BattleCombatant))?.MobileParty;
                        CharacterObject partyLeader = mobileParty?.LeaderHero?.CharacterObject;
                        CharacterObject captain = agent?.Formation?.Captain?.Character as CharacterObject;
                        PerkHelper.AddPerkBonusFromCaptain(DefaultPerks.Medicine.Sledges, captain, ref stat);
                        PerkHelper.AddPerkBonusForCharacter(DefaultPerks.Riding.WellStraped, character, isPrimaryBonus: true, ref stat);
                        PerkHelper.AddPerkBonusFromCaptain(DefaultPerks.Riding.WellStraped, captain, ref stat);
                    }
                }
                __result = stat.ResultNumber;
                return false;
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
            //[HarmonyPrefix]
            //[HarmonyPatch("GetTroopPowerBasedOnContext")]
            //private static bool PrefixGetTroopPowerBasedOnContext(ref float __result, CharacterObject troop, MapEvent.BattleTypes battleType = MapEvent.BattleTypes.None, BattleSideEnum battleSideEnum = BattleSideEnum.None, bool isSimulation = false)
            //{

            //    int tier = (troop.IsHero ? (troop.HeroObject.Level / 4 + 1) : troop.Tier);
            //    bool isNoble = false;
            //    if (troop.Culture != null)
            //    {
            //        CharacterObject EliteBasicTroop = troop.Culture.EliteBasicTroop;
            //        if (troop == EliteBasicTroop)
            //        {
            //            isNoble = true;
            //        }
            //        else
            //        {
            //            List<CharacterObject> cultureNobleTroopList = FillTroopListUntilTier(troop.Culture.EliteBasicTroop, 10);
            //            foreach (CharacterObject co in cultureNobleTroopList)
            //            {
            //                if (co == troop)
            //                {
            //                    isNoble = true;
            //                }
            //            }
            //        }
            //    }
            //    float origPower = (float)((2f + tier) * (8f + tier)) * 0.02f * (troop.IsHero ? 1.5f : (troop.IsMounted ? 1.2f : 1f));
            //    float modifiedTier = (tier - 1) * 3f;
            //    modifiedTier = MathF.Clamp(modifiedTier, 1f, modifiedTier);
            //    if (battleType == BattleTypes.Siege)
            //    {
            //        __result = (float)((2f + modifiedTier) * (8f + modifiedTier)) * 0.02f * (troop.IsHero ? 1.5f : 1f);
            //        return false;
            //    }
            //    __result = (float)((2f + modifiedTier) * (8f + modifiedTier)) * 0.02f * (troop.IsHero ? 1.5f : 1f) * (troop.IsMounted ? 1.5f : 1f) * (isNoble ? 1.5f : 1f);
            //    return false;
            //}

            [HarmonyPatch(typeof(CommonAIComponent))]
            [HarmonyPatch("InitializeMorale")]
            private class InitializeMoralePatch
            {
                private static bool Prefix(ref CommonAIComponent __instance, ref Agent ___Agent, ref float ____initialMorale, ref float ____recoveryMorale)
                {
                    //int num = MBRandom.RandomInt(30);
                    int num = 30;
                    float num2 = ___Agent.Components.Sum((AgentComponent c) => c.GetMoraleAddition());
                    float baseMorale = 35f + (float)num + num2;
                    baseMorale = MissionGameModels.Current.BattleMoraleModel.GetEffectiveInitialMorale(___Agent, baseMorale);
                    baseMorale = (____initialMorale = MBMath.ClampFloat(baseMorale, 15f, 100f));
                    ____recoveryMorale = ____initialMorale * 0.5f;
                    __instance.Morale = ____initialMorale;
                    return false;
                }
            }

            //[HarmonyPatch(typeof(Equipment))]
            //[HarmonyPatch("GetRandomEquipmentElements")]
            //class GetRandomEquipmentElementsPatch
            //{
            //    static bool Prefix(ref Equipment __result, BasicCharacterObject character, bool randomEquipmentModifier, bool isCivilianEquipment = false, int seed = -1)
            //    {
            //        if(seed == -1)
            //        {
            //            return true;
            //        }
            //        Equipment equipment = new Equipment(isCivilianEquipment);
            //        List<Equipment> list = character.AllEquipments.Where((Equipment eq) => eq.IsCivilian == isCivilianEquipment && !eq.IsEmpty()).ToList();
            //        if (list.IsEmpty())
            //        {
            //            __result = equipment;
            //            return false;
            //        }
            //        int count = list.Count;
            //        //Random random = new Random(seed);
            //        int weaponSetNo = MBRandom.RandomInt(count);
            //        for (int i = 0; i < 12; i++)
            //        {
            //            equipment[i] = list[weaponSetNo].GetEquipmentFromSlot((EquipmentIndex)i);
            //        }
            //        __result = equipment;
            //        return false;
            //    }
            //}

            [HarmonyPatch(typeof(Agent))]
            [HarmonyPatch("InitializeSpawnEquipment")]
            private class InitializeSpawnEquipmentPatch
            {
                private static bool Prefix(Equipment spawnEquipment, ref Agent __instance)
                {
                    if (Campaign.Current != null && __instance.IsHuman && __instance.IsHero && !__instance.Character.IsPlayerCharacter)
                    {
                        Equipment spawnEquipment2 = spawnEquipment.Clone();
                        bool shoudReceiveUpgradedGear = false;
                        Hero hero = ((CharacterObject)__instance.Character).HeroObject;
                        foreach (Kingdom kingdom in Kingdom.All)
                        {
                            if (kingdom.Leader != null && kingdom.Leader == hero)
                            {
                                shoudReceiveUpgradedGear = true;
                                break;
                            }
                            foreach (Hero lord in kingdom.Lords)
                            {
                                if (lord != null && lord == hero)
                                {
                                    shoudReceiveUpgradedGear = true;
                                    break;
                                }
                            }
                        }
                        if (shoudReceiveUpgradedGear)
                        {
                            for (int i = 0; i < 12; i++)
                            {
                                if (spawnEquipment2[(EquipmentIndex)i].Item != null)
                                {
                                    IReadOnlyList<ItemModifier> itemModifiers = spawnEquipment2[(EquipmentIndex)i].Item?.ItemComponent?.ItemModifierGroup?.ItemModifiers;
                                    if (itemModifiers != null)
                                    {
                                        EquipmentElement equipmentFromSlot = spawnEquipment2[(EquipmentIndex)i];
                                        equipmentFromSlot.SetModifier(itemModifiers[0]);
                                        spawnEquipment2[(EquipmentIndex)i] = equipmentFromSlot;
                                    }
                                }
                            }
                        }
                        PropertyInfo propertySpawnEquipment = typeof(Agent).GetProperty("SpawnEquipment");
                        propertySpawnEquipment.DeclaringType.GetProperty("SpawnEquipment");
                        propertySpawnEquipment.SetValue(__instance, spawnEquipment2, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                        return false;
                    }
                    return true;
                }
            }

            [HarmonyPatch(typeof(Equipment))]
            [HarmonyPatch("GetRandomizedEquipment")]
            private class GetRandomizedEquipmentPatch
            {
                private static bool Prefix(ref List<Equipment> equipmentSets, ref EquipmentIndex weaponSlot, ref int weaponSetNo, ref bool randomEquipmentModifier, ref EquipmentElement __result)
                {
                    EquipmentElement equipmentFromSlot = equipmentSets[weaponSetNo].GetEquipmentFromSlot(weaponSlot);
                    //if(equipmentSets.Count > 1)
                    //{
                    //    bool testik = false;
                    //}
                    __result = equipmentFromSlot;
                    return false;
                }
            }

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