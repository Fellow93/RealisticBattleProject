using System;
using TaleWorlds.Core;
using TaleWorlds.Library;
using RBMConfig;

namespace RBMCampaign
{
    /// <summary>
    /// A mirror of how RBM's combat module actually decides what a blow is worth, so that a battle resolved on
    /// the map is decided by the same things as a battle fought on the field.
    ///
    /// Three findings drove this, and every one of them contradicted what the auto-resolve model had assumed:
    ///
    /// 1. A WEAPON'S LISTED DAMAGE IS NEVER USED. RBM's melee damage is
    ///        clamp(physicsMagnitude + effectiveSkill*C, MIN*(1+sm), MAX*(1+2*sm)) * SCALE
    ///    and since the skill term alone clears MAX for all but the feeblest blow, damage collapses to the
    ///    skill-borne ceiling: MAX*(1+2*sm)*SCALE, a thing of the weapon's CLASS and the man's TRAINING, and of
    ///    nothing else. A sickle and a longsword swing for the same number if the same man holds them.
    ///
    /// 2. SO WHAT MAKES A GOOD WEAPON GOOD IS PENETRATION, NOT FORCE. The item's damage factor has exactly one
    ///    use in the whole of RBM (Utilities.cs:1191): it DIVIDES the armour threshold. A finer blade does not
    ///    hit harder -- it finds the gap. That is the difference between the sickle and the longsword, and it
    ///    was missing from this model entirely.
    ///
    /// 3. A THRUST IS NOT A SWING. Pierce magnitude is raw thrust energy, capped at 180 (one-handed) or 250
    ///    (two-handed); the ×0.05 it is built with and the ×20 it is paid out with cancel exactly.
    ///
    /// Where physics is needed and cannot be had -- a swing's speed, a thrust's -- the ceiling is used, which is
    /// where the live path lands nearly always anyway. That is an approximation, and it is named as one.
    /// </summary>
    internal static class SimulationWeaponModel
    {
        // RBM's own caps on a thrust's energy (Utilities.cs:1335, 1378) and the arm behind it (1311-1312).
        private const float OneHandedThrustCap = 180f;

        private const float TwoHandedThrustCap = 250f;

        private const float OneHandedArmStrength = 2.5f;

        private const float TwoHandedArmStrength = 5f;

        // A thrust's speed is capped at 9 (one-handed) and 6 (two-handed).
        private const float OneHandedThrustSpeedCap = 9f;

        private const float TwoHandedThrustSpeedCap = 6f;

        // How far into the stroke the blow lands, as RBM's own reference evaluation of a weapon reckons it: all
        // the way. A FULL thrust and a sweet-spot swing.
        //
        // I had these at sqrt(0.1) and sqrt(0.5), which was a straight misreading. In MagnitudeChanges the 0.1 and
        // the 0.5 are CLAMP FLOORS on `progressEffect` -- a live parameter saying how far through the animation the
        // hit landed -- and not values at all:
        //
        //     if (strikeType == Thrust) { if (progressEffect < 0.1f) progressEffect = 0.1f; }
        //     else                      { if (progressEffect < 0.5f) progressEffect = 0.5f; }
        //     float accelerationProgress = MathF.Sqrt(progressEffect);
        //
        // RBM's own CalculateThrustMagnitude and CalculateSweetSpotSwingMagnitude -- the functions that answer
        // "what is this weapon worth", which is the exact question this model asks -- both open with
        // `float progressEffect = 1f;`. So the reference is 1, and taking the floor for the value made every
        // thrust in Calradia land at 0.316 of its speed and, energy being quadratic, a TENTH of its force. Every
        // spear, pike, lance and sword-thrust in the model has been worth a tenth of what RBM pays it.
        private const float ThrustAccelerationProgress = 1f;

        // RBM multiplies the swing by accelerationProgress TWICE -- once turning the speed rating into radians,
        // once inside the class switch -- so the swing carries progressEffect itself rather than its root. At the
        // reference progressEffect of 1 both are 1, which is why one constant serves here.
        private const float SwingAccelerationProgress = 1f;

        // Where along the blade the blow lands. Not the tip, not the hilt: the part of it a man means to hit with.
        private const float SwingImpactPoint = 0.75f;

        // The item's swing speed is a rating, not a rate; RBM turns it into radians a second by this (MagnitudeChanges).
        private const float SwingSpeedToRadians = 4.5454545f;

        // What is left of an arrow's momentum when it arrives. A shot in a battle is not loosed at arm's length --
        // it is loosed across the field, and it has been slowing the whole way. RBM's live path knows the real
        // remaining momentum because it knows the real flight; a simulated shot has no flight, so this stands in
        // for one at the distance men actually shoot at. It is a judgement, and it is a dial.
        private const float MissileMomentumRemaining = 0.7f;

