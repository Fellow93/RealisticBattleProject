using HarmonyLib;
using Helpers;
using SandBox;
using StoryMode.GameModels;
using System;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.SandBox.GameComponents.Map;
using TaleWorlds.Core;
using TaleWorlds.DotNet;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.CampaignSystem.CombatXpModel;
using static TaleWorlds.MountAndBlade.Agent;

namespace RealisticBattleCombatModule
{
    class DamageRework
    {
        //public static Dictionary<int, bool> hasMissilePenetrated = new Dictionary<int, bool> { };

        //[UsedImplicitly]
        //[MBCallback]
        //[HarmonyPatch(typeof(Mission))]
        //[HarmonyPatch("MissileHitCallback")]
        //public class CreateMissileBlowPatch
        //{
        //    static bool Prefix(ref bool __result, ref Dictionary<int, Missile> ____missiles, ref Mission __instance, ref AttackCollisionData collisionData, ref Vec3 missileStartingPosition, ref Vec3 missilePosition, Vec3 missileAngularVelocity, Vec3 movementVelocity, MatrixFrame attachGlobalFrame, MatrixFrame affectedShieldGlobalFrame, int numDamagedAgents, Agent attacker, Agent victim, GameEntity hitEntity)
        //    {
        //        Missile missile = ____missiles[collisionData.AffectorWeaponSlotOrMissileIndex];
        //        if (collisionData.AttackBlockedWithShield)
        //        {
        //            //TODO: code to decide if arrow penerated
        //            hasMissilePenetrated[collisionData.AffectorWeaponSlotOrMissileIndex] = false;
        //        }
        //        if (collisionData.CollidedWithShieldOnBack)
        //        {
        //            hasMissilePenetrated[collisionData.AffectorWeaponSlotOrMissileIndex] = true;
        //        }
        //        bool hasPenetrated = false;
        //        if (collisionData.CollisionResult == CombatCollisionResult.StrikeAgent && hasMissilePenetrated.TryGetValue(collisionData.AffectorWeaponSlotOrMissileIndex, out hasPenetrated))
        //        {
        //            switch (collisionData.VictimHitBodyPart)
        //            {
        //                case BoneBodyPartType.ArmLeft:
        //                case BoneBodyPartType.ShoulderLeft:
        //                    {
        //                        return true;
        //                    }
        //            }
        //            return false;
        //        }
        //        return true;
        //    }

        //    static void Postfix(ref bool __result, ref Dictionary<int, Missile> ____missiles, ref Mission __instance, ref AttackCollisionData collisionData, Vec3 missileStartingPosition, Vec3 missilePosition, Vec3 missileAngularVelocity, Vec3 movementVelocity, MatrixFrame attachGlobalFrame, MatrixFrame affectedShieldGlobalFrame, int numDamagedAgents, Agent attacker, Agent victim, GameEntity hitEntity)
        //    {
        //        Missile missile = ____missiles[collisionData.AffectorWeaponSlotOrMissileIndex];
        //        if (collisionData.AttackBlockedWithShield || collisionData.CollidedWithShieldOnBack)
        //        {
        //            if (collisionData.AttackBlockedWithShield)
        //            {
        //                EquipmentIndex wieldedItemIndex = victim.GetWieldedItemIndex(Agent.HandIndex.OffHand);
        //                if ((float)collisionData.InflictedDamage > TaleWorlds.Core.ManagedParameters.Instance.GetManagedParameter(TaleWorlds.Core.ManagedParametersEnum.ShieldPenetrationOffset) - 5 + TaleWorlds.Core.ManagedParameters.Instance.GetManagedParameter(TaleWorlds.Core.ManagedParametersEnum.ShieldPenetrationFactor) * (float)victim.Equipment[wieldedItemIndex].GetGetModifiedArmorForCurrentUsage())
        //                {
        //                    bool hasPenetrated = false;
        //                    if (hasMissilePenetrated.TryGetValue(collisionData.AffectorWeaponSlotOrMissileIndex, out hasPenetrated))
        //                    {
        //                        __result = false;
        //                        return;
        //                    }
        //                }
        //            }else if (collisionData.CollidedWithShieldOnBack)
        //            {
        //                for (EquipmentIndex shieldIndex = EquipmentIndex.WeaponItemBeginSlot; shieldIndex < EquipmentIndex.Weapon4; shieldIndex++)
        //                {
        //                    if (victim.Equipment[shieldIndex].CurrentUsageItem != null && (victim.Equipment[shieldIndex].CurrentUsageItem.WeaponClass == WeaponClass.LargeShield || victim.Equipment[shieldIndex].CurrentUsageItem.WeaponClass == WeaponClass.SmallShield))
        //                    {
        //                        if ((float)collisionData.InflictedDamage > TaleWorlds.Core.ManagedParameters.Instance.GetManagedParameter(TaleWorlds.Core.ManagedParametersEnum.ShieldPenetrationOffset) - 5 + TaleWorlds.Core.ManagedParameters.Instance.GetManagedParameter(TaleWorlds.Core.ManagedParametersEnum.ShieldPenetrationFactor) * (float)victim.Equipment[shieldIndex].GetGetModifiedArmorForCurrentUsage())
        //                        {
        //                            bool hasPenetrated = false;
        //                            if (hasMissilePenetrated.TryGetValue(collisionData.AffectorWeaponSlotOrMissileIndex, out hasPenetrated))
        //                            {
        //                                __result = false;
        //                                return;
        //                            }
        //                        }
        //                    }
        //                }
        //            }
        //            hasMissilePenetrated.Remove(collisionData.AffectorWeaponSlotOrMissileIndex);
        //        }
        //    }
        //}

        //[HarmonyPatch(typeof(Mission))]
        //[HarmonyPatch("HandleMissileCollisionReaction")]
        //class HandleMissileCollisionReactionPatch
        //{
        //    static bool Prefix(ref Mission __instance, ref Dictionary<int, Missile> ____missiles, int missileIndex, MissileCollisionReaction collisionReaction, MatrixFrame attachLocalFrame, Agent attackerAgent, Agent attachedAgent, bool attachedToShield, sbyte attachedBoneIndex, MissionObject attachedMissionObject, Vec3 bounceBackVelocity, Vec3 bounceBackAngularVelocity, int forcedSpawnIndex)
        //    {
        //        Missile missile = ____missiles[missileIndex];
        //        MissionObjectId missionObjectId = new MissionObjectId(-1, createdAtRuntime: true);
        //        if (collisionReaction == MissileCollisionReaction.Stick)
        //        {
        //            bool hasPenetrated = false;
        //            if (hasMissilePenetrated.TryGetValue(missileIndex, out hasPenetrated))
        //            {
        //                if (hasPenetrated)
        //                {
        //                    missile.Entity.Remove(81);
        //                    //hasMissilePenetrated.Remove(missileIndex);
        //                    return false;
        //                }
        //                //missile.Entity.SetVisibilityExcludeParents(visible: true);
        //                //missionObjectId = __instance.SpawnWeaponAsDropFromMissile(missileIndex, null, ref attachLocalFrame, WeaponSpawnFlags.AsMissile | WeaponSpawnFlags.WithPhysics, ref bounceBackVelocity, ref bounceBackAngularVelocity, forcedSpawnIndex);

        //                if (!attachedToShield)
        //                {
        //                    hasMissilePenetrated.Remove(missileIndex);
        //                    return false;
        //                }

        //                //foreach (MissionBehavior missionBehavior in __instance.MissionBehaviors)
        //                //{
        //                //    missionBehavior.OnMissileCollisionReaction(MissileCollisionReaction.Stick, attackerAgent, attachedAgent, attachedBoneIndex);
        //                //}
        //            }
        //        }
        //        return true;
        //    }
        //}

        [HarmonyPatch(typeof(DefaultDamageParticleModel))]
        [HarmonyPatch("GetMissileAttackParticle")]
        public class GetMissileAttackParticlePatch
        {
            static bool Prefix(ref int __result, ref Mission __instance, Agent attacker, Agent victim, in Blow blow, in AttackCollisionData collisionData)
            {
                //bool hasPenetrated = false;
                //if (hasMissilePenetrated.TryGetValue(collisionData.AffectorWeaponSlotOrMissileIndex, out hasPenetrated))
                //{
                //    __result = ParticleSystemManager.GetRuntimeIdByName("psys_game_sweat_sword_enter");
                //    return false;
                //}
                return true;
            }
        }

        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("ComputeBlowMagnitudeMissile")]
        class RealArrowDamage
        {
            static bool Prefix(ref AttackCollisionData acd, ItemObject weaponItem, bool isVictimAgentNull, float momentumRemaining, float missileTotalDamage, out float baseMagnitude, out float specialMagnitude, Vec2 victimVelocity)
            {
                Vec3 missileVelocity = acd.MissileVelocity;
                //Vec3 gcn = acd.CollisionGlobalNormal;
                // Vec3 wbd = acd.MissileVelocity;

                //float angleModifier = Vec3.DotProduct(gcn, wbd);

                //Vec3 resultVec = gcn + wbd;
                //float angleModifier = 1f - Math.Abs((resultVec.x + resultVec.y + resultVec.z) / 3);

                float length;
                if (!isVictimAgentNull)
                {
                    length = (victimVelocity.ToVec3() - missileVelocity).Length;
                }
                else
                {
                    length = missileVelocity.Length;
                }
                //float expr_32 = length / acd.MissileStartingBaseSpeed;
                //float num = expr_32 * expr_32;
                if (weaponItem != null && weaponItem.PrimaryWeapon != null)
                {
                    switch (weaponItem.PrimaryWeapon.WeaponClass.ToString())
                    {
                        case "Boulder":
                        case "Stone":
                            {
                                missileTotalDamage *= 0.01f;
                                break;
                            }
                        case "ThrowingAxe":
                        case "ThrowingKnife":
                        case "Dagger":
                            {
                                length -= 0f; //5f
                                //if (length < 5.0f)
                                //{
                                //    length = 5f;
                                //}
                                //length += -(7.0f);
                                //if (length < 5.0f)
                                //{
                                //    length = 5.0f;
                                //} 
                                break;
                            }
                        case "Javelin":
                            {
                                length -= 10f;
                                if (length < 5.0f)
                                {
                                    length = 5f;
                                }
                                //missileTotalDamage += 168.0f;
                                //missileTotalDamage *= 0.01f;
                                //missileTotalDamage = 1f;
                                break;
                            }
                        case "OneHandedPolearm":
                            {
                                length -= 10f;
                                if (length < 5.0f)
                                {
                                    length = 5f;
                                }
                                //missileTotalDamage -= 25f;
                                //missileTotalDamage = 1f;
                                break;
                            }
                        case "LowGripPolearm":
                            {
                                length -= 10f;
                                if (length < 5.0f)
                                {
                                    length = 5f;
                                }
                                //missileTotalDamage -= 25f;
                                //missileTotalDamage *= 0.01f;
                                //missileTotalDamage = 1f;
                                break;
                            }
                        case "Arrow":
                            {
                                missileTotalDamage -= 100f;
                                missileTotalDamage *= 0.01f;
                                break;
                            }
                        case "Bolt":
                            {
                                missileTotalDamage -= 100f;
                                missileTotalDamage *= 0.01f;
                                break;
                            }
                    }
                }

                float physicalDamage = ((length * length) * (weaponItem.Weight)) / 2;
                float momentumDamage = (length * weaponItem.Weight);
                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Javelin") && physicalDamage > 300f)
                {
                    physicalDamage = 300f;
                }

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("OneHandedPolearm") && physicalDamage > (weaponItem.Weight) * 300f)
                {
                    physicalDamage = (weaponItem.Weight) * 300f;
                }

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Arrow") && physicalDamage > (weaponItem.Weight) * 2250f)
                {
                    physicalDamage = (weaponItem.Weight) * 2250f;
                }

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Bolt") && physicalDamage > (weaponItem.Weight) * 2500f)
                {
                    physicalDamage = (weaponItem.Weight) * 2500f;
                }

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Stone") ||
                    weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Boulder"))
                {
                    physicalDamage = (length * (weaponItem.Weight));
                }

