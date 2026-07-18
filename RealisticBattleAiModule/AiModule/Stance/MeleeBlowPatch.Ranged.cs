using HarmonyLib;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.TournamentGames;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static RBMAI.PostureDamage;
using static TaleWorlds.Core.ArmorComponent;
using static TaleWorlds.Core.ItemObject;
using static TaleWorlds.MountAndBlade.Agent;
namespace RBMAI
{
    public partial class StanceLogic : MissionLogic
    {
        private partial class CreateMeleeBlowPatch
        {
            public static float calculateRangedPostureLoss(float fixedPS, float dynamicPS, Agent shooterAgent, WeaponClass wc)
            {
                SkillObject attackerWeaponSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(wc);

                float attackerEffectiveWeaponSkill = 0;
                float attackerEffectiveStrengthSkill = 0;

                float fixedPostureLoss = fixedPS;
                float dynamicPostureLoss = dynamicPS;

                if (attackerWeaponSkill != null)
                {
                    attackerEffectiveWeaponSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(shooterAgent, attackerWeaponSkill);
                }
                if (shooterAgent.HasMount)
                {
                    attackerEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(shooterAgent, DefaultSkills.Riding);
                }
                else
                {
                    attackerEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(shooterAgent, DefaultSkills.Athletics);
                }

                dynamicPostureLoss -= Math.Max(0f, 1f - (attackerEffectiveWeaponSkill / 200f)) * (dynamicPS * 0.5f);
                dynamicPostureLoss -= Math.Max(0f, 1f - (attackerEffectiveStrengthSkill / 200f)) * (dynamicPS * 0.5f);

                return fixedPostureLoss + dynamicPostureLoss;
            }

            private static float calculateShootMissileStaminaLoss(Agent agent, WeaponClass wc)
            {
                SkillObject attackerWeaponSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(wc);
                int attackerEffectiveWeaponSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(agent, attackerWeaponSkill);

                //base stamina loss
                float result = 50f;

                switch (wc)
                {
                    case WeaponClass.Bow:
                        {
                            int weaponDifficulty = agent.WieldedWeapon.Item.Difficulty;
                            int skillDifference = Math.Max(0, attackerEffectiveWeaponSkill - weaponDifficulty);
                            float skillModifier = skillDifference * 0.5f;
                            result = Math.Max(20f, 70f - skillModifier);
                            break;
                        }
                    case WeaponClass.Crossbow:
                        {
                            int weaponDifficulty = agent.WieldedWeapon.Item.Difficulty;
                            int skillDifference = Math.Max(0, attackerEffectiveWeaponSkill - weaponDifficulty);
                            float skillModifier = skillDifference * 0.5f;
                            result = Math.Max(20f, 70f - skillModifier);
                            break;
                        }
                    case WeaponClass.Javelin:
                    case WeaponClass.ThrowingAxe:
                    case WeaponClass.ThrowingKnife:
                        {
                            float skillModifier = attackerEffectiveWeaponSkill * 0.5f;
                            result = Math.Max(50f, 100f - skillModifier);
                            break;
                        }
                }

                return result;
            }

            [HarmonyPatch(typeof(Mission))]
            [HarmonyPatch("OnAgentShootMissile")]
            [UsedImplicitly]
            [MBCallback]
            private class OverrideOnAgentShootMissile
            {
                private static void Postfix(ref Agent shooterAgent, EquipmentIndex weaponIndex, Vec3 position, Vec3 velocity, Mat3 orientation, bool hasRigidBody, bool isPrimaryWeaponShot, int forcedMissileIndex, Mission __instance)
                {
                    if (RBMConfig.RBMConfig.postureEnabled)
                    {
                        MissionWeapon missionWeapon = shooterAgent.Equipment[weaponIndex];
                        WeaponClass wc = missionWeapon.CurrentUsageItem.WeaponClass;
                        Stance shooterPosture = null;
                        AgentStances.values.TryGetValue(shooterAgent, out shooterPosture);
                        if (shooterPosture != null)
                        {
                            float currentTime = Mission.Current.CurrentTime;
                            float postureLoss = 0f;
                            switch (wc)
                            {
                                case WeaponClass.Bow:
                                    {
                                        postureLoss = calculateRangedPostureLoss(35f, 25f, shooterAgent, wc);
                                        break;
                                    }
                                case WeaponClass.Crossbow:
                                    {
                                        postureLoss = calculateRangedPostureLoss(5f, 5f, shooterAgent, wc);
                                        break;
                                    }
                                case WeaponClass.Javelin:
                                    {
                                        postureLoss = calculateRangedPostureLoss(25f, 25f, shooterAgent, wc);
                                        break;
                                    }
                                case WeaponClass.ThrowingAxe:
                                case WeaponClass.ThrowingKnife:
                                    {
                                        postureLoss = calculateRangedPostureLoss(25f, 25f, shooterAgent, wc);
                                        break;
                                    }
                            }

                            if (shooterPosture.posture - postureLoss <= 0f)
                            {
                                shooterPosture.posture = 0f;
                                float postureResetModifier = 0.5f;
                                ResetPostureForAgent(ref shooterPosture, postureResetModifier);
                            }
                            else
                            {
                                shooterPosture.posture -= postureLoss;
                            }

                            if (RBMConfig.RBMConfig.staminaEnabled)
                            {
                                float staminaLoss = calculateShootMissileStaminaLoss(shooterAgent, wc);
                                shooterPosture.reduceStamina(staminaLoss);
                            }
                        }
                    }
                }
            }

        }
    }
}
