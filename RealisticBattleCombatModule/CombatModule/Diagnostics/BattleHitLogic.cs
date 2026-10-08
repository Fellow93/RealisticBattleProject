using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMCombat
{
    /// <summary>
    /// Writes down a real battle, blow by blow, in the same columns the auto-resolve trace uses -- so the model's
    /// claim about what a battle is can be held up against a battle that actually happened.
    ///
    /// The auto-resolve model says the archers own the approach and are helpless once it is crossed; that a javelin
    /// is worth more than the man who throws it; that a charge is spent once and a lancer afterwards is a man on a
    /// tired horse; that armour, not tier, decides who walks away. None of that can be argued into being true. But a
    /// battle fought on the field under RBM's combat model answers all of it, and this is what asks the question:
    /// the same striker, struck, what-he-was-doing, weapon, armour-met, damage columns, so a line of one log can be
    /// read straight against a line of the other.
    ///
    /// What is NOT the same, and cannot be, is the clock. The simulation has rounds; a battle has seconds. So the
    /// headers here count time rather than rounds -- and mark the moment the lines actually meet, which is the real
    /// thing the simulation's "volley" is a model OF. Everything logged before that moment was landed across open
    /// ground by men who had not reached each other yet, which is precisely the claim to be tested.
    /// </summary>
    public class BattleHitLogic : MissionLogic
    {
        /// <summary>How often the log stops to say what time it is and who is still standing.</summary>
        private const float HeaderInterval = 15f;

        /// <summary>A tally of one kind of blow across the battle: how many, how much, and how many men it put down.</summary>
        private class Tally
        {
            public int Blows;

            public float Damage;

            public int Kills;
        }

        private readonly Dictionary<string, Tally> _attackerTallies = new Dictionary<string, Tally>();

        private readonly Dictionary<string, Tally> _defenderTallies = new Dictionary<string, Tally>();

        /// <summary>Melee blocks per kind, credited to the side of the man who blocked (indexed by MeleeBlockKind).</summary>
        private readonly int[] _attackerBlocks = new int[RBMConfig.MeleeBlock.KindCount];

        private readonly int[] _defenderBlocks = new int[RBMConfig.MeleeBlock.KindCount];

        /// <summary>
        /// Direct melee hits each side TOOK -- a man's weapon landing, not a horse charge, kick or bash -- so a
        /// side's blocks and the hits that got past them sit on one row.
        /// </summary>
        private int _attackerMeleeHitsTaken;

        private int _defenderMeleeHitsTaken;

        private float _nextHeader;

        private int _blows;

        /// <summary>
        /// When the lines met -- the first melee blow of the battle, from anyone at all. Everything before it was
        /// landed at a distance, on men who could not yet answer, and that is the whole of what the simulation's
        /// volley is trying to be. Negative until it happens.
        /// </summary>
        private float _linesMetAt = -1f;

        /// <summary>
        /// Whether this mission is one we actually log. Set once in <see cref="AfterStart"/> and read by every
        /// callback after it -- a mission that is not a real battle is left alone entirely, no file opened.
        /// </summary>
        private bool _logging;

        public override void AfterStart()
        {
            _logging = false;
            MissileAimTrace.Reset();
            if (!BattleHitLog.IsEnabled || !IsRealBattle())
            {
                return;
            }
            _logging = true;

            StringBuilder header = new StringBuilder();
            header.Append("RBM battle log — every blow of a real battle, as it landed.").Append("\n");
            header.Append("\n");
            header.Append("The same columns as the auto-resolve trace in logs/simulation, so the two can be read").Append("\n");
            header.Append("against each other: what the man was doing, what he hit, what armour it met, what it did.").Append("\n");
            header.Append("\n");
            header.Append("  rbmCombatEnabled    = ").Append(RBMConfig.RBMConfig.rbmCombatEnabled).Append("\n");
            header.Append("  postureEnabled      = ").Append(RBMConfig.RBMConfig.postureEnabled).Append("\n");
            header.Append("  armorMultiplier     = ").Append(BattleHitLog.Fmt(RBMConfig.RBMConfig.armorMultiplier)).Append("\n");
            header.Append("  bluntTraumaMultiplier = ").Append(BattleHitLog.Fmt(RBMConfig.RBMConfig.bluntTraumaMultiplier)).Append("\n");
            header.Append("  armorEffectivenessMultiplier = ").Append(BattleHitLog.Fmt(RBMConfig.RBMConfig.armorEffectivenessMultiplier)).Append("\n");
            header.Append("     (the armor column below is the item value, before this scales it)").Append("\n");
            header.Append("\n");
            header.Append("  raw    = what the blow would have done to a naked man (dealt + absorbed by armour)").Append("\n");
            header.Append("  armor  = the armour standing over the part it actually landed on").Append("\n");
            header.Append("  dealt  = what went through").Append("\n");
            header.Append("\n");
            header.Append("  blocked melee blows are logged too, 'what' naming the block (the posture system's own cases):").Append("\n");
            header.Append("    chamber  = chamber block            w-block / w-parry   = weapon block / perfect weapon parry").Append("\n");
            header.Append("    sh-block = shield block             sh-parry            = perfect shield parry").Append("\n");
            header.Append("    sh-wrong = shield raised to the wrong side").Append("\n");
            header.Append("\n");
            header.Append("  SHOT/LAND = AI bow/crossbow aim trace (see MissileAimTrace): predRange ~ tgtRange means the aim").Append("\n");
            header.Append("              suited the launch; landRange short of predRange means the flight differs from the model").Append("\n");
            header.Append("\n");
            header.Append("  head = a head hit, on the line before its blow: where it landed from the struck man's eyes (m, along his").Append("\n");
            header.Append("         look: fwd ahead, side, up), dot = blow direction against his look (more negative = more head-on),").Append("\n");
            header.Append("         nz = contact normal height, head-on = passed the angle test, zone = landed in the face zone,").Append("\n");
            header.Append("         FACE = counted as a face hit (swings need both, thrusts and missiles only head-on).").Append("\n");
            header.Append("         A posture crush-through can add a head row with nz 0; it is never a face hit.").Append("\n");
            header.Append("\n");
            header.Append("  MORDHAU = prototype mordhau usage (MordhauGrip.cs), one line per melee collision of it, landed or blocked,").Append("\n");
            header.Append("         before that blow's own row. Weapon-local z runs along the sword from the normal grip (0), + toward").Append("\n");
            header.Append("         the tip; the mordhau hand sits at handZ on the blade, the hilt end is z <= ~0.2, the pommel end below 0.").Append("\n");
            header.Append("         cdow = engine CollisionDistanceOnWeapon, realLen = GetRealWeaponLength (hand to tip), offZ = weapon offset z,").Append("\n");
            header.Append("         lever = hand to pommel end, leverHit = |cdow| / lever, the hit point the swing magnitude used,").Append("\n");
            header.Append("         dmgType/mag/dealt = the collision's damage type, base magnitude, inflicted damage,").Append("\n");
            header.Append("         hand->hit / eye->hit = metres from the attacker's weapon hand bone / eyes to the contact point.").Append("\n");
            header.Append("         Collision follows the reversed grip when hand->hit ~ cdow ~ handZ (the guard) to lever (pommel end).").Append("\n");
            header.Append(MordhauGrip.LastApplySummary);
            header.Append("\n");
            header.Append("  weapon = the weapon class; a sword with the prototype modes shows its mode: 2HS/2H, 2HS/HalfSword, 2HS/Mordhau.").Append("\n");
            header.Append("  USAGE = a sword with the prototype modes changed grip: who (AI / player), usage from -> to, mission time.").Append("\n");
            header.Append("         AI agents are held in the normal grip (WeaponModesAiLock.cs), so an AI USAGE line is a bug.").Append("\n");
            header.Append("  every melee row (landed or blocked) ends with the reach of the contact:").Append("\n");
            header.Append("    cdow = engine CollisionDistanceOnWeapon (m from the hand along the weapon), offZ = the engine's current").Append("\n");
            header.Append("    weapon offset along the weapon (the game's own impact share uses cdow / (real length + offZ)), mtd = mounted.").Append("\n");
            header.Append("\n");
            header.Append("    striker            -> struck                what     weapon           part    armor      raw   absorb    dealt   hp").Append("\n");

            BattleHitLog.StartBattle(header.ToString().Replace("\n", System.Environment.NewLine));

            _nextHeader = 0f;
            _blows = 0;
            _linesMetAt = -1f;
            System.Array.Clear(_attackerBlocks, 0, _attackerBlocks.Length);
            System.Array.Clear(_defenderBlocks, 0, _defenderBlocks.Length);
            _attackerMeleeHitsTaken = 0;
            _defenderMeleeHitsTaken = 0;
        }

        /// <summary>
        /// A blocked or parried melee blow never reaches <see cref="OnAgentHit"/> (the engine registers no blow for
        /// it), so it is written down here, from the melee collision itself.
        /// </summary>
        public override void OnMeleeHit(Agent attacker, Agent victim, bool isCanceled, AttackCollisionData collisionData)
        {
            if (_logging && attacker != null && attacker.IsHuman)
            {
                LogMordhau(attacker, victim, in collisionData);
            }
            if (!_logging || attacker == null || victim == null || !attacker.IsHuman || !victim.IsHuman
                || !attacker.IsEnemyOf(victim))
            {
                return;
            }
            RBMConfig.MeleeBlockKind kind = RBMConfig.MeleeBlock.Classify(in collisionData);
            if (kind == RBMConfig.MeleeBlockKind.None)
            {
                return;
            }

            float now = Mission.Current.CurrentTime;
            // A blow met by a blade or a shield is the lines meeting as surely as one that lands.
            if (_linesMetAt < 0f)
            {
                _linesMetAt = now;
            }
            if (now >= _nextHeader)
            {
                WriteHeader(now);
                _nextHeader = now + HeaderInterval;
            }

            (IsAttacker(victim) ? _attackerBlocks : _defenderBlocks)[(int)kind]++;

            int slot = collisionData.AffectorWeaponSlotOrMissileIndex;
            MissionWeapon weapon = (slot >= 0) ? attacker.Equipment[slot] : MissionWeapon.Invalid;

            StringBuilder sb = new StringBuilder();
            sb.Append("    ").Append(IsAttacker(attacker) ? "A " : "D ")
              .Append(Clip(Name(attacker), 19).PadRight(20))
              .Append("-> ").Append(Clip(Name(victim), 21).PadRight(22))
              .Append(RBMConfig.MeleeBlock.Label(kind).PadRight(9))
              .Append(Clip(Weapon(weapon), 16).PadRight(17))
              .Append("-".PadRight(7))
              .Append("-".PadLeft(7))
              .Append("-".PadLeft(9))
              .Append("-".PadLeft(9))
              .Append("-".PadLeft(9))
              .Append("   hp ").Append(BattleHitLog.Fmt(victim.Health).PadLeft(5))
              .Append("/").Append(BattleHitLog.Fmt(victim.HealthLimit))
              .Append(Reach(attacker, in collisionData));

            BattleHitLog.Write(sb.ToString());
        }

        /// <summary>
        /// Where on the weapon a melee contact was and the engine's current weapon offset, for every melee row, so the
        /// normal grip's reach (mounted too) can be read against the prototype modes'.
        /// </summary>
        private static string Reach(Agent striker, in AttackCollisionData collisionData)
        {
            float offZ = 0f;
            try
            {
                offZ = striker.GetCurWeaponOffset().z;
            }
            catch
            {
                // agent gone mid-callback: log 0
            }
            return "   cdow " + F3(collisionData.CollisionDistanceOnWeapon) + " offZ " + F3(offZ) + (striker.HasMount ? " mtd" : "");
        }

        /// <summary>
        /// The prototype mordhau test (MordhauGrip.cs): where on the reversed sword a collision really was, to tell
        /// whether the engine's melee collision follows the usage's hand frame. Only for that usage, so the few engine
        /// queries here cost nothing in a normal battle.
        /// </summary>
        private static void LogMordhau(Agent attacker, Agent victim, in AttackCollisionData collisionData)
        {
            int slot = collisionData.AffectorWeaponSlotOrMissileIndex;
            if (collisionData.IsMissile || slot < 0)
            {
                return;
            }
            MissionWeapon weapon = attacker.Equipment[slot];
            WeaponComponentData usage = weapon.IsEmpty ? null : weapon.CurrentUsageItem;
            if (!MordhauGrip.IsMordhau(usage))
            {
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("    MORDHAU ").Append(Clip(Name(attacker), 19))
              .Append(" -> ").Append(victim != null ? Clip(Name(victim), 21) : "(scene object)")
              .Append("  ").Append(collisionData.CollisionResult)
              .Append(" strike=").Append(collisionData.StrikeType == (int)StrikeType.Swing ? "swing" : "thrust")
              .Append(" dir=").Append(collisionData.AttackDirection)
              .Append(" usage=").Append(weapon.CurrentUsageIndex).Append(":").Append(usage.WeaponDescriptionId)
              .Append(" cdow=").Append(F3(collisionData.CollisionDistanceOnWeapon))
              .Append(" realLen=").Append(F3(usage.GetRealWeaponLength()))
              .Append(" L=").Append(F3(usage.WeaponLength * 0.01f));
            if (MordhauGrip.TryGetHandPosition(weapon.Item, usage, out float handZ, out float bladeLength))
            {
                sb.Append(" handZ=").Append(F3(handZ));
            }
            if (MordhauGrip.TryGetSwingLever(weapon.Item, usage, out float leverLength, out float leverCenterOfMass))
            {
                // What the swing magnitude used: hand-to-pommel-end lever and the contact's share of it.
                sb.Append(" lever=").Append(F3(leverLength))
                  .Append(" leverHit=").Append(F3(MordhauGrip.GetImpactPointOnLever(collisionData.CollisionDistanceOnWeapon, leverLength)));
            }
            sb.Append(" offZ=").Append(F3(attacker.GetCurWeaponOffset().z))
              .Append(" dmgType=").Append((DamageTypes)collisionData.DamageType)
              .Append(" mag=").Append(F3(collisionData.BaseMagnitude))
              .Append(" dealt=").Append(collisionData.InflictedDamage)
              .Append(" part=").Append(collisionData.VictimHitBodyPart);

            Vec3 hit = collisionData.CollisionGlobalPosition;
            try
            {
                sb.Append(" eye->hit=").Append(F3((hit - attacker.GetEyeGlobalPosition()).Length));
                Vec3 handPos = Vec3.Invalid;
                MBAgentVisuals visuals = attacker.AgentVisuals;
                if (visuals != null && visuals.GetSkeleton() != null)
                {
                    MatrixFrame body = visuals.GetGlobalFrame();
                    MatrixFrame handLocal = visuals.GetSkeleton().GetBoneEntitialFrameWithIndex(attacker.Monster.MainHandItemBoneIndex);
                    handPos = body.TransformToParent(in handLocal).origin;
                    sb.Append(" hand->hit=").Append(F3((hit - handPos).Length));
                }
                // No "visible weapon axis" columns: the weapon entity's global frame is not the in-hand weapon frame
                // (it carries neither the usage frame nor a reliably current bone pose, and its local axes follow the
                // mesh, not the blade), so measurements along it disagreed with hand->hit by a metre or more.
            }
            catch
            {
                sb.Append(" (geometry unavailable)");
            }
            BattleHitLog.Write(sb.ToString());
        }

        private static string F3(float value)
        {
            return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent, in MissionWeapon affectorWeapon,
            in Blow blow, in AttackCollisionData attackCollisionData)
        {
            if (!_logging || affectedAgent == null || affectorAgent == null)
            {
                return;
            }

            // A horse that rides a man down is not the one who decided to: the blow belongs to its rider, and a log
            // that credits the horse tells you nothing about who won the battle.
            Agent striker = affectorAgent;
            if (!striker.IsHuman && striker.RiderAgent != null)
            {
                striker = striker.RiderAgent;
            }

            // Only men fighting men. A horse taking an arrow matters to the man on it, but it is not a blow between
            // soldiers and it is not what the simulation is claiming anything about.
            if (!affectedAgent.IsHuman || !striker.IsHuman)
            {
                return;
            }

            float now = Mission.Current.CurrentTime;
            string what = Describe(attackCollisionData, blow, affectorWeapon, striker);

            // The moment the lines meet, which the battle itself decides and nobody has to model: the first time
            // anyone reaches anyone with a weapon in his hand.
            if (_linesMetAt < 0f && what == "melee")
            {
                _linesMetAt = now;
            }

            if (now >= _nextHeader)
            {
                WriteHeader(now);
                _nextHeader = now + HeaderInterval;
            }

            float dealt = blow.InflictedDamage;
            float absorbed = attackCollisionData.AbsorbedByArmor;
            float armor = affectedAgent.GetBaseArmorEffectivenessForBodyPart(blow.VictimBodyPart);
            bool downed = !affectedAgent.IsActive() || affectedAgent.Health <= 0f;

            Record(striker, what, dealt, downed);
            _blows++;

            // A horse as the affector with no charge speed is a trample, still the horse's doing, not a weapon hit.
            if (what == "melee" && affectorAgent.IsHuman && striker.IsEnemyOf(affectedAgent))
            {
                if (IsAttacker(affectedAgent))
                {
                    _attackerMeleeHitsTaken++;
                }
                else
                {
                    _defenderMeleeHitsTaken++;
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("    ").Append(IsAttacker(striker) ? "A " : "D ")
              .Append(Clip(Name(striker), 19).PadRight(20))
              .Append("-> ").Append(Clip(Name(affectedAgent), 21).PadRight(22))
              .Append(Clip(what, 8).PadRight(9))
              .Append(Clip(Weapon(affectorWeapon), 16).PadRight(17))
              .Append(Clip(Part(blow.VictimBodyPart), 6).PadRight(7))
              .Append(Num(armor, 7))
              .Append(Num(dealt + absorbed, 9))
              .Append(Num(absorbed, 9))
              .Append(Num(dealt, 9))
              .Append("   hp ").Append(BattleHitLog.Fmt(affectedAgent.Health).PadLeft(5))
              .Append("/").Append(BattleHitLog.Fmt(affectedAgent.HealthLimit))
              .Append(attackCollisionData.AttackBlockedWithShield ? "   shield" : "")
              .Append(downed ? "   DOWN" : "")
              .Append(what == "melee" && affectorAgent.IsHuman ? Reach(striker, in attackCollisionData) : "");

            BattleHitLog.Write(sb.ToString());
        }

        protected override void OnEndMission()
        {
            if (!_logging)
            {
                return;
            }
            BattleHitLog.EndBattle(Summary());
        }

        /// <summary>
        /// A real battle -- the kind the auto-resolve model is a claim ABOUT -- and nothing else. Town walks,
        /// conversations, arenas and other friendly scenes share the mission plumbing but have no simulation to be
        /// read against, so there is nothing to write down. The same set the rest of RBM treats as a battle: field,
        /// siege, sally-out and naval fights, plus a hideout raid.
        /// </summary>
        private static bool IsRealBattle()
        {
            Mission m = Mission.Current;
            if (m == null)
            {
                return false;
            }
            return m.IsFieldBattle || m.IsSiegeBattle || m.IsSallyOutBattle || m.IsNavalBattle
                || (MapEvent.PlayerMapEvent != null && MapEvent.PlayerMapEvent.IsHideoutBattle);
        }

        /// <summary>
        /// What the man was doing, in the simulation's own words -- shoot, throw, melee, CHARGE -- because a column
        /// that says the same thing in both logs is the entire point of writing this one.
        /// </summary>
        private static string Describe(AttackCollisionData collision, Blow blow, MissionWeapon weapon, Agent striker)
        {
            if (collision.IsFallDamage)
            {
                return "fall";
            }
            if (collision.IsHorseCharge)
            {
                return "CHARGE";
            }
            if (blow.AttackType == AgentAttackType.Kick)
            {
                return "kick";
            }
            if (blow.AttackType == AgentAttackType.Bash)
            {
                return "bash";
            }
            if (collision.IsMissile)
            {
                // A javelin is not an arrow, and the whole javelin claim of the model turns on telling them apart.
                return IsThrown(weapon) ? "throw" : "shoot";
            }
            return "melee";
        }

        private static bool IsThrown(MissionWeapon weapon)
        {
            WeaponComponentData usage = weapon.IsEmpty ? null : weapon.CurrentUsageItem;
            if (usage == null)
            {
                return false;
            }
            switch (usage.WeaponClass)
            {
                case WeaponClass.Javelin:
                case WeaponClass.ThrowingAxe:
                case WeaponClass.ThrowingKnife:
                case WeaponClass.Stone:
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>The weapon class, not the item's name -- it is what the simulation log prints, and names differ per culture.</summary>
        private static string Weapon(MissionWeapon weapon)
        {
            if (weapon.IsEmpty)
            {
                return "unarmed";
            }
            WeaponComponentData usage = weapon.CurrentUsageItem;
            if (usage != null)
            {
                string mode = Mode(weapon);
                return mode != null ? "2HS/" + mode : usage.WeaponClass.ToString();
            }
            return (weapon.Item != null && weapon.Item.Name != null) ? weapon.Item.Name.ToString() : "-";
        }

        /// <summary>
        /// The prototype sword mode in use (RBMConfig.WeaponModes): HalfSword, Mordhau, or 2H for the normal grip of a
        /// sword that has the modes. Null for every other weapon.
        /// </summary>
        private static string Mode(MissionWeapon weapon)
        {
            WeaponComponentData usage = weapon.CurrentUsageItem;
            if (RBMConfig.WeaponModes.IsHalfSword(usage))
            {
                return "HalfSword";
            }
            if (RBMConfig.WeaponModes.IsMordhau(usage))
            {
                return "Mordhau";
            }
            if (weapon.Item != null && weapon.Item.Weapons != null)
            {
                foreach (WeaponComponentData other in weapon.Item.Weapons)
                {
                    if (RBMConfig.WeaponModes.HasOwnDamageType(other))
                    {
                        return "2H";
                    }
                }
            }
            return null;
        }

        /// <summary>The four zones the simulation models, so a real hit can be counted against its distribution.</summary>
        private static string Part(BoneBodyPartType part)
        {
            switch (part)
            {
                case BoneBodyPartType.Head:
                case BoneBodyPartType.Neck:
                    return "head";

                case BoneBodyPartType.Chest:
                case BoneBodyPartType.Abdomen:
                case BoneBodyPartType.ShoulderLeft:
                case BoneBodyPartType.ShoulderRight:
                    return "body";

                case BoneBodyPartType.ArmLeft:
                case BoneBodyPartType.ArmRight:
                    return "arm";

                case BoneBodyPartType.Legs:
                    return "leg";

                default:
                    return "-";
            }
        }

        private static bool IsAttacker(Agent agent)
        {
            return agent.Team != null && agent.Team.IsAttacker;
        }

        private static string Name(Agent agent)
        {
            if (agent.Character != null && agent.Character.Name != null)
            {
                return agent.Character.Name.ToString();
            }
            return (agent.Name != null) ? agent.Name : "?";
        }

        private void Record(Agent striker, string what, float dealt, bool downed)
        {
            Dictionary<string, Tally> side = IsAttacker(striker) ? _attackerTallies : _defenderTallies;
            Tally tally;
            if (!side.TryGetValue(what, out tally))
            {
                tally = new Tally();
                side[what] = tally;
            }
            tally.Blows++;
            tally.Damage += dealt;
            if (downed)
            {
                tally.Kills++;
            }
        }

        /// <summary>
        /// The clock, and who is still standing -- the real battle's answer to the simulation's round header. Before
        /// the lines meet it says so, because that stretch is the thing the whole volley model is about.
        /// </summary>
        private void WriteHeader(float now)
        {
            int attackers = 0;
            int defenders = 0;
            foreach (Agent agent in Mission.Current.Agents)
            {
                if (agent == null || !agent.IsHuman || !agent.IsActive())
                {
                    continue;
                }
                if (IsAttacker(agent))
                {
                    attackers++;
                }
                else
                {
                    defenders++;
                }
            }

            BattleHitLog.Write("");
            BattleHitLog.Write("    ── " + BattleHitLog.Clock(now)
                + ((_linesMetAt < 0f)
                    ? "  ·  THE APPROACH -- no line has reached the other yet"
                    : "  ·  THE LINES HAVE MET (at " + BattleHitLog.Clock(_linesMetAt) + ")")
                + "  ·  " + attackers + " v " + defenders);
        }

        /// <summary>
        /// The battle in totals. Not decoration: it is the row the simulation's own claims are checked against --
        /// what share of the killing the bowmen really did, what a javelin was really worth, whether a charge really
        /// decided anything.
        /// </summary>
        private string Summary()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("\n");
            sb.Append("  the battle in totals (").Append(_blows).Append(" blows):").Append("\n");
            sb.Append("    side  what      blows      damage      downed").Append("\n");
            AppendTallies(sb, "A", _attackerTallies);
            AppendTallies(sb, "D", _defenderTallies);

            sb.Append("\n");
            sb.Append("  melee attacks met, by the side that met them (hit = direct melee hits taken, no charges/kicks/bashes):").Append("\n");
            sb.Append("    side  chamber  w-block  w-parry  sh-block  sh-parry  sh-wrong       hit").Append("\n");
            AppendBlocks(sb, "A", _attackerBlocks, _attackerMeleeHitsTaken);
            AppendBlocks(sb, "D", _defenderBlocks, _defenderMeleeHitsTaken);

            sb.Append("\n");
            sb.Append((_linesMetAt < 0f)
                ? "  The lines never met: nobody reached anybody, and every blow above was landed at a distance."
                : ("  The lines met at " + BattleHitLog.Clock(_linesMetAt)
                    + " -- everything logged before that was landed across open ground."))
              .Append("\n");

            return sb.ToString().Replace("\n", System.Environment.NewLine);
        }

        private static void AppendTallies(StringBuilder sb, string side, Dictionary<string, Tally> tallies)
        {
            foreach (KeyValuePair<string, Tally> entry in tallies)
            {
                sb.Append("    ").Append(side).Append("     ")
                  .Append(Clip(entry.Key, 9).PadRight(10))
                  .Append(entry.Value.Blows.ToString().PadLeft(5))
                  .Append(Num(entry.Value.Damage, 12))
                  .Append(entry.Value.Kills.ToString().PadLeft(12))
                  .Append("\n");
            }
        }

        private static void AppendBlocks(StringBuilder sb, string side, int[] blocks, int hitsTaken)
        {
            sb.Append("    ").Append(side).Append("   ")
              .Append(blocks[(int)RBMConfig.MeleeBlockKind.ChamberBlock].ToString().PadLeft(9))
              .Append(blocks[(int)RBMConfig.MeleeBlockKind.WeaponBlock].ToString().PadLeft(9))
              .Append(blocks[(int)RBMConfig.MeleeBlockKind.WeaponParry].ToString().PadLeft(9))
              .Append(blocks[(int)RBMConfig.MeleeBlockKind.ShieldBlock].ToString().PadLeft(10))
              .Append(blocks[(int)RBMConfig.MeleeBlockKind.ShieldParry].ToString().PadLeft(10))
              .Append(blocks[(int)RBMConfig.MeleeBlockKind.ShieldWrongSide].ToString().PadLeft(10))
              .Append(hitsTaken.ToString().PadLeft(10))
              .Append("\n");
        }

        private static string Clip(string text, int width)
        {
            return (text.Length <= width) ? text : text.Substring(0, width);
        }

        private static string Num(float value, int width)
        {
            return BattleHitLog.Fmt(value).PadLeft(width);
        }
    }
}
