using HarmonyLib;
using System.Reflection;
using TaleWorlds.Core;
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
            static void Postfix(Agent agent, ref AgentDrivenProperties agentDrivenProperties, WeaponComponentData equippedItem, WeaponComponentData secondaryItem)
            {
                int meleeSkill = Utilities.GetMeleeSkill(agent, equippedItem, secondaryItem);
                SkillObject skill = (equippedItem == null) ? DefaultSkills.Athletics : equippedItem.RelevantSkill;
                int effectiveSkill = Utilities.GetEffectiveSkill(agent.Character, agent.Origin, agent.Formation, skill);
                float meleeLevel = Utilities.CalculateAILevel(agent, meleeSkill);                 //num
                float effectiveSkillLevel = Utilities.CalculateAILevel(agent, effectiveSkill);    //num2
                float meleeDefensivness = meleeLevel + agent.Defensiveness;             //num3

                agentDrivenProperties.AiChargeHorsebackTargetDistFactor = 5f;
                agentDrivenProperties.AIBlockOnDecideAbility = MBMath.ClampFloat(meleeLevel * 0.6f, 0.15f, 0.45f); // chance for directed blocking
                agentDrivenProperties.AIParryOnDecideAbility = MBMath.ClampFloat((meleeLevel * 0.30f) + 0.15f, 0.1f, 0.45f);
                agentDrivenProperties.AIRealizeBlockingFromIncorrectSideAbility = MBMath.ClampFloat((meleeLevel * 0.3f) - 0.05f, 0.01f, 0.25f);
                agentDrivenProperties.AIDecideOnAttackChance = MBMath.ClampFloat(meleeLevel + 0.1f, 0f, 0.95f);
                agentDrivenProperties.AIDecideOnRealizeEnemyBlockingAttackAbility = MBMath.ClampFloat(meleeLevel + 0.1f, 0f, 0.95f);

                agentDrivenProperties.AiRangedHorsebackMissileRange = 0.7f;
                agentDrivenProperties.AiUseShieldAgainstEnemyMissileProbability = 0.95f;
                agentDrivenProperties.AiFlyingMissileCheckRadius = 250f;

                agentDrivenProperties.AiShooterError = 0.0001f;

                //agentDrivenProperties.AiRangerLeadErrorMin = 0f;
                //agentDrivenProperties.AiRangerLeadErrorMax = 0f;
                //agentDrivenProperties.AiRangerVerticalErrorMultiplier = 0f;
                //agentDrivenProperties.AiRangerHorizontalErrorMultiplier = 0f;

                agentDrivenProperties.AIAttackOnParryChance = MBMath.ClampFloat(meleeLevel*0.4f, 0.1f, 0.30f); //0.3f - 0.1f * agent.Defensiveness; //0.2-0.3f // chance to break own parry guard - 0 constant parry in reaction to enemy, 1 constant breaking of parry
                agentDrivenProperties.AIDecideOnAttackChance = MBMath.ClampFloat(meleeLevel*0.3f, 0.15f, 0.5f); //0.15f * agent.Defensiveness; //0-0.15f - how often is direction changed (or swtich to parry) when preparing for attack
                agentDrivenProperties.AIAttackOnDecideChance = 0.5f;//MBMath.ClampFloat(0.23f * CalculateAIAttackOnDecideMaxValue() * (3f - agent.Defensiveness), 0.05f, 1f); //0.05-1f, 0.66-line, 0.44 - shield wall - aggressiveness / chance of attack instead of anything else / when set to 0 AI never attacks on its own
                agentDrivenProperties.AiDefendWithShieldDecisionChanceValue = MBMath.ClampFloat(1f - (meleeLevel * 1f), 0.1f, 1.0f);//MBMath.ClampMin(1f, 0.2f + 0.5f * num + 0.2f * num3); 0.599-0.799 = 200 skill line/wall - chance for passive constant block

                agentDrivenProperties.AiRaiseShieldDelayTimeBase = MBMath.ClampFloat(-0.5f + (meleeLevel * 1.25f), -0.5f, 0f); //-0.75f + 0.5f * meleeLevel; delay between block decision and actual block for AI
                agentDrivenProperties.AiAttackCalculationMaxTimeFactor = meleeLevel;
                agentDrivenProperties.AiAttackingShieldDefenseChance = MBMath.ClampFloat(meleeLevel * 2f, 0.1f, 1.0f); ; //0.2f + 0.3f * meleeLevel;
                agentDrivenProperties.AiAttackingShieldDefenseTimer = MBMath.ClampFloat(-0.3f + (meleeLevel * 0.6f), -0.3f, 0f);  //-0.3f + 0.3f * meleeLevel;

                agentDrivenProperties.AiShootFreq = MBMath.ClampFloat(meleeLevel * 1.5f, 0.1f, 0.9f); // when set to 0 AI never shoots
                //agentDrivenProperties.AiWaitBeforeShootFactor = 0f;

                //agentDrivenProperties.AiMinimumDistanceToContinueFactor = 5f; //2f + 0.3f * (3f - meleeSkill);
                //agentDrivenProperties.AIHoldingReadyMaxDuration = 0.1f; //MBMath.Lerp(0.25f, 0f, MBMath.Min(1f, num * 1.2f));
                //agentDrivenProperties.AIHoldingReadyVariationPercentage = //num;
            }
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
                switch (orderEnum)
                {
                    case ArrangementOrderEnum.Line:
                    case ArrangementOrderEnum.Loose:
                        {
                            float currentTime = MBCommon.TimeType.Mission.GetTime();
                            float lastMeleeAttackTime = unit.LastMeleeAttackTime;
                            float lastMeleeHitTime = unit.LastMeleeHitTime;
                            float lastRangedHit = unit.LastRangedHitTime;
                            //if ((currentTime - lastMeleeAttackTime < 4f) || (currentTime - lastMeleeHitTime < 4f))
                            //{
                            //    __result = Agent.UsageDirection.None;
                            //}
                            if (Mission.Current.IsFieldBattle && ((currentTime - lastRangedHit < 10f) || formation.QuerySystem.UnderRangedAttackRatio >= 0.04f))
                            {
                                __result = Agent.UsageDirection.DefendDown;
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

}
