using System.Collections.Generic;
using System.IO;
using System.Xml;
using TaleWorlds.Library;

namespace RBMConfig
{
    public static class RBMConfig
    {
        public static XmlDocument xmlConfig = new XmlDocument();
        public static float ThrustMagnitudeModifier = 0.025f;
        public static float OneHandedThrustDamageBonus = 40f;
        public static float TwoHandedThrustDamageBonus = 40f;
        //modules
        public static bool rbmTournamentEnabled = true;
        public static bool rbmAiEnabled = true;
        public static bool rbmCombatEnabled = true;
        public static bool developerMode = false;
        //RBMAI
        public static bool postureEnabled = true;
        public static float playerPostureMultiplier = 1f;
        public static bool postureGUIEnabled = true;
        public static bool vanillaCombatAi = false;
        //RBMCombat
        public static float armorMultiplier = 2f;
        public static bool armorPenetrationMessage = false;
        public static bool betterArrowVisuals = true;
        public static bool passiveShoulderShields = false;
        public static bool troopOverhaulActive = true;
        public static string realisticRangedReload = "1";
        public static float maceBluntModifier = 1f;
        public static float armorThresholdModifier = 1f;
        public static float bluntTraumaBonus = 1f;
        public static RBMCombatConfigPriceMultipliers priceMultipliers = new RBMCombatConfigPriceMultipliers();
        public static List<RBMCombatConfigWeaponType> weaponTypesFactors = new List<RBMCombatConfigWeaponType>();

        public static void LoadConfig()
        {
            string defaultConfigFilePath = BasePath.Name + "Modules/RBM/DefaultConfigDONOTEDIT.xml";
            string configFolderPath = Utilities.GetConfigFolderPath();
            string configFilePath = Utilities.GetConfigFilePath();

            if (!Directory.Exists(configFolderPath))
            {
                Directory.CreateDirectory(configFolderPath);
            }

            if (File.Exists(configFilePath))
            {
                xmlConfig.Load(configFilePath);
            }
            else
            {
                File.Copy(defaultConfigFilePath, configFilePath);
                xmlConfig.Load(configFilePath);
            }

            parseXmlConfig();
        }

