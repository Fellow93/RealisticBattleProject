using HarmonyLib;
using RBMConfig;
using SandBox.GameComponents;
using SandBox.Missions.MissionLogics;
using System;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ItemObject;
using static TaleWorlds.MountAndBlade.Agent;

namespace RBMCombat
{
    class DamageRework
    {
        [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
        [HarmonyPatch("ComputeBlowMagnitudeMissile")]
        class RealArrowDamage
        {
            static bool Prefix(in AttackInformation attackInformation, in AttackCollisionData acd, in MissionWeapon weapon, float momentumRemaining, in Vec2 victimVelocity, out float baseMagnitude, out float specialMagnitude)
            {
                Vec3 missileVelocity = acd.MissileVelocity;
                //Vec3 cgn = acd.CollisionGlobalNormal;
                //Vec3 wbd = acd.MissileVelocity;

                //float angleModifier = Vec3.DotProduct(gcn, wbd);

                //Vec3 resultVec = gcn + wbd;
                //float angleModifier = 1f - Math.Abs((resultVec.x + resultVec.y + resultVec.z) / 3);
                //Agent attacker = null;
                

                float missileTotalDamage = acd.MissileTotalDamage;

                WeaponComponentData currentUsageItem = weapon.CurrentUsageItem;
                ItemObject weaponItem;
                if (weapon.AmmoWeapon.Item != null)
                {
                    weaponItem = weapon.AmmoWeapon.Item;
                }
                else
                {
                    weaponItem = weapon.Item;
                }

                float length;
                if (!attackInformation.IsVictimAgentNull)
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
                                length -= Utilities.throwableCorrectionSpeed;
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
                                length -= Utilities.throwableCorrectionSpeed;
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
                                length -= Utilities.throwableCorrectionSpeed;
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
                    physicalDamage = (length * length * (weaponItem.Weight) * 0.5f);
                }

                baseMagnitude = physicalDamage * missileTotalDamage * momentumRemaining;

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Javelin"))
                {
                    //baseMagnitude = (physicalDamage * momentumRemaining + (missileTotalDamage * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                    if ((DamageTypes)acd.DamageType == DamageTypes.Pierce)
                    {
                        baseMagnitude = (physicalDamage * momentumRemaining + (missileTotalDamage * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                    }
                    else if ((DamageTypes)acd.DamageType == DamageTypes.Cut)
                    {
                        baseMagnitude = (physicalDamage * momentumRemaining + (missileTotalDamage * 0.5f));
                    }
                    else
                    {
                        baseMagnitude = (physicalDamage * momentumRemaining + (missileTotalDamage * 0.5f)) * 0.5f;
                    }
                }

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("ThrowingAxe"))
                {
                    baseMagnitude = physicalDamage * momentumRemaining + (missileTotalDamage * 1f);
                }

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("ThrowingKnife") ||
                    weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Dagger"))
                {
                    baseMagnitude = (physicalDamage * momentumRemaining + (missileTotalDamage * 0f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier * 0.6f;
                }

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("OneHandedPolearm") ||
                    weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("LowGripPolearm"))
                {
                    baseMagnitude = (physicalDamage * momentumRemaining + (missileTotalDamage * 1f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
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
                        baseMagnitude = physicalDamage * missileTotalDamage * momentumRemaining; // momentum makes more sense for blunt attacks, maybe 500 damage is needed for sling projectiles
                    }
                }
                specialMagnitude = baseMagnitude;

                return false;
            }
        }
    }

    [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
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
                        if (weapon.WeaponClass == WeaponClass.Arrow || weapon.WeaponClass == WeaponClass.Bolt || weapon.WeaponClass == WeaponClass.Javelin)
                        {
                            dmgMultiplier *= 0.1f;
                        }
                        else
                        {
                            dmgMultiplier *= 0.8f;
                        }
                        break;
                    case DamageTypes.Pierce:
                        if (weapon.WeaponClass == WeaponClass.Arrow || weapon.WeaponClass == WeaponClass.Bolt || weapon.WeaponClass == WeaponClass.Javelin)
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

    //[HarmonyPatch(typeof(Mission))]
    //[HarmonyPatch("ComputeBlowMagnitudeImp")]
    //class ComputeBlowMagnitudeImpPatch
    //{
    //    static bool Prefix(ref Mission __instance, ref AttackCollisionData acd, ref AttackInformation attackInformation, Vec2 attackerAgentVelocity, Vec2 victimAgentVelocity, float momentumRemaining, bool cancelDamage, bool hitWithAnotherBone, out float specialMagnitude, out int speedBonusInt, StrikeType strikeType, Agent.UsageDirection attackDirection, in MissionWeapon weapon, bool attackerIsDoingPassiveAttack)
    //    {
    //        //Agent attacker = null;
    //        //Agent victim = null;
    //        //foreach (Agent agent in Mission.Current.Agents){
    //        //    if(attackInformation.VictimAgentOrigin == agent.Origin)
    //        //    {
    //        //        victim = agent;
    //        //    }
    //        //    if (attackInformation.AttackerAgentOrigin == agent.Origin)
    //        //    {
    //        //        attacker = agent;
    //        //    }
    //        //}

    //        //if(attacker != null && victim != null)
    //        //{
    //        //    if(acd.VictimHitBodyPart == BoneBodyPartType.Head)
    //        //    {
    //        //        Vec3 ald = attacker.LookDirection;
    //        //        Vec3 vld = victim.LookDirection;
    //        //        vld *= -1f; //invert look vector
    //        //        if (Math.Abs(ald.x - vld.x) < 0.1f && Math.Abs(ald.y - vld.y) < 0.1f)
    //        //        {
    //        //            InformationManager.DisplayMessage(new InformationMessage("CRIT"));
    //        //        }
    //        //    }
    //        //}

    //        acd.MovementSpeedDamageModifier = 0f;
    //        speedBonusInt = 0;
    //        if (acd.IsMissile)
    //        {
    //            float missileTotalDamage = acd.MissileTotalDamage;
    //            Vec3 missileVelocity = acd.MissileVelocity;
    //            //Vec3 cgn = acd.CollisionGlobalNormal;
    //            //Vec3 wbd = acd.MissileVelocity;

    //            //float angleModifier = Vec3.DotProduct(gcn, wbd);

    //            //Vec3 resultVec = gcn + wbd;
    //            //float angleModifier = 1f - Math.Abs((resultVec.x + resultVec.y + resultVec.z) / 3);
    //            WeaponComponentData currentUsageItem = weapon.CurrentUsageItem;
    //            ItemObject weaponItem;
    //            if (weapon.AmmoWeapon.Item != null)
    //            {
    //                weaponItem = weapon.AmmoWeapon.Item;
    //            }
    //            else
    //            {
    //                weaponItem = weapon.Item;
    //            }

    //            float length;
    //            if (!attackInformation.IsVictimAgentNull)
    //            {
    //                length = (victimAgentVelocity.ToVec3() - missileVelocity).Length;
    //            }
    //            else
    //            {
    //                length = missileVelocity.Length;
    //            }
    //            //float expr_32 = length / acd.MissileStartingBaseSpeed;
    //            //float num = expr_32 * expr_32;
    //            if (weaponItem != null && weaponItem.PrimaryWeapon != null)
    //            {
    //                switch (weaponItem.PrimaryWeapon.WeaponClass.ToString())
    //                {
    //                    case "Boulder":
    //                    case "Stone":
    //                        {
    //                            missileTotalDamage *= 0.01f;
    //                            break;
    //                        }
    //                    case "ThrowingAxe":
    //                    case "ThrowingKnife":
    //                    case "Dagger":
    //                        {
    //                            length -= 0f; //5f
    //                            //if (length < 5.0f)
    //                            //{
    //                            //    length = 5f;
    //                            //}
    //                            //length += -(7.0f);
    //                            //if (length < 5.0f)
    //                            //{
    //                            //    length = 5.0f;
    //                            //} 
    //                            break;
    //                        }
    //                    case "Javelin":
    //                        {
    //                            length -= 10f;
    //                            if (length < 5.0f)
    //                            {
    //                                length = 5f;
    //                            }
    //                            //missileTotalDamage += 168.0f;
    //                            //missileTotalDamage *= 0.01f;
    //                            //missileTotalDamage = 1f;
    //                            break;
    //                        }
    //                    case "OneHandedPolearm":
    //                        {
    //                            length -= 10f;
    //                            if (length < 5.0f)
    //                            {
    //                                length = 5f;
    //                            }
    //                            //missileTotalDamage -= 25f;
    //                            //missileTotalDamage = 1f;
    //                            break;
    //                        }
    //                    case "LowGripPolearm":
    //                        {
    //                            length -= 10f;
    //                            if (length < 5.0f)
    //                            {
    //                                length = 5f;
    //                            }
    //                            //missileTotalDamage -= 25f;
    //                            //missileTotalDamage *= 0.01f;
    //                            //missileTotalDamage = 1f;
    //                            break;
    //                        }
    //                    case "Arrow":
    //                        {
    //                            missileTotalDamage -= 100f;
    //                            missileTotalDamage *= 0.01f;
    //                            break;
    //                        }
    //                    case "Bolt":
    //                        {
    //                            missileTotalDamage -= 100f;
    //                            missileTotalDamage *= 0.01f;
    //                            break;
    //                        }
    //                }
    //            }

    //            float physicalDamage = ((length * length) * (weaponItem.Weight)) / 2;
    //            float momentumDamage = (length * weaponItem.Weight);
    //            if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Javelin") && physicalDamage > 300f)
    //            {
    //                physicalDamage = 300f;
    //            }

    //            if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("OneHandedPolearm") && physicalDamage > (weaponItem.Weight) * 300f)
    //            {
    //                physicalDamage = (weaponItem.Weight) * 300f;
    //            }

    //            if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Arrow") && physicalDamage > (weaponItem.Weight) * 2250f)
    //            {
    //                physicalDamage = (weaponItem.Weight) * 2250f;
    //            }

    //            if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Bolt") && physicalDamage > (weaponItem.Weight) * 2500f)
    //            {
    //                physicalDamage = (weaponItem.Weight) * 2500f;
    //            }

    //            if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Stone") ||
    //                weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Boulder"))
    //            {
    //                physicalDamage = (length * (weaponItem.Weight));
    //            }

    //            acd.BaseMagnitude = physicalDamage * missileTotalDamage * momentumRemaining;

    //            if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Javelin"))
    //            {
    //                //baseMagnitude = (physicalDamage * momentumRemaining + (missileTotalDamage * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
    //                if ((DamageTypes)acd.DamageType == DamageTypes.Cut || (DamageTypes)acd.DamageType == DamageTypes.Pierce)
    //                {
    //                    acd.BaseMagnitude = (physicalDamage * momentumRemaining + (missileTotalDamage * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
    //                }
    //                else
    //                {
    //                    acd.BaseMagnitude = (physicalDamage * momentumRemaining + (missileTotalDamage * 0.5f)) * 0.25f;
    //                }
    //            }

    //            if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("ThrowingAxe"))
    //            {
    //                acd.BaseMagnitude = physicalDamage * momentumRemaining + (missileTotalDamage * 1f);
    //            }

    //            if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("ThrowingKnife") ||
    //                weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Dagger"))
    //            {
    //                acd.BaseMagnitude = (physicalDamage * momentumRemaining + (missileTotalDamage * 0f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier * 0.6f;
    //            }

    //            if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("OneHandedPolearm") ||
    //                weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("LowGripPolearm"))
    //            {
    //                acd.BaseMagnitude = (physicalDamage * momentumRemaining + (missileTotalDamage * 1f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
    //            }
    //            if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Arrow") ||
    //                weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Bolt"))
    //            {
    //                if ((DamageTypes)acd.DamageType == DamageTypes.Cut || (DamageTypes)acd.DamageType == DamageTypes.Pierce)
    //                {
    //                    acd.BaseMagnitude = physicalDamage * missileTotalDamage * momentumRemaining;
    //                }
    //                else
    //                {
    //                    acd.BaseMagnitude = momentumDamage * missileTotalDamage * momentumRemaining; // momentum makes more sense for blunt attacks, maybe 500 damage is needed for sling projectiles
    //                }
    //            }
    //            specialMagnitude = acd.BaseMagnitude;
    //            return false;
    //        }
    //        specialMagnitude = 0f;
    //        return true;
    //    }
    //}

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
            float num2 = 0.5f * weaponWeight * num * num * RBMConfig.RBMConfig.ThrustMagnitudeModifier; // lances need to have 3 times more damage to be preferred over maces
            return num2;

        }
    }

    [HarmonyPatch(typeof(Mission))]
    [HarmonyPatch("GetAttackCollisionResults")]
    class GetAttackCollisionResultsPatch
    {
        static void Postfix(Agent attackerAgent, Agent victimAgent, ref AttackCollisionData attackCollisionData, out CombatLogData combatLog, ref CombatLogData __result)
        {
            if (attackerAgent != null && attackCollisionData.StrikeType == (int)StrikeType.Swing && !attackCollisionData.AttackBlockedWithShield && !attackerAgent.WieldedWeapon.IsEmpty && !Utilities.HitWithWeaponBlade(in attackCollisionData, attackerAgent.WieldedWeapon))
            {
                string typeOfHandle = "Handle";
                if (attackerAgent.WieldedWeapon.CurrentUsageItem != null && 
                    (attackerAgent.WieldedWeapon.CurrentUsageItem.WeaponClass == WeaponClass.Dagger ||
                    attackerAgent.WieldedWeapon.CurrentUsageItem.WeaponClass == WeaponClass.OneHandedSword ||
                    attackerAgent.WieldedWeapon.CurrentUsageItem.WeaponClass == WeaponClass.TwoHandedSword))
                {
                    typeOfHandle = "Pommel";
                }
                if (attackerAgent != null && attackerAgent.IsPlayerControlled)
                {
                    InformationManager.DisplayMessage(new InformationMessage(typeOfHandle + " hit", Color.FromUint(4289612505u)));
                }
                if (victimAgent != null && victimAgent.IsPlayerControlled)
                {
                    InformationManager.DisplayMessage(new InformationMessage(typeOfHandle + " hit", Color.FromUint(4289612505u)));
                }
                __result.DamageType = DamageTypes.Blunt;
            }
            combatLog = __result;
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
                float mixedEnergy = 0.5f * (weaponWeight + 1.5f) * combinedSpeed * combinedSpeed;
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
                __result = thrust * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                return false;
            }
            __result = 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
    [HarmonyPatch("ComputeBlowDamage")]
    public class OverrideDamageCalc
    {
        static bool Prefix(ref AttackInformation attackInformation, ref AttackCollisionData attackCollisionData, WeaponComponentData attackerWeapon, ref DamageTypes damageType, float magnitude, int speedBonus, bool cancelDamage, out int inflictedDamage, out int absorbedByArmor)
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
                float adjustedArmor = Game.Current.BasicModels.StrikeMagnitudeModel.CalculateAdjustedArmorForBlow(armorAmountFloat, attackerAgentCharacter, attackerCaptainCharacter, victimAgentCharacter, victimCaptainCharacter, attackerWeapon);
                armorAmount = adjustedArmor;
            }
            //float num2 = (float)armorAmount;
            //if (collidedWithShieldOnBack && shieldOnBack != null)
            //{
            //    armorAmount += 20f;
            //}

            Agent attacker = null;
            Agent victim = null;
            foreach (Agent agent in Mission.Current.Agents)
            {
                if (attackInformation.VictimAgentOrigin == agent.Origin)
                {
                    victim = agent;
                }
                if (attackInformation.AttackerAgentOrigin == agent.Origin)
                {
                    attacker = agent;
                }
            }
            //if(!attacker.WieldedWeapon.IsEmpty && attackCollisionData.StrikeType == (int)StrikeType.Swing && attacker.WieldedWeapon.GetModifiedItemName().Contains("Falx") && Utilities.HitWithWeaponBladeTip(in attackCollisionData, attacker.WieldedWeapon))
            //{
            //    damageType = DamageTypes.Pierce;
            //    //InformationManager.DisplayMessage(new InformationMessage("Falx pierce hit!", Color.FromUint(4289612505u)));
            //}
            bool isBash = false;
            if(attacker != null && attackCollisionData.StrikeType == (int)StrikeType.Swing && damageType != DamageTypes.Blunt && !attacker.WieldedWeapon.IsEmpty && !Utilities.HitWithWeaponBlade(in attackCollisionData, attacker.WieldedWeapon))
            {
                isBash = true;
                damageType = DamageTypes.Blunt;
            }

            //Agent victim = null;
            //foreach (Agent agent in Mission.Current.Agents)
            //{
            //    if (attackInformation.VictimAgentOrigin == agent.Origin)
            //    {
            //        victim = agent;
            //    }
            //}
            bool faceshot = false;
            //bool backLegHit = false;
            bool lowerShoulderHit = false;

            //if (victim != null && victim.IsHuman && attackCollisionData.VictimHitBodyPart == BoneBodyPartType.Legs)
            //{
            //    //lower leg 2,3,5,6
            //    float dotProduct = Vec3.DotProduct(attackCollisionData.WeaponBlowDir, victim.LookFrame.rotation.f);
            //    float dotProductTrehsold = 0.8f;
            //    InformationManager.DisplayMessage(new InformationMessage(""+ dotProduct+ " " + attackCollisionData.CollisionBoneIndex, Color.FromUint(4289612505u)));
            //    if (dotProduct > dotProductTrehsold)
            //    //if (dotProduct < -0.85f && dotProduct2 < -0.75f)
            //    {
            //        if (attacker != null && attacker.IsPlayerControlled)
            //        {
            //            InformationManager.DisplayMessage(new InformationMessage("Back leg hit!", Color.FromUint(4289612505u)));
            //        }
            //        if (victim != null && victim.IsPlayerControlled)
            //        {
            //            InformationManager.DisplayMessage(new InformationMessage("Back leg hit!", Color.FromUint(4289612505u)));
            //        }
            //        backLegHit = true;
            //    }
            //}

            if (victim != null && victim.IsHuman && attackCollisionData.VictimHitBodyPart == BoneBodyPartType.Head)
            {
                float dotProduct = Vec3.DotProduct(attackCollisionData.WeaponBlowDir, victim.LookFrame.rotation.f);
                float dotProductTrehsold = -0.7f;
                if(attackCollisionData.StrikeType == (int)StrikeType.Swing)
                {
                    dotProductTrehsold = -0.8f;
                }
                if (dotProduct < dotProductTrehsold && attackCollisionData.CollisionGlobalNormal.z < 0f)
                //if (dotProduct < -0.85f && dotProduct2 < -0.75f)
                {
                    if(attacker != null && attacker.IsPlayerControlled)
                    {
                        InformationManager.DisplayMessage(new InformationMessage("Face hit!", Color.FromUint(4289612505u)));
                    }
                    if (victim != null && victim.IsPlayerControlled)
                    {
                        InformationManager.DisplayMessage(new InformationMessage("Face hit!", Color.FromUint(4289612505u)));
                    }
                    faceshot = true;
                }
            }

            //if (victim != null && victim.IsHuman && (attackCollisionData.VictimHitBodyPart == BoneBodyPartType.Chest))
            //{
            //    float dotProduct = Vec3.DotProduct(attackCollisionData.WeaponBlowDir, victim.LookFrame.rotation.f);
            //    if (attacker != null && attacker.IsPlayerControlled)
            //    {
            //        InformationManager.DisplayMessage(new InformationMessage(attackCollisionData.CollisionBoneIndex + " " + dotProduct + " " + attackCollisionData.CollisionGlobalNormal, Color.FromUint(4289612505u)));
            //    }
            //    if ((attackCollisionData.CollisionGlobalNormal.z > 0f) &&
            //        (dotProduct > -0.2f && dotProduct < 0.2f))
            //    {
            //        if (attacker != null && attacker.IsPlayerControlled)
            //        {
            //            InformationManager.DisplayMessage(new InformationMessage("Chest weak point hit! " + attackCollisionData.CollisionBoneIndex, Color.FromUint(4289612505u)));
            //        }
            //        if (victim != null && victim.IsPlayerControlled)
            //        {
            //            InformationManager.DisplayMessage(new InformationMessage("Chest weak point hit! " + attackCollisionData.CollisionBoneIndex, Color.FromUint(4289612505u)));
            //        }
            //        underArmHit = true;
            //    }
            //}

            //if (victim != null && victim.IsHuman && (attackCollisionData.VictimHitBodyPart == BoneBodyPartType.ShoulderLeft || attackCollisionData.VictimHitBodyPart == BoneBodyPartType.ShoulderRight))
            //{
            //    //shoulder bones 14,15,21,22
            //    float dotProduct = Vec3.DotProduct(attackCollisionData.WeaponBlowDir, victim.LookFrame.rotation.f);

            //    if (attacker != null && attacker.IsPlayerControlled)
            //    {
            //        InformationManager.DisplayMessage(new InformationMessage(attackCollisionData.CollisionBoneIndex + " " + dotProduct + " " + attackCollisionData.CollisionGlobalNormal, Color.FromUint(4289612505u)));
            //    }
            //    if ((attackCollisionData.CollisionGlobalNormal.y < -0.7f || attackCollisionData.CollisionGlobalNormal.y > 0.7f ) &&
            //        (dotProduct < -0.7f || dotProduct > 0.7f) &&
            //        (attackCollisionData.CollisionBoneIndex == 15 || attackCollisionData.CollisionBoneIndex == 22))
            //    {
            //        if (attacker != null && attacker.IsPlayerControlled)
            //        {
            //            InformationManager.DisplayMessage(new InformationMessage("Shoulder weak point hit! " + attackCollisionData.CollisionBoneIndex, Color.FromUint(4289612505u)));
            //        }
            //        if (victim != null && victim.IsPlayerControlled)
            //        {
            //            InformationManager.DisplayMessage(new InformationMessage("Shoulder weak point hit! " + attackCollisionData.CollisionBoneIndex, Color.FromUint(4289612505u)));
            //        }
            //        underArmHit = true;
            //    }
            //}

            if (victim != null && victim.IsHuman && (attackCollisionData.VictimHitBodyPart == BoneBodyPartType.ShoulderLeft || attackCollisionData.VictimHitBodyPart == BoneBodyPartType.ShoulderRight))
            {
                if((attackCollisionData.CollisionBoneIndex == 15 || attackCollisionData.CollisionBoneIndex == 22) && attackCollisionData.CollisionGlobalNormal.z < 0.2f)
                {
                    if (attacker != null && attacker.IsPlayerControlled)
                    {
                        InformationManager.DisplayMessage(new InformationMessage("Under shoulder hit!", Color.FromUint(4289612505u)));
                    }
                    if (victim != null && victim.IsPlayerControlled)
                    {
                        InformationManager.DisplayMessage(new InformationMessage("Under shoulder hit!", Color.FromUint(4289612505u)));
                    }
                    lowerShoulderHit = true;
                }
            }

            if (faceshot)
            {
                if (!victim.SpawnEquipment[EquipmentIndex.Head].IsEmpty)
                {
                    armorAmount = victim.SpawnEquipment[EquipmentIndex.Head].GetModifiedBodyArmor();
                    //armorAmount = Game.Current.BasicModels.StrikeMagnitudeModel.CalculateAdjustedArmorForBlow(armorAmountFloat, attackerAgentCharacter, attackerCaptainCharacter, victimAgentCharacter, victimCaptainCharacter, attackerWeapon);
                }
                else
                {
                    armorAmount *= 0.15f;
                }
            }

            if (lowerShoulderHit)
            {
                armorAmount = 0f;
                if (!victim.SpawnEquipment[EquipmentIndex.Body].IsEmpty)
                {
                    armorAmount = (victim.SpawnEquipment[EquipmentIndex.Body].GetModifiedArmArmor() * 2f) - victim.SpawnEquipment[EquipmentIndex.Body].GetModifiedBodyArmor();
                }
                if (!victim.SpawnEquipment[EquipmentIndex.Cape].IsEmpty)
                {
                    armorAmount+= victim.SpawnEquipment[EquipmentIndex.Cape].GetModifiedArmArmor();
                }
            }

            if (collidedWithShieldOnBack && shieldOnBack != null && victim != null)
            {
                for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                {
                    if (victim.Equipment != null && !victim.Equipment[equipmentIndex].IsEmpty)
                    {
                        if (victim.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Shield)
                        {
                            attackInformation.VictimShield = victim.Equipment[equipmentIndex];
                            RBMComputeBlowDamageOnShield(ref attackInformation, ref attackCollisionData, attackerWeapon, magnitude, out inflictedDamage);
                            absorbedByArmor = 0;
                            return false;
                        }
                    }
                }
            }

            if (attacker != null && victim != null)
            {
                if (victim.GetCurrentActionType(0) == ActionCodeType.Fall || victim.GetCurrentActionType(0) == ActionCodeType.StrikeKnockBack)
                {
                    armorAmount *= 0.75f;
                }
            }

            string weaponType = "otherDamage";
            if (attackerWeapon != null)
            {
                weaponType = attackerWeapon.WeaponClass.ToString();
            }

            IAgentOriginBase attackerAgentOrigin = attackInformation.AttackerAgentOrigin;
            Formation attackerFormation = attackInformation.AttackerFormation;

            if (!attackCollisionData.IsAlternativeAttack && !attackInformation.IsAttackerAgentMount && attackerAgentOrigin != null && attackInformation.AttackerAgentCharacter != null && !attackCollisionData.IsMissile)
            {
                SkillObject skill = (attackerWeapon == null) ? DefaultSkills.Athletics : attackerWeapon.RelevantSkill;
                if (skill != null)
                {
                    int effectiveSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attackInformation.AttackerAgentCharacter, attackerAgentOrigin, attackerFormation, skill);
                    float skillModifier = Utilities.CalculateSkillModifier(effectiveSkill);

                    if (attacker != null && attacker.IsPlayerControlled)
                    {
                        InformationManager.DisplayMessage(new InformationMessage("magnitude: " + magnitude));
                    }
                    if (victim != null && victim.IsPlayerControlled)
                    {
                        InformationManager.DisplayMessage(new InformationMessage("magnitude: " + magnitude));
                    }
                    
                    bool isPassiveUsage = attackInformation.IsAttackerAgentDoingPassiveAttack;
                    float skillBasedDamage = 0f;
                    const float ashBreakTreshold = 430f;
                    const float poplarBreakTreshold = 260f;
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
                                else if (damageType == DamageTypes.Blunt)
                                {
                                    //skillBasedDamage = magnitude + 0.50f * (40f + (effectiveSkill * 0.53f));
                                    skillBasedDamage = magnitude + (50f + (effectiveSkill * 0.3f)) * 0.5f;
                                }
                                else
                                {
                                    if (attackCollisionData.StrikeType == (int)StrikeType.Swing)
                                    {
                                        skillBasedDamage = magnitude * RBMConfig.RBMConfig.ThrustMagnitudeModifier + (40f * RBMConfig.RBMConfig.ThrustMagnitudeModifier + (effectiveSkill * 0.53f * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) * 1.3f;
                                    }
                                    else
                                    {
                                        skillBasedDamage = magnitude * 0.2f + 50f * RBMConfig.RBMConfig.ThrustMagnitudeModifier + (effectiveSkill * 0.46f * RBMConfig.RBMConfig.ThrustMagnitudeModifier);
                                    }
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
                                else if (damageType == DamageTypes.Blunt)
                                {
                                    //skillBasedDamage = magnitude * 1.3f + 0.5f * ((40f + (effectiveSkill * 0.53f)) * 1.3f);
                                    skillBasedDamage = magnitude + ((50f + (effectiveSkill * 0.3f)) * 1.3f) * 0.5f;
                                }
                                else
                                {
                                    if(attackCollisionData.StrikeType == (int)StrikeType.Swing)
                                    {
                                        skillBasedDamage = magnitude * RBMConfig.RBMConfig.ThrustMagnitudeModifier + (40f* RBMConfig.RBMConfig.ThrustMagnitudeModifier + (effectiveSkill * 0.53f * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) * 1.3f;
                                    }
                                    else
                                    {
                                        skillBasedDamage = (magnitude * 0.2f + 50f * RBMConfig.RBMConfig.ThrustMagnitudeModifier + (effectiveSkill * 0.46f * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) * 1.3f;
                                    }
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
                                if (damageType == DamageTypes.Blunt)
                                {
                                    //skillBasedDamage = magnitude + 0.5f * (60f + (effectiveSkill * 0.4f));
                                    skillBasedDamage = magnitude + (50f + (effectiveSkill * 0.3f)) * 0.4f;
                                }
                                if (magnitude > 1f)
                                {
                                    magnitude = skillBasedDamage;
                                }
                                break;
                            }
                        case "OneHandedBastardAxe":
                            {
                                skillBasedDamage = magnitude + (60f + (effectiveSkill * 0.4f)) * 1.15f;
                                if (damageType == DamageTypes.Blunt)
                                {
                                    //skillBasedDamage = magnitude * 1.15f + 0.5f * ((60f + (effectiveSkill * 0.4f)) * 1.15f);
                                    skillBasedDamage = magnitude + ((50f + (effectiveSkill * 0.3f)) * 1.15f) * 0.4f;
                                }
                                if (magnitude > 1f)
                                {
                                    magnitude = skillBasedDamage;
                                }
                                break;
                            }
                        case "TwoHandedAxe":
                            {
                                skillBasedDamage = magnitude + (60f + (effectiveSkill * 0.4f)) * 1.3f;
                                if (damageType == DamageTypes.Blunt)
                                {
                                    //skillBasedDamage = magnitude * 1.3f + 0.5f * ((60f + (effectiveSkill * 0.4f)) * 1.30f);
                                    skillBasedDamage = magnitude + ((50f + (effectiveSkill * 0.3f)) * 1.3f) * 0.4f;
                                }
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
                                    skillBasedDamage = magnitude * 0.2f + 40f * RBMConfig.RBMConfig.ThrustMagnitudeModifier + (effectiveSkill * 0.4f * RBMConfig.RBMConfig.ThrustMagnitudeModifier);
                                }
                                else
                                {
                                    skillBasedDamage = magnitude + 50f + (effectiveSkill * 0.3f);
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
                                    skillBasedDamage = (magnitude * 0.2f + 40f * RBMConfig.RBMConfig.ThrustMagnitudeModifier + (effectiveSkill * 0.4f * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) * 1.3f;
                                }
                                else
                                {
                                    skillBasedDamage = magnitude + ((50f + (effectiveSkill * 0.3f)) * 1.3f);
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
                                else if (damageType == DamageTypes.Blunt && !isPassiveUsage)
                                {
                                    //skillBasedDamage = magnitude + 30f + (effectiveSkill * 0.26f);
                                    skillBasedDamage = magnitude + (50f + (effectiveSkill * 0.3f)) * 0.35f;
                                }
                                else
                                {

                                    if (isPassiveUsage)
                                    {
                                        float couchedSkill = 0.5f + effectiveSkill * 0.02f;
                                        float skillCap = (150f + effectiveSkill * 1.5f);

                                        float weaponWeight = attacker.Equipment[attacker.GetWieldedItemIndex(HandIndex.MainHand)].GetWeight();

                                        if (weaponWeight < 2.1f)
                                        {
                                            BraceBonus += 0.5f;
                                            BraceModifier *= 3f;
                                        }
                                        float lanceBalistics = (magnitude * BraceModifier) / weaponWeight;
                                        float CouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus);
                                        float BluntLanceBalistics = ((magnitude * BraceModifier) / weaponWeight) * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                                        float BluntCouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus) * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                                        magnitude = CouchedMagnitude;

                                        if (damageType == DamageTypes.Blunt)
                                        {
                                            magnitude = BluntCouchedMagnitude;
                                            if (BluntCouchedMagnitude > skillCap && (BluntLanceBalistics * (weaponWeight + BraceBonus)) < skillCap) //skill based damage
                                            {
                                                magnitude = skillCap;
                                            }

                                            if ((BluntLanceBalistics * (weaponWeight + BraceBonus)) >= skillCap) //ballistics
                                            {
                                                magnitude = (BluntLanceBalistics * (weaponWeight + BraceBonus));
                                            }

                                            if (magnitude > poplarBreakTreshold) // damage cap - lance break threshold
                                            {
                                                magnitude = poplarBreakTreshold;
                                            }
                                            magnitude *= 1f;
                                        }

                                        else
                                        {
                                            if (CouchedMagnitude > (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier) && (lanceBalistics * (weaponWeight + BraceBonus)) < (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) //skill based damage
                                            {
                                                magnitude = skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                            }

                                            if ((lanceBalistics * (weaponWeight + BraceBonus)) >= (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) //ballistics
                                            {
                                                magnitude = (lanceBalistics * (weaponWeight + BraceBonus));
                                            }

                                            if (magnitude > (ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) // damage cap - lance break threshold
                                            {
                                                magnitude = ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        float weaponWeight = attacker.Equipment[attacker.GetWieldedItemIndex(HandIndex.MainHand)].GetWeight();

                                        if (weaponWeight > 2.1f)
                                        {
                                            magnitude *= 0.34f;
                                        }
                                        skillBasedDamage = magnitude * 0.4f + 60f * RBMConfig.RBMConfig.ThrustMagnitudeModifier + (effectiveSkill * 0.26f * RBMConfig.RBMConfig.ThrustMagnitudeModifier);
                                        if (skillBasedDamage > 260f * RBMConfig.RBMConfig.ThrustMagnitudeModifier)
                                        {
                                            skillBasedDamage = 260f * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                        }
                                    }
                                }
                                if (magnitude > 0.15f && !isPassiveUsage)
                                {
                                    magnitude = skillBasedDamage;
                                }
                                //else if(magnitude > 0f && magnitude <= 0.15f)
                                //{
                                //    InformationManager.DisplayMessage(new InformationMessage("DEBUG WARNING: strike bagnitude below treshlod"));
                                //}
                                break;
                            }
                        case "TwoHandedPolearm":
                            {
                                if (damageType == DamageTypes.Cut)
                                {
                                    skillBasedDamage = magnitude + (50f + (effectiveSkill * 0.46f)) * 1.3f;
                                }
                                else if (damageType == DamageTypes.Blunt && !isPassiveUsage)
                                {
                                    //skillBasedDamage = magnitude + (30f + (effectiveSkill * 0.26f) * 1.3f);
                                    skillBasedDamage = magnitude + ((50f + (effectiveSkill * 0.3f)) * 1.3f) * 0.4f;
                                }
                                else
                                {
                                    if (isPassiveUsage)
                                    {
                                        float couchedSkill = 0.5f + effectiveSkill * 0.02f;
                                        float skillCap = (150f + effectiveSkill * 1.5f);

                                        float weaponWeight = attacker.Equipment[attacker.GetWieldedItemIndex(HandIndex.MainHand)].GetWeight();

                                        if (weaponWeight < 2.1f)
                                        {
                                            BraceBonus += 0.5f;
                                            BraceModifier *= 3f;
                                        }
                                        float lanceBalistics = (magnitude * BraceModifier) / weaponWeight;
                                        float CouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus);
                                        float BluntLanceBalistics = ((magnitude * BraceModifier) / weaponWeight) * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                                        float BluntCouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus) * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                                        magnitude = CouchedMagnitude;

                                        if (damageType == DamageTypes.Blunt)
                                        {
                                            magnitude = BluntCouchedMagnitude;
                                            if (BluntCouchedMagnitude > skillCap && (BluntLanceBalistics * (weaponWeight + BraceBonus)) < skillCap) //skill based damage
                                            {
                                                magnitude = skillCap;
                                            }

                                            if ((BluntLanceBalistics * (weaponWeight + BraceBonus)) >= skillCap) //ballistics
                                            {
                                                magnitude = (BluntLanceBalistics * (weaponWeight + BraceBonus));
                                            }

                                            if (magnitude > poplarBreakTreshold) // damage cap - lance break threshold
                                            {
                                                magnitude = poplarBreakTreshold;
                                            }
                                            magnitude *= 1f;
                                        }

                                        else
                                        {
                                            if (CouchedMagnitude > (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier) && (lanceBalistics * (weaponWeight + BraceBonus)) < (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) //skill based damage
                                            {
                                                magnitude = skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                            }

                                            if ((lanceBalistics * (weaponWeight + BraceBonus)) >= (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) //ballistics
                                            {
                                                magnitude = (lanceBalistics * (weaponWeight + BraceBonus));
                                            }

                                            if (magnitude > (ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) // damage cap - lance break threshold
                                            {
                                                magnitude = ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        float weaponWeight = attacker.Equipment[attacker.GetWieldedItemIndex(HandIndex.MainHand)].GetWeight();

                                        if (weaponWeight > 2.1f)
                                        {
                                            magnitude *= 0.34f;
                                        }
                                        skillBasedDamage = (magnitude * 0.4f + 60f * RBMConfig.RBMConfig.ThrustMagnitudeModifier + (effectiveSkill * 0.26f * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) * 1.3f;
                                        if (skillBasedDamage > 360f * RBMConfig.RBMConfig.ThrustMagnitudeModifier)
                                        {
                                            skillBasedDamage = 360f * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                        }
                                    }
                                }
                                if (magnitude > 0.15f && !isPassiveUsage)
                                {
                                    magnitude = skillBasedDamage;
                                }
                                //else if (magnitude > 0f && magnitude <= 0.15f)
                                //{
                                //    InformationManager.DisplayMessage(new InformationMessage("DEBUG WARNING: strike bagnitude below treshlod"));
                                //}
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

            //if (victim != null)
            //{
            //    Utilities.initiateCheckForArmor(ref victim, ref attackInformation, ref attackCollisionData);
            //    Utilities.numOfHits++;
            //}

            int absoluteDamage = MBMath.ClampInt((int)(MyComputeDamage(weaponType, damageType, magnitude, 0f, victimAgentAbsorbedDamageRatio) * dmgMultiplier), 0, 2000);
            absorbedByArmor = absoluteDamage - inflictedDamage;

            return false;
        }

        public static void RBMComputeBlowDamageOnShield(ref AttackInformation attackInformation, ref AttackCollisionData attackCollisionData, WeaponComponentData attackerWeapon, float blowMagnitude, out int inflictedDamage)
        {
            float localInflictedDamage = 0;
            attackCollisionData.InflictedDamage = 0;
                
            MissionWeapon victimShield = attackInformation.VictimShield;
            if (victimShield.IsEmpty)
            {
                Agent victim = null;
                foreach (Agent agent in Mission.Current.Agents)
                {
                    if (attackInformation.VictimAgentOrigin == agent.Origin)
                    {
                        victim = agent;
                    }
                }
                for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                {
                    if (victim != null && victim.Equipment != null && !victim.Equipment[equipmentIndex].IsEmpty)
                    {
                        if (victim.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Shield)
                        {
                            attackInformation.VictimShield = victim.Equipment[equipmentIndex];
                        }
                    }
                }
            }
            victimShield = attackInformation.VictimShield;
            if (!victimShield.IsEmpty && victimShield.CurrentUsageItem.WeaponFlags.HasAnyFlag(WeaponFlags.CanBlockRanged) & attackInformation.CanGiveDamageToAgentShield)
            {
                DamageTypes damageType = (DamageTypes)attackCollisionData.DamageType;
                int shieldArmorForCurrentUsage = victimShield.GetGetModifiedArmorForCurrentUsage();
                float absorbedDamageRatio = 1f;

                string weaponType = "otherDamage";
                if (attackerWeapon != null)
                {
                    weaponType = attackerWeapon.WeaponClass.ToString();
                }

                bool isPassiveUsage = attackInformation.IsAttackerAgentDoingPassiveAttack;

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
                            if (blowMagnitude > 1f)
                            {
                                if (damageType == DamageTypes.Cut)
                                {
                                    blowMagnitude = blowMagnitude + 40f;
                                }
                                else
                                {
                                    blowMagnitude = blowMagnitude * 0.2f + 50f * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
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
                                    blowMagnitude = (blowMagnitude * 0.2f + 50f * RBMConfig.RBMConfig.ThrustMagnitudeModifier) * 1.3f;
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
                                    blowMagnitude = blowMagnitude * 0.2f + 40f * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
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
                                    blowMagnitude = (blowMagnitude * 0.2f + 40f * RBMConfig.RBMConfig.ThrustMagnitudeModifier) * 1.3f;
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
                                        if (CouchedMagnitude > (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier) && (lanceBalistics * (weaponWeight + BraceBonus)) < (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) //skill based damage
                                        {
                                            blowMagnitude = skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                        }

                                        if ((lanceBalistics * (weaponWeight + BraceBonus)) >= (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) //ballistics
                                        {
                                            blowMagnitude = (lanceBalistics * (weaponWeight + BraceBonus));
                                        }

                                        if (blowMagnitude > (ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) // damage cap - lance break threshold
                                        {
                                            blowMagnitude = ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                        }
                                    }
                                    else
                                    {
                                        //float weaponWeight = attackerWeapon.Item.Weight;

                                        //if (weaponWeight > 2.1f)
                                        //{
                                        //    blowMagnitude *= 0.34f;
                                        //}
                                        blowMagnitude = blowMagnitude * 0.4f + 60f * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                        if (blowMagnitude > 260f * RBMConfig.RBMConfig.ThrustMagnitudeModifier)
                                        {
                                            blowMagnitude = 260f * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
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
                                        if (CouchedMagnitude > (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier) && (lanceBalistics * (weaponWeight + BraceBonus)) < (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier))
                                        {
                                            blowMagnitude = skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                        }

                                        if ((lanceBalistics * (weaponWeight + BraceBonus)) >= (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier))
                                        {
                                            blowMagnitude = (lanceBalistics * (weaponWeight + BraceBonus));
                                        }

                                        if (blowMagnitude > (ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier))
                                        {
                                            blowMagnitude = ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                        }
                                    }
                                    else
                                    {
                                        float weaponWeight = 1.5f;

                                        if (weaponWeight > 2.1f)
                                        {
                                            blowMagnitude *= 0.34f;
                                        }
                                        skillBasedDamage = (blowMagnitude * 0.4f + 60f * RBMConfig.RBMConfig.ThrustMagnitudeModifier) * 1.3f;
                                        if (skillBasedDamage > 360f * RBMConfig.RBMConfig.ThrustMagnitudeModifier)
                                        {
                                            skillBasedDamage = 360f * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                        }
                                    }
                                }
                            }
                            break;
                        }
                }

                localInflictedDamage = MyComputeDamage(weaponType, damageType, blowMagnitude, (float)shieldArmorForCurrentUsage * 10f, absorbedDamageRatio);

                if (attackCollisionData.IsMissile)
                {
                    switch (weaponType)
                    {
                        case "Arrow":
                            {
                                localInflictedDamage *= 1.5f;
                                break;
                            }
                        case "Bolt":
                            {
                                localInflictedDamage *= 1.5f;
                                break;
                            }
                        case "Javelin":
                            {
                                localInflictedDamage *= 20f;
                                break;
                            }
                        case "ThrowingAxe":
                            {
                                localInflictedDamage *= 3f;
                                break;
                            }
                        case "OneHandedPolearm":
                            {
                                localInflictedDamage *= 5f;
                                break;
                            }
                        case "LowGripPolearm":
                            {
                                localInflictedDamage *= 5f;
                                break;
                            }
                        default:
                            {
                                localInflictedDamage *= 0.1f;
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
                                    localInflictedDamage *= 0.09f;
                                    break;
                                }
                                localInflictedDamage *= 1.5f;

                                break;
                            }
                        default:
                            {
                                if (attackCollisionData.DamageType == 0) //cut
                                {
                                    localInflictedDamage *= 1f;
                                }
                                else if (attackCollisionData.DamageType == 1)//pierce
                                {
                                    localInflictedDamage *= 0.09f;
                                }
                                else if (attackCollisionData.DamageType == 2)//blunt
                                {
                                    localInflictedDamage *= 0.75f;
                                }
                                break;
                            }
                    }
                }

                if (attackerWeapon != null && attackerWeapon.WeaponFlags.HasAnyFlag(WeaponFlags.BonusAgainstShield))
                {
                    localInflictedDamage *= 2f;
                }

                if (localInflictedDamage > 0f)
                {
                    if (!attackInformation.IsVictimAgentLeftStance)
                    {
                        localInflictedDamage *= TaleWorlds.Core.ManagedParameters.Instance.GetManagedParameter(TaleWorlds.Core.ManagedParametersEnum.ShieldRightStanceBlockDamageMultiplier);
                    }
                    if (attackCollisionData.CorrectSideShieldBlock)
                    {
                        localInflictedDamage *= TaleWorlds.Core.ManagedParameters.Instance.GetManagedParameter(TaleWorlds.Core.ManagedParametersEnum.ShieldCorrectSideBlockDamageMultiplier);
                    }
                    localInflictedDamage = MissionGameModels.Current.AgentApplyDamageModel.CalculateShieldDamage(attackInformation, localInflictedDamage);
                }
            }
            attackCollisionData.InflictedDamage = (int)localInflictedDamage;
            inflictedDamage = MathF.Floor(localInflictedDamage);
        }

        [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
        [HarmonyPatch("ComputeBlowDamageOnShield")]
        class OverrideDamageCalcShield
        {
            static bool Prefix(ref AttackInformation attackInformation, ref AttackCollisionData attackCollisionData, WeaponComponentData attackerWeapon, float blowMagnitude, out int inflictedDamage)
            {
                RBMComputeBlowDamageOnShield(ref attackInformation, ref attackCollisionData, attackerWeapon, blowMagnitude, out inflictedDamage);
                return false;
            }
        }

        public static float MyComputeDamage(string weaponType, DamageTypes damageType, float magnitude, float armorEffectiveness, float absorbedDamageRatio, BasicCharacterObject player = null, bool isPlayerVictim = false)
        {

            float damage = 0f;
            float armorReduction = 100f / (100f + armorEffectiveness * RBMConfig.RBMConfig.armorMultiplier);
            float mag_1h_thrust;
            float mag_2h_thrust;
            float mag_1h_sword_thrust;
            float mag_2h_sword_thrust;

            if (damageType == DamageTypes.Pierce)
            {
                mag_1h_thrust = magnitude * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                mag_2h_thrust = magnitude * 1f * RBMConfig.RBMConfig.TwoHandedThrustDamageBonus;
                mag_1h_sword_thrust = magnitude * 1.0f * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                mag_2h_sword_thrust = magnitude * 1f * RBMConfig.RBMConfig.TwoHandedThrustDamageBonus;
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
                        damage = weaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_sword_thrust, armorReduction, damageType, armorEffectiveness,player, isPlayerVictim);
                        break;
                    }
                case "ThrowingKnife":
                    {
                        damage = weaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_sword_thrust, armorReduction, damageType, armorEffectiveness,player, isPlayerVictim);
                        break;
                    }
                case "OneHandedSword":
                    {
                        damage = weaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_sword_thrust, armorReduction, damageType, armorEffectiveness,player, isPlayerVictim);
                        break;
                    }
                case "TwoHandedSword":
                    {
                        damage = weaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_2h_sword_thrust, armorReduction, damageType, armorEffectiveness,player, isPlayerVictim);
                        break;
                    }
                case "OneHandedAxe":
                    {
                        damage = weaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness,player, isPlayerVictim);
                        break;
                    }
                case "OneHandedBastardAxe":
                    {
                        damage = weaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness,player, isPlayerVictim);
                        break;
                    }
                case "TwoHandedAxe":
                    {
                        damage = weaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness,player, isPlayerVictim);
                        break;
                    }
                case "OneHandedPolearm":
                    {
                        damage = weaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_thrust, armorReduction, damageType, armorEffectiveness,player, isPlayerVictim);
                        break;
                    }
                case "TwoHandedPolearm":
                    {
                        damage = weaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_2h_thrust, armorReduction, damageType, armorEffectiveness,player, isPlayerVictim);
                        break;
                    }
                case "Mace":
                    {
                        damage = weaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_thrust, armorReduction, damageType, armorEffectiveness,player, isPlayerVictim, 0f);
                        break;
                    }
                case "TwoHandedMace":
                    {
                        damage = weaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_2h_thrust, armorReduction, damageType, armorEffectiveness,player, isPlayerVictim);
                        break;
                    }
                case "Arrow":
                    {
                        damage = weaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness,player, isPlayerVictim, 0f);
                        break;
                    }
                case "Bolt":
                    {
                        damage = weaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness,player, isPlayerVictim, 0f);
                        break;
                    }
                case "Javelin":
                    {
                        damage = weaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_thrust, armorReduction, damageType, armorEffectiveness,player, isPlayerVictim);
                        break;
                    }
                case "ThrowingAxe":
                    {
                        damage = weaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim);
                        break;
                    }
                default:
                    {
                        //InformationManager.DisplayMessage(new InformationMessage("POZOR DEFAULT !!!!"));
                        RBMCombatConfigWeaponType defaultwct = new RBMCombatConfigWeaponType("default", 1f, 1f, 1f, 1f, 1f, 1f);
                        damage = weaponTypeDamage(defaultwct, magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim);
                        break;
                    }
            }

            return damage * absorbedDamageRatio;
        }

