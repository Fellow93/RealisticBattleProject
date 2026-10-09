using HarmonyLib;
using System;
using System.Collections.Concurrent;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    // Staggered order reaction, second half (orderReactionDelayEnabled). FormationShoutsLogic.StaggerReaction gives
    // each man a per-man decide time when the player gives an order. That delays Charge -> Move, but NOT Move -> Move
    // (new position/direction/arrangement): the formation frame (slot position + direction) is pushed to the engine per
    // agent and the engine walks there at once, whatever the decide time. So here each man keeps getting his OLD frame
    // until his own delay (FormationShoutsLogic.ReactionDelay, capped at 6 s) has run out.
    //
    // Threads:
    // - GetFormationFrame postfix: parallel worker threads (Agent.TickParallel, ~0.5 s per agent) AND the serial AI /
    //   main thread (Agent.ForceUpdateCachedAndFormationValues, e.g. from OrderController.AfterSetOrder). The two never
    //   run at the same time, so one agent's Hold is only ever written by one thread at a time; the dictionary is
    //   concurrent for the inserts from parallel workers. The postfix reads managed fields only: no MBRandom, no
    //   engine query. Mission.CurrentTime is a cached field.
    // - AfterSetOrder prefix: main thread, before AfterSetOrder force-pushes the new order's frame to every unit.
    // - SetMovementOrder / SetArrangementOrder prefixes (AI orders, see the section at the bottom): the engine's async
    //   AI thread, serial with the workers and the main thread; thread-static System.Random, no MBRandom.
    //
    // Charges are excluded (Charge, ChargeWithTarget, Retreat, and any frame produced while the formation's movement
    // state is Charge): there is no slot frame worth holding, a held Move frame under a ChargeWithTarget would pin the
    // man to his old slot, and Charge -> Move is already delayed by the decide time. A charging man has HasLast = false,
    // so the next order is not held for him.
    //
    // Reset: the Agent-keyed static is cleared in MissionStartReset.Reset (rule: every Agent/Formation-keyed static goes
    // there). Removed agents are not dropped mid-mission; they are never looked up again, since inactive agents get no
    // formation update and the prefix skips them.
    internal static class OrderReactionHold
    {
        internal sealed class Hold
        {
            public WorldPosition LastPos;
            public Vec2 LastDir;
            public bool HasLast;
            public volatile float ReleaseTime;
            public Formation Formation;
        }

        private static readonly ConcurrentDictionary<Agent, Hold> holds = new ConcurrentDictionary<Agent, Hold>();
        private static readonly Func<Agent, Hold> NewHold = _ => new Hold();

        internal static void Clear()
        {
            holds.Clear();
            aiStampBlockedUntil.Clear();
        }

        /// <summary>Orders the men physically react to with a new frame. Charges/retreat are left to the decide time.</summary>
        private static bool IsHeldOrder(OrderType orderType)
        {
            switch (orderType)
            {
                case OrderType.Move:
                case OrderType.MoveToLineSegment:
                case OrderType.MoveToLineSegmentWithHorizontalLayout:
                case OrderType.StandYourGround:
                case OrderType.FollowMe:
                case OrderType.FollowEntity:
                case OrderType.AdvanceTenPaces:
                case OrderType.FallBackTenPaces:
                case OrderType.Advance:
                case OrderType.FallBack:
                case OrderType.LookAtEnemy:
                case OrderType.LookAtDirection:
                case OrderType.ArrangementLine:
                case OrderType.ArrangementCloseOrder:
                case OrderType.ArrangementLoose:
                case OrderType.ArrangementCircular:
                case OrderType.ArrangementSchiltron:
                case OrderType.ArrangementVee:
                case OrderType.ArrangementColumn:
                case OrderType.ArrangementScatter:
                case OrderType.FormDeep:
                case OrderType.FormWide:
                case OrderType.FormWider:
                case OrderType.FormCustom:
                    return true;
                default:
                    return false;
            }
        }

        [HarmonyPatch(typeof(HumanAIComponent))]
        internal static class GetFormationFramePatch
        {
            // Last: runs after RBM's own GetFormationFrame prefix/postfix (FormationMovement.cs), so it holds the frame
            // they produced. The speed limit is left alone.
            [HarmonyPostfix]
            [HarmonyPatch("GetFormationFrame")]
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(ref Agent ___Agent, ref bool __result, ref WorldPosition formationPosition, ref Vec2 formationDirection)
            {
                try
                {
                    if (!RBMConfig.RBMConfig.orderReactionDelayEnabled)
                    {
                        return;
                    }
                    Agent agent = ___Agent;
                    Mission mission = Mission.Current;
                    if (agent == null || mission == null || !agent.IsHuman || agent.IsPlayerControlled)
                    {
                        return;
                    }
                    Formation formation = agent.Formation;
                    Hold hold = holds.GetOrAdd(agent, NewHold);
                    if (!__result || formation == null
                        || formation.GetReadonlyMovementOrderReference().MovementState == MovementOrder.MovementStateEnum.Charge)
                    {
                        hold.HasLast = false;
                        return;
                    }
                    if (hold.HasLast && mission.CurrentTime < hold.ReleaseTime && formation == hold.Formation)
                    {
                        formationPosition = hold.LastPos;
                        formationDirection = hold.LastDir;
                        __result = true;
                        return;
                    }
                    hold.LastPos = formationPosition;
                    hold.LastDir = formationDirection;
                    hold.Formation = formation;
                    hold.HasLast = true;
                }
                catch
                {
                    // Worker thread: never let an exception escape into the engine's parallel loop.
                }
            }
        }

        [HarmonyPatch(typeof(OrderController), "AfterSetOrder")]
        internal static class AfterSetOrderPatch
        {
            // Main thread, before AfterSetOrder force-updates every unit's formation frame.
            private static void Prefix(OrderController __instance, OrderType orderType)
            {
                try
                {
                    if (!RBMConfig.RBMConfig.orderReactionDelayEnabled || !IsHeldOrder(orderType))
                    {
                        return;
                    }
                    Mission mission = Mission.Current;
                    if (mission == null || mission.Mode != MissionMode.Battle || GameNetwork.IsMultiplayer)
                    {
                        return;
                    }
                    MBReadOnlyList<Formation> formations = __instance.SelectedFormations;
                    if (formations == null)
                    {
                        return;
                    }
                    float now = mission.CurrentTime;
                    Agent owner = __instance.Owner;
                    bool hasSource = owner != null && owner.IsActive();
                    Vec2 source = hasSource ? owner.Position.AsVec2 : Vec2.Zero;

                    for (int f = 0; f < formations.Count; f++)
                    {
                        MBReadOnlyList<IFormationUnit> units = formations[f]?.Arrangement?.GetAllUnits();
                        if (units == null)
                        {
                            continue;
                        }
                        for (int i = 0; i < units.Count; i++)
                        {
                            Agent agent = units[i] as Agent;
                            if (agent == null || !agent.IsActive() || agent.IsPlayerControlled || !agent.IsHuman)
                            {
                                continue;
                            }
                            // Only men with a frame on record: there is nothing to hold for the rest.
                            if (holds.TryGetValue(agent, out Hold hold) && hold.HasLast)
                            {
                                hold.ReleaseTime = now + FormationShoutsLogic.ReactionDelay(agent, hasSource, source);
                            }
                        }
                    }
                }
                catch
                {
                    // Never block an order over this.
                }
            }
        }

        // ---------------- AI orders ----------------
        // AI tactics never go through an OrderController: they call Formation.SetMovementOrder / SetArrangementOrder
        // directly, on the engine's async AI thread (Team.Tick, serial: no parallel agent worker and no main-thread
        // OnMissionTick runs meanwhile), and the new frame is force-pushed to the units in the same AI tick. So the
        // release times are stamped right here, at the order change, before that push. No MBRandom on this thread:
        // a thread-static System.Random. Only a real change counts (AI behaviors re-issue Move every few frames with
        // a new position): a new movement GROUP that is held, or a new arrangement. Charges and retreats are not held.

        [ThreadStatic]
        private static Random aiRandom;

        /// <summary>An AI formation is stamped again at most this often.</summary>
        private const float AiStampCooldown = 3f;

        /// <summary>
        /// Per AI formation: no new stamp before this time -- the later of AiStampCooldown after the last stamp and the
        /// last of that stamp's release times. RBM's own AI flips order types often (the attacker's stepped advance
        /// alternates Move and Stop; regroup, brace); re-stamping on each flip held the men again before they had
        /// caught up, the formation looked out of place, the AI re-ordered, and the loop cost frames (user, 2026-10-09).
        /// Written on the AI thread; cleared with the holds.
        /// </summary>
        private static readonly ConcurrentDictionary<Formation, float> aiStampBlockedUntil = new ConcurrentDictionary<Formation, float>();

        private static bool ShouldStampAi(Formation formation)
        {
            if (!RBMConfig.RBMConfig.orderReactionDelayEnabled || formation == null || !formation.IsAIControlled || formation.CountOfUnits <= 0)
            {
                return false;
            }
            Mission mission = Mission.Current;
            return mission != null && mission.Mode == MissionMode.Battle && !GameNetwork.IsMultiplayer;
        }

        private static bool IsHeldGroup(FormationShoutsLogic.MoveGroup group)
        {
            return group == FormationShoutsLogic.MoveGroup.Stop || group == FormationShoutsLogic.MoveGroup.Advance
                || group == FormationShoutsLogic.MoveGroup.FallBack || group == FormationShoutsLogic.MoveGroup.Move
                || group == FormationShoutsLogic.MoveGroup.Follow;
        }

        /// <summary>Every man of the AI formation with a frame on record keeps it for his own delay, measured from the captain if there is one.</summary>
        private static void StampAi(Formation formation)
        {
            MBReadOnlyList<IFormationUnit> units = formation.Arrangement?.GetAllUnits();
            if (units == null)
            {
                return;
            }
            float now = Mission.Current.CurrentTime;
            if (aiStampBlockedUntil.TryGetValue(formation, out float blockedUntil) && now < blockedUntil)
            {
                return;
            }
            Random random = aiRandom ?? (aiRandom = new Random(Environment.TickCount ^ System.Threading.Thread.CurrentThread.ManagedThreadId));
            float lastRelease = now;
            Agent captain = formation.Captain;
            bool hasSource = captain != null && captain.IsActive();
            Vec2 source = hasSource ? captain.Position.AsVec2 : Vec2.Zero;
            for (int i = 0; i < units.Count; i++)
            {
                Agent agent = units[i] as Agent;
                if (agent == null || !agent.IsActive() || agent.IsPlayerControlled || !agent.IsHuman)
                {
                    continue;
                }
                if (holds.TryGetValue(agent, out Hold hold) && hold.HasLast)
                {
                    float release = now + FormationShoutsLogic.ReactionDelay(agent, hasSource, source, random);
                    hold.ReleaseTime = release;
                    lastRelease = Math.Max(lastRelease, release);
                }
            }
            aiStampBlockedUntil[formation] = Math.Max(now + AiStampCooldown, lastRelease);
        }

        [HarmonyPatch(typeof(Formation), nameof(Formation.SetMovementOrder))]
        internal static class AiMovementOrderPatch
        {
            // Async AI thread (AI tactics) or main thread (OrderController; skipped: not AI controlled).
            private static void Prefix(Formation __instance, MovementOrder input)
            {
                try
                {
                    if (!ShouldStampAi(__instance))
                    {
                        return;
                    }
                    FormationShoutsLogic.MoveGroup newGroup = FormationShoutsLogic.GroupOf(input.OrderEnum);
                    if (!IsHeldGroup(newGroup)
                        || newGroup == FormationShoutsLogic.GroupOf(__instance.GetReadonlyMovementOrderReference().OrderEnum))
                    {
                        return;
                    }
                    StampAi(__instance);
                }
                catch
                {
                    // AI thread: never let an exception escape into the engine's tick.
                }
            }
        }

        [HarmonyPatch(typeof(Formation), nameof(Formation.SetArrangementOrder))]
        internal static class AiArrangementOrderPatch
        {
            private static void Prefix(Formation __instance, ArrangementOrder order)
            {
                try
                {
                    if (!ShouldStampAi(__instance) || order.OrderEnum == __instance.ArrangementOrder.OrderEnum)
                    {
                        return;
                    }
                    StampAi(__instance);
                }
                catch
                {
                    // AI thread: never let an exception escape into the engine's tick.
                }
            }
        }
    }
}
