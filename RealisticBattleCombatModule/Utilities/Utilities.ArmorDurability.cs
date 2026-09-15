using RBMConfig;
using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ArmorComponent;

namespace RBMCombat
{
    public static partial class Utilities
    {
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
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == itemType && equipmentElement.Item.ArmorComponent != null && !attackerWeapon.IsEmpty && blow.InflictedDamage > 1 && !blow.IsFallDamage)
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
                    EquipmentIndex slotIndex = attacker.GetPrimaryWieldedItemIndex();
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
                                if (attacker != null && attacker.Equipment != null && attacker.GetPrimaryWieldedItemIndex() != EquipmentIndex.None)
                                {
                                    itemModifier = attacker.Equipment[attacker.GetPrimaryWieldedItemIndex()].ItemModifier;
                                    magnitude = Utilities.GetSkillBasedDamage(blow.BaseMagnitude, attacker.IsDoingPassiveAttack, weaponType.ToString(), damageType, effectiveSkill, skillModifier, (StrikeType)attackCollisionData.StrikeType, attacker.Equipment[attacker.GetPrimaryWieldedItemIndex()].GetWeight());
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
                            if (rbmCombatConfigWeaponType != null)
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
    }
}
