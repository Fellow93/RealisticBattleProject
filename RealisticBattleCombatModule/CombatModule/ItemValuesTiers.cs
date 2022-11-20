using HarmonyLib;
using System;
using TaleWorlds.Core;

namespace RBMCombat
{
    class ItemValuesTiers
    {
        [HarmonyPatch(typeof(DefaultItemValueModel))]
        [HarmonyPatch("CalculateValue")]
        class OverrideCalculateValue
        {
            static bool Prefix(ref DefaultItemValueModel __instance, ItemObject item, ref int __result)
            {
                float price = 1;
                float tier = 1f;

                if (item.ItemComponent != null)
                {
                    tier = __instance.GetEquipmentValueFromTier(item.Tierf);
                }

                float materialPriceModifier = 1f;
                if (item.ArmorComponent != null)
                {
                    switch (item.ArmorComponent.MaterialType)
                    {
                        case ArmorComponent.ArmorMaterialTypes.Cloth:
                            {
                                materialPriceModifier = 20f;
                                break;
                            }
                        case ArmorComponent.ArmorMaterialTypes.Leather:
                            {
                                materialPriceModifier = 50f;
                                break;
                            }
                        case ArmorComponent.ArmorMaterialTypes.Chainmail:
                            {
                                materialPriceModifier = 100f;
                                break;
                            }
                        case ArmorComponent.ArmorMaterialTypes.Plate:
                            {
                                materialPriceModifier = 120f;
                                break;
                            }
                        default:
                            {
                                materialPriceModifier = 50f;
                                break;
                            }
                    }

                    if (item.ItemType == ItemObject.ItemTypeEnum.LegArmor)
                    {
                        price = 75f + (item.ArmorComponent.LegArmor * materialPriceModifier);
                    }
                    else if (item.ItemType == ItemObject.ItemTypeEnum.HandArmor)
                    {
                        price = 50f + (item.ArmorComponent.ArmArmor * materialPriceModifier * 0.8f);
                    }
                    else if (item.ItemType == ItemObject.ItemTypeEnum.HeadArmor)
                    {
                        price = 100f + ((item.ArmorComponent.HeadArmor * materialPriceModifier * 1.2f) + (item.ArmorComponent.BodyArmor * materialPriceModifier * 0.6f));
                    }
                    else if (item.ItemType == ItemObject.ItemTypeEnum.Cape)
                    {
                        price = 50f + ((item.ArmorComponent.BodyArmor * materialPriceModifier * 0.8f) + (item.ArmorComponent.ArmArmor * materialPriceModifier * 0.8f));
                    }
                    else if (item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                    {
                        price = 200f + ((item.ArmorComponent.BodyArmor * materialPriceModifier * 2.5f) + (item.ArmorComponent.LegArmor * materialPriceModifier) + (item.ArmorComponent.ArmArmor * materialPriceModifier * 0.8f));
                    }
                    else if (item.ItemType == ItemObject.ItemTypeEnum.HorseHarness)
                    {
                        price = 100f + ((item.ArmorComponent.BodyArmor * 0.2f) + (item.ArmorComponent.ArmArmor * 0.2f) + (item.ArmorComponent.LegArmor * 0.4f) + (item.ArmorComponent.HeadArmor * 0.2f) * 450f);
                    }
                    price *= RBMConfig.RBMConfig.priceMultipliers.ArmorPriceModifier;
                }
                else if (item.ItemComponent is WeaponComponent)
                {
                    price = (500f + (tier * 100f)) * RBMConfig.RBMConfig.priceMultipliers.WeaponPriceModifier;
                    if (item.ItemType == ItemObject.ItemTypeEnum.Polearm)
                    {
                        price *= 0.4f;
                    }
                    if (item.ItemType == ItemObject.ItemTypeEnum.Thrown)
                    {
                        price *= 0.3f;
                    }
                    if (item.ItemType == ItemObject.ItemTypeEnum.TwoHandedWeapon)
                    {
                        price *= 1.5f;
                    }
                    if (item.ItemType == ItemObject.ItemTypeEnum.Shield)
                    {
                        price *= 0.5f;
                    }
                    if (item.ItemType == ItemObject.ItemTypeEnum.Arrows || item.ItemType == ItemObject.ItemTypeEnum.Bolts)
                    {
                        price = (50f + (tier * 10f)) * RBMConfig.RBMConfig.priceMultipliers.WeaponPriceModifier;
                    }
                }
                else if (item.ItemComponent is HorseComponent)
                {
                    price = 200f * tier * RBMConfig.RBMConfig.priceMultipliers.HorsePriceModifier * (1f + 0.2f * (item.Appearance - 1f)) + 100f * Math.Max(0f, item.Appearance - 1f);
                }
                else if (item.ItemComponent is TradeItemComponent)
                {
                    price = 100f * tier * RBMConfig.RBMConfig.priceMultipliers.TradePriceModifier * (1f + 0.2f * (item.Appearance - 1f)) + 100f * Math.Max(0f, item.Appearance - 1f);
                }
                else
                {
                    price = 1;
                }

                __result = (int)price;
                return false;
            }
        }

