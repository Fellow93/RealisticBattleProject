using HarmonyLib;
using SandBox.GameComponents;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMCombat
{
    internal partial class HorseChanges
    {
        [HarmonyPatch(typeof(SandboxAgentStatCalculateModel))]
        [HarmonyPatch("UpdateHorseStats")]
        private class SandboxAgentStatCalculateModelUpdateHorseStatsPatch
        {
            private static void Postfix(ref SandboxAgentStatCalculateModel __instance, ref Agent agent, ref AgentDrivenProperties agentDrivenProperties)
            {
                if (agent.RiderAgent != null)
                {
                    int effectiveRidingSkill = 0;
                    effectiveRidingSkill = __instance.GetEffectiveSkill(agent.RiderAgent, DefaultSkills.Riding);

                    Equipment spawnEquipment = agent.SpawnEquipment;
                    EquipmentElement mountElement = spawnEquipment[EquipmentIndex.ArmorItemEndSlot];
                    ItemObject item = mountElement.Item;
                    EquipmentElement harness = spawnEquipment[EquipmentIndex.HorseHarness];
                    int mountDifficulty = mountElement.Item.Difficulty;

                    int mountSkillDifficultyTreshold = 75;
                    float minSkillModifier = 0.9f;
                    float maxSkillModifier = 1.1f;

                    float sceneModifier = 1f;
                    if (!agent.Mission.Scene.IsAtmosphereIndoor)
                    {
                        if (agent.Mission.Scene.GetRainDensity() > 0f)
                        {
                            sceneModifier *= 0.9f;
                        }
                        if (CampaignTime.Now.IsNightTime)
                        {
                            sceneModifier *= 0.9f;
                        }
                    }

                    int mountMastery = effectiveRidingSkill - mountDifficulty;
                    if (mountMastery < 0)
                    {
                        mountMastery = 0;
                    }
                    else if (mountMastery > mountSkillDifficultyTreshold)
                    {
                        mountMastery = mountSkillDifficultyTreshold;
                    }

                    float addedWeight = agent.RiderAgent.SpawnEquipment.GetTotalWeightOfArmor(true) + agent.RiderAgent.SpawnEquipment.GetTotalWeightOfWeapons() + agent.RiderAgent.Monster.Weight;
                    if (harness.Item != null)
                    {
                        addedWeight += harness.Weight;
                    }

                    float weightModifier = MathF.Pow(475f, 2) / MathF.Pow(mountElement.Weight + addedWeight, 2);

                    float mountMasteryModifier = MathF.Lerp(minSkillModifier, maxSkillModifier, (float)mountMastery / (float)mountSkillDifficultyTreshold);

                    int mountStatSpeed = mountElement.GetModifiedMountSpeed(in harness) + 1;
                    ExplainedNumber mountStatSpeedEN = new ExplainedNumber(mountStatSpeed);

                    if (harness.Item == null)
                    {
                        mountStatSpeedEN.AddFactor(-0.1f);
                    }
                    agentDrivenProperties.MountSpeed = sceneModifier * 0.22f * (1f + mountStatSpeedEN.ResultNumber) * mountMasteryModifier;

                    float baseSpeed = 10f;
                    if (mountElement.Weight > 500f)
                    {
                        baseSpeed = 8f;
                    }
                    if (agentDrivenProperties.MountSpeed > baseSpeed)
                    {
                        agentDrivenProperties.MountSpeed = MBMath.Lerp(baseSpeed, agentDrivenProperties.MountSpeed, weightModifier);
                    }
                }

                agentDrivenProperties.AttributeShieldMissileCollisionBodySizeAdder = 0.01f;
                float weightOfHorseAndRaider = 0f;
                if (agent.RiderAgent != null)
                {
                    MissionEquipment equipment = agent.RiderAgent.Equipment;
                    weightOfHorseAndRaider += (float)agent.RiderAgent.Monster.Weight;
                    weightOfHorseAndRaider += agent.RiderAgent.SpawnEquipment.GetTotalWeightOfArmor(forHuman: true);
                    weightOfHorseAndRaider += equipment.GetTotalWeightOfWeapons();
                    weightOfHorseAndRaider += (float)agent.Monster.Weight;
                    weightOfHorseAndRaider += agent.SpawnEquipment.GetTotalWeightOfArmor(forHuman: false);
                    weightOfHorseAndRaider += 100f;
                }
                else
                {
                    weightOfHorseAndRaider += (float)agent.Monster.Weight;
                    weightOfHorseAndRaider += agent.SpawnEquipment.GetTotalWeightOfArmor(forHuman: false);
                    weightOfHorseAndRaider += 100f;
                }
                agentDrivenProperties.MountChargeDamage = weightOfHorseAndRaider;
            }
        }

        [HarmonyPatch(typeof(CustomBattleAgentStatCalculateModel))]
        [HarmonyPatch("UpdateHorseStats")]
        private class CustomBattleAgentStatCalculateModelUpdateHorseStatsPatch
        {
            private static void Postfix(ref CustomBattleAgentStatCalculateModel __instance, ref Agent agent, ref AgentDrivenProperties agentDrivenProperties)
            {
                if (agent.RiderAgent != null)
                {
                    int effectiveRidingSkill = 0;
                    effectiveRidingSkill = __instance.GetEffectiveSkill(agent.RiderAgent, DefaultSkills.Riding);

                    Equipment spawnEquipment = agent.SpawnEquipment;
                    EquipmentElement mountElement = spawnEquipment[EquipmentIndex.ArmorItemEndSlot];
                    ItemObject item = mountElement.Item;
                    EquipmentElement harness = spawnEquipment[EquipmentIndex.HorseHarness];

                    int mountDifficulty = mountElement.Item.Difficulty;

                    int mountSkillDifficultyTreshold = 75;
                    float minSkillModifier = 0.9f;
                    float maxSkillModifier = 1.1f;

                    float sceneModifier = 1f;
                    if (!agent.Mission.Scene.IsAtmosphereIndoor)
                    {
                        if (agent.Mission.Scene.GetRainDensity() > 0f)
                        {
                            sceneModifier *= 0.9f;
                        }
                        if (!MBMath.IsBetween(agent.Mission.Scene.TimeOfDay, 4f, 20.01f))
                        {
                            sceneModifier *= 0.9f;
                        }
                    }

                    int mountMastery = effectiveRidingSkill - mountDifficulty;
                    if (mountMastery < 0)
                    {
                        mountMastery = 0;
                    }
                    else if (mountMastery > mountSkillDifficultyTreshold)
                    {
                        mountMastery = mountSkillDifficultyTreshold;
                    }

                    float addedWeight = harness.Weight + agent.RiderAgent.SpawnEquipment.GetTotalWeightOfArmor(true) + agent.RiderAgent.SpawnEquipment.GetTotalWeightOfWeapons() + agent.RiderAgent.Monster.Weight;

                    float weightModifier = MathF.Pow(475f, 2) / MathF.Pow(mountElement.Weight + addedWeight, 2);

                    float mountMasteryModifier = MathF.Lerp(minSkillModifier, maxSkillModifier, (float)mountMastery / (float)mountSkillDifficultyTreshold);

                    int mountStatSpeed = mountElement.GetModifiedMountSpeed(in harness) + 1;
                    ExplainedNumber mountStatSpeedEN = new ExplainedNumber(mountStatSpeed);

                    if (harness.Item == null)
                    {
                        mountStatSpeedEN.AddFactor(-0.1f);
                    }
                    agentDrivenProperties.MountSpeed = sceneModifier * 0.22f * (1f + mountStatSpeedEN.ResultNumber) * mountMasteryModifier;

                    float baseSpeed = 10f;
                    if (mountElement.Weight > 500f)
                    {
                        baseSpeed = 8f;
                    }
                    if (agentDrivenProperties.MountSpeed > baseSpeed)
                    {
                        agentDrivenProperties.MountSpeed = MBMath.Lerp(baseSpeed, agentDrivenProperties.MountSpeed, weightModifier);
                    }
                }

                agentDrivenProperties.AttributeShieldMissileCollisionBodySizeAdder = 0.01f;
                float weightOfHorseAndRaider = 0f;
                if (agent.RiderAgent != null)
                {
                    MissionEquipment equipment = agent.RiderAgent.Equipment;
                    weightOfHorseAndRaider += (float)agent.RiderAgent.Monster.Weight;
                    weightOfHorseAndRaider += agent.RiderAgent.SpawnEquipment.GetTotalWeightOfArmor(forHuman: true);
                    weightOfHorseAndRaider += equipment.GetTotalWeightOfWeapons();
                    weightOfHorseAndRaider += (float)agent.Monster.Weight;
                    weightOfHorseAndRaider += agent.SpawnEquipment.GetTotalWeightOfArmor(forHuman: false);
                }
                else
                {
                    weightOfHorseAndRaider += (float)agent.Monster.Weight;
                    weightOfHorseAndRaider += agent.SpawnEquipment.GetTotalWeightOfArmor(forHuman: false);
                }
                agentDrivenProperties.MountChargeDamage = weightOfHorseAndRaider;
            }
        }
    }
}
