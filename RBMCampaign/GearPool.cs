using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Gear is a second xp-like resource: it accumulates on a troop stack and is spent when that
    /// stack upgrades. It is earned by winning battles, scaled by the equipment the losers lost.
    /// </summary>
    public static class GearPool
    {
        // TroopRosterElement is a struct with no spare serialized field, so per-stack gear cannot
        // ride along inside the roster. Keyed by party id + character id, which is the same
        // granularity: a TroopRoster holds at most one element per CharacterObject.
        private static Dictionary<string, int> _gear = new Dictionary<string, int>();

        private static string Key(PartyBase party, CharacterObject character)
        {
            return party.Id + "#" + character.StringId;
        }

        public static void SyncData(IDataStore dataStore)
        {
            // The key is bumped whenever the meaning of a gear point changes, so stale pools are
            // dropped rather than reinterpreted on a scale they were never measured against.
            dataStore.SyncData("RBM_troopGearValue", ref _gear);
            if (_gear == null)
            {
                _gear = new Dictionary<string, int>();
            }
            GearLog.Log("SAVE", (dataStore.IsSaving ? "saved " : "loaded ") + _gear.Count + " gear pool entries");
            if (!dataStore.IsSaving)
            {
                GearLog.Log("CONFIG", "gear cost x" + RBMConfig.RBMConfig.troopUpgradeGearCostMultiplier
                    + ", loot x" + RBMConfig.RBMConfig.troopUpgradeGearLootMultiplier
                    + ", gold cost x" + RBMConfig.RBMConfig.troopUpgradeCostMultiplier);
            }
        }

        // Recomputed for every party, stack and upgrade target on each daily tick otherwise.
        // A troop template's equipment does not change at runtime, so this never goes stale.
        private static readonly Dictionary<CharacterObject, int> _equipmentValueCache = new Dictionary<CharacterObject, int>();

        /// <summary>
        /// CharacterObject.Equipment is the roster's DefaultEquipment, which is not necessarily a
        /// battle set, so it can price a soldier by his civilian clothes. Value his war gear.
        /// </summary>
        private static Equipment GetBattleEquipment(CharacterObject character)
        {
            return character.FirstBattleEquipment ?? character.Equipment;
        }

        public static int GetEquipmentValue(CharacterObject character)
        {
            int cached;
            if (_equipmentValueCache.TryGetValue(character, out cached))
            {
                return cached;
            }
            Equipment equipment = GetBattleEquipment(character);
            int value = 0;
            foreach (EquipmentElement item in EnumerateGearSlots(equipment))
            {
                value += item.ItemValue;
            }
            _equipmentValueCache[character] = value;
            return value;
        }

        private static IEnumerable<EquipmentElement> EnumerateGearSlots(Equipment equipment)
        {
            if (equipment == null)
            {
                yield break;
            }
            for (EquipmentIndex i = EquipmentIndex.ArmorItemBeginSlot; i < EquipmentIndex.ArmorItemEndSlot; i++)
            {
                if (!equipment[i].IsEmpty)
                {
                    yield return equipment[i];
                }
            }
            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
            {
                if (!equipment[i].IsEmpty)
                {
                    yield return equipment[i];
                }
            }
        }

        public static bool IsEnabled
        {
            get { return RBMConfig.RBMConfig.troopUpgradeGearCostMultiplier > 0f; }
        }

        /// <summary>
        /// A gear point is a unit of equipment value, so an upgrade costs what the better kit is
        /// worth over the old. Zero means the upgrade needs no gear, so callers must not divide by it.
        /// </summary>
        public static int GetGearCostForUpgrade(CharacterObject character, CharacterObject upgradeTarget)
        {
            if (!IsEnabled)
            {
                return 0;
            }
            int delta = GetEquipmentValue(upgradeTarget) - GetEquipmentValue(character);
            if (delta <= 0)
            {
                return 0;
            }
            return MathF.Max(1, MathF.Round(delta * RBMConfig.RBMConfig.troopUpgradeGearCostMultiplier));
        }

        /// <summary>
        /// The cost of the upgrade a stack is measured against everywhere a single number is needed:
        /// the bar, the loot cap. Index 0 matches the target the xp bar measures itself against.
        /// </summary>
        public static int GetPrimaryGearCost(CharacterObject character)
        {
            return character.UpgradeTargets.Length == 0 ? 0 : GetGearCostForUpgrade(character, character.UpgradeTargets[0]);
        }

        /// <summary>
        /// ItemObject.Tier is clamp(round(Tierf), 0, 6) - 1, so it yields -1 for anything whose
        /// Tierf rounds down to zero. Fold those into Tier1 rather than indexing off the array.
        /// </summary>
        private static int GetItemTier(ItemObject item)
        {
            return MathF.Min(MathF.Max((int)item.Tier, 0), (int)ItemObject.ItemTiers.NumTiers - 1);
        }

        /// <summary>
        /// A soldier only bothers with kit of his own tier or better. Item tiers are zero based
        /// (Tier1 == 0) and troop tiers are one based, so an item matches a troop's tier when its
        /// index is one lower. Veterans therefore ignore the cheap gear, leaving it to float down
        /// to the greener troops behind them.
        /// </summary>
        private static bool IsUpgradeFor(int itemTier, CharacterObject character)
        {
            return itemTier >= character.Tier - 1;
        }

        public static int GetStackSize(PartyBase party, CharacterObject character)
        {
            int index = party.MemberRoster.FindIndexOfTroop(character);
            return index < 0 ? 0 : party.MemberRoster.GetElementCopyAtIndex(index).Number;
        }

        /// <summary>
        /// The stockpile a stack can spend right now. The party screen stages upgrades without
        /// charging for them until the player confirms, so those must be subtracted here or the
        /// same gear would be spent twice within one visit to the screen.
        /// </summary>
        public static int GetAvailableGear(PartyBase party, CharacterObject character)
        {
            return MathF.Max(0, GetGear(party, character) - PartyScreenStagedUpgrades.GetStagedGear(party, character));
        }

        /// <summary>
        /// How many men the stockpile can outfit, as a fraction. Two and a half means two upgrade
        /// free and the third pays half price.
        /// </summary>
        public static float GetCoveredMen(PartyBase party, CharacterObject character, CharacterObject upgradeTarget)
        {
            int gearCost = GetGearCostForUpgrade(character, upgradeTarget);
            return gearCost <= 0 ? 0f : (float)GetAvailableGear(party, character) / gearCost;
        }

        /// <summary>
        /// Of <paramref name="count"/> men upgrading, how many the gold has to pay for. Gear is spent
        /// one man at a time rather than smeared across the stack, so the first men go free and only
        /// what the stockpile cannot reach is charged.
        /// </summary>
        public static float GetUnpaidMen(PartyBase party, CharacterObject character, CharacterObject upgradeTarget, int count)
        {
            return MathF.Max(0f, count - MathF.Min(GetCoveredMen(party, character, upgradeTarget), (float)count));
        }

        /// <summary>Whole men the stockpile outfits outright, capped at the stack.</summary>
        public static int GetFreeUpgradeCount(PartyBase party, CharacterObject character, CharacterObject upgradeTarget)
        {
            int gearCost = GetGearCostForUpgrade(character, upgradeTarget);
            if (gearCost <= 0)
            {
                return 0;
            }
            return MathF.Min(GetAvailableGear(party, character) / gearCost, GetStackSize(party, character));
        }

        /// <summary>Gear drawn down by upgrading <paramref name="count"/> men, never more than the stockpile holds.</summary>
        public static int GetBatchGearSpend(PartyBase party, CharacterObject character, CharacterObject upgradeTarget, int count)
        {
            int gearCost = GetGearCostForUpgrade(character, upgradeTarget);
            if (gearCost <= 0 || count <= 0)
            {
                return 0;
            }
            return MathF.Min(GetAvailableGear(party, character), gearCost * count);
        }

        public static int GetGear(PartyBase party, CharacterObject character)
        {
            int gear;
            return _gear.TryGetValue(Key(party, character), out gear) ? gear : 0;
        }

        public static void AddGear(PartyBase party, CharacterObject character, int amount)
        {
            if (amount == 0)
            {
                return;
            }
            string key = Key(party, character);
            int gear;
            _gear.TryGetValue(key, out gear);
            gear += amount;
            if (gear <= 0)
            {
                _gear.Remove(key);
            }
            else
            {
                _gear[key] = gear;
            }
        }

        public static void OnMapEventEnded(MapEvent mapEvent)
        {
            if (!IsEnabled || RBMConfig.RBMConfig.troopUpgradeGearLootMultiplier <= 0f)
            {
                return;
            }
            MapEventSide winner = mapEvent.Winner;
            if (winner == null || winner.OtherSide == null)
            {
                return;
            }

            // One point per piece of kit left on the field, bucketed by its tier so each piece can
            // only be handed to troops it would actually be an upgrade for. Only the dead are
            // stripped: the wounded are carried off still wearing their gear, and the routed fled
            // with theirs. The victors hold the field, so they recover their own fallen as well as
            // the enemy's.
            // Value, not piece count, so a big battle can sum well past what an int would hold.
            long[] spoilsByTier = new long[(int)ItemObject.ItemTiers.NumTiers];
            long intactValue = 0L;
            foreach (MapEventParty loser in winner.OtherSide.Parties)
            {
                CountStrippedGear(spoilsByTier, loser.DiedInBattle, ref intactValue);
            }
            foreach (MapEventParty victor in winner.Parties)
            {
                CountStrippedGear(spoilsByTier, victor.DiedInBattle, ref intactValue);
            }

            long totalContribution = 0L;
            foreach (MapEventParty victor in winner.Parties)
            {
                totalContribution += MathF.Max(0, victor.ContributionToBattle);
            }
            if (GearLog.IsEnabled)
            {
                long salvagedValue = 0L;
                for (int tier = 0; tier < spoilsByTier.Length; tier++)
                {
                    salvagedValue += spoilsByTier[tier];
                }
                GearLog.Log("LOOT", "battle ended: " + mapEvent.EventType + ", winner side " + mapEvent.WinningSide
                    + ", " + winner.Parties.Count + " victor party(s), " + winner.OtherSide.Parties.Count + " loser party(s)");
                GearLog.Log("LOOT", "  the dead wore " + intactValue + " value; " + salvagedValue + " salvaged ("
                    + (intactValue > 0L ? (100L * salvagedValue / intactValue) : 0L) + "%)");
                for (int tier = 0; tier < spoilsByTier.Length; tier++)
                {
                    if (spoilsByTier[tier] > 0L)
                    {
                        GearLog.Log("LOOT", "  field yields tier " + (tier + 1) + ": " + spoilsByTier[tier] + " value");
                    }
                }
            }

            foreach (MapEventParty victor in winner.Parties)
            {
                // Simulated battles can leave every contribution at zero; fall back to an even split
                // rather than silently paying nobody.
                long weight = (totalContribution > 0L) ? MathF.Max(0, victor.ContributionToBattle) : 1L;
                long divisor = (totalContribution > 0L) ? totalContribution : winner.Parties.Count;
                float share = (float)weight / divisor * RBMConfig.RBMConfig.troopUpgradeGearLootMultiplier;
                GearLog.Log("LOOT", "  " + GearLog.Describe(victor.Party) + ": contribution " + victor.ContributionToBattle
                    + "/" + totalContribution + ", share " + share.ToString("0.000"));
                GrantToParty(victor.Party, spoilsByTier, share);
            }
        }

        /// <summary>
        /// Fires once per upgraded stack when the player commits the party screen, never when the
        /// screen is cancelled, so gear staged during the screen is charged exactly here.
        /// </summary>
        /// <summary>
        /// Fires once per upgraded stack when the player commits the party screen, never when the
        /// screen is cancelled. The screen already worked out what each man cost as it went, so the
        /// gear it reserved is simply drawn down here rather than recomputed.
        /// </summary>
        public static void OnPlayerUpgradedTroops(CharacterObject character, CharacterObject upgradeTarget, int count)
        {
            PartyBase party = PartyBase.MainParty;
            int spend = PartyScreenStagedUpgrades.ConsumeStagedGear(party, character);
            if (GearLog.IsEnabled && spend > 0)
            {
                GearLog.Log("UPGRADE", "player upgraded " + count + "x " + GearLog.Describe(character)
                    + " -> " + GearLog.Describe(upgradeTarget)
                    + " | gear spent " + spend + " of " + (GetGearCostForUpgrade(character, upgradeTarget) * count) + " needed"
                    + ", pool " + GetGear(party, character) + " -> " + (GetGear(party, character) - spend));
            }
            AddGear(party, character, -spend);
            ClearGearIfStackGone(party, character);
        }

        /// <summary>Gear left on a stack dies with the stack, the way its xp does.</summary>
        public static void ClearGearIfStackGone(PartyBase party, CharacterObject character)
        {
            if (party.MemberRoster.FindIndexOfTroop(character) < 0 && _gear.Remove(Key(party, character)))
            {
                GearLog.Log("POOL", "stack of " + GearLog.Describe(character) + " gone from "
                    + GearLog.Describe(party) + "; its remaining gear is lost");
            }
        }

        public static void OnMobilePartyDestroyed(MobileParty party, PartyBase destroyer)
        {
            string prefix = party.Party.Id + "#";
            List<string> stale = new List<string>();
            foreach (string key in _gear.Keys)
            {
                if (key.StartsWith(prefix))
                {
                    stale.Add(key);
                }
            }
            foreach (string key in stale)
            {
                _gear.Remove(key);
            }
            if (stale.Count > 0)
            {
                GearLog.Log("POOL", "party " + GearLog.Describe(party.Party) + " destroyed; pruned "
                    + stale.Count + " gear pool entries");
            }
        }

        /// <summary>
        /// Nothing comes off a battlefield intact. Armour is battered, weapons are chipped, and a
        /// quiver is only worth the arrows still in it. The exact condition of a dead man's kit is
        /// not knowable after the fact -- RBM's armour degradation lives on the mission's agents and
        /// dies with them, and simulated battles never spawn agents at all -- so each piece salvages
        /// a random fraction of its worth.
        /// </summary>
        /// <remarks>
        /// For a quiver of arrows or a bundle of javelins -- anything whose PrimaryWeapon is
        /// IsConsumable -- the roll is the share still unspent when its owner fell. For armour and
        /// weapons it is the share that survived the fighting. Same distribution, different reason.
        /// </remarks>
        private static float RollSalvageFraction(ItemObject item)
        {
            return MBRandom.RandomFloat;
        }

        /// <summary>
        /// Every gear slot of every fallen man yields part of its item's value, bucketed by the
        /// item's tier so each piece can only be claimed by troops it would actually be an upgrade
        /// for. Rolled per man rather than per troop type, so a hundred casualties average out.
        /// </summary>
        private static void CountStrippedGear(long[] spoilsByTier, TroopRoster roster, ref long intactValue)
        {
            if (roster == null)
            {
                return;
            }
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character.IsHero)
                {
                    continue;
                }
                foreach (EquipmentElement item in EnumerateGearSlots(GetBattleEquipment(element.Character)))
                {
                    int tier = GetItemTier(item.Item);
                    intactValue += (long)item.ItemValue * element.Number;
                    long salvaged = 0L;
                    for (int man = 0; man < element.Number; man++)
                    {
                        salvaged += (long)(item.ItemValue * RollSalvageFraction(item.Item));
                    }
                    spoilsByTier[tier] += salvaged;
                }
            }
        }

        /// <summary>
        /// A stack stops taking loot once every man in it is fully covered, leaving the rest for
        /// others. Gear beyond this point could never be spent anyway.
        /// </summary>
        private static int GetRemainingNeed(PartyBase party, CharacterObject character, int stackSize)
        {
            return MathF.Max(0, GetPrimaryGearCost(character) * stackSize - GetGear(party, character));
        }

        /// <summary>
        /// Each tier of loot is shared only among the stacks it would actually upgrade. A tier 1
        /// pitchfork is worth nothing to anyone; a tier 5 hauberk is worth a point to recruits and
        /// veterans alike, but not to the tier 5 troops already wearing one.
        ///
        /// Within a tier the veterans pick over the field first: stacks are served in descending
        /// troop tier, each taking at most what it still needs, and whatever they leave cascades
        /// down to the greener troops behind them.
        /// </summary>
        private static void GrantToParty(PartyBase party, long[] spoilsByTier, float share)
        {
            if (party == null || share <= 0f)
            {
                return;
            }
            for (int tier = 0; tier < spoilsByTier.Length; tier++)
            {
                int points = (int)MathF.Min(spoilsByTier[tier] * share, (float)int.MaxValue);
                if (points > 0)
                {
                    GrantTierToParty(party, tier, points);
                }
            }
        }

        private static void GrantTierToParty(PartyBase party, int itemTier, int points)
        {
            List<TroopRosterElement> claimants = new List<TroopRosterElement>();
            TroopRoster roster = party.MemberRoster;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (!element.Character.IsHero
                    && IsUpgradeFor(itemTier, element.Character)
                    && GetRemainingNeed(party, element.Character, element.Number) > 0)
                {
                    claimants.Add(element);
                }
            }
            // Highest troop tier first, so the veterans take their pick before the recruits.
            claimants.Sort((a, b) => b.Character.Tier.CompareTo(a.Character.Tier));

            if (claimants.Count == 0)
            {
                GearLog.Log("LOOT", "    tier " + (itemTier + 1) + " (" + points + " pts): no claimant in "
                    + GearLog.Describe(party) + ", discarded");
                return;
            }

            int remaining = points;
            int groupStart = 0;
            while (groupStart < claimants.Count && remaining > 0)
            {
                int groupEnd = groupStart;
                int groupTier = claimants[groupStart].Character.Tier;
                while (groupEnd < claimants.Count && claimants[groupEnd].Character.Tier == groupTier)
                {
                    groupEnd++;
                }
                remaining -= GrantToTierGroup(party, claimants, groupStart, groupEnd, remaining, itemTier);
                groupStart = groupEnd;
            }

            if (remaining > 0)
            {
                GearLog.Log("LOOT", "    tier " + (itemTier + 1) + ": " + remaining
                    + " of " + points + " pts unclaimed in " + GearLog.Describe(party) + " (everyone full)");
            }
        }

        /// <summary>
        /// Stacks of equal troop tier have equal claim, so they split by head count, and anything a
        /// stack cannot take because it is nearly full is passed around the group before cascading.
        /// Returns how many points the group actually consumed.
        /// </summary>
        private static int GrantToTierGroup(PartyBase party, List<TroopRosterElement> claimants, int start, int end, int available, int itemTier)
        {
            int groupMen = 0;
            for (int i = start; i < end; i++)
            {
                groupMen += claimants[i].Number;
            }
            if (groupMen <= 0)
            {
                return 0;
            }

            int[] shares = new int[end - start];
            int allocated = 0;
            for (int i = start; i < end; i++)
            {
                TroopRosterElement element = claimants[i];
                int need = GetRemainingNeed(party, element.Character, element.Number);
                int proportional = (int)((long)available * element.Number / groupMen);
                shares[i - start] = MathF.Min(need, proportional);
                allocated += shares[i - start];
            }

            // Hand what the near-full stacks left behind to their peers before it cascades down.
            int leftover = available - allocated;
            for (int i = start; i < end && leftover > 0; i++)
            {
                TroopRosterElement element = claimants[i];
                int room = GetRemainingNeed(party, element.Character, element.Number) - shares[i - start];
                int extra = MathF.Min(leftover, room);
                shares[i - start] += extra;
                leftover -= extra;
            }

            int consumed = 0;
            for (int i = start; i < end; i++)
            {
                TroopRosterElement element = claimants[i];
                int granted = shares[i - start];
                if (GearLog.IsEnabled && granted > 0)
                {
                    int before = GetGear(party, element.Character);
                    GearLog.Log("LOOT", "    tier " + (itemTier + 1) + " -> " + GearLog.Describe(element.Character)
                        + " x" + element.Number + " in " + GearLog.Describe(party)
                        + ": +" + granted + " (pool " + before + " -> " + (before + granted)
                        + ", need was " + GetRemainingNeed(party, element.Character, element.Number) + ")");
                }
                AddGear(party, element.Character, granted);
                consumed += granted;
            }
            return consumed;
        }
    }

    public class RBMGearCampaignBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, GearPool.OnMapEventEnded);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, GearPool.OnMobilePartyDestroyed);
            CampaignEvents.PlayerUpgradedTroopsEvent.AddNonSerializedListener(this, GearPool.OnPlayerUpgradedTroops);
        }

        public override void SyncData(IDataStore dataStore)
        {
            GearPool.SyncData(dataStore);
        }
    }
}
