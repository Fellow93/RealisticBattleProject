using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Library;

namespace RBMStance
{
    // Per-class rows are offsets on the base values (20 each). On a block or parry:
    //   defender loses (BASE_COST + defender's _BLOCK_COST) + (BASE_DRAIN + attacker's _DRAIN),
    //   attacker loses (BASE_COST + attacker's _COST) + (BASE_REFLECT + defender's _BLOCK/_PARRY_REFLECT),
    // each at least 0, before skill, speed, contact point and the parry/chamber factors (MeleeBlowPatch.Math).
    // A parry or chamber costs the defender the matching block row (times its factor), so there are no _PARRY_COST
    // rows. The attacker's _COST grows with the weapon's heft; a landed hit costs the defender its damage instead.
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

        // BASE VALUES
        public const float BASE_DRAIN = 20f;
        public const float BASE_COST = 20f;
        public const float BASE_REFLECT = 20f;

        // UNARMED
        public const float UNARMED_SWING_COST = 10f;

        public const float UNARMED_THRUST_COST = 10f;
        public const float UNARMED_OVERHEAD_COST = 10f;
        public const float UNARMED_SWING_DRAIN = 0f;
        public const float UNARMED_THRUST_DRAIN = 0f;
        public const float UNARMED_OVERHEAD_DRAIN = 0f;
        public const float UNARMED_BLOCK_COST = 20f;
        public const float UNARMED_BLOCK_REFLECT = 0f;
        public const float UNARMED_PARRY_REFLECT = 0f;

        // KICK / SHIELD BASH / WEAPON BASH caught on a guard: the blocker's drain, any direction. A shove is meant to
        // open a guard, so it drains about what a mace overhead does. The kicker's own cost is AiKickBash's ChargeCost.
        public const float KICKBASH_DRAIN = +20f;

        // ONE-HANDED SWORD
        public const float ONEHANDEDSWORD_SWING_COST = -5f;

        public const float ONEHANDEDSWORD_THRUST_COST = -7f;
        public const float ONEHANDEDSWORD_OVERHEAD_COST = -3f;
        public const float ONEHANDEDSWORD_SWING_DRAIN = 10f;
        public const float ONEHANDEDSWORD_THRUST_DRAIN = 5f;
        public const float ONEHANDEDSWORD_OVERHEAD_DRAIN = 15f;
        public const float ONEHANDEDSWORD_BLOCK_COST = -20f;
        public const float ONEHANDEDSWORD_BLOCK_REFLECT = -15f;
        public const float ONEHANDEDSWORD_PARRY_REFLECT = 0f;

        // DAGGER
        // The class is mostly short swords (seax, gladius, skaen, sica), so it sits just under the sword, not far below.
        public const float DAGGER_SWING_COST = -7f;

        public const float DAGGER_THRUST_COST = -9f;
        public const float DAGGER_OVERHEAD_COST = -5f;
        public const float DAGGER_SWING_DRAIN = +6f;
        public const float DAGGER_THRUST_DRAIN = +3f;
        public const float DAGGER_OVERHEAD_DRAIN = +11f;
        public const float DAGGER_BLOCK_COST = -15f;
        public const float DAGGER_BLOCK_REFLECT = -20f;
        public const float DAGGER_PARRY_REFLECT = -5f;

        // TWO-HANDED SWORD
        public const float TWOHANDEDSWORD_SWING_COST = +0f;

        public const float TWOHANDEDSWORD_THRUST_COST = -3f;
        public const float TWOHANDEDSWORD_OVERHEAD_COST = +2f;
        public const float TWOHANDEDSWORD_SWING_DRAIN = +27f;
        public const float TWOHANDEDSWORD_THRUST_DRAIN = +25f;
        public const float TWOHANDEDSWORD_OVERHEAD_DRAIN = +29f;
        public const float TWOHANDEDSWORD_BLOCK_COST = -25f;
        public const float TWOHANDEDSWORD_BLOCK_REFLECT = -5f;
        public const float TWOHANDEDSWORD_PARRY_REFLECT = +10f;

        // ONE-HANDED AXE
        public const float ONEHANDEDAXE_SWING_COST = -2f;

        public const float ONEHANDEDAXE_THRUST_COST = -4f;
        public const float ONEHANDEDAXE_OVERHEAD_COST = +0f;
        public const float ONEHANDEDAXE_SWING_DRAIN = +13f;
        public const float ONEHANDEDAXE_THRUST_DRAIN = +7f;
        public const float ONEHANDEDAXE_OVERHEAD_DRAIN = +17f;
        public const float ONEHANDEDAXE_BLOCK_COST = -15f;
        public const float ONEHANDEDAXE_BLOCK_REFLECT = -20f;
        public const float ONEHANDEDAXE_PARRY_REFLECT = +5f;

