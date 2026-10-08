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
            // Prototype mordhau grip: the hilt IS the striking head, and the blade/handle split below measures from the
            // normal grip (its real length is the stub behind the reversed hand), so it would call hilt blows handle hits.
            if (MordhauGrip.IsMordhau(currentUsageItem))
            {
                return true;
            }
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
