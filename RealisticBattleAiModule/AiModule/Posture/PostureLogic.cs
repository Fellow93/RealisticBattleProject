using HarmonyLib;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using TaleWorlds.CampaignSystem.TournamentGames;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ItemObject;
using static TaleWorlds.MountAndBlade.Agent;

namespace RBMAI
{
    public class PostureLogic : MissionLogic
    {
        //private static int tickCooldownReset = 30;
        //private static int tickCooldown = 0;
        private static float timeToCalc = 0.5f;

        private static float currentDt = 0f;
        private static int postureEffectCheck = 0;
        private static int postureEffectCheckCooldown = 15;

        private static float weaponLengthPostureFactor = 0.2f;
        private static float weaponWeightPostureFactor = 0.5f;
        private static float relativeSpeedPostureFactor = 0.6f;
        private static float lwrResultModifier = 3f;

        //private static float maxAcc = 1.5f;
        //private static float minAcc = 0.1f;
        //private static float curAcc = 1f;
        //private static bool isCountingUp = false;

        public static MBArrayList<Agent> agentsToDropShield = new MBArrayList<Agent> { };
        public static MBArrayList<Agent> agentsToDropWeapon = new MBArrayList<Agent> { };
        public static Dictionary<Agent, FormationClass> agentsToChangeFormation = new Dictionary<Agent, FormationClass> { };

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("EquipItemsFromSpawnEquipment")]
        private class EquipItemsFromSpawnEquipmentPatch
        {
            private static void Prefix(ref Agent __instance)
            {
                if (__instance.IsHuman)
                {
                    AgentPostures.values[__instance] = new Posture();
                }
            }
        }

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("OnWieldedItemIndexChange")]
        private class OnWieldedItemIndexChangePatch
        {
            private static void Postfix(ref Agent __instance, bool isOffHand, bool isWieldedInstantly, bool isWieldedOnSpawn)
            {
                float playerPostureModifier = RBMConfig.RBMConfig.playerPostureMultiplier;
                if (RBMConfig.RBMConfig.postureEnabled)
                {
                    Posture posture = null;
                    AgentPostures.values.TryGetValue(__instance, out posture);
                    if (posture == null)
                    {
                        AgentPostures.values[__instance] = new Posture();
                    }
                    AgentPostures.values.TryGetValue(__instance, out posture);
                    if (posture != null)
                    {
                        float oldPosture = posture.posture;
                        float oldMaxPosture = posture.maxPosture;
                        float oldPosturePercentage = oldPosture / oldMaxPosture;

                        int usageIndex = 0;
                        EquipmentIndex slotIndex = __instance.GetPrimaryWieldedItemIndex();
                        if (slotIndex != EquipmentIndex.None)
                        {
                            usageIndex = __instance.Equipment[slotIndex].CurrentUsageIndex;

                            WeaponComponentData wcd = __instance.Equipment[slotIndex].GetWeaponComponentDataForUsage(usageIndex);
                            SkillObject weaponSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(wcd.WeaponClass);
                            int effectiveWeaponSkill = 0;
                            if (weaponSkill != null)
                            {
                                effectiveWeaponSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(__instance, weaponSkill);
                            }

                            float athleticBase = 20f;
                            float weaponSkillBase = 80f;
                            float strengthSkillModifier = 500f;
                            float weaponSkillModifier = 500f;
                            float athleticRegenBase = 0.008f;
                            float weaponSkillRegenBase = 0.032f;
                            float baseModifier = 1f;

                            if (__instance.HasMount)
                            {
                                int effectiveRidingSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(__instance, DefaultSkills.Riding);
                                posture.maxPosture = (athleticBase * (baseModifier + (effectiveRidingSkill / strengthSkillModifier))) + (weaponSkillBase * (baseModifier + (effectiveWeaponSkill / weaponSkillModifier)));
                                posture.regenPerTick = (athleticRegenBase * (baseModifier + (effectiveRidingSkill / strengthSkillModifier))) + (weaponSkillRegenBase * (baseModifier + (effectiveWeaponSkill / weaponSkillModifier)));
                            }
                            else
                            {
                                int effectiveAthleticSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(__instance, DefaultSkills.Athletics);
                                posture.maxPosture = (athleticBase * (baseModifier + (effectiveAthleticSkill / strengthSkillModifier))) + (weaponSkillBase * (baseModifier + (effectiveWeaponSkill / weaponSkillModifier)));
                                posture.regenPerTick = (athleticRegenBase * (baseModifier + (effectiveAthleticSkill / strengthSkillModifier))) + (weaponSkillRegenBase * (baseModifier + (effectiveWeaponSkill / weaponSkillModifier)));
                            }

                            if (__instance.IsPlayerControlled)
                            {
                                posture.maxPosture *= playerPostureModifier;
                                posture.regenPerTick *= playerPostureModifier;
                            }

                            posture.posture = posture.maxPosture * oldPosturePercentage;
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(MissionState))]
        [HarmonyPatch("LoadMission")]
        public class LoadMissionPatch
        {
            private static void Postfix()
            {
                AgentPostures.values.Clear();
                agentsToDropShield.Clear();
                agentsToDropWeapon.Clear();
                agentsToChangeFormation.Clear();
            }
        }

        [HarmonyPatch(typeof(MissionState))]
        [HarmonyPatch("OnDeactivate")]
        public class OnDeactivatePatch
        {
            private static void Postfix()
            {
                AgentPostures.values.Clear();
                agentsToDropShield.Clear();
                agentsToDropWeapon.Clear();
                agentsToChangeFormation.Clear();
            }
        }

        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("CreateMeleeBlow")]
        private class CreateMeleeBlowPatch
        {
            public static void ResetPostureForAgent(ref Posture posture, float postureResetModifier, Agent agent)
            {
                if(posture != null)
                {
                    //if(agent != null && posture.maxPostureLossCount >= 1)
                    //{
                    //    agent.AgentDrivenProperties.WeaponInaccuracy *= posture.maxPostureLossCount * 1.1f;
                    //}
                    float currentTime = Mission.Current.CurrentTime;
                    int restCount = posture.lastPostureLossTime > 0 ? MathF.Floor((currentTime - posture.lastPostureLossTime) / 20f) : 0;
                    posture.maxPostureLossCount = posture.maxPostureLossCount - restCount;
                    if (posture.maxPostureLossCount < 10)
                    {
                        posture.maxPostureLossCount += 1;
                    }
                    posture.posture = posture.maxPosture * (postureResetModifier * (1f - (0.05f * posture.maxPostureLossCount)));
                    posture.lastPostureLossTime = Mission.Current.CurrentTime;
                }
            }

            private static void Postfix(ref Mission __instance, ref Blow __result, Agent attackerAgent, Agent victimAgent, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, CrushThroughState crushThroughState, Vec3 blowDirection, Vec3 swingDirection, bool cancelDamage)
            {
                if ((new StackTrace()).GetFrame(3).GetMethod().Name.Contains("MeleeHit") && victimAgent != null && victimAgent.IsHuman)
                {
                    if (RBMConfig.RBMConfig.postureEnabled && attackerAgent != null && victimAgent != null && !attackerAgent.IsFriendOf(victimAgent) && attackerWeapon.CurrentUsageItem != null &&
                        attackerWeapon.CurrentUsageItem != null)
                    {
                        Posture defenderPosture = null;
                        Posture attackerPosture = null;
                        AgentPostures.values.TryGetValue(victimAgent, out defenderPosture);
                        AgentPostures.values.TryGetValue(attackerAgent, out attackerPosture);

                        float postureResetModifier = 0.75f;
                        float shieldPostureResetModifier = 1f;

                        float absoluteDamageModifier = 3f;
                        float absoluteShieldDamageModifier = 1.2f;

                        bool isRunningForward = false;
                        float currentSpeed = attackerAgent.GetCurrentVelocity().Length;
                        if (currentSpeed >= attackerAgent.WalkSpeedCached)
                        {
                            isRunningForward = true;
                        }

                        float comHitModifier = Utilities.GetComHitModifier(in collisionData, in attackerWeapon);
                        if (!collisionData.AttackBlockedWithShield)
                        {
                            if (collisionData.CollisionResult == CombatCollisionResult.Blocked)
                            {
                                if (defenderPosture != null)
                                {
                                    float postureDmg = calculateDefenderPostureDamage(victimAgent, attackerAgent, absoluteDamageModifier, 0.85f, ref collisionData, attackerWeapon, comHitModifier);
                                    defenderPosture.posture = defenderPosture.posture - postureDmg;
                                    addPosturedamageVisual(attackerAgent, victimAgent);
                                    if (defenderPosture.posture <= 0f)
                                    {
                                        float healthDamage = calculateHealthDamage(attackerWeapon, attackerAgent, victimAgent, postureDmg, __result, victimAgent);
                                        EquipmentIndex wieldedItemIndex = victimAgent.GetPrimaryWieldedItemIndex();
                                        if (wieldedItemIndex != EquipmentIndex.None)
                                        {
                                            MBTextManager.SetTextVariable("DMG", MathF.Floor(healthDamage));
                                            if (victimAgent.IsPlayerControlled)
                                            {
                                                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_009}Posture break: Posture depleted, {DMG} damage crushed through").ToString(), Color.FromUint(4282569842u)));
                                            }
                                            if (attackerAgent.IsPlayerControlled)
                                            {
                                                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_010}Enemy Posture break: Posture depleted, {DMG} damage crushed through").ToString(), Color.FromUint(4282569842u)));
                                            }
                                            if (!victimAgent.HasMount)
                                            {
                                                if (postureDmg >= defenderPosture.maxPosture * 0.33f)
                                                {
                                                    makePostureCrashThroughBlow(ref __instance, __result, attackerAgent, victimAgent, MathF.Floor(healthDamage), ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                                    agentsToDropWeapon.Add(victimAgent);
                                                    if (!agentsToDropWeapon.Contains(victimAgent))
                                                    {
                                                        agentsToDropWeapon.Add(victimAgent);
                                                    }
                                                }
                                                else
                                                {
                                                    makePostureCrashThroughBlow(ref __instance, __result, attackerAgent, victimAgent, MathF.Floor(healthDamage), ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                                }
                                            }
                                            else
                                            {
                                                makePostureCrashThroughBlow(ref __instance, __result, attackerAgent, victimAgent, MathF.Floor(healthDamage), ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.CanDismount);
                                            }
                                        }
                                        ResetPostureForAgent(ref defenderPosture, postureResetModifier, victimAgent);
                                        addPosturedamageVisual(attackerAgent, victimAgent);
                                    }
                                    // //defenderPosture.lastPostureLossTime = Mission.Current.CurrentTime;
                                }
                                if (attackerPosture != null)
                                {
                                    float attackerPostureDmg = calculateAttackerPostureDamage(victimAgent, attackerAgent, absoluteDamageModifier, 0.25f, ref collisionData, attackerWeapon, comHitModifier);
                                    attackerPosture.posture = attackerPosture.posture - attackerPostureDmg;
                                    if (attackerPosture.posture <= 0f)
                                    {
                                        if (attackerPostureDmg >= attackerPosture.maxPosture * 0.33f)
                                        {
                                            makePostureRiposteBlow(ref __instance, __result, attackerAgent, victimAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                            agentsToDropWeapon.Add(attackerAgent);
                                            if (!agentsToDropWeapon.Contains(attackerAgent))
                                            {
                                                agentsToDropWeapon.Add(attackerAgent);
                                            }
                                            ResetPostureForAgent(ref attackerPosture, postureResetModifier, attackerAgent);
                                            addPosturedamageVisual(attackerAgent, victimAgent);
                                        }
                                        else
                                        {
                                            attackerPosture.posture = 0f;
                                        }
                                    }
                                    //attackerPosture.lastPostureLossTime = Mission.Current.CurrentTime;
                                    addPosturedamageVisual(attackerAgent, victimAgent);
                                }
                            }
                            else if (collisionData.CollisionResult == CombatCollisionResult.Parried)
                            {
                                if (defenderPosture != null)
                                {
                                    float postureDmg = calculateDefenderPostureDamage(victimAgent, attackerAgent, absoluteDamageModifier, 0.5f, ref collisionData, attackerWeapon, comHitModifier);
                                    defenderPosture.posture = defenderPosture.posture - postureDmg;
                                    addPosturedamageVisual(attackerAgent, victimAgent);
                                    if (defenderPosture.posture <= 0f)
                                    {
                                        float healthDamage = calculateHealthDamage(attackerWeapon, attackerAgent, victimAgent, postureDmg, __result, victimAgent);
                                        EquipmentIndex wieldedItemIndex = victimAgent.GetPrimaryWieldedItemIndex();
                                        if (wieldedItemIndex != EquipmentIndex.None)
                                        {
                                            MBTextManager.SetTextVariable("DMG", MathF.Floor(healthDamage));
                                            if (victimAgent.IsPlayerControlled)
                                            {
                                                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_011}Posture break: Posture depleted, perfect parry, {DMG} damage crushed through").ToString(), Color.FromUint(4282569842u)));
                                            }
                                            if (attackerAgent.IsPlayerControlled)
                                            {
                                                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_012}Enemy Posture break: Posture depleted, perfect parry, {DMG} damage crushed through").ToString(), Color.FromUint(4282569842u)));
                                            }
                                            if (postureDmg >= defenderPosture.maxPosture * 0.33f)
                                            {
                                                makePostureCrashThroughBlow(ref __instance, __result, attackerAgent, victimAgent, MathF.Floor(healthDamage), ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                                agentsToDropWeapon.Add(victimAgent);
                                                if (!agentsToDropWeapon.Contains(victimAgent))
                                                {
                                                    agentsToDropWeapon.Add(victimAgent);
                                                }
                                            }
                                            else
                                            {
                                                makePostureCrashThroughBlow(ref __instance, __result, attackerAgent, victimAgent, MathF.Floor(healthDamage), ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                            }
                                        }
                                        ResetPostureForAgent(ref defenderPosture, postureResetModifier, victimAgent);
                                        addPosturedamageVisual(attackerAgent, victimAgent);
                                    }
                                     //defenderPosture.lastPostureLossTime = Mission.Current.CurrentTime;
                                }
                                if (attackerPosture != null)
                                {
                                    float attackerPostureDmg = calculateAttackerPostureDamage(victimAgent, attackerAgent, absoluteDamageModifier, 0.75f, ref collisionData, attackerWeapon, comHitModifier);
                                    attackerPosture.posture = attackerPosture.posture - attackerPostureDmg;
                                    addPosturedamageVisual(attackerAgent, victimAgent);
                                    if (attackerPosture.posture <= 0f)
                                    {
                                        if (attackerAgent.IsPlayerControlled)
                                        {
                                            InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_013}Posture break: Posture depleted, perfect parry").ToString(), Color.FromUint(4282569842u)));
                                        }
                                        if (!attackerAgent.HasMount)
                                        {
                                            if (attackerPostureDmg >= attackerPosture.maxPosture * 0.33f)
                                            {
                                                makePostureRiposteBlow(ref __instance, __result, attackerAgent, victimAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                                agentsToDropWeapon.Add(attackerAgent);
                                                if (!agentsToDropWeapon.Contains(attackerAgent))
                                                {
                                                    agentsToDropWeapon.Add(attackerAgent);
                                                }
                                            }
                                            else
                                            {
                                                makePostureRiposteBlow(ref __instance, __result, attackerAgent, victimAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                            }
                                        }
                                        else
                                        {
                                            makePostureRiposteBlow(ref __instance, __result, attackerAgent, victimAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.CanDismount);
                                        }
                                        ResetPostureForAgent(ref attackerPosture, postureResetModifier, attackerAgent);
                                        addPosturedamageVisual(attackerAgent, victimAgent);
                                    }
                                    //attackerPosture.lastPostureLossTime = Mission.Current.CurrentTime;
                                }
                            }
                            else if (victimAgent.IsHuman && attackerAgent.IsHuman && collisionData.CollisionResult == CombatCollisionResult.StrikeAgent)
                            {
                                if (defenderPosture != null)
                                {
                                    float postureDmg;
                                    postureDmg = calculateDefenderPostureDamage(victimAgent, attackerAgent, absoluteDamageModifier, 0.8f, ref collisionData, attackerWeapon, comHitModifier);
                                    defenderPosture.posture = defenderPosture.posture - postureDmg;
                                    addPosturedamageVisual(attackerAgent, victimAgent);
                                    if (defenderPosture.posture <= 0f)
                                    {
                                        EquipmentIndex wieldedItemIndex = victimAgent.GetPrimaryWieldedItemIndex();
                                        if (wieldedItemIndex != EquipmentIndex.None)
                                        {
                                            if (!victimAgent.HasMount)
                                            {
                                                if (postureDmg >= defenderPosture.maxPosture * 0.33f && !attackerWeapon.IsEmpty && (
                                                    attackerWeapon.CurrentUsageItem.WeaponClass == WeaponClass.TwoHandedAxe ||
                                                    attackerWeapon.CurrentUsageItem.WeaponClass == WeaponClass.TwoHandedMace ||
                                                    attackerWeapon.CurrentUsageItem.WeaponClass == WeaponClass.TwoHandedPolearm ||
                                                    attackerWeapon.CurrentUsageItem.WeaponClass == WeaponClass.TwoHandedSword))
                                                {
                                                    makePostureBlow(ref __instance, __result, attackerAgent, victimAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockDown);
                                                }
                                                else
                                                {
                                                    makePostureBlow(ref __instance, __result, attackerAgent, victimAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                                }
                                            }
                                            else
                                            {
                                                makePostureBlow(ref __instance, __result, attackerAgent, victimAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.CanDismount);
                                            }
                                        }
                                        ResetPostureForAgent(ref defenderPosture, postureResetModifier, victimAgent);
                                        addPosturedamageVisual(attackerAgent, victimAgent);
                                    }
                                     //defenderPosture.lastPostureLossTime = Mission.Current.CurrentTime;
                                }
                                if (attackerPosture != null)
                                {
                                    attackerPosture.posture = attackerPosture.posture - calculateAttackerPostureDamage(victimAgent, attackerAgent, absoluteDamageModifier, 0.5f, ref collisionData, attackerWeapon, comHitModifier);
                                    addPosturedamageVisual(attackerAgent, victimAgent);
                                    if (attackerPosture.posture <= 0f)
                                    {
                                        attackerPosture.posture = 0f;
                                    }
                                }
                            }
                        }
                        else if (collisionData.AttackBlockedWithShield)
                        {
                            if (collisionData.CollisionResult == CombatCollisionResult.Blocked && !collisionData.CorrectSideShieldBlock)
                            {
                                if (defenderPosture != null)
                                {
                                    float postureDmg = calculateDefenderPostureDamage(victimAgent, attackerAgent, absoluteShieldDamageModifier, 1f, ref collisionData, attackerWeapon, comHitModifier);
                                    defenderPosture.posture = defenderPosture.posture - postureDmg;
                                    addPosturedamageVisual(attackerAgent, victimAgent);
                                    float healthDamage = calculateHealthDamage(attackerWeapon, attackerAgent, victimAgent, postureDmg, __result, victimAgent);
                                    if (defenderPosture.posture <= 0f)
                                    {
                                        damageShield(victimAgent, 150);
                                        EquipmentIndex wieldedItemIndex = victimAgent.GetPrimaryWieldedItemIndex();
                                        if (wieldedItemIndex != EquipmentIndex.None)
                                        {
                                            if (victimAgent.IsPlayerControlled)
                                            {
                                                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_014}Posture break: Posture depleted, incorrect side block").ToString(), Color.FromUint(4282569842u)));
                                            }
                                            if (!victimAgent.HasMount)
                                            {
                                                if (postureDmg >= defenderPosture.maxPosture * 0.33f)
                                                {
                                                    makePostureCrashThroughBlow(ref __instance, __result, attackerAgent, victimAgent, MathF.Floor(healthDamage), ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                                    makePostureBlow(ref __instance, __result, attackerAgent, victimAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                                    if (!agentsToDropShield.Contains(victimAgent))
                                                    {
                                                        agentsToDropShield.Add(victimAgent);
                                                    }
                                                }
                                                else
                                                {
                                                    makePostureBlow(ref __instance, __result, attackerAgent, victimAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                                }
                                            }
                                            else
                                            {
                                                makePostureBlow(ref __instance, __result, attackerAgent, victimAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.CanDismount);

                                            }
                                        }
                                        ResetPostureForAgent(ref defenderPosture, shieldPostureResetModifier, victimAgent);
                                        addPosturedamageVisual(attackerAgent, victimAgent);
                                    }
                                     //defenderPosture.lastPostureLossTime = Mission.Current.CurrentTime;
                                }
                                if (attackerPosture != null)
                                {
                                    attackerPosture.posture = attackerPosture.posture - calculateAttackerPostureDamage(victimAgent, attackerAgent, absoluteShieldDamageModifier, 0.2f, ref collisionData, attackerWeapon, comHitModifier);
                                    addPosturedamageVisual(attackerAgent, victimAgent);
                                    if (attackerPosture.posture <= 0f)
                                    {
                                        attackerPosture.posture = 0f;
                                    }
                                }
                            }
                            else if ((collisionData.CollisionResult == CombatCollisionResult.Blocked && collisionData.CorrectSideShieldBlock) || (collisionData.CollisionResult == CombatCollisionResult.Parried && !collisionData.CorrectSideShieldBlock))
                            {
                                if (defenderPosture != null)
                                {
                                    float postureDmg = calculateDefenderPostureDamage(victimAgent, attackerAgent, absoluteShieldDamageModifier, 1f, ref collisionData, attackerWeapon, comHitModifier);
                                    defenderPosture.posture = defenderPosture.posture - postureDmg;
                                    addPosturedamageVisual(attackerAgent, victimAgent);
                                    if (defenderPosture.posture <= 0f)
                                    {
                                        damageShield(victimAgent, 125);
                                        EquipmentIndex wieldedItemIndex = victimAgent.GetPrimaryWieldedItemIndex();
                                        if (wieldedItemIndex != EquipmentIndex.None)
                                        {
                                            if (victimAgent.IsPlayerControlled)
                                            {
                                                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_015}Posture break: Posture depleted, correct side block").ToString(), Color.FromUint(4282569842u)));
                                            }
                                            if (!victimAgent.HasMount)
                                            {
                                                if (postureDmg >= defenderPosture.maxPosture * 0.33f)
                                                {
                                                    makePostureBlow(ref __instance, __result, attackerAgent, victimAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                                    if (!agentsToDropShield.Contains(victimAgent))
                                                    {
                                                        agentsToDropShield.Add(victimAgent);
                                                    }
                                                }
                                                else
                                                {
                                                    makePostureBlow(ref __instance, __result, attackerAgent, victimAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                                }
                                            }
                                            else
                                            {
                                                makePostureBlow(ref __instance, __result, attackerAgent, victimAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.CanDismount);
                                            }
                                        }
                                        ResetPostureForAgent(ref defenderPosture, shieldPostureResetModifier, victimAgent);
                                        addPosturedamageVisual(attackerAgent, victimAgent);
                                    }
                                     //defenderPosture.lastPostureLossTime = Mission.Current.CurrentTime;
                                }
                                if (attackerPosture != null)
                                {
                                    attackerPosture.posture = attackerPosture.posture - calculateAttackerPostureDamage(victimAgent, attackerAgent, absoluteShieldDamageModifier, 0.3f, ref collisionData, attackerWeapon, comHitModifier);
                                    addPosturedamageVisual(attackerAgent, victimAgent);
                                    if (attackerPosture.posture <= 0f)
                                    {
                                        attackerPosture.posture = 0f;
                                    }
                                }
                            }
                            else if (collisionData.CollisionResult == CombatCollisionResult.Parried && collisionData.CorrectSideShieldBlock)
                            {
                                if (defenderPosture != null)
                                {
                                    float postureDmg = calculateDefenderPostureDamage(victimAgent, attackerAgent, absoluteShieldDamageModifier, 0.8f, ref collisionData, attackerWeapon, comHitModifier);
                                    defenderPosture.posture = defenderPosture.posture - postureDmg;
                                    addPosturedamageVisual(attackerAgent, victimAgent);
                                    if (defenderPosture.posture <= 0f)
                                    {
                                        damageShield(victimAgent, 100);
                                        EquipmentIndex wieldedItemIndex = victimAgent.GetPrimaryWieldedItemIndex();
                                        if (wieldedItemIndex != EquipmentIndex.None)
                                        {
                                            if (victimAgent.IsPlayerControlled)
                                            {
                                                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_016}Posture break: Posture depleted, perfect parry, correct side block").ToString(), Color.FromUint(4282569842u)));
                                            }
                                            if (!victimAgent.HasMount)
                                            {
                                                if (postureDmg >= defenderPosture.maxPosture * 0.33f)
                                                {
                                                    makePostureBlow(ref __instance, __result, attackerAgent, victimAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                                    if (!agentsToDropShield.Contains(victimAgent))
                                                    {
                                                        agentsToDropShield.Add(victimAgent);
                                                    }
                                                }
                                                else
                                                {
                                                    makePostureBlow(ref __instance, __result, attackerAgent, victimAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                                }
                                            }
                                            else
                                            {
                                                makePostureBlow(ref __instance, __result, attackerAgent, victimAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                            }
                                        }
                                        ResetPostureForAgent(ref defenderPosture, shieldPostureResetModifier, victimAgent);
                                        addPosturedamageVisual(attackerAgent, victimAgent);
                                    }
                                     //defenderPosture.lastPostureLossTime = Mission.Current.CurrentTime;
                                }
                                if (attackerPosture != null)
                                {
                                    float attackerPostureDmg = calculateAttackerPostureDamage(victimAgent, attackerAgent, absoluteShieldDamageModifier, 0.5f, ref collisionData, attackerWeapon, comHitModifier);
                                    attackerPosture.posture = attackerPosture.posture - attackerPostureDmg;
                                    addPosturedamageVisual(attackerAgent, victimAgent);
                                    if (attackerPosture.posture <= 0f)
                                    {
                                        if (attackerAgent.IsPlayerControlled)
                                        {
                                            InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_017}Posture break: Posture depleted, perfect parry, correct side block").ToString(), Color.FromUint(4282569842u)));
                                        }

                                        {
                                            if (attackerPostureDmg >= attackerPosture.maxPosture * 0.33f)
                                            {
                                                makePostureRiposteBlow(ref __instance, __result, attackerAgent, victimAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                                agentsToDropWeapon.Add(attackerAgent);
                                                if (!agentsToDropWeapon.Contains(attackerAgent))
                                                {
                                                    agentsToDropWeapon.Add(attackerAgent);
                                                }
                                                ResetPostureForAgent(ref attackerPosture, postureResetModifier, attackerAgent);
                                                addPosturedamageVisual(attackerAgent, victimAgent);
                                            }
                                            else
                                            {
                                                makePostureRiposteBlow(ref __instance, __result, attackerAgent, victimAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                            }
                                        }
                                        ResetPostureForAgent(ref attackerPosture, postureResetModifier, attackerAgent);
                                        addPosturedamageVisual(attackerAgent, victimAgent);
                                    }
                                    //attackerPosture.lastPostureLossTime = Mission.Current.CurrentTime;
                                }
                            }
                        }
                        else if (collisionData.CollisionResult == CombatCollisionResult.ChamberBlocked)
                        {
                            if (defenderPosture != null)
                            {
                                defenderPosture.posture = defenderPosture.posture - calculateDefenderPostureDamage(victimAgent, attackerAgent, absoluteDamageModifier, 0.25f, ref collisionData, attackerWeapon, comHitModifier);
                                addPosturedamageVisual(attackerAgent, victimAgent);
                                if (defenderPosture.posture <= 0f)
                                {
                                    EquipmentIndex wieldedItemIndex = victimAgent.GetPrimaryWieldedItemIndex();
                                    if (wieldedItemIndex != EquipmentIndex.None)
                                    {
                                        if (victimAgent.IsPlayerControlled)
                                        {
                                            InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_018}Posture break: Posture depleted, chamber block").ToString(), Color.FromUint(4282569842u)));
                                        }
                                        makePostureBlow(ref __instance, __result, attackerAgent, victimAgent, ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.NonTipThrust);
                                    }
                                    ResetPostureForAgent(ref defenderPosture, postureResetModifier, victimAgent);
                                    addPosturedamageVisual(attackerAgent, victimAgent);
                                }
                                 //defenderPosture.lastPostureLossTime = Mission.Current.CurrentTime;
                            }
                            if (attackerPosture != null)
                            {
                                float postureDmg = calculateAttackerPostureDamage(victimAgent, attackerAgent, absoluteDamageModifier, 2f, ref collisionData, attackerWeapon, comHitModifier);
                                attackerPosture.posture = attackerPosture.posture - postureDmg;
                                addPosturedamageVisual(attackerAgent, victimAgent);
                                if (attackerPosture.posture <= 0f)
                                {
                                    float healthDamage = calculateHealthDamage(attackerWeapon, attackerAgent, victimAgent, postureDmg, __result, attackerAgent);
                                    if (attackerAgent.IsPlayerControlled)
                                    {
                                        MBTextManager.SetTextVariable("DMG", MathF.Floor(healthDamage));
                                        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_019}Posture break: Posture depleted, chamber block {DMG} damage crushed through").ToString(), Color.FromUint(4282569842u)));
                                    }
                                    makePostureCrashThroughBlow(ref __instance, __result, attackerAgent, victimAgent, MathF.Floor(healthDamage), ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockDown);
                                    ResetPostureForAgent(ref attackerPosture, postureResetModifier, attackerAgent);
                                    addPosturedamageVisual(attackerAgent, victimAgent);
                                }
                                else
                                {
                                    float healthDamage = calculateHealthDamage(attackerWeapon, attackerAgent, victimAgent, postureDmg, __result, attackerAgent);
                                    if (attackerAgent.IsPlayerControlled)
                                    {
                                        MBTextManager.SetTextVariable("DMG", MathF.Floor(healthDamage));
                                        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_020}Chamber block {DMG} damage crushed through").ToString(), Color.FromUint(4282569842u)));
                                    }
                                    makePostureCrashThroughBlow(ref __instance, __result, attackerAgent, victimAgent, MathF.Floor(healthDamage), ref collisionData, attackerWeapon, crushThroughState, blowDirection, swingDirection, cancelDamage, BlowFlags.KnockBack);
                                    ResetPostureForAgent(ref attackerPosture, postureResetModifier, attackerAgent);
                                    addPosturedamageVisual(attackerAgent, victimAgent);
                                }
                                //attackerPosture.lastPostureLossTime = Mission.Current.CurrentTime;
                            }
                        }
                    }
                }
            }

            private static void damageShield(Agent victim, int ammount)
            {
                for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                {
                    if (victim.Equipment != null && !victim.Equipment[equipmentIndex].IsEmpty)
                    {
                        if (victim.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Shield && !victim.WieldedOffhandWeapon.IsEmpty && victim.WieldedOffhandWeapon.Item.Id == victim.Equipment[equipmentIndex].Item.Id)
                        {
                            int num = MathF.Max(0, victim.Equipment[equipmentIndex].HitPoints - ammount);
                            victim.ChangeWeaponHitPoints(equipmentIndex, (short)num);
                            break;
                        }
                    }
                }
            }

            private static void addPosturedamageVisual(Agent attackerAgent, Agent victimAgent)
            {
                if (RBMConfig.RBMConfig.postureEnabled)
                {
                    if (victimAgent != null && attackerAgent != null && (victimAgent.IsPlayerControlled || attackerAgent.IsPlayerControlled))
                    {
                        Agent enemyAgent = null;
                        if (victimAgent.IsPlayerControlled)
                        {
                            enemyAgent = attackerAgent;
                            Posture posture = null;
                            if (AgentPostures.values.TryGetValue(victimAgent, out posture))
                            {
                                if (AgentPostures.postureVisual != null && AgentPostures.postureVisual._dataSource.ShowPlayerPostureStatus)
                                {
                                    AgentPostures.postureVisual._dataSource.PlayerPosture = (int)posture.posture;
                                    AgentPostures.postureVisual._dataSource.PlayerPostureMax = (int)posture.maxPosture;
                                    AgentPostures.postureVisual._dataSource.PlayerPostureText = ((int)posture.posture).ToString();
                                    AgentPostures.postureVisual._dataSource.PlayerPostureMaxText = ((int)posture.maxPosture).ToString();
                                }
                            }
                        }
                        else
                        {
                            enemyAgent = victimAgent;
                            Posture posture = null;
                            if (AgentPostures.values.TryGetValue(attackerAgent, out posture))
                            {
                                if (AgentPostures.postureVisual != null && AgentPostures.postureVisual._dataSource.ShowPlayerPostureStatus)
                                {
                                    AgentPostures.postureVisual._dataSource.PlayerPosture = (int)posture.posture;
                                    AgentPostures.postureVisual._dataSource.PlayerPostureMax = (int)posture.maxPosture;
                                    AgentPostures.postureVisual._dataSource.PlayerPostureText = ((int)posture.posture).ToString();
                                    AgentPostures.postureVisual._dataSource.PlayerPostureMaxText = ((int)posture.maxPosture).ToString();
                                }
                            }
                        }
                        if (AgentPostures.postureVisual != null)
                        {
                            Posture posture = null;
                            if (AgentPostures.values.TryGetValue(enemyAgent, out posture))
                            {
                                AgentPostures.postureVisual._dataSource.ShowEnemyStatus = true;
                                AgentPostures.postureVisual.affectedAgent = enemyAgent;
                                if (AgentPostures.postureVisual._dataSource.ShowEnemyStatus && AgentPostures.postureVisual.affectedAgent == enemyAgent)
                                {
                                    AgentPostures.postureVisual.timer = AgentPostures.postureVisual.DisplayTime;
                                    AgentPostures.postureVisual._dataSource.EnemyPosture = (int)posture.posture;
                                    AgentPostures.postureVisual._dataSource.EnemyPostureMax = (int)posture.maxPosture;
                                    AgentPostures.postureVisual._dataSource.EnemyHealth = (int)enemyAgent.Health;
                                    AgentPostures.postureVisual._dataSource.EnemyHealthMax = (int)enemyAgent.HealthLimit;
                                    if (enemyAgent.IsMount)
                                    {
                                        AgentPostures.postureVisual._dataSource.EnemyName = enemyAgent.RiderAgent?.Name + " (" + new TextObject("{=mountnoun}Mount").ToString() + ")";
                                    }
                                    else
                                    {
                                        AgentPostures.postureVisual._dataSource.EnemyName = enemyAgent.Name;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            public static float CalculateSweetSpotSwingMagnitude(BasicCharacterObject character, MissionWeapon weapon, int weaponUsageIndex, int relevantSkill)
            {
                float progressEffect = 1f;
                float sweetSpotMagnitude = -1f;

                if (weapon.Item != null && weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex) != null)
                {
                    float swingSpeed = (float)weapon.GetModifiedSwingSpeedForCurrentUsage() / 4.5454545f * progressEffect;

                    int ef = relevantSkill;
                    float effectiveSkillDR = Utilities.GetEffectiveSkillWithDR(ef);
                    switch (weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).WeaponClass)
                    {
                        case WeaponClass.LowGripPolearm:
                        case WeaponClass.Mace:
                        case WeaponClass.OneHandedAxe:
                        case WeaponClass.OneHandedPolearm:
                        case WeaponClass.TwoHandedMace:
                            {
                                float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                                swingSpeed = swingSpeed * 0.83f * swingskillModifier * progressEffect;
                                break;
                            }
                        case WeaponClass.TwoHandedPolearm:
                            {
                                float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                                swingSpeed = swingSpeed * 0.83f * swingskillModifier * progressEffect;
                                break;
                            }
                        case WeaponClass.TwoHandedAxe:
                            {
                                float swingskillModifier = 1f + (effectiveSkillDR / 800f);

                                swingSpeed = swingSpeed * 0.75f * swingskillModifier * progressEffect;
                                break;
                            }
                        case WeaponClass.OneHandedSword:
                        case WeaponClass.Dagger:
                        case WeaponClass.TwoHandedSword:
                            {
                                float swingskillModifier = 1f + (effectiveSkillDR / 800f);

                                swingSpeed = swingSpeed * 0.83f * swingskillModifier * progressEffect;
                                break;
                            }
                    }
                    float weaponWeight = weapon.Item.Weight;
                    float weaponInertia = weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).TotalInertia;
                    float weaponCOM = weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass;
                    for (float currentSpot = 1f; currentSpot > 0.35f; currentSpot -= 0.01f)
                    {
                        float currentSpotMagnitude = CombatStatCalculator.CalculateStrikeMagnitudeForSwing(swingSpeed, currentSpot, weaponWeight,
                            weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).GetRealWeaponLength(), weaponInertia, weaponCOM, 0f);
                        if (currentSpotMagnitude > sweetSpotMagnitude)
                        {
                            sweetSpotMagnitude = currentSpotMagnitude;
                        }
                    }
                }
                return sweetSpotMagnitude;
            }

            public static float CalculateThrustMagnitude(BasicCharacterObject character, MissionWeapon weapon, int weaponUsageIndex, int relevantSkill)
            {
                float progressEffect = 1f;
                float thrustMagnitude = -1f;

                if (weapon.Item != null && weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex) != null)
                {
                    float thrustWeaponSpeed = (float)weapon.GetModifiedThrustSpeedForCurrentUsage() / 11.7647057f * progressEffect;

                    int ef = relevantSkill;
                    float effectiveSkillDR = Utilities.GetEffectiveSkillWithDR(ef);

                    float weaponWeight = weapon.Item.Weight;
                    float weaponInertia = weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).TotalInertia;
                    float weaponCOM = weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass;

                    switch (weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).WeaponClass)
                    {
                        case WeaponClass.LowGripPolearm:
                        case WeaponClass.Mace:
                        case WeaponClass.OneHandedAxe:
                        case WeaponClass.OneHandedPolearm:
                        case WeaponClass.TwoHandedMace:
                            {
                                float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);

                                thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weaponWeight, weaponInertia, weaponCOM);
                                thrustWeaponSpeed = thrustWeaponSpeed * 0.75f * thrustskillModifier * progressEffect;
                                break;
                            }
                        case WeaponClass.TwoHandedPolearm:
                            {
                                float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);

                                thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weaponWeight, weaponInertia, weaponCOM);
                                thrustWeaponSpeed = thrustWeaponSpeed * 0.7f * thrustskillModifier * progressEffect;
                                break;
                            }
                        case WeaponClass.TwoHandedAxe:
                            {
                                float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);

                                thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weaponWeight, weaponInertia, weaponCOM);
                                thrustWeaponSpeed = thrustWeaponSpeed * 0.9f * thrustskillModifier * progressEffect;
                                break;
                            }
                        case WeaponClass.OneHandedSword:
                        case WeaponClass.Dagger:
                        case WeaponClass.TwoHandedSword:
                            {
                                float thrustskillModifier = 1f + (effectiveSkillDR / 800f);

                                thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weaponWeight, weaponInertia, weaponCOM);
                                thrustWeaponSpeed = thrustWeaponSpeed * 0.7f * thrustskillModifier * progressEffect;
                                break;
                            }
                    }

                    switch (weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).WeaponClass)
                    {
                        case WeaponClass.OneHandedPolearm:
                        case WeaponClass.OneHandedSword:
                        case WeaponClass.Dagger:
                        case WeaponClass.Mace:
                            {
                                thrustMagnitude = Utilities.CalculateThrustMagnitudeForOneHandedWeapon(weaponWeight, effectiveSkillDR, thrustWeaponSpeed, 0f, Agent.UsageDirection.AttackDown);
                                break;
                            }
                        case WeaponClass.TwoHandedPolearm:
                        case WeaponClass.TwoHandedSword:
                            {
                                thrustMagnitude = Utilities.CalculateThrustMagnitudeForTwoHandedWeapon(weaponWeight, effectiveSkillDR, thrustWeaponSpeed, 0f, Agent.UsageDirection.AttackDown);
                                break;
                            }
                            //default:
                            //    {
                            //        //thrustMagnitude = Game.Current.BasicModels.StrikeMagnitudeModel.CalculateStrikeMagnitudeForThrust(character, null, thrustWeaponSpeed, weaponWeight, weapon.Item, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex), 0f, false);
                            //        //break;
                            //    }
                    }
                }
                return thrustMagnitude;
            }

            public static float calculateHealthDamage(MissionWeapon targetWeapon, Agent attacker, Agent vicitm, float overPostureDamage, Blow b, Agent victimAgent)
            {
                float armorSumPosture = victimAgent.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.Head);
                armorSumPosture += victimAgent.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.Neck);
                armorSumPosture += victimAgent.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.Chest);
                armorSumPosture += victimAgent.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.Abdomen);
                armorSumPosture += victimAgent.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.ShoulderLeft);
                armorSumPosture += victimAgent.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.ShoulderRight);
                armorSumPosture += victimAgent.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.ArmLeft);
                armorSumPosture += victimAgent.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.ArmRight);
                armorSumPosture += victimAgent.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.Legs);

                armorSumPosture = (armorSumPosture / 9f);


                if (RBMConfig.RBMConfig.rbmCombatEnabled)
                {
                    int relevantSkill = 0;
                    float swingSpeed = 0f;
                    float thrustSpeed = 0f;
                    float swingDamageFactor = 0f;
                    float thrustDamageFactor = 0f;
                    float sweetSpotOut = 0f;
                    float sweetSpot = 0f;
                    int targetWeaponUsageIndex = targetWeapon.CurrentUsageIndex;
                    BasicCharacterObject currentSelectedChar = attacker.Character;

                    if (currentSelectedChar != null && !targetWeapon.IsEmpty && targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex) != null && targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).IsMeleeWeapon)
                    {
                        if (currentSelectedChar != null)
                        {
                            SkillObject skill = targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).RelevantSkill;
                            int effectiveSkill = currentSelectedChar.GetSkillValue(skill);
                            float effectiveSkillDR = Utilities.GetEffectiveSkillWithDR(effectiveSkill);
                            float skillModifier = Utilities.CalculateSkillModifier(effectiveSkill);

                            Utilities.CalculateVisualSpeeds(targetWeapon, targetWeaponUsageIndex, effectiveSkillDR, out int swingSpeedReal, out int thrustSpeedReal, out int handlingReal);

                            float swingSpeedRealF = swingSpeedReal / Utilities.swingSpeedTransfer;
                            float thrustSpeedRealF = thrustSpeedReal / Utilities.thrustSpeedTransfer;

                            relevantSkill = effectiveSkill;

                            swingSpeed = swingSpeedRealF;
                            thrustSpeed = thrustSpeedRealF;
                            if (b.StrikeType == StrikeType.Swing)
                            {
                                float sweetSpotMagnitude = CalculateSweetSpotSwingMagnitude(currentSelectedChar, targetWeapon, targetWeaponUsageIndex, effectiveSkill);

                                float skillBasedDamage = Utilities.GetSkillBasedDamage(sweetSpotMagnitude, false, targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(),
                                    targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).SwingDamageType, effectiveSkillDR, skillModifier, StrikeType.Swing, targetWeapon.Item.Weight);


                                swingDamageFactor = (float)Math.Sqrt(Utilities.getSwingDamageFactor(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex), targetWeapon.ItemModifier));


                                int realDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(), targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).SwingDamageType, skillBasedDamage, armorSumPosture, 1f, out float penetratedDamage, out float bluntForce, swingDamageFactor, null, false)), 0, 2000);
                                realDamage = MathF.Floor(realDamage * 1f);
                                return realDamage;
                            }
                            else
                            {
                                float thrustMagnitude = CalculateThrustMagnitude(currentSelectedChar, targetWeapon, targetWeaponUsageIndex, effectiveSkill);

                                float skillBasedDamage = Utilities.GetSkillBasedDamage(thrustMagnitude, false, targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(),
                                    targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).ThrustDamageType, effectiveSkillDR, skillModifier, StrikeType.Thrust, targetWeapon.Item.Weight);


                                thrustDamageFactor = (float)Math.Sqrt(Utilities.getThrustDamageFactor(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex), targetWeapon.ItemModifier));

                                int realDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(),
                                targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).ThrustDamageType, skillBasedDamage, armorSumPosture, 1f, out float penetratedDamage, out float bluntForce, thrustDamageFactor, null, false)), 0, 2000);
                                realDamage = MathF.Floor(realDamage * 1f);
                                return realDamage;
                            }
                        }
                    }
                }

                int weaponDamage = 0;
                if (b.StrikeType == StrikeType.Swing)
                {
                    weaponDamage = targetWeapon.GetModifiedSwingDamageForCurrentUsage();
                }
                else
                {
                    weaponDamage = targetWeapon.GetModifiedThrustDamageForCurrentUsage();
                }

                return MBMath.ClampInt(MathF.Ceiling(MissionGameModels.Current.StrikeMagnitudeModel.ComputeRawDamage(b.DamageType, weaponDamage, armorSumPosture, 1f)), 0, 2000);
            }

            private static float calculateDefenderPostureDamage(Agent defenderAgent, Agent attackerAgent, float absoluteDamageModifier, float actionTypeDamageModifier, ref AttackCollisionData collisionData, MissionWeapon weapon, float comHitModifier)
            {

                //Posture defenderPosture = null;
                //AgentPostures.values.TryGetValue(defenderAgent, out defenderPosture);

                //if(defenderPosture != null)
                //{
                //     //defenderPosture.lastPostureLossTime = Mission.Current.CurrentTime;
                //}

                float result = 0f;
                float defenderPostureDamageModifier = 1f; // terms and conditions may apply

                float strengthSkillModifier = 500f;
                float weaponSkillModifier = 500f;
                float basePostureDamage = 20f;

                SkillObject attackerWeaponSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(weapon.CurrentUsageItem.WeaponClass);
                float attackerEffectiveWeaponSkill = 0;
                float attackerEffectiveStrengthSkill = 0;
                if (attackerWeaponSkill != null)
                {
                    attackerEffectiveWeaponSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attackerAgent, attackerWeaponSkill);
                }
                if (attackerAgent.HasMount)
                {
                    attackerEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attackerAgent, DefaultSkills.Riding);
                }
                else
                {
                    attackerEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attackerAgent, DefaultSkills.Athletics);
                }

                float defenderEffectiveWeaponSkill = 0;
                float defenderEffectiveStrengthSkill = 0;

                float defenderWeaponLength = -1f;
                float attackerWeaponLength = -1f;

                float attackerWeaponWeight = -1f;
                float defenderWeaponWeight = -1f;

                if (weapon.CurrentUsageItem != null)
                {
                    attackerWeaponLength = weapon.CurrentUsageItem.WeaponLength;
                    attackerWeaponWeight = weapon.GetWeight();
                }

                if (defenderAgent.GetPrimaryWieldedItemIndex() != EquipmentIndex.None)
                {
                    MissionWeapon defenderWeapon = defenderAgent.Equipment[defenderAgent.GetPrimaryWieldedItemIndex()];
                    SkillObject defenderWeaponSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(defenderWeapon.CurrentUsageItem.WeaponClass);
                    if (defenderWeaponSkill != null)
                    {
                        defenderEffectiveWeaponSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(defenderAgent, defenderWeaponSkill);
                    }
                    if (defenderAgent.GetOffhandWieldedItemIndex() != EquipmentIndex.None)
                    {
                        if (defenderAgent.Equipment[defenderAgent.GetOffhandWieldedItemIndex()].IsShield())
                        {
                            defenderEffectiveWeaponSkill += 20f;
                        }
                    }
                    else
                    {
                        if (defenderWeapon.CurrentUsageItem != null)
                        {
                            defenderWeaponLength = defenderWeapon.CurrentUsageItem.WeaponLength;
                            defenderWeaponWeight = defenderWeapon.GetWeight();
                        }
                    }
                }
                if (defenderAgent.HasMount)
                {
                    defenderEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(defenderAgent, DefaultSkills.Riding);
                }
                else
                {
                    defenderEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(defenderAgent, DefaultSkills.Athletics);
                }

                defenderEffectiveWeaponSkill = defenderEffectiveWeaponSkill / weaponSkillModifier;
                defenderEffectiveStrengthSkill = defenderEffectiveStrengthSkill / strengthSkillModifier;

                attackerEffectiveWeaponSkill = attackerEffectiveWeaponSkill / weaponSkillModifier;
                attackerEffectiveStrengthSkill = attackerEffectiveStrengthSkill / strengthSkillModifier;

                bool attackBlockedByOneHanedWithoutShield = false;
                if (!collisionData.AttackBlockedWithShield)
                {
                    EquipmentIndex ei = defenderAgent.GetPrimaryWieldedItemIndex();
                    if (ei != EquipmentIndex.None)
                    {
                        WeaponClass ws = defenderAgent.Equipment[defenderAgent.GetPrimaryWieldedItemIndex()].CurrentUsageItem.WeaponClass;
                        if (ws == WeaponClass.OneHandedAxe || ws == WeaponClass.OneHandedPolearm || ws == WeaponClass.OneHandedSword || ws == WeaponClass.Mace)
                        {
                            attackBlockedByOneHanedWithoutShield = true;
                        }
                    }
                }
                float lwrpostureModifier = calculateDefenderLWRPostureModifier(attackerAgent, defenderAgent, attackerWeaponLength, defenderWeaponLength, attackerWeaponWeight, attackBlockedByOneHanedWithoutShield, collisionData.AttackBlockedWithShield);

                basePostureDamage = basePostureDamage * ((1f + attackerEffectiveStrengthSkill + attackerEffectiveWeaponSkill) / (1f + defenderEffectiveStrengthSkill + defenderEffectiveWeaponSkill)) * lwrpostureModifier;

                float attackerPostureModifier = 1f;
                //WeaponClass attackerWeaponClass = WeaponClass.Undefined;
                //if (weapon.CurrentUsageItem != null)
                //{
                //    attackerWeaponClass = weapon.CurrentUsageItem.WeaponClass;
                //}
                //switch (attackerWeaponClass)
                //{
                //    case WeaponClass.Dagger:
                //    case WeaponClass.OneHandedSword:
                //        {
                //            attackerPostureModifier = 0.85f;
                //            break;
                //        }
                //    case WeaponClass.TwoHandedSword:
                //        {
                //            attackerPostureModifier = 0.75f;
                //            break;
                //        }
                //    case WeaponClass.TwoHandedAxe:
                //    case WeaponClass.TwoHandedMace:
                //    case WeaponClass.TwoHandedPolearm:
                //        {
                //            attackerPostureModifier = 1f;
                //            break;
                //        }
                //    case WeaponClass.Mace:
                //    case WeaponClass.Pick:
                //        {
                //            attackerPostureModifier = 1.15f;
                //            break;
                //        }
                //    default:
                //        {
                //            attackerPostureModifier = 1f;
                //            break;
                //        }
                //}

                WeaponClass defenderWeaponClass = WeaponClass.Undefined;
                if (defenderAgent.GetOffhandWieldedItemIndex() != EquipmentIndex.None)
                {
                    if (defenderAgent.Equipment[defenderAgent.GetOffhandWieldedItemIndex()].IsShield())
                    {
                        defenderWeaponClass = defenderAgent.Equipment[defenderAgent.GetOffhandWieldedItemIndex()].CurrentUsageItem.WeaponClass;
                    }
                }
                else
                {
                    if (defenderAgent.GetPrimaryWieldedItemIndex() != EquipmentIndex.None)
                    {
                        defenderWeaponClass = defenderAgent.Equipment[defenderAgent.GetPrimaryWieldedItemIndex()].CurrentUsageItem.WeaponClass;
                    }
                }
                switch (defenderWeaponClass)
                {
                    case WeaponClass.Dagger:
                    case WeaponClass.OneHandedSword:
                        {
                            defenderPostureDamageModifier = 0.85f;
                            break;
                        }
                    case WeaponClass.TwoHandedSword:
                        {
                            defenderPostureDamageModifier = 0.75f;
                            break;
                        }
                    case WeaponClass.TwoHandedAxe:
                    case WeaponClass.TwoHandedMace:
                    case WeaponClass.TwoHandedPolearm:
                        {
                            defenderPostureDamageModifier = 0.9f;
                            break;
                        }
                    case WeaponClass.Mace:
                    case WeaponClass.Pick:
                        {
                            defenderPostureDamageModifier = 1.15f;
                            break;
                        }
                    case WeaponClass.LargeShield:
                    case WeaponClass.SmallShield:
                        {
                            defenderPostureDamageModifier = 0.8f;
                            break;
                        }
                    default:
                        {
                            defenderPostureDamageModifier = 1f;
                            break;
                        }
                }

                actionTypeDamageModifier += actionTypeDamageModifier * 0.5f * comHitModifier;
                result = basePostureDamage * actionTypeDamageModifier * defenderPostureDamageModifier * attackerPostureModifier;
                //InformationManager.DisplayMessage(new InformationMessage("Deffender PD: " + result));
                return result;
            }

            private static float calculateAttackerPostureDamage(Agent defenderAgent, Agent attackerAgent, float absoluteDamageModifier, float actionTypeDamageModifier, ref AttackCollisionData collisionData, MissionWeapon weapon, float comHitModifier)
            {
                
                float result = 0f;
                float postureDamageModifier = 1f; // terms and conditions may apply

                float strengthSkillModifier = 500f;
                float weaponSkillModifier = 500f;
                float basePostureDamage = 20f;

                SkillObject attackerWeaponSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(weapon.CurrentUsageItem.WeaponClass);

                float attackerEffectiveWeaponSkill = 0;
                float attackerEffectiveStrengthSkill = 0;

                if (attackerWeaponSkill != null)
                {
                    attackerEffectiveWeaponSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attackerAgent, attackerWeaponSkill);
                }
                if (attackerAgent.HasMount)
                {
                    attackerEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attackerAgent, DefaultSkills.Riding);
                }
                else
                {
                    attackerEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attackerAgent, DefaultSkills.Athletics);
                }

                float defenderEffectiveWeaponSkill = 0;
                float defenderEffectiveStrengthSkill = 0;

                float defenderWeaponLength = -1f;
                float attackerWeaponLength = -1f;

                float attackerWeaponWeight = -1f;
                float defenderWeaponWeight = -1f;

                if (weapon.CurrentUsageItem != null)
                {
                    attackerWeaponLength = weapon.CurrentUsageItem.WeaponLength;
                    attackerWeaponWeight = weapon.GetWeight();
                }

                if (defenderAgent.GetPrimaryWieldedItemIndex() != EquipmentIndex.None)
                {
                    MissionWeapon defenderWeapon = defenderAgent.Equipment[defenderAgent.GetPrimaryWieldedItemIndex()];
                    SkillObject defenderWeaponSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(defenderWeapon.CurrentUsageItem.WeaponClass);
                    if (defenderWeaponSkill != null)
                    {
                        defenderEffectiveWeaponSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(defenderAgent, defenderWeaponSkill);
                    }
                    if (defenderAgent.GetOffhandWieldedItemIndex() != EquipmentIndex.None)
                    {
                        if (defenderAgent.Equipment[defenderAgent.GetOffhandWieldedItemIndex()].IsShield())
                        {
                            defenderEffectiveWeaponSkill += 20f;
                        }
                        else
                        {
                            if (defenderWeapon.CurrentUsageItem != null)
                            {
                                defenderWeaponLength = defenderWeapon.CurrentUsageItem.WeaponLength;
                                defenderWeaponWeight = defenderWeapon.GetWeight();
                            }
                        }
                    }
                }
                if (defenderAgent.HasMount)
                {
                    defenderEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(defenderAgent, DefaultSkills.Riding);
                }
                else
                {
                    defenderEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(defenderAgent, DefaultSkills.Athletics);
                }

                defenderEffectiveWeaponSkill = defenderEffectiveWeaponSkill / weaponSkillModifier;
                defenderEffectiveStrengthSkill = defenderEffectiveStrengthSkill / strengthSkillModifier;

                attackerEffectiveWeaponSkill = attackerEffectiveWeaponSkill / weaponSkillModifier;
                attackerEffectiveStrengthSkill = attackerEffectiveStrengthSkill / strengthSkillModifier;

                bool attackBlockedByOneHanedWithoutShield = false;
                if (!collisionData.AttackBlockedWithShield)
                {
                    EquipmentIndex ei = defenderAgent.GetPrimaryWieldedItemIndex();
                    if (ei != EquipmentIndex.None)
                    {
                        WeaponClass ws = defenderAgent.Equipment[defenderAgent.GetPrimaryWieldedItemIndex()].CurrentUsageItem.WeaponClass;
                        if (ws == WeaponClass.OneHandedAxe || ws == WeaponClass.OneHandedPolearm || ws == WeaponClass.OneHandedSword || ws == WeaponClass.Mace)
                        {
                            attackBlockedByOneHanedWithoutShield = true;
                        }
                    }
                }

                float lwrpostureModifier = calculateAttackerLWRPostureModifier(attackerAgent, defenderAgent, attackerWeaponLength, defenderWeaponLength, attackerWeaponWeight, defenderWeaponWeight, attackBlockedByOneHanedWithoutShield, collisionData.AttackBlockedWithShield);

                basePostureDamage = basePostureDamage * ((1f + defenderEffectiveStrengthSkill + defenderEffectiveWeaponSkill) / (1f + attackerEffectiveStrengthSkill + attackerEffectiveWeaponSkill)) * lwrpostureModifier;

                switch (weapon.CurrentUsageItem.WeaponClass)
                {
                    case WeaponClass.Dagger:
                    case WeaponClass.OneHandedSword:
                        {
                            postureDamageModifier = 0.85f;
                            break;
                        }
                    case WeaponClass.TwoHandedSword:
                        {
                            postureDamageModifier = 0.75f;
                            break;
                        }
                    case WeaponClass.TwoHandedAxe:
                    case WeaponClass.TwoHandedMace:
                    case WeaponClass.TwoHandedPolearm:
                        {
                            postureDamageModifier = 1f;
                            break;
                        }
                    case WeaponClass.Mace:
                    case WeaponClass.Pick:
                        {
                            postureDamageModifier = 1.15f;
                            break;
                        }
                    default:
                        {
                            postureDamageModifier = 1f;
                            break;
                        }
                }

                actionTypeDamageModifier += actionTypeDamageModifier * 0.5f * comHitModifier;
                result = basePostureDamage * actionTypeDamageModifier * postureDamageModifier;
                //InformationManager.DisplayMessage(new InformationMessage("Attacker PD: " + result));
                return result;
            }

            public static float calculateDefenderLWRPostureModifier(
                Agent attackerAgent, Agent defenderAgent,
                float attackerWeaponLength, float defenderWeaponLength, float attackerWeaponWeight, bool attackBlockedByOneHanded, bool attackBlockedByShield)
            {
                float relativeSpeed = (defenderAgent.Velocity - attackerAgent.Velocity).Length * relativeSpeedPostureFactor;
                if (attackBlockedByShield)
                {
                    return 1f + ((attackerWeaponWeight / 2f) + relativeSpeed) / 4f;
                }
                else
                {
                    if (attackBlockedByOneHanded)
                    {
                        return 1f + ((attackerWeaponWeight / 2f) + relativeSpeed) / 2f;
                    }
                    else
                    {
                        return 1f + ((attackerWeaponWeight / 2f) + relativeSpeed) / 3f;
                    }
                }
            }

            public static float calculateAttackerLWRPostureModifier(
                Agent attackerAgent, Agent defenderAgent,
                float attackerWeaponLength, float defenderWeaponLength,
                float attackerWeaponWeight, float defenderWeaponWeight, bool attackBlockedByOneHanded, bool attackBlockedByShield)
            {
                float relativeSpeed = (defenderAgent.Velocity - attackerAgent.Velocity).Length * relativeSpeedPostureFactor;
                if (attackBlockedByShield)
                {
                    return 1f + ((attackerWeaponWeight / 2f) + relativeSpeed) / 2f;
                }
                else
                {
                    if (attackBlockedByOneHanded)
                    {
                        return 1f + ((attackerWeaponWeight / 2f) + relativeSpeed) / 4f;
                    }
                    else
                    {
                        return 1f + ((attackerWeaponWeight / 2f) + relativeSpeed) / 3f;
                    }
                }
            }

            private static void makePostureRiposteBlow(ref Mission mission, Blow blow, Agent attackerAgent, Agent victimAgent, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, CrushThroughState crushThroughState, Vec3 blowDirection, Vec3 swingDirection, bool cancelDamage, BlowFlags addedBlowFlag)
            {
                Blow newBLow = blow;
                newBLow.BaseMagnitude = collisionData.BaseMagnitude;
                newBLow.MovementSpeedDamageModifier = collisionData.MovementSpeedDamageModifier;
                newBLow.InflictedDamage = 1;
                newBLow.SelfInflictedDamage = collisionData.SelfInflictedDamage;
                newBLow.AbsorbedByArmor = collisionData.AbsorbedByArmor;

                sbyte weaponAttachBoneIndex = (sbyte)(attackerWeapon.IsEmpty ? (-1) : attackerAgent.Monster.GetBoneToAttachForItemFlags(attackerWeapon.Item.ItemFlags));
                newBLow.WeaponRecord.FillAsMeleeBlow(attackerWeapon.Item, attackerWeapon.CurrentUsageItem, collisionData.AffectorWeaponSlotOrMissileIndex, weaponAttachBoneIndex);
                newBLow.StrikeType = (StrikeType)collisionData.StrikeType;
                newBLow.DamageType = ((!attackerWeapon.IsEmpty && true && !collisionData.IsAlternativeAttack) ? ((DamageTypes)collisionData.DamageType) : DamageTypes.Blunt);
                newBLow.NoIgnore = collisionData.IsAlternativeAttack;
                newBLow.AttackerStunPeriod = collisionData.AttackerStunPeriod;
                newBLow.DefenderStunPeriod = collisionData.DefenderStunPeriod;
                newBLow.BlowFlag = BlowFlags.None;
                newBLow.GlobalPosition = collisionData.CollisionGlobalPosition;
                newBLow.BoneIndex = collisionData.CollisionBoneIndex;
                newBLow.Direction = blowDirection;
                newBLow.SwingDirection = swingDirection;
                //blow.InflictedDamage = 1;
                newBLow.VictimBodyPart = collisionData.VictimHitBodyPart;
                newBLow.BlowFlag |= addedBlowFlag;
                attackerAgent.RegisterBlow(newBLow, collisionData);
                foreach (MissionBehavior missionBehaviour in mission.MissionBehaviors)
                {
                    missionBehaviour.OnRegisterBlow(victimAgent, attackerAgent, WeakGameEntity.Invalid, newBLow, ref collisionData, in attackerWeapon);
                }
            }

            private static void makePostureBlow(ref Mission mission, Blow blow, Agent attackerAgent, Agent victimAgent, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, CrushThroughState crushThroughState, Vec3 blowDirection, Vec3 swingDirection, bool cancelDamage, BlowFlags addedBlowFlag)
            {
                Blow newBLow = blow;
                newBLow.BaseMagnitude = collisionData.BaseMagnitude;
                newBLow.MovementSpeedDamageModifier = collisionData.MovementSpeedDamageModifier;
                newBLow.InflictedDamage = 1;
                newBLow.SelfInflictedDamage = collisionData.SelfInflictedDamage;
                newBLow.AbsorbedByArmor = collisionData.AbsorbedByArmor;

                sbyte weaponAttachBoneIndex = (sbyte)(attackerWeapon.IsEmpty ? (-1) : attackerAgent.Monster.GetBoneToAttachForItemFlags(attackerWeapon.Item.ItemFlags));
                newBLow.WeaponRecord.FillAsMeleeBlow(attackerWeapon.Item, attackerWeapon.CurrentUsageItem, collisionData.AffectorWeaponSlotOrMissileIndex, weaponAttachBoneIndex);
                newBLow.StrikeType = (StrikeType)collisionData.StrikeType;
                newBLow.DamageType = ((!attackerWeapon.IsEmpty && true && !collisionData.IsAlternativeAttack) ? ((DamageTypes)collisionData.DamageType) : DamageTypes.Blunt);
                newBLow.NoIgnore = collisionData.IsAlternativeAttack;
                newBLow.AttackerStunPeriod = collisionData.AttackerStunPeriod;
                newBLow.DefenderStunPeriod = collisionData.DefenderStunPeriod;
                newBLow.BlowFlag = BlowFlags.None;
                newBLow.GlobalPosition = collisionData.CollisionGlobalPosition;
                newBLow.BoneIndex = collisionData.CollisionBoneIndex;
                newBLow.Direction = blowDirection;
                newBLow.SwingDirection = swingDirection;
                //blow.InflictedDamage = 1;
                newBLow.VictimBodyPart = collisionData.VictimHitBodyPart;
                newBLow.BlowFlag |= addedBlowFlag;
                victimAgent.RegisterBlow(newBLow, collisionData);
                foreach (MissionBehavior missionBehaviour in mission.MissionBehaviors)
                {
                    missionBehaviour.OnRegisterBlow(attackerAgent, victimAgent, WeakGameEntity.Invalid, newBLow, ref collisionData, in attackerWeapon);
                }
            }

            private static void makePostureCrashThroughBlow(ref Mission mission, Blow blow, Agent attackerAgent, Agent victimAgent, int inflictedHpDmg, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, CrushThroughState crushThroughState, Vec3 blowDirection, Vec3 swingDirection, bool cancelDamage, BlowFlags addedBlowFlag)
            {
                Blow newBLow = blow;
                newBLow.BaseMagnitude = collisionData.BaseMagnitude;
                newBLow.MovementSpeedDamageModifier = collisionData.MovementSpeedDamageModifier;
                newBLow.InflictedDamage = inflictedHpDmg;
                newBLow.SelfInflictedDamage = collisionData.SelfInflictedDamage;
                newBLow.AbsorbedByArmor = collisionData.AbsorbedByArmor;

                sbyte weaponAttachBoneIndex = (sbyte)(attackerWeapon.IsEmpty ? (-1) : attackerAgent.Monster.GetBoneToAttachForItemFlags(attackerWeapon.Item.ItemFlags));
                newBLow.WeaponRecord.FillAsMeleeBlow(attackerWeapon.Item, attackerWeapon.CurrentUsageItem, collisionData.AffectorWeaponSlotOrMissileIndex, weaponAttachBoneIndex);
                newBLow.StrikeType = (StrikeType)collisionData.StrikeType;
                newBLow.DamageType = ((!attackerWeapon.IsEmpty && true && !collisionData.IsAlternativeAttack) ? ((DamageTypes)collisionData.DamageType) : DamageTypes.Blunt);
                newBLow.NoIgnore = collisionData.IsAlternativeAttack;
                newBLow.AttackerStunPeriod = collisionData.AttackerStunPeriod / 5f;
                newBLow.DefenderStunPeriod = collisionData.DefenderStunPeriod * 5f;
                newBLow.BlowFlag = BlowFlags.None;
                newBLow.GlobalPosition = collisionData.CollisionGlobalPosition;
                newBLow.BoneIndex = collisionData.CollisionBoneIndex;
                newBLow.Direction = blowDirection;
                newBLow.SwingDirection = swingDirection;
                //blow.InflictedDamage = 1;
                newBLow.VictimBodyPart = collisionData.VictimHitBodyPart;
                //newBLow.BlowFlag |= BlowFlags.CrushThrough;
                newBLow.BlowFlag |= addedBlowFlag;
                victimAgent.RegisterBlow(newBLow, collisionData);
                foreach (MissionBehavior missionBehaviour in mission.MissionBehaviors)
                {
                    missionBehaviour.OnRegisterBlow(attackerAgent, victimAgent, WeakGameEntity.Invalid, newBLow, ref collisionData, in attackerWeapon);
                }
            }

            public static float calculateRangedPostureLoss(float fixedPS, float dynamicPS, Agent shooterAgent, WeaponClass wc ){
                SkillObject attackerWeaponSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(wc);

                float attackerEffectiveWeaponSkill = 0;
                float attackerEffectiveStrengthSkill = 0;

                float fixedPostureLoss = fixedPS;
                float dynamicPostureLoss = dynamicPS;

                if (attackerWeaponSkill != null)
                {
                    attackerEffectiveWeaponSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(shooterAgent, attackerWeaponSkill);
                }
                if (shooterAgent.HasMount)
                {
                    attackerEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(shooterAgent, DefaultSkills.Riding);
                }
                else
                {
                    attackerEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(shooterAgent, DefaultSkills.Athletics);
                }

                dynamicPostureLoss -= MBMath.Lerp(0f, 1f, 1f - (attackerEffectiveWeaponSkill / 200f)) * (dynamicPS * 0.5f);
                dynamicPostureLoss -= MBMath.Lerp(0f, 1f, 1f - (attackerEffectiveStrengthSkill / 200f)) * (dynamicPS * 0.5f);

                return fixedPostureLoss + dynamicPostureLoss;
            }

            [HarmonyPatch(typeof(Mission))]
            [HarmonyPatch("OnAgentShootMissile")]
            [UsedImplicitly]
            [MBCallback]
            private class OverrideOnAgentShootMissile
            {

                private static void Postfix(ref Agent shooterAgent, EquipmentIndex weaponIndex, Vec3 position, Vec3 velocity, Mat3 orientation, bool hasRigidBody, bool isPrimaryWeaponShot, int forcedMissileIndex, Mission __instance)
                {
                    if (RBMConfig.RBMConfig.postureEnabled)
                    {
                        MissionWeapon missionWeapon = shooterAgent.Equipment[weaponIndex];
                        WeaponClass wc = missionWeapon.CurrentUsageItem.WeaponClass;
                        Posture shooterPosture = null;
                        AgentPostures.values.TryGetValue(shooterAgent, out shooterPosture);
                        if(shooterPosture != null)
                        {
                            float currentTime = Mission.Current.CurrentTime;
                            switch (wc)
                                {
                                case WeaponClass.Bow:
                                    {
                                        shooterPosture.posture = shooterPosture.posture - calculateRangedPostureLoss(35f, 25f, shooterAgent, wc);
                                        break;
                                    }
                                case WeaponClass.Crossbow:
                                    {
                                        shooterPosture.posture = shooterPosture.posture - calculateRangedPostureLoss(5f, 5f, shooterAgent, wc);
                                        break;
                                    }
                                case WeaponClass.Javelin:
                                    {
                                        shooterPosture.posture = shooterPosture.posture - calculateRangedPostureLoss(25f, 25f, shooterAgent, wc);
                                        break;
                                    }
                                case WeaponClass.ThrowingAxe:
                                case WeaponClass.ThrowingKnife:
                                    {
                                        
                                        shooterPosture.posture = shooterPosture.posture - calculateRangedPostureLoss(25f, 25f, shooterAgent, wc);
                                        break;
                                    }
                            }
                            if (shooterPosture.posture < 0f)
                            {
                                float postureResetModifier = 0.5f;
                                ResetPostureForAgent(ref shooterPosture, postureResetModifier, shooterAgent);
                            }
                            //shooterPosture.lastPostureLossTime = currentTime;
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("OnShieldDamaged")]
        private class OnShieldDamagedPatch
        {
            private static bool Prefix(ref Agent __instance, ref EquipmentIndex slotIndex, ref int inflictedDamage)
            {
                int num = MathF.Max(0, __instance.Equipment[slotIndex].HitPoints - inflictedDamage);
                __instance.ChangeWeaponHitPoints(slotIndex, (short)num);
                if (num == 0)
                {
                    __instance.RemoveEquippedWeapon(slotIndex);
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(TournamentRound))]
        [HarmonyPatch("EndMatch")]
        private class EndMatchPatch
        {
            private static void Postfix(ref TournamentRound __instance)
            {
                foreach (KeyValuePair<Agent, Posture> entry in AgentPostures.values)
                {
                    entry.Value.posture = entry.Value.maxPosture;
                    if (RBMConfig.RBMConfig.postureGUIEnabled)
                    {
                        if (entry.Key.IsPlayerControlled)
                        {
                            //InformationManager.DisplayMessage(new InformationMessage(entry.Value.posture.ToString()));
                            if (AgentPostures.postureVisual != null && AgentPostures.postureVisual._dataSource.ShowPlayerPostureStatus)
                            {
                                AgentPostures.postureVisual._dataSource.PlayerPosture = (int)entry.Value.posture;
                                AgentPostures.postureVisual._dataSource.PlayerPostureMax = (int)entry.Value.maxPosture;
                                AgentPostures.postureVisual._dataSource.PlayerPostureText = ((int)entry.Value.posture).ToString();
                                AgentPostures.postureVisual._dataSource.PlayerPostureMaxText = ((int)entry.Value.maxPosture).ToString();
                            }
                        }

                        if (AgentPostures.postureVisual != null && AgentPostures.postureVisual._dataSource.ShowEnemyStatus && AgentPostures.postureVisual.affectedAgent == entry.Key)
                        {
                            AgentPostures.postureVisual._dataSource.EnemyPosture = (int)entry.Value.posture;
                            AgentPostures.postureVisual._dataSource.EnemyPostureMax = (int)entry.Value.maxPosture;
                        }
                    }
                }
                agentsToDropShield.Clear();
                agentsToDropWeapon.Clear();
                agentsToChangeFormation.Clear();
                AgentPostures.values.Clear();
            }
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (RBMConfig.RBMConfig.postureEnabled && Mission.Current.AllowAiTicking)
            {
                if (currentDt < timeToCalc)
                {
                    currentDt += dt;
                }
                else
                {
                    MBArrayList<Agent> inactiveAgents = new MBArrayList<Agent>();
                    foreach (KeyValuePair<Agent, Posture> entry in AgentPostures.values)
                    {
                        if(entry.Key != null && entry.Key.Mission != null && !entry.Key.IsActive())
                        {
                            inactiveAgents.Add(entry.Key);
                            continue;
                        }
                        if (entry.Key.IsPlayerControlled)
                        {
                            if (AgentPostures.postureVisual != null && AgentPostures.postureVisual._dataSource.ShowPlayerPostureStatus)
                            {
                                AgentPostures.postureVisual._dataSource.PlayerPosture = (int)entry.Value.posture;
                                AgentPostures.postureVisual._dataSource.PlayerPostureMax = (int)entry.Value.maxPosture;
                                AgentPostures.postureVisual._dataSource.PlayerPostureText = ((int)entry.Value.posture).ToString();
                                AgentPostures.postureVisual._dataSource.PlayerPostureMaxText = ((int)entry.Value.maxPosture).ToString();
                            }
                        }
                        if (entry.Value.posture < entry.Value.maxPosture)
                        {
                            if (RBMConfig.RBMConfig.postureGUIEnabled)
                            {
                                if (AgentPostures.postureVisual != null && AgentPostures.postureVisual._dataSource.ShowEnemyStatus && AgentPostures.postureVisual.affectedAgent == entry.Key)
                                {
                                    AgentPostures.postureVisual._dataSource.EnemyPosture = (int)entry.Value.posture;
                                    AgentPostures.postureVisual._dataSource.EnemyPostureMax = (int)entry.Value.maxPosture;
                                }
                            }
                            entry.Value.posture += entry.Value.regenPerTick * 30f;
                        }
                    }
                    foreach(Agent agent in inactiveAgents)
                    {
                        AgentPostures.values.Remove(agent);
                    }
                    inactiveAgents.Clear();

                    foreach (KeyValuePair<Agent, FormationClass> entry in agentsToChangeFormation)
                    {
                        if (entry.Key != null && entry.Key.Mission != null && entry.Key.IsActive() && entry.Key.Team != null)
                        {
                            entry.Key.Formation = entry.Key.Team.GetFormation(entry.Value);
                            entry.Key.DisableScriptedMovement();
                        }
                    }
                    agentsToChangeFormation.Clear();

                    //shield drop
                    MBArrayList<Agent> agentsAbleToDropShield = new MBArrayList<Agent> { };
                    for (int i = agentsToDropShield.Count - 1; i >= 0; i--)
                    {
                        if (agentsToDropShield[i] != null && agentsToDropShield[i].Mission != null && agentsToDropShield[i].IsActive())
                        {
                            ActionCodeType currentActionType = agentsToDropShield[i].GetCurrentActionType(1);
                            if (
                                currentActionType == ActionCodeType.ReleaseMelee ||
                                currentActionType == ActionCodeType.ReleaseRanged ||
                                currentActionType == ActionCodeType.ReleaseThrowing ||
                                currentActionType == ActionCodeType.WeaponBash)
                            {
                                continue;
                            }
                            else
                            {
                                agentsAbleToDropShield.Add(agentsToDropShield[i]);
                            }
                        }
                        else
                        {
                            agentsAbleToDropShield.Add(agentsToDropShield[i]);
                        }
                    }
                    foreach(Agent agent in agentsAbleToDropShield)
                    {
                        if (agent != null && agent.Mission != null && agent.IsActive())
                        {
                            EquipmentIndex ei = agent.GetOffhandWieldedItemIndex();
                            if (ei != EquipmentIndex.None)
                            {
                                agent.DropItem(ei);
                                agent.UpdateAgentProperties();
                            }
                        }
                        agentsToDropShield.Remove(agent);
                    }
                    agentsAbleToDropShield.Clear();

                    //weapon drop
                    MBArrayList<Agent> agentsAbleToDropWeapon = new MBArrayList<Agent> { };
                    for (int i = agentsToDropWeapon.Count - 1; i >= 0; i--)
                    {
                        if (agentsToDropWeapon[i] != null && agentsToDropWeapon[i].Mission != null && agentsToDropWeapon[i].IsActive())
                        {
                            ActionCodeType currentActionType = agentsToDropWeapon[i].GetCurrentActionType(1);
                            if (
                                currentActionType == ActionCodeType.ReleaseMelee ||
                                currentActionType == ActionCodeType.ReleaseRanged ||
                                currentActionType == ActionCodeType.ReleaseThrowing ||
                                currentActionType == ActionCodeType.WeaponBash)
                            {
                                continue;
                            }
                            else
                            {
                                agentsAbleToDropWeapon.Add(agentsToDropWeapon[i]);
                            }
                        }
                        else
                        {
                            agentsAbleToDropWeapon.Add(agentsToDropWeapon[i]);
                        }
                    }
                    foreach (Agent agent in agentsAbleToDropWeapon)
                    {
                        if (agent != null && agent.Mission != null && agent.IsActive())
                        {
                            EquipmentIndex ei = agent.GetPrimaryWieldedItemIndex();
                            if (ei != EquipmentIndex.None)
                            {
                                agent.DropItem(ei);
                                agent.UpdateAgentProperties();
                            }
                        }
                        agentsToDropWeapon.Remove(agent);
                    }
                    agentsAbleToDropWeapon.Clear();

                    currentDt = 0f;
                }
            }

        }

        //[HarmonyPatch(typeof(Mission))]
        //[HarmonyPatch("OnAgentDismount")]
        //public class OnAgentDismountPatch
        //{
        //    private static void Postfix(Agent agent, Mission __instance)
        //    {
        //        if (!agent.IsPlayerControlled && agent.Formation != null && Mission.Current != null && Mission.Current.IsFieldBattle && agent.IsActive())
        //        {
        //            bool isInfFormationActive = agent.Team.GetFormation(FormationClass.Infantry) != null && agent.Team.GetFormation(FormationClass.Infantry).CountOfUnits > 0;
        //            bool isArcFormationActive = agent.Team.GetFormation(FormationClass.Ranged) != null && agent.Team.GetFormation(FormationClass.Ranged).CountOfUnits > 0;
        //            if (agent.Equipment.HasRangedWeapon(WeaponClass.Arrow) || agent.Equipment.HasRangedWeapon(WeaponClass.Bolt))
        //            {
        //                float distanceToInf = -1f;
        //                float distanceToArc = -1f;
        //                if (agent.Formation != null && isInfFormationActive)
        //                {
        //                    distanceToInf = agent.Team.GetFormation(FormationClass.Infantry).CachedMedianPosition.AsVec2.Distance(agent.Formation.CachedMedianPosition.AsVec2);
        //                }
        //                if (agent.Formation != null && isArcFormationActive)
        //                {
        //                    distanceToArc = agent.Team.GetFormation(FormationClass.Ranged).CachedMedianPosition.AsVec2.Distance(agent.Formation.CachedMedianPosition.AsVec2);
        //                }
        //                if (distanceToArc > 0f && distanceToArc < distanceToInf)
        //                {
        //                    if (agent != null && agent.IsActive())
        //                    {
        //                        try
        //                        {
        //                            agentsToChangeFormation[agent] = FormationClass.Ranged;
        //                            return;
        //                        }
        //                        catch (Exception ex)
        //                        {
        //                            return;
        //                        }
        //                    }
        //                }
        //                else if (distanceToInf > 0f && distanceToInf < distanceToArc)
        //                {
        //                    if (agent != null && agent.IsActive())
        //                    {
        //                        try
        //                        {
        //                            agentsToChangeFormation[agent] = FormationClass.Infantry;
        //                            return;
        //                        }
        //                        catch (Exception ex) 
        //                        {
        //                            return;
        //                        }
        //                    }
        //                }
        //                else
        //                {
        //                    if (distanceToInf > 0f)
        //                    {
        //                        if (agent != null && agent.IsActive())
        //                        {
        //                            try
        //                            {
        //                                agentsToChangeFormation[agent] = FormationClass.Infantry;
        //                                return;
        //                            }
        //                            catch (Exception ex)
        //                            {
        //                                return;
        //                            }
        //                        }
        //                    }
        //                    else if (distanceToArc > 0f)
        //                    {
        //                        if (agent != null && agent.IsActive())
        //                        {
        //                            try
        //                            {
        //                                agentsToChangeFormation[agent] = FormationClass.Ranged;
        //                                return;
        //                            }
        //                            catch (Exception ex)
        //                            {
        //                                return;
        //                            }
        //                        }
        //                    }
        //                }
        //            }
        //            else
        //            {
        //                if (agent.Formation != null && isInfFormationActive)
        //                {
        //                    if (agent != null && agent.IsActive())
        //                    {
        //                        try
        //                        {
        //                            agentsToChangeFormation[agent] = FormationClass.Infantry;
        //                            return;
        //                        }
        //                        catch (Exception ex)
        //                        {
        //                            return;
        //                        }
        //                    }
        //                }
        //            }
        //        }
        //    }
        //}

        //[HarmonyPatch(typeof(Mission))]
        //[HarmonyPatch("OnAgentMount")]
        //internal class OnAgentMountPatch
        //{
        //    private static void Postfix(Agent agent, Mission __instance)
        //    {
        //        if (!agent.IsPlayerControlled && agent.Formation != null && Mission.Current != null && Mission.Current.IsFieldBattle && agent.IsActive())
        //        {
        //            bool isCavFormationActive = agent.Team.GetFormation(FormationClass.Cavalry) != null && agent.Team.GetFormation(FormationClass.Cavalry).CountOfUnits > 0;
        //            bool isHaFormationActive = agent.Team.GetFormation(FormationClass.HorseArcher) != null && agent.Team.GetFormation(FormationClass.HorseArcher).CountOfUnits > 0;
        //            if (agent.Equipment.HasRangedWeapon(WeaponClass.Arrow) || agent.Equipment.HasRangedWeapon(WeaponClass.Bolt))
        //            {
        //                if (agent.Formation != null && isHaFormationActive)
        //                {
        //                    if (agent.IsActive())
        //                    {
        //                        try
        //                        {
        //                            agentsToChangeFormation[agent] = FormationClass.HorseArcher;
        //                            return;
        //                        }
        //                        catch (Exception ex)
        //                        {
        //                            return;
        //                        }
        //                    }
        //                }
        //            }
        //            else
        //            {
        //                if (agent.Formation != null && isCavFormationActive)
        //                {
        //                    if (agent.IsActive())
        //                    {
        //                        try
        //                        {
        //                            agentsToChangeFormation[agent] = FormationClass.Cavalry;
        //                            return;
        //                        }
        //                        catch (Exception ex)
        //                        {
        //                            return;
        //                        }
        //                    }
        //                }
        //            }
        //        }
        //    }
        //}
    }
}