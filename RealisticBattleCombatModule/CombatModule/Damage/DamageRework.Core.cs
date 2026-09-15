using HarmonyLib;
using RBMAI;
using SandBox.GameComponents;
using SandBox.Missions.MissionLogics;
using System;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ArmorComponent;
using static TaleWorlds.Core.ItemObject;
using static TaleWorlds.MountAndBlade.Agent;

namespace RBMCombat
{
    internal partial class DamageRework
    {
        private static readonly MethodInfo UpdateLastAttackAndHitTimesMethod =
            typeof(Agent).GetMethod("UpdateLastAttackAndHitTimes", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo HandleBlowAuxMethod =
            typeof(Agent).GetMethod("HandleBlowAux", BindingFlags.NonPublic | BindingFlags.Instance);

        private static float CalculateCouchedLanceMagnitude(float blowMagnitude, float braceModifier, float braceBonus)
        {
            const float couchedSkill = 2f;
            const float skillCap = 230f;
            const float ashBreakThreshold = 430f;
            const float weaponWeight = 1.5f;

            braceBonus += 0.5f;
            braceModifier *= 3f;

            float lanceBallistics = (blowMagnitude * braceModifier) / weaponWeight;
            float couchedMagnitude = lanceBallistics * (weaponWeight + couchedSkill + braceBonus);
            float result = couchedMagnitude;

            float thrustMod = RBMConfig.RBMConfig.ThrustMagnitudeModifier;
            float ballisticDamage = lanceBallistics * (weaponWeight + braceBonus);

            if (couchedMagnitude > skillCap * thrustMod && ballisticDamage < skillCap * thrustMod)
            {
                result = skillCap * thrustMod;
            }

            if (ballisticDamage >= skillCap * thrustMod)
            {
                result = ballisticDamage;
            }

            if (result > ashBreakThreshold * thrustMod)
            {
                result = ashBreakThreshold * thrustMod;
            }

            return result;
        }

        private static float GetBodyPartDamageMultiplier(BoneBodyPartType bodyPart, DamageTypes damageType)
        {
            switch (bodyPart)
            {
                case BoneBodyPartType.Abdomen:
                    return (damageType == DamageTypes.Pierce || damageType == DamageTypes.Cut || damageType == DamageTypes.Blunt) ? 1f : 0.7f;
                case BoneBodyPartType.Chest:
                    return (damageType == DamageTypes.Pierce || damageType == DamageTypes.Cut || damageType == DamageTypes.Blunt) ? 0.9f : 1f;
                case BoneBodyPartType.ShoulderLeft:
                case BoneBodyPartType.ShoulderRight:
                    return damageType == DamageTypes.Blunt ? 0.7f :
                           (damageType == DamageTypes.Pierce || damageType == DamageTypes.Cut) ? 0.6f : 1f;
                case BoneBodyPartType.ArmLeft:
                case BoneBodyPartType.ArmRight:
                    return damageType == DamageTypes.Pierce ? 0.5f :
                           damageType == DamageTypes.Cut ? 0.6f :
                           damageType == DamageTypes.Blunt ? 0.7f : 1f;
                case BoneBodyPartType.Legs:
                    return damageType == DamageTypes.Pierce ? 0.5f :
                           damageType == DamageTypes.Cut ? 0.6f :
                           damageType == DamageTypes.Blunt ? 0.7f : 1f;
                case BoneBodyPartType.Head:
                case BoneBodyPartType.Neck:
                    return (damageType == DamageTypes.Pierce || damageType == DamageTypes.Cut || damageType == DamageTypes.Blunt) ? 1.5f : 1f;
                default:
                    return 1f;
            }
        }

        [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
        [HarmonyPatch("ComputeBlowDamage")]
        public class OverrideDamageCalc
        {
            private static bool Prefix(ref AttackInformation attackInformation, ref AttackCollisionData attackCollisionData, WeaponComponentData attackerWeapon, ref DamageTypes damageType, float magnitude, int speedBonus, bool cancelDamage, out int inflictedDamage, out int absorbedByArmor, out bool isSneakAttack)
            {
                isSneakAttack = false;
                float armorAmountFloat = attackInformation.ArmorAmountFloat;
                if (!attackCollisionData.IsMissile)
                {
                    float wdm = MissionGameModels.Current.AgentStatCalculateModel.GetWeaponDamageMultiplier(attackInformation.AttackerAgent, attackerWeapon);
                    magnitude = attackCollisionData.BaseMagnitude / wdm;
                }
                WeaponComponentData shieldOnBack = attackInformation.ShieldOnBack;
                AgentFlag victimAgentFlag = attackInformation.VictimAgentFlags;
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
                    float adjustedArmor = MissionGameModels.Current.StrikeMagnitudeModel.CalculateAdjustedArmorForBlow(attackInformation, attackCollisionData, armorAmountFloat, attackerAgentCharacter, attackerCaptainCharacter, victimAgentCharacter, victimCaptainCharacter, attackerWeapon);
                    armorAmount = adjustedArmor;
                }

                Agent attacker = attackInformation.AttackerAgent;
                Agent victim = attackInformation.VictimAgent;

                bool isUnarmedAttack = false;
                //detect unarmed attack
                if (attackerWeapon == null && attacker != null && victim != null && damageType == DamageTypes.Blunt && !attackCollisionData.IsFallDamage && !attackCollisionData.IsHorseCharge)
                {
                    isUnarmedAttack = true;
                }
                if (isUnarmedAttack)
                {
                    magnitude = 1f;
                    ArmorMaterialTypes gauntletMaterial = ArmorRework.GetArmorMaterialForBodyPartRBM(attacker, BoneBodyPartType.ArmRight);
                    switch (gauntletMaterial)
                    {
                        case ArmorMaterialTypes.None:
                            {
                                magnitude *= 0.4f;
                                break;
                            }
                        case ArmorMaterialTypes.Cloth:
                            {
                                magnitude *= 0.3f;
                                break;
                            }
                        case ArmorMaterialTypes.Leather:
                            {
                                magnitude *= 0.3f;
                                break;
                            }
                        case ArmorMaterialTypes.Chainmail:
                            {
                                magnitude *= 0.75f;
                                break;
                            }
                        case ArmorMaterialTypes.Plate:
                            {
                                magnitude *= 1f;
                                break;
                            }
                    }
                    float gauntletWeight = ArmorRework.getGauntletWeight(attacker);
                    magnitude += gauntletWeight;
                }

                ArmorMaterialTypes armorMaterial = ArmorRework.GetArmorMaterialForBodyPartRBM(victim, attackCollisionData.VictimHitBodyPart);

                bool isBash = false;
                if (attacker != null && attackCollisionData.StrikeType == (int)StrikeType.Swing && damageType != DamageTypes.Blunt && !attacker.WieldedWeapon.IsEmpty && !Utilities.HitWithWeaponBlade(in attackCollisionData, attacker.WieldedWeapon))
                {
                    isBash = true;
                    damageType = DamageTypes.Blunt;
                }
                bool isThrustCut = false;
                if (attackerWeapon != null && attacker != null)
                {
                    if (attackerWeapon.WeaponClass == WeaponClass.OneHandedSword ||
                               attackerWeapon.WeaponClass == WeaponClass.Dagger ||
                               attackerWeapon.WeaponClass == WeaponClass.TwoHandedSword)
                    {
                        if (attackCollisionData.StrikeType == (int)StrikeType.Thrust)
                        {

                            if (!Utilities.ThurstWithTip(in attackCollisionData, attacker.WieldedWeapon))
                            {
                                damageType = DamageTypes.Cut;
                                isThrustCut = true;
                            }
                        }
                    }
                }

                bool faceshot = false;
                bool lowerShoulderHit = false;

                if (victim != null && victim.IsHuman && attackCollisionData.VictimHitBodyPart == BoneBodyPartType.Head && !isThrustCut)
                {
                    float dotProduct = Vec3.DotProduct(attackCollisionData.WeaponBlowDir, victim.LookFrame.rotation.f);
                    float dotProductThreshold = -0.75f;
                    if (attackCollisionData.StrikeType == (int)StrikeType.Swing)
                    {
                        dotProductThreshold = -0.8f;
                    }
                    if (dotProduct < dotProductThreshold && attackCollisionData.CollisionGlobalNormal.z < 0f)
                    {
                        if (attacker != null && attacker.IsPlayerControlled)
                        {
                            InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_COM_005}Face hit!").ToString(), Color.FromUint(4289612505u)));
                        }
                        if (victim != null && victim.IsPlayerControlled)
                        {
                            InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_COM_006}Face hit!").ToString(), Color.FromUint(4289612505u)));
                        }
                        faceshot = true;
                    }
                }

                if (victim != null && victim.IsHuman && (attackCollisionData.VictimHitBodyPart == BoneBodyPartType.ShoulderLeft || attackCollisionData.VictimHitBodyPart == BoneBodyPartType.ShoulderRight))
                {
                    if ((attackCollisionData.CollisionBoneIndex == 15 || attackCollisionData.CollisionBoneIndex == 22) && attackCollisionData.CollisionGlobalNormal.z < 0.15f)
                    {
                        if (attacker != null && attacker.IsPlayerControlled)
                        {
                            InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_COM_007}Under shoulder hit!").ToString(), Color.FromUint(4289612505u)));
                        }
                        if (victim != null && victim.IsPlayerControlled)
                        {
                            InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_COM_008}Under shoulder hit!").ToString(), Color.FromUint(4289612505u)));
                        }
                        lowerShoulderHit = true;
                    }
                }

