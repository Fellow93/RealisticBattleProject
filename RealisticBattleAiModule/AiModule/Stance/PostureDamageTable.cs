using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Library;

namespace RBMAI
{
    // When there is attack, there is always default 20 posture drain for both the attack and the defender. For example: ONE_HANDED_SWORD_ATTACK_SWING vs SMALL_SHIELD_BLOCK; Attacker gets 20 default damage - ONE_HANDED_SWORD_ATTACK_SWING posture damage + SMALL_SHIELD_BLOCK posture damage. Defender gets 20 default damage + ONE_HANDED_SWORD_ATTACK_SWING posture damage - SMALL_SHIELD_BLOCK posture damage.
    public static class PostureDamage
    {
        public enum MeleeHitType
        {
            None,
            AgentHit,
            WeaponBlock,
            WeaponParry,
            ShieldIncorrectBlock,
            ShieldBlock,
            ShieldParry,
            ChamberBlock
        }

        public const float POSTURE_RESET_MODIFIER = 0.75f;
        public const float SHIELD_POSTURE_RESET_MODIFIER = 1f;

        // BASE VALUES
        public const float BASE_ATTACK_COST = 20f;

        public const float BASE_DRAIN = 20f;
        public const float BASE_DEFENSE_COST = 20f;
        public const float BASE_REFLECT = 20f;

        // UNARMED
        public const float UNARMED_SWING_COST = 10f;

        public const float UNARMED_THRUST_COST = 10f;
        public const float UNARMED_OVERHEAD_COST = 10f;
        public const float UNARMED_SWING_DRAIN = 0f;
        public const float UNARMED_THRUST_DRAIN = 0f;
        public const float UNARMED_OVERHEAD_DRAIN = 0f;
        public const float UNARMED_BLOCK_COST = 20f;
        public const float UNARMED_PARRY_COST = 10f;
        public const float UNARMED_BLOCK_REFLECT = 0f;
        public const float UNARMED_PARRY_REFLECT = 0f;

        // ONE-HANDED SWORD
        public const float ONEHANDEDSWORD_SWING_COST = -5f;

        public const float ONEHANDEDSWORD_THRUST_COST = -7f;
        public const float ONEHANDEDSWORD_OVERHEAD_COST = -3f;
        public const float ONEHANDEDSWORD_SWING_DRAIN = 10f;
        public const float ONEHANDEDSWORD_THRUST_DRAIN = 5f;
        public const float ONEHANDEDSWORD_OVERHEAD_DRAIN = 15f;
        public const float ONEHANDEDSWORD_BLOCK_COST = -20f;
        public const float ONEHANDEDSWORD_PARRY_COST = -30f;
        public const float ONEHANDEDSWORD_BLOCK_REFLECT = -15f;
        public const float ONEHANDEDSWORD_PARRY_REFLECT = 0f;

        // DAGGER
        public const float DAGGER_SWING_COST = -5f;

        public const float DAGGER_THRUST_COST = -7f;
        public const float DAGGER_OVERHEAD_COST = -3f;
        public const float DAGGER_SWING_DRAIN = +10f;
        public const float DAGGER_THRUST_DRAIN = +5f;
        public const float DAGGER_OVERHEAD_DRAIN = +15f;
        public const float DAGGER_BLOCK_COST = -15f;
        public const float DAGGER_PARRY_COST = -25f;
        public const float DAGGER_HIT_COST = +0f;
        public const float DAGGER_BLOCK_REFLECT = -20f;
        public const float DAGGER_PARRY_REFLECT = -5f;

        // TWO-HANDED SWORD
        public const float TWOHANDEDSWORD_SWING_COST = -5f;

