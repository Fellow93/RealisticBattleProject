using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace  RBMCombat
{
    public static class Utilities
    {
        public static int numOfHits = 0;
        public static int numOfDurabilityDowngrade = 0;
        public static float throwableCorrectionSpeed = 5f;
        public static bool HitWithWeaponBlade(in AttackCollisionData collisionData, in MissionWeapon attackerWeapon)
        {
            WeaponComponentData currentUsageItem = attackerWeapon.CurrentUsageItem;
            if (attackerWeapon.Item != null && currentUsageItem != null && attackerWeapon.Item.WeaponDesign != null &&
                attackerWeapon.Item.WeaponDesign.UsedPieces != null && attackerWeapon.Item.WeaponDesign.UsedPieces.Length > 0 )
            {
                //WeaponClass weaponClass = attackerWeapon.CurrentUsageItem.WeaponClass;
                if (collisionData.CollisionDistanceOnWeapon < currentUsageItem.GetRealWeaponLength() - attackerWeapon.Item.WeaponDesign.UsedPieces[0].ScaledBladeLength)
                {
                    return false;
                }
                return true;
            }
            return true;
        }

        public static float CalculateSkillModifier(int relevantSkillLevel)
        {
            return MBMath.ClampFloat((float)relevantSkillLevel / 250f, 0f, 1f);
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

        public static int calculateMissileSpeed(float ammoWeight, MissionWeapon rangedWeapon, int drawWeight)
        {
            int calculatedMissileSpeed = 10;
            switch (rangedWeapon.CurrentUsageItem.ItemUsage)
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

        public static int calculateThrowableSpeed(float ammoWeight, int effectiveSkill)
        {
            int calculatedThrowingSpeed = (int)Math.Ceiling(Math.Sqrt((60f + effectiveSkill * 0.8f) * 2f / ammoWeight));
            if (calculatedThrowingSpeed > 25)
            {
                calculatedThrowingSpeed = 25;
            }
            return calculatedThrowingSpeed;
        }

        public static int assignThrowableMissileSpeed(MissionWeapon throwable, int correctiveMissileSpeed, int effectiveSkill)
        {
            float ammoWeight = throwable.GetWeight() / throwable.Amount;
            int calculatedThrowingSpeed = calculateThrowableSpeed(ammoWeight,effectiveSkill);
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

        static public void initiateCheckForArmor(ref Agent victim, AttackCollisionData attackCollisionData)
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
            if(equipmentIndex != EquipmentIndex.None && itemType != ItemObject.ItemTypeEnum.Invalid)
            {
                lowerArmorQualityCheck(ref victim, equipmentIndex, itemType);
            }
        }

        static public void lowerArmorQualityCheck(ref Agent agent, EquipmentIndex equipmentIndex, ItemObject.ItemTypeEnum itemType)
        {
            //InformationManager.DisplayMessage(new InformationMessage( (float)numOfHits + ""));
            EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
            //numOfHits++;
            if (equipmentElement.Item != null && equipmentElement.Item.ItemType == itemType)
            {
                float probability = 0.1f;
                //if (equipmentElement.ItemModifier != null)
                //{
                //    probability = equipmentElement.ItemModifier.LootDropScore / 100f;
                //}
                float randomF = MBRandom.RandomFloat;
                if (randomF <= probability)
                {
                    //numOfDurabilityDowngrade++;
                    lowerArmorQuality(ref agent, equipmentIndex, itemType);
                }
            }
        }

        static public void lowerArmorQuality(ref Agent agent, EquipmentIndex equipmentIndex, ItemObject.ItemTypeEnum itemType)
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

