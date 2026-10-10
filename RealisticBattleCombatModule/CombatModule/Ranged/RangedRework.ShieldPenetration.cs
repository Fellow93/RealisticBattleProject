using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.MountAndBlade.Mission;

namespace RBMCombat
{
    public partial class RangedRework
    {
        // Pila (Javelin class with BonusAgainstShield) punching through a raised wooden shield (a metal one stops
        // every missile, RangedRework.Collision.cs).
        //
        // The engine's own CanPenetrateShield path is not used: it lets the missile fly on and stick in the body,
        // skips the shield's damage and knows nothing of how thick the shield was. Here the hit stays an ordinary
        // shield block - the pilum sticks in the shield and the shield takes its damage - and the wound is dealt
        // on top of it:
        //  - the shield takes a base 10% of the throw's magnitude and 5% more for every point of its armor
        //    (armor 4 lets 70% through, armor 18 and up stops it); Arrow Catcher on the holder (or his captain)
        //    scales what gets through down further (RBMConfig.ArrowCatcher);
        //  - the shank comes out the back along the line of flight, as deep as the energy left drives it;
        //  - only when that stretch of shank meets the man behind the shield is he wounded, by a normal missile
        //    blow to the body part it reaches first, carrying that share of the throw against that part's armor.
        internal static class PilumShieldPenetration
        {
            // Iron shank behind the head (spear_blade_38 at its 280% scale is ~0.66 m).
            private const float ShankLength = 0.6f;

            // Throw energy left after the shield (the unit of RBMComputeDamage's pierce penetration, about the
            // missile's kinetic energy) that drives the whole shank through; less comes out proportionally less.
            // A thrown pilum lands with roughly 100-200 (Utilities.calculateThrowableSpeed, less the throwable
            // speed correction).
            private const float FullDepthEnergy = 120f;

            // Magnitude the shield takes from the throw: a base share, plus a share per point of its armor.
            private const float ShieldBaseLoss = 0.10f;
            private const float ShieldLossPerArmorPoint = 0.05f;

            // The top of the skull above the head bone, which sits at the base of the skull.
            private const float SkullHeight = 0.15f;

            // The body as capsules between two bones of the human skeleton. Radius is the limb's half thickness
            // plus the shank. An empty end bone makes the head: it runs on from the start bone, away from the neck.
            private struct BodyCapsule
            {
                public readonly string StartBone;
                public readonly string EndBone;
                public readonly float Radius;
                public readonly BoneBodyPartType BodyPart;

                public BodyCapsule(string startBone, string endBone, float radius, BoneBodyPartType bodyPart)
                {
                    StartBone = startBone;
                    EndBone = endBone;
                    Radius = radius;
                    BodyPart = bodyPart;
                }
            }

            private static readonly BodyCapsule[] BodyCapsules =
            {
                new BodyCapsule("head", "", 0.1f, BoneBodyPartType.Head),
                new BodyCapsule("neck", "head", 0.06f, BoneBodyPartType.Neck),
                new BodyCapsule("spine1", "neck", 0.16f, BoneBodyPartType.Chest),
                new BodyCapsule("pelvis", "spine1", 0.15f, BoneBodyPartType.Abdomen),
                new BodyCapsule("l_clavicle", "l_upperarm", 0.07f, BoneBodyPartType.ShoulderLeft),
                new BodyCapsule("r_clavicle", "r_upperarm", 0.07f, BoneBodyPartType.ShoulderRight),
                new BodyCapsule("l_upperarm", "l_forearm", 0.07f, BoneBodyPartType.ArmLeft),
                new BodyCapsule("l_forearm", "l_hand", 0.06f, BoneBodyPartType.ArmLeft),
                new BodyCapsule("l_hand", "l_finger0", 0.06f, BoneBodyPartType.ArmLeft),
                new BodyCapsule("r_upperarm", "r_forearm", 0.07f, BoneBodyPartType.ArmRight),
                new BodyCapsule("r_forearm", "r_hand", 0.06f, BoneBodyPartType.ArmRight),
                new BodyCapsule("r_hand", "r_finger0", 0.06f, BoneBodyPartType.ArmRight),
                new BodyCapsule("l_thigh", "l_calf", 0.09f, BoneBodyPartType.Legs),
                new BodyCapsule("l_calf", "l_foot", 0.07f, BoneBodyPartType.Legs),
                new BodyCapsule("r_thigh", "r_calf", 0.09f, BoneBodyPartType.Legs),
                new BodyCapsule("r_calf", "r_foot", 0.07f, BoneBodyPartType.Legs),
            };

            // Bone name -> index per monster (skeleton), filled on first use. Main thread only. Holds no agents.
            private static readonly Dictionary<string, Dictionary<string, sbyte>> BoneIndexesByMonster = new Dictionary<string, Dictionary<string, sbyte>>();

            private static readonly MethodInfo GetAttackCollisionResultsMethod = AccessTools.Method(typeof(Mission), "GetAttackCollisionResults");
            private static readonly MethodInfo CreateMissileBlowMethod = AccessTools.Method(typeof(Mission), "CreateMissileBlow");
            private static readonly MethodInfo RegisterBlowMethod = AccessTools.Method(typeof(Mission), "RegisterBlow");

