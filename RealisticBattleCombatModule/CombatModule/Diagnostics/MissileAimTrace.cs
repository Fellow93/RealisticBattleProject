using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMCombat
{
    /// <summary>
    /// DIAGNOSTIC: where an AI bow/crossbow/sling shot was aimed, where RBM's launch should have carried it, and where it
    /// actually came down. Written into the battle hit log (same toggle) as SHOT / LAND line pairs keyed by missile.
    ///
    /// Reading a pair (all ranges are metres along the shot's horizontal direction from the launch point):
    ///   tgtRange  - the target's chest at the moment of the shot
    ///   predRange - where a drag model with the flying missile's CURRENT managed friction says it crosses the target's
    ///               chest height. predRange ~ tgtRange means the aim was right for the launch RBM gave it.
    ///   landRange - where it actually collided.
    /// Aim wrong: predRange short of tgtRange. Flight differs from the model: predRange ~ tgtRange, landRange short.
    /// Main thread only: OnAgentShootMissile and HandleMissileCollisionReaction are both engine callbacks on it.
    /// </summary>
    internal static class MissileAimTrace
    {
        private struct Shot
        {
            public Vec3 Launch;
            public Vec2 Dir;
            public float TgtRange;
            public float TgtChestZ;
            public Agent Target;
            public float Time;
        }

        private static readonly Dictionary<int, Shot> _shots = new Dictionary<int, Shot>();
        private static bool _hasPending;
        private static Shot _pending;
        private static string _pendingLine;

        public static void Reset()
        {
            _shots.Clear();
            _hasPending = false;
        }

        /// <summary>Called from the shot prefix after RBM rescaled the launch. engineVelocity is what the AI aimed with.</summary>
        public static void BeginShot(Agent shooter, WeaponClass launcherClass, Vec3 position, Vec3 engineVelocity, Vec3 finalVelocity, int rbmSpeed, float ammoWeight, float friction)
        {
            _hasPending = false;
            if (!BattleHitLog.IsEnabled || shooter == null || !shooter.IsAIControlled || Mission.Current == null)
            {
                return;
            }
            Agent target = shooter.GetTargetAgent();
            if (target == null || !target.IsActive())
            {
                return;
            }

            Vec2 dir = finalVelocity.AsVec2;
            if (dir.Normalize() < 0.0001f)
            {
                return;
            }
            Vec3 chest = target.GetChestGlobalPosition();
            float tgtRange = Vec2.DotProduct(chest.AsVec2 - position.AsVec2, dir);
            float predRange = PredictRangeAtHeight(position, finalVelocity, chest.z, friction);

            float engineSpeed = engineVelocity.Length;
            float enginePitch = engineSpeed > 0f ? (float)System.Math.Asin(MBMath.ClampFloat(engineVelocity.z / engineSpeed, -1f, 1f)) * 57.29578f : 0f;
            Vec3 tv = target.Velocity;

            _pending = new Shot { Launch = position, Dir = dir, TgtRange = tgtRange, TgtChestZ = chest.z, Target = target, Time = Mission.Current.CurrentTime };
            _pendingLine = "SHOT " + launcherClass
                + " shooter=" + shooter.Index + (shooter.HasMount ? "(mounted)" : "")
                + " launcher=" + (shooter.WieldedWeapon.Item != null ? shooter.WieldedWeapon.Item.StringId : "?")
                + " ammoKg=" + BattleHitLog.Fmt(ammoWeight)
                + " aimSpeed=" + BattleHitLog.Fmt(engineSpeed)
                + " rbmSpeed=" + rbmSpeed
                + " launchSpeed=" + BattleHitLog.Fmt(finalVelocity.Length)
                + " pitch=" + BattleHitLog.Fmt(enginePitch)
                + " tgtRange=" + BattleHitLog.Fmt(tgtRange)
                + " dz=" + BattleHitLog.Fmt(chest.z - position.z)
                + " tgtSpeed=" + BattleHitLog.Fmt(tv.AsVec2.Length)
                + " tgtClosing=" + BattleHitLog.Fmt(-Vec2.DotProduct(tv.AsVec2, dir))
                + " friction=" + friction.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture)
                + " predRange=" + (predRange < 0f ? "unreached" : BattleHitLog.Fmt(predRange))
                + " realReach=" + BattleHitLog.Fmt(RBMConfig.MissileBallistics.MaxReach(rbmSpeed, chest.z - position.z, friction))                + " engineMaxRange=" + BattleHitLog.Fmt(shooter.MaximumMissileRange)
                + " engineRangeAtTgtZ=" + BattleHitLog.Fmt(shooter.GetMissileRangeWithHeightDifferenceAux(chest.z))
                + " ignoreAmmoLimit=" + ((shooter.GetScriptedCombatFlags() & Agent.AISpecialCombatModeFlags.IgnoreAmmoLimitForRangeCalculation) != 0);
            _hasPending = true;
        }

        /// <summary>Called from the shot postfix: the missile vanilla just added is the last in the list.</summary>
        public static void CommitShot(Mission mission)
        {
            if (!_hasPending)
            {
                return;
            }
            _hasPending = false;
            if (mission.MissilesList.Count == 0)
            {
                return;
            }
            int index = mission.MissilesList[mission.MissilesList.Count - 1].Index;
            _shots[index] = _pending;
            BattleHitLog.Write(_pendingLine + " missile=" + index);
        }

        /// <summary>Called on the missile's first collision (ground, object, agent or shield).</summary>
        public static void Land(Mission mission, int missileIndex, Mission.Missile missile, Agent attachedAgent, bool attachedToShield)
        {
            Shot shot;
            if (!_shots.TryGetValue(missileIndex, out shot))
            {
                return;
            }
            _shots.Remove(missileIndex);

            Vec3 impact = missile.GetPosition();
            float landRange = Vec2.DotProduct(impact.AsVec2 - shot.Launch.AsVec2, shot.Dir);
            string tgtNow = "dead";
            if (shot.Target != null && shot.Target.IsActive())
            {
                tgtNow = BattleHitLog.Fmt(Vec2.DotProduct(shot.Target.GetChestGlobalPosition().AsVec2 - shot.Launch.AsVec2, shot.Dir));
            }
            string hit = attachedAgent == null ? "ground/object" : (attachedAgent == shot.Target ? "TARGET" : "other-agent") + (attachedToShield ? "-shield" : "");
            BattleHitLog.Write("LAND missile=" + missileIndex
                + " flight=" + BattleHitLog.Fmt(mission.CurrentTime - shot.Time)
                + " hit=" + hit
                + " landRange=" + BattleHitLog.Fmt(landRange)
                + " tgtRange@shot=" + BattleHitLog.Fmt(shot.TgtRange)
                + " tgtRange@land=" + tgtNow
                + " short=" + BattleHitLog.Fmt(shot.TgtRange - landRange)
                + " landDzToTgtChest=" + BattleHitLog.Fmt(impact.z - shot.TgtChestZ));
        }

        /// <summary>
        /// Horizontal range at which the missile, descending, crosses height z under gravity and quadratic drag
        /// (the engine's documented law: speed -= k * speed^2 * dt). -1 if it never gets there.
        /// </summary>
        private static float PredictRangeAtHeight(Vec3 start, Vec3 v, float z, float k)
        {
            const float dt = 0.002f;
            const float g = 9.806f;
            Vec3 p = start;
            Vec2 dir = v.AsVec2;
            dir.Normalize();
            for (int i = 0; i < 10000; i++)
            {
                float speed = v.Length;
                Vec3 prev = p;
                v -= v * (k * speed * dt);
                v.z -= g * dt;
                p += v * dt;
                if (v.z < 0f && p.z <= z)
                {
                    float t = (prev.z - z) / (prev.z - p.z);
                    Vec3 cross = prev + (p - prev) * t;
                    return Vec2.DotProduct(cross.AsVec2 - start.AsVec2, dir);
                }
            }
            return -1f;
        }
    }
}
