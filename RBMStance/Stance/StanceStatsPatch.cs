using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMStance
{
    /// <summary>
    /// What stamina does to a man's driven properties: a tired man readies, blocks, moves, swings and reloads slower,
    /// aims worse, and (AI) decides worse. Moved out of RBMAI's SetAiRelatedProperties postfix so it works with RBM AI
    /// off; StanceLogic refreshes it when a man's stamina has moved far enough (a full UpdateAgentStats for the player,
    /// an in-place rescale through StaminaStatEffects.TryRescale for an AI man).
    ///
    /// Last on the method, after RBMCombat's postfix (assigns ReloadSpeed outright) and RBMAI's (Priority.Low, assigns
    /// the AI block/parry/aim values): these are multipliers on top of both. Also runs on the
    /// parallel agent tick (NavalDLC's AgentNavalComponent.OnTickParallel calls UpdateAgentStats), so it only reads
    /// managed state: AgentStances.values is a ConcurrentDictionary. Its one write outside the driven properties is the
    /// stance's bakedStaminaFraction, a plain float.
    /// </summary>
    [HarmonyPatch(typeof(AgentStatCalculateModel))]
    [HarmonyPatch("SetAiRelatedProperties")]
    internal class StanceStatsPatch
    {
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("com.rbmai", "com.rbmcombat")]
        private static void Postfix(Agent agent, AgentDrivenProperties agentDrivenProperties)
        {
            //stamina effects
            // Stamina is only tracked/regenerated while the posture system runs, so this needs
            // both toggles: staminaEnabled alone would read a frozen stamina value.
            if (RBMConfig.RBMConfig.postureEnabled && RBMConfig.RBMConfig.staminaEnabled)
            {
                if (agent == null || agentDrivenProperties == null)
                {
                    return;
                }
                Stance stance = null;
                AgentStances.values.TryGetValue(agent, out stance);
                if (stance != null)
                {
                    float staminaLevel = stance.stamina / stance.maxStamina;
                    StaminaStatEffects.Apply(agentDrivenProperties, staminaLevel);
                    stance.bakedStaminaFraction = staminaLevel;
                    stance.bakedAsAI = agent.IsAIControlled;
                }
            }
            else if (agent != null)
            {
                // Rebuilt without the stamina effects: what this man's properties hold is unknown to the in-place
                // rescale, so his next stamina refresh is a full rebuild.
                Stance stance = null;
                AgentStances.values.TryGetValue(agent, out stance);
                if (stance != null)
                {
                    stance.bakedStaminaFraction = float.NaN;
                }
            }
        }
    }

    /// <summary>
    /// The stamina effects as one table, read by both StanceStatsPatch (on every stat rebuild) and StanceLogic's
    /// in-place refresh (TryRescale), so the two cannot drift.
    /// </summary>
    internal static class StaminaStatEffects
    {
        // Each of these is multiplied by Lerp(low, 1, stamina fraction); low is the same index of ScaledLow.
        private static readonly DrivenProperty[] ScaledProperties =
        {
            //readying and blocking
            DrivenProperty.HandlingMultiplier,
            //movement speed
            DrivenProperty.MaxSpeedMultiplier,
            //attack speed
            DrivenProperty.SwingSpeedMultiplier,
            DrivenProperty.ThrustOrRangedReadySpeedMultiplier,
            //reload speed
            DrivenProperty.ReloadSpeed,
            //ranged penalties (the WeaponMaxMovement/UnsteadyAccuracyPenalty properties)
            DrivenProperty.WeaponWorstMobileAccuracyPenalty,
            DrivenProperty.WeaponWorstUnsteadyAccuracyPenalty,
            //ranged AI
            DrivenProperty.WeaponBestAccuracyWaitTime,
            //AI combat ability
            DrivenProperty.AIRealizeBlockingFromIncorrectSideAbility,
            DrivenProperty.AIDecideOnRealizeEnemyBlockingAttackAbility,
            DrivenProperty.AIAttackOnParryChance,
            //AI aggressiveness
            DrivenProperty.AIDecideOnAttackChance,
            DrivenProperty.AiAttackCalculationMaxTimeFactor
        };

        private static readonly float[] ScaledLow =
        {
            0.5f,
            0.85f,
            0.85f,
            0.85f,
            0.8f,
            1.15f,
            1.15f,
            1.3f,
            0.85f,
            0.85f,
            0.85f,
            0.85f,
            0.85f
        };

        // Blocking: OffhandWeaponDefendSpeedMultiplier is assigned outright, Lerp(this, 1, stamina fraction).
        private const float OffhandDefendSpeedLow = 0.5f;

        public static void Apply(AgentDrivenProperties properties, float staminaLevel)
        {
            for (int i = 0; i < ScaledProperties.Length; i++)
            {
                DrivenProperty property = ScaledProperties[i];
                properties.SetStat(property, properties.GetStat(property) * MBMath.Lerp(ScaledLow[i], 1f, staminaLevel));
            }
            properties.SetStat(DrivenProperty.OffhandWeaponDefendSpeedMultiplier, MBMath.Lerp(OffhandDefendSpeedLow, 1f, staminaLevel));
        }

        // Moves the stamina effects baked in at bakedLevel to newLevel without re-running the stat model. False, with
        // nothing changed, when the baked level is unknown (NaN) or a divisor is not positive: the caller rebuilds.
        public static bool TryRescale(AgentDrivenProperties properties, float bakedLevel, float newLevel)
        {
            if (float.IsNaN(bakedLevel))
            {
                return false;
            }
            for (int i = 0; i < ScaledLow.Length; i++)
            {
                if (!(MBMath.Lerp(ScaledLow[i], 1f, bakedLevel) > 0f))
                {
                    return false;
                }
            }
            for (int i = 0; i < ScaledProperties.Length; i++)
            {
                DrivenProperty property = ScaledProperties[i];
                float factor = MBMath.Lerp(ScaledLow[i], 1f, newLevel) / MBMath.Lerp(ScaledLow[i], 1f, bakedLevel);
                properties.SetStat(property, properties.GetStat(property) * factor);
            }
            properties.SetStat(DrivenProperty.OffhandWeaponDefendSpeedMultiplier, MBMath.Lerp(OffhandDefendSpeedLow, 1f, newLevel));
            return true;
        }
    }
}
