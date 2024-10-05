using HarmonyLib;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using TaleWorlds.CampaignSystem.TournamentGames;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static RBMAI.PostureDamage;
using static TaleWorlds.Core.ItemObject;
using static TaleWorlds.MountAndBlade.Agent;

namespace RBMAI
{
    public class PostureLogic : MissionLogic
    {
        public static MBArrayList<Agent> agentsToDropShield = new MBArrayList<Agent> { };

        //private static float maxAcc = 1.5f;
        //private static float minAcc = 0.1f;
        //private static float curAcc = 1f;
        //private static bool isCountingUp = false;
        public static MBArrayList<Agent> agentsToDropWeapon = new MBArrayList<Agent> { };

        public static Dictionary<Agent, FormationClass> agentsToChangeFormation = new Dictionary<Agent, FormationClass> { };

        private static float currentDt = 0f;

        private static float lwrResultModifier = 3f;

        private static int postureEffectCheck = 0;

        private static int postureEffectCheckCooldown = 15;

        private static float relativeSpeedPostureFactor = 0.6f;

        //private static int tickCooldownReset = 30;
        //private static int tickCooldown = 0;
        private static float timeToCalc = 0.5f;

        private static float weaponLengthPostureFactor = 0.2f;
        private static float weaponWeightPostureFactor = 0.5f;

        public static float getEquipWeight(Agent agent)
        {
            float armorWeight = 0f;
            float weaponWeight = 0f;
            //for (EquipmentIndex ei = EquipmentIndex.WeaponItemBeginSlot; ei < EquipmentIndex.ExtraWeaponSlot; ei++)
            //{
            //    if (!agent.Equipment[ei].IsEmpty)
            //    {
            //        weaponWeight += agent.Equipment[ei].Item.Weight;
            //    }
            //}
            //for (EquipmentIndex ei = EquipmentIndex.ArmorItemBeginSlot; ei < EquipmentIndex.ArmorItemEndSlot; ei++)
            //{
            //    if (!agent.Equipment[ei].IsEmpty)
            //    {
            //        armorWeight += agent.Equipment[ei].Item.Weight;
            //    }
            //}
            armorWeight = agent.SpawnEquipment.GetTotalWeightOfArmor(true);
            //weaponWeight = agent.Equipment.GetTotalWeightOfWeapons();
            
            //return armorWeight - weaponWeight;
            return armorWeight;
        }

        public static float getWeaponWeightModifier(Agent agent)
        {
            float weaponWeight = agent.Equipment.GetTotalWeightOfWeapons();

            return MBMath.Lerp(0f, 1f, MathF.Clamp(weaponWeight / 50f, 0f, 1f));
        }

        public static float getEquipWeightModifier(Agent agent)
        {
            return MBMath.Lerp(0f, 1f, MathF.Clamp(getEquipWeight(agent) / 50f, 0f, 1f));
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (RBMConfig.RBMConfig.postureEnabled && Mission.Current.AllowAiTicking)
            {
                if (currentDt < timeToCalc)
                {
                    currentDt += dt;
                }
                else
                {
                    MBArrayList<Agent> inactiveAgents = new MBArrayList<Agent>();
                    foreach (KeyValuePair<Agent, Posture> entry in AgentPostures.values)
                    {
                        if (entry.Key != null && entry.Key.Mission != null && !entry.Key.IsActive())
                        {
                            inactiveAgents.Add(entry.Key);
                            continue;
                        }
                        if (entry.Key.IsPlayerControlled)
                        {
                            if (AgentPostures.postureVisual != null && AgentPostures.postureVisual._dataSource.ShowPlayerPostureStatus)
                            {
                                AgentPostures.postureVisual._dataSource.PlayerPosture = (int)entry.Value.posture;
                                AgentPostures.postureVisual._dataSource.PlayerPostureMax = (int)entry.Value.maxPosture;
                                AgentPostures.postureVisual._dataSource.PlayerPostureText = ((int)entry.Value.posture).ToString();
                                AgentPostures.postureVisual._dataSource.PlayerPostureMaxText = ((int)entry.Value.maxPosture).ToString();
                            }
                        }
                        if (entry.Value.posture < entry.Value.maxPosture)
                        {
                            if (RBMConfig.RBMConfig.postureGUIEnabled)
                            {
                                if (AgentPostures.postureVisual != null && AgentPostures.postureVisual._dataSource.ShowEnemyStatus && AgentPostures.postureVisual.affectedAgent == entry.Key)
                                {
                                    AgentPostures.postureVisual._dataSource.EnemyPosture = (int)entry.Value.posture;
                                    AgentPostures.postureVisual._dataSource.EnemyPostureMax = (int)entry.Value.maxPosture;
                                }
                            }
                            entry.Value.posture += entry.Value.regenPerTick * 30f;

                            entry.Key.UpdateAgentProperties();
                        }
                    }
                    foreach (Agent agent in inactiveAgents)
                    {
                        AgentPostures.values.Remove(agent);
                    }
                    inactiveAgents.Clear();

                    foreach (KeyValuePair<Agent, FormationClass> entry in agentsToChangeFormation)
                    {
                        if (entry.Key != null && entry.Key.Mission != null && entry.Key.IsActive() && entry.Key.Team != null)
                        {
                            entry.Key.Formation = entry.Key.Team.GetFormation(entry.Value);
                            entry.Key.DisableScriptedMovement();
                        }
                    }
                    agentsToChangeFormation.Clear();

                    //shield drop
                    MBArrayList<Agent> agentsAbleToDropShield = new MBArrayList<Agent> { };
                    for (int i = agentsToDropShield.Count - 1; i >= 0; i--)
                    {
                        if (agentsToDropShield[i] != null && agentsToDropShield[i].Mission != null && agentsToDropShield[i].IsActive())
                        {
                            ActionCodeType currentActionType = agentsToDropShield[i].GetCurrentActionType(1);
                            if (
                                currentActionType == ActionCodeType.ReleaseMelee ||
                                currentActionType == ActionCodeType.ReleaseRanged ||
                                currentActionType == ActionCodeType.ReleaseThrowing ||
                                currentActionType == ActionCodeType.WeaponBash)
                            {
                                continue;
                            }
                            else
                            {
                                agentsAbleToDropShield.Add(agentsToDropShield[i]);
                            }
                        }
                        else
                        {
                            agentsAbleToDropShield.Add(agentsToDropShield[i]);
                        }
                    }
                    foreach (Agent agent in agentsAbleToDropShield)
                    {
                        if (agent != null && agent.Mission != null && agent.IsActive())
                        {
                            EquipmentIndex ei = agent.GetWieldedItemIndex(Agent.HandIndex.OffHand);
                            if (ei != EquipmentIndex.None)
                            {
                                agent.DropItem(ei);
                                agent.UpdateAgentProperties();
                            }
                        }
                        agentsToDropShield.Remove(agent);
                    }
                    agentsAbleToDropShield.Clear();

                    //weapon drop
                    MBArrayList<Agent> agentsAbleToDropWeapon = new MBArrayList<Agent> { };
                    for (int i = agentsToDropWeapon.Count - 1; i >= 0; i--)
                    {
                        if (agentsToDropWeapon[i] != null && agentsToDropWeapon[i].Mission != null && agentsToDropWeapon[i].IsActive())
                        {
                            ActionCodeType currentActionType = agentsToDropWeapon[i].GetCurrentActionType(1);
                            if (
                                currentActionType == ActionCodeType.ReleaseMelee ||
                                currentActionType == ActionCodeType.ReleaseRanged ||
                                currentActionType == ActionCodeType.ReleaseThrowing ||
                                currentActionType == ActionCodeType.WeaponBash)
                            {
                                continue;
                            }
                            else
                            {
                                agentsAbleToDropWeapon.Add(agentsToDropWeapon[i]);
                            }
                        }
                        else
                        {
                            agentsAbleToDropWeapon.Add(agentsToDropWeapon[i]);
                        }
                    }
                    foreach (Agent agent in agentsAbleToDropWeapon)
                    {
                        if (agent != null && agent.Mission != null && agent.IsActive())
                        {
                            EquipmentIndex ei = agent.GetWieldedItemIndex(Agent.HandIndex.MainHand);
                            if (ei != EquipmentIndex.None)
                            {
                                agent.DropItem(ei);
                                agent.UpdateAgentProperties();
                            }
                        }
                        agentsToDropWeapon.Remove(agent);
                    }
                    agentsAbleToDropWeapon.Clear();

                    currentDt = 0f;
                }
            }
        }

        [HarmonyPatch(typeof(MissionState))]
        [HarmonyPatch("LoadMission")]
        public class LoadMissionPatch
        {
            private static void Postfix()
            {
                AgentPostures.values.Clear();
                agentsToDropShield.Clear();
                agentsToDropWeapon.Clear();
                agentsToChangeFormation.Clear();
            }
        }

