using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMCombat
{
    [HarmonyPatch(typeof(Mission))]
    internal static class RealisticWeaponCollision
    {
        private static float nakedCutStick = 10f;

        private static float nakedPierceStick = 10f;

        private static float nakedBluntStick = 10f;

        private static float clothCutStick = 15f;

        private static float clothPierceStick = 12f;

        private static float clothBluntStick = 20f;

        private static float leatherCutStick = 20f;

        private static float leatherPierceStick = 15f;

        private static float leatherBluntStick = 20f;

        private static float mailCutStick = 30f;

        private static float mailPierceStick = 17f;

        private static float mailBluntStick = 30f;

        private static float plateCutStick = 33f;

        private static float platePierceStick = 20f;

        private static float plateBluntStick = 33f;

        public static ArmorComponent.ArmorMaterialTypes GetMateirialTypeofHitBodyPart(Agent defender, BoneBodyPartType hitBodyPart)
        {
            EquipmentIndex equipmentIndex = EquipmentIndex.None;
            if (defender?.IsHuman ?? false)
            {
                if (hitBodyPart == BoneBodyPartType.Head || hitBodyPart == BoneBodyPartType.Neck)
                {
                    equipmentIndex = EquipmentIndex.NumAllWeaponSlots;
                }
                else if (hitBodyPart == BoneBodyPartType.Chest || hitBodyPart == BoneBodyPartType.Abdomen || hitBodyPart == BoneBodyPartType.ShoulderLeft || hitBodyPart == BoneBodyPartType.ShoulderRight)
                {
                    equipmentIndex = EquipmentIndex.Body;
                }
                else if (hitBodyPart == BoneBodyPartType.ArmLeft || hitBodyPart == BoneBodyPartType.ArmRight)
                {
                    equipmentIndex = EquipmentIndex.Gloves;
                }
                else if (hitBodyPart == BoneBodyPartType.Legs)
                {
                    equipmentIndex = EquipmentIndex.Leg;
                }
                if (equipmentIndex != EquipmentIndex.None && defender.SpawnEquipment[equipmentIndex].Item != null)
                {
                    return defender.SpawnEquipment[equipmentIndex].Item.ArmorComponent.MaterialType;
                }
            }
            return ArmorComponent.ArmorMaterialTypes.None;
        }

        [HarmonyPostfix]
        [HarmonyPatch("DecideAgentHitParticles")]
        private static void DecideAgentHitParticlesMOD(Blow blow, Agent victim, ref AttackCollisionData collisionData, ref HitParticleResultData hprd)
        {
            if (victim == null || (blow.InflictedDamage <= 0 && !(victim.Health <= 0f)))
            {
                return;
            }
            if (!blow.WeaponRecord.HasWeapon() || blow.WeaponRecord.WeaponFlags.HasFlag(WeaponFlags.NoBlood) || collisionData.IsAlternativeAttack || collisionData.CollidedWithShieldOnBack)
            {
                hprd.StartHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_sweat_sword_enter");
                hprd.ContinueHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_sweat_sword_enter");
                hprd.EndHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_sweat_sword_enter");
                return;
            }
            ArmorComponent.ArmorMaterialTypes mateirialTypeofHitBodyPart = GetMateirialTypeofHitBodyPart(victim, collisionData.VictimHitBodyPart);
            if ((mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Chainmail || mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Plate) && ((sbyte)collisionData.DamageType == 0 || (sbyte)collisionData.DamageType == 2) || blow.InflictedDamage <= 20)
            {
                hprd.StartHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_sweat_sword_enter");
                hprd.ContinueHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_blood_sword_inside");
                hprd.EndHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_sweat_sword_enter");
            }
            else
            {
                hprd.StartHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_blood_sword_enter");
                hprd.ContinueHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_blood_sword_inside");
                hprd.EndHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_blood_sword_exit");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("DecideWeaponCollisionReaction")]
        private static void DecideWeaponCollisionReactionMOD(Blow registeredBlow, in AttackCollisionData collisionData, Agent attacker, Agent defender, in MissionWeapon attackerWeapon, bool isFatalHit, bool isShruggedOff, out MeleeCollisionReaction colReaction)
        {
            if (collisionData.IsColliderAgent && collisionData.StrikeType == 1 && collisionData.CollisionHitResultFlags.HasAnyFlag(CombatHitResultFlags.HitWithStartOfTheAnimation))
            {
                colReaction = MeleeCollisionReaction.Staggered;
                return;
            }
            if (!collisionData.IsColliderAgent && collisionData.PhysicsMaterialIndex != -1 && PhysicsMaterial.GetFromIndex(collisionData.PhysicsMaterialIndex).GetFlags().HasAnyFlag(PhysicsMaterialFlags.AttacksCanPassThrough))
            {
                colReaction = MeleeCollisionReaction.SlicedThrough;
                return;
            }
            if (!collisionData.IsColliderAgent || registeredBlow.InflictedDamage <= 0)
            {
                colReaction = MeleeCollisionReaction.Bounced;
                return;
            }
            if (collisionData.StrikeType == 1 && collisionData.IsHorseCharge)
            {
                colReaction = MeleeCollisionReaction.Stuck;
                return;
            }
            if (collisionData.AttackBlockedWithShield || collisionData.CollidedWithShieldOnBack)
            {
                colReaction = MeleeCollisionReaction.Bounced;
                return;
            }
            MissionWeapon missionWeapon = attackerWeapon;
            if (missionWeapon.IsEmpty)
            {
                WeaponClass weaponClass = WeaponClass.Undefined;
            }
            else
            {
                missionWeapon = attackerWeapon;
                WeaponClass weaponClass = missionWeapon.CurrentUsageItem.WeaponClass;
            }
            if (!missionWeapon.IsEmpty && isFatalHit && defender != null && defender.IsHuman && !collisionData.IsAlternativeAttack && (sbyte)collisionData.DamageType == 0 && (collisionData.VictimHitBodyPart == BoneBodyPartType.Neck || collisionData.VictimHitBodyPart == BoneBodyPartType.ArmLeft || collisionData.VictimHitBodyPart == BoneBodyPartType.ArmRight || collisionData.VictimHitBodyPart == BoneBodyPartType.Legs))
            {
                colReaction = MeleeCollisionReaction.SlicedThrough;
                return;
            }
            if (!missionWeapon.IsEmpty && isFatalHit && defender != null && defender.IsHuman && !collisionData.IsAlternativeAttack)
            {
                colReaction = MeleeCollisionReaction.Stuck;
                return;
            }
            ArmorComponent.ArmorMaterialTypes mateirialTypeofHitBodyPart = GetMateirialTypeofHitBodyPart(defender, collisionData.VictimHitBodyPart);
            float num = collisionData.InflictedDamage;
            if (!missionWeapon.IsEmpty && defender.IsHuman && !collisionData.IsAlternativeAttack && (sbyte)collisionData.DamageType == 0 && ((mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.None && num < nakedCutStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Cloth && num < clothCutStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Leather && num < leatherCutStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Chainmail && num < mailCutStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Plate && num < plateCutStick)))
            {
                colReaction = MeleeCollisionReaction.Bounced;
            }
            else if (!missionWeapon.IsEmpty && defender.IsHuman && !collisionData.IsAlternativeAttack && (sbyte)collisionData.DamageType == 1 && ((mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.None && num < nakedPierceStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Cloth && num < clothPierceStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Leather && num < leatherPierceStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Chainmail && num < mailPierceStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Plate && num < platePierceStick)))
            {
                colReaction = MeleeCollisionReaction.Bounced;
            }
            else if (!missionWeapon.IsEmpty && defender.IsHuman && !collisionData.IsAlternativeAttack && (sbyte)collisionData.DamageType == 2 && ((mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.None && num < nakedBluntStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Cloth && num < clothBluntStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Leather && num < leatherBluntStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Chainmail && num < mailBluntStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Plate && num < plateBluntStick)))
            {
                colReaction = MeleeCollisionReaction.Bounced;
            }
            else
            {
                colReaction = MeleeCollisionReaction.Stuck;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("CreateMeleeBlow")]
        private static Blow CreateMeleeBlowPostFix(Blow __instance, Agent attackerAgent, Agent victimAgent, in AttackCollisionData collisionData, in MissionWeapon attackerWeapon, CrushThroughState crushThroughState, Vec3 blowDirection, Vec3 swingDirection, bool cancelDamage)
        {
            if (collisionData.StrikeType == 0 && !collisionData.IsHorseCharge && !collisionData.IsAlternativeAttack && !Utilities.HitWithWeaponBlade(in collisionData, in attackerWeapon))
            {
                __instance.DamageType = DamageTypes.Blunt;
                //float newDamage = __instance.InflictedDamage * ManagedParameters.Instance.GetManagedParameter(ManagedParametersEnum.OverSwingCombatSpeedGraphZeroProgressValue);
                //__instance.InflictedDamage = MathF.Ceiling(newDamage);
            }
            return __instance;
        }
    }
}

