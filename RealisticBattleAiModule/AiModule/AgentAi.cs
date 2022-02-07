using HarmonyLib;
using Helpers;
using JetBrains.Annotations;
using SandBox;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.MountAndBlade.ArrangementOrder;

namespace RealisticBattleAiModule
{
    class AgentAi
    {
        [HarmonyPatch(typeof(AgentStatCalculateModel))]
        [HarmonyPatch("SetAiRelatedProperties")]
        class OverrideSetAiRelatedProperties
        {
            static void Postfix(Agent agent, ref AgentDrivenProperties agentDrivenProperties, WeaponComponentData equippedItem, WeaponComponentData secondaryItem, AgentStatCalculateModel __instance)
            {
                bool agentHasShield = false;
                if(agent.GetWieldedItemIndex(Agent.HandIndex.OffHand) != EquipmentIndex.None)
                {
                    if(agent.Equipment[agent.GetWieldedItemIndex(Agent.HandIndex.OffHand)].CurrentUsageItem.WeaponClass == WeaponClass.SmallShield || 
                        agent.Equipment[agent.GetWieldedItemIndex(Agent.HandIndex.OffHand)].CurrentUsageItem.WeaponClass == WeaponClass.LargeShield)
                    {
                        agentHasShield = true;
                    }
                }

                MethodInfo method = typeof(AgentStatCalculateModel).GetMethod("GetMeleeSkill", BindingFlags.NonPublic | BindingFlags.Instance);
                method.DeclaringType.GetMethod("GetMeleeSkill");

                //int meleeSkill = Utilities.GetMeleeSkill(agent, equippedItem, secondaryItem);
                //int effectiveSkill = Utilities.GetEffectiveSkill(agent.Character, agent.Origin, agent.Formation, skill);

                SkillObject skill = (equippedItem == null) ? DefaultSkills.Athletics : equippedItem.RelevantSkill;
                int meleeSkill = (int)method.Invoke(__instance, new object[] { agent, equippedItem, secondaryItem });
                int effectiveSkill = __instance.GetEffectiveSkill(agent.Character, agent.Origin, agent.Formation, skill);
                float meleeLevel = Utilities.CalculateAILevel(agent, meleeSkill);                 //num
                float effectiveSkillLevel = Utilities.CalculateAILevel(agent, effectiveSkill);    //num2
                float meleeDefensivness = meleeLevel + agent.Defensiveness;             //num3
                
                if (XmlConfig.isRbmCombatModuleEnabled)
                {
                    agentDrivenProperties.AiChargeHorsebackTargetDistFactor = 7f;
                }
                else
                {
                    agentDrivenProperties.AiChargeHorsebackTargetDistFactor = 3.5f;
                }


                if (XmlConfig.dict["Global.PostureEnabled"] == 1)
                {
                    agentDrivenProperties.AIBlockOnDecideAbility = MBMath.ClampFloat(meleeLevel * 2f, 0.3f, 1f);// chance for directed blocking, always correct side
                    agentDrivenProperties.AIParryOnDecideAbility = MBMath.ClampFloat(meleeLevel, 0.1f, 0.6f);// chance for parry, can be wrong side
                }
                
                if (agentHasShield)
                {
                    if (XmlConfig.dict["Global.PostureEnabled"] == 1)
                    {
                        agentDrivenProperties.AIAttackOnDecideChance = MBMath.ClampFloat(meleeLevel * 0.3f, 0.1f, 0.15f);//MBMath.ClampFloat(0.23f * CalculateAIAttackOnDecideMaxValue() * (3f - agent.Defensiveness), 0.05f, 1f); //0.05-1f, 0.66-line, 0.44 - shield wall - aggressiveness / chance of attack instead of anything else / when set to 0 AI never attacks on its own
                        agentDrivenProperties.AIRealizeBlockingFromIncorrectSideAbility = MBMath.ClampFloat(meleeLevel * 0.3f, 0f, 0.2f);//chance to fix wrong side parry
                        agentDrivenProperties.AIDecideOnRealizeEnemyBlockingAttackAbility = MBMath.ClampFloat(meleeLevel * 0.46f, 0f, 0.35f);// chance to break own attack to do something else (LIKE CHANGING DIRECTION) - fainting
                        agentDrivenProperties.AIAttackOnParryChance = MBMath.ClampFloat(meleeLevel * 0.3f, 0.05f, 0.2f);//0.3f - 0.1f * agent.Defensiveness; //0.2-0.3f // chance to break own parry guard - 0 constant parry in reaction to enemy, 1 constant breaking of parry
                    }
                }
                else
                {
                    if (XmlConfig.dict["Global.PostureEnabled"] == 1)
                    {
                        agentDrivenProperties.AIAttackOnDecideChance = 0.15f;//MBMath.ClampFloat(0.23f * CalculateAIAttackOnDecideMaxValue() * (3f - agent.Defensiveness), 0.05f, 1f); //0.05-1f, 0.66-line, 0.44 - shield wall - aggressiveness / chance of attack instead of anything else / when set to 0 AI never attacks on its own
                        agentDrivenProperties.AIRealizeBlockingFromIncorrectSideAbility = MBMath.ClampFloat(meleeLevel * 0.8f, 0.05f, 0.5f);
                        agentDrivenProperties.AIDecideOnRealizeEnemyBlockingAttackAbility = MBMath.ClampFloat(meleeLevel * 0.46f, 0f, 0.35f);
                        agentDrivenProperties.AIAttackOnParryChance = MBMath.ClampFloat(meleeLevel * 0.45f, 0.2f, 0.4f); //0.3f - 0.1f * agent.Defensiveness; //0.2-0.3f // chance to break own parry guard - 0 constant parry in reaction to enemy, 1 constant breaking of parry
                    }
                }
                //agentDrivenProperties.AIDecideOnAttackChance = MBMath.ClampFloat(meleeLevel + 0.1f, 0f, 0.95f);

                if(agent.HasMount)
                {
                    if (XmlConfig.dict["Global.PostureEnabled"] == 1)
                    {
                        agentDrivenProperties.AIAttackOnDecideChance = 0.3f;
                    }

                }

                agentDrivenProperties.AiRangedHorsebackMissileRange = 0.25f; // percentage of maximum range is used, range of HA circle
                agentDrivenProperties.AiUseShieldAgainstEnemyMissileProbability = 0.95f;
                agentDrivenProperties.AiFlyingMissileCheckRadius = 250f;

                agentDrivenProperties.AiShooterError = 0.0001f;

                //agentDrivenProperties.AiRangerLeadErrorMin = 0f;
                //agentDrivenProperties.AiRangerLeadErrorMax = 0f;

                if(equippedItem != null && equippedItem.RelevantSkill == DefaultSkills.Bow)
                {
                    agentDrivenProperties.AiRangerVerticalErrorMultiplier = MBMath.ClampFloat(0.025f - effectiveSkill * 0.0001f, 0.01f, 0.025f);//bow
                    agentDrivenProperties.AiRangerHorizontalErrorMultiplier = MBMath.ClampFloat(0.025f - effectiveSkill * 0.0001f, 0.01f, 0.025f);//bow
                }
                else if(equippedItem != null && equippedItem.RelevantSkill == DefaultSkills.Crossbow)
                {
                    agentDrivenProperties.AiRangerVerticalErrorMultiplier = MBMath.ClampFloat(0.015f - effectiveSkill * 0.0001f, 0.005f, 0.015f);//crossbow
                    agentDrivenProperties.AiRangerHorizontalErrorMultiplier = MBMath.ClampFloat(0.015f - effectiveSkill * 0.0001f, 0.005f, 0.015f);//crossbow
                }
                else
                {
                    agentDrivenProperties.AiRangerVerticalErrorMultiplier = MBMath.ClampFloat(0.030f - effectiveSkill * 0.0001f, 0.01f, 0.030f);// javelins and axes etc
                    agentDrivenProperties.AiRangerHorizontalErrorMultiplier = MBMath.ClampFloat(0.030f - effectiveSkill * 0.0001f, 0.01f, 0.030f);// javelins and axes etc
                }

                if (XmlConfig.dict["Global.PostureEnabled"] == 1)
                {
                    agentDrivenProperties.AIDecideOnAttackChance = 0.5f;//MBMath.ClampFloat(meleeLevel*0.3f, 0.15f, 0.5f); //0.15f * agent.Defensiveness; //0-0.15f -esentailly ability to reconsider attack, how often is direction changed (or swtich to parry) when preparing for attack
                    agentDrivenProperties.AiDefendWithShieldDecisionChanceValue = 1f;//MBMath.ClampFloat(1f - (meleeLevel * 1f), 0.1f, 1.0f);//MBMath.ClampMin(1f, 0.2f + 0.5f * num + 0.2f * num3); 0.599-0.799 = 200 skill line/wall - chance for passive constant block, seems to trigger if you are prepared to attack AI for long enough
                }

                if (XmlConfig.dict["Global.PostureEnabled"] == 1)
                {
                    agentDrivenProperties.AiAttackCalculationMaxTimeFactor = meleeLevel; //how long does AI prepare for an attack
                    agentDrivenProperties.AiRaiseShieldDelayTimeBase = MBMath.ClampFloat(-0.25f + (meleeLevel * 0.6f), -0.25f, -0.05f); //MBMath.ClampFloat(-0.5f + (meleeLevel * 1.25f), -0.5f, 0f); //-0.75f + 0.5f * meleeLevel; delay between block decision and actual block for AI
                    agentDrivenProperties.AiAttackingShieldDefenseChance = 1f;//MBMath.ClampFloat(meleeLevel * 2f, 0.1f, 1.0f); ; //0.2f + 0.3f * meleeLevel;
                    agentDrivenProperties.AiAttackingShieldDefenseTimer = MBMath.ClampFloat(-0.3f + (meleeLevel * 0.6f), -0.3f, 0f);  //-0.3f + 0.3f * meleeLevel; Delay between deciding to swith from attack to defense
                }

                if (XmlConfig.dict["Global.PostureEnabled"] == 0)
                {
                    agentDrivenProperties.AIBlockOnDecideAbility = MBMath.ClampFloat(0.1f + meleeLevel * 0.6f, 0.2f, 0.45f); // chance for directed blocking
                    agentDrivenProperties.AIParryOnDecideAbility = MBMath.ClampFloat((meleeLevel * 0.30f) + 0.15f, 0.1f, 0.45f);
                    agentDrivenProperties.AIRealizeBlockingFromIncorrectSideAbility = MBMath.ClampFloat((meleeLevel * 0.3f) - 0.05f, 0.01f, 0.25f);
                    agentDrivenProperties.AIDecideOnAttackChance = MBMath.ClampFloat(meleeLevel + 0.1f, 0f, 0.95f);
                    agentDrivenProperties.AIDecideOnRealizeEnemyBlockingAttackAbility = MBMath.ClampFloat(meleeLevel + 0.1f, 0f, 0.95f);
                    agentDrivenProperties.AIAttackOnParryChance = MBMath.ClampFloat(meleeLevel * 0.4f, 0.1f, 0.30f); //0.3f - 0.1f * agent.Defensiveness; //0.2-0.3f // chance to break own parry guard - 0 constant parry in reaction to enemy, 1 constant breaking of parry
                    agentDrivenProperties.AIDecideOnAttackChance = MBMath.ClampFloat(meleeLevel * 0.3f, 0.15f, 0.5f); //0.15f * agent.Defensiveness; //0-0.15f - how often is direction changed (or swtich to parry) when preparing for attack
                    agentDrivenProperties.AIAttackOnDecideChance = 0.5f;//MBMath.ClampFloat(0.23f * CalculateAIAttackOnDecideMaxValue() * (3f - agent.Defensiveness), 0.05f, 1f); //0.05-1f, 0.66-line, 0.44 - shield wall - aggressiveness / chance of attack instead of anything else / when set to 0 AI never attacks on its own
                }

                agentDrivenProperties.AiShootFreq = MBMath.ClampFloat(effectiveSkill * 1.5f, 0.1f, 0.9f); // when set to 0 AI never shoots
                                                                                                          //agentDrivenProperties.AiWaitBeforeShootFactor = 0f;
                //agentDrivenProperties.AiMinimumDistanceToContinueFactor = 5f; //2f + 0.3f * (3f - meleeSkill);
                //agentDrivenProperties.AIHoldingReadyMaxDuration = 0.1f; //MBMath.Lerp(0.25f, 0f, MBMath.Min(1f, num * 1.2f));
                //agentDrivenProperties.AIHoldingReadyVariationPercentage = //num;

                //agentDrivenProperties.ReloadSpeed = 0.19f; //0.12 for heavy crossbows, 0.19f for light crossbows, composite bows and longbows.

                //                GetEffectiveSkill

                if(agent.Formation != null && agent.Formation.QuerySystem.IsInfantryFormation)
                {
                    agentDrivenProperties.ReloadMovementPenaltyFactor = 0.33f;
                }

                if (agent.IsRangedCached)
                {
                    //agent.SetScriptedCombatFlags(Agent.AISpecialCombatModeFlags.IgnoreAmmoLimitForRangeCalculation);
                    agent.SetScriptedCombatFlags(agent.GetScriptedCombatFlags() | Agent.AISpecialCombatModeFlags.IgnoreAmmoLimitForRangeCalculation);
                    //agent.ResetAiWaitBeforeShootFactor();
                }
            }
        }
    }

