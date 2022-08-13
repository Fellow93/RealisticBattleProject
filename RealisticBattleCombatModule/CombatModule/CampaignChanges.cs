using HarmonyLib;
using Helpers;
using SandBox.GameComponents;
using StoryMode.GameComponents;
using StoryMode.Missions;
using System;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.CampaignSystem.ComponentInterfaces.CombatXpModel;
using static TaleWorlds.CampaignSystem.ComponentInterfaces.MilitaryPowerModel;

namespace RBMCombat
{
    class CampaignChanges
    {
        [HarmonyPatch(typeof(DefaultPartyHealingModel))]
        class OverrideDefaultPartyHealingModel
        {
            [HarmonyPrefix]
            [HarmonyPatch("GetSurvivalChance")]
            static bool PrefixGetAiWeight(ref float __result, PartyBase party, CharacterObject character, DamageTypes damageType, PartyBase enemyParty = null)
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
        class AddSkillXpPatch
        {
            static bool Prefix(StoryModeGenericXpModel __instance, Hero hero, ref float __result)
            {
                __result = 1f;
                return false;
            }
        }

        [HarmonyPatch(typeof(DefaultCombatXpModel))]
        [HarmonyPatch("GetXpMultiplierFromShotDifficulty")]
        class GetXpMultiplierFromShotDifficultyPatch
        {
            static bool Prefix(DefaultCombatXpModel __instance, float shotDifficulty, ref float __result)
            {
                if (shotDifficulty > 14.4f)
                {
                    shotDifficulty = 14.4f;
                }
                __result = MBMath.Lerp(0.5f, 2.5f, (shotDifficulty - 1f) / 13.4f);
                return false;
            }
        }

