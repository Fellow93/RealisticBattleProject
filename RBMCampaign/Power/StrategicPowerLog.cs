using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using RC = RBMConfig.RBMConfig;

namespace RBMCampaign
{
    /// <summary>
    /// WHAT A PARTY WAS SAID TO BE WORTH, AND WHY.
    ///
    /// StrategicTroopPower replaces vanilla's tier curve with a number built out of a soldier's kit, his training and
    /// his commander's perks -- and a number like that is worth nothing to anyone if it cannot be read. None of its
    /// constants are derived; they are dials, and a dial cannot be turned by someone who can only see the sum. So
    /// every party is written down here as it was priced: the perks that reached it, then each stack of men with what
    /// ONE of them is worth and what he is made of.
    ///
    /// Its own folder -- logs/powerCalculation -- because this is about what the map believes, and the simulation log
    /// next door is about what a battle did. They are different questions and mixing them helps nobody.
    ///
    /// ONCE A DAY PER PARTY, AND THAT IS THE WHOLE DESIGN OF IT. GetPowerOfParty is not called when something
    /// happens; it is called when anyone WONDERS -- every AI lord weighing every other party he can see, on every
    /// tick. Writing a block per call would produce a gigabyte before the first day turned over and would slow the
    /// campaign to a crawl doing it. So a party is written the first time it is priced each day and then left alone,
    /// which is all the fidelity there is to have anyway: the answer does not change between two questions asked in
    /// the same minute.
    ///
    /// Enabled by StrategicPowerLogging in the config file. On while the model is still being calibrated -- the
    /// numbers it prices with are dials, not derivations, and nobody can turn a dial he cannot see. Turn it off once
    /// they settle. Delete this file and its &lt;Compile Include&gt; line to remove it; StrategicTroopPower calls it
    /// in exactly one place.
    /// </summary>
    internal static class StrategicPowerLog
    {
        /// <summary>The hundred a soldier starts with, for showing what his commander multiplied it by.</summary>
        private const float BaselineHitPoints = 100f;

        private static readonly object _fileLock = new object();

        private static bool _fileLogFailed;

        private static bool _fileOpened;

        private static string _launchStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        /// <summary>The day each party was last written down. See the note above about why this exists.</summary>
        private static readonly Dictionary<PartyBase, double> _lastWritten = new Dictionary<PartyBase, double>();

        /// <summary>When the stale entries were last swept. Guarded by <see cref="_fileLock"/>.</summary>
        private static double _lastPruneDay = double.MinValue;

        internal static bool IsEnabled
        {
            get { return RC.rbmCampaignEnabled && RC.strategicPowerEnabled && RC.strategicPowerLoggingEnabled; }
        }

        private static string LogFolderPath
        {
            get { return Path.Combine(RBMConfig.Utilities.GetConfigFolderPath(), "logs", "powerCalculation"); }
        }

        private static string LogFilePath
        {
            get { return Path.Combine(LogFolderPath, "rbm_power_" + _launchStamp + ".log"); }
        }

        /// <summary>A fresh campaign: roll to a new file and forget who was written when.</summary>
        internal static void ResetForNewSession()
        {
            lock (_fileLock)
            {
                _launchStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                _fileLogFailed = false;
                _fileOpened = false;
                _lastWritten.Clear();
                _lastPruneDay = double.MinValue;
            }
        }

        /// <summary>
        /// Whether this party is due to be written down. Asked before anything is built, because building the block
        /// is far more expensive than the pricing it describes -- it walks the perk table and formats a string per
        /// stack, and it must not happen on the thousands of calls that will not be written.
        /// </summary>
        internal static bool ShouldWrite(PartyBase party)
        {
            if (!IsEnabled || party == null || Campaign.Current == null)
            {
                return false;
            }
            double today = CampaignTime.Now.ToDays;
            lock (_fileLock)
            {
                // Once a day, forget the parties nobody has priced since yesterday. An entry that old has already
                // served its purpose -- its party would be rewritten on its next pricing regardless -- and without
                // the sweep this map holds every destroyed party's PartyBase for the rest of the campaign.
                if (today - _lastPruneDay >= 1.0)
                {
                    _lastPruneDay = today;
                    List<PartyBase> stale = null;
                    foreach (KeyValuePair<PartyBase, double> pair in _lastWritten)
                    {
                        if ((today - pair.Value) >= 1.0)
                        {
                            (stale ?? (stale = new List<PartyBase>())).Add(pair.Key);
                        }
                    }
                    if (stale != null)
                    {
                        foreach (PartyBase gone in stale)
                        {
                            _lastWritten.Remove(gone);
                        }
                    }
                }

                double last;
                if (_lastWritten.TryGetValue(party, out last) && (today - last) < 1.0)
                {
                    return false;
                }
                _lastWritten[party] = today;
            }
            return true;
        }

