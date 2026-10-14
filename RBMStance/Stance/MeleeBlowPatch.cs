using HarmonyLib;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem.TournamentGames;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static RBMStance.PostureDamage;
using static TaleWorlds.Core.ArmorComponent;
using static TaleWorlds.Core.ItemObject;
using static TaleWorlds.MountAndBlade.Agent;
namespace RBMStance
{
    public partial class StanceLogic : MissionLogic
    {
        // After RBMCombat's postfixes on the same method (blow bookkeeping, blunt off-blade swings, XP): it always
        // ran after them while it lived in RBMAI, which was patched later. The kick/bash knockdown postfix
        // (Priority.Last) still runs after this one.
        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("CreateMeleeBlow")]
        [HarmonyAfter("com.rbmcombat")]
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

            private static void CrushThroughBlow(Agent victimAgent, Agent attackerAgent, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon,
                ref Blow blow, ref Mission mission, float postureOverkill, BlowFlags addedBlowFlag, float forceFactor)
            {
                BoneBodyPartType crushBodyPart = EstimateCrushThroughBodyPart(victimAgent, collisionData.CollisionGlobalPosition);
                sbyte crushBone = FindCrushThroughBone(victimAgent, crushBodyPart, collisionData.CollisionGlobalPosition);
                int hpDamage = (int)Math.Floor(CalculateHealthDamage(attackerAgent, victimAgent, ref collisionData, in attackerWeapon, postureOverkill, crushBodyPart, crushBone, out DamageTypes crushDamageType) * forceFactor);
                MakePostureCrushThroughBlow(ref mission, blow, attackerAgent, victimAgent, hpDamage, ref collisionData, attackerWeapon, addedBlowFlag, crushBodyPart, crushBone, crushDamageType);
                if (victimAgent.IsPlayerControlled)
                {
                    TextObject message = new TextObject("{=RBM_AI_033}Posture break: Posture depleted, {DMG} damage crushed through to the {PART}");
                    message.SetTextVariable("DMG", hpDamage);
                    message.SetTextVariable("PART", CrushThroughBodyPartName(crushBodyPart));
                    InformationManager.DisplayMessage(new InformationMessage(message.ToString(), Color.FromUint(4282569842u)));
                }
                if (attackerAgent.IsPlayerControlled)
                {
                    TextObject message = new TextObject("{=RBM_AI_034}Enemy Posture break: Posture depleted, {DMG} damage crushed through to the {PART}");
                    message.SetTextVariable("DMG", hpDamage);
                    message.SetTextVariable("PART", CrushThroughBodyPartName(crushBodyPart));
                    InformationManager.DisplayMessage(new InformationMessage(message.ToString(), Color.FromUint(4282569842u)));
                }
            }

            // EstimateCrushThroughBodyPart only returns these seven.
            private static TextObject CrushThroughBodyPartName(BoneBodyPartType bodyPart)
            {
                switch (bodyPart)
                {
                    case BoneBodyPartType.Head:
                        return new TextObject("{=RBM_AI_035}head");
                    case BoneBodyPartType.Neck:
                        return new TextObject("{=RBM_AI_036}neck");
                    case BoneBodyPartType.Chest:
                        return new TextObject("{=RBM_AI_037}chest");
                    case BoneBodyPartType.ShoulderLeft:
                        return new TextObject("{=RBM_AI_040}left shoulder");
                    case BoneBodyPartType.ShoulderRight:
                        return new TextObject("{=RBM_AI_041}right shoulder");
                    case BoneBodyPartType.Abdomen:
                        return new TextObject("{=RBM_AI_038}abdomen");
                    default:
                        return new TextObject("{=RBM_AI_039}legs");
                }
            }