        public const float TWOHANDEDSWORD_THRUST_COST = -7f;
        public const float TWOHANDEDSWORD_OVERHEAD_COST = -3f;
        public const float TWOHANDEDSWORD_SWING_DRAIN = +27f;
        public const float TWOHANDEDSWORD_THRUST_DRAIN = +25f;
        public const float TWOHANDEDSWORD_OVERHEAD_DRAIN = +29f;
        public const float TWOHANDEDSWORD_BLOCK_COST = -25f;
        public const float TWOHANDEDSWORD_PARRY_COST = -35f;
        public const float TWOHANDEDSWORD_HIT_COST = +8f;
        public const float TWOHANDEDSWORD_BLOCK_REFLECT = -5f;
        public const float TWOHANDEDSWORD_PARRY_REFLECT = +10f;

        // HALF-SWORD (prototype mode of a two-handed sword, RBMConfig.WeaponModes): the two-handed sword's rows. The
        // other prototype mode, the mordhau, has its own MORDHAU rows below the two-handed mace's.
        public const float HALFSWORD_SWING_COST = TWOHANDEDSWORD_SWING_COST;

        public const float HALFSWORD_THRUST_COST = TWOHANDEDSWORD_THRUST_COST;
        public const float HALFSWORD_OVERHEAD_COST = TWOHANDEDSWORD_OVERHEAD_COST;
        public const float HALFSWORD_SWING_DRAIN = TWOHANDEDSWORD_SWING_DRAIN;
        public const float HALFSWORD_THRUST_DRAIN = TWOHANDEDSWORD_THRUST_DRAIN;
        public const float HALFSWORD_OVERHEAD_DRAIN = TWOHANDEDSWORD_OVERHEAD_DRAIN;
        public const float HALFSWORD_BLOCK_COST = TWOHANDEDSWORD_BLOCK_COST;
        public const float HALFSWORD_PARRY_COST = TWOHANDEDSWORD_PARRY_COST;
        public const float HALFSWORD_HIT_COST = TWOHANDEDSWORD_HIT_COST;
        public const float HALFSWORD_BLOCK_REFLECT = TWOHANDEDSWORD_BLOCK_REFLECT;
        public const float HALFSWORD_PARRY_REFLECT = TWOHANDEDSWORD_PARRY_REFLECT;

        // ONE-HANDED AXE
        public const float ONEHANDEDAXE_SWING_COST = -2f;

        public const float ONEHANDEDAXE_THRUST_COST = -4f;
        public const float ONEHANDEDAXE_OVERHEAD_COST = +0f;
        public const float ONEHANDEDAXE_SWING_DRAIN = +13f;
        public const float ONEHANDEDAXE_THRUST_DRAIN = +7f;
        public const float ONEHANDEDAXE_OVERHEAD_DRAIN = +17f;
        public const float ONEHANDEDAXE_BLOCK_COST = -15f;
        public const float ONEHANDEDAXE_PARRY_COST = -25f;
        public const float ONEHANDEDAXE_HIT_COST = +5f;
        public const float ONEHANDEDAXE_BLOCK_REFLECT = -20f;
        public const float ONEHANDEDAXE_PARRY_REFLECT = +5f;

        // TWO-HANDED AXE
        public const float TWOHANDEDAXE_SWING_COST = -2f;

        public const float TWOHANDEDAXE_THRUST_COST = -4f;
        public const float TWOHANDEDAXE_OVERHEAD_COST = +0f;
        public const float TWOHANDEDAXE_SWING_DRAIN = +32f;
        public const float TWOHANDEDAXE_THRUST_DRAIN = +18f;
        public const float TWOHANDEDAXE_OVERHEAD_DRAIN = +32f;
        public const float TWOHANDEDAXE_BLOCK_COST = -20f;
        public const float TWOHANDEDAXE_PARRY_COST = -30f;
        public const float TWOHANDEDAXE_HIT_COST = +15f;
        public const float TWOHANDEDAXE_BLOCK_REFLECT = -15f;
        public const float TWOHANDEDAXE_PARRY_REFLECT = +20f;

        // MACE
        public const float MACE_SWING_COST = +0f;

