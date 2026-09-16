using HarmonyLib;
using Helpers;
using JetBrains.Annotations;
using NetworkMessages.FromServer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ItemObject;
using static TaleWorlds.MountAndBlade.Mission;

namespace RBMCombat
{
    public partial class RangedRework
    {
        [HarmonyPatch(typeof(AgentStatCalculateModel))]
        [HarmonyPatch("SetAiRelatedProperties")]
        private class OverrideSetAiRelatedProperties
        {
            // Runs before RBMAI's postfix on the same method (Priority.Low there), which multiplies
            // ReloadSpeed by stamina. This one assigns the base value, so it must go first.
            [HarmonyPriority(Priority.High)]
            /// <summary>
            /// The reload perks vanilla folds into ReloadSpeed in
            /// SandboxAgentStatCalculateModel.SetPerkAndBannerEffectsOnAgent (Bow.RapidFire, Bow.Deadshot,
            /// Crossbow.WindWinder, Crossbow.MightyPull). RBM assigns its own skill-scaled reload value
            /// below, which would otherwise discard them, so they are re-applied as a factor.
            /// Throwing is not assigned here, so Throwing.QuickDraw survives untouched.
            /// </summary>
            private static float GetReloadPerkFactor(Agent agent, WeaponComponentData equippedItem)
            {
                if (equippedItem == null || Campaign.Current == null)
                {
                    return 1f;
                }
                CharacterObject agentCharacter = agent.Character as CharacterObject;
                if (agentCharacter == null)
                {
                    return 1f;
                }
                SkillObject relevantSkill = equippedItem.RelevantSkill;
                if (relevantSkill != DefaultSkills.Bow && relevantSkill != DefaultSkills.Crossbow)
                {
                    return 1f;
                }
                Agent captainAgent = agent.Formation?.Captain;
                CharacterObject captain = (captainAgent != null && captainAgent != agent) ? captainAgent.Character as CharacterObject : null;
                int epicMinSkill = Campaign.Current.Models.CharacterDevelopmentModel.MinSkillRequiredForEpicPerkBonus;

                ExplainedNumber bonuses = new ExplainedNumber(1f);
                if (relevantSkill == DefaultSkills.Bow)
                {
                    PerkHelper.AddPerkBonusForCharacter(DefaultPerks.Bow.RapidFire, agentCharacter, true, ref bonuses);
                    if (captain != null)
                    {
                        PerkHelper.AddPerkBonusFromCaptain(DefaultPerks.Bow.RapidFire, captain, ref bonuses);
                    }
                    PerkHelper.AddEpicPerkBonusForCharacter(DefaultPerks.Bow.Deadshot, agentCharacter, DefaultSkills.Bow, true, ref bonuses, epicMinSkill);
                }
                else
                {
                    PerkHelper.AddPerkBonusForCharacter(DefaultPerks.Crossbow.WindWinder, agentCharacter, true, ref bonuses);
                    if (captain != null)
                    {
                        PerkHelper.AddPerkBonusFromCaptain(DefaultPerks.Crossbow.WindWinder, captain, ref bonuses);
                    }
                    PerkHelper.AddEpicPerkBonusForCharacter(DefaultPerks.Crossbow.MightyPull, agentCharacter, DefaultSkills.Crossbow, true, ref bonuses, epicMinSkill);
                }
                return bonuses.ResultNumber;
            }

            private static void Postfix(Agent agent, ref AgentDrivenProperties agentDrivenProperties, WeaponComponentData equippedItem, WeaponComponentData secondaryItem, AgentStatCalculateModel __instance)
            {
                float perkFactor = GetReloadPerkFactor(agent, equippedItem);
                if (agent.IsPlayerControlled)
                {
                    if (RBMConfig.RBMConfig.realisticRangedReload.Equals("1"))
                    {
                        SkillObject skill = (equippedItem == null) ? DefaultSkills.Athletics : equippedItem.RelevantSkill;
                        if (skill != null)
                        {
                            int ef = __instance.GetEffectiveSkill(agent, skill);
                            float effectiveSkill = Utilities.GetEffectiveSkillWithDR(ef);
                            if (equippedItem != null)
                            {
                                switch (equippedItem.ItemUsage)
                                {
                                    case "bow":
                                    case "long_bow":
                                        {
                                            agentDrivenProperties.ReloadSpeed = 0.25f * (0.85f + (0.0184f * effectiveSkill)) * perkFactor;
                                            break;
                                        }
                                    case "crossbow_fast":
                                        {
                                            agentDrivenProperties.ReloadSpeed = 0.3f * (1f + (0.0045f * effectiveSkill)) * perkFactor;
                                            break;
                                        }
                                    case "crossbow":
                                        {
                                            agentDrivenProperties.ReloadSpeed = 0.2f * (1f + (0.0045f * effectiveSkill)) * perkFactor;
                                            break;
                                        }
                                }
                            }
                        }
                    }
                    else if (RBMConfig.RBMConfig.realisticRangedReload.Equals("2"))
                    {
                        SkillObject skill = (equippedItem == null) ? DefaultSkills.Athletics : equippedItem.RelevantSkill;
                        if (skill != null)
                        {
                            int ef = __instance.GetEffectiveSkill(agent, skill);
                            float effectiveSkill = Utilities.GetEffectiveSkillWithDR(ef);
                            if (equippedItem != null)
                            {
                                switch (equippedItem.ItemUsage)
                                {
                                    case "bow":
                                    case "long_bow":
                                        {
                                            agentDrivenProperties.ReloadSpeed = 0.38f * (1.5f + (0.0075f * effectiveSkill)) * perkFactor;
                                            break;
                                        }
                                    case "crossbow_fast":
                                        {
                                            agentDrivenProperties.ReloadSpeed = 0.72f * (1 + (0.0035f * effectiveSkill)) * perkFactor;
                                            break;
                                        }
                                    case "crossbow":
                                        {
                                            agentDrivenProperties.ReloadSpeed = 0.36f * (1 + (0.0035f * effectiveSkill)) * perkFactor;
                                            break;
                                        }
                                }
                            }
                        }
                    }
                }
                else
                {
                    SkillObject skill = (equippedItem == null) ? DefaultSkills.Athletics : equippedItem.RelevantSkill;
                    if (skill != null)
                    {
                        int ef = __instance.GetEffectiveSkill(agent, skill);
                        float effectiveSkill = Utilities.GetEffectiveSkillWithDR(ef);

                        if (equippedItem != null)
                        {
                            switch (equippedItem.ItemUsage)
                            {
                                case "bow":
                                case "long_bow":
                                    {
                                        agentDrivenProperties.ReloadSpeed = 0.25f * (1f + (0.016f * effectiveSkill)) * perkFactor;
                                        break;
                                    }
                                case "crossbow_fast":
                                    {
                                        agentDrivenProperties.ReloadSpeed = 0.3f * (1f + (0.0045f * effectiveSkill)) * perkFactor;
                                        break;
                                    }
                                case "crossbow":
                                    {
                                        agentDrivenProperties.ReloadSpeed = 0.2f * (1f + (0.0045f * effectiveSkill)) * perkFactor;
                                        break;
                                    }
                            }
                        }
                    }
                }
                //0.12 for heavy crossbows, 0.19f for light crossbows, composite bows and longbows.
            }
        }
    }
}
