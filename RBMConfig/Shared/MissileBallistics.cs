using System;
using System.Collections.Generic;

namespace RBMConfig
{
    /// <summary>
    /// Launcher missile speed and missile reach, shared by RBMCombat (the real launch, and its aim trace log) and
    /// RBMAI (the crossbow reach gate). Lives here because RBMConfig is the one project both reference; RBMAI does
    /// not reference RBMCombat. Retune launch speed here, never re-copy it into a module.
    /// </summary>
    public static class MissileBallistics
    {
        // Per-projectile weight is weight/amount, so a zero-amount stack or weightless ammo gives
        // NaN/Infinity/0 here. The speed math then divides by it and (int)NaN/(int)Infinity is
        // int.MinValue, which ends up in the weapon stats the native engine reads.
        public static float SanitizeAmmoWeight(float ammoWeight)
        {
            return (ammoWeight > 0f && !float.IsInfinity(ammoWeight)) ? ammoWeight : 0.07f;
        }

        public static int CalculateMissileSpeed(float ammoWeight, string rangedWeaponType, int drawWeight)
        {
            ammoWeight = SanitizeAmmoWeight(ammoWeight);
            int calculatedMissileSpeed = 10;
            switch (rangedWeaponType)
            {
                case "bow": // composite horn-sinew horsebow
                    {
                        float powerstroke = (25f * 0.0254f); // in inches then converted to metres
                        float materialEfficiency = 0.9f; // composite > wood > steel
                        double potentialEnergy = 0.5f * (drawWeight * 4.448f) * powerstroke * materialEfficiency; // draw weight is in pounds and then multiplied to newton metres
                        float virtualArrow = drawWeight * 0.00015f;// virtual arrow - weight of limbs and string added to the weight of the arrow
                        ammoWeight += virtualArrow;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;
                    }
                case "long_bow":
                    {
                        float powerstroke = (25f * 0.0254f); // in inches then converted to metres
                        float materialEfficiency = 0.835f; // composite > wood > steel
                        double potentialEnergy = 0.5f * (drawWeight * 4.448f) * powerstroke * materialEfficiency; // draw weight is in pounds and then multiplied to newton metres
                        float virtualArrow = drawWeight * 0.00018f;// virtual arrow - weight of limbs and string added to the weight of the arrow
                        ammoWeight += virtualArrow;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;
                    }
                case "crossbow": // composite horn-sinew crossbow
                case "crossbow_fast":
                case "crossbow_light": // vanilla light crossbows
                    {
                        // Eastern / Chinese style crossbows
                        float powerstroke = (20f * 0.0254f); // in inches then converted to metres
                        float materialEfficiency = 0.88f; // composite bow + bit of drag
                        double potentialEnergy = 0.5f * (drawWeight * 4.448f) * powerstroke * materialEfficiency; // draw weight is in pounds and then multiplied to newton metres
                        float virtualArrow = drawWeight * 0.00015f;// virtual arrow - weight of limbs and string added to the weight of the arrow
                        ammoWeight += virtualArrow;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;

                        //European crossbows
                        //float powerstroke = (8f * 0.0254f); // in inches then converted to metres
                        //float materialEfficiency = 0.85f; // composite > wood > steel
                        //double potentialEnergy = 0.5f * (drawWeight * 4.448f) * powerstroke * materialEfficiency; // draw weight is in pounds and then multiplied to newton metres
                        //float virtualArrow = drawWeight * 0.00014f;// virtual arrow - weight of limbs and string added to the weight of the arrow
                        //ammoWeight += virtualArrow;
                        //calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        //break;
                    }
                // case "Sling":
                // float weightModifier  = 730f * (1f + throwing skill / 100); takze pri 100 skille to bude * 2
                // float slingLengthModifier  = missile_speed (zo slingu, z tej equipnutej zbrane) * 0.01;
                // int calculatedThrowingSpeed = (int)Math.Ceiling(Math.Sqrt((MBMath.ClampFloat(ammoWeight * weightModifier * slingLengthModifier, 60f, 350f)) * 2f / ammoWeight));
                // return calculatedThrowingSpeed;
                // tie modifiery zo stitu a armoru ktore vplivaju na normalny throw sa mozu aplikovat aj tu
                case "osa_sling":
                    {
                        // 40 grams is added to the weight of projectiles, this results in 60 m/s at 80 grams with good sling, 70 m/s at 50 grams and some 80 ms at 30 grams
                        double potentialEnergy = 0.5f * (drawWeight * drawWeight) * 0.12f;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight + 0.04f)));
                        break;
                    }
                case "cla_musket":
                    {
                        // arquebus , ammo should weight 40g+, kinetic energy should be 1300-1750J
                        double potentialEnergy = drawWeight;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;
                    }
                case "cla_flint_rifle":
                    {
                        // flintlock musket , ammo should weight 18g-25g, kinetic energy should be 2300-3000J
                        double potentialEnergy = drawWeight;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;
                    }
                case "cla_pistol":
                    {
                        // early flintlock pistol , ammo should weight 14g, kinetic energy should be 700+J
                        double potentialEnergy = drawWeight;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;
                    }
                case "cla_revolver":
                    {
                        // wild west revolver , ammo should weight 16.5g, kinetic energy should be 850+J
                        double potentialEnergy = drawWeight;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;
                    }
                case "cla_cannon":
                    {
                        // early hand cannon , ammo should weight 50g, kinetic energy should be 300-500J
                        double potentialEnergy = drawWeight;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;
                    }
                case "cla_bolt_rifle":
                    {
                        // early bolt action , ammo should weight 25g, kinetic energy should be 5000J
                        double potentialEnergy = drawWeight;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / (ammoWeight)));
                        break;
                    }
                case "cla_bomb":
                    {
                        // Just a throw
                        double potentialEnergy = 150f;
                        calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt((potentialEnergy * 2f) / ammoWeight));
                        break;
                    }

                default:
                    {
                        // Unknown (vanilla/modded) usage: its missile_speed is already a launch speed
                        // in m/s, so keep it rather than firing at a crippling 10 m/s.
                        calculatedMissileSpeed = drawWeight;
                        break;
                    }
            }
            return calculatedMissileSpeed;
        }

        private const float Gravity = 9.806f;
        private const float StepSeconds = 0.005f;
        private const float MaxFlightSeconds = 20f;

        private static readonly object _reachLock = new object();
        // (speed, rounded height difference, friction) -> reach. Speeds are ints and heights round to the metre,
        // so a battle fills only a few dozen entries.
        private static readonly Dictionary<long, float> _reachCache = new Dictionary<long, float>();

        /// <summary>
        /// Horizontal distance at which a missile launched at (vHorizontal, vUp) crosses heightDifference metres above
        /// its launch point on the way down, under gravity and the engine's quadratic drag (speed -= k * speed^2 * dt,
        /// per the comment in Native managed_core_parameters.xml). -1 if it never comes down to that height descending.
        /// </summary>
        public static float RangeAtHeight(float vHorizontal, float vUp, float heightDifference, float airFriction)
        {
            float x = 0f, z = 0f, vx = vHorizontal, vz = vUp;
            int steps = (int)(MaxFlightSeconds / StepSeconds);
            for (int i = 0; i < steps; i++)
            {
                float speed = (float)Math.Sqrt(vx * vx + vz * vz);
                float px = x, pz = z;
                float drag = airFriction * speed * StepSeconds;
                vx -= vx * drag;
                vz -= vz * drag;
                vz -= Gravity * StepSeconds;
                x += vx * StepSeconds;
                z += vz * StepSeconds;
                if (vz < 0f && z <= heightDifference)
                {
                    float t = (pz - heightDifference) / (pz - z);
                    return px + (x - px) * t;
                }
            }
            return -1f;
        }

        /// <summary>
        /// The farthest a missile launched at this speed can carry to a point heightDifference metres above (or below)
        /// its launch point, at the best elevation. Cached.
        /// </summary>
        public static float MaxReach(int speed, float heightDifference, float airFriction)
        {
            if (speed <= 0)
            {
                return 0f;
            }
            int dz = (int)Math.Round(heightDifference);
            long key = ((long)speed << 40) ^ ((long)(dz + 100000) << 20) ^ (long)(airFriction * 1000000f);
            lock (_reachLock)
            {
                if (_reachCache.TryGetValue(key, out float cached))
                {
                    return cached;
                }
            }
            float best = 0f;
            for (int deg = 0; deg <= 60; deg++)
            {
                double rad = deg * Math.PI / 180.0;
                float r = RangeAtHeight((float)(Math.Cos(rad) * speed), (float)(Math.Sin(rad) * speed), dz, airFriction);
                if (r > best)
                {
                    best = r;
                }
            }
            lock (_reachLock)
            {
                _reachCache[key] = best;
            }
            return best;
        }
    }
}
