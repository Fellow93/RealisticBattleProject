using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Spoils are a second xp-like resource: it accumulates on a troop stack and is spent when that
    /// stack upgrades. It is earned by winning battles, scaled by the equipment the losers lost.
    /// </summary>
    public static class SpoilsPool
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

        public static void SyncData(IDataStore dataStore)
        {
            // The key is bumped whenever the meaning of a point of spoils changes, so stale pools are
            // dropped rather than reinterpreted on a scale they were never measured against.
            dataStore.SyncData("RBM_troopSpoilsValue", ref _spoils);
            if (_spoils == null)
            {
                _spoils = new Dictionary<string, int>();
            }
            SpoilsLog.Log("SAVE", (dataStore.IsSaving ? "saved " : "loaded ") + _spoils.Count + " spoils pool entries");
            if (!dataStore.IsSaving)
            {
                SpoilsLog.Log("CONFIG", "spoils cost x" + RBMConfig.RBMConfig.troopUpgradeSpoilsCostMultiplier
                    + ", loot x" + RBMConfig.RBMConfig.troopUpgradeSpoilsLootMultiplier
                    + ", gold cost x" + RBMConfig.RBMConfig.troopUpgradeCostMultiplier);
            }
        }

        // Recomputed for every party, stack and upgrade target on each daily tick otherwise.
        // A troop template's equipment does not change at runtime, so this never goes stale.
        private static readonly Dictionary<CharacterObject, int> _equipmentValueCache = new Dictionary<CharacterObject, int>();

        private static readonly Dictionary<CharacterObject, List<Equipment>> _battleEquipmentCache = new Dictionary<CharacterObject, List<Equipment>>();

        /// <summary>
        /// A troop template usually carries several battle sets and the game picks one at random per
        /// man, so no single set speaks for the stack.
        /// </summary>
        /// <remarks>
        /// CharacterObject.Equipment is the roster's DefaultEquipment, which is not necessarily a
        /// battle set at all -- it can be civilian clothes -- so it is only the last resort for a
        /// troop that somehow declares no battle equipment.
        /// </remarks>
        private static List<Equipment> GetBattleEquipments(CharacterObject character)
        {
            List<Equipment> cached;
            if (_battleEquipmentCache.TryGetValue(character, out cached))
            {
                return cached;
            }
            cached = new List<Equipment>();
            if (character.BattleEquipments != null)
            {
                foreach (Equipment equipment in character.BattleEquipments)
                {
                    if (equipment != null)
                    {
                        cached.Add(equipment);
                    }
                }
            }
            if (cached.Count == 0)
            {
                Equipment fallback = character.FirstBattleEquipment ?? character.Equipment;
                if (fallback != null)
                {
                    cached.Add(fallback);
                }
            }
            _battleEquipmentCache[character] = cached;
            return cached;
        }

        private static int GetSetValue(Equipment equipment)
        {
            int value = 0;
            foreach (EquipmentElement item in EnumerateEquipmentSlots(equipment))
            {
                value += item.ItemValue;
            }
            return value;
        }

        /// <summary>
        /// What a man of this troop is worth in kit, averaged over the battle sets he might be
        /// wearing. Pricing an upgrade off one set would quote every man the cost of the set the
        /// template happens to list first, which for a troop whose sets differ in worth is a price
        /// most of the stack never pays.
        /// </summary>
        public static int GetEquipmentValue(CharacterObject character)
        {
            int cached;
            if (_equipmentValueCache.TryGetValue(character, out cached))
            {
                return cached;
            }
            List<Equipment> sets = GetBattleEquipments(character);
            int total = 0;
            foreach (Equipment equipment in sets)
            {
                total += GetSetValue(equipment);
            }
            int value = (sets.Count == 0) ? 0 : total / sets.Count;
            _equipmentValueCache[character] = value;
            return value;
        }

        private static IEnumerable<EquipmentElement> EnumerateEquipmentSlots(Equipment equipment)
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
            get { return RBMConfig.RBMConfig.troopUpgradeSpoilsCostMultiplier > 0f; }
        }

        /// <summary>
        /// A point of spoils is a unit of equipment value, so an upgrade costs what the better kit is
        /// worth over the old. Zero means the upgrade needs no spoils, so callers must not divide by it.
        /// </summary>
        public static int GetSpoilsCostForUpgrade(CharacterObject character, CharacterObject upgradeTarget)
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
            return MathF.Max(1, MathF.Round(delta * RBMConfig.RBMConfig.troopUpgradeSpoilsCostMultiplier));
        }

        /// <summary>
        /// What a gold piece buys when it is spent as spoils rather than as gold. Both prices are the
        /// same equipment value seen through a different multiplier, so their ratio is the exchange
        /// rate: at the defaults an upgrade costs a tenth of its worth in gold but the whole of it in
        /// spoils, making a gold piece worth ten points. Without this a wage would be quoted against
        /// equipment values it was never measured on, and a soldier could not clothe himself in a
        /// lifetime of pay.
        /// </summary>
        public static float SpoilsPerGold
        {
            get
            {
                float goldMultiplier = RBMConfig.RBMConfig.troopUpgradeCostMultiplier;
                // A free upgrade is worth unbounded spoils per gold; hand back the raw rate instead.
                return goldMultiplier <= 0f ? 1f : RBMConfig.RBMConfig.troopUpgradeSpoilsCostMultiplier / goldMultiplier;
            }
        }

        /// <summary>
        /// The cost of the upgrade a stack is measured against everywhere a single number is needed:
        /// the bar, the loot cap. Index 0 matches the target the xp bar measures itself against.
        /// </summary>
        public static int GetPrimarySpoilsCost(CharacterObject character)
        {
            return character.UpgradeTargets.Length == 0 ? 0 : GetSpoilsCostForUpgrade(character, character.UpgradeTargets[0]);
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
        /// index is one lower. Veterans therefore ignore the cheap kit, leaving it to float down
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
        /// same spoils would be spent twice within one visit to the screen.
        /// </summary>
        public static int GetAvailableSpoils(PartyBase party, CharacterObject character)
        {
            return MathF.Max(0, GetSpoils(party, character) - PartyScreenStagedUpgrades.GetStagedSpoils(party, character));
        }

        /// <summary>
        /// How many men the stockpile can outfit, as a fraction. Two and a half means two upgrade
        /// free and the third pays half price.
        /// </summary>
        public static float GetCoveredMen(PartyBase party, CharacterObject character, CharacterObject upgradeTarget)
        {
            int spoilsCost = GetSpoilsCostForUpgrade(character, upgradeTarget);
            return spoilsCost <= 0 ? 0f : (float)GetAvailableSpoils(party, character) / spoilsCost;
        }

        /// <summary>
        /// Of <paramref name="count"/> men upgrading, how many the gold has to pay for. Spoils are spent
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
            int spoilsCost = GetSpoilsCostForUpgrade(character, upgradeTarget);
            if (spoilsCost <= 0)
            {
                return 0;
            }
            return MathF.Min(GetAvailableSpoils(party, character) / spoilsCost, GetStackSize(party, character));
        }

        /// <summary>Spoils drawn down by upgrading <paramref name="count"/> men, never more than the stockpile holds.</summary>
        public static int GetBatchSpoilsSpend(PartyBase party, CharacterObject character, CharacterObject upgradeTarget, int count)
        {
            int spoilsCost = GetSpoilsCostForUpgrade(character, upgradeTarget);
            if (spoilsCost <= 0 || count <= 0)
            {
                return 0;
            }
            return MathF.Min(GetAvailableSpoils(party, character), spoilsCost * count);
        }

        public static int GetSpoils(PartyBase party, CharacterObject character)
        {
            int spoils;
            return _spoils.TryGetValue(Key(party, character), out spoils) ? spoils : 0;
        }

        public static void AddSpoils(PartyBase party, CharacterObject character, int amount)
        {
            if (amount == 0)
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

        public static void OnMapEventEnded(MapEvent mapEvent)
        {
            if (!IsEnabled || RBMConfig.RBMConfig.troopUpgradeSpoilsLootMultiplier <= 0f)
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
            // stripped: the wounded are carried off still wearing their kit, and the routed fled
            // with theirs. The victors hold the field, so they recover their own fallen as well as
            // the enemy's.
            // Value, not piece count, so a big battle can sum well past what an int would hold.
            long[] spoilsByTier = new long[(int)ItemObject.ItemTiers.NumTiers];
            long intactValue = 0L;
            foreach (MapEventParty loser in winner.OtherSide.Parties)
            {
                CountStrippedEquipment(spoilsByTier, loser.DiedInBattle, ref intactValue);
            }
            foreach (MapEventParty victor in winner.Parties)
            {
                CountStrippedEquipment(spoilsByTier, victor.DiedInBattle, ref intactValue);
            }

            long totalContribution = 0L;
            foreach (MapEventParty victor in winner.Parties)
            {
                totalContribution += MathF.Max(0, victor.ContributionToBattle);
            }
            if (SpoilsLog.IsEnabled)
            {
                long salvagedValue = 0L;
                for (int tier = 0; tier < spoilsByTier.Length; tier++)
                {
                    salvagedValue += spoilsByTier[tier];
                }
                SpoilsLog.Log("LOOT", "battle ended: " + mapEvent.EventType + ", winner side " + mapEvent.WinningSide
                    + ", " + winner.Parties.Count + " victor party(s), " + winner.OtherSide.Parties.Count + " loser party(s)");
                SpoilsLog.Log("LOOT", "  the dead wore " + intactValue + " value; " + salvagedValue + " salvaged ("
                    + (intactValue > 0L ? (100L * salvagedValue / intactValue) : 0L) + "%)");
                for (int tier = 0; tier < spoilsByTier.Length; tier++)
                {
                    if (spoilsByTier[tier] > 0L)
                    {
                        SpoilsLog.Log("LOOT", "  field yields tier " + (tier + 1) + ": " + spoilsByTier[tier] + " value");
                    }
                }
            }

            foreach (MapEventParty victor in winner.Parties)
            {
                // Simulated battles can leave every contribution at zero; fall back to an even split
                // rather than silently paying nobody.
                long weight = (totalContribution > 0L) ? MathF.Max(0, victor.ContributionToBattle) : 1L;
                long divisor = (totalContribution > 0L) ? totalContribution : winner.Parties.Count;
                float share = (float)weight / divisor * RBMConfig.RBMConfig.troopUpgradeSpoilsLootMultiplier;
                SpoilsLog.Log("LOOT", "  " + SpoilsLog.Describe(victor.Party) + ": contribution " + victor.ContributionToBattle
                    + "/" + totalContribution + ", share " + share.ToString("0.000"));
                int granted = GrantToParty(victor.Party, spoilsByTier, share);
                if (victor.Party == PartyBase.MainParty)
                {
                    AnnounceSpoilsToPlayer(granted);
                }
            }
        }

        /// <summary>
        /// The stockpiles fill silently otherwise: the party screen shows a bar the player has to go
        /// looking for, and nothing on the map says a battle paid for anything.
        /// </summary>
        /// <remarks>
        /// A stack that is already fully outfitted takes nothing, so a victory can leave the field
        /// covered in kit and grant zero. Saying so is more use than saying nothing, since it tells
        /// the player his army has no more room for what it just won.
        /// </remarks>
        private static void AnnounceSpoilsToPlayer(int granted)
        {
            TextObject message = new TextObject((granted > 0)
                ? "{=RBM_SPOILS_009}Your men strip the fallen and recover {AMOUNT} in spoils."
                : "{=RBM_SPOILS_010}Your men find nothing on the fallen they can use.");
            message.SetTextVariable("AMOUNT", granted);
            InformationManager.DisplayMessage(new InformationMessage(message.ToString()));
        }

        /// <summary>
        /// A stack's wage is not all pay: part of it is what the men lay out on their own kit, mending
        /// what the last march wore through and replacing what they cannot mend. That part comes back
        /// as spoils. The gold the party pays is untouched -- this only says where some of it went.
        /// </summary>
        /// <remarks>
        /// Applied to every party, since every party pays wages. Unlike battlefield loot this is not
        /// capped at what the stack's next upgrade costs: loot is kit, and a man already wearing the
        /// best of it has no use for more, but wage is coin, and a man with no kit left to buy still
        /// has bread and beer to buy. What the stack does not spend on its own upgrade it carries.
        /// </remarks>
        public static void OnDailyTickParty(MobileParty mobileParty)
        {
            if (!IsEnabled || RBMConfig.RBMConfig.troopWageSpoilsFraction <= 0f || mobileParty == null)
            {
                return;
            }
            PartyBase party = mobileParty.Party;
            TroopRoster roster = party?.MemberRoster;
            if (roster == null)
            {
                return;
            }

            PartyWageModel wageModel = Campaign.Current.Models.PartyWageModel;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character.IsHero)
                {
                    continue;
                }
                // The stack's wage, not one man's, so a small troop's half-point is not rounded away.
                int wage = wageModel.GetCharacterWage(element.Character) * element.Number;
                int granted = MathF.Round(wage * RBMConfig.RBMConfig.troopWageSpoilsFraction * SpoilsPerGold);
                if (granted <= 0)
                {
                    continue;
                }
                if (SpoilsLog.IsEnabled && party == PartyBase.MainParty)
                {
                    SpoilsLog.Log("WAGE", SpoilsLog.Describe(element.Character) + " x" + element.Number
                        + ": wage " + wage + " at " + SpoilsPerGold.ToString("0.0") + " spoils/gold"
                        + " -> +" + granted + " spoils (pool " + GetSpoils(party, element.Character)
                        + " -> " + (GetSpoils(party, element.Character) + granted) + ")");
                }
                AddSpoils(party, element.Character, granted);
            }
        }

        /// <summary>
        /// Fires once per upgraded stack when the player commits the party screen, never when the
        /// screen is cancelled, so spoils staged during the screen is charged exactly here.
        /// </summary>
        /// <summary>
        /// Fires once per upgraded stack when the player commits the party screen, never when the
        /// screen is cancelled. The screen already worked out what each man cost as it went, so the
        /// spoils it reserved is simply drawn down here rather than recomputed.
        /// </summary>
        public static void OnPlayerUpgradedTroops(CharacterObject character, CharacterObject upgradeTarget, int count)
        {
            PartyBase party = PartyBase.MainParty;
            int spend = PartyScreenStagedUpgrades.ConsumeStagedSpoils(party, character);
            if (SpoilsLog.IsEnabled && spend > 0)
            {
                SpoilsLog.Log("UPGRADE", "player upgraded " + count + "x " + SpoilsLog.Describe(character)
                    + " -> " + SpoilsLog.Describe(upgradeTarget)
                    + "| spoils spent " + spend + " of " + (GetSpoilsCostForUpgrade(character, upgradeTarget) * count) + " needed"
                    + ", pool " + GetSpoils(party, character) + " -> " + (GetSpoils(party, character) - spend));
            }
            AddSpoils(party, character, -spend);
            ClearSpoilsIfStackGone(party, character);
        }

        /// <summary>Spoils left on a stack die with the stack, the way its xp does.</summary>
        public static void ClearSpoilsIfStackGone(PartyBase party, CharacterObject character)
        {
            TroopUpkeep.ClearIfStackGone(party, character);
            if (party.MemberRoster.FindIndexOfTroop(character) < 0 && _spoils.Remove(Key(party, character)))
            {
                SpoilsLog.Log("POOL", "stack of " + SpoilsLog.Describe(character) + " gone from "
                    + SpoilsLog.Describe(party) + "; its remaining spoils are lost");
            }
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
                SpoilsLog.Log("POOL", "party " + SpoilsLog.Describe(party.Party) + " destroyed; pruned "
                    + stale.Count + " spoils pool entries");
            }
        }

        /// <summary>Narrowest and widest share of its worth a piece of kit can survive a battle with.</summary>
        private const float MinSalvageFraction = 0.25f;
        private const float MaxSalvageFraction = 0.75f;

        /// <summary>
        /// Nothing comes off a battlefield intact, and nothing is destroyed outright either. Armour
        /// is battered, weapons are chipped, and a quiver is only worth the arrows still in it. The
        /// exact condition of a dead man's kit is not knowable after the fact -- RBM's armour
        /// degradation lives on the mission's agents and dies with them, and simulated battles never
        /// spawn agents at all -- so each piece salvages a random fraction of its worth, between a
        /// quarter and three quarters.
        /// </summary>
        /// <remarks>
        /// For a quiver of arrows or a bundle of javelins -- anything whose PrimaryWeapon is
        /// IsConsumable -- the roll is the share still unspent when its owner fell. For armour and
        /// weapons it is the share that survived the fighting. Same distribution, different reason.
        /// The mean is still a half, so the loot a stack yields averages to half what replacing it
        /// costs, exactly as it did when the roll spanned the whole range.
        /// </remarks>
        private static float RollSalvageFraction(ItemObject item)
        {
            return MBRandom.RandomFloatRanged(MinSalvageFraction, MaxSalvageFraction);
        }

        /// <summary>
        /// Every equipment slot of every fallen man yields part of its item's value, bucketed by the
        /// item's tier so each piece can only be claimed by troops it would actually be an upgrade
        /// for. Rolled per man rather than per troop type, so a hundred casualties average out.
        /// </summary>
        /// <remarks>
        /// Each man is stripped of one battle set drawn at random, the way the game dressed him when
        /// it spawned him. Over a stack this averages to the same value GetEquipmentValue prices an
        /// upgrade against, so a troop cannot yield kit worth more than it costs to replace.
        /// </remarks>
        private static void CountStrippedEquipment(long[] spoilsByTier, TroopRoster roster, ref long intactValue)
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
                List<Equipment> sets = GetBattleEquipments(element.Character);
                if (sets.Count == 0)
                {
                    continue;
                }
                for (int man = 0; man < element.Number; man++)
                {
                    foreach (EquipmentElement item in EnumerateEquipmentSlots(sets[MBRandom.RandomInt(sets.Count)]))
                    {
                        intactValue += item.ItemValue;
                        spoilsByTier[GetItemTier(item.Item)] += (long)(item.ItemValue * RollSalvageFraction(item.Item));
                    }
                }
            }
        }

        /// <summary>
        /// A stack stops taking loot once every man in it is fully covered, leaving the rest for
        /// others. Spoils beyond this point could never be spent anyway.
        /// </summary>
        private static int GetRemainingNeed(PartyBase party, CharacterObject character, int stackSize)
        {
            return MathF.Max(0, GetPrimarySpoilsCost(character) * stackSize - GetSpoils(party, character));
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
        /// <returns>The points the party's stacks actually took, which is less than its share
        /// whenever a tier finds no troop it would upgrade.</returns>
        private static int GrantToParty(PartyBase party, long[] spoilsByTier, float share)
        {
            if (party == null || share <= 0f)
            {
                return 0;
            }
            int granted = 0;
            for (int tier = 0; tier < spoilsByTier.Length; tier++)
            {
                int points = (int)MathF.Min(spoilsByTier[tier] * share, (float)int.MaxValue);
                if (points > 0)
                {
                    granted += GrantTierToParty(party, tier, points);
                }
            }
            return granted;
        }

        private static int GrantTierToParty(PartyBase party, int itemTier, int points)
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
                SpoilsLog.Log("LOOT", "    tier " + (itemTier + 1) + " (" + points + " pts): no claimant in "
                    + SpoilsLog.Describe(party) + ", discarded");
                return 0;
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
                SpoilsLog.Log("LOOT", "    tier " + (itemTier + 1) + ": " + remaining
                    + " of " + points + " pts unclaimed in " + SpoilsLog.Describe(party) + " (everyone full)");
            }
            return points - remaining;
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
                if (SpoilsLog.IsEnabled && granted > 0)
                {
                    int before = GetSpoils(party, element.Character);
                    SpoilsLog.Log("LOOT", "    tier " + (itemTier + 1) + " -> " + SpoilsLog.Describe(element.Character)
                        + " x" + element.Number + " in " + SpoilsLog.Describe(party)
                        + ": +" + granted + " (pool " + before + " -> " + (before + granted)
                        + ", need was " + GetRemainingNeed(party, element.Character, element.Number) + ")");
                }
                AddSpoils(party, element.Character, granted);
                consumed += granted;
            }
            return consumed;
        }
    }

    public class RBMSpoilsCampaignBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, SpoilsPool.OnMapEventEnded);
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, SpoilsPool.OnDailyTickParty);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, SpoilsPool.OnMobilePartyDestroyed);
            CampaignEvents.PlayerUpgradedTroopsEvent.AddNonSerializedListener(this, SpoilsPool.OnPlayerUpgradedTroops);
        }

        public override void SyncData(IDataStore dataStore)
        {
            SpoilsPool.SyncData(dataStore);
        }
    }
}