                if (faceshot)
                {
                    if (!victim.SpawnEquipment[EquipmentIndex.Head].IsEmpty)
                    {
                        armorAmount = victim.SpawnEquipment[EquipmentIndex.Head].GetModifiedBodyArmor();

                        if (victim.SpawnEquipment[EquipmentIndex.Head].Item.ArmorComponent != null)
                        {
                            armorMaterial = victim.SpawnEquipment[EquipmentIndex.Head].Item.ArmorComponent.MaterialType;
                            if (victim.SpawnEquipment[EquipmentIndex.Head].Item.ArmorComponent.MaterialType == ArmorMaterialTypes.Plate)
                            {
                                if (victim.SpawnEquipment[EquipmentIndex.Head].GetModifiedItemName().Contains("Closed"))
                                {
                                    armorMaterial = ArmorMaterialTypes.Chainmail;
                                }
                            }
                        }
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
                        armorAmount = victim.SpawnEquipment[EquipmentIndex.Body].GetModifiedArmArmor();

                        if (victim.SpawnEquipment[EquipmentIndex.Body].Item.ArmorComponent != null)
                        {
                            armorMaterial = victim.SpawnEquipment[EquipmentIndex.Body].Item.ArmorComponent.MaterialType;
                            if (victim.SpawnEquipment[EquipmentIndex.Body].Item.ArmorComponent.MaterialType == ArmorMaterialTypes.Plate)
                            {
                                if (victim.SpawnEquipment[EquipmentIndex.Body].GetModifiedItemName().Contains("Brigandine over Hauberk") ||
                                    victim.SpawnEquipment[EquipmentIndex.Body].GetModifiedItemName().Contains("Khan's Coat of Plates") ||
                                    victim.SpawnEquipment[EquipmentIndex.Body].GetModifiedItemName().Contains("Rough Brigandine") ||
                                    victim.SpawnEquipment[EquipmentIndex.Body].GetModifiedItemName().Contains("Mirrored Brigandine Armor") ||
                                    victim.SpawnEquipment[EquipmentIndex.Body].GetModifiedItemName().Contains("Northern Raider Armor") ||
                                    victim.SpawnEquipment[EquipmentIndex.Body].GetModifiedItemName().Contains("Rough Scale Mail"))
                                {
                                    armorMaterial = ArmorMaterialTypes.Plate;
                                }
                                else
                                {
                                    armorMaterial = ArmorMaterialTypes.Chainmail;
                                }
                            }
                        }
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
                else
                {
                    if (isUnarmedAttack)
                    {
                        weaponType = "unarmedAttack";
                    }
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
                        if (attacker != null && attacker.Equipment != null && attacker.GetPrimaryWieldedItemIndex() != EquipmentIndex.None)
                        {
                            itemModifier = attacker.Equipment[attacker.GetPrimaryWieldedItemIndex()].ItemModifier;
                            magnitude = Utilities.GetSkillBasedDamage(magnitude, attackInformation.IsAttackerAgentDoingPassiveAttack, weaponType, damageType, effectiveSkill, skillModifier, (StrikeType)attackCollisionData.StrikeType, attacker.Equipment[attacker.GetPrimaryWieldedItemIndex()].GetWeight());
                        }
                        else
                        {
                        }
                        if (isUnarmedAttack)
                        {
                            magnitude = Utilities.GetSkillBasedDamage(magnitude, attackInformation.IsAttackerAgentDoingPassiveAttack, weaponType, damageType, effectiveSkill, skillModifier, (StrikeType)attackCollisionData.StrikeType, 5f);
                        }
                    }
                }

                if (RBMConfig.RBMConfig.postureEnabled)
                {
                    Stance victimPosture = null;
                    Stance attackerPosture = null;
                    AgentStances.values.TryGetValue(attacker, out attackerPosture);
                    AgentStances.values.TryGetValue(victim, out victimPosture);

                    if (RBMConfig.RBMConfig.staminaEnabled)
                    {
                        //stamina effct on attacker
                        if (attackerPosture != null)
                        {
                            float staminaLevel = attackerPosture.stamina / attackerPosture.maxStamina;

                            //magnitude modifier based on stamina level
                            magnitude *= MBMath.Lerp(0.85f, 1f, staminaLevel);
                        }

                        //stamina effct on victim
                        if (victimPosture != null)
                        {
                            float staminaLevel = victimPosture.stamina / victimPosture.maxStamina;

                            //armor amount modifier based on stamina level
                            float armorStaminaLevel = 0.3f + Math.Max(0.7f, staminaLevel);
                            armorAmount *= armorStaminaLevel;
                        }
                    }
                }

                float dmgMultiplier = 1f;

                if (!attackBlockedWithShield && !isFallDamage)
                {
                    dmgMultiplier *= GetBodyPartDamageMultiplier(attackCollisionData.VictimHitBodyPart, damageType);
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
                    if (attackCollisionData.StrikeType == (int)StrikeType.Thrust && damageType == DamageTypes.Cut)
                    {
                        weaponDamageFactor = (float)Math.Sqrt(Utilities.getSwingDamageFactor(attackerWeapon, itemModifier));
                    }
                }

                //special javelin case
                if (attackerWeapon != null && attackerWeapon.WeaponClass == WeaponClass.Javelin && attackerWeapon.WeaponFlags.HasFlag(WeaponFlags.BonusAgainstShield))
                {
                    weaponDamageFactor *= 3f;
                }

                inflictedDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(weaponType, damageType, magnitude, armorAmount, victimAgentAbsorbedDamageRatio, out _, out _, weaponDamageFactor, player, isPlayerVictim, armorMaterial)), 0, 2000);
                inflictedDamage = MathF.Floor(inflictedDamage * dmgMultiplier);

                //stealth calculation
                float stealthDmgMultiplier = 1f;
                if (!attackBlockedWithShield && !isFallDamage)
                {
                    if (MissionGameModels.Current.AgentApplyDamageModel.CanWeaponDealSneakAttack(in attackInformation, attackerWeapon))
                    {
                        float sneakAttackMultiplier = MissionGameModels.Current.AgentStatCalculateModel.GetSneakAttackMultiplier(attackInformation.AttackerAgent, attackerWeapon);
                        stealthDmgMultiplier *= sneakAttackMultiplier;
                        isSneakAttack = true;
                    }
                }

                inflictedDamage = (int)(inflictedDamage * stealthDmgMultiplier);
                if (isSneakAttack && RBMConfig.RBMConfig.sneakAttackInstaKill)
                {
                    inflictedDamage = 200;
                }

                int absoluteDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(weaponType, damageType, magnitude, 0f, victimAgentAbsorbedDamageRatio, out _, out _, weaponDamageFactor) * dmgMultiplier), 0, 2000);
                absorbedByArmor = absoluteDamage - inflictedDamage;

                return false;
            }

            public static void RBMComputeBlowDamageOnShield(ref AttackInformation attackInformation, ref AttackCollisionData attackCollisionData, WeaponComponentData attackerWeapon, float blowMagnitude, out int inflictedDamage)
            {
                float localInflictedDamage = 0;
                attackCollisionData.InflictedDamage = 0;

                Agent victim = attackInformation.VictimAgent;
                Agent attacker = attackInformation.AttackerAgent;

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
                    UpdateLastAttackAndHitTimesMethod.Invoke(victim, new object[] { attacker, attackCollisionData.IsMissile });
                }

                victimShield = attackInformation.VictimShield;
                if (!victimShield.IsEmpty && victimShield.CurrentUsageItem.WeaponFlags.HasAnyFlag(WeaponFlags.CanBlockRanged) && attackInformation.CanGiveDamageToAgentShield)
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
                                            blowMagnitude = CalculateCouchedLanceMagnitude(blowMagnitude, BraceModifier, BraceBonus);
                                        }
                                        else
                                        {
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
                                            blowMagnitude = CalculateCouchedLanceMagnitude(blowMagnitude, BraceModifier, BraceBonus);
                                        }
                                        else
                                        {
                                            blowMagnitude = (blowMagnitude * 0.4f + 60f * RBMConfig.RBMConfig.ThrustMagnitudeModifier) * 1.3f;
                                            if (blowMagnitude > 360f * RBMConfig.RBMConfig.ThrustMagnitudeModifier)
                                            {
                                                blowMagnitude = 360f * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
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
                                    localInflictedDamage *= 25f;
                                    break;
                                }
                            case "ThrowingAxe":
                                {
                                    localInflictedDamage *= 10f;
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
                                    if (attackCollisionData.DamageType == (int)DamageTypes.Pierce)
                                    {
                                        localInflictedDamage *= 0.09f;
                                        break;
                                    }
                                    localInflictedDamage *= 1.5f;

                                    break;
                                }
                            default:
                                {
                                    if (attackCollisionData.DamageType == (int)DamageTypes.Pierce)
                                    {
                                        localInflictedDamage *= 0.09f;
                                    }
                                    else if (attackCollisionData.DamageType == (int)DamageTypes.Blunt)
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
    }
}
