using System;
using HarmonyLib;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Siege;
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
        /// Held at 4 -- pushed high to keep the widened lethality pool (a man soaks half again as many blows) from
        /// lengthening the fight and diluting the ranged phases back toward melee. Well above vanilla's thin sample.
        /// Flat: every battle is allocated at this multiplier, whatever its size. A ramp that thinned it for a small
        /// fight was once described here and never built -- what a small fight actually needed was a shorter VOLLEY,
        /// not a thinner round, and that is where it went instead (see GetVolleyRounds, which scales the approach by
        /// the battle's size against VolleyBattleSaturation). Raise this to compress a fight and grow the ranged
        /// phases; lower it to stretch the fight and grow the melee.
        /// </summary>
        internal const int TickMultiplier = 4;


        private static void Postfix(MapEvent mapEvent, ref ValueTuple<int, int> __result)
        {
            // With the equipment model off the whole overhaul stands down (see SimulationEquipmentPower.
            // SimulationEnabled): leave vanilla's tick allocation untouched and do not advance our round clock.
            if (!SimulationEquipmentPower.SimulationEnabled)
            {
                return;
            }

            float mult = TickMultiplier;
            int defenderTicks = Math.Max(1, MathF.Round(__result.Item1 * mult));
            int attackerTicks = Math.Max(1, MathF.Round(__result.Item2 * mult));

            // The pace of THIS round -- how much of the fight it carries, and the unit the phase boundaries are
            // counted in. Nothing varies the multiplier per battle, so this is always exactly 1 and Progress tracks
            // the raw round. It is written as a ratio, and passed rather than assumed, so that a model which DOES
            // vary the allocation has one seam to vary it at. See BattleState.Progress.
            SimulationBattleState.AdvanceRound(mapEvent, mult / TickMultiplier);

            // A WALL ASSAULT DIVIDES ITS ROUND DIFFERENTLY. The defender shoots five times as often while the ground
            // is being crossed and twice as often once the ladders are up, and the melee is split by the width of
            // the openings rather than evenly. The TOTAL is left exactly as it was -- this redistributes a round, it
            // does not inflate one -- so nothing about a siege's lethality moves for a reason that is not the wall.
            //
            // AFTER AdvanceRound, and that ordering is load-bearing: AdvanceRound is what turns the approach into
            // the assault, and the blows allocated here are the blows of the round that turn belongs to. Split first
            // and every phase change would be honoured a full round late -- the round the ladders go up would still
            // be diced out as though the men were in the open.
            SimulationBattleState.BattleState state = SimulationBattleState.Get(mapEvent);
            SimulationSiege.SplitTicks(state, ref defenderTicks, ref attackerTicks);

            __result = new ValueTuple<int, int>(defenderTicks, attackerTicks);

            // AND THE ARTILLERY FIRES. Engines are not troops and do not take turns in the blow-by-blow, so they
            // are given their own volley, once, at the top of the round -- ahead of the round's fighting rather
            // than after it, which is safe because SimulateBattleRound opens by asking who has won (a side the
            // engines have just finished off ends the battle cleanly instead of being asked for a man it no longer
            // has). See SimulationSiegeEngines.
            SimulationSiegeEngines.Fire(mapEvent, state);
        }
    }

    /// <summary>
    /// What a round COSTS the campaign clock -- the other half of the round's definition, and the half vanilla
    /// still owned.
    ///
    /// SimulationRoundCounter above decides how much fighting a round contains. This decides how long that fighting
    /// takes. The two must agree, and for a long time they did not: the counter was rewritten (a round became a
    /// phase, a thin slice of a battle) while the clock kept charging vanilla's price for it, a flat half hour --
    /// the price of a round that resolved a large share of the fight. Nothing in the model was wrong; the battle
    /// simply took more rounds than vanilla's (a blow that misses, is blocked, or kills a horse rather than a man
    /// costs a round and yields no casualty), and every one of them billed half an hour. Fights of twenty a side
    /// ran to a day and a half of campaign time.
    ///
    /// So the clock is priced here, beside the counter that gives the round its meaning, and the two stay together.
    /// This touches the CLOCK ONLY: not a blow, not a casualty, not a phase boundary (those are counted in rounds
    /// and Progress, never in minutes), so the battle that is fought is byte-for-byte the battle that was fought
    /// before -- it merely takes the hours it should.
    /// </summary>
    [HarmonyPatch(typeof(DefaultCombatSimulationModel), "GetSimulationTickInterval")]
    internal static class SimulationRoundClock
    {
        /// <summary>
        /// Vanilla's price for a field round, and the number the dial is expressed against. Scaling vanilla's own
        /// answer rather than replacing it is deliberate: a siege assault costs vanilla DOUBLE a field round, and
        /// that ratio is a fact about sieges rather than about this model, so it rides through untouched and the
        /// dial does not have to know a siege from a field at all.
        /// </summary>
        private const float VanillaFieldRoundMinutes = 30f;

        private static void Postfix(MapEvent mapEvent, ref CampaignTime __result)
        {
            // With the equipment model off the whole overhaul stands down (see SimulationEquipmentPower.
            // SimulationEnabled): vanilla's round is back, so vanilla's price for it is the right one.
            if (!SimulationEquipmentPower.SimulationEnabled)
            {
                return;
            }

            float minutes = RBMConfig.RBMConfig.simulationRoundMinutes;
            if (minutes <= 0f)
            {
                return;
            }

            // A round can never be free: at zero the map event would simulate every campaign tick without the clock
            // ever advancing past it, so the floor is one minute.
            long scaled = (long)MathF.Round((float)__result.ToMinutes * (minutes / VanillaFieldRoundMinutes));
            __result = CampaignTime.Minutes(Math.Max(1L, scaled));
        }
    }

    /// <summary>
    /// The two patches above hang the whole round clock off <see cref="DefaultCombatSimulationModel"/>. That is the
    /// right seam for every land battle -- but not for a battle on the water. The War Sails DLC does not extend the
    /// default model; it REPLACES it, wrapping the original in a decorator (NavalDLC's own
    /// <c>NavalDLCCombatSimulationModel : CombatSimulationModel</c>, held as its BaseModel). That decorator resolves
    /// the tick allocation itself for anything on the water -- a sea battle, or a raid whose attacker is still
    /// aboard ship -- and RETURNS before ever calling the base method. So the postfix above never fires for those
    /// fights, and <see cref="SimulationBattleState.AdvanceRound"/> is never called: the round clock is frozen at
    /// zero for the length of the battle. Its SimulateHit override, by contrast, DOES delegate to the base model,
    /// so RBM's blow recorder runs on every naval blow -- and writes every one of them into round zero.
    ///
    /// The result was a naval fight logged as one endless opening VOLLEY: the phase never advances off the ground it
    /// starts on (see IsVolleyPhase, which reads Progress), the lines never meet, and the melee is zeroed the whole
    /// time as "still closing" -- so a coastal raid resolved as a one-sided archery duel that wiped the landing
    /// party for nothing. This patch gives the clock its second driver: the same tick multiply and the same round
    /// advance, on the decorator's own method, for exactly the branches the decorator handles itself.
    ///
    /// Reflected onto the DLC type by name so RBM keeps no build- or load-time dependency on an optional module:
    /// with War Sails absent the type does not resolve, <see cref="Prepare"/> returns false, and the patch is never
    /// applied. It is harmless when the DLC is present but the default model is in use, since Harmony only patches
    /// the method that actually exists.
    /// </summary>
    [HarmonyPatch]
    internal static class NavalSimulationRoundCounter
    {
        internal const string NavalModelTypeName = "NavalDLC.GameComponents.NavalDLCCombatSimulationModel";

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(NavalModelTypeName + ":GetSimulationTicksForBattleRound");
        }

        private static void Postfix(MapEvent mapEvent, ref ValueTuple<int, int> __result)
        {
            if (!SimulationEquipmentPower.SimulationEnabled)
            {
                return;
            }

            // Only advance when the DECORATOR resolved these ticks itself. For every other fight it delegated to the
            // base DefaultCombatSimulationModel, whose own postfix (SimulationRoundCounter, above) has already
            // multiplied the allocation and turned the round -- advancing again here would count that round twice
            // and race the phases through at double speed.
            if (!NavalModelSelfHandled(mapEvent))
            {
                return;
            }

            // The same widening the land path applies -- see SimulationRoundCounter.TickMultiplier for why a round
            // is sampled this much thicker than vanilla -- so a naval round carries the same weight as a land one and
            // the phases, counted in rounds, keep their intended share of the fight.
            float mult = SimulationRoundCounter.TickMultiplier;
            __result = new ValueTuple<int, int>(
                Math.Max(1, MathF.Round(__result.Item1 * mult)),
                Math.Max(1, MathF.Round(__result.Item2 * mult)));
            SimulationBattleState.AdvanceRound(mapEvent, 1f);
        }

        /// <summary>
        /// True when NavalDLC's model computes the tick allocation itself rather than handing it to the base model --
        /// and so the only case in which the base postfix did NOT run and this one must stand in. It mirrors the
        /// decorator's own branch structure exactly: a fight on the water (<see cref="MapEvent.IsNavalMapEvent"/>,
        /// which is simply "not on land"), or a raid whose attacker is still aboard ship -- a coastal settlement
        /// struck from the sea, whose map position is on land so it is NOT a naval map event, but whose landing
        /// party is resolved by the naval model all the same. Everything else falls through to the base model there,
        /// and must fall through here.
        /// </summary>
        private static bool NavalModelSelfHandled(MapEvent mapEvent)
        {
            if (mapEvent == null)
            {
                return false;
            }
            if (mapEvent.IsNavalMapEvent)
            {
                return true;
            }
            if (mapEvent.IsRaid)
            {
                MobileParty attacker = (mapEvent.AttackerSide != null && mapEvent.AttackerSide.LeaderParty != null)
                    ? mapEvent.AttackerSide.LeaderParty.MobileParty : null;
                return attacker != null && attacker.IsCurrentlyAtSea;
            }
            return false;
        }
    }

    /// <summary>
    /// The clock's price, for the naval model, for the same reason its count needs one (see
    /// <see cref="NavalSimulationRoundCounter"/>). NavalDLC's decorator hands a sea battle a flat hour a round and
    /// returns before the base method, so <see cref="SimulationRoundClock"/> above never reprices it -- and a naval
    /// fight, which now turns as many rounds as any other, would bill every one of them at vanilla's full price and
    /// run to days of campaign time. Only the fights the decorator prices itself are repriced here (IsNavalMapEvent
    /// -- the same and only branch its GetSimulationTickInterval self-handles); a sea-launched raid on a land
    /// settlement is priced by the base method and so is already caught above. Reflected on by name, absent-DLC-safe,
    /// exactly as the counter is.
    /// </summary>
    [HarmonyPatch]
    internal static class NavalSimulationRoundClock
    {
        private const float VanillaNavalRoundMinutes = 60f;

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(NavalSimulationRoundCounter.NavalModelTypeName + ":GetSimulationTickInterval");
        }

        private static void Postfix(MapEvent mapEvent, ref CampaignTime __result)
        {
            if (!SimulationEquipmentPower.SimulationEnabled || mapEvent == null || !mapEvent.IsNavalMapEvent)
            {
                return;
            }

            float minutes = RBMConfig.RBMConfig.simulationRoundMinutes;
            if (minutes <= 0f)
            {
                return;
            }

            long scaled = (long)MathF.Round((float)__result.ToMinutes * (minutes / VanillaNavalRoundMinutes));
            __result = CampaignTime.Minutes(Math.Max(1L, scaled));
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

        /// <summary>A wall assault, and which of its two acts this blow fell in -- the approach across the killing
        /// ground, or the storm itself. Null for every battle that is not one. See SimulationSiege.</summary>
        public string SiegePhase;

        /// <summary>The frontage at the openings when this blow was struck, attacker's and defender's. The whole
        /// siege turns on the ratio between them, and it moves with every melee kill, so the log has to carry it or
        /// the assault cannot be read back. Zero outside a wall assault's storm.</summary>
        public int SiegeAttackWidth;

        public int SiegeDefendWidth;

        /// <summary>How good the wall was -- the multiplier on the whole defending advantage. Printed with the
        /// approach, because two sieges with identical rosters and different fortifications should be readable
        /// apart in the log without going and looking the settlement up.</summary>
        public float SiegeWallFactor = 1f;

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
    /// A SHOT FROM AN ENGINE, which is not a blow and cannot be written down as one.
    ///
    /// Everything in <see cref="HitRecord"/> assumes a man struck a man: it has a striker, a weapon he drew, the
    /// part of the body it found and the armour standing over it. An engine has none of that. But a siege in which
    /// the artillery does a third of the killing and the log shows only the sword-work is a log that lies by
    /// omission -- the casualties would appear as a step in the headcount that nothing on the page accounts for.
    /// So the engines keep their own book, and it is printed beside the blows.
    /// </summary>
    internal class ArtilleryRecord
    {
        public int Round;

        /// <summary>Which side served the engine.</summary>
        public bool FiredByAttacker;

        /// <summary>The engine's own name -- "catapult", "fire_onager", "trebuchet".</summary>
        public string Engine;

        /// <summary>What it was shooting at: "men" or "engine".</summary>
        public string Target;

        /// <summary>Whether the shot arrived at all. A shot that went wide still took a round and still goes in the
        /// book, for exactly the reason a missed arrow does: the hit RATE is the only thing an accuracy figure can
        /// be calibrated against.</summary>
        public bool Hit;

        /// <summary>Men put down outright by this shot.</summary>
        public int Killed;

        /// <summary>Men wounded but still standing -- the pot's burns. Kept apart from the kills because they are
        /// a different weapon behaving differently, and because their toll arrives rounds later.</summary>
        public int Wounded;

        /// <summary>What it hit, when it was shooting at equipment, and what that cost the machine.</summary>
        public string TargetEngine;

        public float EngineDamage;

        /// <summary>Whether that shot finished the machine. This is the single most consequential thing artillery
        /// does in a siege -- an engine broken here narrows the assault that follows -- so it gets its own flag.</summary>
        public bool Destroyed;
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
            // THE WIDTH HEARS THE VERDICT FIRST, and it must hear it whether or not anybody is writing the battle
            // down. Every melee kill at a siege opening moves the frontage -- the attackers' by widening it, the
            // defenders' by closing it -- and that is a fact about the fight, not about the log. The HitRecord
            // below exists ONLY when the hit log is switched on, so the width cannot be hung off it; SimulationSiege
            // parks its own blow and claims it here. Ahead of the null check for exactly that reason.
            SimulationSiege.NoteVerdict(__result);

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
        /// fight. This is a calibration target -- HasAmmo is Progress &lt;= AmmoRounds (Progress, so the quiver holds
        /// a constant amount of FIGHTING however the battle is paced; equal to the round count at full size), so
        /// re-check it once the defense system is measured on a paired log (TickMultiplier=4 already shrank rounds).
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

        /// <summary>
        /// The fallback a horse can take before it falls, for the rare mount whose own health did not come through
        /// (a troop the kit could read no Horse item off). Every real animal brings its OWN health now -- the game's
        /// Monster hit points plus its extra_health, carried on TroopKit.HorseHealth -- so a destrier outlasts a
        /// courier's palfrey. See HorsesAlive.
        /// </summary>
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
            /// <summary>Rounds fought, raw. Kept for the log and for physical per-round counts (javelins thrown).
            /// The PHASE boundaries read <see cref="Progress"/> rather than this -- which today is the same number,
            /// but says which of the two a boundary means.</summary>
            public int Round;

            /// <summary>
            /// How much FIGHTING has happened, in units of one full-size round. The volley, the skirmish and the
            /// quiver (VolleyRounds, SkirmishRounds, AmmoRounds) are all denominated in these units.
            ///
            /// AS THE CODE STANDS, THIS EQUALS <see cref="Round"/> EXACTLY, for every battle at every size: each
            /// round adds SimulationRoundCounter's pace, which is TickMultiplier / TickMultiplier -- 1, always.
            /// Nothing thins a round for a small fight. A ramp that would have (an "EffectiveTickMultiplier") was
            /// described in the comments here and never written; the small-battle problem it was meant to solve is
            /// handled a size down, by scaling the VOLLEY to the battle instead of the round (see GetVolleyRounds),
            /// so a small fight's ranged phase is short because its approach is short, not because its rounds are.
            ///
            /// It stays a separate float, and stays what the phases read, because it is the one seam at which the
            /// round could be repriced per battle without touching a boundary. Anything counting PHYSICAL per-round
            /// events (javelins thrown) must read <see cref="Round"/>, not this -- see HasJavelins.
            /// </summary>
            public float Progress;

            /// <summary>The raw round on which the skirmish began -- the first round <see cref="Progress"/> passed
            /// <see cref="VolleyRounds"/>. Javelins are a physical count (a man throws his bundle one per round), so
            /// HasJavelins counts RAW rounds from here, not progress. -1 until the skirmish opens.</summary>
            public int SkirmishStartRound = -1;

            /// <summary>How much of the fight, in <see cref="Progress"/> units, is spent with the bows alone at work
            /// before the javelins start. Not an integer: it is scaled by the battle's size (see GetVolleyRounds), and
            /// a small fight's volley is a fraction of a round.</summary>
            public float VolleyRounds;

            /// <summary>How much of the volley belongs to the defender's bows alone, in <see cref="Progress"/> units.
            /// Scaled by the battle's size in step with <see cref="VolleyRounds"/> -- see GetDefenderOnlyRounds for
            /// why the two cannot be allowed to drift apart.</summary>
            public float DefenderOnlyRounds;

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

            /// <summary>
            /// A WALL BEING STORMED, which is a different battle from the one the three acts above describe -- and
            /// when this is set, those acts do not run at all. The volley/skirmish/contact clock is replaced end to
            /// end by the two-phase siege model: an approach in which nobody is in reach of anybody, and an assault
            /// fought at whatever openings the siege equipment bought. See <see cref="SimulationSiege"/>.
            /// </summary>
            public bool SiegeAssaultBattle;

            /// <summary>Which of the two acts this battle is in -- approach, assault, or the storm that could not be
            /// started at all. Owned by SimulationSiege; meaningless unless <see cref="SiegeAssaultBattle"/>.</summary>
            public int SiegePhase;

            /// <summary>
            /// How good a wall this is, as a multiplier on the besieged side's whole advantage -- 1 for a fully
            /// fortified city or castle, 0.75 for the middle level, 0.5 for a palisade. Level 3 is the reference
            /// and nothing scales ABOVE it. Read once from the settlement's fortification building level when the
            /// battle is set up (nobody finishes a building under assault), and it scales the rate of fire, the
            /// magnitudes and the besieger's scatter. It does NOT touch the width: a hole in a great wall is the
            /// same size as a hole in a poor one. See SimulationSiege.MeasureWall.
            /// </summary>
            public float SiegeWallFactor = 1f;

            /// <summary>How many men each side can bring to bear at the openings, as a proportion of one another.
            /// Frozen from the surviving siege equipment when the approach ends, then moved by melee kills. See
            /// SimulationSiege.Widths.</summary>
            public int AttackWidth;

            public int DefendWidth;

            /// <summary>What the equipment bought at the moment the ladders went up -- the floor the widths can be
            /// ground back down to, and no further.</summary>
            public int StartAttackWidth;

            public int StartDefendWidth;

            /// <summary>
            /// What the three lanes actually held when the ladders went up -- "gate: ram 8/8 · wall: breach 4/4".
            /// Written once at the transition, purely so the log can say WHY the assault had the frontage it had.
            /// Live width is printed every round and moves with the killing; this is the thing that bought it, and
            /// without it a reader can see that a storm was hopeless but not that it was hopeless because two of
            /// the three lanes were empty. Null for a battle that never reached the assault.
            /// </summary>
            public string SiegeLanes;

            /// <summary>
            /// The round this battle stopped being a wall assault, or -1 if it never did. See
            /// SimulationSiege.StandDown -- native reclassifies a siege the moment a relief army joins the
            /// defenders, and the model has to follow it off the wall.
            /// </summary>
            public int SiegeStoodDownRound = -1;

            /// <summary>Every ranged engine both sides had at the muster, and the state of each -- deployed, still
            /// building, redeploying, broken, or sitting in reserve. Diagnostic only. See
            /// SimulationSiege.DescribeEngines for why it is worth carrying.</summary>
            public string SiegeEngineReport;

            /// <summary>
            /// How many men on each side BROKE AND RAN rather than fell.
            ///
            /// This matters because the game's casualty figure does not distinguish them. Native's
            /// <c>MapEventSide.Route()</c> puts every man still standing through <c>OnTroopRouted</c>, which
            /// increments <c>TroopCasualties</c> exactly as a death does -- so a side that broke and a side that
            /// was butchered to the last man report the identical number, and the log has been unable to tell an
            /// army that fled from an army that was destroyed. They are very different events: the fugitives live,
            /// and most of them come back. See SimulationRoutMarker.
            /// </summary>
            public int AttackerRouted;

            public int DefenderRouted;

            /// <summary>The round the break happened, or -1 if neither side ever broke.</summary>
            public int RoutRound = -1;

            /// <summary>
            /// What share of a side's blows this round must be SHOT rather than swung, for a siege's two ratios --
            /// the rate of fire and the width -- to both come out right. The tick allocation can only hand each side
            /// one number; this is the other half of the answer, and the striker selection is what honours it. Set
            /// every round by SimulationSiege.SplitTicks; ignored outside a wall assault.
            /// </summary>
            public float AttackerRangedTickTarget;

            public float DefenderRangedTickTarget;

            /// <summary>
            /// What each defending catapult decided to spend this siege on -- the besieger's equipment, or his men.
            /// Decided once per engine, the first time it fires, and held for the rest of the battle; two mangonels
            /// on the same wall may well be doing different jobs. Keyed on the engine itself. See
            /// SimulationSiegeEngines.
            /// </summary>
            public readonly Dictionary<SiegeEvent.SiegeEngineConstructionProgress, bool> SiegeEngineOrders =
                new Dictionary<SiegeEvent.SiegeEngineConstructionProgress, bool>();

            /// <summary>
            /// How many stones each heavy engine has thrown in THIS battle. A catapult or a trebuchet carries a
            /// finite pile of shot to the wall and it does not refill mid-assault; when the pile is gone the crew
            /// stand and watch. Counted per battle, not per siege -- the pile is restocked between assaults, and a
            /// besieger who storms twice does not do so with half a catapult. See SimulationSiegeEngines.
            /// </summary>
            public readonly Dictionary<SiegeEvent.SiegeEngineConstructionProgress, int> SiegeEngineShots =
                new Dictionary<SiegeEvent.SiegeEngineConstructionProgress, int>();

            public readonly Dictionary<CharacterObject, TroopState> Attackers = new Dictionary<CharacterObject, TroopState>();

            public readonly Dictionary<CharacterObject, TroopState> Defenders = new Dictionary<CharacterObject, TroopState>();

            /// <summary>Every blow of this battle, as it was really struck. Empty unless the hit log is on.</summary>
            public readonly List<HitRecord> Trace = new List<HitRecord>();

            /// <summary>Every shot the engines took. Empty unless the hit log is on, and for any battle without a
            /// wall in it. See <see cref="ArtilleryRecord"/>.</summary>
            public readonly List<ArtilleryRecord> Artillery = new List<ArtilleryRecord>();

            /// <summary>The muster roll: how many of each troop stand on each side, over all its parties.</summary>
            public Dictionary<CharacterObject, int> AttackerCounts = new Dictionary<CharacterObject, int>();

            public Dictionary<CharacterObject, int> DefenderCounts = new Dictionary<CharacterObject, int>();

            /// <summary>
            /// How many parties stood on each side when the muster was last taken. A party can still attach to a
            /// running map event after round 1 (reinforcements, a relief force, an army catching up), and its troops
            /// would fall through the round-1 muster and be handed the one-man fallback by <see cref="For"/>. When
            /// this count grows, the side is re-mustered so the newcomers get their real strength. -1 until round 1.
            /// </summary>
            public int AttackerPartyCount = -1;

            public int DefenderPartyCount = -1;

            /// <summary>
            /// What share of each side can shoot. This is needed because vanilla hands a side pow(men, 0.6) blows a
            /// round and then picks who throws each one UNIFORMLY FROM THE WHOLE SIDE -- so an archer only gets a
            /// turn as often as archers are common. In the volley, when nobody but an archer does anything at all,
            /// four fifths of a typical army's blows are spent on men standing still. The archers are not shooting
            /// slowly; they are being denied their turn. See VolleyFocus.
            /// </summary>
            public float AttackerRangedShare = 1f;

            public float DefenderRangedShare = 1f;

            /// <summary>
            /// What share of each side fights on its feet -- infantry and archers alike, everyone the charge is
            /// allowed to hit. Taken from the muster once, the same way <see cref="AttackerRangedShare"/> is, and
            /// used to read a live foot count off the side's remaining strength without walking the rosters again.
            /// See <see cref="ChargeChanceAgainst"/>.
            /// </summary>
            public float AttackerFootShare = 1f;

            public float DefenderFootShare = 1f;

            /// <summary>
            /// How many men each side still has ON FOOT, refreshed every round. This is the crowd a horse has to
            /// charge, and how big it is decides how often one can: see <see cref="ChargeChanceAgainst"/>.
            ///
            /// It is the side's LIVE strength times its muster foot share, not a fresh roster walk -- the walk is
            /// what the round-1 muster and RefreshReinforcements go out of their way to avoid doing per round, and
            /// this must not undo that. The cost is an assumption: that losses fall on foot and horse alike, so the
            /// share holds as the side is ground down. It does not hold exactly -- infantry die first and hardest --
            /// which makes this read the foot a little HIGH late in a battle, and so the charge a little too free
            /// exactly when the line is breaking. Worth knowing before this number is trusted too far.
            /// </summary>
            public float AttackerFoot;

            public float DefenderFoot;

            /// <summary>
            /// Each side's chain of command -- who leads the whole thing, and who leads each body of men. Built at
            /// round one off the same roster walk the muster takes (see AdvanceRound), and null until then, which is
            /// harmless: no blow lands before round one, and everything downstream reads a null command as "nobody
            /// is captaining anybody", which is exactly what an uncommanded side is. See SimulationCommandStructure.
            /// </summary>
            public SimulationCommandStructure.SideCommand AttackerCommand;

            public SimulationCommandStructure.SideCommand DefenderCommand;

            /// <summary>
            /// The captain over this troop, on this side, and his perk signature. The signature is what the kit
            /// cache is keyed on: two captains with the same perks make the same soldiers, so they share an entry.
            /// A troop with no captain -- and a captain asked about himself -- signs as 0, which is the uncaptained
            /// kit and byte-identical to what this model computed before captains existed.
            /// </summary>
            public CharacterObject CaptainFor(CharacterObject troop, bool attacker, out int signature)
            {
                signature = 0;
                SimulationCommandStructure.SideCommand command = attacker ? AttackerCommand : DefenderCommand;
                if (command == null)
                {
                    return null;
                }
                return command.CaptainFor(troop, out signature);
            }

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
                state.DefenderOnlyRounds = GetDefenderOnlyRounds(mapEvent);
                state.DefendersShootFromStores = mapEvent.IsSiegeAssault;
                state.KitingRoom = GetKitingRoom(mapEvent);
                state.ChargeChance = GetChargeChance(mapEvent);
                state.Dismounted = IsDismounted(mapEvent);
                state.AttackerCounts = Muster(mapEvent.AttackerSide);
                state.DefenderCounts = Muster(mapEvent.DefenderSide);
                state.AttackerRangedShare = RangedShare(state.AttackerCounts);
                state.DefenderRangedShare = RangedShare(state.DefenderCounts);
                SimulationSiege.Begin(mapEvent, state);
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

        /// <summary>
        /// The engines' own book, handed over before the battle is forgotten -- the same hand-off
        /// <see cref="TakeTrace"/> makes, and for the same reason. Null when nothing fired.
        /// </summary>
        internal static List<ArtilleryRecord> TakeArtillery(MapEvent mapEvent)
        {
            BattleState state;
            if (mapEvent == null || !_battles.TryGetValue(mapEvent, out state) || state.Artillery.Count == 0)
            {
                return null;
            }
            return state.Artillery;
        }

        /// <summary>
        /// How the wall assault went, in the four facts that are not visible anywhere else: how good the wall was,
        /// what the lanes held, the frontage that bought, and whether the storm happened at all. Handed over before
        /// Forget, like the trace and the artillery. Null for any battle that was not a wall assault.
        /// </summary>
        internal static BattleState TakeSiegeReport(MapEvent mapEvent)
        {
            BattleState state;
            if (mapEvent == null || !_battles.TryGetValue(mapEvent, out state))
            {
                return null;
            }
            // A battle that WAS a wall assault still owes the log an account of itself, even if it stopped being
            // one when a relief army turned up (SimulationSiege.StandDown clears the flag). Reporting only the
            // battles still flagged at the end would make exactly the fights that went strangest disappear.
            if (!state.SiegeAssaultBattle && state.SiegeStoodDownRound < 0)
            {
                return null;
            }
            return state;
        }

        /// <summary>
        /// How many men each side lost to a BREAK rather than to the fighting, handed over before the battle is
        /// forgotten. Both zero for a battle nobody ran from -- which, for the losing side, means it was destroyed.
        /// </summary>
        internal static void TakeRout(MapEvent mapEvent, out int attackerRouted, out int defenderRouted,
            out int routRound)
        {
            attackerRouted = 0;
            defenderRouted = 0;
            routRound = -1;
            BattleState state;
            if (mapEvent == null || !_battles.TryGetValue(mapEvent, out state))
            {
                return;
            }
            attackerRouted = state.AttackerRouted;
            defenderRouted = state.DefenderRouted;
            routRound = state.RoutRound;
        }

        /// <summary>
        /// The chain of command each side fought under, handed over before the battle is forgotten -- the same
        /// hand-off <see cref="TakeTrace"/> makes, and for the same reason: the write-up happens at MapEventEnded,
        /// by which time Forget has dropped all of this. Null for a battle nobody simulated (the player fought it
        /// himself), which is exactly when there were no captains to report.
        /// </summary>
        internal static void TakeCommands(MapEvent mapEvent, out SimulationCommandStructure.SideCommand attacker,
            out SimulationCommandStructure.SideCommand defender)
        {
            attacker = null;
            defender = null;
            BattleState state;
            if (mapEvent == null || !_battles.TryGetValue(mapEvent, out state))
            {
                return;
            }
            attacker = state.AttackerCommand;
            defender = state.DefenderCommand;
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
        /// A fresh session (new game OR a save loaded). Every battle held here belongs to the campaign being torn
        /// down -- its MapEvent objects will never end now, so their MapEventEnded cleanup will never run and they
        /// would sit here orphaned for the life of the process. Dropped wholesale. Called from OnSessionLaunched.
        /// </summary>
        internal static void ResetForNewSession()
        {
            _battles.Clear();
            // And the blow the siege width is holding, which points into one of the states just dropped.
            SimulationSiege.ResetForNewSession();
        }

        /// <summary>
        /// Called once per round, from the tick allocation. That is the only place the simulation tells us a round
        /// has turned -- a blow cannot, since it does not know how many came before it. <paramref name="pace"/> is
        /// how much of the fight this round carried, in units of one full-size round. Its only caller passes 1, for
        /// every battle at every size; it is a parameter so that the round has one place to be repriced. See
        /// BattleState.Progress.
        /// </summary>
        internal static void AdvanceRound(MapEvent mapEvent, float pace)
        {
            BattleState state = Get(mapEvent);
            if (state == null)
            {
                return;
            }

            state.Round++;
            // Never let a round advance the fight by nothing (a zero or negative pace would freeze the phases and hang
            // the volley forever), and never by more than the full round it is measured against. The live caller
            // passes exactly 1, so the clamp is a guard on a future repricing, not on today's arithmetic.
            state.Progress += MBMath.ClampFloat(pace, 0.01f, 1f);

            // The skirmish opens the first round the volley's progress is spent. Pinned to the RAW round because a
            // javelin is a physical count -- a man throws his bundle one per round from here (see HasJavelins).
            if (state.SkirmishStartRound < 0 && state.Progress > state.VolleyRounds)
            {
                state.SkirmishStartRound = state.Round;
            }

            // A WALL ASSAULT TURNS ITS OWN PHASE HERE. The approach ends, the siege equipment is read for whatever
            // survived it, and the widths are frozen -- or the storm is called off because nothing did. This must
            // sit after Progress has moved (the transition is a question about how much fighting has happened) and
            // before the round is fought, so the round about to be thrown belongs to the phase it is fought in.
            SimulationSiege.OnRound(mapEvent, state);

            // The first round is the first moment the battle can be seen WHOLE. MapEventStarted fires before a
            // lord's allies and the rest of his army have attached themselves to the event, so anything counted
            // there counts one party and misses the others. Here everyone who is coming has come -- and not a blow
            // has landed yet, so the rosters are still the rosters they marched in with.
            if (state.Round == 1)
            {
                state.AttackerCounts = Muster(mapEvent.AttackerSide);
                state.DefenderCounts = Muster(mapEvent.DefenderSide);
                state.AttackerPartyCount = CountParties(mapEvent.AttackerSide);
                state.DefenderPartyCount = CountParties(mapEvent.DefenderSide);
                state.AttackerRangedShare = RangedShare(state.AttackerCounts);
                state.DefenderRangedShare = RangedShare(state.DefenderCounts);
                state.AttackerFootShare = FootShare(state.AttackerCounts);
                state.DefenderFootShare = FootShare(state.DefenderCounts);

                // And the volley, re-measured now that the battle can be seen whole. It was set once already when the
                // state was made, but that was at MapEventStarted -- before a lord's allies had attached themselves --
                // so it was measured against a fraction of the men who turned up, and a full battle would have opened
                // with a small skirmish's short volley. Same reason the muster is taken here and not there. The
                // defender's opening window rides the same size and is re-measured with it, never apart from it.
                state.VolleyRounds = GetVolleyRounds(mapEvent);
                state.DefenderOnlyRounds = GetDefenderOnlyRounds(mapEvent);

                // A lord's armour and training are fixed for the length of a battle but not between battles, so his
                // kit is re-measured here, once, rather than on every blow he throws. His ARM classification is read
                // off that same kit, so it is dropped in step -- or selection would price him by last battle's gear.
                SimulationEquipmentPower.ForgetHeroKits();
                SimulationArmTargeting.ForgetHeroArms();

                // And who commands whom, off the muster that was just taken rather than a second walk of the same
                // rosters. It has to be here and not at MapEventStarted for exactly the reason the muster is here:
                // before round one a lord's allies have not attached themselves to the event, so a side read then is
                // a fraction of the side that turns up -- and a chain of command built over a fraction of an army
                // would hand the biggest body to the wrong man.
                state.AttackerCommand = SimulationCommandStructure.Build(mapEvent.AttackerSide, mapEvent, state.AttackerCounts);
                state.DefenderCommand = SimulationCommandStructure.Build(mapEvent.DefenderSide, mapEvent, state.DefenderCounts);

                SimulationBattleSnapshot.Recapture(mapEvent);
            }
            else
            {
                // After round 1 the only muster change we care about is a party JOINING -- reinforcements attaching to
                // a running event. Cheap party-count check per side; the roster walk only runs on the side that grew.
                bool attackerGrew;
                bool defenderGrew;
                RefreshReinforcements(mapEvent.AttackerSide, state.AttackerCounts, ref state.AttackerPartyCount,
                    out attackerGrew);
                if (attackerGrew)
                {
                    state.AttackerRangedShare = RangedShare(state.AttackerCounts);
                }
                RefreshReinforcements(mapEvent.DefenderSide, state.DefenderCounts, ref state.DefenderPartyCount,
                    out defenderGrew);
                if (defenderGrew)
                {
                    state.DefenderRangedShare = RangedShare(state.DefenderCounts);
                    state.DefenderFootShare = FootShare(state.DefenderCounts);
                }
                if (attackerGrew)
                {
                    state.AttackerFootShare = FootShare(state.AttackerCounts);
                }
            }

            // The crowd, re-read every round on both sides. Unlike the muster this is CHEAP -- a live troop count off
            // the side times a share already computed -- and unlike the muster it has to move, because the whole point
            // is that the charge dies away with the men who are there to be charged.
            state.AttackerFoot = LiveFoot(mapEvent.AttackerSide, state.AttackerFootShare);
            state.DefenderFoot = LiveFoot(mapEvent.DefenderSide, state.DefenderFootShare);

            // And a lord who has gone down stops leading. Four checks a side and no roster walk -- cheap enough to
            // run every round, which is the whole reason it is a validation and not a rebuild.
            if (state.AttackerCommand != null)
            {
                state.AttackerCommand.RetireTheFallen();
            }
            if (state.DefenderCommand != null)
            {
                state.DefenderCommand.RetireTheFallen();
            }
        }

        /// <summary>
        /// RE-READ THE GROUND. Every terrain fact a battle has is measured once, when its state is made, because a
        /// field does not move while it is being fought over. A SIEGE CAN, though -- not the ground, but what kind
        /// of battle is being fought on it: native reclassifies a wall assault the instant a relief army joins the
        /// defenders (see SimulationSiege.StandDown), and everything measured for a wall is then wrong. There are no
        /// horses in a siege, no room to charge and no room to kite; there are all three in the open field the
        /// battle has just become.
        ///
        /// So this re-measures the five, from the battle as it now is. It is the ONLY thing that may re-measure
        /// them, and it is called from exactly one place.
        ///
        /// Progress is deliberately NOT rewound. The fighting that has happened has happened; a battle forty rounds
        /// old does not owe anybody a fresh volley. With Progress already past the field volley and skirmish, the
        /// phase predicates put it straight into contact, which is exactly what it is: the lines are long since met.
        /// </summary>
        internal static void RelatchTerrain(MapEvent mapEvent, BattleState state)
        {
            if (mapEvent == null || state == null)
            {
                return;
            }
            state.VolleyRounds = GetVolleyRounds(mapEvent);
            state.DefenderOnlyRounds = GetDefenderOnlyRounds(mapEvent);
            state.DefendersShootFromStores = mapEvent.IsSiegeAssault;
            state.KitingRoom = GetKitingRoom(mapEvent);
            state.ChargeChance = GetChargeChance(mapEvent);
            state.Dismounted = IsDismounted(mapEvent);
        }

        /// <summary>The number of parties standing on a side -- cheap to count without walking any roster.</summary>
        private static int CountParties(MapEventSide side)
        {
            if (side == null)
            {
                return 0;
            }
            int n = 0;
            foreach (MapEventParty _ in side.Parties)
            {
                n++;
            }
            return n;
        }

        /// <summary>
        /// If a side has GAINED parties since the last muster, fold the newcomers into its counts. Only troop TYPES
        /// not already tracked are added (with their live strength); an existing stack's count stays frozen at what
        /// it mustered with -- its <see cref="TroopState"/> pool is already built from that figure and cannot be
        /// re-sized mid-fight anyway, so re-mustering it would only bleed casualties into a number nothing reads.
        /// This closes the gap where a stack joining after round 1 was handed the one-man fallback by <see cref="For"/>.
        /// </summary>
        private static void RefreshReinforcements(MapEventSide side, Dictionary<CharacterObject, int> counts,
            ref int lastPartyCount, out bool grew)
        {
            grew = false;
            if (side == null || counts == null)
            {
                return;
            }
            int now = CountParties(side);
            if (now <= lastPartyCount)
            {
                lastPartyCount = now;
                return;
            }
            lastPartyCount = now;
            grew = true;

            Dictionary<CharacterObject, int> fresh = Muster(side);
            foreach (KeyValuePair<CharacterObject, int> entry in fresh)
            {
                if (!counts.ContainsKey(entry.Key))
                {
                    counts[entry.Key] = entry.Value;
                }
            }
        }

        /// <summary>
        /// The battle size at which the approach is a full one. Below it the volley shortens, in proportion; at or
        /// above it nothing changes and the old figures stand. The same two hundred the charge saturates at
        /// (ChargeCrowdSaturation), and deliberately so -- both are the model saying the same thing about the same
        /// number: that below this, a fight is not a battle with a line in it.
        /// </summary>
        private const float VolleyBattleSaturation = 200f;

        /// <summary>Whatever the fight, the bows get something. A skirmish so small it had no approach at all would
        /// leave archers with no phase of their own, and an archer who never looses is not a model of anything.</summary>
        private const float MinVolleyRounds = 1f;

        /// <summary>
        /// How long the bowmen have before the lines meet -- which is a question about the ground, and about whether
        /// there is a line at all.
        ///
        /// THE GROUND says how far there is to walk. Across an open plain a man walks a long way under arrows; in a
        /// wood he is on you before the second shaft is nocked. Storming a wall is the longest walk of all, and
        /// everyone on it is shooting at you the whole way.
        ///
        /// THE SIZE says whether anybody walks it as a line. Two warbands of twenty do not deploy at two hundred paces
        /// and advance under arrows; they blunder into each other and start swinging, and the bowmen get off what they
        /// get off. The paired logs say it plainly -- a real 22 v 20 landed some five shots before the lines met,
        /// where the model was spending eighty-odd blows on four full rounds of volley. So the ground's figures are
        /// the ceiling, reached at VolleyBattleSaturation men, and a smaller fight gets a proportional share.
        ///
        /// A SIEGE AND A SEA FIGHT ARE EXEMPT, and that is not an oversight. What a small field battle lacks is a line
        /// and a stretch of ground both sides agreed to cross. A siege has both whatever the numbers: there is a wall,
        /// there is a killing ground in front of it, and thirty men must cross it under the same arrows two hundred
        /// would -- crossing it is what a siege IS. Ships close at the speed of ships. Neither length is a fact about
        /// how many men are present, so neither is scaled.
        /// </summary>
        private static float GetVolleyRounds(MapEvent mapEvent)
        {
            // A WALL ASSAULT'S APPROACH IS NOT A VOLLEY and is not measured like one -- it is its own phase, its own
            // length, and its own rules (see SimulationSiege). The figure is still written here because everything
            // that reads a progress-through-the-approach -- the shot-accuracy ramp, chiefly -- reads VolleyRounds,
            // and it must be the siege's length or the ramp would run against a number this battle never uses.
            if (mapEvent.IsSiegeAssault)
            {
                return SimulationSiege.ApproachRounds;
            }
            return GetVolleyRounds(mapEvent.SimulationContext, mapEvent.IsSiegeAssault, BattleSize(mapEvent));
        }

        /// <summary>Every man still standing on both sides. Read at the muster, when none has fallen yet.</summary>
        private static int BattleSize(MapEvent mapEvent)
        {
            int attackers = (mapEvent.AttackerSide != null) ? mapEvent.AttackerSide.NumRemainingSimulationTroops : 0;
            int defenders = (mapEvent.DefenderSide != null) ? mapEvent.DefenderSide.NumRemainingSimulationTroops : 0;
            return attackers + defenders;
        }

        private static float GetVolleyRounds(MapEvent.PowerCalculationContext context, bool isSiegeAssault, int men)
        {
            float full = FullVolleyRounds(context, isSiegeAssault);
            if (IsFixedApproach(context, isSiegeAssault))
            {
                return full;
            }
            return MathF.Max(MinVolleyRounds, full * MBMath.ClampFloat(men / VolleyBattleSaturation, 0f, 1f));
        }

        /// <summary>Battles whose approach is set by the ground and not by the crowd -- a wall to storm, a hull to come
        /// alongside. See the note on GetVolleyRounds.</summary>
        private static bool IsFixedApproach(MapEvent.PowerCalculationContext context, bool isSiegeAssault)
        {
            if (isSiegeAssault)
            {
                return true;
            }
            switch (context)
            {
                case MapEvent.PowerCalculationContext.Siege:
                case MapEvent.PowerCalculationContext.NavalRaid:
                case MapEvent.PowerCalculationContext.SeaBattle:
                case MapEvent.PowerCalculationContext.OpenSeaBattle:
                case MapEvent.PowerCalculationContext.RiverCrossingBattle:
                    return true;
                default:
                    return false;
            }
        }

        private static float FullVolleyRounds(MapEvent.PowerCalculationContext context, bool isSiegeAssault)
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
        // stay nothing (a wall and a deck have no charge to scale). So the open field reads 0.55, a wood 0.44 and a
        // village street 0.165, before the crowd thins them (ChargeChanceAgainst).
        //
        // It has been up and down: 1.2 first, then pulled to 0.9 when the charge became unblockable and started
        // hitting for its mount's own weight -- the horse was charging too often and running whole battles -- and now
        // back to 1.1. Note it moves TWO things, not one, and they pull against each other: it is also the chance a
        // horse is CLOSING onto a set spear (the same reading, from the other end -- see the brace in
        // SimulationEquipmentPower). Raising it gives the horse more charges AND gives the spearmen waiting for it
        // more of the rebound that wrecks them. It is not a pure buff to cavalry, and the paired log is the only
        // thing that will say which way it nets out.
        private const float ChargeChanceBoost = 1.1f;

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
        /// WHETHER THIS MAN FIGHTS THIS BATTLE ON A HORSE. The answer for everything that prices a man as he actually
        /// stands in this battle -- which is nearly everything here, with one deliberate exception noted below.
        ///
        /// It reads <c>troop.IsMounted</c> -- the formation class off his XML -- and not <c>HasMount()</c>, which
        /// inspects the horse slot of his FIRST equipment set. The two disagree (a Cavalry-classed troop whose
        /// opening set has no horse; an Infantry-classed one who carries a mount), and the model has committed to the
        /// formation class everywhere else: it is what the kit records, what the arm classification reads, and what
        /// the whole cavalry/foot split of this simulation is built on. Native's own models ask an Agent for
        /// HasMount, but an Agent has genuinely been spawned onto a horse or not; a CharacterObject has not, and
        /// picking the equipment slot to imitate the wording gets native's expression while missing its meaning.
        ///
        /// And then the battle overrules him. A siege has no horses in it at all -- the wall is stormed and held on
        /// foot, and the game leaves every mount in the camp -- so a cavalryman there is a lance and a suit of
        /// barding with no animal under it (see <see cref="IsDismounted"/>). Native's HIT-POINT model gets this for
        /// free, because GetEffectiveMaxHealth asks the AGENT (`agent.HasMount`) and a siege agent really was spawned
        /// without a horse. Here it has to be asked deliberately, and SimulationTroopHitPoints does.
        ///
        /// THE EXCEPTION, and it is native's, not ours: GetEffectiveSkill asks the TEMPLATE
        /// (`characterObject.HasMount()`), which the siege never touched -- so native's CAPTAIN perks are blind to
        /// the dismount and hand a horse archer his Horse Master on a ladder. SimulationPerks ports that blindness
        /// deliberately rather than call here; see SimulationPerks.IsCavalryTemplate for why. The two native methods
        /// genuinely disagree with each other, and this is not the place to reconcile them.
        /// </summary>
        internal static bool IsMountedIn(CharacterObject troop, bool dismountedBattle)
        {
            return troop != null && troop.IsMounted && !dismountedBattle;
        }

        /// <summary>Whether this battle is one nobody fights mounted, off the battle's own cached state. For the
        /// callers that hold a MapEvent rather than a BattleState. See <see cref="IsDismounted"/>.</summary>
        internal static bool IsDismountedBattle(MapEvent mapEvent)
        {
            BattleState state = Get(mapEvent);
            return state != null && state.Dismounted;
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
        /// How long the field belongs to the defender's bows alone, in a battle big enough to have a proper approach.
        /// He is standing on his ground with his enemy in the open and the whole distance to shoot across; the
        /// attacker is still coming, too far out to answer, and eats it. That is what it means to advance on a
        /// prepared position.
        ///
        /// This is the FULL-SIZE figure. The window it opens is a share of the approach, not an absolute -- it has to
        /// shorten with the volley or it swallows it whole. See <see cref="GetDefenderOnlyRounds"/>.
        /// </summary>
        private const float DefenderOnlyRoundsFull = 2f;

        /// <summary>
        /// How long the defender's bows have the field to themselves in THIS battle -- the full-size window, cut down
        /// by the size of the fight exactly as the volley is (see <see cref="GetVolleyRounds"/>), and exempt for the
        /// same fixed-approach battles.
        ///
        /// The two MUST be scaled by the same measure. They were not, and it was a real bug: the volley shrank to 1.26
        /// rounds in a 42-man fight while this window stayed a flat 2, so the window was longer than the entire volley
        /// and the attacker's bowmen -- who were the only bowmen on the field -- were barred from loosing for the
        /// whole of it. The defenders had no bows to answer with, so the volley passed in silence and the phase
        /// vanished from the log. A share of nothing has to be nothing.
        /// </summary>
        private static float GetDefenderOnlyRounds(MapEvent mapEvent)
        {
            // A WALL ASSAULT HAS NO SUCH WINDOW, and needs none. The field's version silences the attacker's bows
            // outright for the opening rounds, which is a blunt way of saying the defender shoots and the attacker
            // mostly cannot. A siege says the same thing far better and for the WHOLE approach, as a rate: five
            // shots to one (SimulationSiege.ApproachDefenderFireRatio). Leaving both in would silence the besieger
            // twice over -- barred at the start and out-shot five to one thereafter.
            if (mapEvent.IsSiegeAssault)
            {
                return 0f;
            }
            if (IsFixedApproach(mapEvent.SimulationContext, mapEvent.IsSiegeAssault))
            {
                return DefenderOnlyRoundsFull;
            }
            return DefenderOnlyRoundsFull
                * MBMath.ClampFloat(BattleSize(mapEvent) / VolleyBattleSaturation, 0f, 1f);
        }

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

        /// <summary>
        /// How far through the volley this blow falls: 0 at the opening, 1 at the moment the javelins start.
        ///
        /// The volley is not one distance -- it is a distance CLOSING. It opens with the lines as far apart as they
        /// will ever be, and ends with them near enough to throw a spear across. A shaft loosed at the start of it is
        /// the longest shot anyone takes all battle; the same archer at the end of it is looking at a man he can see
        /// plainly. Anything that treats the whole volley as one range is averaging those two, and the log cannot
        /// tell them apart.
        ///
        /// A fraction of the volley's own length rather than a count, so it holds its meaning whatever the battle's
        /// size (a small fight's volley is a fraction of a round -- see GetVolleyRounds); 1 outside the volley, and
        /// for a battle with no volley at all,
        /// so a caller that asks anyway gets the closest, flattest shot rather than the longest.
        /// </summary>
        internal static float VolleyProgress(BattleState state)
        {
            if (state == null || state.VolleyRounds <= 0f)
            {
                return 1f;
            }
            return MBMath.ClampFloat(state.Progress / state.VolleyRounds, 0f, 1f);
        }

        internal static bool IsVolleyPhase(BattleState state)
        {
            // A WALL ASSAULT ANSWERS THIS ITS OWN WAY. Its approach is the same KIND of thing the volley is -- a
            // stretch of fighting in which only the bows act and a man not shooting lands nothing -- so it answers
            // yes here and inherits all of that machinery. What it does NOT inherit is the volley's length, its
            // defender-only window, or the skirmish that follows it: those are facts about a field, and a siege's
            // are set by SimulationSiege instead. See GetVolleyRounds and IsSkirmishPhase.
            if (state != null && state.SiegeAssaultBattle)
            {
                return SimulationSiege.IsApproach(state);
            }

            // Progress rather than the raw round -- the same number today, but the volley is a length of FIGHTING and
            // this is the side of the comparison that says so. What makes a small battle's volley short is the volley
            // itself being sized to the fight (see GetVolleyRounds), not the rounds running thin. Progress is advanced
            // with the round's allocation, so it already includes this round's fighting -- hence <=, not <.
            return state != null && state.Progress <= state.VolleyRounds;
        }

        /// <summary>The horse are out, the javelins are in the air, and the foot are still walking.</summary>
        internal static bool IsSkirmishPhase(BattleState state)
        {
            // THERE IS NO SKIRMISH IN A SIEGE. The skirmish is the ground between two lines -- javelins hurled
            // across it, and each side's horse riding out at the other before the foot arrive. A wall assault has
            // none of those things: no second line, no open ground, and no horses at all (see IsDismounted). The
            // approach ends and the ladders go up, with nothing in between.
            if (state != null && state.SiegeAssaultBattle)
            {
                return false;
            }

            return state != null
                && state.Progress > state.VolleyRounds
                && state.Progress <= (state.VolleyRounds + SkirmishRounds);
        }

        /// <summary>The point in the fight the foot finally reach each other -- in Progress units, like the phases.</summary>
        internal static float ContactRound(BattleState state)
        {
            if (state != null && state.SiegeAssaultBattle)
            {
                // The ladders go up the moment the killing ground has been crossed; there is no skirmish to wait out.
                return SimulationSiege.ApproachRounds + 1f;
            }
            return (state != null) ? (state.VolleyRounds + SkirmishRounds + 1f) : 1f;
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
                // The model's ONE ranged test -- a slinger counts, though CharacterObject.IsRanged classes him as
                // infantry. Slingers shoot in the volley (SimulationBattleState reclassifies them ranged), so leaving
                // them out here understates the share and over-boosts VolleyFocus (share^-0.4). See IsRangedTroop.
                if (SimulationEquipmentPower.IsRangedTroop(entry.Key))
                {
                    ranged += entry.Value;
                }
            }
            return (total > 0) ? (ranged / (float)total) : 1f;
        }

        /// <summary>
        /// What share of this muster stands on the ground -- infantry and archers together. A charge only ever lands
        /// on a man on foot (see the chargeEligible gate in SimulationEquipmentPower), and an archer on foot is a body
        /// a horse can ride down as surely as a spearman is -- riding down unsupported bowmen is the oldest use a
        /// horse has. So the question here is not which arm a man belongs to but whether he has an animal under him.
        /// </summary>
        internal static float FootShare(Dictionary<CharacterObject, int> roll)
        {
            if (roll == null || roll.Count == 0)
            {
                return 1f;
            }
            int total = 0;
            foreach (KeyValuePair<CharacterObject, int> entry in roll)
            {
                total += entry.Value;
            }
            return (total > 0) ? (FootCount(roll) / (float)total) : 1f;
        }

        /// <summary>How many men in this roll stand on the ground. See <see cref="FootShare"/> for what counts.</summary>
        internal static int FootCount(Dictionary<CharacterObject, int> roll)
        {
            if (roll == null)
            {
                return 0;
            }
            int foot = 0;
            foreach (KeyValuePair<CharacterObject, int> entry in roll)
            {
                int arm = SimulationEquipmentPower.ArmOf(entry.Key);
                if (arm == SimulationEquipmentPower.InfantryType || arm == SimulationEquipmentPower.ArcherType)
                {
                    foot += entry.Value;
                }
            }
            return foot;
        }

        /// <summary>
        /// What a horseman's chance of charging INTO that side was at the opening, before a man of it had fallen.
        /// For the log alone: by the time a battle is written up its state has been forgotten, so this is recomputed
        /// from the snapshot's opening roll. The battle itself reads <see cref="ChargeChanceAgainst"/>, which thins
        /// as the crowd does -- this is only where that curve started.
        /// </summary>
        internal static float ChargeChanceOpening(MapEvent mapEvent, Dictionary<CharacterObject, int> struckRoll)
        {
            float foot = FootCount(struckRoll);
            return GetChargeChance(mapEvent) * MBMath.ClampFloat(foot / ChargeCrowdSaturation, 0f, 1f);
        }

        /// <summary>
        /// How long this battle's volley ran, for the log. Recomputed at write-up the same way
        /// <see cref="ChargeChanceOpening"/> is, because the state is gone by then.
        ///
        /// <paramref name="men"/> must be the OPENING count, off the snapshot -- taking it from the event here would
        /// read the survivors, and report a short volley for every battle that ended in a slaughter.
        /// </summary>
        internal static float VolleyRoundsOpening(MapEvent mapEvent, int men)
        {
            return (mapEvent != null)
                ? GetVolleyRounds(mapEvent.SimulationContext, mapEvent.IsSiegeAssault, men)
                : 0f;
        }

        /// <summary>How many men that side still has on foot. Live strength x the muster's foot share -- see the note
        /// on <see cref="BattleState.AttackerFoot"/> for why it is read this way and what it costs.</summary>
        private static float LiveFoot(MapEventSide side, float footShare)
        {
            return (side != null) ? (side.NumRemainingSimulationTroops * footShare) : 0f;
        }

        /// <summary>
        /// The crowd at which a charge is as free as the ground allows. Below it the horse charges less, in proportion;
        /// at or above it, the terrain figure stands unmodified and nothing here changes today's behaviour.
        ///
        /// The number is read off the paired logs, not chosen: a real 22 v 20 desert battle gave TWO charges in some
        /// fifty mounted melee blows -- four percent -- against roughly twenty men on foot. Four percent is 20/200 of
        /// the open field's own 45%, so two hundred is where the line through that point reaches full. It says
        /// something the model had no way to say before: that a twenty-man bandit scrap is not a battle with a line in
        /// it. Nobody forms up, nobody has anywhere to ride from, and there is no press of bodies to break -- it is a
        /// brawl, and men in a brawl do not couch lances. A field battle with two hundred foot in it has a line, and
        /// there the horse charges as it always did.
        /// </summary>
        private const float ChargeCrowdSaturation = 200f;

        /// <summary>
        /// How often a horseman's blow at THAT side carries the charge -- the terrain's own figure (see
        /// <see cref="GetChargeChance"/>), thinned by how few men that side has left standing on the ground.
        ///
        /// Which side is passed matters and is easy to get backwards: it is the side BEING CHARGED, whose foot are the
        /// bodies in question -- not the side the horse rides for. The anti-cavalry brace reads it from the other
        /// end (the footman is the striker there, the closing horse the struck), so it passes the striker's side.
        /// </summary>
        internal static float ChargeChanceAgainst(BattleState state, bool struckIsAttacker)
        {
            if (state == null)
            {
                return 0f;
            }
            float foot = struckIsAttacker ? state.AttackerFoot : state.DefenderFoot;
            return state.ChargeChance * MBMath.ClampFloat(foot / ChargeCrowdSaturation, 0f, 1f);
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

            // Against Progress rather than the raw round, so the quiver is spent by FIGHTING however the battle comes
            // to be paced. Today the round is never repriced, so this is the raw round count exactly: thirty rounds
            // of loosing and then he is a man with a knife, in a skirmish and a set-piece alike.
            return battle.Progress <= AmmoRounds;
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

            // He hurls one per RAW round of the skirmish -- a javelin is a physical thing, two javelins are two
            // throws, whatever the fight's pace -- so this counts raw rounds from where the skirmish opened, NOT
            // progress. Counted from SkirmishStartRound rather than VolleyRounds because VolleyRounds is not a round
            // number at all: it is scaled by the battle's size and a small fight's is a FRACTION of a round (see
            // GetVolleyRounds), so there is no raw round it names. The round the skirmish actually opened on is
            // recorded when it opens (see AdvanceRound), and that is the only honest thing to count from. Both
            // sides begin the skirmish together, so no side needs a head start or a handicap. If the skirmish has not
            // opened yet (still in the volley), he throws nothing.
            if (battle.SkirmishStartRound < 0)
            {
                return false;
            }
            int roundsIntoSkirmish = battle.Round - battle.SkirmishStartRound + 1;
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
        /// What is left of this stack's horses. Every horse hit wears this pool, and when it empties the mount falls:
        /// its rider is then a man on foot in cavalry harness, stripped of the barding, the charge and the height the
        /// horse gave him. Each animal's share of the pool is ITS OWN health -- a heavy charger takes far more than a
        /// mule -- so <paramref name="horseHealth"/> comes from the struck troop's kit (TroopKit.HorseHealth); the
        /// flat HorseCapacity stands in only when that could not be read.
        /// </summary>
        internal static float HorsesAlive(TroopState state, float horseHealth)
        {
            float perAnimal = (horseHealth > 0f) ? horseHealth : HorseCapacity;
            return MBMath.ClampFloat(1f - (state.HorseDamage / (perAnimal * state.Count)), 0f, 1f);
        }

        internal static void DamageHorse(TroopState state, float amount)
        {
            state.HorseDamage += MathF.Max(0f, amount);
        }
    }
}
