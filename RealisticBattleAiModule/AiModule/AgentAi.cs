using HarmonyLib;
using Helpers;
using JetBrains.Annotations;
using SandBox.GameComponents;
using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.MountAndBlade.ArrangementOrder;

namespace RBMAI
{
    public static class AgentAi
    {

        [HarmonyPatch(typeof(AgentStatCalculateModel))]
        [HarmonyPatch("SetAiRelatedProperties")]
        private class OverrideSetAiRelatedProperties
        {
            private static void Postfix(Agent agent, ref AgentDrivenProperties agentDrivenProperties, WeaponComponentData equippedItem, WeaponComponentData secondaryItem, AgentStatCalculateModel __instance)
            {
                bool agentHasShield = false;
                if (agent.GetWieldedItemIndex(Agent.HandIndex.OffHand) != EquipmentIndex.None)
                {
                    if (agent.Equipment[agent.GetWieldedItemIndex(Agent.HandIndex.OffHand)].CurrentUsageItem.WeaponClass == WeaponClass.SmallShield ||
                        agent.Equipment[agent.GetWieldedItemIndex(Agent.HandIndex.OffHand)].CurrentUsageItem.WeaponClass == WeaponClass.LargeShield)
                    {
                        agentHasShield = true;
                    }
                }

                MethodInfo method = typeof(AgentStatCalculateModel).GetMethod("GetMeleeSkill", BindingFlags.NonPublic | BindingFlags.Instance);
                method.DeclaringType.GetMethod("GetMeleeSkill");

                //int meleeSkill = RBMAI.Utilities.GetMeleeSkill(agent, equippedItem, secondaryItem);
                //int effectiveSkill = RBMAI.Utilities.GetEffectiveSkill(agent.Character, agent.Origin, agent.Formation, skill);

                SkillObject skill = (equippedItem == null) ? DefaultSkills.Athletics : equippedItem.RelevantSkill;
                int meleeSkill = (int)method.Invoke(__instance, new object[] { agent, equippedItem, secondaryItem });
                int effectiveSkill = __instance.GetEffectiveSkill(agent, skill);
                float meleeLevel = RBMAI.Utilities.CalculateAILevel(agent, meleeSkill);                 //num
                float effectiveSkillLevel = RBMAI.Utilities.CalculateAILevel(agent, effectiveSkill);    //num2
                float meleeDefensivness = meleeLevel + agent.Defensiveness;             //num3

                if (RBMConfig.RBMConfig.rbmCombatEnabled)
                {
                    agentDrivenProperties.AiChargeHorsebackTargetDistFactor = 7f;
                }
                else
                {
                    agentDrivenProperties.AiChargeHorsebackTargetDistFactor = 3.5f;
                }

                if (!RBMConfig.RBMConfig.vanillaCombatAi)
                {
                    if (RBMConfig.RBMConfig.postureEnabled)
                    {
                        agentDrivenProperties.AIBlockOnDecideAbility = MBMath.ClampFloat(meleeLevel * 2f, 0.3f, 1f);// chance for directed blocking
                        if (agentHasShield)
                        {
                            agentDrivenProperties.AIParryOnDecideAbility = MBMath.ClampFloat(meleeLevel * 0.5f, 0f, 0.6f);// chance for parry and perfect block, can be wrong side
                            agentDrivenProperties.AIAttackOnDecideChance = MBMath.ClampFloat(meleeLevel * 0.3f, 0.1f, 0.15f);//MBMath.ClampFloat(0.23f * CalculateAIAttackOnDecideMaxValue() * (3f - agent.Defensiveness), 0.05f, 1f); //0.05-1f, 0.66-line, 0.44 - shield wall - aggressiveness / chance of attack instead of anything else / when set to 0 AI never attacks on its own
                            agentDrivenProperties.AIRealizeBlockingFromIncorrectSideAbility = MBMath.ClampFloat(meleeLevel * 0.3f, 0f, 0.2f);//chance to fix wrong side parry
                            agentDrivenProperties.AIDecideOnRealizeEnemyBlockingAttackAbility = MBMath.ClampFloat(meleeLevel * 0.46f, 0f, 0.35f);// chance to break own attack to do something else (LIKE CHANGING DIRECTION) - fainting
                            agentDrivenProperties.AIAttackOnParryChance = MBMath.ClampFloat(meleeLevel * 0.3f, 0.05f, 0.2f);//0.3f - 0.1f * agent.Defensiveness; //0.2-0.3f // chance to break own parry guard - 0 constant parry in reaction to enemy, 1 constant breaking of parry
                        }
                        else
                        {
                            agentDrivenProperties.AIParryOnDecideAbility = MBMath.ClampFloat(meleeLevel, 0.1f, 0.6f);// chance for parry, can be wrong side
                            agentDrivenProperties.AIAttackOnDecideChance = 0.15f;//MBMath.ClampFloat(0.23f * CalculateAIAttackOnDecideMaxValue() * (3f - agent.Defensiveness), 0.05f, 1f); //0.05-1f, 0.66-line, 0.44 - shield wall - aggressiveness / chance of attack instead of anything else / when set to 0 AI never attacks on its own
                            agentDrivenProperties.AIRealizeBlockingFromIncorrectSideAbility = MBMath.ClampFloat(meleeLevel * 0.8f, 0.05f, 0.5f);
                            agentDrivenProperties.AIDecideOnRealizeEnemyBlockingAttackAbility = MBMath.ClampFloat(meleeLevel * 0.46f, 0f, 0.35f);
                            agentDrivenProperties.AIAttackOnParryChance = MBMath.ClampFloat(meleeLevel * 0.45f, 0.2f, 0.4f); //0.3f - 0.1f * agent.Defensiveness; //0.2-0.3f // chance to break own parry guard - 0 constant parry in reaction to enemy, 1 constant breaking of parry
                        }
                        if (agent.HasMount)
                        {
                            agentDrivenProperties.AIAttackOnDecideChance = 0.3f;
                        }

                        agentDrivenProperties.AIDecideOnAttackChance = 0.5f;//MBMath.ClampFloat(meleeLevel*0.3f, 0.15f, 0.5f); //0.15f * agent.Defensiveness; //0-0.15f -esentailly ability to reconsider attack, how often is direction changed (or swtich to parry) when preparing for attack
                        agentDrivenProperties.AiDefendWithShieldDecisionChanceValue = 1f;//MBMath.ClampFloat(1f - (meleeLevel * 1f), 0.1f, 1.0f);//MBMath.ClampMin(1f, 0.2f + 0.5f * num + 0.2f * num3); 0.599-0.799 = 200 skill line/wall - chance for passive constant block, seems to trigger if you are prepared to attack AI for long enough
                        agentDrivenProperties.AiAttackCalculationMaxTimeFactor = meleeLevel; //how long does AI prepare for an attack
                        agentDrivenProperties.AiRaiseShieldDelayTimeBase = MBMath.ClampFloat(-0.25f + (meleeLevel * 0.6f), -0.25f, -0.05f); //MBMath.ClampFloat(-0.5f + (meleeLevel * 1.25f), -0.5f, 0f); //-0.75f + 0.5f * meleeLevel; delay between block decision and actual block for AI
                        agentDrivenProperties.AiAttackingShieldDefenseChance = 1f;//MBMath.ClampFloat(meleeLevel * 2f, 0.1f, 1.0f); ; //0.2f + 0.3f * meleeLevel;
                        agentDrivenProperties.AiAttackingShieldDefenseTimer = MBMath.ClampFloat(-0.3f + (meleeLevel * 0.6f), -0.3f, 0f);  //-0.3f + 0.3f * meleeLevel; Delay between deciding to swith from attack to defense
                    }
                    else
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
                }
                if (RBMConfig.RBMConfig.rbmCombatEnabled)
                {
                    agentDrivenProperties.AiRangedHorsebackMissileRange = 0.35f; // percentage of maximum range is used, range of HA circle
                }
                else
                {
                    agentDrivenProperties.AiRangedHorsebackMissileRange = 0.235f; // percentage of maximum range is used, range of HA circle
                }
                agentDrivenProperties.AiUseShieldAgainstEnemyMissileProbability = 0.95f;
                //agentDrivenProperties.AiFlyingMissileCheckRadius = 250f;

                float num4 = 1f - effectiveSkillLevel;
                if (!agent.WieldedWeapon.IsEmpty && agent.WieldedWeapon.CurrentUsageItem.WeaponClass == WeaponClass.Crossbow)
                {
                    agentDrivenProperties.AiShooterError = 0.020f - (0.007f * effectiveSkillLevel);
                    agentDrivenProperties.WeaponMaxMovementAccuracyPenalty *= 0.33f;
                    agentDrivenProperties.WeaponBestAccuracyWaitTime = 0f;
                }
                else if (!agent.WieldedWeapon.IsEmpty && agent.WieldedWeapon.CurrentUsageItem.WeaponClass == WeaponClass.Bow)
                {
                    agentDrivenProperties.AiShooterError = 0.015f - (0.020f * effectiveSkillLevel);
                    agentDrivenProperties.WeaponMaxMovementAccuracyPenalty *= 0.33f;
                    agentDrivenProperties.WeaponBestAccuracyWaitTime = 1.5f;
                }
                else
                {
                    agentDrivenProperties.AiShooterError = 0.015f - (0.020f * effectiveSkillLevel);
                    agentDrivenProperties.WeaponMaxMovementAccuracyPenalty *= 0.1f;
                    agentDrivenProperties.WeaponBestAccuracyWaitTime = 0.1f;
                }
                if (RBMConfig.RBMConfig.postureEnabled)
                {
                    Posture posture = null;
                    AgentPostures.values.TryGetValue(agent, out posture);
                    if (agent != null && posture != null && posture.maxPostureLossCount >= 1)
                    {
                        agentDrivenProperties.WeaponInaccuracy += posture.maxPostureLossCount * ( agentDrivenProperties.WeaponInaccuracy * 0.3f);
                    }
                }

                //if (agent.IsAIControlled)
                //{
                //    agentDrivenProperties.WeaponMaxMovementAccuracyPenalty *= 0.33f;
                //    agentDrivenProperties.WeaponBestAccuracyWaitTime = 1.5f;
                //}

                agentDrivenProperties.AiRangerLeadErrorMin = (float)((0.0 - (double)num4) * 0.349999994039536) + 0.3f;
                agentDrivenProperties.AiRangerLeadErrorMax = num4 * 0.2f + 0.3f;

                if (equippedItem != null && equippedItem.RelevantSkill == DefaultSkills.Bow)
                {
                    if (agent.MountAgent != null)
                    {
                        //agentDrivenProperties.AiRangerVerticalErrorMultiplier = 0f;//horse archers
                        //agentDrivenProperties.AiRangerHorizontalErrorMultiplier = 0f;//horse archers
                        agentDrivenProperties.AiRangerVerticalErrorMultiplier = MBMath.ClampFloat(0.020f - effectiveSkill * 0.0001f, 0.01f, 0.020f);//bow
                        agentDrivenProperties.AiRangerHorizontalErrorMultiplier = MBMath.ClampFloat(0.020f - effectiveSkill * 0.0001f, 0.01f, 0.020f);//bow
                        agentDrivenProperties.WeaponMaxMovementAccuracyPenalty *= 0.33f;
                        agentDrivenProperties.WeaponMaxUnsteadyAccuracyPenalty *= 0.5f;
                        agentDrivenProperties.WeaponRotationalAccuracyPenaltyInRadians = 0.02f;
                    }
                    else
                    {
                        agentDrivenProperties.AiRangerVerticalErrorMultiplier = MBMath.ClampFloat(0.020f - effectiveSkill * 0.0001f, 0.01f, 0.020f);//bow
                        agentDrivenProperties.AiRangerHorizontalErrorMultiplier = MBMath.ClampFloat(0.020f - effectiveSkill * 0.0001f, 0.01f, 0.020f);//bow
                    }
                }
                else if (equippedItem != null && equippedItem.RelevantSkill == DefaultSkills.Crossbow)
                {
                    agentDrivenProperties.AiRangerVerticalErrorMultiplier = MBMath.ClampFloat(0.015f - effectiveSkill * 0.0001f, 0.005f, 0.015f);//crossbow
                    agentDrivenProperties.AiRangerHorizontalErrorMultiplier = MBMath.ClampFloat(0.010f - effectiveSkill * 0.0001f, 0.005f, 0.010f);//crossbow
                }
                else
                {
                    agentDrivenProperties.AiRangerVerticalErrorMultiplier = MBMath.ClampFloat(0.025f - effectiveSkill * 0.0001f, 0.01f, 0.025f);// javelins and axes etc
                    agentDrivenProperties.AiRangerHorizontalErrorMultiplier = MBMath.ClampFloat(0.025f - effectiveSkill * 0.0001f, 0.01f, 0.025f);// javelins and axes etc
                }

                agentDrivenProperties.AiShootFreq = MBMath.ClampFloat(effectiveSkillLevel * 1.5f, 0.1f, 0.9f); // when set to 0 AI never shoots
                                                                                                               //agentDrivenProperties.AiWaitBeforeShootFactor = 0f;
                                                                                                               //agentDrivenProperties.AiMinimumDistanceToContinueFactor = 5f; //2f + 0.3f * (3f - meleeSkill);
                                                                                                               //agentDrivenProperties.AIHoldingReadyMaxDuration = 0.1f; //MBMath.Lerp(0.25f, 0f, MBMath.Min(1f, num * 1.2f));
                                                                                                               //agentDrivenProperties.AIHoldingReadyVariationPercentage = //num;

                //agentDrivenProperties.ReloadSpeed = 0.19f; //0.12 for heavy crossbows, 0.19f for light crossbows, composite bows and longbows.

                //                GetEffectiveSkill

                if (agent.Formation != null && agent.Formation.QuerySystem.IsInfantryFormation && !agent.IsRangedCached)
                {
                    agentDrivenProperties.ReloadMovementPenaltyFactor = 0.1f;
                }

                if (agent.IsRangedCached)
                {
                    //agent.SetScriptedCombatFlags(Agent.AISpecialCombatModeFlags.IgnoreAmmoLimitForRangeCalculation);
                    agent.SetScriptedCombatFlags(agent.GetScriptedCombatFlags() | Agent.AISpecialCombatModeFlags.IgnoreAmmoLimitForRangeCalculation);
                    //agent.ResetAiWaitBeforeShootFactor();
                }
                agentDrivenProperties.SetStat(DrivenProperty.UseRealisticBlocking, 1f);
                //agentDrivenProperties.SetStat(DrivenProperty.UseRealisticBlocking, 0f);

                agentDrivenProperties.MissileSpeedMultiplier = 1f;
            }
        }