                baseMagnitude = physicalDamage * missileTotalDamage * momentumRemaining;

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Javelin"))
                {
                    baseMagnitude = (physicalDamage * momentumRemaining + (missileTotalDamage * 0.5f)) * XmlConfig.dict["Global.ThrustModifier"];
                }

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("ThrowingAxe"))
                {
                    baseMagnitude = physicalDamage * momentumRemaining + (missileTotalDamage * 1f);
                }

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("ThrowingKnife") ||
                    weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Dagger"))
                {
                    baseMagnitude = (physicalDamage * momentumRemaining + (missileTotalDamage * 0f)) * XmlConfig.dict["Global.ThrustModifier"] * 0.6f;
                }

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("OneHandedPolearm") ||
                    weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("LowGripPolearm"))
                {
                    baseMagnitude = (physicalDamage * momentumRemaining + (missileTotalDamage * 1f)) * XmlConfig.dict["Global.ThrustModifier"];
                }
                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Arrow") ||
                    weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Bolt"))
                {
                    if ((DamageTypes)acd.DamageType == DamageTypes.Cut || (DamageTypes)acd.DamageType == DamageTypes.Pierce)
                    {
                        baseMagnitude = physicalDamage * missileTotalDamage * momentumRemaining;
                    }
                    else
                    {
                        baseMagnitude = momentumDamage * missileTotalDamage * momentumRemaining; // momentum makes more sense for blunt attacks, maybe 500 damage is needed for sling projectiles
                    }
                }
                specialMagnitude = baseMagnitude;

                return false;
            }
        }
    }

    [HarmonyPatch(typeof(Mission))]
    [HarmonyPatch("GetEntityDamageMultiplier")]
    class GetEntityDamageMultiplierPatch
    {
        static bool Prefix(bool isAttackerAgentDoingPassiveAttack, WeaponComponentData weapon, DamageTypes damageType, bool isWoodenBody, ref float __result)
        {
            float dmgMultiplier = 1f;
            if (isAttackerAgentDoingPassiveAttack)
            {
                dmgMultiplier *= 0.2f;
            }
            if (weapon != null)
            {
                if (weapon.WeaponFlags.HasAnyFlag(WeaponFlags.BonusAgainstShield))
                {
                    dmgMultiplier *= 1.2f;
                }
                switch (damageType)
                {
                    case DamageTypes.Cut:
                        if(weapon.WeaponClass == WeaponClass.Arrow || weapon.WeaponClass == WeaponClass.Bolt || weapon.WeaponClass == WeaponClass.Javelin)
                        {
                            dmgMultiplier *= 0.1f;
                        }
                        else
                        {
                            dmgMultiplier *= 0.8f;
                        }
                        break;
                    case DamageTypes.Pierce:
                        if(weapon.WeaponClass == WeaponClass.Arrow || weapon.WeaponClass == WeaponClass.Bolt || weapon.WeaponClass == WeaponClass.Javelin)
                        {
                            dmgMultiplier *= 0.1f;
                        }
                        else
                        {
                            dmgMultiplier *= 0.2f;
                        }
                        break;
                }
                if (isWoodenBody && weapon.WeaponFlags.HasAnyFlag(WeaponFlags.Burning))
                {
                    dmgMultiplier *= 1.5f;
                }
            }
            __result = dmgMultiplier;
            return false;
        }
    }

    [HarmonyPatch(typeof(CombatStatCalculator))]
    [HarmonyPatch("CalculateStrikeMagnitudeForPassiveUsage")]
    class ChangeLanceDamage
    {
        static bool Prefix(float weaponWeight, float extraLinearSpeed, ref float __result)
        {
            __result = CalculateStrikeMagnitudeForThrust(0f, weaponWeight, extraLinearSpeed, isThrown: false);
            return false;
        }

        private static float CalculateStrikeMagnitudeForThrust(float thrustWeaponSpeed, float weaponWeight, float extraLinearSpeed, bool isThrown)
        {
            float num = extraLinearSpeed * 1f; // because cav in the game is roughly 50% faster than it should be
            float num2 = 0.5f * weaponWeight * num * num * XmlConfig.dict["Global.ThrustModifier"]; // lances need to have 3 times more damage to be preferred over maces
            return num2;

        }
    }

    [HarmonyPatch(typeof(CombatStatCalculator))]
    [HarmonyPatch("CalculateStrikeMagnitudeForThrust")]
    class CalculateStrikeMagnitudeForThrustPatch
    {
        static bool Prefix(float thrustWeaponSpeed, float weaponWeight, float extraLinearSpeed, bool isThrown, ref float __result)
        {
            float combinedSpeed = thrustWeaponSpeed + extraLinearSpeed;
            if (combinedSpeed > 0f)
            {
                if (!isThrown && weaponWeight < 2.1f)
                {
                    weaponWeight += 0.5f;
                }
                float kineticEnergy = 0.5f * weaponWeight * combinedSpeed * combinedSpeed;
                float mixedEnergy = 0.5f *  (weaponWeight + 1.5f) * combinedSpeed * combinedSpeed;
                //float basedamage = 0.5f * (weaponWeight + 4.5f) * combinedSpeed * combinedSpeed;
                float basedamage = 120f;
                if (mixedEnergy > 120f)
                {
                    basedamage = mixedEnergy;
                }
                //float handBonus = 0.5f * (weaponWeight + 1.5f) * combinedSpeed * combinedSpeed;
                //float handLimit = 120f;
                //if (handBonus > handLimit)
                //{
                //    handBonus = handLimit;
                //}
                //float thrust = handBonus;
                //if (kineticEnergy > handLimit)
                //{
                //    thrust = kineticEnergy;
                //}
                else if (basedamage > 180f)
                {
                    basedamage = 180f;
                }
                float thrust = basedamage;
                if (kineticEnergy > basedamage)
                {
                    thrust = kineticEnergy;
                }

                //if (thrust > 200f)
                //{
                //    thrust = 200f;
                //}
                __result = thrust * XmlConfig.dict["Global.ThrustModifier"];
                return false;
            }
            __result = 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(Mission))]
    [HarmonyPatch("ComputeBlowDamage")]
    class OverrideDamageCalc
    {
        static bool Prefix(ref AttackInformation attackInformation, ref AttackCollisionData attackCollisionData, in MissionWeapon attackerWeapon, DamageTypes damageType, float magnitude, int speedBonus, bool cancelDamage, out int inflictedDamage, out int absorbedByArmor)
        {
            float armorAmountFloat = attackInformation.ArmorAmountFloat;
            WeaponComponentData shieldOnBack = attackInformation.ShieldOnBack;
            AgentFlag victimAgentFlag = attackInformation.VictimAgentFlag;
            float victimAgentAbsorbedDamageRatio = attackInformation.VictimAgentAbsorbedDamageRatio;
            float damageMultiplierOfBone = attackInformation.DamageMultiplierOfBone;
            float combatDifficultyMultiplier = attackInformation.CombatDifficultyMultiplier;
            _ = attackCollisionData.CollisionGlobalPosition;
            bool attackBlockedWithShield = attackCollisionData.AttackBlockedWithShield;
            bool collidedWithShieldOnBack = attackCollisionData.CollidedWithShieldOnBack;
            bool isFallDamage = attackCollisionData.IsFallDamage;
            BasicCharacterObject attackerAgentCharacter = attackInformation.AttackerAgentCharacter;
            BasicCharacterObject attackerCaptainCharacter = attackInformation.AttackerCaptainCharacter;
            BasicCharacterObject victimAgentCharacter = attackInformation.VictimAgentCharacter;
            BasicCharacterObject victimCaptainCharacter = attackInformation.VictimCaptainCharacter;

            float armorAmount = 0f;

            if (!isFallDamage)
            {
                float adjustedArmor = Game.Current.BasicModels.StrikeMagnitudeModel.CalculateAdjustedArmorForBlow(armorAmountFloat, attackerAgentCharacter, attackerCaptainCharacter, victimAgentCharacter, victimCaptainCharacter, attackerWeapon.CurrentUsageItem);
                armorAmount = adjustedArmor;
            }
            //float num2 = (float)armorAmount;
            if (collidedWithShieldOnBack && shieldOnBack != null)
            {
                armorAmount += 20f;
            }

            string weaponType = "otherDamage";
            if (attackerWeapon.Item != null && attackerWeapon.Item.PrimaryWeapon != null)
            {
                weaponType = attackerWeapon.Item.PrimaryWeapon.WeaponClass.ToString();
            }

            IAgentOriginBase attackerAgentOrigin = attackInformation.AttackerAgentOrigin;
            Formation attackerFormation = attackInformation.AttackerFormation;

            if (!attackCollisionData.IsAlternativeAttack && !attackInformation.IsAttackerAgentMount && attackerAgentOrigin != null && attackInformation.AttackerAgentCharacter != null && !attackCollisionData.IsMissile)
            {
                SkillObject skill = (attackerWeapon.CurrentUsageItem == null) ? DefaultSkills.Athletics : attackerWeapon.CurrentUsageItem.RelevantSkill;
                if (skill != null)
                {
                    int effectiveSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attackInformation.AttackerAgentCharacter, attackerAgentOrigin, attackerFormation, skill);
                    bool isPassiveUsage = attackInformation.IsAttackerAgentDoingPassiveAttack;
                    float skillBasedDamage = 0f;
                    const float ashBreakTreshold = 430f;
                    float BraceBonus = 0f;
                    float BraceModifier = 0.34f; // because lances have 3 times more damage

                    switch (weaponType)
                    {
                        case "Dagger":
                        case "OneHandedSword":
                        case "ThrowingKnife":
                            {
                                if (damageType == DamageTypes.Cut)
                                {
                                    skillBasedDamage = magnitude + 40f + (effectiveSkill * 0.53f);
                                }
                                else
                                {
                                    skillBasedDamage = magnitude * 0.2f + 50f * XmlConfig.dict["Global.ThrustModifier"] + (effectiveSkill * 0.46f * XmlConfig.dict["Global.ThrustModifier"]);
                                }
                                if (magnitude > 1f)
                                {
                                    magnitude = skillBasedDamage;
                                }
                                break;
                            }
                        case "TwoHandedSword":
                            {
                                if (damageType == DamageTypes.Cut)
                                {
                                    skillBasedDamage = magnitude + (40f + (effectiveSkill * 0.53f)) * 1.3f;
                                }
                                else
                                {
                                    skillBasedDamage = (magnitude * 0.2f + 50f * XmlConfig.dict["Global.ThrustModifier"] + (effectiveSkill * 0.46f * XmlConfig.dict["Global.ThrustModifier"])) * 1.3f;
                                }
                                if (magnitude > 1f)
                                {
                                    magnitude = skillBasedDamage;
                                }
                                break;
                            }
                        case "OneHandedAxe":
                        case "ThrowingAxe":
                            {
                                skillBasedDamage = magnitude + 60f + (effectiveSkill * 0.4f);
                                if (magnitude > 1f)
                                {
                                    magnitude = skillBasedDamage;
                                }
                                break;
                            }
                        case "OneHandedBastardAxe":
                            {
                                skillBasedDamage = magnitude + (60f + (effectiveSkill * 0.4f)) * 1.15f;
                                if (magnitude > 1f)
                                {
                                    magnitude = skillBasedDamage;
                                }
                                break;
                            }
                        case "TwoHandedAxe":
                            {
                                skillBasedDamage = magnitude + (60f + (effectiveSkill * 0.4f)) * 1.3f;
                                if (magnitude > 1f)
                                {
                                    magnitude = skillBasedDamage;
                                }
                                break;
                            }
                        case "Mace":
                            {
                                if (damageType == DamageTypes.Pierce)
                                {
                                    skillBasedDamage = magnitude * 0.2f + 40f * XmlConfig.dict["Global.ThrustModifier"] + (effectiveSkill * 0.4f * XmlConfig.dict["Global.ThrustModifier"]);
                                }
                                else
                                {
                                    skillBasedDamage = magnitude + 30f + (effectiveSkill * 0.13f);
                                }
                                if (magnitude > 1f)
                                {
                                    magnitude = skillBasedDamage;
                                }
                                break;
                            }
                        case "TwoHandedMace":
                            {
                                if (damageType == DamageTypes.Pierce)
                                {
                                    skillBasedDamage = (magnitude * 0.2f + 40f * XmlConfig.dict["Global.ThrustModifier"] + (effectiveSkill * 0.4f * XmlConfig.dict["Global.ThrustModifier"])) * 1.3f;
                                }
                                else
                                {
                                    skillBasedDamage = magnitude + (45f + (effectiveSkill * 0.13f) * 1.3f);
                                }
                                if (magnitude > 1f)
                                {
                                    magnitude = skillBasedDamage;
                                }
                                break;
                            }
                        case "OneHandedPolearm":
                            {
                                if (damageType == DamageTypes.Cut)
                                {
                                    skillBasedDamage = magnitude + 50f + (effectiveSkill * 0.46f);
                                }
                                else if (damageType == DamageTypes.Blunt)
                                {
                                    skillBasedDamage = magnitude + 30f + (effectiveSkill * 0.13f);
                                }
                                else
                                {
                                    if (isPassiveUsage)
                                    {
                                        float couchedSkill = 0.5f + effectiveSkill * 0.015f;
                                        float skillCap = (100f + effectiveSkill * 1.3f);

                                        float weaponWeight = attackerWeapon.Item.Weight;

                                        if (weaponWeight < 2.1f)
                                        {
                                            BraceBonus += 0.5f;
                                            BraceModifier *= 3f;
                                        }
                                        float lanceBalistics = (magnitude * BraceModifier) / weaponWeight;
                                        float CouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus);
                                        magnitude = CouchedMagnitude;
                                        if (CouchedMagnitude > (skillCap * XmlConfig.dict["Global.ThrustModifier"]) && (lanceBalistics * (weaponWeight + BraceBonus)) < (skillCap * XmlConfig.dict["Global.ThrustModifier"])) //skill based damage
                                        {
                                            magnitude = skillCap * XmlConfig.dict["Global.ThrustModifier"];
                                        }

                                        if ((lanceBalistics * (weaponWeight + BraceBonus)) >= (skillCap * XmlConfig.dict["Global.ThrustModifier"])) //ballistics
                                        {
                                            magnitude = (lanceBalistics * (weaponWeight + BraceBonus));
                                        }

                                        if (magnitude > (ashBreakTreshold * XmlConfig.dict["Global.ThrustModifier"])) // damage cap - lance break threshold
                                        {
                                            magnitude = ashBreakTreshold * XmlConfig.dict["Global.ThrustModifier"];
                                        }
                                    }
                                    else
                                    {
                                        float weaponWeight = attackerWeapon.Item.Weight;

                                        if (weaponWeight > 2.1f)
                                        {
                                            magnitude *= 0.34f;
                                        }
                                        skillBasedDamage = magnitude * 0.4f + 60f * XmlConfig.dict["Global.ThrustModifier"] + (effectiveSkill * 0.26f * XmlConfig.dict["Global.ThrustModifier"]);
                                        if (skillBasedDamage > 260f * XmlConfig.dict["Global.ThrustModifier"])
                                        {
                                            skillBasedDamage = 260f * XmlConfig.dict["Global.ThrustModifier"];
                                        }
                                    }
                                }
                                if (magnitude > 1f && !isPassiveUsage)
                                {
                                    magnitude = skillBasedDamage;
                                }
                                break;
                            }
                        case "TwoHandedPolearm":
                            {
                                if (damageType == DamageTypes.Cut)
                                {
                                    skillBasedDamage = magnitude + (50f + (effectiveSkill * 0.46f)) * 1.3f;
                                }
                                else if (damageType == DamageTypes.Blunt)
                                {
                                    skillBasedDamage = magnitude + (30f + (effectiveSkill * 0.13f) * 1.3f);
                                }
                                else
                                {
                                    if (isPassiveUsage)
                                    {
                                        float couchedSkill = 0.5f + effectiveSkill * 0.015f;
                                        float skillCap = (100f + effectiveSkill * 1.3f);

                                        float weaponWeight = attackerWeapon.Item.Weight;

                                        if (weaponWeight < 2.1f)
                                        {
                                            BraceBonus += 0.5f;
                                            BraceModifier *= 3f;
                                        }
                                        float lanceBalistics = (magnitude * BraceModifier) / weaponWeight;
                                        float CouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus);
                                        magnitude = CouchedMagnitude;
                                        if (CouchedMagnitude > (skillCap * XmlConfig.dict["Global.ThrustModifier"]) && (lanceBalistics * (weaponWeight + BraceBonus)) < (skillCap * XmlConfig.dict["Global.ThrustModifier"]))
                                        {
                                            magnitude = skillCap * XmlConfig.dict["Global.ThrustModifier"];
                                        }

                                        if ((lanceBalistics * (weaponWeight + BraceBonus)) >= (skillCap * XmlConfig.dict["Global.ThrustModifier"]))
                                        {
                                            magnitude = (lanceBalistics * (weaponWeight + BraceBonus));
                                        }

                                        if (magnitude > (ashBreakTreshold * XmlConfig.dict["Global.ThrustModifier"]))
                                        {
                                            magnitude = ashBreakTreshold * XmlConfig.dict["Global.ThrustModifier"];
                                        }
                                    }
                                    else
                                    {
                                        float weaponWeight = attackerWeapon.Item.Weight;

                                        if (weaponWeight > 2.1f)
                                        {
                                            magnitude *= 0.34f;
                                        }
                                        skillBasedDamage = (magnitude * 0.4f + 60f * XmlConfig.dict["Global.ThrustModifier"] + (effectiveSkill * 0.26f * XmlConfig.dict["Global.ThrustModifier"])) * 1.3f;
                                        if (skillBasedDamage > 360f * XmlConfig.dict["Global.ThrustModifier"])
                                        {
                                            skillBasedDamage = 360f * XmlConfig.dict["Global.ThrustModifier"];
                                        }
                                    }
                                }
                                if (magnitude > 1f && !isPassiveUsage)
                                {
                                    magnitude = skillBasedDamage;
                                }
                                break;
                            }
                    }
                }
            }

            float dmgMultiplier = 1f;

            if (!attackBlockedWithShield && !isFallDamage)
            {
                switch (attackCollisionData.VictimHitBodyPart)
                {
                    case BoneBodyPartType.Abdomen:
                        {
                            switch (damageType)
                            {
                                case DamageTypes.Pierce:
                                    {
                                        dmgMultiplier *= 1f;
                                        break;
                                    }
                                case DamageTypes.Cut:
                                    {
                                        dmgMultiplier *= 1f;
                                        break;
                                    }
                                case DamageTypes.Blunt:
                                    {
                                        dmgMultiplier *= 1f;
                                        break;
                                    }
                                default:
                                    {
                                        dmgMultiplier *= 0.7f;
                                        break;
                                    }
                            }
                            break;
                        }
                    case BoneBodyPartType.Chest:
                        {
                            switch (damageType)
                            {
                                case DamageTypes.Pierce:
                                    {
                                        dmgMultiplier *= 0.9f;
                                        break;
                                    }
                                case DamageTypes.Cut:
                                    {
                                        dmgMultiplier *= 0.9f;
                                        break;
                                    }
                                case DamageTypes.Blunt:
                                    {
                                        dmgMultiplier *= 0.9f;
                                        break;
                                    }
                                default:
                                    {
                                        dmgMultiplier *= 1f;
                                        break;
                                    }
                            }
                            break;
                        }
                    case BoneBodyPartType.ShoulderLeft:
                    case BoneBodyPartType.ShoulderRight:
                        {
                            switch (damageType)
                            {
                                case DamageTypes.Pierce:
                                    {
                                        dmgMultiplier *= 0.6f;
                                        break;
                                    }
                                case DamageTypes.Cut:
                                    {
                                        dmgMultiplier *= 0.6f;
                                        break;
                                    }
                                case DamageTypes.Blunt:
                                    {
                                        dmgMultiplier *= 0.7f;
                                        break;
                                    }
                                default:
                                    {
                                        dmgMultiplier *= 1f;
                                        break;
                                    }
                            }
                            break;
                        }
                    case BoneBodyPartType.ArmLeft:
                    case BoneBodyPartType.ArmRight:
                        {
                            switch (damageType)
                            {
                                case DamageTypes.Pierce:
                                    {
                                        dmgMultiplier *= 0.5f;
                                        break;
                                    }
                                case DamageTypes.Cut:
                                    {
                                        dmgMultiplier *= 0.6f;
                                        break;
                                    }
                                case DamageTypes.Blunt:
                                    {
                                        dmgMultiplier *= 0.7f;
                                        break;
                                    }
                                default:
                                    {
                                        dmgMultiplier *= 1f;
                                        break;
                                    }
                            }
                            break;
                        }
                    case BoneBodyPartType.Legs:
                        {
                            switch (damageType)
                            {
                                case DamageTypes.Pierce:
                                    {
                                        dmgMultiplier *= 0.5f;
                                        break;
                                    }
                                case DamageTypes.Cut:
                                    {
                                        dmgMultiplier *= 0.6f;
                                        break;
                                    }
                                case DamageTypes.Blunt:
                                    {
                                        dmgMultiplier *= 0.7f;
                                        break;
                                    }
                                default:
                                    {
                                        dmgMultiplier *= 1f;
                                        break;
                                    }
                            }
                            break;
                        }
                    case BoneBodyPartType.Head:
                        {
                            switch (damageType)
                            {
                                case DamageTypes.Pierce:
                                    {
                                        dmgMultiplier *= 1.5f;
                                        break;
                                    }
                                case DamageTypes.Cut:
                                    {
                                        dmgMultiplier *= 1.5f;
                                        break;
                                    }
                                case DamageTypes.Blunt:
                                    {
                                        dmgMultiplier *= 1.5f;
                                        break;
                                    }
                                default:
                                    {
                                        dmgMultiplier *= 1f;
                                        break;
                                    }
                            }
                            break;
                        }
                    case BoneBodyPartType.Neck:
                        {
                            switch (damageType)
                            {
                                case DamageTypes.Pierce:
                                    {
                                        dmgMultiplier *= 1.5f;
                                        break;
                                    }
                                case DamageTypes.Cut:
                                    {
                                        dmgMultiplier *= 1.5f;
                                        break;
                                    }
                                case DamageTypes.Blunt:
                                    {
                                        dmgMultiplier *= 1.5f;
                                        break;
                                    }
                                default:
                                    {
                                        dmgMultiplier *= 1f;
                                        break;
                                    }
                            }
                            break;
                        }
                    default:
                        {
                            dmgMultiplier *= 1f;
                            break;
                        }
                }

                dmgMultiplier *= combatDifficultyMultiplier;
            }

            BasicCharacterObject player = null;
            bool isPlayerVictim = false;
            if (attackerAgentCharacter != null && attackInformation.IsAttackerPlayer)
            {
                player = attackerAgentCharacter;
                isPlayerVictim = false;
            }
            else if (victimAgentCharacter != null && attackInformation.IsVictimPlayer)
            {
                player = victimAgentCharacter;
                isPlayerVictim = true;
            }

            inflictedDamage = MBMath.ClampInt((int)MyComputeDamage(weaponType, damageType, magnitude, armorAmount, victimAgentAbsorbedDamageRatio, player, isPlayerVictim), 0, 2000);

            inflictedDamage = (int)(inflictedDamage * dmgMultiplier);

            //float dmgWithPerksSkills = MissionGameModels.Current.AgentApplyDamageModel.CalculateDamage(ref attackInformation, ref attackCollisionData, in attackerWeapon, inflictedDamage, out float bonusFromSkills);

            //InformationManager.DisplayMessage(new InformationMessage("dmgWithPerksSkills: " + dmgWithPerksSkills + " inflictedDamage: " + inflictedDamage +
            //    " HP: " + attackInformation.VictimAgentHealth));

            int absoluteDamage = MBMath.ClampInt((int)(MyComputeDamage(weaponType, damageType, magnitude, 0f, victimAgentAbsorbedDamageRatio) * dmgMultiplier), 0, 2000);
            absorbedByArmor = absoluteDamage - inflictedDamage;

            return false;
        }

        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("ComputeBlowDamageOnShield")]
        class OverrideDamageCalcShield
        {
            static bool Prefix(bool isAttackerAgentNull, bool isAttackerAgentActive, bool isAttackerAgentDoingPassiveAttack, bool canGiveDamageToAgentShield, bool isVictimAgentLeftStance, MissionWeapon victimShield, ref AttackCollisionData attackCollisionData, WeaponComponentData attackerWeapon, float blowMagnitude)
            {
                attackCollisionData.InflictedDamage = 0;
                if (victimShield.CurrentUsageItem.WeaponFlags.HasAnyFlag(WeaponFlags.CanBlockRanged) & canGiveDamageToAgentShield)
                {
                    DamageTypes damageType = (DamageTypes)attackCollisionData.DamageType;
                    int shieldArmorForCurrentUsage = victimShield.GetGetModifiedArmorForCurrentUsage();
                    float absorbedDamageRatio = 1f;

                    string weaponType = "otherDamage";
                    if (attackerWeapon != null)
                    {
                        weaponType = attackerWeapon.WeaponClass.ToString();
                    }

                    bool isPassiveUsage = isAttackerAgentDoingPassiveAttack;

                    float skillBasedDamage = 0f;
                    const float ashBreakTreshold = 430f;
                    float BraceBonus = 0f;
                    float BraceModifier = 0.34f;

                    switch (weaponType)
                    {
                        case "Dagger":
                        case "OneHandedSword":
                        case "ThrowingKnife":
                            {
                                if(blowMagnitude > 1f)
                                {
                                    if (damageType == DamageTypes.Cut)
                                    {
                                        blowMagnitude = blowMagnitude + 40f;
                                    }
                                    else
                                    {
                                        blowMagnitude = blowMagnitude * 0.2f + 50f * XmlConfig.dict["Global.ThrustModifier"];
                                    }
                                }
                                break;
                            }
                        case "TwoHandedSword":
                            {
                                if (blowMagnitude > 1f)
                                {
                                    if (damageType == DamageTypes.Cut)
                                    {
                                        blowMagnitude = blowMagnitude + 40f * 1.3f;
                                    }
                                    else
                                    {
                                        blowMagnitude = (blowMagnitude * 0.2f + 50f * XmlConfig.dict["Global.ThrustModifier"]) * 1.3f;
                                    }
                                }
                                break;
                            }
                        case "OneHandedAxe":
                        case "ThrowingAxe":
                            {
                                if (blowMagnitude > 1f)
                                {
                                    blowMagnitude = blowMagnitude + 60f;
                                }
                                break;
                            }
                        case "OneHandedBastardAxe":
                            {
                                if (blowMagnitude > 1f)
                                {
                                    blowMagnitude = blowMagnitude + 60f * 1.15f;
                                }
                                break;
                            }
                        case "TwoHandedAxe":
                            {
                                if (blowMagnitude > 1f)
                                {
                                    blowMagnitude = blowMagnitude + 60f * 1.3f;
                                }
                                break;
                            }
                        case "Mace":
                            {
                                if (blowMagnitude > 1f)
                                {
                                    if (damageType == DamageTypes.Pierce)
                                    {
                                        blowMagnitude = blowMagnitude * 0.2f + 40f * XmlConfig.dict["Global.ThrustModifier"];
                                    }
                                    else
                                    {
                                        blowMagnitude = blowMagnitude + 30f;
                                    }
                                }
                                break;
                            }
                        case "TwoHandedMace":
                            {
                                if (blowMagnitude > 1f)
                                {
                                    if (damageType == DamageTypes.Pierce)
                                    {
                                        blowMagnitude = (blowMagnitude * 0.2f + 40f * XmlConfig.dict["Global.ThrustModifier"]) * 1.3f;
                                    }
                                    else
                                    {
                                        blowMagnitude = blowMagnitude + 45f * 1.3f;
                                    }
                                }
                                break;
                            }
                        case "OneHandedPolearm":
                            {
                                if (blowMagnitude > 1f)
                                {
                                    if (damageType == DamageTypes.Cut)
                                    {
                                        blowMagnitude = blowMagnitude + 50f;
                                    }
                                    else if (damageType == DamageTypes.Blunt)
                                    {
                                        blowMagnitude = blowMagnitude + 30f;
                                    }
                                    else
                                    {
                                        if (isPassiveUsage)
                                        {
                                            float couchedSkill = 2f;
                                            float skillCap = 230f;

                                            float weaponWeight = 1.5f;

                                            if (weaponWeight < 2.1f)
                                            {
                                                BraceBonus += 0.5f;
                                                BraceModifier *= 3f;
                                            }
                                            float lanceBalistics = (blowMagnitude * BraceModifier) / weaponWeight;
                                            float CouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus);
                                            blowMagnitude = CouchedMagnitude;
                                            if (CouchedMagnitude > (skillCap * XmlConfig.dict["Global.ThrustModifier"]) && (lanceBalistics * (weaponWeight + BraceBonus)) < (skillCap * XmlConfig.dict["Global.ThrustModifier"])) //skill based damage
                                            {
                                                blowMagnitude = skillCap * XmlConfig.dict["Global.ThrustModifier"];
                                            }

                                            if ((lanceBalistics * (weaponWeight + BraceBonus)) >= (skillCap * XmlConfig.dict["Global.ThrustModifier"])) //ballistics
                                            {
                                                blowMagnitude = (lanceBalistics * (weaponWeight + BraceBonus));
                                            }

                                            if (blowMagnitude > (ashBreakTreshold * XmlConfig.dict["Global.ThrustModifier"])) // damage cap - lance break threshold
                                            {
                                                blowMagnitude = ashBreakTreshold * XmlConfig.dict["Global.ThrustModifier"];
                                            }
                                        }
                                        else
                                        {
                                            //float weaponWeight = attackerWeapon.Item.Weight;

                                            //if (weaponWeight > 2.1f)
                                            //{
                                            //    blowMagnitude *= 0.34f;
                                            //}
                                            blowMagnitude = blowMagnitude * 0.4f + 60f * XmlConfig.dict["Global.ThrustModifier"];
                                            if (blowMagnitude > 260f * XmlConfig.dict["Global.ThrustModifier"])
                                            {
                                                blowMagnitude = 260f * XmlConfig.dict["Global.ThrustModifier"];
                                            }
                                        }
                                    }
                                }
                                break;
                            }
                        case "TwoHandedPolearm":
                            {
                                if (blowMagnitude > 1f)
                                {
                                    if (damageType == DamageTypes.Cut)
                                    {
                                        blowMagnitude = blowMagnitude + 50f * 1.3f;
                                    }
                                    else if (damageType == DamageTypes.Blunt)
                                    {
                                        blowMagnitude = blowMagnitude + 30f * 1.3f;
                                    }
                                    else
                                    {
                                        if (isPassiveUsage)
                                        {
                                            float couchedSkill = 2f;
                                            float skillCap = 230f;

                                            float weaponWeight = 1.5f;

                                            if (weaponWeight < 2.1f)
                                            {
                                                BraceBonus += 0.5f;
                                                BraceModifier *= 3f;
                                            }
                                            float lanceBalistics = (blowMagnitude * BraceModifier) / weaponWeight;
                                            float CouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus);
                                            blowMagnitude = CouchedMagnitude;
                                            if (CouchedMagnitude > (skillCap * XmlConfig.dict["Global.ThrustModifier"]) && (lanceBalistics * (weaponWeight + BraceBonus)) < (skillCap * XmlConfig.dict["Global.ThrustModifier"]))
                                            {
                                                blowMagnitude = skillCap * XmlConfig.dict["Global.ThrustModifier"];
                                            }

                                            if ((lanceBalistics * (weaponWeight + BraceBonus)) >= (skillCap * XmlConfig.dict["Global.ThrustModifier"]))
                                            {
                                                blowMagnitude = (lanceBalistics * (weaponWeight + BraceBonus));
                                            }

                                            if (blowMagnitude > (ashBreakTreshold * XmlConfig.dict["Global.ThrustModifier"]))
                                            {
                                                blowMagnitude = ashBreakTreshold * XmlConfig.dict["Global.ThrustModifier"];
                                            }
                                        }
                                        else
                                        {
                                            float weaponWeight = 1.5f;

                                            if (weaponWeight > 2.1f)
                                            {
                                                blowMagnitude *= 0.34f;
                                            }
                                            skillBasedDamage = (blowMagnitude * 0.4f + 60f * XmlConfig.dict["Global.ThrustModifier"]) * 1.3f;
                                            if (skillBasedDamage > 360f * XmlConfig.dict["Global.ThrustModifier"])
                                            {
                                                skillBasedDamage = 360f * XmlConfig.dict["Global.ThrustModifier"];
                                            }
                                        }
                                    }
                                }
                                break;
                            }
                    }

                    float inflictedDamage = MyComputeDamage(weaponType, damageType, blowMagnitude, (float)shieldArmorForCurrentUsage * 10f , absorbedDamageRatio);

                    if (attackCollisionData.IsMissile)
                    {
                        switch (weaponType)
                        {
                            case "Arrow":
                                {
                                    inflictedDamage *= 1.5f;
                                    break;
                                }
                            case "Bolt":
                                {
                                    inflictedDamage *= 1.5f;
                                    break;
                                }
                            case "Javelin":
                                {
                                    inflictedDamage *= 20f;
                                    break;
                                }
                            case "ThrowingAxe":
                                {
                                    inflictedDamage *= 3f;
                                    break;
                                }
                            case "OneHandedPolearm":
                                {
                                    inflictedDamage *= 5f;
                                    break;
                                }
                            case "LowGripPolearm":
                                {
                                    inflictedDamage *= 5f;
                                    break;
                                }
                            default:
                                {
                                    inflictedDamage *= 0.1f;
                                    break;
                                }
                        }
                    }
                    else if (!attackCollisionData.IsMissile)
                    {
                        switch (weaponType)
                        {
                            case "OneHandedAxe":
                            case "TwoHandedAxe":
                            case "OneHandedBastardAxe":
                            case "TwoHandedPolearm":
                                {
                                    if (attackCollisionData.DamageType == 1)//pierce
                                    {
                                        inflictedDamage *= 0.09f;
                                        break;
                                    }
                                    inflictedDamage *= 1.5f;

                                    break;
                                }
                            default:
                                {
                                    if (attackCollisionData.DamageType == 0) //cut
                                    {
                                        inflictedDamage *= 1f;
                                    }
                                    else if(attackCollisionData.DamageType == 1)//pierce
                                    {
                                        inflictedDamage *= 0.09f;
                                    }
                                    else if (attackCollisionData.DamageType == 2)//blunt
                                    {
                                        inflictedDamage *= 0.75f;
                                    }
                                    break;
                                }
                        }
                    }

                    if (attackerWeapon != null && attackerWeapon.WeaponFlags.HasAnyFlag(WeaponFlags.BonusAgainstShield))
                    {
                        inflictedDamage *= 2f;
                    }

                    if (inflictedDamage > 0f)
                    {
                        if (!isVictimAgentLeftStance)
                        {
                            inflictedDamage *= TaleWorlds.Core.ManagedParameters.Instance.GetManagedParameter(TaleWorlds.Core.ManagedParametersEnum.ShieldRightStanceBlockDamageMultiplier);
                        }
                        if (attackCollisionData.CorrectSideShieldBlock)
                        {
                            inflictedDamage *= TaleWorlds.Core.ManagedParameters.Instance.GetManagedParameter(TaleWorlds.Core.ManagedParametersEnum.ShieldCorrectSideBlockDamageMultiplier);
                        }

                        inflictedDamage = MissionGameModels.Current.AgentApplyDamageModel.CalculateShieldDamage(inflictedDamage);
                        attackCollisionData.InflictedDamage = (int)inflictedDamage;
                    }
                }

                return false;
            }
        }

        private static float MyComputeDamage(string weaponType, DamageTypes damageType, float magnitude, float armorEffectiveness, float absorbedDamageRatio, BasicCharacterObject player = null, bool isPlayerVictim = false)
        {

            float damage = 0f;
            float armorReduction = 100f / (100f + armorEffectiveness * XmlConfig.dict["Global.ArmorMultiplier"]);
            float mag_1h_thrust;
            float mag_2h_thrust;
            float mag_1h_sword_thrust;
            float mag_2h_sword_thrust;

            if (damageType == DamageTypes.Pierce)
            {
                mag_1h_thrust = magnitude * XmlConfig.dict["Global.OneHandedThrustBonus"];
                mag_2h_thrust = magnitude * 1f * XmlConfig.dict["Global.TwoHandedThrustBonus"];
                mag_1h_sword_thrust = magnitude * 1.0f * XmlConfig.dict["Global.OneHandedThrustBonus"];
                mag_2h_sword_thrust = magnitude * 1f * XmlConfig.dict["Global.TwoHandedThrustBonus"];
            }
            else if (damageType == DamageTypes.Cut)
            {
                mag_1h_thrust = magnitude;
                mag_2h_thrust = magnitude;
                mag_1h_sword_thrust = magnitude * 1.0f;
                mag_2h_sword_thrust = magnitude * 1.00f;
            }
            else
            {
                mag_1h_thrust = magnitude;
                mag_2h_thrust = magnitude;
                mag_1h_sword_thrust = magnitude;
                mag_2h_sword_thrust = magnitude;
            }

            switch (weaponType)
            {
                case "Dagger":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_1h_sword_thrust, armorReduction, damageType, armorEffectiveness,
                                XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "ThrowingKnife":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_1h_sword_thrust, armorReduction, damageType, armorEffectiveness,
                                XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "OneHandedSword":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_1h_sword_thrust, armorReduction, damageType, armorEffectiveness,
                                XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "TwoHandedSword":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_2h_sword_thrust, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "OneHandedAxe":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], magnitude, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "OneHandedBastardAxe":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], magnitude, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "TwoHandedAxe":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], magnitude, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "OneHandedPolearm":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_1h_thrust, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "TwoHandedPolearm":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_2h_thrust, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "Mace":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_1h_thrust, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim, 0f);
                        break;
                    }
                case "TwoHandedMace":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_2h_thrust, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "Arrow":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], magnitude, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim, 0f);
                        break;
                    }
                case "Bolt":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], magnitude, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim, 0f);
                        break;
                    }
                case "Javelin":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_1h_thrust, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                case "ThrowingAxe":
                    {
                        damage = weaponTypeDamage(XmlConfig.dict[weaponType + ".ExtraBluntFactorCut"], XmlConfig.dict[weaponType + ".ExtraBluntFactorPierce"], mag_1h_thrust, armorReduction, damageType, armorEffectiveness,
                            XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorCut"], XmlConfig.dict[weaponType + ".ExtraArmorThresholdFactorPierce"], player, isPlayerVictim);
                        break;
                    }
                default:
                    {
                        //InformationManager.DisplayMessage(new InformationMessage("POZOR DEFAULT !!!!"));
                        damage = weaponTypeDamage(1f, 1f, magnitude, armorReduction, damageType, armorEffectiveness, 1f, 1f, player, isPlayerVictim);
                        break;
                    }
            }

            return damage * absorbedDamageRatio;
        }

        private static float weaponTypeDamage(float bluntFactorCut, float bluntFactorPierce, float magnitude, float armorReduction, DamageTypes damageType, float armorEffectiveness, float cutTreshold, float pierceTreshold, BasicCharacterObject player, bool isPlayerVictim, float partialPenetrationThreshold = 2f)
        {
            float damage = 0f;
            switch (damageType)
            {
                case DamageTypes.Blunt:
                    {
                        float armorReductionBlunt = 100f / (100f + armorEffectiveness * XmlConfig.dict["Global.ArmorMultiplier"]);
                        damage += magnitude * armorReductionBlunt * XmlConfig.dict["Global.MaceBluntModifier"];

                        break;
                    }
                case DamageTypes.Cut:
                    {
                        float penetratedDamage = Math.Max(0f, magnitude - armorEffectiveness * cutTreshold * XmlConfig.dict["Global.ArmorThresholdModifier"]);
                        float bluntFraction = 0f;
                        if (magnitude > 0f)
                        {
                            bluntFraction = (magnitude - penetratedDamage) / magnitude;
                        }
                        damage += penetratedDamage;

                        float bluntTrauma = magnitude * (bluntFactorCut + XmlConfig.dict["Global.BluntTraumaBonus"]) * bluntFraction;
                        float bluntTraumaAfterArmor = Math.Max(0f, bluntTrauma * armorReduction);
                        damage += bluntTraumaAfterArmor;

                        if (XmlConfig.dict["Global.ArmorPenetrationMessage"] >= 1f)
                        {
                            if (player != null)
                            {
                                if (isPlayerVictim)
                                {
                                    //InformationManager.DisplayMessage(new InformationMessage("You received"));
                                    InformationManager.DisplayMessage(new InformationMessage("You received " + (int)(bluntTraumaAfterArmor) +
                                        " blunt trauma, " + (int)(penetratedDamage) + " armor penetration damage"));
                                    //InformationManager.DisplayMessage(new InformationMessage("damage penetrated: " + penetratedDamage));
                                }
                                else
                                {
                                    InformationManager.DisplayMessage(new InformationMessage("You dealt " + (int)(bluntTraumaAfterArmor) +
                                        " blunt trauma, " + (int)(penetratedDamage) + " armor penetration damage"));
                                }
                            }
                        }
                        break;

                    }
                case DamageTypes.Pierce:
                    {
                        float partialPenetration = Math.Max(0f, magnitude - armorEffectiveness * partialPenetrationThreshold * XmlConfig.dict["Global.ArmorThresholdModifier"]);
                        if (partialPenetration > 15f)
                        {
                            partialPenetration = 15f;
                        }
                        float penetratedDamage = Math.Max(0f, magnitude - armorEffectiveness * pierceTreshold * XmlConfig.dict["Global.ArmorThresholdModifier"]) - partialPenetration;
                        float bluntFraction = 0f;
                        if (magnitude > 0f)
                        {
                            bluntFraction = (magnitude - (penetratedDamage + partialPenetration)) / magnitude;
                        }
                        damage += penetratedDamage + partialPenetration;

                        float bluntTrauma = magnitude * (bluntFactorPierce + XmlConfig.dict["Global.BluntTraumaBonus"]) * bluntFraction;
                        float bluntTraumaAfterArmor = Math.Max(0f, bluntTrauma * armorReduction);
                        damage += bluntTraumaAfterArmor;

                        if (XmlConfig.dict["Global.ArmorPenetrationMessage"] >= 1f)
                        {
                            if (player != null)
                            {
                                if (isPlayerVictim)
                                {
                                    //InformationManager.DisplayMessage(new InformationMessage("You received"));
                                    InformationManager.DisplayMessage(new InformationMessage("You received " + (int)(bluntTraumaAfterArmor) +
                                        " blunt trauma, " + (int)(penetratedDamage + partialPenetration) + " armor penetration damage"));
                                    //InformationManager.DisplayMessage(new InformationMessage("damage penetrated: " + penetratedDamage));
                                }
                                else
                                {
                                    InformationManager.DisplayMessage(new InformationMessage("You dealt " + (int)(bluntTraumaAfterArmor) +
                                        " blunt trauma, " + (int)(penetratedDamage + partialPenetration) + " armor penetration damage"));
                                }
                            }
                        }
                        break;
                    }
                default:
                    {
                        damage = 0f;
                        break;
                    }
            }
            return damage;
        }
    }

    [HarmonyPatch(typeof(DefaultItemValueModel))]
    [HarmonyPatch("CalculateValue")]
    class OverrideCalculateValue
    {
        static bool Prefix(ref DefaultItemValueModel __instance, ItemObject item, ref int __result)
        {
            float price = 0;
            float tier = 1f;

            if (item.ItemComponent != null)
            {
                tier = __instance.GetEquipmentValueFromTier(item.Tierf);
            }

            float materialPriceModifier = 1f;
            if(item.ArmorComponent != null)
            {
                switch (item.ArmorComponent.MaterialType)
                {
                    case ArmorComponent.ArmorMaterialTypes.Cloth:
                        {
                            materialPriceModifier = 50f;
                            break;
                        }
                    case ArmorComponent.ArmorMaterialTypes.Leather:
                        {
                            materialPriceModifier = 75f;
                            break;
                        }
                    case ArmorComponent.ArmorMaterialTypes.Chainmail:
                        {
                            materialPriceModifier = 100f;
                            break;
                        }
                    case ArmorComponent.ArmorMaterialTypes.Plate:
                        {
                            materialPriceModifier = 120f;
                            break;
                        }
                    default:
                        {
                            materialPriceModifier = 50f;
                            break;
                        }
                }
            }
            
            if (item.ItemType == ItemObject.ItemTypeEnum.LegArmor)
            {
                price = (int)((75f + (float)item.ArmorComponent.LegArmor * materialPriceModifier) * XmlConfig.dict["Global.ArmorPriceModifier"]);
            }
            else if (item.ItemType == ItemObject.ItemTypeEnum.HandArmor)
            {
                price = (int)((50f + (float)item.ArmorComponent.ArmArmor * materialPriceModifier * 0.8f) * XmlConfig.dict["Global.ArmorPriceModifier"]);
            }
            else if (item.ItemType == ItemObject.ItemTypeEnum.HeadArmor)
            {
                price = (int)((100f + (float)item.ArmorComponent.HeadArmor * materialPriceModifier * 1.2f) * XmlConfig.dict["Global.ArmorPriceModifier"]);
            }
            else if (item.ItemType == ItemObject.ItemTypeEnum.Cape)
            {
                price = (int)((50f + (float)item.ArmorComponent.BodyArmor * materialPriceModifier * 0.8f + (float)item.ArmorComponent.ArmArmor * materialPriceModifier * 0.8f) * XmlConfig.dict["Global.ArmorPriceModifier"]);
            }
            else if (item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
            {
                price = (int)((200f + (float)item.ArmorComponent.BodyArmor * materialPriceModifier * 2.5f + (float)item.ArmorComponent.LegArmor * materialPriceModifier + (float)item.ArmorComponent.ArmArmor * materialPriceModifier * 0.8f) * XmlConfig.dict["Global.ArmorPriceModifier"]);
            }
            else if (item.ItemType == ItemObject.ItemTypeEnum.HorseHarness)
            {
                price = (int)((100f + (((float)item.ArmorComponent.BodyArmor) * 0.2f + (float)item.ArmorComponent.ArmArmor * 0.2f + (float)item.ArmorComponent.LegArmor * 0.4f + (float)item.ArmorComponent.HeadArmor * 0.2f) * 450f) * XmlConfig.dict["Global.ArmorPriceModifier"]);
            }
            else if (item.ItemComponent is WeaponComponent)
            {
                price = (int)(200f * XmlConfig.dict["Global.WeaponPriceModifier"] * tier * (1f + 0.2f * (item.Appearance - 1f)) + 100f * Math.Max(0f, item.Appearance - 1f));
            }
            else if (item.ItemComponent is HorseComponent)
            {
                price = (int)(200f * tier * XmlConfig.dict["Global.HorsePriceModifier"] * (1f + 0.2f * (item.Appearance - 1f)) + 100f * Math.Max(0f, item.Appearance - 1f));
            }
            else if (item.ItemComponent is TradeItemComponent)
            {
                price = (int)(100f * tier * XmlConfig.dict["Global.TradePriceModifier"] * (1f + 0.2f * (item.Appearance - 1f)) + 100f * Math.Max(0f, item.Appearance - 1f));
            }
            else
            {
                price = 1;
            }

            __result = (int)price;
            return false;
        }
    }

    [HarmonyPatch(typeof(DefaultItemValueModel))]
    [HarmonyPatch("CalculateHorseTier")]
    class OverrideCalculateHorseTier
    {
        static bool Prefix(ref DefaultItemValueModel __instance, HorseComponent horseComponent, ref float __result)
        {
            float tier = 0f;
            if (horseComponent.IsPackAnimal)
            {
                tier = 1f;
            }
            else
            {
                tier += 0.009f * (float)horseComponent.HitPointBonus;
                tier += 0.030f * (float)horseComponent.Maneuver;
                tier += 0.030f * (float)horseComponent.Speed;
            }
            //tier += 1.5f * (float)horseComponent.ChargeDamage;
            //tier = (tier / 13f) - 8f;
            __result = tier;
            return false;
        }
    }

    [HarmonyPatch(typeof(DefaultItemValueModel))]
    [HarmonyPatch("CalculateTierMeleeWeapon")]
    class OverrideCalculateTierMeleeWeapon
    {
        private static float GetFactor(DamageTypes swingDamageType)
        {
            switch (swingDamageType)
            {
                default:
                    return 1f;
                case DamageTypes.Pierce:
                    return 1.15f;
                case DamageTypes.Blunt:
                    return 2.0f;
            }
        }

        static bool Prefix(ref DefaultItemValueModel __instance, WeaponComponent weaponComponent, ref float __result)
        {
            WeaponComponentData weaponComponentData = weaponComponent.Weapons[0];
            float val = ((float)weaponComponentData.ThrustDamage * XmlConfig.dict["Global.OneHandedThrustBonus"] - 75f) * 0.1f * GetFactor(weaponComponentData.ThrustDamageType) * ((float)weaponComponentData.ThrustSpeed * 0.01f);
            float num = ((float)weaponComponentData.SwingDamage) * 0.2f * GetFactor(weaponComponentData.SwingDamageType) * ((float)weaponComponentData.SwingSpeed * 0.01f);
            float maceTier = ((float)weaponComponentData.SwingDamage - 3f) * 0.23f * ((float)weaponComponentData.SwingSpeed * 0.01f);
            if (val < 0f)
            {
                val = 0f;
            }
            if (num < 0f)
            {
                num = 0f;
            }
            if (maceTier < 0f)
            {
                maceTier = 0f;
            }

            float num2 = 0f;
            switch (weaponComponentData.WeaponClass)
            {
                case WeaponClass.OneHandedSword:
                case WeaponClass.Dagger:
                case WeaponClass.ThrowingKnife:
                    {
                        num2 = (val + num) * 0.5f;
                        break;
                    }
                case WeaponClass.TwoHandedSword:
                    {
                        num2 = (val + num) * 0.5f / 1.3f;
                        break;
                    }
                case WeaponClass.TwoHandedPolearm:
                case WeaponClass.LowGripPolearm:
                    {
                        num2 = val + (num * 0.5f);
                        break;
                    }
                case WeaponClass.TwoHandedAxe:
                    {
                        num2 = num / 1.3f;
                        break;
                    }
                case WeaponClass.OneHandedAxe:
                case WeaponClass.Pick:
                    {
                        num2 = num * (float)weaponComponentData.WeaponLength * 0.014f;
                        break;
                    }
                case WeaponClass.TwoHandedMace:
                    {
                        num2 = maceTier * (float)weaponComponentData.WeaponLength * 0.014f / 1.3f;
                        break;
                    }
                case WeaponClass.Mace:
                    {
                        num2 = maceTier * (float)weaponComponentData.WeaponLength * 0.014f;
                        break;
                    }
                case WeaponClass.ThrowingAxe:
                    {
                        num2 = (float)weaponComponentData.SwingDamage * 0.05f;
                        break;
                    }
                case WeaponClass.Javelin:
                    {
                        num2 = ((float)weaponComponentData.ThrustDamage * XmlConfig.dict["Global.OneHandedThrustBonus"] - 60f) * 0.1f; //XmlConfig.dict["Global.ThrustModifier"];
                        break;
                    }
                case WeaponClass.OneHandedPolearm:
                    {
                        num2 = val + (num * 0.5f);
                        break;
                    }
                default:
                    {
                        num2 = (val + num) * 0.5f;
                        break;
                    }
            }
            if (num2 < 0f)
            {
                num2 = 0f;
            }
            if (num2 > 5.1f)
            {
                num2 = 5.1f;
            }
            __result = num2;
            return false;
        }
    }

    [HarmonyPatch(typeof(DefaultItemValueModel))]
    [HarmonyPatch("CalculateRangedWeaponTier")]
    class OverrideCalculateRangedWeaponTier
    {


        static bool Prefix(ref DefaultItemValueModel __instance, WeaponComponent weaponComponent, ref float __result)
        {
            WeaponComponentData weaponComponentData = weaponComponent.Weapons[0];
            //float num;
            float RangedTier;
            float DrawWeight = (float)weaponComponentData.MissileSpeed * 1f;
            switch (weaponComponent.Item?.ItemType ?? ItemObject.ItemTypeEnum.Invalid)
            {
                default:
                    RangedTier = (DrawWeight - 60f) * 0.049f;
                    break;
                case ItemObject.ItemTypeEnum.Crossbow:
                    RangedTier = (DrawWeight - 250f) * 0.021f;
                    break;
            }
            //num = RangedTier;
            __result = RangedTier;
            return false;
        }
    }

    [HarmonyPatch(typeof(DefaultItemValueModel))]
    [HarmonyPatch("CalculateAmmoTier")]
    class OverrideCalculateAmmoTier
    {


        static bool Prefix(ref DefaultItemValueModel __instance, WeaponComponent weaponComponent, ref float __result)
        {
            WeaponComponentData weaponComponentData = weaponComponent.Weapons[0];
            //float num;
            float ArrowTier;
            float ArrowWeight = (float)weaponComponentData.MissileSpeed * 1f;

            ArrowTier = (ArrowWeight - 40f) * 0.066f;
            //num = ArrowTier;
            __result = ArrowTier;
            return false;
        }
    }

    [HarmonyPatch(typeof(DefaultItemValueModel))]
    [HarmonyPatch("CalculateArmorTier")]
    class OverrideCalculateArmorTier
    {
        static bool Prefix(ref DefaultItemValueModel __instance, ArmorComponent armorComponent, ref float __result)
        {
            float ArmorTier = 0f;
            if (armorComponent.Item.ItemType == ItemObject.ItemTypeEnum.LegArmor)
            {
                ArmorTier = (float)armorComponent.LegArmor * 0.10f - 1f;
            }
            else if (armorComponent.Item.ItemType == ItemObject.ItemTypeEnum.HandArmor)
            {
                ArmorTier = (float)armorComponent.ArmArmor * 0.10f - 1f;
            }
            else if (armorComponent.Item.ItemType == ItemObject.ItemTypeEnum.HeadArmor)
            {
                ArmorTier = (float)armorComponent.HeadArmor * 0.06f - 1f;
            }
            else if (armorComponent.Item.ItemType == ItemObject.ItemTypeEnum.Cape)
            {
                ArmorTier = ((float)armorComponent.BodyArmor + (float)armorComponent.ArmArmor) * 0.15f - 1f;
            }
            else if (armorComponent.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
            {
                ArmorTier = ((float)armorComponent.BodyArmor * 0.05f) + ((float)armorComponent.LegArmor * 0.035f) + ((float)armorComponent.ArmArmor * 0.025f) - 1f;
            }
            else if (armorComponent.Item.ItemType == ItemObject.ItemTypeEnum.HorseHarness)
            {
                ArmorTier = ((float)armorComponent.BodyArmor * 0.02f + (float)armorComponent.LegArmor * 0.04f + (float)armorComponent.ArmArmor * 0.02f + (float)armorComponent.HeadArmor * 0.02f) - 1f;
            }
            if (ArmorTier < 0f)
            {
                ArmorTier = 0f;
            }
            __result = ArmorTier;
            return false;
        }
    }

    [HarmonyPatch(typeof(SandboxAgentStatCalculateModel))]
    [HarmonyPatch("UpdateHumanStats")]
    class SandboxAgentUpdateHumanStats
    {
        static void Postfix(Agent agent, ref AgentDrivenProperties agentDrivenProperties)
        {
            agentDrivenProperties.AttributeShieldMissileCollisionBodySizeAdder = 0.01f;
        }
    }

    [HarmonyPatch(typeof(SandboxAgentStatCalculateModel))]
    [HarmonyPatch("UpdateHorseStats")]
    class ChangeHorseChargeBonus
    {
        static void Postfix(Agent agent, ref AgentDrivenProperties agentDrivenProperties)
        {
            float weightOfHorseAndRaider = 0f;
            if (agent.RiderAgent != null)
            {
                MissionEquipment equipment = agent.RiderAgent.Equipment;
                weightOfHorseAndRaider += (float)agent.RiderAgent.Monster.Weight;
                weightOfHorseAndRaider += agent.RiderAgent.SpawnEquipment.GetTotalWeightOfArmor(forHuman: true);
                weightOfHorseAndRaider += equipment.GetTotalWeightOfWeapons();
                weightOfHorseAndRaider += (float)agent.Monster.Weight;
                weightOfHorseAndRaider += agent.SpawnEquipment.GetTotalWeightOfArmor(forHuman: false);
                weightOfHorseAndRaider += 100f;
            }
            else
            {
                weightOfHorseAndRaider += (float)agent.Monster.Weight;
                weightOfHorseAndRaider += agent.SpawnEquipment.GetTotalWeightOfArmor(forHuman: false);
                weightOfHorseAndRaider += 100f;
            }
            agentDrivenProperties.MountChargeDamage = weightOfHorseAndRaider;
        }
    }

    [HarmonyPatch(typeof(Mission))]
    [HarmonyPatch("ComputeBlowMagnitudeFromHorseCharge")]
    class ChangeHorseDamageCalculation
    {
        static bool Prefix(ref AttackCollisionData acd, Vec2 attackerAgentMovementDirection, Vec2 attackerAgentVelocity, float agentMountChargeDamageProperty, Vec2 victimAgentVelocity, Vec3 victimAgentPosition, out float baseMagnitude, out float specialMagnitude)
        {
            Vec2 vec = attackerAgentMovementDirection * Vec2.DotProduct(victimAgentVelocity, attackerAgentMovementDirection);
            Vec2 vec2 = attackerAgentVelocity - vec;
            float num = ChargeDamageDotProduct(victimAgentPosition, attackerAgentMovementDirection, acd.CollisionGlobalPosition);
            float num2 = vec2.Length * num;
            baseMagnitude = (num2 * num2 * num * agentMountChargeDamageProperty) / 2500f;
            specialMagnitude = baseMagnitude;

            return false;
        }

        private static float ChargeDamageDotProduct(Vec3 victimPosition, Vec2 chargerMovementDirection, Vec3 collisionPoint)
        {
            return Vec2.DotProduct((victimPosition.AsVec2 - collisionPoint.AsVec2).Normalized(), chargerMovementDirection);
        }
    }

    [HarmonyPatch(typeof(CustomBattleAgentStatCalculateModel))]
    [HarmonyPatch("UpdateAgentStats")]
    class CustomBattleUpdateAgentStats
    {
        static void Postfix(Agent agent, AgentDrivenProperties agentDrivenProperties)
        {
            agentDrivenProperties.AttributeShieldMissileCollisionBodySizeAdder = 0.01f;

            if (!agent.IsHuman)
            {
                float weightOfHorseAndRaider = 0f;

                if (agent.RiderAgent != null)
                {
                    MissionEquipment equipment = agent.RiderAgent.Equipment;
                    weightOfHorseAndRaider += (float)agent.RiderAgent.Monster.Weight;
                    weightOfHorseAndRaider += agent.RiderAgent.SpawnEquipment.GetTotalWeightOfArmor(forHuman: true);
                    weightOfHorseAndRaider += equipment.GetTotalWeightOfWeapons();
                    weightOfHorseAndRaider += (float)agent.Monster.Weight;
                    weightOfHorseAndRaider += agent.SpawnEquipment.GetTotalWeightOfArmor(forHuman: false);
                }
                else
                {
                    weightOfHorseAndRaider += (float)agent.Monster.Weight;
                    weightOfHorseAndRaider += agent.SpawnEquipment.GetTotalWeightOfArmor(forHuman: false);
                }
                agentDrivenProperties.MountChargeDamage = weightOfHorseAndRaider;
            }
        }

    }

    //[HarmonyPatch(typeof(SandboxAgentApplyDamageModel))]
    //[HarmonyPatch("DecideCrushedThrough")]
    //class OverrideDecideCrushedThrough
    //{
    //    static bool Prefix(Agent attackerAgent, Agent defenderAgent, float totalAttackEnergy, Agent.UsageDirection attackDirection, StrikeType strikeType, WeaponComponentData defendItem, bool isPassiveUsage, ref bool __result)
    //    {
    //        EquipmentIndex wieldedItemIndex = attackerAgent.GetWieldedItemIndex(Agent.HandIndex.OffHand);
    //        if (wieldedItemIndex == EquipmentIndex.None)
    //        {
    //            wieldedItemIndex = attackerAgent.GetWieldedItemIndex(Agent.HandIndex.MainHand);
    //        }
    //        WeaponComponentData weaponComponentData = (wieldedItemIndex != EquipmentIndex.None) ? attackerAgent.Equipment[wieldedItemIndex].CurrentUsageItem : null;
    //        float num = 47f;

    //        EquipmentIndex wieldedItemIndex4 = defenderAgent.GetWieldedItemIndex(Agent.HandIndex.OffHand);
    //        WeaponComponentData secondaryItem = (wieldedItemIndex4 != EquipmentIndex.None) ? defenderAgent.Equipment[wieldedItemIndex4].CurrentUsageItem : null;
    //        int meleeSkill = Utilities.GetMeleeSkill(defenderAgent, weaponComponentData, secondaryItem);
    //        float meleeLevel = Utilities.CalculateAILevel(defenderAgent, meleeSkill);

    //        if (defendItem != null && defendItem.IsShield)
    //        {
    //            num *= (defendItem.WeaponLength / 100f) * meleeLevel * 3f;
    //        }
    //        else
    //        {
    //            num *= (weaponComponentData.WeaponLength / 100f) * meleeLevel * 2f;
    //        }
    //        if (weaponComponentData != null && weaponComponentData.WeaponClass == WeaponClass.TwoHandedMace)
    //        {
    //            num *= 0.8f;
    //        }
    //        if (defendItem != null && defendItem.IsShield)
    //        {
    //            num *= 1.2f;
    //        }
    //        if (totalAttackEnergy > num && (isPassiveUsage || attackDirection == Agent.UsageDirection.AttackUp || attackDirection == Agent.UsageDirection.AttackLeft || attackDirection == Agent.UsageDirection.AttackRight))
    //        {
    //            __result = true;
    //            return false;
    //        }
    //        __result =  false;
    //        return false;
    //    }

    //}

    

    [HarmonyPatch(typeof(Mission))]
    [HarmonyPatch("CreateMeleeBlow")]
    class CreateMeleeBlowPatch
    {
        static void Postfix(ref Mission __instance, ref Blow __result, Agent attackerAgent, Agent victimAgent, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, CrushThroughState crushThroughState, Vec3 blowDirection, Vec3 swingDirection, bool cancelDamage)
        {
            string weaponType = "otherDamage";
            if (attackerWeapon.Item != null && attackerWeapon.Item.PrimaryWeapon != null)
            {
                weaponType = attackerWeapon.Item.PrimaryWeapon.WeaponClass.ToString();
            }

            if ((attackerAgent.IsDoingPassiveAttack && collisionData.CollisionResult == CombatCollisionResult.StrikeAgent))
            {
                if (attackerAgent.Team != victimAgent.Team)
                {
                    __result.BlowFlag |= BlowFlags.KnockDown;
                    return;
                }
            }

            if (victimAgent!= null && victimAgent.Character != null && victimAgent.Character.IsPlayerCharacter)
            {
                if (!collisionData.AttackBlockedWithShield && (collisionData.CollisionResult == CombatCollisionResult.Blocked || collisionData.CollisionResult == CombatCollisionResult.Parried || collisionData.CollisionResult == CombatCollisionResult.ChamberBlocked))
                {
                    CharacterObject affectedCharacter = (CharacterObject)victimAgent.Character;
                    Hero heroObject = affectedCharacter.HeroObject;

                    CharacterObject affectorCharacter = (CharacterObject)attackerAgent.Character;

                    float experience = 1f;
                    Campaign.Current.Models.CombatXpModel.GetXpFromHit(heroObject.CharacterObject, null, affectorCharacter, heroObject.PartyBelongedTo?.Party, (int)collisionData.BaseMagnitude, false, CombatXpModel.MissionTypeEnum.Battle, out var xpAmount);
                    experience = xpAmount * 2f;
                    WeaponComponentData parryWeapon = victimAgent.WieldedWeapon.CurrentUsageItem;
                    if (parryWeapon != null)
                    {
                        SkillObject skillForWeapon = Campaign.Current.Models.CombatXpModel.GetSkillForWeapon(parryWeapon);
                        float num2 = ((skillForWeapon == DefaultSkills.Bow) ? 0.5f : 1f);
                        affectedCharacter.HeroObject.AddSkillXp(skillForWeapon, experience);
                    }
                    else
                    {
                        heroObject.AddSkillXp(DefaultSkills.Athletics, MBRandom.RoundRandomized(experience));
                    }
                    if (victimAgent.HasMount)
                    {
                        float num3 = 0.1f;
                        float speedBonusFromMovement = collisionData.MovementSpeedDamageModifier;
                        if (speedBonusFromMovement > 0f)
                        {
                            num3 *= 1f + speedBonusFromMovement;
                        }
                        if (num3 > 0f)
                        {
                            heroObject.AddSkillXp(DefaultSkills.Riding, MBRandom.RoundRandomized(num3 * experience));
                        }
                    }
                    else
                    {
                        float num5 = 0.2f;
                        float speedBonusFromMovement = collisionData.MovementSpeedDamageModifier;
                        if (speedBonusFromMovement > 0f)
                        {
                            num5 += 1.5f * speedBonusFromMovement;
                        }
                        if (num5 > 0f)
                        {
                            heroObject.AddSkillXp(DefaultSkills.Athletics, num5 * experience);
                        }
                    }
                }
            }

            if ((collisionData.CollisionResult == CombatCollisionResult.StrikeAgent) && (collisionData.DamageType == (int)DamageTypes.Pierce))
            {
                if (attackerAgent.Team != victimAgent.Team)
                {
                    switch (weaponType)
                    {
                        case "TwoHandedPolearm":
                            if (attackerAgent.Team != victimAgent.Team)
                            {

                                //AttackCollisionData newdata = AttackCollisionData.GetAttackCollisionDataForDebugPurpose(false, false, false, true, false, false, false, false, false, true, false, collisionData.CollisionResult, collisionData.AffectorWeaponSlotOrMissileIndex,
                                //    collisionData.StrikeType, collisionData.StrikeType, collisionData.CollisionBoneIndex, BoneBodyPartType.Chest, collisionData.AttackBoneIndex, collisionData.AttackDirection, collisionData.PhysicsMaterialIndex,
                                //    collisionData.CollisionHitResultFlags, collisionData.AttackProgress, collisionData.CollisionDistanceOnWeapon, collisionData.AttackerStunPeriod, collisionData.DefenderStunPeriod, collisionData.MissileTotalDamage,
                                //    0, collisionData.ChargeVelocity, collisionData.FallSpeed, collisionData.WeaponRotUp, collisionData.WeaponBlowDir, collisionData.CollisionGlobalPosition, collisionData.MissileVelocity,
                                //    collisionData.MissileStartingPosition, collisionData.VictimAgentCurVelocity, collisionData.CollisionGlobalNormal);

                                //collisionData = newdata;

                                __result.BlowFlag |= BlowFlags.KnockBack;
                            }
                            break;
                    }
                }
            }

            if ((attackerAgent.IsDoingPassiveAttack && collisionData.CollisionResult == CombatCollisionResult.Blocked))
            {
                if (attackerAgent.Team != victimAgent.Team)
                {

                    //AttackCollisionData newdata = AttackCollisionData.GetAttackCollisionDataForDebugPurpose(false, false, false, true, false, false, false, false, false, true, false, collisionData.CollisionResult, collisionData.AffectorWeaponSlotOrMissileIndex,
                    //    collisionData.StrikeType, collisionData.StrikeType, collisionData.CollisionBoneIndex, BoneBodyPartType.Chest, collisionData.AttackBoneIndex, collisionData.AttackDirection, collisionData.PhysicsMaterialIndex,
                    //    collisionData.CollisionHitResultFlags, collisionData.AttackProgress, collisionData.CollisionDistanceOnWeapon, collisionData.AttackerStunPeriod, collisionData.DefenderStunPeriod, collisionData.MissileTotalDamage,
                    //    0, collisionData.ChargeVelocity, collisionData.FallSpeed, collisionData.WeaponRotUp, collisionData.WeaponBlowDir, collisionData.CollisionGlobalPosition, collisionData.MissileVelocity,
                    //    collisionData.MissileStartingPosition, collisionData.VictimAgentCurVelocity, collisionData.CollisionGlobalNormal);

                    //collisionData = newdata;

                    sbyte weaponAttachBoneIndex = (sbyte)(attackerWeapon.IsEmpty ? (-1) : attackerAgent.Monster.GetBoneToAttachForItemFlags(attackerWeapon.Item.ItemFlags));
                    __result.WeaponRecord.FillAsMeleeBlow(attackerWeapon.Item, attackerWeapon.CurrentUsageItem, collisionData.AffectorWeaponSlotOrMissileIndex, weaponAttachBoneIndex);
                    __result.StrikeType = (StrikeType)collisionData.StrikeType;
                    __result.DamageType = ((!attackerWeapon.IsEmpty && true && !collisionData.IsAlternativeAttack) ? ((DamageTypes)collisionData.DamageType) : DamageTypes.Blunt);
                    __result.NoIgnore = collisionData.IsAlternativeAttack;
                    __result.AttackerStunPeriod = collisionData.AttackerStunPeriod;
                    __result.DefenderStunPeriod = collisionData.DefenderStunPeriod;
                    __result.BlowFlag = BlowFlags.None;
                    __result.Position = collisionData.CollisionGlobalPosition;
                    __result.BoneIndex = collisionData.CollisionBoneIndex;
                    __result.Direction = blowDirection;
                    __result.SwingDirection = swingDirection;
                    //__result.InflictedDamage = 1;
                    __result.VictimBodyPart = collisionData.VictimHitBodyPart;
                    __result.BlowFlag |= BlowFlags.KnockBack;
                    victimAgent.RegisterBlow(__result);
                    foreach (MissionBehavior missionBehaviour in __instance.MissionBehaviors)
                    {
                        missionBehaviour.OnRegisterBlow(attackerAgent, victimAgent, null, __result, ref collisionData, in attackerWeapon);
                    }
                    return;
                }
            }

            if ((collisionData.CollisionResult == CombatCollisionResult.Parried && !collisionData.AttackBlockedWithShield) || (collisionData.AttackBlockedWithShield && !collisionData.CorrectSideShieldBlock))
            {
                switch (weaponType)
                {
                    case "TwoHandedAxe":
                    case "OneHandedAxe":
                    case "OneHandedBastardAxe":
                    case "TwoHandedPolearm":
                    case "TwoHandedMace":
                        {
                            if (attackerAgent.Team != victimAgent.Team)
                            {
                                sbyte weaponAttachBoneIndex = (sbyte)(attackerWeapon.IsEmpty ? (-1) : attackerAgent.Monster.GetBoneToAttachForItemFlags(attackerWeapon.Item.ItemFlags));
                                __result.WeaponRecord.FillAsMeleeBlow(attackerWeapon.Item, attackerWeapon.CurrentUsageItem, collisionData.AffectorWeaponSlotOrMissileIndex, weaponAttachBoneIndex);
                                __result.StrikeType = (StrikeType)collisionData.StrikeType;
                                __result.DamageType = ((!attackerWeapon.IsEmpty && true && !collisionData.IsAlternativeAttack) ? ((DamageTypes)collisionData.DamageType) : DamageTypes.Blunt);
                                __result.NoIgnore = collisionData.IsAlternativeAttack;
                                __result.AttackerStunPeriod = collisionData.AttackerStunPeriod;
                                __result.DefenderStunPeriod = collisionData.DefenderStunPeriod;
                                __result.BlowFlag = BlowFlags.None;
                                __result.Position = collisionData.CollisionGlobalPosition;
                                __result.BoneIndex = collisionData.CollisionBoneIndex;
                                __result.Direction = blowDirection;
                                __result.SwingDirection = swingDirection;
                                //__result.InflictedDamage = 1;
                                __result.VictimBodyPart = collisionData.VictimHitBodyPart;
                                __result.BlowFlag |= BlowFlags.NonTipThrust;
                                victimAgent.RegisterBlow(__result);
                                foreach (MissionBehavior missionBehaviour in __instance.MissionBehaviors)
                                {
                                    missionBehaviour.OnRegisterBlow(attackerAgent, victimAgent, null, __result, ref collisionData, in attackerWeapon);
                                }
                            }
                            break;
                        }
                }
            }

            if (collisionData.CollisionResult != CombatCollisionResult.HitWorld && collisionData.CollisionResult != CombatCollisionResult.None && victimAgent != null && attackerAgent.Team == victimAgent.Team && (__result.BlowFlag.HasAnyFlag(BlowFlags.KnockBack) || __result.BlowFlag.HasAnyFlag(BlowFlags.KnockDown)))
            {
                __result.BlowFlag = BlowFlags.NonTipThrust;
                return;
            }

        }
    }

    [HarmonyPatch(typeof(Mission))]
    [HarmonyPatch("RegisterBlow")]
    class RegisterBlowPatch
    {
        static bool Prefix(ref Mission __instance, Agent attacker, Agent victim, GameEntity realHitEntity, Blow b, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, ref CombatLogData combatLogData)
        {
            if (!collisionData.AttackBlockedWithShield && !collisionData.CollidedWithShieldOnBack)
            {
                return true;
            }
            foreach (MissionBehavior missionBehaviour in __instance.MissionBehaviors)
            {
                missionBehaviour.OnRegisterBlow(attacker, victim, realHitEntity, b, ref collisionData, in attackerWeapon);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(BattleAgentLogic))]
    [HarmonyPatch("OnAgentHit")]
    class OnAgentHitPatch
    {
        static bool Prefix(Agent affectedAgent, Agent affectorAgent, int damage, in MissionWeapon attackerWeapon)
        {
            if (affectedAgent.Character != null && affectorAgent != null && affectorAgent.Character != null && affectedAgent.State == AgentState.Active)
            {
                bool isFatal = affectedAgent.Health - (float)damage < 1f;
                bool isTeamKill;
                if (affectedAgent.Team != null )
                {
                    isTeamKill = affectedAgent.Team.Side == affectorAgent.Team.Side;
                }
                else
                {
                    isTeamKill = true;
                }
                affectorAgent.Origin.OnScoreHit(affectedAgent.Character, affectorAgent.Formation?.Captain?.Character, damage, isFatal, isTeamKill, attackerWeapon.CurrentUsageItem);
                if (Mission.Current.Mode == MissionMode.Battle)
                {
                    _ = affectedAgent.Team;
                }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Agent))]
    [HarmonyPatch("HandleBlow")]
    class HandleBlowPatch
    {

        private static ArmorComponent.ArmorMaterialTypes GetProtectorArmorMaterialOfBone(Agent agent,sbyte boneIndex)
        {
            if(agent != null && agent.SpawnEquipment != null)
            {
                if (boneIndex >= 0)
                {
                    EquipmentIndex equipmentIndex = EquipmentIndex.None;
                    switch (agent.AgentVisuals.GetBoneTypeData(boneIndex).BodyPartType)
                    {
                        case BoneBodyPartType.Chest:
                        case BoneBodyPartType.Abdomen:
                        case BoneBodyPartType.ShoulderLeft:
                        case BoneBodyPartType.ShoulderRight:
                            equipmentIndex = EquipmentIndex.Body;
                            break;
                        case BoneBodyPartType.ArmLeft:
                        case BoneBodyPartType.ArmRight:
                            equipmentIndex = EquipmentIndex.Gloves;
                            break;
                        case BoneBodyPartType.Legs:
                            equipmentIndex = EquipmentIndex.Leg;
                            break;
                        case BoneBodyPartType.Head:
                        case BoneBodyPartType.Neck:
                            equipmentIndex = EquipmentIndex.NumAllWeaponSlots;
                            break;
                    }
                    if (equipmentIndex != EquipmentIndex.None && agent.SpawnEquipment[equipmentIndex].Item != null)
                    {
                        if(agent.SpawnEquipment[equipmentIndex].Item.ArmorComponent != null)
                        {
                            return agent.SpawnEquipment[equipmentIndex].Item.ArmorComponent.MaterialType;
                        }
                    }
                }
            }
	        return ArmorComponent.ArmorMaterialTypes.None;
        }
        static bool Prefix(ref Agent __instance, ref Blow b, AgentLastHitInfo ____lastHitInfo)
        {
            b.BaseMagnitude = Math.Min(b.BaseMagnitude, 1000f)/8f;
            Agent agent = (b.OwnerId != -1) ? __instance.Mission.FindAgentWithIndex(b.OwnerId) : __instance;
            if (!b.BlowFlag.HasAnyFlag(BlowFlags.NoSound))
            {
                bool isCriticalBlow = b.IsBlowCrit(__instance.Monster.HitPoints * 4);
                bool isLowBlow = b.IsBlowLow(__instance.Monster.HitPoints);
                bool isOwnerHumanoid = agent?.IsHuman ?? false;
                bool isNonTipThrust = b.BlowFlag.HasAnyFlag(BlowFlags.NonTipThrust);
                int hitSound = b.WeaponRecord.GetHitSound(isOwnerHumanoid, isCriticalBlow, isLowBlow, isNonTipThrust, b.AttackType, b.DamageType);
                float soundParameterForArmorType = 0.1f*(float)GetProtectorArmorMaterialOfBone(__instance, b.BoneIndex);
                SoundEventParameter parameter = new SoundEventParameter("Armor Type", soundParameterForArmorType);
                __instance.Mission.MakeSound(hitSound, b.Position, soundCanBePredicted: false, isReliable: true, b.OwnerId, __instance.Index, ref parameter);
                if (b.IsMissile && agent != null)
                {
                    int soundCodeMissionCombatPlayerhit = CombatSoundContainer.SoundCodeMissionCombatPlayerhit;
                    __instance.Mission.MakeSoundOnlyOnRelatedPeer(soundCodeMissionCombatPlayerhit, b.Position, agent.Index);
                }
                __instance.Mission.AddSoundAlarmFactorToAgents(b.OwnerId, b.Position, 15f);
            }
            b.DamagedPercentage = (float)b.InflictedDamage / __instance.HealthLimit;

            MethodInfo method = typeof(Agent).GetMethod("UpdateLastAttackAndHitTimes", BindingFlags.NonPublic | BindingFlags.Instance);
            method.DeclaringType.GetMethod("UpdateLastAttackAndHitTimes");
            method.Invoke(__instance, new object[] { agent, b.IsMissile });

            bool isKnockBack = ((b.BlowFlag & BlowFlags.NonTipThrust) != 0) || ((b.BlowFlag & BlowFlags.KnockDown) != 0) || ((b.BlowFlag & BlowFlags.KnockBack) != 0);
            if(b.AttackType == AgentAttackType.Bash || b.AttackType == AgentAttackType.Kick)
            {
                if(b.InflictedDamage <= 0)
                {
                    b.InflictedDamage = 1;
                }
            }
            if (b.InflictedDamage == 0 && isKnockBack)
            {
                b.InflictedDamage = 1;
            }
            if( b.InflictedDamage == 1 && isKnockBack)
            {

            }
            else
            {
                float health = __instance.Health;
                float damagedHp = (((float)b.InflictedDamage > health) ? health : ((float)b.InflictedDamage));
                float newHp = health - damagedHp;
                //float num = __instance.Health - (float)b.InflictedDamage;
                if (newHp < 0f)
                {
                    newHp = 0f;
                }
                if (!__instance.Invulnerable && !Mission.DisableDying)
                {
                    __instance.Health = newHp;
                }
            }

            int affectorWeaponSlotOrMissileIndex = b.WeaponRecord.AffectorWeaponSlotOrMissileIndex;
            float hitDistance = b.IsMissile ? (b.Position - b.WeaponRecord.StartingPosition).Length : 0f;
            if (agent != null && agent != __instance && __instance.IsHuman)
            {
                if (agent.IsMount && agent.RiderAgent != null)
                {
                    ____lastHitInfo.RegisterLastBlow(agent.RiderAgent.Index, b.AttackType);
                }
                else if (agent.IsHuman)
                {
                    ____lastHitInfo.RegisterLastBlow(b.OwnerId, b.AttackType);
                }
            }

            MethodInfo method3 = typeof(Mission).GetMethod("OnAgentHit", BindingFlags.NonPublic | BindingFlags.Instance);
            method3.DeclaringType.GetMethod("OnAgentHit");
            method3.Invoke(__instance.Mission, new object[] { __instance, agent, affectorWeaponSlotOrMissileIndex, b.IsMissile, false,
                b.InflictedDamage, __instance.Health, b.MovementSpeedDamageModifier, hitDistance, b.AttackType, b.VictimBodyPart });

            if (__instance.Health < 1f)
            {
                KillInfo overrideKillInfo = (b.IsFallDamage ? KillInfo.Gravity : KillInfo.Invalid);
                __instance.Die(b, overrideKillInfo);
            }

            MethodInfo method2 = typeof(Agent).GetMethod("HandleBlowAux", BindingFlags.NonPublic | BindingFlags.Instance);
            method2.DeclaringType.GetMethod("HandleBlowAux");
            method2.Invoke(__instance, new object[] { b });

            return false;
        }
    }

    [HarmonyPatch(typeof(Mission))]
    [HarmonyPatch("DecideAgentHitParticles")]
    class DecideAgentHitParticlesPatch
    {
        [EngineStruct("Hit_particle_result_data")]
        internal struct HitParticleResultData
        {
            public int StartHitParticleIndex;

            public int ContinueHitParticleIndex;

            public int EndHitParticleIndex;

            public void Reset()
            {
                StartHitParticleIndex = -1;
                ContinueHitParticleIndex = -1;
                EndHitParticleIndex = -1;
            }
        }
        static void Postfix(Blow blow, Agent victim, ref AttackCollisionData collisionData, ref HitParticleResultData hprd)
        {
            if (victim != null && (blow.InflictedDamage > 0 || victim.Health <= 0f))
            {
                if (!blow.WeaponRecord.HasWeapon() || blow.WeaponRecord.WeaponFlags.HasAnyFlag(WeaponFlags.NoBlood) || collisionData.IsAlternativeAttack || blow.InflictedDamage <= 20)
                {
                    hprd.StartHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_sweat_sword_enter");
                    hprd.ContinueHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_sweat_sword_enter");
                    hprd.EndHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_sweat_sword_enter");
                }
                else
                {
                    hprd.StartHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_blood_sword_enter");
                    hprd.ContinueHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_blood_sword_inside");
                    hprd.EndHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_blood_sword_exit");
                }
            }
        }
    }

    //[HarmonyPatch(typeof(ItemObject))]
    //[HarmonyPatch("CalculateEffectiveness")]
    //class CalculateEffectivenessPatch
    //{
    //    static bool Prefix(ref ItemObject __instance, ref float __result)
    //    {
    //        float result = 1f;
    //        ArmorComponent armorComponent = __instance.ArmorComponent;
    //        if (armorComponent != null)
    //        {
    //            result = ((__instance.Type != ItemTypeEnum.HorseHarness) ? (((float)armorComponent.HeadArmor * 34f + (float)armorComponent.BodyArmor * 42f + (float)armorComponent.LegArmor * 12f + (float)armorComponent.ArmArmor * 12f) * 0.03f) : ((float)armorComponent.BodyArmor * 1.67f));
    //        }
    //        if (__instance.WeaponComponent != null)
    //        {
    //            WeaponComponentData primaryWeapon = __instance.WeaponComponent.PrimaryWeapon;
    //            float num = 1f;
    //            switch (primaryWeapon.WeaponClass)
    //            {
    //                case WeaponClass.Dagger:
    //                    num = 0.4f;
    //                    break;
    //                case WeaponClass.OneHandedSword:
    //                    num = 0.55f;
    //                    break;
    //                case WeaponClass.TwoHandedSword:
    //                    num = 0.6f;
    //                    break;
    //                case WeaponClass.OneHandedAxe:
    //                    num = 0.5f;
    //                    break;
    //                case WeaponClass.TwoHandedAxe:
    //                    num = 0.55f;
    //                    break;
    //                case WeaponClass.Mace:
    //                    num = 0.5f;
    //                    break;
    //                case WeaponClass.Pick:
    //                    num = 0.4f;
    //                    break;
    //                case WeaponClass.TwoHandedMace:
    //                    num = 0.55f;
    //                    break;
    //                case WeaponClass.OneHandedPolearm:
    //                    num = 0.4f;
    //                    break;
    //                case WeaponClass.TwoHandedPolearm:
    //                    num = 0.4f;
    //                    break;
    //                case WeaponClass.LowGripPolearm:
    //                    num = 0.4f;
    //                    break;
    //                case WeaponClass.Arrow:
    //                    num = 3f;
    //                    break;
    //                case WeaponClass.Bolt:
    //                    num = 3f;
    //                    break;
    //                case WeaponClass.Cartridge:
    //                    num = 3f;
    //                    break;
    //                case WeaponClass.Bow:
    //                    num = 0.55f;
    //                    break;
    //                case WeaponClass.Crossbow:
    //                    num = 0.57f;
    //                    break;
    //                case WeaponClass.Stone:
    //                    num = 0.1f;
    //                    break;
    //                case WeaponClass.Boulder:
    //                    num = 0.1f;
    //                    break;
    //                case WeaponClass.ThrowingAxe:
    //                    num = 0.25f;
    //                    break;
    //                case WeaponClass.ThrowingKnife:
    //                    num = 0.2f;
    //                    break;
    //                case WeaponClass.Javelin:
    //                    num = 0.28f;
    //                    break;
    //                case WeaponClass.Pistol:
    //                    num = 1f;
    //                    break;
    //                case WeaponClass.Musket:
    //                    num = 1f;
    //                    break;
    //                case WeaponClass.SmallShield:
    //                    num = 0.4f;
    //                    break;
    //                case WeaponClass.LargeShield:
    //                    num = 0.5f;
    //                    break;
    //            }
    //            if (primaryWeapon.IsRangedWeapon)
    //            {
    //                result = ((!primaryWeapon.IsConsumable) ? (((float)(primaryWeapon.MissileSpeed * primaryWeapon.MissileDamage) * 1.75f + (float)(primaryWeapon.ThrustSpeed * primaryWeapon.Accuracy) * 0.3f) * 0.01f * (float)primaryWeapon.MaxDataValue * num) : (((float)(primaryWeapon.MissileDamage * primaryWeapon.MissileSpeed) * 1.775f + (float)(primaryWeapon.Accuracy * primaryWeapon.MaxDataValue) * 25f + (float)primaryWeapon.WeaponLength * 4f) * 0.006944f * (float)primaryWeapon.MaxDataValue * num));
    //            }
    //            else if (primaryWeapon.IsMeleeWeapon)
    //            {
    //                float val = (float)(primaryWeapon.ThrustSpeed * primaryWeapon.ThrustDamage) * 0.01f;
    //                float val2 = (float)(primaryWeapon.SwingSpeed * primaryWeapon.SwingDamage) * 0.01f;
    //                float num2 = Math.Max(val2, val);
    //                float num3 = Math.Min(val2, val);
    //                result = ((num2 + num3 * num3 / num2) * 120f + (float)primaryWeapon.Handling * 15f + (float)primaryWeapon.WeaponLength * 20f + __instance.Weight * 5f) * 0.01f * num;
    //            }
    //            else if (primaryWeapon.IsConsumable)
    //            {
    //                result = ((float)primaryWeapon.MissileDamage * 550f + (float)primaryWeapon.MissileSpeed * 15f + (float)primaryWeapon.MaxDataValue * 60f) * 0.01f * num;
    //            }
    //            else if (primaryWeapon.IsShield)
    //            {
    //                result = ((float)primaryWeapon.BodyArmor * 60f + (float)primaryWeapon.ThrustSpeed * 10f + (float)primaryWeapon.MaxDataValue * 40f + (float)primaryWeapon.WeaponLength * 20f) * 0.01f * num;
    //            }
    //        }
    //        HorseComponent horseComponent = __instance.HorseComponent;
    //        if (horseComponent != null)
    //        {
    //            result = ((float)(horseComponent.ChargeDamage * horseComponent.Speed + horseComponent.Maneuver * horseComponent.Speed) + (float)horseComponent.BodyLength * __instance.Weight * 0.025f) * (float)(horseComponent.HitPoints + horseComponent.HitPointBonus) * 0.0001f;
    //        }
    //        __result =  result;
    //        return false;
    //    }
    //}

    [HarmonyPatch(typeof(Mission))]
    class DecideAgentDismountedByBlowPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("DecideAgentDismountedByBlow")]
        static bool PrefixDecideAgentDismountedByBlow(Agent attackerAgent, Agent victimAgent, in AttackCollisionData collisionData, WeaponComponentData attackerWeapon, bool isInitialBlowShrugOff, ref Blow blow)
        {
            if (!blow.IsMissile)
            {
                if (victimAgent!= null && victimAgent.HasMount && victimAgent.Character != null && victimAgent.Origin != null)
                {
                    int ridingSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(victimAgent.Character, victimAgent.Origin, victimAgent.Formation, DefaultSkills.Riding);
                    if (attackerWeapon != null && attackerWeapon.ItemUsage != null && blow.StrikeType == StrikeType.Thrust && blow.BaseMagnitude > (2.4f + (ridingSkill * 0.01f)) &&
                    (blow.VictimBodyPart == BoneBodyPartType.Head || blow.VictimBodyPart == BoneBodyPartType.Neck) && 
                    (attackerWeapon.ItemUsage.Equals("polearm_couch") || attackerWeapon.ItemUsage.Equals("polearm_bracing")))
                    {
                        blow.BlowFlag |= BlowFlags.CanDismount;
                        return false;
                    }
                    else if (attackerWeapon != null && attackerWeapon.ItemUsage != null && blow.StrikeType == StrikeType.Thrust && blow.BaseMagnitude > (3f + (ridingSkill * 0.01f)) &&
                    (blow.VictimBodyPart == BoneBodyPartType.Chest || blow.VictimBodyPart == BoneBodyPartType.ShoulderLeft || blow.VictimBodyPart == BoneBodyPartType.ShoulderRight) &&
                    (attackerWeapon.ItemUsage.Equals("polearm_couch") || attackerWeapon.ItemUsage.Equals("polearm_bracing")))
                    {
                        blow.BlowFlag |= BlowFlags.CanDismount;
                        return false;
                    }
                    else
                    {
                        return true;
                    }
                }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(DefaultPartyHealingModel))]
    class OverrideDefaultPartyHealingModel
    {
        [HarmonyPrefix]
        [HarmonyPatch("GetSurvivalChance")]
        static bool PrefixGetAiWeight(ref float __result, PartyBase party, CharacterObject character, DamageTypes damageType, PartyBase enemyParty = null)
        {
            if (character.IsPlayerCharacter)
            {
                __result = 1f;
                return false;
            }
            ExplainedNumber stat = new ExplainedNumber(1f);
            if (party != null && party.MobileParty != null)
            {
                MobileParty mobileParty = party.MobileParty;
                SkillHelper.AddSkillBonusForParty(DefaultSkills.Medicine, DefaultSkillEffects.SurgeonSurvivalBonus, mobileParty, ref stat);
                if (enemyParty?.MobileParty != null && enemyParty.MobileParty.HasPerk(DefaultPerks.Medicine.DoctorsOath))
                {
                    SkillHelper.AddSkillBonusForParty(DefaultSkills.Medicine, DefaultSkillEffects.SurgeonSurvivalBonus, enemyParty.MobileParty, ref stat);
                }
                stat.Add((float)character.Level * 0.02f);
                if (!character.IsHero && party.MapEvent != null && character.Tier < 3)
                {
                    PerkHelper.AddPerkBonusForParty(DefaultPerks.Medicine.PhysicianOfPeople, party.MobileParty, isPrimaryBonus: false, ref stat);
                }
                if (character.IsHero)
                {
                    stat.AddFactor(49f);
                    //stat.Add(stat.ResultNumber * 50f - stat.ResultNumber);
                }
                ExplainedNumber stat2 = new ExplainedNumber(1f / stat.ResultNumber);
                if (character.IsHero)
                {
                    if (party.IsMobile && party.MobileParty.HasPerk(DefaultPerks.Medicine.CheatDeath, checkSecondaryRole: true))
                    {
                        stat2.AddFactor(DefaultPerks.Medicine.CheatDeath.SecondaryBonus, DefaultPerks.Medicine.CheatDeath.Name);
                    }
                    if (character.HeroObject.Clan == Clan.PlayerClan)
                    {
                        float clanMemberDeathChanceMultiplier = Campaign.Current.Models.DifficultyModel.GetClanMemberDeathChanceMultiplier();
                        if (!clanMemberDeathChanceMultiplier.ApproximatelyEqualsTo(0f))
                        {
                            stat2.AddFactor(clanMemberDeathChanceMultiplier, GameTexts.FindText("str_game_difficulty"));
                        }
                    }
                }
                __result = 1f - MBMath.ClampFloat(stat2.ResultNumber, 0f, 1f);
                return false;
            }
            if (stat.ResultNumber.ApproximatelyEqualsTo(0f))
            {
                __result = 0f;
                return false;
            }
            __result = 1f - 1f / stat.ResultNumber;
            return false;
        }
    }

    [HarmonyPatch(typeof(StoryModeGenericXpModel))]
    [HarmonyPatch("GetXpMultiplier")]
    class AddSkillXpPatch
    {
        static bool Prefix(StoryModeGenericXpModel __instance, Hero hero, ref float __result)
        {
            __result = 1f;
            return false;
        }
    }

    [HarmonyPatch(typeof(StoryModeCombatXpModel))]
    class GetXpFromHitPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("GetXpFromHit")]
        static bool PrefixGetXpFromHit(ref StoryModeCombatXpModel __instance, CharacterObject attackerTroop, CharacterObject captain, CharacterObject attackedTroop, PartyBase party, int damage, bool isFatal, MissionTypeEnum missionType, out int xpAmount)
        {
            if (missionType == MissionTypeEnum.Battle || missionType == MissionTypeEnum.PracticeFight || missionType == MissionTypeEnum.Tournament)
            {
                float attackerLevel = attackerTroop.Level;
                float attackedLevel = attackedTroop.Level;

                float levelDiffModifier = Math.Max(1f, 1f + ((attackedLevel / attackerLevel) / 10f));

                int attackedTroopMaxHP = attackedTroop.MaxHitPoints();
                float troopPower = 0f;
                troopPower = ((party?.MapEvent == null) ? Campaign.Current.Models.MilitaryPowerModel.GetTroopPowerBasedOnContext(attackerTroop) : Campaign.Current.Models.MilitaryPowerModel.GetTroopPowerBasedOnContext(attackerTroop, party.MapEvent.EventType, party.Side, missionType == MissionTypeEnum.SimulationBattle));
                float rawXpNum = 0.4f * ((troopPower + 0.5f) * (float)(Math.Min(damage, attackedTroopMaxHP) + (isFatal ? attackedTroopMaxHP : 0)));
                float xpModifier;
                switch (missionType)
                {
                    default:
                        xpModifier = 1f;
                        break;
                    case MissionTypeEnum.Battle:
                        xpModifier = 1f;
                        break;
                    case MissionTypeEnum.SimulationBattle:
                        xpModifier = 1f;
                        break;
                    case MissionTypeEnum.Tournament:
                        xpModifier = 1f;
                        break;
                    case MissionTypeEnum.PracticeFight:
                        xpModifier = 1f;
                        break;
                    case MissionTypeEnum.NoXp:
                        xpModifier = 0f;
                        break;
                }
                rawXpNum = rawXpNum * xpModifier * levelDiffModifier;
                ExplainedNumber xpToGain = new ExplainedNumber(rawXpNum);
                if (party != null)
                {
                    if (party.IsMobile && party.MobileParty.LeaderHero != null)
                    {
                        if (!attackerTroop.IsRanged && party.MobileParty.HasPerk(DefaultPerks.OneHanded.Trainer, checkSecondaryRole: true))
                        {
                            xpToGain.AddFactor(DefaultPerks.OneHanded.Trainer.SecondaryBonus * 0.01f, DefaultPerks.OneHanded.Trainer.Name);
                        }
                        if (attackerTroop.HasThrowingWeapon() && party.MobileParty.HasPerk(DefaultPerks.Throwing.Resourceful, checkSecondaryRole: true))
                        {
                            xpToGain.AddFactor(DefaultPerks.Throwing.Resourceful.SecondaryBonus * 0.01f, DefaultPerks.Throwing.Resourceful.Name);
                        }
                        if (attackerTroop.IsInfantry)
                        {
                            if (party.MobileParty.HasPerk(DefaultPerks.OneHanded.CorpsACorps))
                            {
                                xpToGain.AddFactor(DefaultPerks.OneHanded.CorpsACorps.PrimaryBonus * 0.01f, DefaultPerks.OneHanded.CorpsACorps.Name);
                            }
                            if (party.MobileParty.HasPerk(DefaultPerks.TwoHanded.BaptisedInBlood, checkSecondaryRole: true))
                            {
                                xpToGain.AddFactor(DefaultPerks.TwoHanded.BaptisedInBlood.SecondaryBonus * 0.01f, DefaultPerks.TwoHanded.BaptisedInBlood.Name);
                            }
                        }
                        if (party.MobileParty.HasPerk(DefaultPerks.OneHanded.LeadByExample))
                        {
                            xpToGain.AddFactor(DefaultPerks.OneHanded.LeadByExample.PrimaryBonus * 0.01f, DefaultPerks.OneHanded.LeadByExample.Name);
                        }
                        if (attackerTroop.IsRanged && party.MobileParty.HasPerk(DefaultPerks.Crossbow.MountedCrossbowman, checkSecondaryRole: true))
                        {
                            xpToGain.AddFactor(DefaultPerks.Crossbow.MountedCrossbowman.SecondaryBonus * 0.01f, DefaultPerks.Crossbow.MountedCrossbowman.Name);
                        }
                        if (attackerTroop.Culture.IsBandit && party.MobileParty.HasPerk(DefaultPerks.Roguery.NoRestForTheWicked))
                        {
                            xpToGain.AddFactor(DefaultPerks.Roguery.NoRestForTheWicked.PrimaryBonus * 0.01f, DefaultPerks.Roguery.NoRestForTheWicked.Name);
                        }
                    }
                    if (party.IsMobile && party.MobileParty.IsGarrison && party.MobileParty.CurrentSettlement?.Town.Governor != null)
                    {
                        PerkHelper.AddPerkBonusForTown(DefaultPerks.TwoHanded.ArrowDeflection, party.MobileParty.CurrentSettlement.Town, ref xpToGain);
                        if (attackerTroop.IsMounted)
                        {
                            PerkHelper.AddPerkBonusForTown(DefaultPerks.Polearm.Guards, party.MobileParty.CurrentSettlement.Town, ref xpToGain);
                        }
                    }
                }
                if (captain != null && captain.IsHero && captain.GetPerkValue(DefaultPerks.Leadership.InspiringLeader))
                {
                    xpToGain.AddFactor(DefaultPerks.Leadership.InspiringLeader.SecondaryBonus, DefaultPerks.Leadership.InspiringLeader.Name);
                }
                xpAmount = MathF.Round(xpToGain.ResultNumber);
                return false;
            }
            xpAmount = 0;
            return true;
        }
    }

    //[HarmonyPatch(typeof(Agent))]
    //[HarmonyPatch("OnWieldedItemIndexChange")]
    //class OnWieldedItemIndexChangePatch
    //{
    //    static void Postfix(ref Agent __instance, bool isOffHand, bool isWieldedInstantly, bool isWieldedOnSpawn)
    //    {
    //        if (isOffHand)
    //        {
    //            EquipmentIndex wieldedItemIndex = __instance.GetWieldedItemIndex(HandIndex.OffHand);
    //            if (wieldedItemIndex < EquipmentIndex.None && __instance.Equipment[EquipmentIndex.Weapon4].Item != null)
    //            {
    //                __instance.Equipment[3] = __instance.Equipment[4];
    //                __instance.Equipment[4] = MissionWeapon.Invalid;

    //                WeaponData weaponData = WeaponData.InvalidWeaponData;
    //                WeaponStatsData[] weaponStatsData = null;
    //                WeaponData ammoWeaponData = WeaponData.InvalidWeaponData;
    //                WeaponStatsData[] ammoWeaponStatsData = null;
    //                if (!__instance.Equipment[EquipmentIndex.Weapon3].IsEmpty)
    //                {
    //                    weaponData = __instance.Equipment[EquipmentIndex.Weapon3].GetWeaponData(needBatchedVersionForMeshes: true);
    //                    weaponStatsData = __instance.Equipment[EquipmentIndex.Weapon3].GetWeaponStatsData();
    //                    ammoWeaponData = __instance.Equipment[EquipmentIndex.Weapon3].GetAmmoWeaponData(needBatchedVersion: true);
    //                    ammoWeaponStatsData = __instance.Equipment[EquipmentIndex.Weapon3].GetAmmoWeaponStatsData();
    //                }
    //                //GameEntity gameEntity = weapon.GameEntity;
    //                MethodInfo method = typeof(Agent).GetMethod("WeaponEquipped", BindingFlags.NonPublic | BindingFlags.Instance);
    //                method.DeclaringType.GetMethod("WeaponEquipped");
    //                method.Invoke(__instance, new object[] { EquipmentIndex.Weapon3, weaponData, weaponStatsData, ammoWeaponData, ammoWeaponStatsData, null, true, false });
    //                weaponData.DeinitializeManagedPointers();
    //                ammoWeaponData.DeinitializeManagedPointers();

    //                //__instance.UpdateAgentProperties();

    //                weaponData = WeaponData.InvalidWeaponData;
    //                weaponStatsData = null;
    //                ammoWeaponData = WeaponData.InvalidWeaponData;
    //                ammoWeaponStatsData = null;
    //                if (!__instance.Equipment[EquipmentIndex.Weapon4].IsEmpty)
    //                {
    //                    weaponData = __instance.Equipment[EquipmentIndex.Weapon4].GetWeaponData(needBatchedVersionForMeshes: true);
    //                    weaponStatsData = __instance.Equipment[EquipmentIndex.Weapon4].GetWeaponStatsData();
    //                    ammoWeaponData = __instance.Equipment[EquipmentIndex.Weapon4].GetAmmoWeaponData(needBatchedVersion: true);
    //                    ammoWeaponStatsData = __instance.Equipment[EquipmentIndex.Weapon4].GetAmmoWeaponStatsData();
    //                }
    //                //GameEntity gameEntity = weapon.GameEntity;
    //                method.Invoke(__instance, new object[] { EquipmentIndex.Weapon4, weaponData, weaponStatsData, ammoWeaponData, ammoWeaponStatsData, null, true, false });
    //                weaponData.DeinitializeManagedPointers();
    //                ammoWeaponData.DeinitializeManagedPointers();

    //                //__instance.UpdateAgentProperties();

    //                //__instance.RemoveEquippedWeapon(EquipmentIndex.Weapon3);
    //                //__instance.Equipment[4] = MissionWeapon.Invalid;
    //                //WeaponEquipped(slotIndex, in WeaponData.InvalidWeaponData, null, in WeaponData.InvalidWeaponData, null, null, removeOldWeaponFromScene: true, isWieldedOnSpawn: false);
    //            }
    //            if (wieldedItemIndex >= EquipmentIndex.WeaponItemBeginSlot && __instance.Equipment[wieldedItemIndex].IsShield())
    //            {
    //                if(wieldedItemIndex != EquipmentIndex.Weapon3)
    //                {
    //                    MissionWeapon tempEquip = __instance.Equipment[3];
    //                    __instance.Equipment[3] = __instance.Equipment[wieldedItemIndex];
    //                    __instance.Equipment[wieldedItemIndex] = tempEquip;

    //                    WeaponData weaponData = WeaponData.InvalidWeaponData;
    //                    WeaponStatsData[] weaponStatsData = null;
    //                    WeaponData ammoWeaponData = WeaponData.InvalidWeaponData;
    //                    WeaponStatsData[] ammoWeaponStatsData = null;
    //                    if (!__instance.Equipment[3].IsEmpty)
    //                    {
    //                        weaponData = __instance.Equipment[3].GetWeaponData(needBatchedVersionForMeshes: true);
    //                        weaponStatsData = __instance.Equipment[3].GetWeaponStatsData();
    //                        ammoWeaponData = __instance.Equipment[3].GetAmmoWeaponData(needBatchedVersion: true);
    //                        ammoWeaponStatsData = __instance.Equipment[3].GetAmmoWeaponStatsData();
    //                    }
    //                    //GameEntity gameEntity = weapon.GameEntity;
    //                    MethodInfo method = typeof(Agent).GetMethod("WeaponEquipped", BindingFlags.NonPublic | BindingFlags.Instance);
    //                    method.DeclaringType.GetMethod("WeaponEquipped");
    //                    method.Invoke(__instance, new object[] { 3, weaponData, weaponStatsData, ammoWeaponData, ammoWeaponStatsData, null, true, true });
    //                    weaponData.DeinitializeManagedPointers();
    //                    ammoWeaponData.DeinitializeManagedPointers();

    //                    weaponData = WeaponData.InvalidWeaponData;
    //                    weaponStatsData = null;
    //                    ammoWeaponData = WeaponData.InvalidWeaponData;
    //                    ammoWeaponStatsData = null;
    //                    if (!__instance.Equipment[wieldedItemIndex].IsEmpty)
    //                    {
    //                        weaponData = __instance.Equipment[wieldedItemIndex].GetWeaponData(needBatchedVersionForMeshes: true);
    //                        weaponStatsData = __instance.Equipment[wieldedItemIndex].GetWeaponStatsData();
    //                        ammoWeaponData = __instance.Equipment[wieldedItemIndex].GetAmmoWeaponData(needBatchedVersion: true);
    //                        ammoWeaponStatsData = __instance.Equipment[wieldedItemIndex].GetAmmoWeaponStatsData();
    //                    }
    //                    //GameEntity gameEntity = weapon.GameEntity;
    //                    method.Invoke(__instance, new object[] { wieldedItemIndex, weaponData, weaponStatsData, ammoWeaponData, ammoWeaponStatsData, null, true, true });
    //                    weaponData.DeinitializeManagedPointers();
    //                    ammoWeaponData.DeinitializeManagedPointers();

    //                    __instance.UpdateAgentProperties();
    //                }
    //                EquipmentIndex suitableEI = EquipmentIndex.None;
    //                for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.Weapon4; equipmentIndex++)
    //                {
    //                    if (__instance.Equipment[equipmentIndex].IsEmpty)
    //                    {
    //                        suitableEI = equipmentIndex;
    //                        break;
    //                    }
    //                }
    //                if(suitableEI == EquipmentIndex.None)
    //                {
    //                    for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.Weapon4; equipmentIndex++)
    //                    {
    //                        if (__instance.Equipment[equipmentIndex].IsAnyAmmo() || __instance.Equipment[equipmentIndex].MaxAmmo > 0)
    //                        {
    //                            suitableEI = equipmentIndex;
    //                            break;
    //                        }
    //                    }
    //                }
    //                //__instance.AgentVisuals.ClearAllWeaponMeshes();
    //                //__instance.AgentVisuals.ClearWeaponMeshes(EquipmentIndex.Weapon4);
    //                if(suitableEI != EquipmentIndex.None)
    //                {
    //                    if (__instance.Equipment[suitableEI].IsEmpty)
    //                    {
    //                        string shieldID = __instance.Equipment[3].Item.StringId;
    //                        MissionWeapon weapon = new MissionWeapon(MBObjectManager.Instance.GetObject<ItemObject>(shieldID + "_passive"), null, null);

    //                        __instance.Equipment[suitableEI] = weapon;
    //                        __instance.EquipWeaponWithNewEntity(suitableEI, ref weapon);
    //                        //__instance.TryToWieldWeaponInSlot(EquipmentIndex.Weapon4, Agent.WeaponWieldActionType.Instant, isWieldedOnSpawn: true);

    //                        WeaponData weaponData = WeaponData.InvalidWeaponData;
    //                        WeaponStatsData[] weaponStatsData = null;
    //                        WeaponData ammoWeaponData = WeaponData.InvalidWeaponData;
    //                        WeaponStatsData[] ammoWeaponStatsData = null;
    //                        if (!__instance.Equipment[suitableEI].IsEmpty)
    //                        {
    //                            weaponData = __instance.Equipment[suitableEI].GetWeaponData(needBatchedVersionForMeshes: true);
    //                            weaponStatsData = __instance.Equipment[suitableEI].GetWeaponStatsData();
    //                            ammoWeaponData = __instance.Equipment[suitableEI].GetAmmoWeaponData(needBatchedVersion: true);
    //                            ammoWeaponStatsData = __instance.Equipment[suitableEI].GetAmmoWeaponStatsData();
    //                        }
    //                        //GameEntity gameEntity = weapon.GameEntity;
    //                        MethodInfo method = typeof(Agent).GetMethod("WeaponEquipped", BindingFlags.NonPublic | BindingFlags.Instance);
    //                        method.DeclaringType.GetMethod("WeaponEquipped");
    //                        method.Invoke(__instance, new object[] { suitableEI, weaponData, weaponStatsData, ammoWeaponData, ammoWeaponStatsData, null, true, true });
    //                        weaponData.DeinitializeManagedPointers();
    //                        ammoWeaponData.DeinitializeManagedPointers();

    //                        __instance.UpdateAgentProperties();

    //                    }
    //                    else
    //                    {
    //                        EquipmentIndex extraEI = EquipmentIndex.Weapon4;
    //                        string shieldID = __instance.Equipment[3].Item.StringId;
    //                        MissionWeapon weapon = new MissionWeapon(MBObjectManager.Instance.GetObject<ItemObject>(shieldID + "_passive"), null, null);

    //                        __instance.Equipment[extraEI] = __instance.Equipment[suitableEI];
    //                        __instance.Equipment[suitableEI] = weapon;
    //                        __instance.EquipWeaponWithNewEntity(suitableEI, ref weapon);
    //                        //__instance.TryToWieldWeaponInSlot(EquipmentIndex.Weapon4, Agent.WeaponWieldActionType.Instant, isWieldedOnSpawn: true);

    //                        WeaponData weaponData = WeaponData.InvalidWeaponData;
    //                        WeaponStatsData[] weaponStatsData = null;
    //                        WeaponData ammoWeaponData = WeaponData.InvalidWeaponData;
    //                        WeaponStatsData[] ammoWeaponStatsData = null;
    //                        if (!__instance.Equipment[extraEI].IsEmpty)
    //                        {
    //                            weaponData = __instance.Equipment[extraEI].GetWeaponData(needBatchedVersionForMeshes: true);
    //                            weaponStatsData = __instance.Equipment[extraEI].GetWeaponStatsData();
    //                            ammoWeaponData = __instance.Equipment[extraEI].GetAmmoWeaponData(needBatchedVersion: true);
    //                            ammoWeaponStatsData = __instance.Equipment[extraEI].GetAmmoWeaponStatsData();
    //                        }
    //                        //GameEntity gameEntity = weapon.GameEntity;
    //                        MethodInfo method = typeof(Agent).GetMethod("WeaponEquipped", BindingFlags.NonPublic | BindingFlags.Instance);
    //                        method.DeclaringType.GetMethod("WeaponEquipped");
    //                        method.Invoke(__instance, new object[] { extraEI, weaponData, weaponStatsData, ammoWeaponData, ammoWeaponStatsData, null, true, true });
    //                        weaponData.DeinitializeManagedPointers();
    //                        ammoWeaponData.DeinitializeManagedPointers();

    //                        //__instance.UpdateAgentProperties();

    //                        weaponData = WeaponData.InvalidWeaponData;
    //                        weaponStatsData = null;
    //                        ammoWeaponData = WeaponData.InvalidWeaponData;
    //                        ammoWeaponStatsData = null;
    //                        if (!__instance.Equipment[suitableEI].IsEmpty)
    //                        {
    //                            weaponData = __instance.Equipment[suitableEI].GetWeaponData(needBatchedVersionForMeshes: true);
    //                            weaponStatsData = __instance.Equipment[suitableEI].GetWeaponStatsData();
    //                            ammoWeaponData = __instance.Equipment[suitableEI].GetAmmoWeaponData(needBatchedVersion: true);
    //                            ammoWeaponStatsData = __instance.Equipment[suitableEI].GetAmmoWeaponStatsData();
    //                        }
    //                        //GameEntity gameEntity = weapon.GameEntity;
    //                        method.Invoke(__instance, new object[] { suitableEI, weaponData, weaponStatsData, ammoWeaponData, ammoWeaponStatsData, null, true, true });
    //                        weaponData.DeinitializeManagedPointers();
    //                        ammoWeaponData.DeinitializeManagedPointers();

    //                        __instance.UpdateAgentProperties();
    //                    }
    //                }
    //            }
    //        }
    //    }
    //}

}