        [HarmonyPatch(typeof(DefaultItemValueModel))]
        [HarmonyPatch("CalculateHorseTier")]
        class OverrideCalculateHorseTier
        {
            static bool Prefix(ref DefaultItemValueModel __instance, HorseComponent horseComponent, ref float __result)
            {
                float tier = 0f;
                if (horseComponent.IsPackAnimal)
                {
                    tier = 1f;
                }
                else
                {
                    tier += 0.009f * (float)horseComponent.HitPointBonus;
                    tier += 0.030f * (float)horseComponent.Maneuver;
                    tier += 0.030f * (float)horseComponent.Speed;
                }
                //tier += 1.5f * (float)horseComponent.ChargeDamage;
                //tier = (tier / 13f) - 8f;
                __result = tier;
                return false;
            }
        }

        [HarmonyPatch(typeof(DefaultItemValueModel))]
        [HarmonyPatch("CalculateTierMeleeWeapon")]
        class OverrideCalculateTierMeleeWeapon
        {
            private static float GetFactor(DamageTypes swingDamageType)
            {
                switch (swingDamageType)
                {
                    default:
                        return 1f;
                    case DamageTypes.Pierce:
                        return 1.15f;
                    case DamageTypes.Blunt:
                        return 2.0f;
                }
            }

            static bool Prefix(ref DefaultItemValueModel __instance, WeaponComponent weaponComponent, ref float __result)
            {
                WeaponComponentData weaponComponentData = weaponComponent.Weapons[0];
                float val = ((float)weaponComponentData.ThrustDamage * RBMConfig.RBMConfig.OneHandedThrustDamageBonus - 75f) * 0.1f * GetFactor(weaponComponentData.ThrustDamageType) * ((float)weaponComponentData.ThrustSpeed * 0.01f);
                float num = ((float)weaponComponentData.SwingDamage) * 0.2f * GetFactor(weaponComponentData.SwingDamageType) * ((float)weaponComponentData.SwingSpeed * 0.01f);
                float maceTier = ((float)weaponComponentData.SwingDamage - 3f) * 0.23f * ((float)weaponComponentData.SwingSpeed * 0.01f);
                if (val < 0f)
                {
                    val = 0f;
                }
                if (num < 0f)
                {
                    num = 0f;
                }
                if (maceTier < 0f)
                {
                    maceTier = 0f;
                }

                float num2 = 0f;
                switch (weaponComponentData.WeaponClass)
                {
                    case WeaponClass.OneHandedSword:
                    case WeaponClass.Dagger:
                    case WeaponClass.ThrowingKnife:
                        {
                            num2 = (val + num) * 0.5f;
                            break;
                        }
                    case WeaponClass.TwoHandedSword:
                        {
                            num2 = (val + num) * 0.5f / 1.3f;
                            break;
                        }
                    case WeaponClass.TwoHandedPolearm:
                    case WeaponClass.LowGripPolearm:
                        {
                            num2 = val + (num * 0.5f);
                            break;
                        }
                    case WeaponClass.TwoHandedAxe:
                        {
                            num2 = num / 1.3f;
                            break;
                        }
                    case WeaponClass.OneHandedAxe:
                    case WeaponClass.Pick:
                        {
                            num2 = num * (float)weaponComponentData.WeaponLength * 0.014f;
                            break;
                        }
                    case WeaponClass.TwoHandedMace:
                        {
                            num2 = maceTier * (float)weaponComponentData.WeaponLength * 0.014f / 1.3f;
                            break;
                        }
                    case WeaponClass.Mace:
                        {
                            num2 = maceTier * (float)weaponComponentData.WeaponLength * 0.014f;
                            break;
                        }
                    case WeaponClass.ThrowingAxe:
                        {
                            num2 = (float)weaponComponentData.SwingDamage * 0.05f;
                            break;
                        }
                    case WeaponClass.Javelin:
                        {
                            num2 = ((float)weaponComponentData.ThrustDamage * RBMConfig.RBMConfig.OneHandedThrustDamageBonus - 60f) * 0.1f; //XmlConfig.ThrustModifier;
                            break;
                        }
                    case WeaponClass.OneHandedPolearm:
                        {
                            num2 = val + (num * 0.5f);
                            break;
                        }
                    default:
                        {
                            num2 = (val + num) * 0.5f;
                            break;
                        }
                }
                if (num2 < 0f)
                {
                    num2 = 0f;
                }
                if (num2 > 6.5f)
                {
                    num2 = 6.5f;
                }
                __result = num2;
                return false;
            }
        }

        [HarmonyPatch(typeof(DefaultItemValueModel))]
        [HarmonyPatch("CalculateAmmoTier")]
        class CalculateAmmoTierPatch
        {
            static bool Prefix(ref DefaultItemValueModel __instance, WeaponComponent weaponComponent, ref float __result)
            {
                WeaponComponentData weaponComponentData = weaponComponent.Weapons[0];
                float missileWeight = weaponComponent.Item.Weight;
                float missileDamage = weaponComponentData.MissileDamage;
                float arrowTier = (missileDamage * 0.01f) * (((missileWeight * 100f - 4f) + 0.01f) * 0.8f);
                if (arrowTier > 6.5f)
                { arrowTier = 6.5f; }
                __result = arrowTier;
                return false;
            }
        }

