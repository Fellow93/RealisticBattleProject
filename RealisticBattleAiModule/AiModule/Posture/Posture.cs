using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    public class Posture
    {
        public float posture;
        public float maxPosture = 100f;
        public float regenPerTick = 0.01f;
        public int maxPostureLossCount = 0;
        public float lastPostureLossTime = 0f;

        public Posture()
        {
            this.posture = this.maxPosture;
        }

        public Posture(float maxPosture, float regenPerTick)
        {
            this.maxPosture = maxPosture;
            this.regenPerTick = regenPerTick;
            this.posture = this.maxPosture;
        }
    }

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
        public const float TWOHANDEDSWORD_SWING_COST = +1f;
        public const float TWOHANDEDSWORD_THRUST_COST = -2f;
        public const float TWOHANDEDSWORD_OVERHEAD_COST = +4f;
        public const float TWOHANDEDSWORD_SWING_DRAIN = +22f;
        public const float TWOHANDEDSWORD_THRUST_DRAIN = +15f;
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
        public const float TWOHANDEDAXE_SWING_COST = +5f;
        public const float TWOHANDEDAXE_THRUST_COST = +2f;
        public const float TWOHANDEDAXE_OVERHEAD_COST = +8f;
        public const float TWOHANDEDAXE_SWING_DRAIN = +18f;
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
        public const float TWOHANDEDMACE_SWING_COST = +8f;
        public const float TWOHANDEDMACE_THRUST_COST = +5f;
        public const float TWOHANDEDMACE_OVERHEAD_COST = +11f;
        public const float TWOHANDEDMACE_SWING_DRAIN = +29f;
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
        public const float TWOHANDEDPOLEARM_SWING_COST = +7f;
        public const float TWOHANDEDPOLEARM_THRUST_COST = +1f;
        public const float TWOHANDEDPOLEARM_OVERHEAD_COST = +9f;
        public const float TWOHANDEDPOLEARM_SWING_DRAIN = +36f;
        public const float TWOHANDEDPOLEARM_THRUST_DRAIN = +22f;
        public const float TWOHANDEDPOLEARM_OVERHEAD_DRAIN = +29f;
        public const float TWOHANDEDPOLEARM_BLOCK_COST = -20f;
        public const float TWOHANDEDPOLEARM_PARRY_COST = -30f;
        public const float TWOHANDEDPOLEARM_HIT_COST = +8f;
        public const float TWOHANDEDPOLEARM_BLOCK_REFLECT = -15f;
        public const float TWOHANDEDPOLEARM_PARRY_REFLECT = +0f;

        // SMALL SHIELD
        public const float SMALLSHIELD_INCORRECT_BLOCK_COST = -20f;
        public const float SMALLSHIELD_BLOCK_COST = -25f;
        public const float SMALLSHIELD_PARRY_COST = -35f;
        public const float SMALLSHIELD_HIT_COST = -5f;
        public const float SMALLSHIELD_INCORRECT_BLOCK_REFLECT = -20f;
        public const float SMALLSHIELD_BLOCK_REFLECT = -20f;
        public const float SMALLSHIELD_PARRY_REFLECT = -5f;

        // LARGE SHIELD
        public const float LARGESHIELD_INCORRECT_BLOCK_COST = -25f;
        public const float LARGESHIELD_BLOCK_COST = -30f;
        public const float LARGESHIELD_PARRY_COST = -40f;
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
                case WeaponClass.Dagger:
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

        public static float getDefenseCost(WeaponClass wc, MeleeHitType hitType)
        {
            float retVal = BASE_DEFENSE_COST;
            try
            {
                switch (hitType)
                {
                    case MeleeHitType.WeaponBlock:
                    case MeleeHitType.ShieldBlock:
                        {
                            retVal += (float)typeof(PostureDamage).GetField(wc.ToString().ToUpper() + "_BLOCK_COST").GetValue(null);
                            break;
                        }
                    case MeleeHitType.WeaponParry:
                    case MeleeHitType.ShieldParry:
                        {
                            retVal += (float)typeof(PostureDamage).GetField(wc.ToString().ToUpper() + "_PARRY_COST").GetValue(null);
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
                                retVal += (float)typeof(PostureDamage).GetField(wc.ToString().ToUpper() + "_INCORRECT_BLOCK_COST").GetValue(null);
                            }
                            else
                            {
                                retVal += SHIELD_ON_BACK_HIT_COST;
                            }
                            break;

                        }
                    case MeleeHitType.ChamberBlock:
                        {
                            retVal += (float)typeof(PostureDamage).GetField(wc.ToString().ToUpper() + "_PARRY_COST").GetValue(null) * 0.5f;
                            break;
                        }
                    default:
                        {
                            retVal += 0f;
                            break;
                        }
                }
            }
            catch
            {
                return retVal;
            }
            return retVal;
        }

        public static float getAttackDrain(WeaponClass wc, Agent.UsageDirection attackDirection, StrikeType strikeType)
        {
            float retVal = BASE_DRAIN;
            try
            {
                if (strikeType == StrikeType.Swing)
                {
                    if (attackDirection == Agent.UsageDirection.AttackUp)
                    {
                        retVal += (float)typeof(PostureDamage).GetField(wc.ToString().ToUpper() + "_OVERHEAD_DRAIN").GetValue(null);
                    }
                    else
                    {
                        retVal += (float)typeof(PostureDamage).GetField(wc.ToString().ToUpper() + "_SWING_DRAIN").GetValue(null);
                    }
                }
                else
                {
                    retVal += (float)typeof(PostureDamage).GetField(wc.ToString().ToUpper() + "_THRUST_DRAIN").GetValue(null);
                }
            }
            catch
            {
                return retVal;
            }
            return retVal;
        }

        public static float getAttackCost(WeaponClass wc, Agent.UsageDirection attackDirection, StrikeType strikeType)
        {
            float retVal = BASE_DEFENSE_COST;
            try
            {
                if (strikeType == StrikeType.Swing)
                {
                    if (attackDirection == Agent.UsageDirection.AttackUp)
                    {
                        retVal += (float)typeof(PostureDamage).GetField(wc.ToString().ToUpper() + "_OVERHEAD_COST").GetValue(null);
                    }
                    else
                    {
                        retVal += (float)typeof(PostureDamage).GetField(wc.ToString().ToUpper() + "_SWING_COST").GetValue(null);
                    }
                }
                else
                {
                    retVal += (float)typeof(PostureDamage).GetField(wc.ToString().ToUpper() + "_THRUST_COST").GetValue(null);
                }
            }
            catch
            {
                return retVal;
            }
            return retVal;
        }

        public static float getDefenseReflect(WeaponClass wc, MeleeHitType hitType)
        {
            float retVal = BASE_REFLECT;
            try
            {
                switch (hitType)
                {
                    case MeleeHitType.WeaponBlock:
                    case MeleeHitType.ShieldBlock:
                        {
                            retVal += (float)typeof(PostureDamage).GetField(wc.ToString().ToUpper() + "_BLOCK_REFLECT").GetValue(null);
                            break;
                        }
                    case MeleeHitType.WeaponParry:
                    case MeleeHitType.ShieldParry:
                        {
                            retVal += (float)typeof(PostureDamage).GetField(wc.ToString().ToUpper() + "_PARRY_REFLECT").GetValue(null);
                            break;
                        }
                    case MeleeHitType.ShieldIncorrectBlock:
                        {
                            if (wc == WeaponClass.SmallShield || wc == WeaponClass.LargeShield)
                            {
                                retVal += (float)typeof(PostureDamage).GetField(wc.ToString().ToUpper() + "_INCORRECT_BLOCK_REFLECT").GetValue(null);
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
                            retVal += (float)typeof(PostureDamage).GetField(wc.ToString().ToUpper() + "_PARRY_REFLECT").GetValue(null) * 2f;
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
            }
            catch
            {
                return retVal;
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
                    wc = agent.WieldedOffhandWeapon.CurrentUsageItem.WeaponClass;
                }
                else
                {
                    if (!agent.WieldedWeapon.IsEmpty)
                    {
                        wc = agent.WieldedWeapon.CurrentUsageItem.WeaponClass;
                    }
                }
            }
            else
            {
                if (!agent.WieldedWeapon.IsEmpty)
                {
                    wc = agent.WieldedWeapon.CurrentUsageItem.WeaponClass;
                }
            }
            return wc;
        }

        public static WeaponClass getAttackerWeaponClass(Agent agent)
        {
            WeaponClass wc = WeaponClass.Undefined;
            if (!agent.WieldedWeapon.IsEmpty)
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

            return defenseCost + attackDrain;
        }

        public static float getAttackerPostureDamage(Agent defender, Agent attacker, Agent.UsageDirection attackDirection, StrikeType strikeType, MeleeHitType hitType)
        {
            WeaponClass defenderWC = getDefenderWeaponClass(defender);
            WeaponClass attackerWC = getAttackerWeaponClass(attacker);

            float attackCost = getAttackCost(attackerWC, attackDirection, strikeType);
            float defenseReflect = getDefenseReflect(defenderWC, hitType);

            return attackCost + defenseReflect;
        }
    }
}