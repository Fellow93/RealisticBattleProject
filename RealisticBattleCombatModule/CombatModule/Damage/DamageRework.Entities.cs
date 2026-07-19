using HarmonyLib;
using RBMAI;
using SandBox.GameComponents;
using SandBox.Missions.MissionLogics;
using System;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ArmorComponent;
using static TaleWorlds.Core.ItemObject;
using static TaleWorlds.MountAndBlade.Agent;

namespace RBMCombat
{
    internal partial class DamageRework
    {
        [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
        [HarmonyPatch("GetEntityDamageMultiplier")]
        private class GetEntityDamageMultiplierPatch
        {
            private static bool Prefix(bool isAttackerAgentDoingPassiveAttack, WeaponComponentData weapon, DamageTypes damageType, bool isFlammable, ref float __result)
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
                    if (isFlammable && weapon.WeaponFlags.HasAnyFlag(WeaponFlags.Burning))
                    {
                        dmgMultiplier *= 5f;
                    }
                    if (weapon.WeaponClass == WeaponClass.Boulder && Mission.Current != null)
                    {
                        if (Mission.Current.IsNavalBattle)
                        {
                            dmgMultiplier *= 10f;
                        }
                        else
                        {
                            dmgMultiplier *= 3f;
                        }
                    }
                }
                __result = dmgMultiplier;
                return false;
            }
        }

        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("GetAttackCollisionResults")]
        private class GetAttackCollisionResultsPatch
        {
            private static void Postfix(Agent attackerAgent, Agent victimAgent, ref AttackCollisionData attackCollisionData, out CombatLogData combatLog, ref CombatLogData __result)
            {
                if (attackerAgent != null && attackCollisionData.StrikeType == (int)StrikeType.Swing && !attackCollisionData.AttackBlockedWithShield && !attackerAgent.WieldedWeapon.IsEmpty && !Utilities.HitWithWeaponBlade(in attackCollisionData, attackerAgent.WieldedWeapon))
                {
                    string typeOfHandle = "{=RBM_COM_003}Handle";
                    if (attackerAgent.WieldedWeapon.CurrentUsageItem != null &&
                        (attackerAgent.WieldedWeapon.CurrentUsageItem.WeaponClass == WeaponClass.Dagger ||
                        attackerAgent.WieldedWeapon.CurrentUsageItem.WeaponClass == WeaponClass.OneHandedSword ||
                        attackerAgent.WieldedWeapon.CurrentUsageItem.WeaponClass == WeaponClass.TwoHandedSword))
                    {
                        typeOfHandle = "{=RBM_COM_004}Pommel";
                    }
                    if (attackerAgent != null && attackerAgent.IsPlayerControlled)
                    {
                        MBTextManager.SetTextVariable("TYPE", typeOfHandle);
                        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_COM_001}{TYPE} hit").ToString(), Color.FromUint(4289612505u)));
                    }
                    if (victimAgent != null && victimAgent.IsPlayerControlled)
                    {
                        MBTextManager.SetTextVariable("TYPE", typeOfHandle);
                        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_COM_002}{TYPE} hit").ToString(), Color.FromUint(4289612505u)));
                    }
                    __result.DamageType = DamageTypes.Blunt;
                }
                combatLog = __result;
            }
        }
    }

    [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
    [HarmonyPatch("GetAttackCollisionResults")]
    internal class GetAttackCollisionResultsPatch
    {
        private static void Postfix(in AttackInformation attackInformation, bool crushedThrough, float momentumRemaining, bool cancelDamage, ref AttackCollisionData attackCollisionData, ref CombatLogData combatLog, int speedBonus)
        {
            if (!attackCollisionData.IsColliderAgent && attackCollisionData.EntityExists)
            {
                if (!attackCollisionData.IsMissile)
                {
                    attackCollisionData.InflictedDamage = attackCollisionData.InflictedDamage + 5;
                    combatLog.InflictedDamage = attackCollisionData.InflictedDamage;
                }
            }
        }
    }
}
