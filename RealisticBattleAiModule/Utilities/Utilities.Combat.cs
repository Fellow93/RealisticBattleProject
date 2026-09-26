using RBMConfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ArmorComponent;
using static TaleWorlds.Core.ItemObject;

namespace RBMAI
{
    public static partial class Utilities
    {

        //public static int GetMeleeSkill(Agent agent, WeaponComponentData equippedItem, WeaponComponentData secondaryItem)
        //{
        //    SkillObject skill = DefaultSkills.Athletics;
        //    if (equippedItem != null)
        //    {
        //        SkillObject relevantSkill = equippedItem.RelevantSkill;
        //        skill = ((relevantSkill == DefaultSkills.OneHanded || relevantSkill == DefaultSkills.Polearm) ? relevantSkill : ((relevantSkill != DefaultSkills.TwoHanded) ? DefaultSkills.OneHanded : ((secondaryItem == null) ? DefaultSkills.TwoHanded : DefaultSkills.OneHanded)));
        //    }
        //    return GetEffectiveSkill(agent.Character, agent.Origin, agent.Formation, skill);
        //}

        //public static int GetEffectiveSkill(BasicCharacterObject agentCharacter, IAgentOriginBase agentOrigin, Formation agentFormation, SkillObject skill)
        //{
        //    return agentCharacter.GetSkillValue(skill);
        //}

        public static bool HitWithWeaponBladeTip(in AttackCollisionData collisionData, in MissionWeapon attackerWeapon)
        {
            WeaponComponentData currentUsageItem = attackerWeapon.CurrentUsageItem;
            if (currentUsageItem != null)
            {
                WeaponClass weaponClass = attackerWeapon.CurrentUsageItem.WeaponClass;
                if (collisionData.CollisionDistanceOnWeapon > currentUsageItem.GetRealWeaponLength() * 0.95f)
                {
                    return true;
                }
                return false;
            }
            return false;
        }

        public static bool HitWithWeaponBlade(in AttackCollisionData collisionData, in MissionWeapon attackerWeapon)
        {
            WeaponComponentData currentUsageItem = attackerWeapon.CurrentUsageItem;
            if (attackerWeapon.Item != null && currentUsageItem != null && attackerWeapon.Item.WeaponDesign != null &&
                attackerWeapon.Item.WeaponDesign.UsedPieces != null && attackerWeapon.Item.WeaponDesign.UsedPieces.Length > 0)
            {
                bool isSwordType = false;
                if (attackerWeapon.CurrentUsageItem != null)
                    switch (attackerWeapon.CurrentUsageItem.WeaponClass)
                    {
                        case WeaponClass.Dagger:
                        case WeaponClass.OneHandedSword:
                        case WeaponClass.TwoHandedSword:
                            {
                                isSwordType = true;
                                break;
                            }
                    }
                float bladeLength = attackerWeapon.Item.WeaponDesign.UsedPieces[0].ScaledBladeLength + (isSwordType ? 0f : 0.15f);
                float realWeaponLength = currentUsageItem.GetRealWeaponLength();
                if (collisionData.CollisionDistanceOnWeapon < (realWeaponLength - bladeLength))
                {
                    return false;
                }
                return true;
            }
            return true;
        }

