using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMConfig
{
    /// <summary>
    /// Launcher missile speed and missile reach, shared by RBMCombat (the real launch, the launch speed the engine aims
    /// with, and the aim trace log) and RBMAI (the bow, crossbow and sling reach gate). Lives here because RBMConfig is
    /// the one project both reference; RBMAI does not reference RBMCombat. Retune launch speed here, never re-copy it
    /// into a module.
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

        /// <summary>
        /// The speed RBMCombat's shot prefix will give a bow/crossbow missile: same formula, draw weight + modifier bonus,
        /// and the per-missile weight of the loaded arrow/bolt, else of the first stack of the usage's ammo class.
        /// 0 when the agent carries no ammo for it. drawWeight is passed in because the launcher's own MissileSpeed holds
        /// the real launch speed, not the draw weight, inside RBMCombat's spawn and shot patches.
        /// </summary>
        public static int GetLauncherSpeed(Agent agent, MissionWeapon launcher, WeaponComponentData usage, int drawWeight)
        {
            float ammoWeight = GetLauncherAmmoWeight(agent, launcher, usage);
            if (ammoWeight <= 0f)
            {
                return 0;
            }
            return CalculateMissileSpeed(ammoWeight, usage.ItemUsage, drawWeight + GetLauncherModifierBonus(launcher));
        }

        /// <summary>
        /// Per-missile weight of the loaded arrow/bolt/stone, else of the first stack of the usage's ammo class; 0 if none.
        /// </summary>
        public static float GetLauncherAmmoWeight(Agent agent, MissionWeapon launcher, WeaponComponentData usage)
        {
            MissionWeapon loaded = launcher.AmmoWeapon;
            if (!loaded.IsEmpty && loaded.Item != null && loaded.Amount > 0)
            {
                return loaded.GetWeight() / loaded.Amount;
            }
            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
            {
                MissionWeapon ammo = agent.Equipment[i];
                if (!ammo.IsEmpty && ammo.Item != null && ammo.Amount > 0 && ammo.CurrentUsageItem != null && ammo.CurrentUsageItem.WeaponClass == usage.AmmoClass)
                {
                    return ammo.GetWeight() / ammo.Amount;
                }
            }
            return 0f;
        }

        /// <summary>The launcher's item modifier as a draw weight bonus, as the shot prefix applies it.</summary>
        public static int GetLauncherModifierBonus(MissionWeapon launcher)
        {
            return launcher.ItemModifier != null ? launcher.ItemModifier.ModifyHitPoints(50) - 50 : 0;
        }

        public static int SlingSpeed(float ammoWeight, int drawWeight, float effectiveSkillDR, float armorModifier, WeaponClass shieldType)
        {
            ammoWeight = SanitizeAmmoWeight(ammoWeight);
            // Shield penalty: a shield on the arm restricts the slinging motion.
            float shieldTypeModifier = 1f;
            switch (shieldType)
            {
                case WeaponClass.LargeShield:
                    shieldTypeModifier = 0.87f;
                    break;
                case WeaponClass.SmallShield:
                    shieldTypeModifier = 0.96f;
                    break;
            }

            // Armor on shoulders and arms reduces sling rotation speed, same as for throws.
            float weightTraining = MBMath.ClampFloat(effectiveSkillDR * 0.001f, 0f, 0.2f);
            float equipmentWeightModifier = (float)Math.Sqrt(MBMath.ClampFloat(1f - (armorModifier * 0.005f) + weightTraining, 0.7f, 1f));

            // From the design formula in calculateMissileSpeed:
            // weightModifier = 730 * (1 + skill/100)  → at 100 skill it doubles
            // slingLengthModifier = missile_speed * 0.01  (item MissileSpeed stat encodes cord length/quality)
            // KE = ammoWeight * weightModifier * slingLengthModifier, clamped to [60, 350] J
            // v = sqrt(2 * KE / ammoWeight)
            float weightModifier = 730f * (1f + (effectiveSkillDR / 100f));
            float slingLengthModifier = drawWeight * 0.01f;
            int calculatedSpeed = (int)Math.Ceiling(Math.Sqrt((MBMath.ClampFloat(ammoWeight * weightModifier * slingLengthModifier, 60f, 350f)) * 2f / ammoWeight));

            return (int)Math.Round(calculatedSpeed * shieldTypeModifier * equipmentWeightModifier);
        }

        public static float EffectiveSkillWithDR(int effectiveSkill)
        {
            return (600f / (600f + effectiveSkill)) * (float)effectiveSkill;
        }

        public static float ShoulderArmor(Agent agent)
        {
            float num = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];

                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.Cape)
                {
                    num += (float)equipmentElement.GetModifiedBodyArmor();
                    num += (float)equipmentElement.GetModifiedArmArmor();
                }
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.BodyArmor)
                {
                    num += (float)equipmentElement.GetModifiedArmArmor();
                }
            }
            return num;
        }

        public static float ArmArmor(Agent agent)
        {
            float num = 0f;
            for (EquipmentIndex equipmentIndex = EquipmentIndex.NumAllWeaponSlots; equipmentIndex < EquipmentIndex.ArmorItemEndSlot; equipmentIndex++)
            {
                EquipmentElement equipmentElement = agent.SpawnEquipment[equipmentIndex];
                if (equipmentElement.Item != null && equipmentElement.Item.ItemType == ItemObject.ItemTypeEnum.HandArmor)
                {
                    num += (float)equipmentElement.GetModifiedArmArmor();
                }
            }
            return num;
        }

        /// <summary>
        /// The speed RBMCombat gives a sling stone, at equip (what the engine aims with) and on the shot: the sling
        /// formula from the per-stone weight (as GetLauncherAmmoWeight), the shooter's effective skill, his shield and his
        /// shoulder and arm armor, and draw weight + modifier bonus. 0 when the agent carries no stones. drawWeight is
        /// passed in for the same reason as in GetLauncherSpeed.
        /// </summary>
        public static int GetSlingSpeed(Agent agent, MissionWeapon launcher, WeaponComponentData usage, int drawWeight)
        {
            float ammoWeight = GetLauncherAmmoWeight(agent, launcher, usage);
            if (ammoWeight <= 0f)
            {
                return 0;
            }
            SkillObject skill = launcher.Item == null ? DefaultSkills.Athletics : launcher.Item.RelevantSkill;
            int effectiveSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(agent, skill);
            float effectiveSkillDR = EffectiveSkillWithDR(effectiveSkill);

            WeaponClass shieldType = WeaponClass.Undefined;
            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
            {
                if (agent.Equipment != null && !agent.Equipment[i].IsEmpty && agent.Equipment[i].IsShield())
                {
                    shieldType = agent.Equipment[i].CurrentUsageItem.WeaponClass;
                }
            }
            float armorModifier = MBMath.ClampFloat(ShoulderArmor(agent) - 20f, 0f, 100f) + MBMath.ClampFloat(ArmArmor(agent) - 20f, 0f, 100f);

            return SlingSpeed(ammoWeight, drawWeight + GetLauncherModifierBonus(launcher), effectiveSkillDR, armorModifier, shieldType);
        }

        public const float Gravity = 9.806f;
        // Integration step of every flight model here; SampleTrajectory callers size their sampling in these steps.
        public const float StepSeconds = 0.005f;
        private const float MaxFlightSeconds = 20f;

        private static readonly object _reachLock = new object();
        // (speed, rounded height difference, friction) -> reach. Speeds are ints and heights round to the metre, but
        // speeds differ per ammo, launcher, modifier and slinger, so hilly battles fill hundreds to thousands of entries.
        private static readonly Dictionary<long, float> _reachCache = new Dictionary<long, float>();

        /// <summary>
        /// Horizontal distance at which a missile launched at (vHorizontal, vUp) crosses heightDifference metres above
        /// its launch point on the way down, under gravity and the engine's quadratic drag (speed -= k * speed^2 * dt,
        /// per the comment in Native managed_core_parameters.xml). -1 if it never comes down to that height descending.
        /// </summary>
        public static float RangeAtHeight(float vHorizontal, float vUp, float heightDifference, float airFriction)
        {
            float x = 0f, z = 0f, vx = vHorizontal, vy = 0f, vz = vUp;
            int steps = (int)(MaxFlightSeconds / StepSeconds);
            for (int i = 0; i < steps; i++)
            {
                float px = x, pz = z;
                StepVelocity(ref vx, ref vy, ref vz, airFriction);
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

        // One integration step of the flight velocity: quadratic drag on the current speed, then gravity. Position is
        // advanced by the caller with the NEW velocity (semi-implicit Euler), which is what was verified in game.
        private static void StepVelocity(ref float vx, ref float vy, ref float vz, float airFriction)
        {
            float speed = (float)Math.Sqrt(vx * vx + vy * vy + vz * vz);
            float drag = airFriction * speed * StepSeconds;
            vx -= vx * drag;
            vy -= vy * drag;
            vz -= vz * drag;
            vz -= Gravity * StepSeconds;
        }

        /// <summary>Stops a sampled trajectory: true if the segment from..to hits something, with the hit point.</summary>
        public delegate bool TrajectorySegmentTest(Vec3 from, Vec3 to, out Vec3 hit);

        /// <summary>
        /// World-space points along a missile's flight from start at velocity, under the same integrator as RangeAtHeight.
        /// points[0] is the launch point, then one point every stepsPerSample integration steps (StepSeconds each). Each
        /// new segment is handed to segmentTest; on a hit the hit point is the last point and landed is true. Otherwise it
        /// stops at maxSeconds of flight, maxDrop metres below the launch height, or when points is full. Returns the count.
        /// </summary>
        public static int SampleTrajectory(Vec3 start, Vec3 velocity, float airFriction, int stepsPerSample, float maxSeconds, float maxDrop, Vec3[] points, TrajectorySegmentTest segmentTest, out bool landed)
        {
            landed = false;
            if (points == null || points.Length == 0)
            {
                return 0;
            }
            points[0] = start;
            int count = 1;
            float x = start.x, y = start.y, z = start.z;
            float vx = velocity.x, vy = velocity.y, vz = velocity.z;
            int steps = (int)(Math.Min(maxSeconds, MaxFlightSeconds) / StepSeconds);
            if (stepsPerSample < 1)
            {
                stepsPerSample = 1;
            }
            for (int i = 1; i <= steps && count < points.Length; i++)
            {
                StepVelocity(ref vx, ref vy, ref vz, airFriction);
                x += vx * StepSeconds;
                y += vy * StepSeconds;
                z += vz * StepSeconds;
                bool tooLow = z < start.z - maxDrop;
                if (i % stepsPerSample != 0 && i != steps && !tooLow)
                {
                    continue;
                }
                Vec3 next = new Vec3(x, y, z);
                if (segmentTest != null && segmentTest(points[count - 1], next, out Vec3 hit))
                {
                    points[count++] = hit;
                    landed = true;
                    return count;
                }
                points[count++] = next;
                if (tooLow)
                {
                    break;
                }
            }
            return count;
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
            // Reach is single-peaked in elevation, so a 5-degree scan and a 1-degree refine within 4 degrees of its best
            // give the same maximum as a full 1-degree scan (checked over 252 speed/height/drag cases) at a third of the
            // cost. A miss is 0.5-1 ms of trajectory steps and RangedReachGate misses on every new (speed, height) pair.
            float best = 0f;
            int bestDeg = -1;
            for (int deg = 0; deg <= 60; deg += 5)
            {
                float r = RangeAtElevation(speed, deg, dz, airFriction);
                if (r > best)
                {
                    best = r;
                    bestDeg = deg;
                }
            }
            // Nothing reached the height on the coarse grid: fall back to every degree.
            int from = bestDeg < 0 ? 0 : Math.Max(0, bestDeg - 4);
            int to = bestDeg < 0 ? 60 : Math.Min(60, bestDeg + 4);
            for (int deg = from; deg <= to; deg++)
            {
                if (deg % 5 == 0)
                {
                    continue;
                }
                float r = RangeAtElevation(speed, deg, dz, airFriction);
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

        private static float RangeAtElevation(int speed, int degrees, float heightDifference, float airFriction)
        {
            double rad = degrees * Math.PI / 180.0;
            return RangeAtHeight((float)(Math.Cos(rad) * speed), (float)(Math.Sin(rad) * speed), heightDifference, airFriction);
        }
    }
}
