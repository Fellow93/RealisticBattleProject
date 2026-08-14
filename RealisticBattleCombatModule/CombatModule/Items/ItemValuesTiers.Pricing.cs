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
                                materialPriceModifier = 1f;
                                break;
                            }
                        case ArmorComponent.ArmorMaterialTypes.Leather:
                            {
                                materialPriceModifier = 1.5f;
                                break;
                            }
                        case ArmorComponent.ArmorMaterialTypes.Chainmail:
                            {
                                materialPriceModifier = 3f;
                                break;
                            }
                        case ArmorComponent.ArmorMaterialTypes.Plate:
                            {
                                materialPriceModifier = 3.1f;
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
                        price = 50f + 4f * (item.ArmorComponent.LegArmor * materialPriceModifier);
                    }
                    else if (item.ItemType == ItemObject.ItemTypeEnum.HandArmor)
                    {
                        price = 50f + 5f * (item.ArmorComponent.ArmArmor * materialPriceModifier * 0.8f);
                    }
                    else if (item.ItemType == ItemObject.ItemTypeEnum.HeadArmor)
                    {
                        price = 70f + 3f * ((item.ArmorComponent.HeadArmor * materialPriceModifier * 1.2f) + (item.ArmorComponent.BodyArmor * materialPriceModifier * 0.6f));
                    }
                    else if (item.ItemType == ItemObject.ItemTypeEnum.Cape)
                    {
                        price = 50f + 5f * ((item.ArmorComponent.BodyArmor * materialPriceModifier * 0.8f) + (item.ArmorComponent.ArmArmor * materialPriceModifier * 0.8f));
                    }
                    else if (item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                    {
                        price = 150f + 5f * ((item.ArmorComponent.BodyArmor * materialPriceModifier * 2.5f) + (item.ArmorComponent.LegArmor * materialPriceModifier) + (item.ArmorComponent.ArmArmor * materialPriceModifier * 0.8f));
                    }
                    else if (item.ItemType == ItemObject.ItemTypeEnum.HorseHarness)
                    {
                        price = 400f * ((item.ArmorComponent.BodyArmor * 0.2f) + (item.ArmorComponent.ArmArmor * 0.2f) + (item.ArmorComponent.LegArmor * 0.4f) + (item.ArmorComponent.HeadArmor * 0.2f));
                    }
                    price *= RBMConfig.RBMConfig.priceMultipliers.ArmorPriceModifier;
                }
                else if (item.ItemComponent is WeaponComponent)
                {
                    price = (60f + (tier * 36f)) * RBMConfig.RBMConfig.priceMultipliers.WeaponPriceModifier;
                    if (item.ItemType == ItemObject.ItemTypeEnum.Polearm)
                    {
                        price = (20f + (tier * 20f)) * RBMConfig.RBMConfig.priceMultipliers.WeaponPriceModifier;
                    }
                    if (item.ItemType == ItemObject.ItemTypeEnum.Thrown)
                    {
                        price *= 0.5f;
                    }
                    if (item.ItemType == ItemObject.ItemTypeEnum.Sling)
                    {
                        price *= 0.25f;
                    }
                    if (item.ItemType == ItemObject.ItemTypeEnum.TwoHandedWeapon)
                    {
                        price *= 2f;
                    }
                    if (item.ItemType == ItemObject.ItemTypeEnum.Shield)
                    {
                        price = (20f + (tier * 140f)) * RBMConfig.RBMConfig.priceMultipliers.WeaponPriceModifier;
                    }
                    if (item.ItemType == ItemObject.ItemTypeEnum.Arrows || item.ItemType == ItemObject.ItemTypeEnum.Bolts || item.ItemType == ItemObject.ItemTypeEnum.SlingStones)
                    {
                        price = (20f + (tier * 2f)) * RBMConfig.RBMConfig.priceMultipliers.WeaponPriceModifier;
                    }
                }
                else if (item.ItemComponent is HorseComponent)
                //{
                //    price = 200f * tier * RBMConfig.RBMConfig.priceMultipliers.HorsePriceModifier * (1f + 0.2f * (item.Appearance - 1f)) + 100f * Math.Max(0f, item.Appearance - 1f);
                //}
                {
                    price = 600f + (1000f * tier * RBMConfig.RBMConfig.priceMultipliers.HorsePriceModifier);
                }
                //else if (item.ItemComponent is TradeItemComponent)
                //{
                //    price = 100f * tier * RBMConfig.RBMConfig.priceMultipliers.TradePriceModifier * (1f + 0.2f * (item.Appearance - 1f)) + 100f * Math.Max(0f, item.Appearance - 1f);
                //}
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