        [HarmonyPatch(typeof(MissionState))]
        [HarmonyPatch("OnDeactivate")]
        public class OnDeactivatePatch
        {
            private static void Postfix()
            {
                AgentPostures.values.Clear();
                agentsToDropShield.Clear();
                agentsToDropWeapon.Clear();
                agentsToChangeFormation.Clear();
            }
        }

        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("CreateMeleeBlow")]
        private class CreateMeleeBlowPatch
        {
            

            public static float calculateHealthDamage(MissionWeapon targetWeapon, Agent attacker, Agent vicitm, float overPostureDamage, ref Blow b)
            {
                float armorSumPosture = vicitm.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.Head);
                armorSumPosture += vicitm.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.Neck);
                armorSumPosture += vicitm.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.Chest);
                armorSumPosture += vicitm.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.Abdomen);
                armorSumPosture += vicitm.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.ShoulderLeft);
                armorSumPosture += vicitm.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.ShoulderRight);
                armorSumPosture += vicitm.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.ArmLeft);
                armorSumPosture += vicitm.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.ArmRight);
                armorSumPosture += vicitm.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.Legs);

                armorSumPosture = (armorSumPosture / 9f);

                if (RBMConfig.RBMConfig.rbmCombatEnabled)
                {
                    int relevantSkill = 0;
                    float swingSpeed = 0f;
                    float thrustSpeed = 0f;
                    float swingDamageFactor = 0f;
                    float thrustDamageFactor = 0f;
                    float sweetSpotOut = 0f;
                    float sweetSpot = 0f;
                    int targetWeaponUsageIndex = targetWeapon.CurrentUsageIndex;
                    BasicCharacterObject currentSelectedChar = attacker.Character;

                    if (currentSelectedChar != null && !targetWeapon.IsEmpty && targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex) != null && targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).IsMeleeWeapon)
                    {
                        if (currentSelectedChar != null)
                        {
                            SkillObject skill = targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).RelevantSkill;
                            int effectiveSkill = currentSelectedChar.GetSkillValue(skill);
                            float effectiveSkillDR = Utilities.GetEffectiveSkillWithDR(effectiveSkill);
                            float skillModifier = Utilities.CalculateSkillModifier(effectiveSkill);

                            Utilities.CalculateVisualSpeeds(targetWeapon, targetWeaponUsageIndex, effectiveSkillDR, out int swingSpeedReal, out int thrustSpeedReal, out int handlingReal);

                            float swingSpeedRealF = swingSpeedReal / Utilities.swingSpeedTransfer;
                            float thrustSpeedRealF = thrustSpeedReal / Utilities.thrustSpeedTransfer;

                            relevantSkill = effectiveSkill;

                            swingSpeed = swingSpeedRealF;
                            thrustSpeed = thrustSpeedRealF;
                            if (b.StrikeType == StrikeType.Swing)
                            {
                                float sweetSpotMagnitude = CalculateSweetSpotSwingMagnitude(currentSelectedChar, targetWeapon, targetWeaponUsageIndex, effectiveSkill);

                                float skillBasedDamage = Utilities.GetSkillBasedDamage(sweetSpotMagnitude, false, targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(),
                                    targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).SwingDamageType, effectiveSkillDR, skillModifier, StrikeType.Swing, targetWeapon.Item.Weight);

                                swingDamageFactor = (float)Math.Sqrt(Utilities.getSwingDamageFactor(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex), targetWeapon.ItemModifier));

                                int realDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(), targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).SwingDamageType, skillBasedDamage, armorSumPosture, 1f, out float penetratedDamage, out float bluntForce, swingDamageFactor, null, false)), 0, 2000);
                                realDamage = MathF.Floor(realDamage * 1f);
                                return realDamage;
                            }
                            else
                            {
                                float thrustMagnitude = CalculateThrustMagnitude(currentSelectedChar, targetWeapon, targetWeaponUsageIndex, effectiveSkill);

                                float skillBasedDamage = Utilities.GetSkillBasedDamage(thrustMagnitude, false, targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(),
                                    targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).ThrustDamageType, effectiveSkillDR, skillModifier, StrikeType.Thrust, targetWeapon.Item.Weight);

                                thrustDamageFactor = (float)Math.Sqrt(Utilities.getThrustDamageFactor(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex), targetWeapon.ItemModifier));

                                int realDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(),
                                targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).ThrustDamageType, skillBasedDamage, armorSumPosture, 1f, out float penetratedDamage, out float bluntForce, thrustDamageFactor, null, false)), 0, 2000);
                                realDamage = MathF.Floor(realDamage * 1f);
                                return realDamage;
                            }
                        }
                    }
                }

                int weaponDamage = 0;
                if (b.StrikeType == StrikeType.Swing)
                {
                    weaponDamage = targetWeapon.GetModifiedSwingDamageForCurrentUsage();
                }
                else
                {
                    weaponDamage = targetWeapon.GetModifiedThrustDamageForCurrentUsage();
                }

                return MBMath.ClampInt(MathF.Ceiling(MissionGameModels.Current.StrikeMagnitudeModel.ComputeRawDamage(b.DamageType, weaponDamage, armorSumPosture, 1f)), 0, 2000);
            }

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

                dynamicPostureLoss -= MBMath.Lerp(0f, 1f, 1f - (attackerEffectiveWeaponSkill / 200f)) * (dynamicPS * 0.5f);
                dynamicPostureLoss -= MBMath.Lerp(0f, 1f, 1f - (attackerEffectiveStrengthSkill / 200f)) * (dynamicPS * 0.5f);

                return fixedPostureLoss + dynamicPostureLoss;
            }

            public static float CalculateSweetSpotSwingMagnitude(BasicCharacterObject character, MissionWeapon weapon, int weaponUsageIndex, int relevantSkill)
            {
                float progressEffect = 1f;
                float sweetSpotMagnitude = -1f;

                if (weapon.Item != null && weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex) != null)
                {
                    float swingSpeed = (float)weapon.GetModifiedSwingSpeedForCurrentUsage() / 4.5454545f * progressEffect;

                    int ef = relevantSkill;
                    float effectiveSkillDR = Utilities.GetEffectiveSkillWithDR(ef);
                    switch (weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).WeaponClass)
                    {
                        case WeaponClass.LowGripPolearm:
                        case WeaponClass.Mace:
                        case WeaponClass.OneHandedAxe:
                        case WeaponClass.OneHandedPolearm:
                        case WeaponClass.TwoHandedMace:
                            {
                                float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                                swingSpeed = swingSpeed * 0.83f * swingskillModifier * progressEffect;
                                break;
                            }
                        case WeaponClass.TwoHandedPolearm:
                            {
                                float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                                swingSpeed = swingSpeed * 0.83f * swingskillModifier * progressEffect;
                                break;
                            }
                        case WeaponClass.TwoHandedAxe:
                            {
                                float swingskillModifier = 1f + (effectiveSkillDR / 800f);

                                swingSpeed = swingSpeed * 0.75f * swingskillModifier * progressEffect;
                                break;
                            }
                        case WeaponClass.OneHandedSword:
                        case WeaponClass.Dagger:
                        case WeaponClass.TwoHandedSword:
                            {
                                float swingskillModifier = 1f + (effectiveSkillDR / 800f);

                                swingSpeed = swingSpeed * 0.83f * swingskillModifier * progressEffect;
                                break;
                            }
                    }
                    float weaponWeight = weapon.Item.Weight;
                    float weaponInertia = weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).Inertia;
                    float weaponCOM = weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass;
                    for (float currentSpot = 1f; currentSpot > 0.35f; currentSpot -= 0.01f)
                    {
                        float currentSpotMagnitude = CombatStatCalculator.CalculateStrikeMagnitudeForSwing(swingSpeed, currentSpot, weaponWeight,
                            weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).GetRealWeaponLength(), weaponInertia, weaponCOM, 0f);
                        if (currentSpotMagnitude > sweetSpotMagnitude)
                        {
                            sweetSpotMagnitude = currentSpotMagnitude;
                        }
                    }
                }
                return sweetSpotMagnitude;
            }

            public static float CalculateThrustMagnitude(BasicCharacterObject character, MissionWeapon weapon, int weaponUsageIndex, int relevantSkill)
            {
                float progressEffect = 1f;
                float thrustMagnitude = -1f;

                if (weapon.Item != null && weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex) != null)
                {
                    float thrustWeaponSpeed = (float)weapon.GetModifiedThrustSpeedForCurrentUsage() / 11.7647057f * progressEffect;

                    int ef = relevantSkill;
                    float effectiveSkillDR = Utilities.GetEffectiveSkillWithDR(ef);

                    float weaponWeight = weapon.Item.Weight;
                    float weaponInertia = weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).Inertia;
                    float weaponCOM = weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass;

                    switch (weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).WeaponClass)
                    {
                        case WeaponClass.LowGripPolearm:
                        case WeaponClass.Mace:
                        case WeaponClass.OneHandedAxe:
                        case WeaponClass.OneHandedPolearm:
                        case WeaponClass.TwoHandedMace:
                            {
                                float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);

                                thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weaponWeight, weaponInertia, weaponCOM);
                                thrustWeaponSpeed = thrustWeaponSpeed * 0.75f * thrustskillModifier * progressEffect;
                                break;
                            }
                        case WeaponClass.TwoHandedPolearm:
                            {
                                float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);

                                thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weaponWeight, weaponInertia, weaponCOM);
                                thrustWeaponSpeed = thrustWeaponSpeed * 0.7f * thrustskillModifier * progressEffect;
                                break;
                            }
                        case WeaponClass.TwoHandedAxe:
                            {
                                float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);

                                thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weaponWeight, weaponInertia, weaponCOM);
                                thrustWeaponSpeed = thrustWeaponSpeed * 0.9f * thrustskillModifier * progressEffect;
                                break;
                            }
                        case WeaponClass.OneHandedSword:
                        case WeaponClass.Dagger:
                        case WeaponClass.TwoHandedSword:
                            {
                                float thrustskillModifier = 1f + (effectiveSkillDR / 800f);

                                thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weaponWeight, weaponInertia, weaponCOM);
                                thrustWeaponSpeed = thrustWeaponSpeed * 0.7f * thrustskillModifier * progressEffect;
                                break;
                            }
                    }

                    switch (weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).WeaponClass)
                    {
                        case WeaponClass.OneHandedPolearm:
                        case WeaponClass.OneHandedSword:
                        case WeaponClass.Dagger:
                        case WeaponClass.Mace:
                            {
                                thrustMagnitude = Utilities.CalculateThrustMagnitudeForOneHandedWeapon(weaponWeight, effectiveSkillDR, thrustWeaponSpeed, 0f, Agent.UsageDirection.AttackDown);
                                break;
                            }
                        case WeaponClass.TwoHandedPolearm:
                        case WeaponClass.TwoHandedSword:
                            {
                                thrustMagnitude = Utilities.CalculateThrustMagnitudeForTwoHandedWeapon(weaponWeight, effectiveSkillDR, thrustWeaponSpeed, 0f, Agent.UsageDirection.AttackDown);
                                break;
                            }
                            //default:
                            //    {
                            //        //thrustMagnitude = Game.Current.BasicModels.StrikeMagnitudeModel.CalculateStrikeMagnitudeForThrust(character, null, thrustWeaponSpeed, weaponWeight, weapon.Item, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex), 0f, false);
                            //        //break;
                            //    }
                    }
                }
                return thrustMagnitude;
            }

            public static MeleeHitType DecideMeleeHitType(ref AttackCollisionData collisionData)
            {
                MeleeHitType hitType = MeleeHitType.None;

                if (collisionData.AttackBlockedWithShield)
                {
                    if (collisionData.CorrectSideShieldBlock)
                    {
                        if (collisionData.CollisionResult == CombatCollisionResult.Blocked)
                        {
                            hitType = MeleeHitType.ShieldBlock;
                        }
                        else
                        {
                            hitType = MeleeHitType.ShieldParry;
                        }
                    }
                    else
                    {
                        hitType = MeleeHitType.ShieldIncorrectBlock;
                    }
                }
                else
                {
                    if (collisionData.CollisionResult == CombatCollisionResult.Blocked)
                    {
                        hitType = MeleeHitType.WeaponBlock;
                    }
                    else if (collisionData.CollisionResult == CombatCollisionResult.Parried)
                    {
                        hitType = MeleeHitType.WeaponParry;
                    }
                    else if (collisionData.CollisionResult == CombatCollisionResult.StrikeAgent)
                    {
                        hitType = MeleeHitType.AgentHit;
                    }
                }

                return hitType;
            }

            public static void handleAgentDropShield(Agent defAgent)
            {
                EquipmentIndex wieldedItemIndex = defAgent.GetWieldedItemIndex(HandIndex.OffHand);
                if (wieldedItemIndex != EquipmentIndex.None)
                {
                    if (!agentsToDropShield.Contains(defAgent))
                    {
                        agentsToDropShield.Add(defAgent);
                    }
                }
            }

            public static void handleAgentDropWeapon(Agent defAgent)
            {
                EquipmentIndex wieldedItemIndex = defAgent.GetWieldedItemIndex(HandIndex.MainHand);
                if (wieldedItemIndex != EquipmentIndex.None)
                {
                    if (!agentsToDropWeapon.Contains(defAgent))
                    {
                        agentsToDropWeapon.Add(defAgent);
                    }
                }
            }

            public static void HandleAttackerPostureBreak(MeleeHitType hitType, Posture attackerPosture, Agent defAgent, Agent ataAgent, float postureDmg, MissionWeapon attackerWeapon, ref Blow blow, ref Mission mission, ref AttackCollisionData collisionData, ref Vec3 blowDirection, ref Vec3 swingDirection, ref bool cancelDamage, CrushThroughState crushThroughState)
            {
                switch (hitType)
                {
                    case MeleeHitType.None:
                        {
                            break;
                        }
                    case MeleeHitType.WeaponBlock:
                        {
                            if (postureDmg >= attackerPosture.maxPosture * 0.33f)
                            {
                                makePostureRiposteBlow(ref mission, ref blow, ataAgent, defAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                handleAgentDropWeapon(defAgent);
                                addPostureDamageVisual(ataAgent, defAgent);
                            }
                            else
                            {
                                attackerPosture.posture = 0f;
                            }
                            break;
                        }
                    case MeleeHitType.WeaponParry:
                        {
                            if (ataAgent.IsPlayerControlled)
                            {
                                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_013}Posture break: Posture depleted, perfect parry").ToString(), Color.FromUint(4282569842u)));
                            }
                            if (ataAgent.HasMount)
                            {
                                makePostureRiposteBlow(ref mission, ref blow, ataAgent, defAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.CanDismount);
                            }
                            else
                            {
                                makePostureRiposteBlow(ref mission, ref blow, ataAgent, defAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                if (postureDmg >= attackerPosture.maxPosture * 0.33f)
                                {
                                    handleAgentDropWeapon(ataAgent);
                                }
                            }
                            ResetPostureForAgent(ref attackerPosture, PostureDamage.POSTURE_RESET_MODIFIER, ataAgent);
                            addPostureDamageVisual(ataAgent, defAgent);
                            break;
                        }
                    case MeleeHitType.AgentHit:
                    case MeleeHitType.ShieldIncorrectBlock:
                    case MeleeHitType.ShieldBlock:
                        {
                            attackerPosture.posture = 0f;
                            addPostureDamageVisual(ataAgent, defAgent);
                            break;
                        }
                    case MeleeHitType.ShieldParry:
                        {
                            if (ataAgent.IsPlayerControlled)
                            {
                                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_017}Posture break: Posture depleted, perfect parry, correct side block").ToString(), Color.FromUint(4282569842u)));
                            }
                            if (ataAgent.HasMount)
                            {
                                makePostureRiposteBlow(ref mission, ref blow, ataAgent, defAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.CanDismount);
                            }
                            else
                            {
                                makePostureRiposteBlow(ref mission, ref blow, ataAgent, defAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                if (postureDmg >= attackerPosture.maxPosture * 0.33f)
                                {
                                    handleAgentDropWeapon(ataAgent);
                                }
                            }
                            ResetPostureForAgent(ref attackerPosture, PostureDamage.POSTURE_RESET_MODIFIER, ataAgent);
                            break;
                        }
                    case MeleeHitType.ChamberBlock:
                        {
                            float healthDamage = calculateHealthDamage(attackerWeapon, ataAgent, defAgent, postureDmg, ref blow);
                            if (ataAgent.IsPlayerControlled)
                            {
                                MBTextManager.SetTextVariable("DMG", MathF.Floor(healthDamage));
                                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_019}Posture break: Posture depleted, chamber block {DMG} damage crushed through").ToString(), Color.FromUint(4282569842u)));
                            }
                            makePostureCrashThroughBlow(ref mission, ref blow, ataAgent, defAgent, MathF.Floor(healthDamage), ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockDown);
                            ResetPostureForAgent(ref attackerPosture, PostureDamage.POSTURE_RESET_MODIFIER, ataAgent);
                            break;
                        }
                }
            }

            public static void HandleDefenderPostureBreak(MeleeHitType hitType, Posture defenderPosture, Agent defAgent, Agent ataAgent, float postureDmg, MissionWeapon attackerWeapon, ref Blow blow, ref Mission mission, ref AttackCollisionData collisionData, ref Vec3 blowDirection, ref Vec3 swingDirection, ref bool cancelDamage, CrushThroughState crushThroughState)
            {
                switch (hitType)
                {
                    case MeleeHitType.None:
                        {
                            break;
                        }
                    case MeleeHitType.WeaponBlock:
                        {
                            float healthDamage = calculateHealthDamage(attackerWeapon, ataAgent, defAgent, postureDmg, ref blow);
                            showCrushThroughMessage(healthDamage, defAgent, ataAgent);
                            if (defAgent.HasMount)
                            {
                                makePostureCrashThroughBlow(ref mission, ref blow, ataAgent, defAgent, MathF.Floor(healthDamage), ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.CanDismount);
                            }
                            else
                            {
                                makePostureCrashThroughBlow(ref mission, ref blow, ataAgent, defAgent, MathF.Floor(healthDamage), ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                if (postureDmg >= defenderPosture.maxPosture * 0.33f)
                                {
                                    handleAgentDropWeapon(defAgent);
                                }
                            }
                            ResetPostureForAgent(ref defenderPosture, PostureDamage.POSTURE_RESET_MODIFIER, defAgent);
                            break;
                        }
                    case MeleeHitType.WeaponParry:
                        {
                            float healthDamage = calculateHealthDamage(attackerWeapon, ataAgent, defAgent, postureDmg, ref blow);
                            showCrushThroughMessage(healthDamage, defAgent, ataAgent);
                            if (defAgent.HasMount)
                            {
                                makePostureCrashThroughBlow(ref mission, ref blow, ataAgent, defAgent, MathF.Floor(healthDamage), ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                            }
                            else
                            {
                                makePostureCrashThroughBlow(ref mission, ref blow, ataAgent, defAgent, MathF.Floor(healthDamage), ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                if (postureDmg >= defenderPosture.maxPosture * 0.33f)
                                {
                                    handleAgentDropWeapon(defAgent);
                                }
                            }
                            ResetPostureForAgent(ref defenderPosture, PostureDamage.POSTURE_RESET_MODIFIER, defAgent);
                            break;
                        }
                    case MeleeHitType.AgentHit:
                        {
                            if (defAgent.HasMount)
                            {
                                makePostureBlow(ref mission, ref blow, ataAgent, defAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.CanDismount);
                            }
                            else
                            {
                                if (postureDmg >= defenderPosture.maxPosture * 0.33f && !attackerWeapon.IsEmpty && (
                                    attackerWeapon.CurrentUsageItem.WeaponClass == WeaponClass.TwoHandedAxe ||
                                    attackerWeapon.CurrentUsageItem.WeaponClass == WeaponClass.TwoHandedMace ||
                                    attackerWeapon.CurrentUsageItem.WeaponClass == WeaponClass.TwoHandedPolearm ||
                                    attackerWeapon.CurrentUsageItem.WeaponClass == WeaponClass.TwoHandedSword))
                                {
                                    makePostureBlow(ref mission, ref blow, ataAgent, defAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockDown);
                                }
                                else
                                {
                                    makePostureBlow(ref mission, ref blow, ataAgent, defAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                }
                            }
                            ResetPostureForAgent(ref defenderPosture, PostureDamage.POSTURE_RESET_MODIFIER, defAgent);
                            break;
                        }
                    case MeleeHitType.ShieldIncorrectBlock:
                        {
                            damageShield(defAgent, 150);
                            if (defAgent.IsPlayerControlled)
                            {
                                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_014}Posture break: Posture depleted, incorrect side block").ToString(), Color.FromUint(4282569842u)));
                            }
                            if (defAgent.HasMount)
                            {
                                makePostureBlow(ref mission, ref blow, ataAgent, defAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.CanDismount);
                            }
                            else
                            {
                                if (postureDmg >= defenderPosture.maxPosture * 0.33f)
                                {
                                    float healthDamage = calculateHealthDamage(attackerWeapon, ataAgent, defAgent, postureDmg, ref blow);
                                    makePostureCrashThroughBlow(ref mission, ref blow, ataAgent, defAgent, MathF.Floor(healthDamage), ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                    handleAgentDropShield(defAgent);
                                }
                                else
                                {
                                    makePostureBlow(ref mission, ref blow, ataAgent, defAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                }
                            }
                            ResetPostureForAgent(ref defenderPosture, PostureDamage.SHIELD_POSTURE_RESET_MODIFIER, defAgent);
                            break;
                        }
                    case MeleeHitType.ShieldBlock:
                        {
                            damageShield(defAgent, 125);
                            if (defAgent.IsPlayerControlled)
                            {
                                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_015}Posture break: Posture depleted, correct side block").ToString(), Color.FromUint(4282569842u)));
                            }
                            if (defAgent.HasMount)
                            {
                                makePostureBlow(ref mission, ref blow, ataAgent, defAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.CanDismount);
                            }
                            else
                            {
                                if (postureDmg >= defenderPosture.maxPosture * 0.33f)
                                {
                                    makePostureBlow(ref mission, ref blow, ataAgent, defAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                    handleAgentDropShield(defAgent);
                                }
                                else
                                {
                                    makePostureBlow(ref mission, ref blow, ataAgent, defAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                }
                            }
                            ResetPostureForAgent(ref defenderPosture, PostureDamage.SHIELD_POSTURE_RESET_MODIFIER, defAgent);
                            break;
                        }
                    case MeleeHitType.ShieldParry:
                        {
                            damageShield(defAgent, 100);
                            EquipmentIndex wieldedItemIndex = defAgent.GetWieldedItemIndex(0);
                            if (wieldedItemIndex != EquipmentIndex.None)
                            {
                                if (defAgent.IsPlayerControlled)
                                {
                                    InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_016}Posture break: Posture depleted, perfect parry, correct side block").ToString(), Color.FromUint(4282569842u)));
                                }
                                if (defAgent.HasMount)
                                {
                                    makePostureBlow(ref mission, ref blow, ataAgent, defAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                }
                                else
                                {
                                    makePostureBlow(ref mission, ref blow, ataAgent, defAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                    if (postureDmg >= defenderPosture.maxPosture * 0.33f)
                                    {
                                        handleAgentDropShield(defAgent);
                                    }
                                }
                            }
                            ResetPostureForAgent(ref defenderPosture, PostureDamage.SHIELD_POSTURE_RESET_MODIFIER, defAgent);
                            break;
                        }
                    case MeleeHitType.ChamberBlock:
                        {
                            if (defAgent.IsPlayerControlled)
                            {
                                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_018}Posture break: Posture depleted, chamber block").ToString(), Color.FromUint(4282569842u)));
                            }
                            makePostureBlow(ref mission, ref blow, ataAgent, defAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.NonTipThrust);
                            ResetPostureForAgent(ref defenderPosture, PostureDamage.POSTURE_RESET_MODIFIER, defAgent);
                            break;
                        }
                }
            }

            public static void ResetPostureForAgent(ref Posture posture, float postureResetModifier, Agent agent)
            {
                if (posture != null)
                {
                    float currentTime = Mission.Current.CurrentTime;
                    int restCount = posture.lastPostureLossTime > 0 ? MathF.Floor((currentTime - posture.lastPostureLossTime) / 20f) : 0;
                    posture.maxPostureLossCount = posture.maxPostureLossCount - restCount;
                    if (posture.maxPostureLossCount < 0)
                    {
                        posture.maxPostureLossCount = 0;
                    }
                    if (posture.maxPostureLossCount < 10)
                    {
                        posture.maxPostureLossCount++;
                    }
                    posture.posture = posture.maxPosture * (postureResetModifier * (1f - (0.05f * posture.maxPostureLossCount)));
                    posture.lastPostureLossTime = Mission.Current.CurrentTime;
                }
            }

            public static void showCrushThroughMessage(float healthDamage, Agent defAgent, Agent ataAgent)
            {
                MBTextManager.SetTextVariable("DMG", MathF.Floor(healthDamage));
                if (defAgent.IsPlayerControlled)
                {
                    InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_009}Posture break: Posture depleted, {DMG} damage crushed through").ToString(), Color.FromUint(4282569842u)));
                }
                if (ataAgent.IsPlayerControlled)
                {
                    InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_010}Enemy Posture break: Posture depleted, {DMG} damage crushed through").ToString(), Color.FromUint(4282569842u)));
                }
            }

            private static void addPostureDamageVisual(Agent attackerAgent, Agent victimAgent)
            {
                if (RBMConfig.RBMConfig.postureEnabled)
                {
                    if (victimAgent != null && attackerAgent != null && (victimAgent.IsPlayerControlled || attackerAgent.IsPlayerControlled))
                    {
                        Agent enemyAgent = null;
                        if (victimAgent.IsPlayerControlled)
                        {
                            enemyAgent = attackerAgent;
                            Posture posture = null;
                            if (AgentPostures.values.TryGetValue(victimAgent, out posture))
                            {
                                if (AgentPostures.postureVisual != null && AgentPostures.postureVisual._dataSource.ShowPlayerPostureStatus)
                                {
                                    AgentPostures.postureVisual._dataSource.PlayerPosture = (int)posture.posture;
                                    AgentPostures.postureVisual._dataSource.PlayerPostureMax = (int)posture.maxPosture;
                                    AgentPostures.postureVisual._dataSource.PlayerPostureText = ((int)posture.posture).ToString();
                                    AgentPostures.postureVisual._dataSource.PlayerPostureMaxText = ((int)posture.maxPosture).ToString();
                                }
                            }
                        }
                        else
                        {
                            enemyAgent = victimAgent;
                            Posture posture = null;
                            if (AgentPostures.values.TryGetValue(attackerAgent, out posture))
                            {
                                if (AgentPostures.postureVisual != null && AgentPostures.postureVisual._dataSource.ShowPlayerPostureStatus)
                                {
                                    AgentPostures.postureVisual._dataSource.PlayerPosture = (int)posture.posture;
                                    AgentPostures.postureVisual._dataSource.PlayerPostureMax = (int)posture.maxPosture;
                                    AgentPostures.postureVisual._dataSource.PlayerPostureText = ((int)posture.posture).ToString();
                                    AgentPostures.postureVisual._dataSource.PlayerPostureMaxText = ((int)posture.maxPosture).ToString();
                                }
                            }
                        }
                        if (AgentPostures.postureVisual != null)
                        {
                            Posture posture = null;
                            if (AgentPostures.values.TryGetValue(enemyAgent, out posture))
                            {
                                AgentPostures.postureVisual._dataSource.ShowEnemyStatus = true;
                                AgentPostures.postureVisual.affectedAgent = enemyAgent;
                                if (AgentPostures.postureVisual._dataSource.ShowEnemyStatus && AgentPostures.postureVisual.affectedAgent == enemyAgent)
                                {
                                    AgentPostures.postureVisual.timer = AgentPostures.postureVisual.DisplayTime;
                                    AgentPostures.postureVisual._dataSource.EnemyPosture = (int)posture.posture;
                                    AgentPostures.postureVisual._dataSource.EnemyPostureMax = (int)posture.maxPosture;
                                    AgentPostures.postureVisual._dataSource.EnemyHealth = (int)enemyAgent.Health;
                                    AgentPostures.postureVisual._dataSource.EnemyHealthMax = (int)enemyAgent.HealthLimit;
                                    if (enemyAgent.IsMount)
                                    {
                                        AgentPostures.postureVisual._dataSource.EnemyName = enemyAgent.RiderAgent?.Name + " (" + new TextObject("{=mountnoun}Mount").ToString() + ")";
                                    }
                                    else
                                    {
                                        AgentPostures.postureVisual._dataSource.EnemyName = enemyAgent.Name;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            private static void damageShield(Agent victim, int ammount)
            {
                for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                {
                    if (victim.Equipment != null && !victim.Equipment[equipmentIndex].IsEmpty)
                    {
                        if (victim.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Shield && !victim.WieldedOffhandWeapon.IsEmpty && victim.WieldedOffhandWeapon.Item.Id == victim.Equipment[equipmentIndex].Item.Id)
                        {
                            int num = MathF.Max(0, victim.Equipment[equipmentIndex].HitPoints - ammount);
                            victim.ChangeWeaponHitPoints(equipmentIndex, (short)num);
                            break;
                        }
                    }
                }
            }

            private static void makePostureBlow(ref Mission mission, ref Blow blow, Agent attackerAgent, Agent victimAgent, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, CrushThroughState crushThroughState, Vec3 blowDirection, Vec3 swingDirection, bool cancelDamage, BlowFlags addedBlowFlag)
            {
                Blow newBLow = blow;
                newBLow.BaseMagnitude = collisionData.BaseMagnitude;
                newBLow.MovementSpeedDamageModifier = collisionData.MovementSpeedDamageModifier;
                newBLow.InflictedDamage = 1;
                newBLow.SelfInflictedDamage = collisionData.SelfInflictedDamage;
                newBLow.AbsorbedByArmor = collisionData.AbsorbedByArmor;

                sbyte weaponAttachBoneIndex = (sbyte)(attackerWeapon.IsEmpty ? (-1) : attackerAgent.Monster.GetBoneToAttachForItemFlags(attackerWeapon.Item.ItemFlags));
                newBLow.WeaponRecord.FillAsMeleeBlow(attackerWeapon.Item, attackerWeapon.CurrentUsageItem, collisionData.AffectorWeaponSlotOrMissileIndex, weaponAttachBoneIndex);
                newBLow.StrikeType = (StrikeType)collisionData.StrikeType;
                newBLow.DamageType = ((!attackerWeapon.IsEmpty && true && !collisionData.IsAlternativeAttack) ? ((DamageTypes)collisionData.DamageType) : DamageTypes.Blunt);
                newBLow.NoIgnore = collisionData.IsAlternativeAttack;
                newBLow.AttackerStunPeriod = collisionData.AttackerStunPeriod;
                newBLow.DefenderStunPeriod = collisionData.DefenderStunPeriod;
                newBLow.BlowFlag = BlowFlags.None;
                newBLow.GlobalPosition = collisionData.CollisionGlobalPosition;
                newBLow.BoneIndex = collisionData.CollisionBoneIndex;
                newBLow.Direction = blowDirection;
                newBLow.SwingDirection = swingDirection;
                //blow.InflictedDamage = 1;
                newBLow.VictimBodyPart = collisionData.VictimHitBodyPart;
                newBLow.BlowFlag |= addedBlowFlag;
                victimAgent.RegisterBlow(newBLow, collisionData);
                foreach (MissionBehavior missionBehaviour in mission.MissionBehaviors)
                {
                    missionBehaviour.OnRegisterBlow(attackerAgent, victimAgent, null, newBLow, ref collisionData, in attackerWeapon);
                }
            }

            private static void makePostureCrashThroughBlow(ref Mission mission, ref Blow blow, Agent attackerAgent, Agent victimAgent, int inflictedHpDmg, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, CrushThroughState crushThroughState, Vec3 blowDirection, Vec3 swingDirection, bool cancelDamage, BlowFlags addedBlowFlag)
            {
                Blow newBLow = blow;
                newBLow.BaseMagnitude = collisionData.BaseMagnitude;
                newBLow.MovementSpeedDamageModifier = collisionData.MovementSpeedDamageModifier;
                newBLow.InflictedDamage = inflictedHpDmg;
                newBLow.SelfInflictedDamage = collisionData.SelfInflictedDamage;
                newBLow.AbsorbedByArmor = collisionData.AbsorbedByArmor;

                sbyte weaponAttachBoneIndex = (sbyte)(attackerWeapon.IsEmpty ? (-1) : attackerAgent.Monster.GetBoneToAttachForItemFlags(attackerWeapon.Item.ItemFlags));
                newBLow.WeaponRecord.FillAsMeleeBlow(attackerWeapon.Item, attackerWeapon.CurrentUsageItem, collisionData.AffectorWeaponSlotOrMissileIndex, weaponAttachBoneIndex);
                newBLow.StrikeType = (StrikeType)collisionData.StrikeType;
                newBLow.DamageType = ((!attackerWeapon.IsEmpty && true && !collisionData.IsAlternativeAttack) ? ((DamageTypes)collisionData.DamageType) : DamageTypes.Blunt);
                newBLow.NoIgnore = collisionData.IsAlternativeAttack;
                newBLow.AttackerStunPeriod = collisionData.AttackerStunPeriod / 5f;
                newBLow.DefenderStunPeriod = collisionData.DefenderStunPeriod * 5f;
                newBLow.BlowFlag = BlowFlags.None;
                newBLow.GlobalPosition = collisionData.CollisionGlobalPosition;
                newBLow.BoneIndex = collisionData.CollisionBoneIndex;
                newBLow.Direction = blowDirection;
                newBLow.SwingDirection = swingDirection;
                //blow.InflictedDamage = 1;
                newBLow.VictimBodyPart = collisionData.VictimHitBodyPart;
                //newBLow.BlowFlag |= BlowFlags.CrushThrough;
                newBLow.BlowFlag |= addedBlowFlag;
                victimAgent.RegisterBlow(newBLow, collisionData);
                foreach (MissionBehavior missionBehaviour in mission.MissionBehaviors)
                {
                    missionBehaviour.OnRegisterBlow(attackerAgent, victimAgent, null, newBLow, ref collisionData, in attackerWeapon);
                }
            }

            private static void makePostureRiposteBlow(ref Mission mission, ref Blow blow, Agent attackerAgent, Agent victimAgent, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, CrushThroughState crushThroughState, Vec3 blowDirection, Vec3 swingDirection, bool cancelDamage, BlowFlags addedBlowFlag)
            {
                Blow newBLow = blow;
                newBLow.BaseMagnitude = collisionData.BaseMagnitude;
                newBLow.MovementSpeedDamageModifier = collisionData.MovementSpeedDamageModifier;
                newBLow.InflictedDamage = 1;
                newBLow.SelfInflictedDamage = collisionData.SelfInflictedDamage;
                newBLow.AbsorbedByArmor = collisionData.AbsorbedByArmor;

                sbyte weaponAttachBoneIndex = (sbyte)(attackerWeapon.IsEmpty ? (-1) : attackerAgent.Monster.GetBoneToAttachForItemFlags(attackerWeapon.Item.ItemFlags));
                newBLow.WeaponRecord.FillAsMeleeBlow(attackerWeapon.Item, attackerWeapon.CurrentUsageItem, collisionData.AffectorWeaponSlotOrMissileIndex, weaponAttachBoneIndex);
                newBLow.StrikeType = (StrikeType)collisionData.StrikeType;
                newBLow.DamageType = ((!attackerWeapon.IsEmpty && true && !collisionData.IsAlternativeAttack) ? ((DamageTypes)collisionData.DamageType) : DamageTypes.Blunt);
                newBLow.NoIgnore = collisionData.IsAlternativeAttack;
                newBLow.AttackerStunPeriod = collisionData.AttackerStunPeriod;
                newBLow.DefenderStunPeriod = collisionData.DefenderStunPeriod;
                newBLow.BlowFlag = BlowFlags.None;
                newBLow.GlobalPosition = collisionData.CollisionGlobalPosition;
                newBLow.BoneIndex = collisionData.CollisionBoneIndex;
                newBLow.Direction = blowDirection;
                newBLow.SwingDirection = swingDirection;
                //blow.InflictedDamage = 1;
                newBLow.VictimBodyPart = collisionData.VictimHitBodyPart;
                newBLow.BlowFlag |= addedBlowFlag;
                attackerAgent.RegisterBlow(newBLow, collisionData);
                foreach (MissionBehavior missionBehaviour in mission.MissionBehaviors)
                {
                    missionBehaviour.OnRegisterBlow(victimAgent, attackerAgent, null, newBLow, ref collisionData, in attackerWeapon);
                }
            }

            private static void Postfix(ref Mission __instance, ref Blow __result, Agent attackerAgent, Agent victimAgent, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, CrushThroughState crushThroughState, Vec3 blowDirection, Vec3 swingDirection, bool cancelDamage)
            {
                if ((new StackTrace()).GetFrame(3).GetMethod().Name.Contains("MeleeHit") && victimAgent != null && victimAgent.IsHuman)
                {
                    if (RBMConfig.RBMConfig.postureEnabled && attackerAgent != null && victimAgent != null && !attackerAgent.IsFriendOf(victimAgent) && attackerWeapon.CurrentUsageItem != null &&
                        attackerWeapon.CurrentUsageItem != null)
                    {
                        Posture defenderPosture = null;
                        Posture attackerPosture = null;
                        AgentPostures.values.TryGetValue(victimAgent, out defenderPosture);
                        AgentPostures.values.TryGetValue(attackerAgent, out attackerPosture);

                        float comHitModifier = Utilities.GetComHitModifier(in collisionData, in attackerWeapon);

                        MeleeHitType hitType = DecideMeleeHitType(ref collisionData);

                        switch (hitType)
                        {
                            case MeleeHitType.None:
                                {
                                    break;
                                }
                            case MeleeHitType.WeaponBlock:
                                {
                                    if (defenderPosture != null)
                                    {
                                        float postureDmg = PostureDamage.getDefenderPostureDamage(victimAgent, attackerAgent, collisionData.AttackDirection, (StrikeType)collisionData.StrikeType, hitType);
                                        defenderPosture.posture -= postureDmg;
                                        if (defenderPosture.posture <= 0f)
                                        {
                                            HandleDefenderPostureBreak(hitType, defenderPosture, victimAgent, attackerAgent, postureDmg, attackerWeapon, ref __result, ref __instance, ref collisionData, ref blowDirection, ref swingDirection, ref cancelDamage, crushThroughState);
                                        }
                                    }
                                    if (attackerPosture != null)
                                    {
                                        float postureDmg = PostureDamage.getAttackerPostureDamage(victimAgent, attackerAgent, collisionData.AttackDirection, (StrikeType)collisionData.StrikeType, hitType);
                                        attackerPosture.posture -= postureDmg;
                                        if (attackerPosture.posture <= 0f)
                                        {
                                            HandleAttackerPostureBreak(hitType, attackerPosture, victimAgent, attackerAgent, postureDmg, attackerWeapon, ref __result, ref __instance, ref collisionData, ref blowDirection, ref swingDirection, ref cancelDamage, crushThroughState);
                                        }
                                    }
                                    addPostureDamageVisual(attackerAgent, victimAgent);
                                    break;
                                }
                            case MeleeHitType.WeaponParry:
                                {
                                    if (defenderPosture != null)
                                    {
                                        float postureDmg = PostureDamage.getDefenderPostureDamage(victimAgent, attackerAgent, collisionData.AttackDirection, (StrikeType)collisionData.StrikeType, hitType);
                                        defenderPosture.posture -= postureDmg;
                                        if (defenderPosture.posture <= 0f)
                                        {
                                            HandleDefenderPostureBreak(hitType, defenderPosture, victimAgent, attackerAgent, postureDmg, attackerWeapon, ref __result, ref __instance, ref collisionData, ref blowDirection, ref swingDirection, ref cancelDamage, crushThroughState);
                                        }
                                    }
                                    if (attackerPosture != null)
                                    {
                                        float postureDmg = PostureDamage.getAttackerPostureDamage(victimAgent, attackerAgent, collisionData.AttackDirection, (StrikeType)collisionData.StrikeType, hitType);
                                        attackerPosture.posture -= postureDmg;
                                        if (attackerPosture.posture <= 0f)
                                        {
                                            HandleAttackerPostureBreak(hitType, attackerPosture, victimAgent, attackerAgent, postureDmg, attackerWeapon, ref __result, ref __instance, ref collisionData, ref blowDirection, ref swingDirection, ref cancelDamage, crushThroughState);
                                        }
                                    }
                                    addPostureDamageVisual(attackerAgent, victimAgent);
                                    break;
                                }
                            case MeleeHitType.AgentHit:
                                {
                                    if (defenderPosture != null)
                                    {
                                        float postureDmg = collisionData.InflictedDamage;
                                        defenderPosture.posture -= postureDmg;
                                        if (defenderPosture.posture <= 0f)
                                        {
                                            HandleDefenderPostureBreak(hitType, defenderPosture, victimAgent, attackerAgent, postureDmg, attackerWeapon, ref __result, ref __instance, ref collisionData, ref blowDirection, ref swingDirection, ref cancelDamage, crushThroughState);
                                        }
                                    }
                                    if (attackerPosture != null)
                                    {
                                        float postureDmg = PostureDamage.getAttackerPostureDamage(victimAgent, attackerAgent, collisionData.AttackDirection, (StrikeType)collisionData.StrikeType, hitType);
                                        attackerPosture.posture -= postureDmg;
                                        if (attackerPosture.posture <= 0f)
                                        {
                                            HandleAttackerPostureBreak(hitType, attackerPosture, victimAgent, attackerAgent, postureDmg, attackerWeapon, ref __result, ref __instance, ref collisionData, ref blowDirection, ref swingDirection, ref cancelDamage, crushThroughState);
                                        }
                                    }
                                    addPostureDamageVisual(attackerAgent, victimAgent);
                                    break;
                                }
                            case MeleeHitType.ShieldIncorrectBlock:
                                {
                                    if (defenderPosture != null)
                                    {
                                        float postureDmg = PostureDamage.getDefenderPostureDamage(victimAgent, attackerAgent, collisionData.AttackDirection, (StrikeType)collisionData.StrikeType, hitType);
                                        defenderPosture.posture -= postureDmg;
                                        if (defenderPosture.posture <= 0f)
                                        {
                                            HandleDefenderPostureBreak(hitType, defenderPosture, victimAgent, attackerAgent, postureDmg, attackerWeapon, ref __result, ref __instance, ref collisionData, ref blowDirection, ref swingDirection, ref cancelDamage, crushThroughState);
                                        }
                                    }
                                    if (attackerPosture != null)
                                    {
                                        float postureDmg = PostureDamage.getAttackerPostureDamage(victimAgent, attackerAgent, collisionData.AttackDirection, (StrikeType)collisionData.StrikeType, hitType);
                                        attackerPosture.posture -= postureDmg;
                                        if (attackerPosture.posture <= 0f)
                                        {
                                            HandleAttackerPostureBreak(hitType, attackerPosture, victimAgent, attackerAgent, postureDmg, attackerWeapon, ref __result, ref __instance, ref collisionData, ref blowDirection, ref swingDirection, ref cancelDamage, crushThroughState);
                                        }
                                    }
                                    addPostureDamageVisual(attackerAgent, victimAgent);
                                    break;
                                }
                            case MeleeHitType.ShieldBlock:
                                {
                                    if (defenderPosture != null)
                                    {
                                        float postureDmg = PostureDamage.getDefenderPostureDamage(victimAgent, attackerAgent, collisionData.AttackDirection, (StrikeType)collisionData.StrikeType, hitType);
                                        defenderPosture.posture -= postureDmg;
                                        if (defenderPosture.posture <= 0f)
                                        {
                                            HandleDefenderPostureBreak(hitType, defenderPosture, victimAgent, attackerAgent, postureDmg, attackerWeapon, ref __result, ref __instance, ref collisionData, ref blowDirection, ref swingDirection, ref cancelDamage, crushThroughState);
                                        }
                                    }
                                    if (attackerPosture != null)
                                    {
                                        float postureDmg = PostureDamage.getAttackerPostureDamage(victimAgent, attackerAgent, collisionData.AttackDirection, (StrikeType)collisionData.StrikeType, hitType);
                                        attackerPosture.posture -= postureDmg;
                                        if (attackerPosture.posture <= 0f)
                                        {
                                            HandleAttackerPostureBreak(hitType, attackerPosture, victimAgent, attackerAgent, postureDmg, attackerWeapon, ref __result, ref __instance, ref collisionData, ref blowDirection, ref swingDirection, ref cancelDamage, crushThroughState);
                                        }
                                    }
                                    addPostureDamageVisual(attackerAgent, victimAgent);
                                    break;
                                }
                            case MeleeHitType.ShieldParry:
                                {
                                    if (defenderPosture != null)
                                    {
                                        float postureDmg = PostureDamage.getDefenderPostureDamage(victimAgent, attackerAgent, collisionData.AttackDirection, (StrikeType)collisionData.StrikeType, hitType);
                                        defenderPosture.posture -= postureDmg;
                                        if (defenderPosture.posture <= 0f)
                                        {
                                            HandleDefenderPostureBreak(hitType, defenderPosture, victimAgent, attackerAgent, postureDmg, attackerWeapon, ref __result, ref __instance, ref collisionData, ref blowDirection, ref swingDirection, ref cancelDamage, crushThroughState);
                                        }
                                    }
                                    if (attackerPosture != null)
                                    {
                                        float postureDmg = PostureDamage.getAttackerPostureDamage(victimAgent, attackerAgent, collisionData.AttackDirection, (StrikeType)collisionData.StrikeType, hitType);
                                        attackerPosture.posture -= postureDmg;
                                        if (attackerPosture.posture <= 0f)
                                        {
                                            HandleAttackerPostureBreak(hitType, attackerPosture, victimAgent, attackerAgent, postureDmg, attackerWeapon, ref __result, ref __instance, ref collisionData, ref blowDirection, ref swingDirection, ref cancelDamage, crushThroughState);
                                        }
                                    }
                                    addPostureDamageVisual(attackerAgent, victimAgent);
                                    break;
                                }
                            case MeleeHitType.ChamberBlock:
                                {
                                    if (defenderPosture != null)
                                    {
                                        float postureDmg = PostureDamage.getDefenderPostureDamage(victimAgent, attackerAgent, collisionData.AttackDirection, (StrikeType)collisionData.StrikeType, hitType);
                                        defenderPosture.posture -= postureDmg;
                                        if (defenderPosture.posture <= 0f)
                                        {
                                            HandleDefenderPostureBreak(hitType, defenderPosture, victimAgent, attackerAgent, postureDmg, attackerWeapon, ref __result, ref __instance, ref collisionData, ref blowDirection, ref swingDirection, ref cancelDamage, crushThroughState);
                                        }
                                    }
                                    if (attackerPosture != null)
                                    {
                                        float postureDmg = PostureDamage.getAttackerPostureDamage(victimAgent, attackerAgent, collisionData.AttackDirection, (StrikeType)collisionData.StrikeType, hitType);
                                        attackerPosture.posture -= postureDmg;
                                        if (attackerPosture.posture <= 0f)
                                        {
                                            HandleAttackerPostureBreak(hitType, attackerPosture, victimAgent, attackerAgent, postureDmg, attackerWeapon, ref __result, ref __instance, ref collisionData, ref blowDirection, ref swingDirection, ref cancelDamage, crushThroughState);
                                        }
                                        else
                                        {
                                            float healthDamage = calculateHealthDamage(attackerWeapon, attackerAgent, victimAgent, postureDmg, ref __result);
                                            if (attackerAgent.IsPlayerControlled)
                                            {
                                                MBTextManager.SetTextVariable("DMG", MathF.Floor(healthDamage));
                                                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_020}Chamber block {DMG} damage crushed through").ToString(), Color.FromUint(4282569842u)));
                                            }
                                            makePostureCrashThroughBlow(ref __instance, ref __result, attackerAgent, victimAgent, MathF.Floor(healthDamage), ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                            ResetPostureForAgent(ref attackerPosture, PostureDamage.POSTURE_RESET_MODIFIER, attackerAgent);
                                        }
                                    }
                                    addPostureDamageVisual(attackerAgent, victimAgent);
                                    break;
                                }
                        }
                    }
                }
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
                        Posture shooterPosture = null;
                        AgentPostures.values.TryGetValue(shooterAgent, out shooterPosture);
                        if (shooterPosture != null)
                        {
                            float currentTime = Mission.Current.CurrentTime;
                            switch (wc)
                            {
                                case WeaponClass.Bow:
                                    {
                                        shooterPosture.posture = shooterPosture.posture - calculateRangedPostureLoss(35f, 25f, shooterAgent, wc);
                                        break;
                                    }
                                case WeaponClass.Crossbow:
                                    {
                                        shooterPosture.posture = shooterPosture.posture - calculateRangedPostureLoss(5f, 5f, shooterAgent, wc);
                                        break;
                                    }
                                case WeaponClass.Javelin:
                                    {
                                        shooterPosture.posture = shooterPosture.posture - calculateRangedPostureLoss(25f, 25f, shooterAgent, wc);
                                        break;
                                    }
                                case WeaponClass.ThrowingAxe:
                                case WeaponClass.ThrowingKnife:
                                    {
                                        shooterPosture.posture = shooterPosture.posture - calculateRangedPostureLoss(25f, 25f, shooterAgent, wc);
                                        break;
                                    }
                            }
                            if (shooterPosture.posture < 0f)
                            {
                                float postureResetModifier = 0.5f;
                                ResetPostureForAgent(ref shooterPosture, postureResetModifier, shooterAgent);
                            }
                            //shooterPosture.lastPostureLossTime = currentTime;
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(TournamentRound))]
        [HarmonyPatch("EndMatch")]
        private class EndMatchPatch
        {
            private static void Postfix(ref TournamentRound __instance)
            {
                foreach (KeyValuePair<Agent, Posture> entry in AgentPostures.values)
                {
                    entry.Value.posture = entry.Value.maxPosture;
                    if (RBMConfig.RBMConfig.postureGUIEnabled)
                    {
                        if (entry.Key.IsPlayerControlled)
                        {
                            //InformationManager.DisplayMessage(new InformationMessage(entry.Value.posture.ToString()));
                            if (AgentPostures.postureVisual != null && AgentPostures.postureVisual._dataSource.ShowPlayerPostureStatus)
                            {
                                AgentPostures.postureVisual._dataSource.PlayerPosture = (int)entry.Value.posture;
                                AgentPostures.postureVisual._dataSource.PlayerPostureMax = (int)entry.Value.maxPosture;
                                AgentPostures.postureVisual._dataSource.PlayerPostureText = ((int)entry.Value.posture).ToString();
                                AgentPostures.postureVisual._dataSource.PlayerPostureMaxText = ((int)entry.Value.maxPosture).ToString();
                            }
                        }

                        if (AgentPostures.postureVisual != null && AgentPostures.postureVisual._dataSource.ShowEnemyStatus && AgentPostures.postureVisual.affectedAgent == entry.Key)
                        {
                            AgentPostures.postureVisual._dataSource.EnemyPosture = (int)entry.Value.posture;
                            AgentPostures.postureVisual._dataSource.EnemyPostureMax = (int)entry.Value.maxPosture;
                        }
                    }
                }
                agentsToDropShield.Clear();
                agentsToDropWeapon.Clear();
                agentsToChangeFormation.Clear();
                AgentPostures.values.Clear();
            }
        }

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("EquipItemsFromSpawnEquipment")]
        private class EquipItemsFromSpawnEquipmentPatch
        {
            private static void Prefix(ref Agent __instance)
            {
                if (__instance.IsHuman)
                {
                    AgentPostures.values[__instance] = new Posture();
                }
            }
        }

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("OnShieldDamaged")]
        private class OnShieldDamagedPatch
        {
            private static bool Prefix(ref Agent __instance, ref EquipmentIndex slotIndex, ref int inflictedDamage)
            {
                int num = MathF.Max(0, __instance.Equipment[slotIndex].HitPoints - inflictedDamage);
                __instance.ChangeWeaponHitPoints(slotIndex, (short)num);
                if (num == 0)
                {
                    __instance.RemoveEquippedWeapon(slotIndex);
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("OnWieldedItemIndexChange")]
        private class OnWieldedItemIndexChangePatch
        {
            private static void Postfix(ref Agent __instance, bool isOffHand, bool isWieldedInstantly, bool isWieldedOnSpawn)
            {
                float playerPostureModifier = RBMConfig.RBMConfig.playerPostureMultiplier;
                if (RBMConfig.RBMConfig.postureEnabled)
                {
                    Posture posture = null;
                    AgentPostures.values.TryGetValue(__instance, out posture);
                    if (posture == null)
                    {
                        AgentPostures.values[__instance] = new Posture();
                    }
                    AgentPostures.values.TryGetValue(__instance, out posture);
                    if (posture != null)
                    {
                        float oldPosture = posture.posture;
                        float oldMaxPosture = posture.maxPosture;
                        float oldPosturePercentage = oldPosture / oldMaxPosture;

                        EquipmentIndex slotIndex = __instance.GetWieldedItemIndex(0);
                        if (slotIndex != EquipmentIndex.None)
                        {
                            int usageIndex = __instance.Equipment[slotIndex].CurrentUsageIndex;

                            WeaponComponentData wcd = __instance.Equipment[slotIndex].GetWeaponComponentDataForUsage(usageIndex);
                            SkillObject weaponSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(wcd.WeaponClass);
                            int effectiveWeaponSkill;
                            if (weaponSkill != null)
                            {
                                effectiveWeaponSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(__instance, weaponSkill);
                            }
                            else
                            {
                                effectiveWeaponSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(__instance, DefaultSkills.Athletics);
                            }

                            float athleticBase = 20f;
                            float weaponSkillBase = 80f;
                            float strengthSkillModifier = 500f;
                            float weaponSkillModifier = 500f;
                            float athleticRegenBase = 0.008f;
                            float weaponSkillRegenBase = 0.032f;
                            float baseModifier = 1f;
                            int effectiveStrengthSkill;

                            if (__instance.HasMount)
                            {
                                effectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(__instance, DefaultSkills.Riding);
                            }
                            else
                            {
                                effectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(__instance, DefaultSkills.Athletics);
                            }

                            float maxPosture = (athleticBase * (baseModifier + (effectiveStrengthSkill / strengthSkillModifier))) +
                                                 (weaponSkillBase * (baseModifier + (effectiveWeaponSkill / weaponSkillModifier)));

                            //heavier armor = higher max posture
                            maxPosture *= (1f + getEquipWeightModifier(__instance));
                            //heavier weapons = less max posture
                            maxPosture *= (1f - getWeaponWeightModifier(__instance));

                            posture.maxPosture = maxPosture;

                            float regenPerTick = (athleticRegenBase * (baseModifier + (effectiveStrengthSkill / strengthSkillModifier))) +
                                                 (weaponSkillRegenBase * (baseModifier + (effectiveWeaponSkill / weaponSkillModifier)));

                            //heavier equipment = slower posture regen
                            regenPerTick *= (1f - getEquipWeightModifier(__instance));
                            //heavier weapons = slower posture regen
                            regenPerTick *= (1f - getWeaponWeightModifier(__instance));

                            posture.regenPerTick = regenPerTick;

                            if (__instance.IsPlayerControlled)
                            {
                                posture.maxPosture *= playerPostureModifier;
                                posture.regenPerTick *= playerPostureModifier;
                            }

                            posture.posture = posture.maxPosture * oldPosturePercentage;
                        }
                    }
                }
            }
        }

        //[HarmonyPatch(typeof(Mission))]
        //[HarmonyPatch("OnAgentDismount")]
        //public class OnAgentDismountPatch
        //{
        //    private static void Postfix(Agent agent, Mission __instance)
        //    {
        //        if (!agent.IsPlayerControlled && agent.Formation != null && Mission.Current != null && Mission.Current.IsFieldBattle && agent.IsActive())
        //        {
        //            bool isInfFormationActive = agent.Team.GetFormation(FormationClass.Infantry) != null && agent.Team.GetFormation(FormationClass.Infantry).CountOfUnits > 0;
        //            bool isArcFormationActive = agent.Team.GetFormation(FormationClass.Ranged) != null && agent.Team.GetFormation(FormationClass.Ranged).CountOfUnits > 0;
        //            if (agent.Equipment.HasRangedWeapon(WeaponClass.Arrow) || agent.Equipment.HasRangedWeapon(WeaponClass.Bolt))
        //            {
        //                float distanceToInf = -1f;
        //                float distanceToArc = -1f;
        //                if (agent.Formation != null && isInfFormationActive)
        //                {
        //                    distanceToInf = agent.Team.GetFormation(FormationClass.Infantry).QuerySystem.MedianPosition.AsVec2.Distance(agent.Formation.QuerySystem.MedianPosition.AsVec2);
        //                }
        //                if (agent.Formation != null && isArcFormationActive)
        //                {
        //                    distanceToArc = agent.Team.GetFormation(FormationClass.Ranged).QuerySystem.MedianPosition.AsVec2.Distance(agent.Formation.QuerySystem.MedianPosition.AsVec2);
        //                }
        //                if (distanceToArc > 0f && distanceToArc < distanceToInf)
        //                {
        //                    if (agent != null && agent.IsActive())
        //                    {
        //                        try
        //                        {
        //                            agentsToChangeFormation[agent] = FormationClass.Ranged;
        //                            return;
        //                        }
        //                        catch (Exception ex)
        //                        {
        //                            return;
        //                        }
        //                    }
        //                }
        //                else if (distanceToInf > 0f && distanceToInf < distanceToArc)
        //                {
        //                    if (agent != null && agent.IsActive())
        //                    {
        //                        try
        //                        {
        //                            agentsToChangeFormation[agent] = FormationClass.Infantry;
        //                            return;
        //                        }
        //                        catch (Exception ex)
        //                        {
        //                            return;
        //                        }
        //                    }
        //                }
        //                else
        //                {
        //                    if (distanceToInf > 0f)
        //                    {
        //                        if (agent != null && agent.IsActive())
        //                        {
        //                            try
        //                            {
        //                                agentsToChangeFormation[agent] = FormationClass.Infantry;
        //                                return;
        //                            }
        //                            catch (Exception ex)
        //                            {
        //                                return;
        //                            }
        //                        }
        //                    }
        //                    else if (distanceToArc > 0f)
        //                    {
        //                        if (agent != null && agent.IsActive())
        //                        {
        //                            try
        //                            {
        //                                agentsToChangeFormation[agent] = FormationClass.Ranged;
        //                                return;
        //                            }
        //                            catch (Exception ex)
        //                            {
        //                                return;
        //                            }
        //                        }
        //                    }
        //                }
        //            }
        //            else
        //            {
        //                if (agent.Formation != null && isInfFormationActive)
        //                {
        //                    if (agent != null && agent.IsActive())
        //                    {
        //                        try
        //                        {
        //                            agentsToChangeFormation[agent] = FormationClass.Infantry;
        //                            return;
        //                        }
        //                        catch (Exception ex)
        //                        {
        //                            return;
        //                        }
        //                    }
        //                }
        //            }
        //        }
        //    }
        //}

        //[HarmonyPatch(typeof(Mission))]
        //[HarmonyPatch("OnAgentMount")]
        //internal class OnAgentMountPatch
        //{
        //    private static void Postfix(Agent agent, Mission __instance)
        //    {
        //        if (!agent.IsPlayerControlled && agent.Formation != null && Mission.Current != null && Mission.Current.IsFieldBattle && agent.IsActive())
        //        {
        //            bool isCavFormationActive = agent.Team.GetFormation(FormationClass.Cavalry) != null && agent.Team.GetFormation(FormationClass.Cavalry).CountOfUnits > 0;
        //            bool isHaFormationActive = agent.Team.GetFormation(FormationClass.HorseArcher) != null && agent.Team.GetFormation(FormationClass.HorseArcher).CountOfUnits > 0;
        //            if (agent.Equipment.HasRangedWeapon(WeaponClass.Arrow) || agent.Equipment.HasRangedWeapon(WeaponClass.Bolt))
        //            {
        //                if (agent.Formation != null && isHaFormationActive)
        //                {
        //                    if (agent.IsActive())
        //                    {
        //                        try
        //                        {
        //                            agentsToChangeFormation[agent] = FormationClass.HorseArcher;
        //                            return;
        //                        }
        //                        catch (Exception ex)
        //                        {
        //                            return;
        //                        }
        //                    }
        //                }
        //            }
        //            else
        //            {
        //                if (agent.Formation != null && isCavFormationActive)
        //                {
        //                    if (agent.IsActive())
        //                    {
        //                        try
        //                        {
        //                            agentsToChangeFormation[agent] = FormationClass.Cavalry;
        //                            return;
        //                        }
        //                        catch (Exception ex)
        //                        {
        //                            return;
        //                        }
        //                    }
        //                }
        //            }
        //        }
        //    }
        //}
    }
}