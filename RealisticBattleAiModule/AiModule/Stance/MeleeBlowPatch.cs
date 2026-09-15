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
    public partial class StanceLogic : MissionLogic
    {
        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("CreateMeleeBlow")]
        private partial class CreateMeleeBlowPatch
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

            public static void handleDefender(Stance stance, Agent victimAgent, Agent attackerAgent, ref AttackCollisionData collisionData,
                MissionWeapon attackerWeapon, float comHitModifier, ref Blow blow, ref Mission mission,
                float actionModifier, float staggerActionSpeed, bool dropWeapon, bool dropShield,
                bool damageShield, bool stagger, bool resetPosture, MeleeHitType meleeHitType, bool crushThrough, bool isUnarmedAttack)
            {
                if (stance != null)
                {
                    float postureDmg = calculateDefenderPostureDamage(victimAgent, attackerAgent, actionModifier, ref collisionData, attackerWeapon, comHitModifier, meleeHitType, isUnarmedAttack);

                    if (meleeHitType == MeleeHitType.AgentHit)
                    {
                        postureDmg = blow.InflictedDamage;
                    }

                    if (RBMConfig.RBMConfig.staminaEnabled)
                    {
                        float staminaLevel = stance.stamina / stance.maxStamina;
                        postureDmg *= MBMath.Lerp(1.25f, 1f, staminaLevel);
                    }

                    float postureOverkill = Math.Abs(stance.posture - postureDmg);
                    stance.posture = Math.Max(0f, stance.posture - postureDmg);

                    if (RBMConfig.RBMConfig.staminaEnabled)
                    {
                        int effectiveAthleticSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(victimAgent, DefaultSkills.Athletics);
                        float athlethicModifier = effectiveAthleticSkill / 20;
                        float victimAgentArmorWeight = Math.Max(0f, victimAgent.SpawnEquipment.GetTotalWeightOfArmor(true) - athlethicModifier);
                        float staminaLoss = calculateDefenderStaminaLoss(victimAgent, attackerAgent, ref collisionData, meleeHitType, isUnarmedAttack);
                        staminaLoss *= (1f + victimAgentArmorWeight / 50f);
                        stance.reduceStamina(staminaLoss);
                    }

                    addPosturedamageVisual(attackerAgent, victimAgent);
                    if (stance.posture <= 0f)
                    {
                        if (postureOverkill >= stance.maxPosture * 0.5f)
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
                            ResetPostureForAgent(ref stance, postureResetModifier);
                        }
                        // Hit stop: player broke an enemy's posture
                        if (attackerAgent.IsPlayerControlled)
                        {
                            HitStopLogic.TriggerPostureBreakHitStop();
                        }
                    }
                }
            }

            public static void handleAttacker(Stance stance, Agent victimAgent, Agent attackerAgent, ref AttackCollisionData collisionData,
                MissionWeapon attackerWeapon, float comHitModifier, ref Blow blow, ref Mission mission,
                float actionModifier, float staggerActionSpeed, bool dropWeapon, bool stagger, bool resetPosture, bool tired, MeleeHitType meleeHitType, bool isUnarmedAttack)
            {
                if (stance != null)
                {
                    float postureDmg = calculateAttackerPostureDamage(victimAgent, attackerAgent, actionModifier, ref collisionData, attackerWeapon, comHitModifier, meleeHitType, isUnarmedAttack);

                    if (RBMConfig.RBMConfig.staminaEnabled)
                    {
                        float staminaLevel = stance.stamina / stance.maxStamina;
                        postureDmg *= MBMath.Lerp(1.25f, 1f, staminaLevel);
                    }

                    float postureOverkill = Math.Abs(stance.posture - postureDmg);
                    stance.posture = Math.Max(0f, stance.posture - postureDmg);

                    if (RBMConfig.RBMConfig.staminaEnabled)
                    {
                        int effectiveAthleticSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attackerAgent, DefaultSkills.Athletics);
                        float athlethicModifier = effectiveAthleticSkill / 20;
                        float attackerAgentArmorWeight = Math.Max(0f, attackerAgent.SpawnEquipment.GetTotalWeightOfArmor(true) - athlethicModifier);
                        float staminaLoss = calculateAttackerStaminaLoss(victimAgent, attackerAgent, ref collisionData, meleeHitType, isUnarmedAttack);
                        staminaLoss *= (1f + attackerAgentArmorWeight / 50f);
                        stance.reduceStamina(staminaLoss);
                    }

                    addPosturedamageVisual(attackerAgent, victimAgent);
                    if (stance.posture <= 0f)
                    {
                        if (postureOverkill >= stance.maxPosture * 0.5f)
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
                                ResetPostureForAgent(ref stance, 0.33f);
                            }
                            else
                            {
                                ResetPostureForAgent(ref stance, postureResetModifier);
                            }
                        }
                        // Hit stop: player's parry/block broke the attacker's posture
                        if (victimAgent.IsPlayerControlled)
                        {
                            HitStopLogic.TriggerPostureBreakHitStop();
                        }
                    }
                }
            }

            public static void handleDefenderChamberBlock(Stance defenderPosture, Agent victimAgent, Agent attackerAgent, ref AttackCollisionData collisionData, MissionWeapon attackerWeapon, float comHitModifier, ref Blow blow, ref Mission mission, MeleeHitType meleeHitType)
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

            public static void handleAttackerChamberBlock(Stance attackerPosture, Agent victimAgent, Agent attackerAgent, ref AttackCollisionData collisionData, MissionWeapon attackerWeapon, float comHitModifier, ref Blow blow, ref Mission mission, MeleeHitType meleeHitType)
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

                Stance defenderPosture = null;
                Stance attackerPosture = null;
                AgentStances.values.TryGetValue(victimAgent, out defenderPosture);
                AgentStances.values.TryGetValue(attackerAgent, out attackerPosture);

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
                        // Hit stop: player performed a perfect parry
                        if (victimAgent.IsPlayerControlled)
                        {
                            HitStopLogic.TriggerParryHitStop();
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
                        // Hit stop: player performed a perfect shield parry
                        if (victimAgent.IsPlayerControlled)
                        {
                            HitStopLogic.TriggerParryHitStop();
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

            private static bool isOneHandedWeapon(MissionWeapon weapon)
            {
                if (weapon.IsEmpty || weapon.CurrentUsageItem == null)
                {
                    return false;
                }
                WeaponClass wc = weapon.CurrentUsageItem.WeaponClass;
                return
                    wc == WeaponClass.OneHandedAxe ||
                    wc == WeaponClass.Dagger ||
                    wc == WeaponClass.OneHandedPolearm ||
                    wc == WeaponClass.OneHandedSword ||
                    wc == WeaponClass.Mace ||
                    wc == WeaponClass.LowGripPolearm ||
                    wc == WeaponClass.Pick;
            }

            private static bool isSmallShield(MissionWeapon weapon)
            {
                if (weapon.IsEmpty || weapon.CurrentUsageItem == null)
                {
                    return false;
                }
                WeaponClass wc = weapon.CurrentUsageItem.WeaponClass;
                return wc == WeaponClass.SmallShield;
            }

            private static float calculateDefenderStaminaSkillModifier(int skill)
            {
                //100 skill = 10% reduction
                return Math.Max(0f, 1f - (skill * 0.001f));
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

        }
    }
}
