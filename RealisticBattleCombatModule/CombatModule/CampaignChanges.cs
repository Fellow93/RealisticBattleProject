using HarmonyLib;
using Helpers;
using StoryMode.GameComponents;
using StoryMode.Missions;
using System;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.CampaignSystem.ComponentInterfaces.CombatXpModel;

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
                    float attackerLevel = attackerTroop.Level;
                    float attackedLevel = attackedTroop.Level;

                    float levelDiffModifier = Math.Max(1f, 1f + ((attackedLevel / attackerLevel) / 10f));

                    int attackedTroopMaxHP = attackedTroop.MaxHitPoints();
                    float troopPower = 0f;
                    troopPower = ((party?.MapEvent == null) ? Campaign.Current.Models.MilitaryPowerModel.GetTroopPowerBasedOnContext(attackerTroop) : Campaign.Current.Models.MilitaryPowerModel.GetTroopPowerBasedOnContext(attackerTroop, party.MapEvent.EventType, party.Side, missionType == MissionTypeEnum.SimulationBattle));
                    float rawXpNum = 0;
                    //if (damage < 30)
                    //{
                        rawXpNum = 0.4f * ((troopPower + 0.5f) * (float)(30 + (isFatal ? 70 : 0)));
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
                    rawXpNum = rawXpNum * xpModifier * levelDiffModifier;
                    ExplainedNumber xpToGain = new ExplainedNumber(rawXpNum);
                    if (party != null)
                    {
                        if (party.IsMobile && party.MobileParty.LeaderHero != null)
                        {
                            MethodInfo method = typeof(DefaultCombatXpModel).GetMethod("GetBattleXpBonusFromPerks", BindingFlags.NonPublic | BindingFlags.Instance);
                            method.DeclaringType.GetMethod("GetBattleXpBonusFromPerks");
                            method.Invoke(__instance, new object[] { party, xpToGain, attackerTroop });
                        }
                        if (party.IsMobile && party.MobileParty.IsGarrison && party.MobileParty.CurrentSettlement?.Town.Governor != null)
                        {
                            PerkHelper.AddPerkBonusForTown(DefaultPerks.TwoHanded.ArrowDeflection, party.MobileParty.CurrentSettlement.Town, ref xpToGain);
                            if (attackerTroop.IsMounted)
                            {
                                PerkHelper.AddPerkBonusForTown(DefaultPerks.Polearm.Guards, party.MobileParty.CurrentSettlement.Town, ref xpToGain);
                            }
                        }
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
    }
}
