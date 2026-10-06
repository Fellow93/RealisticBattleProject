using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace RBMConfig
{
    /// <summary>
    /// Hero perks granted to regular (non-hero) troops, read from ModuleData/rbm_troop_perks.xml. Lives here,
    /// not in RBMCombat, because both RBMCombat (the battle effect, the troop tooltip and encyclopedia rows) and
    /// RBMCampaign (the party-screen troop hover) need it and neither references the other; RBMConfig is the one
    /// module both reference, and it already references TaleWorlds.CampaignSystem.
    ///
    /// The file is plain data, not an MBObject XML: it is not registered in SubModule.xml and is read directly
    /// by <see cref="Load"/>, which RBM's SubModule.OnGameInitializationFinished calls once per campaign (new game
    /// and save load alike, after every CharacterObject and PerkObject exists). The map is rebuilt from scratch
    /// on every call and cleared on game end, so nothing from one campaign outlives it.
    /// </summary>
    public static class TroopPerks
    {
        public const string FileName = "rbm_troop_perks.xml";

        /// <summary>Why a perk in <see cref="NoTroopEffectPerkIds"/> does nothing for a troop in battle.</summary>
        private enum NoEffectReason
        {
            /// <summary>The perk's personal half is only checked for heroes (or only for the player).</summary>
            HeroOnly,
            /// <summary>The personal half only acts on the campaign map or on a hero's own campaign actions.</summary>
            CampaignOnly,
            /// <summary>No mission code reads the perk for an AI agent at all.</summary>
            NoCheckFound,
            /// <summary>RBM overwrites the value the perk changes, for every agent (heroes too).</summary>
            RbmBypassed,
        }

        /// <summary>
        /// Perks whose personal effect never reaches a regular troop in battle, so listing them for a troop does
        /// nothing (they are still loaded and listed in the troop's tooltip; <see cref="LoadFile"/> only logs them).
        /// Source: the troop-perk audit in tools/TroopPerkEditor/audit/ (README.md there has the summary, the
        /// per-group .json files the call sites), verified against v1.5.4 and this RBM tree on 2026-10-06: every
        /// perk with verdict hero-only, campaign-only, no-check-found or rbm-bypassed. Hard-coded on purpose: the
        /// runtime never reads tools/. Keep it in step with the audit after a game update; the editor's
        /// Build-TroopPerkData.ps1 parses these entries and warns when they disagree with the audit.
        /// Perks with no personal role at all are not listed here; the loader detects those from the PerkObject.
        /// </summary>
        private static readonly Dictionary<string, NoEffectReason> NoTroopEffectPerkIds = new Dictionary<string, NoEffectReason>(StringComparer.Ordinal)
        {
            // hero-only: checked through agent.IsHero, a Hero object, or only for the player's agent
            { "BowHorseMaster", NoEffectReason.HeroOnly },                // agent flag set only in the agent.IsHero branch of SandboxAgentStatCalculateModel.InitializeAgentStats
            { "CrossbowMountedCrossbowman", NoEffectReason.HeroOnly },    // same agent.IsHero branch
            { "TwoHandedProjectileDeflection", NoEffectReason.HeroOnly }, // same agent.IsHero branch
            { "TwoHandedBaptisedInBlood", NoEffectReason.HeroOnly },      // read via HeroObject in BattleCampaignBehavior.OnHeroCombatHit, raised for heroes only
            { "ThrowingRunningThrow", NoEffectReason.HeroOnly },          // vanilla CalculateStrikeMagnitudeForMissile and RBM ApplyRunningThrowPerk require a hero
            { "BowEagleEye", NoEffectReason.HeroOnly },                   // camera zoom, only read for Mission.MainAgent
            { "ThrowingFocus", NoEffectReason.HeroOnly },                 // camera zoom, only read for Mission.MainAgent
            { "RidingWellStraped", NoEffectReason.HeroOnly },             // only the main hero's own horse death/lame roll (Hero.MainHero)
            { "AthleticsDurable", NoEffectReason.HeroOnly },              // +1 Endurance granted to the hero when learned, never checked in battle
            { "AthleticsSteady", NoEffectReason.HeroOnly },               // +1 Control granted to the hero when learned
            { "AthleticsStrong", NoEffectReason.HeroOnly },               // +1 Vigor granted to the hero when learned
            { "VigorousSmith", NoEffectReason.HeroOnly },                 // +1 Vigor granted to the hero when learned
            { "StrongSmith", NoEffectReason.HeroOnly },                   // +1 Control granted to the hero when learned
            { "EnduringSmith", NoEffectReason.HeroOnly },                 // +1 Endurance granted to the hero when learned
            { "WeaponMasterSmith", NoEffectReason.HeroOnly },             // +1 One/Two Handed focus granted to the hero when learned
            { "MedicineMinisterOfHealth", NoEffectReason.HeroOnly },      // GetEffectiveMaxHealth reads the party leader hero's perk, not the troop's
            { "ShipwrightsInsight", NoEffectReason.HeroOnly },            // War Sails: NavalMissionSiegeEngineCalculationModel.CalculateDamage requires attackerAgent.IsHero

            // no-check-found: nothing reads it for an AI agent
            { "CrossbowLongShots", NoEffectReason.NoCheckFound },         // crossbow zoom, only the player's own camera

            // rbm-bypassed: RBM removes the effect for everyone
            { "OneHandedArrowCatcher", NoEffectReason.RbmBypassed },      // RBMCombat fixes AttributeShieldMissileCollisionBodySizeAdder at 0.01 (DamageRework.HitReaction.cs)

            // campaign-only, Smithing: smithing screen (refining, stamina, part unlocks, crafted quality) for the smith hero
            { "IronYield", NoEffectReason.CampaignOnly },
            { "CharcoalYield", NoEffectReason.CampaignOnly },
            { "SteelMaker", NoEffectReason.CampaignOnly },
            { "SteelMaker2", NoEffectReason.CampaignOnly },
            { "SteelMaker3", NoEffectReason.CampaignOnly },
            { "CuriousSmelter", NoEffectReason.CampaignOnly },
            { "CuriousSmith", NoEffectReason.CampaignOnly },
            { "PracticalRefiner", NoEffectReason.CampaignOnly },
            { "PracticalSmelter", NoEffectReason.CampaignOnly },
            { "PracticalSmith", NoEffectReason.CampaignOnly },
            { "ExperiencedSmith", NoEffectReason.CampaignOnly },
            { "MasterSmith", NoEffectReason.CampaignOnly },
            { "LegendarySmith", NoEffectReason.CampaignOnly },
            // campaign-only, Athletics: persuasion chance / crafting stamina of a hero
            { "AthleticsImposingStature", NoEffectReason.CampaignOnly },
            { "AthleticsStamina", NoEffectReason.CampaignOnly },
            // campaign-only, Tactics: influence from won sieges
            { "TacticsBesieged", NoEffectReason.CampaignOnly },
            // campaign-only, Roguery: sneaking, betting, ransom, surrender, crime, smuggling, loot of the hero/leader
            { "RogueryTwoFaced", NoEffectReason.CampaignOnly },
            { "RogueryDeepPockets", NoEffectReason.CampaignOnly },
            { "RogueryManhunter", NoEffectReason.CampaignOnly },
            { "RogueryScarface", NoEffectReason.CampaignOnly },
            { "RogueryWhiteLies", NoEffectReason.CampaignOnly },
            { "RoguerySmugglerConnections", NoEffectReason.CampaignOnly },
            { "RogueryRogueExtraordinaire", NoEffectReason.CampaignOnly },
            // campaign-only, Charm: relation, persuasion, renown, influence, barter of the hero
            { "CharmVirile", NoEffectReason.CampaignOnly },
            { "CharmSelfPromoter", NoEffectReason.CampaignOnly },
            { "CharmOratory", NoEffectReason.CampaignOnly },
            { "CharmWarlord", NoEffectReason.CampaignOnly },
            { "CharmForgivableGrievances", NoEffectReason.CampaignOnly },
            { "CharmMeaningfulFavors", NoEffectReason.CampaignOnly },
            { "CharmInBloom", NoEffectReason.CampaignOnly },
            { "CharmYoungAndRespectful", NoEffectReason.CampaignOnly },
            { "CharmFlexibleEthics", NoEffectReason.CampaignOnly },
            { "CharmEffortForThePeople", NoEffectReason.CampaignOnly },
            { "CharmSlickNegotiator", NoEffectReason.CampaignOnly },
            { "CharmGoodNatured", NoEffectReason.CampaignOnly },
            { "CharmTribute", NoEffectReason.CampaignOnly },
            { "CharmMoralLeader", NoEffectReason.CampaignOnly },
            { "CharmNaturalLeader", NoEffectReason.CampaignOnly },
            { "CharmParade", NoEffectReason.CampaignOnly },
            { "CharmCamaraderie", NoEffectReason.CampaignOnly },
            { "CharmImmortalCharm", NoEffectReason.CampaignOnly },
            // campaign-only, Leadership: town security, renown, companion limit of the hero/leader
            { "LeadershipPresence", NoEffectReason.CampaignOnly },
            { "LeadershipFamousCommander", NoEffectReason.CampaignOnly },
            { "LeadershipWePledgeOurSwords", NoEffectReason.CampaignOnly },
            // campaign-only, Trade: inventory-screen markers, rumors, barter, hiring and ransom costs of the hero
            { "TradeAppraiser", NoEffectReason.CampaignOnly },
            { "TradeWholeSeller", NoEffectReason.CampaignOnly },
            { "TradeCaravanMaster", NoEffectReason.CampaignOnly },
            { "TradeMarketDealer", NoEffectReason.CampaignOnly },
            { "TradeTravelingRumors", NoEffectReason.CampaignOnly },
            { "TradeLocalConnection", NoEffectReason.CampaignOnly },
            { "TradeDistributedGoods", NoEffectReason.CampaignOnly },
            { "TradeTollgates", NoEffectReason.CampaignOnly },
            { "TradeSwordForBarter", NoEffectReason.CampaignOnly },
            { "TradeSelfMadeMan", NoEffectReason.CampaignOnly },
            { "TradeSilverTongue", NoEffectReason.CampaignOnly },
            { "TradeManOfMeans", NoEffectReason.CampaignOnly },
            { "TradeEverythingHasAPrice", NoEffectReason.CampaignOnly },
            // campaign-only, Steward: workshop production of the owner
            { "StewardSweatshops", NoEffectReason.CampaignOnly },
            // campaign-only, Medicine: post-battle healing of a hero, daily relation, old-age death
            { "MedicineWalkItOff", NoEffectReason.CampaignOnly },
            { "MedicineBestMedicine", NoEffectReason.CampaignOnly },
            { "MedicineGoodLodging", NoEffectReason.CampaignOnly },
            { "MedicineCheatDeath", NoEffectReason.CampaignOnly },
            // campaign-only, War Sails: pirate recruiting / post-battle healing of a hero
            { "Arr", NoEffectReason.CampaignOnly },
            { "Resilience", NoEffectReason.CampaignOnly },
        };

        private static readonly Dictionary<CharacterObject, HashSet<PerkObject>> EmptyMap = new Dictionary<CharacterObject, HashSet<PerkObject>>();
        private static readonly Dictionary<CharacterObject, List<PerkObject>> EmptyOrdered = new Dictionary<CharacterObject, List<PerkObject>>();

        // Swapped whole (built aside, then assigned) so a reader on another thread never sees a half-built map.
        // GetPerkValue is hot and may be called off the main thread during missions; both are read-only there.
        private static volatile Dictionary<CharacterObject, HashSet<PerkObject>> _map = EmptyMap;
        private static volatile Dictionary<CharacterObject, List<PerkObject>> _ordered = EmptyOrdered;

        /// <summary>
        /// Whether troop perks apply and are shown at all: the feature's own toggle, nested under RBM Combat (the
        /// battle effect and the tooltip/encyclopedia rows live in RBMCombat). The party-screen hover in RBMCampaign
        /// is gated separately, on the toggle and rbmCampaignEnabled, because it also shows troops with no perks.
        /// </summary>
        public static bool IsActive
        {
            get { return RBMConfig.rbmCombatEnabled && RBMConfig.troopPerksEnabled; }
        }

        /// <summary>True when at least one troop has a perk. The cheap early-out for every hot caller.</summary>
        public static bool HasAny
        {
            get { return _map.Count > 0; }
        }

        /// <summary>The troop's perk set, or null when it has none.</summary>
        public static HashSet<PerkObject> Get(CharacterObject character)
        {
            if (character == null)
            {
                return null;
            }
            HashSet<PerkObject> perks;
            return _map.TryGetValue(character, out perks) ? perks : null;
        }

        /// <summary>The troop's perks in file order, for display; null when it has none.</summary>
        public static List<PerkObject> GetOrdered(CharacterObject character)
        {
            if (character == null)
            {
                return null;
            }
            List<PerkObject> perks;
            return _ordered.TryGetValue(character, out perks) ? perks : null;
        }

        public static bool Has(CharacterObject character, PerkObject perk)
        {
            if (perk == null)
            {
                return false;
            }
            HashSet<PerkObject> perks = Get(character);
            return perks != null && perks.Contains(perk);
        }

        /// <summary>
        /// Whether a mapped perk should report as owned to the game's perk checks right now: the feature is on,
        /// the character is a regular troop, a mission is running (so no campaign-map model sees troop perks) and
        /// the pair is listed. Shared by both GetPerkValue postfixes.
        /// </summary>
        public static bool AppliesInMission(CharacterObject character, PerkObject perk)
        {
            return _map.Count > 0
                && character != null
                && !character.IsHero
                && Mission.Current != null
                && IsActive
                && Has(character, perk);
        }

        public static void Clear()
        {
            _map = EmptyMap;
            _ordered = EmptyOrdered;
        }

        /// <summary>
        /// Rebuilds the map from every active module's ModuleData/rbm_troop_perks.xml (RBM's own first, so a
        /// submod can add to it). Problems are logged to the game log (rgl_log), never shown on screen.
        /// </summary>
        public static void Load()
        {
            Clear();
            MBObjectManager objectManager = MBObjectManager.Instance;
            if (objectManager == null || Campaign.Current == null)
            {
                return;
            }
            Dictionary<CharacterObject, HashSet<PerkObject>> map = new Dictionary<CharacterObject, HashSet<PerkObject>>();
            Dictionary<CharacterObject, List<PerkObject>> ordered = new Dictionary<CharacterObject, List<PerkObject>>();
            foreach (string path in GetFilePaths())
            {
                try
                {
                    LoadFile(path, objectManager, map, ordered);
                }
                catch (Exception e)
                {
                    Log("could not read " + path + ": " + e.Message);
                }
            }
            _map = map;
            _ordered = ordered;
            if (map.Count > 0)
            {
                Log("loaded perks for " + map.Count + " troop(s)");
            }
        }

        private static IEnumerable<string> GetFilePaths()
        {
            List<string> paths = new List<string>();
            foreach (ModuleInfo module in ModuleHelper.GetModules())
            {
                if (!ModuleHelper.IsModuleActive(module.Id))
                {
                    continue;
                }
                string path;
                try
                {
                    path = Path.Combine(ModuleHelper.GetModuleFullPath(module.Id), "ModuleData", FileName);
                }
                catch (Exception)
                {
                    continue;
                }
                if (!File.Exists(path))
                {
                    continue;
                }
                if (module.Id == "RBM")
                {
                    paths.Insert(0, path);
                }
                else
                {
                    paths.Add(path);
                }
            }
            return paths;
        }

        private static void LoadFile(string path, MBObjectManager objectManager,
            Dictionary<CharacterObject, HashSet<PerkObject>> map, Dictionary<CharacterObject, List<PerkObject>> ordered)
        {
            XmlDocument document = new XmlDocument();
            document.Load(path);
            XmlNodeList troops = document.SelectNodes("/TroopPerks/Troop");
            if (troops == null)
            {
                return;
            }
            foreach (XmlNode troopNode in troops)
            {
                string troopId = troopNode.Attributes?["id"]?.Value;
                if (string.IsNullOrEmpty(troopId))
                {
                    Log(path + ": a <Troop> has no id; skipped");
                    continue;
                }
                CharacterObject character = objectManager.GetObject<CharacterObject>(troopId);
                if (character == null)
                {
                    Log(path + ": unknown troop id '" + troopId + "'; skipped");
                    continue;
                }
                if (character.IsHero)
                {
                    Log(path + ": '" + troopId + "' is a hero; troop perks only apply to regular troops, skipped");
                    continue;
                }
                XmlNodeList perkNodes = troopNode.SelectNodes("Perk");
                if (perkNodes == null)
                {
                    continue;
                }
                foreach (XmlNode perkNode in perkNodes)
                {
                    string perkId = perkNode.Attributes?["id"]?.Value;
                    if (string.IsNullOrEmpty(perkId))
                    {
                        Log(path + ": a <Perk> of '" + troopId + "' has no id; skipped");
                        continue;
                    }
                    PerkObject perk = objectManager.GetObject<PerkObject>(perkId);
                    if (perk == null)
                    {
                        Log(path + ": unknown perk id '" + perkId + "' for '" + troopId + "'; skipped");
                        continue;
                    }
                    NoEffectReason reason;
                    if (NoTroopEffectPerkIds.TryGetValue(perkId, out reason))
                    {
                        Log(path + ": perk '" + perkId + "' for '" + troopId + "' " + DescribeNoEffect(reason));
                    }
                    else if (perk.PrimaryRole != PartyRole.Personal && perk.SecondaryRole != PartyRole.Personal)
                    {
                        Log(path + ": perk '" + perkId + "' for '" + troopId + "' has no personal effect and does nothing for a troop");
                    }
                    if (perk == DefaultPerks.Athletics.MightyBlow && !MightyBlowAllowed(path, troopId, character))
                    {
                        continue;
                    }
                    HashSet<PerkObject> set;
                    if (!map.TryGetValue(character, out set))
                    {
                        set = new HashSet<PerkObject>();
                        map[character] = set;
                        ordered[character] = new List<PerkObject>();
                    }
                    if (set.Add(perk))
                    {
                        ordered[character].Add(perk);
                    }
                }
            }
        }

        private static string DescribeNoEffect(NoEffectReason reason)
        {
            switch (reason)
            {
                case NoEffectReason.HeroOnly:
                    return "is only checked for heroes and has no effect on a troop";
                case NoEffectReason.CampaignOnly:
                    return "only acts on the campaign map (a hero's own actions) and has no effect on a troop in battle";
                case NoEffectReason.NoCheckFound:
                    return "is never checked for an AI troop in battle and has no effect on a troop";
                case NoEffectReason.RbmBypassed:
                    return "has no effect for anyone under RBM: RBM overrides what it changes, for heroes too";
                default:
                    return "has no effect on a troop";
            }
        }

        /// <summary>
        /// Mighty Blow's hit-point half (DefaultCharacterStatsModel.MaxHitpoints) adds (Athletics skill -
        /// MaxSkillRequiredForEpicPerkBonus, 250) to max HP with no floor. Heroes can only take the perk above that
        /// skill, so for them it is always a bonus; a troop below it would LOSE hit points. So a troop only gets the
        /// perk with Athletics above the threshold, which keeps its HP bonus above 0; otherwise it is skipped.
        /// </summary>
        private static bool MightyBlowAllowed(string path, string troopId, CharacterObject character)
        {
            int threshold = Campaign.Current.Models.CharacterDevelopmentModel.MaxSkillRequiredForEpicPerkBonus;
            int athletics = character.GetSkillValue(DefaultSkills.Athletics);
            if (athletics > threshold)
            {
                return true;
            }
            Log(path + ": perk 'AthleticsMightyBlow' for '" + troopId + "' skipped: it needs Athletics above " + threshold
                + " (the troop has " + athletics + "), below that its HP bonus (Athletics - " + threshold + ") would lower the troop's max HP");
            return false;
        }

        private static void Log(string message)
        {
            TaleWorlds.Library.Debug.Print("[RBM] Troop perks: " + message);
        }
    }
}
