using System;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;

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
                float tier = 1f;
                if (item.ItemComponent != null)
                {
                    tier = __instance.GetEquipmentValueFromTier(item.Tierf);
                }

                float price = RBMConfig.RBMConfig.rbmCampaignEnabled
                    ? CalculateCampaignValue(item, tier)
                    : CalculateLegacyValue(item, tier);

                __result = (int)price;
                return false;
            }

            // Pricing used together with the RBMCampaign economy (spoils, wealth ledger, market pricing).
            private static float CalculateCampaignValue(ItemObject item, float tier)
            {
                float price = 1;
                float materialPriceModifier = 1f;
                if (item.ArmorComponent != null)
                {
                    switch (item.ArmorComponent.MaterialType)
                    {
                        case ArmorComponent.ArmorMaterialTypes.Cloth:
                            {
                                materialPriceModifier = 0.4f * MathF.Clamp(tier - 1f, 0f, 4f);
                                break;
                            }
                        case ArmorComponent.ArmorMaterialTypes.Leather:
                            {
                                materialPriceModifier = 0.6f * MathF.Clamp(tier - 1f, 0f, 6f);
                                break;
                            }
                        case ArmorComponent.ArmorMaterialTypes.Chainmail:
                            {
                                materialPriceModifier = 1.6f * MathF.Clamp(tier - 3f, 1f, 6f);
                                break;
                            }
                        case ArmorComponent.ArmorMaterialTypes.Plate:
                            {
                                materialPriceModifier = 1.7f * MathF.Clamp(tier - 3f, 1f, 6f);
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
                        price = 600f + (70f * materialPriceModifier * ((item.ArmorComponent.BodyArmor * 0.2f) + (item.ArmorComponent.ArmArmor * 0.2f) + (item.ArmorComponent.LegArmor * 0.4f) + (item.ArmorComponent.HeadArmor * 0.2f)));
                    }
                    price *= RBMConfig.RBMConfig.priceMultipliers.ArmorPriceModifier;
                }
                else if (item.ItemComponent is WeaponComponent)
                {
                    price = (30f + (tier * 24f)) * RBMConfig.RBMConfig.priceMultipliers.WeaponPriceModifier;
                    if (item.ItemType == ItemObject.ItemTypeEnum.Polearm)
                    {
                        price = (30f + (tier * 16f)) * RBMConfig.RBMConfig.priceMultipliers.WeaponPriceModifier;
                    }
                    if (item.ItemType == ItemObject.ItemTypeEnum.Thrown)
                    {
                        price *= 1f;
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
                        price = (120f + (tier * 2f)) * RBMConfig.RBMConfig.priceMultipliers.WeaponPriceModifier;
                    }
                    if (item.ItemType == ItemObject.ItemTypeEnum.Arrows || item.ItemType == ItemObject.ItemTypeEnum.Bolts || item.ItemType == ItemObject.ItemTypeEnum.SlingStones)
                    {
                        price = (20f + (tier * 1f)) * RBMConfig.RBMConfig.priceMultipliers.WeaponPriceModifier;
                    }
                }
                else if (item.ItemComponent is HorseComponent)
                {
                    price = 600f + (1000f * tier * RBMConfig.RBMConfig.priceMultipliers.HorsePriceModifier);
                }
                else
                {
                    // Trade goods are priced by RBMCampaign's market code.
                    price = 1;
                }
                return price;
            }

            // Pre-RBMCampaign pricing (as of commit 713bde89), used when the campaign module is disabled.
            private static float CalculateLegacyValue(ItemObject item, float tier)
            {
                float price = 1;
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
                return price;
            }
        }
    }
}