            public static void HandleDefender(Stance stance, Agent victimAgent, Agent attackerAgent, ref AttackCollisionData collisionData,
                MissionWeapon attackerWeapon, float comHitModifier, ref Blow blow, ref Mission mission,
                float staggerActionSpeed, bool dropWeapon, bool dropShield,
                bool stagger, MeleeHitType meleeHitType, bool crushThrough, bool isUnarmedAttack)
            {
                if (stance != null)
                {
                    float now = MissionNow();

                    // A direct hit costs posture equal to the damage it dealt; the table-driven
                    // calculator is only meaningful for blocks/parries, so don't run it for AgentHit.
                    float postureDmg = (meleeHitType == MeleeHitType.AgentHit)
                        ? blow.InflictedDamage
                        : CalculateDefenderPostureDamage(victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, meleeHitType, isUnarmedAttack);
                    postureDmg = ApplyStaminaFactor(stance, postureDmg);

                    float postureOverkill = Math.Abs(stance.posture - postureDmg);
                    ApplyPostureLoss(stance, postureDmg, now, meleeHitType, withParryRefund: true);
                    ApplyDefenderStaminaLoss(stance, victimAgent, attackerAgent, ref collisionData, meleeHitType, isUnarmedAttack);

                    AddPostureDamageVisual(attackerAgent, victimAgent);
                    if (stance.posture <= 0f)
                    {
                        if (postureOverkill >= stance.maxPosture * PostureTuning.DropOverkillShare)
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
                        bool perfectParry = meleeHitType == MeleeHitType.WeaponParry || meleeHitType == MeleeHitType.ShieldParry;
                        if (crushThrough)
                        {
                            // A parry's overflow is small by design, so its crush-through is scaled by the parry factor alone.
                            CrushThroughBlow(victimAgent, attackerAgent, ref collisionData, in attackerWeapon, ref blow, ref mission,
                                perfectParry ? Math.Max(postureOverkill, CrushThroughFullDamageOverflow) : postureOverkill,
                                perfectParry ? BlowFlags.None : DismountFlags(victimAgent, stance, postureDmg),
                                perfectParry ? PostureTuning.PerfectParryCrushThroughFactor : 1f);
                        }
                        else if (!perfectParry && DismountFlags(victimAgent, stance, postureDmg) == BlowFlags.CanDismount)
                        {
                            // Shield-block breaks register no blow of their own, so a rider needs a zero-damage one
                            // to carry CanDismount.
                            MakePostureBlow(ref mission, blow, attackerAgent, victimAgent, ref collisionData, attackerWeapon, BlowFlags.CanDismount);
                        }
                        if (stagger)
                        {
                            ForceStaggerAnimation(victimAgent, collisionData, staggerActionSpeed, false);
                        }
                        stance.BreakPosture(now, PostureTuning.PostureBreakResetShare);
                        AddPostureDamageVisual(attackerAgent, victimAgent);
                        // Hit stop: player broke an enemy's posture
                        if (attackerAgent.IsPlayerControlled)
                        {
                            HitStopLogic.TriggerPostureBreakHitStop();
                        }
                    }
                }
            }