        public static void parseXmlConfig()
        {
            if(xmlConfig.SelectSingleNode("/Config/DeveloperMode") != null)
            {
                developerMode = true;
            }
            //modules
            rbmTournamentEnabled = xmlConfig.SelectSingleNode("/Config/RBMTournament/Enabled").InnerText.Equals("1");
            rbmAiEnabled = xmlConfig.SelectSingleNode("/Config/RBMAI/Enabled").InnerText.Equals("1");
            rbmCombatEnabled = xmlConfig.SelectSingleNode("/Config/RBMCombat/Enabled").InnerText.Equals("1");
            //RBMAI
            postureEnabled = xmlConfig.SelectSingleNode("/Config/RBMAI/PostureEnabled").InnerText.Equals("1");
            postureGUIEnabled = xmlConfig.SelectSingleNode("/Config/RBMAI/PostureGUIEnabled").InnerText.Equals("1");
            vanillaCombatAi = xmlConfig.SelectSingleNode("/Config/RBMAI/VanillaCombatAi").InnerText.Equals("1");
            if (xmlConfig.SelectSingleNode("/Config/RBMAI/PlayerPostureMultiplier") != null)
            {
                switch (xmlConfig.SelectSingleNode("/Config/RBMAI/PlayerPostureMultiplier").InnerText)
                {
                    case "0":
                        {
                            playerPostureMultiplier = 1f;
                            break;
                        }
                    case "1":
                        {
                            playerPostureMultiplier = 1.5f;
                            break;
                        }
                    case "2":
                        {
                            playerPostureMultiplier = 2f;
                            break;
                        }
                }
            }
            //RBMCombat
            armorMultiplier = float.Parse(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/ArmorMultiplier").InnerText);
            armorPenetrationMessage = xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/ArmorPenetrationMessage").InnerText.Equals("1");
            betterArrowVisuals = xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/BetterArrowVisuals").InnerText.Equals("1");
            passiveShoulderShields = xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/PassiveShoulderShields").InnerText.Equals("1");
            troopOverhaulActive = xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/TroopOverhaulActive").InnerText.Equals("1");
            realisticRangedReload = xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/RealisticRangedReload").InnerText;
            maceBluntModifier = float.Parse(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/MaceBluntModifier").InnerText);
            armorThresholdModifier = float.Parse(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/ArmorThresholdModifier").InnerText);
            bluntTraumaBonus = float.Parse(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/BluntTraumaBonus").InnerText);
            foreach(XmlNode weaponTypeNode in xmlConfig.SelectSingleNode("/Config/RBMCombat/WeaponTypes").ChildNodes)
            {
                RBMCombatConfigWeaponType weaponTypeFactors = new RBMCombatConfigWeaponType();
                weaponTypeFactors.weaponType = weaponTypeNode.Name;
                weaponTypeFactors.ExtraBluntFactorCut = float.Parse(xmlConfig.SelectSingleNode("/Config/RBMCombat/WeaponTypes/" + weaponTypeNode.Name + "/ExtraBluntFactorCut").InnerText);
                weaponTypeFactors.ExtraBluntFactorPierce = float.Parse(xmlConfig.SelectSingleNode("/Config/RBMCombat/WeaponTypes/" + weaponTypeNode.Name + "/ExtraBluntFactorPierce").InnerText);
                weaponTypeFactors.ExtraBluntFactorBlunt = float.Parse(xmlConfig.SelectSingleNode("/Config/RBMCombat/WeaponTypes/" + weaponTypeNode.Name + "/ExtraBluntFactorBlunt").InnerText);
                weaponTypeFactors.ExtraArmorThresholdFactorPierce = float.Parse(xmlConfig.SelectSingleNode("/Config/RBMCombat/WeaponTypes/" + weaponTypeNode.Name + "/ExtraArmorThresholdFactorPierce").InnerText);
                weaponTypeFactors.ExtraArmorThresholdFactorCut = float.Parse(xmlConfig.SelectSingleNode("/Config/RBMCombat/WeaponTypes/" + weaponTypeNode.Name + "/ExtraArmorThresholdFactorCut").InnerText);
                weaponTypeFactors.ExtraArmorSkillDamageAbsorb = float.Parse(xmlConfig.SelectSingleNode("/Config/RBMCombat/WeaponTypes/" + weaponTypeNode.Name + "/ExtraArmorSkillDamageAbsorb").InnerText);
                weaponTypesFactors.Add(weaponTypeFactors);
            }
            priceMultipliers.ArmorPriceModifier = float.Parse(xmlConfig.SelectSingleNode("/Config/RBMCombat/PriceModifiers/ArmorPriceModifier").InnerText);
            priceMultipliers.WeaponPriceModifier = float.Parse(xmlConfig.SelectSingleNode("/Config/RBMCombat/PriceModifiers/WeaponPriceModifier").InnerText);
            priceMultipliers.HorsePriceModifier = float.Parse(xmlConfig.SelectSingleNode("/Config/RBMCombat/PriceModifiers/HorsePriceModifier").InnerText);
            priceMultipliers.TradePriceModifier = float.Parse(xmlConfig.SelectSingleNode("/Config/RBMCombat/PriceModifiers/TradePriceModifier").InnerText);
        }

        public static void setInnerTextBoolean(XmlNode xmlConfig, bool value)
        {
            if (value)
            {
                xmlConfig.InnerText = "1";
            }
            else
            {
                xmlConfig.InnerText = "0";
            }
        }

        public static void setInnerText(XmlNode xmlConfig, string value)
        {
            xmlConfig.InnerText = value;
        }

        public static void saveXmlConfig()
        {
            //modules
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMTournament/Enabled"), rbmTournamentEnabled);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMAI/Enabled"), rbmAiEnabled);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCombat/Enabled"), rbmCombatEnabled);
            //RBMAI
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMAI/PostureEnabled"), postureEnabled);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMAI/PostureGUIEnabled"), postureGUIEnabled);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMAI/VanillaCombatAi"), vanillaCombatAi);
            switch (playerPostureMultiplier)
            {
                case 1f:
                    {
                        setInnerText(xmlConfig.SelectSingleNode("/Config/RBMAI/PlayerPostureMultiplier"), "0");
                        break;
                    }
                case 1.5f:
                    {
                        setInnerText(xmlConfig.SelectSingleNode("/Config/RBMAI/PlayerPostureMultiplier"), "1");

                        break;
                    }
                case 2f:
                    {
                        setInnerText(xmlConfig.SelectSingleNode("/Config/RBMAI/PlayerPostureMultiplier"), "2");
                        break;
                    }
            }
            //RBMCombat
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/ArmorMultiplier"), armorMultiplier.ToString());
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/ArmorPenetrationMessage"), armorPenetrationMessage);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/BetterArrowVisuals"), betterArrowVisuals);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/PassiveShoulderShields"), passiveShoulderShields);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/TroopOverhaulActive"), troopOverhaulActive);
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/RealisticRangedReload"), realisticRangedReload.ToString());
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/MaceBluntModifier"), maceBluntModifier.ToString());
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/ArmorThresholdModifier"), armorThresholdModifier.ToString());
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/BluntTraumaBonus"), bluntTraumaBonus.ToString());

            foreach (RBMCombatConfigWeaponType weaponTypeFactors in weaponTypesFactors)
            {
                setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/WeaponTypes/" + weaponTypeFactors.weaponType + "/ExtraBluntFactorCut"), weaponTypeFactors.ExtraBluntFactorCut.ToString());
                setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/WeaponTypes/" + weaponTypeFactors.weaponType + "/ExtraBluntFactorPierce"), weaponTypeFactors.ExtraBluntFactorPierce.ToString());
                setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/WeaponTypes/" + weaponTypeFactors.weaponType + "/ExtraBluntFactorBlunt"), weaponTypeFactors.ExtraBluntFactorBlunt.ToString());
                setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/WeaponTypes/" + weaponTypeFactors.weaponType + "/ExtraArmorThresholdFactorPierce"), weaponTypeFactors.ExtraArmorThresholdFactorPierce.ToString());
                setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/WeaponTypes/" + weaponTypeFactors.weaponType + "/ExtraArmorThresholdFactorCut"), weaponTypeFactors.ExtraArmorThresholdFactorCut.ToString());
                setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/WeaponTypes/" + weaponTypeFactors.weaponType + "/ExtraArmorSkillDamageAbsorb"), weaponTypeFactors.ExtraArmorSkillDamageAbsorb.ToString());
            }

            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/PriceModifiers/ArmorPriceModifier"), priceMultipliers.ArmorPriceModifier.ToString());
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/PriceModifiers/WeaponPriceModifier"), priceMultipliers.WeaponPriceModifier.ToString());
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/PriceModifiers/HorsePriceModifier"), priceMultipliers.HorsePriceModifier.ToString());
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/PriceModifiers/TradePriceModifier"), priceMultipliers.TradePriceModifier.ToString());

            xmlConfig.Save(Utilities.GetConfigFilePath());

        }

        public static RBMCombatConfigWeaponType getWeaponTypeFactors(string weaponType)
        {
            foreach(RBMCombatConfigWeaponType weaponTypeFactors in weaponTypesFactors)
            {
                if (weaponTypeFactors.weaponType.Equals(weaponType)){
                    return weaponTypeFactors;
                }
            }
            return null;
        }

    }
}
