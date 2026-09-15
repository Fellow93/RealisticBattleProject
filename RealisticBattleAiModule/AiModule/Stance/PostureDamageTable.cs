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
        public const float UNARME_DOVERHEAD_COST = 10f;
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

        private static string getTableKey(WeaponClass wc)
        {
            return wc == WeaponClass.Undefined ? "UNARMED" : getWeaponClassString(wc);
        }

        // Returns 0 (a no-op offset over the base value) when the row does not exist.
        private static float lookup(string key)
        {
            float value;
            return _table.TryGetValue(key, out value) ? value : 0f;
        }

        public static float getDefenseCost(WeaponClass wc, MeleeHitType hitType)
        {
            float retVal = BASE_DEFENSE_COST;
            string weaponClassString = getTableKey(wc);
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
                        if (wc == WeaponClass.SmallShield || wc == WeaponClass.LargeShield)
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

        public static float getAttackDrain(WeaponClass wc, Agent.UsageDirection attackDirection, StrikeType strikeType)
        {
            float retVal = BASE_DRAIN;
            string weaponClassString = getTableKey(wc);
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

        public static float getAttackCost(WeaponClass wc, Agent.UsageDirection attackDirection, StrikeType strikeType)
        {
            float retVal = BASE_DEFENSE_COST;
            string weaponClassString = getTableKey(wc);
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

        public static float getDefenseReflect(WeaponClass wc, MeleeHitType hitType)
        {
            float retVal = BASE_REFLECT;
            string weaponClassString = getTableKey(wc);
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
                        if (wc == WeaponClass.SmallShield || wc == WeaponClass.LargeShield)
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

        public static WeaponClass getDefenderWeaponClass(Agent agent)
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

        public static WeaponClass getAttackerWeaponClass(Agent agent)
        {
            WeaponClass wc = WeaponClass.Undefined;
            if (!agent.WieldedWeapon.IsEmpty && agent.WieldedWeapon.CurrentUsageItem != null)
            {
                wc = agent.WieldedWeapon.CurrentUsageItem.WeaponClass;
            }
            return wc;
        }

        public static float getDefenderPostureDamage(Agent defender, Agent attacker, Agent.UsageDirection attackDirection, StrikeType strikeType, MeleeHitType hitType)
        {
            WeaponClass defenderWC = getDefenderWeaponClass(defender);
            WeaponClass attackerWC = getAttackerWeaponClass(attacker);

            float defenseCost = getDefenseCost(defenderWC, hitType);
            float attackDrain = getAttackDrain(attackerWC, attackDirection, strikeType);

            // Per-class costs are negative offsets from the base; a large-shield parry against a
            // zero-drain attack sums below zero, which would heal the defender past maxPosture.
            return Math.Max(0f, defenseCost + attackDrain);
        }

        public static float getAttackerPostureDamage(Agent defender, Agent attacker, Agent.UsageDirection attackDirection, StrikeType strikeType, MeleeHitType hitType)
        {
            WeaponClass defenderWC = getDefenderWeaponClass(defender);
            WeaponClass attackerWC = getAttackerWeaponClass(attacker);

            float attackCost = getAttackCost(attackerWC, attackDirection, strikeType);
            float defenseReflect = getDefenseReflect(defenderWC, hitType);

            return Math.Max(0f, attackCost + defenseReflect);
        }
    }
}