        // A thrown weapon keeps more of itself than an arrow does, because it is not thrown nearly as far. A bowman
        // looses at a line eighty yards off; a man does not throw a javelin further than he can see a face.
        private const float ThrownMomentumRemaining = 0.85f;

        // Missile kinetic energy is capped per class, in proportion to the shot's weight (MagnitudeChanges.cs).
        private const float ArrowEnergyCap = 2250f;

        private const float BoltEnergyCap = 2500f;

        private const float SlingStoneEnergyCap = 3000f;

        private const float ThrownSpearEnergyCap = 300f;

        // A bow's draw, its powerstroke and its efficiency (Utilities.calculateMissileSpeed).
        private const float BowPowerstroke = 25f * 0.0254f;

        private const float CrossbowPowerstroke = 20f * 0.0254f;

        private const float PoundsToNewtons = 4.448f;

        /// <summary>What a man's blow is: how hard, of what kind, and how well it finds a gap in armour.</summary>
        internal struct WeaponProfile
        {
            public float Magnitude;

            public DamageTypes DamageType;

            /// <summary>The WeaponClass name -- the key RBM's own per-weapon factor table is written against.</summary>
            public string WeaponType;

            /// <summary>sqrt(the item's damage factor). Divides the armour threshold: this is a weapon's quality.</summary>
            public float DamageFactor;

            public SkillObject Skill;

            public bool IsMissile;

            public bool IsValid;
        }

        internal static float EffectiveSkill(int skill)
        {
            return (600f / (600f + skill)) * skill;
        }

        internal static float SkillModifier(int skill)
        {
            return MBMath.ClampFloat(skill / 250f, 0f, 1f);
        }