        /// <summary>
        /// One party, priced. Everything here is read back out of StrategicTroopPower rather than recomputed, so this
        /// is a transcript and not a second opinion.
        /// </summary>
        internal static void WriteParty(PartyBase party, BattleSideEnum side, MapEvent.PowerCalculationContext context,
            float morale, float total)
        {
            if (!IsEnabled || party == null)
            {
                return;
            }
            try
            {
                Write(BuildBlock(party, side, context, morale, total));
            }
            catch (Exception)
            {
                // A log that throws would take the campaign down with it, from inside an AI tick. It is a log.
            }
        }

        private static string BuildBlock(PartyBase party, BattleSideEnum side,
            MapEvent.PowerCalculationContext context, float morale, float total)
        {
            StringBuilder sb = new StringBuilder();
            MobileParty mobileParty = party.MobileParty;

            sb.Append(Environment.NewLine);
            sb.Append("================================================================================").Append(Environment.NewLine);
            sb.Append(CampaignTime.Now.ToString()).Append("   ").Append(NameOf(party)).Append(Environment.NewLine);
            sb.Append("  side=").Append(side).Append("   context=").Append(context);
            if (mobileParty != null)
            {
                sb.Append("   leader=").Append((mobileParty.LeaderHero != null) ? mobileParty.LeaderHero.Name.ToString() : "(none)");
                sb.Append("   army=").Append((mobileParty.Army != null) ? mobileParty.Army.Name.ToString() : "(none)");
            }
            sb.Append(Environment.NewLine);

            // ---- what the commander is worth, in the only currency this model prices him in ------------------
            sb.Append("--------------------------------------------------------------------------------").Append(Environment.NewLine);
            sb.Append("COMMANDER (commander track only -- the captain track needs formations, which the map has none")
              .Append(Environment.NewLine);
            sb.Append("of; see StrategicTroopPower). He reaches his men here as HIT POINTS and nothing else: every")
              .Append(Environment.NewLine);
            sb.Append("troop-HP perk in the game is PartyLeader, his damage perks are hand-coded into vanilla's own")
              .Append(Environment.NewLine);
            sb.Append("blow, and his morale is already in the morale factor at the foot of this block.")
              .Append(Environment.NewLine);
            sb.Append("These lines are ExplainCommandedHealth's OWN record of which perks fired -- not a second list.")
              .Append(Environment.NewLine);

            if (mobileParty == null)
            {
                sb.Append("  (not a mobile party -- no commander)").Append(Environment.NewLine);
            }
            else
            {
                // One representative per distinct pool, because the perks are conditioned on what the man IS
                // (mounted, ranged, infantry) and a party of one kind would otherwise print the same lines per stack.
                HashSet<string> seen = new HashSet<string>();
                bool anyFired = false;

                for (int i = 0; i < party.MemberRoster.Count; i++)
                {
                    CharacterObject troop = party.MemberRoster.GetElementCopyAtIndex(i).Character;
                    if (troop == null)
                    {
                        continue;
                    }

                    ExplainedNumber hp = SimulationTroopHitPoints.ExplainCommandedHealth(troop, party, dismounted: false);
                    List<string> fired = new List<string>();

                    // GetLines() PREPENDS the base entry -- the flat hundred a soldier starts with. It is not a perk,
                    // and printed as one it reads as a bonus nobody granted. Skipped by POSITION and not by value: it
                    // is always first, whereas a perk that happened to add exactly the base amount would be dropped
                    // by any test on the number.
                    int index = 0;
                    foreach (var line in hp.GetLines())
                    {
                        if (index++ == 0)
                        {
                            continue;
                        }
                        fired.Add(line.name + " " + Fmt(line.number));
                    }

                    string key = string.Join("|", fired.ToArray());
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    if (fired.Count == 0)
                    {
                        sb.Append("  ").Append(Pad(troop.Name.ToString(), 28))
                          .Append("hp ").Append(Fmt(hp.ResultNumber)).Append("   (his commander adds nothing)")
                          .Append(Environment.NewLine);
                        continue;
                    }
                    anyFired = true;
                    sb.Append("  ").Append(Pad(troop.Name.ToString(), 28))
                      .Append("hp ").Append(Fmt(hp.BaseNumber)).Append(" -> ").Append(Fmt(hp.ResultNumber))
                      .Append("  x").Append(Fmt(hp.ResultNumber / BaselineHitPoints)).Append(Environment.NewLine);
                    foreach (string f in fired)
                    {
                        sb.Append("        ").Append(f).Append(Environment.NewLine);
                    }
                }
                if (!anyFired)
                {
                    sb.Append("  -- this party's commander brings nothing to any of his men --").Append(Environment.NewLine);
                }
            }

            // ---- the men -------------------------------------------------------------------------------------
            sb.Append("--------------------------------------------------------------------------------").Append(Environment.NewLine);
            sb.Append("STACKS   (power/man is ONE soldier: offense x active x passive, + mount)").Append(Environment.NewLine);
            sb.Append("         T is his tier and arm is what he FIGHTS as (rng = the game fields him as a shooter,")
              .Append(Environment.NewLine);
            sb.Append("         not merely that he owns a bow). Both are here because the calibration target is")
              .Append(Environment.NewLine);
            sb.Append("         'average ranged ~ average melee OF THE SAME TIER', which cannot be measured without")
              .Append(Environment.NewLine);
            sb.Append("         them. energyJ/charge are the RAW inputs behind 'ranged', printed rather than inferred.")
              .Append(Environment.NewLine);
            sb.Append("         energyJ is what the launcher throws, in joules -- RBM's own physics off the draw")
              .Append(Environment.NewLine);
            sb.Append("         weight, NOT its tier. Tier is a per-class price curve and is not comparable across")
              .Append(Environment.NewLine);
            sb.Append("         bows, crossbows and slings; see StrategicTroopPower.LauncherEnergyOf.")
              .Append(Environment.NewLine);
            sb.Append("         mount is a SHARE of his own power that his horse adds -- the same mount is the same")
              .Append(Environment.NewLine);
            sb.Append("         percentage whoever rides it, so lighter cavalry gain less than armoured. The share is set")
              .Append(Environment.NewLine);
            sb.Append("         by the mount's survivability (health + barding, the only place barding is priced -- the")
              .Append(Environment.NewLine);
            sb.Append("         horse's armour, not the rider's, so it is absent from 'armour'). Charge is the rider's")
              .Append(Environment.NewLine);
            sb.Append("         harder blow and stays in 'offense'. Shown already in power/man units. See MountFractionOf.")
              .Append(Environment.NewLine);
            sb.Append("  ").Append(Pad("troop", 28)).Append(Pad("T", 3)).Append(Pad("arm", 5))
              .Append(Pad("men", 9)).Append(Pad("power/man", 11))
              .Append(Pad("offense", 10)).Append(Pad("melee", 9)).Append(Pad("ranged", 9))
              .Append(Pad("energyJ", 9)).Append(Pad("mSkill", 8)).Append(Pad("rSkill", 8)).Append(Pad("charge", 8))
              .Append(Pad("active", 8)).Append(Pad("passive", 9)).Append(Pad("armour", 8))
              .Append(Pad("shield", 8)).Append(Pad("mount", 8)).Append(Pad("leader", 8)).Append(Pad("terrain", 9))
              .Append("subtotal").Append(Environment.NewLine);

            MilitaryPowerModel model = Campaign.Current.Models.MilitaryPowerModel;
            bool estimated = context == MapEvent.PowerCalculationContext.Estimated;

            for (int i = 0; i < party.MemberRoster.Count; i++)
            {
                TroopRosterElement element = party.MemberRoster.GetElementCopyAtIndex(i);
                CharacterObject troop = element.Character;
                if (troop == null)
                {
                    continue;
                }
                int healthy = element.Number - element.WoundedNumber;
                if (healthy <= 0)
                {
                    continue;
                }

                StrategicTroopPower.PowerBreakdown detail = StrategicTroopPower.Explain(troop);
                float power = StrategicTroopPower.PowerOf(troop);
                bool fellBack = power <= 0f;
                if (fellBack)
                {
                    power = model.GetDefaultTroopPower(troop);
                }

                float leaderMod = (party.LeaderHero != null) ? party.LeaderHero.PowerModifier : 0f;
                // Terrain is shown always, but applied ONLY in a siege (see TryGetPowerOfParty): on a field battle it
                // is what vanilla's arm-vs-terrain heuristic WOULD have done to him, kept in the column so the
                // distortion this model drops stays visible, but left out of the subtotal; in a siege the wall is a
                // real fact and it does go in.
                bool siege = context == MapEvent.PowerCalculationContext.Siege;
                float contextMod = estimated ? 0f : model.GetContextModifier(troop, side, context);
                float appliedContext = siege ? contextMod : 0f;
                // The commander's hit-point factor, exactly as the pricing applied it (see TryGetPowerOfParty).
                // Left out, the rows stop summing to TOTAL the moment a commander perk fires -- which is precisely
                // when someone is reading this file to see what the perk did.
                float hpFactor = StrategicTroopPower.HealthFactorOf(troop, party);
                float subtotal = healthy * power * hpFactor * (1f + leaderMod + appliedContext);

                sb.Append("  ").Append(Pad(troop.Name.ToString(), 28))
                  .Append(Pad(troop.IsHero ? "H" : troop.Tier.ToString(), 3))
                  // The game's own four arms, not three. Folding horse archers in with foot archers made the
                  // archer average a lie: a mounted archer carries barding and a charge, so he outweighs every
                  // foot archer in his own bucket, and the whole "archers vs infantry" measurement was reading
                  // against a mean no foot archer could reach.
                  .Append(Pad(detail.IsShooter
                      ? (troop.IsMounted ? "ha" : "rng")
                      : (troop.IsMounted ? "cav" : "inf"), 5))
                  .Append(Pad(healthy.ToString() + ((element.WoundedNumber > 0) ? ("/" + element.Number) : ""), 9))
                  .Append(Pad(Fmt(power), 11))
                  .Append(Pad(Fmt(detail.Offense), 10))
                  .Append(Pad(Fmt(detail.Melee), 9))
                  .Append(Pad(Fmt(detail.Ranged), 9))
                  .Append(Pad((detail.Ranged > 0f) ? Fmt(detail.LauncherTier) : "-", 9))
                  .Append(Pad(Fmt(detail.MeleeSkill), 8))
                  .Append(Pad((detail.Ranged > 0f) ? Fmt(detail.RangedSkill) : "-", 8))
                  .Append(Pad((detail.ChargeDamage > 0f) ? Fmt(detail.ChargeDamage) : "-", 8))
                  .Append(Pad(Fmt(detail.ActiveFactor), 8))
                  .Append(Pad(Fmt(detail.PassiveFactor), 9))
                  .Append(Pad(Fmt(detail.WeightedArmor), 8))
                  .Append(Pad(detail.HasShield ? Fmt(detail.ShieldTier) : "-", 8))
                  .Append(Pad((detail.MountBonus > 0f) ? Fmt(detail.MountBonus) : "-", 8))
                  .Append(Pad("+" + Fmt(leaderMod), 8))
                  .Append(Pad((contextMod >= 0f ? "+" : "") + Fmt(contextMod), 9))
                  .Append(Fmt(subtotal));
                if (fellBack)
                {
                    sb.Append("   <- UNMEASURABLE, fell back to vanilla tier power");
                }
                else if (detail.Sets > 1)
                {
                    sb.Append("   (avg of ").Append(detail.Sets).Append(" kits)");
                }

                // What he actually looses with. RBM's ranged tier reads MissileSpeed as a DRAW WEIGHT and was fitted
                // to a bow's 60-160; anything else on that formula lands wherever it lands. A bare tier of 6.5 says
                // a weapon maxed out but not WHICH weapon or why, and that is the difference between a noble bow and
                // a mis-scaled one. So the weapon and its raw speed are named.
                if (detail.LauncherName != null)
                {
                    sb.Append(Environment.NewLine).Append("      shoots: ").Append(detail.LauncherName)
                      .Append("  drawWeight=").Append(Fmt(detail.LauncherSpeed))
                      .Append("lb  -> ").Append(Fmt(detail.LauncherTier)).Append(" J");
                }
                sb.Append(Environment.NewLine);
            }

            sb.Append("--------------------------------------------------------------------------------").Append(Environment.NewLine);
            sb.Append("TOTAL  ").Append(Fmt(total / ((morale > 0f) ? morale : 1f)))
              .Append("  x morale ").Append(Fmt(morale))
              .Append("  =  ").Append(Fmt(total)).Append(Environment.NewLine);
            return sb.ToString();
        }

