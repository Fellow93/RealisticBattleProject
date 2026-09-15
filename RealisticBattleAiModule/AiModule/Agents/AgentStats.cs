using HarmonyLib;
using Helpers;
using JetBrains.Annotations;
using SandBox.GameComponents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.MountAndBlade.ArrangementOrder;

namespace RBMAI
{
    public static partial class AgentAi
    {
        [HarmonyPatch(typeof(AgentStatCalculateModel))]
        [HarmonyPatch("SetAiRelatedProperties")]
        private class OverrideSetAiRelatedProperties
        {
            private static readonly MethodInfo _getMeleeSkillMethod = typeof(AgentStatCalculateModel).GetMethod("GetMeleeSkill", BindingFlags.NonPublic | BindingFlags.Instance);
            private static readonly MBList<Agent> _nearbyEnemiesBuffer = new MBList<Agent>();

            private static void Postfix(Agent agent, ref AgentDrivenProperties agentDrivenProperties, WeaponComponentData equippedItem, WeaponComponentData secondaryItem, AgentStatCalculateModel __instance)
            {
                bool agentHasShield = false;
                if (agent.GetOffhandWieldedItemIndex() != EquipmentIndex.None)
                {
                    if (agent.Equipment[agent.GetOffhandWieldedItemIndex()].CurrentUsageItem.WeaponClass == WeaponClass.SmallShield ||
                        agent.Equipment[agent.GetOffhandWieldedItemIndex()].CurrentUsageItem.WeaponClass == WeaponClass.LargeShield)
                    {
                        agentHasShield = true;
                    }
                }

                SkillObject skill = (equippedItem == null) ? DefaultSkills.Athletics : equippedItem.RelevantSkill;
                int meleeSkill = (int)_getMeleeSkillMethod.Invoke(__instance, new object[] { agent, equippedItem, secondaryItem });
                int effectiveSkill = __instance.GetEffectiveSkill(agent, skill);
                float meleeLevel = RBMAI.Utilities.CalculateAILevel(agent, meleeSkill);                 //num
                float effectiveSkillLevel = RBMAI.Utilities.CalculateAILevel(agent, effectiveSkill);    //num2

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
                        //agentDrivenProperties.AIDecideOnAttackChance = MBMath.ClampFloat(meleeLevel + 0.1f, 0f, 0.95f);
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
                    agentDrivenProperties.AiShooterError = 0.015f - (0.007f * effectiveSkillLevel);
                    agentDrivenProperties.WeaponMaxMovementAccuracyPenalty *= 0.33f;
                    agentDrivenProperties.WeaponBestAccuracyWaitTime = 1f - (0.75f * effectiveSkillLevel);
                }
                else if (!agent.WieldedWeapon.IsEmpty && agent.WieldedWeapon.CurrentUsageItem.WeaponClass == WeaponClass.Bow)
                {
                    agentDrivenProperties.AiShooterError = 0.015f - (0.015f * effectiveSkillLevel);
                    agentDrivenProperties.WeaponMaxMovementAccuracyPenalty *= 0.33f;
                    agentDrivenProperties.WeaponBestAccuracyWaitTime = 2f - (1.5f * effectiveSkillLevel);
                }
                else
                {
                    agentDrivenProperties.AiShooterError = 0.01f - (0.010f * effectiveSkillLevel);
                    agentDrivenProperties.WeaponMaxMovementAccuracyPenalty *= 0.1f;
                    agentDrivenProperties.WeaponBestAccuracyWaitTime = 0.1f;
                }

                if (!agent.IsRangedCached)
                {
                    agentDrivenProperties.AIHoldingReadyMaxDuration = 1f;
                    agentDrivenProperties.AIHoldingReadyVariationPercentage = 1f;
                }

                //agentDrivenProperties.AiWeaponFavorMultiplierPolearm

                agentDrivenProperties.AiRangerLeadErrorMin = (float)((0.0 - (double)num4) * 0.349999994039536) + 0.3f;
                agentDrivenProperties.AiRangerLeadErrorMax = num4 * 0.2f + 0.3f;

