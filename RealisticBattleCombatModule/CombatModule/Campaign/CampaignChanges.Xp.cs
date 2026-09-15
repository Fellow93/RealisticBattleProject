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

        [HarmonyPatch(typeof(DefaultCombatXpModel))]
        private class GetXpFromHitPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("GetXpFromHit")]
            private static bool PrefixGetXpFromHit(ref ExplainedNumber __result, ref DefaultCombatXpModel __instance, CharacterObject attackerTroop, PartyBase attackerParty, CharacterObject captain, CharacterObject attackedTroop, int damage, bool isFatal, MissionTypeEnum missionType)
            {
                if (missionType == MissionTypeEnum.Battle || missionType == MissionTypeEnum.PracticeFight || missionType == MissionTypeEnum.Tournament || missionType == MissionTypeEnum.SimulationBattle)
                {
                    float victimTroopPower = 0f;
                    float attackerTroopPower = 0f;
                    if (missionType == MissionTypeEnum.SimulationBattle)
                    {
                        victimTroopPower = OverrideDefaultMilitaryPowerModel.GetTroopPowerBasedOnContextForXPVictim(attackedTroop, isSimulation: true);
                        attackerTroopPower = OverrideDefaultMilitaryPowerModel.GetTroopPowerBasedOnContextForXPAttacker(attackerTroop, isSimulation: true);
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
                    if (attackerParty != null)
                    {
                        MethodInfo method = typeof(DefaultCombatXpModel).GetMethod("GetBattleXpBonusFromPerks", BindingFlags.NonPublic | BindingFlags.Static);
                        method.DeclaringType.GetMethod("GetBattleXpBonusFromPerks");
                        method.Invoke(__instance, new object[] { attackerParty, xpToGain, attackerTroop });
                    }
                    if (captain != null && captain.IsHero && captain.GetPerkValue(DefaultPerks.Leadership.InspiringLeader))
                    {
                        xpToGain.AddFactor(DefaultPerks.Leadership.InspiringLeader.SecondaryBonus, DefaultPerks.Leadership.InspiringLeader.Name);
                    }
                    __result = xpToGain;
                    return false;
                }
                __result = new ExplainedNumber(0);
                return true;
            }
        }

        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("OnAgentShootMissile")]
        [UsedImplicitly]
        [MBCallback]
        private class OverrideOnAgentShootMissile
        {
            private static void Postfix(ref Agent shooterAgent, EquipmentIndex weaponIndex, Vec3 position, Vec3 velocity, Mat3 orientation, bool hasRigidBody, bool isPrimaryWeaponShot, int forcedMissileIndex, Mission __instance)
            {
                if (shooterAgent.IsHero && Campaign.Current != null)
                {
                    CharacterObject shooterCharacter = (CharacterObject)shooterAgent.Character;
                    WeaponClass wc = shooterAgent.Equipment[weaponIndex].CurrentUsageItem.WeaponClass;
                    SkillObject skillForWeapon = Campaign.Current.Models.CombatXpModel.GetSkillForWeapon(shooterAgent.Equipment[weaponIndex].CurrentUsageItem, false);
                    if (wc == WeaponClass.Bow)
                    {
                        shooterCharacter.HeroObject.AddSkillXp(skillForWeapon, 30f);
                        return;
                    }
                    if (wc == WeaponClass.Crossbow)
                    {
                        shooterCharacter.HeroObject.AddSkillXp(skillForWeapon, 60f);
                        return;
                    }
                    if (wc == WeaponClass.Javelin || wc == WeaponClass.ThrowingAxe || wc == WeaponClass.ThrowingKnife)
                    {
                        shooterCharacter.HeroObject.AddSkillXp(skillForWeapon, 50f);
                        return;
                    }
                    if (wc == WeaponClass.Sling)
                    {
                        // Slings use the Throwing skill; XP per shot matches bow (skill-intensive, high rate of fire).
                        shooterCharacter.HeroObject.AddSkillXp(skillForWeapon, 30f);
                        return;
                    }
                }
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
                            ExplainedNumber xpAmount = Campaign.Current.Models.CombatXpModel.GetXpFromHit(heroObject.CharacterObject, null, affectorCharacter, heroObject.PartyBelongedTo?.Party, (int)collisionData.InflictedDamage, false, CombatXpModel.MissionTypeEnum.Battle);
                            if (collisionData.CollisionResult == CombatCollisionResult.Blocked && collisionData.AttackBlockedWithShield)
                            {
                                experience = xpAmount.ResultNumber * 0.8f;
                            }
                            if (collisionData.CollisionResult == CombatCollisionResult.Parried || collisionData.CollisionResult == CombatCollisionResult.ChamberBlocked)
                            {
                                experience = xpAmount.ResultNumber * 1.2f;
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
    }
}
