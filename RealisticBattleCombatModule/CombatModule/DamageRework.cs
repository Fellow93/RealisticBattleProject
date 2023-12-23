using HarmonyLib;
using SandBox.GameComponents;
using SandBox.Missions.MissionLogics;
using System;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ItemObject;
using static TaleWorlds.MountAndBlade.Agent;

namespace RBMCombat
{
    internal class DamageRework
    {
        [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
        [HarmonyPatch("GetEntityDamageMultiplier")]
        private class GetEntityDamageMultiplierPatch
        {
            private static bool Prefix(bool isAttackerAgentDoingPassiveAttack, WeaponComponentData weapon, DamageTypes damageType, bool isWoodenBody, ref float __result)
            {
                float dmgMultiplier = 1f;
                if (isAttackerAgentDoingPassiveAttack)
                {
                    dmgMultiplier *= 0.2f;
                }
                if (weapon != null)
                {
                    if (weapon.WeaponFlags.HasAnyFlag(WeaponFlags.BonusAgainstShield))
                    {
                        dmgMultiplier *= 1.2f;
                    }
                    switch (damageType)
                    {
                        case DamageTypes.Cut:
                            if (weapon.WeaponClass == WeaponClass.Arrow || weapon.WeaponClass == WeaponClass.Bolt || weapon.WeaponClass == WeaponClass.Javelin)
                            {
                                dmgMultiplier *= 0.1f;
                            }
                            else
                            {
                                dmgMultiplier *= 0.8f;
                            }
                            break;

                        case DamageTypes.Pierce:
                            if (weapon.WeaponClass == WeaponClass.Arrow || weapon.WeaponClass == WeaponClass.Bolt || weapon.WeaponClass == WeaponClass.Javelin)
                            {
                                dmgMultiplier *= 0.1f;
                            }
                            else
                            {
                                dmgMultiplier *= 0.2f;
                            }
                            break;
                    }
                    if (isWoodenBody && weapon.WeaponFlags.HasAnyFlag(WeaponFlags.Burning))
                    {
                        dmgMultiplier *= 1.5f;
                    }
                }
                __result = dmgMultiplier;
                return false;
            }
        }

        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("GetAttackCollisionResults")]
        private class GetAttackCollisionResultsPatch
        {
            private static void Postfix(Agent attackerAgent, Agent victimAgent, ref AttackCollisionData attackCollisionData, out CombatLogData combatLog, ref CombatLogData __result)
            {
                if (attackerAgent != null && attackCollisionData.StrikeType == (int)StrikeType.Swing && !attackCollisionData.AttackBlockedWithShield && !attackerAgent.WieldedWeapon.IsEmpty && !Utilities.HitWithWeaponBlade(in attackCollisionData, attackerAgent.WieldedWeapon))
                {
                    string typeOfHandle = "Handle";
                    if (attackerAgent.WieldedWeapon.CurrentUsageItem != null &&
                        (attackerAgent.WieldedWeapon.CurrentUsageItem.WeaponClass == WeaponClass.Dagger ||
                        attackerAgent.WieldedWeapon.CurrentUsageItem.WeaponClass == WeaponClass.OneHandedSword ||
                        attackerAgent.WieldedWeapon.CurrentUsageItem.WeaponClass == WeaponClass.TwoHandedSword))
                    {
                        typeOfHandle = "Pommel";
                    }
                    if (attackerAgent != null && attackerAgent.IsPlayerControlled)
                    {
                        InformationManager.DisplayMessage(new InformationMessage(typeOfHandle + " hit", Color.FromUint(4289612505u)));
                    }
                    if (victimAgent != null && victimAgent.IsPlayerControlled)
                    {
                        InformationManager.DisplayMessage(new InformationMessage(typeOfHandle + " hit", Color.FromUint(4289612505u)));
                    }
                    __result.DamageType = DamageTypes.Blunt;
                }
                combatLog = __result;
            }
        }

