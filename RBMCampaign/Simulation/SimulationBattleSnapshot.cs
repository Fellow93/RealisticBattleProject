using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Who stood on the field before the first blow landed.
    ///
    /// This is a RECORD, not a rehearsal. The log used to replay each battle twenty times with the model and
    /// twenty without, to say what the model had changed -- but a replay is a reimplementation of vanilla's loop,
    /// and a reimplementation drifts from the thing it reimplements. This one did: it gave heroes a line trooper's
    /// single roll where the game accumulates their damage, and quietly killed every lord in the log. It was
    /// answering a question about a battle nobody had fought. So it is gone, and what remains is the battle that
    /// actually happened -- its rosters here, its blows in the trace.
    ///
    /// The rosters must still be taken BEFORE the fighting: by the end the dead are gone from them, and a record
    /// of who fought cannot be read off the survivors.
    /// </summary>
    internal static class SimulationBattleSnapshot
    {
        /// <summary>One party in the line of battle, for the log to name rather than lump into its neighbour.</summary>
        internal class PartyLine
        {
            public string Name;

            /// <summary>Men who can fight. This is the roster the battle is actually decided by.</summary>
            public int Count;

            /// <summary>
            /// And men who cannot. They are printed because a record of "one looter" against a battle that killed
            /// nine of them is either a party that was already beaten half to death -- in which case the eight
            /// wounded are the missing nine and nothing is wrong -- or it is a roster we are failing to read. The
            /// two look identical in the log until the wounded are shown, so they are shown.
            /// </summary>
            public int Wounded;
        }

        /// <summary>The battle as it stood at the top of the first round: everyone who came, and nobody yet dead.</summary>
        internal class BattleSnapshot
        {
            /// <summary>Every troop type on the side, and how many men of it stand there.</summary>
            public Dictionary<CharacterObject, int> AttackerTroops = new Dictionary<CharacterObject, int>();

            public Dictionary<CharacterObject, int> DefenderTroops = new Dictionary<CharacterObject, int>();

            /// <summary>Every party on each side, named and counted. A battle is rarely one party against one party.</summary>
            public List<PartyLine> AttackerParties = new List<PartyLine>();

            public List<PartyLine> DefenderParties = new List<PartyLine>();

            public float AttackerAdvantage = 1f;

            public float DefenderAdvantage = 1f;

            public MapEvent.PowerCalculationContext Context;

            public bool IsSiegeAssault;

            public string AttackerName = "attacker";

            public string DefenderName = "defender";

            public string BattleType = "battle";

            public bool IsPlayerBattle;

            public int AttackerCount;

            public int DefenderCount;
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

        /// <summary>Everything about the battle that can only be seen before it is fought.</summary>
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

            snapshot.AttackerCount = Muster(mapEvent.AttackerSide, snapshot.AttackerTroops, snapshot.AttackerParties);
            snapshot.DefenderCount = Muster(mapEvent.DefenderSide, snapshot.DefenderTroops, snapshot.DefenderParties);

            snapshot.AttackerName = Describe(mapEvent.AttackerSide);
            snapshot.DefenderName = Describe(mapEvent.DefenderSide);

            try
            {
                ExplainedNumber defenderAdvantage;
                ExplainedNumber attackerAdvantage;
                Campaign.Current.Models.CombatSimulationModel.GetBattleAdvantage(mapEvent, out defenderAdvantage, out attackerAdvantage);
                snapshot.AttackerAdvantage = attackerAdvantage.ResultNumber;
                snapshot.DefenderAdvantage = defenderAdvantage.ResultNumber;
            }
            catch
            {
                // A snapshot is a diagnostic, never a reason to break a battle. Defaults stand.
            }

            return snapshot;
        }

        /// <summary>
        /// The muster roll of one side: every troop type on it, counted across every party standing there. A side is
        /// very often several parties -- a lord, his allies, a militia, a caravan swept up in someone else's war --
        /// and two lords who each brought archers have, between them, one body of archers.
        /// </summary>
        private static int Muster(MapEventSide side, Dictionary<CharacterObject, int> troops, List<PartyLine> parties)
        {
            int total = 0;
            if (side == null)
            {
                return 0;
            }

            foreach (MapEventParty mapEventParty in side.Parties)
            {
                PartyBase party = mapEventParty.Party;
                if (party == null || party.MemberRoster == null)
                {
                    continue;
                }

                int brought = 0;
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
                    if (healthy <= 0)
                    {
                        continue;
                    }

                    int running;
                    troops.TryGetValue(element.Character, out running);
                    troops[element.Character] = running + healthy;
                    brought += healthy;
                }

                total += brought;
                if (brought > 0 || wounded > 0)
                {
                    PartyLine line = new PartyLine();
                    line.Name = (party.Name != null) ? party.Name.ToString() : party.Id;
                    line.Count = brought;
                    line.Wounded = wounded;
                    parties.Add(line);
                }
            }
            return total;
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
    }
}
