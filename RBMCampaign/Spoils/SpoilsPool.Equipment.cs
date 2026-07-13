using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>Pricing a troop's kit, which is what every upgrade and every scrap of loot is measured against.</summary>
    public static partial class SpoilsPool
    {
        // Recomputed for every party, stack and upgrade target on each daily tick otherwise.
        // A troop template's equipment does not change at runtime, so this never goes stale.
        private static readonly Dictionary<CharacterObject, int> _equipmentValueCache = new Dictionary<CharacterObject, int>();

        private static readonly Dictionary<CharacterObject, int> _mountedEquipmentValueCache = new Dictionary<CharacterObject, int>();

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

        private static int GetSetValue(Equipment equipment, bool includeMount = false)
        {
            int value = 0;
            foreach (EquipmentElement item in EnumerateEquipmentSlots(equipment, includeMount))
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

        /// <summary>
        /// What a man of this troop is worth in kit including his horse and its harness, averaged over
        /// his battle sets. Wages are drawn against this: a lancer's mount is a real part of what it
        /// costs to keep him in the field. Upgrades are not, so their pricing keeps to the mount-less
        /// <see cref="GetEquipmentValue"/> -- a rider does not buy his horse anew each time he is promoted.
        /// </summary>
        public static int GetEquipmentValueWithMount(CharacterObject character)
        {
            int cached;
            if (_mountedEquipmentValueCache.TryGetValue(character, out cached))
            {
                return cached;
            }
            List<Equipment> sets = GetBattleEquipments(character);
            int total = 0;
            foreach (Equipment equipment in sets)
            {
                total += GetSetValue(equipment, includeMount: true);
            }
            int value = (sets.Count == 0) ? 0 : total / sets.Count;
            _mountedEquipmentValueCache[character] = value;
            return value;
        }

        /// <summary>
        /// The kit value an upgrade is priced against. When the "charge mount value" feature is on the
        /// horse is bought with gold rather than pulled from the baggage train, so the mount counts;
        /// otherwise pricing keeps to the mount-less value as it always has. One switch read by both the
        /// gold cost and the salvage credit, so the two sides of an upgrade always agree on whether the
        /// horse counts. The cost is a differential of two of these, so a same-mount upgrade only ever
        /// charges the difference in horse quality -- a rider does not re-buy his mount on every promotion.
        /// </summary>
        public static int GetUpgradeEquipmentValue(CharacterObject character)
        {
            return MountValueUpgrade.IsEnabled ? GetEquipmentValueWithMount(character) : GetEquipmentValue(character);
        }

        private static IEnumerable<EquipmentElement> EnumerateEquipmentSlots(Equipment equipment, bool includeMount = false)
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
            if (includeMount)
            {
                // Horse (10) then HorseHarness (11): the two slots the armor and weapon loops above stop short of.
                for (EquipmentIndex i = EquipmentIndex.Horse; i <= EquipmentIndex.HorseHarness; i++)
                {
                    if (!equipment[i].IsEmpty)
                    {
                        yield return equipment[i];
                    }
                }
            }
        }
    }
}