        public const float MACE_THRUST_COST = -2f;
        public const float MACE_OVERHEAD_COST = +2f;
        public const float MACE_SWING_DRAIN = +15f;
        public const float MACE_THRUST_DRAIN = +10f;
        public const float MACE_OVERHEAD_DRAIN = +20f;
        public const float MACE_BLOCK_COST = -13f;
        public const float MACE_PARRY_COST = -23f;
        public const float MACE_HIT_COST = +10f;
        public const float MACE_BLOCK_REFLECT = -20f;
        public const float MACE_PARRY_REFLECT = -10f;

        // TWO-HANDED MACE
        public const float TWOHANDEDMACE_SWING_COST = +0f;

        public const float TWOHANDEDMACE_THRUST_COST = -2f;
        public const float TWOHANDEDMACE_OVERHEAD_COST = +2f;
        public const float TWOHANDEDMACE_SWING_DRAIN = +36f;
        public const float TWOHANDEDMACE_THRUST_DRAIN = +22f;
        public const float TWOHANDEDMACE_OVERHEAD_DRAIN = +36f;
        public const float TWOHANDEDMACE_BLOCK_COST = -17f;
        public const float TWOHANDEDMACE_PARRY_COST = -28f;
        public const float TWOHANDEDMACE_HIT_COST = +22f;
        public const float TWOHANDEDMACE_BLOCK_REFLECT = -15f;
        public const float TWOHANDEDMACE_PARRY_REFLECT = -10f;

        // MORDHAU (prototype mode of a two-handed sword held by the blade, RBMConfig.WeaponModes): the two-handed mace's
        // rows, made unwieldy. Attacking costs the attacker ~25% more than the 2H mace (base cost 20 + row:
        // swing 20 -> 25, overhead 22 -> 27.5, thrust 18 -> 22.5). Defending with it sits halfway between the 2H mace
        // and the worst weapon values in this table (block cost -10 and reflect -20, parry cost -23 and reflect -15;
        // UNARMED left out). The posture damage it deals (the DRAIN rows) sits about 6% above the two-handed sword's
        // (swing 20 + 30 = 50 against 47, overhead 52 against 49), well below the 2H mace's 56: its hits are weak
        // through armor, so they should not break guards like a mace.
        public const float MORDHAU_SWING_COST = +5f;

        public const float MORDHAU_THRUST_COST = +2.5f;
        public const float MORDHAU_OVERHEAD_COST = +7.5f;
        public const float MORDHAU_SWING_DRAIN = +30f;
        public const float MORDHAU_THRUST_DRAIN = TWOHANDEDMACE_THRUST_DRAIN;
        public const float MORDHAU_OVERHEAD_DRAIN = +32f;
        public const float MORDHAU_BLOCK_COST = -13.5f;
        public const float MORDHAU_PARRY_COST = -25.5f;
        public const float MORDHAU_HIT_COST = TWOHANDEDMACE_HIT_COST;
        public const float MORDHAU_BLOCK_REFLECT = -17.5f;
        public const float MORDHAU_PARRY_REFLECT = -12.5f;

        // ONE-HANDED POLEARM
        public const float ONEHANDEDPOLEARM_SWING_COST = -2f;

        public const float ONEHANDEDPOLEARM_THRUST_COST = -5f;
        public const float ONEHANDEDPOLEARM_OVERHEAD_COST = +1f;
        public const float ONEHANDEDPOLEARM_SWING_DRAIN = +0f;
        public const float ONEHANDEDPOLEARM_THRUST_DRAIN = +10f;
        public const float ONEHANDEDPOLEARM_OVERHEAD_DRAIN = +15f;
        public const float ONEHANDEDPOLEARM_BLOCK_COST = -10f;
        public const float ONEHANDEDPOLEARM_PARRY_COST = -25f;
        public const float ONEHANDEDPOLEARM_HIT_COST = +0f;
        public const float ONEHANDEDPOLEARM_BLOCK_REFLECT = -20f;
        public const float ONEHANDEDPOLEARM_PARRY_REFLECT = -15f;