        [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
        [HarmonyPatch("ComputeBlowDamage")]
        public class OverrideDamageCalc
        {
            private static bool Prefix(ref AttackInformation attackInformation, ref AttackCollisionData attackCollisionData, WeaponComponentData attackerWeapon, ref DamageTypes damageType, float magnitude, int speedBonus, bool cancelDamage, out int inflictedDamage, out int absorbedByArmor)
            {
                float armorAmountFloat = attackInformation.ArmorAmountFloat;
                if (!attackCollisionData.IsMissile)
                {
                    float wdm = MissionGameModels.Current.AgentStatCalculateModel.GetWeaponDamageMultiplier(attackInformation.AttackerAgent, attackerWeapon);
                    magnitude = attackCollisionData.BaseMagnitude / wdm;
                }
                WeaponComponentData shieldOnBack = attackInformation.ShieldOnBack;
                AgentFlag victimAgentFlag = attackInformation.VictimAgentFlag;
                float victimAgentAbsorbedDamageRatio = attackInformation.VictimAgentAbsorbedDamageRatio;
                float damageMultiplierOfBone = attackInformation.DamageMultiplierOfBone;
                float combatDifficultyMultiplier = attackInformation.CombatDifficultyMultiplier;
                _ = attackCollisionData.CollisionGlobalPosition;
                bool attackBlockedWithShield = attackCollisionData.AttackBlockedWithShield;
                bool collidedWithShieldOnBack = attackCollisionData.CollidedWithShieldOnBack;
                bool isFallDamage = attackCollisionData.IsFallDamage;
                BasicCharacterObject attackerAgentCharacter = attackInformation.AttackerAgentCharacter;
                BasicCharacterObject attackerCaptainCharacter = attackInformation.AttackerCaptainCharacter;
                BasicCharacterObject victimAgentCharacter = attackInformation.VictimAgentCharacter;
                BasicCharacterObject victimCaptainCharacter = attackInformation.VictimCaptainCharacter;

                float armorAmount = 0f;

                if (!isFallDamage)
                {
                    float adjustedArmor = MissionGameModels.Current.StrikeMagnitudeModel.CalculateAdjustedArmorForBlow(armorAmountFloat, attackerAgentCharacter, attackerCaptainCharacter, victimAgentCharacter, victimCaptainCharacter, attackerWeapon);
                    armorAmount = adjustedArmor;
                }
                //float num2 = (float)armorAmount;
                //if (collidedWithShieldOnBack && shieldOnBack != null)
                //{
                //    armorAmount += 20f;
                //}

                Agent attacker = null;
                Agent victim = null;
                foreach (Agent agent in Mission.Current.Agents)
                {
                    if (attackInformation.VictimAgentOrigin == agent.Origin)
                    {
                        victim = agent;
                    }
                    if (attackInformation.AttackerAgentOrigin == agent.Origin)
                    {
                        attacker = agent;
                    }
                }
                //if(!attacker.WieldedWeapon.IsEmpty && attackCollisionData.StrikeType == (int)StrikeType.Swing && attacker.WieldedWeapon.GetModifiedItemName().Contains("Falx") && Utilities.HitWithWeaponBladeTip(in attackCollisionData, attacker.WieldedWeapon))
                //{
                //    damageType = DamageTypes.Pierce;
                //    //InformationManager.DisplayMessage(new InformationMessage("Falx pierce hit!", Color.FromUint(4289612505u)));
                //}
                bool isBash = false;
                if (attacker != null && attackCollisionData.StrikeType == (int)StrikeType.Swing && damageType != DamageTypes.Blunt && !attacker.WieldedWeapon.IsEmpty && !Utilities.HitWithWeaponBlade(in attackCollisionData, attacker.WieldedWeapon))
                {
                    isBash = true;
                    damageType = DamageTypes.Blunt;
                }

                //Agent victim = null;
                //foreach (Agent agent in Mission.Current.Agents)
                //{
                //    if (attackInformation.VictimAgentOrigin == agent.Origin)
                //    {
                //        victim = agent;
                //    }
                //}
                bool faceshot = false;
                //bool backLegHit = false;
                bool lowerShoulderHit = false;

                //if (victim != null && victim.IsHuman && attackCollisionData.VictimHitBodyPart == BoneBodyPartType.Legs)
                //{
                //    //lower leg 2,3,5,6
                //    float dotProduct = Vec3.DotProduct(attackCollisionData.WeaponBlowDir, victim.LookFrame.rotation.f);
                //    float dotProductTrehsold = 0.8f;
                //    InformationManager.DisplayMessage(new InformationMessage(""+ dotProduct+ " " + attackCollisionData.CollisionBoneIndex, Color.FromUint(4289612505u)));
                //    if (dotProduct > dotProductTrehsold)
                //    //if (dotProduct < -0.85f && dotProduct2 < -0.75f)
                //    {
                //        if (attacker != null && attacker.IsPlayerControlled)
                //        {
                //            InformationManager.DisplayMessage(new InformationMessage("Back leg hit!", Color.FromUint(4289612505u)));
                //        }
                //        if (victim != null && victim.IsPlayerControlled)
                //        {
                //            InformationManager.DisplayMessage(new InformationMessage("Back leg hit!", Color.FromUint(4289612505u)));
                //        }
                //        backLegHit = true;
                //    }
                //}

                if (victim != null && victim.IsHuman && attackCollisionData.VictimHitBodyPart == BoneBodyPartType.Head)
                {
                    float dotProduct = Vec3.DotProduct(attackCollisionData.WeaponBlowDir, victim.LookFrame.rotation.f);
                    float dotProductTrehsold = -0.7f;
                    if (attackCollisionData.StrikeType == (int)StrikeType.Swing)
                    {
                        dotProductTrehsold = -0.8f;
                    }
                    if (dotProduct < dotProductTrehsold && attackCollisionData.CollisionGlobalNormal.z < 0f)
                    //if (dotProduct < -0.85f && dotProduct2 < -0.75f)
                    {
                        if (attacker != null && attacker.IsPlayerControlled)
                        {
                            InformationManager.DisplayMessage(new InformationMessage("Face hit!", Color.FromUint(4289612505u)));
                        }
                        if (victim != null && victim.IsPlayerControlled)
                        {
                            InformationManager.DisplayMessage(new InformationMessage("Face hit!", Color.FromUint(4289612505u)));
                        }
                        faceshot = true;
                    }
                }

                //if (victim != null && victim.IsHuman && (attackCollisionData.VictimHitBodyPart == BoneBodyPartType.Chest))
                //{
                //    float dotProduct = Vec3.DotProduct(attackCollisionData.WeaponBlowDir, victim.LookFrame.rotation.f);
                //    if (attacker != null && attacker.IsPlayerControlled)
                //    {
                //        InformationManager.DisplayMessage(new InformationMessage(attackCollisionData.CollisionBoneIndex + " " + dotProduct + " " + attackCollisionData.CollisionGlobalNormal, Color.FromUint(4289612505u)));
                //    }
                //    if ((attackCollisionData.CollisionGlobalNormal.z > 0f) &&
                //        (dotProduct > -0.2f && dotProduct < 0.2f))
                //    {
                //        if (attacker != null && attacker.IsPlayerControlled)
                //        {
                //            InformationManager.DisplayMessage(new InformationMessage("Chest weak point hit! " + attackCollisionData.CollisionBoneIndex, Color.FromUint(4289612505u)));
                //        }
                //        if (victim != null && victim.IsPlayerControlled)
                //        {
                //            InformationManager.DisplayMessage(new InformationMessage("Chest weak point hit! " + attackCollisionData.CollisionBoneIndex, Color.FromUint(4289612505u)));
                //        }
                //        underArmHit = true;
                //    }
                //}

                //if (victim != null && victim.IsHuman && (attackCollisionData.VictimHitBodyPart == BoneBodyPartType.ShoulderLeft || attackCollisionData.VictimHitBodyPart == BoneBodyPartType.ShoulderRight))
                //{
                //    //shoulder bones 14,15,21,22
                //    float dotProduct = Vec3.DotProduct(attackCollisionData.WeaponBlowDir, victim.LookFrame.rotation.f);

                //    if (attacker != null && attacker.IsPlayerControlled)
                //    {
                //        InformationManager.DisplayMessage(new InformationMessage(attackCollisionData.CollisionBoneIndex + " " + dotProduct + " " + attackCollisionData.CollisionGlobalNormal, Color.FromUint(4289612505u)));
                //    }
                //    if ((attackCollisionData.CollisionGlobalNormal.y < -0.7f || attackCollisionData.CollisionGlobalNormal.y > 0.7f ) &&
                //        (dotProduct < -0.7f || dotProduct > 0.7f) &&
                //        (attackCollisionData.CollisionBoneIndex == 15 || attackCollisionData.CollisionBoneIndex == 22))
                //    {
                //        if (attacker != null && attacker.IsPlayerControlled)
                //        {
                //            InformationManager.DisplayMessage(new InformationMessage("Shoulder weak point hit! " + attackCollisionData.CollisionBoneIndex, Color.FromUint(4289612505u)));
                //        }
                //        if (victim != null && victim.IsPlayerControlled)
                //        {
                //            InformationManager.DisplayMessage(new InformationMessage("Shoulder weak point hit! " + attackCollisionData.CollisionBoneIndex, Color.FromUint(4289612505u)));
                //        }
                //        underArmHit = true;
                //    }
                //}

                if (victim != null && victim.IsHuman && (attackCollisionData.VictimHitBodyPart == BoneBodyPartType.ShoulderLeft || attackCollisionData.VictimHitBodyPart == BoneBodyPartType.ShoulderRight))
                {
                    if ((attackCollisionData.CollisionBoneIndex == 15 || attackCollisionData.CollisionBoneIndex == 22) && attackCollisionData.CollisionGlobalNormal.z < 0.15f)
                    {
                        if (attacker != null && attacker.IsPlayerControlled)
                        {
                            InformationManager.DisplayMessage(new InformationMessage("Under shoulder hit!", Color.FromUint(4289612505u)));
                        }
                        if (victim != null && victim.IsPlayerControlled)
                        {
                            InformationManager.DisplayMessage(new InformationMessage("Under shoulder hit!", Color.FromUint(4289612505u)));
                        }
                        lowerShoulderHit = true;
                    }
                }

                if (faceshot)
                {
                    if (!victim.SpawnEquipment[EquipmentIndex.Head].IsEmpty)
                    {
                        armorAmount = victim.SpawnEquipment[EquipmentIndex.Head].GetModifiedBodyArmor();
                        //armorAmount = Game.Current.BasicModels.StrikeMagnitudeModel.CalculateAdjustedArmorForBlow(armorAmountFloat, attackerAgentCharacter, attackerCaptainCharacter, victimAgentCharacter, victimCaptainCharacter, attackerWeapon);
                    }
                    else
                    {
                        armorAmount *= 0.15f;
                    }
                }

                if (lowerShoulderHit)
                {
                    armorAmount = 0f;
                    if (!victim.SpawnEquipment[EquipmentIndex.Body].IsEmpty)
                    {
                        armorAmount = (victim.SpawnEquipment[EquipmentIndex.Body].GetModifiedArmArmor());
                    }
                    if (!victim.SpawnEquipment[EquipmentIndex.Cape].IsEmpty)
                    {
                        armorAmount += victim.SpawnEquipment[EquipmentIndex.Cape].GetModifiedArmArmor();
                    }
                }

                if (collidedWithShieldOnBack && shieldOnBack != null && victim != null)
                {
                    for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                    {
                        if (victim.Equipment != null && !victim.Equipment[equipmentIndex].IsEmpty)
                        {
                            if (victim.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Shield)
                            {
                                attackInformation.VictimShield = victim.Equipment[equipmentIndex];
                                RBMComputeBlowDamageOnShield(ref attackInformation, ref attackCollisionData, attackerWeapon, magnitude, out inflictedDamage);
                                absorbedByArmor = 0;
                                return false;
                            }
                        }
                    }
                }

                if (attacker != null && victim != null)
                {
                    if (victim.GetCurrentActionType(0) == ActionCodeType.Fall || victim.GetCurrentActionType(0) == ActionCodeType.StrikeKnockBack)
                    {
                        armorAmount *= 0.75f;
                    }
                }

                string weaponType = "otherDamage";
                if (attackerWeapon != null)
                {
                    weaponType = attackerWeapon.WeaponClass.ToString();
                }

                IAgentOriginBase attackerAgentOrigin = attackInformation.AttackerAgentOrigin;
                Formation attackerFormation = attackInformation.AttackerFormation;
                ItemModifier itemModifier = null;
                if (!attackCollisionData.IsAlternativeAttack && !attackInformation.IsAttackerAgentMount && !attackCollisionData.IsFallDamage && attackerAgentOrigin != null && attackInformation.AttackerAgentCharacter != null && !attackCollisionData.IsMissile)
                {
                    SkillObject skill = (attackerWeapon == null) ? DefaultSkills.Athletics : attackerWeapon.RelevantSkill;
                    if (skill != null)
                    {
                        int ef = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attackInformation.AttackerAgent, skill);
                        float effectiveSkill = Utilities.GetEffectiveSkillWithDR(ef);
                        float skillModifier = Utilities.CalculateSkillModifier(ef);
                        if (attacker != null && attacker.Equipment != null && attacker.GetWieldedItemIndex(HandIndex.MainHand) != EquipmentIndex.None)
                        {
                            itemModifier = attacker.Equipment[attacker.GetWieldedItemIndex(HandIndex.MainHand)].ItemModifier;
                            magnitude = Utilities.GetSkillBasedDamage(magnitude, attackInformation.IsAttackerAgentDoingPassiveAttack, weaponType, damageType, effectiveSkill, skillModifier, (StrikeType)attackCollisionData.StrikeType, attacker.Equipment[attacker.GetWieldedItemIndex(HandIndex.MainHand)].GetWeight());
                        }
                        else
                        {
                            //float lel = 0;
                        }
                    }
                }

                float dmgMultiplier = 1f;

                if (!attackBlockedWithShield && !isFallDamage)
                {
                    switch (attackCollisionData.VictimHitBodyPart)
                    {
                        case BoneBodyPartType.Abdomen:
                            {
                                switch (damageType)
                                {
                                    case DamageTypes.Pierce:
                                        {
                                            dmgMultiplier *= 1f;
                                            break;
                                        }
                                    case DamageTypes.Cut:
                                        {
                                            dmgMultiplier *= 1f;
                                            break;
                                        }
                                    case DamageTypes.Blunt:
                                        {
                                            dmgMultiplier *= 1f;
                                            break;
                                        }
                                    default:
                                        {
                                            dmgMultiplier *= 0.7f;
                                            break;
                                        }
                                }
                                break;
                            }
                        case BoneBodyPartType.Chest:
                            {
                                switch (damageType)
                                {
                                    case DamageTypes.Pierce:
                                        {
                                            dmgMultiplier *= 0.9f;
                                            break;
                                        }
                                    case DamageTypes.Cut:
                                        {
                                            dmgMultiplier *= 0.9f;
                                            break;
                                        }
                                    case DamageTypes.Blunt:
                                        {
                                            dmgMultiplier *= 0.9f;
                                            break;
                                        }
                                    default:
                                        {
                                            dmgMultiplier *= 1f;
                                            break;
                                        }
                                }
                                break;
                            }
                        case BoneBodyPartType.ShoulderLeft:
                        case BoneBodyPartType.ShoulderRight:
                            {
                                switch (damageType)
                                {
                                    case DamageTypes.Pierce:
                                        {
                                            dmgMultiplier *= 0.6f;
                                            break;
                                        }
                                    case DamageTypes.Cut:
                                        {
                                            dmgMultiplier *= 0.6f;
                                            break;
                                        }
                                    case DamageTypes.Blunt:
                                        {
                                            dmgMultiplier *= 0.7f;
                                            break;
                                        }
                                    default:
                                        {
                                            dmgMultiplier *= 1f;
                                            break;
                                        }
                                }
                                break;
                            }
                        case BoneBodyPartType.ArmLeft:
                        case BoneBodyPartType.ArmRight:
                            {
                                switch (damageType)
                                {
                                    case DamageTypes.Pierce:
                                        {
                                            dmgMultiplier *= 0.5f;
                                            break;
                                        }
                                    case DamageTypes.Cut:
                                        {
                                            dmgMultiplier *= 0.6f;
                                            break;
                                        }
                                    case DamageTypes.Blunt:
                                        {
                                            dmgMultiplier *= 0.7f;
                                            break;
                                        }
                                    default:
                                        {
                                            dmgMultiplier *= 1f;
                                            break;
                                        }
                                }
                                break;
                            }
                        case BoneBodyPartType.Legs:
                            {
                                switch (damageType)
                                {
                                    case DamageTypes.Pierce:
                                        {
                                            dmgMultiplier *= 0.5f;
                                            break;
                                        }
                                    case DamageTypes.Cut:
                                        {
                                            dmgMultiplier *= 0.6f;
                                            break;
                                        }
                                    case DamageTypes.Blunt:
                                        {
                                            dmgMultiplier *= 0.7f;
                                            break;
                                        }
                                    default:
                                        {
                                            dmgMultiplier *= 1f;
                                            break;
                                        }
                                }
                                break;
                            }
                        case BoneBodyPartType.Head:
                            {
                                switch (damageType)
                                {
                                    case DamageTypes.Pierce:
                                        {
                                            dmgMultiplier *= 1.5f;
                                            break;
                                        }
                                    case DamageTypes.Cut:
                                        {
                                            dmgMultiplier *= 1.5f;
                                            break;
                                        }
                                    case DamageTypes.Blunt:
                                        {
                                            dmgMultiplier *= 1.5f;
                                            break;
                                        }
                                    default:
                                        {
                                            dmgMultiplier *= 1f;
                                            break;
                                        }
                                }
                                break;
                            }
                        case BoneBodyPartType.Neck:
                            {
                                switch (damageType)
                                {
                                    case DamageTypes.Pierce:
                                        {
                                            dmgMultiplier *= 1.5f;
                                            break;
                                        }
                                    case DamageTypes.Cut:
                                        {
                                            dmgMultiplier *= 1.5f;
                                            break;
                                        }
                                    case DamageTypes.Blunt:
                                        {
                                            dmgMultiplier *= 1.5f;
                                            break;
                                        }
                                    default:
                                        {
                                            dmgMultiplier *= 1f;
                                            break;
                                        }
                                }
                                break;
                            }
                        default:
                            {
                                dmgMultiplier *= 1f;
                                break;
                            }
                    }

                    dmgMultiplier *= combatDifficultyMultiplier;
                }

                BasicCharacterObject player = null;
                bool isPlayerVictim = false;
                if (attackerAgentCharacter != null && attackInformation.IsAttackerPlayer)
                {
                    player = attackerAgentCharacter;
                    isPlayerVictim = false;
                }
                else if (victimAgentCharacter != null && attackInformation.IsVictimPlayer)
                {
                    player = victimAgentCharacter;
                    isPlayerVictim = true;
                }

                float weaponDamageFactor = 1f;
                if (attackerWeapon != null)
                {
                    weaponDamageFactor = (float)Math.Sqrt((attackCollisionData.StrikeType == (int)StrikeType.Thrust)
                        ? Utilities.getThrustDamageFactor(attackerWeapon, itemModifier)
                        : Utilities.getSwingDamageFactor(attackerWeapon, itemModifier));
                }
                inflictedDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(weaponType, damageType, magnitude, armorAmount, victimAgentAbsorbedDamageRatio, out _, out _, weaponDamageFactor, player, isPlayerVictim)), 0, 2000);
                inflictedDamage = MathF.Floor(inflictedDamage * dmgMultiplier);