        // TWO-HANDED AXE
        public const float TWOHANDEDAXE_SWING_COST = +4f;

        public const float TWOHANDEDAXE_THRUST_COST = +1f;
        public const float TWOHANDEDAXE_OVERHEAD_COST = +6f;
        public const float TWOHANDEDAXE_SWING_DRAIN = +32f;
        public const float TWOHANDEDAXE_THRUST_DRAIN = +18f;
        public const float TWOHANDEDAXE_OVERHEAD_DRAIN = +32f;
        public const float TWOHANDEDAXE_BLOCK_COST = -20f;
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
        public const float MACE_BLOCK_REFLECT = -20f;
        public const float MACE_PARRY_REFLECT = -10f;

        // TWO-HANDED MACE
        public const float TWOHANDEDMACE_SWING_COST = +4f;

        public const float TWOHANDEDMACE_THRUST_COST = +1f;
        public const float TWOHANDEDMACE_OVERHEAD_COST = +6f;
        public const float TWOHANDEDMACE_SWING_DRAIN = +36f;
        public const float TWOHANDEDMACE_THRUST_DRAIN = +22f;
        public const float TWOHANDEDMACE_OVERHEAD_DRAIN = +36f;
        public const float TWOHANDEDMACE_BLOCK_COST = -17f;
        public const float TWOHANDEDMACE_BLOCK_REFLECT = -15f;
        public const float TWOHANDEDMACE_PARRY_REFLECT = -10f;

        // ONE-HANDED POLEARM
        public const float ONEHANDEDPOLEARM_SWING_COST = -2f;

        public const float ONEHANDEDPOLEARM_THRUST_COST = -5f;
        public const float ONEHANDEDPOLEARM_OVERHEAD_COST = +1f;
        public const float ONEHANDEDPOLEARM_SWING_DRAIN = +0f;
        public const float ONEHANDEDPOLEARM_THRUST_DRAIN = +10f;
        public const float ONEHANDEDPOLEARM_OVERHEAD_DRAIN = +15f;
        public const float ONEHANDEDPOLEARM_BLOCK_COST = -10f;
        public const float ONEHANDEDPOLEARM_BLOCK_REFLECT = -20f;
        public const float ONEHANDEDPOLEARM_PARRY_REFLECT = -15f;

        // TWO-HANDED POLEARM
        public const float TWOHANDEDPOLEARM_SWING_COST = +0f;

        public const float TWOHANDEDPOLEARM_THRUST_COST = -3f;
        public const float TWOHANDEDPOLEARM_OVERHEAD_COST = +2f;
        public const float TWOHANDEDPOLEARM_SWING_DRAIN = +29f;
        public const float TWOHANDEDPOLEARM_THRUST_DRAIN = +22f;
        public const float TWOHANDEDPOLEARM_OVERHEAD_DRAIN = +29f;
        public const float TWOHANDEDPOLEARM_BLOCK_COST = -20f;
        public const float TWOHANDEDPOLEARM_BLOCK_REFLECT = -15f;
        public const float TWOHANDEDPOLEARM_PARRY_REFLECT = +0f;

        // SMALL SHIELD
        public const float SMALLSHIELD_INCORRECT_BLOCK_COST = -25f;

        public const float SMALLSHIELD_BLOCK_COST = -30f;
        public const float SMALLSHIELD_INCORRECT_BLOCK_REFLECT = -20f;
        public const float SMALLSHIELD_BLOCK_REFLECT = -20f;
        public const float SMALLSHIELD_PARRY_REFLECT = -5f;

        // LARGE SHIELD
        public const float LARGESHIELD_INCORRECT_BLOCK_COST = -30f;

        public const float LARGESHIELD_BLOCK_COST = -35f;
        public const float LARGESHIELD_INCORRECT_BLOCK_REFLECT = -20f;
        public const float LARGESHIELD_BLOCK_REFLECT = -20f;
        public const float LARGESHIELD_PARRY_REFLECT = -15f;

        public const float SHIELD_ON_BACK_HIT_COST = -10f;
        public const float SHIELD_ON_BACK_HIT_REFLECT = -20f;

        public const float AGENT_HIT_REFLECT = -10f;