        // TWO-HANDED POLEARM
        public const float TWOHANDEDPOLEARM_SWING_COST = -3f;

        public const float TWOHANDEDPOLEARM_THRUST_COST = -5f;
        public const float TWOHANDEDPOLEARM_OVERHEAD_COST = -1f;
        public const float TWOHANDEDPOLEARM_SWING_DRAIN = +29f;
        public const float TWOHANDEDPOLEARM_THRUST_DRAIN = +22f;
        public const float TWOHANDEDPOLEARM_OVERHEAD_DRAIN = +29f;
        public const float TWOHANDEDPOLEARM_BLOCK_COST = -20f;
        public const float TWOHANDEDPOLEARM_PARRY_COST = -30f;
        public const float TWOHANDEDPOLEARM_HIT_COST = +8f;
        public const float TWOHANDEDPOLEARM_BLOCK_REFLECT = -15f;
        public const float TWOHANDEDPOLEARM_PARRY_REFLECT = +0f;

        // SMALL SHIELD
        public const float SMALLSHIELD_INCORRECT_BLOCK_COST = -25f;

        public const float SMALLSHIELD_BLOCK_COST = -30f;
        public const float SMALLSHIELD_PARRY_COST = -40f;
        public const float SMALLSHIELD_HIT_COST = -5f;
        public const float SMALLSHIELD_INCORRECT_BLOCK_REFLECT = -20f;
        public const float SMALLSHIELD_BLOCK_REFLECT = -20f;
        public const float SMALLSHIELD_PARRY_REFLECT = -5f;

        // LARGE SHIELD
        public const float LARGESHIELD_INCORRECT_BLOCK_COST = -30f;

        public const float LARGESHIELD_BLOCK_COST = -35f;
        public const float LARGESHIELD_PARRY_COST = -45f;
        public const float LARGESHIELD_HIT_COST = -5f;
        public const float LARGESHIELD_INCORRECT_BLOCK_REFLECT = -20f;
        public const float LARGESHIELD_BLOCK_REFLECT = -20f;
        public const float LARGESHIELD_PARRY_REFLECT = -15f;

        public const float SHIELD_ON_BACK_HIT_COST = -10f;
        public const float SHIELD_ON_BACK_HIT_REFLECT = -20f;

        public const float AGENT_HIT_COST = -20f;
        public const float AGENT_HIT_REFLECT = -10f;

        public static string getWeaponClassString(WeaponClass wc)
        {
            switch (wc)
            {
                // Dagger has its own DAGGER_* rows; only throwing knives borrow the sword rows.
                case WeaponClass.ThrowingKnife:
                    {
                        return WeaponClass.OneHandedSword.ToString().ToUpper();
                    }
                case WeaponClass.Pick:
                    {
                        return WeaponClass.Mace.ToString().ToUpper();
                    }
                case WeaponClass.LowGripPolearm:
                case WeaponClass.Javelin:
                    {
                        return WeaponClass.OneHandedPolearm.ToString().ToUpper();
                    }
                case WeaponClass.ThrowingAxe:
                    {
                        return WeaponClass.OneHandedAxe.ToString().ToUpper();
                    }
                default:
                    {
                        return wc.ToString().ToUpper();
                    }
            }
        }

        // The per-weapon-class rows above are looked up by name. Reflecting on every call was both
        // slow and wrong (it swallowed a missing row in a bare catch); build the table once instead.
        private static readonly Dictionary<string, float> _table = BuildTable();

        private static Dictionary<string, float> BuildTable()
        {
            Dictionary<string, float> table = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (FieldInfo field in typeof(PostureDamage).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType == typeof(float))
                {
                    table[field.Name] = (float)field.GetValue(null);
                }
            }
            return table;
        }

