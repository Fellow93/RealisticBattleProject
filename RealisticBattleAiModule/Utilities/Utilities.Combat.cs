using RBMConfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ArmorComponent;
using static TaleWorlds.Core.ItemObject;

namespace RBMAI
{
    public static partial class Utilities
    {

        //public static int GetMeleeSkill(Agent agent, WeaponComponentData equippedItem, WeaponComponentData secondaryItem)
        //{
        //    SkillObject skill = DefaultSkills.Athletics;
        //    if (equippedItem != null)
        //    {
        //        SkillObject relevantSkill = equippedItem.RelevantSkill;
        //        skill = ((relevantSkill == DefaultSkills.OneHanded || relevantSkill == DefaultSkills.Polearm) ? relevantSkill : ((relevantSkill != DefaultSkills.TwoHanded) ? DefaultSkills.OneHanded : ((secondaryItem == null) ? DefaultSkills.TwoHanded : DefaultSkills.OneHanded)));
        //    }
        //    return GetEffectiveSkill(agent.Character, agent.Origin, agent.Formation, skill);
        //}

        //public static int GetEffectiveSkill(BasicCharacterObject agentCharacter, IAgentOriginBase agentOrigin, Formation agentFormation, SkillObject skill)
        //{
        //    return agentCharacter.GetSkillValue(skill);
        //}

        public static bool HitWithWeaponBladeTip(in AttackCollisionData collisionData, in MissionWeapon attackerWeapon)
        {
            WeaponComponentData currentUsageItem = attackerWeapon.CurrentUsageItem;
            if (currentUsageItem != null)
            {
                WeaponClass weaponClass = attackerWeapon.CurrentUsageItem.WeaponClass;
                if (collisionData.CollisionDistanceOnWeapon > currentUsageItem.GetRealWeaponLength() * 0.95f)
                {
                    return true;
                }
                return false;
            }
            return false;
        }

        public static bool HitWithWeaponBlade(in AttackCollisionData collisionData, in MissionWeapon attackerWeapon)
        {
            WeaponComponentData currentUsageItem = attackerWeapon.CurrentUsageItem;
            if (attackerWeapon.Item != null && currentUsageItem != null && attackerWeapon.Item.WeaponDesign != null &&
                attackerWeapon.Item.WeaponDesign.UsedPieces != null && attackerWeapon.Item.WeaponDesign.UsedPieces.Length > 0)
            {
                bool isSwordType = false;
                if (attackerWeapon.CurrentUsageItem != null)
                    switch (attackerWeapon.CurrentUsageItem.WeaponClass)
                    {
                        case WeaponClass.Dagger:
                        case WeaponClass.OneHandedSword:
                        case WeaponClass.TwoHandedSword:
                            {
                                isSwordType = true;
                                break;
                            }
                    }
                float bladeLength = attackerWeapon.Item.WeaponDesign.UsedPieces[0].ScaledBladeLength + (isSwordType ? 0f : 0.15f);
                float realWeaponLength = currentUsageItem.GetRealWeaponLength();
                if (collisionData.CollisionDistanceOnWeapon < (realWeaponLength - bladeLength))
                {
                    return false;
                }
                return true;
            }
            return true;
        }