                //float dmgWithPerksSkills = MissionGameModels.Current.AgentApplyDamageModel.CalculateDamage(ref attackInformation, ref attackCollisionData, in attackerWeapon, inflictedDamage, out float bonusFromSkills);

                //InformationManager.DisplayMessage(new InformationMessage("dmgWithPerksSkills: " + dmgWithPerksSkills + " inflictedDamage: " + inflictedDamage +
                //    " HP: " + attackInformation.VictimAgentHealth));

                //if (victim != null)
                //{
                //    Utilities.initiateCheckForArmor(ref victim, ref attackInformation, ref attackCollisionData);
                //    Utilities.numOfHits++;
                //}

                int absoluteDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(weaponType, damageType, magnitude, 0f, victimAgentAbsorbedDamageRatio, out _, out _, weaponDamageFactor) * dmgMultiplier), 0, 2000);
                absorbedByArmor = absoluteDamage - inflictedDamage;

                return false;
            }

            public static void RBMComputeBlowDamageOnShield(ref AttackInformation attackInformation, ref AttackCollisionData attackCollisionData, WeaponComponentData attackerWeapon, float blowMagnitude, out int inflictedDamage)
            {
                float localInflictedDamage = 0;
                attackCollisionData.InflictedDamage = 0;

                Agent victim = null;
                Agent attacker = null;
                foreach (Agent agent in Mission.Current.Agents)
                {
                    if (attackInformation.VictimAgentOrigin == agent.Origin)
                    {
                        victim = agent;
                    }
                    if (attackInformation.AttackerAgentOrigin == agent.Origin)
                    {
                        attacker = agent;
                    }
                }

                MissionWeapon victimShield = attackInformation.VictimShield;
                if (victimShield.IsEmpty)
                {
                    for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                    {
                        if (victim != null && victim.Equipment != null && !victim.Equipment[equipmentIndex].IsEmpty)
                        {
                            if (victim.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Shield)
                            {
                                attackInformation.VictimShield = victim.Equipment[equipmentIndex];
                            }
                        }
                    }
                }

                if (victim != null && attacker != null)
                {
                    MethodInfo method = typeof(Agent).GetMethod("UpdateLastAttackAndHitTimes", BindingFlags.NonPublic | BindingFlags.Instance);
                    method.DeclaringType.GetMethod("UpdateLastAttackAndHitTimes");
                    method.Invoke(victim, new object[] { attacker, attackCollisionData.IsMissile });
                }

                victimShield = attackInformation.VictimShield;
                if (!victimShield.IsEmpty && victimShield.CurrentUsageItem.WeaponFlags.HasAnyFlag(WeaponFlags.CanBlockRanged) & attackInformation.CanGiveDamageToAgentShield)
                {
                    DamageTypes damageType = (DamageTypes)attackCollisionData.DamageType;
                    int shieldArmorForCurrentUsage = victimShield.GetGetModifiedArmorForCurrentUsage();
                    float absorbedDamageRatio = 1f;

                    string weaponType = "otherDamage";
                    if (attackerWeapon != null)
                    {
                        weaponType = attackerWeapon.WeaponClass.ToString();
                    }

                    bool isPassiveUsage = attackInformation.IsAttackerAgentDoingPassiveAttack;

                    float skillBasedDamage = 0f;
                    const float ashBreakTreshold = 430f;
                    float BraceBonus = 0f;
                    float BraceModifier = 0.34f;

                    switch (weaponType)
                    {
                        case "Dagger":
                        case "OneHandedSword":
                        case "ThrowingKnife":
                            {
                                if (blowMagnitude > 1f)
                                {
                                    if (damageType == DamageTypes.Cut)
                                    {
                                        blowMagnitude = blowMagnitude + 40f;
                                    }
                                    else
                                    {
                                        blowMagnitude = blowMagnitude * 0.2f + 50f * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                    }
                                }
                                break;
                            }
                        case "TwoHandedSword":
                            {
                                if (blowMagnitude > 1f)
                                {
                                    if (damageType == DamageTypes.Cut)
                                    {
                                        blowMagnitude = blowMagnitude + 40f * 1.3f;
                                    }
                                    else
                                    {
                                        blowMagnitude = (blowMagnitude * 0.2f + 50f * RBMConfig.RBMConfig.ThrustMagnitudeModifier) * 1.3f;
                                    }
                                }
                                break;
                            }
                        case "OneHandedAxe":
                        case "ThrowingAxe":
                            {
                                if (blowMagnitude > 1f)
                                {
                                    blowMagnitude = blowMagnitude + 60f;
                                }
                                break;
                            }
                        case "OneHandedBastardAxe":
                            {
                                if (blowMagnitude > 1f)
                                {
                                    blowMagnitude = blowMagnitude + 60f * 1.15f;
                                }
                                break;
                            }
                        case "TwoHandedAxe":
                            {
                                if (blowMagnitude > 1f)
                                {
                                    blowMagnitude = blowMagnitude + 60f * 1.3f;
                                }
                                break;
                            }
                        case "Mace":
                            {
                                if (blowMagnitude > 1f)
                                {
                                    if (damageType == DamageTypes.Pierce)
                                    {
                                        blowMagnitude = blowMagnitude * 0.2f + 40f * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                    }
                                    else
                                    {
                                        blowMagnitude = blowMagnitude + 30f;
                                    }
                                }
                                break;
                            }
                        case "TwoHandedMace":
                            {
                                if (blowMagnitude > 1f)
                                {
                                    if (damageType == DamageTypes.Pierce)
                                    {
                                        blowMagnitude = (blowMagnitude * 0.2f + 40f * RBMConfig.RBMConfig.ThrustMagnitudeModifier) * 1.3f;
                                    }
                                    else
                                    {
                                        blowMagnitude = blowMagnitude + 45f * 1.3f;
                                    }
                                }
                                break;
                            }
                        case "OneHandedPolearm":
                            {
                                if (blowMagnitude > 1f)
                                {
                                    if (damageType == DamageTypes.Cut)
                                    {
                                        blowMagnitude = blowMagnitude + 50f;
                                    }
                                    else if (damageType == DamageTypes.Blunt)
                                    {
                                        blowMagnitude = blowMagnitude + 30f;
                                    }
                                    else
                                    {
                                        if (isPassiveUsage)
                                        {
                                            float couchedSkill = 2f;
                                            float skillCap = 230f;

                                            float weaponWeight = 1.5f;

                                            if (weaponWeight < 2.1f)
                                            {
                                                BraceBonus += 0.5f;
                                                BraceModifier *= 3f;
                                            }
                                            float lanceBalistics = (blowMagnitude * BraceModifier) / weaponWeight;
                                            float CouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus);
                                            blowMagnitude = CouchedMagnitude;
                                            if (CouchedMagnitude > (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier) && (lanceBalistics * (weaponWeight + BraceBonus)) < (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) //skill based damage
                                            {
                                                blowMagnitude = skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                            }

                                            if ((lanceBalistics * (weaponWeight + BraceBonus)) >= (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) //ballistics
                                            {
                                                blowMagnitude = (lanceBalistics * (weaponWeight + BraceBonus));
                                            }

                                            if (blowMagnitude > (ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) // damage cap - lance break threshold
                                            {
                                                blowMagnitude = ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                            }
                                        }
                                        else
                                        {
                                            //float weaponWeight = attackerWeapon.Item.Weight;

                                            //if (weaponWeight > 2.1f)
                                            //{
                                            //    blowMagnitude *= 0.34f;
                                            //}
                                            blowMagnitude = blowMagnitude * 0.4f + 60f * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                            if (blowMagnitude > 260f * RBMConfig.RBMConfig.ThrustMagnitudeModifier)
                                            {
                                                blowMagnitude = 260f * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                            }
                                        }
                                    }
                                }
                                break;
                            }
                        case "TwoHandedPolearm":
                            {
                                if (blowMagnitude > 1f)
                                {
                                    if (damageType == DamageTypes.Cut)
                                    {
                                        blowMagnitude = blowMagnitude + 50f * 1.3f;
                                    }
                                    else if (damageType == DamageTypes.Blunt)
                                    {
                                        blowMagnitude = blowMagnitude + 30f * 1.3f;
                                    }
                                    else
                                    {
                                        if (isPassiveUsage)
                                        {
                                            float couchedSkill = 2f;
                                            float skillCap = 230f;

                                            float weaponWeight = 1.5f;

                                            if (weaponWeight < 2.1f)
                                            {
                                                BraceBonus += 0.5f;
                                                BraceModifier *= 3f;
                                            }
                                            float lanceBalistics = (blowMagnitude * BraceModifier) / weaponWeight;
                                            float CouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus);
                                            blowMagnitude = CouchedMagnitude;
                                            if (CouchedMagnitude > (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier) && (lanceBalistics * (weaponWeight + BraceBonus)) < (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier))
                                            {
                                                blowMagnitude = skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                            }

                                            if ((lanceBalistics * (weaponWeight + BraceBonus)) >= (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier))
                                            {
                                                blowMagnitude = (lanceBalistics * (weaponWeight + BraceBonus));
                                            }

                                            if (blowMagnitude > (ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier))
                                            {
                                                blowMagnitude = ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                            }
                                        }
                                        else
                                        {
                                            float weaponWeight = 1.5f;

                                            if (weaponWeight > 2.1f)
                                            {
                                                blowMagnitude *= 0.34f;
                                            }
                                            skillBasedDamage = (blowMagnitude * 0.4f + 60f * RBMConfig.RBMConfig.ThrustMagnitudeModifier) * 1.3f;
                                            if (skillBasedDamage > 360f * RBMConfig.RBMConfig.ThrustMagnitudeModifier)
                                            {
                                                skillBasedDamage = 360f * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                            }
                                        }
                                    }
                                }
                                break;
                            }
                    }

                    localInflictedDamage = Utilities.RBMComputeDamage(weaponType, damageType, blowMagnitude, (float)shieldArmorForCurrentUsage * 10f, absorbedDamageRatio, out _, out _);

                    if (attackCollisionData.IsMissile)
                    {
                        switch (weaponType)
                        {
                            case "Arrow":
                                {
                                    localInflictedDamage *= 1.5f;
                                    break;
                                }
                            case "Bolt":
                                {
                                    localInflictedDamage *= 1.5f;
                                    break;
                                }
                            case "Javelin":
                                {
                                    localInflictedDamage *= 20f;
                                    break;
                                }
                            case "ThrowingAxe":
                                {
                                    localInflictedDamage *= 3f;
                                    break;
                                }
                            case "OneHandedPolearm":
                                {
                                    localInflictedDamage *= 5f;
                                    break;
                                }
                            case "LowGripPolearm":
                                {
                                    localInflictedDamage *= 5f;
                                    break;
                                }
                            default:
                                {
                                    localInflictedDamage *= 0.1f;
                                    break;
                                }
                        }
                    }
                    else if (!attackCollisionData.IsMissile)
                    {
                        switch (weaponType)
                        {
                            case "OneHandedAxe":
                            case "TwoHandedAxe":
                            case "OneHandedBastardAxe":
                            case "TwoHandedPolearm":
                                {
                                    if (attackCollisionData.DamageType == 1)//pierce
                                    {
                                        localInflictedDamage *= 0.09f;
                                        break;
                                    }
                                    localInflictedDamage *= 1.5f;

                                    break;
                                }
                            default:
                                {
                                    if (attackCollisionData.DamageType == 0) //cut
                                    {
                                        localInflictedDamage *= 1f;
                                    }
                                    else if (attackCollisionData.DamageType == 1)//pierce
                                    {
                                        localInflictedDamage *= 0.09f;
                                    }
                                    else if (attackCollisionData.DamageType == 2)//blunt
                                    {
                                        localInflictedDamage *= 0.75f;
                                    }
                                    break;
                                }
                        }
                    }

                    if (attackerWeapon != null && attackerWeapon.WeaponFlags.HasAnyFlag(WeaponFlags.BonusAgainstShield))
                    {
                        localInflictedDamage *= 2f;
                    }

                    if (localInflictedDamage > 0f)
                    {
                        if (!attackInformation.IsVictimAgentLeftStance)
                        {
                            localInflictedDamage *= TaleWorlds.Core.ManagedParameters.Instance.GetManagedParameter(TaleWorlds.Core.ManagedParametersEnum.ShieldRightStanceBlockDamageMultiplier);
                        }
                        if (attackCollisionData.CorrectSideShieldBlock)
                        {
                            localInflictedDamage *= TaleWorlds.Core.ManagedParameters.Instance.GetManagedParameter(TaleWorlds.Core.ManagedParametersEnum.ShieldCorrectSideBlockDamageMultiplier);
                        }
                        localInflictedDamage = MissionGameModels.Current.AgentApplyDamageModel.CalculateShieldDamage(attackInformation, localInflictedDamage);
                    }
                }
                attackCollisionData.InflictedDamage = (int)localInflictedDamage;
                inflictedDamage = MathF.Floor(localInflictedDamage);
            }

            [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
            [HarmonyPatch("ComputeBlowDamageOnShield")]
            private class OverrideDamageCalcShield
            {
                private static bool Prefix(ref AttackInformation attackInformation, ref AttackCollisionData attackCollisionData, WeaponComponentData attackerWeapon, float blowMagnitude, out int inflictedDamage)
                {
                    RBMComputeBlowDamageOnShield(ref attackInformation, ref attackCollisionData, attackerWeapon, blowMagnitude, out inflictedDamage);
                    return false;
                }
            }
        }

        [HarmonyPatch(typeof(SandboxAgentStatCalculateModel))]
        [HarmonyPatch("UpdateHumanStats")]
        private class SandboxAgentUpdateHumanStats
        {
            private static void Postfix(Agent agent, ref AgentDrivenProperties agentDrivenProperties)
            {
                agentDrivenProperties.AttributeShieldMissileCollisionBodySizeAdder = 0.01f;
            }
        }

        //[HarmonyPatch(typeof(SandboxAgentApplyDamageModel))]
        //[HarmonyPatch("DecideCrushedThrough")]
        //class OverrideDecideCrushedThrough
        //{
        //    static bool Prefix(Agent attackerAgent, Agent defenderAgent, float totalAttackEnergy, Agent.UsageDirection attackDirection, StrikeType strikeType, WeaponComponentData defendItem, bool isPassiveUsage, ref bool __result)
        //    {
        //        EquipmentIndex wieldedItemIndex = attackerAgent.GetWieldedItemIndex(Agent.HandIndex.OffHand);
        //        if (wieldedItemIndex == EquipmentIndex.None)
        //        {
        //            wieldedItemIndex = attackerAgent.GetWieldedItemIndex(Agent.HandIndex.MainHand);
        //        }
        //        WeaponComponentData weaponComponentData = (wieldedItemIndex != EquipmentIndex.None) ? attackerAgent.Equipment[wieldedItemIndex].CurrentUsageItem : null;
        //        float num = 47f;

        //        EquipmentIndex wieldedItemIndex4 = defenderAgent.GetWieldedItemIndex(Agent.HandIndex.OffHand);
        //        WeaponComponentData secondaryItem = (wieldedItemIndex4 != EquipmentIndex.None) ? defenderAgent.Equipment[wieldedItemIndex4].CurrentUsageItem : null;
        //        int meleeSkill = Utilities.GetMeleeSkill(defenderAgent, weaponComponentData, secondaryItem);
        //        float meleeLevel = Utilities.CalculateAILevel(defenderAgent, meleeSkill);

        //        if (defendItem != null && defendItem.IsShield)
        //        {
        //            num *= (defendItem.WeaponLength / 100f) * meleeLevel * 3f;
        //        }
        //        else
        //        {
        //            num *= (weaponComponentData.WeaponLength / 100f) * meleeLevel * 2f;
        //        }
        //        if (weaponComponentData != null && weaponComponentData.WeaponClass == WeaponClass.TwoHandedMace)
        //        {
        //            num *= 0.8f;
        //        }
        //        if (defendItem != null && defendItem.IsShield)
        //        {
        //            num *= 1.2f;
        //        }
        //        if (totalAttackEnergy > num && (isPassiveUsage || attackDirection == Agent.UsageDirection.AttackUp || attackDirection == Agent.UsageDirection.AttackLeft || attackDirection == Agent.UsageDirection.AttackRight))
        //        {
        //            __result = true;
        //            return false;
        //        }
        //        __result =  false;
        //        return false;
        //    }

        //}

        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("CreateMeleeBlow")]
        private class CreateMeleeBlowPatch
        {
            private static void Postfix(ref Mission __instance, ref Blow __result, Agent attackerAgent, Agent victimAgent, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, CrushThroughState crushThroughState, Vec3 blowDirection, Vec3 swingDirection, bool cancelDamage)
            {
                string weaponType = "otherDamage";
                if (attackerWeapon.Item != null && attackerWeapon.CurrentUsageItem != null)
                {
                    weaponType = attackerWeapon.CurrentUsageItem.WeaponClass.ToString();
                }

                if ((attackerAgent.IsDoingPassiveAttack && collisionData.CollisionResult == CombatCollisionResult.StrikeAgent))
                {
                    if (attackerAgent.Team != victimAgent.Team)
                    {
                        __result.BlowFlag |= BlowFlags.KnockDown;
                        return;
                    }
                }

                if ((collisionData.CollisionResult == CombatCollisionResult.StrikeAgent) && (collisionData.DamageType == (int)DamageTypes.Pierce))
                {
                    if (attackerAgent.Team != victimAgent.Team)
                    {
                        switch (weaponType)
                        {
                            case "TwoHandedPolearm":
                                if (attackerAgent.Team != victimAgent.Team)
                                {
                                    //AttackCollisionData newdata = AttackCollisionData.GetAttackCollisionDataForDebugPurpose(false, false, false, true, false, false, false, false, false, true, false, collisionData.CollisionResult, collisionData.AffectorWeaponSlotOrMissileIndex,
                                    //    collisionData.StrikeType, collisionData.StrikeType, collisionData.CollisionBoneIndex, BoneBodyPartType.Chest, collisionData.AttackBoneIndex, collisionData.AttackDirection, collisionData.PhysicsMaterialIndex,
                                    //    collisionData.CollisionHitResultFlags, collisionData.AttackProgress, collisionData.CollisionDistanceOnWeapon, collisionData.AttackerStunPeriod, collisionData.DefenderStunPeriod, collisionData.MissileTotalDamage,
                                    //    0, collisionData.ChargeVelocity, collisionData.FallSpeed, collisionData.WeaponRotUp, collisionData.WeaponBlowDir, collisionData.CollisionGlobalPosition, collisionData.MissileVelocity,
                                    //    collisionData.MissileStartingPosition, collisionData.VictimAgentCurVelocity, collisionData.CollisionGlobalNormal);

                                    //collisionData = newdata;

                                    __result.BlowFlag |= BlowFlags.KnockBack;
                                }
                                break;
                        }
                    }
                }

                if ((attackerAgent.IsDoingPassiveAttack && collisionData.CollisionResult == CombatCollisionResult.Blocked))
                {
                    if (attackerAgent.Team != victimAgent.Team)
                    {
                        //AttackCollisionData newdata = AttackCollisionData.GetAttackCollisionDataForDebugPurpose(false, false, false, true, false, false, false, false, false, true, false, collisionData.CollisionResult, collisionData.AffectorWeaponSlotOrMissileIndex,
                        //    collisionData.StrikeType, collisionData.StrikeType, collisionData.CollisionBoneIndex, BoneBodyPartType.Chest, collisionData.AttackBoneIndex, collisionData.AttackDirection, collisionData.PhysicsMaterialIndex,
                        //    collisionData.CollisionHitResultFlags, collisionData.AttackProgress, collisionData.CollisionDistanceOnWeapon, collisionData.AttackerStunPeriod, collisionData.DefenderStunPeriod, collisionData.MissileTotalDamage,
                        //    0, collisionData.ChargeVelocity, collisionData.FallSpeed, collisionData.WeaponRotUp, collisionData.WeaponBlowDir, collisionData.CollisionGlobalPosition, collisionData.MissileVelocity,
                        //    collisionData.MissileStartingPosition, collisionData.VictimAgentCurVelocity, collisionData.CollisionGlobalNormal);

                        //collisionData = newdata;

                        sbyte weaponAttachBoneIndex = (sbyte)(attackerWeapon.IsEmpty ? (-1) : attackerAgent.Monster.GetBoneToAttachForItemFlags(attackerWeapon.Item.ItemFlags));
                        __result.WeaponRecord.FillAsMeleeBlow(attackerWeapon.Item, attackerWeapon.CurrentUsageItem, collisionData.AffectorWeaponSlotOrMissileIndex, weaponAttachBoneIndex);
                        __result.StrikeType = (StrikeType)collisionData.StrikeType;
                        __result.DamageType = ((!attackerWeapon.IsEmpty && true && !collisionData.IsAlternativeAttack) ? ((DamageTypes)collisionData.DamageType) : DamageTypes.Blunt);
                        __result.NoIgnore = collisionData.IsAlternativeAttack;
                        __result.AttackerStunPeriod = collisionData.AttackerStunPeriod;
                        __result.DefenderStunPeriod = collisionData.DefenderStunPeriod;
                        __result.BlowFlag = BlowFlags.None;
                        __result.GlobalPosition = collisionData.CollisionGlobalPosition;
                        __result.BoneIndex = collisionData.CollisionBoneIndex;
                        __result.Direction = blowDirection;
                        __result.SwingDirection = swingDirection;
                        //__result.InflictedDamage = 1;
                        __result.VictimBodyPart = collisionData.VictimHitBodyPart;
                        __result.BlowFlag |= BlowFlags.KnockBack;
                        victimAgent.RegisterBlow(__result, collisionData);
                        foreach (MissionBehavior missionBehaviour in __instance.MissionBehaviors)
                        {
                            missionBehaviour.OnRegisterBlow(attackerAgent, victimAgent, null, __result, ref collisionData, in attackerWeapon);
                        }
                        return;
                    }
                }

                if ((collisionData.CollisionResult == CombatCollisionResult.Parried && !collisionData.AttackBlockedWithShield) || (collisionData.AttackBlockedWithShield && !collisionData.CorrectSideShieldBlock))
                {
                    switch (weaponType)
                    {
                        case "TwoHandedAxe":
                        case "OneHandedAxe":
                        case "OneHandedBastardAxe":
                        case "TwoHandedPolearm":
                        case "TwoHandedMace":
                            {
                                bool hitWithBlade = Utilities.HitWithWeaponBlade(in collisionData, in attackerWeapon);
                                if (attackerAgent.Team != victimAgent.Team && hitWithBlade)
                                {
                                    Blow newBlow = __result;
                                    sbyte weaponAttachBoneIndex = (sbyte)(attackerWeapon.IsEmpty ? (-1) : attackerAgent.Monster.GetBoneToAttachForItemFlags(attackerWeapon.Item.ItemFlags));
                                    newBlow.WeaponRecord.FillAsMeleeBlow(attackerWeapon.Item, attackerWeapon.CurrentUsageItem, collisionData.AffectorWeaponSlotOrMissileIndex, weaponAttachBoneIndex);
                                    newBlow.StrikeType = (StrikeType)collisionData.StrikeType;
                                    newBlow.DamageType = ((!attackerWeapon.IsEmpty && true && !collisionData.IsAlternativeAttack) ? ((DamageTypes)collisionData.DamageType) : DamageTypes.Blunt);
                                    newBlow.NoIgnore = collisionData.IsAlternativeAttack;
                                    newBlow.AttackerStunPeriod = collisionData.AttackerStunPeriod;
                                    newBlow.DefenderStunPeriod = collisionData.DefenderStunPeriod * 0.5f;
                                    newBlow.BlowFlag = BlowFlags.None;
                                    newBlow.GlobalPosition = collisionData.CollisionGlobalPosition;
                                    newBlow.BoneIndex = collisionData.CollisionBoneIndex;
                                    newBlow.Direction = blowDirection;
                                    newBlow.SwingDirection = swingDirection;
                                    newBlow.InflictedDamage = 0;
                                    newBlow.VictimBodyPart = collisionData.VictimHitBodyPart;
                                    newBlow.BlowFlag |= BlowFlags.NonTipThrust;
                                    victimAgent.RegisterBlow(newBlow, collisionData);
                                    foreach (MissionBehavior missionBehaviour in __instance.MissionBehaviors)
                                    {
                                        missionBehaviour.OnRegisterBlow(attackerAgent, victimAgent, null, newBlow, ref collisionData, in attackerWeapon);
                                    }
                                }
                                break;
                            }
                    }
                }

                if (collisionData.CollisionResult != CombatCollisionResult.HitWorld && collisionData.CollisionResult != CombatCollisionResult.None && victimAgent != null && attackerAgent.Team == victimAgent.Team && (__result.BlowFlag.HasAnyFlag(BlowFlags.KnockBack) || __result.BlowFlag.HasAnyFlag(BlowFlags.KnockDown)))
                {
                    __result.BlowFlag = BlowFlags.NonTipThrust;
                    return;
                }
            }
        }

        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("RegisterBlow")]
        private class RegisterBlowPatch
        {
            private static bool Prefix(ref Mission __instance, ref Agent attacker, ref Agent victim, GameEntity realHitEntity, ref Blow b, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, ref CombatLogData combatLogData)
            {
                if (victim != null && victim.IsMount && collisionData.IsMissile)
                {
                    if (MissionGameModels.Current.AgentApplyDamageModel.DecideMountRearedByBlow(attacker, victim, in collisionData, attackerWeapon.CurrentUsageItem, in b))
                    {
                        b.BlowFlag |= BlowFlags.MakesRear;
                    }
                }
                if (attacker != null && attacker.IsMount && collisionData.IsHorseCharge)
                {
                    float horseBodyPartArmor = attacker.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.Chest);
                    //b.SelfInflictedDamage = MathF.Ceiling(b.BaseMagnitude / 6f);
                    b.SelfInflictedDamage = MBMath.ClampInt(MathF.Ceiling(MissionGameModels.Current.StrikeMagnitudeModel.ComputeRawDamage(DamageTypes.Blunt, b.BaseMagnitude / 6f, horseBodyPartArmor, 1f)), 0, 2000);
                    attacker.CreateBlowFromBlowAsReflection(in b, in collisionData, out var outBlow, out var outCollisionData);
                    attacker.RegisterBlow(outBlow, in outCollisionData);
                }
                //if(victim != null && collisionData.CollidedWithShieldOnBack && collisionData.IsMissile)
                //{
                //    for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                //    {
                //        if (victim.Equipment != null && !victim.Equipment[equipmentIndex].IsEmpty)
                //        {
                //            if (victim.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Shield)
                //            {
                //                int num = MathF.Max(0, victim.Equipment[equipmentIndex].HitPoints - b.InflictedDamage);
                //                victim.ChangeWeaponHitPoints(equipmentIndex, (short)num);
                //                if (num == 0)
                //                {
                //                    victim.RemoveEquippedWeapon(equipmentIndex);
                //                }
                //                break;
                //            }
                //        }
                //    }
                //}
                if (!collisionData.AttackBlockedWithShield && !collisionData.CollidedWithShieldOnBack)
                {
                    return true;
                }
                foreach (MissionBehavior missionBehaviour in __instance.MissionBehaviors)
                {
                    missionBehaviour.OnRegisterBlow(attacker, victim, realHitEntity, b, ref collisionData, in attackerWeapon);
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(BattleAgentLogic))]
        [HarmonyPatch("OnAgentHit")]
        private class OnAgentHitPatch
        {
            private static bool Prefix(Agent affectedAgent, Agent affectorAgent, in MissionWeapon attackerWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
            {
                if (affectedAgent != null && blow.InflictedDamage > 1f && affectedAgent.State == AgentState.Active && attackCollisionData.CollisionResult == CombatCollisionResult.StrikeAgent && !blow.IsFallDamage)
                {
                    Utilities.initiateCheckForArmor(ref affectedAgent, attackCollisionData, blow, affectorAgent, attackerWeapon);
                    Utilities.numOfHits++;
                }
                if (affectedAgent.Character != null && affectorAgent != null && affectorAgent.Character != null && affectedAgent.State == AgentState.Active)
                {
                    bool isFatal = affectedAgent.Health - (float)blow.InflictedDamage < 1f;
                    bool isTeamKill;
                    if (affectedAgent.Team != null)
                    {
                        isTeamKill = affectedAgent.Team.Side == affectorAgent.Team.Side;
                    }
                    else
                    {
                        isTeamKill = true;
                    }
                    affectorAgent.Origin.OnScoreHit(affectedAgent.Character, affectorAgent.Formation?.Captain?.Character, blow.InflictedDamage, isFatal, isTeamKill, attackerWeapon.CurrentUsageItem);
                    if (Mission.Current.Mode == MissionMode.Battle)
                    {
                        _ = affectedAgent.Team;
                    }
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(CustomBattleAgentLogic))]
        [HarmonyPatch("OnAgentHit")]
        private class CustomBattleAgentLogicPatch
        {
            private static bool Prefix(Agent affectedAgent, Agent affectorAgent, in MissionWeapon affectorWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
            {
                if (affectedAgent != null && affectedAgent.State == AgentState.Active && blow.InflictedDamage > 1f && attackCollisionData.CollisionResult == CombatCollisionResult.StrikeAgent && !blow.IsFallDamage)
                {
                    Utilities.initiateCheckForArmor(ref affectedAgent, attackCollisionData, blow, affectorAgent, affectorWeapon);
                    Utilities.numOfHits++;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("HandleBlow")]
        private class HandleBlowPatch
        {
            private static ArmorComponent.ArmorMaterialTypes GetProtectorArmorMaterialOfBone(Agent agent, sbyte boneIndex)
            {
                if (agent != null && agent.SpawnEquipment != null)
                {
                    if (boneIndex >= 0)
                    {
                        EquipmentIndex equipmentIndex = EquipmentIndex.None;
                        switch (agent.AgentVisuals.GetBoneTypeData(boneIndex).BodyPartType)
                        {
                            case BoneBodyPartType.Chest:
                            case BoneBodyPartType.Abdomen:
                            case BoneBodyPartType.ShoulderLeft:
                            case BoneBodyPartType.ShoulderRight:
                                equipmentIndex = EquipmentIndex.Body;
                                break;

                            case BoneBodyPartType.ArmLeft:
                            case BoneBodyPartType.ArmRight:
                                equipmentIndex = EquipmentIndex.Gloves;
                                break;

                            case BoneBodyPartType.Legs:
                                equipmentIndex = EquipmentIndex.Leg;
                                break;

                            case BoneBodyPartType.Head:
                            case BoneBodyPartType.Neck:
                                equipmentIndex = EquipmentIndex.NumAllWeaponSlots;
                                break;
                        }
                        if (equipmentIndex != EquipmentIndex.None && agent.SpawnEquipment[equipmentIndex].Item != null)
                        {
                            if (agent.SpawnEquipment[equipmentIndex].Item.ArmorComponent != null)
                            {
                                return agent.SpawnEquipment[equipmentIndex].Item.ArmorComponent.MaterialType;
                            }
                        }
                    }
                }
                return ArmorComponent.ArmorMaterialTypes.None;
            }

            private static bool Prefix(ref Agent __instance, ref Blow b, AgentLastHitInfo ____lastHitInfo, in AttackCollisionData collisionData)
            {
                b.BaseMagnitude = Math.Min(b.BaseMagnitude, 1000f) / 8f;
                Agent agent = (b.OwnerId != -1) ? __instance.Mission.FindAgentWithIndex(b.OwnerId) : __instance;
                if (!b.BlowFlag.HasAnyFlag(BlowFlags.NoSound))
                {
                    bool isCriticalBlow = b.IsBlowCrit(__instance.Monster.HitPoints * 4);
                    bool isLowBlow = b.IsBlowLow(__instance.Monster.HitPoints);
                    bool isOwnerHumanoid = agent?.IsHuman ?? false;
                    bool isNonTipThrust = b.BlowFlag.HasAnyFlag(BlowFlags.NonTipThrust);
                    int hitSound = b.WeaponRecord.GetHitSound(isOwnerHumanoid, isCriticalBlow, isLowBlow, isNonTipThrust, b.AttackType, b.DamageType);
                    float soundParameterForArmorType = 0.1f * (float)GetProtectorArmorMaterialOfBone(__instance, b.BoneIndex);
                    SoundEventParameter parameter = new SoundEventParameter("Armor Type", soundParameterForArmorType);
                    __instance.Mission.MakeSound(hitSound, b.GlobalPosition, soundCanBePredicted: false, isReliable: true, b.OwnerId, __instance.Index, ref parameter);
                    if (b.IsMissile && agent != null)
                    {
                        int soundCodeMissionCombatPlayerhit = CombatSoundContainer.SoundCodeMissionCombatPlayerhit;
                        __instance.Mission.MakeSoundOnlyOnRelatedPeer(soundCodeMissionCombatPlayerhit, b.GlobalPosition, agent.Index);
                    }
                    __instance.Mission.AddSoundAlarmFactorToAgents(b.OwnerId, b.GlobalPosition, 15f);
                }
                b.DamagedPercentage = (float)b.InflictedDamage / __instance.HealthLimit;

                MethodInfo method = typeof(Agent).GetMethod("UpdateLastAttackAndHitTimes", BindingFlags.NonPublic | BindingFlags.Instance);
                method.DeclaringType.GetMethod("UpdateLastAttackAndHitTimes");
                method.Invoke(__instance, new object[] { agent, b.IsMissile });

                bool isKnockBack = ((b.BlowFlag & BlowFlags.NonTipThrust) != 0) || ((b.BlowFlag & BlowFlags.KnockDown) != 0) || ((b.BlowFlag & BlowFlags.KnockBack) != 0);

                float health = __instance.Health;
                float damagedHp = (((float)b.InflictedDamage > health) ? health : ((float)b.InflictedDamage));
                float newHp = health - damagedHp;

                if (b.AttackType == AgentAttackType.Bash || b.AttackType == AgentAttackType.Kick)
                {
                    if (b.InflictedDamage <= 0)
                    {
                        b.InflictedDamage = 1;
                    }
                }
                if (b.InflictedDamage == 0 && isKnockBack)
                {
                    b.InflictedDamage = 1;
                }
                if (b.InflictedDamage == 1 && isKnockBack)
                {
                }
                else
                {
                    if (newHp < 0f)
                    {
                        newHp = 0f;
                    }
                    if (__instance.CurrentMortalityState != MortalityState.Immortal && !Mission.Current.DisableDying)
                    {
                        __instance.Health = newHp;
                    }
                }

                if (agent != null && agent != __instance && __instance.IsHuman)
                {
                    if (agent.IsMount && agent.RiderAgent != null)
                    {
                        ____lastHitInfo.RegisterLastBlow(agent.RiderAgent.Index, b.AttackType);
                    }
                    else if (agent.IsHuman)
                    {
                        ____lastHitInfo.RegisterLastBlow(b.OwnerId, b.AttackType);
                    }
                }

                MethodInfo method3 = typeof(Mission).GetMethod("OnAgentHit", BindingFlags.NonPublic | BindingFlags.Instance);
                method3.DeclaringType.GetMethod("OnAgentHit");
                bool isBlocked = false;
                if (collisionData.CollisionResult == CombatCollisionResult.Blocked || collisionData.CollisionResult == CombatCollisionResult.ChamberBlocked || collisionData.CollisionResult == CombatCollisionResult.Parried)
                {
                    isBlocked = true;
                }

                method3.Invoke(__instance.Mission, new object[] { __instance, agent, b, collisionData, isBlocked, damagedHp });
                if (__instance.Health < 1f)
                {
                    KillInfo overrideKillInfo = (b.IsFallDamage ? KillInfo.Gravity : KillInfo.Invalid);
                    if (__instance.IsActive())
                    {
                        __instance.Die(b, overrideKillInfo);
                    }
                }

                MethodInfo method2 = typeof(Agent).GetMethod("HandleBlowAux", BindingFlags.NonPublic | BindingFlags.Instance);
                method2.DeclaringType.GetMethod("HandleBlowAux");
                method2.Invoke(__instance, new object[] { b });

                return false;
            }
        }
    }

    [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
    [HarmonyPatch("GetAttackCollisionResults")]
    internal class GetAttackCollisionResultsPatch
    {
        private static void Postfix(in AttackInformation attackInformation, bool crushedThrough, float momentumRemaining, in MissionWeapon attackerWeapon, bool cancelDamage, ref AttackCollisionData attackCollisionData, ref CombatLogData combatLog, int speedBonus)
        {
            if (!attackCollisionData.IsColliderAgent && attackCollisionData.EntityExists)
            {
                if (!attackCollisionData.IsMissile)
                {
                    attackCollisionData.InflictedDamage = attackCollisionData.InflictedDamage + 5;
                    combatLog.InflictedDamage = attackCollisionData.InflictedDamage;
                }
            }
        }
    }
}