            public static void HandleAttacker(Stance stance, Agent victimAgent, Agent attackerAgent, ref AttackCollisionData collisionData,
                MissionWeapon attackerWeapon, float comHitModifier, ref Blow blow, ref Mission mission,
                float staggerActionSpeed, bool dropWeapon, bool stagger, bool tired, MeleeHitType meleeHitType, bool isUnarmedAttack)
            {
                // A kick or bash already paid its posture and stamina as it started (AiKickBash ChargeCost), wherever it
                // lands; charging the table cost on top made a landed kick cost the kicker ~40% of his posture.
                if (collisionData.IsAlternativeAttack && attackerAgent.GetComponent<KickBash.AiKickBashComponent>() != null)
                {
                    return;
                }
                if (stance != null)
                {
                    float now = MissionNow();

                    float postureDmg = CalculateAttackerPostureDamage(victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, meleeHitType, isUnarmedAttack);
                    if (meleeHitType == MeleeHitType.AgentHit)
                    {
                        postureDmg *= PostureTuning.LandedHitAttackerFactor;
                    }
                    postureDmg = ApplyStaminaFactor(stance, postureDmg);

                    float postureOverkill = Math.Abs(stance.posture - postureDmg);
                    ApplyPostureLoss(stance, postureDmg, now, meleeHitType, withParryRefund: false);
                    ApplyAttackerStaminaLoss(stance, victimAgent, attackerAgent, ref collisionData, attackerWeapon, meleeHitType, isUnarmedAttack);

                    AddPostureDamageVisual(attackerAgent, victimAgent);
                    if (stance.posture <= 0f)
                    {
                        if (postureOverkill >= stance.maxPosture * PostureTuning.DropOverkillShare)
                        {
                            if (dropWeapon)
                            {
                                TryToDropWeapon(attackerAgent);
                            }
                        }
                        if (stagger)
                        {
                            ForceStaggerAnimation(attackerAgent, collisionData, staggerActionSpeed, true);
                        }
                        if (tired)
                        {
                            ForceTiredAnimation(attackerAgent, collisionData, staggerActionSpeed, false);
                        }
                        // A rider whose blow is blocked or parried hard enough to break his posture is unseated, as a
                        // defender is. This path registers no blow of its own, so a zero-damage riposte carries
                        // CanDismount. Not on a direct hit: there the posture cost is his own strike landing.
                        if (meleeHitType != MeleeHitType.AgentHit && DismountFlags(attackerAgent, stance, postureDmg) == BlowFlags.CanDismount)
                        {
                            MakePostureRiposteBlow(ref mission, blow, attackerAgent, victimAgent, ref collisionData, attackerWeapon, BlowFlags.CanDismount);
                        }
                        float resetShare = meleeHitType == MeleeHitType.AgentHit ? PostureTuning.LandedHitBreakResetShare
                            : PostureTuning.PostureBreakResetShare;
                        stance.BreakPosture(now, resetShare);
                        AddPostureDamageVisual(attackerAgent, victimAgent);
                        // Hit stop: player's parry/block broke the attacker's posture
                        if (victimAgent.IsPlayerControlled)
                        {
                            HitStopLogic.TriggerPostureBreakHitStop();
                        }
                    }
                }
            }

            // A tired man loses up to 25% more posture.
            private static float ApplyStaminaFactor(Stance stance, float postureDamage)
            {
                if (RBMConfig.RBMConfig.staminaEnabled)
                {
                    float staminaLevel = stance.stamina / stance.maxStamina;
                    postureDamage *= MBMath.Lerp(1.25f, 1f, staminaLevel);
                }
                return postureDamage;
            }

            // A refund that outweighs the loss is no posture damage, so it does not pause regen.
            private static void ApplyPostureLoss(Stance stance, float postureDamage, float now, MeleeHitType meleeHitType, bool withParryRefund)
            {
                stance.posture = Math.Max(0f, stance.posture - postureDamage);
                float refund = withParryRefund ? ApplyPerfectParryRefund(stance, meleeHitType) : 0f;
                if (postureDamage > refund)
                {
                    stance.MarkPostureDamage(now);
                }
            }

            private static void ApplyDefenderStaminaLoss(Stance stance, Agent victimAgent, Agent attackerAgent, ref AttackCollisionData collisionData, MeleeHitType meleeHitType, bool isUnarmedAttack)
            {
                if (RBMConfig.RBMConfig.staminaEnabled)
                {
                    float staminaLoss = CalculateDefenderStaminaLoss(victimAgent, attackerAgent, ref collisionData, meleeHitType, isUnarmedAttack);
                    ApplyArmorWeightedStaminaLoss(stance, victimAgent, staminaLoss);
                }
            }