        public static float GetComHitModifier(in AttackCollisionData collisionData, in MissionWeapon attackerWeapon)
        {
            WeaponComponentData currentUsageItem = attackerWeapon.CurrentUsageItem;
            if (collisionData.StrikeType == (int)StrikeType.Thrust)
            {
                if (collisionData.CollisionHitResultFlags == CombatHitResultFlags.NormalHit)
                {
                    return 1f;
                }
                else
                {
                    return 0.3f;
                }
            }

            float comHitModifier = 0f;
            if (attackerWeapon.Item != null && currentUsageItem != null && attackerWeapon.Item.WeaponDesign != null &&
                attackerWeapon.Item.WeaponDesign.UsedPieces != null && attackerWeapon.Item.WeaponDesign.UsedPieces.Length > 0)
            {
                float impactPointAsPercent = MBMath.ClampFloat(collisionData.CollisionDistanceOnWeapon, -0.2f, currentUsageItem.GetRealWeaponLength()) / currentUsageItem.GetRealWeaponLength();
                float comAsPercent = MBMath.ClampFloat(currentUsageItem.CenterOfMass, -0.2f, currentUsageItem.GetRealWeaponLength()) / currentUsageItem.GetRealWeaponLength();
                comHitModifier = 1f - Math.Abs(comAsPercent - impactPointAsPercent);
                if (attackerWeapon.CurrentUsageItem != null)
                {
                    switch (attackerWeapon.CurrentUsageItem.WeaponClass)
                    {
                        case WeaponClass.OneHandedAxe:
                        case WeaponClass.TwoHandedAxe:
                        case WeaponClass.Mace:
                        case WeaponClass.TwoHandedMace:
                        case WeaponClass.TwoHandedPolearm:
                            {
                                if (collisionData.StrikeType == (int)StrikeType.Swing)
                                {
                                    if (HitWithWeaponBlade(collisionData, attackerWeapon))
                                    {
                                        return 1f;
                                    }
                                    else
                                    {
                                        return 0.3f;
                                    }
                                }
                                break;
                            }
                        case WeaponClass.Dagger:
                        case WeaponClass.OneHandedSword:
                        case WeaponClass.TwoHandedSword:
                            {
                                float bladeLength = attackerWeapon.Item.WeaponDesign.UsedPieces[0].ScaledBladeLength + 0f;
                                float realWeaponLength = currentUsageItem.GetRealWeaponLength();
                                if (collisionData.CollisionDistanceOnWeapon < (realWeaponLength - bladeLength))
                                {
                                    return 1f;
                                }
                                break;
                            }
                    }
                }
                if (comHitModifier > 0.66f)
                {
                    return 1f;
                }
                else if (comHitModifier > 0.33f)
                {
                    return 0.66f;
                }
                else
                {
                    return 0.33f;
                }
            }
            return comHitModifier;
        }

        public static int GetHarnessTier(Agent agent)
        {
            int tier = 10;
            EquipmentElement equipmentElement = agent.SpawnEquipment[EquipmentIndex.HorseHarness];
            if (agent != null)
            {
                if (agent.MountAgent != null)
                {
                    if (agent.SpawnEquipment != null)
                    {
                        if (equipmentElement.Item != null)
                        {
                            if (equipmentElement.Item.Effectiveness < 50f)
                            {
                                tier = (int)1;
                            }
                        }
                    }
                }
            }
            return tier;
        }

        public static float GetCombatAIDifficultyMultiplier()
        {
            MissionState missionState = Game.Current.GameStateManager.ActiveState as MissionState;
            if (missionState != null)
            {
                if (!RBMConfig.RBMConfig.vanillaCombatAi)
                {
                    if (missionState.MissionName.Equals("EnhancedBattleTestFieldBattle") || missionState.MissionName.Equals("EnhancedBattleTestSiegeBattle"))
                    {
                        return 1.0f;
                    }
                    switch (CampaignOptions.CombatAIDifficulty)
                    {
                        case CampaignOptions.Difficulty.VeryEasy:
                            return 0.70f;

                        case CampaignOptions.Difficulty.Easy:
                            return 0.85f;

                        case CampaignOptions.Difficulty.Realistic:
                            return 1.0f;

                        default:
                            return 1.0f;
                    }
                }
                else
                {
                    switch (CampaignOptions.CombatAIDifficulty)
                    {
                        case CampaignOptions.Difficulty.VeryEasy:
                            return 0.1f;

                        case CampaignOptions.Difficulty.Easy:
                            return 0.32f;

                        case CampaignOptions.Difficulty.Realistic:
                            return 0.96f;

                        default:
                            return 0.5f;
                    }
                }
            }
            else
            {
                return 1f;
            }
        }

        public static float CalculateAILevel(Agent agent, int relevantSkillLevel)
        {
            float difficultyModifier = GetCombatAIDifficultyMultiplier();
            //float difficultyModifier = 1.0f; // v enhanced battle test je difficulty very easy
            return MBMath.ClampFloat((float)relevantSkillLevel / 250f * difficultyModifier, 0f, 1f);
        }

    }
}
