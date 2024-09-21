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

    public static class PostureDamage
    {
        public const float DEFAULT = 20f;

        public const float ONE_HANDED_SWORD_ATTACK_SWING = 15f;
        public const float ONE_HANDED_SWORD_ATTACK_THRUST = 15f;
        public const float ONE_HANDED_SWORD_ATTACK_OVERHEAD = 15f;
        public const float ONE_HANDED_SWORD_BLOCK = 15f;
        public const float ONE_HANDED_SWORD_PARRY = 15f;
        public const float ONE_HANDED_SWORD_HIT = 15f;

        public const float TWO_HANDED_SWORD_ATTACK_SWING = 15f;
        public const float TWO_HANDED_SWORD_ATTACK_THRUST = 15f;
        public const float TWO_HANDED_SWORD_ATTACK_OVERHEAD = 15f;
        public const float TWO_HANDED_SWORD_BLOCK = 15f;
        public const float TWO_HANDED_SWORD_PARRY = 15f;
        public const float TWO_HANDED_SWORD_HIT = 15f;

        public const float ONE_HANDED_AXE_ATTACK_SWING = 15f;
        public const float ONE_HANDED_AXE_ATTACK_THRUST = 15f;
        public const float ONE_HANDED_AXE_ATTACK_OVERHEAD = 15f;
        public const float ONE_HANDED_AXE_BLOCK = 15f;
        public const float ONE_HANDED_AXE_PARRY = 15f;
        public const float ONE_HANDED_AXE_HIT = 15f;

        public const float TWO_HANDED_AXE_ATTACK_SWING = 15f;
        public const float TWO_HANDED_AXE_ATTACK_THRUST = 15f;
        public const float TWO_HANDED_AXE_ATTACK_OVERHEAD = 15f;
        public const float TWO_HANDED_AXE_BLOCK = 15f;
        public const float TWO_HANDED_AXE_PARRY = 15f;
        public const float TWO_HANDED_AXE_HIT = 15f;

        public const float MACE_ATTACK_SWING = 15f;
        public const float MACE_ATTACK_THRUST = 15f;
        public const float MACE_ATTACK_OVERHEAD = 15f;
        public const float MACE_BLOCK = 15f;
        public const float MACE_PARRY = 15f;
        public const float MACE_HIT = 15f;

        public const float TWO_HANDED_MACE_ATTACK_SWING = 15f;
        public const float TWO_HANDED_MACE_ATTACK_THRUST = 15f;
        public const float TWO_HANDED_MACE_ATTACK_OVERHEAD = 15f;
        public const float TWO_HANDED_MACE_BLOCK = 15f;
        public const float TWO_HANDED_MACE_PARRY = 15f;
        public const float TWO_HANDED_MACE_HIT = 15f;

        public const float ONE_HANDED_POLEARM_ATTACK_SWING = 15f;
        public const float ONE_HANDED_POLEARM_ATTACK_THRUST = 15f;
        public const float ONE_HANDED_POLEARM_ATTACK_OVERHEAD = 15f;
        public const float ONE_HANDED_POLEARM_BLOCK = 15f;
        public const float ONE_HANDED_POLEARM_PARRY = 15f;
        public const float ONE_HANDED_POLEARM_HIT = 15f;

        public const float TWO_HANDED_POLEARM_ATTACK_SWING = 15f;
        public const float TWO_HANDED_POLEARM_ATTACK_THRUST = 15f;
        public const float TWO_HANDED_POLEARM_ATTACK_OVERHEAD = 15f;
        public const float TWO_HANDED_POLEARM_BLOCK = 15f;
        public const float TWO_HANDED_POLEARM_PARRY = 15f;
        public const float TWO_HANDED_POLEARM_HIT = 15f;

        public const float SMALL_SHIELD_ATTACK_SWING = 15f;
        public const float SMALL_SHIELD_ATTACK_THRUST = 15f;
        public const float SMALL_SHIELD_ATTACK_OVERHEAD = 15f;
        public const float SMALL_SHIELD_BLOCK = 15f;
        public const float SMALL_SHIELD_PARRY = 15f;
        public const float SMALL_SHIELD_HIT = 15f;

        public const float LARGE_SHIELD_ATTACK_SWING = 15f;
        public const float LARGE_SHIELD_ATTACK_THRUST = 15f;
        public const float LARGE_SHIELD_ATTACK_OVERHEAD = 15f;
        public const float LARGE_SHIELD_BLOCK = 15f;
        public const float LARGE_SHIELD_PARRY = 15f;
        public const float LARGE_SHIELD_HIT = 15f;

        //public const float TWO_HANDED_SWORD = 25f;
        //public const float ONE_HANDED_AXE = 25f;
        //public const float TWO_HANDED_AXE = 30f;
        //public const float MACE = 25f;
        //public const float PICK = 15f;
        //public const float TWO_HANDED_MACE = 35f;
        //public const float ONE_HANDED_POLEARM = 15f;
        //public const float TWO_HANDED_POLEARM_THRUST = 20f;
        //public const float TWO_HANDED_POLEARM_SWING = 30f;
        //public const float LOW_GRIP_POLEARM = 15f;
        //public const float THROWING_AXE = 20f;
        //public const float JAVELIN = 20f;
        //public const float SMALL_SHIELD = 20f;
        //public const float LARGE_SHIELD = 20f;

        public static float getDefenderBasePostureDamage(WeaponClass defenderWC, bool isParry, bool isHit)
        {
            float postureDmg = 0f;
            switch (defenderWC)
            {
                case WeaponClass.Dagger:
                case WeaponClass.ThrowingKnife:
                case WeaponClass.OneHandedSword:
                    {
                        if (isParry)
                        {
                            postureDmg = ONE_HANDED_SWORD_PARRY;
                            break;
                        }
                        else
                        {
                            if(isHit)
                            {
                                postureDmg = ONE_HANDED_SWORD_HIT;
                                break;
                            }
                            else
                            {
                                postureDmg = ONE_HANDED_SWORD_BLOCK;

                            }
                            break;
                        }
                    }
                case WeaponClass.TwoHandedSword:
                    {
                        if (isParry)
                        {
                            postureDmg = ONE_HANDED_AXE_PARRY;
                            break;
                        }
                        else
                        {
                            if (isHit)
                            {
                                postureDmg = ONE_HANDED_AXE_HIT;
                                break;
                            }
                            else
                            {
                                postureDmg = ONE_HANDED_AXE_BLOCK;

                            }
                            break;
                        }
                    }
                case WeaponClass.OneHandedAxe:
                case WeaponClass.ThrowingAxe:
                    {
                        if (isParry)
                        {
                            postureDmg = ONE_HANDED_AXE_PARRY;
                            break;
                        }
                        else
                        {
                            if (isHit)
                            {
                                postureDmg = ONE_HANDED_AXE_HIT;
                                break;
                            }
                            else
                            {
                                postureDmg = ONE_HANDED_AXE_BLOCK;

                            }
                            break;
                        }
                    }
                case WeaponClass.TwoHandedAxe:
                    {
                        if (isParry)
                        {
                            postureDmg = TWO_HANDED_AXE_PARRY;
                            break;
                        }
                        else
                        {
                            if (isHit)
                            {
                                postureDmg = TWO_HANDED_AXE_HIT;
                                break;
                            }
                            else
                            {
                                postureDmg = TWO_HANDED_AXE_BLOCK;

                            }
                            break;
                        }
                    }
                case WeaponClass.Mace:
                case WeaponClass.Pick:
                    {
                        if (isParry)
                        {
                            postureDmg = TWO_HANDED_AXE_PARRY;
                            break;
                        }
                        else
                        {
                            if (isHit)
                            {
                                postureDmg = TWO_HANDED_AXE_HIT;
                                break;
                            }
                            else
                            {
                                postureDmg = TWO_HANDED_AXE_BLOCK;

                            }
                            break;
                        }
                    }
                case WeaponClass.TwoHandedMace:
                    {
                        if (isParry)
                        {
                            postureDmg = TWO_HANDED_AXE_PARRY;
                            break;
                        }
                        else
                        {
                            if (isHit)
                            {
                                postureDmg = TWO_HANDED_AXE_HIT;
                                break;
                            }
                            else
                            {
                                postureDmg = TWO_HANDED_AXE_BLOCK;

                            }
                            break;
                        }
                    }
                case WeaponClass.OneHandedPolearm:
                case WeaponClass.Javelin:
                    {
                        if (isParry)
                        {
                            postureDmg = TWO_HANDED_AXE_PARRY;
                            break;
                        }
                        else
                        {
                            if (isHit)
                            {
                                postureDmg = TWO_HANDED_AXE_HIT;
                                break;
                            }
                            else
                            {
                                postureDmg = TWO_HANDED_AXE_BLOCK;

                            }
                            break;
                        }
                    }
                case WeaponClass.TwoHandedPolearm:
                case WeaponClass.LowGripPolearm:
                    {
                        if (isParry)
                        {
                            postureDmg = TWO_HANDED_AXE_PARRY;
                            break;
                        }
                        else
                        {
                            if (isHit)
                            {
                                postureDmg = TWO_HANDED_AXE_HIT;
                                break;
                            }
                            else
                            {
                                postureDmg = TWO_HANDED_AXE_BLOCK;

                            }
                            break;
                        }
                    }
                case WeaponClass.SmallShield:
                    {
                        if (isParry)
                        {
                            postureDmg = TWO_HANDED_AXE_PARRY;
                            break;
                        }
                        else
                        {
                            if (isHit)
                            {
                                postureDmg = TWO_HANDED_AXE_HIT;
                                break;
                            }
                            else
                            {
                                postureDmg = TWO_HANDED_AXE_BLOCK;

                            }
                            break;
                        }
                    }
                case WeaponClass.LargeShield:
                    {
                        if (isParry)
                        {
                            postureDmg = TWO_HANDED_AXE_PARRY;
                            break;
                        }
                        else
                        {
                            if (isHit)
                            {
                                postureDmg = TWO_HANDED_AXE_HIT;
                                break;
                            }
                            else
                            {
                                postureDmg = TWO_HANDED_AXE_BLOCK;

                            }
                            break;
                        }
                    }
                default:
                    {
                        postureDmg = 20f;
                        break;
                    }
            }
            return postureDmg;
        }

        public static float getAttackerBasePostureDamage(WeaponClass attackerWC, Agent.UsageDirection attackDirection, StrikeType strikeType)
        {
            float postureDmg = 0f;
            switch (attackerWC)
            {
                case WeaponClass.Dagger:
                case WeaponClass.ThrowingKnife:
                case WeaponClass.OneHandedSword:
                    {
                        if (strikeType == StrikeType.Swing)
                        {
                            if (attackDirection == Agent.UsageDirection.AttackUp)
                            {
                                postureDmg = ONE_HANDED_SWORD_ATTACK_OVERHEAD;
                                break;
                            }
                            else
                            {
                                postureDmg = ONE_HANDED_SWORD_ATTACK_SWING;
                                break;
                            }
                        }
                        else
                        {
                            postureDmg = ONE_HANDED_SWORD_ATTACK_THRUST;
                            break;
                        }
                    }
                case WeaponClass.TwoHandedSword:
                    {
                        if (strikeType == StrikeType.Swing)
                        {
                            if (attackDirection == Agent.UsageDirection.AttackUp)
                            {
                                postureDmg = TWO_HANDED_SWORD_ATTACK_OVERHEAD;
                                break;
                            }
                            else
                            {
                                postureDmg = TWO_HANDED_SWORD_ATTACK_SWING;
                                break;
                            }
                        }
                        else
                        {
                            postureDmg = TWO_HANDED_SWORD_ATTACK_THRUST;
                            break;
                        }
                    }
                case WeaponClass.OneHandedAxe:
                case WeaponClass.ThrowingAxe:
                    {
                        if (strikeType == StrikeType.Swing)
                        {
                            if (attackDirection == Agent.UsageDirection.AttackUp)
                            {
                                postureDmg = ONE_HANDED_AXE_ATTACK_OVERHEAD;
                                break;
                            }
                            else
                            {
                                postureDmg = ONE_HANDED_AXE_ATTACK_SWING;
                                break;
                            }
                        }
                        else
                        {
                            postureDmg = ONE_HANDED_AXE_ATTACK_THRUST;
                            break;
                        }
                    }
                case WeaponClass.TwoHandedAxe:
                    {
                        if (strikeType == StrikeType.Swing)
                        {
                            if (attackDirection == Agent.UsageDirection.AttackUp)
                            {
                                postureDmg = TWO_HANDED_AXE_ATTACK_OVERHEAD;
                                break;
                            }
                            else
                            {
                                postureDmg = TWO_HANDED_AXE_ATTACK_SWING;
                                break;
                            }
                        }
                        else
                        {
                            postureDmg = TWO_HANDED_AXE_ATTACK_THRUST;
                            break;
                        }
                    }
                case WeaponClass.Mace:
                case WeaponClass.Pick:
                    {
                        if (strikeType == StrikeType.Swing)
                        {
                            if (attackDirection == Agent.UsageDirection.AttackUp)
                            {
                                postureDmg = MACE_ATTACK_OVERHEAD;
                                break;
                            }
                            else
                            {
                                postureDmg = MACE_ATTACK_SWING;
                                break;
                            }
                        }
                        else
                        {
                            postureDmg = MACE_ATTACK_THRUST;
                            break;
                        }
                    }
                case WeaponClass.TwoHandedMace:
                    {
                        if (strikeType == StrikeType.Swing)
                        {
                            if (attackDirection == Agent.UsageDirection.AttackUp)
                            {
                                postureDmg = TWO_HANDED_MACE_ATTACK_OVERHEAD;
                                break;
                            }
                            else
                            {
                                postureDmg = TWO_HANDED_MACE_ATTACK_SWING;
                                break;
                            }
                        }
                        else
                        {
                            postureDmg = TWO_HANDED_MACE_ATTACK_THRUST;
                            break;
                        }
                    }
                case WeaponClass.OneHandedPolearm:
                case WeaponClass.Javelin:
                    {
                        if (strikeType == StrikeType.Swing)
                        {
                            if (attackDirection == Agent.UsageDirection.AttackUp)
                            {
                                postureDmg = ONE_HANDED_POLEARM_ATTACK_OVERHEAD;
                                break;
                            }
                            else
                            {
                                postureDmg = ONE_HANDED_POLEARM_ATTACK_SWING;
                                break;
                            }
                        }
                        else
                        {
                            postureDmg = ONE_HANDED_POLEARM_ATTACK_THRUST;
                            break;
                        }
                    }
                case WeaponClass.TwoHandedPolearm:
                case WeaponClass.LowGripPolearm:
                    {
                        if (strikeType == StrikeType.Swing)
                        {
                            if (attackDirection == Agent.UsageDirection.AttackUp)
                            {
                                postureDmg = TWO_HANDED_POLEARM_ATTACK_OVERHEAD;
                                break;
                            }
                            else
                            {
                                postureDmg = TWO_HANDED_POLEARM_ATTACK_SWING;
                                break;
                            }
                        }
                        else
                        {
                            postureDmg = TWO_HANDED_POLEARM_ATTACK_THRUST;
                            break;
                        }
                    }
                case WeaponClass.SmallShield:
                    {
                        if (strikeType == StrikeType.Swing)
                        {
                            if (attackDirection == Agent.UsageDirection.AttackUp)
                            {
                                postureDmg = SMALL_SHIELD_ATTACK_OVERHEAD;
                                break;
                            }
                            else
                            {
                                postureDmg = SMALL_SHIELD_ATTACK_SWING;
                                break;
                            }
                        }
                        else
                        {
                            postureDmg = SMALL_SHIELD_ATTACK_THRUST;
                            break;
                        }
                    }
                case WeaponClass.LargeShield:
                    {
                        if (strikeType == StrikeType.Swing)
                        {
                            if (attackDirection == Agent.UsageDirection.AttackUp)
                            {
                                postureDmg = LARGE_SHIELD_ATTACK_OVERHEAD;
                                break;
                            }
                            else
                            {
                                postureDmg = LARGE_SHIELD_ATTACK_SWING;
                                break;
                            }
                        }
                        else
                        {
                            postureDmg = LARGE_SHIELD_ATTACK_THRUST;
                            break;
                        }
                    }
                default:
                    {
                        postureDmg = 20f;
                        break;
                    }
            }
            return postureDmg;
        }

        public static WeaponClass getDefenderWeaponClass(Agent agent)
        {
            WeaponClass wc = WeaponClass.Undefined;
            if (agent.GetWieldedItemIndex(Agent.HandIndex.OffHand) != EquipmentIndex.None)
            {
                if (agent.Equipment[agent.GetWieldedItemIndex(Agent.HandIndex.OffHand)].IsShield())
                {
                    wc = agent.Equipment[agent.GetWieldedItemIndex(Agent.HandIndex.OffHand)].CurrentUsageItem.WeaponClass;
                }
            }
            else
            {
                if (agent.GetWieldedItemIndex(0) != EquipmentIndex.None)
                {
                    wc = agent.Equipment[agent.GetWieldedItemIndex(0)].CurrentUsageItem.WeaponClass;
                }
            }
            return wc;
        }

        public static WeaponClass getAttackerWeaponClass(Agent agent)
        {
            WeaponClass wc = WeaponClass.Undefined;
            if (agent.GetWieldedItemIndex(0) != EquipmentIndex.None)
            {
                wc = agent.Equipment[agent.GetWieldedItemIndex(0)].CurrentUsageItem.WeaponClass;
            }
            return wc;
        }

        public static float getDefenseBasePostureDamage(Agent defender, Agent attacker, Agent.UsageDirection attackDirection, StrikeType strikeType, bool isParry, bool isDirectHit)
        {
            WeaponClass defenderWC = getDefenderWeaponClass(defender);
            WeaponClass attackerWC = getAttackerWeaponClass(attacker);

            float defenderPostureDmg = getDefenderBasePostureDamage(defenderWC, isParry, isDirectHit);
            float attackerPostureDmg = getAttackerBasePostureDamage(attackerWC, attackDirection, strikeType);

            return DEFAULT - defenderPostureDmg + attackerPostureDmg;
        }

        public static float getAttackBasePostureDamage(Agent defender, Agent attacker, Agent.UsageDirection attackDirection, StrikeType strikeType, bool isParry, bool isDirectHit)
        {
            WeaponClass defenderWC = getDefenderWeaponClass(defender);
            WeaponClass attackerWC = getAttackerWeaponClass(attacker);

            float defenderPostureDmg = getDefenderBasePostureDamage(defenderWC, isParry, isDirectHit);
            float attackerPostureDmg = getAttackerBasePostureDamage(attackerWC, attackDirection, strikeType);

            return DEFAULT - attackerPostureDmg + defenderPostureDmg;
        }
    }
}