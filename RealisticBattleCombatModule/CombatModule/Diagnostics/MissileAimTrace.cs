using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMCombat
{
    /// <summary>
    /// DIAGNOSTIC: where an AI bow/crossbow/sling shot was aimed, where RBM's launch should have carried it, and where it
    /// actually came down. Written into the battle hit log (same toggle) as SHOT / LAND line pairs keyed by missile.
    /// AI throws (javelin, throwing axe/knife, stone) are traced the same way as THROW / LAND pairs (see BeginThrow).
    ///
    /// Reading a pair (all ranges are metres along the shot's horizontal direction from the launch point):
    ///   tgtRange  - the target's chest at the moment of the shot
    ///   predRange - where a drag model with the flying missile's CURRENT managed friction says it crosses the target's
    ///               chest height. predRange ~ tgtRange means the aim was right for the launch RBM gave it.
    ///   landRange - where it actually collided.
    /// Aim wrong: predRange short of tgtRange. Flight differs from the model: predRange ~ tgtRange, landRange short.
    /// The player's shots are traced separately when the aim arc is on: ARCSHOT (RangedAimArcView) / ARCLAND pairs.
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

        // A player shot the aim arc (RangedAimArcView) predicted: where the arc said it would come down.
        private struct ArcShot
        {
            public Vec3 Launch;
            public Vec2 Dir;
            public Vec3 PredImpact;
            public float Time;
        }

        private static readonly Dictionary<int, Shot> _shots = new Dictionary<int, Shot>();
        private static readonly Dictionary<int, ArcShot> _arcShots = new Dictionary<int, ArcShot>();
        private static bool _hasPending;
        private static Shot _pending;
        private static string _pendingLine;

        public static void Reset()
        {
            _shots.Clear();
            _arcShots.Clear();
            _hasPending = false;
        }

        /// <summary>From RangedAimArcView's shot callback: pairs the player's missile with the arc's predicted impact.</summary>
        public static void BeginArcShot(int missileIndex, Vec3 launch, Vec3 direction, Vec3 predImpact, float time)
        {
            Vec2 dir = direction.AsVec2;
            if (!BattleHitLog.IsEnabled || dir.Normalize() < 0.0001f)
            {
                return;
            }
            _arcShots[missileIndex] = new ArcShot { Launch = launch, Dir = dir, PredImpact = predImpact, Time = time };
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

        /// <summary>
        /// Called from the shot prefix for an AI javelin/axe/knife/stone throw. engineVelocity is the throw the AI
        /// aimed, finalVelocity the one launched (the same since RBM's AI release-angle tweaks were removed; kept apart
        /// so a future change to the launch shows up). predRange is where the drag model says the throw crosses the
        /// target's chest height: predRange ~ tgtRange means the AI aimed right; LAND's landRange shows where it
        /// really came down. Logged with or without a target agent; without one the target fields read "none".
        /// </summary>
        public static void BeginThrow(Agent shooter, WeaponClass weaponClass, Vec3 position, Vec3 engineVelocity, Vec3 finalVelocity, float friction)
        {
            _hasPending = false;
            if (!BattleHitLog.IsEnabled || shooter == null || !shooter.IsAIControlled || Mission.Current == null)
            {
                return;
            }
            Vec2 dir = finalVelocity.AsVec2;
            if (dir.Normalize() < 0.0001f)
            {
                return;
            }
            Agent target = shooter.GetTargetAgent();
            if (target != null && !target.IsActive())
            {
                target = null;
            }

            float engineSpeed = engineVelocity.Length;
            float finalSpeed = finalVelocity.Length;
            float aimPitch = engineSpeed > 0f ? (float)System.Math.Asin(MBMath.ClampFloat(engineVelocity.z / engineSpeed, -1f, 1f)) * 57.29578f : 0f;
            float launchPitch = finalSpeed > 0f ? (float)System.Math.Asin(MBMath.ClampFloat(finalVelocity.z / finalSpeed, -1f, 1f)) * 57.29578f : 0f;
            string line = "THROW " + weaponClass
                + " shooter=" + shooter.Index + (shooter.HasMount ? "(mounted)" : "")
                + " weapon=" + (shooter.WieldedWeapon.Item != null ? shooter.WieldedWeapon.Item.StringId : "?")
                + " aimSpeed=" + BattleHitLog.Fmt(engineSpeed)
                + " launchSpeed=" + BattleHitLog.Fmt(finalSpeed)
                + " aimPitch=" + BattleHitLog.Fmt(aimPitch)
                + " launchPitch=" + BattleHitLog.Fmt(launchPitch)
                + " shooterSpeed=" + BattleHitLog.Fmt(shooter.Velocity.AsVec2.Length)
                // 0 while RBMAI's RangedReachGate holds him: a throw logged with 0 slipped past the gate.
                + " shootFreq=" + BattleHitLog.Fmt(shooter.AgentDrivenProperties.AiShootFreq)
                + " friction=" + friction.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);

            Shot shot = new Shot { Launch = position, Dir = dir, Target = target, Time = Mission.Current.CurrentTime };
            if (target != null)
            {
                Vec3 chest = target.GetChestGlobalPosition();
                Vec3 tv = target.Velocity;
                shot.TgtRange = Vec2.DotProduct(chest.AsVec2 - position.AsVec2, dir);
                shot.TgtChestZ = chest.z;
                float predRange = PredictRangeAtHeight(position, finalVelocity, chest.z, friction);
                line += " tgtRange=" + BattleHitLog.Fmt(shot.TgtRange)
                    + " dz=" + BattleHitLog.Fmt(chest.z - position.z)
                    + " tgtSpeed=" + BattleHitLog.Fmt(tv.AsVec2.Length)
                    + " tgtClosing=" + BattleHitLog.Fmt(-Vec2.DotProduct(tv.AsVec2, dir))
                    + " predRange=" + (predRange < 0f ? "unreached" : BattleHitLog.Fmt(predRange));
            }
            else
            {
                shot.TgtChestZ = position.z;
                line += " tgtRange=none";
            }
            _pending = shot;
            _pendingLine = line;
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
            ArcShot arc;
            if (_arcShots.TryGetValue(missileIndex, out arc))
            {
                _arcShots.Remove(missileIndex);
                Vec3 at = missile.GetPosition();
                float arcLandRange = Vec2.DotProduct(at.AsVec2 - arc.Launch.AsVec2, arc.Dir);
                float arcPredRange = Vec2.DotProduct(arc.PredImpact.AsVec2 - arc.Launch.AsVec2, arc.Dir);
                BattleHitLog.Write("ARCLAND missile=" + missileIndex
                    + " flight=" + BattleHitLog.Fmt(mission.CurrentTime - arc.Time)
                    + " hit=" + (attachedAgent == null ? "ground/object" : "agent" + (attachedToShield ? "-shield" : ""))
                    + " landRange=" + BattleHitLog.Fmt(arcLandRange)
                    + " predRange=" + BattleHitLog.Fmt(arcPredRange)
                    + " short=" + BattleHitLog.Fmt(arcPredRange - arcLandRange)
                    + " miss=" + BattleHitLog.Fmt(at.Distance(arc.PredImpact))
                    + " dzToPred=" + BattleHitLog.Fmt(at.z - arc.PredImpact.z));
            }

            Shot shot;
            if (!_shots.TryGetValue(missileIndex, out shot))
            {
                return;
            }
            _shots.Remove(missileIndex);

            Vec3 impact = missile.GetPosition();
            float landRange = Vec2.DotProduct(impact.AsVec2 - shot.Launch.AsVec2, shot.Dir);
            string tgtNow = shot.Target == null ? "none" : "dead";
            if (shot.Target != null && shot.Target.IsActive())
            {
                tgtNow = BattleHitLog.Fmt(Vec2.DotProduct(shot.Target.GetChestGlobalPosition().AsVec2 - shot.Launch.AsVec2, shot.Dir));
            }
            string hit = attachedAgent == null ? "ground/object" : (attachedAgent == shot.Target ? "TARGET" : "other-agent") + (attachedToShield ? "-shield" : "");
            BattleHitLog.Write("LAND missile=" + missileIndex
                + " flight=" + BattleHitLog.Fmt(mission.CurrentTime - shot.Time)
                + " hit=" + hit
                + " landRange=" + BattleHitLog.Fmt(landRange)
                + (shot.Target == null
                    ? " tgtRange@shot=none landDzToLaunch=" + BattleHitLog.Fmt(impact.z - shot.TgtChestZ)
                    : " tgtRange@shot=" + BattleHitLog.Fmt(shot.TgtRange)
                        + " tgtRange@land=" + tgtNow
                        + " short=" + BattleHitLog.Fmt(shot.TgtRange - landRange)
                        + " landDzToTgtChest=" + BattleHitLog.Fmt(impact.z - shot.TgtChestZ)));
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
