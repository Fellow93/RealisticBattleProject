using HarmonyLib;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TaleWorlds.CampaignSystem.TournamentGames;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static RBMAI.PostureDamage;
using static TaleWorlds.Core.ArmorComponent;
using static TaleWorlds.Core.ItemObject;
using static TaleWorlds.MountAndBlade.Agent;

namespace RBMAI
{
    public class PostureLogic : MissionLogic
    {
        //private static int tickCooldownReset = 30;
        //private static int tickCooldown = 0;
        private static float timeToCalc = 0.5f;
        private static float timeToUpdateAgents = 3f;

        private static float currentDt = 0f;
        private static float currentDtToUpdateAgents = 0f;
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

                            float basePosture = 30f;
                            float athleticBase = 20f;
                            float weaponSkillBase = 80f;
                            float strengthSkillModifier = 500f;
                            float weaponSkillModifier = 500f;
                            float athleticRegenBase = 0.016f;
                            float weaponSkillRegenBase = 0.064f;
                            float baseModifier = 1f;

                            posture.maxPosture = basePosture;
                            if (__instance.HasMount)
                            {
                                int effectiveRidingSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(__instance, DefaultSkills.Riding);
                                posture.maxPosture += (athleticBase * (baseModifier + (effectiveRidingSkill / strengthSkillModifier))) + (weaponSkillBase * (baseModifier + (effectiveWeaponSkill / weaponSkillModifier)));
                                posture.regenPerTick = (athleticRegenBase * (baseModifier + (effectiveRidingSkill / strengthSkillModifier))) + (weaponSkillRegenBase * (baseModifier + (effectiveWeaponSkill / weaponSkillModifier)));
                            }
                            else
                            {
                                int effectiveAthleticSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(__instance, DefaultSkills.Athletics);
                                posture.maxPosture += (athleticBase * (baseModifier + (effectiveAthleticSkill / strengthSkillModifier))) + (weaponSkillBase * (baseModifier + (effectiveWeaponSkill / weaponSkillModifier)));
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

            //how much posture is regained after posture break
            static float postureResetModifier = 0.75f;
            //how much posture is regained after posture break while holding shield
            static float shieldPostureResetModifier = 0.4f;

            public static void ResetPostureForAgent(ref Posture posture, float resetModifier)
            {
                if (posture != null)
                {
                    posture.posture += posture.maxPosture * resetModifier;
                    posture.posture = Math.Max(0f, posture.posture);
                }
            }

            public static void TryToDropWeapon(Agent victimAgent)
            {
                EquipmentIndex wieldedItemIndex = victimAgent.GetPrimaryWieldedItemIndex();
                if (wieldedItemIndex != EquipmentIndex.None)
                {
                    int numOfMeleeWeapons = 0;
                    for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                    {
                        if (victimAgent.Equipment != null && !victimAgent.Equipment[equipmentIndex].IsEmpty)
                        {
                            victimAgent.Equipment[equipmentIndex].GatherInformationFromWeapon(out var weaponHasMelee, out var _, out var _, out var _, out var weaponHasThrown, out var _);
                            if (weaponHasMelee && !weaponHasThrown)
                            {
                                numOfMeleeWeapons++;
                            }
                        }
                    }
                    EquipmentIndex ei = victimAgent.GetPrimaryWieldedItemIndex();
                    if (ei != EquipmentIndex.None && numOfMeleeWeapons > 1)
                    {
                        agentsToDropWeapon.Add(victimAgent);
                        if (!agentsToDropWeapon.Contains(victimAgent))
                        {
                            agentsToDropWeapon.Add(victimAgent);
                        }
                    }

                }
            }

            public static void TryToDropShield(Agent victimAgent)
            {
                if (!agentsToDropShield.Contains(victimAgent))
                {
                    agentsToDropShield.Add(victimAgent);
                }
            }

            public static ActionIndexCache DecideAnimation(AttackCollisionData collisionData, bool isAttacker)
            {
                switch (collisionData.AttackDirection)
                {
                    case UsageDirection.AttackLeft:
                        {
                            if (isAttacker)
                            {
                                return ActionIndexCache.act_stagger_left;
                            }
                            else
                            {
                                return ActionIndexCache.act_stagger_right;
                            }
                        }
                    case UsageDirection.AttackRight:
                        {
                            if (isAttacker)
                            {
                                return ActionIndexCache.act_stagger_right;
                            }
                            else
                            {
                                return ActionIndexCache.act_stagger_left;
                            }
                        }
                    case UsageDirection.AttackUp:
                    case UsageDirection.AttackDown:
                        {
                            if (isAttacker)
                            {
                                return ActionIndexCache.act_stagger_forward;
                            }
                            else
                            {
                                return ActionIndexCache.act_stagger_backward;
                            }
                        }
                    default:
                        {
                            return ActionIndexCache.act_stagger_left;
                        }
                }
            }

            public static void forceStaggerAnimation(Agent agent, AttackCollisionData collisionData, float actionSpeed, bool isAttacker)
            {
                agent.SetActionChannel(agent.HasMount ? 1 : 0, DecideAnimation(collisionData, isAttacker), actionSpeed: actionSpeed);
            }

            public static void forceTiredAnimation(Agent agent, AttackCollisionData collisionData, float actionSpeed, bool isAttacker)
            {
                agent.SetActionChannel(agent.HasMount ? 1 : 0, ActionIndexCache.act_pickup_down_begin_left_stance, actionSpeed: actionSpeed);
            }

            public static void handleDefender(Posture posture, Agent victimAgent, Agent attackerAgent, ref AttackCollisionData collisionData,
                MissionWeapon attackerWeapon, float comHitModifier, ref Blow blow, ref Mission mission,
                float actionModifier, float staggerActionSpeed, bool dropWeapon, bool dropShield,
                bool damageShield, bool stagger, bool resetPosture, MeleeHitType meleeHitType, bool crushThrough, bool isUnarmedAttack)
            {
                if (posture != null)
                {
                    float postureDmg = calculateDefenderPostureDamage(victimAgent, attackerAgent, actionModifier, ref collisionData, attackerWeapon, comHitModifier, meleeHitType, isUnarmedAttack);
                    float postureOverkill = Math.Abs(posture.posture - postureDmg);
                    posture.posture = Math.Max(0f, posture.posture - postureDmg);
                    addPosturedamageVisual(attackerAgent, victimAgent);
                    if (posture.posture <= 0f)
                    {
                        if (postureOverkill >= posture.maxPosture * 0.5f)
                        {
                            if (dropWeapon)
                            {
                                TryToDropWeapon(victimAgent);
                            }
                            if (dropShield)
                            {
                                TryToDropShield(victimAgent);
                            }
                        }
                        if (crushThrough)
                        {
                            int hpDamage = (int)Math.Floor(calculateHealthDamage(attackerWeapon, attackerAgent, victimAgent, postureOverkill, blow, isUnarmedAttack));
                            makePostureCrashThroughBlow(ref mission, blow, attackerAgent, victimAgent, hpDamage, ref collisionData, attackerWeapon);
                            MBTextManager.SetTextVariable("DMG", hpDamage);
                            if (victimAgent.IsPlayerControlled)
                            {
                                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_011}Posture break: Posture depleted, {DMG} damage crushed through").ToString(), Color.FromUint(4282569842u)));
                            }
                            if (attackerAgent.IsPlayerControlled)
                            {
                                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_012}Enemy Posture break: Posture depleted, {DMG} damage crushed through").ToString(), Color.FromUint(4282569842u)));
                            }
                        }
                        if (stagger)
                        {
                            forceStaggerAnimation(victimAgent, collisionData, staggerActionSpeed, false);
                        }
                        if (resetPosture)
                        {
                            ResetPostureForAgent(ref posture, postureResetModifier);
                        }

                    }
                    addPosturedamageVisual(attackerAgent, victimAgent);
                }
            }

