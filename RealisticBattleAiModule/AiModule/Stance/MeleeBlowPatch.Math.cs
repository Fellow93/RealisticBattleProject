using HarmonyLib;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.TournamentGames;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static RBMAI.PostureDamage;
using static TaleWorlds.Core.ArmorComponent;
using static TaleWorlds.Core.ItemObject;
using static TaleWorlds.MountAndBlade.Agent;
namespace RBMAI
{
    public partial class StanceLogic : MissionLogic
    {
        private partial class CreateMeleeBlowPatch
        {
            private static float calculateDefenderStaminaLoss(Agent defenderAgent, Agent attackerAgent, ref AttackCollisionData collisionData, MeleeHitType meleeHitType, bool isUnarmedAttack)
            {
                float directHit = (collisionData.InflictedDamage * 2f) + 20f;
                float unarmedDefence = 40f;
                float oneHandedDefence = 40f;
                float twoHandedDefence = 40f;
                float smallShieldDefence = 43f;
                float largeShieldDefence = 46f;

                float defaultDefence = 40f;

                float result = 0f;

                if (meleeHitType == MeleeHitType.AgentHit)
                {
                    result = directHit;
                    return result;
                }

                if (isUnarmedAttack)
                {
                    switch (meleeHitType)
                    {
                        case MeleeHitType.AgentHit:
                            {
                                result = directHit;
                                return result;
                            }
                        default:
                            {
                                int effectiveSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(defenderAgent, DefaultSkills.Athletics);
                                result = unarmedDefence * calculateDefenderStaminaSkillModifier(effectiveSkill);
                                return result;
                            }
                    }
                }
                else
                {
                    MissionWeapon defenderWeapon = defenderAgent.WieldedWeapon;
                    SkillObject defenderWeaponSkill = (defenderAgent.WieldedWeapon.IsEmpty || defenderWeapon.CurrentUsageItem == null) ? DefaultSkills.Athletics : WeaponComponentData.GetRelevantSkillFromWeaponClass(defenderWeapon.CurrentUsageItem.WeaponClass);
                    int effectiveSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(defenderAgent, defenderWeaponSkill);
                    float skillModifier = calculateDefenderStaminaSkillModifier(effectiveSkill);

                    switch (meleeHitType)
                    {
                        case MeleeHitType.WeaponBlock:
                        case MeleeHitType.WeaponParry:
                        case MeleeHitType.ChamberBlock:
                            {
                                bool isDefenderWeaponOneHanded = isOneHandedWeapon(defenderWeapon);

                                if (isDefenderWeaponOneHanded)
                                {
                                    result = oneHandedDefence * skillModifier;
                                    return result;
                                }
                                else
                                {
                                    result = twoHandedDefence * skillModifier;
                                    return result;
                                }
                            }
                        case MeleeHitType.ShieldBlock:
                        case MeleeHitType.ShieldIncorrectBlock:
                        case MeleeHitType.ShieldParry:
                            {
                                bool isDefenderSmallShield = false;
                                if (defenderAgent.GetOffhandWieldedItemIndex() != EquipmentIndex.None)
                                {
                                    MissionWeapon defenderShield = defenderAgent.Equipment[defenderAgent.GetOffhandWieldedItemIndex()];
                                    isDefenderSmallShield = isSmallShield(defenderShield);
                                }
                                if (isDefenderSmallShield)
                                {
                                    result = smallShieldDefence * skillModifier;
                                    return result;
                                }
                                else
                                {
                                    result = largeShieldDefence * skillModifier;
                                    return result;
                                }
                            }
                        default:
                            {
                                result = defaultDefence * skillModifier;
                                return result;
                            }
                    }
                }
            }