        private static float weaponTypeDamage(RBMCombatConfigWeaponType weaponTypeFactors, float magnitude, float armorReduction, DamageTypes damageType, float armorEffectiveness, BasicCharacterObject player, bool isPlayerVictim, float partialPenetrationThreshold = 2f)
        {
            float damage = 0f;
            switch (damageType)
            {
                case DamageTypes.Blunt:
                    {
                        //float armorReductionBlunt = 100f / ((100f + armorEffectiveness) * RBMConfig.RBMConfig.dict["Global.ArmorMultiplier"]);
                        //damage += magnitude * armorReductionBlunt * RBMConfig.RBMConfig.dict["Global.MaceBluntModifier"];

                        float penetratedDamage = Math.Max(0f, magnitude - armorEffectiveness * 5f * RBMConfig.RBMConfig.armorThresholdModifier);
                        float bluntFraction = 0f;
                        if (magnitude > 0f)
                        {
                            bluntFraction = (magnitude - penetratedDamage) / magnitude;
                        }
                        damage += penetratedDamage;

                        float bluntTrauma = magnitude * (0.5f * RBMConfig.RBMConfig.maceBluntModifier) * bluntFraction;
                        float bluntTraumaAfterArmor = Math.Max(0f, bluntTrauma * armorReduction);
                        damage += bluntTraumaAfterArmor;

                        break;
                    }
                case DamageTypes.Cut:
                    {
                        float penetratedDamage = Math.Max(0f, magnitude - armorEffectiveness * weaponTypeFactors.ExtraArmorThresholdFactorCut * RBMConfig.RBMConfig.armorThresholdModifier);
                        float bluntFraction = 0f;
                        if (magnitude > 0f)
                        {
                            bluntFraction = (magnitude - penetratedDamage) / magnitude;
                        }
                        damage += penetratedDamage;

                        float bluntTrauma = magnitude * (weaponTypeFactors.ExtraBluntFactorCut + RBMConfig.RBMConfig.bluntTraumaBonus) * bluntFraction;
                        float bluntTraumaAfterArmor = Math.Max(0f, bluntTrauma * armorReduction);
                        damage += bluntTraumaAfterArmor;

                        if (RBMConfig.RBMConfig.armorPenetrationMessage)
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
                        float partialPenetration = Math.Max(0f, magnitude - armorEffectiveness * partialPenetrationThreshold * RBMConfig.RBMConfig.armorThresholdModifier);
                        if (partialPenetration > 15f)
                        {
                            partialPenetration = 15f;
                        }
                        float penetratedDamage = Math.Max(0f, magnitude - armorEffectiveness * weaponTypeFactors.ExtraArmorThresholdFactorPierce * RBMConfig.RBMConfig.armorThresholdModifier) - partialPenetration;
                        float bluntFraction = 0f;
                        if (magnitude > 0f)
                        {
                            bluntFraction = (magnitude - (penetratedDamage + partialPenetration)) / magnitude;
                        }
                        damage += penetratedDamage + partialPenetration;

                        float bluntTrauma = magnitude * (weaponTypeFactors.ExtraBluntFactorPierce + RBMConfig.RBMConfig.bluntTraumaBonus) * bluntFraction;
                        float bluntTraumaAfterArmor = Math.Max(0f, bluntTrauma * armorReduction);
                        damage += bluntTraumaAfterArmor;

                        if (RBMConfig.RBMConfig.armorPenetrationMessage)
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

    [HarmonyPatch(typeof(SandboxAgentStatCalculateModel))]
    [HarmonyPatch("UpdateHumanStats")]
    class SandboxAgentUpdateHumanStats
    {
        static void Postfix(Agent agent, ref AgentDrivenProperties agentDrivenProperties)
        {
            agentDrivenProperties.AttributeShieldMissileCollisionBodySizeAdder = 0.01f;
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
            if (attackerWeapon.Item != null && attackerWeapon.CurrentUsageItem != null)
            {
                weaponType = attackerWeapon.CurrentUsageItem.WeaponClass.ToString();
            }

            if ((attackerAgent.IsDoingPassiveAttack && collisionData.CollisionResult == CombatCollisionResult.StrikeAgent))
            {
                if (attackerAgent.Team != victimAgent.Team)
                {
                    __result.BlowFlag |= BlowFlags.KnockDown;
                    return;
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
                    victimAgent.RegisterBlow(__result, collisionData);
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
                                __result.DefenderStunPeriod = collisionData.DefenderStunPeriod * 0.5f;
                                __result.BlowFlag = BlowFlags.None;
                                __result.Position = collisionData.CollisionGlobalPosition;
                                __result.BoneIndex = collisionData.CollisionBoneIndex;
                                __result.Direction = blowDirection;
                                __result.SwingDirection = swingDirection;
                                //__result.InflictedDamage = 1;
                                __result.VictimBodyPart = collisionData.VictimHitBodyPart;
                                __result.BlowFlag |= BlowFlags.NonTipThrust;
                                victimAgent.RegisterBlow(__result, collisionData);
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
        static bool Prefix(ref Mission __instance, ref Agent attacker, ref Agent victim, GameEntity realHitEntity, ref Blow b, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon, ref CombatLogData combatLogData)
        {
            if (victim != null && victim.IsMount && collisionData.IsMissile)
            {
                if (MissionGameModels.Current.AgentApplyDamageModel.DecideMountRearedByBlow(attacker, victim, in collisionData, attackerWeapon.CurrentUsageItem, in b))
                {
                    b.BlowFlag |= BlowFlags.MakesRear;
                }
            }
            if (attacker != null && attacker.IsMount && collisionData.IsHorseCharge)
            {
                float horseBodyPartArmor = attacker.GetBaseArmorEffectivenessForBodyPart(BoneBodyPartType.Chest);
                //b.SelfInflictedDamage = MathF.Ceiling(b.BaseMagnitude / 6f);
                b.SelfInflictedDamage = MBMath.ClampInt(MathF.Ceiling(Game.Current.BasicModels.StrikeMagnitudeModel.ComputeRawDamage(DamageTypes.Blunt, b.BaseMagnitude / 6f, horseBodyPartArmor, 1f)), 0, 2000);
                attacker.CreateBlowFromBlowAsReflection(in b, in collisionData, out var outBlow, out var outCollisionData);
                attacker.RegisterBlow(outBlow, in outCollisionData);
            }
            //if(victim != null && collisionData.CollidedWithShieldOnBack && collisionData.IsMissile)
            //{
            //    for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
            //    {
            //        if (victim.Equipment != null && !victim.Equipment[equipmentIndex].IsEmpty)
            //        {
            //            if (victim.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Shield)
            //            {
            //                int num = MathF.Max(0, victim.Equipment[equipmentIndex].HitPoints - b.InflictedDamage);
            //                victim.ChangeWeaponHitPoints(equipmentIndex, (short)num);
            //                if (num == 0)
            //                {
            //                    victim.RemoveEquippedWeapon(equipmentIndex);
            //                }
            //                break;
            //            }
            //        }
            //    }
            //}
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
        static bool Prefix(Agent affectedAgent, Agent affectorAgent, in MissionWeapon attackerWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
        {
            if (affectedAgent.Character != null && affectorAgent != null && affectorAgent.Character != null && affectedAgent.State == AgentState.Active)
            {
                bool isFatal = affectedAgent.Health - (float)blow.InflictedDamage < 1f;
                bool isTeamKill;
                if (affectedAgent.Team != null)
                {
                    isTeamKill = affectedAgent.Team.Side == affectorAgent.Team.Side;
                }
                else
                {
                    isTeamKill = true;
                }
                affectorAgent.Origin.OnScoreHit(affectedAgent.Character, affectorAgent.Formation?.Captain?.Character, blow.InflictedDamage, isFatal, isTeamKill, attackerWeapon.CurrentUsageItem);
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
        private static ArmorComponent.ArmorMaterialTypes GetProtectorArmorMaterialOfBone(Agent agent, sbyte boneIndex)
        {
            if (agent != null && agent.SpawnEquipment != null)
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
                        if (agent.SpawnEquipment[equipmentIndex].Item.ArmorComponent != null)
                        {
                            return agent.SpawnEquipment[equipmentIndex].Item.ArmorComponent.MaterialType;
                        }
                    }
                }
            }
            return ArmorComponent.ArmorMaterialTypes.None;
        }
        static bool Prefix(ref Agent __instance, ref Blow b, AgentLastHitInfo ____lastHitInfo, in AttackCollisionData collisionData)
        {
            b.BaseMagnitude = Math.Min(b.BaseMagnitude, 1000f) / 8f;
            Agent agent = (b.OwnerId != -1) ? __instance.Mission.FindAgentWithIndex(b.OwnerId) : __instance;
            if (!b.BlowFlag.HasAnyFlag(BlowFlags.NoSound))
            {
                bool isCriticalBlow = b.IsBlowCrit(__instance.Monster.HitPoints * 4);
                bool isLowBlow = b.IsBlowLow(__instance.Monster.HitPoints);
                bool isOwnerHumanoid = agent?.IsHuman ?? false;
                bool isNonTipThrust = b.BlowFlag.HasAnyFlag(BlowFlags.NonTipThrust);
                int hitSound = b.WeaponRecord.GetHitSound(isOwnerHumanoid, isCriticalBlow, isLowBlow, isNonTipThrust, b.AttackType, b.DamageType);
                float soundParameterForArmorType = 0.1f * (float)GetProtectorArmorMaterialOfBone(__instance, b.BoneIndex);
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

            float health = __instance.Health;
            float damagedHp = (((float)b.InflictedDamage > health) ? health : ((float)b.InflictedDamage));
            float newHp = health - damagedHp;

            if (b.AttackType == AgentAttackType.Bash || b.AttackType == AgentAttackType.Kick)
            {
                if (b.InflictedDamage <= 0)
                {
                    b.InflictedDamage = 1;
                }
            }
            if (b.InflictedDamage == 0 && isKnockBack)
            {
                b.InflictedDamage = 1;
            }
            if (b.InflictedDamage == 1 && isKnockBack)
            {

            }
            else
            {
                if (newHp < 0f)
                {
                    newHp = 0f;
                }
                if (__instance.CurrentMortalityState != MortalityState.Immortal && !Mission.Current.DisableDying)
                {
                    __instance.Health = newHp;
                }
            }

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
            bool isBlocked = false;
            if (collisionData.CollisionResult == CombatCollisionResult.Blocked || collisionData.CollisionResult == CombatCollisionResult.ChamberBlocked || collisionData.CollisionResult == CombatCollisionResult.Parried)
            {
                isBlocked = true;
            }

            if (__instance != null && damagedHp > 1)
            {
                Utilities.initiateCheckForArmor(ref __instance, collisionData);
                Utilities.numOfHits++;
            }

            method3.Invoke(__instance.Mission, new object[] { __instance, agent, b, collisionData, isBlocked, damagedHp });
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

    //[HarmonyPatch(typeof(Mission))]
    //[HarmonyPatch("DecideAgentHitParticles")]
    //class DecideAgentHitParticlesPatch
    //{
    //    [EngineStruct("Hit_particle_result_data")]
    //    internal struct HitParticleResultData
    //    {
    //        public int StartHitParticleIndex;

    //        public int ContinueHitParticleIndex;

    //        public int EndHitParticleIndex;

    //        public void Reset()
    //        {
    //            StartHitParticleIndex = -1;
    //            ContinueHitParticleIndex = -1;
    //            EndHitParticleIndex = -1;
    //        }
    //    }
    //    static void Postfix(Blow blow, Agent victim, ref AttackCollisionData collisionData, ref HitParticleResultData hprd)
    //    {
    //        if (victim != null && (blow.InflictedDamage > 0 || victim.Health <= 0f))
    //        {
    //            if (!blow.WeaponRecord.HasWeapon() || blow.WeaponRecord.WeaponFlags.HasAnyFlag(WeaponFlags.NoBlood) || collisionData.IsAlternativeAttack || blow.InflictedDamage <= 20)
    //            {
    //                hprd.StartHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_sweat_sword_enter");
    //                hprd.ContinueHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_sweat_sword_enter");
    //                hprd.EndHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_sweat_sword_enter");
    //            }
    //            else
    //            {
    //                hprd.StartHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_blood_sword_enter");
    //                hprd.ContinueHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_blood_sword_inside");
    //                hprd.EndHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_blood_sword_exit");
    //            }
    //        }
    //    }
    //}
}

