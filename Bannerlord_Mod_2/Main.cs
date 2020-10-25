using System;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Library;
using HarmonyLib;
using System.Xml;
using System.Collections.Generic;
using static TaleWorlds.MountAndBlade.ArrangementOrder;
using SandBox;
using System.Reflection;
using JetBrains.Annotations;
using System.Collections;
using System.Linq;
using static TaleWorlds.Core.ItemObject;
using TaleWorlds.Engine;

namespace RealisticBattle
{
    public static class Vars
    {
        public static Dictionary<string, float> dict = new Dictionary<string, float> { };
    }
    public static class MyPatcher
    {
        public static void DoPatching()
        {
            var harmony = new Harmony("com.pf.rb");
            harmony.PatchAll();
        }
    }
    public static class Utilities
    {
        public static int calculateMissileSpeed(float ammoWeight, MissionWeapon rangedWeapon, int drawWeight)
        {
            int calculatedMissileSpeed = 10;
            if (rangedWeapon.CurrentUsageItem.ItemUsage.Equals("bow"))
            {
                float drawlength = (28 * 0.0254f);
                double potentialEnergy = 0.5f * (drawWeight * 4.448f) * drawlength;
                calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt(((potentialEnergy * 2f) / ammoWeight) * 0.91f * ((ammoWeight * 3f) + 0.432f)));
            }
            else if (rangedWeapon.CurrentUsageItem.ItemUsage.Equals("long_bow"))
            {
                float drawlength = (30 * 0.0254f);
                double potentialEnergy = 0.5f * (drawWeight * 4.448f) * drawlength;
                calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt(((potentialEnergy * 2f) / ammoWeight) * 0.89f * ((ammoWeight * 3.3f) + 0.33f) * (1f + (0.416f - (0.0026 * drawWeight)))));
            }
            else if (rangedWeapon.CurrentUsageItem.ItemUsage.Equals("crossbow") || rangedWeapon.CurrentUsageItem.ItemUsage.Equals("crossbow_fast"))
            {
                float drawlength = (4.5f * 0.0254f);
                double potentialEnergy = 0.5f * (drawWeight * 4.448f) * drawlength;
                calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt(((potentialEnergy * 2f) / ammoWeight) * 0.45f));
            }
            return calculatedMissileSpeed;
        }

        public static int calculateThrowableSpeed(float ammoWeight)
        {
            int calculatedThrowingSpeed = (int)Math.Ceiling(Math.Sqrt(160f * 2f / ammoWeight));
            //calculatedThrowingSpeed += 7;
            return calculatedThrowingSpeed;
        }

        public static void assignThrowableMissileSpeed(MissionWeapon throwable, int index,  int correctiveMissileSpeed)
        {
            float ammoWeight = throwable.GetWeight() / throwable.Amount;
            int calculatedThrowingSpeed = Utilities.calculateThrowableSpeed(ammoWeight);
            PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
            property.DeclaringType.GetProperty("MissileSpeed");
            throwable.CurrentUsageIndex = index;
            calculatedThrowingSpeed += correctiveMissileSpeed;
            property.SetValue(throwable.CurrentUsageItem, calculatedThrowingSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
            throwable.CurrentUsageIndex = 0;
        }

        public static void assignStoneMissileSpeed(MissionWeapon throwable, int index)
        {
            PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
            property.DeclaringType.GetProperty("MissileSpeed");
            throwable.CurrentUsageIndex = index;
            property.SetValue(throwable.CurrentUsageItem, 25, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
            throwable.CurrentUsageIndex = 0;
        }

        public static bool HasBattleBeenJoined(Formation mainInfantry, bool hasBattleBeenJoined, float battleJoinRange)
        {
            if(mainInfantry != null)
            {
                FormationQuerySystem cslef = mainInfantry.QuerySystem.ClosestSignificantlyLargeEnemyFormation;
                if (cslef != null && ((!Utilities.CheckIfMountedSkirmishFormation(cslef.Formation)) || cslef.IsInfantryFormation || (cslef.IsRangedFormation && !cslef.IsRangedCavalryFormation) || (cslef.Formation.Team.Formations.Count() == 1)))
                {
                    if (mainInfantry.QuerySystem.MedianPosition.AsVec2.Distance(cslef.MedianPosition.AsVec2) / cslef.MovementSpeedMaximum <= 5f)
                    {
                        mainInfantry.FiringOrder = FiringOrder.FiringOrderHoldYourFire;
                    }
                    else
                    {
                        mainInfantry.FiringOrder = FiringOrder.FiringOrderFireAtWill;
                    }

                    if (cslef.IsInfantryFormation || cslef.IsRangedFormation)
                    {
                        return mainInfantry.QuerySystem.MedianPosition.AsVec2.Distance(cslef.MedianPosition.AsVec2) / cslef.MovementSpeedMaximum <= battleJoinRange + (hasBattleBeenJoined ? 5f : 0f);
                    }
                    else
                    {
                        return mainInfantry.QuerySystem.MedianPosition.AsVec2.Distance(cslef.MedianPosition.AsVec2) / (cslef.MovementSpeedMaximum * 0.6f) <= battleJoinRange + (hasBattleBeenJoined ? 5f : 0f);
                    }
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return true;
            }
            
        }

        public static void FixCharge(ref Formation formation)
        {
            if (formation != null)
            {
                formation.AI.ResetBehaviorWeights();
                formation.AI.SetBehaviorWeight<BehaviorCharge>(1f);
            }
        }

        public static bool CheckIfMountedSkirmishFormation(Formation formation)
        {
            if (formation != null && formation.QuerySystem.IsCavalryFormation)
            {
                int mountedSkirmishersCount = 0;
                PropertyInfo property = typeof(Formation).GetProperty("arrangement", BindingFlags.NonPublic | BindingFlags.Instance);
                if(property != null)
                {
                    property.DeclaringType.GetProperty("arrangement");
                    IFormationArrangement arrangement = (IFormationArrangement)property.GetValue(formation);

                    FieldInfo field = typeof(LineFormation).GetField("_allUnits", BindingFlags.NonPublic | BindingFlags.Instance);
                    if(field != null)
                    {
                        field.DeclaringType.GetField("_allUnits");
                        List<IFormationUnit> agents = (List<IFormationUnit>)field.GetValue(arrangement);

                        foreach (Agent agent in agents.ToList())
                        {
                            bool ismountedSkrimisher = false;
                            for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                            {
                                if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
                                {
                                    if (agent.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Thrown && agent.Equipment[equipmentIndex].Amount > 0 && agent.MountAgent != null)
                                    {
                                        ismountedSkrimisher = true;
                                    }
                                }
                            }
                            if (ismountedSkrimisher)
                            {
                                mountedSkirmishersCount++;
                            }
                        }

                        float mountedSkirmishersRatio = (float)mountedSkirmishersCount / (float)formation.CountOfUnits;
                        if (mountedSkirmishersRatio > 0.6f)
                        {
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                    }
                }
                return false;
            }
            else
            {
                return false;
            }
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

    [HarmonyPatch(typeof(Mission))]
    [HarmonyPatch("ComputeBlowMagnitudeFromHorseCharge")]
    class ChangeHorseDamageCalculation
    {
        static bool Prefix(ref AttackCollisionData acd, Vec3 attackerAgentMovementDirection, Vec3 attackerAgentVelocity, float agentMountChargeDamageProperty, Vec3 victimAgentVelocity, Vec3 victimAgentPosition, out float baseMagnitude, out float specialMagnitude)
        {
            Vec3 v = victimAgentVelocity.ProjectOnUnitVector(attackerAgentMovementDirection);
            Vec3 vec = attackerAgentVelocity - v;
            float num = ChargeDamageDotProduct(victimAgentPosition, attackerAgentMovementDirection, acd.CollisionGlobalPosition);
            float num2 = vec.Length * num;
            baseMagnitude = (num2 * num2 * num * agentMountChargeDamageProperty) / 2500f;
            specialMagnitude = baseMagnitude;

            return false;
        }

        private static float ChargeDamageDotProduct(Vec3 victimPosition, Vec3 chargerMovementDirection, Vec3 collisionPoint)
        {
            Vec2 va = victimPosition.AsVec2 - collisionPoint.AsVec2;
            va.Normalize();
            Vec2 asVec = chargerMovementDirection.AsVec2;
            return Vec2.DotProduct(va, asVec);
        }
    }

    [HarmonyPatch(typeof(CustomBattleAgentStatCalculateModel))]
    [HarmonyPatch("UpdateAgentStats")]
    class ShieldCollisionFix
    {
        static void Postfix( Agent agent, AgentDrivenProperties agentDrivenProperties)
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

    [HarmonyPatch(typeof(Agent))]
    [HarmonyPatch("GetBaseArmorEffectivenessForBodyPart")]
    class ChangeBodyPartArmor
    {
        static bool Prefix(Agent __instance, BoneBodyPartType bodyPart, ref float __result)
        {

            if (!__instance.IsHuman)
            {
                __result = __instance.GetAgentDrivenPropertyValue(DrivenProperty.ArmorTorso);
                return false;
            }
            switch (bodyPart)
            {
                case BoneBodyPartType.None:
                    {
                        __result = 0f;
                        break;
                    }
                case BoneBodyPartType.Head:
                    {
                        __result = __instance.GetAgentDrivenPropertyValue(DrivenProperty.ArmorHead);
                        break;
                    }
                case BoneBodyPartType.Neck:
                    {
                        // __result = getNeckArmor(__instance);
                        __result = __instance.GetAgentDrivenPropertyValue(DrivenProperty.ArmorHead) * 0.8f;
                        break;
                    }
                case BoneBodyPartType.BipedalLegs:
                case BoneBodyPartType.QuadrupedalLegs:
                    {
                        __result = getLegArmor(__instance);
                        //__result = __instance.GetAgentDrivenPropertyValue(DrivenProperty.ArmorLegs) * 0.5f;
                        break;
                    }
                case BoneBodyPartType.BipedalArmLeft:
                case BoneBodyPartType.BipedalArmRight:
                case BoneBodyPartType.QuadrupedalArmLeft:
                case BoneBodyPartType.QuadrupedalArmRight:
                    {
                        __result = getArmArmor(__instance);
                        break;
                    }
                case BoneBodyPartType.Chest:
                    {
                        __result = getMyChestArmor(__instance);
                        break;
                    }
                case BoneBodyPartType.ShoulderLeft:
                case BoneBodyPartType.ShoulderRight:
                    {
                        __result = getShoulderArmor(__instance);
                        break;
                    }
                case BoneBodyPartType.Abdomen:
                    {
                        __result = getAbdomenArmor(__instance);
                        break;
                    }
                default:
                    {
                        _ = 3;
                        __result = 3f;
                        break;
                    }
            }
            return false;
        }

        static public float getNeckArmor(Agent agent)
        {
            float num = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.Cape)
                {
                    num += (float)equipmentElement.Item.ArmorComponent.BodyArmor;
                }
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                {
                    num += (float)equipmentElement.Item.ArmorComponent.ArmArmor;
                }
            }
            return num;
        }

        static public float getShoulderArmor(Agent agent)
        {
            float num = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.Cape)
                {
                    num += (float)equipmentElement.Item.ArmorComponent.BodyArmor;
                    num += (float)equipmentElement.Item.ArmorComponent.ArmArmor;
                }
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                {
                    num += (float)equipmentElement.Item.ArmorComponent.ArmArmor;
                }
            }
            return num;
        }

        static public float getAbdomenArmor(Agent agent)
        {
            float num = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                {
                    num += (float)equipmentElement.Item.ArmorComponent.BodyArmor;
                }
            }
            return num;
        }

        static public float getMyChestArmor(Agent agent)
        {
            float num = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                {
                    num += (float)equipmentElement.Item.ArmorComponent.BodyArmor;
                }
            }
            return num;
        }

        static public float getArmArmor(Agent agent)
        {
            //float num = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HandArmor)
                {
                    return (float)equipmentElement.Item.ArmorComponent.ArmArmor;
                }
            }
            return 0f;
        }

        static public float getLegArmor(Agent agent)
        {
            float num = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.LegArmor)
                {
                    num += ((float)equipmentElement.Item.ArmorComponent.LegArmor) * 0.5f;
                }
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                {
                    num += ((float)equipmentElement.Item.ArmorComponent.LegArmor) * 0.5f;
                }
            }
            return num;
        }
    }

    //[HarmonyPatch(typeof(Formation))]
    //[HarmonyPatch("UpdateAgentDrivenPropertiesBasedOnOrderDefensiveness")]
    //class ChangeDefensivness
    //{
    //    static bool Prefix(Formation __instance)
    //    {
    //        __instance.ApplyActionOnEachUnit(delegate (Agent agent)
    //        {
    //            agent.Defensiveness = 2.1f;
    //        });
    //        return false;
    //    }

    //}

    [HarmonyPatch(typeof(MissionState))]
    [HarmonyPatch("FinishMissionLoading")]
    class MissionLoadChangeParameters
    {
        static void Postfix()
        {
            ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionArrow, 0.0025f);
            ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionJavelin, 0.0025f);
            ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionAxe, 0.01f);
            ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionKnife, 0.01f);
            ManagedParameters.SetParameter(ManagedParametersEnum.MissileMinimumDamageToStick, 20);
        }
    }

    [HarmonyPatch(typeof(ArrangementOrder))]
    [HarmonyPatch("GetShieldDirectionOfUnit")]
    class HoldTheDoor
    {
        static void Postfix(ref Agent.UsageDirection __result, Formation formation, Agent unit, ArrangementOrderEnum orderEnum)
        {
            if(!formation.QuerySystem.IsCavalryFormation && !formation.QuerySystem.IsRangedCavalryFormation)
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
                            else if((currentTime - lastRangedHit < 10f) || formation.QuerySystem.UnderRangedAttackRatio >= 0.1f)
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

    [HarmonyPatch(typeof(AgentStatCalculateModel))]
    [HarmonyPatch("SetAiRelatedProperties")]
    class OverrideSetAiRelatedProperties
    {
        static void Postfix(Agent agent, ref AgentDrivenProperties agentDrivenProperties, WeaponComponentData equippedItem, WeaponComponentData secondaryItem)
        {
            int meleeSkill = GetMeleeSkill(agent, equippedItem, secondaryItem);
            SkillObject skill = (equippedItem == null) ? DefaultSkills.Athletics : equippedItem.RelevantSkill;
            int effectiveSkill = GetEffectiveSkill(agent.Character, agent.Origin, agent.Formation, skill);
            float meleeLevel = CalculateAILevel(agent, meleeSkill);                 //num
            float effectiveSkillLevel = CalculateAILevel(agent, effectiveSkill);    //num2
            float meleeDefensivness = meleeLevel + agent.Defensiveness;             //num3
            //mission.GetNearbyAllyAgents(AveragePosition, 30f, formation.Team), 5f);
            //agentDrivenProperties.AiCheckMovementIntervalFactor = (1f - num) * 0.1f;
            //agentDrivenProperties.AiMoveEnemySideTimeValue = -1.5f;
            //agentDrivenProperties.AiMovemetDelayFactor = 1f;
            //agentDrivenProperties.AiAttackCalculationMaxTimeFactor = 0.85f;
            agentDrivenProperties.AiChargeHorsebackTargetDistFactor = 4f;
            agentDrivenProperties.AIBlockOnDecideAbility = MBMath.ClampFloat(meleeLevel + 0.15f, 0f, 0.95f);
            agentDrivenProperties.AIParryOnDecideAbility = MBMath.ClampFloat((meleeLevel * 0.35f) + 0.15f, 0.1f, 0.95f);
            agentDrivenProperties.AIRealizeBlockingFromIncorrectSideAbility = MBMath.ClampFloat(meleeLevel + 0.1f, 0f, 0.95f);
            agentDrivenProperties.AIDecideOnAttackChance = MBMath.ClampFloat(meleeLevel + 0.1f, 0f, 0.95f);
            agentDrivenProperties.AIDecideOnRealizeEnemyBlockingAttackAbility = MBMath.ClampFloat(meleeLevel + 0.1f, 0f, 0.95f);
            //agentDrivenProperties.AiTryChamberAttackOnDecide = 100f;
            //agentDrivenProperties.AiWaitBeforeShootFactor = 1f;
            //agentDrivenProperties.AiShootFreq = 1f;
            //agentDrivenProperties.AiDecideOnAttackContinueAction = 1f;
            //agentDrivenProperties.AiDecideOnAttackingContinue = 1f; // continuing succesfull attack when enemy is facing other way, 1 = full
            //agentDrivenProperties.AiDecideOnAttackWhenReceiveHitTiming = 0f;
            //agentDrivenProperties.AIDecideOnAttackChance = 2f; // aggresion, when enemy is facing other way, 1 = full
            //agentDrivenProperties.AiDecideOnAttackWhenReceiveHitTiming = -0.25f + (1f - num);
            //agentDrivenProperties.AiDecideOnAttackContinueAction = -0.5f + (1f - num);
            //agentDrivenProperties.AIAttackOnParryChance = 2f; // counter-attack after succesfull parry chance, does not apply to shield block only parry, does not apply to crash through parry, 2 is very high maybe 80%
            //agentDrivenProperties.AiAttackOnParryTiming = 0f;
            //agentDrivenProperties.AIParryOnDecideAbility = 0.2f; // speed of parry reaction, depends on enemy attack speed, 0.2 = high parry chance, 0.1 = almost nothing parried, 0.15 decent parry but vulnurable to player spam, this is general chance to parry - it can be still in wrong direction, parry aplies only to oponent AI is facing, other enemies are ignored
            //agentDrivenProperties.AIParryOnAttackAbility = 0.8f;
            //agentDrivenProperties.AiAttackCalculationMaxTimeFactor = 0.1f; // ???
            //agentDrivenProperties.AIAttackOnDecideChance = 0.9f; // ???
            //agentDrivenProperties.AiMinimumDistanceToContinueFactor = 10f;
            agentDrivenProperties.AiRangedHorsebackMissileRange = 0.7f;
            agentDrivenProperties.AiUseShieldAgainstEnemyMissileProbability = 0.95f;
	        agentDrivenProperties.AiFlyingMissileCheckRadius = 250f;
        }

        static protected float CalculateAILevel(Agent agent, int relevantSkillLevel)
        {
            float difficultyModifier = 1f;
            return MBMath.ClampFloat((float)relevantSkillLevel / 250f * difficultyModifier, 0f, 1f);
        }

        static protected int GetMeleeSkill(Agent agent, WeaponComponentData equippedItem, WeaponComponentData secondaryItem)
        {
            SkillObject skill = DefaultSkills.Athletics;
            if (equippedItem != null)
            {
                SkillObject relevantSkill = equippedItem.RelevantSkill;
                skill = ((relevantSkill == DefaultSkills.OneHanded || relevantSkill == DefaultSkills.Polearm) ? relevantSkill : ((relevantSkill != DefaultSkills.TwoHanded) ? DefaultSkills.OneHanded : ((secondaryItem == null) ? DefaultSkills.TwoHanded : DefaultSkills.OneHanded)));
            }
            return GetEffectiveSkill(agent.Character, agent.Origin, agent.Formation, skill);
        }

        static protected int GetEffectiveSkill(BasicCharacterObject agentCharacter, IAgentOriginBase agentOrigin, Formation agentFormation, SkillObject skill)
        {
            return agentCharacter.GetSkillValue(skill);
        }
    }

    [HarmonyPatch(typeof(CombatStatCalculator))]
    [HarmonyPatch("CalculateStrikeMagnitudeForPassiveUsage")]
    class ChangeLanceDamage
    {
        static bool Prefix(float weaponWeight, float extraLinearSpeed, ref float __result)
        {
            //float weaponWeight2 = 40f + weaponWeight;
            __result = CalculateStrikeMagnitudeForThrust(0f, weaponWeight, extraLinearSpeed, isThrown: false);
            return false;
        }

        private static float CalculateStrikeMagnitudeForThrust(float thrustWeaponSpeed, float weaponWeight, float extraLinearSpeed, bool isThrown)
        {
            float num = extraLinearSpeed;
            if (!isThrown)
            {
            weaponWeight += 0f;
            }
            float num2 = 0.5f * weaponWeight * num * num * 0.315f;
            if (num2 > (weaponWeight * 35.0f))
            {
            num2 = weaponWeight * 35.0f;
            }
            return num2;
            
        }
    }

    [HarmonyPatch(typeof(Mission))]
    [HarmonyPatch("ComputeBlowMagnitudeMissile")]
    class RealArrowDamage
    {

        static bool Prefix(ref AttackCollisionData acd, ItemObject weaponItem, bool isVictimAgentNull, float momentumRemaining, float missileTotalDamage, out float baseMagnitude, out float specialMagnitude, Vec3 victimVel)
        {

            //Vec3 gcn = acd.CollisionGlobalNormal;
            // Vec3 wbd = acd.MissileVelocity;

            //float angleModifier = Vec3.DotProduct(gcn, wbd);

            //Vec3 resultVec = gcn + wbd;
            //float angleModifier = 1f - Math.Abs((resultVec.x + resultVec.y + resultVec.z) / 3);

            float length;
            if (!isVictimAgentNull)
            {
                length = (victimVel - acd.MissileVelocity).Length;
            }
            else
            {
                length = acd.MissileVelocity.Length;
            }
            //float expr_32 = length / acd.MissileStartingBaseSpeed;
            //float num = expr_32 * expr_32;

            if (weaponItem != null && weaponItem.PrimaryWeapon != null)
            {
                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Boulder") ||
                    weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Stone"))
                {
                    missileTotalDamage *= 0.01f;
                }
                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("ThrowingAxe") ||
                    weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("ThrowingKnife"))
                {
                    //length += -(7.0f);
                    //if (length < 5.0f)
                    //{
                    //    length = 5.0f;
                    //} 
                    missileTotalDamage *= 0.007f;
                }
                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Javelin"))
                {
                    length -= 10f;
                    if (length < 5.0f)
                    {
                        length = 5f;
                    }
                    //missileTotalDamage += 168.0f;
                    missileTotalDamage *= 0.01f;
                }
                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("OneHandedPolearm"))
                {
                    length -= 5f;
                    if (length < 5.0f)
                    {
                        length = 5f;
                    }
                    missileTotalDamage *= 0.006f;
                }
                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("LowGripPolearm"))
                {
                    length -= 5f;
                    if (length < 5.0f)
                    {
                        length = 5f;
                    }
                    missileTotalDamage *= 0.006f;
                }
                else
                {
                    if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Arrow"))
                    {
                        missileTotalDamage -= 10f;
                        missileTotalDamage *= 0.01f;
                    }
                    if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Bolt"))
                    {
                        missileTotalDamage -= 10f;
                        missileTotalDamage *= 0.01f;
                    }
                }
            }

            float physicalDamage = ((length * length) * (weaponItem.Weight)) / 2;

            if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Javelin") && physicalDamage > (weaponItem.Weight) * 200f)
            {
                physicalDamage = (weaponItem.Weight) * 200f;
            }

            if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("OneHandedPolearm") && physicalDamage > (weaponItem.Weight) * 150f)
            {
                physicalDamage = (weaponItem.Weight) * 150f;
            }

            if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Arrow") && physicalDamage > (weaponItem.Weight) * 2000f)
            {
                physicalDamage = (weaponItem.Weight) * 2500f;
            }

            if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Bolt") && physicalDamage > (weaponItem.Weight) * 2500f)
            {
                physicalDamage = (weaponItem.Weight) * 2500f;
            }

            //float distnace = (acd.MissileStartingPosition - acd.CollisionGlobalPosition).Length;
            //InformationManager.DisplayMessage(new InformationMessage("Ek:" + physicalDamage + " modif:" + missileTotalDamage + " speed:" + length + " dist:" + distnace));
            // baseMagnitude = physicalDamage * missileTotalDamage * momentumRemaining * angleModifier;
            baseMagnitude = physicalDamage * missileTotalDamage * momentumRemaining;
            specialMagnitude = baseMagnitude;

            return false;
        }
    }

    //[HarmonyPatch(typeof(MissionEquipment))]
    //[HarmonyPatch("GetLongestRangedWeaponWithAimingError")]
    //class OverrideGetLongestRangedWeaponWithAimingError
    //{

    //    static bool Prefix(MissionEquipment __instance, ref int __result, out float inaccuracy, ref Agent agent, MissionWeapon[] ____weaponSlots)
    //    {
    //        int result = -1;
    //        float num = -1f;
    //        inaccuracy = -1f;
    //        for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
    //        {
    //            if (__instance.HasAmmo(equipmentIndex, out int rangedUsageIndex, out bool hasLoadedAmmo) && (hasLoadedAmmo || !agent.HasMount || !____weaponSlots[(int)equipmentIndex].GetWeaponComponentDataForUsage(rangedUsageIndex).WeaponFlags.HasAnyFlag(WeaponFlags.CantReloadOnHorseback)))
    //            {
    //                int modifiedMissileSpeedForUsage = ____weaponSlots[(int)equipmentIndex].GetModifiedMissileSpeedForUsage(rangedUsageIndex);
    //                float num2 = (float)modifiedMissileSpeedForUsage * 0.7071067f;
    //                float num3 = num2 * 0.1019367f;
    //                float num4 = num2 * num3 * 0.5f;
    //                float num5 = (float)Math.Sqrt(2f * num4 * 0.1019367f);
    //                float num6 = num3 + num5;
    //                float val = num2 * num6;
    //                WeaponComponentData weaponComponentDataForUsage = ____weaponSlots[(int)equipmentIndex].GetWeaponComponentDataForUsage(rangedUsageIndex);
    //                int effectiveSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(agent.Character, agent.Origin, agent.Formation, weaponComponentDataForUsage.RelevantSkill);
    //                float weaponInaccuracy = MissionGameModels.Current.AgentStatCalculateModel.GetWeaponInaccuracy(agent, weaponComponentDataForUsage, effectiveSkill);
    //                float x = Math.Max(0.5f / (float)modifiedMissileSpeedForUsage, weaponInaccuracy * 0.5f);
    //                val = Math.Min(2.5f / TaleWorlds.Library.MathF.Tan(x), val);
    //                if (num < val)
    //                {
    //                    inaccuracy = weaponInaccuracy;
    //                    num = val;
    //                    result = (int)equipmentIndex;
    //                }                                                   
    //            }
    //        }
    //        __result = result;
    //        return false;
    //    }
    //}

    //[HarmonyPatch(typeof(Agent))]
    //[HarmonyPatch("GetHasRangedWeapon")]
    //class OverrideGetHasRangedWeapon
    //{
    //    /*
    //    static bool Prefix(Agent __instance, ref bool __result, bool checkHasAmmo = false)
    //    {
    //         __result = false;
    //         for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
    //         {
    //             bool isPolearm = false;
    //             //bool isjavelin = false;
    //             foreach (WeaponComponentData weapon in __instance.Equipment[equipmentIndex].Weapons)
    //             {
    //                 if (weapon.WeaponClass == WeaponClass.LowGripPolearm || weapon.WeaponClass == WeaponClass.OneHandedPolearm || weapon.WeaponClass == WeaponClass.Javelin || weapon.WeaponClass == WeaponClass.ThrowingAxe || weapon.WeaponClass == WeaponClass.ThrowingKnife || weapon.WeaponClass == WeaponClass.Stone)
    //                 {
    //                     isPolearm = true;
    //                 }
    //                 if (weapon != null && weapon.IsRangedWeapon && (!checkHasAmmo || __instance.Equipment.HasAmmo(weapon)))
    //                 {
    //                     if (!isPolearm)
    //                     {
    //                         __result = true;
    //                     }
    //                 }
    //             }
    //         }
    //         return false;
    //     }
    //     */
    //}

    [HarmonyPatch(typeof(Agent))]
    [HarmonyPatch("EquipItemsFromSpawnEquipment")]
    class OverrideEquipItemsFromSpawnEquipment
    {

        private static ArrayList _oldMissileSpeeds = new ArrayList();
        static bool Prefix(Agent __instance)
        {

            ArrayList stringRangedWeapons = new ArrayList();
            //MissionWeapon bow = MissionWeapon.Invalid;
            MissionWeapon arrow = MissionWeapon.Invalid;
            bool firstProjectile = true;

            for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
            {
                if (__instance.Equipment != null && !__instance.Equipment[equipmentIndex].IsEmpty)
                {
                    WeaponStatsData[] wsd = __instance.Equipment[equipmentIndex].GetWeaponStatsData();
                    if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                    {
                        stringRangedWeapons.Add(__instance.Equipment[equipmentIndex]);
                    }
                    if ((wsd[0].WeaponClass == (int)WeaponClass.Arrow) || (wsd[0].WeaponClass == (int)WeaponClass.Bolt))
                    {
                        if (firstProjectile)
                        {
                            arrow = __instance.Equipment[equipmentIndex];
                            firstProjectile = false;
                        }
                    }
                    if ((wsd[0].WeaponClass == (int)WeaponClass.OneHandedPolearm) || (wsd[0].WeaponClass == (int)WeaponClass.LowGripPolearm))
                    {
                        for (int i = 0; i < wsd.Length; i++)
                        {
                            if (wsd[i].MissileSpeed != 0)
                            {
                                Utilities.assignThrowableMissileSpeed(__instance.Equipment[equipmentIndex], i, 5);
                            }
                        }
                    }
                    if  (wsd[0].WeaponClass == (int)WeaponClass.Javelin)
                    {
                        for(int i=0; i < wsd.Length; i++)
                        {
                            if(wsd[i].MissileSpeed != 0)
                            {
                                Utilities.assignThrowableMissileSpeed(__instance.Equipment[equipmentIndex], i, 10);
                            }
                        }
                    }
                    if ((wsd[0].WeaponClass == (int)WeaponClass.ThrowingAxe) || (wsd[0].WeaponClass == (int)WeaponClass.ThrowingKnife))
                    {
                        for (int i = 0; i < wsd.Length; i++)
                        {
                            if (wsd[i].MissileSpeed != 0)
                            {
                                Utilities.assignThrowableMissileSpeed(__instance.Equipment[equipmentIndex], i, 0);
                            }
                        }
                    }
                    if (wsd[0].WeaponClass == (int)WeaponClass.Stone)
                    {
                        for (int i = 0; i < wsd.Length; i++)
                        {
                            if (wsd[i].MissileSpeed != 0)
                            {
                                Utilities.assignStoneMissileSpeed(__instance.Equipment[equipmentIndex], i);
                            }
                        }
                    }
                }
            }
            foreach (MissionWeapon missionWeapon in stringRangedWeapons){ 
                int calculatedMissileSpeed = 50;
                if (!missionWeapon.Equals(MissionWeapon.Invalid) && !arrow.Equals(MissionWeapon.Invalid))
                {
                    if(missionWeapon.ItemModifier != null)
                    {
                        FieldInfo field = typeof(ItemModifier).GetField("_missileSpeed", BindingFlags.NonPublic | BindingFlags.Instance);
                        field.DeclaringType.GetField("_missileSpeed");
                        int missileSpeedModifier  = (int)field.GetValue(missionWeapon.ItemModifier);

                        _oldMissileSpeeds.Add(missionWeapon.GetModifiedMissileSpeedForUsage(0) - missileSpeedModifier);

                    }
                    else
                    {
                        _oldMissileSpeeds.Add(missionWeapon.GetModifiedMissileSpeedForUsage(0));

                    }
                    float ammoWeight = arrow.GetWeight() / arrow.Amount;
                    calculatedMissileSpeed = Utilities.calculateMissileSpeed(ammoWeight, missionWeapon, missionWeapon.GetModifiedMissileSpeedForUsage(0));

                    PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                    property.DeclaringType.GetProperty("MissileSpeed");
                    property.SetValue(missionWeapon.CurrentUsageItem, calculatedMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                }
                else if (!missionWeapon.Equals(MissionWeapon.Invalid))
                {
                    if (missionWeapon.ItemModifier != null)
                    {
                        FieldInfo field = typeof(ItemModifier).GetField("_missileSpeed", BindingFlags.NonPublic | BindingFlags.Instance);
                        field.DeclaringType.GetField("_missileSpeed");
                        int missileSpeedModifier = (int)field.GetValue(missionWeapon.ItemModifier);

                        _oldMissileSpeeds.Add(missionWeapon.GetModifiedMissileSpeedForUsage(0) - missileSpeedModifier);

                    }
                    else
                    {
                        _oldMissileSpeeds.Add(missionWeapon.GetModifiedMissileSpeedForUsage(0));

                    }
                    PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                    property.DeclaringType.GetProperty("MissileSpeed");
                    property.SetValue(missionWeapon.CurrentUsageItem, calculatedMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                }
            }

            return true;
        }
        static void Postfix(Agent __instance)
        {
            int i = 0;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
            {
                
                if (__instance.Equipment != null && !__instance.Equipment[equipmentIndex].IsEmpty)
                {
                    WeaponStatsData[] wsd = __instance.Equipment[equipmentIndex].GetWeaponStatsData();
                    if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                    {
                        MissionWeapon missionWeapon = __instance.Equipment[equipmentIndex];

                        PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                        property.DeclaringType.GetProperty("MissileSpeed");
                        property.SetValue(missionWeapon.CurrentUsageItem, _oldMissileSpeeds.ToArray()[i], BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                        i++;
                    }
                }
            }
            _oldMissileSpeeds.Clear();
        }
    }


    [HarmonyPatch(typeof(Mission))]
    [HarmonyPatch("OnAgentShootMissile")]
    [UsedImplicitly]
    [MBCallback]
    class OverrideOnAgentShootMissile
    {

        private static int _oldMissileSpeed;
        static bool Prefix(Agent shooterAgent, EquipmentIndex weaponIndex, Vec3 position, ref Vec3 velocity, Mat3 orientation, bool hasRigidBody, bool isPrimaryWeaponShot, int forcedMissileIndex, Mission __instance)
        {
            MissionWeapon missionWeapon = shooterAgent.Equipment[weaponIndex];

            //WeaponData wd = missionWeapon.GetWeaponData(needBatchedVersionForMeshes: true);
            WeaponStatsData[] wsd = missionWeapon.GetWeaponStatsData();

            if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
            {
                if (missionWeapon.ItemModifier != null)
                {
                    FieldInfo field = typeof(ItemModifier).GetField("_missileSpeed", BindingFlags.NonPublic | BindingFlags.Instance);
                    field.DeclaringType.GetField("_missileSpeed");
                    int missileSpeedModifier = (int)field.GetValue(missionWeapon.ItemModifier);

                    _oldMissileSpeed = missionWeapon.GetModifiedMissileSpeedForUsage(0) - missileSpeedModifier;

                }
                else
                {
                    _oldMissileSpeed = missionWeapon.GetModifiedMissileSpeedForUsage(0);

                }
                float ammoWeight = missionWeapon.AmmoWeapon.GetWeight();

                int calculatedMissileSpeed = Utilities.calculateMissileSpeed(ammoWeight, missionWeapon, missionWeapon.GetModifiedMissileSpeedForUsage(0));

               // float physicModifier = calculatedMissileSpeed + shooterAgent.Velocity.Length;

                Vec3 shooterAgentVelocity = new Vec3(shooterAgent.Velocity, -1);
                Vec3 myVelocity = new Vec3(velocity, -1);

                myVelocity.Normalize();

                float shooterAgentSpeed = Vec3.DotProduct(shooterAgentVelocity, myVelocity);

                Vec3 modifierVec = shooterAgentVelocity + myVelocity;
                //float modifierAngle = 1 - (modifierVec.x + modifierVec.y + modifierVec.z) / 3;

                velocity.x = myVelocity.x * (calculatedMissileSpeed + shooterAgentSpeed);
                velocity.y = myVelocity.y * (calculatedMissileSpeed + shooterAgentSpeed);
                velocity.z = myVelocity.z * (calculatedMissileSpeed + shooterAgentSpeed);
                //float direction = velocity.Normalize();

                //velocity.x = (shooterAgent.Velocity.x + velocity.x) * physicModifier;
                //velocity.y = (shooterAgent.Velocity.y + velocity.y) * physicModifier;
                //velocity.z = (shooterAgent.Velocity.z + velocity.z) * physicModifier;

                PropertyInfo property2 = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                property2.DeclaringType.GetProperty("MissileSpeed");
                property2.SetValue(shooterAgent.Equipment[weaponIndex].CurrentUsageItem, calculatedMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);

                //missionWeapon = shooterAgent.Equipment[weaponIndex];

               // wd = missionWeapon.GetWeaponData(needBatchedVersionForMeshes: true);
               // wsd = missionWeapon.GetWeaponStatsData();

               // WeaponData awd = WeaponData.InvalidWeaponData;

                //MethodInfo method = typeof(Agent).GetMethod("WeaponEquipped", BindingFlags.NonPublic | BindingFlags.Instance);
                //method.DeclaringType.GetMethod("WeaponEquipped");
                //method.Invoke(shooterAgent, new object[] { weaponIndex, wd, wsd, awd, null, null, true, true });
                //wd.DeinitializeManagedPointers();

                //shooterAgent.TryToWieldWeaponInSlot(weaponIndex, WeaponWieldActionType.Instant, false);

                //shooterAgent.UpdateAgentProperties();

            }
            return true;
        }

        static void Postfix(Agent shooterAgent, EquipmentIndex weaponIndex, Vec3 position, Vec3 velocity, Mat3 orientation, bool hasRigidBody, bool isPrimaryWeaponShot, int forcedMissileIndex, Mission __instance)
        {
            MissionWeapon missionWeapon = shooterAgent.Equipment[weaponIndex];
            WeaponStatsData[] wsd = missionWeapon.GetWeaponStatsData();
            if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
            {
                PropertyInfo property2 = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                property2.DeclaringType.GetProperty("MissileSpeed");
                property2.SetValue(shooterAgent.Equipment[weaponIndex].CurrentUsageItem, _oldMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
            }
        }
    }

    [HarmonyPatch(typeof(Mission))]
    [HarmonyPatch("ComputeBlowDamage")]
    class OverrideDamageCalc
    {
        static bool Prefix(ref AttackInformation attackInformation, ref AttackCollisionData attackCollisionData, in MissionWeapon attackerWeapon, DamageTypes damageType, float magnitude, int speedBonus, bool cancelDamage, out int inflictedDamage, out int absorbedByArmor)
        {
            float armorAmountFloat = attackInformation.ArmorAmountFloat;
            WeaponComponentData shieldOnBack = attackInformation.ShieldOnBack;
            float victimAgentAbsorbedDamageRatio = attackInformation.VictimAgentAbsorbedDamageRatio;
            float damageMultiplierOfBone = attackInformation.DamageMultiplierOfBone;
            float combatDifficultyMultiplier = attackInformation.CombatDifficultyMultiplier;
            bool attackBlockedWithShield = attackCollisionData.AttackBlockedWithShield;
            bool collidedWithShieldOnBack = attackCollisionData.CollidedWithShieldOnBack;
            bool isFallDamage = attackCollisionData.IsFallDamage;

            float armorAmount = 0f;

            if (!isFallDamage)
            {
                int num = (int)armorAmountFloat;
                armorAmount = num;
            }
            else
            {
                armorAmount = 0;
            }
            float num2 = (float)armorAmount;
            if (collidedWithShieldOnBack && shieldOnBack != null)
            {
                num2 += 10f;
            }

            string weaponType = "otherDamage";
            if (attackerWeapon.Item != null && attackerWeapon.Item.PrimaryWeapon != null)
            {
                weaponType = attackerWeapon.Item.PrimaryWeapon.WeaponClass.ToString();
            }

            //InformationManager.DisplayMessage(new InformationMessage("weapon type: " + weaponType));

            float num3 = MBMath.ClampInt((int)MyComputeDamage(weaponType, damageType, magnitude, num2, victimAgentAbsorbedDamageRatio), 0, 2000);
            float num4 = 1f;

            if (!attackBlockedWithShield && !isFallDamage)
            {
                if(damageMultiplierOfBone == 2f)
                {
                    num4 *= 1.5f;
                }
                else
                {
                    num4 *= damageMultiplierOfBone;
                }
                num4 *= combatDifficultyMultiplier;
            }

            num3 *= num4;
            
            inflictedDamage = MBMath.ClampInt((int)num3, 0, 2000);

            int num5 = MBMath.ClampInt((int)(MyComputeDamage(weaponType, damageType, magnitude, 0f, victimAgentAbsorbedDamageRatio) * num4), 0, 2000);
            absorbedByArmor = num5 - inflictedDamage;

            //InformationManager.DisplayMessage(new InformationMessage(weaponType + " dmg:" + inflictedDamage + " absArmor:" + absorbedByArmor));

            return false;
        }

        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("ComputeBlowDamageOnShield")]
        class OverrideDamageCalcShield
        {
            static bool Prefix(bool isAttackerAgentNull, bool isAttackerAgentActive, bool isAttackerAgentDoingPassiveAttack, bool canGiveDamageToAgentShield, bool isVictimAgentLeftStance, MissionWeapon victimShield, ref AttackCollisionData attackCollisionData, WeaponComponentData attackerWeapon, float blowMagnitude)
            {
                attackCollisionData.InflictedDamage = 0;
                if (victimShield.CurrentUsageItem.WeaponFlags.HasAnyFlag(WeaponFlags.CanBlockRanged) & canGiveDamageToAgentShield)
                {
                    DamageTypes damageType = (DamageTypes)attackCollisionData.DamageType;
                    int shieldArmorForCurrentUsage = victimShield.GetGetModifiedArmorForCurrentUsage();
                    float absorbedDamageRatio = 1f;

                    string weaponType = "otherDamage";
                    if (attackerWeapon != null)
                    {
                        weaponType = attackerWeapon.WeaponClass.ToString();
                    }

                    float num = MyComputeDamage(weaponType, damageType, blowMagnitude, (float)shieldArmorForCurrentUsage, absorbedDamageRatio);

                    if (attackCollisionData.IsMissile)
                    {
                        switch (weaponType)
                        {
                            case "Arrow":
                                {
                                    num *= 0.5f;
                                    break;
                                }
                            case "Bolt":
                                {
                                    num *= 0.5f;
                                    break;
                                }
                            case "Javelin":
                                {
                                    num *= 2.5f;
                                    break;
                                }
                            case "ThrowingAxe":
                                {
                                    num *= 1.0f;
                                    break;
                                }
                            case "OneHandedPolearm":
                                {
                                    num *= 100.0f;
                                    break;
                                }
                            case "LowGripPolearm":
                                {
                                    num *= 100.0f;
                                    break;
                                }
                            default:
                                {
                                    num *= 0.1f;
                                    break;
                                }
                        }
                    }
                    else if (attackCollisionData.DamageType == 1)
                    {
                        num *= 1.5f;
                    }
                    else if (attackCollisionData.DamageType == 2)
                    {
                        num *= 1.5f;
                    }
                    if (attackerWeapon != null && attackerWeapon.WeaponFlags.HasAnyFlag(WeaponFlags.BonusAgainstShield))
                    {
                        num *= 2.5f;
                    }
                    if (num > 0f)
                    {
                        if (!isVictimAgentLeftStance)
                        {
                            num *= ManagedParameters.Instance.GetManagedParameter(ManagedParametersEnum.ShieldRightStanceBlockDamageMultiplier);
                        }
                        if (attackCollisionData.CorrectSideShieldBlock)
                        {
                            num *= ManagedParameters.Instance.GetManagedParameter(ManagedParametersEnum.ShieldCorrectSideBlockDamageMultiplier);
                        }

                        num = MissionGameModels.Current.AgentApplyDamageModel.CalculateShieldDamage(num);
                        //InformationManager.DisplayMessage(new InformationMessage("num: " + num));
                        attackCollisionData.InflictedDamage = (int)num;
                    }
                    //InformationManager.DisplayMessage(new InformationMessage(weaponType + " shieldDmg:" + num ));
                }

                return false;
            }
        }

        private static float MyComputeDamage(string weaponType, DamageTypes damageType, float magnitude, float armorEffectiveness, float absorbedDamageRatio)
        {

            float damage = 0f;
            float num3 = 100f / (100f + armorEffectiveness * Vars.dict["Global.ArmorMultiplier"]);
            float mag_1hpol = magnitude + 45f;
            float mag_2hpol = magnitude + 30f;
     
            switch (weaponType)
            {
                case "OneHandedSword":
                    {
                        damage = weaponTypeDamage(Vars.dict[weaponType + ".BluntFactorCut"], Vars.dict[weaponType + ".BluntFactorPierce"], magnitude, num3, damageType, armorEffectiveness,
                                Vars.dict[weaponType + ".ArmorThresholdFactorCut"], Vars.dict[weaponType + ".ArmorThresholdFactorPierce"]);
                        break;
                    }
                case "TwoHandedSword":
                    {
                        damage = weaponTypeDamage(Vars.dict[weaponType + ".BluntFactorCut"], Vars.dict[weaponType + ".BluntFactorPierce"], magnitude, num3, damageType, armorEffectiveness,
                            Vars.dict[weaponType + ".ArmorThresholdFactorCut"], Vars.dict[weaponType + ".ArmorThresholdFactorPierce"]);
                        break;
                    }
                case "OneHandedAxe":
                    {
                        damage = weaponTypeDamage(Vars.dict[weaponType + ".BluntFactorCut"], Vars.dict[weaponType + ".BluntFactorPierce"], magnitude, num3, damageType, armorEffectiveness,
                            Vars.dict[weaponType + ".ArmorThresholdFactorCut"], Vars.dict[weaponType + ".ArmorThresholdFactorPierce"]);
                        break;
                    }
                case "TwoHandedAxe":
                    {
                        damage = weaponTypeDamage(Vars.dict[weaponType + ".BluntFactorCut"], Vars.dict[weaponType + ".BluntFactorPierce"], magnitude, num3, damageType, armorEffectiveness,
                            Vars.dict[weaponType + ".ArmorThresholdFactorCut"], Vars.dict[weaponType + ".ArmorThresholdFactorPierce"]);
                        break;
                    }
                case "OneHandedPolearm":
                    {
                        //magnitude += 45.0f;
                        damage = weaponTypeDamage(Vars.dict[weaponType + ".BluntFactorCut"], Vars.dict[weaponType + ".BluntFactorPierce"], mag_1hpol, num3, damageType, armorEffectiveness,
                            Vars.dict[weaponType + ".ArmorThresholdFactorCut"], Vars.dict[weaponType + ".ArmorThresholdFactorPierce"]);
                        //damage += 5.0f;
                        break;
                    }
                case "TwoHandedPolearm":
                    {
                        //magnitude += 30.0f;
                        damage = weaponTypeDamage(Vars.dict[weaponType + ".BluntFactorCut"], Vars.dict[weaponType + ".BluntFactorPierce"], mag_2hpol, num3, damageType, armorEffectiveness,
                            Vars.dict[weaponType + ".ArmorThresholdFactorCut"], Vars.dict[weaponType + ".ArmorThresholdFactorPierce"]);
                        //damage += 7.0f;
                        break;
                    }
                case "Mace":
                    {
                        damage = weaponTypeDamage(Vars.dict[weaponType + ".BluntFactorCut"], Vars.dict[weaponType + ".BluntFactorPierce"], magnitude, num3, damageType, armorEffectiveness,
                            Vars.dict[weaponType + ".ArmorThresholdFactorCut"], Vars.dict[weaponType + ".ArmorThresholdFactorPierce"]);
                        break;
                    }
                case "TwoHandedMace":
                    {
                        damage = weaponTypeDamage(Vars.dict[weaponType + ".BluntFactorCut"], Vars.dict[weaponType + ".BluntFactorPierce"], magnitude, num3, damageType, armorEffectiveness,
                            Vars.dict[weaponType + ".ArmorThresholdFactorCut"], Vars.dict[weaponType + ".ArmorThresholdFactorPierce"]);
                        break;
                    }
                case "Arrow":
                    {
                        damage = weaponTypeDamage(Vars.dict[weaponType + ".BluntFactorCut"], Vars.dict[weaponType + ".BluntFactorPierce"], magnitude, num3, damageType, armorEffectiveness,
                            Vars.dict[weaponType + ".ArmorThresholdFactorCut"], Vars.dict[weaponType + ".ArmorThresholdFactorPierce"]);
                        break;
                    }
                case "Bolt":
                    {
                        damage = weaponTypeDamage(Vars.dict[weaponType + ".BluntFactorCut"], Vars.dict[weaponType + ".BluntFactorPierce"], magnitude, num3, damageType, armorEffectiveness,
                            Vars.dict[weaponType + ".ArmorThresholdFactorCut"], Vars.dict[weaponType + ".ArmorThresholdFactorPierce"]);
                        break;
                    }
                case "Javelin":
                    {
                        magnitude += 35.0f;
                        damage = weaponTypeDamage(Vars.dict[weaponType + ".BluntFactorCut"], Vars.dict[weaponType + ".BluntFactorPierce"], magnitude, num3, damageType, armorEffectiveness,
                            Vars.dict[weaponType + ".ArmorThresholdFactorCut"], Vars.dict[weaponType + ".ArmorThresholdFactorPierce"]);
                        break;
                    }
                case "ThrowingAxe":
                    {
                        damage = weaponTypeDamage(Vars.dict[weaponType + ".BluntFactorCut"], Vars.dict[weaponType + ".BluntFactorPierce"], magnitude, num3, damageType, armorEffectiveness,
                            Vars.dict[weaponType + ".ArmorThresholdFactorCut"], Vars.dict[weaponType + ".ArmorThresholdFactorPierce"]);
                        break;
                    }
                default:
                    {
                        //InformationManager.DisplayMessage(new InformationMessage("POZOR DEFAULT !!!!"));
                        damage = weaponTypeDamage(1f, 1f, magnitude, num3, damageType, armorEffectiveness, 1f, 1f);
                        break;
                    }
            }


            return damage * absorbedDamageRatio;
        }

        private static float weaponTypeDamage(float bfc, float bfp, float magnitude, float num3, DamageTypes damageType, float armorEffectiveness, float ct, float pt)
        {
            float damage = 0f;
            float num5 = 100f / (100f + armorEffectiveness * Vars.dict["Global.ArmorMultiplier"] * 1.5f);
            switch (damageType)
            {
                case DamageTypes.Blunt:
                    {
                        float num2 = magnitude * 1f;

                        damage += num2 * num5;

                        break;
                    }
                case DamageTypes.Cut:
                    {
                        float num2 = magnitude * bfc;

                        damage += num2 * num3;

                        float num4 = Math.Max(0f, magnitude * num3 - armorEffectiveness * ct);

                        damage += num4 * (1f - bfc);

                        break;
                    }
                case DamageTypes.Pierce:
                    {
                        float num2 = magnitude * bfp;

                        damage += num2 * num3;

                        float num4 = Math.Max(0f, magnitude * num3 - armorEffectiveness * pt);

                        damage += num4 * (1f - bfp);
                        break;
                    }
                default:
                    {
                        damage = 0f;
                        break;
                    }
            }

            return damage;

        }
    }

    //volunteers
    //[HarmonyPatch(typeof(RecruitmentCampaignBehavior))]
    //[HarmonyPatch("UpdateVolunteersOfNotables")]
    //class BetterVolunteers
    //{
    //    static bool Prefix()
    //    {
    //        foreach (TaleWorlds.CampaignSystem.Settlement settlement in TaleWorlds.CampaignSystem.Campaign.Current.Settlements)
    //        {
    //            if ((settlement.IsTown && !settlement.Town.IsRebeling) || (settlement.IsVillage && !settlement.Village.Bound.Town.IsRebeling))
    //            {
    //                foreach (TaleWorlds.CampaignSystem.Hero notable in settlement.Notables)
    //                {
    //                    if (notable.CanHaveRecruits)
    //                    {
    //                        bool flag = false;
    //                        TaleWorlds.CampaignSystem.CultureObject cultureObject = (notable.CurrentSettlement != null) ? notable.CurrentSettlement.Culture : notable.Clan.Culture;
    //                        TaleWorlds.CampaignSystem.CharacterObject basicTroop = cultureObject.BasicTroop;
    //                        double num = (notable.IsRuralNotable && notable.Power >= 200) ? 1.5 : 0.5;

    //                        for (int i = 0; i < 6; i++)
    //                        {
    //                            if (!(notable.VolunteerTypes[i] != null))
    //                            {
    //                                if (MBRandom.RandomFloat < TaleWorlds.CampaignSystem.Campaign.Current.Models.VolunteerProductionModel.GetDailyVolunteerProductionProbability(notable, i, settlement))
    //                                {
    //                                    notable.VolunteerTypes[i] = basicTroop;
    //                                    for (int j = 1; j < Vars.dict["Global.VolunteerStartTier"]; j++)
    //                                    {
    //                                        notable.VolunteerTypes[i] = notable.VolunteerTypes[i].UpgradeTargets[MBRandom.RandomInt(notable.VolunteerTypes[i].UpgradeTargets.Length)];
    //                                    }
    //                                    InformationManager.DisplayMessage(new InformationMessage("vol: " + notable.VolunteerTypes[i].Name));
    //                                    notable.VolunteerTypes[i] = notable.VolunteerTypes[i].UpgradeTargets[MBRandom.RandomInt(notable.VolunteerTypes[i].UpgradeTargets.Length)];
    //                                    flag = true;
    //                                }
    //                            }
    //                            else
    //                            {
    //                                float num3 = 200f * 200f / (Math.Max(50f, (float)notable.Power) * Math.Max(50f, (float)notable.Power));
    //                                int level = notable.VolunteerTypes[i].Level;
    //                                if (MBRandom.RandomInt((int)Math.Max(2.0, (double)((float)level * num3) * num * 2.25)) == 0 && notable.VolunteerTypes[i].UpgradeTargets != null && notable.VolunteerTypes[i].Level < 20)
    //                                {
    //                                    if (notable.VolunteerTypes[i].Tier == Vars.dict["Global.VolunteerStartTier"] && HeroHelper.HeroShouldGiveEliteTroop(notable))
    //                                    {
    //                                        notable.VolunteerTypes[i] = cultureObject.EliteBasicTroop;
    //                                        for (int j = 2; j < Vars.dict["Global.VolunteerStartTier"]; j++)
    //                                        {
    //                                            notable.VolunteerTypes[i] = notable.VolunteerTypes[i].UpgradeTargets[MBRandom.RandomInt(notable.VolunteerTypes[i].UpgradeTargets.Length)];
    //                                        }
    //                                        flag = true;
    //                                    }
    //                                    else
    //                                    {
    //                                        notable.VolunteerTypes[i] = notable.VolunteerTypes[i].UpgradeTargets[MBRandom.RandomInt(notable.VolunteerTypes[i].UpgradeTargets.Length)];
    //                                        flag = true;
    //                                    }
    //                                }

    //                            }
    //                        }
    //                        if (flag)
    //                        {
    //                            for (int j = 0; j < 6; j++)
    //                            {
    //                                for (int k = 0; k < 6; k++)
    //                                {
    //                                    if (notable.VolunteerTypes[k] != null)
    //                                    {
    //                                        int l = k + 1;
    //                                        while (l < 6)
    //                                        {
    //                                            if (notable.VolunteerTypes[l] != null)
    //                                            {
    //                                                if ((float)notable.VolunteerTypes[k].Level > (float)notable.VolunteerTypes[l].Level)
    //                                                {
    //                                                    TaleWorlds.CampaignSystem.CharacterObject characterObject = notable.VolunteerTypes[k];
    //                                                    notable.VolunteerTypes[k] = notable.VolunteerTypes[l];
    //                                                    notable.VolunteerTypes[l] = characterObject;
    //                                                    break;
    //                                                }
    //                                                break;
    //                                            }
    //                                            else
    //                                            {
    //                                                l++;
    //                                            }
    //                                        }
    //                                    }
    //                                }
    //                            }
    //                        }
    //                    }

    //                }
    //            }
    //        }
    //        return false;
    //    }
    //}
    //volunteers



    class Main : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            XmlDocument xmlDocument = new XmlDocument();
            xmlDocument.Load(BasePath.Name + "Modules/RealisticBattle/config.xml");
            foreach (XmlNode childNode in xmlDocument.SelectSingleNode("/config").ChildNodes)
            {
                foreach (XmlNode subNode in childNode)
                {
                    Vars.dict.Add(childNode.Name + "." + subNode.Name, float.Parse(subNode.InnerText));
                }
            }
            MyPatcher.DoPatching();
        }
    }
}