        public static float GetComHitModifier(in AttackCollisionData collisionData, in MissionWeapon attackerWeapon)
        {
            WeaponComponentData currentUsageItem = attackerWeapon.CurrentUsageItem;
            if (collisionData.StrikeType == (int)StrikeType.Thrust)
            {
                if (collisionData.CollisionHitResultFlags == CombatHitResultFlags.NormalHit)
                {
                    return 1f;
                }
                else
                {
                    return 0.3f;
                }
            }

            float comHitModifier = 0f;
            if (attackerWeapon.Item != null && currentUsageItem != null && attackerWeapon.Item.WeaponDesign != null &&
                attackerWeapon.Item.WeaponDesign.UsedPieces != null && attackerWeapon.Item.WeaponDesign.UsedPieces.Length > 0)
            {
                float impactPointAsPercent = MBMath.ClampFloat(collisionData.CollisionDistanceOnWeapon, -0.2f, currentUsageItem.GetRealWeaponLength()) / currentUsageItem.GetRealWeaponLength();
                float comAsPercent = MBMath.ClampFloat(currentUsageItem.CenterOfMass, -0.2f, currentUsageItem.GetRealWeaponLength()) / currentUsageItem.GetRealWeaponLength();
                comHitModifier = 1f - Math.Abs(comAsPercent - impactPointAsPercent);
                if (attackerWeapon.CurrentUsageItem != null)
                {
                    switch (attackerWeapon.CurrentUsageItem.WeaponClass)
                    {
                        case WeaponClass.OneHandedAxe:
                        case WeaponClass.TwoHandedAxe:
                        case WeaponClass.Mace:
                        case WeaponClass.TwoHandedMace:
                        case WeaponClass.TwoHandedPolearm:
                            {
                                if (collisionData.StrikeType == (int)StrikeType.Swing)
                                {
                                    if (HitWithWeaponBlade(collisionData, attackerWeapon))
                                    {
                                        return 1f;
                                    }
                                    else
                                    {
                                        return 0.3f;
                                    }
                                }
                                break;
                            }
                        case WeaponClass.Dagger:
                        case WeaponClass.OneHandedSword:
                        case WeaponClass.TwoHandedSword:
                            {
                                float bladeLength = attackerWeapon.Item.WeaponDesign.UsedPieces[0].ScaledBladeLength + 0f;
                                float realWeaponLength = currentUsageItem.GetRealWeaponLength();
                                if (collisionData.CollisionDistanceOnWeapon < (realWeaponLength - bladeLength))
                                {
                                    return 1f;
                                }
                                break;
                            }
                    }
                }
                if (comHitModifier > 0.66f)
                {
                    return 1f;
                }
                else if (comHitModifier > 0.33f)
                {
                    return 0.66f;
                }
                else
                {
                    return 0.33f;
                }
            }
            return comHitModifier;
        }

        public static float CalculateSkillModifier(int relevantSkillLevel)
        {
            return MBMath.ClampFloat((float)relevantSkillLevel / 250f, 0f, 1f);
        }

        public static float CalculateSkillModifier(float relevantSkillLevel)
        {
            return MBMath.ClampFloat(relevantSkillLevel / 250f, 0f, 1f);
        }

        public static float GetEffectiveSkillWithDR(int effectiveSkill)
        {
            float effectiveSkillWithDR = 0f;
            effectiveSkillWithDR = (600f / (600f + effectiveSkill)) * (float)effectiveSkill;

            //float oneskillStep = 25f;
            //int skillSteps = MathF.Floor(effectiveSkill / 25f);
            //for(int i = 1; i <= skillSteps; i++)
            //{
            //    effectiveSkillWithDR = MathF.Pow(i * oneskillStep, 1f - ((i-1)/100f));
            //}
            return effectiveSkillWithDR;
        }

        public static float CalculateThrustMagnitudeForOneHandedWeapon(float weaponWeight, float effectiveSkill, float thrustSpeed, float exraLinearSpeed, Agent.UsageDirection attackDirection)
        {
            float magnitude = 0f;

            bool isOverheadAttack = attackDirection == Agent.UsageDirection.AttackUp;

            thrustSpeed = (isOverheadAttack ? thrustSpeed * 1.33f : thrustSpeed);
            if (thrustSpeed > 9f)
            {
                thrustSpeed = 9f;
            }
            float combinedSpeed = thrustSpeed + exraLinearSpeed;
            float skillModifier = Utilities.CalculateSkillModifier(effectiveSkill) * 2f;

            float spearKineticEnergy = 0.5f * weaponWeight * (combinedSpeed * combinedSpeed);

            float armStrength = isOverheadAttack ? oneHandedPolearmThrustStrength - 1f : oneHandedPolearmThrustStrength;

            float thrustStrength = weaponWeight + (armStrength * (1f + skillModifier));
            float thrustStrengthWithWeaponWeight = weaponWeight + (armStrength * (1f + skillModifier));

            float thrustEnergyCap = MathF.Clamp(0.5f * thrustStrength * (thrustSpeed * thrustSpeed) * 1.5f, 0f, 180f);
            float thrustEnergy = 0.5f * thrustStrengthWithWeaponWeight * (combinedSpeed * combinedSpeed);
            if (thrustEnergy > thrustEnergyCap)
            {
                thrustEnergy = thrustEnergyCap;
            }

            magnitude = thrustEnergy;

            if (spearKineticEnergy > magnitude)
            {
                magnitude = spearKineticEnergy;
            }

            if (magnitude > thrustEnergyCap)
            {
                magnitude = thrustEnergyCap;
            }

            return magnitude * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
        }

