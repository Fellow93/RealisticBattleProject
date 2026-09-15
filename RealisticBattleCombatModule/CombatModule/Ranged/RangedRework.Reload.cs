using HarmonyLib;
using JetBrains.Annotations;
using NetworkMessages.FromServer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
            private static void Postfix(Agent agent, ref AgentDrivenProperties agentDrivenProperties, WeaponComponentData equippedItem, WeaponComponentData secondaryItem, AgentStatCalculateModel __instance)
            {
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
                                            agentDrivenProperties.ReloadSpeed = 0.25f * (0.85f + (0.0184f * effectiveSkill));
                                            break;
                                        }
                                    case "crossbow_fast":
                                        {
                                            agentDrivenProperties.ReloadSpeed = 0.3f * (1f + (0.0045f * effectiveSkill));
                                            break;
                                        }
                                    case "crossbow":
                                        {
                                            agentDrivenProperties.ReloadSpeed = 0.2f * (1f + (0.0045f * effectiveSkill));
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
                                            agentDrivenProperties.ReloadSpeed = 0.38f * (1.5f + (0.0075f * effectiveSkill));
                                            break;
                                        }
                                    case "crossbow_fast":
                                        {
                                            agentDrivenProperties.ReloadSpeed = 0.72f * (1 + (0.0035f * effectiveSkill));
                                            break;
                                        }
                                    case "crossbow":
                                        {
                                            agentDrivenProperties.ReloadSpeed = 0.36f * (1 + (0.0035f * effectiveSkill));
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
                                        agentDrivenProperties.ReloadSpeed = 0.25f * (1f + (0.016f * effectiveSkill));
                                        break;
                                    }
                                case "crossbow_fast":
                                    {
                                        agentDrivenProperties.ReloadSpeed = 0.3f * (1f + (0.0045f * effectiveSkill));
                                        break;
                                    }
                                case "crossbow":
                                    {
                                        agentDrivenProperties.ReloadSpeed = 0.2f * (1f + (0.0045f * effectiveSkill));
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
