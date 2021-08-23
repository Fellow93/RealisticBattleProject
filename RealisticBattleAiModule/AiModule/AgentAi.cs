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
                agentDrivenProperties.AIBlockOnDecideAbility = MBMath.ClampFloat(meleeLevel * 0.5f, 0.15f, 0.45f);
                agentDrivenProperties.AIParryOnDecideAbility = MBMath.ClampFloat((meleeLevel * 0.25f) + 0.15f, 0.1f, 0.45f);
                agentDrivenProperties.AIRealizeBlockingFromIncorrectSideAbility = MBMath.ClampFloat((meleeLevel * 0.3f) - 0.05f, 0.01f, 0.25f);
                agentDrivenProperties.AIDecideOnAttackChance = MBMath.ClampFloat(meleeLevel + 0.1f, 0f, 0.95f);
                agentDrivenProperties.AIDecideOnRealizeEnemyBlockingAttackAbility = MBMath.ClampFloat(meleeLevel + 0.1f, 0f, 0.95f);

                agentDrivenProperties.AiRangedHorsebackMissileRange = 0.7f;
                agentDrivenProperties.AiUseShieldAgainstEnemyMissileProbability = 0.95f;
                agentDrivenProperties.AiFlyingMissileCheckRadius = 250f;

                agentDrivenProperties.AiShooterError = 0.0001f;
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
                            if ((currentTime - lastMeleeAttackTime < 4f) || (currentTime - lastMeleeHitTime < 4f))
                            {
                                __result = Agent.UsageDirection.None;
                            }
                            else if ((currentTime - lastRangedHit < 10f) || formation.QuerySystem.UnderRangedAttackRatio >= 0.04f)
                            {
                                __result = Agent.UsageDirection.DefendDown;
                            }
                            else
                            {
                                __result = Agent.UsageDirection.None;
                            }
                            break;
                        }
                }
            }
        }
    }

}