        [HarmonyPatch(typeof(DefaultItemValueModel))]
        [HarmonyPatch("CalculateShieldTier")]
        class CalculateShieldTierPatch
        {
            static bool Prefix(ref DefaultItemValueModel __instance, WeaponComponent weaponComponent, ref float __result)
            {
                WeaponComponentData weaponComponentData = weaponComponent.Weapons[0];
                //weaponComponentData.MaxDataValue - hitpointy stitov
                float hp = (float)weaponComponentData.MaxDataValue;
                float bodyArmor = (float)weaponComponentData.BodyArmor;
                float weaponLength = (float)weaponComponentData.WeaponLength;
                float shieldTier = ((hp - 400f) * 0.005f + bodyArmor * 0.2f) * (weaponLength / 60f);
                shieldTier += 1f;
                if (shieldTier > 6.5f)
                { shieldTier = 6.5f; }
                __result = shieldTier;
                return false;
            }
        }

        [HarmonyPatch(typeof(DefaultItemValueModel))]
        [HarmonyPatch("CalculateRangedWeaponTier")]
        class OverrideCalculateRangedWeaponTier
        {
            static bool Prefix(ref DefaultItemValueModel __instance, WeaponComponent weaponComponent, ref float __result)
            {
                WeaponComponentData weaponComponentData = weaponComponent.Weapons[0];
                //float num;
                float RangedTier;
                float DrawWeight = (float)weaponComponentData.MissileSpeed * 1f;
                switch (weaponComponent.Item?.ItemType ?? ItemObject.ItemTypeEnum.Invalid)
                {
                    default:
                        RangedTier = (DrawWeight - 60f) * 0.049f;
                        break;
                    case ItemObject.ItemTypeEnum.Crossbow:
                        RangedTier = (DrawWeight - 250f) * 0.021f;
                        break;
                }
                //num = RangedTier;
                __result = RangedTier;
                return false;
            }
        }

        //[HarmonyPatch(typeof(DefaultItemValueModel))]
        //[HarmonyPatch("CalculateAmmoTier")]
        //class OverrideCalculateAmmoTier
        //{


        //    static bool Prefix(ref DefaultItemValueModel __instance, WeaponComponent weaponComponent, ref float __result)
        //    {
        //        WeaponComponentData weaponComponentData = weaponComponent.Weapons[0];
        //        //float num;
        //        float ArrowTier;
        //        float ArrowWeight = (float)weaponComponentData.MissileSpeed * 1f;

        //        ArrowTier = (ArrowWeight - 40f) * 0.066f;
        //        //num = ArrowTier;
        //        __result = ArrowTier;
        //        return false;
        //    }
        //}

        [HarmonyPatch(typeof(DefaultItemValueModel))]
        [HarmonyPatch("CalculateArmorTier")]
        class OverrideCalculateArmorTier
        {
            static bool Prefix(ref DefaultItemValueModel __instance, ArmorComponent armorComponent, ref float __result)
            {
                float ArmorTier = 0f;
                if (armorComponent.Item.ItemType == ItemObject.ItemTypeEnum.LegArmor)
                {
                    ArmorTier = (float)armorComponent.LegArmor * 0.10f - 1f;
                }
                else if (armorComponent.Item.ItemType == ItemObject.ItemTypeEnum.HandArmor)
                {
                    ArmorTier = (float)armorComponent.ArmArmor * 0.10f - 1f;
                }
                else if (armorComponent.Item.ItemType == ItemObject.ItemTypeEnum.HeadArmor)
                {
                    ArmorTier = (float)armorComponent.HeadArmor * 0.06f - 1f;
                }
                else if (armorComponent.Item.ItemType == ItemObject.ItemTypeEnum.Cape)
                {
                    ArmorTier = ((float)armorComponent.BodyArmor + (float)armorComponent.ArmArmor) * 0.15f - 1f;
                }
                else if (armorComponent.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                {
                    ArmorTier = ((float)armorComponent.BodyArmor * 0.05f) + ((float)armorComponent.LegArmor * 0.035f) + ((float)armorComponent.ArmArmor * 0.025f) - 1f;
                }
                else if (armorComponent.Item.ItemType == ItemObject.ItemTypeEnum.HorseHarness)
                {
                    ArmorTier = ((float)armorComponent.BodyArmor * 0.02f + (float)armorComponent.LegArmor * 0.04f + (float)armorComponent.ArmArmor * 0.02f + (float)armorComponent.HeadArmor * 0.02f) - 1f;
                }
                if (ArmorTier < 0f)
                {
                    ArmorTier = 0f;
                }
                __result = ArmorTier;
                return false;
            }
        }
    }
}
