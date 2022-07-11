using System.Collections.Generic;
using System.Xml;

namespace RBM.Config
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
        public static bool rbmCombatEnabled = false;
        //RBMAI
        public static bool siegeTowersEnabled = true;
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

        public static void parseXmlConfig()
        {
            //modules
            rbmTournamentEnabled = xmlConfig.SelectSingleNode("/Config/RBMTournament/Enabled").InnerText.Equals("1");
            rbmAiEnabled = xmlConfig.SelectSingleNode("/Config/RBMAI/Enabled").InnerText.Equals("1");
            rbmCombatEnabled = xmlConfig.SelectSingleNode("/Config/RBMCombat/Enabled").InnerText.Equals("1");
            //RBMAI
            siegeTowersEnabled = xmlConfig.SelectSingleNode("/Config/RBMAI/SiegeTowersEnabled").InnerText.Equals("1");
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
            maceBluntModifier = float.Parse(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/ArmorMultiplier").InnerText);
            armorThresholdModifier = float.Parse(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/ArmorMultiplier").InnerText);
            bluntTraumaBonus = float.Parse(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/ArmorMultiplier").InnerText);
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
        }

        public static void saveXmlConfig()
        {

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