        public static float CalculateThrustMagnitudeForTwoHandedWeapon(float weaponWeight, float effectiveSkill, float thrustSpeed, float exraLinearSpeed, Agent.UsageDirection attackDirection)
        {
            float magnitude = 0f;

            bool isOverheadAttack = attackDirection == Agent.UsageDirection.AttackUp;
            thrustSpeed = (isOverheadAttack ? thrustSpeed + 1f : thrustSpeed);
            if (thrustSpeed > 6f)
            {
                thrustSpeed = 6f;
            }
            float combinedSpeed = thrustSpeed + exraLinearSpeed;
            float skillModifier = Utilities.CalculateSkillModifier(effectiveSkill) * 2f;

            float spearKineticEnergy = 0.5f * weaponWeight * (combinedSpeed * combinedSpeed);

            float armStrength = isOverheadAttack ? twoHandedPolearmThrustStrength - 1f : twoHandedPolearmThrustStrength;

            float thrustStrength = armStrength * (1f + skillModifier);
            float thrustStrengthWithWeaponWeight = weaponWeight + (armStrength * (1f + skillModifier));

            float thrustEnergyCap = MathF.Clamp(0.5f * thrustStrength * (thrustSpeed * thrustSpeed) * 1.5f, 0f, 250f);

            float thrustEnergy = 0.5f * thrustStrengthWithWeaponWeight * (combinedSpeed * combinedSpeed);
            if (thrustEnergy > thrustEnergyCap)
            {
                thrustEnergy = thrustEnergyCap;
            }

            magnitude = thrustEnergy;

            if (spearKineticEnergy > magnitude)
            {
                magnitude = spearKineticEnergy;
            }

            if (magnitude > thrustEnergyCap)
            {
                magnitude = thrustEnergyCap;
            }

            return magnitude * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
        }

        // RBMComputeDamage (armor penetration / blunt trauma) lives in RBMConfig.BlowDamage,
        // shared with RBMCombat so the crush-through estimate matches the real blow.

        // GetSkillBasedDamage (the per-weapon-class skill table) lives in RBMConfig.SkillDamage,
        // shared with RBMCombat so the crush-through estimate matches the real blow.