        [HarmonyPatch(typeof(DefaultCombatXpModel))]
        class GetXpFromHitPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("GetXpFromHit")]
            static bool PrefixGetXpFromHit(ref DefaultCombatXpModel __instance, CharacterObject attackerTroop, CharacterObject captain, CharacterObject attackedTroop, PartyBase party, int damage, bool isFatal, MissionTypeEnum missionType, out int xpAmount)
            {
                if (missionType == MissionTypeEnum.Battle || missionType == MissionTypeEnum.PracticeFight || missionType == MissionTypeEnum.Tournament)
                {
                    float victimTroopPower = 0f;
                    float attackerTroopPower = 0f;
                    if (party?.MapEvent != null)
                    {
                        victimTroopPower = OverrideDefaultMilitaryPowerModel.GetTroopPowerBasedOnContextForXP(attackedTroop, party.MapEvent?.EventType ?? MapEvent.BattleTypes.None, party.Side, missionType == MissionTypeEnum.SimulationBattle);
                        attackerTroopPower = OverrideDefaultMilitaryPowerModel.GetTroopPowerBasedOnContextForXP(attackerTroop, party.MapEvent?.EventType ?? MapEvent.BattleTypes.None, party.Side, missionType == MissionTypeEnum.SimulationBattle);
                    }
                    else
                    {
                        victimTroopPower = OverrideDefaultMilitaryPowerModel.GetTroopPowerBasedOnContextForXP(attackedTroop);
                        attackerTroopPower = OverrideDefaultMilitaryPowerModel.GetTroopPowerBasedOnContextForXP(attackerTroop);
                    }
                    float rawXpNum = 0;
                    //if (damage < 30)
                    //{
                        rawXpNum = 0.4f * (victimTroopPower + 0.5f) * (attackerTroopPower + 0.5f) * (float)(30);
                    //}
                    //else
                    //{
                    //    rawXpNum = 0.4f * ((troopPower + 0.5f) * (float)(Math.Min(damage, attackedTroopMaxHP) + (isFatal ? attackedTroopMaxHP : 0)));
                    //}
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
        class CreateMeleeBlowPatch
        {
            static void Postfix(ref Mission __instance, ref Blow __result, Agent attackerAgent, Agent victimAgent, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, CrushThroughState crushThroughState, Vec3 blowDirection, Vec3 swingDirection, bool cancelDamage)
            {
                if(Campaign.Current != null)
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
        static class BowInTrainingAreaUpdatePatch
        {
            static int lastBreakeableCount = -1;
            static bool shouldCount = false;
            static void Postfix(int ____trainingProgress, TutorialArea ____activeTutorialArea, int ____trainingSubTypeIndex)
            {
                if (____trainingProgress == 1)
                {
                    lastBreakeableCount = -1;
                }
                if(____trainingProgress == 4)
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
                                if(skillForWeapon != null)
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
        static class BowTrainingEndedSuccessfullyPatch
        {
            static void Postfix(int ____trainingProgress, TutorialArea ____activeTutorialArea, int ____trainingSubTypeIndex)
            {
                EquipmentIndex ei = Mission.Current.MainAgent.GetWieldedItemIndex(Agent.HandIndex.MainHand);
                if (ei != EquipmentIndex.None)
                {
                    CharacterObject playerCharacter = (CharacterObject)CharacterObject.PlayerCharacter;
                    if(playerCharacter != null)
                    {
                        if(Mission.Current.MainAgent.WieldedWeapon.CurrentUsageItem != null)
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
        static class MountedTrainingUpdatePatch
        {
            static int lastBreakeableCount = -1;
            static bool shouldCount = false;
            static void Postfix(int ____trainingProgress, TutorialArea ____activeTutorialArea, int ____trainingSubTypeIndex)
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
        static class MountedTrainingEndedSuccessfullyPatch
        {
            static void Postfix(int ____trainingProgress, TutorialArea ____activeTutorialArea, int ____trainingSubTypeIndex, float ____timeScore)
            {
                int brokenBreakableCount = ____activeTutorialArea.GetBrokenBreakableCount(____trainingSubTypeIndex);
                int breakablesCount = ____activeTutorialArea.GetBreakablesCount(____trainingSubTypeIndex);
                float missFactor = (float)brokenBreakableCount / (float)breakablesCount;
                if(missFactor >= 1f)
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
        static class DefaultVolunteerModelPatch
        {
            static bool Prefix(Hero sellerHero, ref CharacterObject __result)
            {
                float randomF = MBRandom.RandomFloat;
                if(randomF < 0.15f)
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
        class OverrideAddPerkBonusForParty
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
            static bool PrefixAddPerkBonusForParty(PerkObject perk, MobileParty party, bool isPrimaryBonus,
                ref ExplainedNumber stat, ref TextObject ____textLeader ,ref TextObject ____textScout, ref TextObject ____textSurgeon, ref TextObject ____textQuartermaster)
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
                        AddToStat(ref stat, perk.PrimaryIncrementType, num, ____textLeader);
                    }
                    else
                    {
                        AddToStat(ref stat, perk.SecondaryIncrementType, num, ____textLeader);
                    }
                }
                flag = isPrimaryBonus && perk.PrimaryRole == SkillEffect.PerkRole.ClanLeader;
                flag2 = !isPrimaryBonus && perk.SecondaryRole == SkillEffect.PerkRole.ClanLeader;
                if ((flag || flag2) && hero.Clan.Leader != null && hero.Clan.Leader.GetPerkValue(perk))
                {
                    if (flag)
                    {
                        AddToStat(ref stat, perk.PrimaryIncrementType, perk.PrimaryBonus, ____textLeader);
                    }
                    else
                    {
                        AddToStat(ref stat, perk.SecondaryIncrementType, perk.SecondaryBonus, ____textLeader);
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
                            AddToStat(ref stat, flag ? perk.PrimaryIncrementType : perk.SecondaryIncrementType, flag ? perk.PrimaryBonus : perk.SecondaryBonus, ____textLeader);
                        }
                    }
                    else
                    {
                        foreach (TroopRosterElement item in party.MemberRoster.GetTroopRoster())
                        {
                            if (item.Character.IsHero && item.Character.GetPerkValue(perk))
                            {
                                AddToStat(ref stat, flag ? perk.PrimaryIncrementType : perk.SecondaryIncrementType, flag ? perk.PrimaryBonus : perk.SecondaryBonus, ____textLeader);
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
                            AddToStat(ref stat, perk.PrimaryIncrementType, perk.PrimaryBonus, ____textLeader);
                        }
                        else
                        {
                            AddToStat(ref stat, perk.SecondaryIncrementType, perk.SecondaryBonus, ____textLeader);
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
                            AddToStat(ref stat, perk.PrimaryIncrementType, perk.PrimaryBonus, ____textScout);
                        }
                        else
                        {
                            AddToStat(ref stat, perk.SecondaryIncrementType, perk.SecondaryBonus, ____textScout);
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
                            AddToStat(ref stat, perk.PrimaryIncrementType, perk.PrimaryBonus, ____textSurgeon);
                        }
                        else
                        {
                            AddToStat(ref stat, perk.SecondaryIncrementType, perk.SecondaryBonus, ____textSurgeon);
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
                        AddToStat(ref stat, perk.PrimaryIncrementType, perk.PrimaryBonus, ____textQuartermaster);
                    }
                    else
                    {
                        AddToStat(ref stat, perk.SecondaryIncrementType, perk.SecondaryBonus, ____textQuartermaster);
                    }
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(SandboxAgentStatCalculateModel))]
        class OverrideGetEffectiveMaxHealth
        {
            [HarmonyPrefix]
            [HarmonyPatch("GetEffectiveMaxHealth")]
            static bool PrefixGetEffectiveMaxHealth(ref SandboxAgentStatCalculateModel __instance, ref float __result, Agent agent)
            {
                float baseHealthLimit = agent.BaseHealthLimit;
                ExplainedNumber stat = new ExplainedNumber(baseHealthLimit);
                if (agent.IsHuman && !agent.IsHero)
                {
                    CharacterObject characterObject = agent.Character as CharacterObject;
                    IAgentOriginBase agentOriginBase = agent?.Origin;
                    MobileParty mobileParty = ((PartyBase)(agentOriginBase?.BattleCombatant))?.MobileParty;
                    CharacterObject partyLeader = mobileParty?.LeaderHero?.CharacterObject;
                    CharacterObject captain = agent?.Formation?.Captain?.Character as CharacterObject;
                    if (characterObject != null && captain != null)
                    {
                        if (captain.GetPerkValue(DefaultPerks.TwoHanded.ThickHides))
                        {
                            PerkHelper.AddPerkBonusForParty(DefaultPerks.TwoHanded.ThickHides, mobileParty, isPrimaryBonus: false, ref stat);
                        }
                        if (captain.GetPerkValue(DefaultPerks.Polearm.GenerousRations))
                        {
                            PerkHelper.AddPerkBonusForParty(DefaultPerks.Polearm.GenerousRations, mobileParty, isPrimaryBonus: true, ref stat);
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
                            int num = MathF.Max(__instance.GetEffectiveSkill(captain, agentOriginBase, null, DefaultSkills.Medicine) - 200, 0) / 10;
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
        class OverrideCharacterObject
        {
            [HarmonyPrefix]
            [HarmonyPatch("GetPowerImp")]
            static bool PrefixGetPowerImp(ref float __result, int tier, bool isHero = false, bool isMounted = false)
            {
                //__result = (float)((2 + tier) * (8 + tier)) * 0.02f * (isHero ? 1.5f : (isMounted ? 1.2f : 1f));
                var modifiedTier = tier * 3f;
                __result = (float)((2f + modifiedTier) * (8f + modifiedTier)) * 0.02f * (isHero ? 1.5f : (isMounted ? 1.5f : 1f));
                return false;
            }
        }

        [HarmonyPatch(typeof(DefaultMilitaryPowerModel))]
        public class OverrideDefaultMilitaryPowerModel
        {
            private static PowerCalculationContext DetermineContext(MapEvent.BattleTypes battleType, BattleSideEnum battleSideEnum, bool isSimulation)
            {
                PowerCalculationContext result = PowerCalculationContext.Default;
                switch (battleType)
                {
                    case MapEvent.BattleTypes.FieldBattle:
                        result = (isSimulation ? PowerCalculationContext.FieldBattleSimulation : PowerCalculationContext.FieldBattle);
                        break;
                    case MapEvent.BattleTypes.Raid:
                    case MapEvent.BattleTypes.IsForcingVolunteers:
                    case MapEvent.BattleTypes.IsForcingSupplies:
                        result = ((battleSideEnum != BattleSideEnum.Attacker) ? (isSimulation ? PowerCalculationContext.RaidSimulationAsDefender : PowerCalculationContext.RaidAsDefender) : (isSimulation ? PowerCalculationContext.RaidSimulationAsAttacker : PowerCalculationContext.RaidAsAttacker));
                        break;
                    case MapEvent.BattleTypes.Siege:
                    case MapEvent.BattleTypes.SallyOut:
                    case MapEvent.BattleTypes.SiegeOutside:
                        result = ((battleSideEnum != BattleSideEnum.Attacker) ? (isSimulation ? PowerCalculationContext.SiegeSimulationAsDefender : PowerCalculationContext.SiegeAsDefender) : (isSimulation ? PowerCalculationContext.SiegeSimulationAsAttacker : PowerCalculationContext.SiegeAsAttacker));
                        break;
                    case MapEvent.BattleTypes.Hideout:
                        result = PowerCalculationContext.Hideout;
                        break;
                }
                return result;
            }

            [HarmonyPrefix]
            [HarmonyPatch("GetTroopPowerBasedOnContext")]
            static bool PrefixGetTroopPowerBasedOnContext(ref float __result, CharacterObject troop, MapEvent.BattleTypes battleType = MapEvent.BattleTypes.None, BattleSideEnum battleSideEnum = BattleSideEnum.None, bool isSimulation = false)
            {
                PowerCalculationContext context = DetermineContext(battleType, battleSideEnum, isSimulation);

                int tier = (troop.IsHero ? (troop.HeroObject.Level / 4 + 1) : troop.Tier);
                var modifiedTier = tier * 3f;
                if ((uint)(context - 6) <= 1u)
                {
                    __result = (float)((2f + modifiedTier) * (8f + modifiedTier)) * 0.02f * (troop.IsHero ? 1.5f : 1f);
                    return false;
                }
                __result = (float)((2f + modifiedTier) * (8f + modifiedTier)) * 0.02f * (troop.IsHero ? 1.5f : (troop.IsMounted ? 1.5f : 1f));
                return false;
            }

            public static float GetTroopPowerBasedOnContextForXP(CharacterObject troop, MapEvent.BattleTypes battleType = MapEvent.BattleTypes.None, BattleSideEnum battleSideEnum = BattleSideEnum.None, bool isSimulation = false)
            {
                PowerCalculationContext context = DetermineContext(battleType, battleSideEnum, isSimulation);

                int tier = (troop.IsHero ? (troop.HeroObject.Level / 4 + 1) : troop.Tier);
                var modifiedTier = tier * 1f;
                if ((uint)(context - 6) <= 1u)
                {
                    return (float)((2f + modifiedTier) * (8f + modifiedTier)) * 0.02f * (troop.IsHero ? 1.5f : 1f);
                }
                return (float)((2f + modifiedTier) * (8f + modifiedTier)) * 0.02f * (troop.IsHero ? 1.5f : (troop.IsMounted ? 1.5f : 1f));
            }
        }
    }
}
