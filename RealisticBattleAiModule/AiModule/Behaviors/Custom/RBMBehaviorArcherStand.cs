using System.Globalization;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// THE ARCHER STAND (user rule: defending archers without much infantry do not run from the enemy and skirmish endlessly; they stand and
    /// shoot / fight). Owns the defender's archer anchor without an infantry main (ArmyComposition.LineHolder, any battle size) in every tactic
    /// phase: WeightFactor kept through ResetBehaviorWeights (ResetBehavior), woken by ArmyComposition when the holder changes; ArcherFlank,
    /// ArcherSkirmish, HoldPosition and the cover / support glue step aside for it (Owns).
    ///
    /// Spot: the battle position's hold when a plan is published, else where they stand at the take (a fixed copy of their cached median); a plan
    /// published later is taken once while the enemy is beyond LatePlanGap. Faces the enemy's concentration, ranged width, Line / Loose fixed post.
    /// Contact (HoldLateCharge, BrokenOnly): fights in place. Charges (ChargeToTarget the nearest significant enemy; ChargeGuard walks it in) only
    /// when dry or when the enemy it met is broken.
    /// Escort (Escorts, both sides): the AI melee infantry / dry formations of a team without an infantry main stand EscortGap in front of the
    /// holder (else the position anchor), face its way, fight in place and charge when it charges or enemy foot comes within
    /// ChargeGuard.ChargeStartDistance (recalled once no enemy foot is within ChargeKeepDistance); target gone = hold the last point.
    /// Mounted escort (MountedEscorts, both sides, main or not): an AI token cavalry / HA force rides beside the position anchor's flank instead
    /// of skirmishing ahead alone; MountedSkirmish / HorseArcherSkirmish yield to it. No anchor = released (the all-mounted paths).
    ///
    /// THREADING: AI thread; cached formation data and the immutable plan only. No statics. StateName is volatile (map view).
    /// </summary>
    internal class RBMBehaviorArcherStand : BehaviorComponent
    {
        internal const float StandWeight = 50f; // above the holds' 20 with their keep bonus, below React's 100
        private const float ArriveDistance = 15f; // m, + a quarter depth
        internal const float LatePlanGap = 150f; // m edge gap: a later plan is still worth walking to
        internal const float JoinGap = 75f; // the defend tactics' battle-join range
        private const string Name = "ArcherStand";

        private enum Phase
        {
            Idle,
            Moving,
            Holding,
            Charge
        }

        private Phase _phase = Phase.Idle;
        private bool _hasSpot;
        private bool _stay;
        private Vec2 _spot = Vec2.Invalid;
        private Vec2 _facing = Vec2.Invalid;
        private WorldPosition _spotWorld = WorldPosition.Invalid;
        private readonly HoldLateCharge _late = new HoldLateCharge { BrokenOnly = true };
        private Formation _target;
        private bool _footCharge; // an escort's charge at enemy foot that came within ChargeGuard.ChargeStartDistance
        private bool _widthSet;
        /// <summary>Per-battle latch, kept through Release: a battle once joined never flips the defend tactics back to Defend when the stand is taken up again.</summary>
        private volatile bool _joined;
        private volatile string _stateName = "idle";
        private bool _escort;
        private readonly CachedMovePoint _movePoint = new CachedMovePoint();
        private const float EscortGap = 5f; // m in front of the archers' front / between escorts
        private WorldPosition _lastPoint = WorldPosition.Invalid;
        private Vec2 _lastFacing = Vec2.Invalid;
        private Formation _escortTarget;

        public RBMBehaviorArcherStand(Formation formation)
            : base(formation)
        {
            _behaviorSide = formation.AI.Side;
            base.CurrentOrder = MovementOrder.MovementOrderStop;
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
        }

        /// <summary>Map view text. Any thread.</summary>
        internal string StateName => _stateName;

        /// <summary>AI thread: the stand owns this formation (the team's archer stand holder, AI-controlled).</summary>
        internal static bool Owns(Formation formation)
        {
            return formation != null && formation.AI != null && formation.IsAIControlled && formation.CountOfUnits > 0
                && ArmyComposition.LineHolder(formation.Team) == formation && formation.AI.GetBehavior<RBMBehaviorArcherStand>() != null;
        }

        /// <summary>AI thread: escorts its target (user rule: infantry that does not lead escorts the archers, both sides).</summary>
        internal static bool Escorts(Formation formation)
        {
            return FootEscorts(formation) || MountedEscorts(formation);
        }

        private static bool FootEscorts(Formation formation)
        {
            return EscortEligible(formation) && EscortTarget(formation) != null;
        }

        /// <summary>AI thread: an AI token mounted force rides with the position anchor (user rule: a few riders / HA stay with the main body, both sides).</summary>
        internal static bool MountedEscorts(Formation formation)
        {
            if (formation == null || formation.AI == null || !formation.IsAIControlled || formation.Team == null || formation.CountOfUnits <= 0
                || !ArmyComposition.IsMounted(formation) || formation.AI.GetBehavior<RBMBehaviorArcherStand>() == null)
            {
                return false;
            }
            return ArmyComposition.IsToken(formation.Team, ArmyComposition.CategoryOf(formation)) && ArmyComposition.AnchorFor(formation) != null;
        }

        /// <summary>AI foot melee infantry or a dry formation (the dry archers moved into the Infantry slot) of a team without an infantry main, not the anchor itself.</summary>
        private static bool EscortEligible(Formation formation)
        {
            if (formation == null || formation.AI == null || !formation.IsAIControlled || formation.Team == null || formation.CountOfUnits <= 0
                || ArmyComposition.IsMounted(formation) || !(ArmyComposition.IsMeleeInfantryType(formation) || ArmyComposition.IsDry(formation))
                || formation.AI.GetBehavior<RBMBehaviorArcherStand>() == null)
            {
                return false;
            }
            return FormationCohesion.TeamMain(formation.Team) == null && ArmyComposition.PositionAnchor(formation.Team) != formation
                && ArmyComposition.LineHolder(formation.Team) != formation;
        }

        /// <summary>The team's archer stand holder, else its position anchor; null without one.</summary>
        private static Formation EscortTarget(Formation formation)
        {
            Formation holder = ArmyComposition.LineHolder(formation.Team);
            if (holder != formation && Owns(holder))
            {
                return holder;
            }
            return ArmyComposition.AnchorFor(formation);
        }

        /// <summary>Escorts, or keeps holding its last escort point while the target is gone (never marches off alone).</summary>
        private bool Escorting()
        {
            Formation formation = base.Formation;
            return Escorts(formation) || (_escort && _phase != Phase.Idle && _lastPoint.IsValid && EscortEligible(formation));
        }

        /// <summary>AI thread: the stand is in contact or charging.</summary>
        internal bool Fighting => _phase == Phase.Charge || _late.State != HoldLateCharge.FightState.Holding;

        /// <summary>AI thread: the team's archers stand; joined = latched once the enemy came within JoinGap or the stand met it (the defend tactics' battle joined).</summary>
        internal static bool TeamStand(Team team, out bool joined)
        {
            joined = false;
            Formation holder = ArmyComposition.LineHolder(team);
            if (!Owns(holder))
            {
                return false;
            }
            joined = holder.AI.GetBehavior<RBMBehaviorArcherStand>()._joined;
            return true;
        }

        /// <summary>AI thread, from ArmyComposition on each snapshot of a team without a main (holder / anchor / dry state may have changed).</summary>
        internal static void Wake(Team team)
        {
            if (team == null)
            {
                return;
            }
            MBList<Formation> formations = team.FormationsIncludingEmpty;
            for (int i = 0; i < formations.Count; i++)
            {
                Formation f = formations[i];
                RBMBehaviorArcherStand stand = f?.AI?.GetBehavior<RBMBehaviorArcherStand>();
                if (stand != null && stand.WeightFactor <= 0f && f.CountOfUnits > 0 && (Owns(f) || Escorts(f)))
                {
                    stand.WeightFactor = 1f;
                }
            }
        }

        public override void ResetBehavior()
        {
            WeightFactor = (Owns(base.Formation) || Escorting()) ? 1f : 0f;
        }

        protected override float GetAiWeight()
        {
            if (Owns(base.Formation) || Escorting())
            {
                return StandWeight;
            }
            Release(base.Formation.IsAIControlled ? "notHolder" : "player");
            WeightFactor = 0f;
            return 0f;
        }

        public override void TickOccasionally()
        {
            CalculateCurrentOrder();
            base.Formation.SetMovementOrder(base.CurrentOrder);
            base.Formation.SetFacingOrder(CurrentFacingOrder);
            ApplyShape();
        }

        protected override void OnBehaviorActivatedAux()
        {
            _widthSet = false;
            CalculateCurrentOrder();
            base.Formation.SetMovementOrder(base.CurrentOrder);
            base.Formation.SetFacingOrder(CurrentFacingOrder);
            base.Formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
            ApplyShape();
        }

        protected override void CalculateCurrentOrder()
        {
            Mission mission = Mission.Current;
            Formation formation = base.Formation;
            if (mission == null || formation == null || formation.CountOfUnits == 0 || formation.Team == null)
            {
                return;
            }
            float now = mission.CurrentTime;
            bool escort = !Owns(formation);
            if (_phase != Phase.Idle && escort != _escort)
            {
                Release("role");
            }
            _escort = escort;
            if (_phase == Phase.Charge && escort && MountedEscorts(formation) && MountedChargeReason(mission, formation, MountedRecallGap) == null)
            {
                // the threat passed: back beside the main body instead of chasing it
                Log(formation, now, "archerStand escort recall", "target=" + MountedTargetText(ArmyComposition.AnchorFor(formation)));
                _phase = Phase.Moving;
                _target = null;
            }
            float footGap = float.MaxValue;
            if (_phase == Phase.Charge && escort && _footCharge && (NearestSignificantFoot(mission, formation, out footGap) == null || footGap > ChargeGuard.ChargeKeepDistance))
            {
                // the enemy foot is beaten or gone: back in front of the archers instead of chasing riders
                Log(formation, now, "archerStand escort recall", "noFoot gap=" + FormationGeometry.GapText(footGap));
                _phase = Phase.Moving;
                _target = null;
                _footCharge = false;
            }
            if (_phase == Phase.Charge)
            {
                ChargeOrder(mission, formation);
                return;
            }
            if (escort && MountedEscorts(formation))
            {
                MountedEscortOrder(mission, formation, now);
                return;
            }
            if (escort)
            {
                EscortOrder(mission, formation, now);
                return;
            }
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(formation);
            if (!centre.IsValid)
            {
                return;
            }
            float enemyGap;
            Formation enemy = NearestSignificantEnemy(mission, formation, out enemyGap);
            if (enemyGap <= JoinGap)
            {
                _joined = true;
            }
            float ammo;
            if (ArmyComposition.IsDry(formation, out ammo))
            {
                StartCharge(mission, formation, now, "charge dry", "ammo=" + Fmt2(ammo) + "<" + Fmt2(ArmyComposition.DryAmmoShare));
                return;
            }
            if (_phase == Phase.Idle)
            {
                Log(formation, now, "archerStand take", "men=" + formation.CountOfUnits.ToString(CultureInfo.InvariantCulture) + " ammo=" + Fmt2(ArmyComposition.AmmoShare(formation))
                    + " " + ArmyComposition.Describe(formation.Team));
                _phase = Phase.Moving;
            }
            TerrainBattlePosition.Plan plan = TerrainBattlePosition.PlanFor(formation.Team);
            bool planUsable = plan != null && plan.HoldWorld.IsValid;
            if (!_hasSpot || (_stay && planUsable && enemyGap > LatePlanGap))
            {
                PickSpot(mission, formation, centre, enemy, plan, planUsable, now);
            }
            else if (_stay && enemyGap <= LatePlanGap)
            {
                _stay = false; // too late to walk to a plan now
            }
            UpdateFight(mission, formation, now, enemyGap);
            if (_phase == Phase.Charge)
            {
                ChargeOrder(mission, formation);
                return;
            }
            if (!_spotWorld.IsValid)
            {
                base.CurrentOrder = MovementOrder.MovementOrderStop;
                CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                return;
            }
            float distance = centre.Distance(_spot);
            float arrive = ArriveDistance + formation.Depth * 0.25f;
            if (_phase == Phase.Moving && distance <= arrive)
            {
                _phase = Phase.Holding;
            }
            else if (_phase == Phase.Holding && distance > arrive + 20f)
            {
                _phase = Phase.Moving;
            }
            UpdateStateName(distance);
            base.CurrentOrder = MovementOrder.MovementOrderMove(_spotWorld);
            CurrentFacingOrder = _facing.IsValid ? FacingOrder.FacingOrderLookAtDirection(_facing) : FacingOrder.FacingOrderLookAtEnemy;
        }

        /// <summary>
        /// Escort: Move to EscortGap in front of the target's front (escorts side by side), its facing; the target = the stand holder, else the
        /// position anchor. In melee or with the target gone it holds its last point (fights in place); charges when the target charges or
        /// enemy foot is within ChargeGuard.ChargeStartDistance.
        /// </summary>
        private void EscortOrder(Mission mission, Formation formation, float now)
        {
            Formation target = EscortTarget(formation);
            RBMBehaviorArcherStand stand = (target != null && Owns(target)) ? target.AI.GetBehavior<RBMBehaviorArcherStand>() : null;
            string kind = (stand != null) ? "stand" : ((target != null) ? "anchor" : "lastPoint");
            string targetText = kind + ":" + TerrainCavalry.FormationName(target);
            if (_phase == Phase.Idle || target != _escortTarget)
            {
                _phase = Phase.Moving;
                _escortTarget = target;
                Log(formation, now, "archerStand escort take", "men=" + formation.CountOfUnits.ToString(CultureInfo.InvariantCulture) + " target=" + targetText);
            }
            string charge = null;
            if (stand != null && stand._phase == Phase.Charge)
            {
                charge = stand._stateName;
            }
            else if (target != null && ChargeGuard.IsChargeIntent(target))
            {
                charge = "charging";
            }
            else if (target != null && RBMBehaviorArcherSkirmish.HasInitiative(formation.Team))
            {
                charge = "initiative";
            }
            if (charge != null)
            {
                StartCharge(mission, formation, now, "archerStand escort charge", "target=" + targetText + " " + charge);
                return;
            }
            // USER RULE 2026-10-30: enemy foot within the charge distance is charged, the archers keep standing
            Formation foot = NearestSignificantFoot(mission, formation, out float footGap);
            if (foot != null && footGap <= ChargeGuard.ChargeStartDistance)
            {
                _footCharge = true;
                StartCharge(mission, formation, now, "archerStand escort charge", "target=" + targetText + " foot", foot);
                return;
            }
            if (_lastPoint.IsValid && (target == null || RBMAI.Utilities.FormationFightingInMelee(formation, ArmyComposition.InMelee)))
            {
                _stateName = (target == null) ? "escort lastPoint" : "escort contact " + TerrainCavalry.FormationName(target);
                base.CurrentOrder = MovementOrder.MovementOrderMove(_lastPoint);
                CurrentFacingOrder = _lastFacing.IsValid ? FacingOrder.FacingOrderLookAtDirection(_lastFacing) : FacingOrder.FacingOrderLookAtEnemy;
                return;
            }
            if (target == null)
            {
                base.CurrentOrder = MovementOrder.MovementOrderStop;
                CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                return;
            }
            Vec2 facing = (stand != null && stand._facing.IsValid) ? stand._facing : target.Direction;
            WorldPosition source = (stand != null && stand._spotWorld.IsValid) ? stand._spotWorld : target.CachedMedianPosition;
            if (!facing.IsValid || facing.LengthSquared < 0.01f || !source.AsVec2.IsValid)
            {
                base.CurrentOrder = MovementOrder.MovementOrderStop;
                CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                return;
            }
            facing = facing.Normalized();
            // the stand's spot is its front-centre; the median is its centre
            Vec2 holderFront = (stand != null && stand._spotWorld.IsValid) ? stand._spot : source.AsVec2 + facing * (target.Depth * 0.5f);
            int index = 0;
            int count = 0;
            MBList<Formation> formations = formation.Team.FormationsIncludingEmpty;
            for (int i = 0; i < formations.Count; i++)
            {
                if (formations[i] == formation)
                {
                    index = count;
                }
                if (formations[i] == formation || FootEscorts(formations[i]))
                {
                    count++;
                }
            }
            Vec2 right = facing.RightVec();
            Vec2 desired = holderFront + facing * (formation.Depth + EscortGap) + right * ((index - (count - 1) * 0.5f) * (formation.Width + EscortGap));
            WorldPosition point;
            if (!_movePoint.TryGet(desired, now, source, source, false, true, false, out point))
            {
                return;
            }
            _lastPoint = point;
            _lastFacing = facing;
            _stateName = "escort " + TerrainCavalry.FormationName(target);
            base.CurrentOrder = MovementOrder.MovementOrderMove(point);
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(facing);
        }

        /// <summary>
        /// Mounted escort: beside the main body's flank (front level with its centre, outside its width; first right, then left, then further
        /// out), its facing. Charges when the main body charges for real (not ChargeGuard's walk) or is in melee, or an enemy comes within
        /// MountedDefendGap of it; recalled once all of that is over and the enemy is beyond MountedRecallGap.
        /// </summary>
        private void MountedEscortOrder(Mission mission, Formation formation, float now)
        {
            Formation target = ArmyComposition.AnchorFor(formation);
            // re-queried: the snapshot may have rebuilt since MountedEscorts saw an anchor
            if (target == null)
            {
                base.CurrentOrder = MovementOrder.MovementOrderStop;
                CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                return;
            }
            string targetText = MountedTargetText(target);
            if (_phase == Phase.Idle || target != _escortTarget)
            {
                _phase = Phase.Moving;
                _escortTarget = target;
                Log(formation, now, "archerStand escort take", "men=" + formation.CountOfUnits.ToString(CultureInfo.InvariantCulture) + " target=" + targetText);
            }
            string charge = MountedChargeReason(mission, formation, MountedDefendGap);
            if (charge != null)
            {
                StartCharge(mission, formation, now, "archerStand escort charge", "target=" + targetText + " " + charge);
                return;
            }
            Vec2 facing = target.Direction;
            WorldPosition source = target.CachedMedianPosition;
            if (!facing.IsValid || facing.LengthSquared < 0.01f || !source.AsVec2.IsValid)
            {
                base.CurrentOrder = MovementOrder.MovementOrderStop;
                CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                return;
            }
            facing = facing.Normalized();
            int index = 0;
            int count = 0;
            MBList<Formation> formations = formation.Team.FormationsIncludingEmpty;
            for (int i = 0; i < formations.Count; i++)
            {
                if (formations[i] == formation)
                {
                    index = count;
                    count++;
                }
                else if (MountedEscorts(formations[i]))
                {
                    count++;
                }
            }
            float side = (index % 2 == 0) ? 1f : -1f;
            float lateral = target.Width * 0.5f + formation.Width * 0.5f + EscortGap + (index / 2) * (formation.Width + EscortGap);
            lateral = OutsideLane(formation, target, source.AsVec2, facing, facing.RightVec() * side, lateral);
            Vec2 desired = source.AsVec2 + facing.RightVec() * (side * lateral);
            WorldPosition point;
            if (!_movePoint.TryGet(desired, now, source, source, false, true, false, out point))
            {
                return;
            }
            _lastPoint = point;
            _lastFacing = facing;
            _stateName = "escort " + targetText;
            base.CurrentOrder = MovementOrder.MovementOrderMove(point);
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(facing);
        }

        private const float LaneDepth = 30f; // m front/back of the target's centre, beyond the depths, that counts as its flank lane

        /// <summary>
        /// The escort's lateral pushed past the outer edge of every own formation already standing in that flank lane (flank archers, flank
        /// cavalry, the player's), so it never parks on top of them. Only formations overlapping the slot count, so one far out does not drag
        /// it away; repeated so a push into the next formation pushes again. Other mounted escorts are left out: their spacing is the index's.
        /// </summary>
        private static float OutsideLane(Formation formation, Formation target, Vec2 source, Vec2 facing, Vec2 sideDir, float lateral)
        {
            MBList<Formation> formations = formation.Team.FormationsIncludingEmpty;
            float half = formation.Width * 0.5f + EscortGap;
            for (int pass = 0; pass < formations.Count; pass++)
            {
                bool pushed = false;
                for (int i = 0; i < formations.Count; i++)
                {
                    Formation f = formations[i];
                    if (f == null || f == formation || f == target || f.CountOfUnits <= 0 || MountedEscorts(f))
                    {
                        continue;
                    }
                    Vec2 at = f.CachedMedianPosition.AsVec2;
                    if (!at.IsValid)
                    {
                        continue;
                    }
                    Vec2 rel = at - source;
                    if (MathF.Abs(rel.DotProduct(facing)) > (target.Depth + f.Depth + formation.Depth) * 0.5f + LaneDepth)
                    {
                        continue;
                    }
                    float across = rel.DotProduct(sideDir);
                    float outer = across + f.Width * 0.5f;
                    if (across <= 0f || across - f.Width * 0.5f > lateral + half || outer + half <= lateral)
                    {
                        continue;
                    }
                    lateral = outer + half;
                    pushed = true;
                }
                if (!pushed)
                {
                    break;
                }
            }
            return lateral;
        }

        private const float MountedDefendGap = 40f; // m edge gap: an enemy this close is fought
        private const float MountedRecallGap = 80f; // m edge gap: beyond it a mounted escort's charge is recalled

        /// <summary>Why a mounted escort charges now, null to stay with the main body.</summary>
        private static string MountedChargeReason(Mission mission, Formation formation, float enemyGap)
        {
            Formation main = ArmyComposition.AnchorFor(formation);
            if (main != null)
            {
                OrderType order = main.GetReadonlyMovementOrderReference().OrderType;
                if (order == OrderType.Charge || order == OrderType.ChargeWithTarget)
                {
                    return "mainCharging";
                }
                if (RBMAI.Utilities.FormationFightingInMelee(main, ArmyComposition.InMelee))
                {
                    return "mainMelee";
                }
            }
            if (RBMAI.Utilities.FormationFightingInMelee(formation, ArmyComposition.InMelee))
            {
                return "melee";
            }
            float gap;
            NearestSignificantEnemy(mission, formation, out gap);
            return (gap <= enemyGap) ? "enemyNear gap=" + FormationGeometry.GapText(gap) : null;
        }

        private static string MountedTargetText(Formation main)
        {
            return "main(mounted):" + TerrainCavalry.FormationName(main);
        }

        /// <summary>Contact (fight in place) and the broken charge; the late charge's Holding/Contact steps logged.</summary>
        private void UpdateFight(Mission mission, Formation formation, float now, float enemyGap)
        {
            Vec2 facing = _facing.IsValid ? _facing : formation.Direction;
            _late.Read(mission, formation, facing, false, true);
            string why;
            string step = _late.Decide(formation, now, _spot, facing, null, out why);
            if (_late.State != HoldLateCharge.FightState.Holding)
            {
                _joined = true;
            }
            if (step == null)
            {
                return;
            }
            if (_late.State == HoldLateCharge.FightState.Charge)
            {
                StartCharge(mission, formation, now, "charge broken", why);
                return;
            }
            Log(formation, now, "archerStand contact", step + " " + why + " gap=" + FormationGeometry.GapText(enemyGap));
        }

        private void PickSpot(Mission mission, Formation formation, Vec2 centre, Formation enemy, TerrainBattlePosition.Plan plan, bool planUsable, float now)
        {
            _hasSpot = true;
            _phase = Phase.Moving;
            if (planUsable)
            {
                _stay = false;
                _spot = plan.Hold;
                _spotWorld = plan.HoldWorld;
                _facing = plan.Facing;
                Log(formation, now, "archerStand spot", "position " + plan.KindName + " v" + plan.Version.ToString(CultureInfo.InvariantCulture) + " d=" + Fmt0(centre.Distance(_spot)) + "m", _spot);
                return;
            }
            EnemyConcentration.Result r;
            Vec2 target = EnemyConcentration.Find(mission, formation, EnemyConcentration.Range, out r) ? r.Centre : ((enemy != null) ? RBMAI.Utilities.GetFormationCenter(enemy) : Vec2.Invalid);
            _stay = true;
            _spotWorld = formation.CachedMedianPosition;
            _spot = _spotWorld.IsValid ? _spotWorld.AsVec2 : centre;
            _facing = Facing(_spot, target, formation);
            Log(formation, now, "archerStand spot", "stay", _spot);
        }

        private static Vec2 Facing(Vec2 from, Vec2 target, Formation formation)
        {
            if (target.IsValid)
            {
                Vec2 d = target - from;
                if (d.Normalize() > 0.01f)
                {
                    return d;
                }
            }
            Vec2 dir = formation.Direction;
            return (dir.IsValid && dir.LengthSquared > 0.01f) ? dir.Normalized() : Vec2.Invalid;
        }

        private void StartCharge(Mission mission, Formation formation, float now, string what, string why, Formation target = null)
        {
            _phase = Phase.Charge;
            _joined = true;
            float gap;
            _target = (target != null) ? target : NearestSignificantEnemy(mission, formation, out gap);
            gap = FormationGeometry.Gap(formation, FootprintMode.ScatterAware, _target, FootprintMode.ScatterAware);
            Log(formation, now, what, why + " target=" + TerrainCavalry.FormationName(_target) + " gap=" + FormationGeometry.GapText(gap));
            ChargeOrder(mission, formation);
        }

        private void ChargeOrder(Mission mission, Formation formation)
        {
            if (_target == null || _target.CountOfUnits <= 0 || !ThreatSignificance.IsSignificant(formation, _target))
            {
                float gap;
                _target = _footCharge ? NearestSignificantFoot(mission, formation, out gap) : NearestSignificantEnemy(mission, formation, out gap);
            }
            base.CurrentOrder = (_target != null) ? MovementOrder.MovementOrderChargeToTarget(_target) : MovementOrder.MovementOrderCharge;
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
            _stateName = "charge " + TerrainCavalry.FormationName(_target);
        }

        /// <summary>AI thread: the nearest enemy formation significant to the archers (edge gap, ScatterAware); gap MaxValue without one.</summary>
        private static Formation NearestSignificantEnemy(Mission mission, Formation formation, out float gap)
        {
            return NearestSignificant(mission, formation, false, out gap);
        }

        /// <summary>AI thread: as NearestSignificantEnemy, foot formations only.</summary>
        private static Formation NearestSignificantFoot(Mission mission, Formation formation, out float gap)
        {
            return NearestSignificant(mission, formation, true, out gap);
        }

        private static Formation NearestSignificant(Mission mission, Formation formation, bool footOnly, out float gap)
        {
            gap = float.MaxValue;
            Formation best = null;
            Footprint own;
            if (!FormationGeometry.TryGet(formation, FootprintMode.ScatterAware, out own))
            {
                return null;
            }
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(formation.Team))
                {
                    continue;
                }
                foreach (Formation e in team.FormationsIncludingSpecialAndEmpty)
                {
                    Footprint shape;
                    if (e == null || e.CountOfUnits <= 0 || (footOnly && ArmyComposition.IsMounted(e)) || !ThreatSignificance.IsSignificant(formation, e)
                        || !FormationGeometry.TryGet(e, FootprintMode.ScatterAware, out shape))
                    {
                        continue;
                    }
                    float g = FormationGeometry.Gap(own, shape);
                    if (g < gap)
                    {
                        gap = g;
                        best = e;
                    }
                }
            }
            return best;
        }

        private void ApplyShape()
        {
            Formation formation = base.Formation;
            if (formation == null || !formation.IsAIControlled || _escort || _phase == Phase.Charge || _phase == Phase.Idle)
            {
                return;
            }
            if (!_widthSet)
            {
                _widthSet = true;
                RBMAI.Utilities.TrySetRangedWidth(formation);
            }
            RBMAI.Utilities.TryApplyRangedArrangement(formation, ArrangementOrder.ArrangementOrderEnum.Line, true);
        }

        private void UpdateStateName(float distance)
        {
            string fight = (_late.State == HoldLateCharge.FightState.Contact) ? "contact " : "";
            string spot = _stay ? "stay" : "position";
            _stateName = fight + ((_phase == Phase.Holding) ? "holding " + spot : "moving " + spot + " " + Fmt0(distance) + "m");
        }

        /// <summary>Holder lost / player control: back to dormant; a later holder starts again.</summary>
        private void Release(string why)
        {
            if (_phase == Phase.Idle)
            {
                return;
            }
            Formation formation = base.Formation;
            Log(formation, (Mission.Current != null) ? Mission.Current.CurrentTime : 0f, _escort ? "archerStand escort release" : "archerStand release", why + " phase=" + _phase.ToString().ToLowerInvariant()
                + (_escort ? " target=" + TerrainCavalry.FormationName(_escortTarget) : ""));
            _phase = Phase.Idle;
            _movePoint.Clear();
            _lastPoint = WorldPosition.Invalid;
            _lastFacing = Vec2.Invalid;
            _escortTarget = null;
            _hasSpot = false;
            _stay = false;
            _spotWorld = WorldPosition.Invalid;
            _late.Reset(formation);
            _target = null;
            _footCharge = false;
            _stateName = "idle";
        }

        private static void Log(Formation formation, float now, string what, string why, Vec2 point = default(Vec2))
        {
            if (formation?.Team == null)
            {
                return;
            }
            string p = point.IsValid && (point.x != 0f || point.y != 0f) ? Fmt0(point.x) + "," + Fmt0(point.y) : null;
            TerrainLog.Behavior(formation.Team, formation, now, Name, what, (p != null) ? "spot" : null, p, why);
        }

        public override TextObject GetBehaviorString()
        {
            return new TextObject("{=RBMArcherStand01}standing their ground");
        }

        private static string Fmt0(float v)
        {
            return v.ToString("0", CultureInfo.InvariantCulture);
        }

        private static string Fmt2(float v)
        {
            return v.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
