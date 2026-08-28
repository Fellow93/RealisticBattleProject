using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Equipment-aware auto-resolve. Vanilla <see cref="DefaultCombatSimulationModel.SimulateHit"/> prices a
    /// troop purely by its tier (<c>(2+tier)(10+tier)*0.02</c>) and uses that one number for both hitting and
    /// being hit. It never looks at what the man is wearing or carrying, and it cannot tell a mace from a
    /// sabre or mail from linen.
    ///
    /// This postfix leaves vanilla's side advantage, leader modifier, perks and morale intact and replaces only
    /// the power ratio, with the damage the striker's actual weapon would actually land on the struck man's
    /// actual armour -- run through the real armour equation of whichever combat model is live. The matchup is
    /// therefore asymmetric, as a real one is: a lightly-armoured man with a great axe hits hard and dies easily,
    /// which no single power number can say.
    ///
    /// Tier is not merely adjusted here -- it is taken out and replaced. Vanilla decides a battle almost
    /// entirely on the tier number, which gives a tier-1 recruit only 1.41x the blow of a tier-0 looter, no
    /// matter that the recruit stands in mail behind a shield and the looter swings a stick in rags. So this
    /// divides vanilla's tier term back out (see <see cref="GetCorrection"/>) and puts the soldier's real kit
    /// and real training in its place. A tier was only ever shorthand for those two things; having measured
    /// them, we do not also need the shorthand, and keeping it would charge for them twice.
    ///
    /// It also lifts terrain back out of the blow. Vanilla priced how the four arms compare through
    /// DefaultMilitaryPowerModel's context table -- cavalry worth a quarter more in the open, archers worth
    /// half as much defending a wood -- but an arm's edge is meant to come from its horse and its lance now,
    /// both already in the equipment ratio, not from the ground it stands on. So the context modifier is cancelled on
    /// both sides in EVERY blow (see <see cref="GetVanillaPowerNeutralizingFactor"/>), siege included -- a siege's own
    /// facts are priced by this model's siege handling, not by vanilla's table. Each troop is still judged against its
    /// own arm and no finer (see <see cref="GetBucket"/>), so an archer is never taxed for carrying an archer's armour.
    ///
    /// Every baseline is measured off the game's own roster rather than assumed -- see
    /// <see cref="EnsureBaselines"/>, which is where the honesty of this model lives.
    /// </summary>
    [HarmonyPatch(typeof(DefaultCombatSimulationModel), "SimulateHit", new Type[]
    {
        typeof(CharacterObject), typeof(CharacterObject), typeof(PartyBase), typeof(PartyBase),
        typeof(float), typeof(MapEvent), typeof(float), typeof(float)
    })]
    internal static class SimulationEquipmentPower
    {
        // A troop is judged against his own arm of service, and ONLY his arm -- not his tier. Bucketing by tier
        // as well was a mistake: a typical recruit measured against typical recruits always came out typical,
        // so the model resolved every ordinary battle exactly as vanilla did and the whole thing was a no-op
        // wherever it mattered. Tier is not a property of a soldier; it is a summary of his kit and his
        // training, and both of those are measured here directly. So tier is dropped from the baseline, and
        // dropped from vanilla's damage too (see GetCorrection) -- otherwise it would be counted twice.
        //
        // Arm of service stays as the bucket, but for a narrower reason than it once had. It used to lean on
        // vanilla's context table having already priced what an archer is worth against a horseman; that table is now
        // lifted out entirely (see GetVanillaPowerNeutralizingFactor), siege included, so nothing prices arm against
        // arm any more -- by design, an arm's edge is its horse and its lance, both already in the equipment ratio.
        // The bucket earns its place regardless: it normalises the damage units per arm, so a lance is measured
        // against lances and not counted "better kit" than a spear for landing more raw force.
        //
        // A HERO IS NOT AN ARM OF SERVICE, and giving him a bucket of his own was a mistake of exactly the kind
        // this model has made before. The correction is a RATIO against the bucket's baseline, so a bucket cancels
        // precisely the differences BETWEEN buckets: give heroes their own, and a typical lord striking a typical
        // infantryman divides to 1.0 by construction, and every scrap of his plate, his warhorse and his forty
        // years of swordsmanship vanishes into his own baseline. It is why the log had a looter in rags hitting an
        // armoured lord 35% HARDER than vanilla thought he would.
        //
        // So a lord is bucketed as what he FIGHTS as -- horse or foot, bow or blade -- like any other soldier, and
        // his kit then speaks for itself through the equipment ratio, exactly as a tier-5 veteran's does against a
        // recruit. That his kit is nothing like a line troop's is the POINT, not a reason to hide it in a bucket.
        // The four arms of service. Internal, not private, because arm-aware target selection
        // (SimulationArmTargeting) picks strikers and struck men by the same taxonomy the damage model prices
        // them with -- there must be exactly ONE arm classifier, and it is this one (see ArmOf/GetBucket).
        internal const int InfantryType = 0;

        internal const int ArcherType = 1;

        internal const int CavalryType = 2;

        internal const int HorseArcherType = 3;

        private const int TypeCount = 4;

        private const int BucketCount = TypeCount;

        // RBM's per-weapon-type armour thresholds default to 5 for cut and 3 for pierce, with a quarter of a
        // stopped blow carrying through as trauma. RBM's live path looks these up per weapon type; a
        // representative set is enough to price a kit.
        private const float RbmCutThresholdFactor = 5f;

        private const float RbmPierceThresholdFactor = 3f;

        private const float RbmBluntCarryFactor = 0.25f;

        // What a soldier's own training is worth, WITH RBM COMBAT OFF.
        //
        // With RBM Combat on, skill needs nothing from us: it is a first-class lever inside RBM's own damage clamp
        // (a master's cut lands three times a novice's) and SimulationWeaponModel mirrors that at full strength.
        //
        // With RBM Combat OFF, native reaches damage through skill only indirectly -- proficiency buys handling and
        // swing speed, and damage follows from those. It would be easy to conclude from that it should be left out
        // entirely, and for a while it WAS left out: these constants sat here unused and a master swordsman and a
        // raw recruit holding the same sword came out identical. That is not conservative, it is wrong -- because
        // this model DIVIDES OUT vanilla's tier term, and tier was the only thing carrying a soldier's training.
        // Remove tier and put nothing back in its place, and a veteran's whole superiority collapses to his gear.
        //
        // So a modest share goes back. The 0.3 is an estimate of an indirect relationship, not a figure read out of
        // native code -- the one number in the whole model not taken from somewhere.
        private const float SkillSaturationLevel = 250f;

        private const float VanillaSkillDamageShare = 0.3f;

        /// <summary>A master under vanilla rules: 1 + 0.3. A recruit: 1. Saturating at 250 skill, as RBM's own does.</summary>
        private static float VanillaSkillFactor(int skill)
        {
            return 1f + (VanillaSkillDamageShare * MBMath.ClampFloat(skill / SkillSaturationLevel, 0f, 1f));
        }

        /// <summary>What a man brings to a blow: the weapon he swings, the armour he stands in, the shield he hides behind.</summary>
        private struct TroopKit
        {
            // Armour is kept zone by zone rather than averaged into one number, because how much of it a blow
            // meets depends on who is throwing it and from what height. A lancer's leg harness is nearly all
            // that matters to the footman hacking up at him, and nearly nothing to the rider beside him.
            //
            // The zones are RBM's own bones (ArmorRework / GetBodyPartDamageMultiplier), folded only where a fold
            // costs nothing: the neck stands apart from the head (it is as soft as the torso but worth a head's
            // 1.5x), the shoulder apart from the arm (a cuirass plates the shoulder; only gloves reach the arm),
            // and chest and abdomen alone are merged into Torso -- they carry the same armour and differ by a
            // tenth in worth. So six zones, not vanilla's four.
            public float Head;

            public float Neck;

            public float Torso;

            public float Shoulder;

            public float Arm;

            public float Leg;

            /// <summary>What the horse adds at the leg and the body -- kept apart, because a dead horse adds nothing.</summary>
            public float HorseLeg;

            public float HorseBody;

            /// <summary>
            /// How much this troop's MOUNT can take before it falls -- its own health, not a flat figure for every
            /// animal: the game's Monster hit points (200 for a horse) plus the item's extra_health, so a heavy
            /// charger outlasts a courier's palfrey and a mule folds first. The pool a horse hit wears down, per
            /// animal, in SimulationBattleState.HorsesAlive. Zero for a troop with no mount.
            /// </summary>
            public float HorseHealth;

            /// <summary>
            /// The heaviest shot he looses, kept as the LABEL for his quiver and the test of whether he is a bowman
            /// at all. It is not what he is priced on -- see Shots.
            /// </summary>
            public SimulationWeaponModel.WeaponProfile Shot;

            /// <summary>
            /// Every arrow in his quiver, and how often each is the one on the string.
            ///
            /// An archer does not carry one arrow any more than a swordsman carries one sword. An Imperial Archer
            /// carries Greased Flight Arrows in two of his three kits and Needle Bodkins in the third -- and under
            /// RBM a Pierce bodkin HALVES the armour it meets while a Cut broadhead does not, so the two answer a
            /// mail hauberk by rules that have nothing to do with each other. Picking the one with the biggest
            /// number printed on it (125 beats 100, so the flight arrow wins) and pricing every shaft he ever looses
            /// as that arrow made him nine times worse against armour than he is. Each arrow must meet the armour
            /// on its own terms, and the average taken AFTER.
            /// </summary>
            public ShotOption[] Shots;

            /// <summary>
            /// Every melee weapon he carries, and how often each one is the one in his hand. A soldier is not the
            /// hardest blow in his kit -- he is all of them, taken as they come. He carries a spear and an axe and
            /// he swings whichever he happens to be holding, so a blow is priced as the average of what he MIGHT
            /// throw, not the best of it. That also fixes something the single-weapon model got quietly wrong: a
            /// mace and a sabre answer armour differently, so they must meet it separately and be averaged after,
            /// never averaged into one weapon that is neither.
            ///
            /// A bowman keeps this list too. It is what he draws when the quiver is empty.
            /// </summary>
            public MeleeOption[] Melee;

            /// <summary>Whether any of them is a polearm: what he reaches for when a horse comes at him.</summary>
            public bool HasPolearm;

            /// <summary>
            /// The javelins, throwing axes and darts on his back. Not a melee weapon and never was -- he hurls them
            /// while the lines close and then draws steel -- so they are kept apart from the belt and spent in the
            /// volley, where they belong. A skirmisher's two javelins are among the deadliest things in Calradia
            /// and auto-resolve has never once let him throw them.
            /// </summary>
            public SimulationWeaponModel.WeaponProfile Thrown;

            /// <summary>How many he carries. Two or three, mostly -- which is why the volley ends so quickly.</summary>
            public float ThrownPerMan;

            /// <summary>How many ARROWS (or bolts, or stones) he carries into the battle, read off the stack size of
            /// every quiver in his kit and averaged over his sets -- the same way <see cref="ThrownPerMan"/> counts
            /// his javelins. This is the whole of his quiver, and it is spent one shaft per loosed shot until it is
            /// gone and he draws steel. See SimulationBattleState.HasAmmo.</summary>
            public float ShotPerMan;

            /// <summary>The weight behind a charge, which is a thing that happens once and not for the whole battle.</summary>
            public float ChargeDamage;

            /// <summary>A man in plate answers an arrow properly; a man in mail does not. RBM halves all but plate.</summary>
            public bool IsPlate;

            /// <summary>RBM's own reckoning of the shield he carries, or 0 for a man with no shield.</summary>
            public float ShieldQuality;

            /// <summary>And what that shield can take before it is kindling -- the item's own hit points.</summary>
            public float ShieldHitPoints;

            /// <summary>Whether he fights from a horse, and whether he fights at a distance -- both decide where his blows land, and where he is struck.</summary>
            public bool IsMounted;

            public bool IsRanged;

            /// <summary>
            /// His melee skill -- the best of his one-handed, two-handed and polearm training. Read once when the kit
            /// is built (the same GetSkillValue the damage path uses per weapon), and surfaced here as a plain LEVEL
            /// so the defense roll can price how well he blocks and, against a lesser man, how often he parries. The
            /// weapon profile carries the skill TYPE, never the level, which is why this has to be kept apart.
            /// </summary>
            public float MeleeSkill;

            /// <summary>
            /// His SHOOTING hand, as a plain level, for the miss roll -- and it is the launcher's training, never the
            /// shaft's: a man is trained in the bow, not in arrows (which is exactly what WeaponProfile.Skill carries
            /// for a shot; see SimulationWeaponModel.GetMissileProfile). Read off the shot profile's own skill object,
            /// so a crossbowman is priced on Crossbow and a bowman on Bow without either being guessed at. Zero for a
            /// man who looses nothing, which is harmless -- nothing without a valid Shot ever rolls to hit.
            /// </summary>
            public float RangedSkill;

            public bool IsValid;
        }

        /// <summary>One of the arrows in his quiver, and the share of his shots loosed with it.</summary>
        internal struct ShotOption
        {
            public SimulationWeaponModel.WeaponProfile Profile;

            public float Weight;
        }

        /// <summary>One of the weapons on his belt, and the share of his blows thrown with it.</summary>
        internal struct MeleeOption
        {
            public SimulationWeaponModel.WeaponProfile Profile;

            /// <summary>His battle sets, and the weapons within each, are equally likely. These sum to 1 over the kit.</summary>
            public float Weight;

            public bool IsPolearm;
        }

        /// <summary>
        /// What a cached kit belongs to: the troop, AND the captain standing over him -- as a perk signature rather
        /// than as the man himself.
        ///
        /// The captain has to be in this key, and getting that wrong would be a very quiet bug. A troop template's
        /// gear and training do not change at runtime, which is why the cache was keyed on the CharacterObject
        /// alone; a captain's teaching is not a property of the template at all. The same Imperial Legionary fights
        /// on BOTH SIDES of the same battle and in every party in the campaign, so a captain's +20 Bow baked into
        /// the Legionary's cached kit would be handed silently to every Legionary alive, including the ones he is
        /// shooting at.
        ///
        /// The signature and not the hero, because that is what makes the cache stay small: two captains with the
        /// same perks produce byte-identical kits and share an entry, so a battle holds a handful of signatures
        /// rather than one entry per hero per troop type. A troop with no captain signs 0 -- and so does a captain
        /// with none of the perks that matter -- so the uncaptained kit is one entry, shared, and identical to what
        /// this model built before any of this existed. See SimulationPerks.SignatureOf.
        /// </summary>
        private struct KitKey : IEquatable<KitKey>
        {
            public readonly CharacterObject Troop;

            public readonly int CaptainSignature;

            // NO DISMOUNTED BIT, and there was one here until the captain perks stopped asking about the battle.
            // It was carried because a SKILL cannot be undone downstream the way TroopKit.IsMounted can (Explain
            // undoes that for a siege; a skill is already baked into the weapon magnitudes and the block chance by
            // then), so a siege kit drawn from a field kit's entry would have carried the wrong training. Native's
            // GetEffectiveSkill turns out to ask the man's TEMPLATE and not his agent, so his captain's teaching does
            // not depend on today's ground after all -- see SimulationPerks.IsCavalryTemplate. Nothing else in the kit
            // ever varied by it. The kit is terrain-blind again, entirely, and the cache is half the size for it.

            public KitKey(CharacterObject troop, int captainSignature)
            {
                Troop = troop;
                CaptainSignature = captainSignature;
            }

            public bool Equals(KitKey other)
            {
                return Troop == other.Troop && CaptainSignature == other.CaptainSignature;
            }

            public override bool Equals(object obj)
            {
                return obj is KitKey && Equals((KitKey)obj);
            }

            public override int GetHashCode()
            {
                int troopHash = (Troop != null) ? Troop.GetHashCode() : 0;
                return (troopHash * 397) ^ CaptainSignature;
            }
        }

        // A troop template's kit does not change at runtime, so it is cached -- by the troop AND by the captain over
        // him, who is not part of the template and must not be baked into it. See KitKey.
        private static readonly Dictionary<KitKey, TroopKit> _kitCache = new Dictionary<KitKey, TroopKit>();

        // However good a shield, a man cannot hide behind it forever: some share of what is thrown at him gets
        // through regardless, and a shield must never make him untouchable.
        /// <summary>No shield makes a man safe. The best one in Calradia still leaves most of him to be hit.</summary>
        private const float MaxShieldBlock = 0.65f;

        // A shield is a better answer to an arrow than to a swordsman, and always has been. An arrow flies from one
        // known direction and arrives on its own; a man simply gets the board up and it sticks there. A swordsman
        // feints, comes round the edge, waits for the shield to drop and hits above it. So the same shield turns
        // aside rather more shafts than blows -- which is the whole reason a line advancing under fire walks behind
        // its shields and then throws them aside when the lines meet.
        private const float MissileShieldBonus = 1.35f;

        // ---------------------------------------------------------------------------------------------------
        // The skill-based defense system (block / parry / riposte), gated behind RBMConfig.simulationDefenseSystem.
        //
        // A melee blow is met by a single DEFENSE ROLL and, on a success, a block-vs-parry split:
        //   - defenseChance is high and easy behind an intact shield, roughly twice as hard with a bare weapon (but
        //     never nil -- a floor, so even a raw unshielded man turns the odd blow), and BOTH climb with the
        //     DEFENDER'S OWN melee skill. A shattered shield (integrity 0) falls back to the weapon chance.
        //   - a successful defence is a PARRY with probability parryShare, else a plain BLOCK. parryShare turns on
        //     the defender's skill ADVANTAGE (his skill minus his attacker's) -- out-fighting a man turns your
        //     defences into counters. A shield block dumps the whole blow onto the shield; a weapon block just
        //     deflects it; a parry negates it and lands a RIPOSTE on the attacker.
        // A ranged blow is answered by the shield alone (quality-based, skill-blind, better against a missile than a
        // blade -- the existing GetShieldBlock), now to full negation onto the shield rather than a fractional skim.
        //
        // Two DISTINCT, non-overlapping uses of skill: the defender's ABSOLUTE skill raises the defense CHANCE; the
        // skill GAP splits a defence into block-or-parry. Every number here is a starting point -- tune vs a paired log.

        /// <summary>Behind an intact shield, before skill: a high, easy base chance to defend a melee blow.</summary>
        private const float ShieldDefenseBase = 0.45f;

        /// <summary>What the defender's own skill adds to the shield chance across the skill range (at saturation).</summary>
        private const float ShieldDefenseSkillCoeff = 0.30f;

        /// <summary>With only a weapon (or a broken shield): a non-zero FLOOR, so a low-skill man still blocks sometimes.</summary>
        private const float WeaponDefenseFloor = 0.20f;

        /// <summary>What skill adds to the weapon chance -- tuned so a weapon defence runs about half a shield one across the range (~2x harder).</summary>
        private const float WeaponDefenseSkillCoeff = 0.18f;

        /// <summary>No defence makes a man untouchable: the chance to defend a melee blow is capped here.</summary>
        private const float DefenseChanceCap = 0.75f;

        /// <summary>The wall lifts the cap but does not remove it: a besieged man may defend up to here, not beyond,
        /// so a garrison is hard to storm but never invulnerable. Used for both his melee defence and his shield.</summary>
        private const float SiegeDefenseChanceCap = 0.9f;

        /// <summary>At equal skill, this share of successful defences are parries (counters) rather than plain blocks.</summary>
        private const float ParryShareBase = 0.20f;

        /// <summary>How much the skill ADVANTAGE (def - atk, as a fraction of the saturation level) tilts a defence toward a parry.</summary>
        private const float ParryShareSkillGapCoeff = 0.5f;

        /// <summary>The most of his defences a man can turn into counters, however far he out-skills his attacker.</summary>
        private const float ParryShareCap = 0.6f;

        /// <summary>
        /// What is left of an archer's block chance when the man on him is on a horse. A bow is no parrying weapon and
        /// a lightly-armed quiverman gets almost nothing in the way of a lance at the gallop -- so a horseman riding
        /// him down meets a fraction of the defence a footman would, and no parry at all (there is no countering a
        /// charge with a knife). This is the classic death of unsupported archers. Tune vs a paired log.
        /// </summary>
        private const float CavalryVsArcherDefenseFactor = 0.25f;

        /// <summary>
        /// What is left of a MOUNTED man's block, shield and all. A rider sits high and busy -- he is managing a horse
        /// with one hand, he cannot plant himself behind a board the way a footman digs in, and a shield slung for the
        /// saddle does not come across as fast as one carried on the arm on foot. So a cavalryman turns aside a bit
        /// less than the same man would standing, WITH his shield up, not only without it. Applies to any struck man
        /// still on a live horse, against both blows and shots; it stacks with the archer factors above for a mounted
        /// bowman. A "bit less", not a collapse -- 0.85 is ~15% off. Tune vs a paired log.
        /// </summary>
        private const float MountedDefenseFactor = 0.85f;

        /// <summary>
        /// What is left of an archer's shield block against INCOMING SHOTS. A man loosing arrows is watching his own
        /// shot and his target, not the shafts coming back at him, and gets the board up late even when he carries
        /// one. So a ranged troop turns aside fewer of the arrows sent at him than a shield-bearer minding his cover
        /// would. Applies to the struck being ranged, whatever is shooting him. Tune vs a paired log.
        /// </summary>
        private const float ArcherVsRangedBlockFactor = 0.5f;

        /// <summary>
        /// How hard a charge hits, per point of the mount's charge stat. RBM's live combat prices a charge as MOMENTUM
        /// -- velocity times the mass of horse, rider and barding, over seventy (see HorseChanges.ComputeBlowMagnitude-
        /// FromHorseCharge) -- so a heavy destrier hits far harder than a pony. The sim has no velocity, so it leans on
        /// the horse's own charge stat, which already tracks its heft, and turns it into a bonus on the blow: a heavy
        /// warhorse now roughly half-again its blow or more, a light one much less. Raised from the old flat 0.01 to
        /// carry that weight, and made a dial because a charge is now UNBLOCKABLE too -- if the horse comes out too
        /// strong once both land, this is the number to pull back. A literal mass-and-velocity charge is the next step.
        /// </summary>
        private const float ChargeStrength = 0.02f;

        // A CHARGE WEARS THE HORSE THAT MAKES IT. Driving half a ton of animal into a man is violence done to the
        // horse as much as by it -- and RBM's live combat hurts a charging horse for exactly this reason. So every
        // charge feeds some damage back into the striker's OWN HorsesAlive pool (its own health per animal), and cavalry that
        // spend a battle charging are worn down and finally unhorsed -- after which they fight, and die, as the foot
        // do. This is the base toll per charge, in a blow's damage units, before the hard-target amplifiers below.
        private const float ChargeSelfDamageBase = 4f;

        // Multiplied when the horse charges a SPEARMAN. A set point is the one thing a charge dreads: the animal runs
        // onto it and it rebounds into the horse far harder than a bare man does. Keyed on the struck carrying a
        // polearm to brace -- the mirror of the reverse-charge bonus the spearman himself gets (AntiCavalryClosingBonus).
        private const float ChargeSpearRebound = 2.5f;

        // And a milder amplifier for an ARMOURED target: charging plate and mail jars the horse more than charging
        // linen. Per point of the armour the blow actually met.
        private const float ChargeArmorRebound = 0.01f;

        /// <summary>
        /// IN A REAL BATTLE ONLY A MINIMAL NUMBER OF MELEE BLOWS BITE AT FULL FORCE. Most are caught on armour, turned
        /// by a shift of the body, land flat or land short -- a landed melee blow's magnitude is mostly a FRACTION of
        /// the full, and only now and then does one land clean. The sim struck every un-blocked blow home whole, which
        /// is why melee was a bloodbath rather than the slow grind it is, and why the heavy foot won battles the mobile
        /// arms decide on the field. So a landed melee blow is scaled by pow(random, this): a high exponent piles the
        /// draws down near nothing and leaves a thin tail up at full, so the AVERAGE landed blow is worth 1/(exp+1) of
        /// the full -- at 0.5, two-thirds. A charge is exempt (its weight is committed and lands whole); shots and
        /// thrown weapons are spread the SAME now (RangedLandingExponent), and only a charge lands harder.
        ///
        /// CALIBRATED against a paired real-vs-sim log (2026-07-15): at the old value of 2 (a third), sim melee landed
        /// at ~0.4x the dealt of a real fought battle -- across every matchup and both sides, large n. The block/parry
        /// system already removes the turned-aside blows, so spreading the survivors down to a third double-counted the
        /// miss -- which is why melee is no HARSHER than ranged now, not harsher as first supposed. Lowered to 0.5,
        /// ~doubling sim melee to sit near real. Tune vs the log: raise to grind melee down, lower toward 0 to land full.
        /// </summary>
        private const float MeleeLandingExponent = 1.5f;

        /// <summary>
        /// The melee exponent to use when the block/parry defence system is OFF (simulationDefenseSystem = 0). The 0.5
        /// above corrects a double-count that ONLY exists because the defence system discretely removes turned-aside
        /// blows before they reach this spread; with that system off, the old fractional-skim path removes nothing
        /// here, so spreading survivors down to 0.5 (two-thirds) under-counts the miss and lands melee ~2x its
        /// calibrated level -- the ranged-vs-melee winner flip. Held at the pre-defence-system calibration of 2 (a
        /// third) for that path. See MeleeLandingExponent.
        /// </summary>
        private const float MeleeLandingExponentNoDefense = 2f;

        /// <summary>
        /// The same idea for a RANGED blow. A shot or a thrown weapon that lands still varies with the range it flew
        /// and the angle it met -- a plunging shaft at the end of its arc, a glancing hit off a curved helm -- so it is
        /// not worth full magnitude every time either. Its failure modes (a clean miss, a shield got up in time) are
        /// priced elsewhere as accuracy and the block, not here. At 0.5 the AVERAGE landed shot is worth 1/(0.5+1) =
        /// two-thirds of the full. Applies to FIRED missiles (bow, crossbow, sling); a thrown weapon is committed and
        /// lands harder -- see ThrownLandingExponent. Sits at the SAME value as melee -- the paired log (see
        /// MeleeLandingExponent) found the two land alike once each arm's own misses are priced separately -- so raise
        /// it to nerf ranged harder, lower toward 0 to let shots land nearer full.
        /// </summary>
        private const float RangedLandingExponent = 0.5f;

        /// <summary>
        /// A THROWN weapon lands harder than a fired one. A javelin or a throwing axe at skirmish range is a committed,
        /// short-range throw -- the man steps into it and lets fly at a target he can see -- not a shaft arcing in from
        /// two hundred yards, so it does not glance the way a shot does. The paired log (2026-07-16) confirmed it: sim
        /// javelins landed ~half of a real fought battle's against low-armour targets, where the armour absorbs almost
        /// nothing and the shortfall is ALL in the throw -- because they were spread at the arrow rate. At 0.2 the
        /// AVERAGE landed throw is worth 1/(0.2+1) = about five-sixths of the full: near-committed, but not quite the
        /// whole of it (a javelin can still catch flat or land short). Lower toward 0 to land nearer full.
        /// </summary>
        private const float ThrownLandingExponent = 0.2f;

        /// <summary>
        /// THE DEFENDER HOLDS THE HIGH GROUND. A side that stands and waits picks the ground it waits on -- a ridge, a
        /// slope, the lip of a ford -- and its archers shoot DOWNHILL into an enemy toiling up at them: a little more
        /// range, a plunging angle that finds the gaps a level shot glances off, and a target that is climbing rather
        /// than shooting back on even terms. The attacker, coming up at them, shoots UPHILL for the reverse of all of
        /// it. This is a small, flat edge to the DEFENDER's fired shots and an equal debit to the ATTACKER's, in field
        /// battles only -- a wall assault already prices the same idea, harder and by phase (see the siege block in
        /// Explain), and a defender does not always hold the height, so the field figure is kept mild. Set to 1 to
        /// switch the bias off. Applies to FIRED missiles only (bow, crossbow, sling); a thrown weapon at skirmish
        /// range is a level, short throw the slope barely touches.
        /// </summary>
        private const float FieldDefenderShotMagnitude = 1.10f;

        private const float FieldAttackerShotMagnitude = 0.90f;

        // AND THE ARROW THAT SIMPLY MISSES.
        //
        // RangedLandingExponent above says a landed shot is worth a fraction of the full, and its own comment names
        // the two ways a shot fails -- "a clean miss, a shield got up in time" -- and says both are "priced elsewhere
        // as accuracy and the block". The block was built. THE ACCURACY NEVER WAS. Every shot the sim ever loosed
        // connected with somebody, and the only thing that could stop one was a board in the way. A bowman shooting
        // at two hundred yards into a moving line does not hit a man with every shaft, and he never has: he looses
        // into a space and hopes, and most of what he sends goes into the ground between the ranks.
        //
        // So a fired shot now rolls to hit BEFORE it is shaped at all. A missed shaft is not a weak blow -- it is no
        // blow: it rolls no body part, meets no armour, wears no shield and kills no horse, exactly the way the
        // `closing` walker below lands nothing. It is still written to the log (breakdown.Missed keeps the row), and
        // that is the point -- the miss rate is the whole reason this exists and it must be readable off a paired log.
        //
        // Scoped to FIRED missiles (bow, crossbow, sling). A thrown javelin is a committed short-range throw at a man
        // the thrower can see, and ThrownLandingExponent already lands it near full on purpose; giving it a miss roll
        // as well would price the same commitment twice, in opposite directions.
        //
        // CALIBRATION, AND READ THIS BEFORE TUNING ANYTHING ELSE: this removes shots that RangedLandingExponent's
        // spread was implicitly standing in for. That exponent was calibrated (2026-07-15) against a paired log with
        // NO miss roll upstream, so it was carrying the misses itself, in magnitude space. With a discrete miss now
        // taking them out first, ranged output falls by roughly (1 - missChance) and the arm is being charged for the
        // same failure twice -- exactly the double-count MeleeLandingExponent's own comment describes on the melee
        // side. Re-measure ranged on a paired log and expect to LOWER RangedLandingExponent toward 0 to compensate.

        /// <summary>
        /// How much of his misses a fully trained bowman removes. Accuracy is the most trained thing about an archer
        /// -- it is what the whole of his training IS -- so skill bites harder here than anywhere else in the model.
        /// At 0.6 a man at the saturation level (SkillSaturationLevel, 250) misses 40% as often as an untrained one:
        /// a Fian's shafts find men, a levy's find dirt. Never to zero -- nobody hits every shot.
        /// </summary>
        private const float RangedMissSkillReduction = 0.6f;

        /// <summary>
        /// THE VOLLEY IS NOT ONE RANGE -- IT IS A RANGE CLOSING, and these are its two ends.
        ///
        /// The volley opens with the lines as far apart as they will be all battle: the shaft arcs up, comes down
        /// somewhere in a moving formation, and the man who loosed it never aimed at anybody in particular. It ENDS
        /// with the two lines near enough to hurl a spear across, where the same archer is looking at a man he can
        /// see plainly and shooting nearly flat. Those are not the same shot, and pricing the whole volley at one
        /// figure quietly averaged them -- the first exchange of a battle was as accurate as the last, which is the
        /// one thing a bowshot never is.
        ///
        /// So the scatter is lerped across the volley on <c>SimulationBattleState.VolleyProgress</c>: OPENING at the
        /// furthest, CLOSING at the moment the javelins start. Both are above or at 1 -- the volley never shoots
        /// BETTER than the flat skirmish shot, it only ever scatters more.
        ///
        /// The two are deliberately set so their mean is the flat 1.25 this replaced. The ask was that the early
        /// volley hit less, not that archers be quietly weakened a third time on top of the head-zone cut and the
        /// miss roll -- so the opening pays for the closing exactly, and the volley's TOTAL scatter is unchanged.
        /// Move them apart to sharpen the gradient; move them together to go back to a flat volley.
        /// </summary>
        private const float RangedMissVolleyFactorOpening = 1.5f;

        private const float RangedMissVolleyFactorClosing = 1.0f;

        /// <summary>
        /// Shooting FROM a moving horse. The whole trick of the steppe, and it is still much harder than standing on
        /// your feet and drawing: he is timing the loose to the hoofbeat off a platform that will not hold still.
        /// This does not touch the horse archer's evasion (HorseArcherEvasion) -- that is about what he suffers; this
        /// is about what he delivers.
        /// </summary>
        private const float RangedMissMountedShooterFactor = 1.25f;

        /// <summary>
        /// And shooting AT a horseman. A man in a line is a standing target in a wall of standing targets; a horseman
        /// is fast and he is not where he was when the arrow left the string. This prices the lead a bowman has to
        /// take and mostly does not. (Cavalry ARE hit more often on the horse than the man -- that is a separate roll,
        /// HorseHitChanceMissile, and it happens only once a shot has already connected.)
        /// </summary>
        private const float RangedMissMountedTargetFactor = 1.4f;

        /// <summary>The ceiling, so no pairing of dials ever makes an arm that cannot hit anything at all.</summary>
        private const float RangedMaxMissChance = 0.8f;

        // What the launcher itself is worth in accuracy, keyed on the SHAFT's class -- which is how the shot profile
        // names itself (WeaponProfile.WeaponType is the ammo's WeaponClass; see SimulationWeaponModel.GetMissileProfile).
        // A bolt means a crossbow: a flat, fast, mechanically-aimed shot that a conscript can point and loose, and the
        // one ranged weapon in Calradia that does not need a lifetime to shoot straight. An arrow means a bow, the
        // middle case, and the one everything else is measured against. A stone means a sling, which is the least
        // accurate thing on the field by a distance.
        private const float MissFactorBolt = 0.7f;
        private const float MissFactorArrow = 1f;
        private const float MissFactorStone = 1.3f;

        /// <summary>
        /// The chance, PER BLOW, that a foot skirmisher who reaches the melee still carrying javelins hurls one at
        /// point-blank rather than drawing his sidearm. A short skirmish (a wood, a village) can leave a heavy bundle
        /// half-thrown when the lines meet, and a man does not simply drop three good javelins to fence with a knife
        /// -- at arm's length they are the deadliest thing he owns. Not every blow, though: in the press he does not
        /// always get the throw off, and the moment passes (HasJavelins still counts the bundle down by the round, so
        /// the leftovers empty within a round or two of contact whether thrown or not). At 0.25, about a quarter of
        /// his contact blows are the last javelins going in. Raise it to make leftover javelins bite harder at contact,
        /// lower it
        /// toward 0 to have him draw steel the moment the lines meet. Skirmish-phase throwing is unaffected.
        /// </summary>
        private const float ContactJavelinThrowChance = 0.25f;

        /// <summary>
        /// And for a CHARGE, gentler still than a shot. When a couched lance connects at the gallop it delivers
        /// tremendous and fairly consistent force -- the old model was not wrong that a charge is committed -- but it
        /// is not full EVERY time: the lance strikes a shade off-square, the horse is a step short of full speed, the
        /// point catches a pauldron and skids. So a charge lands nearer full than an arrow does, but not always whole.
        /// At 0.35 the AVERAGE landed charge is worth 1/(0.35+1) = about three-quarters of the full charged blow. This
        /// is a cavalry-shock dial: raise it to blunt the charge, lower it toward 0 to let it land nearer full.
        /// </summary>
        private const float ChargeLandingExponent = 0.35f;

        /// <summary>A riposte is a fast jab off a parry, not a full swing: this fraction of the defender's own corrected blow.</summary>
        private const float RiposteScale = 0.5f;

        // Vanilla's fixed base scale on a simulated blow -- the 40 in (0.5+0.5r)*40*pow(power,0.7)*advantage. Absolute
        // mode divides it back out (along with the tier-power core) so the kit-derived blow sets the magnitude itself,
        // while everything vanilla layered on top of that base -- advantage, leader/captain modifier, perks, the random
        // spread -- rides through untouched. Kept as a named constant rather than a literal 40 for exactly this reason.
        private const float VanillaBaseScale = 40f;

        // NOTE (2026-07-15): an OFFENSE COMPRESSION term once sat here -- `actual` pulled toward a baseline by an
        // exponent, to rein the elite's per-blow offense in. It was removed. Compressing toward ANY roster-derived
        // baseline (global OR per-matchup) drags the whole battle's level down, because a battle of real troops fights
        // ABOVE the roster mean, which is weighed down by looters and militia -- so every blow ends up above the
        // baseline and gets pulled toward it. The paired log confirmed it twice (level collapsed to ~0.4x real both
        // times). The elite over-delivery is a SKILL-space problem (the sim reads a skilled man off his damage
        // CEILING), not a magnitude-space one, and must be fixed there if at all -- see SimulationWeaponModel.

        // What a BLOCKED blow costs the shield, weapon by weapon. A shield is not worn by the damage it spares the
        // man; it is worn by the weapon that hits IT, and different weapons wear it very differently. These are the
        // RATIOS RBM's live combat uses (DamageRework.RBMComputeBlowDamageOnShield): a javelin all but destroys a
        // board, a throwing axe splits it, an axe or sword chops it, an arrow wears it slowly, a mace dents it, and a
        // point barely scratches it. Applied to the blow's own magnitude, so a harder blow chops more. The absolute
        // scale is carried by ShieldDamageScale and the shield's own capacity (SimulationBattleState.ShieldCapacityPerMan),
        // so only the ratios here matter -- and they are RBM's. Every number is a starting point -- tune vs a paired log.
        private const float ShieldDamageScale = 1f;      // master dial: shield wear per block, against the capacity budget

        private const float ShieldDmgJavelin = 6f;       // a thrown spear all but destroys a board (RBM x25)
        private const float ShieldDmgThrowingAxe = 4f;   // splits it (RBM x10)
        private const float ShieldDmgThrownPolearm = 3f; // a hurled spear (RBM x5)
        private const float ShieldDmgThrowingKnife = 0.8f;
        private const float ShieldDmgArrow = 1.2f;       // arrows wear a shield down over a volley (RBM x1.5)
        private const float ShieldDmgOtherMissile = 0.15f; // sling stones and the like barely mark it (RBM x0.1)
        private const float ShieldDmgMeleeAxe = 1.2f;    // an axe (or two-handed polearm) cut chops hardest (RBM x1.5)
        private const float ShieldDmgMeleeCut = 0.8f;    // a sword cut chops it
        private const float ShieldDmgMeleeBlunt = 0.7f;  // a mace dents rather than splits
        private const float ShieldDmgMeleePierce = 0.12f; // a point glances off a board and barely marks it (RBM x0.09)

        // A spear set for a horse. Infantry have answered cavalry this way for three thousand years and
        // auto-resolve has never once let them.
        private const float BraceBonus = 1.6f;

        // AND THE HORSE RUNS ONTO THE POINT. A set spear against a CHARGING horse is not merely aimed better -- it is
        // driven home by the horse's own momentum, the same weight that powers the beast's charge, now spent against
        // the man it is charging. So a braced blow that meets a closing horse carries a bonus sourced from the STRUCK
        // horse's charge power (its ChargeDamage), through the same ChargeStrength dial the cavalry's own charge uses:
        // a heavy destrier impales itself far harder than a pony. This is the SHARE of that reverse momentum the
        // spearman keeps -- a horse onto a spear is a two-way wreck, so not the whole of it. It fires only when the
        // horse is actually closing (the same terrain-gated chance the cavalry charge fires by), which on open ground
        // is most of the time. THE cavalry-vs-spear-infantry balance dial: raise it to punish the charge harder.
        private const float AntiCavalryClosingBonus = 0.5f;

        // THE HORSE ARCHER DOES NOT STAND THERE.
        //
        // He is not a cavalryman with a bow, and auto-resolve has always modelled him as one: a mounted man who rides
        // into the line, gets hemmed in, and is hacked down by the infantry around him like anybody else. That is the
        // one thing a horse archer never does. His entire art is the refusal of contact -- he shoots, and when the
        // foot come at him he turns his horse and goes, and shoots again from where they cannot follow. An infantry
        // line does not kill horse archers. It chases them until it is exhausted, and they kill it.
        //
        // So a footman's blow at a mounted archer who still has arrows is nearly nothing: not because he cannot fight
        // -- his spear is as good as it ever was -- but because there is no one standing in front of him to put it
        // into. This is the fraction of a foot blow that finds him anyway: the man who was caught turning, the horse
        // gone lame, the pocket of ground with no way out.
        //
        // Two things end it, and only two.
        //
        //   THE QUIVER. When it is empty he is a lightly armoured man on a tired horse with a bow he cannot use, and
        //   he must close, or run, or die. Everything the model already knows about ammunition (AmmoRounds, on the
        //   battle's own clock) does the work here for free -- HasAmmo asked of the man being STRUCK.
        //
        //   HORSEMEN. A rider can catch a rider. Cavalry chase horse archers down, and this whole exemption is
        //   silent about them: they are mounted, so it never applies, and their lances land in full. Which is
        //   precisely why every steppe army in history feared the other side's cavalry and nothing else.
        //
        // And arrows find him regardless: an archer's shaft does not care how fast his horse is. Only MELEE from
        // FOOT is refused, because only melee from foot requires him to be somewhere he can be reached.
        private const float HorseArcherEvasion = 0.1f;

        // HORSE OR MAN.
        //
        // A blow at a mounted troop is a blow at TWO things -- the man and the animal under him -- and it finds
        // only one of them, never both. The horse is the bigger target and the lower one, so a footman hacking
        // upward is mostly hacking at it; a horseman fighting another horseman is aiming at the man he means to
        // unseat and rarely wastes a stroke on the mount; an arrow is loosed at the mass of the rider and only now
        // and then takes the horse instead. A blow that finds the horse wears the horse ALONE -- its own pool, met
        // through its own barding -- and never touches the rider, his armour, his defence or his wound pool. A blow
        // that finds the rider meets HIS armour, not the barding, because the barding is the horse's and the horse
        // was not hit. These are the shares that decide which it is; they are dials for the cavalry balance and are
        // meant to be TUNED VS A PAIRED LOG (foot infantry should ground a squadron over a fight, not in a round).
        private const float HorseHitChanceFootMelee = 0.45f;

        private const float HorseHitChanceMountedMelee = 0.15f;
        private const float HorseHitChanceMissile = 0.22f;

        private const int ZoneHead = 0;

        private const int ZoneNeck = 1;

        private const int ZoneTorso = 2;

        private const int ZoneShoulder = 3;

        private const int ZoneArm = 4;

        private const int ZoneLeg = 5;

        private const int ZoneCount = 6;

        /// <summary>Not a part of the man at all -- the horse under him. A blow marked with this found the animal,
        /// wore its pool, and dealt the rider nothing. Kept out of the 0..ZoneCount range so the zone loops and the
        /// zone-armour/zone-share tables never touch it.</summary>
        private const int ZoneHorse = 6;

        /// <summary>Where the blows land, as a share of them. RBM's own bones, folded to six (see TroopKit).</summary>
        private struct HitZones
        {
            public float Head;

            public float Neck;

            public float Torso;

            public float Shoulder;

            public float Arm;

            public float Leg;
        }

        // A blow does not land just anywhere: where it lands depends on where the two men are standing, and a
        // simulated blow has no body part unless we give it one. These are the six zones RBM's armour and its
        // body-part worth actually distinguish. Each set sums to 1: it is a distribution, not a set of
        // multipliers. The game itself has NO such table -- a real blow's bone is decided by collision geometry
        // per swing -- so these are honest estimates of that geometry, not figures lifted from anywhere.

        // Foot against foot: the two are eye to eye, so it is the chest, the shoulders and the arms that catch
        // it, the head often enough, the neck now and then, and the legs almost never -- a man does not stoop to
        // hack at ankles.
        private static readonly HitZones FootVsFoot = new HitZones { Head = 0.15f, Neck = 0.05f, Torso = 0.40f, Shoulder = 0.20f, Arm = 0.15f, Leg = 0.05f };

        // Foot against a rider: the horseman is above, and what is at a footman's eye level is the man's legs and
        // his lower body. This is why barding on a horse's flanks is worth so much, and it is what the model
        // could not see before.
        //
        // A SPEAR IS THE EXCEPTION, and it is why this table is not the whole of foot-against-horse. The legs are
        // where a footman's reach ends, not where he wants to strike -- a man with a sword cannot get past them. A
        // polearm gives him back the height the horse took: he sets it at the rider's chest and face and does not
        // stoop to the animal's shins. So a spearman at a horseman rolls FootVsFoot, the same spread two footmen
        // trade, and only a man WITHOUT a spear is reduced to the legs. See GetHitZones -- the pool he draws from
        // narrows to his polearms in the same breath (MeleeDamage's preferPolearms), and the two must agree: it
        // would be nonsense to price the blow as the spear and then aim it as though he were swinging a hatchet.
        private static readonly HitZones FootVsMounted = new HitZones { Head = 0.03f, Neck = 0.02f, Torso = 0.30f, Shoulder = 0.10f, Arm = 0.08f, Leg = 0.47f };

        // A rider against a man on foot: he strikes downward, so it is the head, the neck, the shoulders and the
        // chest that take it, and the legs are all but out of reach.
        private static readonly HitZones MountedVsFoot = new HitZones { Head = 0.22f, Neck = 0.08f, Torso = 0.32f, Shoulder = 0.18f, Arm = 0.15f, Leg = 0.05f };

        // Rider against rider: level with one another again, much as two footmen are, with rather more coming at
        // the legs across the horses.
        private static readonly HitZones MountedVsMounted = new HitZones { Head = 0.15f, Neck = 0.05f, Torso = 0.35f, Shoulder = 0.20f, Arm = 0.15f, Leg = 0.10f };

        // AN ARROW SELDOM FINDS A HEAD, and both missile tables below say so far more firmly than they used to.
        //
        // A swordsman aims at a head. He is an arm's length away, he can see it, and he swings at what he chooses.
        // An archer cannot: he looses at a man forty yards off who is moving, and what arrives is a shaft dropping
        // into whatever of him happens to be under it. What is under it is mostly torso -- the head is a small, high,
        // fast-moving part of a silhouette, perhaps a twelfth of the standing area a man presents and rather less of
        // the area anyone is aiming at, since a bowman who aims anywhere aims at the middle of the mass. In a volley
        // he is not aiming at a man at all, only at a formation, and the shaft lands by area alone.
        //
        // The head share is worth more than its number, which is why getting it wrong shows. A head or a neck is
        // worth 1.5x anywhere else on the body (BodyPartMultiplier, straight out of RBM's own table), so the head is
        // the most lethal thing an arrow can find -- and every point of share handed to it is multiplied before it
        // reaches the man's pool. Head+neck was 15% of shafts at a footman and is now 9%; against a rider it was 10%
        // and is now 6%. Both cut by the same two fifths, and both freed shares go to the TORSO, because an arrow
        // that does not find a head does not then find an elbow: it finds the middle of him.
        //
        // NOT ONLY ARROWS. This table is every MISSILE -- a hurled javelin reads it too (see `missile` in Explain,
        // which is shooting OR throwing), and it should: a spear crossing thirty yards at a moving man is no better
        // aimed at his head than a shaft is, and arguably worse. A sling stone likewise. The only thing that reads
        // the melee tables is a weapon still in somebody's hand.
        //
        // A JUDGEMENT, like the shield chance and the miss chance, and a calibration dial like both. The game has no
        // such table to copy -- a real arrow's bone is decided by collision geometry per shot -- so this is an
        // estimate of that geometry and nothing more. It lowers missile lethality without touching a single arrow's
        // damage, and it stacks with the ranged miss roll: if the bows come out too weak on a paired log, this is one
        // of the two places to look, and the cheaper one to move.
        private static readonly HitZones MissileVsFoot = new HitZones { Head = 0.07f, Neck = 0.02f, Torso = 0.56f, Shoulder = 0.15f, Arm = 0.10f, Leg = 0.10f };

        // An arrow loosed at a rider meets a far larger, lower target -- the horse. A great share of shafts find it
        // at the leg (its barding answers there), and fewer still reach the man's head above: he is the highest part
        // of the tallest target on the field, and the one an arcing shaft is least likely to arrive at. This is why a
        // barded horse is worth so much against archery, and it is what a single missile table could not tell apart.
        private static readonly HitZones MissileVsMounted = new HitZones { Head = 0.05f, Neck = 0.01f, Torso = 0.44f, Shoulder = 0.12f, Arm = 0.08f, Leg = 0.30f };

        private static bool _baselinesBuilt;

        // What the cached kits and baselines were built against. Both bake in settings that can be changed from
        // the config screen in the middle of a session: the armour equation decides every baseline damage AND
        // every kit's magnitude (through the skill curve), and the shield figure decides what a typical man of
        // each arm blocks. Were they left standing after a setting moved, a blow would be priced by the new
        // settings and measured against a baseline built under the old ones, and the correction would quietly
        // skew -- the worst kind of wrong, because nothing would look broken.
        private static bool _baselineRbmCombat;

        private static float _baselineShieldBlockChance;

        private static float _baselineArmorMultiplier;

        private static float _baselineArmorThreshold;

        private static float _baselineThrustModifier;

        // And which defence the baseline mitigation was built for: the skill-based ladder skims a matchup by a
        // different figure (a chance to fully negate) than the old fractional block, so toggling the system mid
        // session must rebuild, exactly as moving the shield chance does.
        private static bool _baselineDefenseSystem;

        // The damage a typical troop of bucket [striker] lands on a typical troop of bucket [struck]. This is
        // the pivot the whole correction turns on.
        private static float[][] _baselineDamage;

        private static float _globalBaselineDamage;

        // The shield the average shield-bearer carries, and the share of blows the average man of each arm
        // turns aside (nought for arms that carry no shields). A man with the common shield of his arm is then
        // neither rewarded nor punished for it; a man with none takes what his fellows would have blocked.
        private static float _typicalShieldQuality;

        // Kept as two tables, not one scaled by a factor at the point of use: the block is CLAMPED, and the mean of
        // clamped numbers is not the clamp of their mean. Scaling the melee average would let a bucket full of men
        // already at the cap keep climbing past it on paper.
        private static float[] _typicalShieldBlock;

        private static float[] _typicalShieldBlockVsMissile;

        // For the skill-based defense system: the share of MELEE blows the average man of each arm turns aside --
        // block or parry alike, since both fully negate. Priced off his own skill and whether he carries a shield,
        // so a bucket of trained shield infantry answers a blow far more often than a bucket of levy skirmishers.
        // Kept clamped-then-averaged like the shield tables above, for the same reason: the mean of clamped chances
        // is not the clamp of their mean.
        private static float[] _typicalMeleeDefense;

        /// <summary>How many troop types went into each bucket's average -- printed, so a skewed population shows.</summary>
        private static int[] _bucketPopulation = new int[BucketCount];

        private static void Postfix(ref ExplainedNumber __result, CharacterObject strikerTroop, CharacterObject struckTroop,
            PartyBase strikerParty, PartyBase struckParty, MapEvent battle)
        {
            // With the model off the whole overhaul stands down: leave the vanilla blow untouched (Explain would keep
            // Correction at 1, but the terrain lift and the absolute cap below would still bend it, and RecordHit
            // would fill the log with junk "-" rows for a model that priced nothing). Return before any of that.
            if (!SimulationEnabled)
            {
                return;
            }

            // The battle is passed in so the blow can be placed in it: which round it falls in, whether the lines
            // have met yet, how many arrows this stack has left, whose shields are still whole, whose horses still
            // stand. A blow that knows none of that cannot spend anything, and a battle in which nothing is spent
            // is not a battle.
            bool strikerIsAttacker = battle != null && strikerParty != null
                && battle.AttackerSide != null && strikerParty.Side == BattleSideEnum.Attacker;

            SimulationBattleState.BattleState state = SimulationBattleState.Get(battle);

            // Explain rather than GetCorrection, so the blow can be written down as it is struck. This is the real
            // battle -- the one the game is actually fighting, and the one the campaign will live with -- and it is
            // the only account of it that cannot drift from the truth.
            Breakdown breakdown;
            Explain(strikerTroop, struckTroop, out breakdown, state, strikerIsAttacker, spend: true, struckParty: struckParty);

            // The ground no longer favours an arm of service, and the leader no longer carries his captains on his
            // back. Vanilla's blow rides on (1 + leader + context) over the same for the man being struck, and the
            // equipment correction above divides out only the tier base, so both terms would otherwise ride
            // untouched into the result. The context -- the (arm x terrain x side) table -- is lifted in every blow,
            // siege included, since an arm's edge is meant to come from its horse and its lance now, both already
            // priced in the equipment ratio. The leader term is lifted whenever this model prices
            // captain perks itself, because that term IS a tally of captain perks and would otherwise be the same
            // thing counted twice. Folded INTO the correction, not applied after it, so the log's Vanilla x
            // Correction = Final identity holds and RecordHit writes the whole of what the model did. See
            // GetVanillaPowerNeutralizingFactor -- it is emphatic about what PowerModifier actually is.
            if (breakdown.Correction > 0f)
            {
                float vanillaPowerFactor = GetVanillaPowerNeutralizingFactor(strikerTroop, struckTroop, strikerParty, struckParty);
                if (vanillaPowerFactor != 1f)
                {
                    breakdown.Correction *= vanillaPowerFactor;
                }

                // THE COMMANDER'S TACTICS, RESHAPED. Vanilla's flat one-sided Tactics advantage rode into the blow
                // through strikerAdvantage, untouched by the equipment correction above; it is lifted back off here
                // and a gentler two-sided edge -- striker's general against struck's -- is folded in to replace it.
                // Only the gap between the two generals' training tells, so an army out-led loses ground and one
                // evenly matched neither gains nor gives it. Nothing off the model (SimulationEnabled gates the
                // whole postfix), so with the overhaul off vanilla's advantage is left exactly as it was.
                float commanderFactor = CommanderTacticsFactor(strikerParty, struckParty);
                if (commanderFactor != 1f)
                {
                    breakdown.Correction *= commanderFactor;
                }
            }

            float vanillaDamage = __result.ResultNumber;
            float correction = breakdown.Correction;

            // THE ABSOLUTE PER-BLOW CAP. In absolute mode the equipment ratio's [0.1,8] clamp is gone -- there is no
            // ratio to clamp -- so nothing else stops one freak kit pairing (a great axe through linen, say) landing
            // a blow worth many times the man's pool. No single blow may exceed this share of the struck man's own
            // hit points. This is the only place the man being struck AND the assembled blow are both in reach. Folded
            // back INTO the correction rather than applied to __result after it, so the log's vanilla x correction =
            // dealt identity holds and RecordHit (which recomputes the dealt figure from breakdown.Correction) stays
            // exact. A defended or zeroed blow (correction <= 0) is left alone. Ratio mode keeps its clamp and skips this.
            if (RBMConfig.RBMConfig.simulationAbsoluteDamage && correction > 0f
                && RBMConfig.RBMConfig.simulationAbsoluteBlowCap > 0f && vanillaDamage > 0f)
            {
                // His party, not null: the cap is a share of the struck man's OWN pool, and his pool now carries his
                // commander's perks (see SimulationTroopHitPoints.CommandedHealth). Passing null here would cap the
                // blow against a pool the man does not have -- tighter than the one his wound is actually spent
                // against -- and the two must be the same number or the dial means something different at each end.
                float pool = SimulationTroopHitPoints.MaxHitPoints(struckTroop, struckParty,
                    state != null && state.Dismounted);
                float maxBlow = RBMConfig.RBMConfig.simulationAbsoluteBlowCap * pool;
                if (vanillaDamage * correction > maxBlow)
                {
                    correction = maxBlow / vanillaDamage;
                    breakdown.Correction = correction;
                }
            }

            if (correction != 1f)
            {
                __result = new ExplainedNumber(vanillaDamage * correction);
            }

            // THE SIEGE WIDTH WATCHES THE MELEE. A blow struck hand to hand at an opening may widen or narrow it,
            // but only if it puts its man down -- and whether it did is not known yet, so the blow is parked and
            // the game's own verdict claims it a breath later (SimulationDownedMarker). Parked for EVERY blow, not
            // only the logged ones: the width is a fact about the fight and must move whether or not anybody is
            // writing it down. "melee" is the damage model's own label for a blow struck in the press -- a shot or
            // a throw is labelled otherwise and moves nothing.
            SimulationSiege.NoteBlow(state, strikerIsAttacker, breakdown.Phase == "melee");

            RecordHit(state, strikerTroop, struckTroop, strikerIsAttacker, battle, breakdown, vanillaDamage);

            // AND THE PARRY BITES BACK. The defender out-fought his attacker, turned the blow, and answered it -- a
            // counter the postfix owes because only here are the battle, the attacker's own side and his selected
            // soldier all in reach. The riposte is spent on the ATTACKER's wound pool, wearing him toward a death
            // the game will realise the next time he is struck; it is never itself blocked or parried, and it never
            // reaches back into the simulation's kill loop mid-blow (see SimulationTroopHitPoints.ApplyRiposte).
            if (breakdown.Riposte && battle != null && vanillaDamage > 0f)
            {
                ApplyRiposte(state, battle, strikerTroop, struckTroop, strikerIsAttacker, vanillaDamage, strikerParty);
            }
        }

        /// <summary>
        /// The counter a parrying defender lands on his attacker. Its size is the DEFENDER'S own corrected blow on
        /// the striker, cut to a fast jab by <see cref="RiposteScale"/>: the reverse-direction correction
        /// (struck-on-striker, asked without rolling or spending anything) priced against the same vanilla draw, so
        /// a heavy man's riposte lands like a heavy man's blow and a lightly-armed one's like his. The reverse
        /// correction reads the [struck][striker] cell of the same baseline table the forward blow read the other
        /// way -- the baseline is bidirectional, and a riposte is simply an offence in the other direction. It
        /// carries the striker's TYPICAL defence (baked into that reverse baseline) but is never rolled for a fresh
        /// block -- consistent with "no recursion".
        /// </summary>
        private static void ApplyRiposte(SimulationBattleState.BattleState state, MapEvent battle,
            CharacterObject strikerTroop, CharacterObject struckTroop, bool strikerIsAttacker, float vanillaDamage,
            PartyBase strikerParty)
        {
            // Everything is the other way round in a riposte: the man who was struck is throwing this one, and the
            // ATTACKER is the one being hit. So the party handed over as "the struck man's" is the striker's -- his
            // horse is the one the counter is coming at, and his lord's veterinary is the one that matters. Easy to
            // get backwards, and it would silently price the wrong side's horses. The same party goes to the wound
            // below for the same reason: it is HIS pool the counter is spent on, and his own lord's hit-point perks
            // that say how deep it is.
            float reverseCorrection = GetCorrection(struckTroop, strikerTroop, state, !strikerIsAttacker, spend: false,
                struckParty: strikerParty);
            float riposteDamage = RiposteScale * vanillaDamage * reverseCorrection;
            if (riposteDamage <= 0f)
            {
                return;
            }

            // The attacker's own side holds the soldier the counter falls on: the game selected him on that side
            // before this blow, and has not moved on yet.
            MapEventSide strikerSide = strikerIsAttacker ? battle.AttackerSide : battle.DefenderSide;
            float hitPointsLeft = SimulationTroopHitPoints.ApplyRiposte(strikerSide, battle, strikerParty, riposteDamage);

            RecordRiposte(state, battle, struckTroop, strikerTroop, strikerIsAttacker, riposteDamage, hitPointsLeft);
        }

        /// <summary>
        /// Write a riposte into the book, right after the parry that earned it. The roles are reversed -- the
        /// DEFENDER is the striker of this counter and the attacker the one struck -- and the down is left to the
        /// ordinary path (ApplyRiposte only deepens the wound), so this row never claims a kill the casualty books
        /// have not yet made.
        /// </summary>
        private static void RecordRiposte(SimulationBattleState.BattleState state, MapEvent battle,
            CharacterObject defenderTroop, CharacterObject attackerTroop, bool strikerIsAttacker, float riposteDamage,
            float hitPointsLeft)
        {
            if (state == null || battle == null || !SimulationLog.IsEnabled || !RBMConfig.RBMConfig.simulationLogHits)
            {
                return;
            }

            HitRecord hit = new HitRecord();
            hit.Round = state.Round;
            hit.VolleyPhase = SimulationBattleState.IsVolleyPhase(state);
            hit.SkirmishPhase = SimulationBattleState.IsSkirmishPhase(state);
            if (state != null && state.SiegeAssaultBattle)
            {
                hit.SiegePhase = SimulationSiege.IsAssault(state) ? "assault" : "approach";
                hit.SiegeAttackWidth = state.AttackWidth;
                hit.SiegeDefendWidth = state.DefendWidth;
                hit.SiegeWallFactor = state.SiegeWallFactor;
            }
            // The defender is the one landing this blow, so its side is the OTHER side from the attacker's.
            hit.StrikerIsAttacker = !strikerIsAttacker;
            hit.Striker = defenderTroop;
            hit.Struck = attackerTroop;
            hit.Phase = "riposte";
            hit.Defense = "riposte";
            hit.Weapon = "-";
            hit.BodyPart = "-";
            hit.ArmorMet = 0f;
            hit.ShieldBlock = 0f;
            hit.Braced = false;
            hit.ChargeBonus = 1f;
            hit.VanillaDamage = 0f;
            hit.Correction = 1f;
            hit.FinalDamage = riposteDamage;
            hit.HitPointsLeft = hitPointsLeft;
            // The wound is only deepened here; the man falls on his next incoming blow, by the ordinary path, so this
            // row never claims the down itself.
            hit.Downed = false;
            hit.AttackersLeft = (battle.AttackerSide != null) ? battle.AttackerSide.NumRemainingSimulationTroops : 0;
            hit.DefendersLeft = (battle.DefenderSide != null) ? battle.DefenderSide.NumRemainingSimulationTroops : 0;

            state.Trace.Add(hit);
        }

        /// <summary>
        /// Write the blow down, exactly as it fell. Whether it put the man down is not known yet -- the game decides
        /// that in the next breath, in ApplySimulationDamageToSelectedTroop -- so the record is parked where that
        /// verdict can be written into it (see SimulationDownedMarker).
        /// </summary>
        private static void RecordHit(SimulationBattleState.BattleState state, CharacterObject strikerTroop,
            CharacterObject struckTroop, bool strikerIsAttacker, MapEvent battle, Breakdown breakdown, float vanillaDamage)
        {
            // Whatever the last blow was, it is answered. Clearing this FIRST is load-bearing: the game calls
            // ApplySimulationDamageToSelectedTroop after every blow, including the ones we decline to write down,
            // and if LastHit were left pointing at an earlier record then this blow's verdict would be written into
            // THAT one -- turning somebody else's kill back into a miss.
            SimulationBattleState.LastHit = null;

            if (state == null || battle == null || !SimulationLog.IsEnabled || !RBMConfig.RBMConfig.simulationLogHits)
            {
                return;
            }

            // A blow the model zeroed is not a blow. In the volley a man who is not shooting never swung at anybody
            // -- he is a bowshot away with his shield up -- so he rolls no body part, splinters no shield, kills no
            // horse, and does not go in the book. Writing him down as a "closing" blow that dealt nothing was
            // recording an event that did not happen, four thousand times a battle, and burying the volley's actual
            // story -- the archers, and who was allowed to answer them -- under the noise of men doing nothing.
            //
            // A DEFENDED blow is the exception: it too deals nothing (correction is 0), but it genuinely HAPPENED --
            // a sword was turned by a shield, a spear parried -- and it is the only way the block and parry rates can
            // be read off the log, so it is kept. breakdown.Defended tells the two cases apart. A HORSE HIT is kept
            // for the same reason: it dealt the rider nothing, but it wore the mount, and that is the only way the
            // horse toll can be read off the log. breakdown.HorseHit marks it.
            //
            // A MISS is the third of these, and the most important of the three to keep: an arrow was loosed and it
            // went wide, which is an event and not a non-event, and the miss RATE is the only thing the accuracy
            // system can be calibrated against. Drop these rows and the log would show a volley of nothing but hits
            // and swear the archers were deadly. breakdown.Missed marks it.
            if (breakdown.Correction <= 0f && !breakdown.Defended && !breakdown.HorseHit && !breakdown.Missed)
            {
                return;
            }

            float finalDamage = vanillaDamage * breakdown.Correction;

            HitRecord hit = new HitRecord();
            hit.Round = state.Round;
            hit.VolleyPhase = SimulationBattleState.IsVolleyPhase(state);
            hit.SkirmishPhase = SimulationBattleState.IsSkirmishPhase(state);
            if (state != null && state.SiegeAssaultBattle)
            {
                hit.SiegePhase = SimulationSiege.IsAssault(state) ? "assault" : "approach";
                hit.SiegeAttackWidth = state.AttackWidth;
                hit.SiegeDefendWidth = state.DefendWidth;
                hit.SiegeWallFactor = state.SiegeWallFactor;
            }
            hit.StrikerIsAttacker = strikerIsAttacker;
            hit.Striker = strikerTroop;
            hit.Struck = struckTroop;
            hit.Phase = breakdown.Phase ?? "-";
            hit.Weapon = breakdown.Weapon;
            hit.BodyPart = ZoneName(breakdown.BodyPart);
            hit.ArmorMet = breakdown.ArmorMet;
            hit.ShieldBlock = breakdown.ShieldBlock;
            hit.Defense = breakdown.Defense ?? "none";
            hit.Braced = breakdown.Braced;
            hit.ChargeBonus = breakdown.ChargeBonus;
            hit.Closing = breakdown.Closing;
            hit.Evaded = breakdown.Evaded;
            hit.VanillaDamage = vanillaDamage;
            hit.Correction = breakdown.Correction;
            hit.FinalDamage = finalDamage;
            // What he has LEFT is not known yet -- the game subtracts this blow in the next breath. The
            // downed-marker fills it in from the game's own books once it has.
            hit.HitPointsLeft = -1f;
            hit.AttackersLeft = (battle.AttackerSide != null) ? battle.AttackerSide.NumRemainingSimulationTroops : 0;
            hit.DefendersLeft = (battle.DefenderSide != null) ? battle.DefenderSide.NumRemainingSimulationTroops : 0;

            state.Trace.Add(hit);
            SimulationBattleState.LastHit = hit;
        }

        /// <summary>
        /// The factor this model multiplies a vanilla simulated blow by, or 1 when it has nothing to say. Public
        /// to the module so the shadow simulation can replay a battle with and without it and show what it did.
        ///
        /// The battle's <paramref name="state"/> is passed in rather than looked up from a MapEvent, because the
        /// shadow replay has no MapEvent: it fights the same battle twenty times over and each of those needs its
        /// own full quivers, whole shields and living horses. Handing it the state directly is what lets the replay
        /// run the SAME model the real battle runs. It did not, before -- the shadow passed no battle at all, so it
        /// quietly ran with no volley phase, archers who never ran dry and shields that never broke, and every A/B
        /// figure in the log was comparing vanilla against a model the game was not using.
        ///
        /// <paramref name="spend"/> is false for the log's reference tables, which must be able to ask what a blow
        /// WOULD do without loosing the arrow or splintering the shield.
        /// </summary>
        /// <param name="struckParty">The party of the man being struck by THIS call -- see Explain. A reverse pass
        /// (the riposte) has the roles the other way round, so its struckParty is the original striker's.</param>
        internal static float GetCorrection(CharacterObject strikerTroop, CharacterObject struckTroop,
            SimulationBattleState.BattleState state = null, bool strikerIsAttacker = false, bool spend = false,
            PartyBase struckParty = null)
        {
            Breakdown breakdown;
            Explain(strikerTroop, struckTroop, out breakdown, state, strikerIsAttacker, spend, struckParty);
            return breakdown.Correction;
        }

        /// <summary>
        /// Every term that goes into one blow's correction, so the log can show its working. The model has been
        /// wrong twice on reasoning that looked sound, so it is made to account for itself rather than be argued
        /// with. Returns false when the model declines to touch the blow at all (correction is then 1).
        /// </summary>
        internal struct Breakdown
        {
            public float ArmorMet;

            public float ShieldBlock;

            public float Actual;

            public float Baseline;

            public float EquipmentRatio;

            public float TierTerm;

            public float Correction;

            // What the man was actually DOING, so a traced blow can say so rather than leave it to be inferred
            // from the numbers. The matchup table alone could never show this: it asks what a blow WOULD do,
            // outside any battle, and a blow outside a battle is never in a volley and never out of arrows.

            /// <summary>"shoot", "throw", or "melee".</summary>
            public string Phase;

            /// <summary>The weapon it came off: the arrow, the javelin, or the heaviest thing in the pool he drew from.</summary>
            public string Weapon;

            /// <summary>Which part of him it found -- one of the six zones (ZoneHead..ZoneLeg), or -1 for the
            /// reference tables, which take the expectation over all of them and so land nowhere in particular.</summary>
            public int BodyPart;

            /// <summary>He set a spear against a horse.</summary>
            public bool Braced;

            /// <summary>What the charge added, as a multiplier. 1 when he is not charging, or no longer is.</summary>
            public float ChargeBonus;

            /// <summary>The lines had not met yet and he was walking into arrows with nothing to answer them.</summary>
            public bool Closing;

            /// <summary>
            /// The shaft went wide. Like a defended blow it deals nothing (correction is 0), and like a defended blow
            /// it genuinely HAPPENED -- an arrow was loosed and a man was shot at -- so this tells RecordHit to keep
            /// the row. It has to: the miss rate is the only thing this system is FOR, and if the misses are not in
            /// the book there is no way to calibrate it. Distinct from Closing, which is a man who never shot at all.
            /// Only ever set on a live rolled blow; the reference tables mitigate by expectation and never miss.
            /// </summary>
            public bool Missed;

            /// <summary>He swung at a horse archer who still had arrows, and the horse archer was not there.</summary>
            public bool Evaded;

            /// <summary>
            /// What answered the blow, for the log: "none" (it landed), "shield-block", "weapon-block", or "parry".
            /// Only ever a discrete outcome on a LIVE rolled blow; the reference tables mitigate by expectation and
            /// leave it "none".
            /// </summary>
            public string Defense;

            /// <summary>
            /// The blow was fully negated by a defence -- so it deals nothing, yet it still HAPPENED and must go in
            /// the book (a blocked blow is not a non-event the way a man standing in the volley is). Correction is 0
            /// on a defended blow, so this is what tells RecordHit to write it down anyway.
            /// </summary>
            public bool Defended;

            /// <summary>The defence was a parry: the postfix owes the attacker a counter-blow. Only set on a live blow.</summary>
            public bool Riposte;

            /// <summary>
            /// The blow found the HORSE, not the man. Like a defended blow it deals the rider nothing (correction is
            /// 0), yet it genuinely happened -- it wore the mount toward being killed -- so this is what tells
            /// RecordHit to keep the row. Only ever set on a live rolled blow against a still-mounted target.
            /// </summary>
            public bool HorseHit;
        }

        /// <summary>
        /// The master switch for the whole auto-resolve overhaul. The equipment-aware damage model is the heart of
        /// it, and every auxiliary system was built to work WITH that model -- the widened tick multiplier, the
        /// morale removal, the per-trooper wound pools, the strength rout, arm-aware targeting. With the model off,
        /// each of those on its own leaves the battle a half-applied hybrid that is neither the vanilla tier-only sim
        /// nor RBM's: arm targeting in particular deliberately routes volley shots onto foot soldiers expecting the
        /// damage model to nullify them, and with the model gone those land full vanilla damage before the lines even
        /// meet. So when this is false, they ALL stand down and the battle is vanilla's own. This is the one condition
        /// they read; it mirrors the gate at the top of <see cref="Explain"/>.
        /// </summary>
        internal static bool SimulationEnabled
        {
            get
            {
                return RBMConfig.RBMConfig.simulationEquipmentEnabled
                    && RBMConfig.RBMConfig.simulationEquipmentPowerWeight > 0f;
            }
        }

        /// <summary>Nobody over him, and the signature to match -- a named stand-in for the no-battle case, so the
        /// ternaries above read as the question they are asking.</summary>
        private static CharacterObject Uncaptained(out int signature)
        {
            signature = 0;
            return null;
        }

        /// <param name="struckParty">
        /// The party the man being STRUCK belongs to -- and only his, because the only thing here that needs a party
        /// is the veterinary on his horse.
        ///
        /// It cannot be read off the side. A captain is a fact about a side (a formation spans every party standing
        /// on it), which is why the chain of command is built per side and reached through <paramref name="state"/>.
        /// A PartyLeader perk is not: a side is often several lords, each with his own party and his own perks, and
        /// asking the SIDE's leader for the veterinary would toughen every horse on the field with one man's
        /// training. So the party is handed in from the postfix, where the game gives it to us directly.
        ///
        /// Null wherever there is no party to speak of -- the log's reference tables, which ask what a matchup does
        /// in the abstract -- and the horse is then worth exactly what its own item data says.
        /// </param>
        internal static bool Explain(CharacterObject strikerTroop, CharacterObject struckTroop, out Breakdown breakdown,
            SimulationBattleState.BattleState state = null, bool strikerIsAttacker = false, bool spend = false,
            PartyBase struckParty = null)
        {
            breakdown = default(Breakdown);
            breakdown.Correction = 1f;

            if (!SimulationEnabled)
            {
                return false;
            }
            if (strikerTroop == null || struckTroop == null)
            {
                return false;
            }

            EnsureBaselines();

            // WHO IS LEADING EACH OF THESE TWO MEN. A captain's perks are training -- they make his soldiers better
            // with the weapon in their hands -- so they belong in the kit, and the kit is where they go: the bonus
            // then flows through the real damage, miss and defence equations exactly as a genuinely better-trained
            // man's would, instead of being approximated by a multiplier stapled on at the end.
            //
            // Null and 0 whenever there is no battle to ask (the reference tables and the riposte's reverse pass
            // hand a null state), which is the uncaptained kit and identical to what this model did before any of
            // this existed. See SimulationCommandStructure for who a captain is and SimulationPerks for what he
            // teaches.
            int strikerSignature;
            int struckSignature;
            CharacterObject strikerCaptain = (state != null)
                ? state.CaptainFor(strikerTroop, strikerIsAttacker, out strikerSignature) : Uncaptained(out strikerSignature);
            CharacterObject struckCaptain = (state != null)
                ? state.CaptainFor(struckTroop, !strikerIsAttacker, out struckSignature) : Uncaptained(out struckSignature);

            // A battle nobody rides in. This has to be read BEFORE the kits, not after: a siege has no horses at all,
            // and which of a captain's perks reach his men turns on whether they are on one -- so it decides what the
            // kit is built with, and rides in the kit's cache key. Everything below re-reads it for the horse itself.
            bool dismounted = state != null && state.Dismounted;

            TroopKit striker = GetKit(strikerTroop, strikerCaptain, strikerSignature);
            TroopKit struck = GetKit(struckTroop, struckCaptain, struckSignature);
            if (!striker.IsValid || !struck.IsValid)
            {
                return false;
            }

            bool rbmCombat = RBMConfig.RBMConfig.rbmCombatEnabled;

            // A SIEGE, and thereby the wall's edge for the man on it. True for any blow inside a siege (both sides'
            // parties carry the Siege context); WHICH man it favours is decided at each roll by strikerIsAttacker --
            // the defender's defence is lifted while a besieger strikes him, and the besieger's shot skews wide while
            // the defender's finds home. Never in the reference tables (they hand a null party: a matchup has no wall).
            bool siege = RBMConfig.RBMConfig.simulationSiegeDefenderEnabled
                && struckParty != null && struckParty.MapEvent != null
                && struckParty.MapEvent.SimulationContext == MapEvent.PowerCalculationContext.Siege;

            // Where in the battle this blow falls, and what the two men have left to spend.
            SimulationBattleState.TroopState strikerState = (state != null) ? state.For(strikerTroop, strikerIsAttacker) : null;
            SimulationBattleState.TroopState struckState = (state != null) ? state.For(struckTroop, !strikerIsAttacker) : null;

            // And once he is not mounted, none of what follows treats him as though he were: no charge, no barding at
            // the leg, no horse to be killed before he is, and no cavalry clash out in front. The kit still records
            // the man's own formation class (it is a fact about him, not about today's ground), so the battle's own
            // answer is applied here. See SimulationBattleState.IsMountedIn -- the one place that question is
            // answered, and the same one the kit above was built against.
            bool strikerMounted = SimulationBattleState.IsMountedIn(strikerTroop, dismounted);
            bool struckMounted = SimulationBattleState.IsMountedIn(struckTroop, dismounted);

            // What the horse still has in it. A footman hacking upward is mostly hacking at the horse, and horses
            // die; when one does its rider keeps none of its barding and none of its height -- and he is no longer
            // a horseman for the purpose of anything below, including whether the cavalry are still fighting each
            // other. A man whose horse is dead has left the skirmish, whatever else he is doing. A man who never had
            // one -- because there are no horses in a siege -- has zero alive from the first blow, which strips the
            // barding the same way a killed mount would.
            // The animal's own health, plus whatever his lord's veterinary adds to it -- computed here rather than
            // baked into the kit, because the kit is cached per troop TEMPLATE and a veterinary is a fact about the
            // party this particular squadron rides for. See SimulationTroopHitPoints.CommandedMountHealth.
            float horseHealth = SimulationTroopHitPoints.CommandedMountHealth(struckTroop, struckParty, struck.HorseHealth);
            float horsesAlive = !struckMounted ? 0f
                : (struckState != null)
                    ? SimulationBattleState.HorsesAlive(struckState, horseHealth)
                    : 1f;
            bool struckStillMounted = struckMounted && horsesAlive > 0.5f;

            // AND WHETHER THE MAN BEING STRUCK IS A HORSE ARCHER WHO IS STILL A HORSE ARCHER. Three things have to
            // hold, and each of them is a real way for the steppe to lose its advantage: he must still have a horse
            // under him, he must still have arrows to shoot (out of them, he has no reason to keep his distance and
            // no way to profit by it), and there must be ground to ride in -- a wood or a village street or a
            // breached wall gives him nowhere to go. See HorseArcherEvasion.
            bool struckIsKiting = struckStillMounted
                && struck.IsRanged
                && state != null
                && state.KitingRoom > 0f
                && SimulationBattleState.HasAmmo(state, struckState, struck.ShotPerMan, !strikerIsAttacker);

            // A BATTLE HAS THREE ACTS. The volley, while the lines are far apart and the bowmen have the field. The
            // skirmish, on the ground between them -- javelins in the air, and the horse of each side riding out at
            // each other before the foot are anywhere near. And then the lines meet, which is the only act
            // auto-resolve has ever known about, and the least interesting of the three.
            bool volley = SimulationBattleState.IsVolleyPhase(state);
            bool skirmish = SimulationBattleState.IsSkirmishPhase(state);
            bool approaching = volley || skirmish;

            // THE OPENING ROUNDS BELONG TO THE DEFENDER. He is standing on his ground with his enemy in the open and
            // the whole field to shoot across; the attacker is still coming, too far out to answer, and eats it.
            // That is what it means to advance on a prepared position, and it is the reason storming one is
            // expensive.
            // The window is the battle's own, not a constant: it shrinks with the volley in a small fight (see
            // SimulationBattleState.GetDefenderOnlyRounds). A flat two rounds here outlived a 1.26-round volley and
            // silenced the only archers on the field for the whole of it.
            bool mayLoose = !(volley && strikerIsAttacker && state != null
                && state.Progress <= state.DefenderOnlyRounds);

            // Whether he still HAS arrows is a question about the clock, not about how many blows he happens to
            // have thrown: a quiver empties per minute, not per swing. Nothing is spent here -- the round counter
            // is the spending.
            bool shooting = striker.IsRanged
                && striker.Shot.IsValid
                && mayLoose
                && SimulationBattleState.HasAmmo(state, strikerState, striker.ShotPerMan, strikerIsAttacker);

            // The shaft leaves the string HERE -- and whether it finds a gap, glances off a helm or is missed
            // clean, it is spent all the same, so one comes out of the stack's quiver the moment he looses. Only on a
            // LIVE blow (spend): the reference tables and the riposte's reverse-correction pass ask what a shot WOULD
            // do and loose nothing. When the last shaft is gone HasAmmo above turns false and he draws his sidearm --
            // see SimulationBattleState.HasAmmo and SpendAmmoOnDeath (which takes the fallen man's share with him).
            if (shooting && spend && strikerState != null)
            {
                strikerState.AmmoRemaining -= 1f;
            }
            // Otherwise the quiver is empty (or he is not yet in range). He draws from his melee arsenal like
            // anybody else, and his armour was never meant for that.

            // THE SKIRMISH, and the javelins come off his back HERE -- not during the long approach. A man does not
            // hurl a spear at somebody a bowshot away; he carries it until the ground between the lines is close
            // enough to cross with it, and then he throws it, and then it is gone and he is a man with a knife.
            // That is the whole life of a skirmisher, and auto-resolve has never once let him live it: his javelins
            // were either ignored entirely or -- worse -- treated as the weapon he swung for the whole battle, an
            // axe thrown on an infinite loop.
            bool hasThrowable = !shooting
                && striker.Thrown.IsValid
                && striker.Thrown.Magnitude > 0f
                && SimulationBattleState.HasJavelins(state, strikerState, striker.ThrownPerMan);

            // He throws all through the skirmish; and if the lines MEET while there are still javelins on his back --
            // a heavy bundle a short skirmish could not empty -- a foot skirmisher will SOMETIMES loose the last of
            // them at point-blank as the enemy closes, rather than always drawing his sidearm untouched. HasJavelins
            // (in hasThrowable) is what "still has javelins" means, so this only fires for a man who genuinely has some
            // left. Only on a live blow (spend) -- the reference tables and the riposte's reverse pass never roll --
            // and only on foot; a horseman in the press is charging or hacking, not skirmishing. The roll sits inside
            // the && so it is drawn only for a foot javelineer actually at contact. See ContactJavelinThrowChance.
            bool throwing = hasThrowable
                && (skirmish
                    || (!approaching && spend && !strikerMounted
                        && MBRandom.RandomFloat < ContactJavelinThrowChance));

            // AND THE HORSE MEET THE HORSE. Each side's cavalry ride out at each other across the open ground while
            // the foot are still walking -- which is what cavalry have always done, and what auto-resolve has never
            // let them do: it held every horseman back until the infantry lines collided and then threw him into the
            // press, where a horse is worth least. Here they have the field to themselves, and they fight the only
            // enemy who can reach them.
            bool cavalryClash = skirmish && strikerMounted && struckStillMounted;

            // A thrown weapon IS a missile, and everything that follows from that follows: it goes to the mass of
            // the man rather than to whatever a footman can reach, and the shield it meets is a shield held up
            // against something in flight, which is a far better shield than one held against a swordsman.
            bool missile = shooting || throwing;

            // Whether this man is FIGHTING at all, or merely getting closer to it. Once the lines have met, everyone
            // is. Before that, only three kinds of man are: the one shooting, the one throwing, and the horseman who
            // has found another horseman. Everybody else is walking, and pays for it.
            bool engaged = !approaching || missile || cavalryClash;

            // A MAN STILL CLOSING THE DISTANCE DOES NOTHING AT ALL -- and that is the whole of the approach, not just
            // the volley.
            //
            // Not a weak blow -- NO blow. Whether the lines are a bowshot apart (the volley) or the ground between
            // them is still being crossed (the skirmish), a footman with no javelin has nothing in front of him to
            // hit: no sword reaches that far, and a man walking toward an enemy he cannot touch is not fighting badly,
            // he is not fighting. The only men who ARE fighting before the lines meet are the three `engaged` names it
            // wide -- the one shooting, the one throwing, and the horseman who has found another horseman. Everyone
            // else is walking, and a walk is not a blow.
            //
            // This used to be split: the volley walker was zeroed here, but the SKIRMISH walker fell through and paid
            // a token "closing PENALTY" of 0.08 instead -- a fraction of a blow, but a blow, landed by a man who by the
            // phase's own definition was not yet in reach of anyone. That was a real body count dealt by men still
            // crossing open ground, and it made no sense next to the volley walker who (rightly) dealt nothing. So the
            // two are one case now: `approaching && !engaged` is a man closing, in either act, and he lands no blow.
            //
            // Nothing is spent here either: he splinters no shield and kills no horse, because he never reached one.
            if (approaching && !engaged)
            {
                breakdown.Phase = "closing";
                breakdown.Weapon = "-";
                breakdown.BodyPart = -1;
                breakdown.Closing = true;
                breakdown.Correction = 0f;
                return true;
            }

            // AND ON THE APPROACH TO A WALL, A BESIEGER CAN ONLY REACH THE MEN SHOOTING AT HIM.
            //
            // The defenders who are not shooting are behind the parapet -- that is what a parapet is for -- and an
            // arrow aimed at one of them hits masonry. Only the men leaning out to loose are exposed, and they are
            // exposed precisely because they are loosing. So a besieger's shot at a defending swordsman is not a
            // weak blow, it is not a blow: it is a shaft in the stonework.
            //
            // This is the HARD half of the rule. The striker selection already biases the besieger's shots toward
            // the defenders who are shooting (ExposedDefenderWeight), but that sampler deliberately never forbids an
            // arm outright -- it redraws and then takes what it has -- so a garrison with few archers, or none at
            // all, would still see its infantry picked off through the wall. The line has to be held here, where a
            // blow can actually be refused. A garrison with no archers on the parapet simply cannot be hurt while
            // the ground is being crossed, which is correct: there is nobody up there to shoot at.
            //
            // The defender is under no such restriction -- from a wall he can see the whole army -- and neither side
            // is restricted once the ladders are up, which is the entire point of getting them up.
            if (strikerIsAttacker && SimulationSiege.IsApproach(state) && !struck.IsRanged)
            {
                breakdown.Phase = "closing";
                breakdown.Weapon = "-";
                breakdown.BodyPart = -1;
                breakdown.Closing = true;
                breakdown.Correction = 0f;
                return true;
            }

            // THE SHAFT GOES WIDE, and it goes wide BEFORE it is a blow at all.
            //
            // Note where this sits: above the hit zones, above the horse-or-man roll, above the shield. That order is
            // the whole meaning of it. An arrow that missed did not find a leg, did not find the horse, and was not
            // turned by a board -- it is in the ground behind the man, and none of those questions were ever asked.
            // A shot must MISS before it can be blocked, never the other way about, or the model would be crediting
            // shields with stopping arrows that were never going to hit anybody.
            //
            // Fired shots only (`shooting`); a thrown javelin is committed and lands -- see the miss constants.
            float missChance = 0f;
            if (shooting && RBMConfig.RBMConfig.simulationRangedMissEnabled)
            {
                missChance = ShotMissChance(striker, strikerMounted, struckStillMounted, volley,
                    SimulationBattleState.VolleyProgress(state));

                // THE WALL SKEWS THE SHOT. A besieger looses UP at a man on the battlement and misses more; the
                // defender looses DOWN into the press and misses less. One knob, symmetric about 1 (attacker x skew,
                // defender x (2 - skew)), carried on missChance so both the live roll below and the reference
                // expectation further down inherit it. Capped like any miss chance.
                if (siege)
                {
                    float skew = RBMConfig.RBMConfig.simulationSiegeRangedMissSkew;
                    missChance *= strikerIsAttacker ? skew : MathF.Max(0f, 2f - skew);
                    missChance = MBMath.ClampFloat(missChance, 0f, RangedMaxMissChance);
                }

                // AND A STORMING BESIEGER MISSES MORE STILL. The skew above is the GEOMETRY of a siege -- shooting
                // up at a battlement against shooting down into a press -- and it applies to any battle fought at a
                // besieged settlement. This is the wall ASSAULT's own term on the same number, and it stacks on it
                // deliberately: a man crossing the killing ground is not merely shooting upward, he is shooting
                // upward while walking, in a press, at a head that ducks behind a merlon between his shafts. It
                // eases once he is on the ladder -- he is closer -- but it never goes away, because he is still
                // fighting from the worse footing. The defender gets nothing here; his edge on the approach is the
                // rate of fire and the weight of the shot, both handled elsewhere.
                // ...and how much of it he suffers is the WALL's answer: a palisade scatters him less than a great
                // city's battlements do. See SimulationSiege.MeasureWall.
                if (strikerIsAttacker && state != null && state.SiegeAssaultBattle)
                {
                    missChance *= SimulationSiege.AttackerMissMultiplier(state);
                    missChance = MBMath.ClampFloat(missChance, 0f, RangedMaxMissChance);
                }

                // A live shot rolls it and is done. The reference tables cannot -- they are asking what a matchup
                // does, not what one arrow did -- so they take the expectation instead, just below the blow, the same
                // way they take the shield's. The shaft was already spent the instant he loosed it, miss or no (see
                // the quiver deduction where `shooting` is set, above), so nothing more is deducted here.
                if (spend && missChance > 0f && MBRandom.RandomFloat < missChance)
                {
                    // "shoot" / "miss": the trace reads it as the act and its outcome, so the miss RATE comes straight
                    // off the log the way the block and parry rates do -- miss rows over shoot rows. That is the only
                    // thing this system can be calibrated against, and it is why the row is kept at all.
                    breakdown.Phase = "shoot";
                    breakdown.Weapon = striker.Shot.IsValid ? striker.Shot.WeaponType : "-";
                    breakdown.BodyPart = -1;
                    breakdown.Defense = "miss";
                    breakdown.Missed = true;
                    breakdown.Correction = 0f;
                    return true;
                }
            }

            // The board comes up once the blow is fully shaped, not before it -- the defence answers what is actually
            // thrown, charge and brace and all. See the defence resolution below the blow.

            // The blow. A bowman looses his shot, a skirmisher hurls a javelin, and everyone else draws from the
            // weapons on his belt -- at random against a man on foot, and reaching for the spear when it is a horse
            // bearing down on him. And it lands SOMEWHERE on him: a real blow rolls a body part, meets the armour
            // standing over that part, and is worth what RBM says a blow to that part is worth. The reference tables
            // take the expectation over all four instead, since they are asking about a matchup and not a moment.
            // IS THE HORSE BEHIND THIS ONE? Asked here, ahead of the body part, because the answer decides the body
            // part -- see the zone override below. What the charge is WORTH is still settled further down with the
            // rest of the multipliers (the ChargeBonus block); this is only the coin-flip, taken early and kept.
            //
            // A CHARGE IS A HORSE SLAMMING INTO A MAN ON FOOT, and that is the whole of it (!struckStillMounted). It is
            // not something two horsemen do to each other: they close at the same height and much the same speed, there
            // is no standing line for either to break, and neither is a wall for the other to hit. What happens when
            // squadrons meet is a melee on horseback -- lance, sword, the horses turning -- and the model already has
            // that, in MountedVsMounted and the cavalry-clash phase. It is not a charge, and it must not draw the
            // charge's weight or its unblockability, which is what let two riders slam each other every other blow.
            //
            // Only a live blow rolls it. The reference tables take the charge as an expectation blended over all his
            // blows (spend == false, the else-branch below), so there is no single charge for them to aim, and they
            // roll the zone the ordinary way -- the same split hitHorse makes just below, and for the same reason.
            // The chance itself is the ground's (GetChargeChance), thinned by how many men that side still has on foot
            // to be charged -- a horse needs a crowd to break, and twenty men in a bandit scrap are not one. Read
            // against the STRUCK side, whose foot these are. See SimulationBattleState.ChargeChanceAgainst.
            bool chargeEligible = strikerMounted && !missile && engaged && !struckStillMounted
                && striker.ChargeDamage > 0f && state != null;
            float chargeChance = chargeEligible
                ? SimulationBattleState.ChargeChanceAgainst(state, !strikerIsAttacker)
                : 0f;
            bool charging = chargeEligible && spend && MBRandom.RandomFloat < chargeChance;

            HitZones zones = GetHitZones(strikerMounted, missile, struckStillMounted, striker.HasPolearm);

            // HORSE OR MAN. Before the blow is even shaped, a stroke at a mounted troop is committed to one target or
            // the other. Only a LIVE blow rolls it (the reference tables ask what a matchup does to the MAN, and the
            // horse is not part of that question -- so they take the man every time, spend == false below); only while
            // the horse is still under him (a dead mount leaves an ordinary footman, struckStillMounted); and the
            // chance is set by who is swinging -- a footman mostly finds the animal, a horseman mostly the rider, an
            // arrow mostly the rider (the HorseHitChance constants). A blow that finds the horse is resolved wholly
            // against the barding just below and, at the foot of the method, wears the horse pool and returns dealing
            // the rider nothing; it skips his defence entirely, because no shield on his arm answers a spear in the
            // animal's chest -- which is exactly how foot infantry bring a squadron down.
            bool hitHorse = false;
            if (spend && struckStillMounted)
            {
                float horseHitChance = missile ? HorseHitChanceMissile
                    : (strikerMounted ? HorseHitChanceMountedMelee : HorseHitChanceFootMelee);
                hitHorse = MBRandom.RandomFloat < horseHitChance;
            }

            int zoneHit;
            float armor;
            SimulationWeaponModel.WeaponProfile drawn;
            bool braced;
            float actual;
            if (hitHorse)
            {
                // Straight at the animal. The barding is the only armour in the way, and the blow is worth the torso's
                // multiplier -- a horse is one great mass, not a head and a shin. The weapon he drew and whether he set
                // it (a spear braced for the horse) come back as for any blow, because the charge and the brace below
                // still shape a stroke that lands on the mount.
                armor = HorseArmor(struck);
                actual = PhaseDamage(striker, struck, armor, rbmCombat, struckStillMounted,
                    shooting, throwing, ZoneTorso, out drawn, out braced);
                zoneHit = ZoneHorse;
            }
            else if (charging)
            {
                // A CHARGE TAKES HIM IN THE CHEST, and there is no roll about it. Every other blow in this model is a
                // stroke aimed somewhere and it may find a head or a shin -- a charge is not aimed at all. It is a
                // horse arriving, and what a horse arrives at is the middle of a man: the lance is couched level and
                // the animal's own breast is at chest height. Nobody charges a footman in the ankle.
                //
                // The struck man is always on foot here -- a charge is only ever a horse against a standing man, and
                // chargeEligible has already said so.
                armor = ZoneArmor(struck, ZoneTorso, horsesAlive);
                actual = PhaseDamage(striker, struck, armor, rbmCombat, struckStillMounted,
                    shooting, throwing, ZoneTorso, out drawn, out braced);
                zoneHit = ZoneTorso;
            }
            else
            {
                actual = Blow(striker, struck, rbmCombat, zones, horsesAlive, struckStillMounted,
                    shooting, throwing, roll: spend, out zoneHit, out armor, out drawn, out braced);
            }

            // And the reference tables' half of the miss. A live shot already returned above if it went wide; this is
            // the matchup question -- what an archer of this sort is worth against a man of that sort -- and the
            // answer has to carry the shafts that never arrive. So the blow is worth what it does times how often it
            // lands, which is the same expectation the shield block takes for the same reason. Folded into `actual`
            // (not the correction) so it sits in the same place the shield's does, ahead of the baseline and ratio:
            // the equipment ratio and arm-aware target selection both read this figure, and both SHOULD see an
            // inaccurate archer as the weaker striker he is.
            if (!spend && missChance > 0f)
            {
                actual *= 1f - missChance;
            }

            breakdown.Phase = shooting ? "shoot" : (throwing ? "throw" : (cavalryClash ? "horse" : "melee"));
            breakdown.Weapon = drawn.WeaponType;
            breakdown.BodyPart = zoneHit;

            // THE ARCHERS GET THEIR TURNS BACK.
            //
            // Vanilla hands a side pow(men, 0.6) blows a round and picks who throws each one uniformly from the
            // WHOLE side -- so an archer is chosen only as often as archers are common. In the volley nobody else
            // does anything at all, so in a typical army four blows in five are spent on men standing still, and the
            // archers are not shooting slowly, they are being SKIPPED. Their own infantry are eating their turns.
            //
            // This gives them back the tick allocation they would have had if the volley were a battle between the
            // archers alone. It is NOT 1/share -- that would hand a side's whole volley to whatever archers it
            // happens to own, so one bowman in a hundred would loose as many shafts as a hundred bowmen, and how
            // many archers you brought would stop mattering. See SimulationBattleState.VolleyFocus.
            //
            // BUT ONLY WHEN THE STRIKERS ARE PICKED AT RANDOM. VolleyFocus exists solely to undo the ticks the
            // uniform selector wastes on non-archers in the volley: it hands the bows back the share of the round
            // their own infantry were eating. When arm-aware selection is on (simulationArmTargeting), the selector
            // hands the volley to the archers DIRECTLY -- there is no waste to undo -- so applying VolleyFocus as
            // well would boost them a second time for a loss they no longer take. It is neutralised here (left at
            // 1.0) in that case; the phase DEFINITION (IsVolleyPhase) still stands, only this compensation stands
            // down. See SimulationArmTargeting. NOTE FOR CALIBRATION: forcing every volley striker to an archer is
            // not identical to random-plus-focus -- it removes the archer-COUNT dependence VolleyFocus carried --
            // so the volley's ranged output should be re-measured on a paired log with targeting on.
            if (shooting && volley && !RBMConfig.RBMConfig.simulationArmTargeting)
            {
                actual *= SimulationBattleState.VolleyFocus(state, strikerIsAttacker);
            }

            // THE WALL DECIDES WHOSE SHAFTS BITE.
            //
            // On the approach the man on the parapet is shooting DOWN, standing still, braced against stone, at
            // men who are packed together and climbing and cannot answer him properly -- so his shots find the
            // gaps in the harness and carry the drop behind them. The besieger is shooting UP, on the move, at a
            // head that is mostly helmet and mostly hidden, and what does land lands soft and at a bad angle.
            //
            // Once the ladders are up the defender's bonus GOES AWAY ENTIRELY, and that is the design and not an
            // omission: in the assault his advantage is the frontage and the rate of fire, not the weight of the
            // arrow. Making him hit harder there as well would be paying him three times for the same wall. The
            // besieger's penalty stays, because he is still the one fighting from a ladder.
            //
            // Fired shots only. A siege has few thrown weapons in it and no javelin duel at all (there is no
            // skirmish phase -- see SimulationBattleState.IsSkirmishPhase), and a melee blow at the top of a ladder
            // is priced by the width and the wall's defence bonus, not by this.
            // And HOW MUCH of all that a wall is worth is a question about the wall. A palisade thrown up around a
            // frontier castle is not the curtain of a great city: the parapet is lower, the merlons are worse, the
            // climb is shorter, and the edge it hands the man on top of it is correspondingly smaller. Both figures
            // come back already scaled by the settlement's fortification level, so the two phases read the same
            // either way. See SimulationSiege.MeasureWall -- and note it does NOT scale the assault's width.
            if (shooting && state != null && state.SiegeAssaultBattle)
            {
                actual *= strikerIsAttacker
                    ? SimulationSiege.AttackerShotMagnitude(state)
                    : SimulationSiege.DefenderShotMagnitude(state);
            }
            // AND IN THE OPEN FIELD, THE DEFENDER'S HIGH GROUND. Not the wall's hard, phased edge above -- a mild,
            // flat lift to the waiting side's fired shots and an equal debit to the attacker climbing at them. Scoped
            // to field battles: a wall assault is caught by the branch above and priced there, so this only fires when
            // the fight is NOT a storm. See FieldDefenderShotMagnitude.
            else if (shooting && (state == null || !state.SiegeAssaultBattle))
            {
                actual *= strikerIsAttacker ? FieldAttackerShotMagnitude : FieldDefenderShotMagnitude;
            }

            // The charge: weight and speed, which a horseman has only some of the time, and only against a man standing
            // on the ground. A lance at the gallop is a different thing from the same man hemmed in and hacking
            // downward from a standing horse -- and over a long fight he is both, by turns, as he rides in, kills,
            // backs out and comes again. So a share of his blows carry the horse behind them and the rest are just a
            // man swinging from a saddle. A horseman flinging a javelin is never charging -- he is riding past at a
            // distance, which is the point of javelins -- and a horseman fighting another horseman is never charging
            // either, whatever the ground gives him. See chargeEligible, where all of that is settled.
            //
            // How large that share is depends on the ground, but coarsely: a charge only wants room to hit hard, and
            // any field gives it -- about half his blows carry the horse behind them on open plain and in a wood
            // alike. A village street gives it a little, and a wall or a deck none. This is state.ChargeChance, its
            // own terrain reading (see SimulationBattleState.GetChargeChance) -- NOT KitingRoom, which is the finer
            // measure of how far a horse archer can flee, and still shortens among the trees where the charge does not.
            //
            // It fires only once he has MET somebody. While the lines are still closing he has nobody to ride down,
            // and a charge delivered into empty ground is not a charge.
            breakdown.ChargeBonus = 1f;
            if (chargeEligible)
            {
                float chargeMagnitude = striker.ChargeDamage * ChargeStrength;
                if (spend)
                {
                    // A live blow either carries the horse or it does not -- rolled up above `charging`, ahead of the
                    // body part, because a charge that lands takes the man in the chest and the zone had to know.
                    if (charging)
                    {
                        breakdown.ChargeBonus = 1f + chargeMagnitude;
                        actual *= breakdown.ChargeBonus;

                        // And the charge wears the horse that made it. The animal takes damage from its own impact --
                        // more onto a set spear, more into armour -- fed into its own stack's HorsesAlive pool, so a
                        // squadron that charges all battle is ground down and finally unhorsed. Scaled by the charge's
                        // own weight (a heavy destrier hits harder and suffers more), then amplified by what it hit.
                        if (strikerState != null)
                        {
                            float horseSelfDamage = ChargeSelfDamageBase * breakdown.ChargeBonus;
                            // A set spear rebounds the charge. The man setting it is on his own two feet by
                            // construction now -- a charge only ever happens against a standing man (chargeEligible) --
                            // so the polearm is the only question left to ask.
                            if (struck.HasPolearm)
                            {
                                horseSelfDamage *= ChargeSpearRebound;
                            }
                            horseSelfDamage *= 1f + (armor * ChargeArmorRebound);
                            SimulationBattleState.DamageHorse(strikerState, horseSelfDamage);
                        }
                    }
                }
                else
                {
                    // The reference tables and the riposte's reverse-correction pass (spend == false, live state) take
                    // the EXPECTATION instead -- blended by the terrain-read charge chance rather than rolled -- so a
                    // reverse correction is deterministic and never stochastically spiked with a full unblockable
                    // charge on a single coin-flip. Pool wear is spend-gated and skipped on this pass. Mirror of the
                    // spend / !spend split the defence and missile-block use below.
                    breakdown.ChargeBonus = 1f + (chargeChance * chargeMagnitude);
                    actual *= breakdown.ChargeBonus;
                }
            }

            // Braced steel. A spear set against a horse is the answer infantry have always had to cavalry, and
            // auto-resolve has never once let them use it. `braced` is already the right question asked and
            // answered: it is true exactly when he drew from his polearms, which he does only against a horse.
            if (braced && !strikerMounted)
            {
                float braceBonus = BraceBonus;

                // The reverse charge: when the horse is CLOSING, its own momentum drives it onto the point, and the
                // braced blow carries a share of the same weight (struck.ChargeDamage x ChargeStrength) that powers a
                // cavalry charge -- mirror of the charge roll above, sourced from the STRUCK horse and gated by the
                // same terrain-read (state.ChargeChance), so on open ground it lands most of the time. This is the one
                // thing that made a charge onto set spears the wreck it was. It stays a normal melee blow for landing
                // (no charge exemption), so the glancing spread still applies -- a deliberate, milder choice.
                if (struckStillMounted && state != null && struck.ChargeDamage > 0f)
                {
                    float closingBonus = struck.ChargeDamage * ChargeStrength * AntiCavalryClosingBonus;

                    // The same thinned chance the charge itself rolls, read from the other end: here the horse is the
                    // STRUCK and the man setting the spear is the striker, so the foot being charged are the STRIKER's
                    // side. Pass strikerIsAttacker, not its negation -- the charge roll above passes the negation, and
                    // the two are opposite for the same reason. If a horse charges rarely into a thin crowd, then it
                    // is also rarely closing onto the spear of a man in that crowd, and the brace must agree.
                    float closingChance = SimulationBattleState.ChargeChanceAgainst(state, strikerIsAttacker);

                    // Same spend / !spend split as the charge above: a live blow rolls the closing charge, the
                    // reference and reverse-correction passes take its expectation so they stay deterministic.
                    braceBonus += spend
                        ? (MBRandom.RandomFloat < closingChance ? closingBonus : 0f)
                        : closingChance * closingBonus;
                }

                actual *= braceBonus;
                breakdown.Braced = true;
            }

            // AND THE HORSE ARCHER RIDES AWAY FROM IT. A footman's spear cannot reach a man who is not standing in
            // front of it, and a horse archer with arrows left never is: he is out at bow range, and if the foot come
            // at him he turns and shoots them from somewhere else. Note where this sits -- AFTER the brace, quite
            // deliberately. A spear set against a charge is no answer to a man who declines the charge; the spearman
            // gets his full 1.6 and still hits nothing but air, and that is exactly the point. The lance and the
            // arrow are untouched by any of it: this asks !striker.IsMounted and !missile, so cavalry and bowmen kill
            // him at the ordinary rate, which is the only way he is ever killed.
            if (struckIsKiting && !strikerMounted && !missile)
            {
                actual *= 1f - (state.KitingRoom * (1f - HorseArcherEvasion));
                breakdown.Evaded = true;
            }

            // THE DEFENCE. However the blow was shaped -- charge, brace and evasion are all in it now -- the man it
            // is aimed at gets to answer it. What a shield can take is denominated in this same simulated damage;
            // see ShieldCapacityPerMan. A blow at the HORSE skips all of this: a shield on the rider's arm and a
            // parry from his blade guard the man, not the animal under him, so a horse hit meets no personal defence
            // (both branches gate on !hitHorse) and falls straight to the horse-routing exit below.
            float shieldIntegrity = 1f;
            bool hasShield = struck.ShieldQuality > 0f;
            if (hasShield && struckState != null)
            {
                shieldIntegrity = SimulationBattleState.ShieldIntegrity(struckState, struck.ShieldHitPoints);
            }
            // A shattered board is no board at all: a man behind it is thrown back on the harder bare-weapon defence.
            bool intactShield = hasShield && shieldIntegrity > 0f;

            if (!hitHorse && RBMConfig.RBMConfig.simulationDefenseSystem)
            {
                // What a block would cost the shield -- the weapon's own toll on a board, NOT the damage it spares
                // the man. Weapon-typed the way RBM's live combat types it (see ShieldDamageFromBlow).
                float shieldDamage = ShieldDamageFromBlow(drawn, missile);
                string defense = "none";

                if (missile)
                {
                    // A missile is answered by the shield ALONE: no weapon block, no parry. The board turns aside
                    // more shafts than blows, and here it either stops the whole of one -- onto the shield's own hit
                    // points -- or it does not stop it at all.
                    float blockChance = intactShield
                        ? GetShieldBlock(struck.ShieldQuality, againstMissile: true) * shieldIntegrity
                        : 0f;
                    // An archer minds his shot, not the shafts coming back: even with a shield he gets it up late, so
                    // he turns aside fewer incoming arrows than a shield-bearer would. See ArcherVsRangedBlockFactor.
                    if (struck.IsRanged)
                    {
                        blockChance *= ArcherVsRangedBlockFactor;
                    }
                    // And a man on a horse blocks a bit less than the same man on foot, board and all -- high, busy,
                    // and slow to bring the shield across from the saddle. See MountedDefenseFactor.
                    if (struckStillMounted)
                    {
                        blockChance *= MountedDefenseFactor;
                    }
                    // The wall. A besieged man gets his board across more often; applied before the branch so the
                    // live roll and the reference expectation both carry it, and capped so the wall is an edge, not
                    // invulnerability. strikerIsAttacker means the struck man is the one on the wall.
                    if (siege && strikerIsAttacker)
                    {
                        blockChance = MBMath.ClampFloat(blockChance * RBMConfig.RBMConfig.simulationSiegeDefenderDefenseBonus,
                            0f, SiegeDefenseChanceCap);
                    }
                    breakdown.ShieldBlock = blockChance;
                    if (spend)
                    {
                        if (blockChance > 0f && MBRandom.RandomFloat < blockChance)
                        {
                            if (spend && struckState != null)
                            {
                                SimulationBattleState.DamageShield(struckState, shieldDamage);
                            }
                            defense = "shield-block";
                            actual = 0f;
                        }
                    }
                    else
                    {
                        // The reference tables want the expectation: a blocked shaft lands nothing, so the mitigation
                        // of the matchup is simply the chance the board is in the way.
                        actual *= 1f - blockChance;
                    }
                }
                else
                {
                    // A melee blow: one defence roll off the DEFENDER'S OWN skill -- easy behind a shield, about
                    // twice as hard with a bare weapon -- and then, on a success, a block-vs-parry split off the
                    // skill GAP between the two men.
                    float defenseChance = intactShield
                        ? ShieldDefenseChance(struck.MeleeSkill)
                        : WeaponDefenseChance(struck.MeleeSkill);

                    // A horseman riding an archer down: his block chance is cut hard and he cannot parry at all. See
                    // CavalryVsArcherDefenseFactor. Applied before the branch so the live roll AND the reference-table
                    // expectation below both carry it, keeping the correction honest.
                    bool riddenDown = strikerMounted && struck.IsRanged;
                    if (riddenDown)
                    {
                        defenseChance *= CavalryVsArcherDefenseFactor;
                    }

                    // A man on a horse blocks a bit less than the same man on foot, shield and all -- high, busy, and
                    // slow to bring the board across from the saddle. Stacks with the ridden-down cut for a mounted
                    // bowman. Applied before the branch so the live roll and the reference expectation both carry it.
                    if (struckStillMounted)
                    {
                        defenseChance *= MountedDefenseFactor;
                    }

                    // A CHARGE is not answered by the defence at all. The weight of the horse is behind it -- no shield
                    // turns it, no blade parries it, and no amount of skill helps the man it is aimed at. So a blow that
                    // carried the charge (breakdown.ChargeBonus > 1, set by the charge roll above) simply lands.
                    if (breakdown.ChargeBonus > 1f)
                    {
                        defenseChance = 0f;
                    }

                    // The wall. A besieged man turns aside more blows; applied after the charge cut (so a charge still
                    // lands, though a siege rarely has one) and capped so the wall is an edge, not invulnerability.
                    // Lifts the parry chance with it, since parry is drawn from this same successful-defence roll.
                    if (siege && strikerIsAttacker)
                    {
                        defenseChance = MBMath.ClampFloat(defenseChance * RBMConfig.RBMConfig.simulationSiegeDefenderDefenseBonus,
                            0f, SiegeDefenseChanceCap);
                    }

                    breakdown.ShieldBlock = defenseChance;
                    if (spend)
                    {
                        if (defenseChance > 0f && MBRandom.RandomFloat < defenseChance)
                        {
                            // No parry when he is being ridden down -- a bow does not answer a lance. And no riposte
                            // FROM a horse against a man on foot: a rider does not fence a footman blade-to-blade for a
                            // counter -- he fights by reach and height, not a parry-riposte between equals at one level
                            // -- so a mounted defender still turns the blow (it falls to a block just below) but lands
                            // no counter on the infantryman beneath him. Cavalry may still riposte each OTHER; this
                            // bars it only against foot attackers, and only while the horse is alive to keep him up.
                            bool noRiposteFromHorse = struckStillMounted && !strikerMounted;
                            float parryShare = (riddenDown || noRiposteFromHorse)
                                ? 0f
                                : ParryShare(struck.MeleeSkill, striker.MeleeSkill);
                            if (MBRandom.RandomFloat < parryShare)
                            {
                                // Parried, and to be answered. The counter itself is spent by the postfix, which
                                // alone holds the battle and the attacker's own soldier to spend it on.
                                defense = "parry";
                                breakdown.Riposte = true;
                            }
                            else if (intactShield)
                            {
                                // A plain shield block: the man takes nothing, the board takes the weapon's toll.
                                defense = "shield-block";
                                if (spend && struckState != null)
                                {
                                    SimulationBattleState.DamageShield(struckState, shieldDamage);
                                }
                            }
                            else
                            {
                                // A weapon deflection carries no hit points to spend: steel turns steel and is none
                                // the worse for it.
                                defense = "weapon-block";
                            }
                            actual = 0f;
                        }
                    }
                    else
                    {
                        // The expectation, for the reference tables: a block and a parry both fully negate, so the
                        // mitigation of a matchup is just the chance the defence lands, whichever kind it is.
                        actual *= 1f - defenseChance;
                    }
                }

                breakdown.Defense = defense;
                breakdown.Defended = defense != "none";
                // The blow reaches the man or it does not; the horse under him is no longer wounded as a side effect
                // of a stroke aimed at the rider. A blow meant for the horse was split off at the top (hitHorse) and
                // never enters this branch -- the animal is worn only by a stroke that genuinely found it.
            }
            else if (!hitHorse)
            {
                // The OLD fractional skim, kept whole for when the defense system is switched off: the board turns
                // aside a capped share of every blow, and wears down by what it turns.
                float shieldBlock = GetShieldBlock(struck.ShieldQuality, againstMissile: missile);
                if (struckState != null && shieldBlock > 0f)
                {
                    shieldBlock *= shieldIntegrity;
                }

                float blocked = actual * shieldBlock;
                actual -= blocked;

                if (spend && struckState != null)
                {
                    SimulationBattleState.DamageShield(struckState, blocked);
                }

                breakdown.ShieldBlock = shieldBlock;
            }

            // THE HORSE, AND THE STROKE THAT FOUND IT. A blow committed to the animal is done here: it wore no
            // personal defence and it wounds no rider. What it deals to the mount is the whole assembled blow --
            // charge, brace and all -- and the horse pool takes it; once that pool is spent the beast falls and its
            // rider fights the rest of the battle on foot (SimulationBattleState.HorsesAlive strips his barding, his
            // charge and his height the moment it does). The rider takes nothing, so the correction is nil the way a
            // defended blow's is, and breakdown.HorseHit keeps the row in the book.
            if (hitHorse)
            {
                breakdown.ArmorMet = armor;
                breakdown.Actual = actual;
                breakdown.HorseHit = true;
                if (spend && struckState != null)
                {
                    SimulationBattleState.DamageHorse(struckState, MathF.Max(0f, actual));
                }
                breakdown.Correction = 0f;
                return true;
            }

            breakdown.ArmorMet = armor;
            breakdown.Actual = actual;

            // A defence that landed negated the whole blow: it deals nothing, full stop. This must short-circuit
            // BEFORE the equipment ratio, because the correction clamp has a 0.1 floor -- run a zeroed blow through
            // it and a blocked sword would still come out dealing a tenth of a hit. The blow is still written down
            // (breakdown.Defended tells RecordHit to keep it), and a parry still owes its riposte, both carried on
            // the breakdown; here we only stop the damage. Only ever true on a live rolled blow.
            if (breakdown.Defended)
            {
                breakdown.Correction = 0f;
                return true;
            }

            // Against what the average man of his arm does to the average man of the other's. In RATIO mode this
            // is the pivot the whole correction turns on; in ABSOLUTE mode the damage no longer turns on it, but
            // it is still measured -- the matchup table's equipment ratio and arm-aware target selection both read
            // it -- so it is computed either way, and only the ratio-mode branch bails when it is missing.
            float baseline = GetBaselineDamage(strikerTroop, struckTroop);
            breakdown.Baseline = baseline;
            breakdown.EquipmentRatio = (baseline > 0f) ? (actual / baseline) : 0f;

            // Vanilla priced this blow on tier and tier alone: pow(power_s / power_k, 0.7), where power is a
            // pure function of the number on the troop card. Divide that back out. A tier is not something a
            // soldier has -- it is a shorthand for the kit he carries and the training behind it, and both are
            // measured above, from his actual armour, his actual weapon and his actual skill. Leaving vanilla's
            // tier term in would charge for the same thing twice, and it is the reason a recruit in mail could
            // not out-fight a looter in rags by more than the 1.41x his tier number allowed.
            //
            // Only the tier BASE is removed HERE. Vanilla's ratio also carries (1 + leaderModifier +
            // contextModifier) on each side. The leader modifier and the captain's perks are left whole and still
            // say everything they said before. The context modifier -- the terrain-vs-arm table -- is NOT divided
            // out here, because this method has no battle and so cannot know the terrain; the postfix lifts it out
            // of every blow afterwards, siege included, folding the factor into breakdown.Correction (see
            // GetVanillaPowerNeutralizingFactor). The reference/matchup tables, which have no battle at all, are
            // terrain-blind by nature and leave it untouched.
            float tierTerm = MathF.Pow(VanillaTierPower(strikerTroop) / VanillaTierPower(struckTroop), 0.7f);
            breakdown.TierTerm = tierTerm;
            if (tierTerm <= 0f)
            {
                return false;
            }

            float correction;
            if (RBMConfig.RBMConfig.simulationAbsoluteDamage)
            {
                // ABSOLUTE. The blow is worth its own real magnitude, not a ratio to a typical one. The postfix
                // multiplies vanilla's number by this correction; setting it to (scale * actual) / (40 * tierTerm)
                // cancels vanilla's 40 base scale AND its tier-power core (pow(power,0.7) = tierTerm * the leader/
                // context part) and puts `actual` in their place. What is left of vanilla's number -- side
                // advantage, the leader/captain modifier, every Tactics and Scouting perk, and its own random
                // spread -- rides through the multiply untouched, which is how "keep all vanilla's factors" and
                // "absolute damage" hold at once. `scale` is the sole calibration dial: it sets how a blow's real
                // magnitude maps onto the hit-point pool the casualty stage wears down. TUNE IT VS A PAIRED LOG.
                //
                // No [0.1,8] clamp here: there is no ratio to clamp, and an absolute mismatch is meant to be
                // lopsided. The upper end is instead bounded per blow against the struck man's own pool, in the
                // postfix (simulationAbsoluteBlowCap), where the man being struck is known.
                correction = (RBMConfig.RBMConfig.simulationAbsoluteScale * actual) / (VanillaBaseScale * tierTerm);
            }
            else
            {
                if (baseline <= 0f)
                {
                    return false;
                }

                // Weight is the whole dial: 0 leaves vanilla exactly as it was, 1 is the model at face value, and
                // above 1 widens the gap between a well-found soldier and a ragged one.
                correction = MathF.Pow(breakdown.EquipmentRatio / tierTerm, RBMConfig.RBMConfig.simulationEquipmentPowerWeight);

                // Wide, because a real mismatch is meant to be lopsided now -- a spear through an unarmoured looter
                // should put him down, and his club should ring off a mail hauberk. But not unbounded: a single
                // simulated blow must not become a massacre on the strength of one freak pairing.
                //
                // The clamp bounds the EQUIPMENT term and nothing else, which is why it is taken here, before the
                // closing penalty rather than after it.
                correction = MBMath.ClampFloat(correction, 0.1f, 8f);
            }

            // A man still closing the distance has already been handled: `approaching && !engaged` returned above with
            // no blow at all. So every blow reaching here is one that genuinely landed -- a shot, a thrown javelin, a
            // cavalry clash, or the melee of lines that have met -- and there is no closing penalty left to apply.

            // A landed blow rarely bites at full force, and none of the four kinds lands whole every time. A melee
            // swing (MeleeLandingExponent) and a FIRED shot (RangedLandingExponent) glance the most -- two-thirds on
            // average. A THROWN weapon is committed and lands harder (ThrownLandingExponent, ~five-sixths), and a
            // CHARGE harder still (ChargeLandingExponent, ~three-quarters) -- both for the blow that catches a shade
            // off-square rather than one that arcs in from afar. Folded INTO the correction (not applied after) so the
            // log's vanilla x correction = dealt identity holds, and placed AFTER the ratio so it does not cancel
            // against the baseline. On the reference tables (spend == false) it is the mean of the draw, 1/(exp+1).
            // Every real landed blow reaching here is a throw, a fired shot, a charge, or a melee, so a spread applies.
            float landingExponent;
            if (throwing)
            {
                landingExponent = ThrownLandingExponent;
            }
            else if (missile)
            {
                landingExponent = RangedLandingExponent;
            }
            else if (breakdown.ChargeBonus > 1f)
            {
                landingExponent = ChargeLandingExponent;
            }
            else
            {
                // 0.5 is calibrated for the defence system removing turned-aside blows upstream; with it off, nothing
                // is removed here, so use the pre-defence-system exponent to avoid landing melee ~2x too hard.
                landingExponent = RBMConfig.RBMConfig.simulationDefenseSystem
                    ? MeleeLandingExponent
                    : MeleeLandingExponentNoDefense;
            }

            float landing = spend
                ? MathF.Pow(MBRandom.RandomFloat, landingExponent)
                : (1f / (landingExponent + 1f));
            correction *= landing;

            breakdown.Correction = correction;
            return true;
        }

        /// <summary>A troop's kit as the model sees it, for the log to print.</summary>
        internal struct KitInfo
        {
            public float Head;

            public float Neck;

            public float Torso;

            public float Shoulder;

            public float Arm;

            public float Leg;

            public float Magnitude;

            public DamageTypes DamageType;

            /// <summary>The weapon class the blow comes from -- which keys RBM's own armour-threshold factors.</summary>
            public string WeaponType;

            /// <summary>The weapon's quality, which in RBM buys PENETRATION rather than force.</summary>
            public float DamageFactor;

            /// <summary>How many melee weapons he has to choose between -- the blow is averaged over all of them.</summary>
            public int WeaponCount;

            /// <summary>And how many kinds of arrow. A bodkin and a broadhead are not the same shot.</summary>
            public int ShotCount;

            /// <summary>Whether one of them is a spear, which is what he reaches for when a horse comes at him.</summary>
            public bool HasPolearm;

            /// <summary>The javelins on his back: what one is worth, what kind it is, and how many he has.</summary>
            public float ThrownMagnitude;

            public string ThrownType;

            public float ThrownPerMan;

            /// <summary>Arrows/bolts/stones per man, read off his quivers -- what his stack's quiver is seeded from.</summary>
            public float ShotPerMan;

            public bool IsPlate;

            public float ShieldBlock;

            public bool IsMounted;

            public bool IsRanged;

            public bool IsValid;
        }

        /// <summary>
        /// The actual items the model read, item by item, with the raw numbers it read off them. RBM builds its
        /// melee weapons from crafting pieces at runtime, so no XML on disk can be trusted to say what a weapon
        /// finally is -- only the loaded ItemObject knows, and this is the only way to see it.
        /// </summary>
        internal static string DescribeItems(CharacterObject troop)
        {
            if (troop == null)
            {
                return "";
            }
            bool rbmCombat = RBMConfig.RBMConfig.rbmCombatEnabled;
            StringBuilder sb = new StringBuilder();

            foreach (Equipment set in EnumerateBattleEquipments(troop))
            {
                if (sb.Length > 0)
                {
                    sb.Append(" | ");
                }
                for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
                {
                    ItemObject item = set[i].Item;
                    if (item == null || item.WeaponComponent == null)
                    {
                        continue;
                    }
                    WeaponComponentData w = item.WeaponComponent.PrimaryWeapon;
                    if (w == null)
                    {
                        continue;
                    }
                    float thrust = w.ThrustDamage;
                    if (rbmCombat && !IsThrown(item) && !IsLauncher(item) && !IsAmmo(item))
                    {
                        thrust *= RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                    }
                    sb.Append(item.Name).Append("[").Append(item.ItemType)
                      .Append(" sw=").Append((int)w.SwingDamage).Append(w.SwingDamageType.ToString().Substring(0, 1))
                      .Append(" th=").Append((int)w.ThrustDamage).Append(w.ThrustDamageType.ToString().Substring(0, 1))
                      .Append("->").Append((int)thrust)
                      .Append("] ");
                }
                for (EquipmentIndex i = EquipmentIndex.NumAllWeaponSlots; i < EquipmentIndex.ArmorItemEndSlot; i++)
                {
                    ItemObject item = set[i].Item;
                    if (item == null || item.ArmorComponent == null)
                    {
                        continue;
                    }
                    ArmorComponent ac = item.ArmorComponent;
                    sb.Append(item.Name).Append("[h").Append(ac.HeadArmor)
                      .Append(" b").Append(ac.BodyArmor)
                      .Append(" a").Append(ac.ArmArmor)
                      .Append(" l").Append(ac.LegArmor).Append("] ");
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// The baselines themselves, written out once at the top of the log.
        ///
        /// Every correction in this model is a ratio against these five numbers per arm, so a baseline that is
        /// quietly wrong makes EVERY blow quietly wrong -- and it will not look wrong anywhere else, because the
        /// individual blows all still add up. Two separate bugs have now hidden in here (heroes given a bucket of
        /// their own, which cancelled their own advantage; and lords and villagers counted into the population,
        /// which moved the average out from under real soldiers). Both would have been obvious in one glance at
        /// this table. So the table gets printed.
        /// </summary>
        internal static string DescribeBaselines()
        {
            EnsureBaselines();

            string[] names = new string[BucketCount];
            names[InfantryType] = "inf";
            names[ArcherType] = "arc";
            names[CavalryType] = "cav";
            names[HorseArcherType] = "HA";

            StringBuilder sb = new StringBuilder();
            sb.Append("Baselines -- the damage a typical man of each arm lands on a typical man of each arm.")
              .Append(Environment.NewLine);
            sb.Append("Measured from the game's own roster (line troops only: no heroes, no villagers).")
              .Append(Environment.NewLine);
            sb.Append("           vs ");
            for (int k = 0; k < BucketCount; k++)
            {
                sb.Append(names[k].PadLeft(8));
            }
            sb.Append("     (troops)").Append(Environment.NewLine);

            for (int s = 0; s < BucketCount; s++)
            {
                sb.Append("  ").Append(names[s].PadRight(9));
                for (int k = 0; k < BucketCount; k++)
                {
                    sb.Append(SimulationLog.Fmt(_baselineDamage[s][k]).PadLeft(8));
                }
                sb.Append(_bucketPopulation[s].ToString().PadLeft(12)).Append(Environment.NewLine);
            }
            return sb.ToString();
        }

        internal static KitInfo ExplainKit(CharacterObject troop)
        {
            KitInfo info = default(KitInfo);
            if (troop == null)
            {
                return info;
            }
            EnsureBaselines();
            TroopKit kit = GetKit(troop);
            info.Head = kit.Head;
            info.Neck = kit.Neck;
            info.Torso = kit.Torso;
            info.Shoulder = kit.Shoulder;
            info.Arm = kit.Arm;
            info.Leg = kit.Leg;
            // What he shoots if he shoots, else the heaviest thing on his belt -- as a LABEL for his arsenal, with
            // the magnitude averaged across the whole of it. He does not fight with one weapon any more, so no one
            // weapon can be printed as his.
            SimulationWeaponModel.WeaponProfile shown = kit.Shot;
            if (!(kit.IsRanged && kit.Shot.IsValid))
            {
                shown = default(SimulationWeaponModel.WeaponProfile);
                float heaviest = 0f, mean = 0f;
                if (kit.Melee != null)
                {
                    foreach (MeleeOption option in kit.Melee)
                    {
                        mean += option.Profile.Magnitude * option.Weight;
                        if (option.Profile.Magnitude > heaviest)
                        {
                            heaviest = option.Profile.Magnitude;
                            shown = option.Profile;
                        }
                    }
                }
                shown.Magnitude = mean;
            }
            info.Magnitude = shown.Magnitude;
            info.DamageType = shown.DamageType;
            info.WeaponType = shown.WeaponType ?? "-";
            info.DamageFactor = shown.DamageFactor;
            info.WeaponCount = (kit.Melee != null) ? kit.Melee.Length : 0;
            info.ShotCount = (kit.Shots != null) ? kit.Shots.Length : 0;
            info.HasPolearm = kit.HasPolearm;
            info.ThrownMagnitude = kit.Thrown.IsValid ? kit.Thrown.Magnitude : 0f;
            info.ThrownType = kit.Thrown.IsValid ? kit.Thrown.WeaponType : null;
            info.ThrownPerMan = kit.ThrownPerMan;
            info.ShotPerMan = kit.ShotPerMan;
            info.IsPlate = kit.IsPlate;
            // The melee figure, as the plain statement of what his shield is worth. The blow-by-blow table below it
            // in the log prints the one that actually applied, which is higher when an arrow was what met it.
            info.ShieldBlock = GetShieldBlock(kit.ShieldQuality, againstMissile: false);
            info.IsMounted = kit.IsMounted;
            info.IsRanged = kit.IsRanged;
            info.IsValid = kit.IsValid;
            return info;
        }

        /// <summary>
        /// Vanilla's tier-only troop power -- <c>DefaultMilitaryPowerModel.GetDefaultTroopPower</c>, the base it
        /// multiplies by the leader and terrain modifiers. Recomputed here rather than called so that a patch on
        /// the model cannot make this divide out something other than what vanilla actually charged.
        /// </summary>
        private static float VanillaTierPower(CharacterObject troop)
        {
            int tier = troop.IsHero ? (troop.HeroObject.Level / 4 + 1) : troop.Tier;
            float power = (2 + tier) * (10 + tier) * 0.02f;
            if (troop.IsHero)
            {
                power *= 1.5f;
            }
            return power;
        }

        /// <summary>
        /// The factor that lifts vanilla's own power modifiers back out of a blow: the terrain-vs-arm bonus always
        /// (on a field battle), and the leader's captain-perk proxy once this model prices captain perks properly.
        ///
        /// Vanilla (<c>DefaultCombatSimulationModel.SimulateHit</c>) prices a blow on
        /// <c>pow(troopPower_s / troopPower_k, 0.7)</c>, where <c>troopPower = defaultPower * (1 + leader +
        /// GetContextModifier)</c>. The equipment correction divides out only the DEFAULT (tier) term, so both of
        /// the other two ride untouched into the result -- and neither cancels between striker and struck, because
        /// they are different arms, on different sides, under different lords.
        ///
        /// THE CONTEXT is the <c>(arm x terrain x side)</c> table -- cavalry worth a quarter more attacking on open
        /// ground, docked defending a wood. An arm's edge is meant to come from its horse and its lance now, both
        /// already priced in the equipment ratio, not from the field it happens to stand on. So it is lifted from
        /// every blow, siege included -- a siege's own facts (no horses, the wall, the field-vs-wall kit) are priced
        /// by this model's siege handling, not by vanilla's table layered on top. (The Estimated context never carried
        /// one to begin with -- vanilla skips GetContextModifier for it. The map power model DOES keep the siege
        /// context for the AI's strength read; that is a different number, see StrategicTroopPower.)
        ///
        /// THE LEADER TERM is <c>Hero.PowerModifier</c>, and it is worth being exact about what that actually is,
        /// because it is not what its name suggests. <c>DefaultMilitaryPowerModel.GetPowerModifierOfHero</c> walks
        /// every perk in the game, counts the ones whose PRIMARY role is Captain and that this hero owns, and turns
        /// the COUNT into a percentage by skill tier. It is not a leadership bonus, not a tactics bonus, not
        /// anything else: it is a tally of captain perks and nothing more. So once SimulationPerks asks the real
        /// captains for their real perks, this term is the same thing counted twice, and out it comes. With the perk
        /// system off it is kept exactly as before, and nothing here changes.
        ///
        /// Not by patching GetPowerModifierOfHero, note: that also feeds GetPowerOfParty, which is how the campaign
        /// AI decides whether a fight is worth having. This is a fixup to a BLOW, and it stays inside the blow.
        ///
        /// Recomputed through the live model, exactly as <see cref="VanillaTierPower"/> mirrors the tier base, so
        /// that whatever vanilla actually charged is what we lift -- no more, no less. Folded INTO the correction by
        /// the caller rather than applied after it, so the log's Vanilla x Correction = Final identity holds.
        /// </summary>
        private static float GetVanillaPowerNeutralizingFactor(CharacterObject strikerTroop, CharacterObject struckTroop,
            PartyBase strikerParty, PartyBase struckParty)
        {
            if (strikerTroop == null || struckTroop == null || strikerParty == null || struckParty == null
                || strikerParty.MapEvent == null || struckParty.MapEvent == null
                || strikerParty.MapEventSide == null || struckParty.MapEventSide == null)
            {
                return 1f;
            }

            var model = Campaign.Current?.Models?.MilitaryPowerModel;
            if (model == null)
            {
                return 1f;
            }

            MapEvent.PowerCalculationContext strikerContext = strikerParty.MapEvent.SimulationContext;
            MapEvent.PowerCalculationContext struckContext = struckParty.MapEvent.SimulationContext;

            // WHAT VANILLA CHARGED. Estimated is asked for nothing, because vanilla asks it for nothing -- calling
            // the model there would invent a modifier the blow never carried and then dutifully divide it out.
            bool estimated = strikerContext == MapEvent.PowerCalculationContext.Estimated;
            float chargedContextStriker = estimated ? 0f : model.GetContextModifier(strikerTroop, strikerParty.Side, strikerContext);
            float chargedContextStruck = estimated ? 0f : model.GetContextModifier(struckTroop, struckParty.Side, struckContext);
            float chargedLeaderStriker = LeaderModifierOf(strikerParty);
            float chargedLeaderStruck = LeaderModifierOf(struckParty);

            // AND WHAT THE BLOW SHOULD KEEP. The context -- vanilla's (arm x terrain x side) table -- is lifted out
            // of EVERY blow now, siege included: an arm's edge comes from its horse and its lance, both already in the
            // equipment ratio, and a siege's own facts (no horses, the wall, the field-vs-wall kit) are priced by this
            // model's own siege handling, not by vanilla's guess layered on top. So the blow keeps no context at all;
            // the map power model keeps the siege context for the AI's strength read, but that is a different number
            // (see StrategicTroopPower). The leader's captain-perk tally goes whenever we price the captains ourselves.
            float keptContextStriker = 0f;
            float keptContextStruck = 0f;
            float keptLeaderStriker = SimulationPerks.Enabled ? 0f : chargedLeaderStriker;
            float keptLeaderStruck = SimulationPerks.Enabled ? 0f : chargedLeaderStruck;

            float chargedStriker = 1f + chargedLeaderStriker + chargedContextStriker;
            float chargedStruck = 1f + chargedLeaderStruck + chargedContextStruck;
            float keptStriker = 1f + keptLeaderStriker + keptContextStriker;
            float keptStruck = 1f + keptLeaderStruck + keptContextStruck;

            // Nothing came off either man: nothing to lift.
            if (chargedStriker == keptStriker && chargedStruck == keptStruck)
            {
                return 1f;
            }

            // A pathological leader+context sum could reach zero or below, where vanilla's own pow() is already
            // undefined; leave such a blow exactly as vanilla left it rather than invent a number for it.
            if (chargedStriker <= 0f || chargedStruck <= 0f || keptStriker <= 0f || keptStruck <= 0f)
            {
                return 1f;
            }

            float vanillaRatio = MathF.Pow(chargedStriker / chargedStruck, 0.7f);
            float neutralRatio = MathF.Pow(keptStriker / keptStruck, 0.7f);
            if (vanillaRatio <= 0f)
            {
                return 1f;
            }
            return neutralRatio / vanillaRatio;
        }

        /// <summary>
        /// The side commander's power modifier, as vanilla caches it into <c>MapEventSide.LeaderSimulationModifier</c>
        /// (an internal field): <c>LeaderParty.LeaderHero?.PowerModifier</c>. Recomputed off the public API so the
        /// fixup lifts the same leader term vanilla actually charged -- see
        /// <see cref="GetVanillaPowerNeutralizingFactor"/> for what that term really contains and when it comes out.
        /// </summary>
        private static float LeaderModifierOf(PartyBase party)
        {
            return party?.MapEventSide?.LeaderParty?.LeaderHero?.PowerModifier ?? 0f;
        }

        /// <summary>
        /// What DefaultSkillEffects.TacticsAdvantage charges a point of the side commander's Tactics: +0.1% a
        /// point, so a hundred-Tactics general lands every one of his side's blows 10% the harder. It is a pure
        /// AddFactor on a base-one advantage (see DefaultCombatSimulationModel.GetPartyBattleAdvantage), riding
        /// into the blow as a flat one-sided multiplier -- the "personal simulation advantage" this rework lifts
        /// out. Kept here as the exact figure to divide back off, so what we replace is precisely what vanilla
        /// added and no more.
        /// </summary>
        private const float VanillaTacticsAdvantagePerPoint = 0.001f;

        /// <summary>
        /// What a point of a side commander's Tactics is worth in the REPLACEMENT: +0.05% a point, +5% at a
        /// hundred -- half vanilla's rate, the "much lower degree" the redesign asked for. Unlike vanilla's it
        /// tells on BOTH sides (see <see cref="CommanderTacticsFactor"/>): a drilled army lands its blows a
        /// little harder and turns the enemy's a little better alike, so only the GAP between the two generals'
        /// training survives into the blow -- which is what raising every man's skill on both sides actually
        /// comes to once the blows are traded, and why two equal commanders cancel to nothing.
        /// </summary>
        private const float CommanderTacticsPerPoint = 0.0005f;

        /// <summary>
        /// A side commander's Tactics skill -- the same hero vanilla reads for its advantage: the LEADER of the
        /// side's leader party, shared by every man standing on that side. Zero where there is no commander to
        /// ask, which leaves the blow exactly as it was.
        /// </summary>
        private static int SideCommanderTactics(PartyBase party)
        {
            Hero commander = party?.MapEventSide?.LeaderParty?.LeaderHero;
            return (commander != null) ? commander.GetSkillValue(DefaultSkills.Tactics) : 0;
        }

        /// <summary>
        /// The commander-Tactics fixup for one blow, folded into the correction like every other lift so the log's
        /// Vanilla x Correction = Final identity holds. Two things happen at once: vanilla's one-sided Tactics
        /// advantage is divided back off the striker's blow (it rode in through <c>strikerAdvantage</c>, untouched
        /// by the equipment correction), and a gentler two-sided edge is put in its place -- the striker's general
        /// sharpening his side's blow against the softening of the struck man's general defending his.
        ///
        /// The neutralisation is <c>1 / (1 + T x per-point)</c>, which is exact for a field battle. In a siege the
        /// storming penalty (a flat -10% AddFactor vanilla lays on the attacker) rides the same base-one
        /// accumulator, so the divide is a percent shy of exact there -- and that is deliberate: the -10% is not a
        /// Tactics term and is kept, along with the PreBattleManeuvers perk gap, both of which this leaves in the
        /// blow. Only the base Tactics skill effect is replaced.
        /// </summary>
        private static float CommanderTacticsFactor(PartyBase strikerParty, PartyBase struckParty)
        {
            int strikerTactics = SideCommanderTactics(strikerParty);
            int struckTactics = SideCommanderTactics(struckParty);
            if (strikerTactics <= 0 && struckTactics <= 0)
            {
                return 1f;
            }

            float neutraliseVanilla = 1f / (1f + strikerTactics * VanillaTacticsAdvantagePerPoint);
            float replacement = (1f + strikerTactics * CommanderTacticsPerPoint)
                / (1f + struckTactics * CommanderTacticsPerPoint);
            return neutraliseVanilla * replacement;
        }

        /// <summary>
        /// Measure, from the game's own roster, the typical armour of each tier and the damage a typical troop
        /// of each tier lands on it. Nothing here is a guessed constant, and that is deliberate: the correction
        /// is a ratio against this baseline, so any baseline whose shape across tiers differs from the model's
        /// own would quietly hand one end of the tier range a bonus and tax the other, for no reason but the
        /// curve. Measuring also means the model adapts to whatever items a mod loads.
        /// </summary>
        private static void EnsureBaselines()
        {
            // EVERY setting the baselines or the kits bake in has to be watched here, and the reason is the nastiest
            // kind of bug there is. `actual` is computed live, on every blow, from whatever the config says NOW.
            // The baseline it is divided by was computed once, from whatever the config said THEN. Move a setting
            // from the config screen mid-session and the two no longer speak the same language -- the correction
            // skews, quietly, and nothing anywhere looks broken.
            //
            //   armorMultiplier / armorThresholdModifier : read by RbmDamage, so they are inside every baseline.
            //   ThrustMagnitudeModifier                  : read by the thrust and thrown energies, so it is inside
            //                                              every KIT. (The config screen really does change it --
            //                                              RBMConfigViewModel recomputes OneHandedThrustDamageBonus
            //                                              from it as the reciprocal.)
            //   rbmCombatEnabled                         : picks the whole armour equation AND the skill curve.
            //   simulationShieldBlockChance              : sits in the baseline as what a typical man turns aside.
            //
            // simulationEquipmentPowerWeight is deliberately absent: it is applied at the very end of Explain and
            // never touches a kit or a baseline, so it can be moved freely.
            //
            // simulationPerkSystem is deliberately absent too, for a different reason: it rides in the CACHE KEY.
            // A captain's teaching only ever reaches a kit through his perk signature, the signature is 0 whenever
            // the system is off, and the signature-0 kit is the uncaptained one -- so a kit under any given key is
            // the same kit whatever the setting says today, and the baselines (which are built uncaptained, and must
            // be: a reference divided by itself measures nothing) never see a captain at all. Moving this setting
            // cannot skew anything, so nothing has to be rebuilt when it moves. See SimulationPerks.SkillOf, which
            // goes out of its way not to read the setting for exactly this reason.
            bool rbmCombat = RBMConfig.RBMConfig.rbmCombatEnabled;
            float shieldBlockChance = RBMConfig.RBMConfig.simulationShieldBlockChance;
            float armorMultiplier = RBMConfig.RBMConfig.armorMultiplier;
            float armorThreshold = RBMConfig.RBMConfig.armorThresholdModifier;
            float thrustModifier = RBMConfig.RBMConfig.ThrustMagnitudeModifier;
            bool defenseSystem = RBMConfig.RBMConfig.simulationDefenseSystem;

            if (_baselinesBuilt
                && _baselineRbmCombat == rbmCombat
                && _baselineShieldBlockChance == shieldBlockChance
                && _baselineArmorMultiplier == armorMultiplier
                && _baselineArmorThreshold == armorThreshold
                && _baselineThrustModifier == thrustModifier
                && _baselineDefenseSystem == defenseSystem)
            {
                return;
            }

            // A setting has moved since these were built (or they were never built at all). The kits go with
            // them: a kit's magnitude carries the skill curve, and which curve that is depends on the combat
            // model. Rebuilding both together is the only way they can be made to agree.
            //
            // _baselinesBuilt is NOT set here. It is set at the very end, once the table actually exists. Setting
            // it up front meant that one bad item on one modded troop -- anything that threw between here and the
            // end -- would leave the flag latched true with _baselineDamage still null, and since this method then
            // early-returns forever, every blow of every battle for the rest of the session would throw a
            // NullReferenceException out of a Harmony postfix. Build first, then claim to have built.
            _baselineRbmCombat = rbmCombat;
            _baselineShieldBlockChance = shieldBlockChance;
            _baselineArmorMultiplier = armorMultiplier;
            _baselineArmorThreshold = armorThreshold;
            _baselineThrustModifier = thrustModifier;
            _baselineDefenseSystem = defenseSystem;
            _kitCache.Clear();

            List<TroopKit>[] byBucket = new List<TroopKit>[BucketCount];
            for (int i = 0; i < BucketCount; i++)
            {
                byBucket[i] = new List<TroopKit>();
            }

            foreach (CharacterObject character in CharacterObject.All)
            {
                if (character == null || !IsBaselineTroop(character))
                {
                    continue;
                }
                TroopKit kit = GetKit(character);
                if (!kit.IsValid)
                {
                    continue;
                }
                byBucket[GetBucket(character)].Add(kit);
            }

            // The armour a typical man of each arm stands in, kept zone by zone -- it cannot be flattened into a
            // single number here any more than it can anywhere else, because how much of it a blow meets is not
            // decided until we know who threw the blow.
            TroopKit[] typical = new TroopKit[BucketCount];
            bool[] bucketMounted = new bool[BucketCount];
            for (int i = 0; i < BucketCount; i++)
            {
                int count = byBucket[i].Count;
                if (count == 0)
                {
                    continue;
                }
                float head = 0f, neck = 0f, torso = 0f, shoulder = 0f, arm = 0f, leg = 0f;
                float horseLeg = 0f, horseBody = 0f;
                int mounted = 0;
                int plate = 0;
                foreach (TroopKit kit in byBucket[i])
                {
                    head += kit.Head;
                    neck += kit.Neck;
                    torso += kit.Torso;
                    shoulder += kit.Shoulder;
                    arm += kit.Arm;
                    leg += kit.Leg;

                    // The horse and its barding, WHICH MUST BE HERE. They were not, and the omission handed every
                    // cavalryman in the game a free suit of armour: the live blow adds HorseLeg and HorseBody to
                    // the man it strikes (see WeightedArmor), so the rider taking the blow was barded while the
                    // "typical rider" he was measured against sat on nothing at all. A footman hacking upward meets
                    // the horse at the leg 45% of the time, so several points of armour were appearing in `actual`
                    // that could never appear in `baseline` -- a permanent, invisible tax on everyone who fights
                    // cavalry, and exactly the bug class the comments above warn about, in the opposite direction.
                    horseLeg += kit.HorseLeg;
                    horseBody += kit.HorseBody;

                    if (kit.IsMounted)
                    {
                        mounted++;
                    }
                    if (kit.IsPlate)
                    {
                        plate++;
                    }
                }
                typical[i].Head = head / count;
                typical[i].Neck = neck / count;
                typical[i].Torso = torso / count;
                typical[i].Shoulder = shoulder / count;
                typical[i].Arm = arm / count;
                typical[i].Leg = leg / count;
                typical[i].HorseLeg = horseLeg / count;
                typical[i].HorseBody = horseBody / count;
                bucketMounted[i] = (mounted * 2) > count;
                typical[i].IsPlate = (plate * 2) > count;
            }

            // The shield the average shield-bearer carries. Measured over the men who actually carry one, so
            // that arms which carry none (horse archers, mostly) do not drag the common shield down to nothing.
            float shieldQualitySum = 0f;
            int shieldBearers = 0;
            for (int i = 0; i < BucketCount; i++)
            {
                foreach (TroopKit kit in byBucket[i])
                {
                    if (kit.ShieldQuality > 0f)
                    {
                        shieldQualitySum += kit.ShieldQuality;
                        shieldBearers++;
                    }
                }
            }
            _typicalShieldQuality = (shieldBearers > 0) ? (shieldQualitySum / shieldBearers) : 0f;

            // And so, the share of blows the average man of each arm turns aside -- counting the men with no
            // shield at all as the zeroes they are, since it is his whole arm he is measured against.
            _typicalShieldBlock = new float[BucketCount];
            _typicalShieldBlockVsMissile = new float[BucketCount];
            for (int i = 0; i < BucketCount; i++)
            {
                if (byBucket[i].Count == 0)
                {
                    continue;
                }
                float blockSum = 0f;
                float missileBlockSum = 0f;
                foreach (TroopKit kit in byBucket[i])
                {
                    blockSum += GetShieldBlock(kit.ShieldQuality, againstMissile: false);
                    missileBlockSum += GetShieldBlock(kit.ShieldQuality, againstMissile: true);
                }
                _typicalShieldBlock[i] = blockSum / byBucket[i].Count;
                _typicalShieldBlockVsMissile[i] = missileBlockSum / byBucket[i].Count;
            }

            // And, for the skill-based defense system, the share of MELEE blows the average man of each arm turns
            // aside -- block or parry alike, since both fully negate, so the mitigation of a matchup is just the
            // chance the defence lands. Shields are whole in the baseline (no battle has splintered them), so a
            // shield-bearer draws the easy shield chance and everyone else the harder weapon one, each off his own
            // skill. This is the denominator the live blow's own defence expectation is measured against, so the two
            // MUST be computed by the same functions -- ShieldDefenseChance / WeaponDefenseChance, on both sides.
            _typicalMeleeDefense = new float[BucketCount];
            for (int i = 0; i < BucketCount; i++)
            {
                if (byBucket[i].Count == 0)
                {
                    continue;
                }
                float defenseSum = 0f;
                foreach (TroopKit kit in byBucket[i])
                {
                    defenseSum += (kit.ShieldQuality > 0f)
                        ? ShieldDefenseChance(kit.MeleeSkill)
                        : WeaponDefenseChance(kit.MeleeSkill);
                }
                _typicalMeleeDefense[i] = defenseSum / byBucket[i].Count;
            }

            // The damage a typical man of each tier lands on it, weapon and damage type included -- so a tier
            // that fields maces prices out against armour differently from one that fields sabres.
            _baselineDamage = new float[BucketCount][];
            float allSum = 0f;
            int allCount = 0;
            for (int s = 0; s < BucketCount; s++)
            {
                _baselineDamage[s] = new float[BucketCount];
                for (int k = 0; k < BucketCount; k++)
                {
                    List<TroopKit> strikers = byBucket[s];
                    if (strikers.Count == 0)
                    {
                        continue;
                    }
                    float sum = 0f;
                    foreach (TroopKit kit in strikers)
                    {
                        // Whether this man SHOOTS has to be settled before anything else, because it decides both
                        // where his blows land and what they are made of -- and those two must agree. They did not:
                        // the hit zones were picked on IsRanged alone while the damage was picked on
                        // (IsRanged && he really has a bow), so a bowman whose bow failed to price got Missile
                        // zones with a sword's damage, and his `actual` met different armour from his `baseline`.
                        bool shooting = kit.IsRanged && kit.Shot.IsValid;

                        // Drawn the same way it will be drawn in the battle -- the shot for a bowman, and for
                        // everyone else the average of his belt, narrowed to his spears when the arm he faces
                        // rides. The polearm preference belongs HERE as well as in the blow, or a spearman would
                        // be measured against a baseline of men who never reached for theirs and every infantry
                        // troop in Calradia would read as unusually good against horse.
                        //
                        // The brace bonus deliberately does NOT come into the baseline. That one is a thumb on
                        // the scale -- auto-resolve has never let infantry set a spear, and it should -- so it
                        // must survive the division rather than cancel in it.
                        //
                        // What the typical man of that arm blocks is part of what a blow at him typically achieves,
                        // so it belongs in the baseline. Without it, every shield-bearer would look unusually hard
                        // to hurt, when carrying a shield is simply what an infantryman does. And a shield answers
                        // an arrow better than a blow, so WHICH of the two figures applies depends on the man
                        // throwing it -- which is why it is taken here, striker by striker, and not once at the end.
                        //
                        // A missile is answered by the shield either way (the ranged path is unchanged by the defense
                        // system). A MELEE blow, under the skill-based system, is mitigated instead by the chance the
                        // defender's block or parry fully negates it -- the same figure his live blow expects, so the
                        // ratio stays clean. Fall back to the old fractional block when the system is off.
                        float blocked = shooting
                            ? _typicalShieldBlockVsMissile[k]
                            : (defenseSystem ? _typicalMeleeDefense[k] : _typicalShieldBlock[k]);

                        // The SAME function the live blow calls, deliberately -- and here asked for the expectation
                        // rather than a roll, since a baseline is a matchup and not a moment. It has to be the same
                        // function or the body-part multipliers would sit in the blow and not in the baseline, and
                        // every striker in Calradia would read as unusually good (or bad) purely because of where
                        // his blows happen to land.
                        HitZones zones = GetHitZones(kit.IsMounted, shooting, bucketMounted[k], kit.HasPolearm);

                        int zoneHit;
                        float armor;
                        SimulationWeaponModel.WeaponProfile drawn;
                        bool braced;
                        sum += Blow(kit, typical[k], rbmCombat, zones, 1f, bucketMounted[k],
                                    shooting, throwing: false, roll: false,
                                    zoneHit: out zoneHit, armorMet: out armor, drawn: out drawn, braced: out braced)
                             * (1f - blocked);
                    }
                    float mean = sum / strikers.Count;
                    _baselineDamage[s][k] = mean;
                    allSum += mean;
                    allCount++;
                }
            }

            _globalBaselineDamage = (allCount > 0) ? (allSum / allCount) : 0f;

            _bucketPopulation = new int[BucketCount];
            for (int i = 0; i < BucketCount; i++)
            {
                _bucketPopulation[i] = byBucket[i].Count;
            }

            // The table exists. NOW it is built.
            _baselinesBuilt = true;
        }

        /// <summary>The typical damage for this matchup, falling back to the roster-wide average for a tier the game never fields.</summary>
        private static float GetBaselineDamage(CharacterObject strikerTroop, CharacterObject struckTroop)
        {
            float baseline = _baselineDamage[GetBucket(strikerTroop)][GetBucket(struckTroop)];
            return (baseline > 0f) ? baseline : _globalBaselineDamage;
        }

        /// <summary>
        /// A lord is bucketed as what he fights as. He is a soldier who happens to own land: he rides or he walks,
        /// he shoots or he swings, and there is a typical man of each of those he can be measured against. What
        /// makes him a lord -- the plate, the warhorse, the lifetime of training -- is the thing we are trying to
        /// MEASURE, so it must not be hidden inside his own baseline.
        /// </summary>
        private static int GetBucket(CharacterObject troop)
        {
            return GetTroopType(troop);
        }

        /// <summary>
        /// The arm this troop fights as -- InfantryType/ArcherType/CavalryType/HorseArcherType -- surfaced for
        /// arm-aware target selection (SimulationArmTargeting). It is the SAME classifier the damage model buckets
        /// by (GetBucket), deliberately, so selection and pricing never disagree about what a man is. Pure over the
        /// troop template, so the caller may cache it by CharacterObject and never invalidate it.
        /// </summary>
        internal static int ArmOf(CharacterObject troop)
        {
            return (troop != null) ? GetBucket(troop) : -1;
        }

        /// <summary>
        /// Whether this troop carries javelins, throwing axes or darts -- the mark of a foot skirmisher who still
        /// has something to do in the skirmish phase (he hurls them as the lines close). Read off the same cached
        /// kit the damage path uses, so it costs nothing after the kit is built. Used only to keep a javelin-armed
        /// footman in the pool of active skirmish strikers; everyone else on foot is merely walking by then.
        /// </summary>
        /// <summary>
        /// What the animal under this troop is worth before anyone's veterinary touches it -- the horse's own Monster
        /// health and extra_health, off the same cached kit the damage path reads, so it costs nothing after the kit
        /// is built. For the log, which needs the base to print a "220 -> 240" against; the battle itself reads it
        /// straight off the kit. Zero for a man who walks.
        /// </summary>
        internal static float MountHealthOf(CharacterObject troop)
        {
            if (troop == null)
            {
                return 0f;
            }
            return GetKit(troop).HorseHealth;
        }

        internal static bool CarriesThrown(CharacterObject troop)
        {
            if (troop == null)
            {
                return false;
            }
            TroopKit kit = GetKit(troop);
            return kit.IsValid && kit.Thrown.IsValid && kit.ThrownPerMan > 0f;
        }

        // Whether a troop carries a sling, cached by template. A template's kit is fixed; a hero's is cleared with
        // his kit in ForgetHeroKits, since a slinger hero is a thing that could change between battles.
        private static readonly Dictionary<CharacterObject, bool> _slingCache = new Dictionary<CharacterObject, bool>();

        /// <summary>
        /// A SLINGER IS AN ARCHER. The game classes a man who carries a sling as INFANTRY -- CharacterObject.IsRanged
        /// comes back false -- because a sling is a shepherd's sidearm, not a soldier's trade. But on the field he
        /// does exactly what a bowman does: he stands off and slings while the lines are apart, and closes to his belt
        /// weapon only when the stones run out. Auto-resolve should field him as that skirmisher -- shooting in the
        /// volley, struck as a shooter, bucketed with the archers -- so a sling in his kit makes him ranged HERE,
        /// whatever the card says. A bow or crossbow already sets IsRanged, so this only ever catches the sling: the
        /// one ranged arm the game hides among the foot.
        /// </summary>
        private static bool CarriesSling(CharacterObject troop)
        {
            bool has;
            if (_slingCache.TryGetValue(troop, out has))
            {
                return has;
            }

            has = false;
            foreach (Equipment set in EnumerateBattleEquipments(troop))
            {
                for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
                {
                    ItemObject item = set[i].Item;
                    if (item != null && item.ItemType == ItemObject.ItemTypeEnum.Sling)
                    {
                        has = true;
                        break;
                    }
                }
                if (has)
                {
                    break;
                }
            }

            _slingCache[troop] = has;
            return has;
        }

        /// <summary>troop.IsRanged, but a slinger counts too (see CarriesSling). The one ranged test for the whole model.</summary>
        internal static bool IsRangedTroop(CharacterObject troop)
        {
            return troop.IsRanged || CarriesSling(troop);
        }

        /// <summary>The four arms of service, split exactly as vanilla's own power model splits them (a slinger being ranged).</summary>
        private static int GetTroopType(CharacterObject troop)
        {
            bool ranged = IsRangedTroop(troop);
            if (troop.IsMounted)
            {
                return ranged ? HorseArcherType : CavalryType;
            }
            return ranged ? ArcherType : InfantryType;
        }

        /// <summary>
        /// Whether this character is a rank-and-file soldier: one of the men who MAKE UP the line, and so one of
        /// the men a baseline is an average of.
        ///
        /// CharacterObject.All is not a muster roll. It is every character the game has ever heard of, and it is
        /// full of people who never see a battle: villagers, townsfolk, tavern keepers, blacksmiths, musicians,
        /// shop workers. They carry pitchforks and kitchen knives, nothing about them looks INVALID, and they were
        /// all quietly counted as infantry -- dragging down the very average that decides whether a real soldier
        /// is any good.
        ///
        /// HEROES ARE EXCLUDED, and that is not the same statement as bucketing them (see GetBucket, where they are
        /// bucketed as whatever arm they fight in). A lord is measured AGAINST the line; he is not part of it.
        /// Calradia fields a few hundred lords, nearly all of them mounted in the finest harness in the game, so
        /// leaving them in the population made the typical "cavalryman" a nobleman in plate -- and a Harami in a
        /// mail hauberk with a good sword came out at 0.57 of typical, which is how a tier-4 elite horseman ended
        /// up striking a tier-1 recruit at 0.30x.
        /// </summary>
        private static bool IsBaselineTroop(CharacterObject character)
        {
            if (character.IsHero)
            {
                return false;
            }

            switch (character.Occupation)
            {
                case Occupation.Soldier:
                case Occupation.Mercenary:
                case Occupation.Bandit:
                case Occupation.Gangster:
                case Occupation.Guard:
                case Occupation.CaravanGuard:
                case Occupation.BannerBearer:
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// The kit a troop fights out of with NOBODY over him -- the man as his own template made him.
        ///
        /// This is the right question for everything that asks what a troop IS rather than what he is doing in some
        /// particular battle: the baselines (which must be a neutral reference, or the correction would be dividing
        /// a captained number by a captained one and quietly cancelling the very thing it is measuring), the log's
        /// reference tables, and the gear questions that never touch a skill at all. It is signature 0, so all of
        /// them share the one cache entry, and that entry is byte-identical to what this model built before captains
        /// existed.
        /// </summary>
        private static TroopKit GetKit(CharacterObject troop)
        {
            return GetKit(troop, null, 0);
        }

        /// <summary>
        /// The kit a troop fights out of, as his CAPTAIN has him fighting.
        ///
        /// <paramref name="captain"/> is the man leading the body he stands in, already excluded from being himself
        /// (SimulationCommandStructure.CaptainFor does that at source), and null when there is nobody over him --
        /// which is the case for every reference table, every baseline, and every blow struck in a battle whose
        /// chain of command has not been built. <paramref name="captainSignature"/> is that captain's perk mask,
        /// passed in rather than recomputed because this is called twice per blow and a battle has thousands.
        /// </summary>
        private static TroopKit GetKit(CharacterObject troop, CharacterObject captain, int captainSignature)
        {
            // A troop template's kit and training are fixed, so it is cached for good. A hero's are not -- he buys
            // gear and trains skills as the campaign runs -- but they do not change in the MIDDLE of a battle, and
            // pricing him afresh on every single blow was ruinous: rebuilding a hero's kit runs the thrust-physics
            // simulation over every weapon in every equipment set, and the shadow replay fights the same battle
            // forty times over. So heroes are cached too, and the cache is emptied when a new battle opens.
            //
            // The captain rides in the key rather than the value, so the same template under two different captains
            // is two entries and neither can be handed the other's training. See KitKey.
            KitKey key = new KitKey(troop, captainSignature);
            TroopKit cached;
            if (_kitCache.TryGetValue(key, out cached))
            {
                return cached;
            }

            bool rbmCombat = RBMConfig.RBMConfig.rbmCombatEnabled;

            // A troop template usually lists several battle sets and each man rolls one at random, so no single
            // set speaks for the stack: average the armour over all of them, and pool every melee weapon in every
            // set into the one arsenal the stack fights out of.
            float head = 0f, neck = 0f, torso = 0f, shoulder = 0f, arm = 0f, leg = 0f;
            float horseLeg = 0f, horseBody = 0f, horseHealth = 0f;
            float shotMagnitude = 0f;
            float shieldQuality = 0f, shieldHitPoints = 0f;
            float charge = 0f;
            float bestShotMagnitude = 0f;
            int plateSets = 0;
            SimulationWeaponModel.WeaponProfile bestShot = default(SimulationWeaponModel.WeaponProfile);
            List<MeleeOption> melee = new List<MeleeOption>();
            int sets = 0;

            float thrownMagnitude = 0f, thrownPerMan = 0f, bestThrownMagnitude = 0f;
            float shotPerMan = 0f;
            int thrownSets = 0;
            SimulationWeaponModel.WeaponProfile bestThrown = default(SimulationWeaponModel.WeaponProfile);

            List<ShotOption> shots = new List<ShotOption>();

            foreach (Equipment set in EnumerateBattleEquipments(troop))
            {
                // What he shoots, if shooting is his trade at all. EVERY quiver, in every battle set: the sets are
                // different kits and the quivers within a set are different arrows, and none of them is "the" shot.
                // They all go into the pool, and the blow is the average over it rather than the pick of it.
                //
                // The quivers within one set share that set's share of the stack, exactly as the weapons on his
                // belt do -- a man looses one arrow at a time, so an archer issued two quivers must not out-shoot
                // the same archer issued one.
                SimulationWeaponModel.WeaponProfile setShot = default(SimulationWeaponModel.WeaponProfile);
                if (IsRangedTroop(troop))
                {
                    float setShotCount;
                    List<SimulationWeaponModel.WeaponProfile> setShots = CollectShotProfiles(troop, set, rbmCombat, captain, out setShotCount);
                    shotPerMan += setShotCount;
                    if (setShots.Count > 0)
                    {
                        float share = 1f / setShots.Count;
                        foreach (SimulationWeaponModel.WeaponProfile profile in setShots)
                        {
                            ShotOption shotOption = default(ShotOption);
                            shotOption.Profile = profile;
                            shotOption.Weight = share;
                            shots.Add(shotOption);

                            if (profile.Magnitude > setShot.Magnitude)
                            {
                                setShot = profile;
                            }
                        }
                    }
                }

                // And what he hurls, whether shooting is his trade or not: half the infantry in Calradia carry a
                // brace of javelins or throwing axes, and they are for the closing, not for the line.
                float setThrownPerMan;
                SimulationWeaponModel.WeaponProfile setThrown = GetThrownProfile(troop, set, rbmCombat, captain, out setThrownPerMan);

                // The COUNT is averaged over all his sets, because that dilution is real: a man who carries javelins
                // in two sets of four is half a skirmisher, and half his stack throws nothing.
                //
                // The MAGNITUDE must not be. What a javelin is worth is a fact about the javelin, and averaging it
                // over the sets that have none makes every throw weaker the fewer of them he carries -- which is the
                // dilution counted a second time, and it put a recruit's javelin at 2.38.
                thrownPerMan += setThrownPerMan;
                if (setThrown.IsValid && setThrown.Magnitude > 0f)
                {
                    thrownMagnitude += setThrown.Magnitude;
                    thrownSets++;
                    if (setThrown.Magnitude > bestThrownMagnitude)
                    {
                        bestThrownMagnitude = setThrown.Magnitude;
                        bestThrown = setThrown;
                    }
                }

                // And every weapon on his belt, each as likely as the next to be the one in his hand. Every man
                // holds one weapon at a time, so the weapons WITHIN a set share that set's share of the stack --
                // otherwise a soldier issued three blades would out-fight the same soldier issued one.
                List<SimulationWeaponModel.WeaponProfile> setMelee = CollectMeleeProfiles(troop, set, rbmCombat, captain);
                if (setMelee.Count > 0)
                {
                    float share = 1f / setMelee.Count;
                    foreach (SimulationWeaponModel.WeaponProfile profile in setMelee)
                    {
                        MeleeOption option = default(MeleeOption);
                        option.Profile = profile;
                        option.Weight = share;
                        option.IsPolearm = IsPolearm(profile.WeaponType);
                        melee.Add(option);
                    }
                }

                float setHead, setNeck, setTorso, setShoulder, setArm, setLeg;
                GetArmorZones(set, rbmCombat, out setHead, out setNeck, out setTorso, out setShoulder, out setArm, out setLeg);
                if (IsPlateArmoured(set))
                {
                    plateSets++;
                }

                // The horse is not merely something he sits on: to the footman hacking upward it IS the target, and
                // its barding and its bulk are what that man's blade finds. So it answers at the leg -- but it is
                // kept APART from his own armour, because a horse can be killed, and a dead one answers nothing.
                float setHorseLeg = 0f, setHorseBody = 0f;
                ItemObject horse = set[EquipmentIndex.Horse].Item;
                if (horse != null && horse.HorseComponent != null)
                {
                    setHorseLeg += horse.HorseComponent.HitPointBonus * 0.05f;
                    charge += horse.HorseComponent.ChargeDamage;
                    // What the animal itself can take before it falls -- the mount's own Monster health (200 for a
                    // horse, less for a mule) plus this item's extra_health. This is the pool a horse hit wears, and
                    // it is the whole of what makes one mount tougher than another; averaged over his sets below like
                    // everything else, so a troop mounted in only some of his sets is only sometimes horsed.
                    horseHealth += horse.HorseComponent.HitPoints + horse.HorseComponent.HitPointBonus;
                }
                ItemObject harness = set[EquipmentIndex.HorseHarness].Item;
                if (harness != null && harness.ArmorComponent != null)
                {
                    setHorseLeg += harness.ArmorComponent.BodyArmor * 0.5f;
                    setHorseBody += harness.ArmorComponent.BodyArmor * 0.2f;
                }

                head += setHead;
                neck += setNeck;
                torso += setTorso;
                shoulder += setShoulder;
                arm += setArm;
                leg += setLeg;
                horseLeg += setHorseLeg;
                horseBody += setHorseBody;
                shotMagnitude += setShot.Magnitude;
                shieldQuality += GetShieldQuality(set);
                shieldHitPoints += GetShieldHitPoints(set);

                if (setShot.Magnitude > bestShotMagnitude)
                {
                    // The kind of a shot, the weapon it comes from and how well it finds a gap are not quantities
                    // and cannot be averaged. They are taken from the hardest shot he can loose.
                    bestShotMagnitude = setShot.Magnitude;
                    bestShot = setShot;
                }
                sets++;
            }

            TroopKit kit = default(TroopKit);
            if (sets > 0)
            {
                kit.Head = head / sets;
                kit.Neck = neck / sets;
                kit.Torso = torso / sets;
                kit.Shoulder = shoulder / sets;
                kit.Arm = arm / sets;
                kit.Leg = leg / sets;
                kit.HorseLeg = horseLeg / sets;
                kit.HorseBody = horseBody / sets;
                kit.HorseHealth = horseHealth / sets;
                kit.ChargeDamage = charge / sets;
                kit.IsPlate = (plateSets * 2) > sets;

                // The label -- the heaviest shaft he owns, and the test of whether he is a bowman at all. What he is
                // actually PRICED on is the pool below, where each arrow meets armour on its own terms.
                kit.Shot = bestShot;
                kit.Shot.Magnitude = shotMagnitude / sets;
                kit.Shots = shots.ToArray();

                // What a javelin is worth: averaged over the sets that actually HAVE one. How many he has: averaged
                // over all of them, so a man who carries javelins in half his sets is half a skirmisher.
                kit.Thrown = bestThrown;
                if (thrownSets > 0)
                {
                    kit.Thrown.Magnitude = thrownMagnitude / thrownSets;
                }
                kit.ThrownPerMan = thrownPerMan / sets;

                // And the quiver, averaged over his sets the same way the javelin count is: a man who carries arrows
                // in one set of two shoots for half a battle. This is what his stack's ammunition pool is seeded from.
                kit.ShotPerMan = shotPerMan / sets;

                // The weights were shares of their own set; divide by the number of sets and they become shares of
                // the whole stack, summing to one across everything he might have in his hand.
                for (int i = 0; i < melee.Count; i++)
                {
                    MeleeOption option = melee[i];
                    option.Weight /= sets;
                    melee[i] = option;
                    kit.HasPolearm |= option.IsPolearm;
                }
                kit.Melee = melee.ToArray();

                // Averaged over his battle sets like everything else: a troop who carries a shield in only half
                // of them is half a shield-bearer, which is exactly what he is on the field.
                kit.ShieldQuality = shieldQuality / sets;
                kit.ShieldHitPoints = shieldHitPoints / sets;
                kit.IsMounted = troop.IsMounted;
                kit.IsRanged = IsRangedTroop(troop);

                // His fighting hand, for the defence roll: the best of his melee trainings. A troop template's skills
                // are fixed and a hero's do not move mid-battle, so this rides in the cache with the rest of the kit.
                kit.MeleeSkill = MeleeSkillOf(troop, captain);

                // And his shooting hand, for the miss roll. Taken off the shot profile's OWN skill object rather than
                // by asking which launcher he seems to carry: the profile already resolved that (it is the launcher's
                // RelevantSkill, since no one is trained in arrows), so a crossbowman is read on Crossbow and a bowman
                // on Bow with nothing inferred. Rides in the cache with the rest -- a template's skills do not move.
                kit.RangedSkill = (kit.Shot.IsValid && kit.Shot.Skill != null)
                    ? SimulationPerks.SkillOf(troop, kit.Shot.Skill, captain)
                    : 0f;

                // A man is worth pricing if he can hit anything at all -- with a bow, or with what is on his belt.
                kit.IsValid = (kit.Shot.IsValid && kit.Shot.Magnitude > 0f) || kit.Melee.Length > 0;
            }

            _kitCache[key] = kit;
            return kit;
        }

        /// <summary>
        /// Forget every hero's kit, at the opening of a battle. A lord buys armour and trains between one fight and
        /// the next, so what was measured last month is not what he rides out in today -- but nothing he owns
        /// changes while the battle is being fought, so within one battle the cache is exact.
        /// </summary>
        internal static void ForgetHeroKits()
        {
            // Both caches, independently. The sling flag can be set by CarriesSling (through ArmOf/GetBucket) for a
            // hero the kit path never built -- an arm classification before any damage was priced -- so scanning only
            // _kitCache would leave that hero's stale sling flag behind. Evict heroes from each cache on its own keys.
            //
            // The kit cache is keyed by (troop, captain signature) and so cannot share the sling cache's eviction:
            // one hero may sit in it several times over, once under each captain who has led him. Every one of those
            // entries is his and every one of them goes.
            EvictHeroKits();
            EvictHeroes(_slingCache);
        }

        /// <summary>
        /// A fresh session (new game or a loaded save). The kit and sling caches are keyed by CharacterObject; a
        /// hero's instance belongs to the campaign being torn down and would sit here orphaned for the life of the
        /// process. Cleared wholesale -- line-troop templates simply rebuild on next use. Baselines are float means,
        /// hold no object refs and self-rebuild on config change, so they are left alone. Called from OnSessionLaunched.
        /// </summary>
        internal static void ResetForNewSession()
        {
            _kitCache.Clear();
            _slingCache.Clear();
        }

        /// <summary>
        /// Drop every kit belonging to a hero, under every captain he has ever fought beneath. See ForgetHeroKits
        /// for why heroes go and line troops stay; the only difference here is that one hero can hold several
        /// entries, since the cache is keyed by the captain over him as well as by him.
        /// </summary>
        private static void EvictHeroKits()
        {
            List<KitKey> heroes = null;
            foreach (KeyValuePair<KitKey, TroopKit> entry in _kitCache)
            {
                if (entry.Key.Troop != null && entry.Key.Troop.IsHero)
                {
                    if (heroes == null)
                    {
                        heroes = new List<KitKey>();
                    }
                    heroes.Add(entry.Key);
                }
            }
            if (heroes != null)
            {
                foreach (KitKey key in heroes)
                {
                    _kitCache.Remove(key);
                }
            }
        }

        /// <summary>Drop every hero-keyed entry from a per-troop cache, on that cache's own keys. See ForgetHeroKits.</summary>
        private static void EvictHeroes<TValue>(Dictionary<CharacterObject, TValue> cache)
        {
            List<CharacterObject> heroes = null;
            foreach (KeyValuePair<CharacterObject, TValue> entry in cache)
            {
                if (entry.Key != null && entry.Key.IsHero)
                {
                    if (heroes == null)
                    {
                        heroes = new List<CharacterObject>();
                    }
                    heroes.Add(entry.Key);
                }
            }
            if (heroes != null)
            {
                foreach (CharacterObject hero in heroes)
                {
                    cache.Remove(hero);
                }
            }
        }

        /// <summary>
        /// A troop template usually lists several battle sets and each man rolls one at random, so callers
        /// average over all of them. CharacterObject.Equipment is only a last resort: it is the default set,
        /// which may well be civilian dress rather than anything he would fight in.
        /// </summary>
        private static IEnumerable<Equipment> EnumerateBattleEquipments(CharacterObject troop)
        {
            bool any = false;
            if (troop.BattleEquipments != null)
            {
                foreach (Equipment equipment in troop.BattleEquipments)
                {
                    if (equipment != null)
                    {
                        any = true;
                        yield return equipment;
                    }
                }
            }
            if (!any)
            {
                Equipment fallback = troop.FirstBattleEquipment ?? troop.Equipment;
                if (fallback != null)
                {
                    yield return fallback;
                }
            }
        }

        /// <summary>The real armour points this kit carries, kept apart by the zone each protects.
        /// (internal rather than private only so StrategicTroopPower can read the same zones; nothing else.)</summary>
        internal static void GetArmorZones(Equipment set, bool rbmCombat,
            out float head, out float neck, out float torso, out float shoulder, out float arm, out float leg)
        {
            // When RBM Combat is live it does not read a piece's rating straight off: a blow lands on a BONE, and
            // RBM composes the armour over that bone from several pieces at once (ArmorRework.GetBaseArmor-
            // EffectivenessForBodyPartRBMHuman). The simulated blow must meet the same armour the live one would,
            // or it prices men into kit RBM would never actually let them fight in.
            if (rbmCombat)
            {
                GetArmorZonesRbm(set, out head, out neck, out torso, out shoulder, out arm, out leg);
                return;
            }

            // Vanilla combat: the raw component ratings, as native armour reads them, each zone taking the stat
            // nearest to it. Native has no bone composition of its own worth mirroring, so this is deliberately
            // plain -- the helm answers the head and neck, the body armour the torso and shoulder, and so on.
            head = 0f;
            float body = 0f;
            arm = 0f;
            leg = 0f;
            for (EquipmentIndex i = EquipmentIndex.NumAllWeaponSlots; i < EquipmentIndex.ArmorItemEndSlot; i++)
            {
                ItemObject item = set[i].Item;
                if (item == null || item.ArmorComponent == null)
                {
                    continue;
                }
                ArmorComponent ac = item.ArmorComponent;
                head += ac.HeadArmor;
                body += ac.BodyArmor;
                arm += ac.ArmArmor;
                leg += ac.LegArmor;
            }
            neck = head;
            torso = body;
            shoulder = body;
        }

        /// <summary>
        /// The armour over each zone as RBM Combat itself composes it, bone by bone. Mirrors RBMCombat's ArmorRework
        /// getters (getHeadArmor / getNeckArmor / getChestArmor / getShoulderArmor / getArmArmor / getLegArmor)
        /// exactly, modifiers and all:
        ///   - Head     : the helmet's head rating.
        ///   - Neck     : the helmet's arm rating plus the body armour's -- an aventail and a collar, both soft.
        ///   - Torso    : the body armour's body rating (RBM's chest and abdomen carry the same value; merged here).
        ///   - Shoulder : a cape's body and arm ratings, plus the body armour's arm rating -- a plated shoulder.
        ///   - Arm      : the gloves' arm rating ALONE. The raw sum instead heaped every arm point a breastplate
        ///                owns onto the bare forearm, arming men far past what RBM would allow in a fight.
        ///   - Leg      : HALF the leg item's leg rating and HALF the body armour's -- a cuirass skirt covers the
        ///                thigh, but only the thigh, so its full leg rating never reaches the shin.
        /// The horse is handled apart from this, in the caller, exactly as before.
        /// </summary>
        private static void GetArmorZonesRbm(Equipment set,
            out float head, out float neck, out float torso, out float shoulder, out float arm, out float leg)
        {
            head = 0f;
            float headArm = 0f;  // the helmet's arm rating -- an aventail, which answers the neck
            float bodyArm = 0f;  // the body armour's arm rating -- answers the neck AND the shoulder
            float bodyOn = 0f;   // the body armour's body rating -- the torso proper
            float capeOn = 0f;   // a cape's body and arm ratings -- answers the shoulder
            float gloves = 0f;   // the gloves' arm rating -- the arm proper, and nothing else
            leg = 0f;

            for (EquipmentIndex i = EquipmentIndex.NumAllWeaponSlots; i < EquipmentIndex.ArmorItemEndSlot; i++)
            {
                EquipmentElement e = set[i];
                ItemObject item = e.Item;
                if (item == null || item.ArmorComponent == null)
                {
                    continue;
                }
                switch (item.ItemType)
                {
                    case ItemObject.ItemTypeEnum.HeadArmor:
                        head += e.GetModifiedHeadArmor();
                        headArm += e.GetModifiedArmArmor();
                        break;

                    case ItemObject.ItemTypeEnum.BodyArmor:
                        bodyOn += e.GetModifiedBodyArmor();
                        bodyArm += e.GetModifiedArmArmor();
                        leg += e.GetModifiedLegArmor() * 0.5f;
                        break;

                    case ItemObject.ItemTypeEnum.LegArmor:
                        leg += e.GetModifiedLegArmor() * 0.5f;
                        break;

                    case ItemObject.ItemTypeEnum.HandArmor:
                        gloves += e.GetModifiedArmArmor();
                        break;

                    case ItemObject.ItemTypeEnum.Cape:
                        capeOn += e.GetModifiedBodyArmor();
                        capeOn += e.GetModifiedArmArmor();
                        break;
                }
            }

            neck = headArm + bodyArm;
            torso = bodyOn;
            shoulder = capeOn + bodyArm;
            arm = gloves;
        }

        /// <summary>
        /// Which way the blows fall in this particular matchup. The live combat path reads armour at the exact
        /// body part struck; a simulated blow has no body part, so it is given the one it would most likely have
        /// found, and that depends entirely on who is swinging at whom -- and, for a footman meeting a horse, on
        /// whether he has a spear in his hands (see the note on FootVsMounted).
        /// </summary>
        private static HitZones GetHitZones(bool strikerMounted, bool strikerRanged, bool struckMounted,
            bool strikerHasPolearm)
        {
            if (strikerRanged)
            {
                return struckMounted ? MissileVsMounted : MissileVsFoot;
            }
            if (strikerMounted)
            {
                return struckMounted ? MountedVsMounted : MountedVsFoot;
            }
            if (!struckMounted)
            {
                return FootVsFoot;
            }

            // Foot at a rider. The spear reaches him; anything shorter finds the legs and the horse.
            return strikerHasPolearm ? FootVsFoot : FootVsMounted;
        }

        /// <summary>
        /// What a blow is WORTH where it lands, which is not the same question as what armour it meets there.
        /// Straight out of RBM's own DamageRework.GetBodyPartDamageMultiplier -- a head or a neck is worth half
        /// again, an arm or a leg between half and seven-tenths. A head hit is three times a leg hit, and the model
        /// had every blow in Calradia worth exactly the same wherever it landed.
        ///
        /// Every value here is RBM's own, bone for bone, save one fold: chest (0.9) and abdomen (1.0) are merged
        /// into Torso at 0.95, because they carry the same armour and differ only here.
        /// </summary>
        private static float BodyPartMultiplier(int zone, DamageTypes damageType)
        {
            bool ordinary = damageType == DamageTypes.Pierce
                || damageType == DamageTypes.Cut
                || damageType == DamageTypes.Blunt;

            switch (zone)
            {
                case ZoneHead:
                case ZoneNeck:
                    return ordinary ? 1.5f : 1f;

                case ZoneTorso:
                    return ordinary ? 0.95f : 1f;

                case ZoneShoulder:
                    if (damageType == DamageTypes.Pierce) { return 0.6f; }
                    if (damageType == DamageTypes.Cut) { return 0.6f; }
                    if (damageType == DamageTypes.Blunt) { return 0.7f; }
                    return 1f;

                case ZoneArm:
                    if (damageType == DamageTypes.Pierce) { return 0.5f; }
                    if (damageType == DamageTypes.Cut) { return 0.6f; }
                    if (damageType == DamageTypes.Blunt) { return 0.7f; }
                    return 1f;

                case ZoneLeg:
                    if (damageType == DamageTypes.Pierce) { return 0.5f; }
                    if (damageType == DamageTypes.Cut) { return 0.6f; }
                    if (damageType == DamageTypes.Blunt) { return 0.7f; }
                    return 1f;

                default:
                    return 1f;
            }
        }

        internal static string ZoneName(int zone)
        {
            switch (zone)
            {
                case ZoneHead: return "head";
                case ZoneNeck: return "neck";
                case ZoneTorso: return "torso";
                case ZoneShoulder: return "shldr";
                case ZoneArm: return "arm";
                case ZoneLeg: return "leg";
                default: return "-";
            }
        }

        /// <summary>
        /// The armour standing over one zone of THE MAN. The barding is no longer folded in here: a blow at the
        /// rider meets the rider's own armour, because a blow that would have met the horse's barding is a blow that
        /// hit the horse instead (see the HorseHitChance roll in Explain and <see cref="HorseArmor"/>), and that is
        /// resolved apart from this. The horsesAlive argument is thus vestigial on this path and kept only so the
        /// callers need not change; it still governs whether the man is mounted at all, upstream.
        /// </summary>
        private static float ZoneArmor(TroopKit struck, int zone, float horsesAlive)
        {
            switch (zone)
            {
                case ZoneHead: return struck.Head;
                case ZoneNeck: return struck.Neck;
                case ZoneTorso: return struck.Torso;
                case ZoneShoulder: return struck.Shoulder;
                case ZoneArm: return struck.Arm;
                case ZoneLeg: return struck.Leg;
                default: return 0f;
            }
        }

        /// <summary>
        /// The armour a blow meets when it finds the HORSE rather than the man -- the harness over its flank and the
        /// beast's own bulk, which is all it has to answer a blade (the two barding components composed at
        /// <see cref="GetArmorZonesRbm"/>'s caller, now the horse's alone). A dead horse is never the thing struck --
        /// the roll is gated on struckStillMounted -- so this is never asked of a mount that is not there.
        /// </summary>
        private static float HorseArmor(TroopKit struck)
        {
            return struck.HorseBody + struck.HorseLeg;
        }

        private static float ZoneShare(HitZones zones, int zone)
        {
            switch (zone)
            {
                case ZoneHead: return zones.Head;
                case ZoneNeck: return zones.Neck;
                case ZoneTorso: return zones.Torso;
                case ZoneShoulder: return zones.Shoulder;
                case ZoneArm: return zones.Arm;
                case ZoneLeg: return zones.Leg;
                default: return 0f;
            }
        }

        /// <summary>
        /// Which part of him the blow found. Drawn from the distribution for this matchup -- so a footman hacking
        /// upward at a rider really does find the legs most of the time, and a rider cutting downward really does
        /// find the head.
        /// </summary>
        private static int RollZone(HitZones zones)
        {
            float roll = MBRandom.RandomFloat;
            if ((roll -= zones.Head) < 0f) { return ZoneHead; }
            if ((roll -= zones.Neck) < 0f) { return ZoneNeck; }
            if ((roll -= zones.Torso) < 0f) { return ZoneTorso; }
            if ((roll -= zones.Shoulder) < 0f) { return ZoneShoulder; }
            if ((roll -= zones.Arm) < 0f) { return ZoneArm; }
            return ZoneLeg;
        }

        /// <summary>The armour a blow actually meets: this man's zones, weighted by where blows of this kind land.</summary>
        private static float WeightedArmor(float head, float neck, float torso, float shoulder, float arm, float leg, HitZones zones)
        {
            return (head * zones.Head) + (neck * zones.Neck) + (torso * zones.Torso)
                + (shoulder * zones.Shoulder) + (arm * zones.Arm) + (leg * zones.Leg);
        }

        /// <summary>
        /// The armour this blow meets -- with the horse counted only for as long as the horse is alive. A rider
        /// whose mount has been killed under him is a man on foot: the barding that was catching every blow is
        /// gone, and the blows stop coming at his legs and start coming at his head.
        /// </summary>
        private static float WeightedArmor(TroopKit struck, TroopKit striker, float horsesAlive, bool struckStillMounted, bool shooting)
        {
            HitZones zones = GetHitZones(striker.IsMounted, shooting, struckStillMounted, striker.HasPolearm);
            // The man's own armour, barding excluded -- a blow at the horse is met by the horse (see HorseArmor).
            return WeightedArmor(struck.Head, struck.Neck, struck.Torso, struck.Shoulder, struck.Arm, struck.Leg, zones);
        }

        private static bool IsPolearm(string weaponType)
        {
            return weaponType == "OneHandedPolearm"
                || weaponType == "TwoHandedPolearm"
                || weaponType == "LowGripPolearm";
        }

        /// <summary>What the blow is, once we know where it landed: the shot, the throw, or what he drew from his belt.</summary>
        private static float PhaseDamage(TroopKit striker, TroopKit struck, float armor, bool rbmCombat,
            bool struckStillMounted, bool shooting, bool throwing, int zone,
            out SimulationWeaponModel.WeaponProfile drawn, out bool braced)
        {
            drawn = default(SimulationWeaponModel.WeaponProfile);
            braced = false;

            if (shooting)
            {
                drawn = striker.Shot;
                return ShotDamage(striker, armor, struck.IsPlate, rbmCombat, zone);
            }
            if (throwing)
            {
                drawn = striker.Thrown;
                return ExpectedDamage(striker.Thrown, armor, struck.IsPlate, rbmCombat, zone);
            }
            return MeleeDamage(striker, armor, struck.IsPlate, rbmCombat, struckStillMounted, zone, out drawn, out braced);
        }

        /// <summary>
        /// One blow, landing somewhere on him.
        ///
        /// A blow does not meet an average of a man. It meets his HEAD, or his leg, and those are different armour
        /// and different consequences -- RBM pays a head half again and a leg about half. So a real blow ROLLS a
        /// body part from the distribution for this matchup and meets that one: a footman hacking upward at a rider
        /// really does find the legs most of the time, and the rider cutting downward really does find the head.
        ///
        /// The reference tables (and every baseline) instead want the EXPECTATION, so they get the average over all
        /// four zones -- each zone's own armour, each zone's own multiplier, averaged AFTER. Which is the same rule
        /// as everywhere else in this model: a mace and a sabre must meet the armour separately, and so must a head
        /// and a shin. Averaging the armour first and applying one multiplier to the result gives a blow that landed
        /// nowhere, on a man who is the mean of himself.
        /// </summary>
        private static float Blow(TroopKit striker, TroopKit struck, bool rbmCombat, HitZones zones,
            float horsesAlive, bool struckStillMounted, bool shooting, bool throwing, bool roll,
            out int zoneHit, out float armorMet, out SimulationWeaponModel.WeaponProfile drawn, out bool braced)
        {
            if (roll)
            {
                zoneHit = RollZone(zones);
                armorMet = ZoneArmor(struck, zoneHit, horsesAlive);
                return PhaseDamage(striker, struck, armorMet, rbmCombat, struckStillMounted, shooting, throwing,
                    zoneHit, out drawn, out braced);
            }

            zoneHit = -1;
            armorMet = 0f;
            drawn = default(SimulationWeaponModel.WeaponProfile);
            braced = false;

            float damage = 0f;
            for (int z = 0; z < ZoneCount; z++)
            {
                float share = ZoneShare(zones, z);
                if (share <= 0f)
                {
                    continue;
                }
                float zoneArmor = ZoneArmor(struck, z, horsesAlive);
                armorMet += share * zoneArmor;

                SimulationWeaponModel.WeaponProfile zoneDrawn;
                bool zoneBraced;
                damage += share * PhaseDamage(striker, struck, zoneArmor, rbmCombat, struckStillMounted,
                    shooting, throwing, z, out zoneDrawn, out zoneBraced);

                // The weapon he drew and whether he braced are facts about the man and his enemy, not about which
                // shin the blow found -- so any zone answers them.
                drawn = zoneDrawn;
                braced = zoneBraced;
            }
            return damage;
        }

        /// <summary>
        /// What this man's shot achieves against that armour -- averaged over the arrows he actually carries,
        /// because he does not carry one kind. Each shaft meets the armour on its own terms and the mean is taken
        /// afterwards, which is the only order that can be right when a bodkin halves armour and a broadhead does
        /// not.
        /// </summary>
        private static float ShotDamage(TroopKit kit, float armor, bool victimIsPlate, bool rbmCombat, int zone)
        {
            if (kit.Shots == null || kit.Shots.Length == 0)
            {
                return ExpectedDamage(kit.Shot, armor, victimIsPlate, rbmCombat, zone);
            }

            float damage = 0f;
            float weight = 0f;
            for (int i = 0; i < kit.Shots.Length; i++)
            {
                ShotOption option = kit.Shots[i];
                damage += ExpectedDamage(option.Profile, armor, victimIsPlate, rbmCombat, zone) * option.Weight;
                weight += option.Weight;
            }

            return (weight > 0f) ? (damage / weight) : 0f;
        }

        /// <summary>
        /// What this man's melee blow achieves against that armour -- averaged over the weapons he actually carries,
        /// because he does not carry one.
        ///
        /// Which of them is in his hand depends on what is coming at him. Against another man on foot it is whatever
        /// he happened to draw, so every weapon on his belt gets its turn and the blow is the mean of all of them.
        /// Against a horse he reaches for the spear, every time, and so does everyone who has one: a man with a
        /// polearm and an axe does not meet a charge with the axe. So the pool narrows to his polearms, and the
        /// average is taken over those alone.
        ///
        /// <paramref name="braced"/> comes back true when the pool that was used is the polearm pool -- which is
        /// exactly the condition for setting a spear against a horse, so the caller need not test the weapon twice.
        /// </summary>
        private static float MeleeDamage(TroopKit kit, float armor, bool victimIsPlate, bool rbmCombat,
            bool targetMounted, int zone, out SimulationWeaponModel.WeaponProfile shown, out bool braced)
        {
            shown = default(SimulationWeaponModel.WeaponProfile);
            braced = false;

            if (kit.Melee == null || kit.Melee.Length == 0)
            {
                return 0f;
            }

            // Against a horse, the spears come out -- if he has any. A man with none swings what he has and hopes.
            bool preferPolearms = targetMounted && kit.HasPolearm;

            float damage = 0f;
            float weight = 0f;
            float heaviest = 0f;
            for (int i = 0; i < kit.Melee.Length; i++)
            {
                MeleeOption option = kit.Melee[i];
                if (preferPolearms && !option.IsPolearm)
                {
                    continue;
                }
                damage += ExpectedDamage(option.Profile, armor, victimIsPlate, rbmCombat, zone) * option.Weight;
                weight += option.Weight;

                // The weapon shown in the log is the heaviest of the pool actually drawn from -- a label for the
                // pool, not the blow. The blow is the average, and no single weapon is it.
                if (option.Profile.Magnitude > heaviest)
                {
                    heaviest = option.Profile.Magnitude;
                    shown = option.Profile;
                }
            }

            if (weight <= 0f)
            {
                return 0f;
            }

            braced = preferPolearms;

            // The weights are shares of his WHOLE arsenal, so narrowing to the spears leaves them summing to less
            // than one. Renormalise, or a man who carries a spear in one set of four would look like he barely
            // fought at all.
            return damage / weight;
        }

        /// <summary>
        /// Every shot this set can loose: each quiver on his back, fired from the best launcher he has for it.
        ///
        /// Not the biggest number. A Battanian Fian carries bodkins AND broadheads, and under either combat model
        /// those two answer armour by different rules -- under RBM a Pierce bodkin halves a hauberk, and even under
        /// native a Pierce shaft meets a 0.33 threshold where a Cut one meets 0.5. So the shaft with the larger
        /// printed damage is very often the WORSE of the two against a man in mail, and picking by that number is
        /// the same mistake that once armed a peasant with his sling and a Sea Raider Chief with a thrown axe.
        ///
        /// Each arrow is priced on its own terms and the average is taken afterwards, in ShotDamage.
        /// </summary>
        private static List<SimulationWeaponModel.WeaponProfile> CollectShotProfiles(CharacterObject troop, Equipment set,
            bool rbmCombat, CharacterObject captain, out float shotCount)
        {
            // The COUNT of shafts he carries in this set, summed across his quivers -- a man with two quivers of
            // thirty carries sixty and shoots twice as long as the same man with one, which is exactly how the
            // throwing count sums his javelins (see GetThrownProfile). A quiver counts only if he has a launcher that
            // can loose it: arrows he cannot fire are not ammunition. Read off the ammo item's own stack size.
            shotCount = 0f;
            List<SimulationWeaponModel.WeaponProfile> profiles = new List<SimulationWeaponModel.WeaponProfile>();

            for (EquipmentIndex a = EquipmentIndex.WeaponItemBeginSlot; a < EquipmentIndex.NumAllWeaponSlots; a++)
            {
                ItemObject ammoItem = set[a].Item;
                if (ammoItem == null || ammoItem.WeaponComponent == null || !IsAmmo(ammoItem))
                {
                    continue;
                }
                WeaponComponentData ammo = ammoItem.WeaponComponent.PrimaryWeapon;
                if (ammo == null)
                {
                    continue;
                }

                // The best launcher he carries that can throw THIS shaft. A man with a bow and a crossbow shoots
                // his arrows from the bow and his bolts from the crossbow, and the profile that comes out hardest
                // is the pairing he would actually use.
                SimulationWeaponModel.WeaponProfile bestForThisAmmo = default(SimulationWeaponModel.WeaponProfile);

                for (EquipmentIndex l = EquipmentIndex.WeaponItemBeginSlot; l < EquipmentIndex.NumAllWeaponSlots; l++)
                {
                    ItemObject launcherItem = set[l].Item;
                    if (launcherItem == null || launcherItem.WeaponComponent == null || !IsLauncher(launcherItem))
                    {
                        continue;
                    }
                    WeaponComponentData launcher = launcherItem.WeaponComponent.PrimaryWeapon;
                    if (launcher == null || launcher.AmmoClass != ammo.WeaponClass)
                    {
                        continue;
                    }

                    int skill = SimulationPerks.SkillOf(troop, launcher.RelevantSkill, captain);

                    // The shot follows the combat model, exactly as the blow does. This branch used to run RBM's
                    // bow physics unconditionally, so with RBM Combat OFF every archer in Calradia was priced on
                    // draw weight and powerstroke while the infantryman beside him was priced on the number printed
                    // on his sword -- two different units, and no baseline can rescue that.
                    SimulationWeaponModel.WeaponProfile shot;
                    bool got = rbmCombat
                        ? SimulationWeaponModel.GetMissileProfile(launcherItem, launcher, ammoItem, ammo, skill, out shot)
                        : GetVanillaMissileProfile(launcher, ammo, skill, out shot);

                    if (got && shot.Magnitude > bestForThisAmmo.Magnitude)
                    {
                        bestForThisAmmo = shot;
                    }
                }

                if (bestForThisAmmo.IsValid && bestForThisAmmo.Magnitude > 0f)
                {
                    profiles.Add(bestForThisAmmo);
                    shotCount += MathF.Max((float)ammo.MaxDataValue, 1f);
                }
            }

            return profiles;
        }

        /// <summary>
        /// The javelins on his back, and how many of them. The heaviest bundle he carries in this set, since a man
        /// with both darts and a throwing spear throws the spear at the thing worth throwing a spear at.
        ///
        /// The count comes from the item's own stack size -- two javelins, three throwing axes -- and it is the
        /// whole reason the throwing phase ends: he runs out. That is exactly how it should feel. A skirmisher is
        /// terrifying for twenty seconds and then he is a man with a knife.
        /// </summary>
        private static SimulationWeaponModel.WeaponProfile GetThrownProfile(CharacterObject troop, Equipment set,
            bool rbmCombat, CharacterObject captain, out float perMan)
        {
            SimulationWeaponModel.WeaponProfile best = default(SimulationWeaponModel.WeaponProfile);
            perMan = 0f;

            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
            {
                ItemObject item = set[i].Item;
                if (item == null || item.WeaponComponent == null || !IsThrown(item))
                {
                    continue;
                }
                WeaponComponentData weapon = item.WeaponComponent.PrimaryWeapon;
                if (weapon == null)
                {
                    continue;
                }

                int skill = SimulationPerks.SkillOf(troop, weapon.RelevantSkill, captain);

                SimulationWeaponModel.WeaponProfile profile;
                bool got = rbmCombat
                    ? SimulationWeaponModel.GetThrownProfile(item, weapon, skill, out profile)
                    : GetVanillaThrownProfile(item, weapon, skill, out profile);

                if (got && profile.Magnitude > best.Magnitude)
                {
                    best = profile;
                    perMan = MathF.Max((float)weapon.MaxDataValue, 1f);
                }
            }

            return best;
        }

        /// <summary>With RBM Combat off, a throw is worth the number printed on it, like everything else in vanilla.</summary>
        private static bool GetVanillaThrownProfile(ItemObject item, WeaponComponentData weapon, int skill,
            out SimulationWeaponModel.WeaponProfile profile)
        {
            profile = default(SimulationWeaponModel.WeaponProfile);
            if (weapon.ThrustDamage <= 0f)
            {
                return false;
            }

            DamageTypes damageType = weapon.ThrustDamageType;
            if (damageType == DamageTypes.Invalid)
            {
                damageType = weapon.SwingDamageType;
            }

            profile.Magnitude = weapon.ThrustDamage * VanillaSkillFactor(skill);
            profile.DamageType = damageType;
            profile.WeaponType = weapon.WeaponClass.ToString();
            profile.DamageFactor = 1f;
            profile.Skill = weapon.RelevantSkill;
            profile.IsMissile = true;
            profile.IsValid = true;
            return true;
        }

        /// <summary>
        /// Every melee weapon in this set -- not the best of them. A soldier carries a spear and an axe and swings
        /// whichever is in his hand, so all of them are collected and the blow is averaged over them afterwards.
        ///
        /// A launcher and its ammunition are set aside: whatever the number on a sling says, a man in the line is
        /// not hitting anybody with it. A shield is not how he fights either. Nor are the three throwing axes on
        /// his belt -- he looses those at the closing and then draws steel, and steel is what he fights the battle
        /// with. A thrown weapon is its own launcher, so it slipped the launcher filter, and a Sea Raider Chief
        /// spent a whole campaign log hurling the same axe over and over: it read as the biggest number in his kit
        /// AND carried a thrust type of Invalid, since a throwing axe is never thrust with, which fed an unset
        /// damage type straight into the armour equation.
        /// </summary>
        private static List<SimulationWeaponModel.WeaponProfile> CollectMeleeProfiles(CharacterObject troop, Equipment set,
            bool rbmCombat, CharacterObject captain)
        {
            List<SimulationWeaponModel.WeaponProfile> profiles = new List<SimulationWeaponModel.WeaponProfile>();

            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
            {
                ItemObject item = set[i].Item;
                if (item == null || item.WeaponComponent == null)
                {
                    continue;
                }
                if (IsAmmo(item) || IsLauncher(item) || IsShield(item) || IsThrown(item))
                {
                    continue;
                }
                WeaponComponentData weapon = item.WeaponComponent.PrimaryWeapon;
                if (weapon == null)
                {
                    continue;
                }

                int skill = SimulationPerks.SkillOf(troop, weapon.RelevantSkill, captain);

                SimulationWeaponModel.WeaponProfile profile;
                bool got = rbmCombat
                    ? SimulationWeaponModel.GetMeleeProfile(item, weapon, skill, out profile)
                    : GetVanillaMeleeProfile(item, weapon, skill, out profile);

                if (got && profile.IsValid && profile.Magnitude > 0f)
                {
                    profiles.Add(profile);
                }
            }

            return profiles;
        }

        /// <summary>
        /// Vanilla prices a blow on the number printed on the weapon, so with RBM Combat off that number is what is
        /// read -- lifted by the modest share of a soldier's training that native lets reach damage. See
        /// VanillaSkillFactor: leaving skill out entirely is not the conservative choice, because this model has
        /// already divided vanilla's tier term away and tier was the only thing carrying it.
        /// </summary>
        private static bool GetVanillaMeleeProfile(ItemObject item, WeaponComponentData weapon, int skill,
            out SimulationWeaponModel.WeaponProfile profile)
        {
            profile = default(SimulationWeaponModel.WeaponProfile);
            float magnitude = weapon.SwingDamage;
            DamageTypes type = weapon.SwingDamageType;
            if (weapon.ThrustDamage > magnitude)
            {
                magnitude = weapon.ThrustDamage;
                type = weapon.ThrustDamageType;
            }
            if (magnitude <= 0f)
            {
                return false;
            }
            profile.Magnitude = magnitude * VanillaSkillFactor(skill);
            profile.DamageType = type;
            profile.WeaponType = weapon.WeaponClass.ToString();
            profile.DamageFactor = 1f;
            profile.Skill = weapon.RelevantSkill;
            profile.IsMissile = false;
            profile.IsValid = true;
            return true;
        }

        /// <summary>
        /// A shot, with RBM Combat off. THIS DID NOT EXIST, and its absence was a real hole: the ranged path
        /// called RBM's missile physics unconditionally, so with RBM Combat disabled every archer
        /// in Calradia was still being priced on draw weight, powerstroke and material efficiency while every
        /// infantryman beside him was priced on the number printed on his sword. Those are not the same scale, and
        /// the baseline cannot rescue a striker who is measured in different units from the man he is measured
        /// against.
        ///
        /// Vanilla's answer is the arrowhead: the shaft's own listed damage. The bow reaches damage through missile
        /// SPEED, so a heavier bow is worth more -- taken linearly against a hundred, which is the speed an ordinary
        /// bow throws at, and clamped so that neither a toy nor a siege crossbow runs away with it.
        /// </summary>
        private static bool GetVanillaMissileProfile(WeaponComponentData launcher, WeaponComponentData ammo,
            int skill, out SimulationWeaponModel.WeaponProfile profile)
        {
            profile = default(SimulationWeaponModel.WeaponProfile);
            if (ammo == null || ammo.ThrustDamage <= 0)
            {
                return false;
            }

            float speedFactor = (launcher != null && launcher.MissileSpeed > 0)
                ? MBMath.ClampFloat(launcher.MissileSpeed / 100f, 0.5f, 2f)
                : 1f;

            profile.Magnitude = ammo.ThrustDamage * speedFactor * VanillaSkillFactor(skill);
            profile.DamageType = ammo.ThrustDamageType;
            profile.WeaponType = ammo.WeaponClass.ToString();
            profile.DamageFactor = 1f;
            profile.Skill = (launcher != null) ? launcher.RelevantSkill : null;
            profile.IsMissile = true;
            profile.IsValid = true;
            return true;
        }

        /// <summary>Whether his body armour is plate -- the only armour RBM does not halve against an arrow.</summary>
        private static bool IsPlateArmoured(Equipment set)
        {
            ItemObject body = set[EquipmentIndex.Body].Item;
            return body != null
                && body.ArmorComponent != null
                && body.ArmorComponent.MaterialType == ArmorComponent.ArmorMaterialTypes.Plate;
        }

        /// <summary>
        /// What the shield on this man's arm is worth, by RBM's own reckoning of shields
        /// (RBMCombat ItemValuesTiers.CalculateShieldTier): a thing of its durability, the armour of its face,
        /// and how much of him it covers. Nought for a man with no shield. The formula is borrowed rather than
        /// invented because the game's shields differ where it counts -- a wooden adarga and a steel round shield
        /// are the same span and the same 60 length, and differ five-fold in hit points.
        /// </summary>
        private static float GetShieldQuality(Equipment set)
        {
            float best = 0f;
            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
            {
                ItemObject item = set[i].Item;
                if (item == null || item.WeaponComponent == null || !IsShield(item))
                {
                    continue;
                }
                WeaponComponentData shield = item.WeaponComponent.PrimaryWeapon;
                if (shield == null)
                {
                    continue;
                }
                float hitPoints = shield.MaxDataValue;
                float bodyArmor = shield.BodyArmor;
                float length = shield.WeaponLength;
                float quality = (((hitPoints - 400f) * 0.005f) + (bodyArmor * 0.2f)) * (length / 60f) + 1f;
                if (quality > best)
                {
                    best = quality;
                }
            }
            return best;
        }

        /// <summary>What his shield can take before it is kindling: the item's own hit points, 400 to 2000 of them.</summary>
        private static float GetShieldHitPoints(Equipment set)
        {
            float best = 0f;
            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
            {
                ItemObject item = set[i].Item;
                if (item == null || item.WeaponComponent == null || !IsShield(item))
                {
                    continue;
                }
                WeaponComponentData shield = item.WeaponComponent.PrimaryWeapon;
                if (shield != null && shield.MaxDataValue > best)
                {
                    best = shield.MaxDataValue;
                }
            }
            return best;
        }

        /// <summary>
        /// The share of blows this man's shield turns aside. Measured against the shield the average shield-bearer
        /// carries, so a better shield than the common sort stops more and a poorer one less, and the config figure
        /// sets the middle of that range rather than the whole of it.
        ///
        /// The quality ratio enters under a square root, and it must. Taken flat, a Pavise -- which is a wall of
        /// wood, and scores accordingly -- sat on the cap at 75% while a Norse round shield turned 21%, so the
        /// shieldwall infantry that shields exist for came off WORSE than a crossbowman hiding behind a board.
        /// A better shield should stop more blows than a poorer one; it should not stop three and a half times as
        /// many. Most of what stops a blow is the man, and men do not differ fourfold.
        /// </summary>
        private static float GetShieldBlock(float shieldQuality, bool againstMissile)
        {
            if (shieldQuality <= 0f || _typicalShieldQuality <= 0f)
            {
                return 0f;
            }
            float ratio = MathF.Sqrt(shieldQuality / _typicalShieldQuality);
            float block = RBMConfig.RBMConfig.simulationShieldBlockChance * ratio;
            if (againstMissile)
            {
                block *= MissileShieldBonus;
            }
            return MBMath.ClampFloat(block, 0f, MaxShieldBlock);
        }

        /// <summary>
        /// A man's fighting hand, as a plain skill LEVEL: the best of his one-handed, two-handed and polearm
        /// training. It is what the defence roll is priced on -- how well he blocks, and, set against his attacker's,
        /// how often his defence becomes a parry. Read through the same captain-aware SkillOf the damage path uses
        /// per weapon, but kept as the level rather than the type (WeaponProfile.Skill is the type and cannot answer
        /// this) -- so a captain whose training reaches his men's blades reaches their guard with it, which is what a
        /// perk that adds a melee skill actually means.
        /// </summary>
        private static float MeleeSkillOf(CharacterObject troop, CharacterObject captain)
        {
            if (troop == null)
            {
                return 0f;
            }
            int oneHanded = SimulationPerks.SkillOf(troop, DefaultSkills.OneHanded, captain);
            int twoHanded = SimulationPerks.SkillOf(troop, DefaultSkills.TwoHanded, captain);
            int polearm = SimulationPerks.SkillOf(troop, DefaultSkills.Polearm, captain);
            int best = oneHanded;
            if (twoHanded > best) { best = twoHanded; }
            if (polearm > best) { best = polearm; }
            return best;
        }

        /// <summary>
        /// The chance a fired shot goes wide of the man it was aimed at. See the miss constants above the landing
        /// exponents for what each term is and why. Deterministic -- it is the CHANCE, not the roll -- so the live
        /// blow rolls against it and the reference tables take it as an expectation, the way they do the shield.
        /// </summary>
        /// <param name="volleyProgress">How far through the volley this shot falls -- 0 at the opening, 1 as the
        /// javelins start. Ignored outside the volley. See SimulationBattleState.VolleyProgress.</param>
        private static float ShotMissChance(TroopKit striker, bool strikerMounted, bool struckMounted, bool volley,
            float volleyProgress)
        {
            float chance = RBMConfig.RBMConfig.simulationRangedMissChance;
            if (chance <= 0f)
            {
                return 0f;
            }

            // What he is shooting, and how much of a lifetime it takes to shoot it straight.
            chance *= ShotAccuracyFactor(striker.Shot.WeaponType);

            // And how much of that lifetime he has actually had. The largest term by far, as it should be.
            chance *= 1f - (RangedMissSkillReduction * SkillFraction(striker.RangedSkill));

            // The long arcing shot into a formation nobody is aiming at in particular -- and how long a shot it is
            // depends on how far into the volley he looses it. The opening exchange is the longest shot of the
            // battle; the last one before the javelins is nearly the flat shot he takes in the skirmish.
            if (volley)
            {
                float t = MBMath.ClampFloat(volleyProgress, 0f, 1f);
                chance *= RangedMissVolleyFactorOpening
                    + ((RangedMissVolleyFactorClosing - RangedMissVolleyFactorOpening) * t);
            }

            // Loosed from a moving horse.
            if (strikerMounted)
            {
                chance *= RangedMissMountedShooterFactor;
            }

            // At a man who is not where he was when it left the string.
            if (struckMounted)
            {
                chance *= RangedMissMountedTargetFactor;
            }

            return MBMath.ClampFloat(chance, 0f, RangedMaxMissChance);
        }

        /// <summary>
        /// The launcher's own accuracy, read off the shaft it throws -- a bolt means a crossbow, an arrow a bow, a
        /// stone a sling. Anything the switch does not name (a mod's own ammunition class) falls through at the bow's
        /// rate, which is the middle of the three and the only sane default.
        /// </summary>
        private static float ShotAccuracyFactor(string weaponType)
        {
            if (weaponType == WeaponClass.Bolt.ToString())
            {
                return MissFactorBolt;
            }
            if (weaponType == WeaponClass.Stone.ToString())
            {
                return MissFactorStone;
            }
            return MissFactorArrow;
        }

        /// <summary>Skill as a fraction of the saturation level, 0..1 -- the same curve the damage side saturates on.</summary>
        private static float SkillFraction(float skill)
        {
            return MBMath.ClampFloat(skill / SkillSaturationLevel, 0f, 1f);
        }

        /// <summary>Behind an intact shield: a high, easy chance to defend a melee blow, climbing with the defender's own skill.</summary>
        private static float ShieldDefenseChance(float defenderSkill)
        {
            return MBMath.ClampFloat(ShieldDefenseBase + (ShieldDefenseSkillCoeff * SkillFraction(defenderSkill)), 0f, DefenseChanceCap);
        }

        /// <summary>With only a weapon (or a broken shield): about half the shield chance -- a non-zero floor, plus what skill adds.</summary>
        private static float WeaponDefenseChance(float defenderSkill)
        {
            return MBMath.ClampFloat(WeaponDefenseFloor + (WeaponDefenseSkillCoeff * SkillFraction(defenderSkill)), 0f, DefenseChanceCap);
        }

        /// <summary>
        /// What share of a successful defence is a parry (a counter) rather than a plain block. It turns on the skill
        /// ADVANTAGE the defender holds over his attacker: out-fight a man and your defences start biting back. A man
        /// out-skilled parries the base share or less, never below nought.
        /// </summary>
        private static float ParryShare(float defenderSkill, float attackerSkill)
        {
            float gap = MBMath.ClampFloat((defenderSkill - attackerSkill) / SkillSaturationLevel, -1f, 1f);
            return MBMath.ClampFloat(ParryShareBase + (ParryShareSkillGapCoeff * gap), 0f, ParryShareCap);
        }

        /// <summary>
        /// What a blocked blow costs the shield, in the same hit-point units as SimulationBattleState.ShieldCapacityPerMan.
        /// This is NOT the damage the block spared the man -- a shield is worn by the weapon that hits IT, and a point,
        /// a blade and a hurled spear wear it wholly differently. Mirrors the ladder RBM's live combat uses in
        /// DamageRework.RBMComputeBlowDamageOnShield: thrown weapons and chopping edges destroy a board, arrows wear it,
        /// maces dent it, thrusts glance off it. The blow's own magnitude scales it, so a harder strike chops more.
        /// </summary>
        private static float ShieldDamageFromBlow(SimulationWeaponModel.WeaponProfile drawn, bool missile)
        {
            float mag = drawn.Magnitude;
            if (mag <= 1f)
            {
                return 0f;
            }

            float factor;
            if (missile)
            {
                switch (drawn.WeaponType)
                {
                    case "Javelin": factor = ShieldDmgJavelin; break;
                    case "ThrowingAxe": factor = ShieldDmgThrowingAxe; break;
                    case "ThrowingKnife": factor = ShieldDmgThrowingKnife; break;
                    // A spear hurled overarm, not shot: it hits a shield like the heavy thrown weapon it is.
                    case "OneHandedPolearm":
                    case "LowGripPolearm": factor = ShieldDmgThrownPolearm; break;
                    case "Arrow":
                    case "Bolt": factor = ShieldDmgArrow; break;
                    // Sling stones and anything else in flight barely mark a board.
                    default: factor = ShieldDmgOtherMissile; break;
                }
            }
            else
            {
                switch (drawn.DamageType)
                {
                    // A point glances off a board; it is the worst thing to bring against a shield and the best to
                    // bring against the man behind it.
                    case DamageTypes.Pierce: factor = ShieldDmgMeleePierce; break;
                    case DamageTypes.Blunt: factor = ShieldDmgMeleeBlunt; break;
                    default: // Cut -- an axe or a two-handed polearm bites deepest; a sword still chops.
                        factor = IsChoppingEdge(drawn.WeaponType) ? ShieldDmgMeleeAxe : ShieldDmgMeleeCut;
                        break;
                }
            }

            return mag * factor * ShieldDamageScale;
        }

        /// <summary>The cutting weapons that bite a shield hardest -- axes and the great two-handed polearms.</summary>
        private static bool IsChoppingEdge(string weaponType)
        {
            switch (weaponType)
            {
                case "OneHandedAxe":
                case "TwoHandedAxe":
                case "OneHandedBastardAxe":
                case "TwoHandedPolearm":
                    return true;

                default:
                    return false;
            }
        }

        private static bool IsShield(ItemObject item)
        {
            return item.ItemType == ItemObject.ItemTypeEnum.Shield;
        }

        /// <summary>A javelin is a missile, and RBM does not scale a missile's thrust the way it scales a spear's.</summary>
        private static bool IsThrown(ItemObject item)
        {
            return item.ItemType == ItemObject.ItemTypeEnum.Thrown;
        }

        private static bool IsAmmo(ItemObject item)
        {
            ItemObject.ItemTypeEnum type = item.ItemType;
            return type == ItemObject.ItemTypeEnum.Arrows
                || type == ItemObject.ItemTypeEnum.Bolts
                || type == ItemObject.ItemTypeEnum.SlingStones;
        }

        private static bool IsLauncher(ItemObject item)
        {
            ItemObject.ItemTypeEnum type = item.ItemType;
            return type == ItemObject.ItemTypeEnum.Bow
                || type == ItemObject.ItemTypeEnum.Crossbow
                || type == ItemObject.ItemTypeEnum.Sling;
        }

        /// <summary>
        /// What this blow lands through that armour. The whole of the reckoning now lives in SimulationWeaponModel,
        /// which mirrors RBM's real equations rather than approximating them: the listed damage on a weapon is not
        /// used at all under RBM (a blow is its class and its wielder's training), and the weapon's quality shows
        /// itself as PENETRATION, dividing the armour threshold, rather than as force.
        /// </summary>
        private static float ExpectedDamage(SimulationWeaponModel.WeaponProfile profile, float armor, bool victimIsPlate,
            bool rbmCombat, int zone)
        {
            if (!profile.IsValid || profile.Magnitude <= 0f)
            {
                return 0f;
            }
            float damage = rbmCombat
                ? SimulationWeaponModel.RbmDamage(profile, armor, victimIsPlate)
                : SimulationWeaponModel.VanillaDamage(profile.Magnitude, armor, profile.DamageType);

            // And what it is worth where it landed. RBM pays a head half again and an arm or a leg about half, and
            // it pays them by DAMAGE TYPE -- so this has to be inside the per-weapon loop, where the type is known.
            // A pool of a mace and a spear does not share one multiplier any more than it shares one armour rule.
            return damage * BodyPartMultiplier(zone, profile.DamageType);
        }
    }
}