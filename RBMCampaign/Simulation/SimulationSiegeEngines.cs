using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// THE ARTILLERY. Engines are not troops and never were, and every part of the simulation up to here is built
    /// around one soldier striking another -- so the engines get their own volley, fired once at the top of each
    /// round, outside the blow-by-blow entirely.
    ///
    /// They are read from the game, not invented: <c>SiegeEnginesContainer.DeployedRangedSiegeEngines</c>, four
    /// slots a side, and an engine counts only while it is built and unbroken. What each one IS comes from its
    /// type, bucketed the way native itself buckets them for map projectiles:
    ///
    ///   BALLISTA (ballista, fire_ballista) -- a bolt-thrower that picks men. It shoots ONLY at men, it shoots once
    ///   or twice a round, and a bolt that finds a man puts a terrible wound in him -- lethal to a whole man at
    ///   the top of its band, survivable in good armour, and cumulative, so a second bolt finishes the first.
    ///
    ///   STONE CATAPULT (catapult, onager, bricole) -- a rock. Against men it kills two or three at a stroke;
    ///   against an engine it is one of the two things that can break one.
    ///
    ///   POT CATAPULT (fire_catapult, fire_onager) -- a pot that bursts. It kills the man it lands on and burns a
    ///   scattering of others around him.
    ///
    ///   TREBUCHET (trebuchet, fire_trebuchet) -- the siege-breaker, and the besieger's alone: no garrison in the
    ///   game fields one. It is slow (one shot every second round), it hits engines harder than anything else, and
    ///   what its counterweight throws kills men wherever it lands.
    ///
    /// WHO SHOOTS AT WHAT, by side:
    ///
    ///   The DEFENDER's catapults decide once, at the start, whether they are working on the besieger's equipment
    ///   or on his men, and they hold to it -- until the ladders go up, when every engine on the wall turns to the
    ///   men climbing it, because at that point the equipment no longer matters and the men do. A defender shooting
    ///   at men during the ASSAULT can miss; on the approach, firing into a packed column crossing open ground, he
    ///   does not.
    ///
    ///   The ATTACKER's heavy engines shoot at the defender's engines, the whole battle through, and they keep
    ///   shooting once the assault begins -- a counter-battery duel does not stop because the ladders went up. What
    ///   they kill among the garrison, they kill incidentally: a stone that misses a mangonel still lands somewhere.
    ///   And every one of his shots is worse than the equivalent shot from the wall, because he is shooting up at a
    ///   target he cannot see properly from ground that was levelled yesterday.
    ///
    /// AN ENGINE BROKEN HERE IS BROKEN FOR GOOD. It is removed from the campaign's own siege event, which means a
    /// ram lost to a mangonel is a ram that has to be rebuilt before the next assault -- and, because the assault
    /// widths are read from the surviving equipment at the end of the approach (see <see cref="SimulationSiege"/>),
    /// a defender who breaks the ram in time has genuinely narrowed the storm that follows.
    /// </summary>
    internal static class SimulationSiegeEngines
    {
        // =========================================================================================================
        // What each kind of engine is.

        private const int KindNone = 0;
        private const int KindBallista = 1;
        private const int KindStoneCatapult = 2;
        private const int KindPotCatapult = 3;
        private const int KindTrebuchet = 4;

        /// <summary>
        /// Bucket an engine by its id.
        ///
        /// BY STRING ID, AND THAT IS NOT LAZINESS. <c>DefaultSiegeEngineTypes.FireTrebuchet</c> is a native BUG: the
        /// property returns <c>_siegeEngineTypeFireTrebuchet</c>'s neighbour, <c>_siegeEngineTypeTrebuchet</c>, so
        /// comparing against it silently matches the PLAIN trebuchet while a real fire trebuchet matches nothing at
        /// all. Ids do not lie, and RBM's own siege engine XML keeps every one of them.
        ///
        /// The grouping is native's own, taken from <c>GetSiegeEngineMapProjectilePrefabName</c>: catapult, onager,
        /// bricole and trebuchet all throw a stone, the fire catapult and fire onager throw a pot.
        /// </summary>
        private static int KindOf(SiegeEngineType type)
        {
            if (type == null || !type.IsRanged)
            {
                return KindNone;
            }
            switch (type.StringId)
            {
                case "ballista":
                case "fire_ballista":
                    return KindBallista;
                case "fire_catapult":
                case "fire_onager":
                    return KindPotCatapult;
                case "trebuchet":
                case "fire_trebuchet":
                    return KindTrebuchet;
                case "catapult":
                case "onager":
                case "bricole":
                    return KindStoneCatapult;
                default:
                    // A ranged engine this model has never heard of -- a mod's, or a new one. It throws something
                    // heavy at something; treat it as the commonest engine there is rather than ignore it.
                    return KindStoneCatapult;
            }
        }

        // =========================================================================================================
        // The dials. Hardcoded, like every other number in the siege model -- see the note on SimulationSiege.
        //
        // HIT CHANCES AND ENGINE DAMAGE ARE NOT HERE. They are read off SiegeEngineType, which is game data and
        // which RBM already overrides in RBMXML/RBMCombat_siege_engines.xml -- so a trebuchet outreaches a catapult
        // because the data says it does, and retuning that XML retunes this with no code change. What lives here is
        // only what the data has no opinion about: how often an engine fires, and what a hit means for the men.

        /// <summary>Every shot the besieger takes is this much likelier to go wide -- his engines are on levelled
        /// ground shooting up at a target on a battlement he can barely see, and the men serving them have been
        /// under fire since they arrived. Multiplies the data's own hit chance for the attacking side only.</summary>
        private const float AttackerHitChanceFactor = 0.7f;

        /// <summary>A ballista shoots fast for a siege engine -- a crew can span and loose it several times in the
        /// time a mangonel is winched once. One or two bolts a round, evenly.</summary>
        private const int BallistaShotsMin = 1;
        private const int BallistaShotsMax = 2;

        /// <summary>What a stone does when it lands among men: two or three of them, at once.</summary>
        private const int StoneCatapultKillsMin = 2;
        private const int StoneCatapultKillsMax = 3;

        /// <summary>A pot kills the man it lands on...</summary>
        private const int PotCatapultKills = 1;

        /// <summary>...and burns this many around him.</summary>
        private const int PotCatapultSplashTargets = 10;

        /// <summary>How badly, as a share of the man's own wound pool. A heavy burn that mostly does not kill on
        /// its own but leaves him easy to finish -- so a pot's real toll arrives a round or two later, in the
        /// ordinary fighting, which is exactly how burns work.</summary>
        private const float SplashDamageMin = 0.2f;
        private const float SplashDamageMax = 0.6f;

        /// <summary>How often a besieger's stone, thrown at an engine, comes down on somebody instead. Rolled
        /// independently of whether the engine was hit: the stone lands somewhere either way.</summary>
        private const float AttackerStoneCollateralChance = 0.25f;

        /// <summary>How many men a besieger's pot catches when it bursts on the wall. Fewer than the defender's,
        /// which is aimed at a crowd in the open; this one is aimed at a machine.</summary>
        private const int AttackerPotSplashMin = 3;
        private const int AttackerPotSplashMax = 6;

        /// <summary>The trebuchet's edge over a catapult against timber, ON TOP of the data -- which already gives
        /// it roughly three times a catapult's damage. This is the last touch, not the whole of it.</summary>
        private const float TrebuchetEngineDamageBonus = 1.15f;

        /// <summary>A counterweight has to be winched back up. One shot every second round.</summary>
        private const int TrebuchetRoundInterval = 2;

        /// <summary>
        /// And once the ladders are up, a catapult slows to the same pace. Nothing about the machine changed --
        /// what changed is everything around it. On the approach a crew loose at a fixed point they have been
        /// ranging on for days, and can keep loosing; in the assault the fight is moving, their own men are in it,
        /// and every shot has to be spotted, argued over and re-laid before the arm is released.
        ///
        /// It is also the single biggest lever on how much a siege's artillery decides. One defending fire catapult
        /// took 63 shots through an assault at Tamnuh Castle and alone killed 58 men and burned 503 more out of a
        /// storming force of 550 -- more than the whole of the hand-to-hand fighting. Halving the rate halves that.
        /// </summary>
        private const int AssaultCatapultRoundInterval = 2;

        /// <summary>
        /// HOW MANY STONES A HEAVY ENGINE BROUGHT. Fifteen apiece, and when they are gone the crew stand and watch.
        ///
        /// A catapult does not shoot from an inexhaustible pile. Every stone has to be cut, hauled and stacked
        /// beside the machine before the assault, and what is stacked there is what it has -- there is no carrying
        /// more up under fire. This is the same reasoning as the archers' quiver (see AmmoRounds in
        /// SimulationBattleState) and it fixes the same failure: an engine that never runs dry decides a long
        /// battle by itself simply because the battle was long.
        ///
        /// A MISS SPENDS A STONE. Whether it found anything is not the crew's business; the stone is gone either way.
        ///
        /// Counted per BATTLE. The pile is restocked between assaults, so a besieger who storms twice does not do
        /// it the second time with half a catapult.
        /// </summary>
        private const int HeavyEngineAmmo = 15;

        /// <summary>
        /// AND A BALLISTA'S SHEAF OF BOLTS. Twenty -- more than a mangonel's pile of stone, because a bolt is a
        /// small thing a crew can carry by the armful where a dressed stone is a cart-load apiece.
        ///
        /// It was exempt at first, on the reasoning that nobody runs a bolt-thrower dry in an afternoon. That was
        /// wrong, and the logs said so plainly: capping the catapults and not the ballistas simply handed the siege
        /// to the one engine still working. Across eight sieges the defending ballistas fired 340 of 470 shots, and
        /// in one of them 350 shots killed 241 men -- near a quarter of the storming army -- purely because they
        /// were the only engine that never stopped. A limit that applies to one engine and not its neighbour does
        /// not restrain a battle; it just picks a different winner.
        /// </summary>
        private const int BallistaAmmo = 30;

        /// <summary>
        /// WHAT A BOLT DOES TO THE MAN IT FINDS, as a share of his own wound pool.
        ///
        /// It used to kill outright -- every hit, one man gone -- and that was too absolute. A bolt-thrower is a
        /// terrible weapon, but it is still a weapon meeting a man in armour, and the rest of this model spends a
        /// great deal of care on what armour is worth; a flat "he dies" threw all of that away for the engine that
        /// fires most often of any. In one logged siege it meant 241 dead from 350 bolts.
        ///
        /// So it is a very heavy wound instead, priced against the man like every other blow. The top of the band
        /// is exactly lethal to a whole man, so a bolt kills outright rather less than half the time and leaves the
        /// rest badly hurt -- and because wounds accumulate in the pool, a second bolt usually finishes what the
        /// first began. Armour tells again: the bolt that kills a levy leaves an armoured serjeant on his feet.
        /// </summary>
        private const float BallistaDamageMin = 0.5f;
        private const float BallistaDamageMax = 1.0f;

        /// <summary>How often a trebuchet's stone comes down among the garrison, and how many it takes when it
        /// does. It is a far bigger rock than a catapult's.</summary>
        private const float TrebuchetCollateralChance = 0.35f;
        private const int TrebuchetKillsMin = 2;
        private const int TrebuchetKillsMax = 3;

        /// <summary>Whether a defending catapult spends the siege on the besieger's equipment or on his men.
        /// Decided once, when the engine is first seen, and held. An even split: both are sound choices and a
        /// garrison that made the same one every time would be readable.</summary>
        private const float DefenderEngineTargetingChance = 0.5f;

        /// <summary>How often an engine on the wall, shooting at men during the ASSAULT, hits nothing worth
        /// counting. It is dropping stones near a fight its own men are in, and it has to be careful. On the
        /// approach there is no such roll -- a column crossing open ground cannot be missed by a mangonel.</summary>
        private const float AssaultUnitFireMissChance = 0.35f;

        /// <summary>Vanilla's own choice for a lethal simulated blow (see MapEvent.SimulateSingleTroopHit, which
        /// picks Blunt or Cut). Cut, always, and deliberately: the surgeon's roll treats Blunt as never fatal, so
        /// an engine casualty booked as Blunt could not die under any circumstances -- and a ballista bolt through
        /// a man is not a bruise. Dead or merely down is still the game's call, exactly as for any other
        /// casualty.</summary>
        private const DamageTypes EngineDamageType = DamageTypes.Cut;

        /// <summary>Damage large enough that any wound pool in the game is worn straight through by it, which is
        /// what "the ballista kills what it hits" means in a model where everyone has hit points. Deliberately
        /// finite: SimulationTroopHitPoints ADDS this to the man's ledger, and int.MaxValue would overflow it.</summary>
        private const int InstantKillDamage = 100000;

        // =========================================================================================================
        // Reaching into the game.

        /// <summary>
        /// Vanilla's casualty path, which is internal. Going through it rather than around it is the whole point:
        /// it is what books the kill against the right party, asks the surgeon whether the man died or was merely
        /// carried off, awards the surgery experience, and tells the battle observer. It also runs RBM's own wound
        /// pool (SimulationTroopHitPoints patches this very method), so an engine's damage accumulates on a man
        /// exactly as a sword's does.
        /// </summary>
        private static readonly MethodInfo ApplyDamage = AccessTools.Method(
            typeof(MapEventSide), "ApplySimulationDamageToSelectedTroop",
            new Type[] { typeof(int), typeof(DamageTypes), typeof(PartyBase) });

        // =========================================================================================================
        // The volley.

        /// <summary>
        /// Every engine on both sides fires once, at the top of the round. Called from the tick allocation, after
        /// the round has turned and the phase is settled.
        ///
        /// Firing HERE, before the round's blows rather than after them, is safe and is checked: MapEvent.
        /// SimulateBattleRound opens by calling CalculateWinner, so a side the artillery has just wiped out ends
        /// the battle cleanly instead of being asked for a soldier it no longer has.
        /// </summary>
        internal static void Fire(MapEvent mapEvent, SimulationBattleState.BattleState state)
        {
            if (mapEvent == null || state == null || !state.SiegeAssaultBattle || ApplyDamage == null)
            {
                return;
            }
            // A battle already decided, or one whose storm was called off, has nothing left to bombard.
            if (mapEvent.BattleState != BattleState.None || SimulationSiege.Repulsed(state))
            {
                return;
            }

            Settlement settlement = mapEvent.MapEventSettlement;
            if (settlement == null || settlement.Party == null)
            {
                return;
            }
            SiegeEvent siegeEvent = settlement.Party.SiegeEvent;
            if (siegeEvent == null)
            {
                return;
            }

            ISiegeEventSide attackerEngines = siegeEvent.GetSiegeEventSide(BattleSideEnum.Attacker);
            ISiegeEventSide defenderEngines = siegeEvent.GetSiegeEventSide(BattleSideEnum.Defender);

            // Nothing an engine does may be mistaken for a blow. Two systems are listening for one: the arm-aware
            // selector brackets a pick between its own two calls, and the siege width is holding the last melee
            // blow waiting for the game's verdict on it. An engine picking a target would walk into both -- the
            // selector would re-pick our man by phase, and the width would step on a casualty struck by a machine.
            // Both are stood down for the length of the volley.
            SimulationArmTargeting.Disarm();
            SimulationSiege.ClearPendingBlow();
            // ...and the log's own parked record, for the same reason. It should already be null (the verdict hook
            // consumes it a breath after every blow is written), but an engine casualty writing its verdict into
            // some swordsman's row would be a lie told in the one place the battle is supposed to be readable.
            SimulationBattleState.LastHit = null;

            FireSide(mapEvent, state, defenderEngines, attackerEngines, defender: true);
            FireSide(mapEvent, state, attackerEngines, defenderEngines, defender: false);

            SimulationSiege.ClearPendingBlow();
        }

        private static void FireSide(MapEvent mapEvent, SimulationBattleState.BattleState state,
            ISiegeEventSide own, ISiegeEventSide enemy, bool defender)
        {
            if (own == null || own.SiegeEngines == null)
            {
                return;
            }
            SiegeEvent.SiegeEngineConstructionProgress[] slots = own.SiegeEngines.DeployedRangedSiegeEngines;
            if (slots == null)
            {
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                SiegeEvent.SiegeEngineConstructionProgress engine = slots[i];
                if (engine == null || !engine.IsActive || engine.Hitpoints <= 0f)
                {
                    continue;
                }
                FireEngine(mapEvent, state, engine, enemy, defender);
            }
        }

        private static void FireEngine(MapEvent mapEvent, SimulationBattleState.BattleState state,
            SiegeEvent.SiegeEngineConstructionProgress engine, ISiegeEventSide enemy, bool defender)
        {
            int kind = KindOf(engine.SiegeEngine);
            if (kind == KindNone)
            {
                return;
            }

            // The men this engine is shooting at, and the men serving it.
            MapEventSide target = defender ? mapEvent.AttackerSide : mapEvent.DefenderSide;
            MapEventSide owner = defender ? mapEvent.DefenderSide : mapEvent.AttackerSide;
            if (target == null)
            {
                return;
            }

            bool assault = SimulationSiege.IsAssault(state);

            // THE BOLT-THROWER, and it is the same weapon on either side of the wall. It shoots at men and only at
            // men -- a bolt does nothing to a timber frame -- and what it finds, it ruins.
            if (kind == KindBallista)
            {
                FireBallista(state, engine, target, owner, defender, assault);
                return;
            }

            // AND IN THE ASSAULT A CATAPULT IS SLOW. Every catapult, on either side and whatever it is shooting at:
            // once the ladders are up, nothing near the wall is standing still to be ranged on any more.
            //
            // The ballista is exempt because it already returned above -- a bolt-thrower is spanned by two men in
            // the time a mangonel's arm is winched once. The trebuchet is exempt because it has its own interval
            // below and is already slower than this; folding it in here would be harmless today (both are two) but
            // would quietly couple two numbers that mean different things. See AssaultCatapultRoundInterval.
            int interval = (kind == KindTrebuchet) ? TrebuchetRoundInterval
                : (assault ? AssaultCatapultRoundInterval : 1);
            if ((state.Round % interval) != 0)
            {
                return;
            }

            // AND THE PILE OF STONE RUNS OUT. Asked here, once, above every path a heavy engine can take -- at men,
            // at engines, catapult or trebuchet -- so there is exactly one place a shot is counted and exactly one
            // place it can be refused. It sits BELOW the rate gates and above everything else, which is the only
            // correct spot: a round the engine was never going to fire in must not cost it a stone, and a shot that
            // goes wide must.
            if (!SpendShot(state, engine, HeavyEngineAmmo))
            {
                return;
            }

            // A DEFENDING CATAPULT MAY BE WORKING ON MEN, and once the ladders are up every engine on the wall is:
            // when the enemy is on the parapet the timber no longer matters and the men on it do. The besieger's
            // heavy engines never make this choice -- they exist to break the wall's engines and they work at that
            // the whole battle through.
            if (defender && (assault || !TargetsEngines(state, engine)))
            {
                FireAtMen(state, engine, target, owner, kind, assault);
                return;
            }

            // Otherwise it is shooting at the enemy's equipment, and what it kills among his men it kills
            // incidentally. The three kinds differ only in what that incident looks like.
            float damageBonus = 1f;
            float killChance = 0f;
            int killsMin = 0;
            int killsMax = 0;
            int splash = 0;

            if (kind == KindTrebuchet)
            {
                // It hits timber harder than anything else on the field, and the rock it drops is big enough that
                // wherever it lands takes two or three men with it. (Its rate was settled above, with every other
                // engine's, so that no engine can be charged a stone for a round it was never going to fire in.)
                damageBonus = TrebuchetEngineDamageBonus;
                killChance = TrebuchetCollateralChance;
                killsMin = TrebuchetKillsMin;
                killsMax = TrebuchetKillsMax;
            }
            else if (kind == KindPotCatapult)
            {
                // A pot bursts wherever it comes down, and a burst pot is a burst pot whichever way it was thrown:
                // the man it lands on is finished, and several around him are badly burned. The kill is certain
                // rather than rolled, exactly as it is for the defender's pot -- what differs between the two is
                // only how many the flames catch, because this one was aimed at a machine and not at a crowd.
                killChance = 1f;
                killsMin = PotCatapultKills;
                killsMax = PotCatapultKills;
                splash = MBRandom.RandomInt(AttackerPotSplashMin, AttackerPotSplashMax + 1);
            }
            else
            {
                // A stone. Sometimes it comes down on somebody.
                killChance = AttackerStoneCollateralChance;
                killsMin = 1;
                killsMax = 1;
            }

            FireAtEngines(state, engine, enemy, target, owner, defender,
                damageBonus, killChance, killsMin, killsMax, splash);
        }

        // =========================================================================================================
        // Shooting at men.

        /// <summary>
        /// The ballista. One or two bolts, each rolled against the engine's own anti-personnel accuracy (the data's
        /// <c>anti_personnel_hit_chance</c> -- a quarter, for a vanilla ballista), and every bolt that arrives
        /// takes a man out of the battle. It is the one engine that is no better on the wall than off it, beyond
        /// the besieger's general handicap: a bolt-thrower is a bolt-thrower.
        /// </summary>
        private static void FireBallista(SimulationBattleState.BattleState state,
            SiegeEvent.SiegeEngineConstructionProgress engine,
            MapEventSide target, MapEventSide owner, bool defender, bool assault)
        {
            float hitChance = AntiPersonnelChance(engine.SiegeEngine, defender);
            if (assault && defender)
            {
                hitChance *= 1f - AssaultUnitFireMissChance;
            }

            int shots = MBRandom.RandomInt(BallistaShotsMin, BallistaShotsMax + 1);
            for (int i = 0; i < shots; i++)
            {
                // The sheaf runs out too. Spent per BOLT, not per round -- a ballista that looses twice in a round
                // has spent two of them, and a crew that has shot its last cannot loose the second half of a volley.
                if (!SpendShot(state, engine, BallistaAmmo))
                {
                    return;
                }

                bool hit = MBRandom.RandomFloat < hitChance;
                int outcome = hit
                    ? WoundOne(state, target, owner, BallistaDamageMin, BallistaDamageMax)
                    : StruckNobody;

                // Every bolt goes in the book, the ones that went wide included -- the hit rate is the only thing
                // an accuracy figure can ever be calibrated against.
                ArtilleryRecord shot = Record(state, engine, !defender, "men", hit);
                if (shot != null)
                {
                    if (outcome == StruckAndDown)
                    {
                        shot.Killed = 1;
                    }
                    else if (outcome == StruckAndStanding)
                    {
                        shot.Wounded = 1;
                    }
                }
            }
        }

        /// <summary>
        /// A catapult working on the men rather than on the equipment -- which, by the rules above, is only ever a
        /// DEFENDER's catapult. A stone takes two or three at a stroke; a pot kills one and burns ten.
        ///
        /// No accuracy roll on the approach, and that is deliberate rather than an oversight: the target is a
        /// column of men crossing open ground toward a fixed point that the crew has been ranging on for days, and
        /// a mangonel does not miss that. Once the assault begins it can, because now it is dropping rocks near a
        /// fight its own men are in.
        /// </summary>
        private static void FireAtMen(SimulationBattleState.BattleState state,
            SiegeEvent.SiegeEngineConstructionProgress engine, MapEventSide target,
            MapEventSide owner, int kind, bool assault)
        {
            if (assault && MBRandom.RandomFloat < AssaultUnitFireMissChance)
            {
                // Only a defender's engine ever shoots at men, so the shot that went wide is always the wall's.
                Record(state, engine, firedByAttacker: false, target: "men", hit: false);
                return;
            }

            ArtilleryRecord shot = Record(state, engine, firedByAttacker: false, target: "men", hit: true);

            if (kind == KindPotCatapult)
            {
                for (int i = 0; i < PotCatapultKills; i++)
                {
                    KillOne(target, owner);
                }
                int burned = Scorch(state, target, owner, PotCatapultSplashTargets);
                if (shot != null)
                {
                    shot.Killed = PotCatapultKills;
                    shot.Wounded = burned;
                }
                return;
            }

            int kills = MBRandom.RandomInt(StoneCatapultKillsMin, StoneCatapultKillsMax + 1);
            for (int i = 0; i < kills; i++)
            {
                KillOne(target, owner);
            }
            if (shot != null)
            {
                shot.Killed = kills;
            }
        }

        // =========================================================================================================
        // Shooting at engines.

        /// <summary>
        /// Counter-battery, and wall-breaking, and ram-smashing: one shot at one of the enemy's surviving engines,
        /// rolled against the data's own hit chance, and on a hit the data's own damage. An engine worn to nothing
        /// is removed from the campaign's siege event there and then -- see <see cref="Destroy"/>.
        ///
        /// The collateral is the other half of the shot and is rolled SEPARATELY from the hit: a stone that misses
        /// a mangonel does not vanish, it lands somewhere, and somewhere is full of people. That is why a besieger's
        /// heavy engines kill men at all in this model despite never aiming at one.
        /// </summary>
        private static void FireAtEngines(SimulationBattleState.BattleState state,
            SiegeEvent.SiegeEngineConstructionProgress engine, ISiegeEventSide enemy, MapEventSide target,
            MapEventSide owner, bool defender, float damageBonus, float collateralChance,
            int collateralKillsMin, int collateralKillsMax, int collateralSplash)
        {
            int index;
            bool ranged;
            SiegeEvent.SiegeEngineConstructionProgress victim = PickEnemyEngine(enemy, out index, out ranged);

            ArtilleryRecord shot = Record(state, engine, !defender, "engine", hit: false);
            if (shot != null && victim != null)
            {
                shot.TargetEngine = Named(victim.SiegeEngine);
            }

            if (victim != null)
            {
                float hitChance = engine.SiegeEngine.HitChance;
                if (!defender)
                {
                    hitChance *= AttackerHitChanceFactor;
                }
                if (MBRandom.RandomFloat < hitChance)
                {
                    float damage = engine.SiegeEngine.Damage * damageBonus;
                    float left = victim.Hitpoints - damage;
                    if (shot != null)
                    {
                        shot.Hit = true;
                        shot.EngineDamage = damage;
                    }
                    if (left <= 0f)
                    {
                        Destroy(enemy, index, ranged);
                        if (shot != null)
                        {
                            shot.Destroyed = true;
                        }
                    }
                    else
                    {
                        victim.SetHitpoints(left);
                    }
                }
            }

            // ...and wherever the shot came down.
            if (collateralChance > 0f && MBRandom.RandomFloat < collateralChance)
            {
                int kills = MBRandom.RandomInt(collateralKillsMin, collateralKillsMax + 1);
                for (int i = 0; i < kills; i++)
                {
                    KillOne(target, owner);
                }
                if (shot != null)
                {
                    shot.Killed = kills;
                }
            }
            if (collateralSplash > 0)
            {
                int burned = Scorch(state, target, owner, collateralSplash);
                if (shot != null)
                {
                    shot.Wounded = burned;
                }
            }
        }

        /// <summary>
        /// One of the enemy's surviving engines, chosen at random from ALL of them -- the rams and towers that will
        /// open the wall and the catapults that are shooting back, with no preference between them. A crew serves
        /// the target it has been given and the officer giving it has his own reasons.
        ///
        /// Both arrays are walked because both matter: breaking a ram narrows the assault that follows (the widths
        /// are read from the survivors -- see SimulationSiege.Widths), while breaking a mangonel stops the stones
        /// coming. The defender's melee array is empty by construction, so a besieger shooting "at any engine" is
        /// automatically shooting counter-battery, which is the right answer with no special case for it.
        /// </summary>
        private static SiegeEvent.SiegeEngineConstructionProgress PickEnemyEngine(ISiegeEventSide enemy,
            out int index, out bool ranged)
        {
            index = -1;
            ranged = false;
            if (enemy == null || enemy.SiegeEngines == null)
            {
                return null;
            }

            // Reservoir-sampled in one pass over both arrays, so every live engine is equally likely and neither
            // array needs a list built for it every round of every siege in the campaign.
            SiegeEvent.SiegeEngineConstructionProgress chosen = null;
            int seen = 0;

            SiegeEvent.SiegeEngineConstructionProgress[] rangedSlots = enemy.SiegeEngines.DeployedRangedSiegeEngines;
            if (rangedSlots != null)
            {
                for (int i = 0; i < rangedSlots.Length; i++)
                {
                    SiegeEvent.SiegeEngineConstructionProgress candidate = rangedSlots[i];
                    if (candidate == null || !candidate.IsActive || candidate.Hitpoints <= 0f)
                    {
                        continue;
                    }
                    seen++;
                    if (MBRandom.RandomInt(seen) == 0)
                    {
                        chosen = candidate;
                        index = i;
                        ranged = true;
                    }
                }
            }

            SiegeEvent.SiegeEngineConstructionProgress[] meleeSlots = enemy.SiegeEngines.DeployedMeleeSiegeEngines;
            if (meleeSlots != null)
            {
                for (int i = 0; i < meleeSlots.Length; i++)
                {
                    SiegeEvent.SiegeEngineConstructionProgress candidate = meleeSlots[i];
                    if (candidate == null || !candidate.IsActive || candidate.Hitpoints <= 0f)
                    {
                        continue;
                    }
                    seen++;
                    if (MBRandom.RandomInt(seen) == 0)
                    {
                        chosen = candidate;
                        index = i;
                        ranged = false;
                    }
                }
            }

            return chosen;
        }

        /// <summary>
        /// Break an engine, for good and for the campaign -- not merely for this battle. It leaves its deployment
        /// slot exactly as native's own post-mission bookkeeping leaves a broken one
        /// (<c>SiegeEvent.SetSiegeEngineStatesAfterSiegeMission</c> takes the same road), so a ram lost to a
        /// mangonel is a ram the besieger has to build again before he can storm the gate.
        ///
        /// Removed by SLOT rather than by type, which matters when a side fields two of a kind: native's own
        /// BreakSiegeEngine takes a SiegeEngineType and breaks whichever one it finds first, which would leave the
        /// engine this shot actually hit standing and kill its twin instead.
        ///
        /// The visual-dirty flag is NOT optional. RemoveDeployedSiegeEngine only nulls the slot; every native
        /// caller (DoSiegeAction, BreakSiegeEngine, MapSiegeProductionVM) pairs it with SetVisualAsDirty, and
        /// SettlementVisual.Tick relies on that: it reads
        /// <c>SiegeEvent.GetSiegeEventSide(side).SiegeEngines.DeployedRangedSiegeEngines[slot].SiegeEngine</c>
        /// for every entity still in its cached _siegeRangedMachineEntities list, with no null check, on a
        /// parallel worker thread. Nulling a slot without dirtying the visual is a guaranteed
        /// NullReferenceException in SettlementVisualManager.OnTick.
        /// </summary>
        private static void Destroy(ISiegeEventSide enemy, int index, bool ranged)
        {
            if (enemy == null || enemy.SiegeEngines == null || index < 0)
            {
                return;
            }
            enemy.SiegeEngines.RemoveDeployedSiegeEngine(index, ranged, moveToReserve: false);

            SiegeEvent siegeEvent = enemy.SiegeEvent;
            if (siegeEvent != null && siegeEvent.BesiegedSettlement != null &&
                siegeEvent.BesiegedSettlement.Party != null)
            {
                siegeEvent.BesiegedSettlement.Party.SetVisualAsDirty();
            }
        }

        // =========================================================================================================
        // Casualties.

        /// <summary>Take one man out of the battle, through the game's own casualty path. Whether he is dead or
        /// carried off wounded is the surgeon's business, exactly as it is for a man cut down with a sword.</summary>
        private static void KillOne(MapEventSide target, MapEventSide owner)
        {
            if (target.NumRemainingSimulationTroops <= 0)
            {
                return;
            }
            target.SelectRandomSimulationTroop();
            Apply(target, owner, InstantKillDamage);
        }

        /// <summary>Burn a scattering of men around where the pot burst -- a heavy wound apiece, priced against
        /// each man's own pool so armour still counts for something. Returns how many were actually caught, which
        /// is fewer than asked for when there are fewer men left than the pot would have reached.</summary>
        private static int Scorch(SimulationBattleState.BattleState state, MapEventSide target, MapEventSide owner,
            int count)
        {
            int burned = 0;
            for (int i = 0; i < count; i++)
            {
                if (WoundOne(state, target, owner, SplashDamageMin, SplashDamageMax) != StruckNobody)
                {
                    burned++;
                }
            }
            return burned;
        }

        // What became of a man this engine reached. Three outcomes, because the log wants to tell a corpse from a
        // casualty from a shot that found nobody at all.
        private const int StruckNobody = 0;
        private const int StruckAndStanding = 1;
        private const int StruckAndDown = 2;

        /// <summary>
        /// Put a heavy wound on one man taken at random, priced as a share of HIS OWN pool -- so the same stone or
        /// bolt that finishes a levy leaves an armoured man on his feet, which is the whole point of pricing a blow
        /// against the man rather than flatly. Wounds accumulate in the pool, so a second one usually finishes what
        /// the first began.
        /// </summary>
        private static int WoundOne(SimulationBattleState.BattleState state, MapEventSide target,
            MapEventSide owner, float shareMin, float shareMax)
        {
            if (target.NumRemainingSimulationTroops <= 0)
            {
                return StruckNobody;
            }

            target.SelectRandomSimulationTroop();
            CharacterObject troop = SelectedTroop(target);
            if (troop == null)
            {
                return StruckNobody;
            }

            PartyBase party = target.GetAllocatedTroopParty(SelectedDescriptor(target));
            float pool = SimulationTroopHitPoints.MaxHitPoints(troop, party, state != null && state.Dismounted);
            if (pool <= 0f)
            {
                return StruckNobody;
            }

            float share = MBRandom.RandomFloatRanged(shareMin, shareMax);
            bool downed = Apply(target, owner, Math.Max(1, MathF.Round(pool * share)));
            return downed ? StruckAndDown : StruckAndStanding;
        }

        /// <summary>
        /// The call itself, on whichever man is currently selected. Returns whether it put him down -- the game's
        /// own verdict, which is what lets the log tell a kill from a wound without re-deriving either.
        ///
        /// Anything thrown by the game's own path is swallowed rather than allowed out of a Harmony postfix and
        /// into the campaign's simulation loop, where it would end the battle mid-round.
        /// </summary>
        private static bool Apply(MapEventSide target, MapEventSide owner, int damage)
        {
            try
            {
                PartyBase striker = (owner != null) ? owner.LeaderParty : null;
                object result = ApplyDamage.Invoke(target, new object[] { damage, EngineDamageType, striker });
                return result is bool && (bool)result;
            }
            catch
            {
                // Nothing to salvage and nothing to say: the man simply was not hit.
                return false;
            }
        }

        private static readonly AccessTools.FieldRef<MapEventSide, CharacterObject> SelectedTroopRef =
            AccessTools.FieldRefAccess<MapEventSide, CharacterObject>("_selectedSimulationTroop");

        private static readonly AccessTools.FieldRef<MapEventSide, UniqueTroopDescriptor> SelectedDescriptorRef =
            AccessTools.FieldRefAccess<MapEventSide, UniqueTroopDescriptor>("_selectedSimulationTroopDescriptor");

        private static CharacterObject SelectedTroop(MapEventSide side)
        {
            return SelectedTroopRef(side);
        }

        private static UniqueTroopDescriptor SelectedDescriptor(MapEventSide side)
        {
            return SelectedDescriptorRef(side);
        }

        // =========================================================================================================
        // The book.

        /// <summary>
        /// Write a shot down. Gated on the hit log exactly as RecordHit is -- and returning null when it is off, so
        /// every caller's "fill in what it did" is skipped with it and a battle nobody is watching costs nothing.
        ///
        /// A shot that MISSED is recorded too, and that is the point of recording at all: with only the hits in the
        /// book an engine's accuracy cannot be read back, and accuracy is most of what separates the wall's
        /// artillery from the besieger's.
        /// </summary>
        private static ArtilleryRecord Record(SimulationBattleState.BattleState state,
            SiegeEvent.SiegeEngineConstructionProgress engine, bool firedByAttacker, string target, bool hit)
        {
            if (state == null || !SimulationLog.IsEnabled || !RBMConfig.RBMConfig.simulationLogHits)
            {
                return null;
            }

            ArtilleryRecord shot = new ArtilleryRecord();
            shot.Round = state.Round;
            shot.FiredByAttacker = firedByAttacker;
            shot.Engine = Named(engine.SiegeEngine);
            shot.Target = target;
            shot.Hit = hit;
            state.Artillery.Add(shot);
            return shot;
        }

        /// <summary>An engine's id -- "fire_onager", "trebuchet". The id and not the display name: it is shorter,
        /// it is what RBM's own siege XML is keyed on, and it is what somebody reading the log will grep for.</summary>
        private static string Named(SiegeEngineType type)
        {
            return (type != null) ? type.StringId : "-";
        }

        // =========================================================================================================
        // What a defending catapult decided to do with its siege.

        /// <summary>
        /// Whether this engine is working on the besieger's equipment or on his men. Decided the first time the
        /// engine is asked and remembered for the rest of the battle, keyed on the engine itself -- so two
        /// mangonels on the same wall may well be doing different jobs, which is what a garrison commander with two
        /// mangonels would actually arrange.
        /// </summary>
        private static bool TargetsEngines(SimulationBattleState.BattleState state,
            SiegeEvent.SiegeEngineConstructionProgress engine)
        {
            Dictionary<SiegeEvent.SiegeEngineConstructionProgress, bool> orders = state.SiegeEngineOrders;
            bool atEngines;
            if (!orders.TryGetValue(engine, out atEngines))
            {
                atEngines = MBRandom.RandomFloat < DefenderEngineTargetingChance;
                orders[engine] = atEngines;
            }
            return atEngines;
        }

        /// <summary>
        /// Take one round of ammunition off this engine, and say whether there was one to take. One counter serves
        /// every engine in the siege; only the limit differs by what the engine throws.
        /// </summary>
        private static bool SpendShot(SimulationBattleState.BattleState state,
            SiegeEvent.SiegeEngineConstructionProgress engine, int limit)
        {
            Dictionary<SiegeEvent.SiegeEngineConstructionProgress, int> shots = state.SiegeEngineShots;
            int spent;
            shots.TryGetValue(engine, out spent);
            if (spent >= limit)
            {
                return false;
            }
            shots[engine] = spent + 1;
            return true;
        }

        /// <summary>An engine's anti-personnel accuracy, from the data, with the besieger's handicap on it. An
        /// engine the data says is not anti-personnel at all reports zero there, so it falls back to its ordinary
        /// hit chance rather than being unable to hit a man it is aimed at.</summary>
        private static float AntiPersonnelChance(SiegeEngineType type, bool defender)
        {
            float chance = type.IsAntiPersonnel ? type.AntiPersonnelHitChance : type.HitChance;
            if (chance <= 0f)
            {
                chance = type.HitChance;
            }
            if (!defender)
            {
                chance *= AttackerHitChanceFactor;
            }
            return MBMath.ClampFloat(chance, 0f, 1f);
        }
    }
}
