using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.MountAndBlade.Mission;

namespace RBMCombat
{
    public partial class RangedRework
    {
        // An arrow or bolt that does not get through the armor at all (only blunt trauma, no penetrated damage in
        // RBMComputeDamage) does not stick in the man: it breaks on the armor or glances off it.
        //
        // The missile's reaction is decided inside the engine's MissileHitCallback, before RBM's damage is known to
        // anything outside it, so the pieces are gathered across one callback: MissileHitCallbackPatch opens it with
        // the collision, DamageRework.Core reports the body hit's penetration, and HandleMissileCollisionReactionPatch
        // swaps a Stick for a break or a bounce. Main thread only.
        internal static class ArmorDeflection
        {
            private const float BreakChance = 0.7f;

            private static bool _inMissileHit;
            private static bool _stoppedByArmor;
            private static AttackCollisionData _collisionData;
            private static Vec3 _missileAngularVelocity;
            private static MatrixFrame _attachGlobalFrame;

            public static void BeginMissileHit(in AttackCollisionData collisionData, Vec3 missileAngularVelocity, MatrixFrame attachGlobalFrame)
            {
                _inMissileHit = true;
                _stoppedByArmor = false;
                _collisionData = collisionData;
                _missileAngularVelocity = missileAngularVelocity;
                _attachGlobalFrame = attachGlobalFrame;
            }

            public static void EndMissileHit()
            {
                _inMissileHit = false;
                _stoppedByArmor = false;
            }

            // From the missile body hit's damage: penetratedDamage before body part and difficulty multipliers.
            public static void RecordBodyHit(float penetratedDamage)
            {
                _stoppedByArmor = _inMissileHit && penetratedDamage <= 0f;
            }

            public static void Apply(Mission mission, Missile missile, ref MissileCollisionReaction collisionReaction, Agent attachedAgent, bool attachedToShield, ref MatrixFrame attachLocalFrame, ref bool isAttachedFrameLocal, ref Vec3 bounceBackVelocity, ref Vec3 bounceBackAngularVelocity)
            {
                if (!_inMissileHit || !_stoppedByArmor || collisionReaction != MissileCollisionReaction.Stick || attachedAgent == null || attachedToShield)
                {
                    return;
                }
                _stoppedByArmor = false;
                WeaponComponentData missileItem = missile.Weapon.CurrentUsageItem;
                if (missileItem == null || (missileItem.WeaponClass != WeaponClass.Arrow && missileItem.WeaponClass != WeaponClass.Bolt))
                {
                    return;
                }

                if (MBRandom.RandomFloat < BreakChance)
                {
                    // Vanilla's broken arrow: the missile vanishes in its broken-arrow particles.
                    collisionReaction = MissileCollisionReaction.BecomeInvisible;
                    mission.AddParticleSystemBurstByName("psys_game_broken_arrow", new MatrixFrame(Mat3.Identity, _collisionData.CollisionGlobalPosition), false);
                    return;
                }

                // Vanilla's bounce: the frame it drops the missile at and the velocity it throws it off with.
                collisionReaction = MissileCollisionReaction.BounceBack;
                MatrixFrame startingFrame = missileItem.GetMissileStartingFrame();
                attachLocalFrame = _attachGlobalFrame.TransformToParent(startingFrame.TransformToParent(missileItem.StickingFrame)).TransformToParent(startingFrame);
                attachLocalFrame.origin.z = MathF.Max(attachLocalFrame.origin.z, -100f);
                isAttachedFrameLocal = false;
                missile.CalculateBounceBackVelocity(_missileAngularVelocity, _collisionData, out bounceBackVelocity, out bounceBackAngularVelocity);
            }
        }
    }
}
