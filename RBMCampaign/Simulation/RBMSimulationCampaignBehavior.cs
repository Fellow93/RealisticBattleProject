using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// Watches every battle on the map and writes down what happened in it: who stood on each side, what they were
    /// carrying, and then every blow of the fight as it landed.
    ///
    /// It does not replay anything. It used to -- twenty times with the model and twenty without, to say what the
    /// model had changed -- but a replay is a reimplementation of vanilla's loop, and this one had drifted from it
    /// (giving heroes a line trooper's single roll where the game accumulates their damage) and was reporting a
    /// battle nobody had fought. A record of the real thing cannot be wrong in that way.
    ///
    /// Costs nothing when the log is off: no snapshot is taken and no blow is recorded.
    /// </summary>
    public class RBMSimulationCampaignBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.MapEventStarted.AddNonSerializedListener(this, OnMapEventStarted);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            // A session launches on a NEW game and on every LOADED save alike. The simulation's per-battle and
            // per-troop caches are static, keyed by MapEvent/CharacterObject identity, and are reclaimed only by the
            // MapEventEnded of the battle that filled them. A save loaded while an event was live tears that campaign
            // down without ever ending its events, so those entries -- and any hero instances they hold -- would sit
            // orphaned for the life of the process, and the loaded battle would resume against a stale round clock.
            // Clear them all here so each session starts from a clean slate.
            SimulationBattleState.ResetForNewSession();
            SimulationTroopHitPoints.ResetForNewSession();
            SimulationBattleSnapshot.ResetForNewSession();
            SimulationRout.ResetForNewSession();
            SimulationEquipmentPower.ResetForNewSession();
            SimulationArmTargeting.ResetForNewSession();
            SimulationPerks.ResetForNewSession();

            SimulationLog.StartCampaignLog();

            // The strategic power model wants the same clean slate, for a stronger reason than the above: nothing
            // reclaims its caches at all. They are keyed by CharacterObject and MobileParty and no event empties them,
            // so a lord's measured power -- and the troop, party and hero instances it is filed under -- would outlive
            // the campaign it was taken from and sit there for the life of the process. Worse than the leak: a hero's
            // measurement is only re-taken when it is a day old, and the day it is stamped with comes from whichever
            // campaign was running when it was taken. Load a save at an earlier date and that stamp is in the future,
            // so the entry never looks stale -- and the lord is priced, indefinitely, off the harness he was wearing
            // in a campaign the player has left.
            StrategicTroopPower.ResetForNewSession();

            // Same reasoning, smaller stakes: the town food caches are keyed by Town and would
            // otherwise hold a departed campaign's settlements for the life of the process.
            RBMTownFoodSupply.ResetForNewSession();
            CitizenDemand.ResetForNewSession();
            TownFoodReserve.ResetForNewSession();
            HiddenMarketStock.ResetForNewSession();
            RBMProsperityEquilibrium.ResetForNewSession();
            RBMMarketPrices.ResetForNewSession();
            WorkshopDemand.ResetForNewSession();
            RBMItemWeightTextWidget.ResetForNewSession();
        }

        /// <summary>
        /// A first, provisional picture of the battle -- and ONLY that.
        ///
        /// The rosters have to be taken before the fighting, since by the end the dead are gone from them. But this
        /// moment is too early to see the battle whole: a lord's allies and the rest of his army have not attached
        /// themselves to the event yet, and a two-party army photographed here comes out as one party. So an
        /// auto-resolved battle takes its picture again at the top of the first simulated round, by which time
        /// everyone has arrived and nobody has died.
        ///
        /// This one stands only for a battle the player fights himself, which never simulates a round and so never
        /// gets the better picture.
        /// </summary>
        private void OnMapEventStarted(MapEvent mapEvent, PartyBase attackerParty, PartyBase defenderParty)
        {
            SimulationBattleSnapshot.CaptureIfAbsent(mapEvent);
        }

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            if (mapEvent == null)
            {
                return;
            }

            // The blows of the battle as they landed. Taken BEFORE the state is forgotten, obviously -- and the
            // ordering here is load-bearing, since Forget drops the trace with everything else.
            List<HitRecord> trace = SimulationBattleState.TakeTrace(mapEvent);

            // And who led it. Taken before Forget for the same reason the trace is, and reported off the men who
            // were APPOINTED rather than the men still standing -- a captain killed in round twenty led the battle
            // whatever the last round says about him.
            SimulationCommandStructure.SideCommand attackerCommand;
            SimulationCommandStructure.SideCommand defenderCommand;
            SimulationBattleState.TakeCommands(mapEvent, out attackerCommand, out defenderCommand);

            // And the engines' own book. Taken before Forget for the same reason as the two above. It is kept
            // apart from the blow trace because an engine's shot is not a blow -- it has no striker, no weapon and
            // no body part -- but its casualties are perfectly real, and without this they would appear in the
            // headcount as men nothing on the page killed.
            List<ArtilleryRecord> artillery = SimulationBattleState.TakeArtillery(mapEvent);

            // Who broke and ran, as against who fell. The game's casualty figure counts a fugitive exactly as it
            // counts a corpse, so without this the log cannot tell a destroyed army from one that gave up.
            int attackerRouted;
            int defenderRouted;
            int routRound;
            SimulationBattleState.TakeRout(mapEvent, out attackerRouted, out defenderRouted, out routRound);

            // How the wall itself went -- how good it was, what the lanes held, the frontage that bought, and
            // whether the storm ever started. None of it is visible anywhere else.
            SimulationBattleState.BattleState siege = SimulationBattleState.TakeSiegeReport(mapEvent);

            // Now the battle is done: let go of its arrows, its splintered shields and its dead horses, or the
            // campaign will carry the memory of every fight it ever fought.
            SimulationBattleState.Forget(mapEvent);
            SimulationRout.Forget(mapEvent);

            SimulationBattleSnapshot.BattleSnapshot snapshot = SimulationBattleSnapshot.Take(mapEvent);
            if (snapshot == null || !SimulationLog.IsEnabled)
            {
                return;
            }

            SimulationLog.Write(Format(mapEvent, snapshot, trace, artillery, siege, attackerCommand, defenderCommand,
                attackerRouted, defenderRouted, routRound));
        }

        private static string Format(MapEvent mapEvent, SimulationBattleSnapshot.BattleSnapshot snapshot,
            List<HitRecord> trace, List<ArtilleryRecord> artillery, SimulationBattleState.BattleState siege,
            SimulationCommandStructure.SideCommand attackerCommand,
            SimulationCommandStructure.SideCommand defenderCommand,
            int attackerRouted, int defenderRouted, int routRound)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("──────────────────────────────────────────────────────────────────────").Append("\n");
            sb.Append("day ").Append(SimulationLog.CampaignDate())
              .Append("  ·  ").Append(snapshot.BattleType)
              .Append("  ·  ").Append(snapshot.Context)
              .Append(snapshot.IsPlayerBattle ? "  ·  PLAYER" : "")
              .Append("\n");
            AppendSide(sb, "attacker", snapshot.AttackerName, snapshot.AttackerCount, snapshot.AttackerParties);
            AppendSide(sb, "defender", snapshot.DefenderName, snapshot.DefenderCount, snapshot.DefenderParties);
            sb.Append("  advantage: attacker ").Append(SimulationLog.Fmt(snapshot.AttackerAdvantage))
              .Append(", defender ").Append(SimulationLog.Fmt(snapshot.DefenderAdvantage)).Append("\n");

            // What the map priced each side at going in -- the number the AI weighed when it decided to fight, and
            // the sum of the two, so a battle's whole weight can be read at a glance against how it turned out.
            sb.Append("  power: attacker ").Append(SimulationLog.Fmt(snapshot.AttackerPower))
              .Append(", defender ").Append(SimulationLog.Fmt(snapshot.DefenderPower))
              .Append("  (sum ").Append(SimulationLog.Fmt(snapshot.AttackerPower + snapshot.DefenderPower))
              .Append(")").Append("\n");

            // How free the horse was to charge, and why. A charge needs a crowd on the ground to break, so the ground's
            // own figure is thinned by how many men the side being charged has on foot -- these are the opening
            // numbers, and both fall away as the foot are killed. A small fight prints a small number, and should.
            // The charge is recomputed live and has the same mid-event type-flip problem as the approach above, but
            // it is left alone deliberately: a wall assault has no charge at all (state.ChargeChance is latched to
            // zero for it), so what this line prints for a siege is noise from a battle type the fight never used.
            // Worth knowing when reading a siege's header -- ignore the charge row there.
            sb.Append("  charge: into attacker's foot ")
              .Append(SimulationLog.Fmt(SimulationBattleState.ChargeChanceOpening(mapEvent, snapshot.AttackerTroops)))
              .Append(" (").Append(SimulationBattleState.FootCount(snapshot.AttackerTroops))
              .Append(" of ").Append(snapshot.AttackerCount).Append(" on foot)")
              .Append("  ·  into defender's foot ")
              .Append(SimulationLog.Fmt(SimulationBattleState.ChargeChanceOpening(mapEvent, snapshot.DefenderTroops)))
              .Append(" (").Append(SimulationBattleState.FootCount(snapshot.DefenderTroops))
              .Append(" of ").Append(snapshot.DefenderCount).Append(" on foot)").Append("\n");

            // And how long the bows had the field alone. Scaled by the size of the fight for the same reason the
            // charge is: forty men do not deploy and advance, they collide. A siege and a sea fight are exempt.
            // A WALL ASSAULT HAS NO VOLLEY -- it has an approach, which is a different thing with different rules
            // (see SimulationSiege) -- so it is named for what it is rather than borrowing the field's word.
            //
            // A SIEGE PRINTS THE LENGTH IT ACTUALLY RAN, taken off the battle's own latched state rather than
            // recomputed here. That distinction is not pedantry: this line is written at MapEventEnded and reads
            // the LIVE MapEvent, but native mutates `_mapEventType` mid-event (AddParty turns a Siege into a
            // SiegeOutside the moment a defender party with no settlement joins), and SimulationContext is derived
            // from that field. So by write-up time a wall assault frequently reports itself as a plain field
            // battle, and this line claimed a 6-round field volley for a storm that had really run its full
            // 12-round approach. Nine of fifteen logged sieges were mislabelled that way before this.
            sb.Append((siege != null) ? "  approach: " : "  volley: ")
              .Append(SimulationLog.Fmt((siege != null)
                  ? SimulationSiege.ApproachRounds
                  : SimulationBattleState.VolleyRoundsOpening(mapEvent,
                      snapshot.AttackerCount + snapshot.DefenderCount)))
              .Append(" rounds  (").Append(snapshot.AttackerCount + snapshot.DefenderCount)
              .Append(" men on the field)").Append("\n");

            AppendSiege(sb, siege);

            AppendPerks(sb, "attacker", attackerCommand, snapshot.AttackerParties);
            AppendPerks(sb, "defender", defenderCommand, snapshot.DefenderParties);
            sb.Append("\n");

            // How it ended. The game's own verdict, on the only battle there is.
            sb.Append("  RESULT  winner ").Append(WinnerOf(mapEvent))
              .Append("  ·  casualties  attacker ").Append(mapEvent.AttackerSide.TroopCasualties)
              .Append(Fugitives(attackerRouted))
              .Append(", defender ").Append(mapEvent.DefenderSide.TroopCasualties)
              .Append(Fugitives(defenderRouted));

            // A BREAK IS NOT A MASSACRE, and the casualty figures alone cannot tell them apart -- native books a
            // fugitive exactly as it books a corpse (see SimulationRoutMarker). A side whose whole strength shows
            // as casualties was either destroyed to the last man or ran; the answer changes what the number means
            // entirely, and it changes what a reader should do about it. So it is said outright.
            if (routRound >= 0)
            {
                sb.Append("  ·  broke at round ").Append(routRound);
            }
            else
            {
                sb.Append("  ·  no side broke -- fought to a finish");
            }
            // A battle that struck blows but never turned a round. The round clock is the spine of the phase model --
            // volley, skirmish, the lines meeting all hang off it -- so a fight frozen at round zero never leaves its
            // opening phase, which is exactly the shape the War Sails decorator bug took (see
            // NavalSimulationRoundCounter, which now drives the clock for the naval model). Flagged here, on the one
            // line every battle prints, so a regression back into that state is a single grep away.
            if (StalledAtRoundZero(trace))
            {
                sb.Append("   *** STALLED AT ROUND 0 -- round clock never advanced, phases frozen ***");
            }
            // A STORM THAT NEVER STARTED reads, on every other line of this page, as an ordinary defender's win --
            // and it is not one. The besiegers crossed the killing ground and found nothing to climb, and the
            // casualties above are what the crossing alone cost them. Said plainly, because a repulse and a
            // defeat at the wall want completely different answers from whoever is reading.
            if (SimulationSiege.Repulsed(siege))
            {
                sb.Append("   *** ATTACKERS REPULSED -- no way in survived the approach; the assault never began ***");
            }
            sb.Append("\n");

            // WHAT THE ENGINES DID, on the line beside the casualties they are part of. A siege whose artillery
            // killed forty men and whose blow-by-blow accounts for none of them reads as a broken model; this is
            // the line that closes the books. Printed before the working and the trace because it belongs with the
            // RESULT it explains.
            AppendArtillery(sb, artillery);

            AppendWorking(sb, snapshot);
            AppendTrace(sb, trace);

            return sb.ToString().Replace("\n", System.Environment.NewLine);
        }

        /// <summary>
        /// EVERY PERK THIS SIDE ACTUALLY GOT, and what each of them was worth.
        ///
        /// This is the only place the perk system can be checked at all. What it does is spread across thousands of
        /// blows and buried inside a skill folded into a cached kit and a pool folded into a wound roll; there is no
        /// other way to look at a battle and see that the archers had a captain with Dead Aim, that the infantry had
        /// nobody, or that a lord's Unwavering Defense was quietly keeping his line on its feet.
        ///
        /// Three things, and they are three different perk mechanisms that happen to end up on the same page:
        ///
        ///   COMMANDERS. Each party's own leader, and the hit-point perks of his that fired for the men HE brought.
        ///   Per party, not per side, because that is how PartyLeader perks work -- a side is several lords and none
        ///   of them commands another's troops. Measured at the muster off his real roster (see
        ///   SimulationBattleSnapshot.DescribeCommanderPerks), and read out of the same ExplainedNumber the battle
        ///   used, so this cannot claim a perk the fight did not apply.
        ///
        ///   CAPTAINS. Who led each body of men, and which of their perks reached it. Per side, because a formation
        ///   spans every party on it.
        ///
        ///   WHAT CAME OUT. The commander's PowerModifier -- vanilla's flat tally of his captain perks -- which this
        ///   model lifts back out of every blow precisely because the captains above are now priced for real. It is
        ///   printed against them deliberately: this number is what the side LOST, the captains' line is what it got
        ///   back, and whether the trade is sane is the whole calibration question.
        ///
        /// Prints nothing at all when the perk system is off.
        /// </summary>
        private static void AppendPerks(StringBuilder sb, string label, SimulationCommandStructure.SideCommand command,
            List<SimulationBattleSnapshot.PartyLine> parties)
        {
            if (command == null || !SimulationPerks.Enabled)
            {
                return;
            }

            List<string> body = new List<string>();

            // The commanders, party by party.
            if (parties != null)
            {
                foreach (SimulationBattleSnapshot.PartyLine party in parties)
                {
                    if (!string.IsNullOrEmpty(party.CommanderPerks))
                    {
                        body.Add("      " + Clip(party.Name, 34).PadRight(34) + party.CommanderPerks);
                    }
                }
            }

            // The captains.
            StringBuilder captains = new StringBuilder();
            string[] names = new string[] { "foot", "bows", "horse", "horse archers" };
            for (int bucket = 0; bucket < SimulationCommandStructure.BucketCount; bucket++)
            {
                CharacterObject captain = command.Appointed[bucket];
                if (captain == null)
                {
                    continue;
                }
                if (captains.Length > 0)
                {
                    captains.Append("  ·  ");
                }
                captains.Append(names[bucket]).Append(": ").Append(captain.Name);

                List<string> perks = SimulationPerks.PerkNamesOf(captain);
                captains.Append((perks.Count > 0) ? (" (" + string.Join(", ", perks.ToArray()) + ")") : " (no captain perks)");
            }
            if (captains.Length > 0)
            {
                body.Add("      " + "captains".PadRight(34) + captains);
            }

            // And what vanilla's proxy was worth before it was taken away.
            if (command.LeaderPowerLifted > 0f)
            {
                body.Add("      " + "power modifier lifted".PadRight(34)
                    + SimulationLog.Fmt(command.LeaderPowerLifted)
                    + ((command.Commander != null) ? ("  (" + command.Commander.Name + "'s captain-perk tally)") : ""));
            }

            if (body.Count == 0)
            {
                return;
            }

            sb.Append("  ").Append(label).Append(" perks").Append("\n");
            foreach (string line in body)
            {
                sb.Append(line).Append("\n");
            }
        }

        /// <summary>
        /// The battle itself, blow by blow -- the battle the game actually fought and the campaign will actually
        /// live with.
        ///
        /// Every blow here was recorded as it landed, from inside SimulateHit's postfix, and whether it put its man
        /// down is the game's own verdict rather than a re-roll of ours.
        ///
        /// This is the thing the log has never had. A summary says a battle went one way; the matchup table says
        /// what a blow WOULD do. Neither can tell you the archers ran out of arrows in round fifteen and spent the
        /// rest of the fight being cut down with knives in their hands, or that the lancers' charge was spent by
        /// round four and they were never dangerous again. That story only exists in the blows.
        /// </summary>
        /// <summary>
        /// A battle landed blows but never advanced past round zero -- the failure the naval decorator fix is meant
        /// to close. Only ever true when there were blows to stall (an empty or unlogged trace is not a stall), so a
        /// legitimate no-blow instant does not trip it. The moment any blow is stamped with a round the game turned,
        /// this is false, so a healthy fight -- naval or land -- never carries the flag.
        /// </summary>
        private static bool StalledAtRoundZero(List<HitRecord> trace)
        {
            if (trace == null || trace.Count == 0)
            {
                return false;
            }
            foreach (HitRecord hit in trace)
            {
                if (hit.Round > 0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// THE WALL, in the facts that decide a storm and appear nowhere else on the page.
        ///
        /// The round headers print the LIVE frontage, which moves with every melee kill. This prints what BOUGHT
        /// it: how good the wall was, what stood in each of the three lanes when the ladders went up, and the
        /// frontage that came out. Without it a reader can see that an assault had a frontage of two but not that
        /// it had a frontage of two because the besieger arrived with one ladder and nothing else.
        /// </summary>
        private static void AppendSiege(StringBuilder sb, SimulationBattleState.BattleState siege)
        {
            if (siege == null)
            {
                return;
            }

            // A BATTLE THAT LEFT THE WALL says so first, because everything else on these lines describes a storm
            // that stopped being one partway through. Native reclassifies a siege the moment a relief army joins
            // the defenders, and the model follows it down onto the field -- horses back, charges back, the wall's
            // bonuses and the frontage gone. Without this line the reader sees siege furniture on a battle whose
            // second half was fought in the open and has no way to tell which half is which.
            // What artillery each side actually had at the muster, and why any of it was not counted. Printed for
            // every wall assault, stand-down or not: it is the only way to tell an attacker who brought no engines
            // from one whose engines this model failed to see. See SimulationSiege.DescribeEngines.
            if (siege.SiegeEngineReport != null)
            {
                sb.Append("  engines: ").Append(siege.SiegeEngineReport).Append("\n");
            }

            if (siege.SiegeStoodDownRound >= 0)
            {
                sb.Append("  wall: LEFT THE WALL at round ").Append(siege.SiegeStoodDownRound)
                  .Append(" -- a relief force joined the defenders and the siege rules stood down")
                  .Append("\n");
                if (siege.SiegeLanes != null)
                {
                    sb.Append("  lanes (while it lasted): ").Append(siege.SiegeLanes).Append("\n");
                }
                return;
            }

            sb.Append("  wall: x").Append(siege.SiegeWallFactor.ToString("0.00"))
              .Append(" on the defender's advantage");

            if (siege.SiegeLanes == null)
            {
                // The approach never ended -- the battle was decided while the men were still crossing.
                sb.Append("  ·  the assault was never reached").Append("\n");
                return;
            }

            sb.Append("\n");
            sb.Append("  lanes: ").Append(siege.SiegeLanes).Append("\n");
            sb.Append("  frontage at the ladders: attacker ").Append(siege.StartAttackWidth)
              .Append(", defender ").Append(siege.StartDefendWidth);
            if (siege.StartAttackWidth > 0)
            {
                sb.Append("  (ended ").Append(siege.AttackWidth).Append(" v ").Append(siege.DefendWidth).Append(")");
            }
            sb.Append("\n");
        }

        /// <summary>
        /// THE ARTILLERY, shot by shot and then totalled.
        ///
        /// Its own section, because an engine's shot is not a blow and cannot be forced into the blow columns: it
        /// has no striker, no weapon drawn, no body part found and no armour met. But it kills men, and if those
        /// casualties appeared nowhere the log would show a siege in which the headcount fell faster than anything
        /// on the page could account for -- which reads exactly like a bug, and is the sort of thing that gets a
        /// working model "fixed" until it is broken.
        ///
        /// The TOTALS are the part worth having. They answer the only two questions the artillery raises: how much
        /// of this siege did the engines decide, and did they hit anything. The per-shot lines below are for
        /// following a particular engine's afternoon.
        /// </summary>
        private static void AppendArtillery(StringBuilder sb, List<ArtilleryRecord> artillery)
        {
            if (artillery == null || artillery.Count == 0)
            {
                return;
            }

            int attackerShots = 0, attackerHits = 0, attackerKilled = 0, attackerWounded = 0, attackerBroke = 0;
            int defenderShots = 0, defenderHits = 0, defenderKilled = 0, defenderWounded = 0, defenderBroke = 0;
            foreach (ArtilleryRecord shot in artillery)
            {
                if (shot.FiredByAttacker)
                {
                    attackerShots++;
                    if (shot.Hit) { attackerHits++; }
                    attackerKilled += shot.Killed;
                    attackerWounded += shot.Wounded;
                    if (shot.Destroyed) { attackerBroke++; }
                }
                else
                {
                    defenderShots++;
                    if (shot.Hit) { defenderHits++; }
                    defenderKilled += shot.Killed;
                    defenderWounded += shot.Wounded;
                    if (shot.Destroyed) { defenderBroke++; }
                }
            }

            sb.Append("\n");
            sb.Append("  the artillery (").Append(artillery.Count).Append(" shots):").Append("\n");
            AppendArtillerySide(sb, "attacker", attackerShots, attackerHits, attackerKilled, attackerWounded, attackerBroke);
            AppendArtillerySide(sb, "defender", defenderShots, defenderHits, defenderKilled, defenderWounded, defenderBroke);

            sb.Append("      round  side  engine          at       result").Append("\n");
            foreach (ArtilleryRecord shot in artillery)
            {
                sb.Append("      ").Append(shot.Round.ToString().PadLeft(5))
                  .Append("  ").Append(shot.FiredByAttacker ? "A   " : "D   ")
                  .Append("  ").Append(Clip(shot.Engine ?? "-", 15).PadRight(16))
                  .Append(Clip(shot.Target ?? "-", 8).PadRight(9));

                if (!shot.Hit && shot.Killed == 0 && shot.Wounded == 0)
                {
                    sb.Append("wide");
                }
                else
                {
                    // A shot at an engine that ALSO caught men reads as both, in the order it happened: the machine
                    // it was aimed at, then whoever the stone came down on.
                    bool first = true;
                    if (shot.TargetEngine != null && shot.Hit)
                    {
                        sb.Append(shot.Destroyed ? "DESTROYED " : "hit ").Append(shot.TargetEngine)
                          .Append(" (").Append(SimulationLog.Fmt(shot.EngineDamage)).Append(" dmg)");
                        first = false;
                    }
                    else if (shot.TargetEngine != null)
                    {
                        sb.Append("missed ").Append(shot.TargetEngine);
                        first = false;
                    }
                    if (shot.Killed > 0)
                    {
                        sb.Append(first ? "" : ", ").Append("killed ").Append(shot.Killed);
                        first = false;
                    }
                    if (shot.Wounded > 0)
                    {
                        sb.Append(first ? "" : ", ").Append("burned ").Append(shot.Wounded);
                    }
                }
                sb.Append("\n");
            }
        }

        private static void AppendArtillerySide(StringBuilder sb, string label, int shots, int hits, int killed,
            int wounded, int broke)
        {
            if (shots == 0)
            {
                return;
            }
            sb.Append("    ").Append(label.PadRight(9))
              .Append(shots).Append(" shots, ").Append(hits).Append(" on target (")
              .Append(SimulationLog.Fmt(100f * hits / shots)).Append("%)")
              .Append("  ·  killed ").Append(killed)
              .Append(", burned ").Append(wounded)
              .Append(", engines broken ").Append(broke)
              .Append("\n");
        }

        private static void AppendTrace(StringBuilder sb, List<HitRecord> trace)
        {
            if (trace == null || trace.Count == 0)
            {
                return;
            }

            sb.Append("\n");
            sb.Append("  the battle, blow by blow -- as the game actually fought it (")
              .Append(trace.Count).Append(" blows):").Append("\n");
            // "odds" was a leftover from when the last column was a chance-of-death; it has printed the man's
            // remaining hit points for a long time, and the header has been lying about it for just as long.
            sb.Append("      striker            -> struck                what     defense       weapon            armor   def%   vanilla  x corr  =  dealt       hp   result").Append("\n");

            int round = -1;
            foreach (HitRecord hit in trace)
            {
                // A round header, whenever the clock turns. The volley is called out by name: it is the part of a
                // battle auto-resolve never had, and half the model's story happens inside it.
                if (hit.Round != round)
                {
                    round = hit.Round;
                    sb.Append("\n    ── round ").Append(round);

                    // A WALL ASSAULT IS ITS OWN BATTLE and says so here, because none of the field's three acts
                    // happened in it. The frontage is printed with the storm: it is the whole of what divides the
                    // round between the two sides, and it moves with every melee kill, so a round header without it
                    // cannot be read back against the casualties that round produced.
                    if (hit.SiegePhase != null)
                    {
                        sb.Append(hit.SiegePhase == "assault"
                            ? "  ·  ASSAULT -- the ladders are up and the openings are held"
                            : "  ·  APPROACH -- crossing the killing ground, and only the bows are at work");
                        if (hit.SiegePhase == "assault")
                        {
                            sb.Append("  ·  width ").Append(hit.SiegeAttackWidth)
                              .Append(" v ").Append(hit.SiegeDefendWidth);
                        }
                        else
                        {
                            // How good the wall is, on the approach where it does the most work. Two sieges with the
                            // same rosters and different fortifications otherwise read identically here.
                            sb.Append("  ·  wall x").Append(hit.SiegeWallFactor.ToString("0.00"));
                        }
                    }
                    else
                    {
                        sb.Append(hit.VolleyPhase
                            ? "  ·  VOLLEY -- the bowmen have the field, the foot are walking into it"
                            : (hit.SkirmishPhase
                                ? "  ·  SKIRMISH -- javelins in the air, and the horse are at each other"
                                : "  ·  THE LINES HAVE MET"));
                    }

                    sb.Append("  ·  ").Append(hit.AttackersLeft).Append(" v ").Append(hit.DefendersLeft)
                      .Append("\n");
                }

                // What he did, and whatever was remarkable about it. A man who is neither shooting nor throwing
                // during the volley is not fighting at all -- he is walking into arrows -- and the trace says so.
                // "closing", not "walking": a horseman crossing the ground is not strolling, and calling him that
                // is what hid the fact that his charge was being spent on the approach and taxed as a stroll.
                string what = hit.Phase;
                if (hit.Closing)
                {
                    what = "closing";
                }
                // He swung at a horse archer and the horse archer was thirty yards away by the time it landed. This
                // is worth its own word in the trace: a line of "melee" blows all dealing a tenth of nothing looks
                // like a broken model, and is in fact infantry doing the one thing infantry cannot do.
                else if (hit.Evaded)
                {
                    what = "KITED";
                }
                else if (hit.ChargeBonus > 1.01f)
                {
                    what = "CHARGE";
                }
                else if (hit.Braced)
                {
                    what = "braced";
                }

                // Clip one short of the pad, always. A name that fills its column exactly leaves no gap, and
                // "Imperial Coast Guard" ran straight into "shoot".
                sb.Append("    ").Append(hit.StrikerIsAttacker ? "A " : "D ")
                  .Append(Clip(Name(hit.Striker), 19).PadRight(20))
                  .Append("-> ").Append(Clip(Name(hit.Struck), 21).PadRight(22))
                  .Append(Clip(what, 8).PadRight(9))
                  // How the blow was answered -- none / shield-block / weapon-block / parry / riposte -- so the
                  // block, parry and riposte rates can be read straight off the log for tuning.
                  .Append(Clip(hit.Defense ?? "none", 12).PadRight(13))
                  .Append(Clip(hit.Weapon ?? "-", 15).PadRight(16))
                  .Append(Clip(hit.BodyPart ?? "-", 5).PadRight(6))
                  .Append(Num(hit.ArmorMet, 7))
                  .Append(Num(hit.ShieldBlock * 100f, 7))
                  .Append(Num(hit.VanillaDamage, 10))
                  .Append(Num(hit.Correction, 8))
                  .Append(Num(hit.FinalDamage, 9))
                  // What is left of the man. EVERY man has a pool now, lord and levy alike, so this is not a
                  // chance-of-death but an actual figure: he is worn down, and when it reaches nothing he falls.
                  //
                  // What he STARTED with is not printed beside it. It was, and it earned its place at the time --
                  // back when a trooper's pool was a flat hundred, "52/100" told you the whole story at a glance.
                  // It does not any more: the pool is the native hundred widened by the lethality scale and then
                  // lifted again by whatever hit-point perks his commander brought, so the denominator moved per
                  // troop, per party and per lord, and a column that changes its own meaning down the page is worse
                  // than no column. The number that matters is the one that reaches zero. The pools themselves are
                  // reported once, properly, in the perks block at the head of the battle.
                  .Append("  hp ")
                  .Append(((hit.HitPointsLeft >= 0f) ? SimulationLog.Fmt(hit.HitPointsLeft) : "-").PadLeft(5))
                  .Append(hit.Downed ? "   DOWN" : "")
                  .Append("\n");
            }

        }

        /// <summary>
        /// One side of the battle: who stands on it, and in what strength. A side is rarely one party -- a lord
        /// brings allies, a garrison brings its militia, a caravan gets swept up in someone else's war -- and the
        /// log used to print the LEADING party's name beside the WHOLE side's headcount, which read as though one
        /// party had somehow fielded three hundred men.
        /// </summary>
        private static void AppendSide(StringBuilder sb, string role, string name, int count, List<SimulationBattleSnapshot.PartyLine> parties)
        {
            // The wounded, if there are any. A side of "one looter" against a battle that killed nine of them is
            // either a band already beaten half to death -- in which case the eight wounded ARE the missing nine --
            // or it is a roster we are failing to read, and the two are indistinguishable until this is printed.
            int wounded = 0;
            if (parties != null)
            {
                foreach (SimulationBattleSnapshot.PartyLine party in parties)
                {
                    wounded += party.Wounded;
                }
            }
            string woundedNote = (wounded > 0) ? (", " + wounded + " wounded") : "";

            if (parties == null || parties.Count <= 1)
            {
                sb.Append("  ").Append(role).Append(" : ").Append(name)
                  .Append("  (").Append(count).Append(" men").Append(woundedNote).Append(")").Append("\n");
                return;
            }

            sb.Append("  ").Append(role).Append(" : ").Append(parties.Count).Append(" parties")
              .Append("  (").Append(count).Append(" men").Append(woundedNote).Append(")").Append("\n");
            foreach (SimulationBattleSnapshot.PartyLine party in parties)
            {
                sb.Append("             · ").Append(Clip(party.Name, 40).PadRight(40))
                  .Append(party.Count.ToString().PadLeft(5))
                  .Append((party.Wounded > 0) ? ("  +" + party.Wounded + "w") : "")
                  .Append("\n");
            }
        }

        /// <summary>
        /// The model's working, shown rather than asserted. Two of its designs have already been wrong on
        /// reasoning that looked perfectly sound from the inside, so it is made to print the numbers a battle
        /// was actually decided by: what each troop carries, and what every term of the correction came to.
        /// </summary>
        private static void AppendWorking(StringBuilder sb, SimulationBattleSnapshot.BattleSnapshot snapshot)
        {
            List<CharacterObject> attackers = TopTroops(snapshot.AttackerTroops, 3);
            List<CharacterObject> defenders = TopTroops(snapshot.DefenderTroops, 3);
            if (attackers.Count == 0 || defenders.Count == 0)
            {
                return;
            }

            sb.Append("\n");
            sb.Append("  kit as the model sees it:").Append("\n");
            sb.Append("    troop                            T  arm   head  neck torso shldr   arm   leg    mag  dmg     blk%  weapon           pen  kit").Append("\n");
            foreach (CharacterObject troop in attackers)
            {
                AppendKit(sb, troop, "A");
            }
            foreach (CharacterObject troop in defenders)
            {
                AppendKit(sb, troop, "D");
            }

            sb.Append("\n");
            sb.Append("  every blow, worked through:").Append("\n");
            sb.Append("    striker              -> struck                armorMet  blk%   actual  baseline   equip/  tier=  correction").Append("\n");
            foreach (CharacterObject striker in attackers)
            {
                foreach (CharacterObject struck in defenders)
                {
                    AppendMatchup(sb, striker, struck);
                }
            }
            foreach (CharacterObject striker in defenders)
            {
                foreach (CharacterObject struck in attackers)
                {
                    AppendMatchup(sb, striker, struck);
                }
            }
        }

        private static void AppendKit(StringBuilder sb, CharacterObject troop, string side)
        {
            SimulationEquipmentPower.KitInfo k = SimulationEquipmentPower.ExplainKit(troop);
            string arm = k.IsMounted ? (k.IsRanged ? "HA " : "cav") : (k.IsRanged ? "arc" : "inf");
            // A hero's Tier is 0 -- he has none -- but vanilla still prices him on a tier, taken from his level.
            // Printing the 0 made every lord in the log look like a peasant.
            int tier = troop.IsHero ? ((troop.HeroObject.Level / 4) + 1) : troop.Tier;
            sb.Append("    ").Append(side).Append(' ').Append(Clip(troop.Name != null ? troop.Name.ToString() : troop.StringId, 29).PadRight(29))
              .Append((troop.IsHero ? ("*" + tier) : tier.ToString()).PadLeft(2))
              .Append("  ").Append(arm)
              .Append(Num(k.Head, 6)).Append(Num(k.Neck, 6)).Append(Num(k.Torso, 6)).Append(Num(k.Shoulder, 6)).Append(Num(k.Arm, 6)).Append(Num(k.Leg, 6))
              .Append(Num(k.Magnitude, 7))
              .Append("  ").Append(k.DamageType.ToString().PadRight(6))
              .Append(Num(k.ShieldBlock * 100f, 5))
              .Append("  ").Append(Clip(k.WeaponType ?? "-", 16).PadRight(16))
              .Append(Num(k.DamageFactor, 5))
              // The weapon named above is only the heaviest of his belt. What he actually swings is the average of
              // all of them, so say how many there are -- and whether one is a spear, since that is the one he
              // reaches for when a horse comes at him.
              .Append("  x").Append(k.WeaponCount)
              .Append(k.HasPolearm ? " spear" : "      ")
              // The quiver: how many KINDS of arrow, since a bodkin and a broadhead answer armour by different
              // rules and the shot is the average of the ones he really carries.
              .Append((k.ShotCount > 1) ? ("  " + k.ShotCount + " arrows") : "")
              // And what he hurls while the lines close, which for half the infantry in Calradia is the deadliest
              // thing they own: "2x Javelin 184" is two throws a man, at 184 magnitude each, and then they are gone.
              .Append(!string.IsNullOrEmpty(k.ThrownType)
                  ? ("  " + SimulationLog.Fmt(k.ThrownPerMan) + "x " + Clip(k.ThrownType, 12) + " " + SimulationLog.Fmt(k.ThrownMagnitude))
                  : "")
              .Append(k.IsPlate ? "  plate" : "")
              .Append(k.IsValid ? "" : "   <INVALID>")
              .Append("\n");

            // The items themselves, exactly as the model read them. RBM crafts its melee weapons from pieces at
            // runtime, so nothing on disk can tell us what a weapon finally is -- only this can.
            string items = SimulationEquipmentPower.DescribeItems(troop);
            if (!string.IsNullOrEmpty(items))
            {
                sb.Append("        ").Append(items).Append("\n");
            }
        }

        private static void AppendMatchup(StringBuilder sb, CharacterObject striker, CharacterObject struck)
        {
            SimulationEquipmentPower.Breakdown b;
            bool applied = SimulationEquipmentPower.Explain(striker, struck, out b);
            sb.Append("    ").Append(Clip(Name(striker), 20).PadRight(20))
              .Append(" -> ").Append(Clip(Name(struck), 20).PadRight(20))
              .Append(Num(b.ArmorMet, 9))
              .Append(Num(b.ShieldBlock * 100f, 6))
              .Append(Num(b.Actual, 9))
              .Append(Num(b.Baseline, 10))
              .Append(Num(b.EquipmentRatio, 9))
              .Append(Num(b.TierTerm, 7))
              .Append(Num(b.Correction, 12))
              .Append(applied ? "" : "  (not applied)")
              .Append("\n");
        }

        /// <summary>
        /// The part of a side's casualty figure that walked away. Prints nothing when nobody ran, so an ordinary
        /// stand-up fight reads exactly as it always has and only a break adds a word.
        /// </summary>
        private static string Fugitives(int routed)
        {
            return (routed > 0) ? (" (" + routed + " of them fugitives, not dead)") : "";
        }

        private static string Name(CharacterObject troop)
        {
            return (troop.Name != null) ? troop.Name.ToString() : troop.StringId;
        }

        private static string Clip(string text, int width)
        {
            return (text.Length <= width) ? text : text.Substring(0, width);
        }

        private static string Num(float value, int width)
        {
            return SimulationLog.Fmt(value).PadLeft(width);
        }

        /// <summary>
        /// The troop types that actually make up a side, commonest first -- a battle is decided by its bulk. Tallied
        /// across every party on the side, since which lord a man came with says nothing about how he fights.
        /// </summary>
        private static List<CharacterObject> TopTroops(Dictionary<CharacterObject, int> troops, int count)
        {
            List<KeyValuePair<CharacterObject, int>> ordered = new List<KeyValuePair<CharacterObject, int>>(troops);
            ordered.Sort((x, y) => y.Value.CompareTo(x.Value));

            List<CharacterObject> result = new List<CharacterObject>();
            for (int i = 0; i < ordered.Count && i < count; i++)
            {
                result.Add(ordered[i].Key);
            }
            return result;
        }

        private static string WinnerOf(MapEvent mapEvent)
        {
            switch (mapEvent.BattleState)
            {
                case BattleState.AttackerVictory:
                    return "attacker";

                case BattleState.DefenderVictory:
                    return "defender";

                default:
                    return "none";
            }
        }
    }
}