            // Called from the MissileHitCallback postfix: main thread, the missile still in the mission's dictionary.
            public static void TryWoundBehindShield(Mission mission, Missile missile, ref AttackCollisionData collisionData, Agent attacker, Agent victim, Vec3 missilePosition, Vec3 missileStartingPosition)
            {
                if (!collisionData.IsMissile || !collisionData.AttackBlockedWithShield || collisionData.CollidedWithShieldOnBack)
                {
                    return;
                }
                if (victim == null || !victim.IsHuman || victim.State != AgentState.Active)
                {
                    return;
                }
                // Same rule as a missile's body hit: no wound on anyone who is not an enemy.
                if (attacker != null && !attacker.IsEnemyOf(victim))
                {
                    return;
                }
                WeaponComponentData pilum = missile.Weapon.CurrentUsageItem;
                if (pilum == null || pilum.WeaponClass != WeaponClass.Javelin || !pilum.WeaponFlags.HasAnyFlag(WeaponFlags.BonusAgainstShield))
                {
                    return;
                }
                // Impale (or another model's flag) already sent this hit down the engine's own penetration.
                WeaponFlags decidedFlags = pilum.WeaponFlags;
                MissionGameModels.Current.AgentApplyDamageModel.DecideMissileWeaponFlags(attacker, missile.Weapon, ref decidedFlags);
                if (decidedFlags.HasAnyFlag(WeaponFlags.CanPenetrateShield))
                {
                    return;
                }
                MissionWeapon shield = victim.WieldedOffhandWeapon;
                // A metal shield turns every missile (RangedRework.Collision.cs).
                if (shield.IsEmpty || shield.CurrentUsageItem == null || !shield.CurrentUsageItem.IsShield || shield.CurrentUsageItem.PhysicsMaterial == "metal_shield")
                {
                    return;
                }

                float penetration = MBMath.ClampFloat(1f - ShieldBaseLoss - ShieldLossPerArmorPoint * shield.GetGetModifiedArmorForCurrentUsage(), 0f, 1f);
                // Arrow Catcher: the holder gives with the hit, so less of it comes through (RBMConfig.ArrowCatcher).
                penetration *= RBMConfig.ArrowCatcher.GetPilumPenetrationFactor(victim);
                if (penetration <= 0f || collisionData.BaseMagnitude <= 0f)
                {
                    return;
                }
                Utilities.RBMComputeDamage(WeaponClass.Javelin.ToString(), (DamageTypes)collisionData.DamageType, collisionData.BaseMagnitude, 0f, 1f, out float unopposed, out _);
                float energyLeft = unopposed * penetration;

                Vec3 direction = collisionData.MissileVelocity;
                if (direction.Normalize() < 0.01f)
                {
                    return;
                }
                Vec3 shankStart = collisionData.CollisionGlobalPosition;
                Vec3 shankEnd = shankStart + direction * (ShankLength * MBMath.ClampFloat(energyLeft / FullDepthEnergy, 0f, 1f));
                if (!FindBodyHit(victim, shankStart, shankEnd, out Vec3 hitPosition, out sbyte hitBone, out BoneBodyPartType hitBodyPart))
                {
                    return;
                }

                AttackCollisionData bodyHit = AttackCollisionData.GetAttackCollisionDataForDebugPurpose(false, false, false, true, false,
                    true, false, collisionData.MissileHasPhysics, collisionData.EntityExists, collisionData.ThrustTipHit, false, false,
                    CombatCollisionResult.StrikeAgent, collisionData.AffectorWeaponSlotOrMissileIndex, collisionData.StrikeType, collisionData.DamageType, hitBone,
                    hitBodyPart, collisionData.AttackBoneIndex, collisionData.AttackDirection, collisionData.PhysicsMaterialIndex, collisionData.CollisionHitResultFlags, collisionData.AttackProgress, collisionData.CollisionDistanceOnWeapon,
                    collisionData.AttackerStunPeriod, collisionData.DefenderStunPeriod, collisionData.MissileTotalDamage, collisionData.MissileStartingBaseSpeed, collisionData.ChargeVelocity, collisionData.FallSpeed, collisionData.WeaponRotUp,
                    collisionData.WeaponBlowDir, hitPosition, collisionData.MissileVelocity, collisionData.MissileStartingPosition, collisionData.VictimAgentCurVelocity, collisionData.CollisionGlobalNormal);

                // The normal missile damage path (magnitude, skill, body part armor), with the share that got through
                // the shield as the momentum left.
                object[] resultArgs = { attacker, victim, WeakGameEntity.Invalid, penetration, missile.Weapon, false, false, false, bodyHit, null, null };
                GetAttackCollisionResultsMethod.Invoke(mission, resultArgs);
                bodyHit = (AttackCollisionData)resultArgs[8];
                CombatLogData combatLog = (CombatLogData)resultArgs[10];
                if (bodyHit.InflictedDamage <= 0)
                {
                    return;
                }

                Blow blow = (Blow)CreateMissileBlowMethod.Invoke(mission, new object[] { attacker, bodyHit, missile.Weapon, missilePosition, missileStartingPosition });
                RegisterBlowMethod.Invoke(mission, new object[] { attacker, victim, WeakGameEntity.Invalid, blow, bodyHit, missile.Weapon, combatLog });
            }