        // ---------------------------------------------------------------------------------------------------
        // Melee
        // ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// The ceiling RBM's damage clamp lands a melee blow on: MAX*(1+2*skillModifier)*SCALE, by weapon class
        /// and damage kind. Straight out of Utilities.GetSkillBasedDamage. A class RBM's switch does not name
        /// (a javelin held in the hand, a low-grip polearm) falls through there untouched, and does here too.
        /// </summary>
        private static bool GetMeleeClamp(WeaponClass weaponClass, DamageTypes damageType,
            out float skillCoefficient, out float min, out float max, out float scale)
        {
            skillCoefficient = 0f;
            min = 0f;
            max = 0f;
            scale = 0f;
            bool blunt = damageType == DamageTypes.Blunt;

            switch (weaponClass)
            {
                case WeaponClass.Dagger:
                case WeaponClass.OneHandedSword:
                case WeaponClass.ThrowingKnife:
                    skillCoefficient = blunt ? 0.075f : 0.133f;
                    min = blunt ? 15f : 5f;
                    max = blunt ? 20f : 15f;
                    scale = blunt ? (4f * 0.4f) : 4.6f;
                    return true;

                case WeaponClass.TwoHandedSword:
                    skillCoefficient = blunt ? 0.112f : 0.199f;
                    min = blunt ? 20f : 12f;
                    max = blunt ? 26f : 20f;
                    scale = blunt ? (4f * 0.4f) : 4.6f;
                    return true;

                case WeaponClass.OneHandedAxe:
                case WeaponClass.ThrowingAxe:
                    skillCoefficient = blunt ? 0.075f : 0.1f;
                    min = blunt ? 15f : 10f;
                    max = blunt ? 20f : 18f;
                    scale = blunt ? (4f * 0.3f) : 4.6f;
                    return true;

                case WeaponClass.TwoHandedAxe:
                    skillCoefficient = blunt ? 0.112f : 0.15f;
                    min = blunt ? 20f : 15f;
                    max = blunt ? 26f : 24f;
                    scale = blunt ? (4f * 0.3f) : 4.6f;
                    return true;

                case WeaponClass.Mace:
                    // A mace's Pierce is raw magnitude; everything else it does lands on this clamp.
                    skillCoefficient = 0.075f;
                    min = 10f;
                    max = 15f;
                    scale = 4.6f;
                    return true;

                case WeaponClass.TwoHandedMace:
                    skillCoefficient = 1.125f;
                    min = 15f;
                    max = 22f;
                    scale = 4.6f;
                    return true;

                case WeaponClass.OneHandedPolearm:
                    skillCoefficient = blunt ? 0.075f : 0.1f;
                    min = blunt ? 15f : 15f;
                    max = blunt ? 20f : 24f;
                    scale = blunt ? (4f * 0.3f) : 4f;
                    return true;

                case WeaponClass.TwoHandedPolearm:
                    skillCoefficient = blunt ? 0.0975f : 0.1495f;
                    min = blunt ? 20f : 18f;
                    max = blunt ? 26f : 28f;
                    scale = blunt ? (4f * 0.3f) : 4f;
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Vanilla's own swing physics (CombatStatCalculator.CalculateStrikeMagnitudeForSwing), which RBM feeds
        /// its scaled swing speed into. Reproduced so that a blow can land BETWEEN the clamp's floor and its
        /// ceiling, where a real one does -- rather than being pinned to the ceiling, which flattened every
        /// weapon of a class into the same blow.
        /// </summary>
        private static float StrikeMagnitudeForSwing(float angularSpeed, float weight, float length, float inertia, float centerOfMass)
        {
            if (weight <= 0f || inertia <= 0f)
            {
                return 0f;
            }
            float arm = (length * SwingImpactPoint) - centerOfMass;
            float linear = angularSpeed * (0.5f + centerOfMass);

            float energyBefore = (0.5f * weight * linear * linear) + (0.5f * inertia * angularSpeed * angularSpeed);

            float denominator = (1f / weight) + ((arm * arm) / inertia);
            if (denominator <= 0f)
            {
                return 0f;
            }
            float impulse = (linear + (angularSpeed * arm)) / denominator;

            float linearAfter = linear - (impulse / weight);
            float angularAfter = angularSpeed - ((impulse * arm) / inertia);
            float energyAfter = (0.5f * weight * linearAfter * linearAfter)
                              + (0.5f * inertia * angularAfter * angularAfter);

            return 0.067f * (energyBefore - energyAfter + 0.5f);
        }

        /// <summary>The speed RBM actually swings this weapon at: its rating, blunted by its class and its wielder's arm.</summary>
        private static float GetSwingMagnitude(WeaponComponentData weapon, float weight, int skill)
        {
            float skillDR = EffectiveSkill(skill);

            // These are the SWING factors. They were the THRUST factors -- 0.90/1000 for a two-handed axe and a
            // bare 0.75 default -- which is RBM's thrust table copied into the swing path by hand. A two-handed
            // axe swung 20% too fast (energy +44%) and every mace, hand-axe and polearm 10% too slow (-17%).
            // Compare MagnitudeChanges: the swing switch pays 0.83/1000 to the maces and polearms, 0.75/800 to the
            // two-handed axe, and 0.83/800 to the blades.
            float classFactor;
            float skillDivisor;
            switch (weapon.WeaponClass)
            {
                case WeaponClass.TwoHandedAxe:
                    classFactor = 0.75f;
                    skillDivisor = 800f;
                    break;

                case WeaponClass.OneHandedSword:
                case WeaponClass.Dagger:
                case WeaponClass.TwoHandedSword:
                    classFactor = 0.83f;
                    skillDivisor = 800f;
                    break;

                default:
                    // Mace, OneHandedAxe, OneHandedPolearm, TwoHandedPolearm, TwoHandedMace, LowGripPolearm.
                    classFactor = 0.83f;
                    skillDivisor = 1000f;
                    break;
            }

            float angularSpeed = (weapon.SwingSpeed / SwingSpeedToRadians)
                               * classFactor
                               * (1f + (skillDR / skillDivisor))
                               * SwingAccelerationProgress;

            return StrikeMagnitudeForSwing(angularSpeed, weight, weapon.GetRealWeaponLength(),
                weapon.TotalInertia, weapon.CenterOfMass);
        }

        /// <summary>
        /// RBM's own thrust-speed physics (Utilities.CalculateThrustSpeed / SimulateThrustLayer), reproduced whole.
        /// It is the ONLY thing that tells a dagger's thrust from a pike's: a heavy, unwieldy shaft accelerates
        /// slowly and so carries less. Assuming the speed cap instead -- which is what this model did at first --
        /// pinned every one-handed thrust at the 180 energy ceiling and made a looter's knife strike as hard as a
        /// peasant's pitchfork, which is exactly what the log showed.
        /// </summary>
        private static float CalculateThrustSpeed(float weight, float inertia, float centerOfMass)
        {
            float inertiaAroundGrip = inertia + (weight * centerOfMass * centerOfMass);
            double n = 1.8 + weight + (inertiaAroundGrip * 0.2);

            double t1 = SimulateThrustLayer(0.6, 250.0, 48.0, 4.0 + n);
            double t2 = SimulateThrustLayer(0.6, 170.0, 24.0, 2.0 + n);
            double t3 = SimulateThrustLayer(0.6, 90.0, 15.0, 0.5 + n);

            double meanTime = 0.33 * (t1 + t2 + t3);
            if (meanTime <= 0.0)
            {
                return 0f;
            }
            return (float)(3.8500000000000005 / meanTime);
        }

        /// <summary>How long the arm takes to push this much mass through the thrust, integrated a hundredth at a time.</summary>
        private static double SimulateThrustLayer(double distance, double usablePower, double maxUsableForce, double mass)
        {
            double travelled = 0.0;
            double speed = 0.01;
            double time = 0.0;
            while (travelled < distance)
            {
                double force = usablePower / speed;
                if (force > maxUsableForce)
                {
                    force = maxUsableForce;
                }
                speed += 0.01 * force / mass;
                travelled += speed * 0.01;
                time += 0.01;
            }
            return time;
        }

        /// <summary>
        /// A thrust's energy, which is what a Pierce blow's magnitude simply IS in RBM -- no clamp, no ceiling,
        /// just what the arm and the shaft carry. Capped at 180 or 250 by how many hands are on it.
        /// </summary>
        private static float GetThrustEnergy(WeaponComponentData weapon, float weaponWeight, int skill)
        {
            WeaponClass weaponClass = weapon.WeaponClass;
            bool twoHanded = weaponClass == WeaponClass.TwoHandedPolearm
                          || weaponClass == WeaponClass.TwoHandedSword
                          || weaponClass == WeaponClass.TwoHandedMace;

            float skillDR = EffectiveSkill(skill);

            // The speed the shaft is actually driven at: its own physics, blunted by its class and by the fact
            // that a thrust is thrown from a standstill (MagnitudeChanges.cs:96-133).
            float raw = CalculateThrustSpeed(weaponWeight, weapon.TotalInertia, weapon.CenterOfMass);
            float classFactor;
            float skillDivisor;
            switch (weaponClass)
            {
                case WeaponClass.TwoHandedPolearm:
                    classFactor = 0.65f;
                    skillDivisor = 800f;
                    break;

                case WeaponClass.TwoHandedAxe:
                    classFactor = 0.90f;
                    skillDivisor = 1000f;
                    break;

                case WeaponClass.OneHandedSword:
                case WeaponClass.Dagger:
                case WeaponClass.TwoHandedSword:
                    classFactor = 0.70f;
                    skillDivisor = 800f;
                    break;

                default:
                    classFactor = 0.75f;
                    skillDivisor = 1000f;
                    break;
            }

            float thrustSpeed = raw * classFactor * (1f + (skillDR / skillDivisor)) * ThrustAccelerationProgress;
            float speedCap = twoHanded ? TwoHandedThrustSpeedCap : OneHandedThrustSpeedCap;
            thrustSpeed = MathF.Min(thrustSpeed, speedCap);

            // RBM doubles the skill modifier here, and feeds it the diminished skill rather than the raw one.
            float skillModifier = MBMath.ClampFloat(skillDR / 250f, 0f, 1f) * 2f;

            float armStrength = twoHanded ? TwoHandedArmStrength : OneHandedArmStrength;
            float cap = twoHanded ? TwoHandedThrustCap : OneHandedThrustCap;

            // The one-handed strength carries the shaft's weight; the two-handed does not (Utilities.cs:1332, 1375).
            float thrustStrength = armStrength * (1f + skillModifier);
            if (!twoHanded)
            {
                thrustStrength += weaponWeight;
            }

            float energyCap = MBMath.ClampFloat(0.5f * thrustStrength * thrustSpeed * thrustSpeed * 1.5f, 0f, cap);
            float energy = (0.5f * thrustStrength * thrustSpeed * thrustSpeed)
                         + (0.5f * weaponWeight * thrustSpeed * thrustSpeed);

            energy = MathF.Min(energy, energyCap);

            // The shaft's own kinetic energy wins if it is the greater -- a heavy spear carries through.
            float shaftEnergy = 0.5f * weaponWeight * thrustSpeed * thrustSpeed;
            if (shaftEnergy > energy)
            {
                energy = shaftEnergy;
            }
            return MathF.Min(energy, energyCap);
        }

        // ---------------------------------------------------------------------------------------------------
        // Missiles
        // ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// What an arrow lands with. RBM makes a shot a product of two things: the kinetic energy the shaft
        /// carries, and how good a head is on it. The bow contributes NEITHER directly -- it contributes SPEED,
        /// out of its draw weight, and the shaft's own weight does the rest.
        ///
        ///     speed  = sqrt(2 * (draw * 4.448 * powerstroke * efficiency / 2) / (ammoWeight + virtualArrow))
        ///     energy = 0.5 * ammoWeight * speed^2                (capped: weight x 2250 / 2500 / 3000)
        ///     shot   = energy * (bowThrustDamage / 100)
        ///
        /// Skill does not enter a bow's damage at all in RBM -- it buys reload speed and nothing else. A sling
        /// and a throwing arm are different, and skill does reach their speed; that is left for the volley work.
        /// </summary>
        private static float GetMissileMagnitude(WeaponComponentData launcher, WeaponComponentData ammo, float ammoWeight, int skill)
        {
            // RBM repurposes MissileSpeed as the DRAW WEIGHT in pounds for a bow, and as the sling's length for a
            // sling (RangedWeaponStats.drawWeight).
            float drawWeight = launcher.MissileSpeed;
            if (drawWeight <= 0f || ammoWeight <= 0f)
            {
                return 0f;
            }

            bool sling = launcher.WeaponClass == WeaponClass.Sling;
            bool crossbow = launcher.WeaponClass == WeaponClass.Crossbow;

            float speed;
            if (sling)
            {
                // A sling is not a bow and does not obey a bow's law. It stores nothing: the whole of the shot is
                // the arm whirling it, so the man's own training reaches the speed -- which is true of nothing a
                // bowman does (Utilities.assignSlingMissileSpeed).
                float weightModifier = 730f * (1f + (EffectiveSkill(skill) / 100f));
                float slingLength = drawWeight * 0.01f;
                float energyStored = MBMath.ClampFloat(ammoWeight * weightModifier * slingLength, 60f, 350f);
                speed = MathF.Sqrt((energyStored * 2f) / ammoWeight);
            }
            else
            {
                // RBM branches on the ITEM USAGE string, not the weapon class, and a longbow is its own case: a
                // stave of yew wastes more of the draw than a composite horn-and-sinew bow does (0.835 against
                // 0.90) and throws heavier limbs along with the shaft. Reading only the class made every longbow
                // in Calradia a horsebow -- and most of RBM's bows are longbows.
                bool longBow = !crossbow && launcher.ItemUsage == "long_bow";

                float powerstroke = crossbow ? CrossbowPowerstroke : BowPowerstroke;
                float efficiency = crossbow ? 0.88f : (longBow ? 0.835f : 0.90f);

                float potentialEnergy = 0.5f * (drawWeight * PoundsToNewtons) * powerstroke * efficiency;

                // The bow throws a little of itself with the shaft; RBM calls it the virtual arrow.
                float virtualArrow = drawWeight * (longBow ? 0.00018f : 0.00015f);
                float launchedWeight = ammoWeight + virtualArrow;
                if (launchedWeight <= 0f)
                {
                    return 0f;
                }
                speed = MathF.Sqrt((2f * potentialEnergy) / launchedWeight);
            }

            float energy = 0.5f * ammoWeight * speed * speed;
            energy = MathF.Min(energy, ammoWeight * GetMissileEnergyCap(ammo.WeaponClass));

            // The head on the shaft: RBM computes (missileTotalDamage - 100) * 0.01, and it hands
            // CalculateMissileMagnitude `ammo.ThrustDamage + 100`. So the hundred it subtracts is a CONSTANT, and
            // what survives the subtraction is the AMMO's own damage -- which is the whole point, because that is
            // the only number that tells a bodkin from a broadhead.
            //
            // I had this reading the LAUNCHER's thrust damage, and every bow, crossbow and sling in RBM's XML is
            // thrust_damage="100" without exception. So the term was a hard 1.0 for every shot in the game, and
            // arrow quality did not exist: a Greased Steel Bodkin (135) and a plain arrow (100) were the same shot.
            float head = ammo.ThrustDamage * 0.01f;
            if (head <= 0f)
            {
                return 0f;
            }

            // And what the shot has left when it arrives, which is not what it left the string with.
            return energy * head * MissileMomentumRemaining;
        }

        /// <summary>RBM caps a shot's energy in proportion to what it is throwing (MagnitudeChanges.cs:345-370).</summary>
        private static float GetMissileEnergyCap(WeaponClass ammoClass)
        {
            switch (ammoClass)
            {
                case WeaponClass.Bolt:
                    return BoltEnergyCap;

                // A sling's ammunition is WeaponClass.SlingStone. WeaponClass.Stone is the rock a looter picks up
                // and hurls by hand, which RBM does not cap at all -- so keying this on Stone meant no sling stone
                // in the game ever met its own cap, and they all fell through to the arrow's.
                case WeaponClass.SlingStone:
                    return SlingStoneEnergyCap;

                default:
                    return ArrowEnergyCap;
            }
        }

        /// <summary>
        /// A javelin or a throwing axe: the arm throws it, so the shot is pure kinetic energy -- the listed damage
        /// on the item is not read at all, exactly as with every other RBM weapon. Mirrors Utilities.calculateThrowableSpeed
        /// and the thrown branches of MagnitudeChanges.CalculateMissileMagnitude.
        ///
        /// RBM's menu adds throwableCorrectionSpeed to the speed and CalculateMissileMagnitude subtracts it again,
        /// so the two cancel and the physics runs on the raw thrown speed -- floored at 5 m/s, as it floors it.
        /// </summary>
        private static float GetThrownMagnitude(WeaponComponentData weapon, float weight, int skill)
        {
            if (weight <= 0f)
            {
                return 0f;
            }

            // Utilities.calculateThrowableSpeed: the arm's energy, clamped by the weight it is throwing.
            float energy = MBMath.ClampFloat(weight * 70f, 60f, 250f) + (EffectiveSkill(skill) * 0.75f);
            float speed = MathF.Max(MathF.Sqrt((2f * energy) / weight), 5f);

            float physical = 0.5f * weight * speed * speed;

            WeaponClass weaponClass = weapon.WeaponClass;

            // Only a spear-shaped thing has its energy capped -- a javelin, or a one-handed polearm flung as one.
            // A throwing axe and a throwing knife are uncapped in RBM, and must be here too.
            if (weaponClass == WeaponClass.Javelin
                || weaponClass == WeaponClass.OneHandedPolearm
                || weaponClass == WeaponClass.LowGripPolearm)
            {
                physical = MathF.Min(physical, weight * ThrownSpearEnergyCap);
            }

            physical *= ThrownMomentumRemaining;

            // ---------------------------------------------------------------------------------------------------
            // DO NOT "FIX" THE MISSING ThrustMagnitudeModifier HERE. It is missing on purpose.
            //
            // RBMConfig sets OneHandedThrustDamageBonus = 1f / ThrustMagnitudeModifier -- they are exact
            // reciprocals, 0.05 and 20, and they are two halves of ONE scaling. CalculateMissileMagnitude multiplies
            // a Pierce throw by the 0.05; RBMComputeDamage then multiplies every Pierce magnitude by the 20. They
            // cancel, exactly, and the number the armour equation actually sees is the raw energy.
            //
            // This model works in that cancelled scale -- see the melee thrust above, which is raw GetThrustEnergy
            // with neither factor applied -- so a throw must be raw energy too. Applying the 0.05 alone (which I did,
            // "correcting" it against CalculateMissileMagnitude in isolation) divided every javelin in Calradia by
            // twenty and put a Mahagony Throwing Lance at magnitude 5.62, against 51.8 for the same man's sword.
            //
            // What survives below is only what does NOT cancel.
            // ---------------------------------------------------------------------------------------------------
            switch (weaponClass)
            {
                case WeaponClass.ThrowingKnife:
                case WeaponClass.Dagger:
                    // RBM pays these 0.6 of the rest, on top of the pair that cancels.
                    physical *= 0.6f;
                    break;

                case WeaponClass.Javelin:
                    // A javelin that is neither Pierce nor Cut is paid half. Pierce and Cut both come out at 1.0:
                    // Pierce because the 0.05 and the 20 cancel, Cut because RBM applies neither to it.
                    if (weapon.ThrustDamageType != DamageTypes.Pierce && weapon.ThrustDamageType != DamageTypes.Cut)
                    {
                        physical *= 0.5f;
                    }
                    break;

                case WeaponClass.Stone:
                case WeaponClass.Boulder:
                    // A rock picked up off the ground is the ONE thrown thing RBM still prices on its listed damage.
                    // None of the class overrides below the assembly line fire for it, so it keeps the general
                    // `physical * missileTotalDamage * momentum`, and for a stone missileTotalDamage is its printed
                    // damage over a hundred -- about a third. Missing that made a looter's rock (74.91) very nearly
                    // the equal of a Mahagony Throwing Lance (112.36), which it is not.
                    physical *= weapon.ThrustDamage * 0.01f;
                    break;
            }

            return physical;
        }

        // ---------------------------------------------------------------------------------------------------
        // Choosing the blow
        // ---------------------------------------------------------------------------------------------------

        /// <summary>The blow this weapon lands, priced RBM's way. Returns false for a thing that cannot strike.</summary>
        internal static bool GetMeleeProfile(ItemObject item, WeaponComponentData weapon, int skill, out WeaponProfile profile)
        {
            profile = default(WeaponProfile);

            float best = 0f;
            DamageTypes bestType = DamageTypes.Blunt;
            bool bestIsThrust = false;

            float skillModifier = SkillModifier(skill);
            float skillDR = EffectiveSkill(skill);

            // The swing: its real physics, then RBM's clamp -- which is the point. A blow lands where the clamp
            // lets it, and that is somewhere BETWEEN the floor and the ceiling for anything but the very best or
            // the very worst weapon. Assuming the ceiling made every sword in Calradia swing identically.
            if (weapon.SwingDamage > 0)
            {
                float c, min, max, scale;
                if (GetMeleeClamp(weapon.WeaponClass, weapon.SwingDamageType, out c, out min, out max, out scale))
                {
                    float physics = GetSwingMagnitude(weapon, item.Weight, skill);
                    float value = physics + (skillDR * c);
                    float swing = MBMath.ClampFloat(value, min * (1f + skillModifier), max * (1f + (2f * skillModifier))) * scale;
                    if (swing > best)
                    {
                        best = swing;
                        bestType = weapon.SwingDamageType;
                        bestIsThrust = false;
                    }
                }
            }

            // The thrust. A Pierce thrust is raw energy and no clamp at all; anything else goes through the same
            // clamp a swing does, on the thrust's own physics.
            if (weapon.ThrustDamage > 0)
            {
                float thrust = 0f;
                if (weapon.ThrustDamageType == DamageTypes.Pierce)
                {
                    thrust = GetThrustEnergy(weapon, item.Weight, skill);
                }
                else
                {
                    float c, min, max, scale;
                    if (GetMeleeClamp(weapon.WeaponClass, weapon.ThrustDamageType, out c, out min, out max, out scale))
                    {
                        float physics = GetThrustEnergy(weapon, item.Weight, skill);
                        float value = physics + (skillDR * c);
                        thrust = MBMath.ClampFloat(value, min * (1f + skillModifier), max * (1f + (2f * skillModifier))) * scale;
                    }
                }
                if (thrust > best)
                {
                    best = thrust;
                    bestType = weapon.ThrustDamageType;
                    bestIsThrust = true;
                }
            }

            if (best <= 0f)
            {
                return false;
            }

            profile.Magnitude = best;
            profile.DamageType = bestType;
            profile.WeaponType = weapon.WeaponClass.ToString();
            profile.DamageFactor = GetDamageFactor(weapon, bestIsThrust);
            profile.Skill = weapon.RelevantSkill;
            profile.IsMissile = false;
            profile.IsValid = true;
            return true;
        }

        /// <summary>The shot this bow looses with this shaft.</summary>
        internal static bool GetMissileProfile(ItemObject launcherItem, WeaponComponentData launcher,
            ItemObject ammoItem, WeaponComponentData ammo, int skill, out WeaponProfile profile)
        {
            profile = default(WeaponProfile);
            if (launcher == null || ammo == null || ammoItem == null)
            {
                return false;
            }

            float magnitude = GetMissileMagnitude(launcher, ammo, ammoItem.Weight, skill);
            if (magnitude <= 0f)
            {
                return false;
            }

            profile.Magnitude = magnitude;
            // The head is what lands, so it is the head's kind of wound and the head's factors that answer.
            profile.DamageType = ammo.ThrustDamageType;
            profile.WeaponType = ammo.WeaponClass.ToString();
            profile.DamageFactor = GetDamageFactor(ammo, isThrust: true);
            // But the training is the bow's: no one is trained in arrows.
            profile.Skill = launcher.RelevantSkill;
            profile.IsMissile = true;
            profile.IsValid = true;
            return true;
        }

        /// <summary>A javelin or a throwing axe, which carries its own head and its own arm.</summary>
        internal static bool GetThrownProfile(ItemObject item, WeaponComponentData weapon, int skill, out WeaponProfile profile)
        {
            profile = default(WeaponProfile);
            float magnitude = GetThrownMagnitude(weapon, item.Weight, skill);
            if (magnitude <= 0f)
            {
                return false;
            }

            // A throwing axe is never THRUST with, so its thrust damage type is literally Invalid in the item data.
            // It is a Cut when it lands, and its swing type says so. Reading the thrust type blindly is what fed an
            // unset damage type into the armour equation and left three troop types priced on nonsense.
            DamageTypes damageType = weapon.ThrustDamageType;
            if (damageType == DamageTypes.Invalid)
            {
                damageType = weapon.SwingDamageType;
            }

            profile.Magnitude = magnitude;
            profile.DamageType = damageType;
            profile.WeaponType = weapon.WeaponClass.ToString();
            profile.DamageFactor = GetDamageFactor(weapon, isThrust: true);
            profile.Skill = weapon.RelevantSkill;
            profile.IsMissile = true;
            profile.IsValid = true;
            return true;
        }

        /// <summary>
        /// A weapon's quality, and the ONE thing RBM does with it: sqrt of its damage factor, which then divides
        /// the armour threshold. This -- not force -- is what separates a peasant's sickle from a knight's blade.
        /// </summary>
        private static float GetDamageFactor(WeaponComponentData weapon, bool isThrust)
        {
            float factor = isThrust ? weapon.ThrustDamageFactor : weapon.SwingDamageFactor;
            if (factor <= 0f)
            {
                return 1f;
            }
            return MathF.Sqrt(factor);
        }

        // ---------------------------------------------------------------------------------------------------
        // The armour equation
        // ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// What this blow lands through that armour, by RBM's own equation (Utilities.WeaponTypeDamage), with
        /// RBM's own per-weapon factors -- read from the config it actually runs on, rather than guessed at.
        /// The armour reduction touches ONLY the blunt trauma; what penetrates is a flat threshold subtraction.
        /// </summary>
        internal static float RbmDamage(WeaponProfile profile, float armor, bool victimIsPlate)
        {
            float magnitude = profile.Magnitude;
            if (magnitude <= 0f)
            {
                return 0f;
            }

            float armorEffectiveness = armor;

            // A mail coat is poor answer to an arrow: RBM halves it, for everything but plate.
            if (!victimIsPlate && profile.DamageType == DamageTypes.Pierce
                && (profile.WeaponType.Contains("Arrow") || profile.WeaponType.Contains("Bolt")))
            {
                armorEffectiveness *= 0.5f;
            }

            float armorReduction = 100f / (100f + (armorEffectiveness * RBMConfig.RBMConfig.armorMultiplier));
            float thresholdModifier = RBMConfig.RBMConfig.armorThresholdModifier / MathF.Max(profile.DamageFactor, 0.01f);

            RBMCombatConfigWeaponType f = RBMConfig.RBMConfig.getWeaponTypeFactors(profile.WeaponType);
            float thresholdCut = (f != null) ? f.ExtraArmorThresholdFactorCut : 5f;
            float thresholdPierce = (f != null) ? f.ExtraArmorThresholdFactorPierce : 3f;
            float carryCut = (f != null) ? f.ExtraBluntFactorCut : 0.25f;
            float carryPierce = (f != null) ? f.ExtraBluntFactorPierce : 0.35f;
            float bluntBonus = RBMConfig.RBMConfig.bluntTraumaBonus;

            switch (profile.DamageType)
            {
                case DamageTypes.Blunt:
                    {
                        // A blunt blow's threshold is a hardcoded five, and its carry a flat seven tenths.
                        float penetrated = MathF.Max(0f, magnitude - (armorEffectiveness * 5f * thresholdModifier));
                        float stopped = (magnitude - penetrated) / magnitude;
                        float trauma = magnitude * (0.7f * RBMConfig.RBMConfig.maceBluntModifier) * stopped * armorReduction;
                        return penetrated + MathF.Max(0f, trauma);
                    }

                case DamageTypes.Pierce:
                    {
                        // A point finds a gap even in harness: a little always goes in, capped at fifteen. Maces
                        // and arrows have no such gap to find (their partial threshold is nought), so for them
                        // the partial term is the whole magnitude -- which is why the cap matters so much.
                        float partialThreshold = GetPartialPenetrationThreshold(profile.WeaponType);
                        float partial = MathF.Max(0f, magnitude - (armorEffectiveness * partialThreshold * thresholdModifier));
                        if (partial > 15f)
                        {
                            partial = 15f;
                        }
                        float penetrated = MathF.Max(0f, magnitude - (armorEffectiveness * thresholdPierce * thresholdModifier)) - partial;
                        float stopped = (magnitude - (penetrated + partial)) / magnitude;
                        penetrated += partial;
                        float trauma = magnitude * (carryPierce + bluntBonus) * stopped * armorReduction;
                        return MathF.Max(0f, penetrated) + MathF.Max(0f, trauma);
                    }

                default:
                    {
                        float penetrated = MathF.Max(0f, magnitude - (armorEffectiveness * thresholdCut * thresholdModifier));
                        float stopped = (magnitude - penetrated) / magnitude;
                        float trauma = magnitude * (carryCut + bluntBonus) * stopped * armorReduction;
                        return penetrated + MathF.Max(0f, trauma);
                    }
            }
        }

        /// <summary>RBM gives a mace, an arrow, a bolt and a sling-stone no partial penetration at all (Utilities.cs:1142-1175).</summary>
        private static float GetPartialPenetrationThreshold(string weaponType)
        {
            switch (weaponType)
            {
                case "Mace":
                case "Arrow":
                case "Bolt":
                case "SlingStone":
                case "Stone":
                    return 0f;

                default:
                    return 2f;
            }
        }

        /// <summary>
        /// Vanilla's armour equation (DefaultStrikeMagnitudeModel.ComputeRawDamage), for when RBM Combat is off
        /// and the battles being fought are native ones. Here the listed damage IS the damage, so it is used.
        /// </summary>
        internal static float VanillaDamage(float magnitude, float armor, DamageTypes damageType)
        {
            if (magnitude <= 0f)
            {
                return 0f;
            }
            float reduced = magnitude * (50f / (50f + armor));

            float thresholdFactor;
            float bluntFactor;
            switch (damageType)
            {
                case DamageTypes.Pierce:
                    thresholdFactor = 0.33f;
                    bluntFactor = 0.25f;
                    break;

                case DamageTypes.Blunt:
                    thresholdFactor = 0.2f;
                    bluntFactor = 0.6f;
                    break;

                default:
                    thresholdFactor = 0.5f;
                    bluntFactor = 0.1f;
                    break;
            }

            float penetrated = MathF.Max(0f, reduced - (armor * thresholdFactor));
            return (bluntFactor * reduced) + ((1f - bluntFactor) * penetrated);
        }
    }
}
