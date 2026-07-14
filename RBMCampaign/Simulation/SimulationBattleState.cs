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
        private static void Postfix(MapEvent mapEvent)
        {
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
    internal static class SimulationBattleState
    {
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
        /// </summary>
        private const int AmmoRounds = 14;

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
        private const float ShieldCapacityPerMan = 25f;

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

            public readonly Dictionary<CharacterObject, TroopState> Attackers = new Dictionary<CharacterObject, TroopState>();

            public readonly Dictionary<CharacterObject, TroopState> Defenders = new Dictionary<CharacterObject, TroopState>();

            /// <summary>The muster roll: how many of each troop stand on each side, over all its parties.</summary>
            public Dictionary<CharacterObject, int> AttackerCounts = new Dictionary<CharacterObject, int>();

            public Dictionary<CharacterObject, int> DefenderCounts = new Dictionary<CharacterObject, int>();

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
                state.AttackerCounts = Muster(mapEvent.AttackerSide);
                state.DefenderCounts = Muster(mapEvent.DefenderSide);
                _battles[mapEvent] = state;
            }
            return state;
        }

        /// <summary>
        /// A fresh battle with nothing spent yet, for the shadow replay -- which fights the same battle twenty times
        /// over and must start each one with full quivers, whole shields and living horses. It cannot key off the
        /// MapEvent like the real battle does: the real battle IS the MapEvent, and there is only one of it.
        /// </summary>
        internal static BattleState CreateDetached(int volleyRounds, bool defendersShootFromStores,
            Dictionary<CharacterObject, int> attackerCounts, Dictionary<CharacterObject, int> defenderCounts)
        {
            BattleState state = new BattleState();
            state.VolleyRounds = volleyRounds;
            state.DefendersShootFromStores = defendersShootFromStores;
            state.AttackerCounts = attackerCounts;
            state.DefenderCounts = defenderCounts;
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

        /// <summary>A battle is over; let it go, or the campaign will carry every fight it ever fought.</summary>
        internal static void Forget(MapEvent mapEvent)
        {
            if (mapEvent != null)
            {
                _battles.Remove(mapEvent);
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

                // A lord's armour and training are fixed for the length of a battle but not between battles, so his
                // kit is re-measured here, once, rather than on every blow he throws.
                SimulationEquipmentPower.ForgetHeroKits();

                SimulationShadow.Recapture(mapEvent);
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

        /// <summary>The same question asked of a snapshot rather than a live battle, for the shadow replay.</summary>
        internal static int GetVolleyRounds(MapEvent.PowerCalculationContext context, bool isSiegeAssault)
        {
            if (isSiegeAssault)
            {
                return 5;
            }

            switch (context)
            {
                case MapEvent.PowerCalculationContext.ForestBattle:
                    return 1;

                case MapEvent.PowerCalculationContext.RiverCrossingBattle:
                case MapEvent.PowerCalculationContext.SeaBattle:
                case MapEvent.PowerCalculationContext.OpenSeaBattle:
                case MapEvent.PowerCalculationContext.RiverBattle:
                    return 2;

                case MapEvent.PowerCalculationContext.Village:
                case MapEvent.PowerCalculationContext.NavalRaid:
                    return 2;

                case MapEvent.PowerCalculationContext.Siege:
                    return 4;

                default:
                    // Plain, steppe, desert, dune, snow: open ground, and a long walk into the arrows.
                    return 4;
            }
        }

        internal static bool IsVolleyPhase(BattleState state)
        {
            // The round counter is advanced when the round's blows are handed out, so it is 1 during the first
            // round's fighting -- hence <=, not <.
            return state != null && state.Round <= state.VolleyRounds;
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
            return battle.Round <= (int)MathF.Ceiling(thrownPerMan);
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