            private static void ApplyAttackerStaminaLoss(Stance stance, Agent victimAgent, Agent attackerAgent, ref AttackCollisionData collisionData, MissionWeapon attackerWeapon, MeleeHitType meleeHitType, bool isUnarmedAttack)
            {
                if (RBMConfig.RBMConfig.staminaEnabled)
                {
                    float staminaLoss = CalculateAttackerStaminaLoss(victimAgent, attackerAgent, ref collisionData, attackerWeapon, meleeHitType, isUnarmedAttack);
                    ApplyArmorWeightedStaminaLoss(stance, attackerAgent, staminaLoss);
                }
            }

            // Heavier armour costs more stamina per action, offset by Athletics.
            private static void ApplyArmorWeightedStaminaLoss(Stance stance, Agent agent, float staminaLoss)
            {
                int effectiveAthleticSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(agent, DefaultSkills.Athletics);
                float athleticModifier = effectiveAthleticSkill / 20f;
                float agentArmorWeight = Math.Max(0f, StanceUtilities.GetEffectiveArmorWeight(agent) - athleticModifier);
                stance.ReduceStamina(staminaLoss * (1f + agentArmorWeight / 50f));
            }

            // Gives a perfect parry's or chamber's refund back to a defender left unbroken by it; returns the amount.
            private static float ApplyPerfectParryRefund(Stance stance, MeleeHitType meleeHitType)
            {
                float share;
                switch (meleeHitType)
                {
                    case MeleeHitType.WeaponParry:
                    case MeleeHitType.ChamberBlock:
                        share = PostureTuning.PerfectParryWeaponRefund;
                        break;
                    case MeleeHitType.ShieldParry:
                        share = PostureTuning.PerfectParryShieldRefund;
                        break;
                    default:
                        return 0f;
                }
                if (stance.posture <= 0f)
                {
                    return 0f;
                }
                float before = stance.posture;
                stance.AddPosture(share * stance.maxPosture);
                return stance.posture - before;
            }

