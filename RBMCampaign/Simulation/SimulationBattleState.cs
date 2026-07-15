using System;
using HarmonyLib;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// The tick allocation is called once, at the top of every simulated round, and it is the ONLY place the
    /// simulation ever says "a round has turned". A blow cannot say it -- a blow does not know how many came
    /// before it. So the battle's clock is read from here, and everything that is spent over a battle rather
    /// than in an instant -- arrows, shields, horses, the weight of a charge -- hangs off it.
    /// </summary>
    [HarmonyPatch(typeof(DefaultCombatSimulationModel), "GetSimulationTicksForBattleRound")]
    internal static class SimulationRoundCounter
    {
        /// <summary>
        /// How many more men get to act in a round than vanilla allows.
        ///
        /// Vanilla hands a side pow(men, 0.6) blows in a round -- about forty-five for an army of six hundred. That
        /// is a thin sample of a battle, and it was thin enough when a round was just "a bit of the brawl". It is far
        /// too thin now that a round is a PHASE with a job to do: a volley in which only the archers act, a skirmish
        /// in which only the javelins and the horse do. Too few men get their turn for a phase to say what it is for.
        ///
        /// Multiplying the allocation multiplies how many soldiers take part in every phase of every round. Note it
        /// does NOT change the casualties: the same men are being fought over, so the battle simply resolves in
        /// proportionally fewer rounds -- which in turn makes the volley and the skirmish a LARGER share of the whole
        /// fight, since their lengths are counted in rounds and did not change. That larger ranged share is the point:
        /// it is how the archers' edge -- the arm that actually decides a field battle -- gets to tell in the sim.
        ///
        /// Held at 3. It was 4 for a while -- pushed high to keep the widened lethality pool (a man soaks half again as
        /// many blows) from lengthening the fight and diluting the ranged phases back toward melee. Eased to 3: fewer
        /// men act each round, the fight runs a few more rounds, and the volley and skirmish give back a little of the
        /// share they had -- the approach was carrying too much of the battle. Still well above vanilla's thin sample.
        /// Raise it to compress the fight and grow the ranged phases; lower it to stretch the fight and grow the melee.
        /// </summary>
        internal const int TickMultiplier = 4;

        private static void Postfix(MapEvent mapEvent, ref ValueTuple<int, int> __result)
        {
            __result = new ValueTuple<int, int>(__result.Item1 * TickMultiplier, __result.Item2 * TickMultiplier);
            SimulationBattleState.AdvanceRound(mapEvent);
        }
    }

    /// <summary>
    /// What a battle has spent. Arrows are loosed and not got back, shields are hacked to kindling, horses are
    /// killed under their riders, and a charge is a thing that happens once. None of that can be said by a model
    /// that sees only one blow at a time and forgets it -- so the battle remembers here.
    ///
    /// What it CANNOT remember is men. The simulation hands us troop TYPES, never soldiers: a blow is struck by
    /// "an Imperial Archer", not by a man with eleven arrows left. So everything here is kept for a whole stack
    /// at once, and a "blow" stands in for what that stack did in one exchange. That is an abstraction, and it is
    /// the honest limit of what the game gives us to work with.
    /// </summary>
    /// <summary>
    /// A blow that was really struck, in the battle the game really fought.
    ///
    /// This is recorded from inside the SimulateHit postfix, which sees every blow of the real thing as it lands.
    /// It used to be recorded from the shadow REPLAY instead -- a reimplementation of vanilla's loop -- and that is
    /// precisely how a bug got in: the replay gave heroes a line trooper's single roll where vanilla accumulates
    /// their damage, so every lord in the log was dying to the first lance that touched him. A reimplementation can
    /// drift from what it reimplements, and when it does, the log lies about the battle with perfect confidence.
    ///
    /// The replay still earns its keep -- it is the only way to ask what the battle would have been WITHOUT the
    /// model, and a battle cannot be fought twice. But the blow-by-blow is the real battle now.
    /// </summary>
    internal class HitRecord
    {
        public int Round;

        public bool VolleyPhase;

        public bool SkirmishPhase;

        public bool StrikerIsAttacker;

        public CharacterObject Striker;

        public CharacterObject Struck;

        /// <summary>"shoot", "throw", "melee" -- or "-" when the model declined the blow entirely.</summary>
        public string Phase;

        /// <summary>
        /// What answered the blow: "none" (it landed), "shield-block", "weapon-block", "parry", or "riposte" for the
        /// counter itself. This is what lets the block, parry and riposte rates be read straight off the log, which
        /// is the whole point of the column -- the defense system is untunable without it.
        /// </summary>
        public string Defense;

        public string Weapon;

        /// <summary>Where it landed on him. A head is worth half again what a leg is; RBM says so, and now so do we.</summary>
        public string BodyPart;

        /// <summary>The armour standing over THAT part of him -- not an average of the whole man.</summary>
        public float ArmorMet;

        public float ShieldBlock;

        public bool Braced;

        public float ChargeBonus;

        public bool Closing;

        /// <summary>The man struck at was a horse archer with arrows left, and he simply rode away from it.</summary>
        public bool Evaded;

        /// <summary>What vanilla alone would have hit for, before the model had its say.</summary>
        public float VanillaDamage;

        public float Correction;

        public float FinalDamage;

        public int StruckHitPoints;

        /// <summary>
        /// What the man has LEFT after this blow -- every man, not just the lords. Read from the game's own books
        /// (a hero's from the Hero, a trooper's from the pool in SimulationTroopHitPoints), never reconstructed.
        /// </summary>
        public float HitPointsLeft;

        /// <summary>Filled in by the game itself, not guessed: whether the man actually went down.</summary>
        public bool Downed;

        public int AttackersLeft;

        public int DefendersLeft;
    }

    /// <summary>
    /// Whether the last blow put its man down is not decided by SimulateHit -- it is decided afterwards, in
    /// MapEventSide.ApplySimulationDamageToSelectedTroop, which rolls the damage against the man's hit points (or,
    /// for a hero, adds it to what he has already soaked). So the answer is taken from there, from the game's own
    /// verdict, rather than re-rolled by us. The two calls are strictly sequential, so the blow being answered is
    /// always the one just recorded.
    /// </summary>
    [HarmonyPatch(typeof(MapEventSide), "ApplySimulationDamageToSelectedTroop")]
    internal static class SimulationDownedMarker
    {
        private static void Postfix(bool __result)
        {
            HitRecord hit = SimulationBattleState.LastHit;
            if (hit == null)
            {
                return;
            }

            hit.Downed = __result;

            // And what the blow left of him. EVERY man has a pool now, so this is no longer a lord's privilege: a
            // hero's is read off the Hero, a trooper's off the pool that SimulationTroopHitPoints has just updated.
            // Either way it is the game's own arithmetic and not a reconstruction of it.
            hit.HitPointsLeft = (hit.Struck != null && hit.Struck.IsHero && hit.Struck.HeroObject != null)
                ? hit.Struck.HeroObject.HitPoints
                : SimulationTroopHitPoints.LastHitPointsLeft;

            SimulationBattleState.LastHit = null;
        }
    }

    internal static class SimulationBattleState
    {
        /// <summary>The blow just recorded, waiting to hear from the game whether it landed home. See SimulationDownedMarker.</summary>
        internal static HitRecord LastHit;

        // WHAT A STACK SPENDS IS SPENT PER MAN -- but WHICH CLOCK it is spent against depends on what it is.
        //
        // A simulated blow is one soldier striking one soldier (vanilla picks both by SelectRandomSimulationTroop),
        // and the number of blows a round contains is pow(menOnThatSide, 0.6). So the blows landing on any one
        // stack grow with the stack. Anything measured in blows and NOT multiplied by the stack's headcount is
        // therefore a budget that silently shrinks as the army grows -- which is why the shield and the horse,
        // which are worn down by BLOWS ARRIVING, are per-man capacities against a per-man rate of arrival, and come
        // out right at every scale.
        //
        // AMMUNITION IS NOT LIKE THAT, and denominating it in blows had it exactly backwards.
        //
        // A quiver does not empty per blow. It empties per MINUTE: a man looses arrows at a rate, and he keeps
        // loosing until the quiver is out or the enemy is on him. Blows per man per round go as N^-0.4, so counting
        // shots in blows meant twenty archers in a roadside skirmish burned their quivers dry before the fight was
        // decided, while eight hundred archers in the great set-piece battle of the war shot from a full quiver
        // from the first exchange to the last. That is precisely the wrong way round: the skirmish is over in a
        // minute and nobody empties anything, and the long battle is exactly where the arrows run out.
        //
        // So arrows and javelins are spent against the ROUND COUNTER, which is the only clock this simulation has.
        // A man shoots for so many rounds and then he is a man with a knife, and how many friends he brought has
        // nothing to do with it.

        /// <summary>
        /// Rounds of shooting in a full quiver. Thirty-odd arrows at a couple of shafts a round is a bit over a
        /// dozen rounds of steady loosing -- long enough to matter in a set-piece battle, longer than a skirmish
        /// lasts at all.
        ///
        /// Raised from 14 to 30 alongside the skill-based defense system: with landed melee lethality now
        /// skill-gated (most blows fully negated), a battle grinds on for more rounds before it resolves, and a
        /// quiver that ran dry in fourteen rounds would have the archers reduced to knives for most of a longer
        /// fight. This is a calibration target -- HasAmmo is Round <= AmmoRounds, so re-check the actual round
        /// count once the defense system is measured on a paired log (TickMultiplier=4 already shrank rounds).
        /// </summary>
        private const int AmmoRounds = 30;

        // A shield eats the blow it stops -- but the two numbers have to be in the same units, and they were not.
        //
        // A shield's hit points (400 to 2000) are denominated in the damage a REAL weapon does in a REAL battle,
        // where a man blocks dozens of blows and each is worth thirty to eighty. What this simulation feeds the
        // shield is `actual * shieldBlock` -- eight to twenty -- and a man receives, over an ENTIRE simulated
        // battle, between two and six blows. So a shield needed thirty to a hundred blows per man to break and was
        // offered maybe two. `ShieldIntegrity` returned 1.0 in every battle that has ever been fought, and reading
        // ShieldHitPoints off the items was pure ceremony.
        //
        // So the capacity is denominated in what actually arrives: simulated damage, per man, over a battle. The
        // item's own hit points still decide the spread -- a steel shield really does outlast an adarga five times
        // over -- but they are normalised against a reference shield rather than used raw.
        //
        // ShieldCapacityPerMan is a judgement, like the shield block chance itself: it is the simulated damage an
        // ordinary shield absorbs before it is kindling.
        //
        // CALIBRATION TARGET. Raised from 25 to 600 (~24x) for the skill-based defense system. Under the old
        // fractional skim a shield ate `actual * shieldBlock` -- a third to a half of every blow, eight to twenty
        // points. A discrete SHIELD BLOCK now dumps the WHOLE blow onto the board (30 to 110), but only on the
        // fraction of blows the man actually gets it in the way of. At ~25 points a whole-blow dump the old
        // capacity broke a shield in a single block; 600 lets it soak roughly fifteen to twenty full blocks before
        // it splinters, which is the order the field asks for. Too durable and shields never break and battles
        // never resolve; too brittle and shields are useless -- tune against total downs (~1448) on a paired log.
        private const float ShieldCapacityPerMan = 600f;

        private const float ReferenceShieldHitPoints = 800f;

        /// <summary>The floor, for a shield whose item data says nothing useful.</summary>
        private const float ShieldCapacityFloor = 200f;

        /// <summary>A horse is a big target and a footman is hacking at it. This is what one can take before it falls.</summary>
        private const float HorseCapacity = 260f;

        internal class TroopState
        {
            /// <summary>
            /// The men in this stack, counted across EVERY party on the side -- two lords who both brought Imperial
            /// Archers have, between them, one body of Imperial Archers, and it is that body which runs out of
            /// arrows. Never below one, so that nothing here can divide by zero.
            /// </summary>
            public int Count = 1;

            // Arrows and javelins are NOT counted here. They are spent against the battle's round counter, not
            // against the blows this stack happened to throw -- see the note at the top of the class. Counting them
            // per blow is what made a skirmish empty a quiver and a pitched battle never touch one.

            /// <summary>What his shield has eaten. This one IS worn down by blows arriving, so it is counted in them.</summary>
            public float ShieldDamage;

            /// <summary>And so is the horse under him.</summary>
            public float HorseDamage;
        }

        internal class BattleState
        {
            /// <summary>Rounds fought. The lines are still closing while this is under VolleyRounds.</summary>
            public int Round;

            public int VolleyRounds;

            /// <summary>
            /// Men on a wall do not shoot from a quiver -- they shoot from the town's arrow stores, stacked behind
            /// the parapet for exactly this, and a siege that has been prepared for has been prepared for. A
            /// besieger carries what he can climb with; a defender has been stockpiling since the banners appeared.
            /// </summary>
            public bool DefendersShootFromStores;

            /// <summary>
            /// How much room there is to ride in. This is what a horse archer's whole art depends on: he does not
            /// stand and trade blows, he keeps the distance and shoots, and a footman who wants to touch him has to
            /// catch him first. On the steppe he never will. In a wood, or in among the houses of a village, or on a
            /// siege ladder, there is nowhere to ride TO -- and a horse archer hemmed in is just a poorly armoured
            /// cavalryman holding a bow. See HorseArcherEvasion.
            /// </summary>
            public float KitingRoom = 1f;

            /// <summary>
            /// How often a mounted man's melee blow carries the weight of the horse behind it, once the lines meet.
            /// Kept SEPARATE from KitingRoom (which governs horse-archer evasion): the two used to ride one terrain
            /// reading, but a charge is a coarser thing than a kite -- a horse finds room to hit hard on any open
            /// field, wood or plain alike, while a horse archer's escape still shortens among the trees. So charge is
            /// a flat field/village/none, and kiting keeps its gradient.
            /// </summary>
            public float ChargeChance = 0f;

            /// <summary>
            /// Whether this is a battle nobody fights on horseback. A siege has no horses in it at all -- the wall is
            /// stormed on foot and defended on foot, and the game leaves every mount in the camp. A cavalryman here is
            /// a man in cavalry harness holding a lance, with no animal under him: he gets no charge, no barding at the
            /// leg, and no horse to be killed before he is. The kit is cached terrain-blind and still says he is
            /// mounted, so the fact that he cannot be has to be carried on the battle, not the man. See IsDismounted.
            /// </summary>
            public bool Dismounted;

            public readonly Dictionary<CharacterObject, TroopState> Attackers = new Dictionary<CharacterObject, TroopState>();

            public readonly Dictionary<CharacterObject, TroopState> Defenders = new Dictionary<CharacterObject, TroopState>();

            /// <summary>Every blow of this battle, as it was really struck. Empty unless the hit log is on.</summary>
            public readonly List<HitRecord> Trace = new List<HitRecord>();

            /// <summary>The muster roll: how many of each troop stand on each side, over all its parties.</summary>
            public Dictionary<CharacterObject, int> AttackerCounts = new Dictionary<CharacterObject, int>();

            public Dictionary<CharacterObject, int> DefenderCounts = new Dictionary<CharacterObject, int>();

            /// <summary>
            /// What share of each side can shoot. This is needed because vanilla hands a side pow(men, 0.6) blows a
            /// round and then picks who throws each one UNIFORMLY FROM THE WHOLE SIDE -- so an archer only gets a
            /// turn as often as archers are common. In the volley, when nobody but an archer does anything at all,
            /// four fifths of a typical army's blows are spent on men standing still. The archers are not shooting
            /// slowly; they are being denied their turn. See VolleyFocus.
            /// </summary>
            public float AttackerRangedShare = 1f;

            public float DefenderRangedShare = 1f;

            public TroopState For(CharacterObject troop, bool attacker)
            {
                Dictionary<CharacterObject, TroopState> side = attacker ? Attackers : Defenders;
                TroopState state;
                if (!side.TryGetValue(troop, out state))
                {
                    state = new TroopState();

                    // How many men this stack is, which is what everything it spends is measured against.
                    Dictionary<CharacterObject, int> roll = attacker ? AttackerCounts : DefenderCounts;
                    int count;
                    if (roll != null && roll.TryGetValue(troop, out count) && count > 0)
                    {
                        state.Count = count;
                    }
                    side[troop] = state;
                }
                return state;
            }
        }

        private static readonly Dictionary<MapEvent, BattleState> _battles = new Dictionary<MapEvent, BattleState>();

        internal static BattleState Get(MapEvent mapEvent)
        {
            if (mapEvent == null)
            {
                return null;
            }
            BattleState state;
            if (!_battles.TryGetValue(mapEvent, out state))
            {
                state = new BattleState();
                state.VolleyRounds = GetVolleyRounds(mapEvent);
                state.DefendersShootFromStores = mapEvent.IsSiegeAssault;
                state.KitingRoom = GetKitingRoom(mapEvent);
                state.ChargeChance = GetChargeChance(mapEvent);
                state.Dismounted = IsDismounted(mapEvent);
                state.AttackerCounts = Muster(mapEvent.AttackerSide);
                state.DefenderCounts = Muster(mapEvent.DefenderSide);
                state.AttackerRangedShare = RangedShare(state.AttackerCounts);
                state.DefenderRangedShare = RangedShare(state.DefenderCounts);
                _battles[mapEvent] = state;
            }
            return state;
        }

        /// <summary>
        /// The muster roll of a side: every troop on it, counted across every party standing there. A side is very
        /// often several parties, and two lords who each brought archers have, between them, one body of archers.
        /// </summary>
        private static Dictionary<CharacterObject, int> Muster(MapEventSide side)
        {
            Dictionary<CharacterObject, int> roll = new Dictionary<CharacterObject, int>();
            if (side == null)
            {
                return roll;
            }

            foreach (MapEventParty mapEventParty in side.Parties)
            {
                PartyBase party = mapEventParty.Party;
                if (party == null || party.MemberRoster == null)
                {
                    continue;
                }
                for (int i = 0; i < party.MemberRoster.Count; i++)
                {
                    TroopRosterElement element = party.MemberRoster.GetElementCopyAtIndex(i);
                    if (element.Character == null)
                    {
                        continue;
                    }
                    int healthy = element.Number - element.WoundedNumber;
                    if (healthy <= 0)
                    {
                        continue;
                    }
                    int running;
                    roll.TryGetValue(element.Character, out running);
                    roll[element.Character] = running + healthy;
                }
            }
            return roll;
        }

        /// <summary>
        /// The blows this battle was really decided by, handed over before the battle is forgotten. Null if nothing
        /// was recorded -- the hit log is off, or the battle never simulated a round because the player fought it.
        /// </summary>
        internal static List<HitRecord> TakeTrace(MapEvent mapEvent)
        {
            BattleState state;
            if (mapEvent == null || !_battles.TryGetValue(mapEvent, out state) || state.Trace.Count == 0)
            {
                return null;
            }
            return state.Trace;
        }

        /// <summary>A battle is over; let it go, or the campaign will carry every fight it ever fought.</summary>
        internal static void Forget(MapEvent mapEvent)
        {
            if (mapEvent != null)
            {
                _battles.Remove(mapEvent);
                SimulationTroopHitPoints.Forget(mapEvent);
            }
        }

        /// <summary>
        /// Called once per round, from the tick allocation. That is the only place the simulation tells us a round
        /// has turned -- a blow cannot, since it does not know how many came before it.
        /// </summary>
        internal static void AdvanceRound(MapEvent mapEvent)
        {
            BattleState state = Get(mapEvent);
            if (state == null)
            {
                return;
            }

            state.Round++;

            // The first round is the first moment the battle can be seen WHOLE. MapEventStarted fires before a
            // lord's allies and the rest of his army have attached themselves to the event, so anything counted
            // there counts one party and misses the others. Here everyone who is coming has come -- and not a blow
            // has landed yet, so the rosters are still the rosters they marched in with.
            if (state.Round == 1)
            {
                state.AttackerCounts = Muster(mapEvent.AttackerSide);
                state.DefenderCounts = Muster(mapEvent.DefenderSide);
                state.AttackerRangedShare = RangedShare(state.AttackerCounts);
                state.DefenderRangedShare = RangedShare(state.DefenderCounts);

                // A lord's armour and training are fixed for the length of a battle but not between battles, so his
                // kit is re-measured here, once, rather than on every blow he throws.
                SimulationEquipmentPower.ForgetHeroKits();

                SimulationBattleSnapshot.Recapture(mapEvent);
            }
        }

        /// <summary>
        /// How long the bowmen have before the lines meet -- which is a question about the ground. Across an open
        /// plain a man walks a long way under arrows; in a wood he is on you before the second shaft is nocked.
        /// Storming a wall is the longest walk of all, and everyone on it is shooting at you the whole way.
        /// </summary>
        private static int GetVolleyRounds(MapEvent mapEvent)
        {
            return GetVolleyRounds(mapEvent.SimulationContext, mapEvent.IsSiegeAssault);
        }

        private static int GetVolleyRounds(MapEvent.PowerCalculationContext context, bool isSiegeAssault)
        {
            // A siege is the longest approach there is, whether you are storming the wall or grinding at it. Every
            // man on the parapet is shooting at you the whole way, and there is nowhere to go but forward.
            if (isSiegeAssault)
            {
                return 12;
            }

            switch (context)
            {
                case MapEvent.PowerCalculationContext.Siege:
                    return 12;

                // A village is not an approach at all. There is no ground to cross -- the fighting starts in among
                // the houses, at arm's length, almost the moment anyone arrives.
                case MapEvent.PowerCalculationContext.Village:
                    return 2;

                case MapEvent.PowerCalculationContext.NavalRaid:
                    return 4;

                // Ships closing on one another: a short thing, and then it is boarding and butchery.
                case MapEvent.PowerCalculationContext.SeaBattle:
                case MapEvent.PowerCalculationContext.OpenSeaBattle:
                case MapEvent.PowerCalculationContext.RiverCrossingBattle:
                    return 4;

                // Trees and water. There is little ground to cross and less of it open -- the lines are on each other
                // almost at once, so the bows get only a few rounds before it is hand to hand. This matches the
                // shortened charge and kiting a forest already carries (see GetChargeChance, GetKitingRoom).
                case MapEvent.PowerCalculationContext.ForestBattle:
                case MapEvent.PowerCalculationContext.RiverBattle:
                    return 3;

                default:
                    // Plain, steppe, desert, dune, snow: open ground to cross, under arrows the whole way.
                    return 6;
            }
        }

        /// <summary>
        /// How much ground a horseman has to ride in, which is the whole question a horse archer's life turns on.
        ///
        /// It is the same question the volley asks -- how much field is there between the lines -- and it is answered
        /// off the same terrain, because it is the same terrain. On the steppe there is nothing BUT room, and a
        /// footman chasing a Khuzait on a pony will be chasing him at dusk. In a forest the room is gone: the horse
        /// cannot get up to speed, the trees close the lanes, and a man on foot with an axe gets his chance. In a
        /// village he is riding between houses, and on a wall he is not riding at all.
        /// </summary>
        private static float GetKitingRoom(MapEvent mapEvent)
        {
            // Storming a wall. Nobody kites up a ladder.
            if (mapEvent.IsSiegeAssault)
            {
                return 0f;
            }

            switch (mapEvent.SimulationContext)
            {
                // A wall, a street, a deck. There is nowhere to ride to, and a horse archer caught in any of them is
                // simply a lightly armoured man who is easier to reach than he would like to be.
                case MapEvent.PowerCalculationContext.Siege:
                case MapEvent.PowerCalculationContext.Village:
                case MapEvent.PowerCalculationContext.SeaBattle:
                case MapEvent.PowerCalculationContext.OpenSeaBattle:
                case MapEvent.PowerCalculationContext.NavalRaid:
                    return 0f;

                // Trees and water. There is ground here, but it is broken ground -- the lanes are short, the horse
                // cannot run, and the footman gets far closer than he ever would on the plain.
                case MapEvent.PowerCalculationContext.ForestBattle:
                case MapEvent.PowerCalculationContext.RiverBattle:
                case MapEvent.PowerCalculationContext.RiverCrossingBattle:
                    return 0.4f;

                // Plain, steppe, desert, dune, snow. Open country, and open country belongs to the horse -- but not
                // quite absolutely. Even on the steppe there are hollows and broken ground and horses that stumble,
                // and a man on foot occasionally gets his chance. It is a tenth of one.
                default:
                    return 0.9f;
            }
        }

        /// <summary>How often a mounted melee blow is a charge, by terrain -- separate from KitingRoom (see the note on
        /// BattleState.ChargeChance). A charge only wants room to hit hard, which any open field gives it, wood or
        /// plain; a village street gives it a little; a wall, a deck and a besieged gate give it none.</summary>
        private const float FieldChargeChance = 0.5f;   // open field -- plain, steppe, desert
        private const float ForestChargeChance = 0.4f;  // trees and water -- the horse charges a little less often
        private const float VillageChargeChance = 0.15f; // riding between houses, room for the odd charge
        // Every charge chance above is scaled this much across the board -- but NOT the naval and siege zeroes, which
        // stay nothing (a wall and a deck have no charge to scale). Was 1.2 (a 20% lift); pulled to 0.9 -- with the
        // charge landing unblocked and hitting harder now, the horse was charging too often and running whole battles.
        private const float ChargeChanceBoost = 0.9f;

        private static float GetChargeChance(MapEvent mapEvent)
        {
            // Storming a wall: no room to ride, so no charge -- and nothing to boost.
            if (mapEvent.IsSiegeAssault)
            {
                return 0f;
            }

            switch (mapEvent.SimulationContext)
            {
                // A besieged gate, a deck, the open sea -- nowhere to bring a horse up to speed, and often no horse at
                // all (see IsDismounted). No charge, and left out of the across-the-board boost.
                case MapEvent.PowerCalculationContext.Siege:
                case MapEvent.PowerCalculationContext.SeaBattle:
                case MapEvent.PowerCalculationContext.OpenSeaBattle:
                case MapEvent.PowerCalculationContext.NavalRaid:
                    return 0f;

                // Streets and houses: a horse still charges when a lane opens, but rarely.
                case MapEvent.PowerCalculationContext.Village:
                    return VillageChargeChance * ChargeChanceBoost;

                // Trees and water: the lanes are shorter and the horse gets up to the charge a little less often than
                // on the open plain -- but it is still a field, and still mostly charges. (The trees shorten a horse
                // archer's kite further; that finer measure is KitingRoom's business, not this.)
                case MapEvent.PowerCalculationContext.ForestBattle:
                case MapEvent.PowerCalculationContext.RiverBattle:
                case MapEvent.PowerCalculationContext.RiverCrossingBattle:
                    return ForestChargeChance * ChargeChanceBoost;

                // Open field -- plain, steppe, desert, dune, snow. All the room a charge wants.
                default:
                    return FieldChargeChance * ChargeChanceBoost;
            }
        }

        /// <summary>
        /// A battle nobody fights mounted. A siege is stormed and held on foot -- the game brings no horses to a wall
        /// at all -- and a ship is no place for one either: a boarding action is fought on foot across the decks. So a
        /// cavalryman in either is a lance and a suit of horse harness with no animal under it. This is a stronger
        /// thing than kiting room going to nothing: a horse hemmed into a village street is still a horse, and still
        /// charges when it finds room and dies before its rider does. A horse that is not there does none of it. Kept
        /// apart from GetKitingRoom for exactly that reason -- a wall, a deck and a village street all read zero room,
        /// but the village keeps its horses and the other two have none, and those are not the same zero.
        /// </summary>
        private static bool IsDismounted(MapEvent mapEvent)
        {
            if (mapEvent.IsSiegeAssault)
            {
                return true;
            }

            switch (mapEvent.SimulationContext)
            {
                case MapEvent.PowerCalculationContext.Siege:
                case MapEvent.PowerCalculationContext.SeaBattle:
                case MapEvent.PowerCalculationContext.OpenSeaBattle:
                case MapEvent.PowerCalculationContext.NavalRaid:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// How long the field belongs to the defender's bows alone. He is standing on his ground with his enemy in
        /// the open and the whole distance to shoot across; the attacker is still coming, too far out to answer,
        /// and eats it. That is what it means to advance on a prepared position.
        /// </summary>
        internal const int DefenderOnlyRounds = 2;

        // A BATTLE HAS THREE ACTS, and only the last of them is what auto-resolve ever modelled.
        //
        //   1. THE VOLLEY. The lines are far apart. The bowmen shoot and the foot walk into it, and in the first
        //      round only the defender may loose -- the attacker is still too far out to answer.
        //
        //   2. THE SKIRMISH. The ground between the lines. The javelins come off their backs and are hurled here,
        //      not during the long approach: a man does not throw a spear at somebody a bowshot away. And this is
        //      where the HORSE meet the HORSE -- each side's cavalry ride out at each other in the open before the
        //      foot are anywhere near, which is what cavalry have always done and what auto-resolve has never let
        //      them do. Everyone else is still closing.
        //
        //   3. THE LINES MEET. Everything after that is the battle as auto-resolve has always imagined it: one long
        //      brawl. It is only ever been the third act, and it is the least interesting of the three.

        /// <summary>
        /// How long the horse have the field to themselves, and the javelins are in the air.
        ///
        /// It also decides how many javelins a man ever gets to throw -- he hurls one a round, so a bundle of three
        /// needs three rounds of skirmish to be spent, and a shorter phase would leave spears on his back that he
        /// paid for and carried.
        /// </summary>
        private const int SkirmishRounds = 3;

        internal static bool IsVolleyPhase(BattleState state)
        {
            // The round counter is advanced when the round's blows are handed out, so it is 1 during the first
            // round's fighting -- hence <=, not <.
            return state != null && state.Round <= state.VolleyRounds;
        }

        /// <summary>The horse are out, the javelins are in the air, and the foot are still walking.</summary>
        internal static bool IsSkirmishPhase(BattleState state)
        {
            return state != null
                && state.Round > state.VolleyRounds
                && state.Round <= (state.VolleyRounds + SkirmishRounds);
        }

        /// <summary>The round the foot finally reach each other, and the battle becomes what auto-resolve thinks it is.</summary>
        internal static int ContactRound(BattleState state)
        {
            return (state != null) ? (state.VolleyRounds + SkirmishRounds + 1) : 1;
        }

        /// <summary>
        /// What an archer's shot must be multiplied by during the volley, to undo the fact that his own infantry are
        /// eating his turns.
        ///
        /// Vanilla gives a side pow(men, 0.6) blows in a round and chooses the man who throws each one uniformly
        /// from the WHOLE side. In the volley nobody but an archer does anything -- so if a fifth of the army
        /// shoots, four fifths of every round's blows are spent on men who are, by construction, standing there
        /// doing nothing. The archers are not shooting slowly. They are being skipped.
        ///
        /// The obvious fix -- hand the archers every tick, a factor of 1/share -- is WRONG, and badly. It makes the
        /// side's arrow output the same whether it brought one archer or a thousand: one bowman in a hundred would
        /// loose as many shafts as a hundred bowmen. The volley would stop depending on how many archers you own,
        /// which is the one thing it must depend on.
        ///
        /// What the archers SHOULD get is the tick allocation they would have had if the volley were a battle
        /// between the archers alone: pow(share * men, 0.6) rather than share * pow(men, 0.6). Divide the one by
        /// the other and the men fall out entirely, leaving share^0.6 / share, which is share^-0.4:
        ///
        ///     half the army shoots  -> x1.32
        ///     a fifth              -> x1.90
        ///     a twentieth          -> x3.31
        ///
        /// More archers still means more shooting -- just sublinearly, exactly as vanilla scales everything else.
        /// Capped, because a side with three archers in a thousand is a rounding error and should not have them
        /// firing like a legion.
        /// </summary>
        internal static float VolleyFocus(BattleState state, bool attacker)
        {
            if (state == null)
            {
                return 1f;
            }
            float share = attacker ? state.AttackerRangedShare : state.DefenderRangedShare;
            if (share <= 0f || share >= 1f)
            {
                return 1f;
            }
            return MBMath.ClampFloat(MathF.Pow(share, -0.4f), 1f, 4f);
        }

        /// <summary>The share of a muster roll that can shoot.</summary>
        internal static float RangedShare(Dictionary<CharacterObject, int> roll)
        {
            if (roll == null || roll.Count == 0)
            {
                return 1f;
            }
            int total = 0;
            int ranged = 0;
            foreach (KeyValuePair<CharacterObject, int> entry in roll)
            {
                total += entry.Value;
                if (entry.Key.IsRanged)
                {
                    ranged += entry.Value;
                }
            }
            return (total > 0) ? (ranged / (float)total) : 1f;
        }

        /// <summary>
        /// Whether this stack still has anything to shoot. A defender on a wall always does: he is not shooting
        /// from his quiver, he is shooting from the stores, and the stores were filled before the siege began.
        /// </summary>
        internal static bool HasAmmo(BattleState battle, TroopState state, bool attacker)
        {
            // A man on a wall shoots from the town's stores, and the stores were filled before the siege began.
            if (battle != null && battle.DefendersShootFromStores && !attacker)
            {
                return true;
            }

            // No battle to read the clock from (the log's reference tables ask what a blow WOULD do, outside any
            // battle at all) -- so assume a full quiver and let the question be about the kit, not the moment.
            if (battle == null)
            {
                return true;
            }

            return battle.Round <= AmmoRounds;
        }

        /// <summary>
        /// Whether this stack has a javelin left to throw. He hurls one in the press of each round, so the bundle
        /// on his back IS the number of rounds he can throw for -- two javelins, two rounds, and then he is out.
        ///
        /// Note what that does to the volley: an approach across open ground runs four rounds, and he has two or
        /// three javelins. So he hurls them in the opening rounds, does terrible damage, and then spends the rest
        /// of the walk paying the closing penalty like everyone else, with nothing in his hand and the enemy line
        /// still coming. That is exactly what being a skirmisher is.
        ///
        /// There is no store to fall back on and no siege exception, because nobody stockpiles javelins behind a
        /// parapet; a javelin is a thing you carry and lose.
        /// </summary>
        internal static bool HasJavelins(BattleState battle, TroopState state, float thrownPerMan)
        {
            if (battle == null)
            {
                return true;
            }

            // He hurls one per round of the skirmish, so the bundle on his back is how many rounds of it he is
            // dangerous for. Both sides begin the skirmish together, so no side needs a head start or a handicap --
            // the old first-round shift for attackers is gone with the reason for it, since nobody throws anything
            // during the volley any more.
            int roundsIntoSkirmish = battle.Round - battle.VolleyRounds;
            return roundsIntoSkirmish >= 1 && roundsIntoSkirmish <= (int)MathF.Ceiling(thrownPerMan);
        }

        /// <summary>
        /// What is left of this stack's shields. A shield stops a blow by taking it, and a wooden board that has
        /// eaten thirty mace-blows is kindling. Its hit points are the item's own -- which is why a steel shield
        /// at two thousand outlasts an adarga at four hundred by five times over, exactly as it should -- and there
        /// are as many of them as there are men holding them.
        /// </summary>
        internal static float ShieldIntegrity(TroopState state, float shieldHitPoints)
        {
            float quality = MathF.Max(shieldHitPoints, ShieldCapacityFloor) / ReferenceShieldHitPoints;
            float capacity = ShieldCapacityPerMan * quality * state.Count;
            if (capacity <= 0f)
            {
                return 1f;
            }
            return MBMath.ClampFloat(1f - (state.ShieldDamage / capacity), 0f, 1f);
        }

        internal static void DamageShield(TroopState state, float amount)
        {
            state.ShieldDamage += MathF.Max(0f, amount);
        }

        /// <summary>
        /// What is left of this stack's horses. A footman hacking upward at a rider is mostly hacking at the horse,
        /// and horses die. When one does, its rider is a man on foot in cavalry harness: he loses the barding that
        /// was answering those blows, and the blows start finding his head instead of his legs.
        /// </summary>
        internal static float HorsesAlive(TroopState state)
        {
            return MBMath.ClampFloat(1f - (state.HorseDamage / (HorseCapacity * state.Count)), 0f, 1f);
        }

        internal static void DamageHorse(TroopState state, float amount)
        {
            state.HorseDamage += MathF.Max(0f, amount);
        }
    }
}