            private static float calculateAttackerStaminaLoss(Agent defenderAgent, Agent attackerAgent, ref AttackCollisionData collisionData, MeleeHitType meleeHitType, bool isUnarmedAttack)
            {
                float result = 0f;
                MissionWeapon attackerWeapon = attackerAgent.WieldedWeapon;

                SkillObject attackerWeaponSkill = null;
                if (!isUnarmedAttack && !attackerWeapon.IsEmpty && attackerWeapon.CurrentUsageItem != null)
                {
                    attackerWeaponSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(attackerWeapon.CurrentUsageItem.WeaponClass);
                }
                int effectiveSkill = (isUnarmedAttack || attackerWeaponSkill == null)
                    ? MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attackerAgent, DefaultSkills.Athletics)
                    : MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attackerAgent, attackerWeaponSkill);

                float directHit = 35f;
                float unarmedAttack = 35f;
                float oneHandedAttack = 35f;
                float twoHandedAttack = 40f;

                float defaultAttack = 35f;

                //100 skill = 10% reduction
                float skillModifier = 1f - (effectiveSkill * 0.001f);

                if (isUnarmedAttack)
                {
                    switch (meleeHitType)
                    {
                        case MeleeHitType.AgentHit:
                            {
                                result = directHit;
                                return result;
                            }
                        default:
                            {
                                result = unarmedAttack * skillModifier;
                                return result;
                            }
                    }
                }
                else
                {
                    bool isAttackerWeaponOneHanded = isOneHandedWeapon(attackerWeapon);

                    switch (meleeHitType)
                    {
                        case MeleeHitType.AgentHit:
                            {
                                result = isAttackerWeaponOneHanded ? directHit : directHit * 1.4f;
                                return result;
                            }
                        case MeleeHitType.WeaponBlock:
                        case MeleeHitType.WeaponParry:
                        case MeleeHitType.ChamberBlock:
                        case MeleeHitType.ShieldBlock:
                        case MeleeHitType.ShieldIncorrectBlock:
                        case MeleeHitType.ShieldParry:
                            {
                                if (isAttackerWeaponOneHanded)
                                {
                                    result = oneHandedAttack * skillModifier;
                                    return result;
                                }
                                else
                                {
                                    result = twoHandedAttack * skillModifier;
                                    return result;
                                }
                            }
                        default:
                            {
                                result = defaultAttack * skillModifier;
                                return result;
                            }
                    }
                }
            }

