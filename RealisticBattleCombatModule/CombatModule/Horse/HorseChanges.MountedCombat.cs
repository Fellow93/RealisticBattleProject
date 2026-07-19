using HarmonyLib;
using SandBox.GameComponents;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMCombat
{
    internal partial class HorseChanges
    {
        [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
        [HarmonyPatch("DecideMountRearedByBlow")]
        private class DecideMountRearedByBlowPatch
        {
            private static bool Prefix(ref Mission __instance, Agent attackerAgent, Agent victimAgent, in AttackCollisionData collisionData, WeaponComponentData attackerWeapon, in Blow blow, ref bool __result)
            {
                //__result = false;
                //return false;
                //if(collisionData.InflictedDamage < 0)
                //{
                //    return true;
                //}
                //float damageMultiplierOfCombatDifficulty = Mission.Current.GetDamageMultiplierOfCombatDifficulty(victimAgent, attackerAgent);
                if (victimAgent.IsMount)
                {
                    float speed = victimAgent.MovementVelocity.Length;
                    if (speed > 5f)
                    {
                        __result = false;
                        return false;
                    }
                }
                if (collisionData.IsMissile)
                {
                    if (victimAgent.IsMount && attackerAgent != null && victimAgent.GetAgentFlags().HasAnyFlag(AgentFlag.CanRear) && Vec3.DotProduct(blow.Direction, victimAgent.Frame.rotation.f) < -0.35f)
                    {
                        __result = (float)collisionData.InflictedDamage >= TaleWorlds.Core.ManagedParameters.Instance.GetManagedParameter(TaleWorlds.Core.ManagedParametersEnum.MakesRearAttackDamageThreshold) * 2f; /// damageMultiplierOfCombatDifficulty;
                        return false;
                    }
                    else
                    {
                        __result = false;
                        return false;
                    }
                }
                else
                {
                    if (attackerWeapon != null && (attackerWeapon.WeaponFlags.HasFlag(WeaponFlags.WideGrip) || attackerWeapon.WeaponClass == WeaponClass.OneHandedPolearm || attackerWeapon.WeaponClass == WeaponClass.TwoHandedPolearm || attackerWeapon.WeaponClass == WeaponClass.LowGripPolearm)
                    && blow.StrikeType == StrikeType.Thrust && attackerAgent != null && victimAgent.GetAgentFlags().HasAnyFlag(AgentFlag.CanRear) && Vec3.DotProduct(blow.Direction, victimAgent.Frame.rotation.f) < -0.35f)
                    {
                        //__result = (float)collisionData.InflictedDamage >= TaleWorlds.Core.ManagedParameters.Instance.GetManagedParameter(TaleWorlds.Core.ManagedParametersEnum.MakesRearAttackDamageThreshold) * damageMultiplierOfCombatDifficulty;
                        __result = (float)collisionData.InflictedDamage >= TaleWorlds.Core.ManagedParameters.Instance.GetManagedParameter(TaleWorlds.Core.ManagedParametersEnum.MakesRearAttackDamageThreshold); // * damageMultiplierOfCombatDifficulty;
                        return false;
                    }
                    __result = false;
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
        private class DecideAgentDismountedByBlowPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("DecideAgentDismountedByBlow")]
            private static bool PrefixDecideAgentDismountedByBlow(Agent attackerAgent, Agent victimAgent, in AttackCollisionData collisionData, WeaponComponentData attackerWeapon, ref Blow blow)
            {
                if (!blow.IsMissile)
                {
                    if (victimAgent != null && victimAgent.HasMount && victimAgent.Character != null && victimAgent.Origin != null)
                    {
                        int ridingSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(victimAgent, DefaultSkills.Riding);
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

        [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
        [HarmonyPatch("ComputeBlowMagnitudeFromHorseCharge")]
        private class ChangeHorseDamageCalculation
        {
            private static bool Prefix(in AttackInformation attackInformation, in AttackCollisionData acd, Vec2 attackerAgentVelocity, Vec2 victimAgentVelocity, out float baseMagnitude, out float specialMagnitude)
            {
                Vec2 chargerMovementDirection = attackInformation.AttackerAgentMovementDirection;
                Vec2 vec = chargerMovementDirection * Vec2.DotProduct(victimAgentVelocity, chargerMovementDirection);
                Vec2 vec2 = attackerAgentVelocity - vec;
                ref readonly Vec3 victimAgentPosition = ref attackInformation.VictimAgentPosition;
                float num = ChargeDamageDotProduct(victimAgentPosition, chargerMovementDirection, acd.CollisionGlobalPosition);
                float num2 = vec2.Length * num;
                //baseMagnitude = (num2 * num2 * attackInformation.AttackerAgentMountChargeDamageProperty * 0.5f) / 520f; // default kinetic energy setting
                baseMagnitude = (num2 * attackInformation.AttackerAgentMountChargeDamageProperty) / 70f; // momentum experiment
                specialMagnitude = baseMagnitude;

                return false;
            }

            private static float ChargeDamageDotProduct(Vec3 victimPosition, Vec2 chargerMovementDirection, Vec3 collisionPoint)
            {
                float b = Vec2.DotProduct((victimPosition.AsVec2 - collisionPoint.AsVec2).Normalized(), chargerMovementDirection);
                return MathF.Max(0f, b);
            }
        }
    }
}
