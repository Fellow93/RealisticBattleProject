using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace RBMCampaign
{
    /// <summary>
    /// Debug console command <c>campaign.rbm_test_battle [countPerTroop] [attacker=..] [defender=..]
    /// [player=attacker|defender]</c>: a field battle at a random spot on the campaign map. By default both
    /// sides hold the same roster of every culture's regular troops (the basic and noble upgrade trees),
    /// every tavern mercenary and every minor-faction troop, so all troop types meet in one fight; the
    /// attacker/defender filters narrow a side to cultures, mercenaries or minor factions, and player picks
    /// the side the player fights on.
    /// </summary>
    /// <remarks>
    /// The player's own troops are put aside for the test and come back exactly when the encounter is
    /// over, however it ends (win, loss, retreat, leaving, capture). Restored with them: the prisoners,
    /// the inventory, the main hero's gold, the clan's renown (unless it crossed a clan tier), party
    /// morale, the heroes' hit points, RBM's spoils, rations and luxury state for the main party, the RBM
    /// clan finance record, and the party's map position. Not restored: skill and troop XP the heroes
    /// earned, influence, relations, and a hero killed in the fight. The enemy is a temporary looters-clan
    /// party (quest-flagged, AI off), removed afterwards. Being a player battle with a quest party in it,
    /// the fight leaves no v1.5 battle wreckage (BattleWreckageCampaignBehavior skips both).
    ///
    /// The restore is polled from <c>RBM.SubModule.OnApplicationTick</c> (<see cref="Tick"/>), every frame
    /// on the map, and again before a save. It waits until no player encounter or map event is left and
    /// the map is the active screen, so the loot and prisoner screens have run first.
    /// </remarks>
    public static partial class TestBattleCommand
    {
        private const int DefaultCountPerTroop = 5;
        private const int MaxCountPerTroop = 100;

        // Side filter group ids (besides culture and minor-faction clan ids).
        private const string AllGroup = "all";
        private const string MercenaryGroup = "mercenaries";
        private const string MinorGroup = "minor";

        // Distances in campaign-map units. The encounter joining radius is 3 (DefaultEncounterModel), so a
        // spot with no other party within 6 keeps strangers out of the battle.
        private const float MinDistanceFromGate = 4f;
        private const float MaxDistanceFromGate = 9f;
        private const float MinDistanceFromSettlement = 3f;
        private const float MinDistanceFromOtherParty = 6f;
        private const int PositionAttempts = 60;

        private static Session _session;

        private sealed class Session
        {
            internal Campaign Campaign;
            internal MobileParty Enemy;
            internal CampaignVec2 OriginalPosition;
            // Non-hero stacks, and the order of every stack (heroes included) to put them back in.
            internal List<TroopRosterElement> Members;
            internal List<CharacterObject> MemberOrder;
            internal List<TroopRosterElement> Prisoners;
            internal List<ItemRosterElement> Items;
            internal Dictionary<Hero, int> HeroHitPoints;
            internal int Gold;
            internal float Renown;
            internal int ClanTier;
            internal float RecentEventsMorale;
            internal Dictionary<string, int> Spoils;
            internal Dictionary<string, int> FedUntil;
            internal Dictionary<string, int> LuxuryCooldown;
            internal ClanEventGoldLedger.Snapshot GoldLedger;
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("rbm_test_battle", "campaign")]
        public static string ConsoleTestBattle(List<string> args)
        {
            const string usage = "Usage: campaign.rbm_test_battle [countPerTroop=5] [attacker=<groups>] "
                + "[defender=<groups>] [player=attacker|defender]\n"
                + "Starts a field battle at a random spot on the map. Each side gets countPerTroop men of every "
                + "troop in its groups. A group is a culture id (vlandia, empire, ...), mercenaries, minor (every "
                + "minor faction), a minor-faction clan id, or all (the default: every culture's regular troops, "
                + "every tavern mercenary and every minor-faction troop). Join groups with commas, e.g. "
                + "attacker=vlandia,mercenaries. player picks your side (default attacker). Your own troops come "
                + "back when the encounter ends.";

            if (Campaign.Current == null)
            {
                return "Not in a campaign.";
            }
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return "RBM Campaign is switched off.";
            }

            if (args == null || args.All(string.IsNullOrWhiteSpace))
            {
                string openError = CheckCanOpenSetup();
                if (openError != null)
                {
                    return openError;
                }
                OpenSetup();
                return "Test battle setup opened. Close the console to pick the options.\n"
                    + "(Parameters start it directly instead: campaign.rbm_test_battle help)";
            }

            int countPerTroop = DefaultCountPerTroop;
            string attackerGroups = AllGroup;
            string defenderGroups = AllGroup;
            bool playerIsAttacker = true;
            foreach (string rawArg in args)
            {
                if (string.IsNullOrWhiteSpace(rawArg))
                {
                    continue;
                }
                string arg = rawArg.Trim();
                if (arg == "help" || arg == "?")
                {
                    return usage;
                }
                int equals = arg.IndexOf('=');
                if (equals < 0)
                {
                    if (!int.TryParse(arg, out countPerTroop) || countPerTroop < 1)
                    {
                        return "countPerTroop must be a whole number of 1 or more.\n" + usage;
                    }
                    countPerTroop = Math.Min(countPerTroop, MaxCountPerTroop);
                    continue;
                }
                string key = arg.Substring(0, equals).ToLowerInvariant();
                string value = arg.Substring(equals + 1).Trim().ToLowerInvariant();
                if (key == "attacker" || key == "att")
                {
                    attackerGroups = value;
                }
                else if (key == "defender" || key == "def")
                {
                    defenderGroups = value;
                }
                else if (key == "player" || key == "side")
                {
                    if (value == "attacker" || value == "att")
                    {
                        playerIsAttacker = true;
                    }
                    else if (value == "defender" || value == "def")
                    {
                        playerIsAttacker = false;
                    }
                    else
                    {
                        return "player must be attacker or defender.\n" + usage;
                    }
                }
                else
                {
                    return "Unknown parameter '" + key + "'.\n" + usage;
                }
            }

            bool started;
            return StartTestBattle(countPerTroop, attackerGroups, defenderGroups, playerIsAttacker, out started);
        }

        /// <summary>
        /// Sets up and starts the test battle. Returns the summary, or the reason it did not start
        /// (<paramref name="started"/> false).
        /// </summary>
        private static string StartTestBattle(int countPerTroop, string attackerGroups, string defenderGroups,
            bool playerIsAttacker, out bool started)
        {
            started = false;
            string openError = CheckCanOpenSetup();
            if (openError != null)
            {
                return openError;
            }
            _session = null;

            string guardError = CheckCanStart();
            if (guardError != null)
            {
                return guardError;
            }

            Dictionary<CharacterObject, string> sourceOf;
            Dictionary<CharacterObject, string> groupOf;
            List<CharacterObject> allTroops = CollectTestTroops(out sourceOf, out groupOf);
            if (allTroops.Count == 0)
            {
                return "No troops found for the test roster.";
            }
            string filterError;
            List<CharacterObject> attackerTroops = FilterTroops(allTroops, groupOf, attackerGroups, out filterError);
            if (attackerTroops == null)
            {
                return "attacker: " + filterError;
            }
            List<CharacterObject> defenderTroops = FilterTroops(allTroops, groupOf, defenderGroups, out filterError);
            if (defenderTroops == null)
            {
                return "defender: " + filterError;
            }
            List<CharacterObject> playerTroops = playerIsAttacker ? attackerTroops : defenderTroops;
            List<CharacterObject> enemyTroops = playerIsAttacker ? defenderTroops : attackerTroops;

            Clan enemyClan = Clan.BanditFactions.FirstOrDefault(c => c.StringId == "looters")
                ?? Clan.BanditFactions.FirstOrDefault();
            if (enemyClan == null || !enemyClan.IsAtWarWith(MobileParty.MainParty.MapFaction))
            {
                return "No bandit clan at war with you to field the enemy side.";
            }

            Settlement nearSettlement;
            CampaignVec2 battlePosition;
            if (!TryFindBattlePosition(out battlePosition, out nearSettlement))
            {
                return "Could not find a free land position for the battle. Try again.";
            }

            MobileParty mainParty = MobileParty.MainParty;
            Session session = TakeSnapshot(mainParty);
            try
            {
                // Clean purses for the test stacks: the real ones are in the snapshot.
                SpoilsPool.RestorePartySpoils(mainParty.Party, null);
                TroopUpkeep.RestorePartyState(mainParty.Party, null, null);

                mainParty.Position = battlePosition;
                mainParty.SetMoveModeHold();

                MobileParty enemy = CustomPartyComponent.CreateCustomPartyWithTroopRoster(battlePosition, 1f, null,
                    new TextObject("{=!}Test Battle Opponents"), enemyClan, TroopRoster.CreateDummyTroopRoster(),
                    TroopRoster.CreateDummyTroopRoster(), null);
                session.Enemy = enemy;
                enemy.SetPartyUsedByQuest(true);
                enemy.Ai.DisableAi();

                mainParty.MemberRoster.RemoveIf(e => e.Character != null && !e.Character.IsHero);
                foreach (CharacterObject troop in playerTroops)
                {
                    mainParty.MemberRoster.AddToCounts(troop, countPerTroop);
                }
                foreach (CharacterObject troop in enemyTroops)
                {
                    enemy.MemberRoster.AddToCounts(troop, countPerTroop);
                }

                _session = session;
                CampaignEvents.OnBeforeSaveEvent.AddNonSerializedListener(session, OnBeforeSave);

                // The same start the vanilla naval test cheat uses: encounter set up with the chosen sides, the
                // field battle created, then straight to the battle menu. EncounterManager.StartPartyEncounter
                // would open the encounter conversation first, which for a looters-clan party plays the bandit
                // dialogue with one of the test troops. As the defender the menu has no free Leave (vanilla:
                // only attack, send troops, try to get away or surrender).
                if (playerIsAttacker)
                {
                    PlayerEncounter.RestartPlayerEncounter(enemy.Party, PartyBase.MainParty);
                }
                else
                {
                    PlayerEncounter.RestartPlayerEncounter(PartyBase.MainParty, enemy.Party);
                }
                PlayerEncounter.Current.PlayerPartyInitialStrength = mainParty.Party.CalculateCurrentStrength();
                PlayerEncounter.StartBattle();
                GameMenu.ActivateGameMenu("encounter");
            }
            catch (Exception e)
            {
                _session = session;
                if (PlayerEncounter.Current != null)
                {
                    try
                    {
                        PlayerEncounter.Finish();
                    }
                    catch (Exception finishError)
                    {
                        Debug.Print("[RBM test battle] PlayerEncounter.Finish after a failed start: " + finishError);
                    }
                }
                Restore(false);
                return "Could not start the test battle (" + e.GetType().Name + ": " + e.Message
                    + "). Your party was put back as it was.";
            }

            started = true;
            SaveLastUsedPreset(countPerTroop, attackerGroups, defenderGroups, playerIsAttacker);
            return BuildSummary(attackerTroops, defenderTroops, playerIsAttacker, sourceOf, countPerTroop,
                battlePosition, nearSettlement);
        }

        /// <summary>The checks that don't depend on the map state: campaign, module, no test running.</summary>
        private static string CheckCanOpenSetup()
        {
            if (Campaign.Current == null)
            {
                return "Not in a campaign.";
            }
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return "RBM Campaign is switched off.";
            }
            if (_session != null && _session.Campaign == Campaign.Current)
            {
                return "A test battle is still running. Finish it first (your troops come back when the encounter ends).";
            }
            return null;
        }

        /// <summary>Polled every frame on the map (no mission running) from RBM.SubModule.</summary>
        public static void Tick()
        {
            TickSetup();
            if (_session == null)
            {
                return;
            }
            TryRestore(false);
        }

        private static void OnBeforeSave()
        {
            TryRestore(true);
        }

        private static string CheckCanStart()
        {
            MobileParty mainParty = MobileParty.MainParty;
            if (mainParty == null || Hero.MainHero == null || !mainParty.IsActive)
            {
                return "There is no active main party.";
            }
            if (Hero.MainHero.IsPrisoner)
            {
                return "You are a prisoner.";
            }
            if (PlayerEncounter.Current != null || mainParty.MapEvent != null)
            {
                return "You are in an encounter or battle.";
            }
            if (mainParty.CurrentSettlement != null)
            {
                return "Leave the settlement first.";
            }
            if (mainParty.Army != null)
            {
                return "Leave your army first.";
            }
            if (mainParty.BesiegerCamp != null || mainParty.SiegeEvent != null)
            {
                return "You are taking part in a siege.";
            }
            if (mainParty.IsCurrentlyAtSea || !mainParty.Position.IsOnLand)
            {
                return "You are at sea. Land first.";
            }
            if (Campaign.Current.CurrentMenuContext != null || !(Game.Current.GameStateManager.ActiveState is MapState))
            {
                return "Close the open menu or screen first and run the command on the campaign map.";
            }
            return null;
        }

        // ---------------------------------------------------------------------------------------------
        // Troops
        // ---------------------------------------------------------------------------------------------

        private static List<CultureObject> GetMainCultures()
        {
            return MBObjectManager.Instance.GetObjectTypeList<CultureObject>()
                .Where(c => c != null && c.IsMainCulture)
                .OrderBy(c => c.StringId, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Every culture's regular troops, the tavern mercenaries and the minor-faction troops, in that
        /// order, each once. <paramref name="sourceOf"/> names where each troop was first found, and
        /// <paramref name="groupOf"/> the group id the command's side filters match: the culture id for
        /// regulars, <see cref="MercenaryGroup"/> for mercenaries, the clan id for minor-faction troops.
        /// </summary>
        private static List<CharacterObject> CollectTestTroops(out Dictionary<CharacterObject, string> sourceOf,
            out Dictionary<CharacterObject, string> groupOf)
        {
            List<CharacterObject> ordered = new List<CharacterObject>();
            sourceOf = new Dictionary<CharacterObject, string>();
            groupOf = new Dictionary<CharacterObject, string>();

            List<CultureObject> mainCultures = GetMainCultures();

            // Regulars: the basic (recruit) and noble trees, as a lord recruits them.
            foreach (CultureObject culture in mainCultures)
            {
                string source = culture.Name != null ? culture.Name.ToString() : culture.StringId;
                AddTree(culture.BasicTroop, source, culture.StringId, ordered, sourceOf, groupOf);
                AddTree(culture.EliteBasicTroop, source, culture.StringId, ordered, sourceOf, groupOf);
            }

            // Mercenaries: what the tavern sells, i.e. each culture's basic_mercenary_troops and their
            // upgrades (RecruitmentCampaignBehavior.UpdateCurrentMercenaryTroopAndCount). Caravan guards,
            // which the tavern also sells, are left out.
            foreach (CultureObject culture in mainCultures)
            {
                if (culture.BasicMercenaryTroops == null)
                {
                    continue;
                }
                foreach (CharacterObject mercenary in culture.BasicMercenaryTroops)
                {
                    AddTree(mercenary, "Mercenaries", MercenaryGroup, ordered, sourceOf, groupOf);
                }
            }
            // Any other Mercenary-occupation troop a mod adds without listing it in a culture. Hidden ones
            // are skipped: vanilla's obsolete mercenary_1..9 are Mercenary troops hidden from the encyclopedia.
            foreach (CharacterObject character in CharacterObject.All)
            {
                if (character != null && character.Occupation == Occupation.Mercenary && !character.HiddenInEncyclopedia)
                {
                    AddTree(character, "Mercenaries", MercenaryGroup, ordered, sourceOf, groupOf);
                }
            }

            // Minor factions: the mercenary and outlaw bands that hire out to kingdoms. Their troops come from
            // the clan's party template; a clan without one of its own falls back to its culture's, which
            // only adds troops already listed.
            foreach (Clan clan in Clan.All.Where(c => c != null && c.IsMinorFaction && !c.IsBanditFaction
                && !c.IsRebelClan && c != Clan.PlayerClan).OrderBy(c => c.StringId, StringComparer.Ordinal))
            {
                PartyTemplateObject template = clan.DefaultPartyTemplate;
                if (template == null || template.Stacks == null)
                {
                    continue;
                }
                string source = clan.Name != null ? clan.Name.ToString() : clan.StringId;
                foreach (PartyTemplateStack stack in template.Stacks)
                {
                    AddTree(stack.Character, source, clan.StringId, ordered, sourceOf, groupOf);
                }
            }
            return ordered;
        }

        private static void AddTree(CharacterObject root, string source, string group, List<CharacterObject> ordered,
            Dictionary<CharacterObject, string> sourceOf, Dictionary<CharacterObject, string> groupOf)
        {
            if (root == null)
            {
                return;
            }
            HashSet<CharacterObject> visited = new HashSet<CharacterObject>();
            Queue<CharacterObject> queue = new Queue<CharacterObject>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                CharacterObject character = queue.Dequeue();
                if (character == null || !visited.Add(character))
                {
                    continue;
                }
                if (IsTestTroop(character) && !sourceOf.ContainsKey(character))
                {
                    sourceOf[character] = source;
                    groupOf[character] = group;
                    ordered.Add(character);
                }
                CharacterObject[] targets = character.UpgradeTargets;
                if (targets != null)
                {
                    foreach (CharacterObject target in targets)
                    {
                        queue.Enqueue(target);
                    }
                }
            }
        }

        /// <summary>A regular soldier or mercenary that can take the field: no heroes, templates, bandits,
        /// villagers, militia-only townsfolk or caravan guards, and nothing that failed to load.</summary>
        private static bool IsTestTroop(CharacterObject character)
        {
            return character.IsReady
                && !character.IsHero
                && !character.IsTemplate
                && !character.IsObsolete
                && (character.Occupation == Occupation.Soldier || character.Occupation == Occupation.Mercenary)
                && character.FirstBattleEquipment != null;
        }

        /// <summary>
        /// The troops of a comma-separated group list, in collection order. A group is <see cref="AllGroup"/>,
        /// <see cref="MercenaryGroup"/> (or merc), <see cref="MinorGroup"/> (every minor faction), a main culture
        /// id or a minor-faction clan id, matched without case. Null, with <paramref name="error"/> naming the
        /// valid groups, when a group is unknown.
        /// </summary>
        private static List<CharacterObject> FilterTroops(List<CharacterObject> troops,
            Dictionary<CharacterObject, string> groupOf, string groupList, out string error)
        {
            error = null;
            HashSet<string> wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in groupList.Split(','))
            {
                string group = part.Trim();
                if (group.Equals("merc", StringComparison.OrdinalIgnoreCase)
                    || group.Equals("mercs", StringComparison.OrdinalIgnoreCase))
                {
                    group = MercenaryGroup;
                }
                if (group.Length > 0)
                {
                    wanted.Add(group);
                }
            }
            if (wanted.Count == 0 || wanted.Contains(AllGroup))
            {
                return new List<CharacterObject>(troops);
            }

            HashSet<string> cultureIds = new HashSet<string>(GetMainCultures().Select(c => c.StringId),
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> knownGroups = new HashSet<string>(groupOf.Values, StringComparer.OrdinalIgnoreCase);
            List<string> unknown = wanted.Where(g => !knownGroups.Contains(g)
                && !g.Equals(MinorGroup, StringComparison.OrdinalIgnoreCase)).ToList();
            if (unknown.Count > 0)
            {
                List<string> minorIds = knownGroups.Where(g => IsMinorGroup(g, cultureIds))
                    .OrderBy(g => g, StringComparer.Ordinal).ToList();
                error = "unknown group " + string.Join(", ", unknown) + ".\nGroups: " + AllGroup + ", "
                    + MercenaryGroup + ", " + MinorGroup + "; cultures: "
                    + string.Join(", ", cultureIds.OrderBy(g => g, StringComparer.Ordinal))
                    + "; minor factions: " + string.Join(", ", minorIds);
                return null;
            }

            bool wantMinor = wanted.Contains(MinorGroup);
            return troops.Where(t =>
            {
                string group = groupOf[t];
                return wanted.Contains(group) || (wantMinor && IsMinorGroup(group, cultureIds));
            }).ToList();
        }

        /// <summary>A minor-faction clan group: neither the mercenaries nor a main culture.</summary>
        private static bool IsMinorGroup(string group, HashSet<string> cultureIds)
        {
            return group != MercenaryGroup && !cultureIds.Contains(group);
        }

        // ---------------------------------------------------------------------------------------------
        // Position
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// A reachable land spot a few units outside a random town, castle or village, away from every
        /// settlement and every other party.
        /// </summary>
        private static bool TryFindBattlePosition(out CampaignVec2 position, out Settlement nearSettlement)
        {
            position = CampaignVec2.Invalid;
            nearSettlement = null;
            List<Settlement> candidates = Settlement.All
                .Where(s => s != null && (s.IsTown || s.IsCastle || s.IsVillage) && s.GatePosition.IsValid()
                    && s.GatePosition.IsOnLand)
                .ToList();
            if (candidates.Count == 0)
            {
                return false;
            }
            for (int attempt = 0; attempt < PositionAttempts; attempt++)
            {
                Settlement settlement = candidates[MBRandom.RandomInt(candidates.Count)];
                CampaignVec2 gate = settlement.GatePosition;
                CampaignVec2 point = NavigationHelper.FindPointAroundPosition(gate, MobileParty.NavigationType.Default,
                    MaxDistanceFromGate, MinDistanceFromGate);
                // FindPointAroundPosition hands the center back when it finds nothing.
                if (point == gate || !point.IsValid() || !point.IsOnLand
                    || !NavigationHelper.IsPositionValidForNavigationType(point, MobileParty.NavigationType.Default)
                    || !IsClear(point))
                {
                    continue;
                }
                position = point;
                nearSettlement = settlement;
                return true;
            }
            return false;
        }

        private static bool IsClear(CampaignVec2 point)
        {
            foreach (Settlement settlement in Settlement.All)
            {
                if (settlement != null && settlement.GatePosition.IsValid()
                    && settlement.GatePosition.Distance(point) < MinDistanceFromSettlement)
                {
                    return false;
                }
            }
            foreach (MobileParty party in MobileParty.All)
            {
                if (party != null && party != MobileParty.MainParty && party.IsActive && party.CurrentSettlement == null
                    && party.Position.IsValid() && party.Position.Distance(point) < MinDistanceFromOtherParty)
                {
                    return false;
                }
            }
            return true;
        }

        // ---------------------------------------------------------------------------------------------
        // Snapshot and restore
        // ---------------------------------------------------------------------------------------------

        private static Session TakeSnapshot(MobileParty mainParty)
        {
            Session session = new Session
            {
                Campaign = Campaign.Current,
                OriginalPosition = mainParty.Position,
                Members = new List<TroopRosterElement>(),
                MemberOrder = new List<CharacterObject>(),
                Prisoners = new List<TroopRosterElement>(),
                Items = new List<ItemRosterElement>(),
                HeroHitPoints = new Dictionary<Hero, int>(),
                Gold = Hero.MainHero.Gold,
                Renown = Clan.PlayerClan.Renown,
                ClanTier = Clan.PlayerClan.Tier,
                RecentEventsMorale = mainParty.RecentEventsMorale,
                Spoils = SpoilsPool.CopyPartySpoils(mainParty.Party),
                GoldLedger = ClanEventGoldLedger.TakeSnapshot(),
            };
            TroopUpkeep.CopyPartyState(mainParty.Party, out session.FedUntil, out session.LuxuryCooldown);

            TroopRoster members = mainParty.MemberRoster;
            for (int i = 0; i < members.Count; i++)
            {
                TroopRosterElement element = members.GetElementCopyAtIndex(i);
                if (element.Character == null)
                {
                    continue;
                }
                session.MemberOrder.Add(element.Character);
                if (element.Character.IsHero)
                {
                    if (element.Character.HeroObject != null)
                    {
                        session.HeroHitPoints[element.Character.HeroObject] = element.Character.HeroObject.HitPoints;
                    }
                }
                else
                {
                    session.Members.Add(element);
                }
            }

            TroopRoster prisoners = mainParty.PrisonRoster;
            for (int i = 0; i < prisoners.Count; i++)
            {
                TroopRosterElement element = prisoners.GetElementCopyAtIndex(i);
                if (element.Character != null && !element.Character.IsHero)
                {
                    session.Prisoners.Add(element);
                }
            }

            ItemRoster items = mainParty.ItemRoster;
            for (int i = 0; i < items.Count; i++)
            {
                session.Items.Add(items.GetElementCopyAtIndex(i));
            }
            return session;
        }

        private static void TryRestore(bool beforeSave)
        {
            Session session = _session;
            if (session == null)
            {
                return;
            }
            if (Campaign.Current == null || Campaign.Current != session.Campaign)
            {
                // Another campaign was loaded: the snapshot belongs to one that is gone.
                _session = null;
                return;
            }
            MobileParty mainParty = MobileParty.MainParty;
            if (mainParty == null || PlayerEncounter.Current != null || mainParty.MapEvent != null)
            {
                return;
            }
            if (session.Enemy != null && session.Enemy.IsActive && session.Enemy.MapEvent != null)
            {
                return;
            }
            if (!beforeSave && !(Game.Current.GameStateManager.ActiveState is MapState))
            {
                return;
            }
            if (Campaign.Current.ConversationManager != null && Campaign.Current.ConversationManager.IsConversationInProgress)
            {
                return;
            }
            // Releasing a captive player exits menus; leave that to the next map frame rather than a save.
            if (beforeSave && Hero.MainHero.IsPrisoner)
            {
                return;
            }
            Restore(true);
        }

        /// <summary>
        /// Puts the main party back as the snapshot holds it and removes the enemy. Each step is guarded on
        /// its own so one failure does not leave the rest undone.
        /// </summary>
        private static void Restore(bool releaseCaptives)
        {
            Session session = _session;
            _session = null;
            if (session == null)
            {
                return;
            }
            CampaignEvents.OnBeforeSaveEvent.ClearListeners(session);

            MobileParty mainParty = MobileParty.MainParty;
            MobileParty enemy = session.Enemy;

            if (releaseCaptives)
            {
                Guard("release captives", () => ReleaseCaptives(session, enemy));
            }

            Guard("member roster", () =>
            {
                TroopRoster members = mainParty.MemberRoster;
                members.RemoveIf(e => e.Character != null && !e.Character.IsHero);
                foreach (TroopRosterElement element in session.Members)
                {
                    members.AddToCounts(element.Character, element.Number, false, element.WoundedNumber, element.Xp);
                }
                int target = 0;
                foreach (CharacterObject character in session.MemberOrder)
                {
                    int index = members.FindIndexOfTroop(character);
                    if (index < 0)
                    {
                        continue;
                    }
                    if (index != target)
                    {
                        members.ShiftTroopToIndex(index, target);
                    }
                    target++;
                }
            });

            Guard("prison roster", () =>
            {
                TroopRoster prisoners = mainParty.PrisonRoster;
                prisoners.RemoveIf(e => e.Character != null && !e.Character.IsHero);
                foreach (TroopRosterElement element in session.Prisoners)
                {
                    prisoners.AddToCounts(element.Character, element.Number, false, element.WoundedNumber, element.Xp);
                }
            });

            Guard("inventory", () =>
            {
                ItemRoster items = mainParty.ItemRoster;
                items.Clear();
                foreach (ItemRosterElement element in session.Items)
                {
                    items.AddToCounts(element.EquipmentElement, element.Amount);
                }
            });

            Guard("gold, renown, morale, hit points", () =>
            {
                Hero.MainHero.Gold = session.Gold;
                // Renown that crossed a clan tier is left alone: the tier has already been raised and
                // lowering the renown under it would leave the two out of step.
                if (Clan.PlayerClan.Tier == session.ClanTier)
                {
                    Clan.PlayerClan.Renown = session.Renown;
                }
                mainParty.RecentEventsMorale = session.RecentEventsMorale;
                foreach (KeyValuePair<Hero, int> entry in session.HeroHitPoints)
                {
                    if (entry.Key != null && entry.Key.IsAlive)
                    {
                        entry.Key.HitPoints = entry.Value;
                    }
                }
            });

            Guard("spoils", () =>
            {
                SpoilsPool.RestorePartySpoils(mainParty.Party, session.Spoils);
                TroopUpkeep.RestorePartyState(mainParty.Party, session.FedUntil, session.LuxuryCooldown);
                // The spoils gold paid out in the battle was taken back above; so are its finance-tab lines.
                ClanEventGoldLedger.RestoreSnapshot(session.GoldLedger);
            });

            Guard("position", () =>
            {
                if (mainParty.IsActive && session.OriginalPosition.IsValid())
                {
                    mainParty.Position = session.OriginalPosition;
                    mainParty.SetMoveModeHold();
                }
            });

            Guard("remove enemy", () =>
            {
                if (enemy != null && enemy.IsActive)
                {
                    DestroyPartyAction.Apply(null, enemy);
                }
            });
        }

        /// <summary>
        /// Frees the main hero and any of his companions the test enemy took. The player goes through
        /// <see cref="EndCaptivityAction"/> (PlayerCaptivity.EndCaptivity puts him back at the head of his
        /// party and closes the captivity menu); companions are freed and rejoin as vanilla's companion
        /// rescue does.
        /// </summary>
        private static void ReleaseCaptives(Session session, MobileParty enemy)
        {
            if (enemy == null || !enemy.IsActive)
            {
                return;
            }
            if (Hero.MainHero.IsPrisoner && Hero.MainHero.PartyBelongedToAsPrisoner == enemy.Party)
            {
                EndCaptivityAction.ApplyByReleasedAfterBattle(Hero.MainHero);
            }
            List<Hero> captives = new List<Hero>();
            TroopRoster prison = enemy.PrisonRoster;
            for (int i = 0; i < prison.Count; i++)
            {
                CharacterObject character = prison.GetCharacterAtIndex(i);
                Hero hero = character != null ? character.HeroObject : null;
                if (hero != null && hero != Hero.MainHero && session.HeroHitPoints.ContainsKey(hero))
                {
                    captives.Add(hero);
                }
            }
            foreach (Hero hero in captives)
            {
                if (!hero.IsAlive || !hero.IsPrisoner)
                {
                    continue;
                }
                EndCaptivityAction.ApplyByReleasedAfterBattle(hero);
                hero.ChangeState(Hero.CharacterStates.Active);
                MobileParty.MainParty.AddElementToMemberRoster(hero.CharacterObject, 1);
            }
        }

        private static void Guard(string step, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.Print("[RBM test battle] restoring " + step + " failed: " + e);
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Output
        // ---------------------------------------------------------------------------------------------

        private static string BuildSummary(List<CharacterObject> attackerTroops, List<CharacterObject> defenderTroops,
            bool playerIsAttacker, Dictionary<CharacterObject, string> sourceOf, int countPerTroop,
            CampaignVec2 position, Settlement nearSettlement)
        {
            StringBuilder text = new StringBuilder();
            text.Append("Test battle started at (").Append(position.X.ToString("0.0")).Append(", ")
                .Append(position.Y.ToString("0.0")).Append(")");
            if (nearSettlement != null)
            {
                text.Append(" near ").Append(nearSettlement.Name);
            }
            text.Append(". You are the ").Append(playerIsAttacker ? "attacker" : "defender")
                .Append(" (your heroes fight on your side too).\n");
            if (attackerTroops.SequenceEqual(defenderTroops))
            {
                AppendSide(text, "Each side", attackerTroops, sourceOf, countPerTroop);
            }
            else
            {
                AppendSide(text, playerIsAttacker ? "Attacker (you)" : "Attacker (enemy)", attackerTroops, sourceOf,
                    countPerTroop);
                AppendSide(text, playerIsAttacker ? "Defender (enemy)" : "Defender (you)", defenderTroops, sourceOf,
                    countPerTroop);
            }
            text.Append("Your own troops, prisoners, inventory, gold and position come back when the encounter ends.");
            return text.ToString();
        }

        private static void AppendSide(StringBuilder text, string label, List<CharacterObject> troops,
            Dictionary<CharacterObject, string> sourceOf, int countPerTroop)
        {
            text.Append(label).Append(": ").Append(troops.Count).Append(" troop types x ").Append(countPerTroop)
                .Append(" = ").Append(troops.Count * countPerTroop).Append(" men.\n");

            // Grouped by where each troop was found, in the order they were collected.
            List<string> sources = new List<string>();
            Dictionary<string, List<string>> bySource = new Dictionary<string, List<string>>();
            foreach (CharacterObject troop in troops)
            {
                string source = sourceOf[troop];
                List<string> names;
                if (!bySource.TryGetValue(source, out names))
                {
                    names = new List<string>();
                    bySource[source] = names;
                    sources.Add(source);
                }
                names.Add(troop.Name + " (T" + troop.Tier + ")");
            }
            foreach (string source in sources)
            {
                List<string> names = bySource[source];
                text.Append("  ").Append(source).Append(" (").Append(names.Count).Append("): ")
                    .Append(string.Join(", ", names)).Append("\n");
            }
        }
    }
}
