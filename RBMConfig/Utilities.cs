using System;
using System.Collections.Generic;
using System.Xml;

namespace RBMConfig
{
    public static class Utilities
    {
        public static string GetConfigFilePath()
        {
            return System.IO.Path.Combine(GetConfigFolderPath(), "config.xml");
        }

        public static string GetConfigFolderPath()
        {
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal),
             "Mount and Blade II Bannerlord", "Configs", "RBM");
        }

        public static string GetCustomGamePresetFolderPath()
        {
            return System.IO.Path.Combine(GetConfigFolderPath(), "CustomBattlePresets");
        }

        public static void createWeaponTypesFactors(ref List<RBMCombatConfigWeaponType> weaponTypesFactors)
        {
            weaponTypesFactors.Add(new RBMCombatConfigWeaponType(
                weaponType: "Dagger",
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
               ExtraBluntFactorCut: 0.3f,
               ExtraBluntFactorPierce: 0.35f,
               ExtraBluntFactorBlunt: 1f,
               ExtraArmorThresholdFactorPierce: 6f,
               ExtraArmorThresholdFactorCut: 10f,
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
            Config.SetAttribute("version", RBMConfig.CONFIG_VERSION.ToString());

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
            XmlElement StaminaEnabled = xmlconfig.CreateElement("StaminaEnabled");
            StaminaEnabled.InnerText = RBMConfig.staminaEnabled ? "1" : "0";
            XmlElement PostureGUIEnabled = xmlconfig.CreateElement("PostureGUIEnabled");
            PostureGUIEnabled.InnerText = RBMConfig.postureGUIEnabled ? "1" : "0";
            XmlElement VanillaCombatAi = xmlconfig.CreateElement("VanillaCombatAi");
            VanillaCombatAi.InnerText = RBMConfig.vanillaCombatAi ? "1" : "0";
            XmlElement KeepBattleEnabled = xmlconfig.CreateElement("KeepBattleEnabled");
            KeepBattleEnabled.InnerText = RBMConfig.keepBattleEnabled ? "1" : "0";
            XmlElement PlayerPostureMultiplier = xmlconfig.CreateElement("PlayerPostureMultiplier");
            PlayerPostureMultiplier.InnerText = getPostureMultiplier(RBMConfig.playerPostureMultiplier);

            RBMAI.AppendChild(RBMAIEnabled);
            RBMAI.AppendChild(PostureEnabled);
            RBMAI.AppendChild(StaminaEnabled);
            RBMAI.AppendChild(PlayerPostureMultiplier);
            RBMAI.AppendChild(PostureGUIEnabled);
            RBMAI.AppendChild(VanillaCombatAi);
            RBMAI.AppendChild(KeepBattleEnabled);
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

            //RBM campaign
            XmlElement RBMCampaign = xmlconfig.CreateElement("RBMCampaign");

            XmlElement RBMCampaignEnabled = xmlconfig.CreateElement("Enabled");
            RBMCampaignEnabled.InnerText = RBMConfig.rbmCampaignEnabled ? "1" : "0";
            XmlElement TroopUpgradeCostMultiplier = xmlconfig.CreateElement("TroopUpgradeCostMultiplier");
            TroopUpgradeCostMultiplier.InnerText = RBMConfig.troopUpgradeCostMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture);
            XmlElement TroopUpgradeSpoilsLootMultiplier = xmlconfig.CreateElement("TroopUpgradeSpoilsLootMultiplier");
            TroopUpgradeSpoilsLootMultiplier.InnerText = RBMConfig.troopUpgradeSpoilsLootMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture);
            XmlElement TroopUpgradeRequireSupplyTown = xmlconfig.CreateElement("TroopUpgradeRequireSupplyTown");
            TroopUpgradeRequireSupplyTown.InnerText = RBMConfig.troopUpgradeRequireSupplyTown ? "1" : "0";
            XmlElement TroopUpgradeSupplyRadius = xmlconfig.CreateElement("TroopUpgradeSupplyRadius");
            TroopUpgradeSupplyRadius.InnerText = RBMConfig.troopUpgradeSupplyRadius.ToString(System.Globalization.CultureInfo.InvariantCulture);
            XmlElement TroopUpgradeChargeMountValue = xmlconfig.CreateElement("TroopUpgradeChargeMountValue");
            TroopUpgradeChargeMountValue.InnerText = RBMConfig.troopUpgradeChargeMountValue ? "1" : "0";
            XmlElement TroopLootPiecesPerMan = xmlconfig.CreateElement("TroopLootPiecesPerMan");
            TroopLootPiecesPerMan.InnerText = RBMConfig.troopLootPiecesPerMan.ToString(System.Globalization.CultureInfo.InvariantCulture);
            XmlElement TroopLootOverlookChancePerTier = xmlconfig.CreateElement("TroopLootOverlookChancePerTier");
            TroopLootOverlookChancePerTier.InnerText = RBMConfig.troopLootOverlookChancePerTier.ToString(System.Globalization.CultureInfo.InvariantCulture);
            XmlElement TroopWageTierBase = xmlconfig.CreateElement("TroopWageTierBase");
            TroopWageTierBase.InnerText = RBMConfig.troopWageTierBase.ToString(System.Globalization.CultureInfo.InvariantCulture);
            XmlElement TroopMaintenanceFraction = xmlconfig.CreateElement("TroopMaintenanceFraction");
            TroopMaintenanceFraction.InnerText = RBMConfig.troopMaintenanceFraction.ToString(System.Globalization.CultureInfo.InvariantCulture);
            XmlElement TroopSettlementFoodDays = xmlconfig.CreateElement("TroopSettlementFoodDays");
            TroopSettlementFoodDays.InnerText = RBMConfig.troopSettlementFoodDays.ToString(System.Globalization.CultureInfo.InvariantCulture);
            XmlElement RecruitMaintenanceDays = xmlconfig.CreateElement("RecruitMaintenanceDays");
            RecruitMaintenanceDays.InnerText = RBMConfig.recruitMaintenanceDays.ToString(System.Globalization.CultureInfo.InvariantCulture);
            XmlElement TroopFoodWageFraction = xmlconfig.CreateElement("TroopFoodWageFraction");
            TroopFoodWageFraction.InnerText = RBMConfig.troopFoodWageFraction.ToString(System.Globalization.CultureInfo.InvariantCulture);
            XmlElement TroopSettlementFunWageFraction = xmlconfig.CreateElement("TroopSettlementFunWageFraction");
            TroopSettlementFunWageFraction.InnerText = RBMConfig.troopSettlementFunWageFraction.ToString(System.Globalization.CultureInfo.InvariantCulture);
            XmlElement SettlementProsperityPerGoldSpent = xmlconfig.CreateElement("SettlementProsperityPerGoldSpent");
            SettlementProsperityPerGoldSpent.InnerText = RBMConfig.settlementProsperityPerGoldSpent.ToString(System.Globalization.CultureInfo.InvariantCulture);
            XmlElement MaintenanceProsperityFraction = xmlconfig.CreateElement("MaintenanceProsperityFraction");
            MaintenanceProsperityFraction.InnerText = RBMConfig.maintenanceProsperityFraction.ToString(System.Globalization.CultureInfo.InvariantCulture);
            XmlElement MilitiaWageModifier = xmlconfig.CreateElement("MilitiaWageModifier");
            MilitiaWageModifier.InnerText = RBMConfig.militiaWageModifier.ToString(System.Globalization.CultureInfo.InvariantCulture);
            XmlElement TroopRaidSpoilsMultiplier = xmlconfig.CreateElement("TroopRaidSpoilsMultiplier");
            TroopRaidSpoilsMultiplier.InnerText = RBMConfig.troopRaidSpoilsMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture);
            XmlElement TroopLeaderSpoilsCutFraction = xmlconfig.CreateElement("TroopLeaderSpoilsCutFraction");
            TroopLeaderSpoilsCutFraction.InnerText = RBMConfig.troopLeaderSpoilsCutFraction.ToString(System.Globalization.CultureInfo.InvariantCulture);
            XmlElement TroopSpoilsCapDays = xmlconfig.CreateElement("TroopSpoilsCapDays");
            TroopSpoilsCapDays.InnerText = RBMConfig.troopSpoilsCapDays.ToString(System.Globalization.CultureInfo.InvariantCulture);
            XmlElement TroopLuxuryCooldownDays = xmlconfig.CreateElement("TroopLuxuryCooldownDays");
            TroopLuxuryCooldownDays.InnerText = RBMConfig.troopLuxuryCooldownDays.ToString(System.Globalization.CultureInfo.InvariantCulture);
            XmlElement TroopLuxurySpendChance = xmlconfig.CreateElement("TroopLuxurySpendChance");
            TroopLuxurySpendChance.InnerText = RBMConfig.troopLuxurySpendChance.ToString(System.Globalization.CultureInfo.InvariantCulture);
            XmlElement TroopSpoilsHealGoldPerTier = xmlconfig.CreateElement("TroopSpoilsHealGoldPerTier");
            TroopSpoilsHealGoldPerTier.InnerText = RBMConfig.troopSpoilsHealGoldPerTier.ToString(System.Globalization.CultureInfo.InvariantCulture);
            XmlElement TroopSpoilsHealFractionPerHour = xmlconfig.CreateElement("TroopSpoilsHealFractionPerHour");
            TroopSpoilsHealFractionPerHour.InnerText = RBMConfig.troopSpoilsHealFractionPerHour.ToString(System.Globalization.CultureInfo.InvariantCulture);
            XmlElement TroopFallenSpoilsCaptureFraction = xmlconfig.CreateElement("TroopFallenSpoilsCaptureFraction");
            TroopFallenSpoilsCaptureFraction.InnerText = RBMConfig.troopFallenSpoilsCaptureFraction.ToString(System.Globalization.CultureInfo.InvariantCulture);

            RBMCampaign.AppendChild(RBMCampaignEnabled);
            RBMCampaign.AppendChild(TroopUpgradeCostMultiplier);
            XmlElement SpoilsLoggingEnabled = xmlconfig.CreateElement("SpoilsLoggingEnabled");
            SpoilsLoggingEnabled.InnerText = RBMConfig.spoilsLoggingEnabled ? "1" : "0";
            XmlElement SpoilsVerboseLoggingEnabled = xmlconfig.CreateElement("SpoilsVerboseLoggingEnabled");
            SpoilsVerboseLoggingEnabled.InnerText = RBMConfig.spoilsVerboseLoggingEnabled ? "1" : "0";

            RBMCampaign.AppendChild(TroopUpgradeSpoilsLootMultiplier);
            RBMCampaign.AppendChild(TroopUpgradeRequireSupplyTown);
            RBMCampaign.AppendChild(TroopUpgradeSupplyRadius);
            RBMCampaign.AppendChild(TroopUpgradeChargeMountValue);
            RBMCampaign.AppendChild(TroopLootPiecesPerMan);
            RBMCampaign.AppendChild(TroopLootOverlookChancePerTier);
            RBMCampaign.AppendChild(TroopWageTierBase);
            RBMCampaign.AppendChild(TroopMaintenanceFraction);
            RBMCampaign.AppendChild(TroopSettlementFoodDays);
            RBMCampaign.AppendChild(RecruitMaintenanceDays);
            RBMCampaign.AppendChild(TroopFoodWageFraction);
            RBMCampaign.AppendChild(TroopSettlementFunWageFraction);
            RBMCampaign.AppendChild(SettlementProsperityPerGoldSpent);
            RBMCampaign.AppendChild(MaintenanceProsperityFraction);
            RBMCampaign.AppendChild(MilitiaWageModifier);
            RBMCampaign.AppendChild(TroopRaidSpoilsMultiplier);
            RBMCampaign.AppendChild(TroopLeaderSpoilsCutFraction);
            RBMCampaign.AppendChild(TroopSpoilsCapDays);
            RBMCampaign.AppendChild(TroopLuxuryCooldownDays);
            RBMCampaign.AppendChild(TroopLuxurySpendChance);
            RBMCampaign.AppendChild(TroopSpoilsHealGoldPerTier);
            RBMCampaign.AppendChild(TroopSpoilsHealFractionPerHour);
            RBMCampaign.AppendChild(TroopFallenSpoilsCaptureFraction);
            RBMCampaign.AppendChild(SpoilsLoggingEnabled);
            RBMCampaign.AppendChild(SpoilsVerboseLoggingEnabled);
            Config.AppendChild(RBMCampaign);

            xmlconfig.AppendChild(Config);
            xmlconfig.Save(GetConfigFilePath());
        }
    }
}