    [HarmonyPatch(typeof(SandboxAgentStatCalculateModel))]
    [HarmonyPatch("GetSkillEffectsOnAgent")]
    class GetSkillEffectsOnAgentPatch
    {
        static bool Prefix(ref SandboxAgentStatCalculateModel __instance, ref Agent agent,ref AgentDrivenProperties agentDrivenProperties, WeaponComponentData rightHandEquippedItem)
        {
            CharacterObject characterObject = agent.Character as CharacterObject;
            float swingSpeedMultiplier = agentDrivenProperties.SwingSpeedMultiplier;
            float thrustOrRangedReadySpeedMultiplier = agentDrivenProperties.ThrustOrRangedReadySpeedMultiplier;
            float reloadSpeed = agentDrivenProperties.ReloadSpeed;
            if (characterObject != null && rightHandEquippedItem != null)
            {
                int effectiveSkill = __instance.GetEffectiveSkill(characterObject, agent.Origin, agent.Formation, rightHandEquippedItem.RelevantSkill);
                ExplainedNumber stat = new ExplainedNumber(swingSpeedMultiplier);
                ExplainedNumber stat2 = new ExplainedNumber(thrustOrRangedReadySpeedMultiplier);
                ExplainedNumber stat3 = new ExplainedNumber(reloadSpeed);
                if (rightHandEquippedItem.RelevantSkill == DefaultSkills.OneHanded)
                {
                    if (effectiveSkill > 150)
                    {
                        effectiveSkill = 150;
                    }
                    SkillHelper.AddSkillBonusForCharacter(DefaultSkills.OneHanded, DefaultSkillEffects.OneHandedSpeed, characterObject, ref stat, effectiveSkill);
                    SkillHelper.AddSkillBonusForCharacter(DefaultSkills.OneHanded, DefaultSkillEffects.OneHandedSpeed, characterObject, ref stat2, effectiveSkill);
                }
                else if (rightHandEquippedItem.RelevantSkill == DefaultSkills.TwoHanded)
                {
                    if (effectiveSkill > 150)
                    {
                        effectiveSkill = 150;
                    }
                    SkillHelper.AddSkillBonusForCharacter(DefaultSkills.TwoHanded, DefaultSkillEffects.TwoHandedSpeed, characterObject, ref stat, effectiveSkill);
                    SkillHelper.AddSkillBonusForCharacter(DefaultSkills.TwoHanded, DefaultSkillEffects.TwoHandedSpeed, characterObject, ref stat2, effectiveSkill);
                }
                else if (rightHandEquippedItem.RelevantSkill == DefaultSkills.Polearm)
                {
                    if (effectiveSkill > 150)
                    {
                        effectiveSkill = 150;
                    }
                    SkillHelper.AddSkillBonusForCharacter(DefaultSkills.Polearm, DefaultSkillEffects.PolearmSpeed, characterObject, ref stat, effectiveSkill);
                    SkillHelper.AddSkillBonusForCharacter(DefaultSkills.Polearm, DefaultSkillEffects.PolearmSpeed, characterObject, ref stat2, effectiveSkill);
                }
                else if (rightHandEquippedItem.RelevantSkill == DefaultSkills.Crossbow)
                {
                    SkillHelper.AddSkillBonusForCharacter(DefaultSkills.Crossbow, DefaultSkillEffects.CrossbowReloadSpeed, characterObject, ref stat3, effectiveSkill);
                }
                else if (rightHandEquippedItem.RelevantSkill == DefaultSkills.Throwing)
                {
                    SkillHelper.AddSkillBonusForCharacter(DefaultSkills.Throwing, DefaultSkillEffects.ThrowingSpeed, characterObject, ref stat2, effectiveSkill);
                }
                if (agent.HasMount)
                {
                    int effectiveSkill2 = __instance.GetEffectiveSkill(characterObject, agent.Origin, agent.Formation, DefaultSkills.Riding);
                    float value = -0.01f * MathF.Max(0f, DefaultSkillEffects.HorseWeaponSpeedPenalty.GetPrimaryValue(effectiveSkill2));
                    stat.AddFactor(value);
                    stat2.AddFactor(value);
                    stat3.AddFactor(value);
                }
                agentDrivenProperties.SwingSpeedMultiplier = stat.ResultNumber;
                agentDrivenProperties.ThrustOrRangedReadySpeedMultiplier = stat2.ResultNumber;
                agentDrivenProperties.ReloadSpeed = stat3.ResultNumber;
            }

            return false;
        }
    }
  