            private static float calculateDefenderPostureDamage(Agent defenderAgent, Agent attackerAgent, float actionTypeDamageModifier, ref AttackCollisionData collisionData, MissionWeapon weapon, float comHitModifier, MeleeHitType meleeHitType, bool isUnarmedAttack)
            {
                float result = 0f;
                float defenderPostureDamageModifier = 1f; // terms and conditions may apply
                float strengthSkillModifier = 500f;
                float weaponSkillModifier = 500f;

                float basePostureDamage = getDefenderPostureDamage(defenderAgent, attackerAgent, collisionData.AttackDirection, (StrikeType)collisionData.StrikeType, meleeHitType);
                actionTypeDamageModifier = 1f;

                //SkillObject attackerWeaponSkill = isUnarmedAttack ? null : WeaponComponentData.GetRelevantSkillFromWeaponClass(weapon.CurrentUsageItem.WeaponClass);
                SkillObject attackerWeaponSkill = null;
                if (!isUnarmedAttack && !weapon.IsEmpty && weapon.CurrentUsageItem != null)
                {
                    attackerWeaponSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(weapon.CurrentUsageItem.WeaponClass);
                }
                float attackerEffectiveWeaponSkill = 0;
                float attackerEffectiveStrengthSkill = 0;
                if (attackerWeaponSkill != null)
                {
                    attackerEffectiveWeaponSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attackerAgent, attackerWeaponSkill);
                }
                if (attackerAgent.HasMount)
                {
                    attackerEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attackerAgent, DefaultSkills.Riding);
                }
                else
                {
                    attackerEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attackerAgent, DefaultSkills.Athletics);
                }

                float defenderEffectiveWeaponSkill = 0;
                float defenderEffectiveStrengthSkill = 0;

                if (defenderAgent.GetPrimaryWieldedItemIndex() != EquipmentIndex.None)
                {
                    MissionWeapon defenderWeapon = defenderAgent.Equipment[defenderAgent.GetPrimaryWieldedItemIndex()];
                    if (defenderWeapon.CurrentUsageItem != null)
                    {
                        SkillObject defenderWeaponSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(defenderWeapon.CurrentUsageItem.WeaponClass);
                        if (defenderWeaponSkill != null)
                        {
                            defenderEffectiveWeaponSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(defenderAgent, defenderWeaponSkill);
                        }
                    }
                    if (defenderAgent.GetOffhandWieldedItemIndex() != EquipmentIndex.None)
                    {
                        if (defenderAgent.Equipment[defenderAgent.GetOffhandWieldedItemIndex()].IsShield())
                        {
                            defenderEffectiveWeaponSkill += 20f;
                        }
                    }
                }
                if (defenderAgent.HasMount)
                {
                    defenderEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(defenderAgent, DefaultSkills.Riding);
                }
                else
                {
                    defenderEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(defenderAgent, DefaultSkills.Athletics);
                }

                if (isUnarmedAttack)
                {
                    attackerEffectiveWeaponSkill = attackerEffectiveStrengthSkill;
                }

                defenderEffectiveWeaponSkill = defenderEffectiveWeaponSkill / weaponSkillModifier;
                defenderEffectiveStrengthSkill = defenderEffectiveStrengthSkill / strengthSkillModifier;

                attackerEffectiveWeaponSkill = attackerEffectiveWeaponSkill / weaponSkillModifier;
                attackerEffectiveStrengthSkill = attackerEffectiveStrengthSkill / strengthSkillModifier;

                float skillModifier = (1f + attackerEffectiveStrengthSkill + attackerEffectiveWeaponSkill) / (1f + defenderEffectiveStrengthSkill + defenderEffectiveWeaponSkill);
                float additiveSpeedModifier = getRelativeSpeedPostureModifier(attackerAgent, defenderAgent);
                basePostureDamage = (basePostureDamage + additiveSpeedModifier) * skillModifier;

                //actionTypeDamageModifier += actionTypeDamageModifier * 0.5f * comHitModifier;
                result = basePostureDamage * actionTypeDamageModifier * defenderPostureDamageModifier * comHitModifier;
                //InformationManager.DisplayMessage(new InformationMessage("Deffender PD: " + result));
                return result;
            }

            private static float calculateAttackerPostureDamage(Agent defenderAgent, Agent attackerAgent, float actionTypeDamageModifier, ref AttackCollisionData collisionData, MissionWeapon weapon, float comHitModifier, MeleeHitType meleeHitType, bool isUnarmedAttack)
            {
                float result = 0f;

                float strengthSkillModifier = 500f;
                float weaponSkillModifier = 500f;

                float basePostureDamage = getAttackerPostureDamage(defenderAgent, attackerAgent, collisionData.AttackDirection, (StrikeType)collisionData.StrikeType, meleeHitType);
                actionTypeDamageModifier = 1f;

                SkillObject attackerWeaponSkill = null;
                if (!isUnarmedAttack && !weapon.IsEmpty && weapon.CurrentUsageItem != null)
                {
                    attackerWeaponSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(weapon.CurrentUsageItem.WeaponClass);
                }

                float attackerEffectiveWeaponSkill = 0;
                float attackerEffectiveStrengthSkill = 0;

                if (attackerWeaponSkill != null)
                {
                    attackerEffectiveWeaponSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attackerAgent, attackerWeaponSkill);
                }
                if (attackerAgent.HasMount)
                {
                    attackerEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attackerAgent, DefaultSkills.Riding);
                }
                else
                {
                    attackerEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attackerAgent, DefaultSkills.Athletics);
                }

                float defenderEffectiveWeaponSkill = 0;
                float defenderEffectiveStrengthSkill = 0;

                if (defenderAgent.GetPrimaryWieldedItemIndex() != EquipmentIndex.None)
                {
                    MissionWeapon defenderWeapon = defenderAgent.Equipment[defenderAgent.GetPrimaryWieldedItemIndex()];
                    if (defenderWeapon.CurrentUsageItem != null)
                    {
                        SkillObject defenderWeaponSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(defenderWeapon.CurrentUsageItem.WeaponClass);
                        if (defenderWeaponSkill != null)
                        {
                            defenderEffectiveWeaponSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(defenderAgent, defenderWeaponSkill);
                        }
                    }
                    if (defenderAgent.GetOffhandWieldedItemIndex() != EquipmentIndex.None)
                    {
                        if (defenderAgent.Equipment[defenderAgent.GetOffhandWieldedItemIndex()].IsShield())
                        {
                            defenderEffectiveWeaponSkill += 20f;
                        }
                    }
                }
                if (defenderAgent.HasMount)
                {
                    defenderEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(defenderAgent, DefaultSkills.Riding);
                }
                else
                {
                    defenderEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(defenderAgent, DefaultSkills.Athletics);
                }

                if (isUnarmedAttack)
                {
                    attackerEffectiveWeaponSkill = attackerEffectiveStrengthSkill;
                }

                defenderEffectiveWeaponSkill = defenderEffectiveWeaponSkill / weaponSkillModifier;
                defenderEffectiveStrengthSkill = defenderEffectiveStrengthSkill / strengthSkillModifier;

                attackerEffectiveWeaponSkill = attackerEffectiveWeaponSkill / weaponSkillModifier;
                attackerEffectiveStrengthSkill = attackerEffectiveStrengthSkill / strengthSkillModifier;

                float skillModifier = (1f + defenderEffectiveStrengthSkill + defenderEffectiveWeaponSkill) / (1f + attackerEffectiveStrengthSkill + attackerEffectiveWeaponSkill);
                float additiveSpeedModifier = getRelativeSpeedPostureModifier(attackerAgent, defenderAgent);
                basePostureDamage = (basePostureDamage + additiveSpeedModifier) * skillModifier;

                //actionTypeDamageModifier += actionTypeDamageModifier * 0.5f * comHitModifier;
                result = basePostureDamage * actionTypeDamageModifier * comHitModifier;
                //InformationManager.DisplayMessage(new InformationMessage("Attacker PD: " + result));
                return result;
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
                    float weaponInertia = weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).TotalInertia;
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
                    float weaponInertia = weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).TotalInertia;
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

            public static float calculateHealthDamage(MissionWeapon targetWeapon, Agent attacker, Agent victimAgent, float overPostureDamage, Blow b, bool isUnarmedAttack)
            {
                float armorSumPosture = victimAgent.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.Head);
                armorSumPosture += victimAgent.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.Neck);
                armorSumPosture += victimAgent.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.Chest);
                armorSumPosture += victimAgent.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.Abdomen);
                armorSumPosture += victimAgent.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.ShoulderLeft);
                armorSumPosture += victimAgent.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.ShoulderRight);
                armorSumPosture += victimAgent.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.ArmLeft);
                armorSumPosture += victimAgent.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.ArmRight);
                armorSumPosture += victimAgent.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.Legs);

                armorSumPosture = (armorSumPosture / 9f);
                float threshold = 20f;

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

                    if (currentSelectedChar != null && isUnarmedAttack)
                    {
                        int realDamage = 0;
                        int effectiveSkill = currentSelectedChar.GetSkillValue(DefaultSkills.Athletics);
                        float effectiveSkillDR = Utilities.GetEffectiveSkillWithDR(effectiveSkill);
                        float skillModifier = Utilities.CalculateSkillModifier(effectiveSkill);

                        float magnitude = 1f;

                        if (isUnarmedAttack)
                        {
                            ArmorMaterialTypes gauntletMaterial = Utilities.getArmArmorMaterial(attacker);
                            switch (gauntletMaterial)
                            {
                                case ArmorMaterialTypes.None:
                                    {
                                        magnitude *= 0.25f;
                                        break;
                                    }
                                case ArmorMaterialTypes.Cloth:
                                    {
                                        magnitude *= 0.4f;
                                        break;
                                    }
                                case ArmorMaterialTypes.Leather:
                                    {
                                        magnitude *= 0.5f;
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
                            float gauntletWeight = Utilities.getGauntletWeight(attacker);
                            magnitude += gauntletWeight;
                        }

                        float skillBasedDamage = Utilities.GetSkillBasedDamage(magnitude, false, "unarmedAttack", DamageTypes.Blunt, effectiveSkillDR, skillModifier, StrikeType.Swing, 5f);

                        realDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage("unarmedAttack", DamageTypes.Blunt, skillBasedDamage, armorSumPosture, 1f, out float penetratedDamage, out float bluntForce, swingDamageFactor, null, false)), 0, 2000);
                        realDamage = MathF.Floor(realDamage * 1f);
                        if (overPostureDamage > threshold)
                        {
                            return realDamage;
                        }
                        else
                        {
                            return realDamage * (overPostureDamage / threshold);
                        }
                    }

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
                            int realDamage = 0;
                            if (b.StrikeType == StrikeType.Swing)
                            {
                                float sweetSpotMagnitude = CalculateSweetSpotSwingMagnitude(currentSelectedChar, targetWeapon, targetWeaponUsageIndex, effectiveSkill);

                                float skillBasedDamage = Utilities.GetSkillBasedDamage(sweetSpotMagnitude, false, targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(),
                                    targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).SwingDamageType, effectiveSkillDR, skillModifier, StrikeType.Swing, targetWeapon.Item.Weight);

                                swingDamageFactor = (float)Math.Sqrt(Utilities.getSwingDamageFactor(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex), targetWeapon.ItemModifier));

                                realDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(), targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).SwingDamageType, skillBasedDamage, armorSumPosture, 1f, out float penetratedDamage, out float bluntForce, swingDamageFactor, null, false)), 0, 2000);
                                realDamage = MathF.Floor(realDamage * 1f);
                            }
                            else
                            {
                                float thrustMagnitude = CalculateThrustMagnitude(currentSelectedChar, targetWeapon, targetWeaponUsageIndex, effectiveSkill);

                                float skillBasedDamage = Utilities.GetSkillBasedDamage(thrustMagnitude, false, targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(),
                                    targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).ThrustDamageType, effectiveSkillDR, skillModifier, StrikeType.Thrust, targetWeapon.Item.Weight);

                                thrustDamageFactor = (float)Math.Sqrt(Utilities.getThrustDamageFactor(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex), targetWeapon.ItemModifier));

                                realDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(),
                                targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).ThrustDamageType, skillBasedDamage, armorSumPosture, 1f, out float penetratedDamage, out float bluntForce, thrustDamageFactor, null, false)), 0, 2000);
                                realDamage = MathF.Floor(realDamage * 1f);
                            }
                            if (overPostureDamage > threshold)
                            {
                                return realDamage;
                            }
                            else
                            {
                                return realDamage * (overPostureDamage / threshold);
                            }
                        }
                    }
                }

                int weaponDamage = 0;
                if (b.StrikeType == StrikeType.Swing)
                {
                    weaponDamage = isUnarmedAttack ? 4 : targetWeapon.GetModifiedSwingDamageForCurrentUsage();
                }
                else
                {
                    weaponDamage = isUnarmedAttack ? 4 : targetWeapon.GetModifiedThrustDamageForCurrentUsage();
                }

                int hpDamage = MBMath.ClampInt(MathF.Ceiling(MissionGameModels.Current.StrikeMagnitudeModel.ComputeRawDamage(b.DamageType, weaponDamage, armorSumPosture, 1f)), 0, 2000);
                if (overPostureDamage > threshold)
                {
                    return hpDamage;
                }
                else
                {
                    return hpDamage * (overPostureDamage / threshold);
                }
            }

        }
    }
}
