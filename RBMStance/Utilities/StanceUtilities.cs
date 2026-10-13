using System;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMStance
{
    // Helpers the posture system and kick/bash use (moved here from RBMAI's Utilities.Combat.cs, which had no other
    // caller for them).
    public static class StanceUtilities
    {
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
            // Not built from crafting parts, so there is no blade length to read. A sword's handle is its short hilt,
            // the bottom share of its length; anything else is a head on a haft, so all but its top share is haft.
            // Same rule as RBMCombat's copy.
            if (attackerWeapon.Item != null && currentUsageItem != null)
            {
                float realWeaponLength = currentUsageItem.GetRealWeaponLength();
                if (realWeaponLength > 0f)
                {
                    WeaponClass weaponClass = currentUsageItem.WeaponClass;
                    bool isSwordType = weaponClass == WeaponClass.Dagger || weaponClass == WeaponClass.OneHandedSword || weaponClass == WeaponClass.TwoHandedSword;
                    float handleLength = isSwordType ? realWeaponLength * NonCraftedHiltShare : realWeaponLength * (1f - NonCraftedHeadShare);
                    if (collisionData.CollisionDistanceOnWeapon < handleLength)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        // Share of a non-crafted sword's or dagger's length, from the hand, that HitWithWeaponBlade treats as the hilt.
        private const float NonCraftedHiltShare = 0.1f;

        // Share of any other non-crafted weapon's length, from the tip, that HitWithWeaponBlade treats as the head
        // (blade plus the crafted rule's 0.15 m margin); the rest is haft. Crafted spears come out nearer 20%,
        // crafted one-handed axes nearer 45%.
        private const float NonCraftedHeadShare = 0.35f;

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

            // neutral when the weapon can't be measured; 0 here used to zero all block/parry posture damage
            float comHitModifier = 1f;
            // the centre-of-mass distance only needs the usage item, so non-crafted weapons get it too
            if (attackerWeapon.Item != null && currentUsageItem != null)
            {
                bool hasDesign = attackerWeapon.Item.WeaponDesign != null &&
                    attackerWeapon.Item.WeaponDesign.UsedPieces != null && attackerWeapon.Item.WeaponDesign.UsedPieces.Length > 0;
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
                                if (!hasDesign)
                                {
                                    break;
                                }
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

        /// <summary>
        /// The man's armour weight as the game's stat model sees it, so Athletics' Form Fitting Armor (applied only in
        /// SandboxAgentStatCalculateModel.GetEffectiveArmorEncumbrance) lightens RBM's armour-weight terms too. Calls
        /// into the model: main thread only. Raw weight if there is no model.
        /// </summary>
        public static float GetEffectiveArmorWeight(Agent agent)
        {
            AgentStatCalculateModel model = MissionGameModels.Current?.AgentStatCalculateModel;
            if (model == null)
            {
                return agent.SpawnEquipment.GetTotalWeightOfArmor(true);
            }
            return model.GetEffectiveArmorEncumbrance(agent, agent.SpawnEquipment);
        }
    }
}
