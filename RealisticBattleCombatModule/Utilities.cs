using RBMConfig;
using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ArmorComponent;
using static TaleWorlds.MountAndBlade.Agent;

namespace RBMCombat
{
    public static class Utilities
    {
        public static int numOfHits = 0;
        public static int numOfDurabilityDowngrade = 0;
        public static float throwableCorrectionSpeed = 15f;

        public static float swingSpeedTransfer = 4.5454545f;
        public static float thrustSpeedTransfer = 11.7647057f;

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

        public static int calculateMissileSpeed(float ammoWeight, string rangedWeaponType, int drawWeight)
        {
            int calculatedMissileSpeed = 10;
            switch (rangedWeaponType)
            {
                case "bow":
                    {
                        float powerstroke = (25f * 0.0254f); //28f
                        double potentialEnergy = 0.5f * (drawWeight * 4.448f) * powerstroke * 0.91f;
                        //calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt(((potentialEnergy * 2f) / ammoWeight) * 0.91f * ((ammoWeight * 3f) + 0.432f)));
                        //calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight + (drawWeight * 0.00012f))));
                        ammoWeight += drawWeight * 0.00012f;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;
                    }
                case "long_bow":
                    {
                        float powerstroke = (25f * 0.0254f); //30f
                        double potentialEnergy = 0.5f * (drawWeight * 4.448f) * powerstroke * 0.89f;
                        //calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt(((potentialEnergy * 2f) / ammoWeight) * 0.89f * ((ammoWeight * 3.3f) + 0.33f) * (1f + (0.416f - (0.0026 * drawWeight)))));
                        //calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight + (drawWeight * 0.00020f))));
                        ammoWeight += drawWeight * 0.00020f;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;
                    }
                case "crossbow":
                case "crossbow_fast":
                    {
                        float powerstroke = (12f * 0.0254f); //4.5f
                        double potentialEnergy = 0.5f * (drawWeight * 4.448f) * powerstroke * 0.7f;
                        //calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt(((potentialEnergy * 2f) / ammoWeight) * 0.45f));
                        //calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight + (drawWeight * 0.0000588f))));
                        ammoWeight += drawWeight * 0.00013f;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;
                    }
                case "osa_sling":
                    {
                        // 40 grams is added to the weight of projectiles, this results in 60 m/s at 80 grams with good sling, 70 m/s at 50 grams and some 80 ms at 30 grams
                        double potentialEnergy = 0.5f * (drawWeight * drawWeight) * 0.12f;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight + 0.04f)));
                        break;
                    }
                default:
                    {
                        calculatedMissileSpeed = 10;
                        break;
                    }
            }
            return calculatedMissileSpeed;
        }

        public static int calculateThrowableSpeed(float ammoWeight, float effectiveSkill)
        {
            int calculatedThrowingSpeed = (int)Math.Ceiling(Math.Sqrt((60f + effectiveSkill * 0.8f) * 2f / ammoWeight));
            if (calculatedThrowingSpeed > 32)
            {
                calculatedThrowingSpeed = 32;
            }
            return calculatedThrowingSpeed;
        }

        public static int assignThrowableMissileSpeedForMenu(float ammoWeight, int correctiveMissileSpeed, float effectiveSkill)
        {
            //float ammoWeight = throwable.GetWeight() / throwable.Amount;
            int calculatedThrowingSpeed = calculateThrowableSpeed(ammoWeight, effectiveSkill);
            //PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
            //property.DeclaringType.GetProperty("MissileSpeed");
            //throwable.CurrentUsageIndex = index;
            calculatedThrowingSpeed += correctiveMissileSpeed;
            return calculatedThrowingSpeed;
            //property.SetValue(throwable.CurrentUsageItem, calculatedThrowingSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
            //throwable.CurrentUsageIndex = 0;
        }

        public static int assignThrowableMissileSpeed(float ammoWeight, int correctiveMissileSpeed, float effectiveSkill, float equipmentWeight, WeaponClass shieldType)
        {
            //float ammoWeight = throwable.GetWeight() / throwable.Amount;
            float shieldTypeModifier = 1f;
            float equipmentWeightModifier = (float)Math.Sqrt(MBMath.ClampFloat(1f - (equipmentWeight * 0.015f),0.5f, 1f));
            switch ( shieldType ){
                case WeaponClass.LargeShield:
                    {
                        shieldTypeModifier = 0.836f;
                        break;
                    }
                case WeaponClass.SmallShield:
                    {
                        shieldTypeModifier = 0.948f;
                        break;
                    }
            }
            int calculatedThrowingSpeed = (int)Math.Round(calculateThrowableSpeed(ammoWeight, effectiveSkill) * shieldTypeModifier * equipmentWeightModifier);
            //PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
            //property.DeclaringType.GetProperty("MissileSpeed");
            //throwable.CurrentUsageIndex = index;
            calculatedThrowingSpeed += correctiveMissileSpeed;
            return calculatedThrowingSpeed;
            //property.SetValue(throwable.CurrentUsageItem, calculatedThrowingSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
            //throwable.CurrentUsageIndex = 0;
        }

        public static int assignStoneMissileSpeed(MissionWeapon throwable)
        {
            //PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
            //property.DeclaringType.GetProperty("MissileSpeed");
            //throwable.CurrentUsageIndex = index;
            //property.SetValue(throwable.CurrentUsageItem, 25, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
            //throwable.CurrentUsageIndex = 0;
            return 25;
        }

        public static void initiateCheckForArmor(ref Agent victim, AttackCollisionData attackCollisionData, Blow blow, Agent affectorAgent, in MissionWeapon attackerWeapon)
        {
            BoneBodyPartType bodyPartHit = attackCollisionData.VictimHitBodyPart;

            EquipmentIndex equipmentIndex = EquipmentIndex.None;
            ItemObject.ItemTypeEnum itemType = ItemObject.ItemTypeEnum.Invalid;

            if (!victim.IsHuman)
            {
                equipmentIndex = EquipmentIndex.HorseHarness;
                itemType = ItemObject.ItemTypeEnum.HorseHarness;
            }
            else
            {
                switch (bodyPartHit)
                {
                    case BoneBodyPartType.Head:
                    case BoneBodyPartType.Neck:
                        {
                            equipmentIndex = EquipmentIndex.Head;
                            itemType = ItemObject.ItemTypeEnum.HeadArmor;
                            break;
                        }
                    case BoneBodyPartType.Legs:
                        {
                            equipmentIndex = EquipmentIndex.Leg;
                            itemType = ItemObject.ItemTypeEnum.LegArmor;
                            break;
                        }
                    case BoneBodyPartType.ArmLeft:
                    case BoneBodyPartType.ArmRight:
                        {
                            equipmentIndex = EquipmentIndex.Gloves;
                            itemType = ItemObject.ItemTypeEnum.HandArmor;
                            break;
                        }
                    case BoneBodyPartType.Abdomen:
                    case BoneBodyPartType.Chest:
                        {
                            equipmentIndex = EquipmentIndex.Body;
                            itemType = ItemObject.ItemTypeEnum.BodyArmor;
                            break;
                        }
                    case BoneBodyPartType.ShoulderLeft:
                    case BoneBodyPartType.ShoulderRight:
                        {
                            equipmentIndex = EquipmentIndex.Cape;
                            itemType = ItemObject.ItemTypeEnum.Cape;
                            break;
                        }
                }
            }
            if (equipmentIndex != EquipmentIndex.None && itemType != ItemObject.ItemTypeEnum.Invalid)
            {
                lowerArmorQualityCheck(ref victim, equipmentIndex, itemType, attackCollisionData, blow, affectorAgent, attackerWeapon);
            }
        }

        public static void lowerArmorQualityCheck(ref Agent agent, EquipmentIndex equipmentIndex, ItemObject.ItemTypeEnum itemType, AttackCollisionData attackCollisionData, Blow blow, Agent attacker, in MissionWeapon attackerWeapon)
        {
            EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
             if (equipmentElement.Item != null && equipmentElement.Item.ItemType == itemType && !attackerWeapon.IsEmpty && blow.InflictedDamage > 1 && !blow.IsFallDamage)
            {
                WeaponClass weaponType = attackerWeapon.CurrentUsageItem.WeaponClass;

                float weaponTypeScaling = 1f;
                float weaponDamageFactor = 1f;
                float magnitude = blow.BaseMagnitude;
                RBMCombatConfigWeaponType rbmCombatConfigWeaponType = RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType.ToString());
                float armorThreshold = 4f;
                float armorValue = ArmorRework.GetBaseArmorEffectivenessForBodyPartRBM(agent, attackCollisionData.VictimHitBodyPart);

                ArmorMaterialTypes armorMaterialType = equipmentElement.Item.ArmorComponent.MaterialType;
                DamageTypes damageType = (DamageTypes)attackCollisionData.DamageType;
                if (attacker.IsHuman)
                {
                    EquipmentIndex slotIndex = attacker.GetWieldedItemIndex(HandIndex.MainHand);
                    if (slotIndex != EquipmentIndex.None)
                    {
                        WeaponComponentData wcd = attackerWeapon.CurrentUsageItem;
                        ItemModifier itemModifier = null;
                        if (!attackCollisionData.IsAlternativeAttack && attacker.IsHuman && !attackCollisionData.IsFallDamage && attacker.Origin != null && !attackCollisionData.IsMissile && wcd != null)
                        {
                            if (!attackCollisionData.IsMissile)
                            {
                                float wdm = MissionGameModels.Current.AgentStatCalculateModel.GetWeaponDamageMultiplier(attacker, wcd);
                                magnitude = attackCollisionData.BaseMagnitude / wdm;
                            }
                            SkillObject skill = (wcd == null) ? DefaultSkills.Athletics : wcd.RelevantSkill;
                            if (skill != null)
                            {
                                int ef = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attacker, skill);
                                float effectiveSkill = Utilities.GetEffectiveSkillWithDR(ef);
                                float skillModifier = Utilities.CalculateSkillModifier(ef);
                                if (attacker != null && attacker.Equipment != null && attacker.GetWieldedItemIndex(HandIndex.MainHand) != EquipmentIndex.None)
                                {
                                    itemModifier = attacker.Equipment[attacker.GetWieldedItemIndex(HandIndex.MainHand)].ItemModifier;
                                    magnitude = Utilities.GetSkillBasedDamage(blow.BaseMagnitude, attacker.IsDoingPassiveAttack, weaponType.ToString(), damageType, effectiveSkill, skillModifier, (StrikeType)attackCollisionData.StrikeType, attacker.Equipment[attacker.GetWieldedItemIndex(HandIndex.MainHand)].GetWeight());
                                }
                                else
                                {
                                }
                            }
                        }
                        weaponDamageFactor = (float)Math.Sqrt((attackCollisionData.StrikeType == (int)StrikeType.Thrust)
                        ? Utilities.getThrustDamageFactor(wcd, itemModifier)
                        : Utilities.getSwingDamageFactor(wcd, itemModifier));
                    }
                }

                if (attacker != null && attackCollisionData.StrikeType == (int)StrikeType.Swing && !attackCollisionData.AttackBlockedWithShield && !attacker.WieldedWeapon.IsEmpty && !Utilities.HitWithWeaponBlade(in attackCollisionData, attacker.WieldedWeapon))
                {
                    damageType = DamageTypes.Blunt;
                }

                switch (damageType)
                {
                    case DamageTypes.Pierce:
                        {
                            if(rbmCombatConfigWeaponType != null)
                            {
                                armorThreshold = rbmCombatConfigWeaponType.ExtraArmorThresholdFactorPierce;
                            }
                            weaponTypeScaling = 1f;
                            break;
                        }
                    case DamageTypes.Cut:
                        {
                            if (rbmCombatConfigWeaponType != null)
                            {
                                armorThreshold = rbmCombatConfigWeaponType.ExtraArmorThresholdFactorCut;
                            }
                            switch (weaponType)
                            {
                                case WeaponClass.OneHandedSword:
                                case WeaponClass.Dagger:
                                    {
                                        switch (armorMaterialType)
                                        {
                                            case ArmorMaterialTypes.Cloth:
                                            case ArmorMaterialTypes.Leather:
                                                {
                                                    weaponTypeScaling = 5f;
                                                    break;
                                                }
                                            case ArmorMaterialTypes.Chainmail:
                                                {
                                                    weaponTypeScaling = 1f;
                                                    break;
                                                }
                                            case ArmorMaterialTypes.Plate:
                                                {
                                                    weaponTypeScaling = 2f;
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                                case WeaponClass.TwoHandedSword:
                                    {
                                        switch (armorMaterialType)
                                        {
                                            case ArmorMaterialTypes.Cloth:
                                            case ArmorMaterialTypes.Leather:
                                                {
                                                    weaponTypeScaling = 5f;
                                                    break;
                                                }
                                            case ArmorMaterialTypes.Chainmail:
                                                {
                                                    weaponTypeScaling = 1.25f;
                                                    break;
                                                }
                                            case ArmorMaterialTypes.Plate:
                                                {
                                                    weaponTypeScaling = 2.5f;
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                                default:
                                    {
                                        switch (armorMaterialType)
                                        {
                                            case ArmorMaterialTypes.Cloth:
                                            case ArmorMaterialTypes.Leather:
                                                {
                                                    weaponTypeScaling = 2f;
                                                    break;
                                                }
                                            case ArmorMaterialTypes.Chainmail:
                                                {
                                                    weaponTypeScaling = 2f;
                                                    break;
                                                }
                                            case ArmorMaterialTypes.Plate:
                                                {
                                                    weaponTypeScaling = 4f;
                                                    break;
                                                }
                                        }
                                        break;
                                    }
                            }
                            break;
                        }
                    case DamageTypes.Blunt:
                        {
                            switch (armorMaterialType)
                            {
                                case ArmorMaterialTypes.Cloth:
                                case ArmorMaterialTypes.Leather:
                                case ArmorMaterialTypes.Chainmail:
                                    {
                                        weaponTypeScaling = 1f;
                                        break;
                                    }
                                case ArmorMaterialTypes.Plate:
                                    {
                                        weaponTypeScaling = 12f;
                                        break;
                                    }
                            }
                            break;
                        }
                }
                float defaultProbability = 0.05f;
                if (damageType == DamageTypes.Pierce && !blow.IsMissile)
                {
                    magnitude = magnitude * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                }
                //float magScaling = (float)Math.Pow((magnitude * weaponDamageFactor) / (armorThreshold * armorValue), 2);
                float magScaling = (blow.AbsorbedByArmor / (armorValue * armorThreshold)) / 5f;
                float scaledProbability = defaultProbability + (magScaling * weaponTypeScaling);
                float randomF = MBRandom.RandomFloat;
                //InformationManager.DisplayMessage(new InformationMessage(weaponType + " " + damageType + " " + armorMaterialType + ": " + Math.Round(scaledProbability * 100f, 2) + "%"));
                if (randomF <= scaledProbability)
                {
                    //numOfDurabilityDowngrade++;
                    lowerArmorQuality(ref agent, equipmentIndex, itemType);
                }
            }
        }

        public static void lowerArmorQuality(ref Agent agent, EquipmentIndex equipmentIndex, ItemObject.ItemTypeEnum itemType)
        {
            string oldItemModifier = " ";
            EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == itemType)
            {
                if (equipmentElement.Item != null)
                {
                    int currentModifier = 0;
                    if (equipmentElement.ItemModifier != null)
                    {
                        oldItemModifier = equipmentElement.ItemModifier.StringId;
                        currentModifier = equipmentElement.ItemModifier.ModifyArmor(100) - 100;
                    }
                    ItemModifier newIM = equipmentElement.ItemModifier;
                    IReadOnlyList<ItemModifier> itemModifiers = equipmentElement.Item?.ItemComponent?.ItemModifierGroup?.ItemModifiers;
                    if (itemModifiers != null && itemModifiers.Count > 0)
                    {
                        foreach (ItemModifier im in itemModifiers)
                        {
                            int tempIm = im.ModifyArmor(100) - 100;
                            if (equipmentElement.ItemModifier == null)
                            {
                                if (tempIm < 0)
                                {
                                    newIM = im;
                                    break;
                                }
                            }
                            if (!currentModifier.Equals(im))
                            {
                                if (currentModifier > tempIm)
                                {
                                    newIM = im;
                                    break;
                                }
                            }
                        }
                    }
                    if (currentModifier > 0 && newIM != null && ((newIM.ModifyArmor(100) - 100) < 0))
                    {
                        equipmentElement.SetModifier(null);
                        agent.SpawnEquipment[equipmentIndex] = equipmentElement;
                    }
                    else if (newIM != null || equipmentElement.ItemModifier == null)
                    {
                        equipmentElement.SetModifier(newIM);
                        agent.SpawnEquipment[equipmentIndex] = equipmentElement;
                    }
                    //InformationManager.DisplayMessage(new InformationMessage(agent.Name + ": " + itemType.ToString() + " " + oldItemModifier + " -> " + newIM?.StringId));
                    //InformationManager.DisplayMessage(new InformationMessage(((float)numOfDurabilityDowngrade / (float)numOfHits) + ""));
                }
            }
        }

        public static float GetSkillBasedDamage(float magnitude, bool isPassiveUsage, string weaponType, DamageTypes damageType, float effectiveSkill, float skillModifier, StrikeType strikeType, float weaponWeight)
        {
            float skillBasedDamage = 0f;
            const float ashBreakTreshold = 430f;
            const float poplarBreakTreshold = 260f;
            float BraceBonus = 0f;
            float BraceModifier = 1f; // because lances have 3 times more damage
            switch (weaponType)
            {
                case "Dagger":
                case "OneHandedSword":
                case "ThrowingKnife":
                    {
                        if (damageType == DamageTypes.Cut)
                        {
                            float value = magnitude + (effectiveSkill * 0.133f);
                            float min = 5f * (1 + skillModifier);
                            float max = 15f * (1 + (2 * skillModifier));
                            skillBasedDamage = (MBMath.ClampFloat(value, min, max) * 4.6f);
                            //skillBasedDamage = magnitude + 40f + (effectiveSkill * 0.53f);
                        }
                        else if (damageType == DamageTypes.Blunt)
                        {
                            //skillBasedDamage = magnitude + 0.50f * (40f + (effectiveSkill * 0.53f));
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.075f), 15f * (1 + skillModifier), 20f * (1 + (2 * skillModifier))) * 4f) * 0.4f;
                        }
                        else
                        {
                            if (strikeType == (int)StrikeType.Swing)
                            {
                                skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.133f), 5f * (1 + skillModifier), 15f * (1 + (2 * skillModifier))) * 4f) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                            }
                            else
                            {
                                //float weaponWeight = attacker.Equipment[attacker.GetWieldedItemIndex(HandIndex.MainHand)].GetWeight();
                                //float totalSpeed = (float)Math.Sqrt((magnitude * 2) / 8f);
                                //totalSpeed += 3f;
                                //skillBasedDamage = 0.5f * 8f * totalSpeed * totalSpeed * (1 + (skillModifier * 0.4f));
                                //if (skillBasedDamage > 170f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier)
                                //{
                                //    skillBasedDamage = 170f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                //}
                                skillBasedDamage = magnitude;
                            }
                        }
                        if (magnitude > 1f)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
                case "TwoHandedSword":
                    {
                        if (damageType == DamageTypes.Cut)
                        {
                            float value = magnitude + (effectiveSkill * 0.199f);
                            float min = 12f * (1 + skillModifier);
                            float max = 20f * (1 + (2 * skillModifier));
                            skillBasedDamage = MBMath.ClampFloat(value, min, max) * 4.6f;
                        }
                        else if (damageType == DamageTypes.Blunt)
                        {
                            //skillBasedDamage = magnitude * 1.3f + 0.5f * ((40f + (effectiveSkill * 0.53f)) * 1.3f);
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.112f), 20f * (1 + skillModifier), 26f * (1 + (2 * skillModifier))) * 4f) * 0.4f;
                        }
                        else
                        {
                            if (strikeType == (int)StrikeType.Swing)
                            {
                                skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.199f), 12f * (1 + skillModifier), 20f * (1 + (2 * skillModifier))) * 4f) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                            }
                            else
                            {
                                //float weaponWeight = attacker.Equipment[attacker.GetWieldedItemIndex(HandIndex.MainHand)].GetWeight();
                                //float totalSpeed = (float)Math.Sqrt((magnitude * 2) / 8f);
                                //skillBasedDamage = 0.5f * 15f * totalSpeed * totalSpeed * (1 + (skillModifier * 0.4f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                //if (skillBasedDamage > 240f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier)
                                //{
                                //    skillBasedDamage = 240 * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                //}
                                skillBasedDamage = magnitude;
                            }
                        }
                        if (magnitude > 1f)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
                case "OneHandedAxe":
                case "ThrowingAxe":
                    {
                        float value = magnitude + (effectiveSkill * 0.1f);
                        float min = 10f * (1 + skillModifier);
                        float max = 18f * (1 + (2 * skillModifier));
                        skillBasedDamage = (MBMath.ClampFloat(value, min, max) * 4.6f);
                        if (damageType == DamageTypes.Blunt)
                        {
                            //skillBasedDamage = magnitude + 0.5f * (60f + (effectiveSkill * 0.4f));
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.075f), 15f * (1 + skillModifier), 20f * (1 + (2 * skillModifier))) * 4f) * 0.3f;
                        }
                        if (magnitude > 1f)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
                case "OneHandedBastardAxe":
                    {
                        skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.13f), 12f * (1 + skillModifier), 20f * (1 + (2 * skillModifier))) * 4.6f);
                        if (damageType == DamageTypes.Blunt)
                        {
                            //skillBasedDamage = magnitude * 1.15f + 0.5f * ((60f + (effectiveSkill * 0.4f)) * 1.15f);
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.09375f), 20f * (1 + skillModifier), 26f * (1 + (2 * skillModifier))) * 4f) * 0.3f;
                        }
                        if (magnitude > 1f)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
                case "TwoHandedAxe":
                    {
                        float value = magnitude + (effectiveSkill * 0.15f);
                        float min = 15f * (1 + skillModifier);
                        float max = 24f * (1 + (2 * skillModifier));
                        skillBasedDamage = (MBMath.ClampFloat(value, min, max) * 4.6f);
                        if (damageType == DamageTypes.Blunt)
                        {
                            //skillBasedDamage = magnitude * 1.3f + 0.5f * ((60f + (effectiveSkill * 0.4f)) * 1.30f);
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.112f), 20f * (1 + skillModifier), 26f * (1 + (2 * skillModifier))) * 4f) * 0.3f;
                        }
                        if (magnitude > 1f)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
                case "Mace":
                    {
                        if (damageType == DamageTypes.Pierce)
                        {
                            //float totalSpeed = (float)Math.Sqrt((magnitude * 2) / 8f);
                            //totalSpeed += 3f;
                            //skillBasedDamage = 0.5f * 8f * totalSpeed * totalSpeed * (1 + (skillModifier * 0.4f));

                            //if (skillBasedDamage > 170f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier)
                            //{
                            //    skillBasedDamage = 170f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                            //}
                            skillBasedDamage = magnitude;
                        }
                        else
                        {
                            float value = magnitude + (effectiveSkill * 0.075f);
                            float min = 10f * (1 + skillModifier);
                            float max = 15f * (1 + (2 * skillModifier));
                            skillBasedDamage = (MBMath.ClampFloat(value, min, max) * 4.6f);
                            //skillBasedDamage = value;
                        }
                        if (magnitude > 1f)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
                case "TwoHandedMace":
                    {
                        if (damageType == DamageTypes.Pierce)
                        {
                            skillBasedDamage = (magnitude * 0.2f + 40f * RBMConfig.RBMConfig.ThrustMagnitudeModifier + (effectiveSkill * 0.4f * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) * 1.3f;
                        }
                        else
                        {
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.112f), 20f * (1 + skillModifier), 26f * (1 + (2 * skillModifier))) * 4.6f);
                        }
                        if (magnitude > 1f)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
                case "OneHandedPolearm":
                    {
                        if (damageType == DamageTypes.Cut)
                        {
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.1f), 15f * (1 + skillModifier), 24f * (1 + (2 * skillModifier))) * 4f);
                        }
                        else if (damageType == DamageTypes.Blunt && !isPassiveUsage)
                        {
                            //skillBasedDamage = magnitude + 30f + (effectiveSkill * 0.26f);
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.075f), 15f * (1 + skillModifier), 20f * (1 + (2 * skillModifier))) * 4f) * 0.3f;
                        }
                        else
                        {
                            if (isPassiveUsage)
                            {
                                float couchedSkill = 0.5f + effectiveSkill * 0.02f;
                                float skillCap = (150f + effectiveSkill * 1.5f);

                                if (weaponWeight < 2.1f)
                                {
                                    BraceBonus += 0.5f;
                                    BraceModifier *= 1f;
                                }
                                float lanceBalistics = (magnitude * BraceModifier) / weaponWeight;
                                float CouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus);
                                float BluntLanceBalistics = ((magnitude * BraceModifier) / weaponWeight) * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                                float BluntCouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus) * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                                magnitude = CouchedMagnitude;

                                if (damageType == DamageTypes.Blunt)
                                {
                                    magnitude = BluntCouchedMagnitude;
                                    if (BluntCouchedMagnitude > skillCap && (BluntLanceBalistics * (weaponWeight + BraceBonus)) < skillCap) //skill based damage
                                    {
                                        magnitude = skillCap;
                                    }

                                    if ((BluntLanceBalistics * (weaponWeight + BraceBonus)) >= skillCap) //ballistics
                                    {
                                        magnitude = (BluntLanceBalistics * (weaponWeight + BraceBonus));
                                    }

                                    if (magnitude > poplarBreakTreshold) // damage cap - lance break threshold
                                    {
                                        magnitude = poplarBreakTreshold;
                                    }
                                    magnitude *= 1f;
                                }
                                else
                                {
                                    if (CouchedMagnitude > (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier) && (lanceBalistics * (weaponWeight + BraceBonus)) < (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) //skill based damage
                                    {
                                        magnitude = skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                    }

                                    if ((lanceBalistics * (weaponWeight + BraceBonus)) >= (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) //ballistics
                                    {
                                        magnitude = (lanceBalistics * (weaponWeight + BraceBonus));
                                    }

                                    if (magnitude > (ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) // damage cap - lance break threshold
                                    {
                                        magnitude = ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                    }
                                }
                            }
                            else
                            {
                                float totalSpeed = (float)Math.Sqrt((magnitude * 2f) / 8f);
                                //totalSpeed += 3f;
                                skillBasedDamage = magnitude;

                                //skillBasedDamage = magnitude * 0.4f + 60f * RBMConfig.RBMConfig.ThrustMagnitudeModifier + (effectiveSkill * 0.26f * RBMConfig.RBMConfig.ThrustMagnitudeModifier);
                                //if (skillBasedDamage > 170f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier)

                                //{
                                //    skillBasedDamage = 170f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                //}
                            }
                        }
                        if (magnitude > 0.15f && !isPassiveUsage)
                        {
                            magnitude = skillBasedDamage;
                        }
                        //else if(magnitude > 0f && magnitude <= 0.15f)
                        //{
                        //    InformationManager.DisplayMessage(new InformationMessage("DEBUG WARNING: strike bagnitude below treshlod"));
                        //}
                        break;
                    }
                case "TwoHandedPolearm":
                    {
                        if (damageType == DamageTypes.Cut)
                        {
                            float value = magnitude + (effectiveSkill * 0.1495f);
                            float min = 18f * (1 + skillModifier);
                            float max = 28f * (1 + (2 * skillModifier));
                            skillBasedDamage = (MBMath.ClampFloat(value, min, max) * 4f);
                        }
                        else if (damageType == DamageTypes.Blunt && !isPassiveUsage)
                        {
                            //skillBasedDamage = magnitude + (30f + (effectiveSkill * 0.26f) * 1.3f);
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.0975f), 20f * (1 + skillModifier), 26f * (1 + (2 * skillModifier))) * 4f) * 0.3f;
                        }
                        else
                        {
                            if (isPassiveUsage)
                            {
                                float couchedSkill = 0.5f + effectiveSkill * 0.02f;
                                float skillCap = (150f + effectiveSkill * 1.5f);

                                if (weaponWeight < 2.1f)
                                {
                                    BraceBonus += 0.5f;
                                    BraceModifier *= 1f;
                                }
                                float lanceBalistics = (magnitude * BraceModifier) / weaponWeight;
                                float CouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus);
                                float BluntLanceBalistics = ((magnitude * BraceModifier) / weaponWeight) * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                                float BluntCouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus) * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                                magnitude = CouchedMagnitude;

                                if (damageType == DamageTypes.Blunt)
                                {
                                    magnitude = BluntCouchedMagnitude;
                                    if (BluntCouchedMagnitude > skillCap && (BluntLanceBalistics * (weaponWeight + BraceBonus)) < skillCap) //skill based damage
                                    {
                                        magnitude = skillCap;
                                    }

                                    if ((BluntLanceBalistics * (weaponWeight + BraceBonus)) >= skillCap) //ballistics
                                    {
                                        magnitude = (BluntLanceBalistics * (weaponWeight + BraceBonus));
                                    }

                                    if (magnitude > poplarBreakTreshold) // damage cap - lance break threshold
                                    {
                                        magnitude = poplarBreakTreshold;
                                    }
                                    magnitude *= 1f;
                                }
                                else
                                {
                                    if (CouchedMagnitude > (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier) && (lanceBalistics * (weaponWeight + BraceBonus)) < (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) //skill based damage
                                    {
                                        magnitude = skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                    }

                                    if ((lanceBalistics * (weaponWeight + BraceBonus)) >= (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) //ballistics
                                    {
                                        magnitude = (lanceBalistics * (weaponWeight + BraceBonus));
                                    }

                                    if (magnitude > (ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) // damage cap - lance break threshold
                                    {
                                        magnitude = ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                    }
                                }
                            }
                            else
                            {
                                //float weaponWeight = attacker.Equipment[attacker.GetWieldedItemIndex(HandIndex.MainHand)].GetWeight();
                                //float totalSpeed = (float)Math.Sqrt((magnitude * 2f) / 8f);
                                //skillBasedDamage = 0.5f * 15f * totalSpeed * totalSpeed * (1 + (skillModifier * 0.4f));
                                ////skillBasedDamage = (magnitude * 0.4f + 60f * RBMConfig.RBMConfig.ThrustMagnitudeModifier + (effectiveSkill * 0.26f * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) * 1.3f;

                                //if (skillBasedDamage > 240f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier)
                                //{
                                //    skillBasedDamage = 240 * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                //}
                                skillBasedDamage = magnitude;
                            }
                        }
                        if (magnitude > 0.15f && !isPassiveUsage)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
            }
            return magnitude;
        }

        public static float RBMComputeDamage(string weaponType, DamageTypes damageType, float magnitude, float armorEffectiveness, float absorbedDamageRatio, out float penetratedDamage, out float bluntTraumaAfterArmor, float weaponDamageFactor = 1f, BasicCharacterObject player = null, bool isPlayerVictim = false)
        {
            float damage = 0f;
            float armorReduction = 100f / (100f + armorEffectiveness * RBMConfig.RBMConfig.armorMultiplier);
            float mag_1h_thrust;
            float mag_2h_thrust;
            float mag_1h_sword_thrust;
            float mag_2h_sword_thrust;

            if (damageType == DamageTypes.Pierce)
            {
                mag_1h_thrust = magnitude * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                mag_2h_thrust = magnitude * 1f * RBMConfig.RBMConfig.TwoHandedThrustDamageBonus;
                mag_1h_sword_thrust = magnitude * 1.0f * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                mag_2h_sword_thrust = magnitude * 1f * RBMConfig.RBMConfig.TwoHandedThrustDamageBonus;
            }
            else if (damageType == DamageTypes.Cut)
            {
                mag_1h_thrust = magnitude;
                mag_2h_thrust = magnitude;
                mag_1h_sword_thrust = magnitude * 1.0f;
                mag_2h_sword_thrust = magnitude * 1.00f;
            }
            else
            {
                mag_1h_thrust = magnitude;
                mag_2h_thrust = magnitude;
                mag_1h_sword_thrust = magnitude;
                mag_2h_sword_thrust = magnitude;
            }

            switch (weaponType)
            {
                case "Dagger":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_sword_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "ThrowingKnife":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_sword_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "OneHandedSword":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_sword_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "TwoHandedSword":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_2h_sword_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "OneHandedAxe":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "OneHandedBastardAxe":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "TwoHandedAxe":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "OneHandedPolearm":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "TwoHandedPolearm":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_2h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "Mace":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor, 0f);
                        break;
                    }
                case "TwoHandedMace":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_2h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "Arrow":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor, 0f);
                        break;
                    }
                case "Bolt":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor, 0f);
                        break;
                    }
                case "Javelin":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "ThrowingAxe":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                default:
                    {
                        //InformationManager.DisplayMessage(new InformationMessage("POZOR DEFAULT !!!!"));
                        RBMCombatConfigWeaponType defaultwct = new RBMCombatConfigWeaponType("default", 1f, 1f, 1f, 1f, 1f, 1f);
                        damage = WeaponTypeDamage(defaultwct, magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
            }
            return damage * absorbedDamageRatio;
        }

        private static float WeaponTypeDamage(RBMCombatConfigWeaponType weaponTypeFactors, float magnitude, float armorReduction, DamageTypes damageType, float armorEffectiveness, BasicCharacterObject player, bool isPlayerVictim, float weaponDamageFactor, out float penetratedDamage, out float bluntTraumaAfterArmor, float partialPenetrationThreshold = 2f)
        {
            float damage = 0f;
            float armorThresholdModifier = RBMConfig.RBMConfig.armorThresholdModifier / weaponDamageFactor;
            switch (damageType)
            {
                case DamageTypes.Blunt:
                    {
                        //float armorReductionBlunt = 100f / ((100f + armorEffectiveness) * RBMConfig.RBMConfig.dict["Global.ArmorMultiplier"]);
                        //damage += magnitude * armorReductionBlunt * RBMConfig.RBMConfig.dict["Global.MaceBluntModifier"];

                        penetratedDamage = Math.Max(0f, magnitude - armorEffectiveness * 5f * armorThresholdModifier);
                        float bluntFraction = 0f;
                        if (magnitude > 0f)
                        {
                            bluntFraction = (magnitude - penetratedDamage) / magnitude;
                        }
                        damage += penetratedDamage;

                        float bluntTrauma = magnitude * (0.7f * RBMConfig.RBMConfig.maceBluntModifier) * bluntFraction;
                        bluntTraumaAfterArmor = Math.Max(0f, bluntTrauma * armorReduction);
                        damage += bluntTraumaAfterArmor;

                        break;
                    }
                case DamageTypes.Cut:
                    {
                        penetratedDamage = Math.Max(0f, magnitude - armorEffectiveness * weaponTypeFactors.ExtraArmorThresholdFactorCut * armorThresholdModifier);
                        float bluntFraction = 0f;
                        if (magnitude > 0f)
                        {
                            bluntFraction = (magnitude - penetratedDamage) / magnitude;
                        }
                        damage += penetratedDamage;

                        float bluntTrauma = magnitude * (weaponTypeFactors.ExtraBluntFactorCut + RBMConfig.RBMConfig.bluntTraumaBonus) * bluntFraction;
                        bluntTraumaAfterArmor = Math.Max(0f, bluntTrauma * armorReduction);
                        damage += bluntTraumaAfterArmor;

                        if (RBMConfig.RBMConfig.armorPenetrationMessage)
                        {
                            if (player != null)
                            {
                                if (isPlayerVictim)
                                {
                                    //InformationManager.DisplayMessage(new InformationMessage("You received"));
                                    InformationManager.DisplayMessage(new InformationMessage("You received " + (int)(bluntTraumaAfterArmor) +
                                        " blunt trauma, " + (int)(penetratedDamage) + " armor penetration damage"));
                                    //InformationManager.DisplayMessage(new InformationMessage("damage penetrated: " + penetratedDamage));
                                }
                                else
                                {
                                    InformationManager.DisplayMessage(new InformationMessage("You dealt " + (int)(bluntTraumaAfterArmor) +
                                        " blunt trauma, " + (int)(penetratedDamage) + " armor penetration damage"));
                                }
                            }
                        }
                        break;
                    }
                case DamageTypes.Pierce:
                    {
                        float partialPenetration = Math.Max(0f, magnitude - armorEffectiveness * partialPenetrationThreshold * armorThresholdModifier);
                        if (partialPenetration > 15f)
                        {
                            partialPenetration = 15f;
                        }
                        penetratedDamage = Math.Max(0f, magnitude - armorEffectiveness * weaponTypeFactors.ExtraArmorThresholdFactorPierce * armorThresholdModifier) - partialPenetration;
                        float bluntFraction = 0f;
                        if (magnitude > 0f)
                        {
                            bluntFraction = (magnitude - (penetratedDamage + partialPenetration)) / magnitude;
                        }
                        penetratedDamage += partialPenetration;
                        damage += penetratedDamage;

                        float bluntTrauma = magnitude * (weaponTypeFactors.ExtraBluntFactorPierce + RBMConfig.RBMConfig.bluntTraumaBonus) * bluntFraction;
                        bluntTraumaAfterArmor = Math.Max(0f, bluntTrauma * armorReduction);
                        damage += bluntTraumaAfterArmor;

                        if (RBMConfig.RBMConfig.armorPenetrationMessage)
                        {
                            if (player != null)
                            {
                                if (isPlayerVictim)
                                {
                                    //InformationManager.DisplayMessage(new InformationMessage("You received"));
                                    InformationManager.DisplayMessage(new InformationMessage("You received " + (int)(bluntTraumaAfterArmor) +
                                        " blunt trauma, " + (int)(penetratedDamage) + " armor penetration damage"));
                                    //InformationManager.DisplayMessage(new InformationMessage("damage penetrated: " + penetratedDamage));
                                }
                                else
                                {
                                    InformationManager.DisplayMessage(new InformationMessage("You dealt " + (int)(bluntTraumaAfterArmor) +
                                        " blunt trauma, " + (int)(penetratedDamage) + " armor penetration damage"));
                                }
                            }
                        }
                        break;
                    }
                default:
                    {
                        penetratedDamage = 0f;
                        bluntTraumaAfterArmor = 0f;
                        damage = 0f;
                        break;
                    }
            }
            return damage;
        }

        public const float oneHandedPolearmThrustStrength = 2.5f;
        public const float twoHandedPolearmThrustStrength = 5f;

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

        public static void CalculateVisualSpeeds(MissionWeapon weapon, WeaponData weaponData, WeaponClass weaponClass, float effectiveSkillDR, out int swingSpeedReal, out int thrustSpeedReal, out int handlingReal)
        {
            swingSpeedReal = -1;
            thrustSpeedReal = -1;
            handlingReal = -1;
            if (!weapon.IsEmpty && weapon.Item != null)
            {
                int swingSpeed = weapon.GetModifiedSwingSpeedForCurrentUsage();
                int handling = weapon.GetModifiedHandlingForCurrentUsage();

                switch (weaponClass)
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
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.Item.Weight, weaponData.Inertia, weaponData.CenterOfMass) * Utilities.thrustSpeedTransfer);
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
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.Item.Weight, weaponData.Inertia, weaponData.CenterOfMass) * Utilities.thrustSpeedTransfer);
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
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.Item.Weight, weaponData.Inertia, weaponData.CenterOfMass) * Utilities.thrustSpeedTransfer);
                            thrustSpeedReal = MathF.Ceiling((thrustSpeedReal * 0.9f) * thrustskillModifier);
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

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.9f) * swingskillModifier);
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.Item.Weight, weaponData.Inertia, weaponData.CenterOfMass) * Utilities.thrustSpeedTransfer);
                            thrustSpeedReal = MathF.Ceiling((thrustSpeedReal * 1.15f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 0.9f) * handlingskillModifier);
                            break;
                        }
                }
            }
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
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.Weight, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).Inertia, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass) * Utilities.thrustSpeedTransfer);
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
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.Weight, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).Inertia, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass) * Utilities.thrustSpeedTransfer);
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
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.Weight, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).Inertia, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass) * Utilities.thrustSpeedTransfer);
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

        public static string GetConfigFilePath()
        {
            return System.IO.Path.Combine(GetConfigFolderPath(), "config.xml");
        }

        public static string GetConfigFolderPath()
        {
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal),
             "Mount and Blade II Bannerlord", "Configs", "RealisticBattleCombatModule");
        }
    }
}