        public static ArmorMaterialTypes getArmArmorMaterial(Agent agent)
        {
            ArmorMaterialTypes material = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HandArmor)
                {
                    if (equipmentElement.Item.ArmorComponent != null)
                    {
                        return equipmentElement.Item.ArmorComponent.MaterialType;
                    }
                }
            }
            return material;
        }

        public static float getGauntletWeight(Agent agent)
        {
            float weight = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HandArmor)
                {
                    if (equipmentElement.Item.ArmorComponent != null)
                    {
                        return equipmentElement.Item.Weight / 2f;
                    }
                }
            }
            return weight;
        }

        public static void CalculateVisualSpeeds(EquipmentElement weapon, int weaponUsageIndex, float effectiveSkillDR, out int swingSpeedReal, out int thrustSpeedReal, out int handlingReal)
        {
            swingSpeedReal = -1;
            thrustSpeedReal = -1;
            handlingReal = -1;
            if (!weapon.IsEmpty && weapon.Item != null && weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex) != null)
            {
                int swingSpeed = weapon.GetModifiedSwingSpeedForUsage(weaponUsageIndex);
                int handling = weapon.GetModifiedHandlingForUsage(weaponUsageIndex);

                switch (weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).WeaponClass)
                {
                    case WeaponClass.LowGripPolearm:
                    case WeaponClass.Mace:
                    case WeaponClass.OneHandedAxe:
                    case WeaponClass.OneHandedPolearm:
                    case WeaponClass.TwoHandedMace:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 700f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.83f) * swingskillModifier);
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.Weight, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).TotalInertia, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass) * Utilities.thrustSpeedTransfer);
                            thrustSpeedReal = MathF.Ceiling((thrustSpeedReal * 1.1f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 0.83f) * handlingskillModifier);
                            break;
                        }
                    case WeaponClass.TwoHandedPolearm:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 700f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.83f) * swingskillModifier);
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.Weight, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).TotalInertia, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass) * Utilities.thrustSpeedTransfer);
                            thrustSpeedReal = MathF.Ceiling((thrustSpeedReal * 1.05f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 5f) * handlingskillModifier);
                            break;
                        }
                    case WeaponClass.TwoHandedAxe:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 800f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 700f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.75f) * swingskillModifier);
                            thrustSpeedReal = MathF.Ceiling((weapon.GetModifiedThrustSpeedForUsage(weaponUsageIndex) * 0.9f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 0.83f) * handlingskillModifier);
                            break;
                        }
                    case WeaponClass.OneHandedSword:
                    case WeaponClass.Dagger:
                    case WeaponClass.TwoHandedSword:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 800f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 800f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 800f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.83f) * swingskillModifier);
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.Weight, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).TotalInertia, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass) * Utilities.thrustSpeedTransfer);
                            thrustSpeedReal = MathF.Ceiling((thrustSpeedReal * 1.15f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 0.9f) * handlingskillModifier);
                            break;
                        }
                }
            }
        }

        public static void CalculateVisualSpeeds(MissionWeapon weapon, int weaponUsageIndex, float effectiveSkillDR, out int swingSpeedReal, out int thrustSpeedReal, out int handlingReal)
        {
            swingSpeedReal = -1;
            thrustSpeedReal = -1;
            handlingReal = -1;
            if (!weapon.IsEmpty && weapon.Item != null && weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex) != null)
            {
                int swingSpeed = weapon.GetModifiedSwingSpeedForCurrentUsage();
                int handling = weapon.GetModifiedHandlingForCurrentUsage();

                switch (weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).WeaponClass)
                {
                    case WeaponClass.LowGripPolearm:
                    case WeaponClass.Mace:
                    case WeaponClass.OneHandedAxe:
                    case WeaponClass.OneHandedPolearm:
                    case WeaponClass.TwoHandedMace:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 700f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.83f) * swingskillModifier);
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.GetWeight(), weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).TotalInertia, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass) * Utilities.thrustSpeedTransfer);
                            thrustSpeedReal = MathF.Ceiling((thrustSpeedReal * 1.1f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 0.83f) * handlingskillModifier);
                            break;
                        }
                    case WeaponClass.TwoHandedPolearm:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 700f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.83f) * swingskillModifier);
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.GetWeight(), weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).TotalInertia, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass) * Utilities.thrustSpeedTransfer);
                            thrustSpeedReal = MathF.Ceiling((thrustSpeedReal * 1.05f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 5f) * handlingskillModifier);
                            break;
                        }
                    case WeaponClass.TwoHandedAxe:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 800f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 700f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.75f) * swingskillModifier);
                            thrustSpeedReal = MathF.Ceiling((weapon.GetModifiedThrustSpeedForCurrentUsage() * 0.9f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 0.83f) * handlingskillModifier);
                            break;
                        }
                    case WeaponClass.OneHandedSword:
                    case WeaponClass.Dagger:
                    case WeaponClass.TwoHandedSword:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 800f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 800f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 800f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.83f) * swingskillModifier);
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.GetWeight(), weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).TotalInertia, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass) * Utilities.thrustSpeedTransfer);
                            thrustSpeedReal = MathF.Ceiling((thrustSpeedReal * 1.15f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 0.9f) * handlingskillModifier);
                            break;
                        }
                }
            }
        }

        public static float getSwingDamageFactor(WeaponComponentData wcd, ItemModifier itemModifier)
        {
            if (itemModifier == null)
            {
                return wcd.SwingDamageFactor;
            }
            else
            {
                float factorBonus = (itemModifier.ModifyDamage(100) - 100) / 100f;
                return wcd.SwingDamageFactor + factorBonus;
            }
        }

        public static float getThrustDamageFactor(WeaponComponentData wcd, ItemModifier itemModifier)
        {
            if (itemModifier == null)
            {
                return wcd.ThrustDamageFactor;
            }
            else
            {
                float factorBonus = (itemModifier.ModifyDamage(100) - 100) / 100f;
                return wcd.ThrustDamageFactor + factorBonus;
            }
        }

        public static void SimulateThrustLayer(double distance, double usablePower, double maxUsableForce, double mass, out double finalSpeed, out double finalTime)
        {
            double num = 0.0;
            double num2 = 0.01;
            double num3 = 0.0;
            while (num < distance)
            {
                double num4 = usablePower / num2;
                if (num4 > maxUsableForce)
                {
                    num4 = maxUsableForce;
                }
                double num5 = 0.01 * num4 / mass;
                num2 += num5;
                num += num2 * 0.01;
                num3 += 0.01;
            }
            finalSpeed = num2;
            finalTime = num3;
        }

        public static float CalculateThrustSpeed(float _currentWeaponWeight, float inertia, float com)
        {
            float _currentWeaponInertiaAroundGrip = inertia + _currentWeaponWeight * com * com;
            double num = 1.8 + (double)_currentWeaponWeight + (double)_currentWeaponInertiaAroundGrip * 0.2;
            double num2 = 170.0;
            double num3 = 90.0;
            double num4 = 24.0;
            double num5 = 15.0;
            //if (_weaponDescription.WeaponFlags.HasAllFlags(WeaponFlags.MeleeWeapon | WeaponFlags.NotUsableWithOneHand) && !_weaponDescription.WeaponFlags.HasAnyFlag(WeaponFlags.WideGrip))
            //{
            //    num += 0.6;
            //    num5 *= 1.9;
            //    num4 *= 1.1;
            //    num3 *= 1.2;
            //    num2 *= 1.05;
            //}
            //else if (_weaponDescription.WeaponFlags.HasAllFlags(WeaponFlags.MeleeWeapon | WeaponFlags.NotUsableWithOneHand | WeaponFlags.WideGrip))
            //{
            //    num += 0.9;
            //    num5 *= 2.1;
            //    num4 *= 1.2;
            //    num3 *= 1.2;
            //    num2 *= 1.05;
            //}
            SimulateThrustLayer(0.6, 250.0, 48.0, 4.0 + num, out var finalSpeed, out var finalTime);
            SimulateThrustLayer(0.6, num2, num4, 2.0 + num, out var finalSpeed2, out var finalTime2);
            SimulateThrustLayer(0.6, num3, num5, 0.5 + num, out var finalSpeed3, out var finalTime3);
            double num6 = 0.33 * (finalTime + finalTime2 + finalTime3);
            return (float)(3.8500000000000005 / num6);
        }

        public static int GetHarnessTier(Agent agent)
        {
            int tier = 10;
            EquipmentElement equipmentElement = agent.SpawnEquipment[EquipmentIndex.HorseHarness];
            if (agent != null)
            {
                if (agent.MountAgent != null)
                {
                    if (agent.SpawnEquipment != null)
                    {
                        if (equipmentElement.Item != null)
                        {
                            if (equipmentElement.Item.Effectiveness < 50f)
                            {
                                tier = (int)1;
                            }
                        }
                    }
                }
            }
            return tier;
        }

        public static float GetCombatAIDifficultyMultiplier()
        {
            MissionState missionState = Game.Current.GameStateManager.ActiveState as MissionState;
            if (missionState != null)
            {
                if (!RBMConfig.RBMConfig.vanillaCombatAi)
                {
                    if (missionState.MissionName.Equals("EnhancedBattleTestFieldBattle") || missionState.MissionName.Equals("EnhancedBattleTestSiegeBattle"))
                    {
                        return 1.0f;
                    }
                    switch (CampaignOptions.CombatAIDifficulty)
                    {
                        case CampaignOptions.Difficulty.VeryEasy:
                            return 0.70f;

                        case CampaignOptions.Difficulty.Easy:
                            return 0.85f;

                        case CampaignOptions.Difficulty.Realistic:
                            return 1.0f;

                        default:
                            return 1.0f;
                    }
                }
                else
                {
                    switch (CampaignOptions.CombatAIDifficulty)
                    {
                        case CampaignOptions.Difficulty.VeryEasy:
                            return 0.1f;

                        case CampaignOptions.Difficulty.Easy:
                            return 0.32f;

                        case CampaignOptions.Difficulty.Realistic:
                            return 0.96f;

                        default:
                            return 0.5f;
                    }
                }
            }
            else
            {
                return 1f;
            }
        }

        public static float CalculateAILevel(Agent agent, int relevantSkillLevel)
        {
            float difficultyModifier = GetCombatAIDifficultyMultiplier();
            //float difficultyModifier = 1.0f; // v enhanced battle test je difficulty very easy
            return MBMath.ClampFloat((float)relevantSkillLevel / 250f * difficultyModifier, 0f, 1f);
        }

    }
}
