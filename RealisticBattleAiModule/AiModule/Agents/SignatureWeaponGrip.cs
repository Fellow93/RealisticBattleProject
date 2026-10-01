using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    public static partial class AgentAi
    {
        /// <summary>
        /// Troops whose signature weapon is two-handed (cataphract lance, falx, rhomphaia) fight with it in both hands and
        /// keep the shield slung on the back.
        ///
        /// None of the engine's knobs do this on their own: with a shield carried, the native AI always takes the
        /// weapon's one-handed grip (OneHandedPolearm / OneHandedBastardSword usage, or the couch grip on a charge) and
        /// raises the shield, and weapon favors / missile settings do not change that for mounted agents. What works:
        ///  1. the per-agent copy of the weapon's data handed to the engine gets its one-handed grips replaced by the
        ///     two-handed grip and every melee grip marked two-handed only, so no grip admits a shield;
        ///  2. the native AI's per-tick input is filtered (OnAIInputSet) - the AI switches weapons through the
        ///     Wield0..3 / Sheath0 input events - per the troop's profile below.
        /// The ItemObject is untouched, so the player and every other troop keep all grips.
        /// </summary>
        public static class SignatureWeaponGrip
        {
            // Both profiles: throwing weapons may be drawn only while the target is at throwing distance, and are
            // never used in melee.
            public enum Profile
            {
                None,
                // Shield and sidearm are allowed once an enemy is within WeaponPreference's close radius.
                SidearmWhenClose,
                // Never the shield.
                NoShieldThrowOnly
            }

            // Troops whose signature weapon is two-handed. The component is only attached when the soldier's main
            // (first melee) weapon actually has a two-handed grip and he carries no bow/crossbow, so per-roster
            // variants without the two-hander and hybrid horse archers are left alone. Never list a troop whose
            // main weapon can be a spear: spears share the TwoHandedPolearm template (2H-only grip + 1H grip) and would
            // be forced two-handed without their shield.
            public static Profile GetProfile(Agent agent)
            {
                if (agent.Controller != AgentControllerType.AI || agent.Character == null)
                {
                    return Profile.None;
                }
                switch (agent.Character.StringId)
                {
                    // Eastern heavy lancers: two-handed lance, mace and shield up close.
                    case "imperial_cataphract":
                    case "imperial_elite_cataphract":
                    case "imperial_heavy_horseman":
                    case "khuzait_horseman":
                    case "khuzait_lancer":
                    case "khuzait_heavy_lancer":
                    case "druzhinnik":
                    case "druzhinnik_champion":
                    case "aserai_vanguard_faris":
                    case "eleftheroi_tier_1":
                    case "eleftheroi_tier_2":
                    case "eleftheroi_tier_3":
                        return Profile.SidearmWhenClose;
                    // Two-handed infantry: falx, rhomphaia, warrazor, two-handed axes, atgeirs.
                    case "battanian_falxman":
                    case "battanian_veteran_falxman":
                    case "battanian_raider":
                    case "sturgian_shock_troop":
                    case "sturgian_berzerker":
                    case "sturgian_ulfhednar":
                    case "sturgian_woodsman":
                    case "nord_berserkr":
                    case "nord_hew-bearer":
                    case "nord_hirdmann":
                    case "nord_huscarl":
                    case "nord_jarlsmann":
                    case "nord_thegn":
                    case "nord_ungmann":
                    case "nord_ulfhednar":
                    case "nord_vargr":
                    case "sturgia_marine_t5":
                        return Profile.NoShieldThrowOnly;
                    default:
                        return Profile.None;
                }
            }

            public static bool IsLauncher(ItemObject item)
            {
                WeaponComponentData primary = item?.PrimaryWeapon;
                return primary != null && primary.IsRangedWeapon && !primary.IsConsumable;
            }

            /// <summary>
            /// Whether this soldier gets the signature-weapon handling, read from his spawn equipment so the build-time
            /// weapon-data patch and the spawn-time component agree: a listed troop whose main (first melee) weapon has
            /// a two-handed grip and who carries no bow/crossbow. Returns the main weapon slot and the shield slot
            /// (None if no shield).
            /// </summary>
            public static bool TryGetSignatureSlots(Agent agent, out EquipmentIndex mainSlot, out EquipmentIndex shieldSlot)
            {
                mainSlot = EquipmentIndex.None;
                shieldSlot = EquipmentIndex.None;
                Equipment kit = agent.SpawnEquipment;
                if (kit == null || GetProfile(agent) == Profile.None)
                {
                    return false;
                }
                // Never a banner bearer. He holds the banner in the off hand, and a two-handed-only grip on his
                // replacement weapon (RBM's bastard swords count as one-handed swords) or sheathing his off hand drops
                // it (DropOnWeaponChange); BannerBearerLogic.UpdateAgent then reads the emptied ExtraWeaponSlot and the
                // engine crashes with an access violation at deployment.
                if (BannerBearerLogic.IsBannerItem(kit[EquipmentIndex.ExtraWeaponSlot].Item))
                {
                    return false;
                }
                for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.ExtraWeaponSlot; i++)
                {
                    ItemObject item = kit[i].Item;
                    if (item == null)
                    {
                        continue;
                    }
                    if (IsLauncher(item))
                    {
                        return false;
                    }
                    if (item.ItemType == ItemObject.ItemTypeEnum.Shield)
                    {
                        if (shieldSlot == EquipmentIndex.None)
                        {
                            shieldSlot = i;
                        }
                        continue;
                    }
                    WeaponComponentData primary = item.PrimaryWeapon;
                    if (mainSlot == EquipmentIndex.None && primary != null && primary.IsMeleeWeapon && !primary.IsConsumable)
                    {
                        mainSlot = i;
                    }
                }
                return mainSlot != EquipmentIndex.None && FindTwoHandedGrip(kit[mainSlot].Item) >= 0;
            }

            private static readonly Agent.EventControlFlag[] WieldSlotFlags =
            {
                Agent.EventControlFlag.Wield0, Agent.EventControlFlag.Wield1, Agent.EventControlFlag.Wield2, Agent.EventControlFlag.Wield3
            };

            public static Agent.EventControlFlag WieldFlag(int slot)
            {
                return WieldSlotFlags[slot];
            }

            public static int SlotCount => WieldSlotFlags.Length;

            /// <summary>The first two-handed-only melee grip of a weapon; -1 if it has none.</summary>
            public static int FindTwoHandedGrip(ItemObject item)
            {
                if (item?.WeaponComponent == null)
                {
                    return -1;
                }
                MBReadOnlyList<WeaponComponentData> usages = item.Weapons;
                for (int i = 0; i < usages.Count; i++)
                {
                    WeaponComponentData usage = usages[i];
                    if (usage.IsMeleeWeapon && !usage.IsConsumable && usage.WeaponFlags.HasAnyFlag(WeaponFlags.NotUsableWithOneHand))
                    {
                        return i;
                    }
                }
                return -1;
            }

            /// <summary>A one-handed grip the engine gets a copy of the two-handed grip for (see the stats patch).</summary>
            public static bool IsReplacedGrip(ItemObject item, int usageIndex, int twoHanded)
            {
                MBReadOnlyList<WeaponComponentData> usages = item.Weapons;
                if (usageIndex < 0 || usageIndex >= usages.Count || usageIndex == twoHanded)
                {
                    return false;
                }
                WeaponComponentData usage = usages[usageIndex];
                return usage.IsMeleeWeapon && !usage.IsConsumable && !usage.WeaponFlags.HasAnyFlag(WeaponFlags.NotUsableWithOneHand)
                    && usage.WeaponClass != usages[twoHanded].WeaponClass;
            }

            public static bool IsThrowingWeapon(ItemObject item)
            {
                WeaponComponentData primary = item?.PrimaryWeapon;
                return primary != null && primary.IsConsumable && primary.IsRangedWeapon;
            }
        }

        /// <summary>
        /// Filters one soldier's native AI input. OnAIInputSet runs on the engine's AI thread, so it only masks flags and
        /// reads fields the main thread (OnTick) keeps up to date; no engine queries there.
        /// </summary>
        public class SignatureWeaponGripComponent : AgentComponent
        {
            private readonly SignatureWeaponGrip.Profile _profile;
            private readonly int _mainSlot;
            private readonly int _shieldSlot;
            private readonly ItemObject _mainWeapon;

            // Written on the main thread, read on the AI thread. Plain bool/int writes are atomic.
            private volatile bool _hasMainWeapon = true;
            private volatile bool _enemyClose;
            private volatile int _mainHandSlot = (int)EquipmentIndex.None;
            private volatile bool _throwingWeaponInHandOutOfWindow;
            private volatile bool _inThrowWindow;
            private volatile int _throwingSlotMask;
            // Swimming (NavalDLC sheathes both hands and clears CanAttack when a man goes overboard), climbing a
            // ladder/climbing machine (ClimbingMachineDetachment also clears CanAttack) or otherwise driven by a game
            // object: native input passes through untouched,
            // so the filter never re-wields the two-hander in a swim or climb state.
            private volatile bool _suspended;

            // Throwing weapons are only allowed out while the current target is at throwing distance; otherwise the
            // engine periodically draws the axe with nothing to throw at.
            private const float ThrowWindowMin = 6f;
            private const float ThrowWindowMax = 25f;

            public SignatureWeaponGripComponent(Agent agent, SignatureWeaponGrip.Profile profile, EquipmentIndex mainSlot, EquipmentIndex shieldSlot) : base(agent)
            {
                _profile = profile;
                _mainSlot = (int)mainSlot;
                _shieldSlot = (int)shieldSlot;
                _mainWeapon = agent.Equipment[mainSlot].Item;
            }

            public override void OnAIInputSet(ref Agent.EventControlFlag eventFlag, ref Agent.MovementControlFlag movementFlag, ref Vec2 inputVector)
            {
                // Signature weapon broken or lost: fight with whatever is left, as normal.
                if (!_hasMainWeapon || _suspended)
                {
                    return;
                }
                if (_shieldSlot >= 0)
                {
                    if (_profile == SignatureWeaponGrip.Profile.NoShieldThrowOnly || !_enemyClose)
                    {
                        eventFlag &= ~SignatureWeaponGrip.WieldFlag(_shieldSlot);
                    }
                }
                if (_profile == SignatureWeaponGrip.Profile.SidearmWhenClose && _enemyClose)
                {
                    return;
                }
                // Keep the signature weapon: no sheathing the main hand, and a request for another weapon becomes a
                // request for the signature weapon when it is not in hand (else the hand is left empty). A throwing
                // weapon may still be drawn while the target is at throwing distance - that is how a throw starts.
                eventFlag &= ~Agent.EventControlFlag.Sheath0;
                bool wantedOtherWeapon = false;
                for (int i = 0; i < SignatureWeaponGrip.SlotCount; i++)
                {
                    Agent.EventControlFlag wield = SignatureWeaponGrip.WieldFlag(i);
                    if (i == _mainSlot || (eventFlag & wield) == 0)
                    {
                        continue;
                    }
                    if (_inThrowWindow && (_throwingSlotMask & (1 << i)) != 0)
                    {
                        continue;
                    }
                    eventFlag &= ~wield;
                    wantedOtherWeapon = true;
                }
                // A throwing weapon in hand with nothing to throw at (or switched to its melee mode) goes back for the
                // signature weapon.
                if ((wantedOtherWeapon && _mainHandSlot != _mainSlot) || _throwingWeaponInHandOutOfWindow)
                {
                    eventFlag |= SignatureWeaponGrip.WieldFlag(_mainSlot);
                }
            }

            public override void OnTick(float dt)
            {
                _suspended = !Agent.IsOnLand() || Agent.IsUsingGameObject || (Agent.GetAgentFlags() & AgentFlag.CanAttack) == 0;

                MissionWeapon main = Agent.Equipment[(EquipmentIndex)_mainSlot];
                _hasMainWeapon = !main.IsEmpty && main.Item == _mainWeapon;
                // Already sticky (WeaponPreference.CloseCombatStickiness): after a charge riders hover around the close
                // radius, and a raw flag flipped every half second between "sidearm allowed" and "signature weapon
                // only", swapping weapons back and forth.
                _enemyClose = WeaponPreference.enemyClose.TryGetValue(Agent, out bool close) && close;

                EquipmentIndex inHand = Agent.GetPrimaryWieldedItemIndex();
                _mainHandSlot = (int)inHand;

                int throwingMask = 0;
                for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.ExtraWeaponSlot; i++)
                {
                    MissionWeapon weapon = Agent.Equipment[i];
                    if (!weapon.IsEmpty && SignatureWeaponGrip.IsThrowingWeapon(weapon.Item))
                    {
                        throwingMask |= 1 << (int)i;
                    }
                }
                _throwingSlotMask = throwingMask;

                bool inThrowWindow = false;
                Agent target = Agent.GetTargetAgent();
                if (target != null && target.IsActive())
                {
                    float distance = Agent.Position.Distance(target.Position);
                    inThrowWindow = distance >= ThrowWindowMin && distance <= ThrowWindowMax;
                }
                _inThrowWindow = inThrowWindow;

                bool throwingOutOfWindow = false;
                if (inHand != EquipmentIndex.None && (throwingMask & (1 << (int)inHand)) != 0)
                {
                    WeaponComponentData usage = Agent.Equipment[inHand].CurrentUsageItem;
                    bool meleeMode = usage != null && !usage.IsRangedWeapon;
                    throwingOutOfWindow = meleeMode || !inThrowWindow;
                }
                _throwingWeaponInHandOutOfWindow = throwingOutOfWindow;
            }
        }

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("WieldInitialWeapons")]
        internal class SignatureWeaponGripSpawnPatch
        {
            private static void Postfix(Agent __instance)
            {
                SignatureWeaponGrip.Profile profile = SignatureWeaponGrip.GetProfile(__instance);
                if (profile == SignatureWeaponGrip.Profile.None || __instance.GetComponent<SignatureWeaponGripComponent>() != null)
                {
                    return;
                }
                if (!SignatureWeaponGrip.TryGetSignatureSlots(__instance, out EquipmentIndex mainSlot, out EquipmentIndex shieldSlot) ||
                    __instance.Equipment[mainSlot].IsEmpty)
                {
                    return;
                }
                if (__instance.GetOffhandWieldedItemIndex() != EquipmentIndex.None)
                {
                    __instance.TryToSheathWeaponInHand(Agent.HandIndex.OffHand, Agent.WeaponWieldActionType.Instant);
                }
                if (__instance.GetPrimaryWieldedItemIndex() != mainSlot)
                {
                    __instance.TryToWieldWeaponInSlot(mainSlot, Agent.WeaponWieldActionType.Instant, isWieldedOnSpawn: true);
                }
                __instance.AddComponent(new SignatureWeaponGripComponent(__instance, profile, mainSlot, shieldSlot));
                __instance.SetHasOnAiInputSetCallback(true);
            }
        }

        /// <summary>
        /// Per-agent weapon data for the signature weapon: one-handed grips (a different weapon class than the two-handed
        /// grip) become a copy of the two-handed grip, and every melee grip (couch and brace too) is marked two-handed
        /// only, so the engine has no grip that admits the shield. Low priority so it runs after RBMCombat's
        /// WeaponEquipped prefix, which rewrites per-usage speeds.
        /// </summary>
        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("WeaponEquipped")]
        internal class SignatureWeaponGripStatsPatch
        {
            [HarmonyPriority(Priority.Low)]
            private static void Prefix(Agent __instance, EquipmentIndex equipmentSlot, WeaponStatsData[] weaponStatsData)
            {
                // Only the signature weapon of a qualifying soldier: a spear variant of a listed troop, or a lancer who
                // also carries a bow, keeps its one-handed grip.
                if (weaponStatsData == null || !SignatureWeaponGrip.TryGetSignatureSlots(__instance, out EquipmentIndex mainSlot, out _) || equipmentSlot != mainSlot)
                {
                    return;
                }
                ItemObject item = __instance.Equipment[equipmentSlot].Item;
                int twoHanded = SignatureWeaponGrip.FindTwoHandedGrip(item);
                if (twoHanded < 0 || twoHanded >= weaponStatsData.Length)
                {
                    return;
                }
                for (int i = 0; i < weaponStatsData.Length; i++)
                {
                    if (SignatureWeaponGrip.IsReplacedGrip(item, i, twoHanded))
                    {
                        weaponStatsData[i] = weaponStatsData[twoHanded];
                    }
                    else if ((weaponStatsData[i].WeaponFlags & (ulong)WeaponFlags.MeleeWeapon) != 0)
                    {
                        weaponStatsData[i].WeaponFlags |= (ulong)WeaponFlags.NotUsableWithOneHand;
                    }
                }
            }
        }

        // No usage-index redirect: moving the engine off a replaced one-handed grip onto the real two-handed index (so
        // RBM's damage code would read the two-handed class) ping-ponged after a couch - the engine drops back to the
        // first usable grip and the redirect moved it again - and cataphracts visibly switched grips back and forth.
        // Known cost: a hit made while the engine uses a replaced grip is scored as that grip's one-handed class.
    }
}