            public static void HandleDefenderChamberBlock(Stance defenderStance, Agent victimAgent, Agent attackerAgent, ref AttackCollisionData collisionData, MissionWeapon attackerWeapon, float comHitModifier, ref Blow blow, ref Mission mission, MeleeHitType meleeHitType)
            {
                float now = MissionNow();
                float postureDmg = CalculateDefenderPostureDamage(victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, meleeHitType, false);
                postureDmg = ApplyStaminaFactor(defenderStance, postureDmg);
                ApplyPostureLoss(defenderStance, postureDmg, now, meleeHitType, withParryRefund: true);
                ApplyDefenderStaminaLoss(defenderStance, victimAgent, attackerAgent, ref collisionData, meleeHitType, false);
                AddPostureDamageVisual(attackerAgent, victimAgent);
                if (defenderStance.posture <= 0f)
                {
                    EquipmentIndex wieldedItemIndex = victimAgent.GetPrimaryWieldedItemIndex();
                    if (wieldedItemIndex != EquipmentIndex.None)
                    {
                        if (victimAgent.IsPlayerControlled)
                        {
                            InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_018}Posture break: Posture depleted, chamber block").ToString(), Color.FromUint(4282569842u)));
                        }
                        MakePostureBlow(ref mission, blow, attackerAgent, victimAgent, ref collisionData, attackerWeapon, DismountFlags(victimAgent, defenderStance, postureDmg));
                    }
                    defenderStance.BreakPosture(now, PostureTuning.PostureBreakResetShare);
                    AddPostureDamageVisual(attackerAgent, victimAgent);
                    if (attackerAgent.IsPlayerControlled)
                    {
                        HitStopLogic.TriggerPostureBreakHitStop();
                    }
                }
            }

            public static void HandleAttackerChamberBlock(Stance attackerStance, Agent victimAgent, Agent attackerAgent, ref AttackCollisionData collisionData, MissionWeapon attackerWeapon, float comHitModifier, ref Blow blow, ref Mission mission, MeleeHitType meleeHitType)
            {
                float now = MissionNow();
                float postureDmg = CalculateAttackerPostureDamage(victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, meleeHitType, false);
                postureDmg = ApplyStaminaFactor(attackerStance, postureDmg);
                ApplyPostureLoss(attackerStance, postureDmg, now, meleeHitType, withParryRefund: false);
                ApplyAttackerStaminaLoss(attackerStance, victimAgent, attackerAgent, ref collisionData, attackerWeapon, meleeHitType, false);
                AddPostureDamageVisual(attackerAgent, victimAgent);
                if (attackerStance.posture <= 0f)
                {
                    // The victim chambered this attack, so the broken posture belongs to the
                    // attacker: register the stagger blow on the attacker, not on the chamberer.
                    MakePostureRiposteBlow(ref mission, blow, attackerAgent, victimAgent, ref collisionData, attackerWeapon, DismountFlags(attackerAgent, attackerStance, postureDmg));
                    attackerStance.BreakPosture(now, PostureTuning.PostureBreakResetShare);
                    AddPostureDamageVisual(attackerAgent, victimAgent);
                    if (victimAgent.IsPlayerControlled)
                    {
                        HitStopLogic.TriggerPostureBreakHitStop();
                    }
                }
            }

            private static void Postfix(ref Mission __instance, ref Blow __result, Agent attackerAgent, Agent victimAgent, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, CrushThroughState crushThroughState, Vec3 blowDirection, Vec3 swingDirection, bool cancelDamage)
            {
                //sanity gate
                // Don't gate on cancelDamage: vanilla MeleeHitCallback also sets it for every
                // weapon block/parry/chamber block (flag && !AttackBlockedWithShield), which would
                // skip all non-shield block posture/stamina. Exclude only the cases it was meant
                // for: invulnerable victims and non-enemy (friendly/neutral) hits.
                if (!_inMeleeHitContext || victimAgent == null || !victimAgent.IsHuman ||
                    !RBMConfig.RBMConfig.postureEnabled || attackerAgent == null ||
                    victimAgent.CurrentMortalityState == MortalityState.Invulnerable || !attackerAgent.IsEnemyOf(victimAgent))
                {
                    return;
                }

                Stance defenderStance = null;
                Stance attackerStance = null;
                AgentStances.values.TryGetValue(victimAgent, out defenderStance);
                AgentStances.values.TryGetValue(attackerAgent, out attackerStance);

                bool isUnarmedAttack = false;
                //detect unarmed attack
                // A shield bash / pommel strike is an alternative attack: it uses the body's row, not
                // the wielded weapon's, even though a weapon is equipped.
                if ((attackerWeapon.IsEmpty || collisionData.IsAlternativeAttack) && attackerAgent != null && victimAgent != null && collisionData.DamageType == (int)DamageTypes.Blunt && !collisionData.IsFallDamage && !collisionData.IsHorseCharge)
                {
                    isUnarmedAttack = true;
                }

                Blow blow = __result;
                Mission mission = __instance;

                //modifier of posture damage, closer the hit is to COM ( center of mass ), higher the Modifier
                float comHitModifier = isUnarmedAttack ? 1f : StanceUtilities.GetComHitModifier(in collisionData, in attackerWeapon);

                float exchangeTime = MissionNow();
                if (defenderStance != null)
                {
                    defenderStance.lastPostureEventTime = exchangeTime;
                }
                if (attackerStance != null)
                {
                    attackerStance.lastPostureEventTime = exchangeTime;
                }

                //chamber block
                if (collisionData.CollisionResult == CombatCollisionResult.ChamberBlocked)
                {
                    if (defenderStance != null)
                    {
                        HandleDefenderChamberBlock(defenderStance, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission, MeleeHitType.ChamberBlock);
                    }
                    if (attackerStance != null)
                    {
                        HandleAttackerChamberBlock(attackerStance, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission, MeleeHitType.ChamberBlock);
                    }
                }
                //weapon block
                else if (!collisionData.AttackBlockedWithShield)
                {
                    //normal weapon block
                    if (collisionData.CollisionResult == CombatCollisionResult.Blocked)
                    {
                        if (defenderStance != null)
                        {
                            HandleDefender(defenderStance, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                stagger: true,
                                crushThrough: true,
                                staggerActionSpeed: 0.85f,
                                dropWeapon: true,
                                dropShield: false,
                                meleeHitType: MeleeHitType.WeaponBlock,
                                isUnarmedAttack: isUnarmedAttack
                                );
                        }
                        if (attackerStance != null)
                        {
                            HandleAttacker(attackerStance, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                stagger: true,
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
                        if (defenderStance != null)
                        {
                            HandleDefender(defenderStance, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                stagger: true,
                                crushThrough: true,
                                staggerActionSpeed: 0.95f,
                                dropWeapon: false,
                                dropShield: false,
                                meleeHitType: MeleeHitType.WeaponParry,
                                isUnarmedAttack: isUnarmedAttack
                                );
                        }
                        if (attackerStance != null)
                        {
                            HandleAttacker(attackerStance, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                stagger: true,
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
                        if (defenderStance != null)
                        {
                            // No forced stagger: the hit reaction is already playing.
                            HandleDefender(defenderStance, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                stagger: false,
                                crushThrough: false,
                                staggerActionSpeed: 0.85f,
                                dropWeapon: false,
                                dropShield: false,
                                meleeHitType: MeleeHitType.AgentHit,
                                isUnarmedAttack: isUnarmedAttack
                                );
                        }
                        if (attackerStance != null)
                        {
                            HandleAttacker(attackerStance, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                stagger: false,
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
                        if (defenderStance != null)
                        {
                            HandleDefender(defenderStance, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                stagger: true,
                                crushThrough: false,
                                staggerActionSpeed: 0.85f,
                                dropWeapon: false,
                                dropShield: true,
                                meleeHitType: MeleeHitType.ShieldIncorrectBlock,
                                isUnarmedAttack: isUnarmedAttack
                                );
                        }
                        if (attackerStance != null)
                        {
                            HandleAttacker(attackerStance, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                stagger: false,
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
                        if (defenderStance != null)
                        {
                            HandleDefender(defenderStance, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                stagger: true,
                                crushThrough: false,
                                staggerActionSpeed: 0.9f,
                                dropWeapon: false,
                                dropShield: true,
                                meleeHitType: MeleeHitType.ShieldBlock,
                                isUnarmedAttack: isUnarmedAttack
                                );
                        }
                        if (attackerStance != null)
                        {
                            HandleAttacker(attackerStance, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                stagger: true,
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
                        if (defenderStance != null)
                        {
                            HandleDefender(defenderStance, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                stagger: true,
                                crushThrough: false,
                                staggerActionSpeed: 0.95f,
                                dropWeapon: false,
                                dropShield: false,
                                meleeHitType: MeleeHitType.ShieldParry,
                                isUnarmedAttack: isUnarmedAttack
                                );
                        }
                        if (attackerStance != null)
                        {
                            HandleAttacker(attackerStance, victimAgent, attackerAgent, ref collisionData, attackerWeapon, comHitModifier, ref blow, ref mission,
                                stagger: true,
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

            private static bool IsOneHandedWeapon(MissionWeapon weapon)
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

            private static bool IsSmallShield(MissionWeapon weapon)
            {
                if (weapon.IsEmpty || weapon.CurrentUsageItem == null)
                {
                    return false;
                }
                WeaponClass wc = weapon.CurrentUsageItem.WeaponClass;
                return wc == WeaponClass.SmallShield;
            }

            private static float CalculateDefenderStaminaSkillModifier(int skill)
            {
                //100 skill = 10% reduction
                return Math.Max(0f, 1f - (skill * 0.001f));
            }

            // Closing speed only: how fast the two close along the line between them (horizontal, world space).
            public static float GetRelativeSpeedPostureModifier(Agent attackerAgent, Agent defenderAgent)
            {
                Vec2 toDefender = defenderAgent.Position.AsVec2 - attackerAgent.Position.AsVec2;
                float distance = toDefender.Length;
                if (distance < 0.001f)
                {
                    return 0f;
                }
                Vec2 relativeVelocity = attackerAgent.Velocity.AsVec2 - defenderAgent.Velocity.AsVec2;
                float closingSpeed = Vec2.DotProduct(relativeVelocity, toDefender) / distance;
                return Math.Max(0f, closingSpeed) * PostureTuning.ClosingSpeedPostureFactor;
            }

            // Share of a rider's max posture that the breaking hit alone must deal for the break to unseat him, so a
            // heavy blow knocks him out of the saddle but chip damage that happens to empty the bar does not.
            private const float DismountPostureShare = 0.33f;

            // The posture rework reset every posture blow's flags to None (dropping the old knockback flags in
            // favour of forced stagger animations), which took CanDismount with it, so broken riders stayed
            // mounted. Native HandleBlowAux dismounts on this flag.
            private static BlowFlags DismountFlags(Agent target, Stance stance, float postureDmg)
            {
                return target != null && target.HasMount && stance != null && postureDmg >= stance.maxPosture * DismountPostureShare
                    ? BlowFlags.CanDismount
                    : BlowFlags.None;
            }

            private static readonly MethodInfo HandleBlowAuxMethod = AccessTools.Method(typeof(Agent), "HandleBlowAux");

            // Vanilla HandleBlow returns before HandleBlowAux (where the engine unseats a rider) on a 0-damage blow,
            // so a posture blow carrying CanDismount never dismounted anyone. Replay it engine-side with 1 damage;
            // the managed health change was already skipped. RBMCombat's HandleBlowPatch already replays kicks and
            // bashes, so don't run them twice.
            private static void RegisterPostureBlow(Agent target, Blow blow, ref AttackCollisionData collisionData)
            {
                target.RegisterBlow(blow, collisionData);
                bool replayedByCombat = RBMConfig.RBMConfig.rbmCombatEnabled && (blow.AttackType == AgentAttackType.Bash || blow.AttackType == AgentAttackType.Kick);
                if (blow.InflictedDamage <= 0 && (blow.BlowFlag & BlowFlags.CanDismount) != 0 && !replayedByCombat
                    && HandleBlowAuxMethod != null && target.IsActive() && target.HasMount)
                {
                    blow.InflictedDamage = 1;
                    HandleBlowAuxMethod.Invoke(target, new object[] { blow });
                }
            }

            // Posture breaks happen on blocks, parries and chambers, which already made their clash sound; the
            // zero-damage break blow would add a body-hit sound on top. The crush-through blow lands for real and keeps
            // its sound.
            private const BlowFlags SilentBreakBlow = BlowFlags.NoSound;

            // A zero-damage copy of the attacker's blow carrying the block's collision data; callers set what differs.
            private static Blow BuildPostureBlow(Blow blow, Agent attackerAgent, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, BlowFlags addedBlowFlag)
            {
                Blow newBlow = blow;
                newBlow.BaseMagnitude = collisionData.BaseMagnitude;
                newBlow.MovementSpeedDamageModifier = collisionData.MovementSpeedDamageModifier;
                newBlow.InflictedDamage = 0;
                newBlow.SelfInflictedDamage = collisionData.SelfInflictedDamage;
                newBlow.AbsorbedByArmor = collisionData.AbsorbedByArmor;
                sbyte weaponAttachBoneIndex = (sbyte)(attackerWeapon.IsEmpty ? (-1) : attackerAgent.Monster.GetBoneToAttachForItemFlags(attackerWeapon.Item.ItemFlags));
                newBlow.WeaponRecord.FillAsMeleeBlow(attackerWeapon.Item, attackerWeapon.CurrentUsageItem, collisionData.AffectorWeaponSlotOrMissileIndex, weaponAttachBoneIndex);
                newBlow.StrikeType = (StrikeType)collisionData.StrikeType;
                newBlow.DamageType = (!attackerWeapon.IsEmpty && !collisionData.IsAlternativeAttack) ? (DamageTypes)collisionData.DamageType : DamageTypes.Blunt;
                newBlow.NoIgnore = collisionData.IsAlternativeAttack;
                newBlow.AttackerStunPeriod = collisionData.AttackerStunPeriod;
                newBlow.DefenderStunPeriod = collisionData.DefenderStunPeriod;
                newBlow.BlowFlag = addedBlowFlag;
                newBlow.GlobalPosition = collisionData.CollisionGlobalPosition;
                newBlow.BoneIndex = collisionData.CollisionBoneIndex;
                newBlow.Direction = blow.Direction;
                newBlow.SwingDirection = blow.SwingDirection;
                newBlow.VictimBodyPart = collisionData.VictimHitBodyPart;
                return newBlow;
            }

            // Registers the blow on the target and tells every mission behavior that the striker dealt it.
            private static void DeliverPostureBlow(ref Mission mission, Agent striker, Agent target, Blow blow, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon)
            {
                RegisterPostureBlow(target, blow, ref collisionData);
                foreach (MissionBehavior missionBehaviour in mission.MissionBehaviors)
                {
                    missionBehaviour.OnRegisterBlow(striker, target, WeakGameEntity.Invalid, blow, ref collisionData, in attackerWeapon);
                }
            }

            // Lands on the attacker: the defender's guard broke his posture.
            private static void MakePostureRiposteBlow(ref Mission mission, Blow blow, Agent attackerAgent, Agent victimAgent, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, BlowFlags addedBlowFlag)
            {
                Blow newBlow = BuildPostureBlow(blow, attackerAgent, ref collisionData, in attackerWeapon, addedBlowFlag | SilentBreakBlow);
                DeliverPostureBlow(ref mission, victimAgent, attackerAgent, newBlow, ref collisionData, in attackerWeapon);
                if (CanForceReaction(attackerAgent))
                {
                    attackerAgent.SetActionChannel(0, ActionIndexCache.act_stagger_left, actionSpeed: 0.9f);
                }
            }

            private static void MakePostureBlow(ref Mission mission, Blow blow, Agent attackerAgent, Agent victimAgent, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, BlowFlags addedBlowFlag)
            {
                Blow newBlow = BuildPostureBlow(blow, attackerAgent, ref collisionData, in attackerWeapon, addedBlowFlag | SilentBreakBlow);
                DeliverPostureBlow(ref mission, attackerAgent, victimAgent, newBlow, ref collisionData, in attackerWeapon);
                if (CanForceReaction(victimAgent))
                {
                    victimAgent.SetActionChannel(0, ActionIndexCache.act_stagger_left, actionSpeed: 0.9f);
                }
            }

            // The blow lands on the estimated body part and bone, not the guarding arm the block reports, with the damage
            // type the damage was computed with. The block's own collision data still goes with it, so listeners keep
            // seeing a block (RBMCombat's armor wear, for one, only runs on StrikeAgent).
            private static void MakePostureCrushThroughBlow(ref Mission mission, Blow blow, Agent attackerAgent, Agent victimAgent, int hpDamage, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, BlowFlags addedBlowFlag, BoneBodyPartType bodyPart, sbyte bone, DamageTypes damageType)
            {
                Blow newBlow = BuildPostureBlow(blow, attackerAgent, ref collisionData, in attackerWeapon, addedBlowFlag);
                newBlow.InflictedDamage = hpDamage;
                newBlow.DamageType = damageType;
                newBlow.AttackerStunPeriod = collisionData.AttackerStunPeriod / 5f;
                newBlow.DefenderStunPeriod = collisionData.DefenderStunPeriod * 5f;
                newBlow.BoneIndex = bone;
                newBlow.VictimBodyPart = bodyPart;
                DeliverPostureBlow(ref mission, attackerAgent, victimAgent, newBlow, ref collisionData, in attackerWeapon);
            }

        }
    }
}
