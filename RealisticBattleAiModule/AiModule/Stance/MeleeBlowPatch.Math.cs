using HarmonyLib;
using Helpers;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
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
        private partial class CreateMeleeBlowPatch
        {
            private static float calculateDefenderStaminaLoss(Agent defenderAgent, Agent attackerAgent, ref AttackCollisionData collisionData, MeleeHitType meleeHitType, bool isUnarmedAttack)
            {
                float directHit = (collisionData.InflictedDamage * 2f) + 20f;
                float unarmedDefence = 40f;
                float oneHandedDefence = 40f;
                float twoHandedDefence = 40f;
                float smallShieldDefence = 43f;
                float largeShieldDefence = 46f;

                float defaultDefence = 40f;

                float result = 0f;

                if (meleeHitType == MeleeHitType.AgentHit)
                {
                    result = directHit;
                    return result;
                }

                if (isUnarmedAttack)
                {
                    switch (meleeHitType)
                    {
                        case MeleeHitType.AgentHit:
                            {
                                result = directHit;
                                return result;
                            }
                        default:
                            {
                                int effectiveSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(defenderAgent, DefaultSkills.Athletics);
                                result = unarmedDefence * calculateDefenderStaminaSkillModifier(effectiveSkill);
                                return result;
                            }
                    }
                }
                else
                {
                    MissionWeapon defenderWeapon = defenderAgent.WieldedWeapon;
                    SkillObject defenderWeaponSkill = (defenderAgent.WieldedWeapon.IsEmpty || defenderWeapon.CurrentUsageItem == null) ? DefaultSkills.Athletics : WeaponComponentData.GetRelevantSkillFromWeaponClass(defenderWeapon.CurrentUsageItem.WeaponClass);
                    int effectiveSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(defenderAgent, defenderWeaponSkill);
                    float skillModifier = calculateDefenderStaminaSkillModifier(effectiveSkill);

                    switch (meleeHitType)
                    {
                        case MeleeHitType.WeaponBlock:
                        case MeleeHitType.WeaponParry:
                        case MeleeHitType.ChamberBlock:
                            {
                                bool isDefenderWeaponOneHanded = isOneHandedWeapon(defenderWeapon);

                                if (isDefenderWeaponOneHanded)
                                {
                                    result = oneHandedDefence * skillModifier;
                                    return result;
                                }
                                else
                                {
                                    result = twoHandedDefence * skillModifier;
                                    return result;
                                }
                            }
                        case MeleeHitType.ShieldBlock:
                        case MeleeHitType.ShieldIncorrectBlock:
                        case MeleeHitType.ShieldParry:
                            {
                                bool isDefenderSmallShield = false;
                                if (defenderAgent.GetOffhandWieldedItemIndex() != EquipmentIndex.None)
                                {
                                    MissionWeapon defenderShield = defenderAgent.Equipment[defenderAgent.GetOffhandWieldedItemIndex()];
                                    isDefenderSmallShield = isSmallShield(defenderShield);
                                }
                                if (isDefenderSmallShield)
                                {
                                    result = smallShieldDefence * skillModifier;
                                    return result;
                                }
                                else
                                {
                                    result = largeShieldDefence * skillModifier;
                                    return result;
                                }
                            }
                        default:
                            {
                                result = defaultDefence * skillModifier;
                                return result;
                            }
                    }
                }
            }

            private static float calculateAttackerStaminaLoss(Agent defenderAgent, Agent attackerAgent, ref AttackCollisionData collisionData, MissionWeapon attackerWeapon, MeleeHitType meleeHitType, bool isUnarmedAttack)
            {
                float result = 0f;

                SkillObject attackerWeaponSkill = null;
                if (!isUnarmedAttack && !attackerWeapon.IsEmpty && attackerWeapon.CurrentUsageItem != null)
                {
                    attackerWeaponSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(attackerWeapon.CurrentUsageItem.WeaponClass);
                }
                int effectiveSkill = (isUnarmedAttack || attackerWeaponSkill == null)
                    ? MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attackerAgent, DefaultSkills.Athletics)
                    : MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attackerAgent, attackerWeaponSkill);

                float directHit = 35f;
                float unarmedAttack = 35f;
                float oneHandedAttack = 35f;
                float twoHandedAttack = 40f;

                float defaultAttack = 35f;

                //100 skill = 10% reduction
                float skillModifier = Math.Max(0f, 1f - (effectiveSkill * 0.001f));

                if (isUnarmedAttack)
                {
                    switch (meleeHitType)
                    {
                        case MeleeHitType.AgentHit:
                            {
                                result = directHit;
                                return result;
                            }
                        default:
                            {
                                result = unarmedAttack * skillModifier;
                                return result;
                            }
                    }
                }
                else
                {
                    bool isAttackerWeaponOneHanded = isOneHandedWeapon(attackerWeapon);

                    switch (meleeHitType)
                    {
                        case MeleeHitType.AgentHit:
                            {
                                result = isAttackerWeaponOneHanded ? directHit : directHit * 1.4f;
                                return result;
                            }
                        case MeleeHitType.WeaponBlock:
                        case MeleeHitType.WeaponParry:
                        case MeleeHitType.ChamberBlock:
                        case MeleeHitType.ShieldBlock:
                        case MeleeHitType.ShieldIncorrectBlock:
                        case MeleeHitType.ShieldParry:
                            {
                                if (isAttackerWeaponOneHanded)
                                {
                                    result = oneHandedAttack * skillModifier;
                                    return result;
                                }
                                else
                                {
                                    result = twoHandedAttack * skillModifier;
                                    return result;
                                }
                            }
                        default:
                            {
                                result = defaultAttack * skillModifier;
                                return result;
                            }
                    }
                }
            }

            // The action type (block / parry / shield block / chamber) is encoded in the PostureDamage
            // table rows selected by meleeHitType, so there is no separate per-action multiplier.
            private static float calculateDefenderPostureDamage(Agent defenderAgent, Agent attackerAgent, ref AttackCollisionData collisionData, MissionWeapon weapon, float comHitModifier, MeleeHitType meleeHitType, bool isUnarmedAttack)
            {
                float result = 0f;
                float strengthSkillModifier = 500f;
                float weaponSkillModifier = 500f;

                float basePostureDamage = getDefenderPostureDamage(defenderAgent, attackerAgent, collisionData.AttackDirection, (StrikeType)collisionData.StrikeType, meleeHitType, isUnarmedAttack);

                //SkillObject attackerWeaponSkill = isUnarmedAttack ? null : WeaponComponentData.GetRelevantSkillFromWeaponClass(weapon.CurrentUsageItem.WeaponClass);
                SkillObject attackerWeaponSkill = null;
                if (!isUnarmedAttack && !weapon.IsEmpty && weapon.CurrentUsageItem != null)
                {
                    attackerWeaponSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(weapon.CurrentUsageItem.WeaponClass);
                }
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
                    if (defenderWeapon.CurrentUsageItem != null)
                    {
                        SkillObject defenderWeaponSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(defenderWeapon.CurrentUsageItem.WeaponClass);
                        if (defenderWeaponSkill != null)
                        {
                            defenderEffectiveWeaponSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(defenderAgent, defenderWeaponSkill);
                        }
                    }
                }
                // A shield is worth the same bonus whether or not the defender also has a primary
                // weapon wielded, so this check must sit outside the primary-weapon gate.
                if (defenderAgent.GetOffhandWieldedItemIndex() != EquipmentIndex.None)
                {
                    if (defenderAgent.Equipment[defenderAgent.GetOffhandWieldedItemIndex()].IsShield())
                    {
                        defenderEffectiveWeaponSkill += 20f;
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
                    attackerEffectiveWeaponSkill = attackerEffectiveStrengthSkill;
                }

                defenderEffectiveWeaponSkill = defenderEffectiveWeaponSkill / weaponSkillModifier;
                defenderEffectiveStrengthSkill = defenderEffectiveStrengthSkill / strengthSkillModifier;

                attackerEffectiveWeaponSkill = attackerEffectiveWeaponSkill / weaponSkillModifier;
                attackerEffectiveStrengthSkill = attackerEffectiveStrengthSkill / strengthSkillModifier;

                float skillModifier = (1f + attackerEffectiveStrengthSkill + attackerEffectiveWeaponSkill) / (1f + defenderEffectiveStrengthSkill + defenderEffectiveWeaponSkill);
                float additiveSpeedModifier = getRelativeSpeedPostureModifier(attackerAgent, defenderAgent);
                basePostureDamage = (basePostureDamage + additiveSpeedModifier) * skillModifier;

                result = basePostureDamage * comHitModifier;
                result *= GetDefenderBlockPerkFactor(defenderAgent, meleeHitType);
                //InformationManager.DisplayMessage(new InformationMessage("Deffender PD: " + result));
                return result;
            }

            /// <summary>
            /// Posture reads skills but never the driven properties vanilla's personal perks land on.
            /// This maps the block-related ones onto the posture cost of a block:
            ///  - handling perks (Athletics.Fury, OneHanded.WrappedHandles, TwoHanded.StrongGrip,
            ///    Polearm.CounterWeight; SandboxAgentStatCalculateModel.SetPerkAndBannerEffectsOnAgent)
            ///    reduce posture lost on any block or parry, since handling is vanilla's block readiness;
            ///  - shield perks (Engineering.Scaffolds shield HP; OneHanded.SteelCoreShields and
            ///    OneHanded.ShieldWall shield-damage reduction; SandboxAgentApplyDamageModel) reduce
            ///    posture lost on shield blocks by the same proportion they protect the shield.
            /// The factors are rebuilt from the perks rather than read from the driven properties,
            /// because RBM already multiplies those properties by stamina and posture applies its own
            /// stamina penalty. Returns 1 for unperked agents and for direct hits.
            /// </summary>
            private static float GetDefenderBlockPerkFactor(Agent defenderAgent, MeleeHitType meleeHitType)
            {
                if (meleeHitType == MeleeHitType.AgentHit || Campaign.Current == null)
                {
                    return 1f;
                }

                bool shieldBlock = meleeHitType == MeleeHitType.ShieldBlock || meleeHitType == MeleeHitType.ShieldIncorrectBlock || meleeHitType == MeleeHitType.ShieldParry;
                bool incorrectShieldBlock = meleeHitType == MeleeHitType.ShieldIncorrectBlock;

                Agent captainAgent = defenderAgent.Formation?.Captain;
                EquipmentIndex primaryIndex = defenderAgent.GetPrimaryWieldedItemIndex();
                EquipmentIndex offhandIndex = defenderAgent.GetOffhandWieldedItemIndex();
                int usageIndex = defenderAgent.WieldedWeapon.IsEmpty ? -1 : defenderAgent.WieldedWeapon.CurrentUsageIndex;

                Stance stance = null;
                AgentStances.values.TryGetValue(defenderAgent, out stance);
                if (stance == null)
                {
                    // No stance entry to hang the cache on - fall back to computing it every time.
                    float weaponOnly;
                    float shieldOnly;
                    float shieldIncorrectOnly;
                    ComputeDefenderBlockPerkFactors(defenderAgent, captainAgent, out weaponOnly, out shieldOnly, out shieldIncorrectOnly);
                    return shieldBlock ? (incorrectShieldBlock ? shieldIncorrectOnly : shieldOnly) : weaponOnly;
                }

                if (!stance.blockPerkFactorsValid
                    || stance.blockPerkCaptain != captainAgent
                    || stance.blockPerkPrimaryIndex != primaryIndex
                    || stance.blockPerkOffhandIndex != offhandIndex
                    || stance.blockPerkUsageIndex != usageIndex)
                {
                    ComputeDefenderBlockPerkFactors(defenderAgent, captainAgent,
                        out stance.blockPerkWeaponFactor, out stance.blockPerkShieldFactor, out stance.blockPerkShieldIncorrectFactor);
                    stance.blockPerkCaptain = captainAgent;
                    stance.blockPerkPrimaryIndex = primaryIndex;
                    stance.blockPerkOffhandIndex = offhandIndex;
                    stance.blockPerkUsageIndex = usageIndex;
                    stance.blockPerkFactorsValid = true;
                }

                return shieldBlock
                    ? (incorrectShieldBlock ? stance.blockPerkShieldIncorrectFactor : stance.blockPerkShieldFactor)
                    : stance.blockPerkWeaponFactor;
            }

            /// <summary>
            /// The uncached perk math behind <see cref="GetDefenderBlockPerkFactor"/>. Produces the
            /// factor for each of the three block classes in one pass: weapon block/parry (handling
            /// perks only), correct shield block/parry, and incorrect shield block (which also gets
            /// ShieldWall). The arithmetic is identical to the per-blow version it replaced.
            /// </summary>
            private static void ComputeDefenderBlockPerkFactors(Agent defenderAgent, Agent captainAgent, out float weaponFactor, out float shieldFactor, out float shieldIncorrectFactor)
            {
                weaponFactor = 1f;
                shieldFactor = 1f;
                shieldIncorrectFactor = 1f;

                CharacterObject character = defenderAgent.Character as CharacterObject;
                if (character == null)
                {
                    return;
                }
                CharacterObject captain = (captainAgent != null && captainAgent != defenderAgent) ? captainAgent.Character as CharacterObject : null;
                bool onFoot = !defenderAgent.HasMount;

                float factor = 1f;

                // Handling: applies to every block/parry type.
                WeaponComponentData wielded = defenderAgent.WieldedWeapon.IsEmpty ? null : defenderAgent.WieldedWeapon.CurrentUsageItem;
                if (wielded != null && wielded.IsMeleeWeapon)
                {
                    ExplainedNumber handling = new ExplainedNumber(1f);
                    if (onFoot)
                    {
                        PerkHelper.AddPerkBonusForCharacter(DefaultPerks.Athletics.Fury, character, true, ref handling);
                        if (captain != null)
                        {
                            PerkHelper.AddPerkBonusFromCaptain(DefaultPerks.Athletics.Fury, captain, ref handling);
                        }
                    }
                    if (wielded.RelevantSkill == DefaultSkills.OneHanded)
                    {
                        PerkHelper.AddPerkBonusForCharacter(DefaultPerks.OneHanded.WrappedHandles, character, true, ref handling);
                    }
                    else if (wielded.RelevantSkill == DefaultSkills.TwoHanded)
                    {
                        PerkHelper.AddPerkBonusForCharacter(DefaultPerks.TwoHanded.StrongGrip, character, true, ref handling);
                    }
                    else if (wielded.RelevantSkill == DefaultSkills.Polearm && wielded.SwingDamageType != DamageTypes.Invalid)
                    {
                        PerkHelper.AddPerkBonusForCharacter(DefaultPerks.Polearm.CounterWeight, character, true, ref handling);
                    }
                    if (handling.ResultNumber > 0f)
                    {
                        factor /= handling.ResultNumber;
                    }
                }

                weaponFactor = factor;

                // Shield: only on shield blocks. Computed twice, once without and once with ShieldWall
                // (which vanilla only grants on an incorrect block).
                {
                    float shieldBase = factor;
                    ExplainedNumber shieldHp = new ExplainedNumber(1f);
                    PerkHelper.AddPerkBonusForCharacter(DefaultPerks.Engineering.Scaffolds, character, false, ref shieldHp);
                    if (shieldHp.ResultNumber > 0f)
                    {
                        shieldBase /= shieldHp.ResultNumber;
                    }

                    ExplainedNumber shieldDamage = new ExplainedNumber(1f);
                    PerkHelper.AddPerkBonusForCharacter(DefaultPerks.OneHanded.SteelCoreShields, character, true, ref shieldDamage);
                    if (onFoot && captain != null)
                    {
                        PerkHelper.AddPerkBonusFromCaptain(DefaultPerks.OneHanded.SteelCoreShields, captain, ref shieldDamage);
                    }
                    ExplainedNumber shieldDamageIncorrect = shieldDamage;
                    PerkHelper.AddPerkBonusForCharacter(DefaultPerks.OneHanded.ShieldWall, character, true, ref shieldDamageIncorrect);

                    shieldFactor = shieldBase * Math.Max(0f, shieldDamage.ResultNumber);
                    shieldIncorrectFactor = shieldBase * Math.Max(0f, shieldDamageIncorrect.ResultNumber);
                }
            }

            private static float calculateAttackerPostureDamage(Agent defenderAgent, Agent attackerAgent, ref AttackCollisionData collisionData, MissionWeapon weapon, float comHitModifier, MeleeHitType meleeHitType, bool isUnarmedAttack)
            {
                float result = 0f;

                float strengthSkillModifier = 500f;
                float weaponSkillModifier = 500f;

                float basePostureDamage = getAttackerPostureDamage(defenderAgent, attackerAgent, collisionData.AttackDirection, (StrikeType)collisionData.StrikeType, meleeHitType, isUnarmedAttack);

                SkillObject attackerWeaponSkill = null;
                if (!isUnarmedAttack && !weapon.IsEmpty && weapon.CurrentUsageItem != null)
                {
                    attackerWeaponSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(weapon.CurrentUsageItem.WeaponClass);
                }

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
                    if (defenderWeapon.CurrentUsageItem != null)
                    {
                        SkillObject defenderWeaponSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(defenderWeapon.CurrentUsageItem.WeaponClass);
                        if (defenderWeaponSkill != null)
                        {
                            defenderEffectiveWeaponSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(defenderAgent, defenderWeaponSkill);
                        }
                    }
                }
                // A shield is worth the same bonus whether or not the defender also has a primary
                // weapon wielded, so this check must sit outside the primary-weapon gate.
                if (defenderAgent.GetOffhandWieldedItemIndex() != EquipmentIndex.None)
                {
                    if (defenderAgent.Equipment[defenderAgent.GetOffhandWieldedItemIndex()].IsShield())
                    {
                        defenderEffectiveWeaponSkill += 20f;
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
                    attackerEffectiveWeaponSkill = attackerEffectiveStrengthSkill;
                }

                defenderEffectiveWeaponSkill = defenderEffectiveWeaponSkill / weaponSkillModifier;
                defenderEffectiveStrengthSkill = defenderEffectiveStrengthSkill / strengthSkillModifier;

                attackerEffectiveWeaponSkill = attackerEffectiveWeaponSkill / weaponSkillModifier;
                attackerEffectiveStrengthSkill = attackerEffectiveStrengthSkill / strengthSkillModifier;

                float skillModifier = (1f + defenderEffectiveStrengthSkill + defenderEffectiveWeaponSkill) / (1f + attackerEffectiveStrengthSkill + attackerEffectiveWeaponSkill);
                float additiveSpeedModifier = getRelativeSpeedPostureModifier(attackerAgent, defenderAgent);
                basePostureDamage = (basePostureDamage + additiveSpeedModifier) * skillModifier;

                result = basePostureDamage * comHitModifier;
                //InformationManager.DisplayMessage(new InformationMessage("Attacker PD: " + result));
                return result;
            }

            // A blocked strike that breaks the guard still loses some of its force to it.
            private const float CrushThroughDamageMultiplier = 0.85f;

            // A posture overflow up to this lets only a proportional share of the damage through.
            private const float CrushThroughFullDamageOverflow = 20f;

            // How far below the defender's eyes (m, at agent scale 1) each body part of the crush-through hit starts:
            // anything higher is the head, anything lower than the last is the legs.
            private const float CrushThroughNeckBelowEyes = 0.12f;
            private const float CrushThroughChestBelowEyes = 0.22f;
            private const float CrushThroughAbdomenBelowEyes = 0.5f;
            private const float CrushThroughLegsBelowEyes = 0.75f;

            // In the chest band down to this depth below the eyes, a contact at least this far (m, at agent scale 1) to
            // either side of the defender's centreline is a shoulder hit.
            private const float CrushThroughShoulderLowestBelowEyes = 0.4f;
            private const float CrushThroughShoulderSideOffset = 0.17f;

            // The HP damage a guard-breaking block lets through: what the blocked strike would have dealt had it landed.
            // Vanilla cancels a weapon block's damage before CreateMeleeBlow (GetAttackCollisionResults is skipped and
            // BaseMagnitude zeroed), so the contact is re-run as an unblocked hit through the code a landed blow goes
            // through: its magnitude from the contact point, attack progress, movement and momentum, then the hit part's
            // armor, material and multiplier, stamina, and perks.
            // bodyPart and bone are the estimated landing spot (EstimateCrushThroughBodyPart / FindCrushThroughBone).
            // The block's own bone is never used: it is on the arm, and AttackInformation takes the vanilla body-part
            // multiplier from the bone, not the body part. damageType is the type the damage was computed with.
            public static float calculateHealthDamage(Agent attacker, Agent victimAgent, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, float overPostureDamage, BoneBodyPartType bodyPart, sbyte bone, out DamageTypes damageType)
            {
                // An up normal: the weapon-on-weapon normal says nothing about a face or under-shoulder hit, and DamageRework
                // reads it for both (face needs z < 0, under-shoulder z < 0.15 on bone 15/22); up rules both out.
                AttackCollisionData hit = AttackCollisionData.GetAttackCollisionDataForDebugPurpose(false, false, collisionData.IsAlternativeAttack, true, false,
                    false, false, false, false, collisionData.ThrustTipHit, false, false,
                    CombatCollisionResult.StrikeAgent, collisionData.AffectorWeaponSlotOrMissileIndex, collisionData.StrikeType, collisionData.DamageType, bone,
                    bodyPart, collisionData.AttackBoneIndex, collisionData.AttackDirection, collisionData.PhysicsMaterialIndex, collisionData.CollisionHitResultFlags, collisionData.AttackProgress, collisionData.CollisionDistanceOnWeapon,
                    collisionData.AttackerStunPeriod, collisionData.DefenderStunPeriod, collisionData.MissileTotalDamage, collisionData.MissileStartingBaseSpeed, collisionData.ChargeVelocity, collisionData.FallSpeed, collisionData.WeaponRotUp,
                    collisionData.WeaponBlowDir, collisionData.CollisionGlobalPosition, collisionData.MissileVelocity, collisionData.MissileStartingPosition, collisionData.VictimAgentCurVelocity, Vec3.Up);

                AttackInformation attackInformation = new AttackInformation(attacker, victimAgent, WeakGameEntity.Invalid, in hit, in attackerWeapon);
                RBMConfig.BlowDamage.LastComputedDamageType = null;
                MissionCombatMechanicsHelper.GetAttackCollisionResults(in attackInformation, false, _meleeHitMomentumRemaining, false, ref hit, out CombatLogData combatLog, out _);
                // RBMCombat's override reports the type after its handle -> blunt and off-tip thrust -> cut rules; without
                // RBM Combat, the engine's own choice (blunt for unarmed, kicks/bashes and off-weapon bones) is final.
                damageType = RBMConfig.BlowDamage.LastComputedDamageType ?? combatLog.DamageType;
                if (hit.InflictedDamage <= 0)
                {
                    return 0f;
                }
                // Perk amplifications/reductions, difficulty scaling and damage-ignore rules, as
                // Mission.GetAttackCollisionResults applies them to a landed hit.
                float damage = MissionGameModels.Current.AgentApplyDamageModel.CalculateDamage(in attackInformation, in hit, hit.InflictedDamage);
                damage *= CrushThroughDamageMultiplier;
                if (overPostureDamage <= CrushThroughFullDamageOverflow)
                {
                    damage *= overPostureDamage / CrushThroughFullDamageOverflow;
                }
                return damage;
            }

            // A weapon block reports the arm holding the guard, wherever the strike was going, so the part the blocked
            // strike would have landed on is taken from how far below the defender's eyes the weapons met (eye-relative,
            // so it holds for riders too) and, at shoulder height, how far to the side of him.
            private static BoneBodyPartType EstimateCrushThroughBodyPart(Agent victimAgent, Vec3 contact)
            {
                float scale = MathF.Max(victimAgent.AgentScale, 0.1f);
                float belowEyes = (victimAgent.GetEyeGlobalPosition().z - contact.z) / scale;
                if (belowEyes < CrushThroughNeckBelowEyes)
                {
                    return BoneBodyPartType.Head;
                }
                if (belowEyes < CrushThroughChestBelowEyes)
                {
                    return BoneBodyPartType.Neck;
                }
                if (belowEyes < CrushThroughAbdomenBelowEyes)
                {
                    if (belowEyes < CrushThroughShoulderLowestBelowEyes)
                    {
                        Vec2 facing = victimAgent.GetMovementDirection();
                        Vec2 toContact = contact.AsVec2 - victimAgent.Position.AsVec2;
                        float sideOffset = MathF.Abs(toContact.x * facing.y - toContact.y * facing.x) / scale;
                        if (sideOffset >= CrushThroughShoulderSideOffset)
                        {
                            // The side goes by whichever shoulder's bones are nearer the contact.
                            FindCrushThroughBone(victimAgent, BoneBodyPartType.ShoulderLeft, contact, out float leftDistanceSquared);
                            FindCrushThroughBone(victimAgent, BoneBodyPartType.ShoulderRight, contact, out float rightDistanceSquared);
                            if (leftDistanceSquared < rightDistanceSquared)
                            {
                                return BoneBodyPartType.ShoulderLeft;
                            }
                            if (rightDistanceSquared < leftDistanceSquared)
                            {
                                return BoneBodyPartType.ShoulderRight;
                            }
                        }
                    }
                    return BoneBodyPartType.Chest;
                }
                if (belowEyes < CrushThroughLegsBelowEyes)
                {
                    return BoneBodyPartType.Abdomen;
                }
                return BoneBodyPartType.Legs;
            }

            // Body part of each bone, per skeleton type (Monster id). Main thread only. Holds no agents.
            private static readonly Dictionary<string, BoneBodyPartType[]> BoneBodyPartsByMonster = new Dictionary<string, BoneBodyPartType[]>();

            // The defender's bone of the given body part nearest the contact, or -1 when he has no skeleton or no such bone.
            private static sbyte FindCrushThroughBone(Agent victimAgent, BoneBodyPartType bodyPart, Vec3 contact)
            {
                return FindCrushThroughBone(victimAgent, bodyPart, contact, out _);
            }

            // As above, with that bone's squared distance to the contact (float.MaxValue when there is none).
            private static sbyte FindCrushThroughBone(Agent victimAgent, BoneBodyPartType bodyPart, Vec3 contact, out float nearestDistanceSquared)
            {
                nearestDistanceSquared = float.MaxValue;
                MBAgentVisuals visuals = victimAgent.AgentVisuals;
                Skeleton skeleton = visuals?.GetSkeleton();
                if (skeleton == null)
                {
                    return -1;
                }
                if (!BoneBodyPartsByMonster.TryGetValue(victimAgent.Monster.StringId, out BoneBodyPartType[] boneParts))
                {
                    boneParts = new BoneBodyPartType[skeleton.GetBoneCount()];
                    for (sbyte boneIndex = 0; boneIndex < boneParts.Length; boneIndex++)
                    {
                        boneParts[boneIndex] = visuals.GetBoneTypeData(boneIndex).BodyPartType;
                    }
                    BoneBodyPartsByMonster[victimAgent.Monster.StringId] = boneParts;
                }
                MatrixFrame globalFrame = visuals.GetGlobalFrame();
                sbyte nearestBone = -1;
                for (sbyte boneIndex = 0; boneIndex < boneParts.Length; boneIndex++)
                {
                    if (boneParts[boneIndex] != bodyPart)
                    {
                        continue;
                    }
                    float distanceSquared = globalFrame.TransformToParent(skeleton.GetBoneEntitialFrameWithIndex(boneIndex).origin).DistanceSquared(contact);
                    if (distanceSquared < nearestDistanceSquared)
                    {
                        nearestDistanceSquared = distanceSquared;
                        nearestBone = boneIndex;
                    }
                }
                return nearestBone;
            }

        }
    }
}
