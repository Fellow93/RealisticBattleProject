using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Spoils are a troop stack's purse. It fills from the kit its men strip off a field they hold
    /// and from the share of their wage they do not pocket, and it empties on their upgrades, their
    /// food, and their drink. Every stack loots, including the ones with no upgrade left to buy.
    /// </summary>
    /// <remarks>
    /// The class is split across several files by what fills or drains the purse:
    /// <list type="bullet">
    /// <item><description>SpoilsPool.cs — the store itself: the dictionary, its keys, save/load.</description></item>
    /// <item><description>SpoilsPool.Equipment.cs — pricing a troop's kit.</description></item>
    /// <item><description>SpoilsPool.UpgradeMath.cs — what an upgrade costs and how far the purse reaches.</description></item>
    /// <item><description>SpoilsPool.BattleLoot.cs — stripping the fallen after a battle.</description></item>
    /// <item><description>SpoilsPool.Plunder.cs — sacking raided villages and stormed towns.</description></item>
    /// <item><description>SpoilsPool.Wages.cs — the share of a daily wage that comes back as spoils.</description></item>
    /// </list>
    /// </remarks>
    public static partial class SpoilsPool
    {
        // TroopRosterElement is a struct with no spare serialized field, so per-stack spoils cannot
        // ride along inside the roster. Keyed by party id + character id, which is the same
        // granularity: a TroopRoster holds at most one element per CharacterObject.
        private static Dictionary<string, int> _spoils = new Dictionary<string, int>();

        /// <summary>Identifies one stack. Shared with the stores that key state the same way.</summary>
        public static string Key(PartyBase party, CharacterObject character)
        {
            return party.Id + "#" + character.StringId;
        }

        /// <summary>Whether <paramref name="key"/> belongs to <paramref name="party"/>, for pruning.</summary>
        public static bool KeyBelongsToParty(string key, PartyBase party)
        {
            return key.StartsWith(party.Id + "#");
        }

        /// <summary>
        /// Villager parties carry goods to market, not war-kit: they take no field loot, sack no
        /// settlement, mend no armour off their wage, and drink in no tavern. The whole spoils system is
        /// about troops keeping their arms, which villagers have none of, so they are exempt from all of
        /// it. Gated at <see cref="AddSpoils"/>, the one funnel every purse fills or drains through, so an
        /// exempt party never holds a purse and everything downstream -- food, carousing, prosperity --
        /// finds nothing to spend and does nothing.
        /// </summary>
        public static bool IsExemptParty(PartyBase party)
        {
            return party == null || (party.MobileParty != null && party.MobileParty.IsVillager);
        }

        /// <summary>
        /// Drops the previous campaign's purses and the caches derived from its characters.
        ///
        /// Called from <see cref="RBMSpoilsCampaignBehavior"/>'s CONSTRUCTOR, which is the only hook
        /// early enough. On load the engine runs LoadBehaviorData -- and so SyncData -- BEFORE
        /// RegisterEvents, so resetting from RegisterEvents or OnSessionLaunched would wipe a genuine
        /// save. The constructor runs from OnGameStart, ahead of the load, so a real save still
        /// repopulates and only a new or keyless campaign starts empty.
        ///
        /// The null guards in SyncData below cannot stand in for this: a key absent from the save
        /// leaves the dictionary untouched rather than nulling it, so leaked state survives them.
        /// </summary>
        public static void Reset()
        {
            _spoils.Clear();
            // Same partial class, so the per-character caches its other files own are reachable here.
            // All are keyed on campaign objects rebuilt for each game; entries from a finished one are
            // dead weight holding a whole campaign's characters alive.
            _equipmentValueCache.Clear();
            _mountedEquipmentValueCache.Clear();
            _battleEquipmentCache.Clear();
            _nobleLineByCulture.Clear();
            // The besieger snapshots the siege drain keeps are transient and settlement-keyed; a finished
            // campaign's entries would otherwise hold its settlements alive into the next.
            _siegeBesiegers.Clear();
        }

        public static void SyncData(IDataStore dataStore)
        {
            // The key is bumped whenever the meaning of a point of spoils changes, so stale pools are
            // dropped rather than reinterpreted on a scale they were never measured against. A point
            // used to be a unit of equipment value, worth ten of the gold an upgrade was priced in.
            // It is now a gold piece.
            dataStore.SyncData("RBM_troopSpoilsGold", ref _spoils);
            if (_spoils == null)
            {
                _spoils = new Dictionary<string, int>();
            }
            SpoilsLog.Log("SAVE", (dataStore.IsSaving ? "saved " : "loaded ") + _spoils.Count + " spoils pool entries");
            if (!dataStore.IsSaving)
            {
                SpoilsLog.Log("CONFIG", "upgrade cost x" + RBMConfig.RBMConfig.troopUpgradeCostMultiplier
                    + " (gold and spoils alike), loot x" + RBMConfig.RBMConfig.troopUpgradeSpoilsLootMultiplier);
            }
        }

        /// <summary>Zero makes an upgrade free, and a free upgrade has nothing for spoils to buy.</summary>
        public static bool IsEnabled
        {
            get { return RBMConfig.RBMConfig.troopUpgradeCostMultiplier > 0f; }
        }

        public static int GetStackSize(PartyBase party, CharacterObject character)
        {
            int index = party.MemberRoster.FindIndexOfTroop(character);
            return index < 0 ? 0 : party.MemberRoster.GetElementCopyAtIndex(index).Number;
        }

        /// <summary>
        /// The stockpile a stack can spend right now. The party screen stages upgrades without
        /// charging for them until the player confirms, so those must be subtracted here or the
        /// same spoils would be spent twice within one visit to the screen.
        /// </summary>
        public static int GetAvailableSpoils(PartyBase party, CharacterObject character)
        {
            return MathF.Max(0, GetSpoils(party, character) - PartyScreenStagedUpgrades.GetStagedSpoils(party, character));
        }

        public static int GetSpoils(PartyBase party, CharacterObject character)
        {
            int spoils;
            return _spoils.TryGetValue(Key(party, character), out spoils) ? spoils : 0;
        }

        /// <summary>The whole party's purse: the spoils of every stack on its member roster, summed.</summary>
        public static int GetPartyTotalSpoils(PartyBase party)
        {
            if (party == null)
            {
                return 0;
            }
            int total = 0;
            TroopRoster roster = party.MemberRoster;
            for (int i = 0; i < roster.Count; i++)
            {
                total += GetSpoils(party, roster.GetCharacterAtIndex(i));
            }
            return total;
        }

        public static void AddSpoils(PartyBase party, CharacterObject character, int amount)
        {
            if (amount == 0 || IsExemptParty(party))
            {
                return;
            }
            string key = Key(party, character);
            int spoils;
            _spoils.TryGetValue(key, out spoils);
            spoils += amount;
            if (spoils <= 0)
            {
                _spoils.Remove(key);
            }
            else
            {
                _spoils[key] = spoils;
            }
        }

        /// <summary>Spoils left on a stack die with the stack, the way its xp does.</summary>
        public static void ClearSpoilsIfStackGone(PartyBase party, CharacterObject character)
        {
            TroopUpkeep.ClearIfStackGone(party, character);
            if (party.MemberRoster.FindIndexOfTroop(character) < 0 && _spoils.Remove(Key(party, character)))
            {
                SpoilsLog.Log("POOL", party, "stack of " + SpoilsLog.Describe(character) + " gone from "
                    + SpoilsLog.Describe(party) + "; its remaining spoils are lost");
            }
        }

        /// <summary>
        /// Clears every purse and ration held by a party now exempt from the system. A save made before
        /// villagers were exempted carries pools their owners can no longer spend or prune, so they are
        /// swept once when a session launches. Nothing is paid back to gold: an exempt party was never
        /// meant to hold spoils, so its stranded pool is dropped rather than paid out.
        /// </summary>
        public static void PruneExemptParties()
        {
            HashSet<string> exempt = new HashSet<string>();
            foreach (MobileParty mobileParty in MobileParty.All)
            {
                PartyBase party = mobileParty?.Party;
                if (party != null && IsExemptParty(party))
                {
                    exempt.Add(party.Id);
                }
            }
            if (exempt.Count == 0)
            {
                return;
            }
            int removed = RemoveEntriesForParties(_spoils, exempt);
            if (removed > 0)
            {
                SpoilsLog.Log("POOL", "pruned " + removed + " spoils pool entries from exempt (villager) parties");
            }
            TroopUpkeep.PruneExemptParties(exempt);
        }

        /// <summary>
        /// Removes every entry a stack-keyed store holds for one of <paramref name="partyIds"/>. Keys are
        /// <c>partyId#charId</c>, so the party is read off the key rather than matched prefix by prefix.
        /// Shared by the spoils pool and the ration stores, which key state the same way.
        /// </summary>
        public static int RemoveEntriesForParties(Dictionary<string, int> store, HashSet<string> partyIds)
        {
            List<string> stale = null;
            foreach (string key in store.Keys)
            {
                int hash = key.IndexOf('#');
                if (hash > 0 && partyIds.Contains(key.Substring(0, hash)))
                {
                    (stale ?? (stale = new List<string>())).Add(key);
                }
            }
            if (stale == null)
            {
                return 0;
            }
            foreach (string key in stale)
            {
                store.Remove(key);
            }
            return stale.Count;
        }

        public static void OnMobilePartyDestroyed(MobileParty party, PartyBase destroyer)
        {
            string prefix = party.Party.Id + "#";
            List<string> stale = new List<string>();
            foreach (string key in _spoils.Keys)
            {
                if (key.StartsWith(prefix))
                {
                    stale.Add(key);
                }
            }
            foreach (string key in stale)
            {
                _spoils.Remove(key);
            }
            if (stale.Count > 0)
            {
                SpoilsLog.Log("POOL", party.Party, "party " + SpoilsLog.Describe(party.Party) + " destroyed; pruned "
                    + stale.Count + " spoils pool entries");
            }
        }
    }
}
