using System;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Library;
using HarmonyLib;
using System.Xml;
using System.Collections.Generic;
using System.Linq;
using static TaleWorlds.MountAndBlade.ArrangementOrder;
using SandBox;
using System.Reflection;
using JetBrains.Annotations;
using static TaleWorlds.MountAndBlade.Agent;

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
            var harmony = new Harmony("com.jj.dmg");
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
            int calculatedThrowingSpeed = (int)Math.Ceiling(Math.Sqrt(320f * 2f / ammoWeight));

            return calculatedThrowingSpeed;
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

    [HarmonyPatch(typeof(MissionCombatantsLogic))]
    [HarmonyPatch("EarlyStart")]
    class TeamAiFieldBattle
    {
        static void Postfix()
        {
            if (Mission.Current.Teams.Any())
            {
                if (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle)
                {
                    foreach (Team team in Mission.Current.Teams.Where((Team t) => t.HasTeamAi))
                    {
                        if (team.Side == BattleSideEnum.Attacker)
                        {
                            team.ClearTacticOptions();
                            team.AddTacticOption(new TacticFullScaleAttack(team));
                            team.AddTacticOption(new TacticRangedHarrassmentOffensive(team));
                            team.AddTacticOption(new TacticFrontalCavalryCharge(team));
                            team.AddTacticOption(new TacticCoordinatedRetreat(team));
                            team.AddTacticOption(new TacticCharge(team));
                        }
                        if (team.Side == BattleSideEnum.Defender)
                        {
                            team.ClearTacticOptions();
                            team.AddTacticOption(new TacticDefensiveEngagement(team));
                            team.AddTacticOption(new TacticDefensiveLine(team));
                            team.AddTacticOption(new TacticHoldChokePoint(team));
                            team.AddTacticOption(new TacticHoldTheHill(team));
                            team.AddTacticOption(new TacticRangedHarrassmentOffensive(team));
                            team.AddTacticOption(new TacticCoordinatedRetreat(team));
                            team.AddTacticOption(new TacticFullScaleAttack(team));
                            team.AddTacticOption(new TacticFrontalCavalryCharge(team));
                            team.AddTacticOption(new TacticCharge(team));

                            //team.AddTacticOption(new TacticDefensiveRing(team));
                            //team.AddTacticOption(new TacticArchersOnTheHill(team));
                        }
                    }
                }
            }
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
                        __result = __instance.GetAgentDrivenPropertyValue(DrivenProperty.ArmorHead) * 1.2f;
                        break;
                    }
                case BoneBodyPartType.Neck:
                    {
                        // __result = getNeckArmor(__instance);
                        __result = __instance.GetAgentDrivenPropertyValue(DrivenProperty.ArmorHead);
                        break;
                    }
                case BoneBodyPartType.BipedalLegs:
                case BoneBodyPartType.QuadrupedalLegs:
                    {
                        __result = __instance.GetAgentDrivenPropertyValue(DrivenProperty.ArmorLegs);
                        break;
                    }
                case BoneBodyPartType.BipedalArmLeft:
                case BoneBodyPartType.BipedalArmRight:
                case BoneBodyPartType.QuadrupedalArmLeft:
                case BoneBodyPartType.QuadrupedalArmRight:
                    {
                        __result = getArmArmor(__instance) * 1.5f;
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
                    num += (float)equipmentElement.Item.ArmorComponent.BodyArmor * 0.5f;
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
            float num = 0f;
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
    }

    [HarmonyPatch(typeof(Formation))]
    [HarmonyPatch("UpdateAgentDrivenPropertiesBasedOnOrderDefensiveness")]
    class ChangeDefensivness
    {
        static bool Prefix(Formation __instance)
        {
            __instance.ApplyActionOnEachUnit(delegate (Agent agent)
            {
                agent.Defensiveness = 2.5f;
            });
            return false;
        }
        
    }

    [HarmonyPatch(typeof(MissionState))]
    [HarmonyPatch("FinishMissionLoading")]
    class MissionLoadChangeParameters
    {
        static void Postfix()
        {
            ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionArrow, 0.0025f);
            ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionJavelin, 0.0025f);
            ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionAxe, 0.005f);
            ManagedParameters.SetParameter(ManagedParametersEnum.AirFrictionKnife, 0.005f);
            ManagedParameters.SetParameter(ManagedParametersEnum.MissileMinimumDamageToStick, 20);
        }
    }

    [HarmonyPatch(typeof(ArrangementOrder))]
    [HarmonyPatch("GetShieldDirectionOfUnit")]
    class HoldTheDoor
    {
        static void Postfix(ref Agent.UsageDirection __result, Formation formation, Agent unit, ArrangementOrderEnum orderEnum)
        {
            switch (orderEnum)
            {
                case ArrangementOrderEnum.Line:
                case ArrangementOrderEnum.Loose:
                    {
                        __result = Agent.UsageDirection.DefendDown;
                        break;
                    }
            }
        }
    }

    [HarmonyPatch(typeof(CombatStatCalculator))]
    [HarmonyPatch("CalculateStrikeMagnitudeForPassiveUsage")]
    class ChangeLanceDamage
    {
        static bool Prefix(float weaponWeight, float exraLinearSpeed, ref float __result)
        {
            //float weaponWeight2 = 40f + weaponWeight;
            __result = CalculateStrikeMagnitudeForThrust(0f, weaponWeight, exraLinearSpeed, isThrown: false);
            return false;
        }

        private static float CalculateStrikeMagnitudeForThrust(float thrustWeaponSpeed, float weaponWeight, float extraLinearSpeed, bool isThrown)
        {
            float num = extraLinearSpeed;
            if (!isThrown)
            {
            weaponWeight += 0f;
            }
            float num2 = 0.5f * weaponWeight * num * num * 0.6f;
            if (num2 > weaponWeight * 50.0f)
            {
            num2 = weaponWeight * 50.0f;
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
            //Vec3 wbd = acd.WeaponBlowDir;

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
                    length += -(7.0f);
                    if (length < 5.0f)
                    {
                        length = 5.0f;
                    } 
                    missileTotalDamage *= 0.01f;
                }
                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Javelin"))
                {
                  length += -(7.0f);
                    if (length < 5.0f)
                    {
                        length = 5.0f;
                    } 
                    //missileTotalDamage += 168.0f;
                    missileTotalDamage *= 0.005f;
                }
                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("OneHandedPolearm"))
                {
                    length += -(7.0f);
                    if (length < 5.0f)
                    {
                        length = 5.0f;
                    }
                    missileTotalDamage *= 0.005f;
                }
                else
                {
                    if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Arrow")){
                        missileTotalDamage -= 10f;
                        missileTotalDamage *= 0.01f;
                    }
                    if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Bolt"))
                    {
                        missileTotalDamage *= 0.1f;
                    }
                }
            }

            float physicalDamage = ((length * length) * (weaponItem.Weight)) / 2;
            /*
            if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Arrow") && physicalDamage > (weaponItem.Weight) * 1500f)
            { 
                physicalDamage = (weaponItem.Weight) * 1500f; 
            }

            if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Bolt") && physicalDamage > (weaponItem.Weight) * 2000f)
            {
                physicalDamage = (weaponItem.Weight) * 2000f;
            }*/

            if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Javelin") && physicalDamage > (weaponItem.Weight) * 300f)
            {
                physicalDamage = (weaponItem.Weight) * 300f;
            }

            if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("OneHandedPolearm") && physicalDamage > (weaponItem.Weight) * 400f)
            {
                physicalDamage = (weaponItem.Weight) * 200f;
            }
            

            //float distnace = (acd.MissileStartingPosition - acd.CollisionGlobalPosition).Length;
            //InformationManager.DisplayMessage(new InformationMessage("Ek:" + physicalDamage + " modif:" + missileTotalDamage + " speed:" + length + " dist:" + distnace));
            // baseMagnitude = physicalDamage * missileTotalDamage * momentumRemaining * angleModifier;
            baseMagnitude = physicalDamage * missileTotalDamage * momentumRemaining;
            specialMagnitude = baseMagnitude;

            return false;
        }
    }

    //[HarmonyPatch(typeof(TacticComponent))]
    //[HarmonyPatch("AssignTacticFormations1121")]
    //class OverrideAssignTacticFormations1121
    //{
    //    static void Postfix(TacticComponent __instance)
    //    {
    //        __instance.ToString();
    //    }
    //}

    [HarmonyPatch(typeof(Agent))]
    [HarmonyPatch("GetHasRangedWeapon")]
    class OverrideGetHasRangedWeapon
    {
        static bool Prefix(Agent __instance, ref bool __result, bool checkHasAmmo = false)
        {
            __result = false;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
            {
                bool isPolearm = false;
                //bool isjavelin = false;
                foreach (WeaponComponentData weapon in __instance.Equipment[equipmentIndex].Weapons)
                {
                    if (weapon.WeaponClass == WeaponClass.LowGripPolearm || weapon.WeaponClass == WeaponClass.OneHandedPolearm || weapon.WeaponClass == WeaponClass.Javelin || weapon.WeaponClass == WeaponClass.ThrowingAxe || weapon.WeaponClass == WeaponClass.ThrowingKnife || weapon.WeaponClass == WeaponClass.Stone)
                    {
                        isPolearm = true;
                    }
                    if (weapon != null && weapon.IsRangedWeapon && (!checkHasAmmo || __instance.Equipment.HasAmmo(weapon)))
                    {
                        if (!isPolearm)
                        {
                            __result = true;
                        }
                    }
                }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Agent))]
    [HarmonyPatch("EquipItemsFromSpawnEquipment")]
    class OverrideEquipItemsFromSpawnEquipment
    {

        private static int _oldMissileSpeed;
        static bool Prefix(Agent __instance)
        {
            MissionWeapon bow = MissionWeapon.Invalid;
            MissionWeapon arrow = MissionWeapon.Invalid;
            bool firstProjectile = true;

            for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
            {
                if (__instance.Equipment != null && !__instance.Equipment[equipmentIndex].IsEmpty)
                {
                    WeaponStatsData[] wsd = __instance.Equipment[equipmentIndex].GetWeaponStatsData();
                    if ((wsd[0].WeaponClass == (int)WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow))
                    {
                        bow = __instance.Equipment[equipmentIndex];
                    }
                    if ((wsd[0].WeaponClass == (int)WeaponClass.Arrow) || (wsd[0].WeaponClass == (int)WeaponClass.Bolt))
                    {
                        if (firstProjectile)
                        {
                            arrow = __instance.Equipment[equipmentIndex];
                            firstProjectile = false;
                        }
                    }
                    if ((wsd[0].WeaponClass == (int)WeaponClass.OneHandedPolearm) || (wsd[0].WeaponClass == (int)WeaponClass.LowGripPolearm) || (wsd[0].WeaponClass == (int)WeaponClass.Javelin) || (wsd[0].WeaponClass == (int)WeaponClass.ThrowingAxe) || (wsd[0].WeaponClass == (int)WeaponClass.ThrowingKnife))
                    {
                        for(int i=0; i < wsd.Length; i++)
                        {
                            if(wsd[i].MissileSpeed != 0)
                            {
                                MissionWeapon throwable = __instance.Equipment[equipmentIndex];
                                float ammoWeight = __instance.Equipment[equipmentIndex].GetWeight() / __instance.Equipment[equipmentIndex].Amount;
                                int calculatedThrowingSpeed = Utilities.calculateThrowableSpeed(ammoWeight);
                                PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                                property.DeclaringType.GetProperty("MissileSpeed");
                                throwable.CurrentUsageIndex = i;
                                property.SetValue(throwable.CurrentUsageItem, calculatedThrowingSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                                throwable.CurrentUsageIndex = 0;
                            }
                        }
                    }
                }
            }

            int calculatedMissileSpeed = 50;
            if (!bow.Equals(MissionWeapon.Invalid) && !arrow.Equals(MissionWeapon.Invalid))
            {
                _oldMissileSpeed = bow.GetMissileSpeedForUsage(0);
                float ammoWeight = arrow.GetWeight() / arrow.Amount;

                calculatedMissileSpeed = Utilities.calculateMissileSpeed(ammoWeight, bow, bow.GetMissileSpeedForUsage(0));

                PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                property.DeclaringType.GetProperty("MissileSpeed");
                property.SetValue(bow.CurrentUsageItem, calculatedMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
            }
            else if (!bow.Equals(MissionWeapon.Invalid))
            {
                PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                property.DeclaringType.GetProperty("MissileSpeed");
                property.SetValue(bow.CurrentUsageItem, calculatedMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
            }

            

            return true;
        }
        static void Postfix(Agent __instance)
        {
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
                        property.SetValue(missionWeapon.CurrentUsageItem, _oldMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
                    }
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

        private static int _oldMissileSpeed;
        static bool Prefix(Agent shooterAgent, EquipmentIndex weaponIndex, Vec3 position, Vec3 velocity, Mat3 orientation, bool hasRigidBody, bool isPrimaryWeaponShot, int forcedMissileIndex, Mission __instance)
        {
            MissionWeapon missionWeapon = shooterAgent.Equipment[weaponIndex];

            WeaponData wd = missionWeapon.GetWeaponData(needBatchedVersionForMeshes: true);
            WeaponStatsData[] wsd = missionWeapon.GetWeaponStatsData();

            if ((wsd[0].WeaponClass == (int) WeaponClass.Bow) || (wsd[0].WeaponClass == (int)WeaponClass.Crossbow)) {

                _oldMissileSpeed = missionWeapon.GetMissileSpeedForUsage(0);
                float ammoWeight = missionWeapon.AmmoWeapon.GetWeight();
                
                int calculatedMissileSpeed = Utilities.calculateMissileSpeed(ammoWeight, missionWeapon, missionWeapon.GetMissileSpeedForUsage(0));

                PropertyInfo property2 = typeof(WeaponComponentData).GetProperty("MissileSpeed");
                property2.DeclaringType.GetProperty("MissileSpeed");
                property2.SetValue(shooterAgent.Equipment[weaponIndex].CurrentUsageItem, calculatedMissileSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);

                missionWeapon = shooterAgent.Equipment[weaponIndex];

                wd = missionWeapon.GetWeaponData(needBatchedVersionForMeshes: true);
                wsd = missionWeapon.GetWeaponStatsData();

                WeaponData awd = WeaponData.InvalidWeaponData;

                MethodInfo method = typeof(Agent).GetMethod("WeaponEquipped", BindingFlags.NonPublic | BindingFlags.Instance);
                method.DeclaringType.GetMethod("WeaponEquipped");
                method.Invoke(shooterAgent, new object[] { weaponIndex, wd, wsd, awd, null, null, true, true });
                wd.DeinitializeManagedPointers();

                shooterAgent.TryToWieldWeaponInSlot(weaponIndex, WeaponWieldActionType.InstantAfterPickUp, true);

                shooterAgent.UpdateAgentProperties();
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

        static bool Prefix(float armorAmountFloat, WeaponComponentData shieldOnBack, AgentFlag victimAgentFlag, float victimAgentAbsorbedDamageRatio, float damageMultiplierOfBone, float combatDifficultyMultiplier, DamageTypes damageType, float magnitude, Vec3 blowPosition, ItemObject item, bool blockedWithShield, bool hitShieldOnBack, int speedBonus, bool cancelDamage, bool isFallDamage, out int inflictedDamage, out int absorbedByArmor, out int armorAmount)
        {
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
            if (hitShieldOnBack && shieldOnBack != null)
            {
                num2 += 10f;
            }

            string weaponType = "otherDamage";
            if (item != null && item.PrimaryWeapon != null)
            {
                weaponType = item.PrimaryWeapon.WeaponClass.ToString();
            }

            //InformationManager.DisplayMessage(new InformationMessage("weapon type: " + weaponType));

            float num3 = MBMath.ClampInt((int)MyComputeDamage(weaponType, damageType, magnitude, num2, victimAgentAbsorbedDamageRatio), 0, 2000);
            float num4 = 1f;

            if (!blockedWithShield && !isFallDamage)
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
            static bool Prefix(bool isAttackerAgentNull, bool isAttackerAgentActive, bool isAttackerAgentCharging, bool canGiveDamageToAgentShield, bool isVictimAgentLeftStance, MissionWeapon victimShield, ref AttackCollisionData attackCollisionData, WeaponComponentData attackerWeapon, float blowMagnitude)
            {
                attackCollisionData.InflictedDamage = 0;
                if (victimShield.CurrentUsageItem.WeaponFlags.HasAnyFlag(WeaponFlags.CanBlockRanged) & canGiveDamageToAgentShield)
                {
                    DamageTypes damageType = (DamageTypes)attackCollisionData.DamageType;
                    int shieldArmorForCurrentUsage = victimShield.GetShieldArmorForCurrentUsage();
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
                                    num *= 3.5f;
                                    break;
                                }
                            case "ThrowingAxe":
                                {
                                    num *= 2.0f;
                                    break;
                                }
                            case "OneHandedPolearm":
                                {
                                    num *= 2.0f;
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
                        magnitude += 10.0f;
                        damage = weaponTypeDamage(Vars.dict[weaponType + ".BluntFactorCut"], Vars.dict[weaponType + ".BluntFactorPierce"], magnitude, num3, damageType, armorEffectiveness,
                            Vars.dict[weaponType + ".ArmorThresholdFactorCut"], Vars.dict[weaponType + ".ArmorThresholdFactorPierce"]);
                        damage += 5.0f;
                        break;
                    }
                case "TwoHandedPolearm":
                    {
                        magnitude += 15.0f;
                        damage = weaponTypeDamage(Vars.dict[weaponType + ".BluntFactorCut"], Vars.dict[weaponType + ".BluntFactorPierce"], magnitude, num3, damageType, armorEffectiveness,
                            Vars.dict[weaponType + ".ArmorThresholdFactorCut"], Vars.dict[weaponType + ".ArmorThresholdFactorPierce"]);
                        damage += 7.0f;
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
                        magnitude += 15.0f;
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
            switch (damageType)
            {
                case DamageTypes.Blunt:
                    {
                        float num2 = magnitude * 1f;

                        damage += num2 * num3;

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
    /*    [HarmonyPatch(typeof(RecruitCampaignBehavior))]
        [HarmonyPatch("UpdateVolunteersOfNotables")]
        class BetterVolunteers
        {
            static bool Prefix()
            {
                foreach (TaleWorlds.CampaignSystem.Settlement settlement in TaleWorlds.CampaignSystem.Campaign.Current.Settlements)
                {
                    if ((settlement.IsTown && !settlement.Town.IsRebeling) || (settlement.IsVillage && !settlement.Village.Bound.Town.IsRebeling))
                    {
                        foreach (TaleWorlds.CampaignSystem.Hero notable in settlement.Notables)
                        {
                            if (notable.CanHaveRecruits)
                            {
                                bool flag = false;
                                TaleWorlds.CampaignSystem.CultureObject cultureObject = (notable.CurrentSettlement != null) ? notable.CurrentSettlement.Culture : notable.Clan.Culture;
                                TaleWorlds.CampaignSystem.CharacterObject basicTroop = cultureObject.BasicTroop;
                                double num = (notable.IsRuralNotable && notable.Power >= 200) ? 1.5 : 0.5;

                                for (int i = 0; i < 6; i++)
                                {
                                    if (!(notable.VolunteerTypes[i] != null))
                                    {
                                        if (MBRandom.RandomFloat < TaleWorlds.CampaignSystem.Campaign.Current.Models.VolunteerProductionModel.GetDailyVolunteerProductionProbability(notable, i, settlement))
                                        {
                                            notable.VolunteerTypes[i] = basicTroop;
                                            for (int j = 1; j < Vars.dict["Global.VolunteerStartTier"]; j++)
                                            {
                                                notable.VolunteerTypes[i] = notable.VolunteerTypes[i].UpgradeTargets[MBRandom.RandomInt(notable.VolunteerTypes[i].UpgradeTargets.Length)];
                                            }
                                            //InformationManager.DisplayMessage(new InformationMessage("vol: " + notable.VolunteerTypes[i].Name));
                                            //notable.VolunteerTypes[i] = notable.VolunteerTypes[i].UpgradeTargets[MBRandom.RandomInt(notable.VolunteerTypes[i].UpgradeTargets.Length)];
                                            flag = true;
                                        }
                                    }
                                    else
                                    {
                                        float num3 = 200f * 200f / (Math.Max(50f, (float)notable.Power) * Math.Max(50f, (float)notable.Power));
                                        int level = notable.VolunteerTypes[i].Level;
                                        if (MBRandom.RandomInt((int)Math.Max(2.0, (double)((float)level * num3) * num * 2.25)) == 0 && notable.VolunteerTypes[i].UpgradeTargets != null && notable.VolunteerTypes[i].Level < 20)
                                        {
                                            if (notable.VolunteerTypes[i].Tier == Vars.dict["Global.VolunteerStartTier"] && HeroHelper.HeroShouldGiveEliteTroop(notable))
                                            {
                                                notable.VolunteerTypes[i] = cultureObject.EliteBasicTroop;
                                                for (int j = 2; j < Vars.dict["Global.VolunteerStartTier"]; j++)
                                                {
                                                    notable.VolunteerTypes[i] = notable.VolunteerTypes[i].UpgradeTargets[MBRandom.RandomInt(notable.VolunteerTypes[i].UpgradeTargets.Length)];
                                                }
                                                flag = true;
                                            }
                                            else
                                            {
                                                notable.VolunteerTypes[i] = notable.VolunteerTypes[i].UpgradeTargets[MBRandom.RandomInt(notable.VolunteerTypes[i].UpgradeTargets.Length)];
                                                flag = true;
                                            }
                                        }

                                    }
                                }
                                if (flag)
                                {
                                    for (int j = 0; j < 6; j++)
                                    {
                                        for (int k = 0; k < 6; k++)
                                        {
                                            if (notable.VolunteerTypes[k] != null)
                                            {
                                                int l = k + 1;
                                                while (l < 6)
                                                {
                                                    if (notable.VolunteerTypes[l] != null)
                                                    {
                                                        if ((float)notable.VolunteerTypes[k].Level > (float)notable.VolunteerTypes[l].Level)
                                                        {
                                                            TaleWorlds.CampaignSystem.CharacterObject characterObject = notable.VolunteerTypes[k];
                                                            notable.VolunteerTypes[k] = notable.VolunteerTypes[l];
                                                            notable.VolunteerTypes[l] = characterObject;
                                                            break;
                                                        }
                                                        break;
                                                    }
                                                    else
                                                    {
                                                        l++;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                        }
                    }
                }
                return false;
            }
        } */
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