        private static string NameOf(PartyBase party)
        {
            try
            {
                return (party.Name != null) ? party.Name.ToString() : "(unnamed party)";
            }
            catch (Exception)
            {
                return "(unnamed party)";
            }
        }

        private static string Pad(string value, int width)
        {
            if (value == null)
            {
                value = "";
            }
            if (value.Length >= width)
            {
                // Never truncate silently into a column that looks like a number: better a ragged row than a lie.
                return value + " ";
            }
            return value.PadRight(width);
        }

        internal static string Fmt(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static void Write(string block)
        {
            if (_fileLogFailed)
            {
                return;
            }
            EnsureFileOpen();
            lock (_fileLock)
            {
                if (!_fileOpened)
                {
                    return;
                }
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        File.AppendAllText(LogFilePath, block);
                        return;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(2);
                    }
                    catch
                    {
                        _fileLogFailed = true;
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Opens the log and stamps the settings on it the first time there is something to say. The settings belong
        /// at the top because every number below is meaningless without them -- rbmCombatEnabled alone decides which
        /// of two offense models produced the whole file.
        /// </summary>
        private static void EnsureFileOpen()
        {
            if (!IsEnabled)
            {
                return;
            }
            lock (_fileLock)
            {
                if (_fileOpened || _fileLogFailed)
                {
                    return;
                }
                try
                {
                    Directory.CreateDirectory(LogFolderPath);
                    LogRetention.PruneOldest(LogFolderPath, "rbm_power_*.log");

                    StringBuilder header = new StringBuilder();
                    header.Append("RBM strategic troop power log — ").Append(DateTime.Now).Append(Environment.NewLine);
                    header.Append(Environment.NewLine);
                    header.Append("What the map believes a party is worth, and why. This is NOT auto-resolve: the blow").Append(Environment.NewLine);
                    header.Append("model is untouched and has its own log next door in logs/simulation. This is the").Append(Environment.NewLine);
                    header.Append("number the AI reads before it decides whether it can beat you.").Append(Environment.NewLine);
                    header.Append(Environment.NewLine);
                    header.Append("Each party is written the first time it is priced on any given day, and then left").Append(Environment.NewLine);
                    header.Append("alone until the next -- the number does not change between two questions asked in").Append(Environment.NewLine);
                    header.Append("the same minute, and the AI asks thousands of them.").Append(Environment.NewLine);
                    header.Append(Environment.NewLine);
                    header.Append("power/man = (offense x activeFactor x passiveFactor + mount) / powerScale, averaged over his kits.").Append(Environment.NewLine);
                    header.Append("  offense       what one blow of his achieves (melee and ranged blended by his kind)").Append(Environment.NewLine);
                    header.Append("  activeFactor  how much longer he lives for the blows he turns aside (skill, shield)").Append(Environment.NewLine);
                    header.Append("  passiveFactor how much longer he lives for the armour the rest must get through").Append(Environment.NewLine);
                    header.Append("  mount         a SHARE of his own power that his horse adds, not a flat sum -- so the same").Append(Environment.NewLine);
                    header.Append("                mount is the same percentage whoever rides it, and lighter cavalry gain less").Append(Environment.NewLine);
                    header.Append("                than armoured. The share is set by the mount's survivability (its health and").Append(Environment.NewLine);
                    header.Append("                barding, the only place barding is priced), off a barded warhorse as yardstick.").Append(Environment.NewLine);
                    header.Append("                0 for a man on foot. Shown in the column already in power/man units.").Append(Environment.NewLine);
                    header.Append("  powerScale    the divisor that puts him back on the scale vanilla prices men in,").Append(Environment.NewLine);
                    header.Append("                0.40 to 2.56 by tier. It buys nothing except that vanilla's own").Append(Environment.NewLine);
                    header.Append("                hardcoded power thresholds -- the 1000f army floor, the 100f siege").Append(Environment.NewLine);
                    header.Append("                damper -- mean again what they were written to mean. Ratios do not").Append(Environment.NewLine);
                    header.Append("                notice it. The three columns above are printed RAW, before it, so").Append(Environment.NewLine);
                    header.Append("                they do not multiply out to power/man on the page: they multiply").Append(Environment.NewLine);
                    header.Append("                out to power/man x powerScale. See StrategicTroopPower.PowerScale,").Append(Environment.NewLine);
                    header.Append("                which is MEASURED off this log and must be re-measured if the").Append(Environment.NewLine);
                    header.Append("                settings below move.").Append(Environment.NewLine);
                    header.Append("  leader        his own party's commander perks (applied)").Append(Environment.NewLine);
                    header.Append("  terrain       vanilla's arm-vs-terrain modifier -- shown always, APPLIED ONLY IN A SIEGE.").Append(Environment.NewLine);
                    header.Append("                On a field battle this model prices the man, not the ground he stands on, so").Append(Environment.NewLine);
                    header.Append("                the column is kept only to keep the distortion it used to add visible (an").Append(Environment.NewLine);
                    header.Append("                archer at half power in a wood, level with a looter vanilla files as infantry)").Append(Environment.NewLine);
                    header.Append("                and is NOT in subtotal. In a siege the wall is a real fact and it IS applied.").Append(Environment.NewLine);
                    header.Append(Environment.NewLine);
                    header.Append("Settings:").Append(Environment.NewLine);
                    header.Append("  rbmCombatEnabled              = ").Append(RC.rbmCombatEnabled).Append(Environment.NewLine);
                    header.Append("     (picks the offense model outright: ")
                          .Append(RC.rbmCombatEnabled ? "RBM's class ceilings" : "vanilla's listed damage").Append(")").Append(Environment.NewLine);
                    header.Append("  strategicPowerEnabled         = ").Append(RC.strategicPowerEnabled).Append(Environment.NewLine);
                    header.Append("  simulationPerkSystem          = ").Append(RC.simulationPerkSystem).Append(Environment.NewLine);
                    header.Append("     (gates the commander's hit-point perks; see SimulationTroopHitPoints)").Append(Environment.NewLine);
                    header.Append("  armorMultiplier               = ").Append(Fmt(RC.armorMultiplier)).Append(Environment.NewLine);
                    header.Append("  OneHandedThrustDamageBonus    = ").Append(Fmt(RC.OneHandedThrustDamageBonus)).Append(Environment.NewLine);
                    header.Append("     (read by RBM's melee TIER formula, so it moves ranged/shield/horse numbers)").Append(Environment.NewLine);
                    header.Append("  powerScale                    = ").Append(Fmt(StrategicTroopPower.PowerScale)).Append(Environment.NewLine);
                    header.Append("     (measured off a run of THIS log; the three settings above all move the raw").Append(Environment.NewLine);
                    header.Append("      number it divides, so a change to any of them makes this value a lie until").Append(Environment.NewLine);
                    header.Append("      it is re-measured -- sum(men x power/man x powerScale) / sum(men x vanillaTier))").Append(Environment.NewLine);

                    File.WriteAllText(LogFilePath, header.ToString());
                    _fileOpened = true;
                }
                catch
                {
                    _fileLogFailed = true;
                }
            }
        }
    }
}
