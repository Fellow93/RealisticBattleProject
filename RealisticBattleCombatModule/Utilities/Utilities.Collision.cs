using RBMConfig;
using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ArmorComponent;

namespace RBMCombat
{
    public static partial class Utilities
    {
        public static bool ThurstWithTip(in AttackCollisionData collisionData, in MissionWeapon attackerWeapon)
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
                float impactPointAsPercent = collisionData.CollisionDistanceOnWeapon / realWeaponLength;
                if (impactPointAsPercent < 0.85f)
                {
                    return false;
                }
                return true;
            }
            return true;
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
            // Not built from crafting parts, so there is no blade length to read: the bottom share of the weapon
            // counts as the handle.
            if (attackerWeapon.Item != null && currentUsageItem != null)
            {
                float realWeaponLength = currentUsageItem.GetRealWeaponLength();
                if (realWeaponLength > 0f && collisionData.CollisionDistanceOnWeapon < realWeaponLength * NonCraftedHandleShare)
                {
                    return false;
                }
            }
            return true;
        }

        // Share of a non-crafted weapon's length, from the hand, that HitWithWeaponBlade treats as the handle.
        private const float NonCraftedHandleShare = 0.25f;

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
    }
}
