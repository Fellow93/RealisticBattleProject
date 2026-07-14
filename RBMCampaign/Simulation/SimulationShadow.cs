using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// A battle cannot be fought twice: the real one mutates the rosters it resolves, so there is no way to ask
    /// what would have happened without our changes by simply running it again. This replays it instead -- from
    /// the same starting rosters, many times over, once with the equipment model and once without -- so the two
    /// can be set side by side and the model's actual effect on a battle read off rather than guessed at.
    ///
    /// The replay is a faithful copy of vanilla's own loop (MapEvent.SimulateBattleRound and the pieces it
    /// calls), and it leans on the game's real power model for troop strength, so the numbers are the game's
    /// and not a second invention. Three things it does NOT reproduce, and which therefore make its absolute
    /// results a little cleaner than a real battle's:
    ///   - the Tactics and Scouting perks that adjust each blow (they need live parties, not a roster snapshot),
    ///   - morale drift during the battle (side morale is frozen at what it was when the battle opened),
    ///   - the wounded-versus-killed split (a downed man is simply counted a casualty).
    /// None of that matters for the comparison, which is the point: both replays are wrong in exactly the same
    /// way, so what differs between them is the equipment model and nothing else.
    /// </summary>
    internal static class SimulationShadow
    {
        // A battle that somehow cannot resolve must not hang the campaign thread.
        private const int MaxRounds = 500;


        /// <summary>
        /// One man in the line, and the lord whose banner he stands under. The lord matters: vanilla passes the
        /// STRIKER'S OWN party into SimulateHit, so a captain's power modifier reaches his own men and nobody
        /// else's. A side is very often several parties -- a lord, his allies, a militia, a caravan swept up in it
        /// -- and taking the leading party's hero as the leader of all of them lent his bonus to troops he has
        /// never met.
        /// </summary>
        internal struct Soldier
        {
            public CharacterObject Character;

            public float LeaderModifier;

            /// <summary>
            /// What this man has soaked so far -- EVERY man, since SimulationTroopHitPoints now gives a line trooper
            /// the health a lord has always had. The replay must model the same battle the game fights, or the A/B
            /// numbers describe a battle nobody had.
            /// </summary>
            public float Damage;

            /// <summary>The company he marches with, whose lord's perks decide how much he can take.</summary>
            public PartyBase Party;
        }

        /// <summary>One party in the line of battle, for the log to name rather than lump into its neighbour.</summary>
        internal class PartyLine
        {
            public string Name;

            /// <summary>Men who can fight. This is the roster the battle is actually decided by.</summary>
            public int Count;

            /// <summary>
            /// And men who cannot. They are printed because a replay of "one looter" against a battle that killed
            /// nine of them is either a party that was already beaten half to death -- in which case the eight
            /// wounded are the missing nine and nothing is wrong -- or it is a roster we are failing to read. The
            /// two look identical in the log until the wounded are shown, so they are shown.
            /// </summary>
            public int Wounded;
        }

        /// <summary>Everything about a battle needed to fight it again, taken before the first blow lands.</summary>
        internal class BattleSnapshot
        {
            public List<Soldier> AttackerTroops = new List<Soldier>();

            public List<Soldier> DefenderTroops = new List<Soldier>();

            /// <summary>Every party on each side, named and counted. A battle is rarely one party against one party.</summary>
            public List<PartyLine> AttackerParties = new List<PartyLine>();

            public List<PartyLine> DefenderParties = new List<PartyLine>();

            public float AttackerAdvantage = 1f;

            public float DefenderAdvantage = 1f;

            public float AttackerMorale = 50f;

            public float DefenderMorale = 50f;

            public MapEvent.PowerCalculationContext Context;

            public bool IsSiegeAssault;

            public float SettlementAdvantage = 1f;

            public string AttackerName = "attacker";

            public string DefenderName = "defender";

            public string BattleType = "battle";

            public bool IsPlayerBattle;

            public int AttackerCount { get { return AttackerTroops.Count; } }

            public int DefenderCount { get { return DefenderTroops.Count; } }
        }

        /// <summary>The averaged outcome of replaying one battle many times.</summary>
        internal struct ShadowResult
        {
            public float AttackerWinRate;

            public float AttackerCasualties;

            public float DefenderCasualties;

            public float AttackerSurvivors;

            public float DefenderSurvivors;
        }

        // Snapshots are held from the moment a battle can first be seen whole until it ends. Keyed by the event
        // itself, since a battle carries no id of its own and several can run at once across the map.
        private static readonly Dictionary<MapEvent, BattleSnapshot> _snapshots = new Dictionary<MapEvent, BattleSnapshot>();

        /// <summary>
        /// Take the battle's picture, replacing any earlier one.
        ///
        /// This is called at the top of the FIRST simulated round, and that timing is the whole point. MapEventStarted
        /// fires at the end of StartBattle -- before a lord's allies and the rest of his army have attached themselves
        /// to the event, which they do afterwards by claiming a MapEventSide as they arrive. Snapshot there and a
        /// two-party army is recorded as one party, which is exactly what the log was doing. By the first round
        /// everyone who is coming has come, and not a blow has landed, so the rosters are still the opening rosters.
        /// </summary>
        internal static void Recapture(MapEvent mapEvent)
        {
            if (mapEvent == null || !SimulationLog.IsEnabled)
            {
                return;
            }
            BattleSnapshot snapshot = Capture(mapEvent);
            if (snapshot != null && snapshot.AttackerCount > 0 && snapshot.DefenderCount > 0)
            {
                _snapshots[mapEvent] = snapshot;
            }
        }

        /// <summary>Take the battle's picture only if none has been taken -- the fallback for a battle the player fights himself, which never simulates a round.</summary>
        internal static void CaptureIfAbsent(MapEvent mapEvent)
        {
            if (mapEvent == null || _snapshots.ContainsKey(mapEvent))
            {
                return;
            }
            Recapture(mapEvent);
        }

        /// <summary>The battle's picture, forgotten as it is handed over. Null if it was never taken.</summary>
        internal static BattleSnapshot Take(MapEvent mapEvent)
        {
            BattleSnapshot snapshot;
            if (mapEvent == null || !_snapshots.TryGetValue(mapEvent, out snapshot))
            {
                return null;
            }
            _snapshots.Remove(mapEvent);
            return snapshot;
        }

        /// <summary>Take everything from a live map event that is needed to fight it again later.</summary>
        internal static BattleSnapshot Capture(MapEvent mapEvent)
        {
            if (mapEvent == null)
            {
                return null;
            }

            BattleSnapshot snapshot = new BattleSnapshot();
            snapshot.BattleType = mapEvent.EventType.ToString();
            snapshot.IsSiegeAssault = mapEvent.IsSiegeAssault;
            snapshot.Context = mapEvent.SimulationContext;
            snapshot.IsPlayerBattle = mapEvent.IsPlayerMapEvent;

            Flatten(mapEvent.AttackerSide, snapshot.AttackerTroops, snapshot.AttackerParties);
            Flatten(mapEvent.DefenderSide, snapshot.DefenderTroops, snapshot.DefenderParties);

            snapshot.AttackerName = Describe(mapEvent.AttackerSide);
            snapshot.DefenderName = Describe(mapEvent.DefenderSide);

            try
            {
                ExplainedNumber defenderAdvantage;
                ExplainedNumber attackerAdvantage;
                Campaign.Current.Models.CombatSimulationModel.GetBattleAdvantage(mapEvent, out defenderAdvantage, out attackerAdvantage);
                snapshot.AttackerAdvantage = attackerAdvantage.ResultNumber;
                snapshot.DefenderAdvantage = defenderAdvantage.ResultNumber;

                snapshot.AttackerMorale = mapEvent.AttackerSide.GetSideMorale();
                snapshot.DefenderMorale = mapEvent.DefenderSide.GetSideMorale();

                Settlement settlement = mapEvent.MapEventSettlement;
                if (settlement != null)
                {
                    snapshot.SettlementAdvantage = Campaign.Current.Models.CombatSimulationModel.GetSettlementAdvantage(settlement);
                }
            }
            catch
            {
                // A snapshot is a diagnostic, never a reason to break a battle. Defaults stand.
            }

            return snapshot;
        }

        /// <summary>
        /// Every man on a side, from every party standing on it -- each carrying the power modifier of HIS OWN
        /// lord, not of whoever happens to be leading the side. A side is very often several parties, and vanilla
        /// hands the striker's own party to SimulateHit precisely so that a captain's bonus reaches his own men.
        /// </summary>
        private static void Flatten(MapEventSide side, List<Soldier> into, List<PartyLine> parties)
        {
            foreach (MapEventParty mapEventParty in side.Parties)
            {
                PartyBase party = mapEventParty.Party;
                if (party == null || party.MemberRoster == null)
                {
                    continue;
                }

                float leaderModifier = (party.LeaderHero != null) ? party.LeaderHero.PowerModifier : 0f;
                int before = into.Count;
                int wounded = 0;

                for (int i = 0; i < party.MemberRoster.Count; i++)
                {
                    TroopRosterElement element = party.MemberRoster.GetElementCopyAtIndex(i);
                    if (element.Character == null)
                    {
                        continue;
                    }
                    wounded += element.WoundedNumber;

                    int healthy = element.Number - element.WoundedNumber;
                    for (int n = 0; n < healthy; n++)
                    {
                        Soldier soldier = default(Soldier);
                        soldier.Character = element.Character;
                        soldier.LeaderModifier = leaderModifier;
                        soldier.Party = party;
                        into.Add(soldier);
                    }
                }

                int brought = into.Count - before;
                if (brought > 0 || wounded > 0)
                {
                    PartyLine line = new PartyLine();
                    line.Name = (party.Name != null) ? party.Name.ToString() : party.Id;
                    line.Count = brought;
                    line.Wounded = wounded;
                    parties.Add(line);
                }
            }
        }

        private static string Describe(MapEventSide side)
        {
            PartyBase leaderParty = side.LeaderParty;
            if (leaderParty == null)
            {
                return "<none>";
            }
            return (leaderParty.Name != null) ? leaderParty.Name.ToString() : leaderParty.Id;
        }


        /// <summary>
        /// Fight the snapshot <paramref name="samples"/> times over and average what happened.
        ///
        /// This produces the A/B NUMBERS and nothing else. It does not trace the blows -- the blow-by-blow is taken
        /// from the real battle now, in SimulateHit's postfix, because a reimplementation of vanilla's loop can
        /// drift from vanilla's loop, and this one did: it gave heroes a line trooper's single roll where the game
        /// accumulates their damage, and quietly killed every lord in the log.
        /// </summary>
        internal static ShadowResult Run(BattleSnapshot snapshot, bool applyCorrection, int samples)
        {
            ShadowResult result = default(ShadowResult);
            if (snapshot == null || samples <= 0 || snapshot.AttackerCount == 0 || snapshot.DefenderCount == 0)
            {
                return result;
            }

            int attackerWins = 0;
            long attackerCasualties = 0;
            long defenderCasualties = 0;

            List<Soldier> attackers = new List<Soldier>(snapshot.AttackerCount);
            List<Soldier> defenders = new List<Soldier>(snapshot.DefenderCount);

            // The muster roll, taken once: every stack's headcount, which is what its arrows, its shields and its
            // horses are all measured against. It does not change between samples -- each replay starts from the
            // same opening rosters -- so it is built here rather than twenty times over.
            Dictionary<CharacterObject, int> attackerCounts = Muster(snapshot.AttackerTroops);
            Dictionary<CharacterObject, int> defenderCounts = Muster(snapshot.DefenderTroops);

            for (int sample = 0; sample < samples; sample++)
            {
                attackers.Clear();
                attackers.AddRange(snapshot.AttackerTroops);
                defenders.Clear();
                defenders.AddRange(snapshot.DefenderTroops);

                // A FRESH battle each time: full quivers, whole shields, living horses. Carrying one replay's spent
                // arrows into the next would make every sample after the first a rout.
                SimulationBattleState.BattleState state = applyCorrection
                    ? SimulationBattleState.CreateDetached(
                        SimulationBattleState.GetVolleyRounds(snapshot.Context, snapshot.IsSiegeAssault),
                        snapshot.IsSiegeAssault, attackerCounts, defenderCounts)
                    : null;

                FightOnce(snapshot, attackers, defenders, applyCorrection, state);

                if (defenders.Count == 0 && attackers.Count > 0)
                {
                    attackerWins++;
                }
                attackerCasualties += snapshot.AttackerCount - attackers.Count;
                defenderCasualties += snapshot.DefenderCount - defenders.Count;
            }

            result.AttackerWinRate = (float)attackerWins / samples;
            result.AttackerCasualties = (float)attackerCasualties / samples;
            result.DefenderCasualties = (float)defenderCasualties / samples;
            result.AttackerSurvivors = snapshot.AttackerCount - result.AttackerCasualties;
            result.DefenderSurvivors = snapshot.DefenderCount - result.DefenderCasualties;
            return result;
        }

        /// <summary>The muster roll of one side of the snapshot: how many men each troop type has standing.</summary>
        private static Dictionary<CharacterObject, int> Muster(List<Soldier> troops)
        {
            Dictionary<CharacterObject, int> roll = new Dictionary<CharacterObject, int>();
            foreach (Soldier soldier in troops)
            {
                int running;
                roll.TryGetValue(soldier.Character, out running);
                roll[soldier.Character] = running + 1;
            }
            return roll;
        }

        /// <summary>One battle, round by round, exactly as MapEvent.SimulateBattleRound runs it.</summary>
        private static void FightOnce(BattleSnapshot snapshot, List<Soldier> attackers, List<Soldier> defenders,
            bool applyCorrection, SimulationBattleState.BattleState state)
        {
            for (int round = 0; round < MaxRounds; round++)
            {
                if (attackers.Count == 0 || defenders.Count == 0)
                {
                    return;
                }

                int defenderTicks;
                int attackerTicks;
                ComputeTicks(snapshot, defenders.Count, attackers.Count, out defenderTicks, out attackerTicks);

                // The real battle's allocation is doubled (SimulationRoundCounter). The replay must feel the same
                // round, or the two are counting different battles -- and the phases, which are measured in ROUNDS,
                // would cover a different share of each.
                defenderTicks *= SimulationRoundCounter.TickMultiplier;
                attackerTicks *= SimulationRoundCounter.TickMultiplier;
                if (defenderTicks + attackerTicks <= 0)
                {
                    return;
                }

                // The clock. In the real battle this is the tick allocation being called at the top of the round;
                // here it is the same moment, counted the same way, so the volley phase and the charge decay fall
                // exactly where they fall in the game.
                if (state != null)
                {
                    state.Round++;
                }

                while (attackerTicks + defenderTicks > 0 && attackers.Count > 0 && defenders.Count > 0)
                {
                    float attackerShare = (float)attackerTicks / (attackerTicks + defenderTicks);
                    if (MBRandom.RandomFloat < attackerShare)
                    {
                        attackerTicks--;
                        Strike(snapshot, attackers, defenders, BattleSideEnum.Attacker, applyCorrection, state);
                    }
                    else
                    {
                        defenderTicks--;
                        Strike(snapshot, defenders, attackers, BattleSideEnum.Defender, applyCorrection, state);
                    }
                }
            }
        }

        /// <summary>
        /// One blow: a random man of the striking side at a random man of the other, priced exactly as
        /// DefaultCombatSimulationModel.SimulateHit prices it, and then our correction if it is being applied.
        /// </summary>
        private static void Strike(BattleSnapshot snapshot, List<Soldier> strikers, List<Soldier> struckSide,
            BattleSideEnum strikerSide, bool applyCorrection, SimulationBattleState.BattleState state)
        {
            Soldier strikerSoldier = strikers[MBRandom.RandomInt(strikers.Count)];
            int victimIndex = MBRandom.RandomInt(struckSide.Count);
            Soldier struckSoldier = struckSide[victimIndex];

            CharacterObject striker = strikerSoldier.Character;
            CharacterObject struck = struckSoldier.Character;

            bool strikerIsAttacker = strikerSide == BattleSideEnum.Attacker;
            BattleSideEnum struckSideEnum = strikerIsAttacker ? BattleSideEnum.Defender : BattleSideEnum.Attacker;

            // Each man's own lord, not the lord of whoever leads the side. In a battle of several parties these
            // differ, and vanilla is careful about it -- SimulateHit is handed the striker's own party.
            float strikerLeaderModifier = strikerSoldier.LeaderModifier;
            float struckLeaderModifier = struckSoldier.LeaderModifier;
            float advantage = strikerIsAttacker ? snapshot.AttackerAdvantage : snapshot.DefenderAdvantage;
            float strikerMorale = strikerIsAttacker ? snapshot.AttackerMorale : snapshot.DefenderMorale;
            float struckMorale = strikerIsAttacker ? snapshot.DefenderMorale : snapshot.AttackerMorale;

            MilitaryPowerModel powerModel = Campaign.Current.Models.MilitaryPowerModel;
            float strikerPower = powerModel.GetTroopPower(striker, strikerSide, snapshot.Context, strikerLeaderModifier);
            float struckPower = powerModel.GetTroopPower(struck, struckSideEnum, snapshot.Context, struckLeaderModifier);
            if (struckPower <= 0f)
            {
                return;
            }

            float damage = (0.5f + 0.5f * MBRandom.RandomFloat) * (40f * MathF.Pow(strikerPower / struckPower, 0.7f) * advantage);

            // The morale factor, as CalculateSimulationMoraleEffects applies it.
            float strikerShortfall = MathF.Min(strikerMorale - 50f, 0f);
            float struckSurplus = MathF.Max(struckMorale - 50f, 0f);
            damage *= 1f + ((strikerShortfall - struckSurplus) * 0.005f);

            float vanillaDamage = damage;
            SimulationEquipmentPower.Breakdown breakdown = default(SimulationEquipmentPower.Breakdown);
            breakdown.Correction = 1f;

            if (applyCorrection)
            {
                // spend: true -- the arrow really is loosed and the shield really is splintered, in this replay's
                // own private battle state. Without that the replay would run a model the game does not run.
                //
                // Explain rather than GetCorrection, because the breakdown is the whole point of a trace: the
                // correction alone is a number, and a number does not tell you the man was out of arrows.
                SimulationEquipmentPower.Explain(striker, struck, out breakdown, state, strikerIsAttacker, spend: true);
                damage *= breakdown.Correction;
            }

            int blow = (int)damage;

            // The same pool the real battle gives him -- his lord's perks included. MaxHitPoints() alone is the flat
            // hundred nobody actually fights with any more.
            int hitPoints = SimulationTroopHitPoints.MaxHitPoints(struck, struckSoldier.Party);

            // ApplySimulationDamageToSelectedTroop, and it has two branches -- so this has two branches.
            //
            // A line trooper is rolled against his hit points and is either untouched or out of the battle. There
            // is no pool and nothing accumulates: RandomInt(maxHitPoints) < damage, once, and that is the whole of
            // it. A HERO does accumulate -- AddHeroDamage -- and falls only when it has added up.
            //
            // Whether the man is then killed or merely wounded is left out; either way he is out of the battle,
            // and it is the battle we are comparing.
            // Every man is worn down now, lord and levy alike -- there is no coin-flip left in it. A blow is
            // subtracted, and when there is nothing left to subtract from, he falls. Mirrors the prefix in
            // SimulationTroopHitPoints, which is what the real battle actually runs.
            struckSoldier.Damage += damage;
            struckSide[victimIndex] = struckSoldier;
            bool downed = struckSoldier.Damage >= hitPoints;

            if (downed)
            {
                struckSide.RemoveAt(victimIndex);
            }

        }

        /// <summary>How many blows each side gets this round, as DefaultCombatSimulationModel hands them out.</summary>
        private static void ComputeTicks(BattleSnapshot snapshot, int defenders, int attackers, out int defenderTicks, out int attackerTicks)
        {
            if (snapshot.IsSiegeAssault && defenders > 30)
            {
                float advantage = snapshot.SettlementAdvantage * 0.7f;
                attackerTicks = MathF.Round(1.5f + MathF.Pow(defenders, 0.3f)) * 2;
                defenderTicks = MathF.Round(0.5f + MathF.Max(1f + MathF.Pow(defenders, 0.3f) * advantage, (float)((defenders + 1) / (attackers + 1)))) * 2;
            }
            else if (defenders <= 10)
            {
                defenderTicks = Math.Max(MathF.Round(MathF.Min(attackers * 3f, defenders * 0.3f)), 1);
                attackerTicks = Math.Max(MathF.Round(MathF.Min(defenders * 3f, attackers * 0.3f)), 1);
            }
            else
            {
                defenderTicks = MathF.Round(MathF.Min(attackers * 2f, MathF.Pow(defenders, 0.6f)));
                attackerTicks = MathF.Round(MathF.Min(defenders * 2f, MathF.Pow(attackers, 0.6f)));
            }
        }
    }
}
