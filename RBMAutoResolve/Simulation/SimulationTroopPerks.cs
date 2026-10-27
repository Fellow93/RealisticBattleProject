using System.Collections.Generic;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace RBMAutoResolve
{
    /// <summary>
    /// What a regular troop's OWN perks are worth in a battle nobody watched -- the hero perks RBM hands to line troops
    /// (RBMConfig.TroopPerks, ModuleData/rbm_troop_perks.xml), carried into the Detailed Auto Resolve.
    ///
    /// In a fought battle they work because RBMCombat postfixes CharacterObject.GetPerkValue and answers "yes" for a
    /// listed (troop, perk) pair, so every native perk check that asks the troop's character simply pays out. That
    /// grant is gated on Mission.Current (TroopPerks.AppliesInMission), and it has to stay that way: it is the one
    /// thing that keeps every campaign-map model -- wages, party speed, prisoner rolls, the lot -- from ever seeing a
    /// troop perk. Auto-resolve runs on the map, so the postfix never answers for it, and the perks did nothing here.
    ///
    /// SO THIS FILE READS THE MAP DIRECTLY AND NEVER WIDENS THAT GATE. It is the only place in RBMAutoResolve that asks
    /// RBMConfig.TroopPerks anything, and a troop's perks are never asked through GetPerkValue. The one exception is
    /// the double-count guard: a map battle can auto-resolve while a mission is open (the spectator battle does exactly
    /// that), and then native's own call has already been answered by the mission grant. The hit-point paths below
    /// ask native's own question and step aside when it already said yes (see <see cref="NativeAlreadyGrants"/>).
    ///
    /// HEROES TOO -- their OWN personal combat perks. Nothing else in the simulation reads a lord's or a companion's
    /// personal Bodkin, Powerful, Deep Quivers or Ignore Pain (only his captain perks, his party-leader hit-point perks,
    /// Minister of Health and Veterinary), so without this a perked troop would out-train the lord who leads him. The
    /// same port serves both; only two things differ (see <see cref="Owns"/> and <see cref="AppliesTo"/>):
    ///   - ownership: a hero is asked his own GetPerkValue, the question native asks him; a troop is looked up in the map;
    ///   - gating: a hero needs only the perk system (SimulationPerks.Enabled), since his perks work in a fought battle
    ///     whatever the Troop Perks and RBM Combat switches say; a troop keeps the full gate below.
    /// What native already pays a hero is left out: his hit-point perks (his own MaxHitPoints() counts them, on the map
    /// too) and Veterinary (CommandedMountHealth's AddPerkBonusForCharacter already pays a hero rider). Nothing here
    /// meets anything else the simulation credits him with. His captain perks are the Captain slot (SimulationPerks),
    /// and every perk here is read from its Personal slot, so the two are disjoint by construction. Vanilla's
    /// PowerModifier tally counts only PrimaryRole == Captain perks, and it is lifted from the blow under the same
    /// SimulationPerks.Enabled anyway. The party-role perks DefaultCombatSimulationModel leaves in the blow are all
    /// Tactics/Scouting PartyLeader slots, none of them ported here.
    ///
    /// NOTHING HERE IS A NUMBER OF OURS. Every bonus is the perk's own (PerkHelper.TryGetPerkEffectBonus for the
    /// battle's environment), read from the SAME slot and folded in by the SAME rule as
    /// the native call it stands in for: PerkHelper.AddPerkBonusForCharacter (Personal role on that slot, Add vs
    /// AddFactor by the slot's increment type) where native calls that, the raw effect value where native reads
    /// GetPerkValue's `out effectValue` directly. The call sites are:
    ///
    ///   hit points    DefaultCharacterStatsModel.MaxHitpoints (personal halves, BattleEnvironment.Any)
    ///   mount health  SandboxAgentStatCalculateModel.GetEffectiveMaxHealth, horse branch (Riding.Veterinary)
    ///   ammunition    SandboxAgentStatCalculateModel.InitializeMissionEquipment (per stack, rounded)
    ///   damage        SandboxAgentApplyDamageModel.ApplyDamageAmplifications (+ the War Sails model on top)
    ///   damage taken  SandboxAgentApplyDamageModel.ApplyDamageReductions
    ///   armour pen.   SandboxStrikeMagnitudeModel / NavalStrikeMagnitudeModel.CalculateAdjustedArmorForBlow
    ///   own armour    SandboxAgentStatCalculateModel.SetPerkAndBannerEffectsOnAgent (Athletics.IgnorePain) and
    ///                 UpdateHorseStats (Riding.ToughSteed), both carried into RBM's per-part armour as a ratio by
    ///                 ArmorRework.ApplyDrivenArmorBonus
    ///
    /// WHERE RBM's LIVE COMBAT CHANGES HOW A PERK LANDS, THIS FOLLOWS RBM, because the simulation prices blows with
    /// RBM's damage math. Two places matter. The armour-penetration perks reach RBM's equation through the ratio
    /// DamageRework.Core takes off CalculateAdjustedArmorForBlow (adjusted / raw), so here they scale the zone armour
    /// handed to BlowDamage.RBMComputeDamage -- the same armour, met the same way. And every percentage damage perk is
    /// paid by Mission.GetAttackCollisionResults on the damage AFTER armour (AgentApplyDamageModel.CalculateDamage, which
    /// RBM keeps and stochastically rounds so a small percentage is paid in full on average), so here they multiply
    /// the post-armour damage, never the magnitude: a percentage on the magnitude would also buy penetration, which no
    /// damage perk does.
    ///
    /// GATED LIKE THE LIVE GRANT AND THE REST OF THE PERK SYSTEM (<see cref="Enabled"/>): the auto-resolve's perk system
    /// on, and the very switches that let troop perks work on the field (rbmCombatEnabled + troopPerksEnabled). Heroes
    /// never get TROOP perks (the loader refuses them too); their own perks are gated as above. Campaign strategic power
    /// gets the troops' hit points and nothing else, through SimulationTroopHitPoints.CommandedHealth, and only while
    /// SimulationPerks.Enabled.
    ///
    /// WHAT THE SIMULATION CANNOT CARRY IS LEFT OUT, NOT INVENTED. A perk is ported only where it lands on a number the
    /// model already computes (a weapon's blow, a zone's armour, a quiver, a pool of hit points, a shield's wear, a
    /// charge, a missile taken). Speed, reload, aim and handling perks have no number to land on; neither have the
    /// hit-point-threshold perks (the model knows a stack's wounds, not the striker's own health at the moment of the
    /// blow), wrong-side shield blocks, couch pass-through, or anything about horse speed and handling. The full list,
    /// with the reason for each, is in RBMAutoResolve/AUTO_RESOLVE.md (Troop perks).
    ///
    /// AND SOME PERSONAL DAMAGE PERKS ARE NOT PORTED, because no troop is given them yet: they work in fought battles
    /// only. These are OneHanded.Cavalry, OneHanded.Duelist, Polearm.Cavalry, Polearm.Pikeman, Athletics.StrongArms,
    /// Throwing.SlingingCompetitions and Roguery.Carver. Also the epic perks: WayOfTheSword, WayOfTheGreatAxe,
    /// WayOfTheSpear, Deadshot, MightyPull, UnstoppableForce and TheWayOfTheSaddle. Also every War Sails personal perk
    /// except WarriorsMight / ShatteringBlow / ShatteringVolley: TheSkysFury, TheCorsairsEdge, AxeOfTheNorthwind,
    /// SunnyDisposition, BruteForce, BoardingMaster and HomeTurfAdvantage. Port one here, beside its native
    /// neighbours in Grant.ApplyTo, the day rbm_troop_perks.xml lists it.
    ///
    /// THREAD SAFETY. The hit-point path runs on the campaign's parallel AI ticks (strategic power). Everything it
    /// touches here is read-only: the troop-perk map is a lock-free snapshot swapped whole on load, PerkObjects and
    /// DefaultPerks are immutable once the campaign is up, and this class keeps no mutable static state at all. The
    /// kit path (<see cref="Grant"/>) runs on the main thread inside the kit cache's build and owns its own instance.
    /// </summary>
    internal static class SimulationTroopPerks
    {
        /// <summary>
        /// Whether troop perks count in the auto-resolve at all: the perk system (and with it the auto-resolve) on,
        /// and troop perks live on the field -- RBM Combat on, troopPerksEnabled on, and a map loaded with at least one
        /// troop in it. The same switches as a fought battle, plus the auto-resolve's own.
        /// </summary>
        internal static bool Enabled
        {
            get
            {
                return SimulationPerks.Enabled && RBMConfig.TroopPerks.IsActive && RBMConfig.TroopPerks.HasAny;
            }
        }

        /// <summary>
        /// Whether THIS man fights out of a perk kit. A regular troop needs the full <see cref="Enabled"/> and at least
        /// one perk listed for him. A hero needs only the perk system -- his perks are his own and work on the field
        /// whatever the troop-perk switches say -- and every hero gets the bit, since his kit is rebuilt each battle
        /// anyway (SimulationEquipmentPower.ForgetHeroKits) and is never in a baseline (IsBaselineTroop excludes heroes).
        /// The kit cache's "perks applied" bit is exactly this answer, asked where the kit is fetched -- never inside
        /// the build, so one cache key always names one kit (the same principle SimulationPerks.SkillOf documents for
        /// the captain signature).
        /// </summary>
        internal static bool AppliesTo(CharacterObject troop)
        {
            if (troop == null)
            {
                return false;
            }
            if (troop.IsHero)
            {
                return SimulationPerks.Enabled;
            }
            return Enabled && RBMConfig.TroopPerks.Get(troop) != null;
        }

        /// <summary>
        /// Whether this man has this perk, asked the way native would ask him. A hero: his own GetPerkValue, which is
        /// what every native call site asks (main thread only -- the kit build). A troop: the troop-perk map, because
        /// on the map his GetPerkValue says no (RBM's grant is mission-only).
        /// </summary>
        private static bool Owns(CharacterObject troop, PerkObject perk)
        {
            return troop.IsHero ? troop.GetPerkValue(perk) : RBMConfig.TroopPerks.Has(troop, perk);
        }

        /// <summary>A regular troop whose own hit-point and mount perks the simulation must add (native pays a hero's).</summary>
        private static bool AddsPoolPerks(CharacterObject troop)
        {
            return troop != null && !troop.IsHero && AppliesTo(troop);
        }

        // -------------------------------------------------------------------------------------------------------------
        // What a kit carries
        // -------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// What the troop's perks do to ONE weapon's blows -- each field a sum of the perks' own AddFactor fractions,
        /// split by the condition native attaches to it, so the blow can add up exactly the ones that hold. All zero on
        /// a perk-free profile. Filled by <see cref="Grant.ApplyTo"/>.
        /// </summary>
        internal struct WeaponPerks
        {
            /// <summary>Paid on every landed blow of this weapon (ApplyDamageAmplifications, unconditional for it).</summary>
            public float Damage;

            /// <summary>Paid while the striker is on a horse (Riding.MountedWarrior, Riding.HorseArcher: native's `flag2`).</summary>
            public float Mounted;

            /// <summary>Paid on a head hit (native's IsHeadShot: the head bone, not the neck -- the model's ZoneHead).</summary>
            public float Head;

            /// <summary>Paid when the blow finds the horse (native's IsVictimAgentMount).</summary>
            public float Horse;

            /// <summary>
            /// Paid on a blow a SHIELD takes. Under RBM a blocked blow's damage IS the shield's damage, and it still goes
            /// through CalculateDamage, so the shield-only perks pay here -- and so does every general perk native does
            /// not gate on a body hit. Melee and throwing perks are not gated, so their <see cref="Damage"/> is folded in;
            /// bow and crossbow perks are (CollisionBoneIndex != -1), so theirs is not. The mounted bonus is added at the
            /// blow, since it is gated on neither.
            /// </summary>
            public float Shield;

            /// <summary>The War Sails model's own amplification, which native multiplies on top of the base model's result
            /// rather than adding into it -- so it is a separate factor here too. Zero on land.</summary>
            public float Sea;

            /// <summary>The armour this weapon's blows ignore, as a fraction: armour x max(0, 1 - this).</summary>
            public float Armor;

            /// <summary>Crossbow.Piercer: armour BELOW this many points counts as none at all (a cliff, not a reduction).
            /// Zero when the perk is not held.</summary>
            public float ArmorZeroBelow;
        }

        /// <summary>
        /// What the troop's perks do to HIM rather than to a weapon -- the struck man's side of the blow, and his
        /// horse's charge. All zero on a perk-free kit. Filled by <see cref="Grant.BuildKitPerks"/>.
        /// </summary>
        internal struct KitPerks
        {
            /// <summary>Athletics.IgnorePain: his armour x (1 + this), on foot only.</summary>
            public float FootArmor;

            /// <summary>Riding.FullSpeed: the charge he delivers x (1 + this).</summary>
            public float ChargeDamage;

            /// <summary>Polearm.SureFooted + Athletics.Braced (negative): a charge into him x (1 + this).</summary>
            public float ChargeTaken;

            /// <summary>Bow.SkirmishPhaseMaster (negative): every missile into him -- and into his horse -- x (1 + this).</summary>
            public float MissileTaken;

            /// <summary>Crossbow.CounterFire (negative): only while a crossbow is in his hand.</summary>
            public float MissileTakenCrossbow;

            /// <summary>Throwing.Skirmisher (negative): only while a throwing weapon is in his hand.</summary>
            public float MissileTakenThrowing;

            /// <summary>OneHanded.SteelCoreShields (negative): what a blocked blow costs his shield.</summary>
            public float ShieldTaken;
        }

        // -------------------------------------------------------------------------------------------------------------
        // Applying them -- the arithmetic native does, on the numbers the simulation has
        // -------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// The armour a blow of this weapon actually meets: CalculateAdjustedArmorForBlow's own sequence. Piercer
        /// first -- below its threshold the armour is gone and nothing else is asked; otherwise the summed reduction,
        /// floored at nothing (`max(0, base - base x sum)`, which is how both the Sandbox and the War Sails model write it).
        /// </summary>
        internal static float ArmorAgainst(WeaponPerks perks, float armor)
        {
            if (armor <= 0f)
            {
                return armor;
            }
            if (perks.ArmorZeroBelow > 0f && armor < perks.ArmorZeroBelow)
            {
                return 0f;
            }
            if (perks.Armor == 0f)
            {
                return armor;
            }
            return MathF.Max(0f, armor - (armor * perks.Armor));
        }

        /// <summary>
        /// What the post-armour damage of a blow is multiplied by: native's one amplification ExplainedNumber (every
        /// condition that holds, ADDED -- they share one sum, they do not compound), then the War Sails model's on top.
        /// Exactly 1 for a perk-free profile.
        /// </summary>
        internal static float DamageFactor(WeaponPerks perks, bool headHit, bool strikerMounted, bool atHorse)
        {
            float sum = perks.Damage;
            if (headHit)
            {
                sum += perks.Head;
            }
            if (strikerMounted)
            {
                sum += perks.Mounted;
            }
            if (atHorse)
            {
                sum += perks.Horse;
            }
            return MathF.Max(0f, 1f + sum) * (1f + perks.Sea);
        }

        /// <summary>The same, for a blow a shield takes -- see <see cref="WeaponPerks.Shield"/>.</summary>
        internal static float ShieldDamageFactor(WeaponPerks perks, bool strikerMounted)
        {
            float sum = perks.Shield + (strikerMounted ? perks.Mounted : 0f);
            return MathF.Max(0f, 1f + sum) * (1f + perks.Sea);
        }

        /// <summary>
        /// His own armour, as RBM sees it with Athletics.IgnorePain: native multiplies the driven armour by (1 + effect)
        /// while he is on foot, and ArmorRework.ApplyDrivenArmorBonus carries that onto RBM's per-part armour as the same
        /// ratio -- a part with nothing on it stays at nothing, which multiplying a zero also gives.
        /// </summary>
        internal static float FootArmorFactor(KitPerks perks, bool mounted)
        {
            return mounted ? 1f : (1f + perks.FootArmor);
        }

        /// <summary>
        /// What a charge is worth once both men's perks have had their say: the rider's FullSpeed is an amplification of
        /// the horse-charge blow and the footman's SureFooted/Braced a reduction of it -- two separate ExplainedNumbers
        /// in native, so they multiply.
        /// </summary>
        internal static float ChargeFactor(KitPerks striker, KitPerks struck)
        {
            return (1f + striker.ChargeDamage) * MathF.Max(0f, 1f + struck.ChargeTaken);
        }

        /// <summary>
        /// What a missile into him is worth after his own reductions -- one ExplainedNumber in native, so the perks add.
        /// The two "in his hand" perks are the caller's question (the model knows what he carries and whether he still
        /// has the ammunition for it); a blow at his HORSE passes false for both, since native reads them off the struck
        /// agent's main hand and a horse has none, while Skirmish Phase Master is asked of the rider and covers it.
        /// </summary>
        internal static float MissileTakenFactor(KitPerks perks, bool crossbowInHand, bool throwingInHand)
        {
            float sum = perks.MissileTaken;
            if (crossbowInHand)
            {
                sum += perks.MissileTakenCrossbow;
            }
            if (throwingInHand)
            {
                sum += perks.MissileTakenThrowing;
            }
            return MathF.Max(0f, 1f + sum);
        }

        /// <summary>
        /// What a blocked blow costs his shield after his own reductions. The same ExplainedNumber as the missile
        /// reductions, so a missile caught on the board is reduced by those as well as by SteelCoreShields.
        /// </summary>
        internal static float ShieldTakenFactor(KitPerks perks, bool missile, bool crossbowInHand, bool throwingInHand)
        {
            float sum = perks.ShieldTaken;
            if (missile)
            {
                sum += perks.MissileTaken;
                if (crossbowInHand)
                {
                    sum += perks.MissileTakenCrossbow;
                }
                if (throwingInHand)
                {
                    sum += perks.MissileTakenThrowing;
                }
            }
            return MathF.Max(0f, 1f + sum);
        }

        // -------------------------------------------------------------------------------------------------------------
        // Building them -- one kit at a time
        // -------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// One troop's perks, being folded into one kit as it is built (SimulationEquipmentPower.GetKit with the troop-
        /// perk bit on). Owned by that build alone and thrown away with it, so it needs no locking; what it records in
        /// <see cref="Applied"/> rides out on the kit for the log.
        ///
        /// It does NOT check <see cref="Enabled"/>: that question is asked once, where the kit is fetched, and a kit
        /// built under the bit is the perk kit whatever the settings say later (see <see cref="AppliesTo"/>).
        /// </summary>
        internal sealed class Grant
        {
            private readonly CharacterObject _troop;

            private readonly BattleEnvironment _environment;

            /// <summary>Every perk that actually changed a number in this kit -- what the log calls "applied".</summary>
            internal readonly HashSet<PerkObject> Applied = new HashSet<PerkObject>();

            internal Grant(CharacterObject troop, BattleEnvironment environment)
            {
                _troop = troop;
                _environment = environment;
            }

            /// <summary>
            /// A percentage perk, asked the way PerkHelper.AddPerkBonusForCharacter asks it: its primary slot, which must
            /// be Personal and must work in this environment, and its bonus for this environment (halved at sea where
            /// the perk says so). A slot that is not AddFactor is not a percentage, and would need its own handling;
            /// nothing this file reads that way is one.
            /// </summary>
            private float Factor(PerkObject perk)
            {
                float bonus;
                EffectIncrementType type;
                if (!Grants(_troop, perk, _environment, true, out bonus, out type)
                    || type != EffectIncrementType.AddFactor || bonus == 0f)
                {
                    return 0f;
                }
                Applied.Add(perk);
                return bonus;
            }

            /// <summary>
            /// A perk whose number native uses as it comes (no role test, no increment type): GetPerkValue, then
            /// PerkHelper.TryGetPerkEffectBonus's primary value for the environment, so that is what is read here.
            /// </summary>
            private float Value(PerkObject perk)
            {
                if (perk == null || !Owns(_troop, perk)
                    || !PerkHelper.TryGetPerkEffectBonus(perk, true, out float value, _environment))
                {
                    return 0f;
                }
                if (value != 0f)
                {
                    Applied.Add(perk);
                }
                return value;
            }

            /// <summary>AddPerkBonusForCharacter into a real ExplainedNumber -- Add or AddFactor by the slot, as native.</summary>
            private void AddTo(PerkObject perk, ref ExplainedNumber stat)
            {
                float bonus;
                EffectIncrementType type;
                if (Grants(_troop, perk, _environment, true, out bonus, out type) && bonus != 0f)
                {
                    AddToStat(ref stat, type, bonus, null);
                    Applied.Add(perk);
                }
            }

            /// <summary>
            /// Every perk that reaches this weapon's blows, sorted into the conditions native attaches to it. A port of
            /// ApplyDamageAmplifications (the troop's PERSONAL halves; the captain halves are SimulationPerks' business
            /// and none of them is a damage perk there), the War Sails ApplyDamageAmplifications on top, and
            /// CalculateAdjustedArmorForBlow -- each branch keyed on what native keys it on: the weapon's relevant skill
            /// (a sling's is Throwing, a shot's is its launcher's, which is the ammunition's too), its class, a melee
            /// thrust, and whether it flies.
            /// </summary>
            internal void ApplyTo(ref SimulationWeaponModel.WeaponProfile profile)
            {
                if (!profile.IsValid)
                {
                    return;
                }

                WeaponPerks perks = default(WeaponPerks);
                SkillObject skill = profile.Skill;
                string weaponClass = profile.WeaponType;

                // CalculateAdjustedArmorForBlow: Vandal is asked before the weapon is, so it reaches every attack he makes.
                perks.Armor += Factor(DefaultPerks.TwoHanded.Vandal);

                if (!profile.IsMissile)
                {
                    float damage = 0f;
                    float shieldOnly = 0f;
                    if (skill == DefaultSkills.OneHanded)
                    {
                        damage += Factor(DefaultPerks.OneHanded.DeadlyPurpose);
                        if (weaponClass == WeaponClass.Mace.ToString() || weaponClass == WeaponClass.OneHandedAxe.ToString())
                        {
                            damage += Factor(DefaultPerks.OneHanded.ToBeBlunt);
                        }
                        shieldOnly += Factor(DefaultPerks.OneHanded.Prestige);
                        // Chink in the Armor is keyed on the One Handed skill alone -- one-handed weapons and shield
                        // bashes, never a two-hander or a polearm. The model has no shield bash, so it is the weapons.
                        perks.Armor += Factor(DefaultPerks.OneHanded.ChinkInTheArmor);
                    }
                    else if (skill == DefaultSkills.TwoHanded)
                    {
                        shieldOnly += Factor(DefaultPerks.TwoHanded.WoodChopper);
                        shieldOnly += Factor(DefaultPerks.TwoHanded.ShieldBreaker);
                        if (weaponClass == WeaponClass.TwoHandedAxe.ToString() || weaponClass == WeaponClass.TwoHandedMace.ToString())
                        {
                            damage += Factor(DefaultPerks.TwoHanded.HeadBasher);
                        }
                        perks.Horse += Factor(DefaultPerks.TwoHanded.BeastSlayer);
                        damage += Factor(DefaultPerks.TwoHanded.BladeMaster);
                    }
                    else if (skill == DefaultSkills.Polearm)
                    {
                        // A thrust only (collisionData.StrikeType == 1). The kit's polearm prices the blow it lands
                        // hardest with (SimulationWeaponModel.GetMeleeProfile), so a spear is a thrust and a glaive a swing.
                        if (profile.IsThrust)
                        {
                            damage += Factor(DefaultPerks.Polearm.CleanThrust);
                            damage += Factor(DefaultPerks.Polearm.SharpenTheTip);
                        }
                        perks.Horse += Factor(DefaultPerks.Polearm.SteedKiller);
                        perks.Head += Factor(DefaultPerks.Polearm.Guards);
                    }

                    // Every melee weapon, whatever its skill.
                    damage += Factor(DefaultPerks.Athletics.Powerful);
                    perks.Mounted += Factor(DefaultPerks.Riding.MountedWarrior);

                    perks.Damage = damage;
                    perks.Shield = damage + shieldOnly;

                    // War Sails: Warrior's Might is any melee weapon at sea (its own ExplainedNumber, see WeaponPerks.Sea);
                    // Shattering Blow is the melee half of the naval armour reduction, summed with the land perks.
                    perks.Sea += Factor(NavalPerk("WarriorsMight"));
                    perks.Armor += Factor(NavalPerk("ShatteringBlow"));
                }
                else
                {
                    float damage = 0f;
                    float shieldOnly = 0f;
                    bool boneGated = false;
                    if (skill == DefaultSkills.Bow)
                    {
                        // The whole bow branch sits under CollisionBoneIndex != -1 -- a shaft in a body (or a horse),
                        // never one in a board.
                        boneGated = true;
                        damage += Factor(DefaultPerks.Bow.StrongBows);
                        perks.Head += Factor(DefaultPerks.Bow.DeadAim);
                        perks.Horse += Factor(DefaultPerks.Bow.HunterClan);
                        perks.Armor += Factor(DefaultPerks.Bow.Bodkin);
                    }
                    else if (skill == DefaultSkills.Crossbow)
                    {
                        boneGated = true;
                        perks.Horse += Factor(DefaultPerks.Crossbow.Unhorser);
                        perks.Head += Factor(DefaultPerks.Crossbow.Sheriff);
                        perks.Armor += Factor(DefaultPerks.Crossbow.Puncture);
                        perks.ArmorZeroBelow = Value(DefaultPerks.Crossbow.Piercer);
                    }
                    else if (skill == DefaultSkills.Throwing)
                    {
                        shieldOnly += Factor(DefaultPerks.Throwing.ShieldBreaker);
                        if (weaponClass == WeaponClass.ThrowingAxe.ToString())
                        {
                            shieldOnly += Factor(DefaultPerks.Throwing.Splinters);
                        }
                        perks.Horse += Factor(DefaultPerks.Throwing.Hunter);
                        perks.Head += Factor(DefaultPerks.Throwing.HeadHunter);
                        perks.Armor += Factor(DefaultPerks.Throwing.WeakSpot);
                    }

                    // Riding.HorseArcher is asked of any consumable from a horse, outside the per-skill branches -- so a
                    // crossbow, a sling and a javelin collect it as a bow does, and it is not gated on a body hit.
                    perks.Mounted += Factor(DefaultPerks.Riding.HorseArcher);

                    perks.Damage = damage;
                    perks.Shield = (boneGated ? 0f : damage) + shieldOnly;

                    // War Sails: Shattering Volley is any consumable with a skill behind it.
                    if (skill != null)
                    {
                        perks.Armor += Factor(NavalPerk("ShatteringVolley"));
                    }
                }

                profile.Perks = perks;
            }

            /// <summary>
            /// What InitializeMissionEquipment adds to ONE stack of ammunition trained by <paramref name="ammoSkill"/> (the
            /// consumable's own relevant skill: arrows Bow, bolts Crossbow, sling stones and everything thrown Throwing):
            /// the personal perks summed into a fresh ExplainedNumber at nought and rounded, exactly as native rounds them --
            /// so a perk halved at sea can round to nothing. The party halves (DeepQuivers' and WellPrepared's secondary,
            /// Engineering.MilitaryPlanner) belong to a party leader, not to this troop, and are not here.
            ///
            /// <paramref name="mounted"/> stands in for native's `agent.HasMount` at spawn, which an auto-resolve has no
            /// agent to ask; it is the troop's template (his formation class), as the captain track's own port reads it
            /// (SimulationPerks.IsCavalryTemplate). The one case it misreads is a horseman spawned on foot for a siege, who
            /// would get no Saddlebags there.
            /// </summary>
            internal int AmmoBonus(SkillObject ammoSkill, bool mounted)
            {
                ExplainedNumber bonuses = new ExplainedNumber(0f);
                if (ammoSkill == DefaultSkills.Bow)
                {
                    AddTo(DefaultPerks.Bow.DeepQuivers, ref bonuses);
                }
                else if (ammoSkill == DefaultSkills.Crossbow)
                {
                    AddTo(DefaultPerks.Crossbow.Fletcher, ref bonuses);
                }
                else if (ammoSkill == DefaultSkills.Throwing)
                {
                    AddTo(DefaultPerks.Throwing.WellPrepared, ref bonuses);
                    AddTo(DefaultPerks.Throwing.Resourceful, ref bonuses);
                    if (mounted)
                    {
                        AddTo(DefaultPerks.Throwing.Saddlebags, ref bonuses);
                    }
                }
                return MathF.Round(bonuses.ResultNumber);
            }

            /// <summary>
            /// Riding.ToughSteed on the horse he rides: native AddFactors it onto the mount's driven torso armour, and RBM
            /// carries that onto its own horse armour as the same ratio -- but only for a horse wearing a harness (with
            /// none, the driven armour is the bare zero and RBM keeps its own figure untouched). So the caller asks this
            /// only for a harnessed set, and scales that set's horse armour by it.
            /// </summary>
            internal float HorseArmorFactor()
            {
                return 1f + Factor(DefaultPerks.Riding.ToughSteed);
            }

            /// <summary>
            /// The perks that belong to the man rather than to a weapon. Each is asked only when the kit can use it, so
            /// the log does not credit a man with a charge perk he has no horse to deliver, or a shield perk with no shield.
            /// </summary>
            internal KitPerks BuildKitPerks(bool hasHorse, bool hasShield, bool shootsCrossbow, bool usesThrowing)
            {
                KitPerks perks = default(KitPerks);

                // SetPerkAndBannerEffectsOnAgent reads IgnorePain's PRIMARY effect value directly (num2 += effectValue).
                perks.FootArmor = Value(DefaultPerks.Athletics.IgnorePain);

                if (hasHorse)
                {
                    perks.ChargeDamage = Factor(DefaultPerks.Riding.FullSpeed);
                }

                // ApplyDamageReductions, the victim's personal halves.
                perks.ChargeTaken = Factor(DefaultPerks.Polearm.SureFooted) + Factor(DefaultPerks.Athletics.Braced);
                perks.MissileTaken = Factor(DefaultPerks.Bow.SkirmishPhaseMaster);
                if (shootsCrossbow)
                {
                    perks.MissileTakenCrossbow = Factor(DefaultPerks.Crossbow.CounterFire);
                }
                if (usesThrowing)
                {
                    perks.MissileTakenThrowing = Factor(DefaultPerks.Throwing.Skirmisher);
                }
                if (hasShield)
                {
                    perks.ShieldTaken = Factor(DefaultPerks.OneHanded.SteelCoreShields);
                }
                return perks;
            }
        }

        // -------------------------------------------------------------------------------------------------------------
        // Hit points -- the troop's own, ahead of his commander's
        // -------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// The troop's hit points with his own perks counted: DefaultCharacterStatsModel.MaxHitpoints' personal list,
        /// on the hundred CharacterObject.MaxHitPoints() reads off the map, rounded as MaxHitPoints() rounds it. This
        /// is the number a fought battle spawns him with (Agent's BaseHealthLimit), and the base his commander's
        /// perks are then added to (SimulationTroopHitPoints.BuildCommandedHealth). Unchanged for a hero, a troop with
        /// no perks, or with the system off. Thread-safe: read-only (see the class note).
        /// </summary>
        internal static int HitPointsWithTroopPerks(CharacterObject troop, int baseHitPoints)
        {
            if (!AddsPoolPerks(troop))
            {
                return baseHitPoints;
            }
            ExplainedNumber stat = new ExplainedNumber(baseHitPoints);
            AddHitPointPerks(troop, ref stat, false);
            return MathF.Round(stat.ResultNumber);
        }

        /// <summary>
        /// The same perks as named lines on an explained pool, for the log -- labelled as the troop's own, so a reader
        /// can tell the man's Well Built from his lord's (the two share a name and a number).
        /// </summary>
        internal static void AddHitPointLines(CharacterObject troop, ref ExplainedNumber stat)
        {
            if (AddsPoolPerks(troop))
            {
                AddHitPointPerks(troop, ref stat, true);
            }
        }

        /// <summary>
        /// DefaultCharacterStatsModel.MaxHitpoints, transcribed: the same perks, the same slots (Doctor's Oath and
        /// Fortitude Tonic pay their SECONDARY half here), BattleEnvironment.Any as native passes (so nothing is halved
        /// at sea), and Mighty Blow's epic formula last. The loader never gives Mighty Blow to a troop at or below the
        /// epic threshold, so its line is always a gain.
        /// </summary>
        private static void AddHitPointPerks(CharacterObject troop, ref ExplainedNumber stat, bool label)
        {
            AddPersonal(troop, DefaultPerks.OneHanded.Trainer, BattleEnvironment.Any, true, ref stat, label);
            AddPersonal(troop, DefaultPerks.TwoHanded.ThickHides, BattleEnvironment.Any, true, ref stat, label);
            AddPersonal(troop, DefaultPerks.Medicine.DoctorsOath, BattleEnvironment.Any, false, ref stat, label);
            AddPersonal(troop, DefaultPerks.Medicine.FortitudeTonic, BattleEnvironment.Any, false, ref stat, label);
            AddPersonal(troop, DefaultPerks.Athletics.WellBuilt, BattleEnvironment.Any, true, ref stat, label);
            AddPersonal(troop, DefaultPerks.OneHanded.UnwaveringDefense, BattleEnvironment.Any, true, ref stat, label);
            AddPersonal(troop, DefaultPerks.Medicine.PreventiveMedicine, BattleEnvironment.Any, true, ref stat, label);

            PerkObject mightyBlow = DefaultPerks.Athletics.MightyBlow;
            if (mightyBlow != null && Owns(troop, mightyBlow) && !NativeAlreadyGrants(troop, mightyBlow))
            {
                int bonus = troop.GetSkillValue(DefaultSkills.Athletics)
                    - Campaign.Current.Models.CharacterDevelopmentModel.MaxSkillRequiredForEpicPerkBonus;
                stat.Add(bonus, label ? TroopLabel(mightyBlow) : null);
            }
        }

        /// <summary>
        /// Riding.Veterinary on the troop's OWN horse: the personal, primary half of GetEffectiveMaxHealth's horse
        /// branch, in the rider's environment (land only -- the perk says so). Added to the same pool, by the same rule
        /// (AddFactor), right where native adds it. Nothing for a hero (his own perk already pays through native's call)
        /// or with the system off.
        /// </summary>
        internal static void AddMountPerks(CharacterObject rider, BattleEnvironment environment, ref ExplainedNumber stat,
            bool label)
        {
            if (AddsPoolPerks(rider))
            {
                AddPersonal(rider, DefaultPerks.Riding.Veterinary, environment, true, ref stat, label);
            }
        }

        private static void AddPersonal(CharacterObject troop, PerkObject perk, BattleEnvironment environment,
            bool primary, ref ExplainedNumber stat, bool label)
        {
            float bonus;
            EffectIncrementType type;
            if (Grants(troop, perk, environment, primary, out bonus, out type) && !NativeAlreadyGrants(troop, perk))
            {
                AddToStat(ref stat, type, bonus, label ? TroopLabel(perk) : null);
            }
        }

        /// <summary>
        /// Whether native's own call has ALREADY paid this perk, so adding it here would count it twice. That happens
        /// only while a mission is open -- RBM's live grant answers GetPerkValue then -- and a map battle can auto-
        /// resolve while one is (the spectator battle runs a mission beside the map battle it copies). Then
        /// CharacterObject.MaxHitPoints() and the native Veterinary call already carry the troop's perks, and this
        /// steps aside. On the map, which is every ordinary auto-resolve, it is always false.
        ///
        /// It asks the very question those native calls ask -- the one-argument CharacterObject.GetPerkValue, which is
        /// what MaxHitpoints and AddPerkBonusForCharacter use -- rather than TroopPerks.AppliesInMission. That matters:
        /// AppliesInMission reads the LIVE switches, but the GetPerkValue postfix is only installed if RBM Combat was on
        /// at game start, so turning it on mid-session would make AppliesInMission say "native paid it" when native,
        /// unpatched, paid nothing. Read-only, so safe on the parallel AI ticks.
        /// </summary>
        private static bool NativeAlreadyGrants(CharacterObject troop, PerkObject perk)
        {
            return troop != null && perk != null && troop.GetPerkValue(perk);
        }

        // -------------------------------------------------------------------------------------------------------------
        // The log
        // -------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// The troop's perks the simulation actually applied, in the order the perk file lists them: those that changed
        /// a number in his kit (<paramref name="kitApplied"/>, recorded by <see cref="Grant"/>), his hit-point perks, and
        /// Veterinary when he has a horse for it. A listed perk the model cannot carry, or one that does nothing in this
        /// environment or with this kit, is not named -- the line says what the battle used, not what the file says.
        /// In a battle with no horses in it (<paramref name="dismounted"/>: a siege, a deck) the perks that need one --
        /// Mounted Warrior, Horse Archer, Full Speed, Tough Steed, Veterinary -- are in his kit but never fire, so they
        /// are left out. (Saddlebags stays: the kit counts it off his template, see Grant.AmmoBonus.)
        ///
        /// A HERO has no perk file, so his are the kit's own record, in the game's perk order (PerkObject.All). Only
        /// what this file applied is named. His hit-point perks and Veterinary are native's to pay, already in his own
        /// pool, so they are not this list's to claim.
        /// </summary>
        internal static List<string> AppliedNames(CharacterObject troop, HashSet<PerkObject> kitApplied, bool hasHorse,
            BattleEnvironment environment, bool dismounted)
        {
            List<string> names = new List<string>();
            if (!AppliesTo(troop))
            {
                return names;
            }
            if (troop.IsHero)
            {
                if (kitApplied != null && kitApplied.Count > 0)
                {
                    foreach (PerkObject perk in PerkObject.All)
                    {
                        if (perk != null && kitApplied.Contains(perk) && !(dismounted && NeedsHorse(perk)))
                        {
                            names.Add(perk.Name != null ? perk.Name.ToString() : perk.StringId);
                        }
                    }
                }
                return names;
            }
            List<PerkObject> ordered = RBMConfig.TroopPerks.GetOrdered(troop);
            if (ordered == null)
            {
                return names;
            }
            foreach (PerkObject perk in ordered)
            {
                if (perk == null)
                {
                    continue;
                }
                if (dismounted && NeedsHorse(perk))
                {
                    continue;
                }
                bool applied = (kitApplied != null && kitApplied.Contains(perk))
                    || IsHitPointPerk(troop, perk)
                    || (hasHorse && perk == DefaultPerks.Riding.Veterinary && Pays(troop, perk, environment, true));
                if (applied)
                {
                    names.Add(perk.Name != null ? perk.Name.ToString() : perk.StringId);
                }
            }
            return names;
        }

        /// <summary>The applied perks that only ever fire for a man on a horse (for the log's dismounted battles).</summary>
        private static bool NeedsHorse(PerkObject perk)
        {
            return perk == DefaultPerks.Riding.MountedWarrior || perk == DefaultPerks.Riding.HorseArcher
                || perk == DefaultPerks.Riding.FullSpeed || perk == DefaultPerks.Riding.ToughSteed
                || perk == DefaultPerks.Riding.Veterinary;
        }

        private static bool IsHitPointPerk(CharacterObject troop, PerkObject perk)
        {
            if (perk == DefaultPerks.Athletics.MightyBlow)
            {
                return true;
            }
            if (perk == DefaultPerks.Medicine.DoctorsOath || perk == DefaultPerks.Medicine.FortitudeTonic)
            {
                return Pays(troop, perk, BattleEnvironment.Any, false);
            }
            if (perk == DefaultPerks.OneHanded.Trainer || perk == DefaultPerks.TwoHanded.ThickHides
                || perk == DefaultPerks.Athletics.WellBuilt || perk == DefaultPerks.OneHanded.UnwaveringDefense
                || perk == DefaultPerks.Medicine.PreventiveMedicine)
            {
                return Pays(troop, perk, BattleEnvironment.Any, true);
            }
            return false;
        }

        private static bool Pays(CharacterObject troop, PerkObject perk, BattleEnvironment environment, bool primary)
        {
            float bonus;
            EffectIncrementType type;
            return Grants(troop, perk, environment, primary, out bonus, out type) && bonus != 0f;
        }

        // -------------------------------------------------------------------------------------------------------------
        // The one test, and the one sum
        // -------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// PerkHelper.AddPerkBonusForCharacter's test, asked of the troop-perk map instead of GetPerkValue: the slot works
        /// in this environment, its role is Personal, and the troop has the perk. The bonus is TryGetPerkEffectBonus's
        /// (the slot's bonus, halved at sea for a NavalReduced slot).
        /// </summary>
        private static bool Grants(CharacterObject troop, PerkObject perk, BattleEnvironment environment, bool primary,
            out float bonus, out EffectIncrementType type)
        {
            bonus = 0f;
            type = EffectIncrementType.Invalid;
            if (perk == null || troop == null || !PerkHelper.TryGetPerkEffectBonus(perk, primary, out bonus, environment))
            {
                bonus = 0f;
                return false;
            }
            PartyRole role = primary ? perk.PrimaryRole : perk.SecondaryRole;
            if (role != PartyRole.Personal || !Owns(troop, perk))
            {
                bonus = 0f;
                return false;
            }
            type = primary ? perk.PrimaryIncrementType : perk.SecondaryIncrementType;
            return true;
        }

        /// <summary>PerkHelper.AddToStat (private there): an Add adds, an AddFactor adds to the factor sum.</summary>
        private static void AddToStat(ref ExplainedNumber stat, EffectIncrementType type, float bonus, TextObject text)
        {
            switch (type)
            {
                case EffectIncrementType.Add:
                    stat.Add(bonus, text);
                    break;

                case EffectIncrementType.AddFactor:
                    stat.AddFactor(bonus, text);
                    break;
            }
        }

        private static TextObject TroopLabel(PerkObject perk)
        {
            string name = (perk.Name != null) ? perk.Name.ToString() : perk.StringId;
            return new TextObject(name + " (troop)");
        }

        /// <summary>
        /// A War Sails perk, by its StringId. RBMAutoResolve does not reference NavalDLC (and must run without it), so
        /// NavalPerks cannot be named; the object manager finds the same PerkObject when the DLC is loaded and nothing
        /// when it is not -- and nothing is then simply a perk no troop has. Asked only while a kit is built (main thread).
        /// </summary>
        private static PerkObject NavalPerk(string stringId)
        {
            MBObjectManager manager = MBObjectManager.Instance;
            return (manager != null) ? manager.GetObject<PerkObject>(stringId) : null;
        }
    }
}