                if (equippedItem != null && equippedItem.RelevantSkill == DefaultSkills.Bow)
                {
                    if (agent.MountAgent != null)
                    {
                        //agentDrivenProperties.AiRangerVerticalErrorMultiplier = 0f;//horse archers
                        //agentDrivenProperties.AiRangerHorizontalErrorMultiplier = 0f;//horse archers
                        agentDrivenProperties.AiRangerVerticalErrorMultiplier = MBMath.ClampFloat(0.025f - effectiveSkill * 0.0001f, 0.01f, 0.025f);//bow
                        agentDrivenProperties.AiRangerHorizontalErrorMultiplier = MBMath.ClampFloat(0.025f - effectiveSkill * 0.0001f, 0.01f, 0.025f);//bow
                        agentDrivenProperties.WeaponMaxMovementAccuracyPenalty *= 0.1f;
                        agentDrivenProperties.WeaponMaxUnsteadyAccuracyPenalty *= 0.2f;
                        agentDrivenProperties.WeaponRotationalAccuracyPenaltyInRadians = 0.015f;
                    }
                    else
                    {
                        agentDrivenProperties.AiRangerVerticalErrorMultiplier = MBMath.ClampFloat(0.025f - effectiveSkill * 0.0001f, 0.01f, 0.025f);//bow
                        agentDrivenProperties.AiRangerHorizontalErrorMultiplier = MBMath.ClampFloat(0.025f - effectiveSkill * 0.0001f, 0.01f, 0.025f);//bow
                    }
                }
                else if (equippedItem != null && equippedItem.RelevantSkill == DefaultSkills.Crossbow)
                {
                    if (agent.MountAgent != null)
                    {
                        //agentDrivenProperties.AiRangerVerticalErrorMultiplier = 0f;//horse archers
                        //agentDrivenProperties.AiRangerHorizontalErrorMultiplier = 0f;//horse archers
                        agentDrivenProperties.AiRangerVerticalErrorMultiplier = MBMath.ClampFloat(0.020f - effectiveSkill * 0.0001f, 0.005f, 0.020f);//xbow
                        agentDrivenProperties.AiRangerHorizontalErrorMultiplier = MBMath.ClampFloat(0.020f - effectiveSkill * 0.0001f, 0.005f, 0.020f);//xbow
                        agentDrivenProperties.WeaponMaxMovementAccuracyPenalty *= 0.20f;
                        agentDrivenProperties.WeaponMaxUnsteadyAccuracyPenalty *= 0.3f;
                        agentDrivenProperties.WeaponRotationalAccuracyPenaltyInRadians = 0.010f;
                    }
                    else
                    {
                        agentDrivenProperties.AiRangerVerticalErrorMultiplier = MBMath.ClampFloat(0.020f - effectiveSkill * 0.0001f, 0.005f, 0.020f);//crossbow
                        agentDrivenProperties.AiRangerHorizontalErrorMultiplier = MBMath.ClampFloat(0.020f - effectiveSkill * 0.0001f, 0.005f, 0.020f);//crossbow
                    }
                }
                else
                {
                    if (agent.MountAgent != null)
                    {
                        //agentDrivenProperties.AiRangerVerticalErrorMultiplier = 0f;//horse archers
                        //agentDrivenProperties.AiRangerHorizontalErrorMultiplier = 0f;//horse archers
                        agentDrivenProperties.AiRangerVerticalErrorMultiplier = MBMath.ClampFloat(0.03f - effectiveSkill * 0.0001f, 0.005f, 0.020f);//xbow
                        agentDrivenProperties.AiRangerHorizontalErrorMultiplier = MBMath.ClampFloat(0.03f - effectiveSkill * 0.0001f, 0.005f, 0.020f);//xbow
                        agentDrivenProperties.WeaponMaxMovementAccuracyPenalty *= 0.20f;
                        agentDrivenProperties.WeaponMaxUnsteadyAccuracyPenalty *= 0.3f;
                        agentDrivenProperties.WeaponRotationalAccuracyPenaltyInRadians = 0.010f;
                    }
                    else
                    {
                        agentDrivenProperties.AiRangerVerticalErrorMultiplier = MBMath.ClampFloat(0.03f - effectiveSkill * 0.0001f, 0.005f, 0.02f);// javelins and axes etc
                        agentDrivenProperties.AiRangerHorizontalErrorMultiplier = MBMath.ClampFloat(0.03f - effectiveSkill * 0.0001f, 0.005f, 0.02f);// javelins and axes etc
                    }
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
                if (agent != null && agent.IsActive() && Mission.Current != null && Mission.Current.IsDeploymentFinished)
                {
                    _nearbyEnemiesBuffer.Clear();
                    Mission.Current.GetNearbyEnemyAgents(agent.GetWorldPosition().AsVec2, 2.5f, agent.Team, _nearbyEnemiesBuffer);
                    if (_nearbyEnemiesBuffer.Count > 0)
                    {
                        agent.AgentDrivenProperties.AiWeaponFavorMultiplierMelee = 55f;
                    }
                    else
                    {
                        agent.AgentDrivenProperties.AiWeaponFavorMultiplierPolearm = 35f;
                    }
                }

                agentDrivenProperties.SetStat(DrivenProperty.UseRealisticBlocking, 1f);
                //agentDrivenProperties.SetStat(DrivenProperty.UseRealisticBlocking, 0f);

                agentDrivenProperties.MissileSpeedMultiplier = 1f;

                if (agent.IsRangedCached)
                {
                    agentDrivenProperties.AiUseShieldAgainstEnemyMissileProbability = 0f;
                    agentDrivenProperties.AiFacingMissileWatch = 0f;
                    agentDrivenProperties.AiFlyingMissileCheckRadius = 0f;
                }

                //stamina effects
                if (RBMConfig.RBMConfig.postureEnabled)
                {
                    Stance stance = null;
                    AgentStances.values.TryGetValue(agent, out stance);
                    if (agent != null && stance != null)
                    {
                        float staminaLevel = stance.stamina / stance.maxStamina;

                        //readying and blocking
                        agentDrivenProperties.HandlingMultiplier *= MBMath.Lerp(0.5f, 1f, staminaLevel);
                        agentDrivenProperties.OffhandWeaponDefendSpeedMultiplier = MBMath.Lerp(0.5f, 1f, staminaLevel);

                        //movement speed
                        agentDrivenProperties.MaxSpeedMultiplier *= MBMath.Lerp(0.85f, 1f, staminaLevel);

                        //attack speed
                        agentDrivenProperties.SwingSpeedMultiplier *= MBMath.Lerp(0.85f, 1f, staminaLevel);
                        agentDrivenProperties.ThrustOrRangedReadySpeedMultiplier *= MBMath.Lerp(0.85f, 1f, staminaLevel);

                        //reload speed
                        agentDrivenProperties.ReloadSpeed *= MBMath.Lerp(0.8f, 1f, staminaLevel);

                        //ranged penalties
                        agentDrivenProperties.WeaponMaxMovementAccuracyPenalty *= MBMath.Lerp(1.15f, 1f, staminaLevel);
                        agentDrivenProperties.WeaponMaxUnsteadyAccuracyPenalty *= MBMath.Lerp(1.15f, 1f, staminaLevel);

                        //ranged AI
                        agentDrivenProperties.WeaponBestAccuracyWaitTime *= MBMath.Lerp(1.3f, 1f, staminaLevel);

                        //AI combat ability
                        agentDrivenProperties.AIRealizeBlockingFromIncorrectSideAbility *= MBMath.Lerp(0.85f, 1f, staminaLevel);
                        agentDrivenProperties.AIDecideOnRealizeEnemyBlockingAttackAbility *= MBMath.Lerp(0.85f, 1f, staminaLevel);
                        agentDrivenProperties.AIAttackOnParryChance *= MBMath.Lerp(0.85f, 1f, staminaLevel);

                        //AI aggressiveness
                        agentDrivenProperties.AIDecideOnAttackChance *= MBMath.Lerp(0.85f, 1f, staminaLevel);
                        agentDrivenProperties.AiAttackCalculationMaxTimeFactor *= MBMath.Lerp(0.85f, 1f, staminaLevel);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(SandboxAgentStatCalculateModel))]
        [HarmonyPatch("SetWeaponSkillEffectsOnAgent")]
        internal class SetWeaponSkillEffectsOnAgentPatch
        {
            private static void AddToStat(ref ExplainedNumber stat, EffectIncrementType effectIncrementType, float number, TextObject text)
            {
                switch (effectIncrementType)
                {
                    case EffectIncrementType.Add:
                        stat.Add(number, text);
                        break;

                    case EffectIncrementType.AddFactor:
                        stat.AddFactor(number, text);
                        break;
                }
            }

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
                        float skillEffectValue = DefaultSkillEffects.OneHandedSpeed.GetSkillEffectValue(effectiveSkill);
                        AddToStat(ref stat, DefaultSkillEffects.OneHandedSpeed.IncrementType, skillEffectValue, stat.IncludeDescriptions ? GameTexts.FindText("role", DefaultSkillEffects.OneHandedSpeed.Role.ToString()) : null);
                        AddToStat(ref stat2, DefaultSkillEffects.OneHandedSpeed.IncrementType, skillEffectValue, stat2.IncludeDescriptions ? GameTexts.FindText("role", DefaultSkillEffects.OneHandedSpeed.Role.ToString()) : null);
                    }
                    else if (equippedWeaponComponent.RelevantSkill == DefaultSkills.TwoHanded)
                    {
                        if (effectiveSkill > 150)
                        {
                            effectiveSkill = 150;
                        }
                        float skillEffectValue = DefaultSkillEffects.TwoHandedSpeed.GetSkillEffectValue(effectiveSkill);
                        AddToStat(ref stat, DefaultSkillEffects.TwoHandedSpeed.IncrementType, skillEffectValue, stat.IncludeDescriptions ? GameTexts.FindText("role", DefaultSkillEffects.OneHandedSpeed.Role.ToString()) : null);
                        AddToStat(ref stat2, DefaultSkillEffects.TwoHandedSpeed.IncrementType, skillEffectValue, stat2.IncludeDescriptions ? GameTexts.FindText("role", DefaultSkillEffects.OneHandedSpeed.Role.ToString()) : null);
                    }
                    else if (equippedWeaponComponent.RelevantSkill == DefaultSkills.Polearm)
                    {
                        if (effectiveSkill > 150)
                        {
                            effectiveSkill = 150;
                        }
                        float skillEffectValue = DefaultSkillEffects.PolearmSpeed.GetSkillEffectValue(effectiveSkill);
                        AddToStat(ref stat, DefaultSkillEffects.PolearmSpeed.IncrementType, skillEffectValue, stat.IncludeDescriptions ? GameTexts.FindText("role", DefaultSkillEffects.OneHandedSpeed.Role.ToString()) : null);
                        AddToStat(ref stat2, DefaultSkillEffects.PolearmSpeed.IncrementType, skillEffectValue, stat2.IncludeDescriptions ? GameTexts.FindText("role", DefaultSkillEffects.OneHandedSpeed.Role.ToString()) : null);
                    }
                    else if (equippedWeaponComponent.RelevantSkill == DefaultSkills.Crossbow)
                    {
                        SkillHelper.AddSkillBonusForCharacter(DefaultSkillEffects.CrossbowReloadSpeed, characterObject, ref stat3);
                    }
                    else if (equippedWeaponComponent.RelevantSkill == DefaultSkills.Throwing)
                    {
                        SkillHelper.AddSkillBonusForCharacter(DefaultSkillEffects.ThrowingSpeed, characterObject, ref stat2);
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
    }
}
