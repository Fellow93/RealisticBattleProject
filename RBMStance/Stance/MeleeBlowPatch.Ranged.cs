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
using static RBMStance.PostureDamage;
using static TaleWorlds.Core.ArmorComponent;
using static TaleWorlds.Core.ItemObject;
using static TaleWorlds.MountAndBlade.Agent;
namespace RBMStance
{
    public partial class StanceLogic : MissionLogic
    {
        private partial class CreateMeleeBlowPatch
        {
            // The shooter's effective skill for the weapon class (0 without an agent or a relevant skill). The posture
            // and stamina costs of one shot both read it, so the shot asks the model once.
            private static int GetShooterWeaponSkill(Agent agent, WeaponClass wc)
            {
                SkillObject attackerWeaponSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(wc);
                if (agent == null || attackerWeaponSkill == null)
                {
                    return 0;
                }
                return MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(agent, attackerWeaponSkill);
            }

            public static float CalculateRangedPostureLoss(float fixedPS, float dynamicPS, Agent shooterAgent, int shooterWeaponSkill)
            {
                if (shooterAgent == null)
                {
                    return fixedPS + dynamicPS;
                }

                float attackerEffectiveWeaponSkill = shooterWeaponSkill;

                float fixedPostureLoss = fixedPS;
                float dynamicPostureLoss = dynamicPS;

                float attackerEffectiveStrengthSkill = GetBodySkill(shooterAgent);

                // Higher skill reduces the dynamic cost: skill 0 pays the full dynamicPS, skill >= 200 pays none.
                dynamicPostureLoss -= Math.Min(1f, attackerEffectiveWeaponSkill / 200f) * (dynamicPS * 0.5f);
                dynamicPostureLoss -= Math.Min(1f, attackerEffectiveStrengthSkill / 200f) * (dynamicPS * 0.5f);

                return fixedPostureLoss + dynamicPostureLoss;
            }

            private static float CalculateShootMissileStaminaLoss(int shooterWeaponSkill, WeaponClass wc, MissionWeapon firedWeapon)
            {
                int attackerEffectiveWeaponSkill = shooterWeaponSkill;

                // Item can be null for e.g. ammo-less usages; treat as difficulty 0.
                int firedWeaponDifficulty = firedWeapon.Item?.Difficulty ?? 0;

                //base stamina loss
                float result = 50f;

                switch (wc)
                {
                    case WeaponClass.Bow:
                        {
                            int weaponDifficulty = firedWeaponDifficulty;
                            int skillDifference = Math.Max(0, attackerEffectiveWeaponSkill - weaponDifficulty);
                            float skillModifier = skillDifference * 0.5f;
                            result = Math.Max(20f, 70f - skillModifier);
                            break;
                        }
                    case WeaponClass.Crossbow:
                        {
                            int weaponDifficulty = firedWeaponDifficulty;
                            int skillDifference = Math.Max(0, attackerEffectiveWeaponSkill - weaponDifficulty);
                            float skillModifier = skillDifference * 0.5f;
                            result = Math.Max(20f, 70f - skillModifier);
                            break;
                        }
                    case WeaponClass.Javelin:
                    case WeaponClass.ThrowingAxe:
                    case WeaponClass.ThrowingKnife:
                        {
                            float skillModifier = attackerEffectiveWeaponSkill * 0.5f;
                            result = Math.Max(50f, 100f - skillModifier);
                            break;
                        }
                }

                return result;
            }

