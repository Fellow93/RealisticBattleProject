using HarmonyLib;
using SandBox.GameComponents;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMCombat
{
    class HorseChanges
    {
        [HarmonyPatch(typeof(SandboxAgentStatCalculateModel))]
        [HarmonyPatch("UpdateHorseStats")]
        class UpdateHorseStatsPatch
        {
            static void Postfix(ref SandboxAgentStatCalculateModel __instance, ref Agent agent, ref AgentDrivenProperties agentDrivenProperties)
            {
                if (agent.RiderAgent != null)
                {
                    int effectiveRidingSkill = 0;
                    effectiveRidingSkill = __instance.GetEffectiveSkill(agent.RiderAgent.Character, agent.RiderAgent.Origin, agent.RiderAgent.Formation, DefaultSkills.Riding);

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
            }
        }

        [HarmonyPatch(typeof(CustomBattleAgentStatCalculateModel))]
        [HarmonyPatch("UpdateAgentStats")]
        class CustomBattleAgentStatCalculateModelUpdateHorseStatsPatch
        {
            static void Postfix(ref SandboxAgentStatCalculateModel __instance, ref Agent agent, ref AgentDrivenProperties agentDrivenProperties)
            {
                if (!agent.IsHuman && agent.RiderAgent != null)
                {
                    int effectiveRidingSkill = 0;
                    effectiveRidingSkill = __instance.GetEffectiveSkill(agent.RiderAgent.Character, agent.RiderAgent.Origin, agent.RiderAgent.Formation, DefaultSkills.Riding);

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

                    float weightModifier = MathF.Pow(475f, 2) / MathF.Pow(mountElement.Weight + addedWeight, 2) ;

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
            }
        }

        [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
        [HarmonyPatch("DecideMountRearedByBlow")]
        class DecideMountRearedByBlowPatch
        {
            static bool Prefix(ref Mission __instance, Agent attackerAgent, Agent victimAgent, in AttackCollisionData collisionData, WeaponComponentData attackerWeapon, in Blow blow, ref bool __result)
            {
                //if(collisionData.InflictedDamage < 0)
                //{
                //    return true;
                //}
                //float damageMultiplierOfCombatDifficulty = Mission.Current.GetDamageMultiplierOfCombatDifficulty(victimAgent, attackerAgent);
                if (collisionData.IsMissile)
                {
                    if (victimAgent.IsMount && attackerAgent != null && victimAgent.GetAgentFlags().HasAnyFlag(AgentFlag.CanRear) && Vec3.DotProduct(blow.Direction, victimAgent.Frame.rotation.f) < -0.35f)
                    {
                        __result = (float)collisionData.InflictedDamage >= TaleWorlds.Core.ManagedParameters.Instance.GetManagedParameter(TaleWorlds.Core.ManagedParametersEnum.MakesRearAttackDamageThreshold) * 2f; /// damageMultiplierOfCombatDifficulty;
                        return false;
                    }
                    else
                    {
                        __result = false;
                        return false;
                    }
                }
                else
                {
                    if (attackerWeapon != null && (attackerWeapon.WeaponFlags.HasFlag(WeaponFlags.WideGrip) || attackerWeapon.WeaponClass == WeaponClass.OneHandedPolearm || attackerWeapon.WeaponClass == WeaponClass.TwoHandedPolearm || attackerWeapon.WeaponClass == WeaponClass.LowGripPolearm)
                    && blow.StrikeType == StrikeType.Thrust && attackerAgent != null && victimAgent.GetAgentFlags().HasAnyFlag(AgentFlag.CanRear) && Vec3.DotProduct(blow.Direction, victimAgent.Frame.rotation.f) < -0.35f)
                    {
                        //__result = (float)collisionData.InflictedDamage >= TaleWorlds.Core.ManagedParameters.Instance.GetManagedParameter(TaleWorlds.Core.ManagedParametersEnum.MakesRearAttackDamageThreshold) * damageMultiplierOfCombatDifficulty;
                        __result = (float)collisionData.InflictedDamage >= TaleWorlds.Core.ManagedParameters.Instance.GetManagedParameter(TaleWorlds.Core.ManagedParametersEnum.MakesRearAttackDamageThreshold); // * damageMultiplierOfCombatDifficulty;
                        return false;
                    }
                    __result = false;
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
        class DecideAgentDismountedByBlowPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("DecideAgentDismountedByBlow")]
            static bool PrefixDecideAgentDismountedByBlow(Agent attackerAgent, Agent victimAgent, in AttackCollisionData collisionData, WeaponComponentData attackerWeapon, ref Blow blow)
            {
                if (!blow.IsMissile)
                {
                    if (victimAgent != null && victimAgent.HasMount && victimAgent.Character != null && victimAgent.Origin != null)
                    {
                        int ridingSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(victimAgent.Character, victimAgent.Origin, victimAgent.Formation, DefaultSkills.Riding);
                        if (attackerWeapon != null && attackerWeapon.ItemUsage != null && blow.StrikeType == StrikeType.Thrust && blow.BaseMagnitude > (2.4f + (ridingSkill * 0.01f)) &&
                        (blow.VictimBodyPart == BoneBodyPartType.Head || blow.VictimBodyPart == BoneBodyPartType.Neck) &&
                        (attackerWeapon.ItemUsage.Equals("polearm_couch") || attackerWeapon.ItemUsage.Equals("polearm_bracing")))
                        {
                            blow.BlowFlag |= BlowFlags.CanDismount;
                            return false;
                        }
                        else if (attackerWeapon != null && attackerWeapon.ItemUsage != null && blow.StrikeType == StrikeType.Thrust && blow.BaseMagnitude > (3f + (ridingSkill * 0.01f)) &&
                        (blow.VictimBodyPart == BoneBodyPartType.Chest || blow.VictimBodyPart == BoneBodyPartType.ShoulderLeft || blow.VictimBodyPart == BoneBodyPartType.ShoulderRight) &&
                        (attackerWeapon.ItemUsage.Equals("polearm_couch") || attackerWeapon.ItemUsage.Equals("polearm_bracing")))
                        {
                            blow.BlowFlag |= BlowFlags.CanDismount;
                            return false;
                        }
                        else
                        {
                            return true;
                        }
                    }
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(CustomBattleAgentStatCalculateModel))]
        [HarmonyPatch("UpdateAgentStats")]
        class CustomBattleUpdateAgentStats
        {
            static void Postfix(Agent agent, AgentDrivenProperties agentDrivenProperties)
            {
                agentDrivenProperties.AttributeShieldMissileCollisionBodySizeAdder = 0.01f;

                if (!agent.IsHuman)
                {
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

        [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
        [HarmonyPatch("ComputeBlowMagnitudeFromHorseCharge")]
        class ChangeHorseDamageCalculation
        {
            static bool Prefix(in AttackInformation attackInformation, in AttackCollisionData acd, Vec2 attackerAgentVelocity, Vec2 victimAgentVelocity, out float baseMagnitude, out float specialMagnitude)
            {
                Vec2 chargerMovementDirection = attackInformation.AttackerAgentMovementDirection;
                Vec2 vec = chargerMovementDirection * Vec2.DotProduct(victimAgentVelocity, chargerMovementDirection);
                Vec2 vec2 = attackerAgentVelocity - vec;
                ref readonly Vec3 victimAgentPosition = ref attackInformation.VictimAgentPosition;
                float num = ChargeDamageDotProduct(victimAgentPosition, chargerMovementDirection, acd.CollisionGlobalPosition);
                float num2 = vec2.Length * num;
                baseMagnitude = (num2 * num2 * attackInformation.AttackerAgentMountChargeDamageProperty * 0.5f) / 520f;
                specialMagnitude = baseMagnitude;

                return false;
            }

            private static float ChargeDamageDotProduct(Vec3 victimPosition, Vec2 chargerMovementDirection, Vec3 collisionPoint)
            {
                float b = Vec2.DotProduct((victimPosition.AsVec2 - collisionPoint.AsVec2).Normalized(), chargerMovementDirection);
                return MathF.Max(0f, b);
            }
        }

        [HarmonyPatch(typeof(SandboxAgentStatCalculateModel))]
        [HarmonyPatch("UpdateHorseStats")]
        class ChangeHorseChargeBonus
        {
            static void Postfix(Agent agent, ref AgentDrivenProperties agentDrivenProperties)
            {
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
    }
}
