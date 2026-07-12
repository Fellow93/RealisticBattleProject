using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace RBMConfig
{
    public static class RBMConfig
    {
        // Bump this to force all users to reset to defaults on next launch.
        public const int CONFIG_VERSION = 2;

        public static XmlDocument xmlConfig = new XmlDocument();
        public static float ThrustMagnitudeModifier = 0.05f;
        public static float OneHandedThrustDamageBonus = 20f;
        public static float TwoHandedThrustDamageBonus = 20f;

        //modules
        public static bool rbmTournamentEnabled = true;

        public static bool rbmAiEnabled = true;
        public static bool rbmCombatEnabled = true;
        public static bool rbmCampaignEnabled = true;
        public static bool developerMode = false;

        //RBMCampaign
        // An upgrade costs what the better kit is worth over the old, scaled by this, and it costs the
        // same number whether it is paid in gold or in spoils. One point of spoils is therefore one
        // gold piece, and everything a troop earns or spends can be quoted in either without conversion.
        // Zero makes upgrades free and disables the spoils system with them.
        public static float troopUpgradeCostMultiplier = 1f;

        public static float troopUpgradeSpoilsLootMultiplier = 1f;

        // SupplyTown gate: troops may only be upgraded while a friendly or neutral town is within
        // TroopUpgradeSupplyRadius map units of the party, and the upgrade buys its kit from that town.
        // False (0) restores upgrade-anywhere and the plain gold sink.
        public static bool troopUpgradeRequireSupplyTown = true;

        // How near, in campaign-map units, a friendly town must be to supply a party's upgrades. Roughly
        // a short march at the default; raise it to be lenient, lower it to force troops back to town.
        public static float troopUpgradeSupplyRadius = 30f;

        // Pieces of kit one man carries off a battlefield, however much of it he sees lying there.
        public static int troopLootPiecesPerMan = 3;

        // Chance a troop overlooks a piece of kit for each tier it sits beneath his own, compounded:
        // at 0.5 a veteran sees half of what is one tier under him and a quarter of what is two. He
        // never overlooks kit of his own tier or better. At 1 he sees nothing beneath him at all.
        public static float troopLootOverlookChancePerTier = 0.5f;

        // A troop's daily wage is this flat base value multiplied by its tier, replacing the vanilla
        // per-tier wage the game hands out. A base of zero leaves the vanilla wage untouched.
        public static int troopWageTierBase = 50;

        // The daily cost of keeping a soldier in the field, as a share of his whole kit's worth -- his
        // gear, his horse and its armour alike. A lancer in full harness costs more to maintain than a
        // spearman, in proportion to what he carries. Paid first out of the stack's own spoils; whatever
        // the purse cannot cover falls to the party leader, out of his gold. Zero stops maintenance.
        public static float troopMaintenanceFraction = 0.005f;

        // How long a stack's men stay fed on one visit to a settlement. They buy exactly the food they
        // will eat over that span at the game's own rate of one item per twenty men per day, so at 20
        // days each man carries off one item. Zero stops troops buying food.
        public static int troopSettlementFoodDays = 20;

        // Share of a day's wage a man will lay out on a day's rations before he calls it extravagant.
        // Nothing else sets a soldier's taste, so raising this feeds veterans on meat and cheese while
        // recruits still buy grain. Zero leaves everyone eating whatever is cheapest.
        public static float troopFoodWageFraction = 0.5f;

        // A day's wage a stack drinks and gambles away for each day it sits in a settlement. At 1 the
        // men spend everything the day paid them; above that they eat into what they came in with, so
        // a long stay in town costs a stack the kit it was saving for.
        public static float troopSettlementFunWageFraction = 1.5f;

        // Prosperity, or hearth in a village, a settlement gains per gold its visitors spend there.
        public static float settlementProsperityPerGoldSpent = 0.02f;

        // Share of their gear-based wage a settlement's militia actually cost the place that keeps them.
        // Militia are part-time defenders, so they draw only a fraction of a standing soldier's pay
        // when their upkeep is drawn from Prosperity or Hearth. Zero makes militia free to garrison.
        public static float militiaWageModifier = 0.2f;

        // Share of a sacked village's plundered wealth its soldiers pocket as spoils, on top of the
        // goods the party carts off. Scaled against how much of the village the raid actually stripped.
        // Zero leaves raiding paying the party but not its men.
        public static float troopRaidSpoilsMultiplier = 0.25f;

        // The base share of the spoils a party's men gather -- off a battlefield, a raid or a sack -- that
        // their leader skims into his own purse as gold before the rest settles into the stacks: a
        // commander's cut. Multiplied by the leader's clan tier plus one, so a tier-0 or clanless leader
        // takes this share once over and a tier-6 dynasty seven times it. Drawn out of the same purses the
        // gather just filled, so it moves coin from the men's pool into their keeper's treasury rather than
        // minting any. 0 leaves the men all they take.
        public static float troopLeaderSpoilsCutFraction = 0.05f;

        // The war chest a man keeps back from the spill, per tier he holds: a tier 6 veteran keeps six
        // times what this sets, a tier 1 recruit keeps one. On top of what his stack needs for its own
        // upgrades, so a stack still saving for better kit never has it swept away.
        public static int troopSpoilsWarChestGoldPerTier = 25;

        // Days a stack waits after buying a luxury before it will indulge again, so the splurge stays
        // an occasional treat rather than a daily habit. Kept per stack. Zero lets it buy on every roll.
        public static int troopLuxuryCooldownDays = 20;

        // The chance, each hour a stack idles in a settlement holding more spoils than its cap, that it
        // buys a luxury off the market. Small: over a full day's stay the odds add up. Zero stops it.
        public static float troopLuxurySpendChance = 0.02f;

        // Gold a wounded man's stack pays the local surgeons, per tier he holds, to mend him faster than
        // he would heal on the march while the stack rests in a settlement. A veteran costs more to patch
        // up than a recruit, and his richer purse can bear it. Drawn from the stack's own spoils and left
        // in the settlement the way carousing is. Zero stops troops paying to heal.
        public static int troopSpoilsHealGoldPerTier = 10;

        // The most of a stack's wounded that paid healing can mend in a single hour, so even a deep purse
        // buys a faster recovery rather than an instant one. A stay in town still takes a bad wounding a
        // while to clear; it just costs the stack its savings.
        public static float troopSpoilsHealFractionPerHour = 0.05f;

        // Share of a beaten enemy's fallen-and-wounded spoils the victors strip off the field; the rest
        // is trampled and lost. Split across the winning parties by their part in the battle, and within
        // a party across its stacks by weight -- men times tier -- so veterans take the larger cut. Zero
        // leaves the dead's purse on the field.
        public static float troopFallenSpoilsCaptureFraction = 0.75f;

        // Writes every spoils pool change, loot award and upgrade to rbm_spoils.log next to this config.
        public static bool spoilsLoggingEnabled = true;

        // Whether that log carries the full per-stack detail or only the party-level summaries. On, it
        // reads as now: a line per stack. Off, individual-soldier lines are dropped and only what each
        // party did is kept. No effect unless logging above is on.
        public static bool spoilsVerboseLoggingEnabled = true;

        //RBMAI
        public static bool hitStopEnabled = true;

        public static bool postureEnabled = true;
        public static bool staminaEnabled = true;

        public static float playerPostureMultiplier = 1f;
        public static bool postureGUIEnabled = true;
        public static bool vanillaCombatAi = false;
        public static bool keepBattleEnabled = false;

        //RBMCombat
        public static bool realisticArrowArc = false;

        public static bool armorStatusUIEnabled = true;
        public static float armorMultiplier = 2f;
        public static bool armorPenetrationMessage = false;
        public static bool betterArrowVisuals = true;
        public static bool passiveShoulderShields = false;
        public static bool troopOverhaulActive = true;
        public static string realisticRangedReload = "2";
        public static float maceBluntModifier = 1f;
        public static float armorThresholdModifier = 1f;
        public static float bluntTraumaBonus = 0f;

        public static bool sneakAttackInstaKill = false;

        public static RBMCombatConfigPriceMultipliers priceMultipliers = new RBMCombatConfigPriceMultipliers();
        public static List<RBMCombatConfigWeaponType> weaponTypesFactors = new List<RBMCombatConfigWeaponType>();

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
            troopLootPiecesPerMan = int.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopLootPiecesPerMan", "3"), CultureInfo.InvariantCulture);
            troopLootOverlookChancePerTier = float.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopLootOverlookChancePerTier", "0.5"), CultureInfo.InvariantCulture);
            troopWageTierBase = int.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopWageTierBase", "50"), CultureInfo.InvariantCulture);
            troopMaintenanceFraction = float.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopMaintenanceFraction", "0.005"), CultureInfo.InvariantCulture);
            troopSettlementFoodDays = int.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopSettlementFoodDays", "20"), CultureInfo.InvariantCulture);
            troopFoodWageFraction = float.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopFoodWageFraction", "0.5"), CultureInfo.InvariantCulture);
            troopSettlementFunWageFraction = float.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopSettlementFunWageFraction", "1.5"), CultureInfo.InvariantCulture);
            settlementProsperityPerGoldSpent = float.Parse(ReadOrCreate("/Config/RBMCampaign", "SettlementProsperityPerGoldSpent", "0.02"), CultureInfo.InvariantCulture);
            militiaWageModifier = float.Parse(ReadOrCreate("/Config/RBMCampaign", "MilitiaWageModifier", "0.2"), CultureInfo.InvariantCulture);
            troopRaidSpoilsMultiplier = float.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopRaidSpoilsMultiplier", "0.25"), CultureInfo.InvariantCulture);
            troopLeaderSpoilsCutFraction = float.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopLeaderSpoilsCutFraction", "0.05"), CultureInfo.InvariantCulture);
            troopSpoilsWarChestGoldPerTier = int.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopSpoilsWarChestGoldPerTier", "25"), CultureInfo.InvariantCulture);
            troopLuxuryCooldownDays = int.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopLuxuryCooldownDays", "20"), CultureInfo.InvariantCulture);
            troopLuxurySpendChance = float.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopLuxurySpendChance", "0.02"), CultureInfo.InvariantCulture);
            troopSpoilsHealGoldPerTier = int.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopSpoilsHealGoldPerTier", "10"), CultureInfo.InvariantCulture);
            troopSpoilsHealFractionPerHour = float.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopSpoilsHealFractionPerHour", "0.05"), CultureInfo.InvariantCulture);
            troopFallenSpoilsCaptureFraction = float.Parse(ReadOrCreate("/Config/RBMCampaign", "TroopFallenSpoilsCaptureFraction", "0.75"), CultureInfo.InvariantCulture);
            spoilsLoggingEnabled = ReadOrCreate("/Config/RBMCampaign", "SpoilsLoggingEnabled", "1").Equals("1");
            spoilsVerboseLoggingEnabled = ReadOrCreate("/Config/RBMCampaign", "SpoilsVerboseLoggingEnabled", "1").Equals("1");

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
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopLootPiecesPerMan"), troopLootPiecesPerMan.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopLootOverlookChancePerTier"), troopLootOverlookChancePerTier.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopWageTierBase"), troopWageTierBase.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopMaintenanceFraction"), troopMaintenanceFraction.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopSettlementFoodDays"), troopSettlementFoodDays.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopFoodWageFraction"), troopFoodWageFraction.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopSettlementFunWageFraction"), troopSettlementFunWageFraction.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SettlementProsperityPerGoldSpent"), settlementProsperityPerGoldSpent.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/MilitiaWageModifier"), militiaWageModifier.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopRaidSpoilsMultiplier"), troopRaidSpoilsMultiplier.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopLeaderSpoilsCutFraction"), troopLeaderSpoilsCutFraction.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopSpoilsWarChestGoldPerTier"), troopSpoilsWarChestGoldPerTier.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopLuxuryCooldownDays"), troopLuxuryCooldownDays.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopLuxurySpendChance"), troopLuxurySpendChance.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopSpoilsHealGoldPerTier"), troopSpoilsHealGoldPerTier.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopSpoilsHealFractionPerHour"), troopSpoilsHealFractionPerHour.ToString(CultureInfo.InvariantCulture));
            setInnerText(xmlConfig.SelectSingleNode("/Config/RBMCampaign/TroopFallenSpoilsCaptureFraction"), troopFallenSpoilsCaptureFraction.ToString(CultureInfo.InvariantCulture));
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SpoilsLoggingEnabled"), spoilsLoggingEnabled);
            setInnerTextBoolean(xmlConfig.SelectSingleNode("/Config/RBMCampaign/SpoilsVerboseLoggingEnabled"), spoilsVerboseLoggingEnabled);
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