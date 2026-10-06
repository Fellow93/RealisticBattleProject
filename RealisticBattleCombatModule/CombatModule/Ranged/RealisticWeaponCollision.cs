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

        // Same shield lookup as vanilla AttackInformation: the wielded off-hand item, or the first non-wielded shield slot for the back.
        internal static bool IsHitShieldMetal(Agent victim, bool onBack)
        {
            if ((victim.GetAgentFlags() & AgentFlag.CanWieldWeapon) == AgentFlag.None)
            {
                return false;
            }
            EquipmentIndex offhandIndex = victim.GetOffhandWieldedItemIndex();
            WeaponComponentData shield = null;
            if (!onBack)
            {
                if (offhandIndex != EquipmentIndex.None)
                {
                    shield = victim.Equipment[offhandIndex].CurrentUsageItem;
                }
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    WeaponComponentData item = victim.Equipment[i].CurrentUsageItem;
                    if (i != (int)offhandIndex && item != null && item.IsShield)
                    {
                        shield = item;
                        break;
                    }
                }
            }
            return shield != null && shield.PhysicsMaterial == "metal_shield";
        }

        [HarmonyPostfix]
        [HarmonyPatch("DecideAgentHitParticles")]
        private static void DecideAgentHitParticlesMOD(Mission __instance, Blow blow, Agent victim, ref AttackCollisionData collisionData, ref HitParticleResultData hprd)
        {
            // Shield blocks carry the shield's damage in InflictedDamage, so without this they'd get body-hit particles.
            // The engine doesn't draw hit particles for shield collisions, so burst the effect at the impact point ourselves.
            // Native impact effects carry dust/smoke emitters, so only dust-free systems: sparks off a metal_shield item on
            // every block, splinters off a wooden one when the blow bites deep.
            if (victim != null && (collisionData.AttackBlockedWithShield || collisionData.CollidedWithShieldOnBack))
            {
                hprd.Reset();
                string effect = null;
                if (IsHitShieldMetal(victim, collisionData.CollidedWithShieldOnBack))
                {
                    effect = "psys_game_sparkle_a";
                }
                else if (blow.InflictedDamage > 20)
                {
                    effect = "psys_game_wood_splinter_a";
                }
                if (effect != null)
                {
                    __instance.AddParticleSystemBurstByName(effect, new MatrixFrame(Mat3.Identity, collisionData.CollisionGlobalPosition), false);
                }
                return;
            }
            if (victim == null || (blow.InflictedDamage <= 0 && !(victim.Health <= 0f)))
            {
                return;
            }
            if (!blow.WeaponRecord.HasWeapon() || blow.WeaponRecord.WeaponFlags.HasFlag(WeaponFlags.NoBlood) || collisionData.IsAlternativeAttack)
            {
                hprd.StartHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_sweat_sword_enter");
                hprd.ContinueHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_sweat_sword_enter");
                hprd.EndHitParticleIndex = ParticleSystemManager.GetRuntimeIdByName("psys_game_sweat_sword_enter");
                return;
            }
            ArmorComponent.ArmorMaterialTypes mateirialTypeofHitBodyPart = GetMateirialTypeofHitBodyPart(victim, collisionData.VictimHitBodyPart);
            if ((mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Chainmail || mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Plate) && (blow.DamageType == DamageTypes.Cut || blow.DamageType == DamageTypes.Blunt) || blow.InflictedDamage <= 20)
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

        // v1.5.x: a one-shot reaction override (NavalDLC sets SlicedThrough when a blow cuts a ship rope) is
        // consumed and cleared by vanilla before the postfix runs, so note here whether one was pending.
        [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
        [HarmonyPrefix]
        [HarmonyPatch("DecideWeaponCollisionReaction")]
        private static void DecideWeaponCollisionReactionOverridePrefix(out bool __state)
        {
            __state = MissionCombatMechanicsHelper.NextBlowCollisionReactionOverride.HasValue;
        }

        [HarmonyPatch(typeof(MissionCombatMechanicsHelper))]
        [HarmonyPostfix]
        [HarmonyPatch("DecideWeaponCollisionReaction")]
        private static void DecideWeaponCollisionReactionMOD(Blow registeredBlow, in AttackCollisionData collisionData, Agent attacker, Agent defender, in MissionWeapon attackerWeapon, bool isFatalHit, bool isShruggedOff, float momentumRemaining, ref MeleeCollisionReaction colReaction, bool __state)
        {
            // Keep the overridden reaction vanilla already applied.
            if (__state)
            {
                return;
            }
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
            // Couched lance / braced polearm: only take vanilla's pass-through roll on a mounted kill (5% + Skewer perk);
            // otherwise fall through so kills stick and weak hits bounce off armor like any other thrust.
            if (collisionData.StrikeType == 1 && attacker.IsDoingPassiveAttack &&
                MissionGameModels.Current.AgentApplyDamageModel.DecidePassiveAttackCollisionReaction(attacker, defender, isFatalHit) == MeleeCollisionReaction.SlicedThrough)
            {
                colReaction = MeleeCollisionReaction.SlicedThrough;
                return;
            }
            // Vanilla cases the armor thresholds below don't model: kicks and bashes that still have momentum,
            // hits with the hilt or arm, shrugged-off blows and body punches.
            if (collisionData.IsAlternativeAttack && momentumRemaining > 0f)
            {
                colReaction = MeleeCollisionReaction.ContinueChecking;
                return;
            }
            if (MissionCombatMechanicsHelper.HitWithAnotherBone(in collisionData, attacker, in attackerWeapon))
            {
                colReaction = MeleeCollisionReaction.Bounced;
                return;
            }
            if ((!attackerWeapon.IsEmpty && !isFatalHit && isShruggedOff) || (attackerWeapon.IsEmpty && defender != null && defender.IsHuman && !collisionData.IsAlternativeAttack && (collisionData.VictimHitBodyPart == BoneBodyPartType.Chest || collisionData.VictimHitBodyPart == BoneBodyPartType.ShoulderLeft || collisionData.VictimHitBodyPart == BoneBodyPartType.ShoulderRight || collisionData.VictimHitBodyPart == BoneBodyPartType.Abdomen || collisionData.VictimHitBodyPart == BoneBodyPartType.Legs)))
            {
                colReaction = MeleeCollisionReaction.Bounced;
                return;
            }
            if (collisionData.AttackBlockedWithShield || collisionData.CollidedWithShieldOnBack)
            {
                colReaction = MeleeCollisionReaction.Bounced;
                return;
            }
            MissionWeapon missionWeapon = attackerWeapon;
            // The blow's type, not the collision's: off-blade swings were turned blunt in CreateMeleeBlow.
            DamageTypes damageType = registeredBlow.DamageType;
            if (!missionWeapon.IsEmpty && isFatalHit && defender != null && defender.IsHuman && !collisionData.IsAlternativeAttack && damageType == DamageTypes.Cut && (collisionData.VictimHitBodyPart == BoneBodyPartType.Neck || collisionData.VictimHitBodyPart == BoneBodyPartType.ArmLeft || collisionData.VictimHitBodyPart == BoneBodyPartType.ArmRight || collisionData.VictimHitBodyPart == BoneBodyPartType.Legs))
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
            if (!missionWeapon.IsEmpty && defender.IsHuman && !collisionData.IsAlternativeAttack && damageType == DamageTypes.Cut && ((mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.None && num < nakedCutStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Cloth && num < clothCutStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Leather && num < leatherCutStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Chainmail && num < mailCutStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Plate && num < plateCutStick)))
            {
                colReaction = MeleeCollisionReaction.Bounced;
            }
            else if (!missionWeapon.IsEmpty && defender.IsHuman && !collisionData.IsAlternativeAttack && damageType == DamageTypes.Pierce && ((mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.None && num < nakedPierceStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Cloth && num < clothPierceStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Leather && num < leatherPierceStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Chainmail && num < mailPierceStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Plate && num < platePierceStick)))
            {
                colReaction = MeleeCollisionReaction.Bounced;
            }
            else if (!missionWeapon.IsEmpty && defender.IsHuman && !collisionData.IsAlternativeAttack && damageType == DamageTypes.Blunt && ((mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.None && num < nakedBluntStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Cloth && num < clothBluntStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Leather && num < leatherBluntStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Chainmail && num < mailBluntStick) || (mateirialTypeofHitBodyPart == ArmorComponent.ArmorMaterialTypes.Plate && num < plateBluntStick)))
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
        private static Blow CreateMeleeBlowPostFix(Blow __result, Agent attackerAgent, Agent victimAgent, in AttackCollisionData collisionData, in MissionWeapon attackerWeapon, CrushThroughState crushThroughState, Vec3 blowDirection, Vec3 swingDirection, bool cancelDamage)
        {
            if (collisionData.StrikeType == 0 && !collisionData.IsHorseCharge && !collisionData.IsAlternativeAttack && !Utilities.HitWithWeaponBlade(in collisionData, in attackerWeapon))
            {
                __result.DamageType = DamageTypes.Blunt;
                //float newDamage = __result.InflictedDamage * ManagedParameters.Instance.GetManagedParameter(ManagedParametersEnum.OverSwingCombatSpeedGraphZeroProgressValue);
                //__result.InflictedDamage = MathF.Ceiling(newDamage);
            }
            return __result;
        }
    }
}