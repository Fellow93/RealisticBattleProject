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
            dataStore.SyncData("RBM_troopGear", ref _gear);
            if (_gear == null)
            {
                _gear = new Dictionary<string, int>();
            }
        }

        // Recomputed for every party, stack and upgrade target on each daily tick otherwise.
        // A troop template's equipment does not change at runtime, so this never goes stale.
        private static readonly Dictionary<CharacterObject, int> _equipmentValueCache = new Dictionary<CharacterObject, int>();

        public static int GetEquipmentValue(CharacterObject character)
        {
            int cached;
            if (_equipmentValueCache.TryGetValue(character, out cached))
            {
                return cached;
            }
            int value = 0;
            for (EquipmentIndex i = EquipmentIndex.ArmorItemBeginSlot; i < EquipmentIndex.ArmorItemEndSlot; i++)
            {
                if (!character.Equipment[i].IsEmpty)
                {
                    value += character.Equipment[i].ItemValue;
                }
            }
            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
            {
                if (!character.Equipment[i].IsEmpty)
                {
                    value += character.Equipment[i].ItemValue;
                }
            }
            _equipmentValueCache[character] = value;
            return value;
        }

        public static bool IsEnabled
        {
            get { return RBMConfig.RBMConfig.troopUpgradeGearCostMultiplier > 0f; }
        }

        /// <summary>Zero means the upgrade needs no gear at all, so callers must not divide by it.</summary>
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

        public static int GetStackSize(PartyBase party, CharacterObject character)
        {
            int index = party.MemberRoster.FindIndexOfTroop(character);
            return index < 0 ? 0 : party.MemberRoster.GetElementCopyAtIndex(index).Number;
        }

        /// <summary>
        /// Every man in a stack carries an equal share of its gear, so one man's share does not
        /// depend on how many of his fellows are being upgraded alongside him. That keeps the
        /// per-unit gold price the party screen shows honest.
        /// </summary>
        private static int GetGearPerMan(PartyBase party, CharacterObject character, int stackSize)
        {
            return stackSize <= 0 ? 0 : GetGear(party, character) / stackSize;
        }

        /// <summary>The share one soldier carries, counting men the open party screen has staged.</summary>
        public static int GetGearPerMan(PartyBase party, CharacterObject character)
        {
            return GetGearPerMan(party, character, GetEffectiveStackSize(party, character));
        }

        private static int GetEffectiveStackSize(PartyBase party, CharacterObject character)
        {
            return GetStackSize(party, character) + PartyScreenStagedUpgrades.GetStagedCount(party, character);
        }

        /// <summary>
        /// Fraction of an upgrade's gear requirement that a soldier already carries. Gold pays for
        /// the rest, so full coverage means a free upgrade and no coverage means today's full price.
        /// </summary>
        public static float GetGoldCoverage(PartyBase party, CharacterObject character, CharacterObject upgradeTarget)
        {
            int gearCost = GetGearCostForUpgrade(character, upgradeTarget);
            if (gearCost <= 0)
            {
                return 0f;
            }
            // The open party screen removes upgraded men from the roster but only charges their gear
            // on confirm. Counting them keeps the quoted price from sliding as the stack shrinks.
            int perMan = GetGearPerMan(party, character, GetEffectiveStackSize(party, character));
            return MathF.Clamp((float)perMan / gearCost, 0f, 1f);
        }

        /// <summary>
        /// Gear drawn down by upgrading <paramref name="count"/> men out of a stack that held
        /// <paramref name="stackSize"/> before the upgrade. Never exceeds the pool, since count &lt;= stackSize.
        /// </summary>
        public static int GetGearSpend(PartyBase party, CharacterObject character, CharacterObject upgradeTarget, int count, int stackSize)
        {
            int gearCost = GetGearCostForUpgrade(character, upgradeTarget);
            if (gearCost <= 0 || count <= 0)
            {
                return 0;
            }
            return MathF.Min(gearCost, GetGearPerMan(party, character, stackSize)) * count;
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

            int spoils = 0;
            foreach (MapEventParty loser in winner.OtherSide.Parties)
            {
                spoils += GetStrippedEquipmentValue(loser.DiedInBattle);
                spoils += GetStrippedEquipmentValue(loser.WoundedInBattle);
            }
            spoils = MathF.Round(spoils * RBMConfig.RBMConfig.troopUpgradeGearLootMultiplier);
            if (spoils <= 0)
            {
                return;
            }

            long totalContribution = 0L;
            foreach (MapEventParty victor in winner.Parties)
            {
                totalContribution += MathF.Max(0, victor.ContributionToBattle);
            }
            foreach (MapEventParty victor in winner.Parties)
            {
                // Simulated battles can leave every contribution at zero; fall back to an even split
                // rather than silently paying nobody.
                long weight = (totalContribution > 0L) ? MathF.Max(0, victor.ContributionToBattle) : 1L;
                long divisor = (totalContribution > 0L) ? totalContribution : winner.Parties.Count;
                GrantToParty(victor.Party, (int)((long)spoils * weight / divisor));
            }
        }

        /// <summary>
        /// Fires once per upgraded stack when the player commits the party screen, never when the
        /// screen is cancelled, so gear staged during the screen is charged exactly here.
        /// </summary>
        public static void OnPlayerUpgradedTroops(CharacterObject character, CharacterObject upgradeTarget, int count)
        {
            PartyBase party = PartyBase.MainParty;
            // The roster has already lost the upgraded men by the time this fires, so reconstruct
            // the stack size their gear share was priced against.
            int stackSize = GetStackSize(party, character) + PartyScreenStagedUpgrades.GetStagedCount(party, character);
            AddGear(party, character, -GetGearSpend(party, character, upgradeTarget, count, stackSize));
            ClearGearIfStackGone(party, character);
        }

        /// <summary>Gear left on a stack dies with the stack, the way its xp does.</summary>
        public static void ClearGearIfStackGone(PartyBase party, CharacterObject character)
        {
            if (party.MemberRoster.FindIndexOfTroop(character) < 0)
            {
                _gear.Remove(Key(party, character));
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
        }

        private static int GetStrippedEquipmentValue(TroopRoster roster)
        {
            if (roster == null)
            {
                return 0;
            }
            int value = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (!element.Character.IsHero)
                {
                    value += GetEquipmentValue(element.Character) * element.Number;
                }
            }
            return value;
        }

        /// <summary>Spread a party's spoils across its regular stacks in proportion to head count.</summary>
        private static void GrantToParty(PartyBase party, int amount)
        {
            if (party == null || amount <= 0)
            {
                return;
            }
            TroopRoster roster = party.MemberRoster;
            int totalRegulars = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (!element.Character.IsHero)
                {
                    totalRegulars += element.Number;
                }
            }
            if (totalRegulars <= 0)
            {
                return;
            }
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (!element.Character.IsHero)
                {
                    AddGear(party, element.Character, (int)((long)amount * element.Number / totalRegulars));
                }
            }
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