            // After RBMCombat's postfixes on the same method, the order it always ran in while it lived in RBMAI.
            [HarmonyPatch(typeof(Mission))]
            [HarmonyPatch("OnAgentShootMissile")]
            [HarmonyAfter("com.rbmcombat")]
            [UsedImplicitly]
            [MBCallback]
            private class OverrideOnAgentShootMissile
            {
                private static void Postfix(ref Agent shooterAgent, EquipmentIndex weaponIndex, Vec3 position, Vec3 velocity, Mat3 orientation, bool hasRigidBody, bool isPrimaryWeaponShot, int forcedMissileIndex, Mission __instance)
                {
                    if (RBMConfig.RBMConfig.postureEnabled)
                    {
                        if (shooterAgent == null || shooterAgent.Equipment == null || weaponIndex == EquipmentIndex.None)
                        {
                            return;
                        }
                        MissionWeapon missionWeapon = shooterAgent.Equipment[weaponIndex];
                        if (missionWeapon.IsEmpty || missionWeapon.CurrentUsageItem == null)
                        {
                            return;
                        }
                        WeaponClass wc = missionWeapon.CurrentUsageItem.WeaponClass;
                        Stance shooterStance = null;
                        AgentStances.values.TryGetValue(shooterAgent, out shooterStance);
                        if (shooterStance != null)
                        {
                            float currentTime = Mission.Current.CurrentTime;
                            float postureLoss = 0f;
                            // Crossbows pay stamina for spanning, not for loosing: see OverrideOnWeaponReloadPhaseChange.
                            bool chargeStamina = RBMConfig.RBMConfig.staminaEnabled && wc != WeaponClass.Crossbow;
                            bool costsPosture = wc == WeaponClass.Bow || wc == WeaponClass.Crossbow || wc == WeaponClass.Javelin
                                || wc == WeaponClass.ThrowingAxe || wc == WeaponClass.ThrowingKnife;
                            int shooterWeaponSkill = costsPosture || chargeStamina ? GetShooterWeaponSkill(shooterAgent, wc) : 0;
                            switch (wc)
                            {
                                case WeaponClass.Bow:
                                    {
                                        postureLoss = CalculateRangedPostureLoss(35f, 25f, shooterAgent, shooterWeaponSkill);
                                        break;
                                    }
                                case WeaponClass.Crossbow:
                                    {
                                        postureLoss = CalculateRangedPostureLoss(5f, 5f, shooterAgent, shooterWeaponSkill);
                                        break;
                                    }
                                case WeaponClass.Javelin:
                                    {
                                        postureLoss = CalculateRangedPostureLoss(25f, 25f, shooterAgent, shooterWeaponSkill);
                                        break;
                                    }
                                case WeaponClass.ThrowingAxe:
                                case WeaponClass.ThrowingKnife:
                                    {
                                        postureLoss = CalculateRangedPostureLoss(25f, 25f, shooterAgent, shooterWeaponSkill);
                                        break;
                                    }
                            }

                            // Shooting drains posture; it never refills it. Regen handles recovery.
                            shooterStance.posture = Math.Max(0f, shooterStance.posture - postureLoss);

                            if (chargeStamina)
                            {
                                float staminaLoss = CalculateShootMissileStaminaLoss(shooterWeaponSkill, wc, missionWeapon);
                                shooterStance.ReduceStamina(staminaLoss);
                            }
                        }
                    }
                }
            }

            // Charges crossbow stamina once per completed reload: only on the transition into the final
            // reload phase. An interrupted reload never reaches it, so it costs nothing.
            [HarmonyPatch(typeof(Agent))]
            [HarmonyPatch("OnWeaponReloadPhaseChange")]
            [UsedImplicitly]
            [MBCallback]
            private class OverrideOnWeaponReloadPhaseChange
            {
                private static void Prefix(Agent __instance, EquipmentIndex slotIndex, out short __state)
                {
                    __state = -1;
                    if (__instance?.Equipment != null && slotIndex != EquipmentIndex.None)
                    {
                        __state = __instance.Equipment[slotIndex].ReloadPhase;
                    }
                }

                private static void Postfix(Agent __instance, EquipmentIndex slotIndex, short reloadPhase, short __state)
                {
                    if (!RBMConfig.RBMConfig.postureEnabled || !RBMConfig.RBMConfig.staminaEnabled || __state < 0)
                    {
                        return;
                    }
                    MissionWeapon missionWeapon = __instance.Equipment[slotIndex];
                    if (missionWeapon.IsEmpty || missionWeapon.CurrentUsageItem == null || missionWeapon.CurrentUsageItem.WeaponClass != WeaponClass.Crossbow)
                    {
                        return;
                    }
                    short reloadPhaseCount = missionWeapon.ReloadPhaseCount;
                    if (__state >= reloadPhaseCount || reloadPhase < reloadPhaseCount)
                    {
                        return;
                    }
                    Stance shooterStance = null;
                    AgentStances.values.TryGetValue(__instance, out shooterStance);
                    if (shooterStance != null)
                    {
                        shooterStance.ReduceStamina(CalculateShootMissileStaminaLoss(GetShooterWeaponSkill(__instance, WeaponClass.Crossbow), WeaponClass.Crossbow, missionWeapon));
                    }
                }
            }

        }
    }
}
