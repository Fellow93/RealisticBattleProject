using System.Collections.Generic;
using System.Text;
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

            /// <summary>
            /// What this party's COMMANDER is worth to the men in it: every hit-point perk of his that actually fired
            /// for at least one of them, by name and number, and the pool it produced.
            ///
            /// Captured HERE, at the muster, and not written at the end, for two reasons. His roster is whole now --
            /// by the last round a wiped-out party has no troops left to ask the question of, and a party whose
            /// cavalry all died would report no veterinary. And it is his PARTY's perks, which is a thing only this
            /// loop knows: a side is several lords with several parties, and the side's leader is not the commander
            /// of the men in someone else's. Null when nothing fired, which is most parties.
            /// </summary>
            public string CommanderPerks;
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

            /// <summary>
            /// What the map believes each side is worth: every party on it priced and summed (see
            /// <see cref="StrategicTroopPower.SidePower"/>). Taken here, before a blow lands, because that is the
            /// number the AI read when it chose to fight and the only one worth comparing the outcome against.
            /// </summary>
            public float AttackerPower;

            public float DefenderPower;

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

        /// <summary>A fresh session (new game or a loaded save): drop every snapshot of the torn-down campaign's
        /// battles, which will never be handed over now. Called from OnSessionLaunched.</summary>
        internal static void ResetForNewSession()
        {
            _snapshots.Clear();
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

            // Whether this is a battle with no horses in it -- which decides which of a commander's perks his men
            // collect, so the log must ask it exactly as the battle did or it will report a pool nobody had.
            bool dismounted = SimulationBattleState.IsDismountedBattle(mapEvent);

            snapshot.AttackerCount = Muster(mapEvent.AttackerSide, snapshot.AttackerTroops, snapshot.AttackerParties, dismounted);
            snapshot.DefenderCount = Muster(mapEvent.DefenderSide, snapshot.DefenderTroops, snapshot.DefenderParties, dismounted);

            snapshot.AttackerName = Describe(mapEvent.AttackerSide);
            snapshot.DefenderName = Describe(mapEvent.DefenderSide);

            // For a naval raid the attacker is priced by what it can land, not what it embarked -- the same discount
            // the raid-decision AI was given (see StrategicTroopPower.AmphibiousLandingFactor). Without this the log
            // would print the full manifest (e.g. 348 for 223 men) while the AI acted on a fraction of it, and the
            // raid it "should have won on paper" reads as an unexplained upset. The defenders are ashore already and
            // are always priced whole.
            bool navalRaid = snapshot.Context == MapEvent.PowerCalculationContext.NavalRaid;
            snapshot.AttackerPower = StrategicTroopPower.SidePower(mapEvent.AttackerSide, navalRaid);
            snapshot.DefenderPower = StrategicTroopPower.SidePower(mapEvent.DefenderSide);

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
        private static int Muster(MapEventSide side, Dictionary<CharacterObject, int> troops, List<PartyLine> parties,
            bool dismounted)
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
                List<CharacterObject> partyTroops = new List<CharacterObject>();

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
                    partyTroops.Add(element.Character);

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
                    line.CommanderPerks = DescribeCommanderPerks(party, partyTroops, dismounted);
                    parties.Add(line);
                }
            }
            return total;
        }

        /// <summary>
        /// Every hit-point perk this party's commander actually pays out, by name and number, with the pools they
        /// produce -- or null if his training does nothing for these men.
        ///
        /// ASKED OF HIS REAL TROOPS, not of a representative one. The perks are conditional (some reach only foot,
        /// some only infantry, some only the ranged, some only the horses), so the only honest way to say which of
        /// them fire is to put the question to every troop type he actually brought and collect the answers. That
        /// also makes the range meaningful: "100 -> 110-125" says his worst-served man gained ten and his best
        /// twenty-five, which is the shape of the thing and not an average that hides it.
        ///
        /// The perks and numbers are read out of ExplainedNumber's own record of what the REAL method did (see
        /// SimulationTroopHitPoints.ExplainCommandedHealth), never from a list kept here. Nothing in this file knows
        /// which perks exist, and nothing in it can drift from the ones that fired.
        /// </summary>
        private static string DescribeCommanderPerks(PartyBase party, List<CharacterObject> troops, bool dismounted)
        {
            if (party == null || troops.Count == 0 || !SimulationPerks.Enabled)
            {
                return null;
            }

            List<string> fired = new List<string>();
            float baseline = -1f;
            float lowest = float.MaxValue;
            float highest = 0f;
            string mount = null;

            foreach (CharacterObject troop in troops)
            {
                if (troop == null || troop.IsHero)
                {
                    // A hero's pool is his own and takes no party bonus, in native and here alike -- he would only
                    // widen the range with a number no perk on this line produced.
                    continue;
                }

                ExplainedNumber pool = SimulationTroopHitPoints.ExplainCommandedHealth(troop, party, dismounted);
                CollectLines(pool, troop.MaxHitPoints(), fired);

                if (baseline < 0f)
                {
                    baseline = troop.MaxHitPoints();
                }
                if (pool.ResultNumber < lowest) { lowest = pool.ResultNumber; }
                if (pool.ResultNumber > highest) { highest = pool.ResultNumber; }

                // And his horses, once -- the mount perks are the party's, so the first mounted troop answers for
                // every one of them. A battle with no horses in it reports none, and rightly: there is nothing on
                // the wall for a veterinary to keep alive.
                if (mount == null && SimulationBattleState.IsMountedIn(troop, dismounted))
                {
                    float baseMount = SimulationEquipmentPower.MountHealthOf(troop);
                    if (baseMount > 0f)
                    {
                        ExplainedNumber mountPool = SimulationTroopHitPoints.ExplainCommandedMountHealth(troop, party, baseMount);
                        List<string> mountFired = new List<string>();
                        CollectLines(mountPool, baseMount, mountFired);
                        if (mountFired.Count > 0)
                        {
                            mount = "mounts " + MathF.Round(baseMount) + " -> " + MathF.Round(mountPool.ResultNumber)
                                + " (" + string.Join(", ", mountFired.ToArray()) + ")";
                        }
                    }
                }
            }

            if (fired.Count == 0 && mount == null)
            {
                return null;
            }

            StringBuilder sb = new StringBuilder();
            if (fired.Count > 0)
            {
                string range = (MathF.Round(lowest) == MathF.Round(highest))
                    ? MathF.Round(highest).ToString()
                    : MathF.Round(lowest) + "-" + MathF.Round(highest);
                sb.Append("hit points ").Append(MathF.Round(baseline)).Append(" -> ").Append(range)
                  .Append(" (").Append(string.Join(", ", fired.ToArray())).Append(")");
            }
            if (mount != null)
            {
                if (sb.Length > 0)
                {
                    sb.Append("  ·  ");
                }
                sb.Append(mount);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Fold one explained pool's perk lines into the running list, without repeating a perk that has already
        /// fired for another troop type in the same party.
        ///
        /// MIND THE FIRST LINE. ExplainedNumber.GetLines() does not hand back the perks -- it hands back the whole
        /// explanation, and the explanation OPENS with the base: its constructor records a Base line whenever it is
        /// built with descriptions on and a non-zero starting number, and GetLines emits that entry before any of
        /// the rest. Taken at face value this prints "Base +100" as though a hundred hit points were a perk the lord
        /// brought. So the leading line is dropped -- and only when there is a base to drop, since a zero base
        /// records none and the first line would then be a real one.
        /// </summary>
        private static void CollectLines(ExplainedNumber explained, float baseNumber, List<string> fired)
        {
            List<(string name, float number)> lines = explained.GetLines();
            int first = (baseNumber != 0f && lines.Count > 0) ? 1 : 0;
            for (int i = first; i < lines.Count; i++)
            {
                string text = lines[i].name + " +" + MathF.Round(lines[i].number);
                if (!fired.Contains(text))
                {
                    fired.Add(text);
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
    }
}
