using System;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    public class Stance
    {
        public const float PostureRegenRubberBandStrength = 0f;
        public const float StaminaRegenRubberBandStrength = 2f;

        public float posture;
        public float maxPosture = 100f;
        public float postureRegenPerTick = 0.01f;

        public float stamina;
        public float maxStamina = 1500f;
        public float staminaRegenPerTick = 0.01f;

        public Stance()
        {
            this.posture = this.maxPosture;

            this.stamina = this.maxStamina;
        }

        public static void InitializeStamina(Agent agent, ref Stance stance)
        {
            float athleticBase = 1000f;
            int effectiveAthleticSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(agent, DefaultSkills.Athletics);
            float athleticSkillModifier = 500f;
            stance.maxStamina = athleticBase * (1f + (effectiveAthleticSkill / athleticSkillModifier));
            stance.stamina = athleticBase * (1f + (effectiveAthleticSkill / athleticSkillModifier));
            stance.staminaRegenPerTick = 0.04f * (1f + (effectiveAthleticSkill / athleticSkillModifier));

            //face armor effect
            float faceArmor = 0f;
            if (!agent.SpawnEquipment[EquipmentIndex.Head].IsEmpty)
            {
                faceArmor = agent.SpawnEquipment[EquipmentIndex.Head].GetModifiedBodyArmor();
            }
            if (faceArmor >= 30f)
            {
                stance.staminaRegenPerTick *= 0.5f;
            }

            if (agent.IsPlayerControlled)
            {
                stance.maxStamina *= RBMConfig.RBMConfig.playerPostureMultiplier;
                stance.staminaRegenPerTick *= RBMConfig.RBMConfig.playerPostureMultiplier;
            }

        }

        public static void InitializePosture(Agent agent, ref Stance stance)
        {
            float oldPosture = stance.posture;
            float oldMaxPosture = stance.maxPosture;
            float oldPosturePercentage = oldPosture / oldMaxPosture;

            int usageIndex = 0;
            EquipmentIndex slotIndex = agent.GetPrimaryWieldedItemIndex();
            if (slotIndex != EquipmentIndex.None)
            {
                usageIndex = agent.Equipment[slotIndex].CurrentUsageIndex;

                WeaponComponentData wcd = agent.Equipment[slotIndex].GetWeaponComponentDataForUsage(usageIndex);
                SkillObject weaponSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(wcd.WeaponClass);
                int effectiveWeaponSkill = 0;
                if (weaponSkill != null)
                {
                    effectiveWeaponSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(agent, weaponSkill);
                }

                float basePosture = 30f;
                float athleticBase = 20f;
                float weaponSkillBase = 80f;
                float strengthSkillModifier = 500f;
                float weaponSkillModifier = 500f;
                float athleticRegenBase = 0.016f;
                float weaponSkillRegenBase = 0.064f;
                float baseModifier = 1f;

                stance.maxPosture = basePosture;
                if (agent.HasMount)
                {
                    int effectiveRidingSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(agent, DefaultSkills.Riding);
                    stance.maxPosture += (athleticBase * (baseModifier + (effectiveRidingSkill / strengthSkillModifier))) + (weaponSkillBase * (baseModifier + (effectiveWeaponSkill / weaponSkillModifier)));
                    stance.postureRegenPerTick = (athleticRegenBase * (baseModifier + (effectiveRidingSkill / strengthSkillModifier))) + (weaponSkillRegenBase * (baseModifier + (effectiveWeaponSkill / weaponSkillModifier)));
                }
                else
                {
                    int effectiveAthleticSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(agent, DefaultSkills.Athletics);
                    stance.maxPosture += (athleticBase * (baseModifier + (effectiveAthleticSkill / strengthSkillModifier))) + (weaponSkillBase * (baseModifier + (effectiveWeaponSkill / weaponSkillModifier)));
                    stance.postureRegenPerTick = (athleticRegenBase * (baseModifier + (effectiveAthleticSkill / strengthSkillModifier))) + (weaponSkillRegenBase * (baseModifier + (effectiveWeaponSkill / weaponSkillModifier)));
                }

                if (agent.IsPlayerControlled)
                {
                    stance.maxPosture *= RBMConfig.RBMConfig.playerPostureMultiplier;
                    stance.postureRegenPerTick *= RBMConfig.RBMConfig.playerPostureMultiplier;
                }

                //armor weight effect
                float armorWeight = Math.Max(0f, agent.SpawnEquipment.GetTotalWeightOfArmor(true) - 5f);
                stance.maxPosture += armorWeight;

                stance.posture = stance.maxPosture * oldPosturePercentage;
            }
        }

        public void reduceStamina(float value)
        {
            this.stamina = Math.Max(0f, this.stamina - value);
        }

        public void addStamina(float value)
        {
            this.stamina = Math.Min(maxStamina, this.stamina + value);
        }
        public void reducePosture(float value)
        {
            this.posture = Math.Max(0f, this.posture - value);
        }
        public void addPosture(float value)
        {
            this.posture = Math.Min(maxPosture, this.posture + value);
        }

        public float calcualteRubberBandFactor(float current, float max, float rubberBandStrength)
        {
            float deficit = 1f - (current / max);
            return ((float)Math.Pow(1f + deficit, rubberBandStrength));
        }

        public void tickStaminaRegen(int tickCount = 30, float multiplier = 1f)
        {
            float rubberBandFactor = calcualteRubberBandFactor(this.stamina, this.maxStamina, StaminaRegenRubberBandStrength);
            float value = this.staminaRegenPerTick;
            value *= tickCount;
            value *= multiplier;
            value *= rubberBandFactor;
            addStamina(value);
        }

        public void tickPostureRegen(int tickCount = 30, float multiplier = 1f)
        {
            float rubberBandFactor = calcualteRubberBandFactor(this.posture, this.maxPosture, PostureRegenRubberBandStrength);
            float value = this.postureRegenPerTick;
            value *= tickCount;
            value *= multiplier;
            value *= rubberBandFactor;
            addPosture(value);
        }

        public Stance(float maxPosture, float regenPerTick, float maxStamina, float staminaRegenPerTick)
        {
            this.maxPosture = maxPosture;
            this.postureRegenPerTick = regenPerTick;
            this.posture = this.maxPosture;

            this.maxStamina = maxStamina;
            this.staminaRegenPerTick = staminaRegenPerTick;
            this.stamina = this.maxStamina;
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
        public const float TWOHANDEDAXE_SWING_COST = -2f;
        public const float TWOHANDEDAXE_THRUST_COST = -4f;
        public const float TWOHANDEDAXE_OVERHEAD_COST = +0f;
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
        public const float TWOHANDEDMACE_SWING_COST = +0f;
        public const float TWOHANDEDMACE_THRUST_COST = -2f;
        public const float TWOHANDEDMACE_OVERHEAD_COST = +2f;
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
        public const float TWOHANDEDPOLEARM_SWING_COST = -3f;
        public const float TWOHANDEDPOLEARM_THRUST_COST = -5f;
        public const float TWOHANDEDPOLEARM_OVERHEAD_COST = -1f;
        public const float TWOHANDEDPOLEARM_SWING_DRAIN = +36f;
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
            string weaponClassString = wc == WeaponClass.Undefined ? "UNARMED" : wc.ToString().ToUpper();
            try
            {
                switch (hitType)
                {
                    case MeleeHitType.WeaponBlock:
                    case MeleeHitType.ShieldBlock:
                        {
                            retVal += (float)typeof(PostureDamage).GetField(weaponClassString + "_BLOCK_COST").GetValue(null);
                            break;
                        }
                    case MeleeHitType.WeaponParry:
                    case MeleeHitType.ShieldParry:
                        {
                            retVal += (float)typeof(PostureDamage).GetField(weaponClassString + "_PARRY_COST").GetValue(null);
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
                                retVal += (float)typeof(PostureDamage).GetField(weaponClassString + "_INCORRECT_BLOCK_COST").GetValue(null);
                            }
                            else
                            {
                                retVal += SHIELD_ON_BACK_HIT_COST;
                            }
                            break;

                        }
                    case MeleeHitType.ChamberBlock:
                        {
                            retVal += (float)typeof(PostureDamage).GetField(weaponClassString + "_PARRY_COST").GetValue(null) * 0.5f;
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
            string weaponClassString = wc == WeaponClass.Undefined ? "UNARMED" : wc.ToString().ToUpper();
            try
            {
                if (strikeType == StrikeType.Swing)
                {
                    if (attackDirection == Agent.UsageDirection.AttackUp)
                    {
                        retVal += (float)typeof(PostureDamage).GetField(weaponClassString + "_OVERHEAD_DRAIN").GetValue(null);
                    }
                    else
                    {
                        retVal += (float)typeof(PostureDamage).GetField(weaponClassString + "_SWING_DRAIN").GetValue(null);
                    }
                }
                else
                {
                    retVal += (float)typeof(PostureDamage).GetField(weaponClassString + "_THRUST_DRAIN").GetValue(null);
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
            string weaponClassString = wc == WeaponClass.Undefined ? "UNARMED" : wc.ToString().ToUpper();
            try
            {
                if (strikeType == StrikeType.Swing)
                {
                    if (attackDirection == Agent.UsageDirection.AttackUp)
                    {
                        retVal += (float)typeof(PostureDamage).GetField(weaponClassString + "_OVERHEAD_COST").GetValue(null);
                    }
                    else
                    {
                        retVal += (float)typeof(PostureDamage).GetField(weaponClassString + "_SWING_COST").GetValue(null);
                    }
                }
                else
                {
                    retVal += (float)typeof(PostureDamage).GetField(weaponClassString + "_THRUST_COST").GetValue(null);
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
            string weaponClassString = wc == WeaponClass.Undefined ? "UNARMED" : wc.ToString().ToUpper();
            try
            {
                switch (hitType)
                {
                    case MeleeHitType.WeaponBlock:
                    case MeleeHitType.ShieldBlock:
                        {
                            retVal += (float)typeof(PostureDamage).GetField(weaponClassString + "_BLOCK_REFLECT").GetValue(null);
                            break;
                        }
                    case MeleeHitType.WeaponParry:
                    case MeleeHitType.ShieldParry:
                        {
                            retVal += (float)typeof(PostureDamage).GetField(weaponClassString + "_PARRY_REFLECT").GetValue(null);
                            break;
                        }
                    case MeleeHitType.ShieldIncorrectBlock:
                        {
                            if (wc == WeaponClass.SmallShield || wc == WeaponClass.LargeShield)
                            {
                                retVal += (float)typeof(PostureDamage).GetField(weaponClassString + "_INCORRECT_BLOCK_REFLECT").GetValue(null);
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
                            retVal += (float)typeof(PostureDamage).GetField(weaponClassString + "_PARRY_REFLECT").GetValue(null) * 2f;
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