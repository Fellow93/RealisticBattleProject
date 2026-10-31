using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace RBMAI
{
    /// <summary>
    /// A behavior's Move point: a copy of a cached WorldPosition with ONE SetVec2MT, kept until the wanted point moved more than RebuildDistance
    /// and RebuildSeconds passed since the last build (or a forced rebuild). AI thread, one instance per behavior; no engine query.
    /// </summary>
    internal sealed class CachedMovePoint
    {
        internal const float RebuildDistance = 5f;
        internal const float RebuildSeconds = 2f;

        private WorldPosition _point = WorldPosition.Invalid;
        private Vec2 _vec = Vec2.Invalid;
        private float _builtAt = float.NegativeInfinity;
        private bool _has;

        internal WorldPosition Point => _point;

        internal void Reset()
        {
            _has = false;
            _point = WorldPosition.Invalid;
        }

        /// <summary>The point for desired: rebuilt from the first source with a valid position (requireScene: WorldPosition.IsValid, else a valid Vec2)
        /// when forced, none is held (or, with requireScene, the held one is invalid), or desired moved past RebuildDistance after RebuildSeconds.
        /// No usable source: keepOnFail keeps a held point, else false.</summary>
        internal bool TryGet(Vec2 desired, float now, WorldPosition first, WorldPosition second, bool requireScene, bool keepOnFail, bool force, out WorldPosition point)
        {
            bool rebuilt;
            return TryGet(desired, now, first, second, requireScene, keepOnFail, force, RebuildDistance, RebuildSeconds, out point, out rebuilt);
        }

        /// <summary>As above with the caller's rebuild distance / seconds (seconds 0 = on distance alone); one source, keepOnFail; rebuilt = built this call (the caller's own cached extras follow it).</summary>
        internal bool TryGet(Vec2 desired, float now, WorldPosition source, bool force, float rebuildDistance, float rebuildSeconds, out WorldPosition point, out bool rebuilt)
        {
            return TryGet(desired, now, source, source, false, true, force, rebuildDistance, rebuildSeconds, out point, out rebuilt);
        }

        /// <summary>True when the held point still serves desired (no rebuild due; requireScene: the held one is scene-valid).</summary>
        internal bool IsFresh(Vec2 desired, float now, bool requireScene, float rebuildDistance = RebuildDistance, float rebuildSeconds = RebuildSeconds)
        {
            return _has && (!requireScene || _point.IsValid)
                && !(_vec.DistanceSquared(desired) > rebuildDistance * rebuildDistance && now - _builtAt >= rebuildSeconds);
        }

        /// <summary>The desired point of the last build, kept through Reset (Invalid before the first build and after Clear).</summary>
        internal Vec2 Desired => _vec;

        /// <summary>Reset and forget the last build's point and time.</summary>
        internal void Clear()
        {
            Reset();
            _vec = Vec2.Invalid;
            _builtAt = float.NegativeInfinity;
        }

        /// <summary>The full form: the caller's rebuild distance / seconds; rebuilt = built this call.</summary>
        internal bool TryGet(Vec2 desired, float now, WorldPosition first, WorldPosition second, bool requireScene, bool keepOnFail, bool force,
            float rebuildDistance, float rebuildSeconds, out WorldPosition point, out bool rebuilt)
        {
            rebuilt = false;
            bool rebuild = force || !_has || (requireScene && !_point.IsValid)
                || (_vec.DistanceSquared(desired) > rebuildDistance * rebuildDistance && now - _builtAt >= rebuildSeconds);
            if (rebuild)
            {
                WorldPosition source = Usable(first, requireScene) ? first : second;
                if (!Usable(source, requireScene))
                {
                    point = _point;
                    return keepOnFail && _has;
                }
                source.SetVec2MT(desired);
                _point = source;
                _vec = desired;
                _builtAt = now;
                _has = true;
                rebuilt = true;
            }
            point = _point;
            return true;
        }

        private static bool Usable(WorldPosition p, bool requireScene)
        {
            return requireScene ? p.IsValid : p.AsVec2.IsValid;
        }
    }
}
