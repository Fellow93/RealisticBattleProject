using System;
using System.Globalization;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace RBMConfig
{
    /// <summary>
    /// RBM's One Handed "Arrow Catcher" perk. Vanilla's only effect is a larger shield catch box against missiles
    /// (AttributeShieldMissileCollisionBodySizeAdder), which RBMCombat fixes at 0.01 for everyone on purpose
    /// (DamageRework.HitReaction.cs: no magic shields). The perk instead makes the shield holder better at taking
    /// missiles on a raised shield:
    ///  - posture and stamina a missile block costs (RBMAI StanceLogic.OnAgentHit, needs rbmAiEnabled + postureEnabled);
    ///  - damage a missile does to the shield (RBMCombat DamageRework.RBMComputeBlowDamageOnShield);
    ///  - how much of a pilum gets through the shield (RBMCombat RangedRework.PilumShieldPenetration).
    /// Only for a wielded shield; a missile into a shield on the back gets none of it.
    ///
    /// Lives here because RBMAI and RBMCombat both need it and neither references the other.
    ///
    /// Strength follows the perk's two halves as vanilla resolves them: the personal half (primary, NavalReduced) on
    /// the shield holder's own CharacterObject is full strength (half at sea); the captain half (secondary,
    /// LandOnly) from his formation's captain, when that is someone else (SandboxAgentStatCalculateModel.
    /// SetPerkAndBannerEffectsOnAgent's captain rule), adds <see cref="CaptainStrength"/>. Capped at 1. Both checks
    /// go through CharacterObject.GetPerkValue(perk, environment, isPrimaryEffect, out value), so troops given the
    /// perk in rbm_troop_perks.xml get it too (RBMCombat TroopPerkEffects).
    ///
    /// Main thread only (the missile-hit callbacks); never call it from an AI thread.
    /// </summary>
    public static class ArrowCatcher
    {
        // Full-strength factors (personal perk); a captain alone gives half the reduction.
        public const float FullPostureFactor = 0.75f;
        public const float FullShieldDamageFactor = 0.8f;
        public const float FullPilumPenetrationFactor = 0.85f;
        public const float CaptainStrength = 0.5f;

        /// <summary>
        /// 0 (no perk) to 1 (personal perk on land) for the agent holding the shield. 0 outside a campaign, where
        /// there are no perks.
        /// </summary>
        public static float GetStrength(Agent agent)
        {
            if (agent == null || !agent.IsHuman || Campaign.Current == null)
            {
                return 0f;
            }
            PerkObject perk = DefaultPerks.OneHanded.ArrowCatcher;
            if (perk == null)
            {
                return 0f;
            }
            BattleEnvironment environment = agent.CurrentBattleEnvironment;
            float strength = 0f;
            CharacterObject character = agent.Character as CharacterObject;
            if (character != null && perk.PrimaryRole == PartyRole.Personal
                && character.GetPerkValue(perk, environment, true, out float personalValue))
            {
                strength += perk.PrimaryBonus > 0f ? personalValue / perk.PrimaryBonus : 1f;
            }
            Agent captainAgent = agent.Formation?.Captain;
            CharacterObject captain = captainAgent != null && captainAgent != agent ? captainAgent.Character as CharacterObject : null;
            if (captain != null && perk.SecondaryRole == PartyRole.Captain
                && captain.GetPerkValue(perk, environment, false, out float captainValue))
            {
                strength += CaptainStrength * (perk.SecondaryBonus > 0f ? captainValue / perk.SecondaryBonus : 1f);
            }
            return Math.Min(1f, strength);
        }

        /// <summary>Multiplier on the posture and stamina a missile block costs.</summary>
        public static float GetPostureFactor(Agent agent)
        {
            return Scale(FullPostureFactor, GetStrength(agent));
        }

        /// <summary>Multiplier on the damage a blocked missile does to the shield.</summary>
        public static float GetShieldDamageFactor(Agent agent)
        {
            return Scale(FullShieldDamageFactor, GetStrength(agent));
        }

        /// <summary>Multiplier on the share of a pilum's throw that gets through the shield.</summary>
        public static float GetPilumPenetrationFactor(Agent agent)
        {
            return Scale(FullPilumPenetrationFactor, GetStrength(agent));
        }

        private static float Scale(float fullFactor, float strength)
        {
            return strength <= 0f ? 1f : 1f + (fullFactor - 1f) * strength;
        }

        /// <summary>
        /// Replaces the perk's texts ("larger shield protection area against projectiles") with what it does under
        /// RBM. Only when RBM Combat is on: without it vanilla's catch box is not overridden and the vanilla text is
        /// right. Re-runs the perk's own public Initialize with every value read back from the perk, so only the
        /// texts change. Called once per campaign from RBM's SubModule.OnGameInitializationFinished (DefaultPerks is
        /// rebuilt for every campaign, new or loaded, with vanilla texts).
        /// </summary>
        public static void ApplyDescription()
        {
            if (!RBMConfig.rbmCombatEnabled || Campaign.Current == null)
            {
                return;
            }
            PerkObject perk = DefaultPerks.OneHanded.ArrowCatcher;
            if (perk == null || perk.Skill == null)
            {
                return;
            }
            perk.Initialize("{=a94mkNNk}Arrow Catcher", perk.Skill, (int)perk.RequiredSkillValue, perk.AlternativePerk,
                "{=RBM_PERK_ARROWCATCHER_P}Blocking projectiles with your shield costs {POSTURE}% less posture and stamina, the shield takes {SHIELD}% less damage from them, and pila punch {PILUM}% less through it.",
                perk.PrimaryRole, perk.PrimaryBonus, perk.PrimaryIncrementType,
                "{=RBM_PERK_ARROWCATCHER_C}Troops in your formation: blocking projectiles with a shield costs {POSTURE}% less posture and stamina, the shield takes {SHIELD}% less damage from them, and pila punch {PILUM}% less through it.",
                perk.SecondaryRole, perk.SecondaryBonus, perk.SecondaryIncrementType,
                perk.PrimaryTroopUsageMask, perk.SecondaryTroopUsageMask, perk.PrimaryEffectEnvironment, perk.SecondaryEffectEnvironment);
            // The combined description shown in the perk tooltip holds these same TextObjects, so it follows.
            SetPercentVariables(perk.PrimaryDescription, 1f);
            SetPercentVariables(perk.SecondaryDescription, CaptainStrength);
        }

        private static void SetPercentVariables(TaleWorlds.Localization.TextObject text, float strength)
        {
            text.SetTextVariable("POSTURE", Percent(Scale(FullPostureFactor, strength)));
            text.SetTextVariable("SHIELD", Percent(Scale(FullShieldDamageFactor, strength)));
            text.SetTextVariable("PILUM", Percent(Scale(FullPilumPenetrationFactor, strength)));
        }

        private static string Percent(float factor)
        {
            return ((1f - factor) * 100f).ToString("0.#", CultureInfo.InvariantCulture);
        }
    }
}
