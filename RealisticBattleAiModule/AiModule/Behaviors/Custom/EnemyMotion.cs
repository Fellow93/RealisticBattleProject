using System.Collections.Concurrent;
using System.Collections.Generic;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// The shared, SAMPLED motion of formations that every closing / coming-in check reads instead of native's
    /// CachedCurrentVelocity (a ~0.1 s difference of the unit average that jumps when men die, join or the block reshapes).
    /// A formation's centre is sampled at most once per SampleMinSeconds by whoever asks first (ReactionMonitor touches every
    /// enemy formation each 1 s check); velocity = mean of the last step and the previous estimate (ReactionMonitor's rule),
    /// none after a gap over SampleMaxGap. Observer-independent: one immutable sample per formation, replaced by
    /// compare-and-swap. Any thread; cached formation data only. Cleared in MissionStartReset.
    /// </summary>
    internal static class EnemyMotion
    {
        internal const float SampleMinSeconds = 0.9f;
        private const float SampleMaxGap = 5f; // older samples are no base for a velocity
        // faster than any horse: the centre jumped (men moved between formations, an emptied formation refilled elsewhere), not a ride
        internal const float MaxPlausibleSpeed = 20f;

        private sealed class Sample
        {
            public readonly Vec2 Position;
            public readonly Vec2 Velocity;
            public readonly float Time;
            public readonly bool HasVelocity;

            public Sample(Vec2 position, Vec2 velocity, float time, bool hasVelocity)
            {
                Position = position;
                Velocity = velocity;
                Time = time;
                HasVelocity = hasVelocity;
            }
        }

        // keyed by reference: native Formation.GetHashCode throws for team-less simulation formations
        private sealed class ReferenceComparer : IEqualityComparer<Formation>
        {
            public bool Equals(Formation x, Formation y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(Formation obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }

        private static readonly ConcurrentDictionary<Formation, Sample> Samples = new ConcurrentDictionary<Formation, Sample>(new ReferenceComparer());

        internal static void Clear()
        {
            Samples.Clear();
        }

        /// <summary>Takes a sample now when due (also starts tracking a formation not asked for before).</summary>
        internal static void Touch(Formation formation, float now)
        {
            Current(formation, now);
        }

        /// <summary>The sampled velocity (m/s); false without one yet.</summary>
        internal static bool TryVelocity(Formation formation, out Vec2 velocity)
        {
            Mission mission = Mission.Current;
            Sample s = (mission != null) ? Current(formation, mission.CurrentTime) : null;
            if (s == null || !s.HasVelocity)
            {
                velocity = Vec2.Zero;
                return false;
            }
            velocity = s.Velocity;
            return true;
        }

        /// <summary>The sampled velocity, Zero without one.</summary>
        internal static Vec2 Velocity(Formation formation)
        {
            Vec2 velocity;
            TryVelocity(formation, out velocity);
            return velocity;
        }

        /// <summary>The sampled speed (m/s) of mover toward point (from its centre now); 0 without a velocity.</summary>
        internal static float ClosingSpeed(Formation mover, Vec2 point)
        {
            Vec2 velocity;
            if (mover == null || !point.IsValid || !TryVelocity(mover, out velocity))
            {
                return 0f;
            }
            Vec2 c = RBMAI.Utilities.GetFormationCenter(mover);
            if (!c.IsValid)
            {
                return 0f;
            }
            Vec2 to = point - c;
            float length = to.Length;
            return (length < 0.5f) ? 0f : Vec2.DotProduct(velocity, to) / length;
        }

        private static Sample Current(Formation formation, float now)
        {
            if (formation == null)
            {
                return null;
            }
            if (formation.CountOfUnits <= 0)
            {
                // a refill (reinforcements into an emptied formation) starts fresh, not from where its last men stood
                Samples.TryRemove(formation, out _);
                return null;
            }
            Sample s;
            Samples.TryGetValue(formation, out s);
            if (s != null && now - s.Time < SampleMinSeconds && now >= s.Time)
            {
                return s;
            }
            Vec2 c = RBMAI.Utilities.GetFormationCenter(formation);
            if (!c.IsValid)
            {
                return s;
            }
            Sample next = Step(s, c, now);
            if (s == null ? Samples.TryAdd(formation, next) : Samples.TryUpdate(formation, next, s))
            {
                return next;
            }
            Samples.TryGetValue(formation, out s);
            return s;
        }

        /// <summary>The next sample from the previous one and the centre now; a gap or an implausible jump restarts without a velocity.</summary>
        private static Sample Step(Sample s, Vec2 centre, float now)
        {
            if (s == null || now < s.Time)
            {
                return new Sample(centre, Vec2.Zero, now, false);
            }
            float dt = now - s.Time;
            Vec2 v;
            if (!TryStepVelocity(s.Position, centre, dt, out v))
            {
                return new Sample(centre, Vec2.Zero, now, false);
            }
            return new Sample(centre, s.HasVelocity ? (s.Velocity + v) * 0.5f : v, now, true);
        }

        /// <summary>Pure: the step velocity between two samples dt apart; false for no time, a gap over SampleMaxGap or a jump over MaxPlausibleSpeed.</summary>
        internal static bool TryStepVelocity(Vec2 from, Vec2 to, float dt, out Vec2 velocity)
        {
            velocity = Vec2.Zero;
            if (!(dt > 0f) || dt > SampleMaxGap)
            {
                return false;
            }
            Vec2 v = (to - from) * (1f / dt);
            if (v.LengthSquared > MaxPlausibleSpeed * MaxPlausibleSpeed)
            {
                return false;
            }
            velocity = v;
            return true;
        }
    }
}