            public static void handleAttacker(Posture posture, Agent victimAgent, Agent attackerAgent, ref AttackCollisionData collisionData,
                MissionWeapon attackerWeapon, float comHitModifier, ref Blow blow, ref Mission mission,
                float actionModifier, float staggerActionSpeed, bool dropWeapon, bool stagger, bool resetPosture, bool tired, MeleeHitType meleeHitType, bool isUnarmedAttack)
            {
                if (posture != null)
                {
                    float postureDmg = calculateAttackerPostureDamage(victimAgent, attackerAgent, actionModifier, ref collisionData, attackerWeapon, comHitModifier, meleeHitType, isUnarmedAttack);
                    float postureOverkill = Math.Abs(posture.posture - postureDmg);
                    posture.posture = Math.Max(0f, posture.posture - postureDmg);
                    addPosturedamageVisual(attackerAgent, victimAgent);
                    if (posture.posture <= 0f)
                    {
                        if (postureOverkill >= posture.maxPosture * 0.5f)
                        {
                            if (dropWeapon)
                            {
                                TryToDropWeapon(attackerAgent);
                            }
                        }
                        if (stagger)
                        {
                            forceStaggerAnimation(attackerAgent, collisionData, staggerActionSpeed, true);
                        }
                        if (tired)
                        {
                            forceTiredAnimation(attackerAgent, collisionData, staggerActionSpeed, false);
                        }
                        if (resetPosture)
                        {
                            if (meleeHitType == MeleeHitType.AgentHit)
                            {
                                ResetPostureForAgent(ref posture, 0.33f);
                            }
                            else
                            {
                                ResetPostureForAgent(ref posture, postureResetModifier);
                            }
                        }
                    }
                    addPosturedamageVisual(attackerAgent, victimAgent);
                }
            }