            // The body part the shank behind the shield reaches first, if any.
            private static bool FindBodyHit(Agent victim, Vec3 shankStart, Vec3 shankEnd, out Vec3 hitPosition, out sbyte hitBone, out BoneBodyPartType hitBodyPart)
            {
                hitPosition = shankStart;
                hitBone = -1;
                hitBodyPart = BoneBodyPartType.None;
                Skeleton skeleton = victim.AgentVisuals?.GetSkeleton();
                if (skeleton == null)
                {
                    return false;
                }
                Dictionary<string, sbyte> boneIndexes = GetBoneIndexes(victim.Monster, skeleton);
                MatrixFrame globalFrame = victim.AgentVisuals.GetGlobalFrame();
                float firstAlongShank = float.MaxValue;
                foreach (BodyCapsule capsule in BodyCapsules)
                {
                    if (!boneIndexes.TryGetValue(capsule.StartBone, out sbyte startBone))
                    {
                        continue;
                    }
                    Vec3 start = globalFrame.TransformToParent(skeleton.GetBoneEntitialFrameWithIndex(startBone).origin);
                    Vec3 end;
                    if (capsule.EndBone.Length > 0)
                    {
                        if (!boneIndexes.TryGetValue(capsule.EndBone, out sbyte endBone))
                        {
                            continue;
                        }
                        end = globalFrame.TransformToParent(skeleton.GetBoneEntitialFrameWithIndex(endBone).origin);
                    }
                    else
                    {
                        Vec3 up = Vec3.Up;
                        if (boneIndexes.TryGetValue("neck", out sbyte neckBone))
                        {
                            up = start - globalFrame.TransformToParent(skeleton.GetBoneEntitialFrameWithIndex(neckBone).origin);
                            if (up.Normalize() < 0.001f)
                            {
                                up = Vec3.Up;
                            }
                        }
                        end = start + up * SkullHeight;
                    }

                    float distance = SegmentDistance(shankStart, shankEnd, start, end, out float alongShank, out Vec3 pointOnBody);
                    if (distance <= capsule.Radius && alongShank < firstAlongShank)
                    {
                        firstAlongShank = alongShank;
                        hitPosition = pointOnBody;
                        hitBone = startBone;
                        hitBodyPart = capsule.BodyPart;
                    }
                }
                return hitBone >= 0;
            }

            private static Dictionary<string, sbyte> GetBoneIndexes(Monster monster, Skeleton skeleton)
            {
                if (!BoneIndexesByMonster.TryGetValue(monster.StringId, out Dictionary<string, sbyte> boneIndexes))
                {
                    boneIndexes = new Dictionary<string, sbyte>();
                    sbyte boneCount = skeleton.GetBoneCount();
                    for (sbyte boneIndex = 0; boneIndex < boneCount; boneIndex++)
                    {
                        boneIndexes[skeleton.GetBoneName(boneIndex)] = boneIndex;
                    }
                    BoneIndexesByMonster[monster.StringId] = boneIndexes;
                }
                return boneIndexes;
            }

            // Shortest distance between segments a and b; alongA (0..1) and pointOnB mark where they come closest.
            private static float SegmentDistance(Vec3 aStart, Vec3 aEnd, Vec3 bStart, Vec3 bEnd, out float alongA, out Vec3 pointOnB)
            {
                Vec3 a = aEnd - aStart;
                Vec3 b = bEnd - bStart;
                Vec3 r = aStart - bStart;
                float aa = Vec3.DotProduct(a, a);
                float bb = Vec3.DotProduct(b, b);
                float br = Vec3.DotProduct(b, r);
                float s;
                float t;
                if (aa <= 1e-6f && bb <= 1e-6f)
                {
                    s = 0f;
                    t = 0f;
                }
                else if (aa <= 1e-6f)
                {
                    s = 0f;
                    t = MBMath.ClampFloat(br / bb, 0f, 1f);
                }
                else
                {
                    float ar = Vec3.DotProduct(a, r);
                    if (bb <= 1e-6f)
                    {
                        t = 0f;
                        s = MBMath.ClampFloat(-ar / aa, 0f, 1f);
                    }
                    else
                    {
                        float ab = Vec3.DotProduct(a, b);
                        float denominator = aa * bb - ab * ab;
                        s = denominator > 1e-6f ? MBMath.ClampFloat((ab * br - ar * bb) / denominator, 0f, 1f) : 0f;
                        t = (ab * s + br) / bb;
                        if (t < 0f)
                        {
                            t = 0f;
                            s = MBMath.ClampFloat(-ar / aa, 0f, 1f);
                        }
                        else if (t > 1f)
                        {
                            t = 1f;
                            s = MBMath.ClampFloat((ab - ar) / aa, 0f, 1f);
                        }
                    }
                }
                alongA = s;
                pointOnB = bStart + b * t;
                return (aStart + a * s - pointOnB).Length;
            }
        }
    }
}
