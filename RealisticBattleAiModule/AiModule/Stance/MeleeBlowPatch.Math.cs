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

                float basePostureDamage = getDefenderPostureDamage(defenderAgent, attackerAgent, collisionData.AttackDirection, (StrikeType)collisionData.StrikeType, meleeHitType);

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
                BattleEnvironment env = defenderAgent.CurrentBattleEnvironment;
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
                        PerkHelper.AddPerkBonusForCharacter(DefaultPerks.Athletics.Fury, env, character, true, ref handling);
                        if (captain != null)
                        {
                            PerkHelper.AddPerkBonusFromCaptain(DefaultPerks.Athletics.Fury, env, captain, ref handling);
                        }
                    }
                    if (wielded.RelevantSkill == DefaultSkills.OneHanded)
                    {
                        PerkHelper.AddPerkBonusForCharacter(DefaultPerks.OneHanded.WrappedHandles, env, character, true, ref handling);
                    }
                    else if (wielded.RelevantSkill == DefaultSkills.TwoHanded)
                    {
                        PerkHelper.AddPerkBonusForCharacter(DefaultPerks.TwoHanded.StrongGrip, env, character, true, ref handling);
                    }
                    else if (wielded.RelevantSkill == DefaultSkills.Polearm && wielded.SwingDamageType != DamageTypes.Invalid)
                    {
                        PerkHelper.AddPerkBonusForCharacter(DefaultPerks.Polearm.CounterWeight, env, character, true, ref handling);
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
                    PerkHelper.AddPerkBonusForCharacter(DefaultPerks.Engineering.Scaffolds, env, character, false, ref shieldHp);
                    if (shieldHp.ResultNumber > 0f)
                    {
                        shieldBase /= shieldHp.ResultNumber;
                    }

                    ExplainedNumber shieldDamage = new ExplainedNumber(1f);
                    PerkHelper.AddPerkBonusForCharacter(DefaultPerks.OneHanded.SteelCoreShields, env, character, true, ref shieldDamage);
                    if (onFoot && captain != null)
                    {
                        PerkHelper.AddPerkBonusFromCaptain(DefaultPerks.OneHanded.SteelCoreShields, env, captain, ref shieldDamage);
                    }
                    ExplainedNumber shieldDamageIncorrect = shieldDamage;
                    PerkHelper.AddPerkBonusForCharacter(DefaultPerks.OneHanded.ShieldWall, env, character, true, ref shieldDamageIncorrect);

                    shieldFactor = shieldBase * Math.Max(0f, shieldDamage.ResultNumber);
                    shieldIncorrectFactor = shieldBase * Math.Max(0f, shieldDamageIncorrect.ResultNumber);
                }
            }

            private static float calculateAttackerPostureDamage(Agent defenderAgent, Agent attackerAgent, ref AttackCollisionData collisionData, MissionWeapon weapon, float comHitModifier, MeleeHitType meleeHitType, bool isUnarmedAttack)
            {
                float result = 0f;

                float strengthSkillModifier = 500f;
                float weaponSkillModifier = 500f;

                float basePostureDamage = getAttackerPostureDamage(defenderAgent, attackerAgent, collisionData.AttackDirection, (StrikeType)collisionData.StrikeType, meleeHitType);

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

            /// <summary>
            /// Key for the sweet-spot magnitude cache. The computation reads nothing agent-specific
            /// beyond <paramref name="relevantSkill"/>: the <c>character</c> parameter is unused, and
            /// everything else comes off the weapon (item, item modifier via the modified swing speed,
            /// and the two usage indices).
            /// </summary>
            private struct SweetSpotKey : IEquatable<SweetSpotKey>
            {
                private readonly ItemObject _item;
                private readonly ItemModifier _modifier;
                private readonly int _usageIndex;
                private readonly int _currentUsageIndex;
                private readonly int _skill;

                public SweetSpotKey(ItemObject item, ItemModifier modifier, int usageIndex, int currentUsageIndex, int skill)
                {
                    _item = item;
                    _modifier = modifier;
                    _usageIndex = usageIndex;
                    _currentUsageIndex = currentUsageIndex;
                    _skill = skill;
                }

                public bool Equals(SweetSpotKey other)
                {
                    return _item == other._item
                        && _modifier == other._modifier
                        && _usageIndex == other._usageIndex
                        && _currentUsageIndex == other._currentUsageIndex
                        && _skill == other._skill;
                }

                public override bool Equals(object obj)
                {
                    return obj is SweetSpotKey && Equals((SweetSpotKey)obj);
                }

                public override int GetHashCode()
                {
                    int hash = _item != null ? _item.GetHashCode() : 0;
                    hash = (hash * 397) ^ (_modifier != null ? _modifier.GetHashCode() : 0);
                    hash = (hash * 397) ^ _usageIndex;
                    hash = (hash * 397) ^ _currentUsageIndex;
                    hash = (hash * 397) ^ _skill;
                    return hash;
                }
            }

            private static readonly Dictionary<SweetSpotKey, float> _sweetSpotMagnitudeCache = new Dictionary<SweetSpotKey, float>();

            public static void ClearSweetSpotMagnitudeCache()
            {
                lock (_sweetSpotMagnitudeCache)
                {
                    _sweetSpotMagnitudeCache.Clear();
                }
            }

            public static float CalculateSweetSpotSwingMagnitude(BasicCharacterObject character, MissionWeapon weapon, int weaponUsageIndex, int relevantSkill)
            {
                if (weapon.Item != null)
                {
                    SweetSpotKey key = new SweetSpotKey(weapon.Item, weapon.ItemModifier, weaponUsageIndex, weapon.CurrentUsageIndex, relevantSkill);
                    float cached;
                    lock (_sweetSpotMagnitudeCache)
                    {
                        if (_sweetSpotMagnitudeCache.TryGetValue(key, out cached))
                        {
                            return cached;
                        }
                    }
                    float computed = CalculateSweetSpotSwingMagnitudeUncached(character, weapon, weaponUsageIndex, relevantSkill);
                    lock (_sweetSpotMagnitudeCache)
                    {
                        _sweetSpotMagnitudeCache[key] = computed;
                    }
                    return computed;
                }
                return CalculateSweetSpotSwingMagnitudeUncached(character, weapon, weaponUsageIndex, relevantSkill);
            }

            private static float CalculateSweetSpotSwingMagnitudeUncached(BasicCharacterObject character, MissionWeapon weapon, int weaponUsageIndex, int relevantSkill)
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
                    int targetWeaponUsageIndex = targetWeapon.CurrentUsageIndex;
                    BasicCharacterObject currentSelectedChar = attacker.Character;

                    if (currentSelectedChar != null && isUnarmedAttack)
                    {
                        int realDamage = 0;
                        int effectiveSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attacker, DefaultSkills.Athletics);
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
                            int effectiveSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attacker, skill);
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

        }
    }
}
