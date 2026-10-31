using System;
using System.Globalization;
using System.Threading.Tasks;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>The shared gate of every terrain plan: plans are read only with terrainTacticsEnabled and for a side ArmyScale lets plan. Any thread.</summary>
    internal static class TerrainPlans
    {
        internal static bool Active(BattleSideEnum side)
        {
            return RBMConfig.RBMConfig.terrainTacticsEnabled && !ArmyScale.NoPlans(side);
        }

        internal static bool Active(Team team)
        {
            return team != null && Active(team.Side);
        }

        /// <summary>At least one side may use terrain plans (re-plan work worth doing).</summary>
        internal static bool AnyActive => Active(BattleSideEnum.Attacker) || Active(BattleSideEnum.Defender);
    }

    /// <summary>
    /// One terrain plan per side: written on the MAIN thread (publish, re-plans), read anywhere through one volatile read of an
    /// immutable plan. ForSide / ForTeam are gated by TerrainPlans.Active; Published is not (log and map).
    /// </summary>
    internal sealed class PlanSlot<T> where T : class
    {
        private volatile T _attacker;
        private volatile T _defender;

        internal T Attacker => _attacker;

        internal T Defender => _defender;

        internal bool IsEmpty => _attacker == null && _defender == null;

        internal T Published(BattleSideEnum side)
        {
            return (side == BattleSideEnum.Attacker) ? _attacker : (side == BattleSideEnum.Defender) ? _defender : null;
        }

        internal T ForSide(BattleSideEnum side)
        {
            return TerrainPlans.Active(side) ? Published(side) : null;
        }

        internal T ForTeam(Team team)
        {
            return (team != null) ? ForSide(team.Side) : null;
        }

        internal void Publish(T attacker, T defender)
        {
            _attacker = attacker;
            _defender = defender;
        }

        internal void Set(BattleSideEnum side, T plan)
        {
            if (side == BattleSideEnum.Attacker)
            {
                _attacker = plan;
            }
            else if (side == BattleSideEnum.Defender)
            {
                _defender = plan;
            }
        }

        internal void Clear()
        {
            _attacker = null;
            _defender = null;
        }
    }

    /// <summary>
    /// A terrain re-plan on a background Task (MAIN thread only: Start / TryFinish / Clear). The work must read immutable data
    /// only (no engine call). Clear abandons a running job by generation and observes a late fault.
    /// </summary>
    internal sealed class ReplanJob<TResult> where TResult : class
    {
        internal enum Outcome
        {
            Pending,
            Dropped,
            Failed,
            Done
        }

        private readonly string _canceledText;
        private int _generation;
        private Task<TResult> _task;
        private int _taskGeneration;
        private long _startTicks;

        internal ReplanJob(string canceledText)
        {
            _canceledText = canceledText;
        }

        internal bool Running => _task != null;

        /// <summary>The running (or last finished) job's reason.</summary>
        internal string Reason { get; private set; }

        internal void Start(string reason, Func<TResult> work)
        {
            Reason = reason;
            _taskGeneration = _generation;
            _startTicks = DateTime.UtcNow.Ticks;
            _task = Task.Run(work);
        }

        /// <summary>Pending while running; Dropped (stale generation); Failed (failure = "Type:message"); Done with its result. ms = wall time of the job.</summary>
        internal Outcome TryFinish(out TResult result, out string ms, out string failure)
        {
            result = null;
            ms = null;
            failure = null;
            Task<TResult> task = _task;
            if (task == null || !task.IsCompleted)
            {
                return Outcome.Pending;
            }
            _task = null;
            if (_taskGeneration != _generation)
            {
                return Outcome.Dropped;
            }
            ms = ((DateTime.UtcNow.Ticks - _startTicks) / (double)TimeSpan.TicksPerMillisecond).ToString("0", CultureInfo.InvariantCulture);
            if (task.IsFaulted || task.IsCanceled)
            {
                Exception e = (task.Exception != null) ? task.Exception.GetBaseException() : new OperationCanceledException(_canceledText);
                failure = e.GetType().Name + ":" + BattleTerrainRead.Sanitize(e.Message);
                return Outcome.Failed;
            }
            result = task.Result;
            return Outcome.Done;
        }

        internal void Clear()
        {
            _generation++;
            if (_task != null)
            {
                // a late fault must never be reported as an unobserved task exception
                _task.ContinueWith(t => { var unused = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                _task = null;
            }
            Reason = null;
        }
    }
}