            public static void handleDefenderChamberBlock(Posture defenderPosture, Agent victimAgent, Agent attackerAgent, ref AttackCollisionData collisionData, MissionWeapon attackerWeapon, float comHitModifier, ref Blow blow, ref Mission mission, MeleeHitType meleeHitType)
            {
                float defenderChamberBlockAction = 0.25f;
                defenderPosture.posture = defenderPosture.posture - calculateDefenderPostureDamage(victimAgent, attackerAgent, defenderChamberBlockAction, ref collisionData, attackerWeapon, comHitModifier, meleeHitType, false);
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
                        makePostureBlow(ref mission, blow, attackerAgent, victimAgent, ref collisionData, attackerWeapon, BlowFlags.NonTipThrust);
                    }
                    ResetPostureForAgent(ref defenderPosture, postureResetModifier);
                    addPosturedamageVisual(attackerAgent, victimAgent);
                }
            }

            public static void handleAttackerChamberBlock(Posture attackerPosture, Agent victimAgent, Agent attackerAgent, ref AttackCollisionData collisionData, MissionWeapon attackerWeapon, float comHitModifier, ref Blow blow, ref Mission mission, MeleeHitType meleeHitType)
            {
                float attackerChamberBlockAction = 2f;
                float postureDmg = calculateAttackerPostureDamage(victimAgent, attackerAgent, attackerChamberBlockAction, ref collisionData, attackerWeapon, comHitModifier, meleeHitType, false);
                attackerPosture.posture = attackerPosture.posture - postureDmg;
                addPosturedamageVisual(attackerAgent, victimAgent);
                if (attackerPosture.posture <= 0f)
                {
                    if (attackerAgent.IsPlayerControlled)
                    {
                        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_019}Posture break: Posture depleted, chamber block {DMG} damage crushed through").ToString(), Color.FromUint(4282569842u)));
                    }
                    makePostureCrashThroughBlow(ref mission, blow, attackerAgent, victimAgent, 0, ref collisionData, attackerWeapon);
                    ResetPostureForAgent(ref attackerPosture, postureResetModifier);
                    addPosturedamageVisual(attackerAgent, victimAgent);
                }
                else
                {
                    if (attackerAgent.IsPlayerControlled)
                    {
                        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_020}Chamber block {DMG} damage crushed through").ToString(), Color.FromUint(4282569842u)));
                    }
                    makePostureCrashThroughBlow(ref mission, blow, attackerAgent, victimAgent, 0, ref collisionData, attackerWeapon);
                    ResetPostureForAgent(ref attackerPosture, postureResetModifier);
                    addPosturedamageVisual(attackerAgent, victimAgent);
                }
            }

            private static void Postfix(ref Mission __instance, ref Blow __result, Agent attackerAgent, Agent victimAgent, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, CrushThroughState crushThroughState, Vec3 blowDirection, Vec3 swingDirection, bool cancelDamage)
            {
                //sanity gate
                if (!(new StackTrace()).GetFrame(3).GetMethod().Name.Contains("MeleeHit") || victimAgent == null || !victimAgent.IsHuman ||
                    !RBMConfig.RBMConfig.postureEnabled || attackerAgent == null || victimAgent == null || attackerAgent.IsFriendOf(victimAgent))
                {
                    return;
                }

                Posture defenderPosture = null;
                Posture attackerPosture = null;
                AgentPostures.values.TryGetValue(victimAgent, out defenderPosture);
                AgentPostures.values.TryGetValue(attackerAgent, out attackerPosture);

                bool isUnarmedAttack = false;
                //detect unarmed attack
                if (attackerWeapon.IsEmpty && attackerAgent != null && victimAgent != null && collisionData.DamageType == (int)DamageTypes.Blunt)
                {
                    isUnarmedAttack = true;
                }

                Blow blow = __result;
                Mission mission = __instance;

                //modifier of psoture damage, loser the hit is to COM ( center of mass ), higher the Modifier
                float comHitModifier = isUnarmedAttack ? 1f : Utilities.GetComHitModifier(in collisionData, in attackerWeapon);

                //weapon block
                if (!collisionData.AttackBlockedWithShield)
                {
                    //normal weapon block
                    if (collisionData.CollisionResult == CombatCollisionResult.Blocked)
                    {
                        if (defenderPosture != null)
                        {
                            //handleDefenderWeaponBlock(defenderPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission, shouldPostureBreakEffectApply);
                            handleDefender(defenderPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                actionModifier: 0.85f,
                                stagger: true,
                                crushThrough: true,
                                resetPosture: true,
                                staggerActionSpeed: 0.85f,
                                dropWeapon: true,
                                dropShield: false,
                                damageShield: false,
                                meleeHitType: MeleeHitType.WeaponBlock,
                                isUnarmedAttack: isUnarmedAttack
                                );
                        }
                        if (attackerPosture != null)
                        {
                            //handleAttackerWeaponBlock(attackerPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission, shouldPostureBreakEffectApply);
                            handleAttacker(attackerPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                actionModifier: 0.6f,
                                stagger: true,
                                resetPosture: true,
                                staggerActionSpeed: 0.95f,
                                dropWeapon: true,
                                tired: false,
                                meleeHitType: MeleeHitType.WeaponBlock,
                                isUnarmedAttack: isUnarmedAttack
                                );
                        }
                    }
                    //perfect weapon block
                    else if (collisionData.CollisionResult == CombatCollisionResult.Parried)
                    {
                        if (defenderPosture != null)
                        {
                            //handleDefenderWeaponParry(defenderPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission, shouldPostureBreakEffectApply);
                            handleDefender(defenderPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                actionModifier: 0.5f,
                                stagger: true,
                                crushThrough: true,
                                resetPosture: true,
                                staggerActionSpeed: 0.95f,
                                dropWeapon: true,
                                dropShield: false,
                                damageShield: false,
                                meleeHitType: MeleeHitType.WeaponParry,
                                isUnarmedAttack: isUnarmedAttack
                                );
                        }
                        if (attackerPosture != null)
                        {
                            //handleAttackerWeaponParry(attackerPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission, shouldPostureBreakEffectApply);
                            handleAttacker(attackerPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                actionModifier: 0.75f,
                                stagger: true,
                                resetPosture: true,
                                staggerActionSpeed: 0.85f,
                                dropWeapon: true,
                                tired: false,
                                meleeHitType: MeleeHitType.WeaponParry,
                                isUnarmedAttack: isUnarmedAttack
                                );
                        }
                    }
                    //direct hit
                    else if (victimAgent.IsHuman && attackerAgent.IsHuman && collisionData.CollisionResult == CombatCollisionResult.StrikeAgent)
                    {
                        if (defenderPosture != null)
                        {
                            //handleDefenderDirectHit(defenderPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission);
                            handleDefender(defenderPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                actionModifier: 0.75f,
                                stagger: false,
                                crushThrough: false,
                                resetPosture: false,
                                staggerActionSpeed: 0.85f,
                                dropWeapon: false,
                                dropShield: false,
                                damageShield: false,
                                meleeHitType: MeleeHitType.AgentHit,
                                isUnarmedAttack: isUnarmedAttack
                                );
                        }
                        if (attackerPosture != null)
                        {
                            //handleAttackerDirectHit(attackerPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission, shouldPostureBreakEffectApply);
                            handleAttacker(attackerPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                actionModifier: 0.5f,
                                stagger: false,
                                resetPosture: true,
                                staggerActionSpeed: 1f,
                                dropWeapon: false,
                                tired: true,
                                meleeHitType: MeleeHitType.AgentHit,
                                isUnarmedAttack: isUnarmedAttack
                                );
                        }
                    }
                }
                //shield block
                else if (collisionData.AttackBlockedWithShield)
                {
                    //bad shield block
                    if (collisionData.CollisionResult == CombatCollisionResult.Blocked && !collisionData.CorrectSideShieldBlock)
                    {
                        if (defenderPosture != null)
                        {
                            //handleDefenderShieldBlockBad(defenderPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission, shouldPostureBreakEffectApply);
                            handleDefender(defenderPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                actionModifier: 1f,
                                stagger: true,
                                resetPosture: true,
                                crushThrough: false,
                                staggerActionSpeed: 0.85f,
                                dropWeapon: false,
                                dropShield: true,
                                damageShield: true,
                                meleeHitType: MeleeHitType.ShieldIncorrectBlock,
                                isUnarmedAttack: isUnarmedAttack
                                );
                        }
                        if (attackerPosture != null)
                        {
                            //handleAttackerShieldBlockBad(attackerPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission, shouldPostureBreakEffectApply);
                            handleAttacker(attackerPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                actionModifier: 0.4f,
                                stagger: false,
                                resetPosture: true,
                                staggerActionSpeed: 1f,
                                dropWeapon: false,
                                tired: true,
                                meleeHitType: MeleeHitType.ShieldIncorrectBlock,
                                isUnarmedAttack: isUnarmedAttack
                                );
                        }
                    }
                    //normal shield block
                    else if ((collisionData.CollisionResult == CombatCollisionResult.Blocked && collisionData.CorrectSideShieldBlock) || (collisionData.CollisionResult == CombatCollisionResult.Parried && !collisionData.CorrectSideShieldBlock))
                    {
                        if (defenderPosture != null)
                        {
                            //handleDefenderShieldBlockNormal(defenderPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission, shouldPostureBreakEffectApply);
                            handleDefender(defenderPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                actionModifier: 0.9f,
                                stagger: true,
                                crushThrough: false,
                                resetPosture: true,
                                staggerActionSpeed: 0.9f,
                                dropWeapon: false,
                                dropShield: true,
                                damageShield: true,
                                meleeHitType: MeleeHitType.ShieldBlock,
                                isUnarmedAttack: isUnarmedAttack
                                );
                        }
                        if (attackerPosture != null)
                        {
                            //handleAttackerShieldBlockNormal(attackerPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission, shouldPostureBreakEffectApply);
                            handleAttacker(attackerPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                actionModifier: 0.5f,
                                stagger: true,
                                resetPosture: true,
                                staggerActionSpeed: 0.9f,
                                dropWeapon: false,
                                tired: false,
                                meleeHitType: MeleeHitType.ShieldBlock,
                                isUnarmedAttack: isUnarmedAttack
                                );
                        }
                    }
                    //parry shield block
                    else if (collisionData.CollisionResult == CombatCollisionResult.Parried && collisionData.CorrectSideShieldBlock)
                    {
                        if (defenderPosture != null)
                        {
                            //handleDefenderShieldBlockParry(defenderPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission, shouldPostureBreakEffectApply);
                            handleDefender(defenderPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                actionModifier: 0.8f,
                                stagger: true,
                                crushThrough: false,
                                resetPosture: true,
                                staggerActionSpeed: 0.95f,
                                dropWeapon: false,
                                dropShield: true,
                                damageShield: true,
                                meleeHitType: MeleeHitType.ShieldParry,
                                isUnarmedAttack: isUnarmedAttack
                                );
                        }
                        if (attackerPosture != null)
                        {
                            //handleAttackerShieldBlockParry(attackerPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission, shouldPostureBreakEffectApply);
                            handleAttacker(attackerPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                actionModifier: 0.8f,
                                stagger: true,
                                resetPosture: true,
                                staggerActionSpeed: 0.85f,
                                dropWeapon: true,
                                tired: false,
                                meleeHitType: MeleeHitType.ShieldParry,
                                isUnarmedAttack: isUnarmedAttack
                                );
                        }
                    }
                }
                //chamber block
                else if (collisionData.CollisionResult == CombatCollisionResult.ChamberBlocked)
                {
                    if (defenderPosture != null)
                    {
                        handleDefenderChamberBlock(defenderPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission, MeleeHitType.ChamberBlock);
                    }
                    if (attackerPosture != null)
                    {
                        handleAttackerChamberBlock(attackerPosture, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission, MeleeHitType.ChamberBlock);


                    }
                }
            }

            private static void applyShieldDamage(Agent victim, int ammount)
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

            private static float calculateDefenderPostureDamage(Agent defenderAgent, Agent attackerAgent, float actionTypeDamageModifier, ref AttackCollisionData collisionData, MissionWeapon weapon, float comHitModifier, MeleeHitType meleeHitType, bool isUnarmedAttack)
            {
                float result = 0f;
                float defenderPostureDamageModifier = 1f; // terms and conditions may apply
                float strengthSkillModifier = 500f;
                float weaponSkillModifier = 500f;

                float basePostureDamage = getDefenderPostureDamage(defenderAgent, attackerAgent, collisionData.AttackDirection, (StrikeType)collisionData.StrikeType, meleeHitType);
                actionTypeDamageModifier = 1f;

                SkillObject attackerWeaponSkill = isUnarmedAttack ? null : WeaponComponentData.GetRelevantSkillFromWeaponClass(weapon.CurrentUsageItem.WeaponClass);
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
                }
                if (defenderAgent.HasMount)
                {
                    defenderEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(defenderAgent, DefaultSkills.Riding);
                }
                else
                {
                    defenderEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(defenderAgent, DefaultSkills.Athletics);
                }

                if (isUnarmedAttack)
                {
                    attackerEffectiveWeaponSkill = defenderEffectiveStrengthSkill;
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
                //float lwrpostureModifier = calculateDefenderLWRPostureModifier(attackerAgent, defenderAgent, attackerWeaponLength, defenderWeaponLength, attackerWeaponWeight, attackBlockedByOneHanedWithoutShield, collisionData.AttackBlockedWithShield);
                float skillModifier = (1f + attackerEffectiveStrengthSkill + attackerEffectiveWeaponSkill) / (1f + defenderEffectiveStrengthSkill + defenderEffectiveWeaponSkill);
                float aditiveSpeedModifier = getRelativeSpeedPostureModifier(attackerAgent, defenderAgent);
                basePostureDamage = (basePostureDamage + aditiveSpeedModifier) * skillModifier;

                //actionTypeDamageModifier += actionTypeDamageModifier * 0.5f * comHitModifier;
                result = basePostureDamage * actionTypeDamageModifier * defenderPostureDamageModifier * comHitModifier;
                //InformationManager.DisplayMessage(new InformationMessage("Deffender PD: " + result));
                return result;
            }

            private static float calculateAttackerPostureDamage(Agent defenderAgent, Agent attackerAgent, float actionTypeDamageModifier, ref AttackCollisionData collisionData, MissionWeapon weapon, float comHitModifier, MeleeHitType meleeHitType, bool isUnarmedAttack)
            {
                float result = 0f;

                float strengthSkillModifier = 500f;
                float weaponSkillModifier = 500f;

                float basePostureDamage = getAttackerPostureDamage(defenderAgent, attackerAgent, collisionData.AttackDirection, (StrikeType)collisionData.StrikeType, meleeHitType);
                actionTypeDamageModifier = 1f;

                SkillObject attackerWeaponSkill = isUnarmedAttack ? null : WeaponComponentData.GetRelevantSkillFromWeaponClass(weapon.CurrentUsageItem.WeaponClass);

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
                }
                if (defenderAgent.HasMount)
                {
                    defenderEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(defenderAgent, DefaultSkills.Riding);
                }
                else
                {
                    defenderEffectiveStrengthSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(defenderAgent, DefaultSkills.Athletics);
                }

                if (isUnarmedAttack)
                {
                    attackerEffectiveWeaponSkill = defenderEffectiveStrengthSkill;
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

                //float lwrpostureModifier = calculateAttackerLWRPostureModifier(attackerAgent, defenderAgent, attackerWeaponLength, defenderWeaponLength, attackerWeaponWeight, defenderWeaponWeight, attackBlockedByOneHanedWithoutShield, collisionData.AttackBlockedWithShield);
                float skillModifier = (1f + defenderEffectiveStrengthSkill + defenderEffectiveWeaponSkill) / (1f + attackerEffectiveStrengthSkill + attackerEffectiveWeaponSkill);
                float aditiveSpeedModifier = getRelativeSpeedPostureModifier(attackerAgent, defenderAgent);
                basePostureDamage = (basePostureDamage + aditiveSpeedModifier) * skillModifier;

                //actionTypeDamageModifier += actionTypeDamageModifier * 0.5f * comHitModifier;
                result = basePostureDamage * actionTypeDamageModifier * comHitModifier;
                //InformationManager.DisplayMessage(new InformationMessage("Attacker PD: " + result));
                return result;
            }

            public static float getRelativeSpeedPostureModifier(Agent attackerAgent, Agent defenderAgent)
            {
                float retVal = 0f;
                float relativeSpeed = (defenderAgent.Velocity - attackerAgent.Velocity).Length;
                if (relativeSpeed > 0f)
                {
                    retVal = relativeSpeed * 4f;
                }
                return retVal;
            }

            //public static float calculateDefenderLWRPostureModifier(
            //    Agent attackerAgent, Agent defenderAgent,
            //    float attackerWeaponLength, float defenderWeaponLength, float attackerWeaponWeight, bool attackBlockedByOneHanded, bool attackBlockedByShield)
            //{
            //    float relativeSpeed = (defenderAgent.Velocity - attackerAgent.Velocity).Length * relativeSpeedPostureFactor;
            //    if (attackBlockedByShield)
            //    {
            //        return 1f + ((attackerWeaponWeight / 2f) + relativeSpeed) / 4f;
            //    }
            //    else
            //    {
            //        if (attackBlockedByOneHanded)
            //        {
            //            return 1f + ((attackerWeaponWeight / 2f) + relativeSpeed) / 2f;
            //        }
            //        else
            //        {
            //            return 1f + ((attackerWeaponWeight / 2f) + relativeSpeed) / 3f;
            //        }
            //    }
            //}

            //public static float calculateAttackerLWRPostureModifier(
            //    Agent attackerAgent, Agent defenderAgent,
            //    float attackerWeaponLength, float defenderWeaponLength,
            //    float attackerWeaponWeight, float defenderWeaponWeight, bool attackBlockedByOneHanded, bool attackBlockedByShield)
            //{
            //    float relativeSpeed = (defenderAgent.Velocity - attackerAgent.Velocity).Length * relativeSpeedPostureFactor;
            //    if (attackBlockedByShield)
            //    {
            //        return 1f + (relativeSpeed) / 2f;
            //    }
            //    else
            //    {
            //        if (attackBlockedByOneHanded)
            //        {
            //            return 1f + (relativeSpeed) / 4f;
            //        }
            //        else
            //        {
            //            return 1f + (relativeSpeed) / 3f;
            //        }
            //    }
            //}

            private static void makePostureRiposteBlow(ref Mission mission, Blow blow, Agent attackerAgent, Agent victimAgent, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, BlowFlags addedBlowFlag)
            {
                Blow newBLow = blow;
                newBLow.BaseMagnitude = collisionData.BaseMagnitude;
                newBLow.MovementSpeedDamageModifier = collisionData.MovementSpeedDamageModifier;
                newBLow.InflictedDamage = 0;
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
                newBLow.Direction = blow.Direction;
                newBLow.SwingDirection = blow.SwingDirection;
                //blow.InflictedDamage = 1;
                newBLow.VictimBodyPart = collisionData.VictimHitBodyPart;
                //newBLow.BlowFlag |= addedBlowFlag;
                attackerAgent.RegisterBlow(newBLow, collisionData);
                foreach (MissionBehavior missionBehaviour in mission.MissionBehaviors)
                {
                    missionBehaviour.OnRegisterBlow(victimAgent, attackerAgent, WeakGameEntity.Invalid, newBLow, ref collisionData, in attackerWeapon);
                }
                attackerAgent.SetActionChannel(0, ActionIndexCache.act_stagger_left, actionSpeed: 0.9f);
            }

            private static void makePostureBlow(ref Mission mission, Blow blow, Agent attackerAgent, Agent victimAgent, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, BlowFlags addedBlowFlag)
            {
                Blow newBLow = blow;
                newBLow.BaseMagnitude = collisionData.BaseMagnitude;
                newBLow.MovementSpeedDamageModifier = collisionData.MovementSpeedDamageModifier;
                newBLow.SelfInflictedDamage = collisionData.SelfInflictedDamage;
                newBLow.AbsorbedByArmor = collisionData.AbsorbedByArmor;
                newBLow.InflictedDamage = 0;
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
                newBLow.Direction = blow.Direction;
                newBLow.SwingDirection = blow.SwingDirection;
                newBLow.VictimBodyPart = collisionData.VictimHitBodyPart;
                //newBLow.BlowFlag |= addedBlowFlag;
                victimAgent.RegisterBlow(newBLow, collisionData);
                foreach (MissionBehavior missionBehaviour in mission.MissionBehaviors)
                {
                    missionBehaviour.OnRegisterBlow(attackerAgent, victimAgent, WeakGameEntity.Invalid, newBLow, ref collisionData, in attackerWeapon);
                }
                victimAgent.SetActionChannel(0, ActionIndexCache.act_stagger_left, actionSpeed: 0.9f);
            }

            private static void makePostureCrashThroughBlow(ref Mission mission, Blow blow, Agent attackerAgent, Agent victimAgent, int hpDamage, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon)
            {
                Blow newBLow = blow;
                newBLow.BaseMagnitude = collisionData.BaseMagnitude;
                newBLow.MovementSpeedDamageModifier = collisionData.MovementSpeedDamageModifier;
                newBLow.InflictedDamage = hpDamage;
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
                newBLow.Direction = blow.Direction;
                newBLow.SwingDirection = blow.SwingDirection;
                newBLow.VictimBodyPart = collisionData.VictimHitBodyPart;
                victimAgent.RegisterBlow(newBLow, collisionData);
                foreach (MissionBehavior missionBehaviour in mission.MissionBehaviors)
                {
                    missionBehaviour.OnRegisterBlow(attackerAgent, victimAgent, WeakGameEntity.Invalid, newBLow, ref collisionData, in attackerWeapon);
                }
                //victimAgent.SetActionChannel(0, ActionIndexCache.act_stagger_left, actionSpeed: 0.9f);
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

            public static float calculateHealthDamage(MissionWeapon targetWeapon, Agent attacker, Agent victimAgent, float overPostureDamage, Blow b, bool isUnarmedAttack)
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
                float threshold = 20f;

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

                    if (currentSelectedChar != null && isUnarmedAttack)
                    {
                        int realDamage = 0;
                        int effectiveSkill = currentSelectedChar.GetSkillValue(DefaultSkills.Athletics);
                        float effectiveSkillDR = Utilities.GetEffectiveSkillWithDR(effectiveSkill);
                        float skillModifier = Utilities.CalculateSkillModifier(effectiveSkill);

                        float magnitude = 1f;

                        if (isUnarmedAttack)
                        {
                            ArmorMaterialTypes gauntletMaterial = Utilities.getArmArmorMaterial(attacker);
                            switch (gauntletMaterial)
                            {
                                case ArmorMaterialTypes.None:
                                    {
                                        magnitude *= 0.25f;
                                        break;
                                    }
                                case ArmorMaterialTypes.Cloth:
                                    {
                                        magnitude *= 0.4f;
                                        break;
                                    }
                                case ArmorMaterialTypes.Leather:
                                    {
                                        magnitude *= 0.5f;
                                        break;
                                    }
                                case ArmorMaterialTypes.Chainmail:
                                    {
                                        magnitude *= 0.75f;
                                        break;
                                    }
                                case ArmorMaterialTypes.Plate:
                                    {
                                        magnitude *= 1f;
                                        break;
                                    }
                            }
                            float gauntletWeight = Utilities.getGauntletWeight(attacker);
                            magnitude += gauntletWeight;
                        }

                        float skillBasedDamage = Utilities.GetSkillBasedDamage(magnitude, false, "unarmedAttack", DamageTypes.Blunt, effectiveSkillDR, skillModifier, StrikeType.Swing, 5f);

                        realDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage("unarmedAttack", DamageTypes.Blunt, skillBasedDamage, armorSumPosture, 1f, out float penetratedDamage, out float bluntForce, swingDamageFactor, null, false)), 0, 2000);
                        realDamage = MathF.Floor(realDamage * 1f);
                        if (overPostureDamage > threshold)
                        {
                            return realDamage;
                        }
                        else
                        {
                            return realDamage * (overPostureDamage / threshold);
                        }
                    }

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
                            int realDamage = 0;
                            if (b.StrikeType == StrikeType.Swing)
                            {
                                float sweetSpotMagnitude = CalculateSweetSpotSwingMagnitude(currentSelectedChar, targetWeapon, targetWeaponUsageIndex, effectiveSkill);

                                float skillBasedDamage = Utilities.GetSkillBasedDamage(sweetSpotMagnitude, false, targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(),
                                    targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).SwingDamageType, effectiveSkillDR, skillModifier, StrikeType.Swing, targetWeapon.Item.Weight);


                                swingDamageFactor = (float)Math.Sqrt(Utilities.getSwingDamageFactor(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex), targetWeapon.ItemModifier));


                                realDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(), targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).SwingDamageType, skillBasedDamage, armorSumPosture, 1f, out float penetratedDamage, out float bluntForce, swingDamageFactor, null, false)), 0, 2000);
                                realDamage = MathF.Floor(realDamage * 1f);
                            }
                            else
                            {
                                float thrustMagnitude = CalculateThrustMagnitude(currentSelectedChar, targetWeapon, targetWeaponUsageIndex, effectiveSkill);

                                float skillBasedDamage = Utilities.GetSkillBasedDamage(thrustMagnitude, false, targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(),
                                    targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).ThrustDamageType, effectiveSkillDR, skillModifier, StrikeType.Thrust, targetWeapon.Item.Weight);


                                thrustDamageFactor = (float)Math.Sqrt(Utilities.getThrustDamageFactor(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex), targetWeapon.ItemModifier));

                                realDamage = MBMath.ClampInt(MathF.Floor(Utilities.RBMComputeDamage(targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).WeaponClass.ToString(),
                                targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).ThrustDamageType, skillBasedDamage, armorSumPosture, 1f, out float penetratedDamage, out float bluntForce, thrustDamageFactor, null, false)), 0, 2000);
                                realDamage = MathF.Floor(realDamage * 1f);
                            }
                            if (overPostureDamage > threshold)
                            {
                                return realDamage;
                            }
                            else
                            {
                                return realDamage * (overPostureDamage / threshold);
                            }
                        }
                    }
                }

                int weaponDamage = 0;
                if (b.StrikeType == StrikeType.Swing)
                {
                    weaponDamage = isUnarmedAttack ? 4 : targetWeapon.GetModifiedSwingDamageForCurrentUsage();
                }
                else
                {
                    weaponDamage = isUnarmedAttack ? 4 : targetWeapon.GetModifiedThrustDamageForCurrentUsage();
                }

                int hpDamage = MBMath.ClampInt(MathF.Ceiling(MissionGameModels.Current.StrikeMagnitudeModel.ComputeRawDamage(b.DamageType, weaponDamage, armorSumPosture, 1f)), 0, 2000);
                if (overPostureDamage > threshold)
                {
                    return hpDamage;
                }
                else
                {
                    return hpDamage * (overPostureDamage / threshold);
                }
            }

            public static float calculateRangedPostureLoss(float fixedPS, float dynamicPS, Agent shooterAgent, WeaponClass wc)
            {
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
                        if (shooterPosture != null)
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
                                ResetPostureForAgent(ref shooterPosture, postureResetModifier);
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

        //public void handlePostureLevelEffects(Agent agent, Posture posture)
        //{
        //    agent.UpdateAgentStats();
        //}

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (RBMConfig.RBMConfig.postureEnabled && Mission.Current.AllowAiTicking)
            {
                if (currentDtToUpdateAgents < timeToUpdateAgents)
                {
                    currentDtToUpdateAgents += dt;
                }
                else
                {
                    foreach (var agent in Mission.Current.Agents.Where((Agent a) => a.IsActive() && a.IsHuman))
                    {
                        agent.UpdateAgentStats();
                    }
                    currentDtToUpdateAgents = 0f;
                }
                if (currentDt < timeToCalc)
                {
                    currentDt += dt;
                }
                else
                {
                    MBArrayList<Agent> inactiveAgents = new MBArrayList<Agent>();
                    foreach (KeyValuePair<Agent, Posture> entry in AgentPostures.values)
                    {
                        if (entry.Key != null && entry.Key.Mission != null && !entry.Key.IsActive())
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
                    foreach (Agent agent in inactiveAgents)
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
                    foreach (Agent agent in agentsAbleToDropShield)
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