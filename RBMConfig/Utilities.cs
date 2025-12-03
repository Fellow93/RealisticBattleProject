using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace RBMConfig
{
    public static class Utilities
    {
        public static string GetConfigFilePath()
        {
            return System.IO.Path.Combine(GetConfigFolderPath(), "config5.xml");
        }

        public static string GetConfigFolderPath()
        {
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal),
             "Mount and Blade II Bannerlord", "Configs", "RBM");
        }

        public static void createWeaponTypesFactors(ref List<RBMCombatConfigWeaponType> weaponTypesFactors)
        {
            weaponTypesFactors.Add(new RBMCombatConfigWeaponType(
                weaponType:"Dagger", 
                ExtraBluntFactorCut: 0.25f, 
                ExtraBluntFactorPierce: 0.35f, 
                ExtraBluntFactorBlunt: 1f,
                ExtraArmorThresholdFactorPierce: 3f,
                ExtraArmorThresholdFactorCut: 5f,
                ExtraArmorSkillDamageAbsorb: 1f
                )
            );
            weaponTypesFactors.Add(new RBMCombatConfigWeaponType(
                weaponType: "ThrowingKnife",
                ExtraBluntFactorCut: 0.15f,
                ExtraBluntFactorPierce: 0.15f,
                ExtraBluntFactorBlunt: 1f,
                ExtraArmorThresholdFactorPierce: 3f,
                ExtraArmorThresholdFactorCut: 5f,
                ExtraArmorSkillDamageAbsorb: 1f
                )
            );
            weaponTypesFactors.Add(new RBMCombatConfigWeaponType(
                weaponType: "OneHandedSword",
                ExtraBluntFactorCut: 0.25f,
                ExtraBluntFactorPierce: 0.35f,
                ExtraBluntFactorBlunt: 1f,
                ExtraArmorThresholdFactorPierce: 3.5f,
                ExtraArmorThresholdFactorCut: 5,
                ExtraArmorSkillDamageAbsorb: 1
                )
            );
            weaponTypesFactors.Add(new RBMCombatConfigWeaponType(
                weaponType: "TwoHandedSword",
                ExtraBluntFactorCut: 0.25f,
                ExtraBluntFactorPierce: 0.35f,
                ExtraBluntFactorBlunt: 1f,
                ExtraArmorThresholdFactorPierce: 3.5f,
                ExtraArmorThresholdFactorCut: 5f,
                ExtraArmorSkillDamageAbsorb: 1f
                )
            );
            weaponTypesFactors.Add(new RBMCombatConfigWeaponType(
                weaponType: "OneHandedBastardAxe",
                ExtraBluntFactorCut: 0.3f,
                ExtraBluntFactorPierce: 0.25f,
                ExtraBluntFactorBlunt: 1f,
                ExtraArmorThresholdFactorPierce: 2.5f,
                ExtraArmorThresholdFactorCut: 5f,
                ExtraArmorSkillDamageAbsorb: 1f
                )
            );
            weaponTypesFactors.Add(new RBMCombatConfigWeaponType(
               weaponType: "OneHandedAxe",
               ExtraBluntFactorCut: 0.3f,
               ExtraBluntFactorPierce: 0.25f,
               ExtraBluntFactorBlunt: 1f,
               ExtraArmorThresholdFactorPierce: 2.5f,
               ExtraArmorThresholdFactorCut: 5f,
               ExtraArmorSkillDamageAbsorb: 1f
               )
           );
            weaponTypesFactors.Add(new RBMCombatConfigWeaponType(
                weaponType: "TwoHandedAxe",
                ExtraBluntFactorCut: 0.3f,
                ExtraBluntFactorPierce: 0.3f,
                ExtraBluntFactorBlunt: 1f,
                ExtraArmorThresholdFactorPierce: 2.5f,
                ExtraArmorThresholdFactorCut: 5f,
                ExtraArmorSkillDamageAbsorb: 1f
                )
            );
            weaponTypesFactors.Add(new RBMCombatConfigWeaponType(
                weaponType: "OneHandedPolearm",
                ExtraBluntFactorCut: 0.3f,
                ExtraBluntFactorPierce: 0.35f,
                ExtraBluntFactorBlunt: 1f,
                ExtraArmorThresholdFactorPierce: 3f,
                ExtraArmorThresholdFactorCut: 5f,
                ExtraArmorSkillDamageAbsorb: 1f
                )
            );
            weaponTypesFactors.Add(new RBMCombatConfigWeaponType(
                weaponType: "TwoHandedPolearm",
                ExtraBluntFactorCut: 0.3f,
                ExtraBluntFactorPierce: 0.35f,
                ExtraBluntFactorBlunt: 1f,
                ExtraArmorThresholdFactorPierce: 3f,
                ExtraArmorThresholdFactorCut: 5f,
                ExtraArmorSkillDamageAbsorb: 1f
                )
            );
            weaponTypesFactors.Add(new RBMCombatConfigWeaponType(
                weaponType: "Mace",
                ExtraBluntFactorCut: 0.1f,
                ExtraBluntFactorPierce: 0.25f,
                ExtraBluntFactorBlunt: 1f,
                ExtraArmorThresholdFactorPierce: 2f,
                ExtraArmorThresholdFactorCut: 4f,
                ExtraArmorSkillDamageAbsorb: 1f
                )
            );
            weaponTypesFactors.Add(new RBMCombatConfigWeaponType(
                weaponType: "TwoHandedMace",
                ExtraBluntFactorCut: 0.1f,
                ExtraBluntFactorPierce: 0.25f,
                ExtraBluntFactorBlunt: 1f,
                ExtraArmorThresholdFactorPierce: 2f,
                ExtraArmorThresholdFactorCut: 4f,
                ExtraArmorSkillDamageAbsorb: 1f
                )
            );
            weaponTypesFactors.Add(new RBMCombatConfigWeaponType(
               weaponType: "Arrow",
               ExtraBluntFactorCut: 0.15f,
               ExtraBluntFactorPierce: 0.15f,
               ExtraBluntFactorBlunt: 1f,
               ExtraArmorThresholdFactorPierce: 2f,
               ExtraArmorThresholdFactorCut: 2.6f,
               ExtraArmorSkillDamageAbsorb: 1f
               )
            );
            weaponTypesFactors.Add(new RBMCombatConfigWeaponType(
               weaponType: "Bolt",
               ExtraBluntFactorCut: 0.15f,
               ExtraBluntFactorPierce: 0.15f,
               ExtraBluntFactorBlunt: 1f,
               ExtraArmorThresholdFactorPierce: 2f,
               ExtraArmorThresholdFactorCut: 2.6f,
               ExtraArmorSkillDamageAbsorb: 1f
               )
            );
            weaponTypesFactors.Add(new RBMCombatConfigWeaponType(
               weaponType: "Javelin",
               ExtraBluntFactorCut: 0.05f,
               ExtraBluntFactorPierce: 0.2f,
               ExtraBluntFactorBlunt: 1f,
               ExtraArmorThresholdFactorPierce: 3f,
               ExtraArmorThresholdFactorCut: 3f,
               ExtraArmorSkillDamageAbsorb: 1f
               )
            );
            weaponTypesFactors.Add(new RBMCombatConfigWeaponType(
               weaponType: "ThrowingAxe",
               ExtraBluntFactorCut: 0.3f,
               ExtraBluntFactorPierce: 0.2f,
               ExtraBluntFactorBlunt: 1f,
               ExtraArmorThresholdFactorPierce: 2.5f,
               ExtraArmorThresholdFactorCut: 4f,
               ExtraArmorSkillDamageAbsorb: 1f
               )
            );
            weaponTypesFactors.Add(new RBMCombatConfigWeaponType(
               weaponType: "SlingStone",
               ExtraBluntFactorCut: 0.4f,
               ExtraBluntFactorPierce: 0.4f,
               ExtraBluntFactorBlunt: 1f,
               ExtraArmorThresholdFactorPierce: 4f,
               ExtraArmorThresholdFactorCut: 6f,
               ExtraArmorSkillDamageAbsorb: 1f
               )
            );
        }

        public static string getPostureMultiplier(float playerPostureMultiplier)
        {
            switch (playerPostureMultiplier)
            {
                case 1f:
                    {
                        return "0";
                    }
                case 1.5f:
                    {
                        return "1";
                    }
                case 2f:
                    {
                        return "2";
                    }
                default:
                    {
                        return "0";
                    }
            }
        }

        public static void createXmlConfig(ref XmlDocument xmlconfig)
        {
            XmlElement Config = xmlconfig.CreateElement("Config");

            //RBM tournament
            XmlElement RBMTournament = xmlconfig.CreateElement("RBMTournament");

            XmlElement RBMTournamentEnabled = xmlconfig.CreateElement("Enabled");
            RBMTournamentEnabled.InnerText = RBMConfig.rbmTournamentEnabled ? "1" : "0";
            RBMTournament.AppendChild(RBMTournamentEnabled);
            Config.AppendChild(RBMTournament);

            //RBM AI
            XmlElement RBMAI = xmlconfig.CreateElement("RBMAI");

            XmlElement RBMAIEnabled = xmlconfig.CreateElement("Enabled");
            RBMAIEnabled.InnerText = RBMConfig.rbmAiEnabled ? "1" : "0";
            XmlElement PostureEnabled = xmlconfig.CreateElement("PostureEnabled");
            PostureEnabled.InnerText = RBMConfig.postureEnabled ? "1" : "0";
            XmlElement PostureGUIEnabled = xmlconfig.CreateElement("PostureGUIEnabled");
            PostureGUIEnabled.InnerText = RBMConfig.postureGUIEnabled ? "1" : "0";
            XmlElement VanillaCombatAi = xmlconfig.CreateElement("VanillaCombatAi");
            VanillaCombatAi.InnerText = RBMConfig.vanillaCombatAi ? "1" : "0";
            XmlElement PlayerPostureMultiplier = xmlconfig.CreateElement("PlayerPostureMultiplier");
            PlayerPostureMultiplier.InnerText = getPostureMultiplier(RBMConfig.playerPostureMultiplier);

            RBMAI.AppendChild(RBMAIEnabled);
            RBMAI.AppendChild(PostureEnabled);
            RBMAI.AppendChild(PlayerPostureMultiplier);
            RBMAI.AppendChild(PostureGUIEnabled);
            RBMAI.AppendChild(VanillaCombatAi);
            Config.AppendChild(RBMAI);

            //RBM combat
            XmlElement RBMCombat = xmlconfig.CreateElement("RBMCombat");

            XmlElement RBMCombatEnabled = xmlconfig.CreateElement("Enabled");
            RBMCombatEnabled.InnerText = RBMConfig.rbmCombatEnabled ? "1" : "0";

            //price modifiers
            XmlElement PriceModifiers = xmlconfig.CreateElement("PriceModifiers");
            XmlElement ArmorPriceModifier = xmlconfig.CreateElement("ArmorPriceModifier");
            ArmorPriceModifier.InnerText = RBMConfig.priceMultipliers.ArmorPriceModifier.ToString();
            XmlElement WeaponPriceModifier = xmlconfig.CreateElement("WeaponPriceModifier");
            WeaponPriceModifier.InnerText = RBMConfig.priceMultipliers.WeaponPriceModifier.ToString();
            XmlElement HorsePriceModifier = xmlconfig.CreateElement("HorsePriceModifier");
            HorsePriceModifier.InnerText = RBMConfig.priceMultipliers.HorsePriceModifier.ToString();
            XmlElement TradePriceModifier = xmlconfig.CreateElement("TradePriceModifier");
            TradePriceModifier.InnerText = RBMConfig.priceMultipliers.TradePriceModifier.ToString();
            PriceModifiers.AppendChild(ArmorPriceModifier);
            PriceModifiers.AppendChild(WeaponPriceModifier);
            PriceModifiers.AppendChild(HorsePriceModifier);
            PriceModifiers.AppendChild(TradePriceModifier);

            //RBM combat global
            XmlElement Global = xmlconfig.CreateElement("Global");
            XmlElement ArmorMultiplier = xmlconfig.CreateElement("ArmorMultiplier");
            ArmorMultiplier.InnerText = RBMConfig.armorMultiplier.ToString();
            XmlElement ArmorPenetrationMessage = xmlconfig.CreateElement("ArmorPenetrationMessage");
            ArmorPenetrationMessage.InnerText = RBMConfig.armorPenetrationMessage ? "1" : "0";
            XmlElement BetterArrowVisuals = xmlconfig.CreateElement("BetterArrowVisuals");
            BetterArrowVisuals.InnerText = RBMConfig.betterArrowVisuals ? "1" : "0";
            XmlElement PassiveShoulderShields = xmlconfig.CreateElement("PassiveShoulderShields");
            PassiveShoulderShields.InnerText = RBMConfig.passiveShoulderShields ? "1" : "0";
            XmlElement TroopOverhaulActive = xmlconfig.CreateElement("TroopOverhaulActive");
            TroopOverhaulActive.InnerText = RBMConfig.troopOverhaulActive ? "1" : "0";
            XmlElement SneakAttackInstaKill = xmlconfig.CreateElement("SneakAttackInstaKill");
            SneakAttackInstaKill.InnerText = RBMConfig.sneakAttackInstaKill ? "1" : "0";
            XmlElement RealisticRangedReload = xmlconfig.CreateElement("RealisticRangedReload");
            RealisticRangedReload.InnerText = RBMConfig.realisticRangedReload;
            XmlElement MaceBluntModifier = xmlconfig.CreateElement("MaceBluntModifier");
            MaceBluntModifier.InnerText = RBMConfig.maceBluntModifier.ToString();
            XmlElement ArmorThresholdModifier = xmlconfig.CreateElement("ArmorThresholdModifier");
            ArmorThresholdModifier.InnerText = RBMConfig.armorThresholdModifier.ToString();
            XmlElement BluntTraumaBonus = xmlconfig.CreateElement("BluntTraumaBonus");
            BluntTraumaBonus.InnerText = RBMConfig.bluntTraumaBonus.ToString();
            XmlElement ArmorStatusUIEnabled = xmlconfig.CreateElement("ArmorStatusUIEnabled");
            ArmorStatusUIEnabled.InnerText = RBMConfig.armorStatusUIEnabled ? "1" : "0";
            XmlElement RealisticArrowArc = xmlconfig.CreateElement("RealisticArrowArc");
            RealisticArrowArc.InnerText = RBMConfig.realisticArrowArc ? "1" : "0";
            XmlElement ThrustMagnitudeModifier = xmlconfig.CreateElement("ThrustMagnitudeModifier");
            ThrustMagnitudeModifier.InnerText = RBMConfig.ThrustMagnitudeModifier.ToString();

            Global.AppendChild(ArmorMultiplier);
            Global.AppendChild(ArmorPenetrationMessage);
            Global.AppendChild(BetterArrowVisuals);
            Global.AppendChild(PassiveShoulderShields);
            Global.AppendChild(TroopOverhaulActive);
            Global.AppendChild(SneakAttackInstaKill);
            Global.AppendChild(RealisticRangedReload);
            Global.AppendChild(MaceBluntModifier);
            Global.AppendChild(ArmorThresholdModifier);
            Global.AppendChild(BluntTraumaBonus);
            Global.AppendChild(ArmorStatusUIEnabled);
            Global.AppendChild(RealisticArrowArc);
            Global.AppendChild(ThrustMagnitudeModifier);

            //Weapon types
            XmlElement WeaponTypes = xmlconfig.CreateElement("WeaponTypes");
            foreach (RBMCombatConfigWeaponType weaponTypesFactor in RBMConfig.weaponTypesFactors)
            {
                XmlElement WeaponType = xmlconfig.CreateElement(weaponTypesFactor.weaponType);
                XmlElement ExtraBluntFactorCut = xmlconfig.CreateElement("ExtraBluntFactorCut");
                ExtraBluntFactorCut.InnerText = weaponTypesFactor.ExtraBluntFactorCut.ToString();
                XmlElement ExtraBluntFactorPierce = xmlconfig.CreateElement("ExtraBluntFactorPierce");
                ExtraBluntFactorPierce.InnerText = weaponTypesFactor.ExtraBluntFactorPierce.ToString();
                XmlElement ExtraBluntFactorBlunt = xmlconfig.CreateElement("ExtraBluntFactorBlunt");
                ExtraBluntFactorBlunt.InnerText = weaponTypesFactor.ExtraBluntFactorBlunt.ToString();
                XmlElement ExtraArmorThresholdFactorPierce = xmlconfig.CreateElement("ExtraArmorThresholdFactorPierce");
                ExtraArmorThresholdFactorPierce.InnerText = weaponTypesFactor.ExtraArmorThresholdFactorPierce.ToString();
                XmlElement ExtraArmorThresholdFactorCut = xmlconfig.CreateElement("ExtraArmorThresholdFactorCut");
                ExtraArmorThresholdFactorCut.InnerText = weaponTypesFactor.ExtraArmorThresholdFactorCut.ToString();
                XmlElement ExtraArmorSkillDamageAbsorb = xmlconfig.CreateElement("ExtraArmorSkillDamageAbsorb");
                ExtraArmorSkillDamageAbsorb.InnerText = weaponTypesFactor.ExtraArmorSkillDamageAbsorb.ToString();

                WeaponType.AppendChild(ExtraBluntFactorCut);
                WeaponType.AppendChild(ExtraBluntFactorPierce);
                WeaponType.AppendChild(ExtraBluntFactorBlunt);
                WeaponType.AppendChild(ExtraArmorThresholdFactorPierce);
                WeaponType.AppendChild(ExtraArmorThresholdFactorCut);
                WeaponType.AppendChild(ExtraArmorSkillDamageAbsorb);

                WeaponTypes.AppendChild(WeaponType);
            }

            RBMCombat.AppendChild(RBMCombatEnabled);
            RBMCombat.AppendChild(PriceModifiers);
            RBMCombat.AppendChild(Global);
            RBMCombat.AppendChild(WeaponTypes);
            Config.AppendChild(RBMCombat);

            xmlconfig.AppendChild(Config);
            xmlconfig.Save(GetConfigFilePath());
        }
    }
}