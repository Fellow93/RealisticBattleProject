using HarmonyLib;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
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
        private static float timeToCalc = 0.5f;
        private static float timeToCalcStaminaHealth = 10f;
        private static float timeToUpdateAgents = 3f;

        private static float currentDt = 0f;
        private static float currentDtToUpdateAgents = 0f;
        private static float currentDtToUpdateStaminaHealth = 0f;

        public static MBArrayList<Agent> agentsToDropShield = new MBArrayList<Agent> { };
        public static MBArrayList<Agent> agentsToDropWeapon = new MBArrayList<Agent> { };
        public static Dictionary<Agent, FormationClass> agentsToChangeFormation = new Dictionary<Agent, FormationClass> { };

        private readonly MBArrayList<Agent> _inactiveAgentsBuffer = new MBArrayList<Agent>();
        private readonly MBArrayList<Agent> _dropShieldBuffer = new MBArrayList<Agent>();
        private readonly MBArrayList<Agent> _dropWeaponBuffer = new MBArrayList<Agent>();

        public static void TryToDropShield(Agent victimAgent)
        {
            if (!agentsToDropShield.Contains(victimAgent))
            {
                agentsToDropShield.Add(victimAgent);
            }
        }

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

                                AgentPostures.postureVisual._dataSource.PlayerStamina = (int)posture.stamina;
                                AgentPostures.postureVisual._dataSource.PlayerStaminaMax = (int)posture.maxStamina;
                                AgentPostures.postureVisual._dataSource.PlayerStaminaText = ((int)posture.stamina).ToString();
                                AgentPostures.postureVisual._dataSource.PlayerStaminaMaxText = ((int)posture.maxStamina).ToString();
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

                                AgentPostures.postureVisual._dataSource.PlayerStamina = (int)posture.stamina;
                                AgentPostures.postureVisual._dataSource.PlayerStaminaMax = (int)posture.maxStamina;
                                AgentPostures.postureVisual._dataSource.PlayerStaminaText = ((int)posture.stamina).ToString();
                                AgentPostures.postureVisual._dataSource.PlayerStaminaMaxText = ((int)posture.maxStamina).ToString();
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

        [ThreadStatic]
        private static bool _inMeleeHitContext;

        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("MeleeHitCallback")]
        private class MeleeHitContextPatch
        {
            private static void Prefix() => _inMeleeHitContext = true;
            private static void Finalizer() => _inMeleeHitContext = false;
        }

        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("EquipItemsFromSpawnEquipment")]
        private class EquipItemsFromSpawnEquipmentPatch
        {
            private static void Prefix(ref Agent __instance)
            {
                if (__instance.IsHuman)
                {
                    AgentPostures.values[__instance] = new Posture();
                    Posture posture = AgentPostures.values[__instance];
                    float athleticBase = 1000f;
                    int effectiveAthleticSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(__instance, DefaultSkills.Athletics);
                    float athleticSkillModifier = 500f;
                    posture.maxStamina = athleticBase * (1f + (effectiveAthleticSkill / athleticSkillModifier));
                    posture.stamina = athleticBase * (1f + (effectiveAthleticSkill / athleticSkillModifier));
                    posture.staminaRegenPerTick = 0.02f * (1f + (effectiveAthleticSkill / athleticSkillModifier));
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
                        float athleticBase = 1000f;
                        int effectiveAthleticSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(__instance, DefaultSkills.Athletics);
                        float athleticSkillModifier = 500f;
                        posture = AgentPostures.values[__instance];
                        posture.maxStamina = athleticBase * (1f + (effectiveAthleticSkill / athleticSkillModifier));
                        posture.stamina = athleticBase * (1f + (effectiveAthleticSkill / athleticSkillModifier));
                        posture.staminaRegenPerTick = 0.02f * (1f + (effectiveAthleticSkill / athleticSkillModifier));
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

                            //armor weight effect
                            float armorWeight = Math.Max(0f, __instance.SpawnEquipment.GetTotalWeightOfArmor(true) - 5f);
                            posture.maxPosture += armorWeight;

                            //face armor effect
                            float faceArmor = 0f;
                            if (!__instance.SpawnEquipment[EquipmentIndex.Head].IsEmpty)
                            {
                                faceArmor = __instance.SpawnEquipment[EquipmentIndex.Head].GetModifiedBodyArmor();
                            }
                            if (faceArmor >= 30f)
                            {
                                posture.staminaRegenPerTick *= 0.5f;
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
                        if (!agentsToDropWeapon.Contains(victimAgent))
                        {
                            agentsToDropWeapon.Add(victimAgent);
                        }
                    }

                }
            }

            public static void handleDefender(Posture posture, Agent victimAgent, Agent attackerAgent, ref AttackCollisionData collisionData,
                MissionWeapon attackerWeapon, float comHitModifier, ref Blow blow, ref Mission mission,
                float actionModifier, float staggerActionSpeed, bool dropWeapon, bool dropShield,
                bool damageShield, bool stagger, bool resetPosture, MeleeHitType meleeHitType, bool crushThrough, bool isUnarmedAttack)
            {
                if (posture != null)
                {
                    float postureDmg = calculateDefenderPostureDamage(victimAgent, attackerAgent, actionModifier, ref collisionData, attackerWeapon, comHitModifier, meleeHitType, isUnarmedAttack);

                    if (meleeHitType == MeleeHitType.AgentHit)
                    {
                        postureDmg = blow.InflictedDamage;
                    }

                    //stamina effect
                    float staminaLevel = posture.stamina / posture.maxStamina;
                    postureDmg *= MBMath.Lerp(1.25f, 1f, staminaLevel);

                    float postureOverkill = Math.Abs(posture.posture - postureDmg);
                    posture.posture = Math.Max(0f, posture.posture - postureDmg);

                    int effectiveAthleticSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(victimAgent, DefaultSkills.Athletics);
                    float athlethicModifier = effectiveAthleticSkill / 20;
                    float victimAgentArmorWeight = Math.Max(0f, victimAgent.SpawnEquipment.GetTotalWeightOfArmor(true) - athlethicModifier);
                    float staminaDamage = postureDmg * (1f + victimAgentArmorWeight / 50f);
                    if (meleeHitType == MeleeHitType.AgentHit)
                    {
                        staminaDamage = postureDmg;
                    }

                    posture.stamina = Math.Max(0f, posture.stamina - staminaDamage);

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
                }
            }

            public static void handleAttacker(Posture posture, Agent victimAgent, Agent attackerAgent, ref AttackCollisionData collisionData,
                MissionWeapon attackerWeapon, float comHitModifier, ref Blow blow, ref Mission mission,
                float actionModifier, float staggerActionSpeed, bool dropWeapon, bool stagger, bool resetPosture, bool tired, MeleeHitType meleeHitType, bool isUnarmedAttack)
            {
                if (posture != null)
                {
                    float postureDmg = calculateAttackerPostureDamage(victimAgent, attackerAgent, actionModifier, ref collisionData, attackerWeapon, comHitModifier, meleeHitType, isUnarmedAttack);

                    //stamina effect
                    float staminaLevel = posture.stamina / posture.maxStamina;
                    postureDmg *= MBMath.Lerp(1.25f, 1f, staminaLevel);

                    float postureOverkill = Math.Abs(posture.posture - postureDmg);
                    posture.posture = Math.Max(0f, posture.posture - postureDmg);

                    int effectiveAthleticSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attackerAgent, DefaultSkills.Athletics);
                    float athlethicModifier = effectiveAthleticSkill / 20;
                    float attackerAgentArmorWeight = Math.Max(0f, attackerAgent.SpawnEquipment.GetTotalWeightOfArmor(true) - athlethicModifier);
                    float staminaDamage = postureDmg * (1f + attackerAgentArmorWeight / 50f);
                    posture.stamina = Math.Max(0f, posture.stamina - staminaDamage);

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
                }
            }

            private static void Postfix(ref Mission __instance, ref Blow __result, Agent attackerAgent, Agent victimAgent, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, CrushThroughState crushThroughState, Vec3 blowDirection, Vec3 swingDirection, bool cancelDamage)
            {
                //sanity gate
                if (!_inMeleeHitContext || victimAgent == null || !victimAgent.IsHuman ||
                    !RBMConfig.RBMConfig.postureEnabled || attackerAgent == null || attackerAgent.IsFriendOf(victimAgent))
                {
                    return;
                }

                Posture defenderPosture = null;
                Posture attackerPosture = null;
                AgentPostures.values.TryGetValue(victimAgent, out defenderPosture);
                AgentPostures.values.TryGetValue(attackerAgent, out attackerPosture);

                bool isUnarmedAttack = false;
                //detect unarmed attack
                if (attackerWeapon.IsEmpty && attackerAgent != null && victimAgent != null && collisionData.DamageType == (int)DamageTypes.Blunt && !collisionData.IsFallDamage && !collisionData.IsHorseCharge)
                {
                    isUnarmedAttack = true;
                }

                Blow blow = __result;
                Mission mission = __instance;

                //modifier of posture damage, closer the hit is to COM ( center of mass ), higher the Modifier
                float comHitModifier = isUnarmedAttack ? 1f : Utilities.GetComHitModifier(in collisionData, in attackerWeapon);

                //chamber block
                if (collisionData.CollisionResult == CombatCollisionResult.ChamberBlocked)
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
                //weapon block
                else if (!collisionData.AttackBlockedWithShield)
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
                else
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
            }

            private static void applyShieldDamage(Agent victim, int amount)
            {
                for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                {
                    if (victim.Equipment != null && !victim.Equipment[equipmentIndex].IsEmpty)
                    {
                        if (victim.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Shield && !victim.WieldedOffhandWeapon.IsEmpty && victim.WieldedOffhandWeapon.Item.Id == victim.Equipment[equipmentIndex].Item.Id)
                        {
                            int num = MathF.Max(0, victim.Equipment[equipmentIndex].HitPoints - amount);
                            victim.ChangeWeaponHitPoints(equipmentIndex, (short)num);
                            break;
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

                float skillModifier = (1f + attackerEffectiveStrengthSkill + attackerEffectiveWeaponSkill) / (1f + defenderEffectiveStrengthSkill + defenderEffectiveWeaponSkill);
                float additiveSpeedModifier = getRelativeSpeedPostureModifier(attackerAgent, defenderAgent);
                basePostureDamage = (basePostureDamage + additiveSpeedModifier) * skillModifier;

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

                float skillModifier = (1f + defenderEffectiveStrengthSkill + defenderEffectiveWeaponSkill) / (1f + attackerEffectiveStrengthSkill + attackerEffectiveWeaponSkill);
                float additiveSpeedModifier = getRelativeSpeedPostureModifier(attackerAgent, defenderAgent);
                basePostureDamage = (basePostureDamage + additiveSpeedModifier) * skillModifier;

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

                dynamicPostureLoss -= Math.Max(0f, 1f - (attackerEffectiveWeaponSkill / 200f)) * (dynamicPS * 0.5f);
                dynamicPostureLoss -= Math.Max(0f, 1f - (attackerEffectiveStrengthSkill / 200f)) * (dynamicPS * 0.5f);

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
                            float postureLoss = 0f;
                            switch (wc)
                            {
                                case WeaponClass.Bow:
                                    {
                                        postureLoss = calculateRangedPostureLoss(35f, 25f, shooterAgent, wc);
                                        break;
                                    }
                                case WeaponClass.Crossbow:
                                    {
                                        postureLoss = calculateRangedPostureLoss(5f, 5f, shooterAgent, wc);
                                        break;
                                    }
                                case WeaponClass.Javelin:
                                    {
                                        postureLoss = calculateRangedPostureLoss(25f, 25f, shooterAgent, wc);
                                        break;
                                    }
                                case WeaponClass.ThrowingAxe:
                                case WeaponClass.ThrowingKnife:
                                    {

                                        postureLoss = calculateRangedPostureLoss(25f, 25f, shooterAgent, wc);
                                        break;
                                    }
                            }

                            shooterPosture.stamina = Math.Max(0f, shooterPosture.stamina - postureLoss);
                            if (shooterPosture.posture - postureLoss <= 0f)
                            {
                                shooterPosture.posture = 0f;
                                float postureResetModifier = 0.5f;
                                ResetPostureForAgent(ref shooterPosture, postureResetModifier);
                            }
                            else
                            {
                                shooterPosture.posture -= postureLoss;
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

                                AgentPostures.postureVisual._dataSource.PlayerStamina = (int)entry.Value.stamina;
                                AgentPostures.postureVisual._dataSource.PlayerStaminaMax = (int)entry.Value.maxStamina;
                                AgentPostures.postureVisual._dataSource.PlayerStaminaText = ((int)entry.Value.stamina).ToString();
                                AgentPostures.postureVisual._dataSource.PlayerStaminaMaxText = ((int)entry.Value.maxStamina).ToString();
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
                if (currentDtToUpdateAgents < timeToUpdateAgents)
                {
                    currentDtToUpdateAgents += dt;
                }
                else
                {
                    foreach (Agent agent in Mission.Current.Agents)
                    {
                        if (agent.IsActive() && agent.IsHuman)
                        {
                            agent.UpdateAgentStats();
                        }
                    }
                    currentDtToUpdateAgents = 0f;
                }

                currentDtToUpdateStaminaHealth += dt;

                if (currentDt < timeToCalc)
                {
                    currentDt += dt;
                }
                else
                {
                    _inactiveAgentsBuffer.Clear();
                    foreach (KeyValuePair<Agent, Posture> entry in AgentPostures.values)
                    {
                        if (entry.Key != null && entry.Key.Mission != null && !entry.Key.IsActive())
                        {
                            _inactiveAgentsBuffer.Add(entry.Key);
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

                                AgentPostures.postureVisual._dataSource.PlayerStamina = (int)entry.Value.stamina;
                                AgentPostures.postureVisual._dataSource.PlayerStaminaMax = (int)entry.Value.maxStamina;
                                AgentPostures.postureVisual._dataSource.PlayerStaminaText = ((int)entry.Value.stamina).ToString();
                                AgentPostures.postureVisual._dataSource.PlayerStaminaMaxText = ((int)entry.Value.maxStamina).ToString();
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
                        if (entry.Value.stamina < entry.Value.maxStamina)
                        {
                            entry.Value.stamina += entry.Value.staminaRegenPerTick * 30f;
                        }
                        float staminaLevel = entry.Value.stamina / entry.Value.maxStamina;
                        if (currentDtToUpdateStaminaHealth > timeToCalcStaminaHealth)
                        {
                            if (staminaLevel > 0.85f)
                            {
                                entry.Key.Health = Math.Min(entry.Key.HealthLimit, entry.Key.Health + 0.9f);
                            }
                        }
                    }
                    if (currentDtToUpdateStaminaHealth > timeToCalcStaminaHealth)
                    {
                        currentDtToUpdateStaminaHealth = 0f;
                    }
                    foreach (Agent agent in _inactiveAgentsBuffer)
                    {
                        AgentPostures.values.Remove(agent);
                    }

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
                    _dropShieldBuffer.Clear();
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
                                _dropShieldBuffer.Add(agentsToDropShield[i]);
                            }
                        }
                        else
                        {
                            _dropShieldBuffer.Add(agentsToDropShield[i]);
                        }
                    }
                    foreach (Agent agent in _dropShieldBuffer)
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

                    //weapon drop
                    _dropWeaponBuffer.Clear();
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
                                _dropWeaponBuffer.Add(agentsToDropWeapon[i]);
                            }
                        }
                        else
                        {
                            _dropWeaponBuffer.Add(agentsToDropWeapon[i]);
                        }
                    }
                    foreach (Agent agent in _dropWeaponBuffer)
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

                    currentDt = 0f;
                }
            }

        }

        public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent, in MissionWeapon affectorWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
        {
            base.OnAgentHit(affectedAgent, affectorAgent, in affectorWeapon, in blow, in attackCollisionData);
            if (affectedAgent == null || !affectedAgent.IsActive() || !affectedAgent.IsHuman)
            {
                return;
            }
            if (RBMConfig.RBMConfig.postureEnabled)
            {
                Posture affectedAgentPosture = null;
                AgentPostures.values.TryGetValue(affectedAgent, out affectedAgentPosture);
                if (affectedAgentPosture != null)
                {
                    //missile hit posture/stamina loss
                    if (blow.IsMissile && affectorWeapon.CurrentUsageItem != null)
                    {
                        bool isDirectHit = !attackCollisionData.AttackBlockedWithShield;
                        WeaponClass missileWeaponClass = affectorWeapon.CurrentUsageItem.WeaponClass;

                        float arrowAgentPostureDamage = 15f;
                        float throwingAgentPostureDamage = 60f;

                        float arrowShieldPostureDamage = 10f;
                        float throwingShieldPostureDamage = 30f;

                        //agent hit
                        if (isDirectHit)
                        {
                            //headshot multiplier
                            if (blow.VictimBodyPart == BoneBodyPartType.Head || blow.VictimBodyPart == BoneBodyPartType.Head)
                            {
                                arrowAgentPostureDamage = 30f;
                                throwingAgentPostureDamage = 100f;
                            }
                            switch (missileWeaponClass)
                            {
                                case WeaponClass.Bow:
                                case WeaponClass.Crossbow:
                                case WeaponClass.Arrow:
                                case WeaponClass.Bolt:
                                case WeaponClass.ThrowingKnife:
                                    {
                                        affectedAgentPosture.posture = Math.Max(0f, affectedAgentPosture.posture - arrowAgentPostureDamage);
                                        affectedAgentPosture.stamina = Math.Max(0f, affectedAgentPosture.stamina - arrowAgentPostureDamage);
                                        break;
                                    }
                                case WeaponClass.Javelin:
                                case WeaponClass.ThrowingAxe:

                                    {
                                        affectedAgentPosture.posture = Math.Max(0f, affectedAgentPosture.posture - throwingAgentPostureDamage);
                                        affectedAgentPosture.stamina = Math.Max(0f, affectedAgentPosture.stamina - throwingAgentPostureDamage);
                                        break;
                                    }
                            }
                        }
                        //shield hit
                        else
                        {
                            switch (missileWeaponClass)
                            {
                                case WeaponClass.Bow:
                                case WeaponClass.Crossbow:
                                case WeaponClass.Arrow:
                                case WeaponClass.Bolt:
                                case WeaponClass.ThrowingKnife:

                                    {
                                        affectedAgentPosture.posture = Math.Max(0f, affectedAgentPosture.posture - arrowShieldPostureDamage);
                                        affectedAgentPosture.stamina = Math.Max(0f, affectedAgentPosture.stamina - arrowShieldPostureDamage);
                                        break;
                                    }
                                case WeaponClass.Javelin:
                                case WeaponClass.ThrowingAxe:
                                    {
                                        affectedAgentPosture.posture = Math.Max(0f, affectedAgentPosture.posture - throwingShieldPostureDamage);
                                        affectedAgentPosture.stamina = Math.Max(0f, affectedAgentPosture.stamina - throwingShieldPostureDamage);
                                        break;
                                    }
                            }
                        }

                        //ranged posture break
                        if (affectedAgentPosture.posture <= 0f)
                        {
                            affectedAgentPosture.posture = 0f;
                            forceStaggerAnimation(affectedAgent, attackCollisionData, 0.85f, false);
                            ResetPostureForAgent(ref affectedAgentPosture, postureResetModifier);
                        }

                        addPosturedamageVisual(affectorAgent, affectedAgent);

                    }
                }
            }

            int ammoStuckInAgent = affectedAgent.GetAttachedWeaponsCount();
            int arrowsBoltsStuckInAgent = 0;
            int maxAmmoStuckInAgent = 5;
            for (int i = 0; i < ammoStuckInAgent; i++)
            {
                //remove stuck javelins
                WeaponClass ammoWeaponClass = affectedAgent.GetAttachedWeapon(i).CurrentUsageItem.WeaponClass;
                if (ammoWeaponClass == WeaponClass.Javelin || ammoWeaponClass == WeaponClass.ThrowingAxe || ammoWeaponClass == WeaponClass.ThrowingKnife)
                {
                    affectedAgent.DeleteAttachedWeapon(i);
                }

                if (ammoWeaponClass == WeaponClass.Arrow || ammoWeaponClass == WeaponClass.Bolt)
                {
                    arrowsBoltsStuckInAgent++;
                }

                //remove stuck arrows/bolts if there are too many of them
                if (arrowsBoltsStuckInAgent >= maxAmmoStuckInAgent)
                {
                    affectedAgent.DeleteAttachedWeapon(i);
                }
            }

            //drop shield if too many arrows/bolts or javelins/throwing axes/throwing knives are stuck in the shield, disabled for now
            //int maxAmmoStuckInShield = 15;
            //int maxJavelinsAxesKnivesStuckInShield = 3;
            //if (affectedAgent.WieldedOffhandWeapon.IsShield())
            //{
            //    int ammoStuckInShieldCount = affectedAgent.WieldedOffhandWeapon.GetAttachedWeaponsCount();
            //    int arrowsBoltsStuckInShieldCount = 0;
            //    int javelinsAxesKnivesStuckInShieldCount = 0;
            //    if (ammoStuckInShieldCount >= maxAmmoStuckInShield)
            //    {
            //        TryToDropShield(affectedAgent);
            //    }
            //    else
            //    {
            //        for (int i = 0; i < ammoStuckInShieldCount; i++)
            //        {
            //            WeaponClass ammoWeaponClass = affectedAgent.WieldedOffhandWeapon.GetAttachedWeapon(i).CurrentUsageItem.WeaponClass;
            //            if (ammoWeaponClass == WeaponClass.Arrow || ammoWeaponClass == WeaponClass.Bolt)
            //            {
            //                arrowsBoltsStuckInShieldCount++;
            //            }
            //            else if (ammoWeaponClass == WeaponClass.Javelin || ammoWeaponClass == WeaponClass.ThrowingAxe || ammoWeaponClass == WeaponClass.ThrowingKnife)
            //            {
            //                javelinsAxesKnivesStuckInShieldCount++;
            //            }
            //        }
            //        if (javelinsAxesKnivesStuckInShieldCount >= maxJavelinsAxesKnivesStuckInShield)
            //        {
            //            TryToDropShield(affectedAgent);
            //        }
            //    }
            //}
        }
    }
}