using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace RBMConfig
{
    public static partial class RBMConfig
    {
        // Bump this to force all users to reset to defaults on next launch.
        public const int CONFIG_VERSION = 2;

        public static XmlDocument xmlConfig = new XmlDocument();

        //modules
        public static bool rbmTournamentEnabled = true;

        public static bool rbmAiEnabled = true;
        public static bool rbmCombatEnabled = true;
        public static bool rbmCampaignEnabled = true;
        public static bool developerMode = false;

        public static void LoadConfig()
        {
            weaponTypesFactors.Clear();
            Utilities.createWeaponTypesFactors(ref weaponTypesFactors);
            string configFolderPath = Utilities.GetConfigFolderPath();
            string configFilePath = Utilities.GetConfigFilePath();

            if (!Directory.Exists(configFolderPath))
            {
                Directory.CreateDirectory(configFolderPath);
            }

            // Migrate from legacy versioned config file if new one doesn't exist yet
            if (!File.Exists(configFilePath))
            {
                string legacyPath = System.IO.Path.Combine(configFolderPath, "config5.xml");
                if (File.Exists(legacyPath))
                    File.Copy(legacyPath, configFilePath);
            }

            if (File.Exists(configFilePath))
            {
                xmlConfig.Load(configFilePath);
                XmlElement root = xmlConfig.SelectSingleNode("/Config") as XmlElement;
                string storedStr = root?.GetAttribute("version") ?? "0";
                if (!int.TryParse(storedStr, out int storedVersion) || storedVersion != CONFIG_VERSION)
                {
                    xmlConfig = new XmlDocument();
                    Utilities.createXmlConfig(ref xmlConfig);
                }
                else
                {
                    parseXmlConfig();
                }
            }
            else
            {
                Utilities.createXmlConfig(ref xmlConfig);
            }
        }

        // Ensures a structural (non-leaf) XML node exists, creating it if missing.
        private static XmlNode EnsureNode(string parentXpath, string name)
        {
            string xpath = parentXpath + "/" + name;
            XmlNode node = xmlConfig.SelectSingleNode(xpath);
            if (node != null) return node;
            XmlNode parent = xmlConfig.SelectSingleNode(parentXpath);
            if (parent == null) return null;
            XmlElement created = xmlConfig.CreateElement(name);
            parent.AppendChild(created);
            return created;
        }

        // Reads a leaf node value, or creates it with defaultValue if missing.
        private static string ReadOrCreate(string parentXpath, string name, string defaultValue)
        {
            string xpath = parentXpath + "/" + name;
            XmlNode node = xmlConfig.SelectSingleNode(xpath);
            if (node != null) return node.InnerText;
            XmlNode parent = xmlConfig.SelectSingleNode(parentXpath);
            if (parent == null) return defaultValue;
            XmlElement created = xmlConfig.CreateElement(name);
            created.InnerText = defaultValue;
            parent.AppendChild(created);
            return defaultValue;
        }

        public static void parseXmlConfig()
        {
            // Ensure root and all structural nodes exist before reading any values.
            // This means a config missing any section will have it created with defaults.
            if (xmlConfig.SelectSingleNode("/Config") == null)
                xmlConfig.AppendChild(xmlConfig.CreateElement("Config"));
            EnsureNode("/Config", "RBMTournament");
            EnsureNode("/Config", "RBMAI");
            EnsureNode("/Config", "RBMCombat");
            EnsureNode("/Config", "RBMCampaign");
            EnsureNode("/Config/RBMCombat", "PriceModifiers");
            EnsureNode("/Config/RBMCombat", "Global");
            EnsureNode("/Config/RBMCombat", "WeaponTypes");

            developerMode = xmlConfig.SelectSingleNode("/Config/DeveloperMode") != null;

            // Modules
            rbmTournamentEnabled = ReadOrCreate("/Config/RBMTournament", "Enabled", "1").Equals("1");
            rbmAiEnabled = ReadOrCreate("/Config/RBMAI", "Enabled", "1").Equals("1");
            rbmCombatEnabled = ReadOrCreate("/Config/RBMCombat", "Enabled", "1").Equals("1");
            rbmCampaignEnabled = ReadOrCreate("/Config/RBMCampaign", "Enabled", "1").Equals("1");

            // RBMCampaign
            // Invariant culture: on comma-decimal locales "0.1" would otherwise parse as 1.
            troopUpgradeCostMultiplier = float.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopUpgradeCostMultiplier", "1"), CultureInfo.InvariantCulture);
            troopUpgradeSpoilsLootMultiplier = float.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopUpgradeSpoilsLootMultiplier", "1"), CultureInfo.InvariantCulture);
            troopUpgradeRequireSupplyTown = ReadOrCreate("/Config/RBMCampaign", "TroopUpgradeRequireSupplyTown", "1").Equals("1");
            troopUpgradeSupplyRadius = float.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopUpgradeSupplyRadius", "30"), CultureInfo.InvariantCulture);
            troopUpgradeChargeMountValue = ReadOrCreate("/Config/RBMCampaign", "TroopUpgradeChargeMountValue", "1").Equals("1");
            troopLootPiecesPerMan = int.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopLootPiecesPerMan", "3"), CultureInfo.InvariantCulture);
            troopLootOverlookChancePerTier = float.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopLootOverlookChancePerTier", "0.5"), CultureInfo.InvariantCulture);
            troopWageTierBase = int.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopWageTierBase", "20"), CultureInfo.InvariantCulture);
            troopMaintenanceFraction = float.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopMaintenanceFraction", "0.005"), CultureInfo.InvariantCulture);
            mercenaryMaintenancePurseFraction = float.Parse(ReadOrCreate("/Config/RBMCampaign", "MercenaryMaintenancePurseFraction", "0.5"), CultureInfo.InvariantCulture);
            independentMaintenancePurseFraction = float.Parse(ReadOrCreate("/Config/RBMCampaign", "IndependentMaintenancePurseFraction", "1.0"), CultureInfo.InvariantCulture);
            troopSettlementFoodDays = int.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopSettlementFoodDays", "20"), CultureInfo.InvariantCulture);
            recruitMaintenanceDays = int.Parse(ReadOrCreate("/Config/RBMCampaign", "RecruitMaintenanceDays", "5"), CultureInfo.InvariantCulture);
            troopFoodWageFraction = float.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopFoodWageFraction", "0.5"), CultureInfo.InvariantCulture);
            troopSettlementFunWageFraction = float.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopSettlementFunWageFraction", "1.5"), CultureInfo.InvariantCulture);
            troopRaidSpoilsMultiplier = float.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopRaidSpoilsMultiplier", "0.25"), CultureInfo.InvariantCulture);
            troopLeaderSpoilsCutFraction = float.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopLeaderSpoilsCutFraction", "0.05"), CultureInfo.InvariantCulture);
            troopSpoilsCapDays = int.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopSpoilsCapDays", "20"), CultureInfo.InvariantCulture);
            troopLuxuryCooldownDays = int.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopLuxuryCooldownDays", "20"), CultureInfo.InvariantCulture);
            troopLuxurySpendChance = float.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopLuxurySpendChance", "0.02"), CultureInfo.InvariantCulture);
            troopSpoilsHealGoldPerTier = int.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopSpoilsHealGoldPerTier", "10"), CultureInfo.InvariantCulture);
            troopSpoilsHealFractionPerHour = float.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopSpoilsHealFractionPerHour", "0.05"), CultureInfo.InvariantCulture);
            troopFallenSpoilsCaptureFraction = float.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopFallenSpoilsCaptureFraction", "0.75"), CultureInfo.InvariantCulture);
            realisticTradeGoodPrices = ReadOrCreate("/Config/RBMCampaign", "RealisticTradeGoodPrices", "1").Equals("1");
            showInventoryItemWeight = ReadOrCreate("/Config/RBMCampaign", "ShowInventoryItemWeight", "1").Equals("1");
            spoilsLoggingEnabled = ReadOrCreate("/Config/RBMCampaign", "SpoilsLoggingEnabled", "1").Equals("1");
            spoilsVerboseLoggingEnabled = ReadOrCreate("/Config/RBMCampaign", "SpoilsVerboseLoggingEnabled", "1").Equals("1");
            economyLoggingEnabled = ReadOrCreate("/Config/RBMCampaign", "EconomyLoggingEnabled", "1").Equals("1");
            simulationEquipmentEnabled = ReadOrCreate("/Config/RBMCampaign", "SimulationEquipmentEnabled", "1").Equals("1");
            simulationEquipmentPowerWeight = float.Parse(ReadOrCreate("/Config/RBMCampaign", "SimulationEquipmentPowerWeight", "1"), CultureInfo.InvariantCulture);
            strategicPowerEnabled = ReadOrCreate("/Config/RBMCampaign", "StrategicPowerEnabled", "1").Equals("1");
            strategicPowerLoggingEnabled = ReadOrCreate("/Config/RBMCampaign", "StrategicPowerLogging", "1").Equals("1");
            simulationAbsoluteDamage = ReadOrCreate("/Config/RBMCampaign", "SimulationAbsoluteDamage", "1").Equals("1");
            simulationAbsoluteScale = float.Parse(ReadOrCreate("/Config/RBMCampaign", "SimulationAbsoluteScale", "1"), CultureInfo.InvariantCulture);
            simulationAbsoluteBlowCap = float.Parse(ReadOrCreate("/Config/RBMCampaign", "SimulationAbsoluteBlowCap", "1.5"), CultureInfo.InvariantCulture);
            simulationShieldBlockChance = float.Parse(ReadOrCreate("/Config/RBMCampaign", "SimulationShieldBlockChance", "0.4"), CultureInfo.InvariantCulture);
            simulationDefenseSystem = ReadOrCreate("/Config/RBMCampaign", "SimulationDefenseSystem", "1").Equals("1");
            simulationArmTargeting = ReadOrCreate("/Config/RBMCampaign", "SimulationArmTargeting", "1").Equals("1");
            simulationRangedMissEnabled = ReadOrCreate("/Config/RBMCampaign", "SimulationRangedMissEnabled", "1").Equals("1");
            simulationRangedMissChance = float.Parse(ReadOrCreate("/Config/RBMCampaign", "SimulationRangedMissChance", "0.35"), CultureInfo.InvariantCulture);
            simulationRoundMinutes = float.Parse(ReadOrCreate("/Config/RBMCampaign", "SimulationRoundMinutes", "10"), CultureInfo.InvariantCulture);
            simulationSiegeDefenderEnabled = ReadOrCreate("/Config/RBMCampaign", "SimulationSiegeDefenderEnabled", "1").Equals("1");
            simulationSiegeDefenderDefenseBonus = float.Parse(ReadOrCreate("/Config/RBMCampaign", "SimulationSiegeDefenderDefenseBonus", "1.3"), CultureInfo.InvariantCulture);
            simulationSiegeRangedMissSkew = float.Parse(ReadOrCreate("/Config/RBMCampaign", "SimulationSiegeRangedMissSkew", "1.4"), CultureInfo.InvariantCulture);
            simulationRoutEnabled = ReadOrCreate("/Config/RBMCampaign", "SimulationRoutEnabled", "0").Equals("1");
            simulationPerkSystem = ReadOrCreate("/Config/RBMCampaign", "SimulationPerkSystem", "1").Equals("1");
            simulationLoggingEnabled = ReadOrCreate("/Config/RBMCampaign", "SimulationLoggingEnabled", "1").Equals("1");
            simulationLogHits = ReadOrCreate("/Config/RBMCampaign", "SimulationLogHits", "1").Equals("1");
            spectateBattlesEnabled = ReadOrCreate("/Config/RBMCampaign", "SpectateBattlesEnabled", "0").Equals("1");
            spectateMinTroopsPerSide = int.Parse(ReadOrCreate("/Config/RBMCampaign", "SpectateMinTroopsPerSide", "100"), CultureInfo.InvariantCulture);

            // RBMAI
            hitStopEnabled = ReadOrCreate("/Config/RBMAI", "HitStopEnabled", "1").Equals("1");
            postureEnabled = ReadOrCreate("/Config/RBMAI", "PostureEnabled", "1").Equals("1");
            staminaEnabled = ReadOrCreate("/Config/RBMAI", "StaminaEnabled", "1").Equals("1");
            postureGUIEnabled = ReadOrCreate("/Config/RBMAI", "PostureGUIEnabled", "1").Equals("1");
            vanillaCombatAi = ReadOrCreate("/Config/RBMAI", "VanillaCombatAi", "0").Equals("1");
            keepBattleEnabled = ReadOrCreate("/Config/RBMAI", "KeepBattleEnabled", "0").Equals("1");
            switch (ReadOrCreate("/Config/RBMAI", "PlayerPostureMultiplier", "0"))
            {
                case "1": playerPostureMultiplier = 1.5f; break;
                case "2": playerPostureMultiplier = 2f; break;
                default: playerPostureMultiplier = 1f; break;
            }

            // RBMCombat Global
            armorStatusUIEnabled = ReadOrCreate("/Config/RBMCombat/Global", "ArmorStatusUIEnabled", "1").Equals("1");
            battleHitLoggingEnabled = ReadOrCreate("/Config/RBMCombat/Global", "BattleHitLoggingEnabled", "0").Equals("1");
            realisticArrowArc = ReadOrCreate("/Config/RBMCombat/Global", "RealisticArrowArc", "0").Equals("1");
            armorMultiplier = float.Parse(ReadOrCreate("/Config/RBMCombat/Global", "ArmorMultiplier", "2"));
            armorPenetrationMessage = ReadOrCreate("/Config/RBMCombat/Global", "ArmorPenetrationMessage", "0").Equals("1");
            betterArrowVisuals = ReadOrCreate("/Config/RBMCombat/Global", "BetterArrowVisuals", "1").Equals("1");
            passiveShoulderShields = ReadOrCreate("/Config/RBMCombat/Global", "PassiveShoulderShields", "0").Equals("1");
            troopOverhaulActive = ReadOrCreate("/Config/RBMCombat/Global", "TroopOverhaulActive", "1").Equals("1");
            sneakAttackInstaKill = ReadOrCreate("/Config/RBMCombat/Global", "SneakAttackInstaKill", "0").Equals("1");
            realisticRangedReload = ReadOrCreate("/Config/RBMCombat/Global", "RealisticRangedReload", "2");
            maceBluntModifier = float.Parse(ReadOrCreate("/Config/RBMCombat/Global", "MaceBluntModifier", "1"));
            armorThresholdModifier = float.Parse(ReadOrCreate("/Config/RBMCombat/Global", "ArmorThresholdModifier", "1"));
            bluntTraumaBonus = float.Parse(ReadOrCreate("/Config/RBMCombat/Global", "BluntTraumaBonus", "0"));
            ThrustMagnitudeModifier = float.Parse(ReadOrCreate("/Config/RBMCombat/Global", "ThrustMagnitudeModifier", "0.05"));
            OneHandedThrustDamageBonus = 1f / ThrustMagnitudeModifier;
            TwoHandedThrustDamageBonus = 1f / ThrustMagnitudeModifier;

            // Weapon types: merge XML entries into defaults (keeps any new defaults not present in old config files)
            XmlNode weaponTypesXmlNode = xmlConfig.SelectSingleNode("/Config/RBMCombat/WeaponTypes");
            if (weaponTypesXmlNode != null && weaponTypesXmlNode.HasChildNodes)
            {
                foreach (XmlNode weaponTypeNode in weaponTypesXmlNode.ChildNodes)
                {
                    string name = weaponTypeNode.Name;
                    RBMCombatConfigWeaponType wt = weaponTypesFactors.Find(x => x.weaponType == name);
                    if (wt == null)
                    {
                        wt = new RBMCombatConfigWeaponType();
                        wt.weaponType = name;
                        weaponTypesFactors.Add(wt);
                    }
                    wt.ExtraBluntFactorCut = float.Parse(weaponTypeNode["ExtraBluntFactorCut"]?.InnerText ?? "0.25");
                    wt.ExtraBluntFactorPierce = float.Parse(weaponTypeNode["ExtraBluntFactorPierce"]?.InnerText ?? "0.35");
                    wt.ExtraBluntFactorBlunt = float.Parse(weaponTypeNode["ExtraBluntFactorBlunt"]?.InnerText ?? "1");
                    wt.ExtraArmorThresholdFactorPierce = float.Parse(weaponTypeNode["ExtraArmorThresholdFactorPierce"]?.InnerText ?? "3");
                    wt.ExtraArmorThresholdFactorCut = float.Parse(weaponTypeNode["ExtraArmorThresholdFactorCut"]?.InnerText ?? "5");
                    wt.ExtraArmorSkillDamageAbsorb = float.Parse(weaponTypeNode["ExtraArmorSkillDamageAbsorb"]?.InnerText ?? "1");
                }
            }

            // Price modifiers
            priceMultipliers.ArmorPriceModifier = float.Parse(ReadOrCreate("/Config/RBMCombat/PriceModifiers", "ArmorPriceModifier", "1"));
            priceMultipliers.WeaponPriceModifier = float.Parse(ReadOrCreate("/Config/RBMCombat/PriceModifiers", "WeaponPriceModifier", "1"));
            priceMultipliers.HorsePriceModifier = float.Parse(ReadOrCreate("/Config/RBMCombat/PriceModifiers", "HorsePriceModifier", "0.2"));
            priceMultipliers.TradePriceModifier = float.Parse(ReadOrCreate("/Config/RBMCombat/PriceModifiers", "TradePriceModifier", "1"));

            saveXmlConfig();
        }

        public static void setInnerTextBoolean(XmlNode node, bool value)
        {
            if (node == null) return;
            node.InnerText = value ? "1" : "0";
        }

        public static void setInnerText(XmlNode node, string value)
        {
            if (node == null) return;
            node.InnerText = value;
        }

        public static void saveXmlConfig()
        {
            (xmlConfig.SelectSingleNode("/Config") as XmlElement)?.SetAttribute("version", CONFIG_VERSION.ToString());
            //modules
            if (xmlConfig.SelectSingleNode("/Config/DeveloperMode") != null && developerMode)
            {
                setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/DeveloperMode"), developerMode);
            }
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMTournament/Enabled"), rbmTournamentEnabled);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMAI/Enabled"), rbmAiEnabled);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCombat/Enabled"), rbmCombatEnabled);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCampaign/Enabled"), rbmCampaignEnabled);
            //RBMCampaign
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopUpgradeCostMultiplier"), troopUpgradeCostMultiplier.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopUpgradeSpoilsLootMultiplier"), troopUpgradeSpoilsLootMultiplier.ToString(CultureInfo.InvariantCulture));
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopUpgradeRequireSupplyTown"), troopUpgradeRequireSupplyTown);
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopUpgradeSupplyRadius"), troopUpgradeSupplyRadius.ToString(CultureInfo.InvariantCulture));
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopUpgradeChargeMountValue"), troopUpgradeChargeMountValue);
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopLootPiecesPerMan"), troopLootPiecesPerMan.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopLootOverlookChancePerTier"), troopLootOverlookChancePerTier.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopWageTierBase"), troopWageTierBase.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopMaintenanceFraction"), troopMaintenanceFraction.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/MercenaryMaintenancePurseFraction"), mercenaryMaintenancePurseFraction.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/IndependentMaintenancePurseFraction"), independentMaintenancePurseFraction.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopSettlementFoodDays"), troopSettlementFoodDays.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/RecruitMaintenanceDays"), recruitMaintenanceDays.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopFoodWageFraction"), troopFoodWageFraction.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopSettlementFunWageFraction"), troopSettlementFunWageFraction.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopRaidSpoilsMultiplier"), troopRaidSpoilsMultiplier.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopLeaderSpoilsCutFraction"), troopLeaderSpoilsCutFraction.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopSpoilsCapDays"), troopSpoilsCapDays.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopLuxuryCooldownDays"), troopLuxuryCooldownDays.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopLuxurySpendChance"), troopLuxurySpendChance.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopSpoilsHealGoldPerTier"), troopSpoilsHealGoldPerTier.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopSpoilsHealFractionPerHour"), troopSpoilsHealFractionPerHour.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopFallenSpoilsCaptureFraction"), troopFallenSpoilsCaptureFraction.ToString(CultureInfo.InvariantCulture));
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCampaign/RealisticTradeGoodPrices"), realisticTradeGoodPrices);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCampaign/ShowInventoryItemWeight"), showInventoryItemWeight);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SpoilsLoggingEnabled"), spoilsLoggingEnabled);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SpoilsVerboseLoggingEnabled"), spoilsVerboseLoggingEnabled);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCampaign/EconomyLoggingEnabled"), economyLoggingEnabled);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SimulationEquipmentEnabled"), simulationEquipmentEnabled);
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SimulationEquipmentPowerWeight"), simulationEquipmentPowerWeight.ToString(CultureInfo.InvariantCulture));
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCampaign/StrategicPowerEnabled"), strategicPowerEnabled);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCampaign/StrategicPowerLogging"), strategicPowerLoggingEnabled);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SimulationAbsoluteDamage"), simulationAbsoluteDamage);
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SimulationAbsoluteScale"), simulationAbsoluteScale.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SimulationAbsoluteBlowCap"), simulationAbsoluteBlowCap.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SimulationShieldBlockChance"), simulationShieldBlockChance.ToString(CultureInfo.InvariantCulture));
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SimulationDefenseSystem"), simulationDefenseSystem);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SimulationArmTargeting"), simulationArmTargeting);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SimulationRangedMissEnabled"), simulationRangedMissEnabled);
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SimulationRangedMissChance"), simulationRangedMissChance.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SimulationRoundMinutes"), simulationRoundMinutes.ToString(CultureInfo.InvariantCulture));
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SimulationSiegeDefenderEnabled"), simulationSiegeDefenderEnabled);
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SimulationSiegeDefenderDefenseBonus"), simulationSiegeDefenderDefenseBonus.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SimulationSiegeRangedMissSkew"), simulationSiegeRangedMissSkew.ToString(CultureInfo.InvariantCulture));
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SimulationRoutEnabled"), simulationRoutEnabled);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SimulationPerkSystem"), simulationPerkSystem);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SimulationLoggingEnabled"), simulationLoggingEnabled);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SimulationLogHits"), simulationLogHits);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SpectateBattlesEnabled"), spectateBattlesEnabled);
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SpectateMinTroopsPerSide"), spectateMinTroopsPerSide.ToString(CultureInfo.InvariantCulture));
            //RBMAI
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMAI/HitStopEnabled"), hitStopEnabled);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMAI/PostureEnabled"), postureEnabled);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMAI/StaminaEnabled"), staminaEnabled);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMAI/PostureGUIEnabled"), postureGUIEnabled);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMAI/VanillaCombatAi"), vanillaCombatAi);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMAI/KeepBattleEnabled"), keepBattleEnabled);
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
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/ArmorStatusUIEnabled"), armorStatusUIEnabled);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/BattleHitLoggingEnabled"), battleHitLoggingEnabled);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/RealisticArrowArc"), realisticArrowArc);
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/ArmorMultiplier"), armorMultiplier.ToString());
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/ArmorPenetrationMessage"), armorPenetrationMessage);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/BetterArrowVisuals"), betterArrowVisuals);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/PassiveShoulderShields"), passiveShoulderShields);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/TroopOverhaulActive"), troopOverhaulActive);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/SneakAttackInstaKill"), sneakAttackInstaKill);
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/RealisticRangedReload"), realisticRangedReload.ToString());
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/MaceBluntModifier"), maceBluntModifier.ToString());
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/ArmorThresholdModifier"), armorThresholdModifier.ToString());
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/BluntTraumaBonus"), bluntTraumaBonus.ToString());
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/Global/ThrustMagnitudeModifier"), ThrustMagnitudeModifier.ToString());

            // Rebuild WeaponTypes section from scratch to handle any additions or missing nodes
            XmlNode weaponTypesNode = xmlConfig.SelectSingleNode("/Config/RBMCombat/WeaponTypes");
            if (weaponTypesNode != null)
            {
                weaponTypesNode.RemoveAll();
                foreach (RBMCombatConfigWeaponType wt in weaponTypesFactors)
                {
                    XmlElement wtNode = xmlConfig.CreateElement(wt.weaponType);
                    XmlElement cut = xmlConfig.CreateElement("ExtraBluntFactorCut"); cut.InnerText = wt.ExtraBluntFactorCut.ToString(); wtNode.AppendChild(cut);
                    XmlElement pierce = xmlConfig.CreateElement("ExtraBluntFactorPierce"); pierce.InnerText = wt.ExtraBluntFactorPierce.ToString(); wtNode.AppendChild(pierce);
                    XmlElement blunt = xmlConfig.CreateElement("ExtraBluntFactorBlunt"); blunt.InnerText = wt.ExtraBluntFactorBlunt.ToString(); wtNode.AppendChild(blunt);
                    XmlElement atPierce = xmlConfig.CreateElement("ExtraArmorThresholdFactorPierce"); atPierce.InnerText = wt.ExtraArmorThresholdFactorPierce.ToString(); wtNode.AppendChild(atPierce);
                    XmlElement atCut = xmlConfig.CreateElement("ExtraArmorThresholdFactorCut"); atCut.InnerText = wt.ExtraArmorThresholdFactorCut.ToString(); wtNode.AppendChild(atCut);
                    XmlElement absorb = xmlConfig.CreateElement("ExtraArmorSkillDamageAbsorb"); absorb.InnerText = wt.ExtraArmorSkillDamageAbsorb.ToString(); wtNode.AppendChild(absorb);
                    weaponTypesNode.AppendChild(wtNode);
                }
            }

            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/PriceModifiers/ArmorPriceModifier"), priceMultipliers.ArmorPriceModifier.ToString());
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/PriceModifiers/WeaponPriceModifier"), priceMultipliers.WeaponPriceModifier.ToString());
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/PriceModifiers/HorsePriceModifier"), priceMultipliers.HorsePriceModifier.ToString());
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCombat/PriceModifiers/TradePriceModifier"), priceMultipliers.TradePriceModifier.ToString());

            xmlConfig.Save(Utilities.GetConfigFilePath());
        }

        public static RBMCombatConfigWeaponType getWeaponTypeFactors(string weaponType)
        {
            foreach (RBMCombatConfigWeaponType weaponTypeFactors in weaponTypesFactors)
            {
                if (weaponTypeFactors.weaponType.Equals(weaponType))
                {
                    return weaponTypeFactors;
                }
            }
            return null;
        }
    }
}
