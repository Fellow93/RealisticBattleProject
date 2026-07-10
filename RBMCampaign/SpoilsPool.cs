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
    /// Spoils are a troop stack's purse. It fills from the kit its men strip off a field they hold
    /// and from the share of their wage they do not pocket, and it empties on their upgrades, their
    /// food, and their drink. Every stack loots, including the ones with no upgrade left to buy.
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

        /// <summary>Zero makes an upgrade free, and a free upgrade has nothing for spoils to buy.</summary>
        public static bool IsEnabled
        {
            get { return RBMConfig.RBMConfig.troopUpgradeCostMultiplier > 0f; }
        }

        /// <summary>
        /// What the better kit is worth over the old, which is exactly what the upgrade costs in gold.
        /// A point of spoils is a gold piece: the two prices are the same number because they are the
        /// same price, paid out of different pockets. Zero means the upgrade needs no spoils, so
        /// callers must not divide by it.
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
            return MathF.Max(1, MathF.Round(delta * RBMConfig.RBMConfig.troopUpgradeCostMultiplier));
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
        /// How far beneath a troop a piece of kit is. Item tiers are zero based (Tier1 == 0) and troop
        /// tiers are one based, so an item matches a troop's tier when its index is one lower. Zero or
        /// less means the kit is as good as his own or better.
        /// </summary>
        private static int GetTierGap(int itemTier, CharacterObject character)
        {
            return (character.Tier - 1) - itemTier;
        }

        /// <summary>
        /// The share of a heap of kit a troop stoops to pick up. He never walks past his own tier or
        /// better, but the further beneath him a piece is the likelier he is to leave it lying: a
        /// veteran picking over a field steps over the recruits' spears without quite seeing them.
        /// What he passes over stays on the field for the greener troops behind him, which is how the
        /// cheap kit reaches the men it would actually be an upgrade for.
        /// </summary>
        /// <remarks>
        /// Compounded per tier of gap rather than subtracted, so nothing is ever certain to be
        /// overlooked and a rag of tier 1 is still worth something to the tier 6 man who finds
        /// himself alone on the field. An overlook chance of 1 is the exception: then a troop sees
        /// nothing at all beneath his own tier, which is how this worked before.
        /// </remarks>
        private static float GetNoticeFraction(int tierGap)
        {
            if (tierGap <= 0)
            {
                return 1f;
            }
            float overlook = MathF.Clamp(RBMConfig.RBMConfig.troopLootOverlookChancePerTier, 0f, 1f);
            return MathF.Pow(1f - overlook, tierGap);
        }

        /// <summary>
        /// Of <paramref name="available"/> pieces lying at a troop's feet, how many he sees. The
        /// fractional piece is rolled for rather than dropped, so a lone piece a veteran would notice
        /// a quarter of the time is not silently unlootable.
        /// </summary>
        private static long GetNoticedPieces(long available, int tierGap)
        {
            float exact = available * GetNoticeFraction(tierGap);
            long whole = (long)exact;
            return whole + ((MBRandom.RandomFloat < exact - whole) ? 1L : 0L);
        }

        /// <summary>Pieces of kit one man will carry off a field, however much of it he sees.</summary>
        private static int GetCarryCapacity(int menInStack)
        {
            return MathF.Max(0, RBMConfig.RBMConfig.troopLootPiecesPerMan) * menInStack;
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

            // What the field yields, bucketed by the tier of the kit so a man's own tier can decide
            // how likely he is to stoop for it. Only the dead are stripped: the wounded are carried
            // off still wearing their kit, and the routed fled with theirs. The victors hold the
            // field, so they recover their own fallen as well as the enemy's.
            //
            // Counted twice over: pieces, because a man can only carry so many, and value, because
            // that is what a piece is worth once it is his. Both are long, since a big battle sums
            // well past what an int would hold.
            long[] spoilsByTier = new long[(int)ItemObject.ItemTiers.NumTiers];
            long[] piecesByTier = new long[(int)ItemObject.ItemTiers.NumTiers];
            long intactValue = 0L;
            foreach (MapEventParty loser in winner.OtherSide.Parties)
            {
                CountStrippedEquipment(spoilsByTier, piecesByTier, loser.DiedInBattle, ref intactValue);
            }
            foreach (MapEventParty victor in winner.Parties)
            {
                CountStrippedEquipment(spoilsByTier, piecesByTier, victor.DiedInBattle, ref intactValue);
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
                        SpoilsLog.Log("LOOT", "  field yields tier " + (tier + 1) + ": " + piecesByTier[tier]
                            + " pieces worth " + spoilsByTier[tier]);
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
                SpoilsLog.Log("LOOT", victor.Party, SpoilsLog.Describe(victor.Party) + ": contribution " + victor.ContributionToBattle
                    + "/" + totalContribution + ", share " + share.ToString("0.000"));
                int granted = GrantToParty(victor.Party, spoilsByTier, piecesByTier, share);
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
        /// A victory can still grant nothing: a small party with its arms already full, or one whose
        /// men walked past everything the field had left. Saying so is more use than saying nothing.
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
        /// Applied to every party, since every party pays wages. A point of spoils is a gold piece, so
        /// half a wage deposits half its gold and no conversion is needed. What the stack does not
        /// spend on its own upgrade it carries, to spend on bread and beer instead.
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
                int granted = MathF.Round(wage * RBMConfig.RBMConfig.troopWageSpoilsFraction);
                if (granted <= 0)
                {
                    continue;
                }
                if (SpoilsLog.IsEnabled && party == PartyBase.MainParty)
                {
                    SpoilsLog.Log("WAGE", party, SpoilsLog.Describe(element.Character) + " x" + element.Number
                        + ": wage " + wage
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
                SpoilsLog.Log("UPGRADE", party, "player upgraded " + count + "x " + SpoilsLog.Describe(character)
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
                SpoilsLog.Log("POOL", party, "stack of " + SpoilsLog.Describe(character) + " gone from "
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
                SpoilsLog.Log("POOL", party.Party, "party " + SpoilsLog.Describe(party.Party) + " destroyed; pruned "
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
        /// item's tier so a looter's own tier can decide how likely he is to notice it. Rolled per man
        /// rather than per troop type, so a hundred casualties average out.
        /// </summary>
        /// <remarks>
        /// Each man is stripped of one battle set drawn at random, the way the game dressed him when
        /// it spawned him. Over a stack this averages to the same value GetEquipmentValue prices an
        /// upgrade against, so a troop cannot yield kit worth more than it costs to replace.
        /// </remarks>
        private static void CountStrippedEquipment(long[] spoilsByTier, long[] piecesByTier, TroopRoster roster, ref long intactValue)
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
                        int tier = GetItemTier(item.Item);
                        intactValue += item.ItemValue;
                        spoilsByTier[tier] += (long)(item.ItemValue * RollSalvageFraction(item.Item));
                        piecesByTier[tier]++;
                    }
                }
            }
        }

        /// <summary>
        /// The party's share of the field, tier by tier. Every stack has a claim on every tier now --
        /// a veteran will take a recruit's spear if nothing better is lying near him -- but the
        /// veterans walk the field first, and the further beneath them a piece is the likelier they
        /// are to step over it. What they overlook or cannot carry is left for the greener troops.
        /// </summary>
        /// <remarks>
        /// The pieces a stack has already carried off are tracked across the whole field, not per
        /// tier, so a man who filled his arms with mail cannot also stoop for six spears.
        /// </remarks>
        /// <returns>The points the party's stacks actually took, which is less than its share
        /// whenever a tier is overlooked or is more than the men have arms to carry.</returns>
        private static int GrantToParty(PartyBase party, long[] spoilsByTier, long[] piecesByTier, float share)
        {
            if (party == null || share <= 0f)
            {
                return 0;
            }
            Dictionary<CharacterObject, int> carried = new Dictionary<CharacterObject, int>();
            int granted = 0;
            for (int tier = 0; tier < spoilsByTier.Length; tier++)
            {
                if (piecesByTier[tier] <= 0L)
                {
                    continue;
                }
                // A piece is worth the tier's average. Scaling the count by the share rather than the
                // worth keeps a party's total at exactly the fraction of the field it earned.
                float valuePerPiece = (float)spoilsByTier[tier] / piecesByTier[tier];
                int pieces = (int)MathF.Min(piecesByTier[tier] * share, (float)int.MaxValue);
                if (pieces > 0 && valuePerPiece > 0f)
                {
                    granted += GrantTierToParty(party, carried, tier, pieces, valuePerPiece);
                }
            }
            return granted;
        }

        private static int GrantTierToParty(PartyBase party, Dictionary<CharacterObject, int> carried, int itemTier, int pieces, float valuePerPiece)
        {
            List<TroopRosterElement> claimants = new List<TroopRosterElement>();
            TroopRoster roster = party.MemberRoster;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (!element.Character.IsHero && GetCarryRoom(carried, element) > 0)
                {
                    claimants.Add(element);
                }
            }
            // Highest troop tier first, so the veterans take their pick before the recruits.
            claimants.Sort((a, b) => b.Character.Tier.CompareTo(a.Character.Tier));

            if (claimants.Count == 0)
            {
                SpoilsLog.Log("LOOT", party, "  tier " + (itemTier + 1) + " (" + pieces + " pieces): no claimant in "
                    + SpoilsLog.Describe(party) + ", discarded");
                return 0;
            }

            int granted = 0;
            int remaining = pieces;
            int groupStart = 0;
            while (groupStart < claimants.Count && remaining > 0)
            {
                int groupEnd = groupStart;
                int groupTier = claimants[groupStart].Character.Tier;
                while (groupEnd < claimants.Count && claimants[groupEnd].Character.Tier == groupTier)
                {
                    groupEnd++;
                }
                // What this rank of troops sees of what is still lying there. The rest they walk past.
                int gap = GetTierGap(itemTier, claimants[groupStart].Character);
                int noticed = (int)MathF.Min(GetNoticedPieces(remaining, gap), (long)remaining);
                remaining -= GrantToTierGroup(party, carried, claimants, groupStart, groupEnd, noticed, itemTier, valuePerPiece, ref granted);
                groupStart = groupEnd;
            }

            if (remaining > 0)
            {
                SpoilsLog.Log("LOOT", party, "  tier " + (itemTier + 1) + ": " + remaining
                    + " of " + pieces + " pieces left lying in " + SpoilsLog.Describe(party)
                    + " (overlooked, full, or no arms to carry them)");
            }
            return granted;
        }

        /// <summary>Pieces this stack has arms left to carry.</summary>
        private static int GetCarryRoom(Dictionary<CharacterObject, int> carried, TroopRosterElement element)
        {
            int taken;
            carried.TryGetValue(element.Character, out taken);
            return MathF.Max(0, GetCarryCapacity(element.Number) - taken);
        }

        /// <summary>
        /// Stacks of equal troop tier have equal claim, so they split by head count, and anything a
        /// stack cannot take, because its arms are already full, is passed around the group before
        /// cascading. Returns how many pieces the group actually carried off.
        /// </summary>
        private static int GrantToTierGroup(PartyBase party, Dictionary<CharacterObject, int> carried, List<TroopRosterElement> claimants, int start, int end, int available, int itemTier, float valuePerPiece, ref int granted)
        {
            int groupMen = 0;
            for (int i = start; i < end; i++)
            {
                groupMen += claimants[i].Number;
            }
            if (groupMen <= 0 || available <= 0)
            {
                return 0;
            }

            int[] shares = new int[end - start];
            int allocated = 0;
            for (int i = start; i < end; i++)
            {
                int proportional = (int)((long)available * claimants[i].Number / groupMen);
                shares[i - start] = MathF.Min(GetCarryRoom(carried, claimants[i]), proportional);
                allocated += shares[i - start];
            }

            // Hand what the full-handed stacks left behind to their peers before it cascades down.
            int leftover = available - allocated;
            for (int i = start; i < end && leftover > 0; i++)
            {
                int room = GetCarryRoom(carried, claimants[i]) - shares[i - start];
                int extra = MathF.Min(leftover, room);
                shares[i - start] += extra;
                leftover -= extra;
            }

            int consumed = 0;
            for (int i = start; i < end; i++)
            {
                TroopRosterElement element = claimants[i];
                int taken = shares[i - start];
                if (taken <= 0)
                {
                    continue;
                }
                int points = MathF.Round(taken * valuePerPiece);
                if (SpoilsLog.IsEnabled)
                {
                    int before = GetSpoils(party, element.Character);
                    SpoilsLog.Log("LOOT", party, "  tier " + (itemTier + 1) + " -> " + SpoilsLog.Describe(element.Character)
                        + " x" + element.Number + " in " + SpoilsLog.Describe(party)
                        + ": " + taken + " pieces, +" + points + " (pool " + before + " -> " + (before + points)
                        + ", arms left " + (GetCarryRoom(carried, element) - taken)
                        + ", gap " + GetTierGap(itemTier, element.Character) + ")");
                }
                AddSpoils(party, element.Character, points);
                int alreadyCarried;
                carried.TryGetValue(element.Character, out alreadyCarried);
                carried[element.Character] = alreadyCarried + taken;
                granted += points;
                consumed += taken;
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