    [HarmonyPatch(typeof(ArrangementOrder))]
    [HarmonyPatch("GetShieldDirectionOfUnit")]
    class HoldTheDoor
    {
        static void Postfix(ref Agent.UsageDirection __result, Formation formation, Agent unit, ArrangementOrderEnum orderEnum)
        {
            if (!formation.QuerySystem.IsCavalryFormation && !formation.QuerySystem.IsRangedCavalryFormation)
            {

                float currentTime = MBCommon.GetTotalMissionTime();
                if (currentTime - unit.LastRangedAttackTime < 7f)
                {
                    __result = Agent.UsageDirection.None;
                    return;
                }
                switch (orderEnum)
                {
                    case ArrangementOrderEnum.Line:
                    case ArrangementOrderEnum.Loose:
                        {
                            //float currentTime = MBCommon.TimeType.Mission.GetTime();
                            float lastMeleeAttackTime = unit.LastMeleeAttackTime;
                            float lastMeleeHitTime = unit.LastMeleeHitTime;
                            float lastRangedHit = unit.LastRangedHitTime;
                            if ((currentTime - lastMeleeAttackTime < 4f) || (currentTime - lastMeleeHitTime < 4f))
                            {
                                __result = Agent.UsageDirection.None;
                                return;
                            }
                            if (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle && (((currentTime - lastRangedHit < 2f) || formation.QuerySystem.UnderRangedAttackRatio >= 0.08f)))
                            {
                                __result = Agent.UsageDirection.DefendDown;
                                return;
                            }
                            //else
                            //{
                            //    __result = Agent.UsageDirection.None;
                            //}
                            break;
                        }
                }
            }
        }
    }

