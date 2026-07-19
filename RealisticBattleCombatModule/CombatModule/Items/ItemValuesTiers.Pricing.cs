using HarmonyLib;
using System;
using TaleWorlds.Core;

namespace RBMCombat
{
    internal partial class ItemValuesTiers
    {
        [HarmonyPatch(typeof(DefaultItemValueModel))]
        [HarmonyPatch("CalculateValue")]
        private class OverrideCalculateValue
        {
            private static bool Prefix(ref DefaultItemValueModel __instance, ItemObject item, ref int __result)
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
                                materialPriceModifier = 5f;
                                break;
                            }
                        case ArmorComponent.ArmorMaterialTypes.Leather:
                            {
                                materialPriceModifier = 15f;
                                break;
                            }
                        case ArmorComponent.ArmorMaterialTypes.Chainmail:
                            {
                                materialPriceModifier = 80f;
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
                    price = (500f + (tier * 100f)) * 0.7f * RBMConfig.RBMConfig.priceMultipliers.WeaponPriceModifier;
                    if (item.ItemType == ItemObject.ItemTypeEnum.Polearm)
                    {
                        price *= 0.3f;
                    }
                    if (item.ItemType == ItemObject.ItemTypeEnum.Thrown)
                    {
                        price *= 0.25f;
                    }
                    if (item.ItemType == ItemObject.ItemTypeEnum.Sling)
                    {
                        price *= 0.25f;
                    }
                    if (item.ItemType == ItemObject.ItemTypeEnum.TwoHandedWeapon)
                    {
                        price *= 1.5f;
                    }
                    if (item.ItemType == ItemObject.ItemTypeEnum.Shield)
                    {
                        price *= 0.3f;
                    }
                    if (item.ItemType == ItemObject.ItemTypeEnum.Arrows || item.ItemType == ItemObject.ItemTypeEnum.Bolts || item.ItemType == ItemObject.ItemTypeEnum.SlingStones)
                    {
                        price = (30f + (tier * 10f)) * RBMConfig.RBMConfig.priceMultipliers.WeaponPriceModifier;
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
    }
}