        // Rows by the usage's damage type (RBMConfig.WeaponModes): its weapon class, except the prototype sword modes,
        // whose class stays TwoHandedSword: the half-sword reads HALFSWORD_*, the mordhau MORDHAU_* (its damage type is
        // the two-handed mace, but it is clumsier to swing and to defend with).
        private static string getTableKey(WeaponComponentData usage)
        {
            if (usage == null || usage.WeaponClass == WeaponClass.Undefined)
            {
                return "UNARMED";
            }
            if (RBMConfig.WeaponModes.IsMordhau(usage))
            {
                return "MORDHAU";
            }
            if (RBMConfig.WeaponModes.HasOwnDamageType(usage))
            {
                return RBMConfig.WeaponModes.GetDamageWeaponType(usage).ToUpperInvariant();
            }
            return getWeaponClassString(usage.WeaponClass);
        }

        private static bool isShield(WeaponComponentData usage)
        {
            return usage != null && (usage.WeaponClass == WeaponClass.SmallShield || usage.WeaponClass == WeaponClass.LargeShield);
        }

        // Returns 0 (a no-op offset over the base value) when the row does not exist.
        private static float lookup(string key)
        {
            float value;
            return _table.TryGetValue(key, out value) ? value : 0f;
        }

        public static float getDefenseCost(WeaponComponentData usage, MeleeHitType hitType)
        {
            float retVal = BASE_DEFENSE_COST;
            string weaponClassString = getTableKey(usage);
            switch (hitType)
            {
                case MeleeHitType.WeaponBlock:
                case MeleeHitType.ShieldBlock:
                    {
                        retVal += lookup(weaponClassString + "_BLOCK_COST");
                        break;
                    }
                case MeleeHitType.WeaponParry:
                case MeleeHitType.ShieldParry:
                    {
                        retVal += lookup(weaponClassString + "_PARRY_COST");
                        break;
                    }
                case MeleeHitType.AgentHit:
                    {
                        retVal += AGENT_HIT_COST;
                        break;
                    }
                case MeleeHitType.ShieldIncorrectBlock:
                    {
                        if (isShield(usage))
                        {
                            retVal += lookup(weaponClassString + "_INCORRECT_BLOCK_COST");
                        }
                        else
                        {
                            retVal += SHIELD_ON_BACK_HIT_COST;
                        }
                        break;
                    }
                case MeleeHitType.ChamberBlock:
                    {
                        retVal += lookup(weaponClassString + "_PARRY_COST") * 0.5f;
                        break;
                    }
                default:
                    {
                        retVal += 0f;
                        break;
                    }
            }
            return retVal;
        }

        public static float getAttackDrain(WeaponComponentData usage, Agent.UsageDirection attackDirection, StrikeType strikeType)
        {
            float retVal = BASE_DRAIN;
            string weaponClassString = getTableKey(usage);
            if (strikeType == StrikeType.Swing)
            {
                if (attackDirection == Agent.UsageDirection.AttackUp)
                {
                    retVal += lookup(weaponClassString + "_OVERHEAD_DRAIN");
                }
                else
                {
                    retVal += lookup(weaponClassString + "_SWING_DRAIN");
                }
            }
            else
            {
                retVal += lookup(weaponClassString + "_THRUST_DRAIN");
            }
            return retVal;
        }

        public static float getAttackCost(WeaponComponentData usage, Agent.UsageDirection attackDirection, StrikeType strikeType)
        {
            float retVal = BASE_DEFENSE_COST;
            string weaponClassString = getTableKey(usage);
            if (strikeType == StrikeType.Swing)
            {
                if (attackDirection == Agent.UsageDirection.AttackUp)
                {
                    retVal += lookup(weaponClassString + "_OVERHEAD_COST");
                }
                else
                {
                    retVal += lookup(weaponClassString + "_SWING_COST");
                }
            }
            else
            {
                retVal += lookup(weaponClassString + "_THRUST_COST");
            }
            return retVal;
        }

