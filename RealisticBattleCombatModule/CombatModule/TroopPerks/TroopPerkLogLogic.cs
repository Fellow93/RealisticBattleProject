using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace RBMCombat
{
    /// <summary>
    /// The troop perk log: for one mission, which perks listed in rbm_troop_perks.xml the game actually asked about
    /// for the troops that fought, and which it never asked about (those had no effect, whatever the file says).
    /// Added by RBM SubModule when rbmCombatEnabled, troopPerksEnabled and troopPerkLoggingEnabled are all on.
    ///
    /// <see cref="TroopPerkLog"/> records the answers (from the GetPerkValue postfixes, any thread); this logic
    /// counts which listed troops spawned (OnAgentBuild, main thread) and writes one file per mission at
    /// OnEndMission, which every way out of a mission goes through (victory, retreat, leaving a scene). A mission in
    /// which no listed troop spawned and nothing was asked writes no file, so town walks and the like leave none.
    /// </summary>
    public class TroopPerkLogLogic : MissionLogic
    {
        [Flags]
        private enum Gear
        {
            None = 0,
            OneHanded = 1,
            TwoHanded = 2,
            Polearm = 4,
            Shield = 8,
            Bow = 16,
            Crossbow = 32,
            Sling = 64,
            Thrown = 128,
        }

        private class Spawned
        {
            public int Agents;
            public int Mounted;
            public Gear Gear;
        }

        private readonly Dictionary<CharacterObject, Spawned> _spawned = new Dictionary<CharacterObject, Spawned>();

        // Display order: troops in the order they first spawned.
        private readonly List<CharacterObject> _spawnOrder = new List<CharacterObject>();

        private string _sceneName;
        private MissionMode _startMode;
        private DateTime _startedAt;

        /// <summary>
        /// Recording starts here, before AfterStart and before the first agent is built, so no early perk check of
        /// the mission is missed. (OnBehaviorInitialize runs before RBM adds this behavior, so it never fires for it.)
        /// </summary>
        public override void EarlyStart()
        {
            _spawned.Clear();
            _spawnOrder.Clear();
            _startedAt = DateTime.Now;
            _sceneName = Mission.SceneName;
            _startMode = Mission.Mode;
            TroopPerkLog.Begin();
        }

        public override void OnAgentBuild(Agent agent, Banner banner)
        {
            if (agent == null || !agent.IsHuman)
            {
                return;
            }
            CharacterObject troop = agent.Character as CharacterObject;
            if (troop == null || troop.IsHero || RBMConfig.TroopPerks.GetOrdered(troop) == null)
            {
                return;
            }
            Spawned spawned;
            if (!_spawned.TryGetValue(troop, out spawned))
            {
                spawned = new Spawned();
                _spawned[troop] = spawned;
                _spawnOrder.Add(troop);
            }
            spawned.Agents++;
            if (agent.HasMount)
            {
                spawned.Mounted++;
            }
            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
            {
                MissionWeapon weapon = agent.Equipment[i];
                if (!weapon.IsEmpty && weapon.Item != null)
                {
                    spawned.Gear |= GearOf(weapon.Item.ItemType);
                }
            }
        }

        protected override void OnEndMission()
        {
            TroopPerkLog.Recording = false;
            try
            {
                KeyValuePair<TroopPerkLog.Key, TroopPerkLog.Entry>[] entries = TroopPerkLog.Snapshot();
                if (_spawnOrder.Count > 0 || entries.Length > 0)
                {
                    TroopPerkLog.WriteFile(_sceneName, Compose(entries).Replace("\n", Environment.NewLine));
                }
            }
            catch
            {
                // A diagnostic must never break a mission's end.
            }
            TroopPerkLog.Reset();
        }

        public override void OnRemoveBehavior()
        {
            TroopPerkLog.Reset();
            base.OnRemoveBehavior();
        }

        private static Gear GearOf(ItemObject.ItemTypeEnum type)
        {
            switch (type)
            {
                case ItemObject.ItemTypeEnum.OneHandedWeapon: return Gear.OneHanded;
                case ItemObject.ItemTypeEnum.TwoHandedWeapon: return Gear.TwoHanded;
                case ItemObject.ItemTypeEnum.Polearm: return Gear.Polearm;
                case ItemObject.ItemTypeEnum.Shield: return Gear.Shield;
                case ItemObject.ItemTypeEnum.Bow: return Gear.Bow;
                case ItemObject.ItemTypeEnum.Crossbow: return Gear.Crossbow;
                case ItemObject.ItemTypeEnum.Sling: return Gear.Sling;
                case ItemObject.ItemTypeEnum.Thrown: return Gear.Thrown;
                default: return Gear.None;
            }
        }

        private static string GearText(Gear gear)
        {
            if (gear == Gear.None)
            {
                return "none";
            }
            List<string> parts = new List<string>();
            if ((gear & Gear.OneHanded) != 0) parts.Add("1h");
            if ((gear & Gear.TwoHanded) != 0) parts.Add("2h");
            if ((gear & Gear.Polearm) != 0) parts.Add("polearm");
            if ((gear & Gear.Shield) != 0) parts.Add("shield");
            if ((gear & Gear.Bow) != 0) parts.Add("bow");
            if ((gear & Gear.Crossbow) != 0) parts.Add("crossbow");
            if ((gear & Gear.Sling) != 0) parts.Add("sling");
            if ((gear & Gear.Thrown) != 0) parts.Add("thrown");
            return string.Join(", ", parts);
        }

        // ---- the report --------------------------------------------------------------------------------------

        private const int StatusWidth = 25;

        private string Compose(KeyValuePair<TroopPerkLog.Key, TroopPerkLog.Entry>[] entries)
        {
            // troop -> perk -> its answers, so each listed perk can be looked up as the troop's list is walked.
            Dictionary<CharacterObject, Dictionary<PerkObject, List<KeyValuePair<TroopPerkLog.Key, TroopPerkLog.Entry>>>> byTroop =
                new Dictionary<CharacterObject, Dictionary<PerkObject, List<KeyValuePair<TroopPerkLog.Key, TroopPerkLog.Entry>>>>();
            List<CharacterObject> unspawnedOrder = new List<CharacterObject>();
            foreach (KeyValuePair<TroopPerkLog.Key, TroopPerkLog.Entry> entry in entries)
            {
                Dictionary<PerkObject, List<KeyValuePair<TroopPerkLog.Key, TroopPerkLog.Entry>>> byPerk;
                if (!byTroop.TryGetValue(entry.Key.Troop, out byPerk))
                {
                    byPerk = new Dictionary<PerkObject, List<KeyValuePair<TroopPerkLog.Key, TroopPerkLog.Entry>>>();
                    byTroop[entry.Key.Troop] = byPerk;
                    if (!_spawned.ContainsKey(entry.Key.Troop))
                    {
                        unspawnedOrder.Add(entry.Key.Troop);
                    }
                }
                List<KeyValuePair<TroopPerkLog.Key, TroopPerkLog.Entry>> answers;
                if (!byPerk.TryGetValue(entry.Key.Perk, out answers))
                {
                    answers = new List<KeyValuePair<TroopPerkLog.Key, TroopPerkLog.Entry>>();
                    byPerk[entry.Key.Perk] = answers;
                }
                answers.Add(entry);
            }

            StringBuilder sb = new StringBuilder();
            AppendHeader(sb);

            // Across all spawned troops: which listed perks were granted somewhere, refused somewhere, or never asked.
            HashSet<PerkObject> listedPerks = new HashSet<PerkObject>();
            HashSet<PerkObject> askedPerks = new HashSet<PerkObject>();
            HashSet<PerkObject> grantedPerks = new HashSet<PerkObject>();

            if (_spawnOrder.Count == 0)
            {
                sb.Append("No troop listed in the troop perks file spawned in this mission.\n\n");
            }
            foreach (CharacterObject troop in _spawnOrder)
            {
                Spawned spawned = _spawned[troop];
                sb.Append(TroopLabel(troop))
                  .Append("   agents ").Append(spawned.Agents)
                  .Append(", mounted ").Append(spawned.Mounted)
                  .Append("   gear: ").Append(GearText(spawned.Gear))
                  .Append("\n");

                Dictionary<PerkObject, List<KeyValuePair<TroopPerkLog.Key, TroopPerkLog.Entry>>> byPerk;
                byTroop.TryGetValue(troop, out byPerk);
                List<PerkObject> perks = RBMConfig.TroopPerks.GetOrdered(troop);
                if (perks != null)
                {
                    foreach (PerkObject perk in perks)
                    {
                        listedPerks.Add(perk);
                        List<KeyValuePair<TroopPerkLog.Key, TroopPerkLog.Entry>> answers = null;
                        if (byPerk != null)
                        {
                            byPerk.TryGetValue(perk, out answers);
                        }
                        bool granted = AppendPerk(sb, perk, answers);
                        if (answers != null)
                        {
                            askedPerks.Add(perk);
                        }
                        if (granted)
                        {
                            grantedPerks.Add(perk);
                        }
                    }
                }
                sb.Append("\n");
            }

            // A listed troop asked about with no agent of its own in the mission: some model checked the character
            // itself (rare, but worth seeing; these do not count toward the summary below).
            if (unspawnedOrder.Count > 0)
            {
                sb.Append("Asked about for troops with no agent in this mission:\n\n");
                foreach (CharacterObject troop in unspawnedOrder)
                {
                    sb.Append(TroopLabel(troop)).Append("   agents 0\n");
                    foreach (KeyValuePair<PerkObject, List<KeyValuePair<TroopPerkLog.Key, TroopPerkLog.Entry>>> perk in byTroop[troop])
                    {
                        AppendPerk(sb, perk.Key, perk.Value);
                    }
                    sb.Append("\n");
                }
            }

            AppendSummary(sb, listedPerks, askedPerks, grantedPerks);
            return sb.ToString();
        }

        private void AppendHeader(StringBuilder sb)
        {
            Mission mission = Mission;
            MissionMode endMode = mission.Mode;
            sb.Append("RBM troop perk log: which perks from the troop perks file the game asked about, for the troops that fought.\n");
            sb.Append("\n");
            sb.Append("  date      ").Append(_startedAt.ToString("yyyy-MM-dd HH:mm:ss")).Append("\n");
            sb.Append("  scene     ").Append(_sceneName ?? "?").Append("\n");
            sb.Append("  mode      ").Append(endMode);
            if (endMode != _startMode)
            {
                sb.Append(" (").Append(_startMode).Append(" at start)");
            }
            sb.Append("\n");
            sb.Append("  type      ").Append(MissionType(mission)).Append("\n");
            sb.Append("  duration  ").Append(BattleHitLog.Clock(mission.CurrentTime)).Append(" (mission time)\n");
            sb.Append("\n");
            sb.Append("  GRANTED                  the game asked and RBM answered yes: the perk took effect (x = times asked)\n");
            sb.Append("  QUERIED-NOT-APPLICABLE   asked with a battle environment, but this effect does not apply there\n");
            sb.Append("                           (e.g. a land-only effect at sea), so the answer was no\n");
            sb.Append("  NEVER QUERIED            nothing asked about this perk for this troop: it had no effect in this mission\n");
            sb.Append("  plain                    GetPerkValue(perk)\n");
            sb.Append("  Land primary             GetPerkValue(perk, Land, isPrimaryEffect true, out value); secondary = false\n");
            sb.Append("  callers                  the first methods above the perk check (PerkHelper and the patch skipped),\n");
            sb.Append("                           innermost first, as seen the first time that exact question was asked\n");
            sb.Append("  [no personal effect]     the perk has no personal half, so a troop can never benefit from it\n");
            sb.Append("\n");
        }

        private static string MissionType(Mission mission)
        {
            if (mission.IsNavalBattle) return "naval battle";
            if (mission.IsSiegeBattle) return "siege battle";
            if (mission.IsSallyOutBattle) return "sally-out battle";
            if (mission.IsFieldBattle) return "field battle";
            return "other (" + mission.MissionTeamAIType + ")";
        }

        /// <summary>Writes one listed perk's status and answers. Returns whether it was granted at least once.</summary>
        private static bool AppendPerk(StringBuilder sb, PerkObject perk, List<KeyValuePair<TroopPerkLog.Key, TroopPerkLog.Entry>> answers)
        {
            bool granted = false;
            if (answers != null)
            {
                foreach (KeyValuePair<TroopPerkLog.Key, TroopPerkLog.Entry> answer in answers)
                {
                    if (answer.Key.Granted)
                    {
                        granted = true;
                        break;
                    }
                }
            }
            string status = answers == null ? "NEVER QUERIED" : (granted ? "GRANTED" : "QUERIED-NOT-APPLICABLE");
            sb.Append("  ").Append(status.PadRight(StatusWidth)).Append(perk.StringId);
            if (perk.PrimaryRole != PartyRole.Personal && perk.SecondaryRole != PartyRole.Personal)
            {
                sb.Append("   [no personal effect]");
            }
            sb.Append("\n");
            if (answers == null)
            {
                return false;
            }

            // Granted answers first, then the refusals; within each, the most asked first.
            answers.Sort((a, b) =>
            {
                if (a.Key.Granted != b.Key.Granted)
                {
                    return a.Key.Granted ? -1 : 1;
                }
                return b.Value.Count.CompareTo(a.Value.Count);
            });
            foreach (KeyValuePair<TroopPerkLog.Key, TroopPerkLog.Entry> answer in answers)
            {
                string how = answer.Key.WithEnvironment
                    ? answer.Key.Environment + (answer.Key.IsPrimary ? " primary" : " secondary")
                    : "plain";
                if (!answer.Key.Granted)
                {
                    how += " (not applicable)";
                }
                sb.Append("      ").Append(how.PadRight(30))
                  .Append(("x" + answer.Value.Count).PadLeft(9))
                  .Append("   ")
                  .Append(answer.Value.Callers.Length > 0 ? string.Join(" < ", answer.Value.Callers) : "?")
                  .Append("\n");
            }
            return granted;
        }

        private static void AppendSummary(StringBuilder sb, HashSet<PerkObject> listed, HashSet<PerkObject> asked, HashSet<PerkObject> granted)
        {
            List<PerkObject> never = new List<PerkObject>();
            int refusedOnly = 0;
            foreach (PerkObject perk in listed)
            {
                if (!asked.Contains(perk))
                {
                    never.Add(perk);
                }
                else if (!granted.Contains(perk))
                {
                    refusedOnly++;
                }
            }

            sb.Append("Summary: ").Append(listed.Count).Append(" distinct listed perk(s) on the troops that spawned: ")
              .Append(granted.Count).Append(" granted, ")
              .Append(refusedOnly).Append(" only queried-not-applicable, ")
              .Append(never.Count).Append(" never queried for any of them.\n");
            if (never.Count == 0)
            {
                return;
            }

            sb.Append("\nNever queried for any spawned troop, by skill:\n");
            never.Sort((a, b) =>
            {
                int bySkill = string.CompareOrdinal(SkillId(a), SkillId(b));
                return bySkill != 0 ? bySkill : string.CompareOrdinal(a.StringId, b.StringId);
            });
            string currentSkill = null;
            foreach (PerkObject perk in never)
            {
                string skill = SkillId(perk);
                if (skill != currentSkill)
                {
                    if (currentSkill != null)
                    {
                        sb.Append("\n");
                    }
                    sb.Append("  ").Append(skill.PadRight(13));
                    currentSkill = skill;
                }
                else
                {
                    sb.Append(", ");
                }
                sb.Append(perk.StringId);
            }
            sb.Append("\n");
        }

        private static string SkillId(PerkObject perk)
        {
            return perk.Skill != null ? perk.Skill.StringId : "?";
        }

        private static string TroopLabel(CharacterObject troop)
        {
            string name = troop.Name != null ? troop.Name.ToString() : "";
            return troop.StringId + (name.Length > 0 ? " (" + name + ")" : "");
        }
    }
}