        public static string GetWeaponClassString(WeaponClass wc)
        {
            switch (wc)
            {
                // Dagger has its own DAGGER_* rows; only throwing knives borrow the sword rows.
                case WeaponClass.ThrowingKnife:
                    {
                        return WeaponClass.OneHandedSword.ToString().ToUpperInvariant();
                    }
                case WeaponClass.Pick:
                    {
                        return WeaponClass.Mace.ToString().ToUpperInvariant();
                    }
                case WeaponClass.LowGripPolearm:
                case WeaponClass.Javelin:
                    {
                        return WeaponClass.OneHandedPolearm.ToString().ToUpperInvariant();
                    }
                case WeaponClass.ThrowingAxe:
                    {
                        return WeaponClass.OneHandedAxe.ToString().ToUpperInvariant();
                    }
                default:
                    {
                        return wc.ToString().ToUpperInvariant();
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

        private static string GetTableKey(WeaponClass wc)
        {
            return wc == WeaponClass.Undefined ? "UNARMED" : GetWeaponClassString(wc);
        }

        // Returns 0 (a no-op offset over the base value) when the row does not exist.
        private static float Lookup(string key)
        {
            float value;
            return _table.TryGetValue(key, out value) ? value : 0f;
        }

        // The row suffixes the Get* methods read, in Row order.
        private enum Row
        {
            SwingCost,
            ThrustCost,
            OverheadCost,
            SwingDrain,
            ThrustDrain,
            OverheadDrain,
            BlockCost,
            BlockReflect,
            ParryReflect,
            IncorrectBlockCost,
            IncorrectBlockReflect,
            Count
        }

        private static readonly string[] RowSuffixes =
        {
            "_SWING_COST",
            "_THRUST_COST",
            "_OVERHEAD_COST",
            "_SWING_DRAIN",
            "_THRUST_DRAIN",
            "_OVERHEAD_DRAIN",
            "_BLOCK_COST",
            "_BLOCK_REFLECT",
            "_PARRY_REFLECT",
            "_INCORRECT_BLOCK_COST",
            "_INCORRECT_BLOCK_REFLECT"
        };

        // Every (weapon class, row) value resolved once through the same key and Lookup as a by-name read, so a
        // melee blow does no enum-to-string or concatenation. The rows are constants, so it never needs a rebuild.
        // After _table and RowSuffixes: static initializers run in textual order.
        private static readonly float[,] _rows = BuildRows();

        private static float[,] BuildRows()
        {
            int classCount = (int)WeaponClass.NumClasses + 1;
            float[,] rows = new float[classCount, (int)Row.Count];
            for (int i = 0; i < classCount; i++)
            {
                string key = GetTableKey((WeaponClass)i);
                for (int row = 0; row < (int)Row.Count; row++)
                {
                    rows[i, row] = Lookup(key + RowSuffixes[row]);
                }
            }
            return rows;
        }

        private static float Lookup(WeaponClass wc, Row row)
        {
            int i = (int)wc;
            if (i >= 0 && i < _rows.GetLength(0))
            {
                return _rows[i, (int)row];
            }
            // Not a known class: resolve by name, as before.
            return Lookup(GetTableKey(wc) + RowSuffixes[(int)row]);
        }

        public static float GetDefenseCost(WeaponClass wc, MeleeHitType hitType)
        {
            float retVal = BASE_COST;
            switch (hitType)
            {
                // A parry or chamber reads the block row; the caller applies its factor.
                case MeleeHitType.WeaponBlock:
                case MeleeHitType.ShieldBlock:
                case MeleeHitType.WeaponParry:
                case MeleeHitType.ShieldParry:
                case MeleeHitType.ChamberBlock:
                    {
                        retVal += Lookup(wc, Row.BlockCost);
                        break;
                    }
                case MeleeHitType.ShieldIncorrectBlock:
                    {
                        if (wc == WeaponClass.SmallShield || wc == WeaponClass.LargeShield)
                        {
                            retVal += Lookup(wc, Row.IncorrectBlockCost);
                        }
                        else
                        {
                            retVal += SHIELD_ON_BACK_HIT_COST;
                        }
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

        public static float GetAttackDrain(WeaponClass wc, Agent.UsageDirection attackDirection, StrikeType strikeType)
        {
            float retVal = BASE_DRAIN;
            if (strikeType == StrikeType.Swing)
            {
                if (attackDirection == Agent.UsageDirection.AttackUp)
                {
                    retVal += Lookup(wc, Row.OverheadDrain);
                }
                else
                {
                    retVal += Lookup(wc, Row.SwingDrain);
                }
            }
            else
            {
                retVal += Lookup(wc, Row.ThrustDrain);
            }
            return retVal;
        }

        public static float GetAttackCost(WeaponClass wc, Agent.UsageDirection attackDirection, StrikeType strikeType)
        {
            float retVal = BASE_COST;
            if (strikeType == StrikeType.Swing)
            {
                if (attackDirection == Agent.UsageDirection.AttackUp)
                {
                    retVal += Lookup(wc, Row.OverheadCost);
                }
                else
                {
                    retVal += Lookup(wc, Row.SwingCost);
                }
            }
            else
            {
                retVal += Lookup(wc, Row.ThrustCost);
            }
            return retVal;
        }

        public static float GetDefenseReflect(WeaponClass wc, MeleeHitType hitType)
        {
            float retVal = BASE_REFLECT;
            switch (hitType)
            {
                case MeleeHitType.WeaponBlock:
                case MeleeHitType.ShieldBlock:
                    {
                        retVal += Lookup(wc, Row.BlockReflect);
                        break;
                    }
                case MeleeHitType.WeaponParry:
                case MeleeHitType.ShieldParry:
                case MeleeHitType.ChamberBlock:
                    {
                        retVal += Lookup(wc, Row.ParryReflect);
                        break;
                    }
                case MeleeHitType.ShieldIncorrectBlock:
                    {
                        if (wc == WeaponClass.SmallShield || wc == WeaponClass.LargeShield)
                        {
                            retVal += Lookup(wc, Row.IncorrectBlockReflect);
                        }
                        else
                        {
                            retVal += SHIELD_ON_BACK_HIT_REFLECT;
                        }
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

        public static WeaponClass GetDefenderWeaponClass(Agent agent)
        {
            WeaponClass wc = WeaponClass.Undefined;
            if (!agent.WieldedOffhandWeapon.IsEmpty)
            {
                if (agent.WieldedOffhandWeapon.IsShield())
                {
                    if (agent.WieldedOffhandWeapon.CurrentUsageItem != null)
                        wc = agent.WieldedOffhandWeapon.CurrentUsageItem.WeaponClass;
                }
                else
                {
                    if (!agent.WieldedWeapon.IsEmpty && agent.WieldedWeapon.CurrentUsageItem != null)
                    {
                        wc = agent.WieldedWeapon.CurrentUsageItem.WeaponClass;
                    }
                }
            }
            else
            {
                if (!agent.WieldedWeapon.IsEmpty && agent.WieldedWeapon.CurrentUsageItem != null)
                {
                    wc = agent.WieldedWeapon.CurrentUsageItem.WeaponClass;
                }
            }
            return wc;
        }

        public static WeaponClass GetAttackerWeaponClass(Agent agent)
        {
            WeaponClass wc = WeaponClass.Undefined;
            if (!agent.WieldedWeapon.IsEmpty && agent.WieldedWeapon.CurrentUsageItem != null)
            {
                wc = agent.WieldedWeapon.CurrentUsageItem.WeaponClass;
            }
            return wc;
        }

        // A punch uses the UNARMED rows, a kick, shield bash or weapon bash KICKBASH_DRAIN, not the wielded weapon's.
        public static float GetDefenderPostureDamage(Agent defender, Agent attacker, Agent.UsageDirection attackDirection, StrikeType strikeType, MeleeHitType hitType, bool isUnarmedAttack, bool isKickOrBash)
        {
            WeaponClass defenderWC = GetDefenderWeaponClass(defender);
            WeaponClass attackerWC = isUnarmedAttack ? WeaponClass.Undefined : GetAttackerWeaponClass(attacker);

            float defenseCost = GetDefenseCost(defenderWC, hitType);
            float attackDrain = isKickOrBash ? BASE_DRAIN + KICKBASH_DRAIN : GetAttackDrain(attackerWC, attackDirection, strikeType);

            // Per-class costs are negative offsets from the base; a large-shield parry against a
            // zero-drain attack sums below zero, which would heal the defender past maxPosture.
            return Math.Max(0f, defenseCost + attackDrain);
        }

        public static float GetAttackerPostureDamage(Agent defender, Agent attacker, Agent.UsageDirection attackDirection, StrikeType strikeType, MeleeHitType hitType, bool isUnarmedAttack)
        {
            WeaponClass defenderWC = GetDefenderWeaponClass(defender);
            WeaponClass attackerWC = isUnarmedAttack ? WeaponClass.Undefined : GetAttackerWeaponClass(attacker);

            float attackCost = GetAttackCost(attackerWC, attackDirection, strikeType);
            float defenseReflect = GetDefenseReflect(defenderWC, hitType);

            return Math.Max(0f, attackCost + defenseReflect);
        }
    }
}
