using RBMConfig;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
            // A bash with real damage (kick/bash feature) is a shove like a punch or kick, which never wear armor.
            if (RBMConfig.RBMConfig.aiKickBashEnabled && attackCollisionData.IsAlternativeAttack)
            {
                return;
            }
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == itemType && equipmentElement.Item.ArmorComponent != null && !attackerWeapon.IsEmpty && blow.InflictedDamage > 1 && !blow.IsFallDamage)
            {
                WeaponClass weaponType = attackerWeapon.CurrentUsageItem.WeaponClass;

                float weaponTypeScaling = 1f;
                RBMCombatConfigWeaponType rbmCombatConfigWeaponType = RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType.ToString());
                float armorThreshold = 4f;
                float armorValue = ArmorRework.GetBaseArmorEffectivenessForBodyPartRBM(agent, attackCollisionData.VictimHitBodyPart);

                ArmorMaterialTypes armorMaterialType = equipmentElement.Item.ArmorComponent.MaterialType;
                DamageTypes damageType = (DamageTypes)attackCollisionData.DamageType;

                // the wear roll reads only blow.AbsorbedByArmor, which the live damage pass already computed with the
                // weapon's skill, swing and modifier factors (and the handle-hit rules), so none are recomputed here
                // attacker is null for a missile whose shooter left the mission mid-flight
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
                            if (rbmCombatConfigWeaponType != null)
                            {
                                armorThreshold = rbmCombatConfigWeaponType.ExtraArmorThresholdFactorBlunt;
                            }
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
                // The blow math scaled the armor by armorEffectivenessMultiplier, so AbsorbedByArmor grows with it;
                // scale the capacity it is measured against the same way, or stronger armor would wear faster.
                float magScaling = (blow.AbsorbedByArmor / (armorValue * RBMConfig.RBMConfig.armorEffectivenessMultiplier * armorThreshold)) / 5f;
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

        // Spawn equipment sets this mechanic has made private to an agent. Armor wear is meant to last one
        // mission: Mission.SpawnAgent clones the character's equipment, so writing into SpawnEquipment is
        // throwaway. But native code later hands some agents a hero's LIVE set through
        // UpdateSpawnEquipmentAndRefreshVisuals (closing the inventory mid-mission, hideout/prison-break
        // stealth gear, the War Sails storyline battles), and wearing that down in place left the hero's
        // saved gear Scratched for good. Weak keys, so nothing outlives the mission.
        private static readonly ConditionalWeakTable<Equipment, object> missionOwnedSpawnEquipment = new ConditionalWeakTable<Equipment, object>();
        private static readonly object missionOwnedMarker = new object();

        private static Equipment GetMissionOwnedSpawnEquipment(Agent agent)
        {
            Equipment equipment = agent.SpawnEquipment;
            if (!missionOwnedSpawnEquipment.TryGetValue(equipment, out _))
            {
                equipment = equipment.Clone();
                missionOwnedSpawnEquipment.Add(equipment, missionOwnedMarker);
                agent.InitializeSpawnEquipment(equipment);
            }
            return equipment;
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
                        GetMissionOwnedSpawnEquipment(agent)[equipmentIndex] = equipmentElement;
                    }
                    else if (newIM != null || equipmentElement.ItemModifier == null)
                    {
                        equipmentElement.SetModifier(newIM);
                        GetMissionOwnedSpawnEquipment(agent)[equipmentIndex] = equipmentElement;
                    }
                    //InformationManager.DisplayMessage(new InformationMessage(agent.Name + ": " + itemType.ToString() + " " + oldItemModifier + " -> " + newIM?.StringId));
                    //InformationManager.DisplayMessage(new InformationMessage(((float)numOfDurabilityDowngrade / (float)numOfHits) + ""));
                }
            }
        }
    }
}
