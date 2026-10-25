using System;
using System.Globalization;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// When an army-relative terrain plan (archer spots, cavalry ground) is made again from where the armies stand NOW; the
    /// ground itself never changes. One instance per plan family, owned and cleared by it; this class only decides WHEN.
    /// Checked every CheckInterval against the last plan's reference (both armies' centres and main formations' facings): due
    /// ReplanPeriod after the last plan when a centre moved more than MinMove or a main formation turned more than MinTurn,
    /// or ReplanMinSeconds after it when a centre moved more than FastMove; at most ReplanMax per battle; never again once the
    /// lines are in melee (latched; centres closer than MeleeGap or a main infantry at MeleeStopShare in melee). Per side
    /// (SideMoved / SidePlanned): one side's new version only when THAT side's own army moved or turned since its own last plan.
    /// Each army's line direction (Armies.*Across) = across its main formation's facing within AcrossMaxAngle of the axis, else Invalid.
    /// THREADING: MAIN thread only (BattleTerrainLogic's tick): cached formation/team data and HoldLateCharge.MeleeShare's
    /// per-agent read; instance fields only, reset by Clear.
    /// </summary>
    internal sealed class TerrainPlanRefresh
    {

        public const float CheckInterval = 1f;

        // TIMER: ReplanPeriod after the last plan, a smaller change is enough: a centre moved MinMove or a main formation turned MinTurn.
        public const float ReplanPeriod = 30f;

        public const float MinMove = 40f; // m

        public const float MinTurn = 15f; // degrees

        public const float FastMove = 100f; // m: a centre that moved this much is due already ReplanMinSeconds after the last plan

        public const float ReplanMinSeconds = 20f; // between re-plans, and after the publish

        public const int ReplanMax = 10; // per battle and family

        public const float MeleeGap = 120f; // m: no re-plan once the armies' centres are closer ...

        public const float MeleeStopShare = 0.1f; // ... or once either main infantry has this share in melee (6 s window)

        public const float AcrossMaxAngle = 60f; // a main formation's facing sets its line direction only within this of the axis between the centres

        private static readonly float AcrossMinCos = (float)Math.Cos(AcrossMaxAngle * Math.PI / 180.0);

        /// <summary>Main-thread gather handed to the background re-plan. Immutable once made.</summary>
        internal sealed class Armies
        {
            public Vec2 Attacker;
            public Vec2 Defender;
            // Main formations' facings (unit; Invalid = unknown).
            public Vec2 AttackerFacing;
            public Vec2 DefenderFacing;
            // The direction each line runs across (unit; Invalid = across the axis).
            public Vec2 AttackerAcross;
            public Vec2 DefenderAcross;
        }

        private readonly string _name;
        private float _nextCheckAt;
        private bool _hasRef;
        private Armies _ref;
        private float _planAt = float.NaN; // first check after the publish, then each re-plan's start
        private int _replans;
        private bool _stopped;
        private bool _capLogged;
        // Per side (SideIndex): army centre and main facing at THAT side's own last plan (SideMoved / SidePlanned).
        private readonly Vec2[] _sideCentre = { Vec2.Invalid, Vec2.Invalid };
        private readonly Vec2[] _sideFacing = { Vec2.Invalid, Vec2.Invalid };

        public TerrainPlanRefresh(string name)
        {
            _name = name;
        }

        public void Clear()
        {
            _nextCheckAt = 0f;
            _hasRef = false;
            _ref = null;
            _planAt = float.NaN;
            _replans = 0;
            _stopped = false;
            _capLogged = false;
            for (int i = 0; i < _sideCentre.Length; i++)
            {
                _sideCentre[i] = Vec2.Invalid;
                _sideFacing[i] = Vec2.Invalid;
            }
        }

        private static int SideIndex(BattleSideEnum side)
        {
            return (side == BattleSideEnum.Attacker) ? 1 : 0;
        }

        /// <summary>Main thread, with a due re-plan's current armies: true when the side's OWN army centre moved more than MinMove or its main formation turned more than MinTurn since that side's own last plan. text = "own moved Xm turned Ydeg".</summary>
        public bool SideMoved(BattleSideEnum side, Armies current, out string text)
        {
            int i = SideIndex(side);
            text = null;
            if (current == null)
            {
                return false;
            }
            if (!_sideCentre[i].IsValid)
            {
                text = "own noRef";
                return true;
            }
            Vec2 centre = (side == BattleSideEnum.Attacker) ? current.Attacker : current.Defender;
            Vec2 facing = (side == BattleSideEnum.Attacker) ? current.AttackerFacing : current.DefenderFacing;
            float moved = centre.Distance(_sideCentre[i]);
            float turned = AngleDegrees(facing, _sideFacing[i]);
            text = "own moved " + Fmt0(moved) + "m turned " + Fmt0(turned) + "deg";
            return moved > MinMove || turned > MinTurn;
        }

        /// <summary>Main thread: <paramref name="side"/> was re-planned from <paramref name="armies"/> (its own reference from now on).</summary>
        public void SidePlanned(BattleSideEnum side, Armies armies)
        {
            if (armies == null)
            {
                return;
            }
            int i = SideIndex(side);
            _sideCentre[i] = (side == BattleSideEnum.Attacker) ? armies.Attacker : armies.Defender;
            _sideFacing[i] = (side == BattleSideEnum.Attacker) ? armies.AttackerFacing : armies.DefenderFacing;
        }

        /// <summary>MAIN thread, every tick after the publish (the owner skips it while its task runs or with the toggle off): the reason text when a re-plan is due now (the caller then calls Started), else null. note = a "stop" / "cap" line's fields, once each.</summary>
        public string Check(Mission mission, float now, out Armies armies, out string note)
        {
            armies = null;
            note = null;
            if (mission == null || _stopped || now < _nextCheckAt)
            {
                return null;
            }
            _nextCheckAt = now + CheckInterval;
            Armies current = Gather(mission);
            if (current == null)
            {
                return null;
            }
            if (!_hasRef)
            {
                _hasRef = true;
                _ref = current;
                _planAt = now;
                SidePlanned(BattleSideEnum.Attacker, current);
                SidePlanned(BattleSideEnum.Defender, current);
                return null;
            }
            float gap = current.Attacker.Distance(current.Defender);
            string melee = (gap < MeleeGap) ? "gap " + Fmt0(gap) + "m<" + Fmt0(MeleeGap) + "m" : MeleeText(mission);
            if (melee != null)
            {
                _stopped = true;
                note = "replanStop\tmelee " + melee + "\treplans=" + _replans.ToString(CultureInfo.InvariantCulture) + "/" + ReplanMax.ToString(CultureInfo.InvariantCulture);
                return null;
            }
            if (_replans >= ReplanMax)
            {
                if (!_capLogged)
                {
                    _capLogged = true;
                    note = "replanStop\tcap " + ReplanMax.ToString(CultureInfo.InvariantCulture);
                }
                return null;
            }
            float since = now - _planAt;
            float movedA = current.Attacker.Distance(_ref.Attacker);
            float movedD = current.Defender.Distance(_ref.Defender);
            float moved = Math.Max(movedA, movedD);
            float turned = Math.Max(AngleDegrees(current.AttackerFacing, _ref.AttackerFacing), AngleDegrees(current.DefenderFacing, _ref.DefenderFacing));
            string why;
            if (since >= ReplanMinSeconds && moved > FastMove)
            {
                why = "moved";
            }
            else if (since >= ReplanPeriod && (moved > MinMove || turned > MinTurn))
            {
                why = "timer";
            }
            else
            {
                return null;
            }
            armies = current;
            return why + " atk moved " + Fmt0(movedA) + "m def moved " + Fmt0(movedD) + "m turned " + Fmt0(turned) + "deg gap " + Fmt0(gap)
                + "m after " + Fmt0(since) + "s n=" + (_replans + 1).ToString(CultureInfo.InvariantCulture) + "/" + ReplanMax.ToString(CultureInfo.InvariantCulture)
                + " across atk=" + (current.AttackerAcross.IsValid ? "line" : "axis") + " def=" + (current.DefenderAcross.IsValid ? "line" : "axis");
        }

        /// <summary>Main thread: a re-plan from <paramref name="armies"/> started; they are the reference from now on.</summary>
        public void Started(float now, Armies armies)
        {
            _replans++;
            _planAt = now;
            _ref = armies;
            _hasRef = true;
        }

        /// <summary>Main thread: both centres, both main formations' facings and line directions; null without both centres.</summary>
        private static Armies Gather(Mission mission)
        {
            Vec2 attacker;
            Vec2 defender;
            if (!TerrainFlankPlan.ArmyCentre(mission, BattleSideEnum.Attacker, out attacker) || !TerrainFlankPlan.ArmyCentre(mission, BattleSideEnum.Defender, out defender))
            {
                return null;
            }
            Vec2 axis = defender - attacker;
            bool hasAxis = axis.Normalize() >= 1f;
            Armies armies = new Armies
            {
                Attacker = attacker,
                Defender = defender,
                AttackerFacing = Facing(TerrainFlankPlan.MainFormation(mission, BattleSideEnum.Attacker, true)),
                DefenderFacing = Facing(TerrainFlankPlan.MainFormation(mission, BattleSideEnum.Defender, true)),
                AttackerAcross = Vec2.Invalid,
                DefenderAcross = Vec2.Invalid
            };
            if (hasAxis)
            {
                armies.AttackerAcross = Across(armies.AttackerFacing, axis);
                armies.DefenderAcross = Across(armies.DefenderFacing, axis);
            }
            return armies;
        }

        private static Vec2 Facing(Formation formation)
        {
            if (formation == null)
            {
                return Vec2.Invalid;
            }
            Vec2 facing = formation.Direction;
            if (!facing.IsValid || facing.Normalize() < 0.001f)
            {
                return Vec2.Invalid;
            }
            return facing;
        }

        /// <summary>Across a facing within AcrossMaxAngle of the axis (either way along it); Invalid otherwise. The worker orients it.</summary>
        private static Vec2 Across(Vec2 facing, Vec2 axis)
        {
            if (!facing.IsValid || Math.Abs(Vec2.DotProduct(facing, axis)) < AcrossMinCos)
            {
                return Vec2.Invalid;
            }
            return new Vec2(-facing.y, facing.x);
        }

        /// <summary>"atk main 0.12 in melee" when a main infantry is in melee, else null.</summary>
        private static string MeleeText(Mission mission)
        {
            foreach (BattleSideEnum side in new[] { BattleSideEnum.Attacker, BattleSideEnum.Defender })
            {
                Formation main = TerrainFlankPlan.MainFormation(mission, side, true);
                if (main == null || !main.QuerySystem.IsInfantryFormation)
                {
                    continue;
                }
                float share = HoldLateCharge.MeleeShare(main);
                if (share >= MeleeStopShare)
                {
                    return ((side == BattleSideEnum.Attacker) ? "atk" : "def") + " main " + share.ToString("0.##", CultureInfo.InvariantCulture) + " in melee";
                }
            }
            return null;
        }

        /// <summary>0 when either is not a direction.</summary>
        private static float AngleDegrees(Vec2 a, Vec2 b)
        {
            if (!a.IsValid || !b.IsValid || a.LengthSquared < 1e-6f || b.LengthSquared < 1e-6f)
            {
                return 0f;
            }
            float dot = Vec2.DotProduct(a.Normalized(), b.Normalized());
            return (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, dot))) * 180.0 / Math.PI);
        }

        private static string Fmt0(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0", CultureInfo.InvariantCulture);
        }

        public override string ToString()
        {
            return _name;
        }
    }
}
