using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using HarmonyLib;
using TaleWorlds.Library;

namespace RBMCombat
{
    public class MagnitudeChanges
    {
        [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
        [HarmonyPatch("CalculateBaseMeleeBlowMagnitude")]
        public class CalculateBaseMeleeBlowMagnitudePatch
        {
            const float oneHandedPolearmThrustStrength = 2.5f;
            const float twoHandedPolearmThrustStrength = 5f;

            public static float CalculateThrustMagnitudeForOneHandedWeapon(float weaponWeight, float effectiveSkill, float thrustSpeed, float exraLinearSpeed , Agent attackerAgent)
            {
                float magnitude = 0f;

                bool isOverheadAttack = attackerAgent.AttackDirection == Agent.UsageDirection.AttackUp;
                
                thrustSpeed = (isOverheadAttack ? thrustSpeed * 1.33f : thrustSpeed);
                float combinedSpeed = thrustSpeed + exraLinearSpeed;
                float skillModifier = Utilities.CalculateSkillModifier(effectiveSkill) * 2f;

                float spearKineticEnergy = 0.5f * weaponWeight * (combinedSpeed * combinedSpeed);

                float armStrength = isOverheadAttack ? oneHandedPolearmThrustStrength - 1f : oneHandedPolearmThrustStrength;

                float thrustStrength = weaponWeight + (armStrength * (1f + skillModifier));
                float thrustStrengthWithWeaponWeight = weaponWeight + (armStrength * (1f + skillModifier));

                float thrustEnergyCap = MathF.Clamp(0.5f * thrustStrength * (thrustSpeed * thrustSpeed) * 1.5f, 0f, 180f);
                float thrustEnergy = 0.5f * thrustStrengthWithWeaponWeight * (combinedSpeed * combinedSpeed);
                if(thrustEnergy > thrustEnergyCap)
                {
                    thrustEnergy = thrustEnergyCap;
                }

                magnitude = thrustEnergy;

                if (spearKineticEnergy > magnitude)
                {
                    magnitude = spearKineticEnergy;
                }

                if (magnitude > thrustEnergyCap)
                {
                    magnitude = thrustEnergyCap;
                }

                return magnitude * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
            }

            public static float CalculateThrustMagnitudeForTwoHandedWeapon(float weaponWeight, float effectiveSkill, float thrustSpeed, float exraLinearSpeed, Agent attackerAgent)
            {
                float magnitude = 0f;

                bool isOverheadAttack = attackerAgent.AttackDirection == Agent.UsageDirection.AttackUp;
                thrustSpeed = (isOverheadAttack ? thrustSpeed + 1f : thrustSpeed);
                float combinedSpeed = thrustSpeed + exraLinearSpeed;
                float skillModifier = Utilities.CalculateSkillModifier(effectiveSkill) * 2f;

                float spearKineticEnergy = 0.5f * weaponWeight * (combinedSpeed * combinedSpeed);

                float armStrength = isOverheadAttack ? twoHandedPolearmThrustStrength - 1f : twoHandedPolearmThrustStrength;

                float thrustStrength = armStrength * (1f + skillModifier);
                float thrustStrengthWithWeaponWeight = weaponWeight + (armStrength * (1f + skillModifier));

                float thrustEnergyCap = MathF.Clamp(0.5f * thrustStrength * (thrustSpeed * thrustSpeed) * 1.5f, 0f, 250f);

                float thrustEnergy = 0.5f * thrustStrengthWithWeaponWeight * (combinedSpeed * combinedSpeed);
                if (thrustEnergy > thrustEnergyCap)
                {
                    thrustEnergy = thrustEnergyCap;
                }

                magnitude = thrustEnergy;

                if (spearKineticEnergy > magnitude)
                {
                    magnitude = spearKineticEnergy;
                }

                if (magnitude > thrustEnergyCap)
                {
                    magnitude = thrustEnergyCap;
                }

                return magnitude * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
            }

            public static bool Prefix(ref float __result, in AttackInformation attackInformation, in MissionWeapon weapon, StrikeType strikeType, float progressEffect, float impactPointAsPercent, float exraLinearSpeed, bool doesAttackerHaveMount)
            {
                WeaponComponentData currentUsageItem = weapon.CurrentUsageItem;
                BasicCharacterObject attackerAgentCharacter = attackInformation.AttackerAgentCharacter;
                BasicCharacterObject attackerCaptainCharacter = attackInformation.AttackerCaptainCharacter;
                float num = MathF.Sqrt(progressEffect);

                if (strikeType == StrikeType.Thrust)
                {
                    exraLinearSpeed *= 1f;
                    float thrustWeaponSpeed = (float)weapon.GetModifiedThrustSpeedForCurrentUsage() / 11.7647057f * num;

                    if (weapon.Item != null && weapon.CurrentUsageItem != null)
                    {

                        Agent attacker = null;
                        foreach (Agent agent in Mission.Current.Agents)
                        {
                            if (attackInformation.AttackerAgentOrigin == agent.Origin)
                            {
                                attacker = agent;
                            }
                        }

                        if (attacker != null)
                        {
                            SkillObject skill = weapon.CurrentUsageItem.RelevantSkill;
                            int ef = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attackerAgentCharacter, attackInformation.AttackerAgentOrigin, attacker.Formation, skill);
                            float effectiveSkillDR = Utilities.GetEffectiveSkillWithDR(ef);
                            switch (weapon.CurrentUsageItem.WeaponClass)
                            {
                                case WeaponClass.LowGripPolearm:
                                case WeaponClass.Mace:
                                case WeaponClass.OneHandedAxe:
                                case WeaponClass.OneHandedPolearm:
                                case WeaponClass.TwoHandedMace:
                                    {
                                        float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                                        float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);
                                        float handlingskillModifier = 1f + (effectiveSkillDR / 700f);

                                        thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weapon.Item.Weight, weapon.CurrentUsageItem.Inertia, weapon.CurrentUsageItem.CenterOfMass);
                                        thrustWeaponSpeed = thrustWeaponSpeed * 0.75f * thrustskillModifier * num;
                                        break;
                                    }
                                case WeaponClass.TwoHandedPolearm:
                                    {
                                        float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                                        float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);
                                        float handlingskillModifier = 1f + (effectiveSkillDR / 700f);

                                        thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weapon.Item.Weight, weapon.CurrentUsageItem.Inertia, weapon.CurrentUsageItem.CenterOfMass);
                                        thrustWeaponSpeed = thrustWeaponSpeed * 0.7f * thrustskillModifier * num;
                                        break;
                                    }
                                case WeaponClass.TwoHandedAxe:
                                    {
                                        float swingskillModifier = 1f + (effectiveSkillDR / 800f);
                                        float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);
                                        float handlingskillModifier = 1f + (effectiveSkillDR / 700f);

                                        thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weapon.Item.Weight, weapon.CurrentUsageItem.Inertia, weapon.CurrentUsageItem.CenterOfMass);
                                        thrustWeaponSpeed = thrustWeaponSpeed * 0.9f * thrustskillModifier * num;
                                        break;
                                    }
                                case WeaponClass.OneHandedSword:
                                case WeaponClass.Dagger:
                                case WeaponClass.TwoHandedSword:
                                    {
                                        float swingskillModifier = 1f + (effectiveSkillDR / 800f);
                                        float thrustskillModifier = 1f + (effectiveSkillDR / 800f);
                                        float handlingskillModifier = 1f + (effectiveSkillDR / 800f);

                                        thrustWeaponSpeed = Utilities.CalculateThrustSpeed(weapon.Item.Weight, weapon.CurrentUsageItem.Inertia, weapon.CurrentUsageItem.CenterOfMass);
                                        thrustWeaponSpeed = thrustWeaponSpeed * 0.7f * thrustskillModifier * num;
                                        break;
                                    }
                            }