        public static float getDefenseReflect(WeaponComponentData usage, MeleeHitType hitType)
        {
            float retVal = BASE_REFLECT;
            string weaponClassString = getTableKey(usage);
            switch (hitType)
            {
                case MeleeHitType.WeaponBlock:
                case MeleeHitType.ShieldBlock:
                    {
                        retVal += lookup(weaponClassString + "_BLOCK_REFLECT");
                        break;
                    }
                case MeleeHitType.WeaponParry:
                case MeleeHitType.ShieldParry:
                    {
                        retVal += lookup(weaponClassString + "_PARRY_REFLECT");
                        break;
                    }
                case MeleeHitType.ShieldIncorrectBlock:
                    {
                        if (isShield(usage))
                        {
                            retVal += lookup(weaponClassString + "_INCORRECT_BLOCK_REFLECT");
                        }
                        else
                        {
                            retVal += SHIELD_ON_BACK_HIT_REFLECT;
                        }
                        break;
                    }
                case MeleeHitType.ChamberBlock:
                    {
                        //TODO: decide chamber block posture damage
                        retVal += lookup(weaponClassString + "_PARRY_REFLECT") * 2f;
                        break;
                    }
                case MeleeHitType.AgentHit:
                    {
                        retVal += AGENT_HIT_REFLECT;
                        break;
                    }
                default:
                    {
                        retVal += 0f;
                        break;
                    }
            }
            return retVal;
        }

        // The usage whose rows the defender's block reads (null = UNARMED): the shield when one is wielded, else the
        // weapon in hand.
        public static WeaponComponentData getDefenderWeapon(Agent agent)
        {
            WeaponComponentData usage = null;
            if (!agent.WieldedOffhandWeapon.IsEmpty)
            {
                if (agent.WieldedOffhandWeapon.IsShield())
                {
                    usage = agent.WieldedOffhandWeapon.CurrentUsageItem;
                }
                else
                {
                    if (!agent.WieldedWeapon.IsEmpty)
                    {
                        usage = agent.WieldedWeapon.CurrentUsageItem;
                    }
                }
            }
            else
            {
                if (!agent.WieldedWeapon.IsEmpty)
                {
                    usage = agent.WieldedWeapon.CurrentUsageItem;
                }
            }
            return usage;
        }

        public static WeaponComponentData getAttackerWeapon(Agent agent)
        {
            return agent.WieldedWeapon.IsEmpty ? null : agent.WieldedWeapon.CurrentUsageItem;
        }

        // A kick, shield bash or pommel strike uses the UNARMED rows, not the wielded weapon's.
        public static float getDefenderPostureDamage(Agent defender, Agent attacker, Agent.UsageDirection attackDirection, StrikeType strikeType, MeleeHitType hitType, bool isUnarmedAttack)
        {
            WeaponComponentData defenderWeapon = getDefenderWeapon(defender);
            WeaponComponentData attackerWeapon = isUnarmedAttack ? null : getAttackerWeapon(attacker);

            float defenseCost = getDefenseCost(defenderWeapon, hitType);
            float attackDrain = getAttackDrain(attackerWeapon, attackDirection, strikeType);

            // Per-class costs are negative offsets from the base; a large-shield parry against a
            // zero-drain attack sums below zero, which would heal the defender past maxPosture.
            return Math.Max(0f, defenseCost + attackDrain);
        }

        public static float getAttackerPostureDamage(Agent defender, Agent attacker, Agent.UsageDirection attackDirection, StrikeType strikeType, MeleeHitType hitType, bool isUnarmedAttack)
        {
            WeaponComponentData defenderWeapon = getDefenderWeapon(defender);
            WeaponComponentData attackerWeapon = isUnarmedAttack ? null : getAttackerWeapon(attacker);

            float attackCost = getAttackCost(attackerWeapon, attackDirection, strikeType);
            float defenseReflect = getDefenseReflect(defenderWeapon, hitType);

            return Math.Max(0f, attackCost + defenseReflect);
        }
    }
}
