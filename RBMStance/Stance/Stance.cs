using System;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Library;

namespace RBMStance
{
    public class Stance
    {
        public const float StaminaRegenRubberBandStrength = 2.5f;

        public float posture;
        public float maxPosture = 100f;
        public float postureRegenPerTick = 0.01f;

        public float stamina;
        public float maxStamina = 1500f;
        public float staminaRegenPerTick = 0.01f;

        // Cache for CreateMeleeBlowPatch.GetDefenderBlockPerkFactor. The perk math is static for a
        // mission except for the wielded item (and its usage) and the formation captain, so the three
        // block-class factors are recomputed only when one of those keys moves.
        public bool blockPerkFactorsValid = false;
        public Agent blockPerkCaptain = null;
        public EquipmentIndex blockPerkPrimaryIndex = EquipmentIndex.None;
        public EquipmentIndex blockPerkOffhandIndex = EquipmentIndex.None;
        public int blockPerkUsageIndex = -1;
        public float blockPerkWeaponFactor = 1f;
        public float blockPerkShieldFactor = 1f;
        public float blockPerkShieldIncorrectFactor = 1f;

        // Mission time at which this stance was created (i.e. the agent was being built). The
        // stat-refresh loop skips agents younger than StanceLogic.MinAgeForStatRefresh so
        // UpdateAgentStats never runs on an agent whose spawn is still in progress.
        public float createdAt = 0f;

        // Stamina fraction that was last pushed into AgentDrivenProperties via UpdateAgentStats.
        // The refresh is event-driven: it only fires when the live fraction has drifted from this.
        public float lastAppliedStaminaFraction = 1f;

        // Plain fields, written on the main thread only (RBMAI's Frontline job reads this object off-thread).
        // Times are Mission.CurrentTime. The break count lets a blow's later patches tell that it broke posture.
        public int postureBreakCount = 0;
        public float lastPostureDamageTime = float.MinValue;
        public float lastPostureEventTime = float.MinValue;

        public Stance()
        {
            Mission mission = Mission.Current;
            this.createdAt = mission != null ? mission.CurrentTime : 0f;
            this.posture = this.maxPosture;

            this.stamina = this.maxStamina;
        }

        public static void InitializeStamina(Agent agent, ref Stance stance)
        {
            float athleticBase = 1000f;
            int effectiveAthleticSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(agent, DefaultSkills.Athletics);
            float athleticSkillModifier = 500f;
            stance.maxStamina = athleticBase * (1f + 3f * (effectiveAthleticSkill / athleticSkillModifier));
            stance.stamina = athleticBase * (1f + 3f * (effectiveAthleticSkill / athleticSkillModifier));
            stance.staminaRegenPerTick = 0.03f * (1f + 2f * (effectiveAthleticSkill / athleticSkillModifier));

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
                stance.stamina = stance.maxStamina;
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
            // The weapon-dependent term is the only part that needs a wielded item; an unarmed agent
            // falls back to WeaponClass.Undefined (the UNARMED table rows), whose relevant skill is
            // Athletics. Everything below then runs for wielded and unwielded agents alike.
            SkillObject weaponSkill = DefaultSkills.Athletics;
            if (slotIndex != EquipmentIndex.None)
            {
                usageIndex = agent.Equipment[slotIndex].CurrentUsageIndex;
                int usageCount = agent.Equipment[slotIndex].Item?.Weapons?.Count ?? 0;
                if (usageCount > 0)
                {
                    usageIndex = MBMath.ClampInt(usageIndex, 0, usageCount - 1);
                }

                WeaponComponentData wcd = agent.Equipment[slotIndex].GetWeaponComponentDataForUsage(usageIndex);
                weaponSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(wcd.WeaponClass);
            }
            {
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
                float armorWeight = Math.Max(0f, StanceUtilities.GetEffectiveArmorWeight(agent) - 5f);
                stance.maxPosture += armorWeight;

                stance.posture = stance.maxPosture * oldPosturePercentage;
            }
        }

        public void ReduceStamina(float value)
        {
            this.stamina = Math.Max(0f, this.stamina - value);
        }

        public void AddStamina(float value)
        {
            this.stamina = Math.Min(maxStamina, this.stamina + value);
        }

        public void ReducePosture(float value)
        {
            this.posture = Math.Max(0f, this.posture - value);
        }

        public void AddPosture(float value)
        {
            this.posture = Math.Min(maxPosture, this.posture + value);
        }

        // Posture refills at once to the reset share of max.
        public void BreakPosture(float now, float resetShare)
        {
            posture = Math.Max(0f, resetShare) * maxPosture;
            postureBreakCount++;
            MarkPostureDamage(now);
        }

        public void MarkPostureDamage(float now)
        {
            lastPostureDamageTime = now;
            lastPostureEventTime = now;
        }

        // Regen speeds up as the bar empties.
        private static float CalculateRubberBandFactor(float current, float max, float rubberBandStrength)
        {
            float deficit = 1f - (current / max);
            return ((float)Math.Pow(1f + deficit, rubberBandStrength));
        }

        public void TickStaminaRegen(float multiplier)
        {
            float rubberBandFactor = CalculateRubberBandFactor(this.stamina, this.maxStamina, StaminaRegenRubberBandStrength);
            float value = this.staminaRegenPerTick;
            value *= PostureTuning.RegenTicksPerUpdate;
            value *= multiplier;
            value *= rubberBandFactor;
            AddStamina(value);
        }

        public void TickPostureRegen(float multiplier)
        {
            float value = this.postureRegenPerTick;
            value *= PostureTuning.RegenTicksPerUpdate;
            value *= multiplier;
            AddPosture(value);
        }
    }
}