                            float thrustMagnitude = 0f;
                            switch (weapon.CurrentUsageItem.WeaponClass)
                            {
                                case WeaponClass.OneHandedPolearm:
                                case WeaponClass.OneHandedSword:
                                case WeaponClass.Dagger:
                                    {
                                        thrustMagnitude = CalculateThrustMagnitudeForOneHandedWeapon(weapon.Item.Weight, effectiveSkillDR, thrustWeaponSpeed, exraLinearSpeed, attacker);
                                        break;
                                    }
                                case WeaponClass.TwoHandedPolearm:
                                case WeaponClass.TwoHandedSword:
                                    {
                                        thrustMagnitude = CalculateThrustMagnitudeForTwoHandedWeapon(weapon.Item.Weight, effectiveSkillDR, thrustWeaponSpeed, exraLinearSpeed, attacker);
                                        break;
                                    }
                                default:
                                    {
                                        thrustMagnitude = Game.Current.BasicModels.StrikeMagnitudeModel.CalculateStrikeMagnitudeForThrust(attackerAgentCharacter, attackerCaptainCharacter, thrustWeaponSpeed, weapon.Item.Weight, currentUsageItem, exraLinearSpeed, doesAttackerHaveMount);
                                        break;
                                    }
                            }

                            __result = thrustMagnitude;
                            return false;
                        }
                    }

                    __result = Game.Current.BasicModels.StrikeMagnitudeModel.CalculateStrikeMagnitudeForThrust(attackerAgentCharacter, attackerCaptainCharacter, thrustWeaponSpeed, weapon.Item.Weight, currentUsageItem, exraLinearSpeed, doesAttackerHaveMount);
                    return false;
                }
                exraLinearSpeed *= 1f;
                float swingSpeed = (float)weapon.GetModifiedSwingSpeedForCurrentUsage() / 4.5454545f * num;

                if (weapon.Item != null && weapon.CurrentUsageItem != null)
                {
                    Agent attacker = null;
                    foreach (Agent agent in Mission.Current.Agents)
                    {
                        if (attackInformation.AttackerAgentOrigin == agent.Origin)
                        {
                            attacker = agent;
                        }
                    }
                    SkillObject skill = weapon.CurrentUsageItem.RelevantSkill;
                    int ef = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(attackerAgentCharacter, attackInformation.AttackerAgentOrigin, attacker.Formation, skill);
                    float effectiveSkillDR = Utilities.GetEffectiveSkillWithDR(ef);
                    switch (weapon.CurrentUsageItem.WeaponClass)
                    {
                        case WeaponClass.LowGripPolearm:
                        case WeaponClass.Mace:
                        case WeaponClass.OneHandedAxe:
                        case WeaponClass.OneHandedPolearm:
                        case WeaponClass.TwoHandedMace:
                            {
                                float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                                swingSpeed = swingSpeed * 0.83f * swingskillModifier * num;
                                break;
                            }
                        case WeaponClass.TwoHandedPolearm:
                            {
                                float swingskillModifier = 1f + (effectiveSkillDR / 1000f);

                                swingSpeed = swingSpeed * 0.83f * swingskillModifier * num;
                                break;
                            }
                        case WeaponClass.TwoHandedAxe:
                            {
                                float swingskillModifier = 1f + (effectiveSkillDR / 800f);

                                swingSpeed = swingSpeed * 0.75f * swingskillModifier * num;
                                break;
                            }
                        case WeaponClass.OneHandedSword:
                        case WeaponClass.Dagger:
                        case WeaponClass.TwoHandedSword:
                            {
                                float swingskillModifier = 1f + (effectiveSkillDR / 800f);

                                swingSpeed = swingSpeed * 0.9f * swingskillModifier * num;
                                break;
                            }
                    }
                }

                float num2 = MBMath.ClampFloat(0.4f / currentUsageItem.GetRealWeaponLength(), 0f, 1f);
                float num3 = MathF.Min(0.93f, impactPointAsPercent);
                float num4 = MathF.Min(0.93f, impactPointAsPercent + num2);
                //float originalValue = 0f;
                float newValue = 0f;
                int j = 0;
                //for (int i = 0; i < 5; i++)
                //{
                //    //float bladeLength = weapon.Item.WeaponDesign.UsedPieces[0].ScaledBladeLength;
                //    float impactPointAsPercent2 = num3 + (float)i / 4f * (num4 - num3);
                //    float num6 = Game.Current.BasicModels.StrikeMagnitudeModel.CalculateStrikeMagnitudeForSwing(attackerAgentCharacter, attackerCaptainCharacter, swingSpeed, impactPointAsPercent2, weapon.Item.Weight, currentUsageItem, currentUsageItem.GetRealWeaponLength(), currentUsageItem.Inertia, currentUsageItem.CenterOfMass, exraLinearSpeed, doesAttackerHaveMount);
                //    if (originalValue < num6)
                //    {
                //        originalValue = num6;
                //    }
                //}

                float impactPointAsPercent3 = num3 + (float)0 / 4f * (num4 - num3);
                newValue = Game.Current.BasicModels.StrikeMagnitudeModel.CalculateStrikeMagnitudeForSwing(attackerAgentCharacter, attackerCaptainCharacter, swingSpeed, impactPointAsPercent3, weapon.Item.Weight, currentUsageItem, currentUsageItem.GetRealWeaponLength(), currentUsageItem.Inertia, currentUsageItem.CenterOfMass, exraLinearSpeed, doesAttackerHaveMount);

                __result = newValue;
                return false;
            }
        }

        //[HarmonyPatch(typeof(CombatStatCalculator))]
        //[HarmonyPatch("CalculateStrikeMagnitudeForSwing")]
        //public class CalculateStrikeMagnitudeForSwingPatch
        //{
        //    public static bool Prefix(ref float __result, float swingSpeed, float impactPointAsPercent, float weaponWeight, float weaponLength, float weaponInertia, float weaponCoM, float extraLinearSpeed)
        //    {
        //        float distanceFromCoM = weaponLength * impactPointAsPercent - weaponCoM;
        //        float num2 = swingSpeed * (0.5f + weaponCoM) + extraLinearSpeed;
        //        float kineticEnergy = 0.5f * weaponWeight * num2 * num2;
        //        float inertiaEnergy = 0.5f * weaponInertia * swingSpeed * swingSpeed;
        //        float num5 = kineticEnergy + inertiaEnergy;
        //        float num6 = (num2 + swingSpeed * distanceFromCoM) / (1f / weaponWeight + distanceFromCoM * distanceFromCoM / weaponInertia);
        //        float num7 = num2 - num6 / weaponWeight;
        //        float num8 = swingSpeed - num6 * distanceFromCoM / weaponInertia;
        //        float num9 = 0.5f * weaponWeight * num7 * num7;
        //        float num10 = 0.5f * weaponInertia * num8 * num8;
        //        float num11 = num9 + num10;
        //        float num12 = num5 - num11 + 0.5f;
        //        __result =  0.067f * num12;
        //        //InformationManager.DisplayMessage(new InformationMessage("energy " + num12, Color.FromUint(4289612505u)));
        //        return false;
        //    }
        //}

        [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
        [HarmonyPatch("ComputeBlowMagnitudeMissile")]
        class ComputeBlowMagnitudeMissilePacth
        {
            static bool Prefix(in AttackInformation attackInformation, in AttackCollisionData acd, in MissionWeapon weapon, float momentumRemaining, in Vec2 victimVelocity, out float baseMagnitude, out float specialMagnitude)
            {
                Vec3 missileVelocity = acd.MissileVelocity;

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
                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Javelin") && physicalDamage > (weaponItem.Weight) * 300f)
                {
                    physicalDamage = (weaponItem.Weight) * 300f;
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
                    missileTotalDamage = 0f;
                    //baseMagnitude = (physicalDamage * momentumRemaining + (missileTotalDamage * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                    if ((DamageTypes)acd.DamageType == DamageTypes.Pierce)
                    {
                        baseMagnitude = (physicalDamage * momentumRemaining) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                    }
                    else if ((DamageTypes)acd.DamageType == DamageTypes.Cut)
                    {
                        baseMagnitude = (physicalDamage * momentumRemaining);
                    }
                    else
                    {
                        baseMagnitude = (physicalDamage * momentumRemaining) * 0.5f;
                    }
                }

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("ThrowingAxe"))
                {
                    baseMagnitude = physicalDamage * momentumRemaining;
                }

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("ThrowingKnife") ||
                    weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("Dagger"))
                {
                    baseMagnitude = (physicalDamage * momentumRemaining) * RBMConfig.RBMConfig.ThrustMagnitudeModifier * 0.6f;
                }

                if (weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("OneHandedPolearm") ||
                    weaponItem.PrimaryWeapon.WeaponClass.ToString().Equals("LowGripPolearm"))
                {
                    baseMagnitude = (physicalDamage * momentumRemaining) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
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

        [HarmonyPatch(typeof(CombatStatCalculator))]
        [HarmonyPatch("CalculateStrikeMagnitudeForThrust")]
        class CalculateStrikeMagnitudeForThrustPatch
        {
            static bool Prefix(float thrustWeaponSpeed, float weaponWeight, float extraLinearSpeed, bool isThrown, ref float __result)
            {
                float combinedSpeed = MBMath.ClampFloat(thrustWeaponSpeed, 4f, 6f) + extraLinearSpeed;
                if (combinedSpeed > 0f)
                {

                    float kineticEnergy = 0.5f * weaponWeight * combinedSpeed * combinedSpeed;
                    float mixedEnergy = 0.5f * (weaponWeight + 1.5f) * combinedSpeed * combinedSpeed;
                    float baselineEnergy = 0.5f * 8f * combinedSpeed * combinedSpeed;
                    //float basedamage = 0.5f * (weaponWeight + 4.5f) * combinedSpeed * combinedSpeed;

                    //float basedamage = 120f;
                    //if (mixedEnergy > 120f)
                    //{
                    //    basedamage = mixedEnergy;
                    //}


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
                    //else if (basedamage > 180f)
                    //{
                    //    basedamage = 180f;
                    //}
                    //float thrust = basedamage;
                    //if (kineticEnergy > basedamage)
                    //{
                    //    thrust = kineticEnergy;
                    //}

                    //if (thrust > 200f)
                    //{
                    //    thrust = 200f;
                    //}
                    float thrust = baselineEnergy;
                    __result = thrust * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                    return false;
                }
                __result = 0f;
                return false;
            }
        }
    }
}
