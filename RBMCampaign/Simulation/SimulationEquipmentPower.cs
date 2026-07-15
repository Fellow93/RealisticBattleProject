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
    /// It also lifts terrain back out of a FIELD blow. Vanilla priced how the four arms compare through
    /// DefaultMilitaryPowerModel's context table -- cavalry worth a quarter more in the open, archers worth
    /// half as much defending a wood -- but an arm's edge is meant to come from its horse and its lance now,
    /// both already in the equipment ratio, not from the ground it stands on. So on a field battle the context
    /// modifier is cancelled on both sides (see <see cref="GetTerrainNeutralizingFactor"/>); a siege keeps its
    /// own. Each troop is still judged against its own arm and no finer (see <see cref="GetBucket"/>), so an
    /// archer is never taxed for carrying an archer's armour.
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
        // vanilla's context table having already priced what an archer is worth against a horseman; the field
        // half of that table is now lifted out (see GetTerrainNeutralizingFactor), so on open ground nothing
        // prices arm against arm any more -- by design, an arm's edge is its horse and its lance, both already in
        // the equipment ratio. The bucket earns its place regardless: it normalises the damage units per arm, so
        // a lance is measured against lances and not counted "better kit" than a spear for landing more raw force.
        // (A siege still keeps vanilla's context, arm-vs-arm included.)
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
        private const int InfantryType = 0;

        private const int ArcherType = 1;

        private const int CavalryType = 2;

        private const int HorseArcherType = 3;

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
            public float Head;

            public float Body;

            public float Arm;

            public float Leg;

            /// <summary>What the horse adds at the leg and the body -- kept apart, because a dead horse adds nothing.</summary>
            public float HorseLeg;

            public float HorseBody;

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

        // A troop template's kit does not change at runtime, so it is cached by CharacterObject.
        private static readonly Dictionary<CharacterObject, TroopKit> _kitCache = new Dictionary<CharacterObject, TroopKit>();

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

        // What a man with a sword achieves while the lines are still closing, which is very nearly nothing: he is
        // walking into arrows with his shield up. Not quite nought -- skirmishers do meet, and the front ranks of
        // a charge do reach the line early -- but as near to it as makes no difference.
        private const float ClosingPenalty = 0.08f;

        // How often a horseman's blow is a charge and not a chop, on open ground where the horse has all the room it
        // wants. A rider does not gallop through the melee at a steady speed for three rounds and then stop: he comes
        // in at the gallop, kills, is hemmed in, backs out, finds room and comes again. Some of his blows carry the
        // weight of the horse behind them and most do not, and which is which is a matter of where he happens to be.
        // So it is a coin, not a countdown -- and how weighted the coin is falls with the room to ride (KitingRoom),
        // from half his blows on the steppe to none at all on a wall or a village street.
        private const float ChargeChance = 0.5f;

        // A spear set for a horse. Infantry have answered cavalry this way for three thousand years and
        // auto-resolve has never once let them.
        private const float BraceBonus = 1.6f;

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

        private const int ZoneHead = 0;

        private const int ZoneBody = 1;

        private const int ZoneArm = 2;

        private const int ZoneLeg = 3;

        private const int ZoneCount = 4;

        /// <summary>Where the blows land, as a share of them. The four zones Bannerlord's armour actually has.</summary>
        private struct HitZones
        {
            public float Head;

            public float Body;

            public float Arm;

            public float Leg;
        }

        // A blow does not land just anywhere: where it lands depends on where the two men are standing, and a
        // simulated blow has no body part unless we give it one. Bannerlord keeps armour in four zones -- the
        // neck, the shoulders and the abdomen a fighter thinks of separately all fall under Body -- so these are
        // the four we can weight. Each set sums to 1: it is a distribution, not a set of multipliers.

        // Foot against foot: the two are eye to eye, so it is the chest, the shoulders and the arms that catch
        // it, the head often enough, and the legs almost never -- a man does not stoop to hack at ankles.
        private static readonly HitZones FootVsFoot = new HitZones { Head = 0.20f, Body = 0.55f, Arm = 0.20f, Leg = 0.05f };

        // Foot against a rider: the horseman is above, and what is at a footman's eye level is the man's legs and
        // his lower body. This is why barding on a horse's flanks is worth so much, and it is what the model
        // could not see before.
        private static readonly HitZones FootVsMounted = new HitZones { Head = 0.05f, Body = 0.40f, Arm = 0.10f, Leg = 0.45f };

        // A rider against a man on foot: he strikes downward, so it is the head, the shoulders and the chest that
        // take it, and the legs are all but out of reach.
        private static readonly HitZones MountedVsFoot = new HitZones { Head = 0.30f, Body = 0.50f, Arm = 0.15f, Leg = 0.05f };

        // Rider against rider: level with one another again, much as two footmen are, with rather more coming at
        // the legs across the horses.
        private static readonly HitZones MountedVsMounted = new HitZones { Head = 0.20f, Body = 0.50f, Arm = 0.20f, Leg = 0.10f };

        // An arrow does not care how tall its target is: it goes where it was aimed, and that is the mass of the
        // man. So a missile keeps one distribution whether it is loosed at a footman or a horseman.
        private static readonly HitZones Missile = new HitZones { Head = 0.15f, Body = 0.60f, Arm = 0.15f, Leg = 0.10f };

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

        /// <summary>How many troop types went into each bucket's average -- printed, so a skewed population shows.</summary>
        private static int[] _bucketPopulation = new int[BucketCount];

        private static void Postfix(ref ExplainedNumber __result, CharacterObject strikerTroop, CharacterObject struckTroop,
            PartyBase strikerParty, PartyBase struckParty, MapEvent battle)
        {
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
            Explain(strikerTroop, struckTroop, out breakdown, state, strikerIsAttacker, spend: true);

            // The ground no longer favours an arm of service. Vanilla's blow carries GetContextModifier -- the
            // (arm x terrain x side) table that hands cavalry a quarter more on open ground and docks it in a wood
            // -- and the equipment correction above divides out only the tier base, so that table would otherwise
            // ride untouched into the result. An arm's edge is meant to come from its horse and its lance now, both
            // already priced in the equipment ratio, not from the field it happens to stand on. So on a field battle
            // the context is lifted back out; a siege keeps its own, and the leader's modifier is not terrain and
            // stays. Folded INTO the correction, not applied after it, so the log's Vanilla x Correction = Final
            // identity holds and RecordHit writes the whole of what the model did (see GetTerrainNeutralizingFactor).
            if (breakdown.Correction > 0f)
            {
                float terrainFactor = GetTerrainNeutralizingFactor(strikerTroop, struckTroop, strikerParty, struckParty);
                if (terrainFactor != 1f)
                {
                    breakdown.Correction *= terrainFactor;
                }
            }

            float vanillaDamage = __result.ResultNumber;
            float correction = breakdown.Correction;

            if (correction != 1f)
            {
                __result = new ExplainedNumber(vanillaDamage * correction);
            }

            RecordHit(state, strikerTroop, struckTroop, strikerIsAttacker, battle, breakdown, vanillaDamage);
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
            if (breakdown.Correction <= 0f)
            {
                return;
            }

            float finalDamage = vanillaDamage * breakdown.Correction;
            int hitPoints = struckTroop.MaxHitPoints();

            HitRecord hit = new HitRecord();
            hit.Round = state.Round;
            hit.VolleyPhase = SimulationBattleState.IsVolleyPhase(state);
            hit.SkirmishPhase = SimulationBattleState.IsSkirmishPhase(state);
            hit.StrikerIsAttacker = strikerIsAttacker;
            hit.Striker = strikerTroop;
            hit.Struck = struckTroop;
            hit.Phase = breakdown.Phase ?? "-";
            hit.Weapon = breakdown.Weapon;
            hit.BodyPart = ZoneName(breakdown.BodyPart);
            hit.ArmorMet = breakdown.ArmorMet;
            hit.ShieldBlock = breakdown.ShieldBlock;
            hit.Braced = breakdown.Braced;
            hit.ChargeBonus = breakdown.ChargeBonus;
            hit.Closing = breakdown.Closing;
            hit.Evaded = breakdown.Evaded;
            hit.VanillaDamage = vanillaDamage;
            hit.Correction = breakdown.Correction;
            hit.FinalDamage = finalDamage;
            hit.StruckHitPoints = hitPoints;
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
        internal static float GetCorrection(CharacterObject strikerTroop, CharacterObject struckTroop,
            SimulationBattleState.BattleState state = null, bool strikerIsAttacker = false, bool spend = false)
        {
            Breakdown breakdown;
            Explain(strikerTroop, struckTroop, out breakdown, state, strikerIsAttacker, spend);
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

            /// <summary>Which part of him it found -- ZoneHead/Body/Arm/Leg, or -1 for the reference tables, which
            /// take the expectation over all four and so land nowhere in particular.</summary>
            public int BodyPart;

            /// <summary>He set a spear against a horse.</summary>
            public bool Braced;

            /// <summary>What the charge added, as a multiplier. 1 when he is not charging, or no longer is.</summary>
            public float ChargeBonus;

            /// <summary>The lines had not met yet and he was walking into arrows with nothing to answer them.</summary>
            public bool Closing;

            /// <summary>He swung at a horse archer who still had arrows, and the horse archer was not there.</summary>
            public bool Evaded;
        }

        internal static bool Explain(CharacterObject strikerTroop, CharacterObject struckTroop, out Breakdown breakdown,
            SimulationBattleState.BattleState state = null, bool strikerIsAttacker = false, bool spend = false)
        {
            breakdown = default(Breakdown);
            breakdown.Correction = 1f;

            if (!RBMConfig.RBMConfig.simulationEquipmentEnabled || RBMConfig.RBMConfig.simulationEquipmentPowerWeight <= 0f)
            {
                return false;
            }
            if (strikerTroop == null || struckTroop == null)
            {
                return false;
            }

            EnsureBaselines();

            TroopKit striker = GetKit(strikerTroop);
            TroopKit struck = GetKit(struckTroop);
            if (!striker.IsValid || !struck.IsValid)
            {
                return false;
            }

            bool rbmCombat = RBMConfig.RBMConfig.rbmCombatEnabled;

            // Where in the battle this blow falls, and what the two men have left to spend.
            SimulationBattleState.TroopState strikerState = (state != null) ? state.For(strikerTroop, strikerIsAttacker) : null;
            SimulationBattleState.TroopState struckState = (state != null) ? state.For(struckTroop, !strikerIsAttacker) : null;

            // A battle nobody rides in. A siege has no horses at all: the wall is stormed on foot and held on foot,
            // and a cavalry troop here is a lance and a suit of barding with no animal under it. The kit is cached
            // terrain-blind and still calls him mounted, so we undo it here -- and once he is not mounted, none of
            // what follows treats him as though he were: no charge, no barding at the leg, no horse to be killed
            // before he is, and no cavalry clash out in front. See SimulationBattleState.IsDismounted.
            bool dismounted = state != null && state.Dismounted;
            bool strikerMounted = striker.IsMounted && !dismounted;
            bool struckMounted = struck.IsMounted && !dismounted;

            // What the horse still has in it. A footman hacking upward is mostly hacking at the horse, and horses
            // die; when one does its rider keeps none of its barding and none of its height -- and he is no longer
            // a horseman for the purpose of anything below, including whether the cavalry are still fighting each
            // other. A man whose horse is dead has left the skirmish, whatever else he is doing. A man who never had
            // one -- because there are no horses in a siege -- has zero alive from the first blow, which strips the
            // barding the same way a killed mount would.
            float horsesAlive = !struckMounted ? 0f
                : (struckState != null)
                    ? SimulationBattleState.HorsesAlive(struckState)
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
                && SimulationBattleState.HasAmmo(state, struckState, !strikerIsAttacker);

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
            bool mayLoose = !(volley && strikerIsAttacker && state != null
                && state.Round <= SimulationBattleState.DefenderOnlyRounds);

            // Whether he still HAS arrows is a question about the clock, not about how many blows he happens to
            // have thrown: a quiver empties per minute, not per swing. Nothing is spent here -- the round counter
            // is the spending.
            bool shooting = striker.IsRanged
                && striker.Shot.IsValid
                && mayLoose
                && SimulationBattleState.HasAmmo(state, strikerState, strikerIsAttacker);
            // Otherwise the quiver is empty (or he is not yet in range). He draws from his melee arsenal like
            // anybody else, and his armour was never meant for that.

            // THE SKIRMISH, and the javelins come off his back HERE -- not during the long approach. A man does not
            // hurl a spear at somebody a bowshot away; he carries it until the ground between the lines is close
            // enough to cross with it, and then he throws it, and then it is gone and he is a man with a knife.
            // That is the whole life of a skirmisher, and auto-resolve has never once let him live it: his javelins
            // were either ignored entirely or -- worse -- treated as the weapon he swung for the whole battle, an
            // axe thrown on an infinite loop.
            bool throwing = !shooting
                && skirmish
                && striker.Thrown.IsValid
                && striker.Thrown.Magnitude > 0f
                && SimulationBattleState.HasJavelins(state, strikerState, striker.ThrownPerMan);

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

            // AND IN THE VOLLEY, A MAN WHO IS NOT SHOOTING DOES NOTHING AT ALL.
            //
            // Not a weak blow -- NO blow. The lines are a bowshot apart. There is nothing there to hit: no sword
            // reaches that far, and a man walking toward an enemy he cannot touch is not fighting badly, he is not
            // fighting. The volley is the archers' round and nobody else's, which is the entire reason it is worth
            // having archers.
            //
            // It used to be a closing PENALTY -- a hundredth of a blow, but a blow -- and across four thousand of
            // them that adds up to a real body count landed by men who were, at the time, several hundred yards
            // away with their shields up.
            //
            // Nothing is spent here either: he splinters no shield and kills no horse, because he never reached one.
            if (volley && !shooting)
            {
                breakdown.Phase = "closing";
                breakdown.Weapon = "-";
                breakdown.BodyPart = -1;
                breakdown.Closing = true;
                breakdown.Correction = 0f;
                return true;
            }

            // The board comes up. It turns aside more arrows than blows -- an arrow flies in from one direction and
            // sticks where it lands, while a swordsman feints, comes round the edge, and waits for it to drop.
            float shieldBlock = GetShieldBlock(struck.ShieldQuality, againstMissile: missile);
            if (struckState != null && shieldBlock > 0f)
            {
                shieldBlock *= SimulationBattleState.ShieldIntegrity(struckState, struck.ShieldHitPoints);
            }

            // The blow. A bowman looses his shot, a skirmisher hurls a javelin, and everyone else draws from the
            // weapons on his belt -- at random against a man on foot, and reaching for the spear when it is a horse
            // bearing down on him. And it lands SOMEWHERE on him: a real blow rolls a body part, meets the armour
            // standing over that part, and is worth what RBM says a blow to that part is worth. The reference tables
            // take the expectation over all four instead, since they are asking about a matchup and not a moment.
            HitZones zones = GetHitZones(strikerMounted, missile, struckStillMounted);

            int zoneHit;
            float armor;
            SimulationWeaponModel.WeaponProfile drawn;
            bool braced;
            float actual = Blow(striker, struck, rbmCombat, zones, horsesAlive, struckStillMounted,
                shooting, throwing, roll: spend, out zoneHit, out armor, out drawn, out braced);

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
            if (shooting && volley)
            {
                actual *= SimulationBattleState.VolleyFocus(state, strikerIsAttacker);
            }

            // The charge: weight and speed, which a horseman has only some of the time. A lance at the gallop is a
            // different thing from the same man hemmed in and hacking downward from a standing horse -- and over a
            // long fight he is both, by turns, as he rides in, kills, backs out and comes again. So a share of his
            // blows carry the horse behind them and the rest are just a man swinging from a saddle. A horseman
            // flinging a javelin is never charging -- he is riding past at a distance, which is the point of javelins.
            //
            // How large that share is depends on the ground, and on exactly the same ground the horse archer's kiting
            // depends on: it is room for the horse to run. On the open steppe he can chop, wheel out, find speed and
            // come again, and half his blows carry the charge; in a wood the lanes are short and the horse never gets
            // up to it; in a village street or on a wall there is no charge at all. `KitingRoom` is that same measure,
            // so the two ride together off one terrain reading. See GetKitingRoom.
            //
            // It fires only once he has MET somebody. While the lines are still closing he has nobody to ride down,
            // and a charge delivered into empty ground is not a charge.
            breakdown.ChargeBonus = 1f;
            if (strikerMounted && !missile && engaged && striker.ChargeDamage > 0f && state != null
                && MBRandom.RandomFloat < ChargeChance * state.KitingRoom)
            {
                breakdown.ChargeBonus = 1f + (striker.ChargeDamage * 0.01f);
                actual *= breakdown.ChargeBonus;
            }

            // Braced steel. A spear set against a horse is the answer infantry have always had to cavalry, and
            // auto-resolve has never once let them use it. `braced` is already the right question asked and
            // answered: it is true exactly when he drew from his polearms, which he does only against a horse.
            if (braced && !strikerMounted)
            {
                actual *= BraceBonus;
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

            float blocked = actual * shieldBlock;
            actual -= blocked;

            if (spend && struckState != null)
            {
                // A shield takes what it stops, and a horse takes what a footman aims at its flank. A javelin does
                // not go for the horse -- it goes where it was thrown, at the mass of the man -- so a throw is a
                // missile here too, and leaves the animal alone. (What the shield can take is denominated in this
                // same simulated damage; see ShieldCapacityPerMan.)
                SimulationBattleState.DamageShield(struckState, blocked);
                if (struckMounted && !strikerMounted && !missile)
                {
                    SimulationBattleState.DamageHorse(struckState, actual);
                }
            }

            breakdown.ArmorMet = armor;
            breakdown.ShieldBlock = shieldBlock;
            breakdown.Actual = actual;

            // Against what the average man of his arm does to the average man of the other's. This says how
            // good the matchup is, and nothing about how senior either soldier is.
            float baseline = GetBaselineDamage(strikerTroop, struckTroop);
            breakdown.Baseline = baseline;
            if (baseline <= 0f)
            {
                return false;
            }
            float equipmentRatio = actual / baseline;
            breakdown.EquipmentRatio = equipmentRatio;

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
            // of field blows afterwards, folding the factor into breakdown.Correction (see
            // GetTerrainNeutralizingFactor). A siege keeps its context; the reference/matchup tables, which have no
            // battle at all, are terrain-blind by nature and leave it untouched.
            float tierTerm = MathF.Pow(VanillaTierPower(strikerTroop) / VanillaTierPower(struckTroop), 0.7f);
            breakdown.TierTerm = tierTerm;
            if (tierTerm <= 0f)
            {
                return false;
            }

            // Weight is the whole dial: 0 leaves vanilla exactly as it was, 1 is the model at face value, and
            // above 1 widens the gap between a well-found soldier and a ragged one.
            float correction = MathF.Pow(equipmentRatio / tierTerm, RBMConfig.RBMConfig.simulationEquipmentPowerWeight);

            // Wide, because a real mismatch is meant to be lopsided now -- a spear through an unarmoured looter
            // should put him down, and his club should ring off a mail hauberk. But not unbounded: a single
            // simulated blow must not become a massacre on the strength of one freak pairing.
            //
            // The clamp bounds the EQUIPMENT term and nothing else, which is why it is taken here, before the
            // closing penalty rather than after it.
            correction = MBMath.ClampFloat(correction, 0.1f, 8f);

            // The SKIRMISH, for a man with no javelin and no horse. He is close enough now that a blow is at least
            // conceivable -- the ground between the lines is not a bowshot any more -- but he is still walking, past
            // a cavalry battle he can do nothing about and under javelins he cannot answer. So he pays the closing
            // penalty here. (In the VOLLEY he pays no penalty because he lands no blow: see above.)
            //
            // This is applied AFTER the clamp, and it has to be. Inside it, 0.08 times anything below 1.25 landed
            // under the 0.1 floor -- so every walking infantryman in Calradia came out at exactly 0.1, and a
            // Hireling Elite Pikeman in plate was worth precisely as much as a naked recruit for as long as the
            // lines were closing. The trace showed it in one glance: sixty-four walking blows, every one of them
            // 0.10. A phase of the battle is not an equipment mismatch and has no business inside a clamp that
            // exists to bound one.
            if (!engaged)
            {
                correction *= ClosingPenalty;
                breakdown.Closing = true;
            }

            breakdown.Correction = correction;
            return true;
        }

        /// <summary>A troop's kit as the model sees it, for the log to print.</summary>
        internal struct KitInfo
        {
            public float Head;

            public float Body;

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
            info.Body = kit.Body;
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
        /// The factor that lifts vanilla's terrain-vs-arm bonus back out of a FIELD blow, leaving a siege alone.
        ///
        /// Vanilla (<c>DefaultCombatSimulationModel.SimulateHit</c>) prices a blow on
        /// <c>pow(troopPower_s / troopPower_k, 0.7)</c>, where <c>troopPower = defaultPower * (1 + leader +
        /// GetContextModifier)</c>. That context modifier is the <c>(arm x terrain x side)</c> table -- cavalry
        /// worth a quarter more attacking on open ground, docked defending a wood -- and it does NOT cancel between
        /// striker and struck, because they are different arms on different sides. The equipment correction divides
        /// out only the DEFAULT (tier) term, so the context rides untouched into the result.
        ///
        /// So we recompute the ratio with the context zeroed on both sides and hand back
        /// <c>neutralRatio / vanillaRatio</c> -- the factor that turns vanilla's terrain-laden blow into a
        /// terrain-free one. The leader modifier is not terrain and is kept on both sides; a SIEGE keeps its full
        /// vanilla context (the wall is its own fact, not "terrain" in the sense meant here) and gets 1 here.
        ///
        /// Recomputed through the live model, exactly as <see cref="VanillaTierPower"/> mirrors the tier base, so
        /// that whatever context vanilla actually charged is what we lift -- no more, no less.
        /// </summary>
        private static float GetTerrainNeutralizingFactor(CharacterObject strikerTroop, CharacterObject struckTroop,
            PartyBase strikerParty, PartyBase struckParty)
        {
            if (strikerTroop == null || struckTroop == null || strikerParty == null || struckParty == null
                || strikerParty.MapEvent == null || struckParty.MapEvent == null
                || strikerParty.MapEventSide == null || struckParty.MapEventSide == null)
            {
                return 1f;
            }

            MapEvent.PowerCalculationContext strikerContext = strikerParty.MapEvent.SimulationContext;
            MapEvent.PowerCalculationContext struckContext = struckParty.MapEvent.SimulationContext;

            // A siege keeps its whole context; and the Estimated context carries no modifier to begin with (vanilla
            // skips GetContextModifier for it), so there is nothing there to lift.
            if (strikerContext == MapEvent.PowerCalculationContext.Siege
                || strikerContext == MapEvent.PowerCalculationContext.Estimated)
            {
                return 1f;
            }

            var model = Campaign.Current?.Models?.MilitaryPowerModel;
            if (model == null)
            {
                return 1f;
            }

            float leaderStriker = LeaderModifierOf(strikerParty);
            float leaderStruck = LeaderModifierOf(struckParty);

            float contextStriker = model.GetContextModifier(strikerTroop, strikerParty.Side, strikerContext);
            float contextStruck = model.GetContextModifier(struckTroop, struckParty.Side, struckContext);

            // Neither man got anything from the ground: nothing to lift.
            if (contextStriker == 0f && contextStruck == 0f)
            {
                return 1f;
            }

            float withTerrainStriker = 1f + leaderStriker + contextStriker;
            float withTerrainStruck = 1f + leaderStruck + contextStruck;
            float withoutTerrainStriker = 1f + leaderStriker;
            float withoutTerrainStruck = 1f + leaderStruck;

            // A pathological leader+context sum could reach zero or below, where vanilla's own pow() is already
            // undefined; leave such a blow exactly as vanilla left it rather than invent a number for it.
            if (withTerrainStriker <= 0f || withTerrainStruck <= 0f
                || withoutTerrainStriker <= 0f || withoutTerrainStruck <= 0f)
            {
                return 1f;
            }

            float vanillaRatio = MathF.Pow(withTerrainStriker / withTerrainStruck, 0.7f);
            float neutralRatio = MathF.Pow(withoutTerrainStriker / withoutTerrainStruck, 0.7f);
            if (vanillaRatio <= 0f)
            {
                return 1f;
            }
            return neutralRatio / vanillaRatio;
        }

        /// <summary>
        /// The side commander's power modifier, as vanilla caches it into <c>MapEventSide.LeaderSimulationModifier</c>
        /// (an internal field): <c>LeaderParty.LeaderHero?.PowerModifier</c>. Recomputed off the public API so the
        /// terrain fixup lifts the same leader term vanilla actually charged, and keeps it rather than removing it.
        /// </summary>
        private static float LeaderModifierOf(PartyBase party)
        {
            return party?.MapEventSide?.LeaderParty?.LeaderHero?.PowerModifier ?? 0f;
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
            bool rbmCombat = RBMConfig.RBMConfig.rbmCombatEnabled;
            float shieldBlockChance = RBMConfig.RBMConfig.simulationShieldBlockChance;
            float armorMultiplier = RBMConfig.RBMConfig.armorMultiplier;
            float armorThreshold = RBMConfig.RBMConfig.armorThresholdModifier;
            float thrustModifier = RBMConfig.RBMConfig.ThrustMagnitudeModifier;

            if (_baselinesBuilt
                && _baselineRbmCombat == rbmCombat
                && _baselineShieldBlockChance == shieldBlockChance
                && _baselineArmorMultiplier == armorMultiplier
                && _baselineArmorThreshold == armorThreshold
                && _baselineThrustModifier == thrustModifier)
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
                float head = 0f, body = 0f, arm = 0f, leg = 0f;
                float horseLeg = 0f, horseBody = 0f;
                int mounted = 0;
                int plate = 0;
                foreach (TroopKit kit in byBucket[i])
                {
                    head += kit.Head;
                    body += kit.Body;
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
                typical[i].Body = body / count;
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
                        float blocked = shooting ? _typicalShieldBlockVsMissile[k] : _typicalShieldBlock[k];

                        // The SAME function the live blow calls, deliberately -- and here asked for the expectation
                        // rather than a roll, since a baseline is a matchup and not a moment. It has to be the same
                        // function or the body-part multipliers would sit in the blow and not in the baseline, and
                        // every striker in Calradia would read as unusually good (or bad) purely because of where
                        // his blows happen to land.
                        HitZones zones = GetHitZones(kit.IsMounted, shooting, bucketMounted[k]);

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

        /// <summary>The four arms of service, split exactly as vanilla's own power model splits them.</summary>
        private static int GetTroopType(CharacterObject troop)
        {
            if (troop.IsMounted)
            {
                return troop.IsRanged ? HorseArcherType : CavalryType;
            }
            return troop.IsRanged ? ArcherType : InfantryType;
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

        private static TroopKit GetKit(CharacterObject troop)
        {
            // A troop template's kit and training are fixed, so it is cached for good. A hero's are not -- he buys
            // gear and trains skills as the campaign runs -- but they do not change in the MIDDLE of a battle, and
            // pricing him afresh on every single blow was ruinous: rebuilding a hero's kit runs the thrust-physics
            // simulation over every weapon in every equipment set, and the shadow replay fights the same battle
            // forty times over. So heroes are cached too, and the cache is emptied when a new battle opens.
            TroopKit cached;
            if (_kitCache.TryGetValue(troop, out cached))
            {
                return cached;
            }

            bool rbmCombat = RBMConfig.RBMConfig.rbmCombatEnabled;

            // A troop template usually lists several battle sets and each man rolls one at random, so no single
            // set speaks for the stack: average the armour over all of them, and pool every melee weapon in every
            // set into the one arsenal the stack fights out of.
            float head = 0f, body = 0f, arm = 0f, leg = 0f;
            float horseLeg = 0f, horseBody = 0f;
            float shotMagnitude = 0f;
            float shieldQuality = 0f, shieldHitPoints = 0f;
            float charge = 0f;
            float bestShotMagnitude = 0f;
            int plateSets = 0;
            SimulationWeaponModel.WeaponProfile bestShot = default(SimulationWeaponModel.WeaponProfile);
            List<MeleeOption> melee = new List<MeleeOption>();
            int sets = 0;

            float thrownMagnitude = 0f, thrownPerMan = 0f, bestThrownMagnitude = 0f;
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
                if (troop.IsRanged)
                {
                    List<SimulationWeaponModel.WeaponProfile> setShots = CollectShotProfiles(troop, set, rbmCombat);
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
                SimulationWeaponModel.WeaponProfile setThrown = GetThrownProfile(troop, set, rbmCombat, out setThrownPerMan);

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
                List<SimulationWeaponModel.WeaponProfile> setMelee = CollectMeleeProfiles(troop, set, rbmCombat);
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

                float setHead, setBody, setArm, setLeg;
                GetArmorZones(set, out setHead, out setBody, out setArm, out setLeg);
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
                }
                ItemObject harness = set[EquipmentIndex.HorseHarness].Item;
                if (harness != null && harness.ArmorComponent != null)
                {
                    setHorseLeg += harness.ArmorComponent.BodyArmor * 0.5f;
                    setHorseBody += harness.ArmorComponent.BodyArmor * 0.2f;
                }

                head += setHead;
                body += setBody;
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
                kit.Body = body / sets;
                kit.Arm = arm / sets;
                kit.Leg = leg / sets;
                kit.HorseLeg = horseLeg / sets;
                kit.HorseBody = horseBody / sets;
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
                kit.IsRanged = troop.IsRanged;

                // A man is worth pricing if he can hit anything at all -- with a bow, or with what is on his belt.
                kit.IsValid = (kit.Shot.IsValid && kit.Shot.Magnitude > 0f) || kit.Melee.Length > 0;
            }

            _kitCache[troop] = kit;
            return kit;
        }

        /// <summary>
        /// Forget every hero's kit, at the opening of a battle. A lord buys armour and trains between one fight and
        /// the next, so what was measured last month is not what he rides out in today -- but nothing he owns
        /// changes while the battle is being fought, so within one battle the cache is exact.
        /// </summary>
        internal static void ForgetHeroKits()
        {
            List<CharacterObject> heroes = null;
            foreach (KeyValuePair<CharacterObject, TroopKit> entry in _kitCache)
            {
                if (entry.Key.IsHero)
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
                    _kitCache.Remove(hero);
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

        /// <summary>The real armour points this kit carries, kept apart by the zone each protects.</summary>
        private static void GetArmorZones(Equipment set, out float head, out float body, out float arm, out float leg)
        {
            head = 0f;
            body = 0f;
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
        }

        /// <summary>
        /// Which way the blows fall in this particular matchup. The live combat path reads armour at the exact
        /// body part struck; a simulated blow has no body part, so it is given the one it would most likely have
        /// found, and that depends entirely on who is swinging at whom.
        /// </summary>
        private static HitZones GetHitZones(bool strikerMounted, bool strikerRanged, bool struckMounted)
        {
            if (strikerRanged)
            {
                return Missile;
            }
            if (strikerMounted)
            {
                return struckMounted ? MountedVsMounted : MountedVsFoot;
            }
            return struckMounted ? FootVsMounted : FootVsFoot;
        }

        /// <summary>
        /// What a blow is WORTH where it lands, which is not the same question as what armour it meets there.
        /// Straight out of RBM's own DamageRework.GetBodyPartDamageMultiplier -- a head is worth half again, and an
        /// arm or a leg between half and seven-tenths. A head hit is three times a leg hit, and the model had every
        /// blow in Calradia worth exactly the same wherever it landed.
        ///
        /// RBM's table is over six bones; this model has Bannerlord's four ARMOUR zones, so two of them fold:
        ///   - Body  = chest (0.9) and abdomen (1.0). Taken at 0.95.
        ///   - Arm   = the arms (0.5/0.6/0.7) and the shoulders (0.6/0.6/0.7), which share an armour value here.
        /// Head, Neck and Legs map across untouched.
        /// </summary>
        private static float BodyPartMultiplier(int zone, DamageTypes damageType)
        {
            bool ordinary = damageType == DamageTypes.Pierce
                || damageType == DamageTypes.Cut
                || damageType == DamageTypes.Blunt;

            switch (zone)
            {
                case ZoneHead:
                    return ordinary ? 1.5f : 1f;

                case ZoneBody:
                    return ordinary ? 0.95f : 1f;

                case ZoneArm:
                    if (damageType == DamageTypes.Pierce) { return 0.55f; }
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
                case ZoneBody: return "body";
                case ZoneArm: return "arm";
                case ZoneLeg: return "leg";
                default: return "-";
            }
        }

        /// <summary>The armour standing over one zone of this man, the horse under him included where it belongs.</summary>
        private static float ZoneArmor(TroopKit struck, int zone, float horsesAlive)
        {
            switch (zone)
            {
                case ZoneHead: return struck.Head;
                case ZoneBody: return struck.Body + (struck.HorseBody * horsesAlive);
                case ZoneArm: return struck.Arm;
                case ZoneLeg: return struck.Leg + (struck.HorseLeg * horsesAlive);
                default: return 0f;
            }
        }

        private static float ZoneShare(HitZones zones, int zone)
        {
            switch (zone)
            {
                case ZoneHead: return zones.Head;
                case ZoneBody: return zones.Body;
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
            if ((roll -= zones.Body) < 0f) { return ZoneBody; }
            if ((roll -= zones.Arm) < 0f) { return ZoneArm; }
            return ZoneLeg;
        }

        /// <summary>The armour a blow actually meets: this man's zones, weighted by where blows of this kind land.</summary>
        private static float WeightedArmor(float head, float body, float arm, float leg, HitZones zones)
        {
            return (head * zones.Head) + (body * zones.Body) + (arm * zones.Arm) + (leg * zones.Leg);
        }

        /// <summary>
        /// The armour this blow meets -- with the horse counted only for as long as the horse is alive. A rider
        /// whose mount has been killed under him is a man on foot: the barding that was catching every blow is
        /// gone, and the blows stop coming at his legs and start coming at his head.
        /// </summary>
        private static float WeightedArmor(TroopKit struck, TroopKit striker, float horsesAlive, bool struckStillMounted, bool shooting)
        {
            HitZones zones = GetHitZones(striker.IsMounted, shooting, struckStillMounted);
            float leg = struck.Leg + (struck.HorseLeg * horsesAlive);
            float body = struck.Body + (struck.HorseBody * horsesAlive);
            return WeightedArmor(struck.Head, body, struck.Arm, leg, zones);
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
        /// The shot a bowman looses, priced by RBM's own missile model (see SimulationWeaponModel).
        ///
        /// Whether he shoots at all is decided by what he IS, not by which of his weapons carries the biggest
        /// number. An Empire Peasant carries a sling and a scythe; the game fields him as infantry, so he closes
        /// and swings the scythe. Reading the largest number in his kit instead armed every peasant in Calradia
        /// with his sling in the melee line and made a farmhand hit harder than a tribesman's spear.
        /// </summary>
        private static SimulationWeaponModel.WeaponProfile GetWeaponProfile(CharacterObject troop, Equipment set, bool rbmCombat, bool ranged)
        {
            SimulationWeaponModel.WeaponProfile best = default(SimulationWeaponModel.WeaponProfile);

            // A bowman shoots -- with EVERY quiver on his back, not just the one with the biggest number on it.
            if (ranged)
            {
                List<SimulationWeaponModel.WeaponProfile> shots = CollectShotProfiles(troop, set, rbmCombat);
                foreach (SimulationWeaponModel.WeaponProfile shot in shots)
                {
                    if (shot.Magnitude > best.Magnitude)
                    {
                        best = shot;
                    }
                }
                // A bowman with no bow after all is not a bowman. He has a belt like everyone else, and CollectMeleeProfiles
                // will find it.
            }

            return best;
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
        private static List<SimulationWeaponModel.WeaponProfile> CollectShotProfiles(CharacterObject troop, Equipment set, bool rbmCombat)
        {
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

                    int skill = (launcher.RelevantSkill != null) ? troop.GetSkillValue(launcher.RelevantSkill) : 0;

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
            bool rbmCombat, out float perMan)
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

                int skill = (weapon.RelevantSkill != null) ? troop.GetSkillValue(weapon.RelevantSkill) : 0;

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
        private static List<SimulationWeaponModel.WeaponProfile> CollectMeleeProfiles(CharacterObject troop, Equipment set, bool rbmCombat)
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

                int skill = (weapon.RelevantSkill != null) ? troop.GetSkillValue(weapon.RelevantSkill) : 0;

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
        /// A shot, with RBM Combat off. THIS DID NOT EXIST, and its absence was a real hole: the ranged branch of
        /// GetWeaponProfile called RBM's missile physics unconditionally, so with RBM Combat disabled every archer
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
