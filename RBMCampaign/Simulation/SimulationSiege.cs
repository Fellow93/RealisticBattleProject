using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// A STORMED WALL IS NOT A FIELD BATTLE, and auto-resolve has only ever known how to fight one of those.
    ///
    /// The field model has three acts -- a volley across open ground, a skirmish of horse and javelins, and then the
    /// lines meet (see SimulationBattleState). None of that is a siege. There is no ground both sides agreed to
    /// cross, no cavalry to ride out, no line to meet: there is a wall, a killing ground in front of it, and a
    /// handful of places a man can actually get up or through. So a wall assault gets its own two acts instead, and
    /// this file is the whole of them.
    ///
    ///   1. THE APPROACH. Nobody is in reach of anybody. The men on the parapet shoot down into the open, the
    ///      besiegers shoot back up at them, and no sword touches anything. The defender has every advantage there
    ///      is: he shoots FIVE times as often (he is standing still behind stone with the stores at his elbow, while
    ///      the attacker is carrying a ladder), his shafts do more when they land, and the besieger's go wide more
    ///      often and land softer. And the besieger can only reach the men who are shooting at him -- the defending
    ///      infantry are behind the parapet where an arrow cannot find them, and there is no point loosing at stone.
    ///
    ///   2. THE ASSAULT. The ladders go up, the ram reaches the gate, and the fighting is hand to hand at whatever
    ///      openings exist. The defender's edge narrows: he shoots twice as often rather than five times, and with no
    ///      bonus to what the shot is worth. The attacker keeps his penalties -- he is fighting up a ladder -- but he
    ///      can now reach anybody.
    ///
    /// WIDTH is what makes the assault a siege rather than a brawl. Three lanes -- native's own
    /// <c>MaximumAttackerMeleeSiegeEngineCount = 3</c> -- and what stands in each decides how many men can fight
    /// there. A breach in the wall is a wide fight; a ladder is one man at the top against five waiting for him. The
    /// two sides get DIFFERENT widths, and the ratio between them is the proportion of the round's melee each side
    /// gets to throw. See <see cref="Widths"/> for the table.
    ///
    /// And width MOVES. Every man the attackers kill at the opening widens it by one for both sides -- the press
    /// gives ground, the fight spills along the wall. Every man the defenders kill narrows it again. It can never
    /// fall below what the equipment bought at the start, and there is no ceiling: an assault that is going well goes
    /// better, which is what a collapsing defence looks like from the outside.
    ///
    /// GATED ON THE MASTER SWITCH ONLY (SimulationEquipmentPower.SimulationEnabled). Every number here is a
    /// hardcoded constant, deliberately: they are judgements about what a wall is worth, of a piece with the charge
    /// chances and the volley lengths beside them, and they are calibrated against a paired log rather than tuned by
    /// hand in a menu.
    /// </summary>
    internal static class SimulationSiege
    {
        // =========================================================================================================
        // THE APPROACH

        /// <summary>
        /// How long the killing ground takes to cross, in Progress units (see BattleState.Progress).
        ///
        /// Held at twelve, which is exactly what the field model already spent on a siege volley. That is not
        /// laziness: keeping the length where it was means this rewrite is a REDISTRIBUTION of a siege's fire and
        /// not also a change to how much of one there is, so the first paired log reads as one change rather than
        /// two. It is the obvious first dial to move once the rest is measured.
        /// </summary>
        internal const float ApproachRounds = 12f;

        /// <summary>
        /// How many shots the defender gets for each one the attacker gets, while the ground is being crossed.
        ///
        /// Five, and it is meant to be brutal. The man on the wall is standing still behind stone, shooting down,
        /// with the town's arrow stores stacked at his elbow (see BattleState.DefendersShootFromStores) and nothing
        /// to do but shoot. The man below him is walking uphill into it carrying a ladder, in a formation that
        /// cannot stop to loose without stopping the assault. This ratio is the single largest reason storming a
        /// wall should cost more men than meeting the same garrison in a field.
        /// </summary>
        private const float ApproachDefenderFireRatio = 5f;

        /// <summary>What a defender's shot is worth on the approach. He is shooting down, at his leisure, into
        /// men who are packed and climbing -- the roll runs high.</summary>
        private const float ApproachDefenderMagnitude = 1.25f;

        /// <summary>...and what an attacker's is worth. He is shooting up, at a head behind a merlon, on the move --
        /// the roll runs low. Applied to fired shots, which on the approach is every blow there is.</summary>
        private const float ApproachAttackerMagnitude = 0.85f;

        /// <summary>How much likelier the besieger is to put a shaft into the stonework rather than the man behind
        /// it, on the approach. Stacks on the existing wall skew (simulationSiegeRangedMissSkew), which is the
        /// geometry of shooting up at a battlement; this is the phase's own term on top of it.</summary>
        private const float ApproachAttackerMissMultiplier = 1.5f;

        // =========================================================================================================
        // THE ASSAULT

        /// <summary>
        /// The defender's rate of fire once the ladders are up, against the attacker's. Down from five to two: the
        /// enemy is over the parapet and among the shooters now, and a man loosing into a fight his own friends are
        /// in cannot loose as freely as he did into an empty field. He keeps an edge -- he is still on his own
        /// ground, still has the stores -- but it is an edge, not a monopoly.
        /// </summary>
        private const float AssaultDefenderFireRatio = 2f;

        /// <summary>The attacker's shot is still worse once he is on the wall -- he is fighting up a ladder or
        /// through a gap with no room to draw. The defender gets NO bonus here, by design: his advantage in the
        /// assault is the width and the rate of fire, not the weight of the arrow.</summary>
        private const float AssaultAttackerMagnitude = 0.85f;

        /// <summary>And he still misses more, though less wildly than on the approach: he is closer, but he is
        /// shooting from a ladder into a press.</summary>
        private const float AssaultAttackerMissMultiplier = 1.35f;

        // =========================================================================================================
        // THE WALL ITSELF -- how good a wall it is.
        //
        // EVERYTHING ABOVE DESCRIBES THE BEST WALL IN THE GAME. A settlement's fortifications are BUILT, though, and
        // a hastily raised palisade is not the curtain wall of a great city: native tracks it as a building level on
        // the settlement (Town.GetWallLevel, 1 to 3, off SettlementFortifications for a town and
        // CastleFortifications for a castle). Higher walls mean higher, thicker parapets, better merlons to shoot
        // from and hide behind, deeper embrasures, and a longer, worse climb.
        //
        // So level 3 is the REFERENCE and scaling only ever runs DOWNWARD from it. A fully fortified city plays
        // exactly as this model did before wall level was read at all; everything below it is a lesser version of
        // that same wall. This is the right way round because the constants above were judged against the picture
        // of a proper siege -- men on a high battlement with the stores behind them -- and that picture is a level 3.
        //
        // WHAT IT SCALES IS THE ADVANTAGE, NOT THE NUMBER. Every dial above is expressed as a departure from
        // parity -- the defender's fire ratio is 1 plus four, his magnitude is 1 plus a quarter, the attacker's is 1
        // minus a sixth -- and the wall scales that DEPARTURE. A poor wall is a weaker version of the same edge and
        // a great one a stronger version of it, and no amount of scaling can ever invert an advantage into a
        // handicap, which scaling the raw numbers absolutely could.
        //
        // IT DOES NOT TOUCH THE WIDTH, deliberately. Width is a fact about the OPENINGS -- how wide the breach is,
        // how many men fit through a gatehouse, how many can stand at the top of one ladder -- and a hole in a great
        // wall is the same size as a hole in a poor one. What a better wall buys you is a worse approach to it, not
        // a narrower gap once it is down.

        /// <summary>The wall a settlement is credited with when its level cannot be read at all -- the best one, so
        /// a bad reading can never quietly hand a great city to a besieger by treating it as a palisade.</summary>
        private const int ReferenceWallLevel = 3;

        /// <summary>What each level of Fortifications is worth, as a share of the whole advantage. Built walls now
        /// EARN the defender's edge rather than merely failing to lose it: a palisade scales every departure from
        /// parity by 1.1, middling walls by 1.2, great walls by 1.3 -- so the defender's rate of fire on the approach
        /// runs 5.4:1, 5.8:1 and 6.2:1 down the three levels, and the besieger's magnitude penalty deepens from
        /// 0.835 through 0.82 to 0.805. Enough to feel, not enough to make the wall the whole battle; the garrison
        /// is still what decides a siege.</summary>
        private const float WallLevelStep = 0.1f;

        /// <summary>Wall levels run 1 to 3, but GetWallLevel returns 0 for a fortification whose building it cannot
        /// find at all -- so the reading is clamped rather than trusted.</summary>
        private const int MinWallLevel = 1;
        private const int MaxWallLevel = 3;

        /// <summary>
        /// Scale an advantage held ABOVE parity by the quality of the wall. The defender's fire ratio and magnitude
        /// bonus, and the besieger's miss multiplier, are all of this shape: 1 plus something.
        /// </summary>
        private static float ScaleUp(float value, float wallFactor)
        {
            return 1f + ((value - 1f) * wallFactor);
        }

        /// <summary>...and the mirror, for a penalty held BELOW parity -- the besieger's magnitude. Kept separate
        /// from ScaleUp only for readability at the call sites; the arithmetic is the same 1 + (v-1)*f.</summary>
        private static float ScaleDown(float value, float wallFactor)
        {
            return 1f - ((1f - value) * wallFactor);
        }

        /// <summary>What the defender's shot is worth on the approach, on THIS wall.</summary>
        internal static float DefenderShotMagnitude(SimulationBattleState.BattleState state)
        {
            // Only the approach carries a defender bonus at all; the assault deliberately carries none.
            return IsApproach(state) ? ScaleUp(ApproachDefenderMagnitude, WallFactor(state)) : 1f;
        }

        /// <summary>What the besieger's shot is worth, on THIS wall, in whichever act he is in.</summary>
        internal static float AttackerShotMagnitude(SimulationBattleState.BattleState state)
        {
            float baseline = IsApproach(state) ? ApproachAttackerMagnitude : AssaultAttackerMagnitude;
            return ScaleDown(baseline, WallFactor(state));
        }

        /// <summary>How much wider the besieger's shots scatter, on THIS wall, in whichever act he is in.</summary>
        internal static float AttackerMissMultiplier(SimulationBattleState.BattleState state)
        {
            float baseline = IsApproach(state) ? ApproachAttackerMissMultiplier : AssaultAttackerMissMultiplier;
            return ScaleUp(baseline, WallFactor(state));
        }

        /// <summary>How many shots the defender gets for each of the attacker's, on THIS wall, in this act.</summary>
        private static float FireRatio(SimulationBattleState.BattleState state, bool approach)
        {
            float baseline = approach ? ApproachDefenderFireRatio : AssaultDefenderFireRatio;
            return ScaleUp(baseline, WallFactor(state));
        }

        private static float WallFactor(SimulationBattleState.BattleState state)
        {
            return (state != null) ? state.SiegeWallFactor : 1f;
        }

        /// <summary>
        /// Read the wall once, when the battle is set up. It cannot change while a storm is being fought -- nobody
        /// finishes a building under assault -- so this is measured at the muster and carried, rather than asked on
        /// every blow.
        ///
        /// A settlement with no Town component, or a wall level the game could not name, falls back to the
        /// REFERENCE wall rather than to a poor one. That is the conservative failure and not the generous one:
        /// falling back to the reference means an unreadable settlement behaves exactly as this model did before
        /// wall level existed here, so a bad reading can never quietly hand a siege to the besieger by treating a
        /// great city as a palisade. GetWallLevel really does return 0 -- not 1 -- when it cannot find the building.
        /// </summary>
        private static float MeasureWall(Settlement settlement)
        {
            if (settlement == null || settlement.Town == null)
            {
                return 1f;
            }
            int level = settlement.Town.GetWallLevel();
            if (level < MinWallLevel)
            {
                level = ReferenceWallLevel;
            }
            else if (level > MaxWallLevel)
            {
                level = MaxWallLevel;
            }
            return 1f + (level * WallLevelStep);
        }

        // =========================================================================================================
        // WIDTH -- what each lane is worth to each side.
        //
        // Read as "how many men can fight here at once", though the numbers are used as a RATIO and not as a
        // headcount (see SplitTicks). A ladder is the shape of the whole idea: one attacker can be at the top of it
        // at a time, and five defenders can be waiting for him when he arrives.

        /// <summary>A hole in the wall. Wide enough for a real fight, and it fights the same both ways -- the men
        /// coming through and the men holding it have the same frontage.</summary>
        private const int BreachAttacker = 4;
        private const int BreachDefender = 4;

        /// <summary>A siege tower: a ramp at parapet height, and a fight across it on level footing.</summary>
        private const int TowerAttacker = 4;
        private const int TowerDefender = 4;

        /// <summary>The gate, once the ram is at it. The widest opening in any siege -- a gatehouse passage is
        /// built for carts -- which is why the ram is worth having and worth destroying.</summary>
        private const int RamAttacker = 8;
        private const int RamDefender = 8;

        /// <summary>A ladder, and the reason nobody wants to storm a wall with ladders. One man at the top of it
        /// against five with their spears already levelled at where his head is going to appear.</summary>
        private const int LadderAttacker = 1;
        private const int LadderDefender = 5;

        /// <summary>
        /// How many stretches of wall a fortification has, which is how many climbing lanes there can be.
        ///
        /// Two, and it is the game's own number: <c>Settlement.WallSectionCount</c> returns a hardcoded 2 for every
        /// fortification in Calradia. Restated here rather than read per battle because it is a constant of the
        /// game and the loop wants a name for it -- but if a future version ever makes it vary, this is the one
        /// place that has to learn to ask.
        ///
        /// The third lane is the gate, and it is not a wall section -- see Widths.
        /// </summary>
        private const int WallSections = 2;

        // =========================================================================================================
        // Phases.

        private const int PhaseApproach = 0;
        private const int PhaseAssault = 1;

        /// <summary>The attackers had no way in at all when the ladders should have gone up, and the storm is over
        /// before it began. See <see cref="Repulsed"/>.</summary>
        private const int PhaseRepulsed = 2;

        /// <summary>
        /// Is this the crossing of the killing ground -- nobody in reach of anybody, and only the bows at work?
        /// False for every battle that is not a wall assault.
        ///
        /// A REPULSED assault counts as approach, and that is not a fudge. Repulsed means the men crossed the ground
        /// and found nothing to climb: they are still out there in the open, still being shot at, and still unable
        /// to touch anybody -- which is the approach exactly. The battle is ended at the close of that same round
        /// (SimulationSiegeRepulse), and this keeps the one round in between fought as what it actually was rather
        /// than as a melee that could not physically have happened.
        /// </summary>
        internal static bool IsApproach(SimulationBattleState.BattleState state)
        {
            return state != null && state.SiegeAssaultBattle && state.SiegePhase != PhaseAssault;
        }

        /// <summary>Is this the storm itself -- the ladders up, the fight at the openings?</summary>
        internal static bool IsAssault(SimulationBattleState.BattleState state)
        {
            return state != null && state.SiegeAssaultBattle && state.SiegePhase == PhaseAssault;
        }

        /// <summary>The storm never happened: nothing survived to climb or break, and the attackers are done.</summary>
        internal static bool Repulsed(SimulationBattleState.BattleState state)
        {
            return state != null && state.SiegeAssaultBattle && state.SiegePhase == PhaseRepulsed;
        }

        // =========================================================================================================
        // The round.

        /// <summary>
        /// Set a battle up as a wall assault, if that is what it is. Called once, when the state is made.
        ///
        /// Nothing is measured here but the FACT of it. The widths cannot be read yet -- they are read at the moment
        /// the approach ends, because what matters is what is still standing THEN, not what was standing when the
        /// banners came into view.
        /// </summary>
        internal static void Begin(MapEvent mapEvent, SimulationBattleState.BattleState state)
        {
            if (mapEvent == null || state == null)
            {
                return;
            }
            state.SiegeAssaultBattle = mapEvent.IsSiegeAssault;
            state.SiegePhase = PhaseApproach;
            state.SiegeWallFactor = state.SiegeAssaultBattle ? MeasureWall(mapEvent.MapEventSettlement) : 1f;
            if (state.SiegeAssaultBattle)
            {
                state.SiegeEngineReport = DescribeEngines(mapEvent);
            }
        }

        /// <summary>
        /// A round has turned. Called from AdvanceRound, after Progress has been advanced, so the phase this sets is
        /// the phase the round about to be fought belongs to.
        ///
        /// The transition is the whole of the interesting part: the approach ends, and at that instant the siege
        /// equipment is read and the widths are frozen. A ram that was broken on the way in contributes nothing
        /// (its lane goes to zero -- there is no ladder up a gate), and if NOTHING survived, there is no assault to
        /// fight at all.
        /// </summary>
        internal static void OnRound(MapEvent mapEvent, SimulationBattleState.BattleState state)
        {
            if (state == null || !state.SiegeAssaultBattle)
            {
                return;
            }

            // FIRST, IS THIS STILL A WALL ASSAULT AT ALL? Asked every round, ahead of everything else, because the
            // answer can change underneath us -- see StandDown.
            if (!mapEvent.IsSiegeAssault)
            {
                StandDown(mapEvent, state);
                return;
            }

            if (state.SiegePhase != PhaseApproach)
            {
                return;
            }
            if (state.Progress <= ApproachRounds)
            {
                return;
            }

            int attackWidth;
            int defendWidth;
            _lanes = new StringBuilder();
            if (!Widths(mapEvent, out attackWidth, out defendWidth))
            {
                // THE WALL COULD NOT BE READ AT ALL -- no settlement on the event, or no siege event on it. That
                // should not happen for a battle the game itself calls a siege assault, but if it ever does, the
                // answer must not be "the attackers lose". A width of nothing would repulse every storm in the
                // campaign off a null reference, silently, and hand every siege in the world to the defender. So an
                // unreadable wall is assumed to be the poorest assault there is -- ladders against both stretches
                // of wall -- which is a fight the attackers can still lose honestly and one they can still win.
                attackWidth = 2 * LadderAttacker;
                defendWidth = 2 * LadderDefender;
                Note("wall unreadable, assumed ladders");
            }
            state.SiegeLanes = (_lanes.Length > 0) ? _lanes.ToString() : "nothing -- no way in";
            _lanes = null;

            // NOTHING TO CLIMB AND NOTHING TO BREAK. Every ladder burned, every tower broken, the ram destroyed and
            // the wall still whole -- so the men who crossed the killing ground have arrived at a sheer face with
            // nothing in their hands. That is not a fight they can lose slowly; it is a fight they cannot start. The
            // storm is over, and the casualties they took crossing are what the assault cost them.
            if (attackWidth <= 0)
            {
                state.SiegePhase = PhaseRepulsed;
                return;
            }

            state.StartAttackWidth = attackWidth;
            state.StartDefendWidth = defendWidth;
            state.AttackWidth = attackWidth;
            state.DefendWidth = defendWidth;
            state.SiegePhase = PhaseAssault;
        }

        /// <summary>
        /// THE BATTLE HAS COME DOWN OFF THE WALL, and the model has to come down with it.
        ///
        /// A wall assault does not necessarily stay one. Native keeps the battle's kind in a mutable field and
        /// changes it mid-event: the instant a defending party that is not IN the settlement joins -- a relief army
        /// arriving to lift the siege -- <c>MapEvent.AddParty</c> rewrites the type from Siege to SiegeOutside, and
        /// the fight becomes a field battle in the open beneath the walls. <c>SimulationContext</c> is derived from
        /// that same field, so the ground changes with it.
        ///
        /// EVERY SIEGE FACT IS LATCHED AT ROUND ONE, which is right for a fact about a wall and wrong for a battle
        /// that has stopped being about one. Left alone, the model went on for another hundred rounds treating a
        /// pitched field battle as a storm: the attackers dismounted and unable to charge, confined to the frontage
        /// their ladders bought, and eating the defenders' wall bonuses -- against an army standing in the open
        /// field beside them. (Tamnuh Castle, 1084-030: the garrison went from 223 men to 673 at round 13 and the
        /// storm rules ran to round 116.)
        ///
        /// So the siege model stands down and the terrain is re-read as what it now is (see
        /// SimulationBattleState.RelatchTerrain). The horses come back, the charge comes back, the wall's bonuses
        /// stop, the artillery falls silent, and the frontage stops mattering -- because none of those things are
        /// true of a field.
        ///
        /// IT IS ONE-WAY, and that is deliberate. Native flips the type BACK to Siege when the relief force is gone
        /// again (<c>RemoveParty</c>, when every remaining defender is once more inside the settlement), so an
        /// arriving-and-destroyed relief army would otherwise have this oscillating -- re-arming a storm whose
        /// approach was crossed an hour ago, with fresh widths read off equipment the assault already used. A
        /// battle that has left the wall does not go back onto it; the men are on the field now.
        /// </summary>
        private static void StandDown(MapEvent mapEvent, SimulationBattleState.BattleState state)
        {
            state.SiegeAssaultBattle = false;
            state.SiegeStoodDownRound = state.Round;
            state.SiegeWallFactor = 1f;
            SimulationBattleState.RelatchTerrain(mapEvent, state);
        }

        /// <summary>
        /// EVERY RANGED ENGINE ON BOTH SIDES, AND WHY EACH ONE DOES OR DOES NOT COUNT. Taken once, at the muster.
        ///
        /// This exists because the logs raised a question the logs could not answer: across eight sieges the
        /// besiegers fired seven artillery shots between them, and six of the eight fired none at all. An engine
        /// that never appears is indistinguishable, from the outside, between three quite different causes -- it
        /// was never deployed, it was deployed but is not finished (<c>Progress &lt; 1</c>, so not
        /// <c>IsConstructed</c>), it is mid-redeployment (<c>RedeploymentProgress &lt; 1</c>), or it is broken. So
        /// each slot reports which of those it is, and the next siege settles it instead of another guess.
        /// </summary>
        private static string DescribeEngines(MapEvent mapEvent)
        {
            Settlement settlement = (mapEvent != null) ? mapEvent.MapEventSettlement : null;
            SiegeEvent siegeEvent = (settlement != null && settlement.Party != null)
                ? settlement.Party.SiegeEvent : null;
            if (siegeEvent == null)
            {
                return "no siege event to read";
            }

            StringBuilder sb = new StringBuilder();
            AppendSideEngines(sb, "attacker", siegeEvent.GetSiegeEventSide(BattleSideEnum.Attacker));
            AppendSideEngines(sb, "defender", siegeEvent.GetSiegeEventSide(BattleSideEnum.Defender));
            return sb.ToString();
        }

        private static void AppendSideEngines(StringBuilder sb, string label, ISiegeEventSide side)
        {
            if (sb.Length > 0)
            {
                sb.Append("  |  ");
            }
            sb.Append(label).Append(" ");

            if (side == null || side.SiegeEngines == null)
            {
                sb.Append("(no engine container)");
                return;
            }

            SiegeEvent.SiegeEngineConstructionProgress[] slots = side.SiegeEngines.DeployedRangedSiegeEngines;
            if (slots == null || slots.Length == 0)
            {
                sb.Append("(no ranged slots)");
                return;
            }

            bool any = false;
            for (int i = 0; i < slots.Length; i++)
            {
                SiegeEvent.SiegeEngineConstructionProgress engine = slots[i];
                if (engine == null)
                {
                    continue;
                }
                if (any)
                {
                    sb.Append(", ");
                }
                any = true;
                sb.Append(engine.SiegeEngine != null ? engine.SiegeEngine.StringId : "?");
                if (engine.Hitpoints <= 0f)
                {
                    sb.Append("[broken]");
                }
                else if (!engine.IsConstructed)
                {
                    sb.Append("[building ").Append(engine.Progress.ToString("0.00")).Append("]");
                }
                else if (engine.IsBeingRedeployed)
                {
                    sb.Append("[redeploying ").Append(engine.RedeploymentProgress.ToString("0.00")).Append("]");
                }
            }
            // The reserve matters too: an engine sitting in reserve is built and paid for but is not on the wall,
            // and would explain a side that "has" artillery which never fires.
            int reserved = 0;
            foreach (SiegeEvent.SiegeEngineConstructionProgress spare in side.SiegeEngines.ReservedSiegeEngines)
            {
                if (spare != null && spare.SiegeEngine != null && spare.SiegeEngine.IsRanged)
                {
                    reserved++;
                }
            }
            if (!any)
            {
                sb.Append("none deployed");
            }
            if (reserved > 0)
            {
                sb.Append(" (+").Append(reserved).Append(" in reserve)");
            }
        }

        // =========================================================================================================
        // Reading the wall.

        /// <summary>
        /// What the lanes held, written down as they are counted -- purely for the log, which otherwise can show
        /// that a storm had a frontage of two without showing that it had a frontage of two because the besieger
        /// brought a single ladder. Non-null only for the one call that measures the wall (see OnRound), so the
        /// counting itself pays nothing for it on any other path.
        /// </summary>
        private static StringBuilder _lanes;

        private static void Note(string what)
        {
            if (_lanes == null)
            {
                return;
            }
            if (_lanes.Length > 0)
            {
                _lanes.Append(" · ");
            }
            _lanes.Append(what);
        }

        /// <summary>
        /// What the three lanes are worth, to each side.
        ///
        /// LANES ARE IDENTIFIED BY WHAT STANDS IN THEM, NOT BY SLOT INDEX, and that correction was paid for in a
        /// real siege. Native's three melee slots (<c>SiegeEnginesContainer.DeployedMeleeSiegeEngines</c>) look
        /// like three positions on the wall and are nothing of the kind: <c>DefaultSiegeStrategyActionModel</c>
        /// deploys into <c>FindIndex(engine == null)</c> -- THE FIRST FREE SLOT. The index is the order the
        /// besieger happened to finish building things in, and carries no meaning whatever. Reading slot 1 as "the
        /// gate" put a ram in slot 0 outside the model entirely: it was standing, the defenders were shooting at
        /// it, and it bought its side nothing (Tamnuh Castle, 1084-029).
        ///
        /// So the lanes are assembled from the equipment instead:
        ///
        ///   THE GATE is wherever the ram is, if there is one. A ram has exactly one thing it can be pointed at.
        ///
        ///   THE TWO WALL LANES are the settlement's two wall sections -- <c>Settlement.WallSectionCount</c> is
        ///   hardcoded to 2 for every fortification in the game. Each is either a HOLE, if that section's hit
        ///   points have reached nothing, or whatever climbing equipment is assigned to it: a tower, or a ladder.
        ///
        /// A BREACHED SECTION IS A HOLE AND NOTHING ELSE. Men walk through a gap; they do not queue for a ladder
        /// beside it. The climbing equipment displaced by a hole is not lost, it is simply not needed there -- and
        /// with only two stretches of wall to climb, a besieger who built three towers has one with nowhere to go.
        /// That surplus is NOTED rather than silently dropped, because silently dropping equipment is the exact bug
        /// this rewrite exists to fix.
        ///
        /// AN EMPTY LANE IS WORTH NOTHING. No fallback ladder, no floor -- if the attacker brought nothing to a
        /// stretch of wall and did not break it, nobody fights there. That is what makes a repulse possible.
        ///
        /// Returns false if the wall could not be read at all -- no settlement, or no siege event on it. That is a
        /// different thing from reading it and finding nothing, and the caller must treat it differently: a wall
        /// that cannot be read must never be mistaken for a wall with no way in. See OnRound.
        /// </summary>
        private static bool Widths(MapEvent mapEvent, out int attackWidth, out int defendWidth)
        {
            attackWidth = 0;
            defendWidth = 0;

            Settlement settlement = (mapEvent != null) ? mapEvent.MapEventSettlement : null;
            if (settlement == null || settlement.Party == null)
            {
                return false;
            }

            SiegeEvent siegeEvent = settlement.Party.SiegeEvent;
            if (siegeEvent == null)
            {
                return false;
            }

            SiegeEvent.SiegeEngineConstructionProgress[] engines = null;
            ISiegeEventSide attackerSide = siegeEvent.GetSiegeEventSide(BattleSideEnum.Attacker);
            if (attackerSide != null && attackerSide.SiegeEngines != null)
            {
                engines = attackerSide.SiegeEngines.DeployedMeleeSiegeEngines;
            }

            // Sort the surviving equipment by what it is FOR: the one thing that breaks a gate, and the things that
            // get men onto a wall. Slot order is kept only so the log reads in the order the besieger built them.
            bool hasRam = false;
            List<SiegeEngineType> climbers = new List<SiegeEngineType>();
            if (engines != null)
            {
                for (int slot = 0; slot < engines.Length; slot++)
                {
                    SiegeEngineType engine = LiveEngineAt(engines, slot);
                    if (engine == null)
                    {
                        continue;
                    }
                    if (engine == DefaultSiegeEngineTypes.Ram || engine == DefaultSiegeEngineTypes.ImprovedRam)
                    {
                        hasRam = true;
                    }
                    else if (engine == DefaultSiegeEngineTypes.SiegeTower
                        || engine == DefaultSiegeEngineTypes.HeavySiegeTower
                        || engine == DefaultSiegeEngineTypes.Ladder)
                    {
                        climbers.Add(engine);
                    }
                    // Anything else in a melee slot is not a way in (native puts nothing else there) and is worth
                    // nothing rather than guessed at.
                }
            }

            // THE GATE.
            if (hasRam)
            {
                attackWidth += RamAttacker;
                defendWidth += RamDefender;
                Note("gate: ram " + RamAttacker + "/" + RamDefender);
            }
            else
            {
                Note("gate: shut");
            }

            // THE TWO STRETCHES OF WALL.
            int nextClimber = 0;
            for (int section = 0; section < WallSections; section++)
            {
                string name = (section == 0) ? "left" : "right";

                if (IsBreached(settlement, section))
                {
                    attackWidth += BreachAttacker;
                    defendWidth += BreachDefender;
                    Note(name + ": breach " + BreachAttacker + "/" + BreachDefender);
                    continue;
                }

                if (nextClimber >= climbers.Count)
                {
                    Note(name + ": empty");
                    continue;
                }

                SiegeEngineType climber = climbers[nextClimber];
                nextClimber++;

                if (climber == DefaultSiegeEngineTypes.Ladder)
                {
                    attackWidth += LadderAttacker;
                    defendWidth += LadderDefender;
                    Note(name + ": ladder " + LadderAttacker + "/" + LadderDefender);
                }
                else
                {
                    attackWidth += TowerAttacker;
                    defendWidth += TowerDefender;
                    Note(name + ": tower " + TowerAttacker + "/" + TowerDefender);
                }
            }

            // What he built and could not use. There are two stretches of wall and no more, so a third tower stands
            // in the camp. Said out loud -- equipment that vanishes without a word is what went wrong last time.
            int surplus = climbers.Count - nextClimber;
            if (surplus > 0)
            {
                Note(surplus + " spare (no wall left to climb)");
            }

            return true;
        }

        /// <summary>A wall section is a hole when its hit points have reached nothing. Native's own reading -- the
        /// ratio list is what the siege mission builds its breaches from.</summary>
        private static bool IsBreached(Settlement settlement, int section)
        {
            MBReadOnlyList<float> sections = settlement.SettlementWallSectionHitPointsRatioList;
            if (sections == null || section < 0 || section >= sections.Count)
            {
                return false;
            }
            return sections[section] <= 0f;
        }

        /// <summary>The engine standing in a lane, if there is one and it is still worth something. An engine that
        /// was never finished (<c>IsActive</c> is false until it is built and in place) or has been broken down to
        /// nothing is not a way in.</summary>
        private static SiegeEngineType LiveEngineAt(SiegeEvent.SiegeEngineConstructionProgress[] engines, int lane)
        {
            if (engines == null || lane < 0 || lane >= engines.Length)
            {
                return null;
            }
            SiegeEvent.SiegeEngineConstructionProgress engine = engines[lane];
            if (engine == null || !engine.IsActive || engine.Hitpoints <= 0f)
            {
                return null;
            }
            return engine.SiegeEngine;
        }

        // =========================================================================================================
        // Dividing the round.

        /// <summary>
        /// HOW MANY BLOWS EACH SIDE THROWS THIS ROUND, and what share of each side's blows are shot rather than
        /// swung. Called from the tick allocation, which is the only place the simulation says a round has turned.
        ///
        /// The game gives each side ONE number -- its tick count -- and a siege has two quite different things to
        /// say about a round, so the answer is assembled in two halves and the striker selection carries the second
        /// (see SimulationArmTargeting, and the ranged targets set at the foot of this method).
        ///
        /// THE SHOOTING is a RATE. As much shooting happens as the two sides' archers would naturally produce, and
        /// it is divided by the rate of fire -- five to one on the approach, two to one in the assault. Nothing
        /// about a wall stops a man shooting over it, so this half is a share of a fixed pool, exactly as before.
        ///
        /// THE FIGHTING is a CEILING. At most AttackWidth attacker melee blows and DefendWidth defender melee blows
        /// in a round, whatever the size of the armies -- see the note at the melee lines below for why this is an
        /// absolute limit and not, as it first was, a ratio.
        ///
        /// So the round's TOTAL is no longer preserved, and that is the intended consequence rather than a
        /// regression: a storm through a single gap contains less fighting than a field battle between the same
        /// armies, because most of the men are queueing. It does mean a siege resolves over more rounds than it
        /// used to, and each of those rounds still bills the campaign clock at simulationRoundMinutes -- so if
        /// sieges start taking implausible campaign TIME, the round clock is the thing to reprice, not this.
        ///
        /// On the approach there is no melee at all -- every blow is a shot, and the ceiling never binds.
        /// </summary>
        internal static void SplitTicks(SimulationBattleState.BattleState state, ref int defenderTicks, ref int attackerTicks)
        {
            if (state == null || !state.SiegeAssaultBattle)
            {
                return;
            }

            float baseDefender = defenderTicks;
            float baseAttacker = attackerTicks;
            float total = baseDefender + baseAttacker;
            if (total <= 0f)
            {
                return;
            }

            // Anything that is not the storm itself is dealt out as the approach -- including the one round a
            // repulsed assault is still nominally alive for. See IsApproach.
            bool approach = state.SiegePhase != PhaseAssault;

            // What share of the round's blows are SHOT, before any siege rule touches it: the two sides' archer
            // shares, weighted by how many blows each side was going to throw. On the approach this is forced to 1
            // -- nothing else happens out there. A side with no archers simply cannot meet its share of the quota;
            // the striker selection degrades to whoever is present and the damage model settles what that man is
            // worth, which on the approach is nothing (he is walking) and in the assault is an ordinary melee blow.
            float ranged = approach
                ? 1f
                : MBMath.ClampFloat(
                    ((state.DefenderRangedShare * baseDefender) + (state.AttackerRangedShare * baseAttacker)) / total,
                    0f, 1f);

            float fireRatio = FireRatio(state, approach);

            // The shooting, split by the rate of fire.
            float shots = ranged * total;
            float attackerShots = shots / (1f + fireRatio);
            float defenderShots = shots - attackerShots;

            // THE FIGHTING, AND THE FRONTAGE IS A HARD CEILING ON IT.
            //
            // Width was a RATIO first -- each side got a share of the round's melee in proportion to its frontage --
            // and eight logged sieges showed why that could not work. Native's besiegers build rams and towers and
            // nothing else, and both are symmetric (8/8, 4/4), so every siege opened at 12:12 or 16:16 and the two
            // widths step together by construction. A ratio of equals is 1:1 for ever: the frontage never once
            // touched an outcome. The ladder's 1/5 -- the whole reason widths differ at all -- was never built.
            //
            // So it is a CEILING now, not a share. At most AttackWidth attacker melee blows and DefendWidth defender
            // melee blows in a round, whatever the armies' size. That is what a breach IS: a gap that only so many
            // men can fight in at once, and a thousand men behind them do not widen it by standing there. Symmetric
            // frontages now bite just as hard as asymmetric ones, because what bites is the absolute number.
            //
            // Each side's own natural melee output is the other bound -- a side with few melee troops does not
            // suddenly field a full frontage of them -- so it is the smaller of what it HAS and what FITS.
            //
            // RANGED IS UNTOUCHED, deliberately: men shoot over the fight from the whole length of the wall and the
            // whole width of the field, and no gap in the masonry limits that.
            float attackerMelee = MathF.Min((1f - state.AttackerRangedShare) * baseAttacker,
                MathF.Max(0f, (float)state.AttackWidth));
            float defenderMelee = MathF.Min((1f - state.DefenderRangedShare) * baseDefender,
                MathF.Max(0f, (float)state.DefendWidth));

            float defenderTotal = defenderShots + defenderMelee;
            float attackerTotal = attackerShots + attackerMelee;

            // What share of each side's own blows must be shots, for the two ratios above to both come out right.
            // This is what the striker selection is told to aim at -- it is the other half of the answer, and
            // without it the tick counts alone would honour only one of the two.
            state.DefenderRangedTickTarget = (defenderTotal > 0f)
                ? MBMath.ClampFloat(defenderShots / defenderTotal, 0f, 1f)
                : 0f;
            state.AttackerRangedTickTarget = (attackerTotal > 0f)
                ? MBMath.ClampFloat(attackerShots / attackerTotal, 0f, 1f)
                : 0f;

            // Never let a side lose its turn entirely to rounding -- a side with no blows cannot fight, and a siege
            // whose attacker never acts would grind until the defenders' arrows alone decided it.
            defenderTicks = Math.Max(1, MathF.Round(defenderTotal));
            attackerTicks = Math.Max(1, MathF.Round(attackerTotal));
        }

        // =========================================================================================================
        // Width, and how it moves.
        //
        // Every man the attackers put down at the opening widens it by one for BOTH sides; every man the defenders
        // put down narrows it by one for both. Melee kills only -- an archer on the wall picking a man off the
        // ground below does not close a breach, and a besieger's arrow does not open one. What moves the width is
        // the press at the opening itself, which is the only thing width was ever a measure of.
        //
        // The verdict on a blow is not known when the blow is struck -- the game decides it in the next breath, in
        // ApplySimulationDamageToSelectedTroop -- so the blow is parked here and the verdict claims it. This is the
        // same two-step SimulationBattleState.LastHit makes for the log, and it is deliberately NOT that one: the
        // hit log is off by default, and the width must move whether or not anyone is writing the battle down.

        private static SimulationBattleState.BattleState _pendingState;
        private static bool _pendingStrikerIsAttacker;
        private static bool _pendingIsMelee;

        /// <summary>A blow has been struck. Park what the width needs to know about it, if it is the kind of blow
        /// width cares about at all.</summary>
        internal static void NoteBlow(SimulationBattleState.BattleState state, bool strikerIsAttacker, bool melee)
        {
            if (state != null && melee && IsAssault(state))
            {
                _pendingState = state;
                _pendingStrikerIsAttacker = strikerIsAttacker;
                _pendingIsMelee = true;
                return;
            }
            _pendingState = null;
            _pendingIsMelee = false;
        }

        /// <summary>The game has ruled on the parked blow. If it put its man down, the opening moves.</summary>
        internal static void NoteVerdict(bool downed)
        {
            SimulationBattleState.BattleState state = _pendingState;
            _pendingState = null;
            if (state == null || !_pendingIsMelee || !downed)
            {
                _pendingIsMelee = false;
                return;
            }
            _pendingIsMelee = false;

            int step = _pendingStrikerIsAttacker ? 1 : -1;

            // The floor is what the equipment bought at the start of the storm, and there is no ceiling. A defence
            // that is holding cannot squeeze the breach shut tighter than the hole in the wall actually is; an
            // assault that is winning spreads along the wall without limit, which is what a collapse looks like.
            state.AttackWidth = Math.Max(state.StartAttackWidth, state.AttackWidth + step);
            state.DefendWidth = Math.Max(state.StartDefendWidth, state.DefendWidth + step);
        }

        /// <summary>
        /// Drop the parked blow without answering it. The artillery needs this: an engine's casualty goes through
        /// the same casualty path a sword's does, so it reaches the same verdict hook -- and a stone that kills a
        /// man on the wall must not be allowed to claim a melee blow's parked record and widen the breach. Called
        /// either side of the engine volley (see SimulationSiegeEngines.Fire).
        /// </summary>
        internal static void ClearPendingBlow()
        {
            _pendingState = null;
            _pendingIsMelee = false;
        }

        /// <summary>A fresh session, or simply a battle that ended between the blow and the verdict: let go of the
        /// parked blow so no state from a torn-down campaign is held or acted on.</summary>
        internal static void ResetForNewSession()
        {
            _pendingState = null;
            _pendingIsMelee = false;
            _pendingStrikerIsAttacker = false;
        }
    }

    /// <summary>
    /// THE STORM THAT NEVER STARTED. When the approach ends with nothing left to climb or break, the assault cannot
    /// be fought -- and there is no point simulating rounds of a battle in which the attackers cannot reach anybody.
    /// The besiegers are repulsed, carrying whatever the crossing cost them.
    ///
    /// This ends the battle the same way vanilla's own morale rout does, and the same way SimulationRout does: the
    /// beaten side is put through native's <c>Route()</c> so its survivors leave as fugitives rather than corpses,
    /// and then the event's BattleState is set, which fires OnBattleWon and finalises everything downstream. Nothing
    /// here reimplements what the game already does; it only decides that the storm is over.
    ///
    /// Note this is the mirror image of SimulationRout's exclusion. That system refuses to touch sieges because a
    /// besieged DEFENDER cannot flee a wall (native's <c>MapEventSide.OnTroopRouted</c> declines to rout him). Here
    /// it is the ATTACKER who breaks off, and native is perfectly willing to rout him -- the guard is on the
    /// defending side of a siege, not on sieges as such.
    /// </summary>
    [HarmonyPatch(typeof(MapEvent), "SimulateBattleRound")]
    internal static class SimulationSiegeRepulse
    {
        private static readonly MethodInfo SetBattleState =
            typeof(MapEvent).GetProperty("BattleState")?.GetSetMethod(nonPublic: true);

        private static void Postfix(MapEvent __instance)
        {
            if (!SimulationEquipmentPower.SimulationEnabled || __instance == null || SetBattleState == null)
            {
                return;
            }
            if (__instance.BattleState != BattleState.None || !__instance.IsSiegeAssault)
            {
                return;
            }
            if (!SimulationSiege.Repulsed(SimulationBattleState.Get(__instance)))
            {
                return;
            }
            if (__instance.AttackerSide == null || __instance.DefenderSide == null)
            {
                return;
            }
            // If either side is already gone the game's own CalculateWinner has this battle; do not fight it for it.
            if (__instance.AttackerSide.NumRemainingSimulationTroops <= 0
                || __instance.DefenderSide.NumRemainingSimulationTroops <= 0)
            {
                return;
            }

            __instance.AttackerSide.Route();
            SetBattleState.Invoke(__instance, new object[] { BattleState.DefenderVictory });
        }
    }
}
