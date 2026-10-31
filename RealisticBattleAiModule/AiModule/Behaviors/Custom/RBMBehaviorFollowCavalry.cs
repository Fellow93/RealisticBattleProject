using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// FOOT FOLLOWS THE CAVALRY (kMount, our army cavalry-heavy): an AI melee foot formation walks to StandOff short of the near edge of the enemy
    /// formation our strongest non-token mounted formation is fighting (its CavalryCharge target, its ChargeToTarget target, or its closest enemy while
    /// charging / in melee), facing it, and charges (ChargeToTarget, through ChargeGuard)
    /// within ChargeGap, or within EngagedChargeGap once our cavalry is in melee with it; back to following beyond ChargeReleaseGap.
    ///
    /// Weight = kMount x BaseWeight (at kMount 1 above an Advance with its keep bonus, below the holds' 20 and React's 100); 0 once the formation is in
    /// melee (its tactic's charge takes over; off until under ContactOff for ContactCalmSeconds), without a target, for player-controlled, mounted or ranged formations, the defender before its battle is
    /// joined (ArmyComposition.FollowJoined), and while the tactic gives the formation a hedgehog infantry / choke Line / ReservePlug role.
    /// WeightFactor: 1 only for melee foot of a team with kMount > 0 (ResetBehavior, and ArmyComposition wakes it when kMount rises), set back to 0
    /// by GetAiWeight once kMount is 0 - FindBestBehavior skips it, so kMount = 0 is unchanged.
    ///
    /// THREADING: GetAiWeight / CalculateCurrentOrder / TickOccasionally on the async AI thread: cached formation data; the Move point is a copy of the
    /// formation's (or the target's) cached median with ONE SetVec2MT, rebuilt past RebuildDistance after RebuildSeconds (CachedMovePoint). StateName is volatile (map view).
    /// </summary>
    internal class RBMBehaviorFollowCavalry : BehaviorComponent
    {
        internal const float BaseWeight = 3f;
        internal const float StandOff = 60f; // m short of the target's near edge
        internal const float ChargeGap = 50f; // edge gap: ChargeGuard's start distance
        internal const float EngagedChargeGap = 60f; // our cavalry in melee with the target
        internal const float ChargeReleaseGap = 75f;
        private const float ContactOff = 0.05f; // own melee share; contact starts at ArmyComposition.InMelee
        private const float ContactCalmSeconds = 5f;
        private const float FacingTurnCos = 0.985f;
        private const string Name = "FollowCavalry";

        // token cavalry is never "our cavalry" (user rule)
        private static readonly System.Func<Formation, bool> MountedTest = f => ArmyComposition.IsMounted(f) && !ArmyComposition.IsToken(f.Team, ArmyComposition.CategoryOf(f));

        private Formation _target;
        private Formation _cavalry;
        private bool _charging;
        private string _offReason = "idle";
        private readonly CachedMovePoint _point = new CachedMovePoint();
        private Vec2 _facing = Vec2.Invalid;
        private volatile string _stateName = "idle";
        private bool _inContact;
        private float _calmSince = float.NaN;
        private float _contactAt = float.NegativeInfinity;

        public RBMBehaviorFollowCavalry(Formation formation)
            : base(formation)
        {
            _behaviorSide = formation.AI.Side;
            base.CurrentOrder = MovementOrder.MovementOrderStop;
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
        }

        /// <summary>Map view text ("follow f2:Infantry", "charge f2:Infantry", "idle"). Any thread.</summary>
        internal string StateName => _stateName;

        public override void ResetBehavior()
        {
            WeightFactor = InWakeBand(base.Formation) ? 1f : 0f;
        }

        /// <summary>Melee foot (troop type infantry, so never mounted): the formations this behavior can serve.</summary>
        internal static bool IsCandidate(Formation formation)
        {
            return formation != null && FormationCohesion.IsMeleeInfantry(formation);
        }

        // awake only while kMount > 0; ArmyComposition wakes it when kMount rises, GetAiWeight puts it back to sleep
        private static bool InWakeBand(Formation formation)
        {
            ArmyComposition.Snapshot snap;
            return IsCandidate(formation) && ArmyComposition.TryPeek(formation.Team, out snap) && ArmyComposition.FollowBand(snap);
        }

        // contact hysteresis: on at InMelee, off only after ContactCalmSeconds under ContactOff
        private bool InContact(Formation formation)
        {
            float melee = HoldLateCharge.MeleeShare(formation);
            float now = MBCommon.GetTotalMissionTime();
            if (melee >= ArmyComposition.InMelee)
            {
                _inContact = true;
                _calmSince = float.NaN;
                _contactAt = now;
                return true;
            }
            if (!_inContact)
            {
                return false;
            }
            if (melee >= ContactOff)
            {
                _calmSince = float.NaN;
                _contactAt = now;
                return true;
            }
            if (float.IsNaN(_calmSince))
            {
                _calmSince = now;
            }
            if (now - _calmSince >= ContactCalmSeconds)
            {
                _inContact = false;
                _calmSince = float.NaN;
                return false;
            }
            return true;
        }

        protected override float GetAiWeight()
        {
            Formation formation = base.Formation;
            string off = OffReason(formation, out float kMount);
            Formation cavalry = null;
            Formation target = null;
            if (off == null)
            {
                target = CavalryTarget(formation.Team, out cavalry);
                if (target == null)
                {
                    off = "noTarget";
                }
                else if (InContact(formation))
                {
                    off = "melee";
                }
            }
            if (off != null)
            {
                _offReason = off;
                // a band left (kMount gone, no longer melee foot): dormant until the next ResetBehavior
                if (off == "kMount=0" || off == "notMeleeFoot" || off == "noMen")
                {
                    if (!InWakeBand(formation))
                    {
                        WeightFactor = 0f;
                    }
                }
                return 0f;
            }
            if (target != _target)
            {
                _target = target;
                _charging = false;
                _point.Reset();
            }
            _cavalry = cavalry;
            _offReason = null;
            return kMount * BaseWeight;
        }

        private static string OffReason(Formation formation, out float kMount)
        {
            kMount = 0f;
            Mission mission = Mission.Current;
            if (formation == null || formation.Team == null || mission == null || formation.CountOfUnits <= 0)
            {
                return "noMen";
            }
            if (!formation.IsAIControlled)
            {
                return "player";
            }
            if (!FormationCohesion.IsMeleeInfantry(formation))
            {
                return "notMeleeFoot";
            }
            kMount = ArmyComposition.KMount(formation.Team);
            if (kMount <= 0f)
            {
                return "kMount=0";
            }
            if (mission.RetreatSide != BattleSideEnum.None && mission.RetreatSide == formation.Team.Side)
            {
                return "armyRetreat";
            }
            if (!ArmyComposition.FollowJoined(formation.Team))
            {
                return "notJoined";
            }
            FormationAI ai = formation.AI;
            RBMBehaviorHedgehog hedgehog = ai.GetBehavior<RBMBehaviorHedgehog>();
            if (hedgehog != null && hedgehog.WeightFactor > 0f && hedgehog.CurrentRole == RBMBehaviorHedgehog.Role.Infantry)
            {
                return "hedgehog";
            }
            RBMBehaviorHoldChoke choke = ai.GetBehavior<RBMBehaviorHoldChoke>();
            if (choke != null && choke.WeightFactor > 0f && choke.CurrentRole == RBMBehaviorHoldChoke.Role.Line)
            {
                return "choke";
            }
            RBMBehaviorReservePlug plug = ai.GetBehavior<RBMBehaviorReservePlug>();
            if (plug != null && plug.WeightFactor > 0f)
            {
                return "reservePlug";
            }
            return null;
        }

        /// <summary>The enemy formation our strongest mounted formation fights / charges, else null.</summary>
        private static Formation CavalryTarget(Team team, out Formation cavalry)
        {
            cavalry = ArmyComposition.Strongest(team, MountedTest);
            if (cavalry == null || cavalry.AI == null)
            {
                return null;
            }
            Formation target = null;
            RBMBehaviorCavalryCharge charge = cavalry.AI.ActiveBehavior as RBMBehaviorCavalryCharge;
            if (charge != null)
            {
                // a held charge (CavalryWearDown) is not fighting
                target = charge.IsWearDownHeld ? null : charge.TargetFormation;
            }
            else
            {
                OrderType order = cavalry.GetReadonlyMovementOrderReference().OrderType;
                if (order == OrderType.ChargeWithTarget)
                {
                    // Formation.TargetFormation is cleared by SetMovementOrder; the order holds the target
                    target = cavalry.GetReadonlyMovementOrderReference().TargetFormation;
                }
                if (target == null && (order == OrderType.Charge || order == OrderType.ChargeWithTarget || HoldLateCharge.MeleeShare(cavalry) >= ArmyComposition.InMelee))
                {
                    target = cavalry.CachedClosestEnemyFormation?.Formation;
                }
            }
            if (target == null || target.CountOfUnits <= 0 || target.Team == null || !target.Team.IsEnemyOf(team))
            {
                return null;
            }
            // foot never follows onto riders, unless our cavalry is fighting them right there (P3)
            if (ArmyComposition.IsMounted(target) && !CavalryEngaged(cavalry, target))
            {
                ArmyComposition.Note(team, null, ArmyComposition.KindGate, "followCavalry skip", "mountedTarget " + TerrainCavalry.FormationName(target) + " cav=" + TerrainCavalry.FormationName(cavalry) + " notEngaged");
                return null;
            }
            return target;
        }

        public override void TickOccasionally()
        {
            Formation formation = base.Formation;
            if (formation == null || formation.AI == null || formation.AI.ActiveBehavior != this || _offReason != null || _target == null)
            {
                return;
            }
            UpdateCharge(formation);
            CalculateCurrentOrder();
            formation.SetMovementOrder(base.CurrentOrder);
            formation.SetFacingOrder(CurrentFacingOrder);
        }

        protected override void OnBehaviorActivatedAux()
        {
            Formation formation = base.Formation;
            if (formation == null || _offReason != null || _target == null)
            {
                return;
            }
            Log(formation, "follow take", Describe(formation));
            UpdateCharge(formation);
            CalculateCurrentOrder();
            formation.SetMovementOrder(base.CurrentOrder);
            formation.SetFacingOrder(CurrentFacingOrder);
            formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
        }

        public override void OnBehaviorCanceled()
        {
            Formation formation = base.Formation;
            if (formation != null && _target != null)
            {
                Log(formation, "follow end", (_offReason ?? "replaced") + " target=" + TerrainCavalry.FormationName(_target));
            }
            // canceled by its own contact: keep the charge for the return after the fight
            if (MBCommon.GetTotalMissionTime() - _contactAt >= ContactCalmSeconds)
            {
                _charging = false;
                _point.Reset();
            }
            _stateName = "idle";
            base.OnBehaviorCanceled();
        }

        private void UpdateCharge(Formation formation)
        {
            Formation target = _target;
            float gap = FormationGeometry.Gap(formation, FootprintMode.Current, target, FootprintMode.ScatterAware);
            if (_charging)
            {
                if (gap > ChargeReleaseGap)
                {
                    _charging = false;
                    _point.Reset();
                    Log(formation, "follow take", "release gap=" + ReactionMonitor.Fmt0(gap) + "m " + Describe(formation));
                }
                return;
            }
            bool engaged = CavalryEngaged(_cavalry, target);
            // riders: stay at the stand-off, charge only inside ChargeGuard's start distance
            bool mounted = ArmyComposition.IsMounted(target);
            if (gap <= ChargeGap || (engaged && !mounted && gap <= EngagedChargeGap))
            {
                _charging = true;
                Log(formation, "follow charge", "gap=" + ReactionMonitor.Fmt0(gap) + "m" + (engaged ? " cavalryEngaged" : "") + " " + Describe(formation));
            }
        }

        private static bool CavalryEngaged(Formation cavalry, Formation target)
        {
            return cavalry != null && cavalry.CountOfUnits > 0 && HoldLateCharge.MeleeShare(cavalry) >= ArmyComposition.InMelee
                && FormationGeometry.Gap(cavalry, FootprintMode.ScatterAware, target, FootprintMode.ScatterAware) <= ArmyComposition.EngagedGap;
        }

        protected override void CalculateCurrentOrder()
        {
            Formation formation = base.Formation;
            Formation target = _target;
            Mission mission = Mission.Current;
            if (formation == null || mission == null || target == null || target.CountOfUnits <= 0)
            {
                // nothing to follow: leave the formation's order as it is
                base.CurrentOrder = (formation != null) ? formation.GetReadonlyMovementOrderReference() : MovementOrder.MovementOrderStop;
                CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                return;
            }
            if (_charging)
            {
                base.CurrentOrder = MovementOrder.MovementOrderChargeToTarget(target);
                CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                _stateName = "charge " + TerrainCavalry.FormationName(target);
                return;
            }
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(formation);
            Footprint shape;
            if (!centre.IsValid || !FormationGeometry.TryGet(target, FootprintMode.ScatterAware, out shape))
            {
                base.CurrentOrder = MovementOrder.MovementOrderChargeToTarget(target);
                CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                return;
            }
            Vec2 away = centre - shape.Centre;
            Vec2 dir = (away.LengthSquared > 0.01f) ? away.Normalized() : -target.Direction;
            float gap = FormationGeometry.Gap(formation, FootprintMode.Current, target, FootprintMode.ScatterAware);
            // never walk back toward our own side to make the stand-off
            float standOff = (gap < StandOff) ? MathF.Max(gap, 0f) : StandOff;
            Vec2 front = shape.Centre + dir * (FormationGeometry.ExtentAlong(shape, dir) + standOff);
            Vec2 facing = -dir;
            if (!_facing.IsValid || _facing.DotProduct(facing) < FacingTurnCos)
            {
                _facing = facing;
            }
            // own median, else the target's; no source keeps the last point
            WorldPosition point;
            if (!_point.TryGet(front, mission.CurrentTime, formation.CachedMedianPosition, target.CachedMedianPosition, false, true, false, out point))
            {
                base.CurrentOrder = MovementOrder.MovementOrderChargeToTarget(target);
                CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                return;
            }
            base.CurrentOrder = MovementOrder.MovementOrderMove(point);
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(_facing);
            _stateName = "follow " + TerrainCavalry.FormationName(target);
        }

        private string Describe(Formation formation)
        {
            return "target=" + TerrainCavalry.FormationName(_target) + " cav=" + ((_cavalry != null) ? TerrainCavalry.FormationName(_cavalry) : "-")
                + " " + ArmyComposition.DescribeOwnK(formation.Team);
        }

        private static void Log(Formation formation, string what, string why)
        {
            Mission mission = Mission.Current;
            if (mission == null || formation?.Team == null)
            {
                return;
            }
            TerrainBattlePosition.Enqueue(formation.Team, formation, mission.CurrentTime, Name, what, "-", why);
        }

        public override TextObject GetBehaviorString()
        {
            return new TextObject("{=RBMFollowCav01}following the cavalry");
        }
    }
}