        [HarmonyPatch(typeof(SandboxAgentStatCalculateModel))]
        [HarmonyPatch("SetWeaponSkillEffectsOnAgent")]
        internal class SetWeaponSkillEffectsOnAgentPatch
        {
            private static bool Prefix(ref SandboxAgentStatCalculateModel __instance, ref Agent agent, ref AgentDrivenProperties agentDrivenProperties, WeaponComponentData equippedWeaponComponent)
            {
                CharacterObject characterObject = agent.Character as CharacterObject;
                float swingSpeedMultiplier = agentDrivenProperties.SwingSpeedMultiplier;
                float thrustOrRangedReadySpeedMultiplier = agentDrivenProperties.ThrustOrRangedReadySpeedMultiplier;
                float reloadSpeed = agentDrivenProperties.ReloadSpeed;
                if (characterObject != null && equippedWeaponComponent != null)
                {
                    int effectiveSkill = __instance.GetEffectiveSkill(agent, equippedWeaponComponent.RelevantSkill);
                    ExplainedNumber stat = new ExplainedNumber(swingSpeedMultiplier);
                    ExplainedNumber stat2 = new ExplainedNumber(thrustOrRangedReadySpeedMultiplier);
                    ExplainedNumber stat3 = new ExplainedNumber(reloadSpeed);
                    if (equippedWeaponComponent.RelevantSkill == DefaultSkills.OneHanded)
                    {
                        if (effectiveSkill > 150)
                        {
                            effectiveSkill = 150;
                        }
                        SkillHelper.AddSkillBonusForCharacter(DefaultSkills.OneHanded, DefaultSkillEffects.OneHandedSpeed, characterObject, ref stat, effectiveSkill);
                        SkillHelper.AddSkillBonusForCharacter(DefaultSkills.OneHanded, DefaultSkillEffects.OneHandedSpeed, characterObject, ref stat2, effectiveSkill);
                    }
                    else if (equippedWeaponComponent.RelevantSkill == DefaultSkills.TwoHanded)
                    {
                        if (effectiveSkill > 150)
                        {
                            effectiveSkill = 150;
                        }
                        SkillHelper.AddSkillBonusForCharacter(DefaultSkills.TwoHanded, DefaultSkillEffects.TwoHandedSpeed, characterObject, ref stat, effectiveSkill);
                        SkillHelper.AddSkillBonusForCharacter(DefaultSkills.TwoHanded, DefaultSkillEffects.TwoHandedSpeed, characterObject, ref stat2, effectiveSkill);
                    }
                    else if (equippedWeaponComponent.RelevantSkill == DefaultSkills.Polearm)
                    {
                        if (effectiveSkill > 150)
                        {
                            effectiveSkill = 150;
                        }
                        SkillHelper.AddSkillBonusForCharacter(DefaultSkills.Polearm, DefaultSkillEffects.PolearmSpeed, characterObject, ref stat, effectiveSkill);
                        SkillHelper.AddSkillBonusForCharacter(DefaultSkills.Polearm, DefaultSkillEffects.PolearmSpeed, characterObject, ref stat2, effectiveSkill);
                    }
                    else if (equippedWeaponComponent.RelevantSkill == DefaultSkills.Crossbow)
                    {
                        SkillHelper.AddSkillBonusForCharacter(DefaultSkills.Crossbow, DefaultSkillEffects.CrossbowReloadSpeed, characterObject, ref stat3, effectiveSkill);
                    }
                    else if (equippedWeaponComponent.RelevantSkill == DefaultSkills.Throwing)
                    {
                        SkillHelper.AddSkillBonusForCharacter(DefaultSkills.Throwing, DefaultSkillEffects.ThrowingSpeed, characterObject, ref stat2, effectiveSkill);
                    }
                    //if (agent.HasMount)
                    //{
                    //    int effectiveSkill2 = __instance.GetEffectiveSkill(agent, DefaultSkills.Riding);
                    //    float value = -0.01f * MathF.Max(0f, DefaultSkillEffects.MountedWeaponSpeedPenalty.GetPrimaryValue(effectiveSkill2));
                    //    stat.AddFactor(value);
                    //    stat2.AddFactor(value);
                    //    stat3.AddFactor(value);
                    //}
                    agentDrivenProperties.SwingSpeedMultiplier = stat.ResultNumber;
                    agentDrivenProperties.ThrustOrRangedReadySpeedMultiplier = stat2.ResultNumber;
                    agentDrivenProperties.ReloadSpeed = stat3.ResultNumber;
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(ArrangementOrder))]
        [HarmonyPatch("GetShieldDirectionOfUnit")]
        internal class HoldTheDoor
        {
            private static void Postfix(ref Agent.UsageDirection __result, Formation formation, Agent unit, ArrangementOrderEnum orderEnum)
            {
                //if (!formation.QuerySystem.IsCavalryFormation && !formation.QuerySystem.IsRangedCavalryFormation)
                //{
                //    if(Mission.Current != null)
                //    {
                //        float currentTime = Mission.Current.CurrentTime;
                //        if (currentTime - unit.LastRangedAttackTime < 7f)
                //        {
                //            __result = Agent.UsageDirection.None;
                //            return;
                //        }
                //        switch (orderEnum)
                //        {
                //            case ArrangementOrderEnum.Line:
                //            case ArrangementOrderEnum.Loose:
                //                {
                //                    float lastMeleeAttackTime = unit.LastMeleeAttackTime;
                //                    float lastMeleeHitTime = unit.LastMeleeHitTime;
                //                    float lastRangedHit = unit.LastRangedHitTime;
                //                    if ((currentTime - lastMeleeAttackTime < 4f) || (currentTime - lastMeleeHitTime < 4f))
                //                    {
                //                        __result = Agent.UsageDirection.None;
                //                        return;
                //                    }
                //                    if (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle && formation.QuerySystem.IsInfantryFormation && (((currentTime - lastRangedHit < 2f) || formation.QuerySystem.UnderRangedAttackRatio >= 0.08f)))
                //                    {
                //                        __result = Agent.UsageDirection.DefendDown;
                //                        return;
                //                    }
                //                    break;
                //                }
                //        }
                //    }
                //}
                if (unit.IsDetachedFromFormation)
                {
                    __result = Agent.UsageDirection.None;
                    return;
                }
                bool test = true;
                switch (orderEnum)
                {
                    case ArrangementOrderEnum.ShieldWall:
                        if (unit.Formation.FiringOrder.OrderEnum != FiringOrder.RangedWeaponUsageOrderEnum.HoldYourFire)
                        {
                            bool hasRanged = unit.Equipment.HasAnyWeaponWithFlags(WeaponFlags.HasString);
                            bool hasTwoHanded = unit.Equipment.HasAnyWeaponWithFlags(WeaponFlags.NotUsableWithOneHand);
                            if (hasRanged || hasTwoHanded)
                            {
                                test = false;
                            }
                        }
                        if (test)
                        {
                            if (((IFormationUnit)unit).FormationRankIndex == 0)
                            {
                                __result = Agent.UsageDirection.DefendDown;
                                return;
                            }
                            if (formation.Arrangement.GetNeighborUnitOfLeftSide(unit) == null)
                            {
                                __result = Agent.UsageDirection.DefendLeft;
                                return;
                            }
                            if (formation.Arrangement.GetNeighborUnitOfRightSide(unit) == null)
                            {
                                __result = Agent.UsageDirection.DefendRight;
                                return;
                            }
                            __result = Agent.UsageDirection.AttackEnd;
                            return;
                        }
                        __result = Agent.UsageDirection.None;
                        return;

                    case ArrangementOrderEnum.Circle:
                    case ArrangementOrderEnum.Square:
                        if (unit.Formation.FiringOrder.OrderEnum != FiringOrder.RangedWeaponUsageOrderEnum.HoldYourFire)
                        {
                            bool hasRanged = unit.Equipment.HasAnyWeaponWithFlags(WeaponFlags.HasString);
                            bool hasTwoHanded = unit.Equipment.HasAnyWeaponWithFlags(WeaponFlags.NotUsableWithOneHand);
                            if (hasRanged || hasTwoHanded)
                            {
                                test = false;
                            }
                        }
                        if (test)
                        {
                            if (((IFormationUnit)unit).FormationRankIndex == 0)
                            {
                                __result = Agent.UsageDirection.DefendDown;
                                return;
                            }
                            __result = Agent.UsageDirection.AttackEnd;
                            return;
                        }
                        __result = Agent.UsageDirection.None;
                        return;

                    default:
                        __result = Agent.UsageDirection.None;
                        return;
                }
            }
        }

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("UpdateLastAttackAndHitTimes")]
        internal class UpdateLastAttackAndHitTimesFix
        {
            private static bool Prefix(ref Agent __instance, Agent attackerAgent, bool isMissile)
            {
                PropertyInfo LastRangedHitTime = typeof(Agent).GetProperty("LastRangedHitTime");
                LastRangedHitTime.DeclaringType.GetProperty("LastRangedHitTime");

                PropertyInfo LastRangedAttackTime = typeof(Agent).GetProperty("LastRangedAttackTime");
                LastRangedAttackTime.DeclaringType.GetProperty("LastRangedAttackTime");

                PropertyInfo LastMeleeHitTime = typeof(Agent).GetProperty("LastMeleeHitTime");
                LastMeleeHitTime.DeclaringType.GetProperty("LastMeleeHitTime");

                PropertyInfo LastMeleeAttackTime = typeof(Agent).GetProperty("LastMeleeAttackTime");
                LastMeleeAttackTime.DeclaringType.GetProperty("LastMeleeAttackTime");

                float currentTime = MBCommon.GetTotalMissionTime();
                if (isMissile)
                {
                    //__instance.LastRangedHitTime = currentTime;
                    LastRangedHitTime.SetValue(__instance, currentTime, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                }
                else
                {
                    //LastMeleeHitTime = currentTime;
                    LastMeleeHitTime.SetValue(__instance, currentTime, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                }
                if (attackerAgent != __instance && attackerAgent != null)
                {
                    if (isMissile)
                    {
                        //attackerAgent.LastRangedAttackTime = currentTime;
                        LastRangedAttackTime.SetValue(attackerAgent, currentTime, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                    }
                    else
                    {
                        //attackerAgent.LastMeleeAttackTime = currentTime;
                        LastMeleeAttackTime.SetValue(attackerAgent, currentTime, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                    }
                }

                if (!__instance.IsHuman)
                {
                    if (__instance.RiderAgent != null)
                    {
                        if (isMissile)
                        {
                            //__instance.LastRangedHitTime = currentTime;
                            LastRangedHitTime.SetValue(__instance.RiderAgent, currentTime, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                        }
                        else
                        {
                            //LastMeleeHitTime = currentTime;
                            LastMeleeHitTime.SetValue(__instance.RiderAgent, currentTime, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                        }
                    }
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(HumanAIComponent))]
        [HarmonyPatch("OnTickAsAI")]
        public static class OnTickAsAIPatch
        {
            public static Dictionary<Agent, float> itemPickupDistanceStorage = new Dictionary<Agent, float> { };

            private static bool Prefix(ref HumanAIComponent __instance, ref SpawnedItemEntity ____itemToPickUp, ref Agent ___Agent, ref MissionTimer ____itemPickUpTickTimer, ref bool ____disablePickUpForAgent, ref GameEntity[] ____tempPickableEntities, ref UIntPtr[] ____pickableItemsId)
            {
                bool timer = ____itemPickUpTickTimer.Check(reset: true);
                bool mended = ___Agent.Mission.MissionEnded;
                bool hasWeapon = false;

                for (int i = 0; i < 5; i++)
                {
                    WeaponComponentData currentUsageItem = ___Agent.Equipment[i].CurrentUsageItem;
                    if (currentUsageItem != null && currentUsageItem.WeaponFlags.HasAnyFlag(WeaponFlags.MeleeWeapon))
                    {
                        hasWeapon = true;
                    }
                }

                if (timer && !mended && !hasWeapon)
                {
                    EquipmentIndex wieldedItemIndex = ___Agent.GetWieldedItemIndex(Agent.HandIndex.MainHand);
                    bool flag = ((wieldedItemIndex == EquipmentIndex.None) ? null : ___Agent.Equipment[wieldedItemIndex].CurrentUsageItem)?.IsRangedWeapon ?? false;
                    if ( ___Agent.CanBeAssignedForScriptedMovement() && ___Agent.CurrentWatchState == Agent.WatchState.Alarmed && (___Agent.GetAgentFlags() & AgentFlag.CanAttack) != 0)
                    {
                        Agent targetAgent = ___Agent.GetTargetAgent();
                        float maximumForwardUnlimitedSpeed = ___Agent.MaximumForwardUnlimitedSpeed;
                        if (____itemToPickUp == null)
                        {
                            Vec3 bMin = ___Agent.Position - new Vec3(50f, 50f, 1f);
                            Vec3 bMax = ___Agent.Position + new Vec3(50f, 50f, 1.8f);

                            Vec3 v = ((targetAgent == null) ? Vec3.Invalid : (targetAgent.Position - ___Agent.Position));
                            int num = ___Agent.Mission.Scene.SelectEntitiesInBoxWithScriptComponent<SpawnedItemEntity>(ref bMin, ref bMax, ____tempPickableEntities, ____pickableItemsId);
                            float num2 = -1f;
                            SpawnedItemEntity result = null;
                            for (int i = 0; i < num; i++)
                            {
                                SpawnedItemEntity firstScriptOfType = ____tempPickableEntities[i].GetFirstScriptOfType<SpawnedItemEntity>();
                                bool flag2 = false;
                                if (firstScriptOfType != null)
                                {
                                    MissionWeapon weaponCopy = firstScriptOfType.WeaponCopy;
                                    flag2 = !weaponCopy.IsEmpty && (!weaponCopy.IsShield() && !weaponCopy.IsBanner() && !firstScriptOfType.IsStuckMissile() && !firstScriptOfType.IsQuiverAndNotEmpty());
                                }
                                if (!flag2 || firstScriptOfType.HasUser || (firstScriptOfType.HasAIMovingTo && !firstScriptOfType.IsAIMovingTo(___Agent)) || !(firstScriptOfType.GameEntityWithWorldPosition.WorldPosition.GetNavMesh() != UIntPtr.Zero))
                                {
                                    continue;
                                }
                                Vec3 v2 = firstScriptOfType.GetUserFrameForAgent(___Agent).Origin.GetGroundVec3() - ___Agent.Position;
                                v2.Normalize();
                                EquipmentIndex equipmentIndex = MissionEquipment.SelectWeaponPickUpSlot(___Agent, firstScriptOfType.WeaponCopy, firstScriptOfType.IsStuckMissile());
                                WorldPosition worldPosition = firstScriptOfType.GameEntityWithWorldPosition.WorldPosition;
                                if (equipmentIndex != EquipmentIndex.None && worldPosition.GetNavMesh() != UIntPtr.Zero && ___Agent.Equipment[equipmentIndex].IsEmpty && ___Agent.CanMoveDirectlyToPosition(worldPosition.AsVec2))
                                {
                                    float itemScoreForAgent = MissionGameModels.Current.ItemPickupModel.GetItemScoreForAgent(firstScriptOfType, ___Agent);
                                    if (itemScoreForAgent > num2)
                                    {
                                        result = firstScriptOfType;
                                        num2 = itemScoreForAgent;
                                    }
                                }
                            }
                            ____itemToPickUp = result;
                            if (____itemToPickUp != null)
                            {
                                ____itemToPickUp.MovingAgent?.StopUsingGameObject(isSuccessful: false);
                                __instance.MoveToUsableGameObject(result, null);
                            }
                        }
                    }
                }
                return true;
            }

            private static void Postfix(ref SpawnedItemEntity ____itemToPickUp, ref Agent ___Agent)
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
                    if (newDist == 0f)
                    {
                        itemPickupDistanceStorage[___Agent] = distanceSq;
                    }
                    else
                    {
                        if (distanceSq == newDist)
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
        internal class OverrideOnAgentShootMissile
        {
            //private static int _oldMissileSpeed;
            private static bool Prefix(Agent shooterAgent, EquipmentIndex weaponIndex, Vec3 position, ref Vec3 velocity, Mat3 orientation, bool hasRigidBody, bool isPrimaryWeaponShot, int forcedMissileIndex, Mission __instance)
            {
                MissionWeapon missionWeapon = shooterAgent.Equipment[weaponIndex];
                WeaponStatsData[] wsd = missionWeapon.GetWeaponStatsData();

                if (!RBMConfig.RBMConfig.rbmCombatEnabled && (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle && !shooterAgent.IsMainAgent && (wsd[0].WeaponClass == (int)WeaponClass.Javelin || wsd[0].WeaponClass == (int)WeaponClass.ThrowingAxe)))
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

        [HarmonyPatch(typeof(Formation))]
        [HarmonyPatch("ApplyActionOnEachUnit", new Type[] { typeof(Action<Agent>), typeof(Agent) })]
        internal class ApplyActionOnEachUnitPatch
        {
            private static bool Prefix(ref Action<Agent> action, ref Agent ignoreAgent, ref Formation __instance)
            {
                try
                {
                    __instance.ApplyActionOnEachUnitViaBackupList(action);
                    return false;
                }
                catch (Exception e)
                {
                    {
                        return true;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(CustomBattleApplyWeatherEffectsModel))]
        [HarmonyPatch("ApplyWeatherEffects")]
        public class OverrideApplyWeatherEffectsCustomBattle
        {
            private static bool Prefix()
            {
                Scene scene = Mission.Current.Scene;
                if (scene != null)
                {
                    Mission.Current.SetBowMissileSpeedModifier(1f);
                    Mission.Current.SetCrossbowMissileSpeedModifier(1f);
                    Mission.Current.SetMissileRangeModifier(1f);
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(SandboxApplyWeatherEffectsModel))]
        [HarmonyPatch("ApplyWeatherEffects")]
        public class OverrideApplyWeatherEffectsSandbox
        {
            private static bool Prefix()
            {
                Scene scene = Mission.Current.Scene;
                if (scene != null)
                {
                    Mission.Current.SetBowMissileSpeedModifier(1f);
                    Mission.Current.SetCrossbowMissileSpeedModifier(1f);
                    Mission.Current.SetMissileRangeModifier(1f);
                }

                return false;
            }
        }

        //[HarmonyPatch(typeof(BannerBearerLogic))]
        //[HarmonyPatch("RespawnAsBannerBearer")]
        //internal class RespawnAsBannerBearerPatch
        //{
        //    private static bool Prefix(ref BannerBearerLogic __instance, Agent agent, ref Agent __result, bool isAlarmed, bool wieldInitialWeapons, bool forceDismounted, string specialActionSetSuffix = null, bool useTroopClassForSpawn = false)
        //    {
        //        if (agent != null && agent.Formation != null)
        //        {
        //            Formation formation = agent.Formation;
        //            MethodInfo method = typeof(BannerBearerLogic).GetMethod("GetFormationControllerFromFormation", BindingFlags.NonPublic | BindingFlags.Instance);
        //            method.DeclaringType.GetMethod("GetFormationControllerFromFormation");
        //            Object obj = method.Invoke(__instance, new object[] { agent.Formation });
        //            if (obj != null)
        //            {
        //                return true;
        //            }
        //            else
        //            {
        //                if (agent.IsActive())
        //                {
        //                    __result = agent;
        //                    return false;
        //                }
        //                else
        //                {
        //                    return true;
        //                }
        //            }
        //        }
        //        else
        //        {
        //            return false;
        //        }
        //    }
        //}

        //[HarmonyPatch(typeof(MissionAgentLabelView))]
        //[HarmonyPatch("SetHighlightForAgents")]
        //class SetHighlightForAgentsPatch
        //{
        //    static bool Prefix( bool highlight, ref bool useSiegeMachineUsers, ref bool useAllTeamAgents, Dictionary<Agent, MetaMesh> ____agentMeshes, MissionAgentLabelView __instance)
        //    {
        //        if (__instance.Mission.PlayerTeam?.PlayerOrderController == null)
        //        {
        //            bool flag = __instance.Mission.PlayerTeam == null;
        //            Debug.Print($"PlayerOrderController is null and playerTeamIsNull: {flag}", 0, Debug.DebugColor.White, 17179869184uL);
        //        }
        //        if (useSiegeMachineUsers)
        //        {
        //            foreach (TaleWorlds.MountAndBlade.SiegeWeapon selectedWeapon in __instance.Mission.PlayerTeam?.PlayerOrderController.SiegeWeaponController.SelectedWeapons)
        //            {
        //                foreach (Agent user in selectedWeapon.Users)
        //                {
        //                    MetaMesh agentMesh;
        //                    if (____agentMeshes.TryGetValue(user, out agentMesh))
        //                    {
        //                        MethodInfo method = typeof(MissionAgentLabelView).GetMethod("UpdateSelectionVisibility", BindingFlags.NonPublic | BindingFlags.Instance);
        //                        method.DeclaringType.GetMethod("UpdateSelectionVisibility");
        //                        method.Invoke(__instance, new object[] { user, agentMesh, highlight });
        //                    }

        //                }
        //            }
        //            return false;
        //        }
        //        if (useAllTeamAgents)
        //        {
        //            if (__instance.Mission.PlayerTeam?.PlayerOrderController.Owner == null)
        //            {
        //                return false;
        //            }
        //            foreach (Agent activeAgent in __instance.Mission.PlayerTeam?.PlayerOrderController.Owner.Team.ActiveAgents)
        //            {
        //                MetaMesh agentMesh;
        //                if (____agentMeshes.TryGetValue(activeAgent, out agentMesh))
        //                {
        //                    MethodInfo method = typeof(MissionAgentLabelView).GetMethod("UpdateSelectionVisibility", BindingFlags.NonPublic | BindingFlags.Instance);
        //                    method.DeclaringType.GetMethod("UpdateSelectionVisibility");
        //                    method.Invoke(__instance, new object[] { activeAgent, agentMesh, highlight });
        //                }
        //            }
        //            return false;
        //        }
        //        foreach (Formation selectedFormation in __instance.Mission.PlayerTeam?.PlayerOrderController.SelectedFormations)
        //        {
        //            selectedFormation.ApplyActionOnEachUnit(delegate (Agent agent)
        //            {
        //                MetaMesh agentMesh;
        //                if(____agentMeshes.TryGetValue(agent, out agentMesh))
        //                {
        //                    MethodInfo method = typeof(MissionAgentLabelView).GetMethod("UpdateSelectionVisibility", BindingFlags.NonPublic | BindingFlags.Instance);
        //                    method.DeclaringType.GetMethod("UpdateSelectionVisibility");
        //                    method.Invoke(__instance, new object[] { agent, agentMesh, highlight });
        //                }
        //            });
        //        }
        //        return false;
        //    }
        //}

        //[HarmonyPatch(typeof(WorldPosition))]
        //[HarmonyPatch("GetGroundZ")]
        //class GetGroundZPatch
        //{
        //    [HandleProcessCorruptedStateExceptions]
        //    static bool Prefix(ref WorldPosition __instance, ref Vec3 ____position, ref float __result)
        //    {
        //        try
        //        {
        //            MethodInfo method = typeof(WorldPosition).GetMethod("ValidateZ", BindingFlags.NonPublic | BindingFlags.Instance);
        //            method.DeclaringType.GetMethod("ValidateZ");
        //            method.Invoke(__instance, new object[] { ZValidityState.Valid });
        //            if (__instance.State >= ZValidityState.Valid)
        //            {
        //                __result = ____position.z;
        //                return true;
        //            }
        //            __result = float.NaN;
        //            return true;
        //        }
        //        catch (Exception e)
        //        {
        //            __result = 0f;
        //            return false;
        //        }
        //        return true;
        //    }
        //}

        //[MBCallback]
        //[HarmonyPatch(typeof(Agent))]
        //class OverrideOnWeaponAmmoReload
        //{
        //    [HarmonyPrefix]
        //    [HarmonyPatch("OnWeaponAmmoReload")]
        //    static bool PrefixOnWeaponAmmoReload(ref Agent __instance, EquipmentIndex slotIndex, EquipmentIndex ammoSlotIndex, short totalAmmo)
        //    {
        //        if (__instance.IsMount || __instance.IsPlayerControlled || __instance.Formation == null || __instance.Formation.FormationIndex == FormationClass.Infantry || __instance.Formation.FormationIndex == FormationClass.Cavalry)
        //        {
        //            return true;
        //        }
        //        bool flag = false;
        //        if (__instance.Formation != null && __instance.Equipment.HasRangedWeapon(WeaponClass.Arrow) && __instance.Equipment.GetAmmoAmount(WeaponClass.Arrow) <= 2)
        //        {
        //            flag = true;
        //        }
        //        else if (__instance.Formation != null && __instance.Equipment.HasRangedWeapon(WeaponClass.Bolt) && __instance.Equipment.GetAmmoAmount(WeaponClass.Bolt) <= 2)
        //        {
        //            flag = true;
        //        }
        //        if (flag)
        //        {
        //            if (__instance.Formation != null && __instance.HasMount)
        //            {
        //                __instance.Formation = __instance.Team.GetFormation(FormationClass.Cavalry);
        //            }
        //            else
        //            {
        //                __instance.Formation = __instance.Team.GetFormation(FormationClass.Infantry);
        //            }
        //        }
        //        return true;
        //    }
        //}
    }
}