    [HarmonyPatch(typeof(HumanAIComponent))]
    [HarmonyPatch("OnTickAsAI")]
    class OnTickAsAIPatch
    {

        public static Dictionary<Agent, float> itemPickupDistanceStorage = new Dictionary<Agent, float> { };

        static void Postfix(ref SpawnedItemEntity ____itemToPickUp, ref Agent ___Agent)
        {
            if (____itemToPickUp != null && (___Agent.AIStateFlags & Agent.AIStateFlag.UseObjectMoving) != 0)
            {
                float num = MissionGameModels.Current.AgentStatCalculateModel.GetInteractionDistance(___Agent) * 3f;
                WorldFrame userFrameForAgent = ____itemToPickUp.GetUserFrameForAgent(___Agent);
                ref WorldPosition origin = ref userFrameForAgent.Origin;
                Vec3 targetPoint = ___Agent.Position;
                float distanceSq = origin.DistanceSquaredWithLimit(in targetPoint, num * num + 1E-05f);
                float newDist = -1f;
                itemPickupDistanceStorage.TryGetValue(___Agent, out newDist);
                if(newDist == 0f)
                {
                    itemPickupDistanceStorage[___Agent] = distanceSq;
                }
                else
                {
                    if(distanceSq == newDist)
                    {
                        ___Agent.StopUsingGameObject(isSuccessful: false);
                        itemPickupDistanceStorage.Remove(___Agent);

                    }
                    itemPickupDistanceStorage[___Agent] = distanceSq;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Mission))]
    [HarmonyPatch("OnAgentShootMissile")]
    [UsedImplicitly]
    [MBCallback]
    class OverrideOnAgentShootMissile
    {

        //private static int _oldMissileSpeed;
        static bool Prefix(Agent shooterAgent, EquipmentIndex weaponIndex, Vec3 position, ref Vec3 velocity, Mat3 orientation, bool hasRigidBody, bool isPrimaryWeaponShot, int forcedMissileIndex, Mission __instance)
        {
            MissionWeapon missionWeapon = shooterAgent.Equipment[weaponIndex];
            WeaponStatsData[] wsd = missionWeapon.GetWeaponStatsData();

            if (!XmlConfig.isRbmCombatModuleEnabled && (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle && !shooterAgent.IsMainAgent && (wsd[0].WeaponClass == (int)WeaponClass.Javelin || wsd[0].WeaponClass == (int)WeaponClass.ThrowingAxe)))
            {
                //float shooterSpeed = shooterAgent.MovementVelocity.Normalize();
                if (!shooterAgent.HasMount)
                {
                    velocity.z = velocity.z - 1.4f;
                }
                else
                {
                    velocity.z = velocity.z - 2f;

                }
            }

            return true;
        }
    }

}
