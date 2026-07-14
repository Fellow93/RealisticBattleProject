using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using RC = RBMConfig.RBMConfig;

namespace RBMCampaign
{
    /// <summary>
    /// Watches every battle on the map, takes a snapshot of it before the first blow, and when it is over
    /// replays it both ways -- with the equipment model and without -- into the simulation log, so the model's
    /// effect on a real campaign's battles can be read off instead of guessed at.
    ///
    /// Costs nothing when the log is off: no snapshot is taken and no replay is run.
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
            SimulationLog.StartCampaignLog();
        }

        /// <summary>
        /// A first, provisional picture of the battle -- and ONLY that.
        ///
        /// The rosters have to be taken before the fighting, since by the end the dead are gone from them and there
        /// is nothing left to replay. But this moment is too early to see the battle whole: a lord's allies and the
        /// rest of his army have not attached themselves to the event yet, and a two-party army photographed here
        /// comes out as one party. So an auto-resolved battle takes its picture again at the top of the first
        /// simulated round, by which time everyone has arrived and nobody has died.
        ///
        /// This one stands only for a battle the player fights himself, which never simulates a round and so never
        /// gets the better picture.
        /// </summary>
        private void OnMapEventStarted(MapEvent mapEvent, PartyBase attackerParty, PartyBase defenderParty)
        {
            SimulationShadow.CaptureIfAbsent(mapEvent);
        }

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            if (mapEvent == null)
            {
                return;
            }

            // The blows of the battle that was ACTUALLY fought, recorded as they landed. Taken BEFORE the state is
            // forgotten, obviously -- and the ordering here is load-bearing, since Forget drops the trace with
            // everything else.
            List<HitRecord> trace = SimulationBattleState.TakeTrace(mapEvent);

            // Now the battle is done: let go of its arrows, its splintered shields and its dead horses, or the
            // campaign will carry the memory of every fight it ever fought.
            SimulationBattleState.Forget(mapEvent);

            SimulationShadow.BattleSnapshot snapshot = SimulationShadow.Take(mapEvent);
            if (snapshot == null || !SimulationLog.IsEnabled)
            {
                return;
            }

            // The replay is now only ever asked for the A/B NUMBERS -- what this battle would have been without the
            // model, which is the one thing that cannot be observed and must be simulated. The blow-by-blow above is
            // the real thing and needs no replay at all.
            int samples = (RC.simulationLogSamples > 0) ? RC.simulationLogSamples : 1;
            SimulationShadow.ShadowResult withoutModel = SimulationShadow.Run(snapshot, applyCorrection: false, samples: samples);
            SimulationShadow.ShadowResult withModel = SimulationShadow.Run(snapshot, applyCorrection: true, samples: samples);

            SimulationLog.Write(Format(mapEvent, snapshot, withoutModel, withModel, trace));
        }

        private static string Format(MapEvent mapEvent, SimulationShadow.BattleSnapshot snapshot,
            SimulationShadow.ShadowResult withoutModel, SimulationShadow.ShadowResult withModel,
            List<HitRecord> trace)
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
            sb.Append("\n");

            // What really happened, for reference. One roll of the dice, so it will scatter around the averages
            // below rather than match them -- it is the two replays that are the comparison.
            sb.Append("  ACTUAL  winner ").Append(WinnerOf(mapEvent))
              .Append("  ·  casualties  attacker ").Append(mapEvent.AttackerSide.TroopCasualties)
              .Append(", defender ").Append(mapEvent.DefenderSide.TroopCasualties).Append("\n");
            sb.Append("\n");

            sb.Append("  replayed ").Append((RC.simulationLogSamples > 0) ? RC.simulationLogSamples : 1).Append("x each:").Append("\n");
            sb.Append("                     atk win%   atk losses   def losses").Append("\n");
            sb.Append("    BASE (vanilla) ").Append(Row(withoutModel)).Append("\n");
            sb.Append("    RBM  (model on)").Append(Row(withModel)).Append("\n");

            sb.Append("    delta          ").Append(Delta(withoutModel, withModel)).Append("\n");

            AppendWorking(sb, snapshot);
            AppendTrace(sb, trace);

            return sb.ToString().Replace("\n", System.Environment.NewLine);
        }

        /// <summary>
        /// The battle itself, blow by blow -- THE REAL ONE, the battle the game actually fought and the campaign
        /// will actually live with. Not a replay of it.
        ///
        /// Every blow here was recorded as it landed, from inside SimulateHit's postfix, and whether it put its man
        /// down is the game's own verdict rather than our re-roll. That distinction is not pedantry: this WAS taken
        /// from the shadow replay, and the replay had quietly got heroes wrong -- giving a lord the single roll of a
        /// line trooper where the game accumulates his damage -- so the log was killing every lord in it. A
        /// reimplementation can drift from the thing it reimplements, and when it does, it lies with confidence.
        ///
        /// This is the thing the log has never had. The averages say a battle went one way; the matchup table says
        /// what a blow WOULD do. Neither can tell you the archers ran out of arrows in round fifteen and spent the
        /// rest of the fight being cut down with knives in their hands, or that the lancers' charge was spent by
        /// round four and they were never dangerous again. That story only exists in the blows.
        /// </summary>
        private static void AppendTrace(StringBuilder sb, List<HitRecord> trace)
        {
            if (trace == null || trace.Count == 0)
            {
                return;
            }

            sb.Append("\n");
            sb.Append("  the battle, blow by blow -- THE REAL ONE, as the game actually fought it (")
              .Append(trace.Count).Append(" blows):").Append("\n");
            sb.Append("      striker            -> struck                what     weapon            armor   blk%   vanilla  x corr  =  dealt   odds").Append("\n");
            sb.Append("    (a trooper has no health bar: the damage is rolled against his hit points, so it is a").Append("\n");
            sb.Append("     CHANCE he is finished. Only heroes have a pool, and theirs is shown as hp left.)").Append("\n");

            int round = -1;
            foreach (HitRecord hit in trace)
            {
                // A round header, whenever the clock turns. The volley is called out by name: it is the part of a
                // battle auto-resolve never had, and half the model's story happens inside it.
                if (hit.Round != round)
                {
                    round = hit.Round;
                    sb.Append("\n    ── round ").Append(round)
                      .Append(hit.VolleyPhase
                          ? "  ·  VOLLEY -- the bowmen have the field, the foot are walking into it"
                          : (hit.SkirmishPhase
                              ? "  ·  SKIRMISH -- javelins in the air, and the horse are at each other"
                              : "  ·  THE LINES HAVE MET"))
                      .Append("  ·  ").Append(hit.AttackersLeft).Append(" v ").Append(hit.DefendersLeft)
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
                  .Append(Clip(hit.Weapon ?? "-", 15).PadRight(16))
                  .Append(Clip(hit.BodyPart ?? "-", 5).PadRight(6))
                  .Append(Num(hit.ArmorMet, 7))
                  .Append(Num(hit.ShieldBlock * 100f, 7))
                  .Append(Num(hit.VanillaDamage, 10))
                  .Append(Num(hit.Correction, 8))
                  .Append(Num(hit.FinalDamage, 9))
                  // For a trooper: the odds this blow finished him, because that IS the blow -- vanilla rolls the
                  // damage against his hit points and there is no bar behind it. For a hero, who really does have
                  // a pool, what is left of it.
                  // What is left of the man. EVERY man has a pool now, lord and levy alike, so this is no longer
                  // a chance-of-death but an actual figure: he is worn down, and when it reaches nothing he falls.
                  .Append((hit.HitPointsLeft >= 0f)
                      ? ("  hp " + SimulationLog.Fmt(hit.HitPointsLeft).PadLeft(5) + "/" + hit.StruckHitPoints)
                      : "")
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
        private static void AppendSide(StringBuilder sb, string role, string name, int count, List<SimulationShadow.PartyLine> parties)
        {
            // The wounded, if there are any. A side of "one looter" against a battle that killed nine of them is
            // either a band already beaten half to death -- in which case the eight wounded ARE the missing nine --
            // or it is a roster we are failing to read, and the two are indistinguishable until this is printed.
            int wounded = 0;
            if (parties != null)
            {
                foreach (SimulationShadow.PartyLine party in parties)
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
            foreach (SimulationShadow.PartyLine party in parties)
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
        private static void AppendWorking(StringBuilder sb, SimulationShadow.BattleSnapshot snapshot)
        {
            List<CharacterObject> attackers = TopTroops(snapshot.AttackerTroops, 3);
            List<CharacterObject> defenders = TopTroops(snapshot.DefenderTroops, 3);
            if (attackers.Count == 0 || defenders.Count == 0)
            {
                return;
            }

            sb.Append("\n");
            sb.Append("  kit as the model sees it:").Append("\n");
            sb.Append("    troop                            T  arm   head  body   arm   leg    mag  dmg     blk%  weapon           pen  kit").Append("\n");
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
              .Append(Num(k.Head, 6)).Append(Num(k.Body, 6)).Append(Num(k.Arm, 6)).Append(Num(k.Leg, 6))
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
        private static List<CharacterObject> TopTroops(List<SimulationShadow.Soldier> troops, int count)
        {
            Dictionary<CharacterObject, int> tally = new Dictionary<CharacterObject, int>();
            foreach (SimulationShadow.Soldier soldier in troops)
            {
                int n;
                tally.TryGetValue(soldier.Character, out n);
                tally[soldier.Character] = n + 1;
            }
            List<KeyValuePair<CharacterObject, int>> ordered = new List<KeyValuePair<CharacterObject, int>>(tally);
            ordered.Sort((x, y) => y.Value.CompareTo(x.Value));

            List<CharacterObject> result = new List<CharacterObject>();
            for (int i = 0; i < ordered.Count && i < count; i++)
            {
                result.Add(ordered[i].Key);
            }
            return result;
        }

        private static string Row(SimulationShadow.ShadowResult r)
        {
            return Pad(SimulationLog.Fmt(r.AttackerWinRate * 100f) + "%", 11)
                 + Pad(SimulationLog.Fmt(r.AttackerCasualties), 13)
                 + Pad(SimulationLog.Fmt(r.DefenderCasualties), 12);
        }

        /// <summary>What the model did to this battle: the whole point of the record.</summary>
        private static string Delta(SimulationShadow.ShadowResult baseline, SimulationShadow.ShadowResult model)
        {
            return Pad(Signed((model.AttackerWinRate - baseline.AttackerWinRate) * 100f) + "%", 11)
                 + Pad(Signed(model.AttackerCasualties - baseline.AttackerCasualties), 13)
                 + Pad(Signed(model.DefenderCasualties - baseline.DefenderCasualties), 12);
        }

        private static string Signed(float value)
        {
            string text = SimulationLog.Fmt(value);
            return (value > 0f) ? ("+" + text) : text;
        }

        private static string Pad(string text, int width)
        {
            return text.PadLeft(width);
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
