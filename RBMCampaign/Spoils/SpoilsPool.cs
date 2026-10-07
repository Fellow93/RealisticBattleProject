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
        // granularity: a TroopRoster holds at most one element per CharacterObject. Saved flat as
        // "partyId#charId" (see StackStore), held in memory per party so a lookup builds no string and
        // pruning one party touches only its own stacks.
        private static readonly StackStore _spoils = new StackStore();

        /// <summary>Identifies one stack, as the stores key it in a save. Shared with the stores that key state the same way.</summary>
        public static string Key(PartyBase party, CharacterObject character)
        {
            return party.Id + "#" + character.StringId;
        }

        /// <summary>Whether <paramref name="key"/> belongs to <paramref name="party"/>, for pruning.</summary>
        public static bool KeyBelongsToParty(string key, PartyBase party)
        {
            // Ordinal, and without building party.Id + "#": a culture-aware StartsWith can match or miss
            // on locale collation rules (the Czech "ch", for one) for what are plain identifiers.
            string id = party.Id ?? string.Empty;
            return key != null && key.Length > id.Length && key[id.Length] == '#'
                && key.StartsWith(id, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// A per-stack integer store -- the spoils pool, and the ration and luxury stores that key state
        /// the same way. In a save it is the flat <c>Dictionary&lt;string, int&gt;</c> it always was, keyed
        /// <c>partyId#charId</c>; in memory it is split by party id, then by character StringId, so a lookup
        /// hashes two ids it already holds instead of concatenating a key, and dropping a party's stacks
        /// touches only that party.
        /// </summary>
        /// <remarks>
        /// Keyed by the ids rather than the objects on purpose: the id pair is exactly what the flat key
        /// encoded, so two stacks share an entry here precisely when they shared one before, and a loaded
        /// key needs no party or character resolved at load time. A saved key is split at its first '#',
        /// which is where the party id ends (no party id holds one), and joined back the same way on save,
        /// so every key round-trips unchanged.
        ///
        /// Every change to a party's entries stamps its bucket with a number never handed out before, so
        /// a cache derived from one party's entries (the unfed-men profile in TroopUpkeep) can tell it is
        /// stale by comparing one int. A party with no bucket reads stamp 0.
        /// </remarks>
        internal sealed class StackStore
        {
            /// <summary>One party's entries, by character StringId, and the stamp of their last change.</summary>
            internal sealed class Bucket
            {
                internal readonly Dictionary<string, int> Values = new Dictionary<string, int>();
                internal int Stamp;
            }

            private static int _lastStamp;

            private readonly Dictionary<string, Bucket> _byParty = new Dictionary<string, Bucket>();

            // Saved keys with no '#' in them. Nothing can produce or address one, but the flat store would
            // have carried it forever, so it is carried here too and the save round-trips unchanged.
            private readonly Dictionary<string, int> _unsplittable = new Dictionary<string, int>();

            private int _count;

            /// <summary>Every entry held, as the flat store would have counted them.</summary>
            internal int Count
            {
                get { return _count + _unsplittable.Count; }
            }

            // A null id joined into the flat key as an empty string, so it is stored under one here.
            internal static string PartyId(PartyBase party)
            {
                return party.Id ?? string.Empty;
            }

            internal static string CharId(CharacterObject character)
            {
                return character.StringId ?? string.Empty;
            }

            private static int NextStamp()
            {
                return System.Threading.Interlocked.Increment(ref _lastStamp);
            }

            internal Bucket GetBucket(PartyBase party)
            {
                return GetBucket(PartyId(party));
            }

            internal Bucket GetBucket(string partyId)
            {
                Bucket bucket;
                return (partyId != null && _byParty.TryGetValue(partyId, out bucket)) ? bucket : null;
            }

            /// <summary>The stamp of the party's last change; 0 when it holds nothing.</summary>
            internal int GetStamp(PartyBase party)
            {
                Bucket bucket = GetBucket(party);
                return bucket != null ? bucket.Stamp : 0;
            }

            internal static bool TryGetValue(Bucket bucket, CharacterObject character, out int value)
            {
                if (bucket == null)
                {
                    value = 0;
                    return false;
                }
                return bucket.Values.TryGetValue(CharId(character), out value);
            }

            internal bool TryGetValue(PartyBase party, CharacterObject character, out int value)
            {
                return TryGetValue(GetBucket(party), character, out value);
            }

            internal void Set(PartyBase party, CharacterObject character, int value)
            {
                Set(PartyId(party), CharId(character), value);
            }

            private void Set(string partyId, string charId, int value)
            {
                Bucket bucket;
                if (!_byParty.TryGetValue(partyId, out bucket))
                {
                    bucket = new Bucket();
                    _byParty[partyId] = bucket;
                }
                int before = bucket.Values.Count;
                bucket.Values[charId] = value;
                _count += bucket.Values.Count - before;
                bucket.Stamp = NextStamp();
            }

            internal bool Remove(PartyBase party, CharacterObject character)
            {
                return Remove(PartyId(party), CharId(character));
            }

            internal bool Remove(string partyId, string charId)
            {
                Bucket bucket;
                if (partyId == null || charId == null || !_byParty.TryGetValue(partyId, out bucket)
                    || !bucket.Values.Remove(charId))
                {
                    return false;
                }
                _count--;
                if (bucket.Values.Count == 0)
                {
                    _byParty.Remove(partyId);
                }
                else
                {
                    bucket.Stamp = NextStamp();
                }
                return true;
            }

            /// <summary>Drops every entry of one party. Returns how many there were.</summary>
            internal int RemoveParty(string partyId)
            {
                Bucket bucket;
                if (partyId == null || !_byParty.TryGetValue(partyId, out bucket))
                {
                    return 0;
                }
                _byParty.Remove(partyId);
                _count -= bucket.Values.Count;
                return bucket.Values.Count;
            }

            internal void Clear()
            {
                _byParty.Clear();
                _unsplittable.Clear();
                _count = 0;
            }

            /// <summary>The flat <c>partyId#charId</c> map the save holds. Built only to save.</summary>
            internal Dictionary<string, int> ToFlat()
            {
                Dictionary<string, int> flat = new Dictionary<string, int>(Count);
                foreach (KeyValuePair<string, Bucket> party in _byParty)
                {
                    string prefix = party.Key + "#";
                    foreach (KeyValuePair<string, int> entry in party.Value.Values)
                    {
                        flat[prefix + entry.Key] = entry.Value;
                    }
                }
                foreach (KeyValuePair<string, int> entry in _unsplittable)
                {
                    flat[entry.Key] = entry.Value;
                }
                return flat;
            }

            /// <summary>Replaces the contents with a flat map read from a save. Null clears it.</summary>
            internal void LoadFlat(Dictionary<string, int> flat)
            {
                Clear();
                if (flat == null)
                {
                    return;
                }
                foreach (KeyValuePair<string, int> entry in flat)
                {
                    string key = entry.Key;
                    int hash = key.IndexOf('#');
                    if (hash < 0)
                    {
                        _unsplittable[key] = entry.Value;
                        continue;
                    }
                    Set(key.Substring(0, hash), key.Substring(hash + 1), entry.Value);
                }
            }

            /// <summary>
            /// Saves or loads under <paramref name="saveKey"/> in the flat format. The current contents
            /// are handed in on load as well, so a save without the key leaves the store as it was, the
            /// way a dictionary passed straight to SyncData is left untouched.
            /// </summary>
            internal void Sync(IDataStore dataStore, string saveKey)
            {
                Dictionary<string, int> flat = ToFlat();
                dataStore.SyncData(saveKey, ref flat);
                if (!dataStore.IsSaving)
                {
                    LoadFlat(flat);
                }
            }
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
            // Same reason: the capture/aftermath handshake tables are settlement-keyed and transient.
            ResetSackHandshake();
        }

        public static void SyncData(IDataStore dataStore)
        {
            // The key is bumped whenever the meaning of a point of spoils changes, so stale pools are
            // dropped rather than reinterpreted on a scale they were never measured against. A point
            // used to be a unit of equipment value, worth ten of the gold an upgrade was priced in.
            // It is now a gold piece. Saved as the flat partyId#charId map it has always been.
            _spoils.Sync(dataStore, "RBM_troopSpoilsGold");
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
        /// same spoils would be spent twice within one visit to the screen. Men dragged in from the other
        /// party this visit bring their purse share only on Done, so that is counted in as well, or men
        /// fetched from a garrison and promoted on the spot would be quoted as if they owned nothing. Men
        /// dragged out take their share away on Done, so that is held back in turn.
        /// </summary>
        public static int GetAvailableSpoils(PartyBase party, CharacterObject character)
        {
            return MathF.Max(0, GetSpoils(party, character)
                + SpoilsTransferOnPartyScreen.GetIncomingSpoils(party, character)
                - SpoilsTransferOnPartyScreen.GetOutgoingSpoils(party, character)
                - PartyScreenStagedUpgrades.GetStagedSpoils(party, character));
        }

        public static int GetSpoils(PartyBase party, CharacterObject character)
        {
            int spoils;
            return _spoils.TryGetValue(party, character, out spoils) ? spoils : 0;
        }

        /// <summary>The whole party's purse: the spoils of every stack on its member roster, summed.</summary>
        public static int GetPartyTotalSpoils(PartyBase party)
        {
            if (party == null)
            {
                return 0;
            }
            // Read through the party's own bucket once rather than per stack.
            StackStore.Bucket bucket = _spoils.GetBucket(party);
            if (bucket == null)
            {
                return 0;
            }
            int total = 0;
            TroopRoster roster = party.MemberRoster;
            for (int i = 0; i < roster.Count; i++)
            {
                int spoils;
                if (StackStore.TryGetValue(bucket, roster.GetCharacterAtIndex(i), out spoils))
                {
                    total += spoils;
                }
            }
            return total;
        }

        public static void AddSpoils(PartyBase party, CharacterObject character, int amount)
        {
            if (amount == 0 || IsExemptParty(party))
            {
                return;
            }
            bool existed = _spoils.TryGetValue(party, character, out int spoils);
            spoils += amount;
            if (spoils <= 0)
            {
                if (existed)
                {
                    _spoils.Remove(party, character);
                }
            }
            else
            {
                _spoils.Set(party, character, spoils);
            }
        }

        /// <summary>Spoils left on a stack die with the stack, the way its xp does.</summary>
        public static void ClearSpoilsIfStackGone(PartyBase party, CharacterObject character)
        {
            TroopUpkeep.ClearIfStackGone(party, character);
            if (party.MemberRoster.FindIndexOfTroop(character) < 0 && _spoils.Remove(party, character))
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
            int removed = 0;
            foreach (string id in exempt)
            {
                removed += _spoils.RemoveParty(id);
            }
            if (removed > 0)
            {
                SpoilsLog.Log("POOL", "pruned " + removed + " spoils pool entries from exempt (villager) parties");
            }
            TroopUpkeep.PruneExemptParties(exempt);
        }

        /// <summary>
        /// Removes every entry a flat <c>partyId#charId</c> map holds for one of <paramref name="partyIds"/>.
        /// The party is read off the key rather than matched prefix by prefix. The pool and the ration
        /// stores now sit in a <see cref="StackStore"/> and drop a party with
        /// <see cref="StackStore.RemoveParty"/>; this stays for any flat map keyed the same way.
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
            // Touches only this party's own bucket rather than scanning the whole pool.
            int count = _spoils.RemoveParty(StackStore.PartyId(party.Party));
            if (count == 0)
            {
                return;
            }
            SpoilsLog.Log("POOL", party.Party, "party " + SpoilsLog.Describe(party.Party) + " destroyed; pruned "
                + count + " spoils pool entries");
        }
    }
}
