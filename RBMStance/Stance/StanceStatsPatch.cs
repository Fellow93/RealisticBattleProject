using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMStance
{
    /// <summary>
    /// What stamina does to a man's driven properties: a tired man readies, blocks, moves, swings and reloads slower,
    /// aims worse, and (AI) decides worse. Moved out of RBMAI's SetAiRelatedProperties postfix so it works with RBM AI
    /// off; StanceLogic re-runs UpdateAgentStats when a man's stamina has moved far enough.
    ///
    /// Last on the method, after RBMCombat's postfix (assigns ReloadSpeed outright) and RBMAI's (Priority.Low, assigns
    /// the AI block/parry/aim values): these are multipliers on top of both. Also runs on the
    /// parallel agent tick (NavalDLC's AgentNavalComponent.OnTickParallel calls UpdateAgentStats), so it only reads
    /// managed state: AgentStances.values is a ConcurrentDictionary.
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

                    //readying and blocking
                    agentDrivenProperties.HandlingMultiplier *= MBMath.Lerp(0.5f, 1f, staminaLevel);
                    agentDrivenProperties.OffhandWeaponDefendSpeedMultiplier = MBMath.Lerp(0.5f, 1f, staminaLevel);

                    //movement speed
                    agentDrivenProperties.MaxSpeedMultiplier *= MBMath.Lerp(0.85f, 1f, staminaLevel);

                    //attack speed
                    agentDrivenProperties.SwingSpeedMultiplier *= MBMath.Lerp(0.85f, 1f, staminaLevel);
                    agentDrivenProperties.ThrustOrRangedReadySpeedMultiplier *= MBMath.Lerp(0.85f, 1f, staminaLevel);

                    //reload speed
                    agentDrivenProperties.ReloadSpeed *= MBMath.Lerp(0.8f, 1f, staminaLevel);

                    //ranged penalties
                    agentDrivenProperties.WeaponMaxMovementAccuracyPenalty *= MBMath.Lerp(1.15f, 1f, staminaLevel);
                    agentDrivenProperties.WeaponMaxUnsteadyAccuracyPenalty *= MBMath.Lerp(1.15f, 1f, staminaLevel);

                    //ranged AI
                    agentDrivenProperties.WeaponBestAccuracyWaitTime *= MBMath.Lerp(1.3f, 1f, staminaLevel);

                    //AI combat ability
                    agentDrivenProperties.AIRealizeBlockingFromIncorrectSideAbility *= MBMath.Lerp(0.85f, 1f, staminaLevel);
                    agentDrivenProperties.AIDecideOnRealizeEnemyBlockingAttackAbility *= MBMath.Lerp(0.85f, 1f, staminaLevel);
                    agentDrivenProperties.AIAttackOnParryChance *= MBMath.Lerp(0.85f, 1f, staminaLevel);

                    //AI aggressiveness
                    agentDrivenProperties.AIDecideOnAttackChance *= MBMath.Lerp(0.85f, 1f, staminaLevel);
                    agentDrivenProperties.AiAttackCalculationMaxTimeFactor *= MBMath.Lerp(0.85f, 1f, staminaLevel);
                }
            }
        }
    }
}
