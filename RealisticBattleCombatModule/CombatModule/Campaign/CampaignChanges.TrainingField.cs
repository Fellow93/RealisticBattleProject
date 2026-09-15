using HarmonyLib;
using Helpers;
using JetBrains.Annotations;
using RBMAI;
using StoryMode.GameComponents;
using StoryMode.Missions;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.CampaignSystem.ComponentInterfaces.CombatXpModel;
using static TaleWorlds.CampaignSystem.MapEvents.MapEvent;

namespace RBMCombat
{
    internal partial class CampaignChanges
    {
        [HarmonyPatch(typeof(StoryMode.Extensions.Extensions))]
        [HarmonyPatch("IsTrainingField")]
        private class IsTrainingFieldPatch
        {
            private static bool Prefix(ref bool __result)
            {
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(TrainingFieldMissionController))]
        [HarmonyPatch("BowInTrainingAreaUpdate")]
        private static class BowInTrainingAreaUpdatePatch
        {
            private static int lastBreakeableCount = -1;
            private static bool shouldCount = false;

            private static void Postfix(int ____trainingProgress, TutorialArea ____activeTutorialArea, int ____trainingSubTypeIndex)
            {
                if (____trainingProgress == 1)
                {
                    lastBreakeableCount = -1;
                }
                if (____trainingProgress == 4)
                {
                    if (lastBreakeableCount == -1)
                    {
                        lastBreakeableCount = ____activeTutorialArea.GetBrokenBreakableCount(____trainingSubTypeIndex);
                    }
                    else
                    {
                        if (lastBreakeableCount != ____activeTutorialArea.GetBrokenBreakableCount(____trainingSubTypeIndex))
                        {
                            lastBreakeableCount = ____activeTutorialArea.GetBrokenBreakableCount(____trainingSubTypeIndex);
                            shouldCount = true;
                        }
                    }
                }
                if (shouldCount && ____trainingProgress == 4)
                {
                    shouldCount = false;
                    EquipmentIndex ei = Mission.Current.MainAgent.GetPrimaryWieldedItemIndex();
                    if (ei != EquipmentIndex.None)
                    {
                        CharacterObject playerCharacter = (CharacterObject)CharacterObject.PlayerCharacter;
                        if (playerCharacter != null)
                        {
                            if (Mission.Current.MainAgent.WieldedWeapon.CurrentUsageItem != null)
                            {
                                WeaponComponentData wieldedWeapon = Mission.Current.MainAgent.WieldedWeapon.CurrentUsageItem;
                                SkillObject skillForWeapon = Campaign.Current.Models.CombatXpModel.GetSkillForWeapon(wieldedWeapon, false);
                                if (skillForWeapon != null)
                                {
                                    playerCharacter.HeroObject.AddSkillXp(skillForWeapon, 50);
                                    if (Mission.Current.MainAgent.HasMount)
                                    {
                                        playerCharacter.HeroObject.AddSkillXp(DefaultSkills.Riding, 25);
                                    }
                                    else
                                    {
                                        playerCharacter.HeroObject.AddSkillXp(DefaultSkills.Athletics, 25);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public static void ResetAgentPostureStamina(Agent agent)
        {
            Stance stance = null;
            AgentStances.values.TryGetValue(agent, out stance);
            if (stance != null)
            {
                stance.posture = stance.maxPosture;
                stance.stamina = stance.maxStamina;
            }
        }

        public static void ResetPostureStaminaTraining(Agent trainerEasy, Agent trainerNormal)
        {
            ResetAgentPostureStamina(Agent.Main);
            ResetAgentPostureStamina(trainerEasy);
            ResetAgentPostureStamina(trainerNormal);
        }

        [HarmonyPatch(typeof(TrainingFieldMissionController))]
        [HarmonyPatch("OnTrainingAreaEnter")]
        private static class OnTrainingAreaEnterPatch
        {
            private static void Postfix(Agent ____advancedMeleeTrainerEasy, Agent ____advancedMeleeTrainerNormal)
            {
                ResetPostureStaminaTraining(____advancedMeleeTrainerEasy, ____advancedMeleeTrainerNormal);
            }
        }

        [HarmonyPatch(typeof(TrainingFieldMissionController))]
        [HarmonyPatch("OnEasyTrainerBeaten")]
        private static class OnEasyTrainerBeatenPatch
        {
            private static void Postfix(Agent ____advancedMeleeTrainerEasy, Agent ____advancedMeleeTrainerNormal)
            {
                ResetPostureStaminaTraining(____advancedMeleeTrainerEasy, ____advancedMeleeTrainerNormal);
            }
        }

        [HarmonyPatch(typeof(TrainingFieldMissionController))]
        [HarmonyPatch("MakeTrainersPatrolling")]
        private static class MakeTrainersPatrollingPatch
        {
            private static void Postfix(Agent ____advancedMeleeTrainerEasy, Agent ____advancedMeleeTrainerNormal)
            {
                ResetPostureStaminaTraining(____advancedMeleeTrainerEasy, ____advancedMeleeTrainerNormal);
            }
        }

        [HarmonyPatch(typeof(TrainingFieldMissionController))]
        [HarmonyPatch("BowTrainingEndedSuccessfully")]
        private static class BowTrainingEndedSuccessfullyPatch
        {
            private static void Postfix(int ____trainingProgress, TutorialArea ____activeTutorialArea, int ____trainingSubTypeIndex, Agent ____advancedMeleeTrainerEasy, Agent ____advancedMeleeTrainerNormal)
            {
                ResetPostureStaminaTraining(____advancedMeleeTrainerEasy, ____advancedMeleeTrainerNormal);
                EquipmentIndex ei = Mission.Current.MainAgent.GetPrimaryWieldedItemIndex();
                if (ei != EquipmentIndex.None)
                {
                    CharacterObject playerCharacter = (CharacterObject)CharacterObject.PlayerCharacter;
                    if (playerCharacter != null)
                    {
                        if (Mission.Current.MainAgent.WieldedWeapon.CurrentUsageItem != null)
                        {
                            WeaponComponentData wieldedWeapon = Mission.Current.MainAgent.WieldedWeapon.CurrentUsageItem;
                            SkillObject skillForWeapon = Campaign.Current.Models.CombatXpModel.GetSkillForWeapon(wieldedWeapon, false);
                            if (skillForWeapon != null)
                            {
                                playerCharacter.HeroObject.AddSkillXp(skillForWeapon, 500);
                                if (Mission.Current.MainAgent.HasMount)
                                {
                                    playerCharacter.HeroObject.AddSkillXp(DefaultSkills.Riding, 250);
                                }
                                else
                                {
                                    playerCharacter.HeroObject.AddSkillXp(DefaultSkills.Athletics, 250);
                                }
                            }
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(TrainingFieldMissionController))]
        [HarmonyPatch("MountedTrainingUpdate")]
        private static class MountedTrainingUpdatePatch
        {
            private static int lastBreakeableCount = -1;
            private static bool shouldCount = false;

            private static void Postfix(int ____trainingProgress, TutorialArea ____activeTutorialArea, int ____trainingSubTypeIndex)
            {
                if (____trainingProgress == 1)
                {
                    lastBreakeableCount = -1;
                }
                if (____trainingProgress == 4)
                {
                    if (lastBreakeableCount == -1)
                    {
                        lastBreakeableCount = ____activeTutorialArea.GetBrokenBreakableCount(____trainingSubTypeIndex);
                    }
                    else
                    {
                        if (lastBreakeableCount != ____activeTutorialArea.GetBrokenBreakableCount(____trainingSubTypeIndex))
                        {
                            lastBreakeableCount = ____activeTutorialArea.GetBrokenBreakableCount(____trainingSubTypeIndex);
                            shouldCount = true;
                        }
                    }
                }
                if (shouldCount && ____trainingProgress == 4)
                {
                    shouldCount = false;
                    EquipmentIndex ei = Mission.Current.MainAgent.GetPrimaryWieldedItemIndex();
                    if (ei != EquipmentIndex.None)
                    {
                        CharacterObject playerCharacter = (CharacterObject)CharacterObject.PlayerCharacter;
                        if (playerCharacter != null)
                        {
                            if (Mission.Current.MainAgent.WieldedWeapon.CurrentUsageItem != null)
                            {
                                WeaponComponentData wieldedWeapon = Mission.Current.MainAgent.WieldedWeapon.CurrentUsageItem;
                                SkillObject skillForWeapon = Campaign.Current.Models.CombatXpModel.GetSkillForWeapon(wieldedWeapon, false);
                                if (skillForWeapon != null)
                                {
                                    playerCharacter.HeroObject.AddSkillXp(skillForWeapon, 50);
                                    if (Mission.Current.MainAgent.HasMount)
                                    {
                                        playerCharacter.HeroObject.AddSkillXp(DefaultSkills.Riding, 25);
                                    }
                                    else
                                    {
                                        playerCharacter.HeroObject.AddSkillXp(DefaultSkills.Athletics, 25);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(TrainingFieldMissionController))]
        [HarmonyPatch("MountedTrainingEndedSuccessfully")]
        private static class MountedTrainingEndedSuccessfullyPatch
        {
            private static void Postfix(int ____trainingProgress, TutorialArea ____activeTutorialArea, int ____trainingSubTypeIndex, float ____timeScore, Agent ____advancedMeleeTrainerEasy, Agent ____advancedMeleeTrainerNormal)
            {
                ResetPostureStaminaTraining(____advancedMeleeTrainerEasy, ____advancedMeleeTrainerNormal);
                int brokenBreakableCount = ____activeTutorialArea.GetBrokenBreakableCount(____trainingSubTypeIndex);
                int breakablesCount = ____activeTutorialArea.GetBreakablesCount(____trainingSubTypeIndex);
                float missFactor = (float)brokenBreakableCount / (float)breakablesCount;
                if (missFactor >= 1f)
                {
                    missFactor = 1.25f;
                }
                float defaultTime = 80f;
                float timeFactor = defaultTime / ____timeScore;
                EquipmentIndex ei = Mission.Current.MainAgent.GetPrimaryWieldedItemIndex();
                if (ei != EquipmentIndex.None)
                {
                    CharacterObject playerCharacter = (CharacterObject)CharacterObject.PlayerCharacter;
                    if (playerCharacter != null)
                    {
                        if (Mission.Current.MainAgent.WieldedWeapon.CurrentUsageItem != null)
                        {
                            WeaponComponentData wieldedWeapon = Mission.Current.MainAgent.WieldedWeapon.CurrentUsageItem;
                            SkillObject skillForWeapon = Campaign.Current.Models.CombatXpModel.GetSkillForWeapon(wieldedWeapon, false);
                            if (skillForWeapon != null)
                            {
                                playerCharacter.HeroObject.AddSkillXp(skillForWeapon, 1000 * missFactor * timeFactor);
                                if (Mission.Current.MainAgent.HasMount)
                                {
                                    playerCharacter.HeroObject.AddSkillXp(DefaultSkills.Riding, 500 * missFactor * timeFactor);
                                }
                                else
                                {
                                    playerCharacter.HeroObject.AddSkillXp(DefaultSkills.Athletics, 500 * missFactor * timeFactor);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
