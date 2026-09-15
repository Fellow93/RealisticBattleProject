using HarmonyLib;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// WHAT A TROOP IS WORTH, AS THE PLAYER AND THE AI ARE TOLD IT.
    ///
    /// Vanilla prices a troop for this purpose by his TIER and nothing else --
    /// <c>DefaultMilitaryPowerModel.GetDefaultTroopPower</c> is <c>(2+tier)(10+tier)*0.02</c>, with a flat 1.5x if he
    /// is a lord. Not his armour, not his weapon, not his shield, not his training. A tier-3 elite in mail and a
    /// tier-3 levy in a tunic are the same soldier to it. And that one number is the whole of what the game means by
    /// strength: it is what a party is worth in the encounter screen, what an AI lord weighs before he decides to
    /// attack you, and what a kingdom's armies are compared on.
    ///
    /// This replaces it with the man himself: what he can hurt someone with, and how long he lasts while he does it.
    ///
    /// ------------------------------------------------------------------------------------------------------------
    /// WHY THIS PATCHES GetPowerOfParty AND NOT GetDefaultTroopPower, WHICH IS THE OBVIOUS PLACE
    ///
    /// Because auto-resolve reads the obvious place. <c>DefaultCombatSimulationModel.SimulateHit</c> prices every
    /// simulated blow as <c>40 * (GetTroopPower(striker)/GetTroopPower(struck))^0.7</c>, and GetTroopPower is
    /// GetDefaultTroopPower with the leader and terrain modifiers hung off it. So a patch there does not just change
    /// what the player is told -- it changes what a blow does.
    ///
    /// And it would change it WRONGLY, because RBM already prices those blows on equipment, by a better model than
    /// this one (SimulationEquipmentPower: hit zones, real arrows, shields that splinter, a battle with phases in it).
    /// That model works by dividing vanilla's tier term back out of the blow and multiplying its own equipment ratio
    /// in. Put equipment into the tier term and it divides out something that is no longer there and multiplies
    /// equipment in a second time -- equipment counted twice, at two different exponents, silently. See the comment on
    /// SimulationEquipmentPower.VanillaTierPower: it recomputes the tier formula by hand rather than calling the model
    /// precisely so a patch cannot move the DIVISOR under it. Nothing can protect it from the DIVIDEND moving.
    ///
    /// GetPowerOfParty is not on that path. SimulateHit never calls it. Everything the player and the AI read does:
    /// PartyBase.GetCustomStrength and TotalStrength, MapEventSide.RecalculateStrengthOfSide (and so StrengthOfSide,
    /// StrengthRatio, renown and influence), Army.GetCustomStrength, MobileParty.GetTotalLandStrengthWithFollowers,
    /// and every AI decision built on those. So the strength a man is told about changes, and the blow does not.
    ///
    /// The cost of that choice, stated plainly: the displayed strength no longer predicts auto-resolve exactly. Both
    /// numbers are equipment-aware, but by two different models, so they agree in direction and not in magnitude.
    ///
    /// ------------------------------------------------------------------------------------------------------------
    /// THE COMMANDER, AND WHY ONLY HALF OF HIM IS HERE
    ///
    /// Bannerlord runs two perk tracks that never touch. The COMMANDER track is party-scoped: a PartyLeader /
    /// ArmyCommander / ClanLeader / Surgeon / ... perk is resolved through MobileParty.HasPerk, which asks a different
    /// hero depending on the role, and always about the soldier's OWN party. Three lords in one battle each buff only
    /// their own men. The CAPTAIN track is formation-scoped and party-blind: it goes through the formation's captain
    /// and reaches everyone standing in that formation whoever they marched in with.
    ///
    /// Only the commander track is here, and that is not a shortcut -- it is the only one that has any meaning at this
    /// scope. GetPowerOfParty is handed one party. The commander track IS party-scoped, so the two agree exactly, and
    /// vanilla's own MobileParty.HasPerk does the role-to-hero work (there is no case for Captain in it; it returns
    /// false, always). The captain track needs a formation, and there are no formations on the campaign map -- for the
    /// same reason auto-resolve has none, see SimulationCommandStructure. Synthesising them per party would be wrong
    /// anyway: formations are a per-SIDE thing, so a five-lord army would collect five sets of captains where a battle
    /// gives it four.
    ///
    /// Worth knowing, since it lands squarely on the passive term below: there is no captain hit-point perk anywhere
    /// in the game. Every troop-HP perk is PartyLeader. Staying power is always a fact about a party, and a party is
    /// exactly what this method has.
    ///
    /// ------------------------------------------------------------------------------------------------------------
    /// TO REMOVE THIS FEATURE ENTIRELY
    ///
    ///   1. Delete the whole Power folder (this file and StrategicPowerLog.cs), and both &lt;Compile Include&gt;
    ///      lines in RBMCampaign.csproj.
    ///   2. RBMConfig.cs: delete strategicPowerEnabled and strategicPowerLoggingEnabled, their ReadOrCreate lines
    ///      and their setInnerText lines.
    ///   3. RBMConfigViewModel.cs: delete StrategicPowert, StrategicPowerEnabledText, StrategicPowerEnabled and the
    ///      four lines that touch them (ctor, load, save, defaults). RBMConfig.xml: delete the one ListPanel block.
    ///   4. Put `private` back in place of `internal` on SimulationEquipmentPower.GetArmorZones and on
    ///      SimulationTroopHitPoints.CommandedHealth.
    ///
    /// Nothing else in the mod calls into this file, and it calls nothing in the mod except those two.
    /// </summary>
    internal static class StrategicTroopPower
    {
        // ---------------------------------------------------------------------------------------------------------
        // TUNING. Apart from PowerScale, none of these are derived; they are the dials this model is calibrated on.
        // They deliberately live here rather than in the config screen -- the screen carries the on/off switch and
        // nothing else, the same way the auto-resolve equipment model keeps its weight in the config file (see
        // RBMConfigViewModel).
        // ---------------------------------------------------------------------------------------------------------

        /// <summary>
        /// What this model's raw number must be divided by to land back on the scale vanilla prices men in.
        ///
        /// THIS IS NOT A DIAL. It is measured, and it exists because a number is not a number: offense x active x
        /// passive comes out in the low hundreds, and vanilla's (2+tier)(10+tier)*0.02 comes out between 0.40 and
        /// 2.56. Everything that reasons in RATIOS -- which is nearly all of the AI -- cannot tell the difference.
        /// But the parts of the game that compare a party's power to a HARDCODED CONSTANT can, and they were all
        /// written against vanilla's range:
        ///
        ///   DefaultArmyManagementCalculationModel.CanLordCreateArmy   if (num5 &lt; 1000f) -- the army-power floor,
        ///                                                             which unscaled a handful of men clears, so
        ///                                                             the gate stands permanently open
        ///   DefaultTargetScoreCalculatingModel :212                   if (num6 &lt; 100f && Besieger) -- the damper
        ///                                                             on a weak party starting a siege; never fires
        ///   DefaultTargetScoreCalculatingModel :174                   MathF.Max(100f, num2) -- a floor that never
        ///                                                             binds
        ///
        /// So the choice is to divide once here, or to chase vanilla's constants forever -- including the ones
        /// nobody has found yet. This divides.
        ///
        /// 197 was measured, not picked, off 19,989 party pricings in logs/powerCalculation (1.85M men, 146k stack
        /// rows): sum(men x thisModel) / sum(men x vanillaTier) = 197.4 in aggregate, and the MEDIAN party lands at
        /// 198.8 -- the agreement between those two is what says one flat constant is honest here rather than a
        /// fudge that happens to fit the average. Per-tier the ratio is flat within 187-227 across T1-T6, which is
        /// the same statement seen from the side.
        ///
        /// What this deliberately does NOT do is flatten the model back into vanilla. Dividing every man by one
        /// number cannot touch what this model is FOR: that two tier-3 men differ (the measured spread at tier 3 is
        /// 160 to 428, where vanilla says 1.30 and means it). After the divide the median party still lands 9.7%
        /// off vanilla's tier-only answer, and that residue is the entire point -- it is the model disagreeing
        /// about THIS party, on the scale where the disagreement can be read.
        ///
        /// The one tier that moves is T0, whose ratio is 345 rather than ~200: a peasant comes out at 0.70 against
        /// vanilla's 0.40. That is this model saying vanilla under-prices the rabble by 1.75x, and it is kept.
        ///
        /// Re-measure this if the offense model moves (rbmCombatEnabled, OneHandedThrustDamageBonus, armorMultiplier
        /// -- see _cacheRbmCombat) or if the tuning above is re-cut. The log prints what it was measured under.
        /// </summary>
        internal const float PowerScale = 272f;

        /// <summary>Where a blow lands. The armour a man wears is worth what the blows he takes actually meet, so a
        /// greave is worth less than a cuirass for no reason but that fewer blows go there.</summary>
        private const float ZoneHead = 0.16f;

        private const float ZoneNeck = 0.03f;

        private const float ZoneTorso = 0.44f;

        private const float ZoneShoulder = 0.12f;

        private const float ZoneArm = 0.14f;

        private const float ZoneLeg = 0.11f;

        /// <summary>
        /// How many armour points buy one man's worth of extra life, at armorMultiplier == 1 (vanilla). RBM's own
        /// armour equation is <c>100/(100 + armor*armorMultiplier)</c>, so the divisor the passive term actually uses
        /// is <c>ArmorConstant/armorMultiplier</c> whenever RBM Combat is on -- that is what makes the passive term
        /// say the same thing about armour that a real RBM blow does (see PowerOfSet). At the default multiplier of 2
        /// that halves the divisor, doubling armour's weight. This base value is only the armorMultiplier == 1 case.
        /// </summary>
        private const float ArmorConstant = 100f;

        /// <summary>
        /// What a shield is worth as standing cover, per point of its tier. KEPT SMALL ON PURPOSE. A shield already
        /// moves the ACTIVE term from 1.25x to 4.0x, which is the bulk of what carrying one is worth; this is only the
        /// board's own bulk, answering what he never saw coming. Overfeed it and shielded infantry read as strictly
        /// dominant, and the AI will believe it.
        /// </summary>
        private const float ShieldPassiveWeight = 4f;

        // MOUNT. A horse is worth a SHARE of what the man on it is worth, and how big that share is depends on how
        // survivable the mount is -- a barded charger carries him through the whole battle, a courier's pony drops
        // under the first spear. Priced as a fraction OF his own power (offense x active x passive), not a flat sum,
        // for one reason: a flat sum is a bigger fraction of a cheap troop than a dear one, so it made light cavalry
        // gain a larger PERCENTAGE from their horse than knights did -- backwards. As a share it tracks the mount and
        // not the base: same horse, same percentage, whoever is on it. Charge stays in offense (the lance is his).
        //
        // The two constants below are the whole of it, and neither is a free dial -- they are an ANCHOR and read as a
        // sentence: "a reference barded warhorse is worth MountBonusAtReference of its rider's power." Everything
        // lighter or heavier scales straight off it by how its survival compares. So the "magic number" is just:
        //   fraction added = (this mount's survival / a barded warhorse's survival) x (a warhorse's worth, 0.43).

        /// <summary>
        /// The survival of the mount we calibrate against -- a fully barded warhorse, the kind a knight rides: about a
        /// horse's own health (Monster ~200) plus heavy barding counted at BardingToHealth. Measured off the log at
        /// ~440. It is only a yardstick: a mount at exactly this survival is worth MountBonusAtReference; nothing is
        /// clamped to it.
        /// </summary>
        private const float ReferenceMountSurvival = 440f;

        /// <summary>
        /// What that reference barded warhorse adds, as a fraction of its rider's own power. 0.43 puts it at ~30% of
        /// his MOUNTED total (0.43 / 1.43), which is the "a good warhorse is worth about a third of him" target. A
        /// lighter mount (less barding, less horse) lands proportionally below it, a cataphract's above -- so lighter
        /// cavalry gain a smaller share than armoured cavalry, by construction.
        /// </summary>
        private const float MountBonusAtReference = 0.43f;

        /// <summary>Barding's exchange rate into the mount's survival, in effective hit points per armour point. This is
        /// the ONLY place barding is priced: it is the horse's armour, not the rider's, and a blow that finds the man
        /// meets his own steel instead (the auto-resolve model prices it the same way).</summary>
        private const float BardingToHealth = 2f;

        /// <summary>A man swings whichever weapon is in his hand, but he leads with his best. 1 would be "best only".</summary>
        private const float BestWeaponWeight = 0.7f;

        /// <summary>The share of a shooter's battle spent shooting. The rest of it he is a swordsman.</summary>
        private const float RangedShare = 0.7f;

        /// <summary>
        /// What a shooter's OUTPUT is worth beyond the blow itself -- for archers and horse archers alike.
        ///
        /// He looses from behind the line, at a man who is still walking toward him and cannot answer. That is worth
        /// something this model has no other place to put: it prices a blow, and an archer's whole point is WHERE he
        /// throws his from. Armour he does not need is not a weakness in him.
        ///
        /// Measured, not chosen -- and measured twice, because the first reading was a lie. It was 1.1, off a gap of
        /// 1.10; but that average had SLINGERS in it, priced at nearly twice the best bow in Calradia by a tier
        /// formula reading a sling's length as a draw weight (see SlingEnergy). With them gone and the launcher
        /// priced on its real energy, infantry outweigh foot archers by 1.37, 1.43, 1.18, 1.24 across tiers 2-5 --
        /// a mean of 1.24. So 1.1 x 1.24 = 1.35, which lands the two arms level.
        ///
        /// Worth knowing what that means: an archer is NOT being handed a tenth for free. The gap is entirely
        /// protection -- his offense already matches an infantryman's at every tier -- and this is the price of
        /// saying that armour he does not need is not a weakness in him.
        /// </summary>
        private const float RangedOffenseWeight = 1.35f;

        /// <summary>Training's worth to a blow: at saturation a man hits this much harder than a raw recruit.</summary>
        private const float SkillOffenseSpread = 1f;

        /// <summary>Where training stops paying. Mirrors the auto-resolve model's own saturation.</summary>
        private const float SkillSaturation = 250f;

        /// <summary>How much a weapon's own quality (its damage factor) is worth on top of its class. The small part.</summary>
        private const float PenetrationWeight = 0.35f;

        /// <summary>The charge behind a horse, per point of ChargeDamage.</summary>
        private const float ChargeWeight = 0.004f;

        // RBM's own missile physics, lifted from SimulationWeaponModel so a shot is priced here the way a shot is
        // priced there. MissileSpeed is NOT a speed: RBM overwrites it with the draw weight in pounds.
        private const float PoundsToNewtons = 4.448f;

        private const float BowPowerstroke = 25f * 0.0254f;

        private const float CrossbowPowerstroke = 20f * 0.0254f;

        private const float BowEfficiency = 0.90f;

        /// <summary>A yew stave wastes more of the draw than horn and sinew. RBM reads this off ItemUsage.</summary>
        private const float LongBowEfficiency = 0.835f;

        private const float CrossbowEfficiency = 0.88f;

        /// <summary>
        /// What a crossbow's power costs it in rate of fire. RBM's crossbows draw 250-300lb against a bow's 60-163,
        /// so on raw energy a crossbowman would price at about twice a bowman; the reload is what he pays for it.
        /// The one invented number in the ranged path, and the only one the physics cannot supply.
        /// </summary>
        private const float CrossbowReloadDivisor = 2.5f;

        /// <summary>Joules to this model's offense units, so a shot lands in the same range as a melee ceiling.</summary>
        private const float RangedEnergyScale = 0.7f;

        /// <summary>The top of the tier scale. RBM clamps its own melee/ammo/shield tiers here; see TierfOf.</summary>
        private const float MaxItemTier = 6.5f;

        /// <summary>The hundred a soldier starts with (DefaultCharacterStatsModel.MaxHitpoints). See HealthFactorOf.</summary>
        private const float BaselineHitPoints = 100f;

        /// <summary>
        /// What a sling is worth, flat -- because its tier is not a tier and cannot be made into one.
        ///
        /// RBM repurposes MissileSpeed: for a bow it is the DRAW WEIGHT in pounds (60-160), and for a sling it is
        /// the sling's LENGTH (see SimulationWeaponModel.GetMissileMagnitude, which branches on WeaponClass.Sling
        /// and reads it as slingLength = MissileSpeed * 0.01). CalculateRangedWeaponTier has no Sling case, so it
        /// runs a bow's draw-weight curve over a length: (320 - 60) * 0.049 = 12.74, nearly twice the best bow in
        /// Calradia. Slingers then topped every archer bucket in the game -- a Jawwal Master Slinger at 926 against
        /// a Battanian Fian's 386 -- and the poisoned average was why no noble bow line could read as exceptional.
        ///
        /// Clamping to 6.5 only pinned the nonsense to the ceiling: a sling still out-rated a Fian's bow. There is
        /// no honest tier to recover here, because the number being read is a length. So a sling is priced flat.
        ///
        /// 110 J sits it just under a middling bow. The sim says a sling stores real energy (it clamps 60-350 J,
        /// against ~84-224 J for a bow), but that it is tamed by what it throws -- damage is
        /// energy x (ammo.ThrustDamage/100), and a stone has little. This model prices the launcher alone and never
        /// sees the stone, so the brake has to be here instead. A sling is a shepherd's sidearm; it is not a warbow.
        /// </summary>
        private const float SlingEnergy = 110f;

        // The active-defence ladder, lifted verbatim from SimulationEquipmentPower so that what a man is SAID to turn
        // aside and what a simulated blow finds him turning aside are the same figure. Shield is binary here, exactly
        // as it is there -- the board's quality is paid for in the passive term instead.
        private const float ShieldDefenseBase = 0.45f;

        private const float ShieldDefenseSkillCoeff = 0.30f;

        private const float WeaponDefenseFloor = 0.20f;

        private const float WeaponDefenseSkillCoeff = 0.18f;

        private const float DefenseChanceCap = 0.75f;

        /// <summary>
        /// How hard a turned-aside blow is allowed to translate into a longer life.
        ///
        /// The CHANCES above are the sim's own and are not touched -- what is damped is only this model's use of
        /// them. 1/(1-chance) is savagely non-linear: a trained man behind a shield reaches the 0.75 cap and comes
        /// out at 4.0x, while the same man with only his sword sits at 1.61x. One item, worth two and a half times
        /// a man's life. Measured against the roster, that -- and nothing else -- was the whole archer problem:
        /// offense is at parity at every tier (126 vs 103, 119 vs 107, 181 vs 162), and at tier 0, where NOBODY
        /// carries a shield, protection is equal and archers simply win. The gap appeared exactly where shields did.
        ///
        /// And the term is being asked to do something it was never about: it prices how well a man blocks a sword,
        /// which is a fact about a brawl. An archer spends his battle shooting from behind the line, not parrying.
        ///
        /// 0.4 was solved off the log, not picked: it brings infantry/archer power per tier from 1.28-1.52 down to
        /// 1.08-1.26, and leaves a shield worth 4.0^0.4 / 1.61^0.4 = 1.44x rather than 2.5x. Exact parity would need
        /// ~0, i.e. deleting active defence altogether -- there is a passive gap underneath it (armour 1.75 vs 1.95
        /// at tier 5) that no damping of THIS term can reach, and a shield that bought nothing would be a worse lie
        /// than the one being fixed.
        /// </summary>
        private const float ActiveDefenseDamping = 0.4f;

        // ---------------------------------------------------------------------------------------------------------

        internal static bool Enabled
        {
            get
            {
                return RBMConfig.RBMConfig.rbmCampaignEnabled
                    && RBMConfig.RBMConfig.strategicPowerEnabled
                    && Campaign.Current != null;
            }
        }

        /// <summary>
        /// A troop template's kit and training never change, so his power is worked out once. A HERO's do -- he buys
        /// harness and trains -- so his is stamped and re-measured as the campaign runs.
        /// </summary>
        private struct PowerEntry
        {
            public float Power;

            public double Day;
        }

        // One lock over both caches and their freshness stamps. GetPowerOfParty is asked from wherever the AI
        // happens to be thinking (the capture fields below are [ThreadStatic] for the same reason), and a plain
        // Dictionary written from two threads at once does not throw -- it corrupts, into an infinite loop on the
        // next read. The lock is held only around the dictionary touches; Measure and Tierf run outside it.
        private static readonly object _cacheLock = new object();

        private static readonly Dictionary<CharacterObject, PowerEntry> _powerCache = new Dictionary<CharacterObject, PowerEntry>();

        private static readonly Dictionary<ItemObject, float> _tierCache = new Dictionary<ItemObject, float>();

        // What the measurements were taken under. Both of these move the answer: rbmCombatEnabled picks the whole
        // offense model, and OneHandedThrustDamageBonus is read by RBM's own melee TIER formula -- so a config screen
        // slider really can make every cached number here a lie. Same reasoning as EnsureBaselines.
        private static bool _cacheRbmCombat;

        private static float _cacheThrustBonus;

        private static bool _cachePrimed;

        /// <summary>When the hero entries were last swept. Guarded by <see cref="_cacheLock"/>.</summary>
        private static double _lastSweepDay = double.MinValue;

        /// <summary>A fresh session: every key here belongs to the campaign being torn down.</summary>
        internal static void ResetForNewSession()
        {
            lock (_cacheLock)
            {
                _powerCache.Clear();
                _tierCache.Clear();
                _cachePrimed = false;
                _lastSweepDay = double.MinValue;
            }
            StrategicPowerLog.ResetForNewSession();
        }

        private static void EnsureCacheFresh()
        {
            bool rbmCombat = RBMConfig.RBMConfig.rbmCombatEnabled;
            float thrustBonus = RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
            lock (_cacheLock)
            {
                if (_cachePrimed && _cacheRbmCombat == rbmCombat && _cacheThrustBonus == thrustBonus)
                {
                    return;
                }
                _cacheRbmCombat = rbmCombat;
                _cacheThrustBonus = thrustBonus;
                _powerCache.Clear();
                _tierCache.Clear();
                _cachePrimed = true;
            }
        }

        // =========================================================================================================
        // THE PATCH
        // =========================================================================================================

        /// <summary>
        /// One stack of one roster, priced: which troop, how many of him stood, and what they are worth together.
        /// <see cref="Total"/> already carries the party's morale, so a caller may add these up across parties
        /// without knowing anything about the party they came from.
        /// </summary>
        internal struct StackPower
        {
            public CharacterObject Troop;

            /// <summary>The healthy only. The total was built from them alone, so they are the men it is an average of.</summary>
            public int Healthy;

            public float Total;
        }

        // Who is currently being asked to show his working, and where to put it. The tooltip cannot simply re-run the
        // loop itself: which side a party is on and what ground it stands on are decided inside
        // PartyBase.CalculateCurrentStrength, and a second copy of that reasoning would be free to drift from the bar
        // it is supposed to explain. So the working is taken from the real pricing, as it happens.
        [ThreadStatic]
        private static PartyBase _captureFor;

        [ThreadStatic]
        private static List<StackPower> _captureInto;

        /// <summary>
        /// Prices <paramref name="party"/> exactly as the strength bar does, and writes down each stack on the way.
        /// Returns false -- leaving <paramref name="into"/> empty -- when this model did not price the party, which is
        /// every case where vanilla answered instead: the feature is off, or the party was not one we could read.
        /// </summary>
        internal static bool TryExplainParty(PartyBase party, List<StackPower> into, out float total)
        {
            total = 0f;
            if (party == null || into == null || !Enabled)
            {
                return false;
            }
            into.Clear();
            _captureFor = party;
            _captureInto = into;
            try
            {
                total = party.CalculateCurrentStrength();
            }
            finally
            {
                _captureFor = null;
                _captureInto = null;
            }
            return into.Count > 0;
        }

        /// <summary>
        /// The whole strength of one side of a battle: every party standing on it, priced as the map itself prices
        /// it, and summed. This is the number the encounter bar shows and the AI weighs -- RBM's own when the model
        /// is on (CalculateCurrentStrength routes through the patched GetPowerOfParty above) and vanilla's when it is
        /// off, so it needs no separate path for either. Zero for a missing or empty side; never throws, since it is
        /// only ever asked for a log line or a prompt.
        /// </summary>
        internal static float SidePower(MapEventSide side)
        {
            return SidePower(side, false);
        }

        /// <summary>
        /// As above, but with <paramref name="discountLanding"/> the caller can ask for each party to be priced at only
        /// the share of it that can come off its ships -- the same amphibious discount the raid-decision AI is given
        /// (see <see cref="LandingFactor"/>). Passed true only for the ATTACKER of a naval raid, so the logged
        /// AttackerPower shows the landing party the AI weighed rather than the whole manifest that never gets ashore.
        /// The defenders stand on land and are always priced whole.
        /// </summary>
        internal static float SidePower(MapEventSide side, bool discountLanding)
        {
            if (side == null)
            {
                return 0f;
            }
            float total = 0f;
            foreach (MapEventParty mapEventParty in side.Parties)
            {
                PartyBase party = (mapEventParty != null) ? mapEventParty.Party : null;
                if (party == null)
                {
                    continue;
                }
                try
                {
                    float strength = party.CalculateCurrentStrength();
                    if (discountLanding)
                    {
                        strength *= LandingFactor(party);
                    }
                    total += strength;
                }
                catch (Exception)
                {
                }
            }
            return total;
        }

        [HarmonyPatch(typeof(DefaultMilitaryPowerModel), "GetPowerOfParty")]
        internal static class GetPowerOfPartyPatch
        {
            private static bool Prefix(PartyBase party, BattleSideEnum side, MapEvent.PowerCalculationContext context,
                ref float __result)
            {
                if (!Enabled)
                {
                    return true;
                }
                try
                {
                    float power;
                    if (!TryGetPowerOfParty(party, side, context, out power))
                    {
                        return true;
                    }
                    __result = power;
                    return false;
                }
                catch (Exception)
                {
                    // Strength is asked for constantly, from the AI's own tick. A throw here would not be a wrong
                    // number, it would be a dead campaign -- so anything unexpected hands the question back to vanilla.
                    return true;
                }
            }
        }

        /// <summary>
        /// Vanilla's own loop, with the tier base AND the FIELD terrain modifier taken out of it. The rest is kept
        /// deliberately: the healthy count (a wounded man fights in no one's line) and the morale map -- those are not
        /// this feature's business, and a party that disagreed with vanilla about how many men it has would be a bug
        /// wearing a balance change's clothes. Field terrain is dropped on purpose: vanilla's GetContextModifier is a
        /// per-arm guess (archers weak in a wood, infantry strong there) that this model, which prices the man
        /// himself, must not layer on top. A SIEGE'S context is the exception and IS kept -- the wall is a real fact
        /// about the fight, not a guess about the ground -- see the note at the perMan line below.
        /// </summary>
        private static bool TryGetPowerOfParty(PartyBase party, BattleSideEnum side,
            MapEvent.PowerCalculationContext context, out float result)
        {
            result = 0f;
            if (party == null || party.MemberRoster == null || Campaign.Current == null
                || Campaign.Current.Models == null)
            {
                return false;
            }
            MilitaryPowerModel model = Campaign.Current.Models.MilitaryPowerModel;
            if (model == null)
            {
                return false;
            }

            EnsureCacheFresh();

            bool estimated = context == MapEvent.PowerCalculationContext.Estimated;
            float total = 0f;

            // Running headcount of the men actually priced below, kept only for the amphibious landing discount at
            // the foot of this method (see AmphibiousLandingFactor). Wounded are already dropped, so this is the
            // fighting strength -- exactly the number the landing capacity is measured against.
            int healthyMen = 0;

            // Only ever non-null while TryExplainParty is on the stack above us, for this exact party.
            List<StackPower> capture = (_captureFor == party) ? _captureInto : null;

            // Worked out before the men are, though it is not applied to the total until after them. It depends on
            // nothing the loop does, and a captured stack has to leave here carrying it: the whole point of the
            // capture is that stacks can be added up ACROSS parties, and a stack that still owed its own party's
            // morale could not be.
            float morale = MoraleOf(party, estimated);

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
                healthyMen += healthy;

                float power = PowerOf(troop);
                if (power <= 0f)
                {
                    // Nothing measurable about him -- a villager with a stick, or an item this model could not read.
                    // He is not worth nothing, he is worth what vanilla always said he was.
                    //
                    // This line only became true when PowerScale did. Before it, vanilla's answer -- 0.4 to 2.56 --
                    // was dropped into a sum of men priced in the hundreds, so the man this branch exists to rescue
                    // was rescued into counting for nothing at all, about 197x under his neighbours. Both prices are
                    // in vanilla's units now, which is the whole reason the divide belongs in Measure and not on the
                    // party total below: down there it would divide this one a second time.
                    power = model.GetDefaultTroopPower(troop);
                }

                // What his commander is worth to him: his staying power, and not a percentage. See HealthFactorOf.
                power *= HealthFactorOf(troop, party);

                // FIELD TERRAIN IS NOT APPLIED; A SIEGE'S CONTEXT IS. Vanilla's GetContextModifier is a per-arm
                // heuristic -- archers weak in a wood, cavalry weak in a wood, infantry strong there -- layered on top
                // of the tier it priced everyone by. On open ground that double-counts what this model already prices
                // in the man himself: it once halved a Battanian Highborn Youth -- a noble ARCHER -- in a forest and
                // landed him level with a Looter, whom vanilla files as INFANTRY and rewards on the same ground,
                // though the youth is worth better than twice him. So field terrain is dropped. A SIEGE is a different
                // fact: attacking or defending a wall genuinely changes what a man is worth to the party the AI is
                // weighing, and that belongs in the strength it reads -- so the context is KEPT for a siege alone.
                bool siege = context == MapEvent.PowerCalculationContext.Siege;
                float contextMod = siege ? model.GetContextModifier(troop, side, context) : 0f;

                // Vanilla's own leader term, left exactly as vanilla computes it. It is worth nearly nothing -- it
                // counts only PrimaryRole == Captain perks, of which the game has two -- but fixing that is not this
                // model's business, and the (1 + leader + context) shape is kept intact.
                float leaderMod = (party.LeaderHero != null) ? party.LeaderHero.PowerModifier : 0f;

                float perMan = power * (1f + leaderMod + contextMod);
                total += healthy * perMan;

                if (capture != null)
                {
                    StackPower stack;
                    stack.Troop = troop;
                    stack.Healthy = healthy;
                    stack.Total = healthy * perMan * morale;
                    capture.Add(stack);
                }
            }

            result = total * morale;

            // AN AT-SEA PARTY IS ONLY AS STRONG ASHORE AS IT CAN PUT ASHORE. A raider carrying 223 men whose shallow-
            // draft hulls seat 30 lands 30 a wave and feeds them into the defenders piecemeal -- the War Sails sim caps
            // the beach party to that deck crew (NavalDLCCombatSimulationModel.GetParticipatingTroopCount), and the
            // remaining 193 never touch the fight. Yet the raid decision weighs the WHOLE party: the AI's own strength
            // for target scoring is GetTotalLandStrengthWithFollowers, which prices the raider through this method with
            // the PlainBattle context, so an amphibious raider reads three times the strength it can actually land and
            // commits to raids it then loses. Discount the LAND strength of an at-sea party by the fraction of it that
            // can come off the ships, so the AI weighs the landing party, not the manifest. See AmphibiousLandingFactor.
            result *= AmphibiousLandingFactor(party, context, healthyMen);

            // The one call into the log, and it asks first: building a block walks the perk table and formats a row
            // per stack, which must not happen on the thousands of prices that will never be written down.
            if (StrategicPowerLog.ShouldWrite(party))
            {
                StrategicPowerLog.WriteParty(party, side, context, morale, result);
            }
            return true;
        }

        /// <summary>
        /// The least an amphibious raider's land strength can be discounted to. A party at sea whose hulls are all
        /// deep-water can put no one ashore and by the letter of the cap is worth nothing on land -- true, but a raw
        /// zero is a brittle thing to hand a scoring model (it turns "a poor raid" into "no party at all"), so the
        /// discount bottoms out here and leaves a token weight. It never fires for a party with a shallow hull, which
        /// is every party that would actually be choosing to raid.
        /// </summary>
        private const float MinLandingFactor = 0.1f;

        /// <summary>
        /// How much of an at-sea party's land strength it can actually put on a beach, in [<see cref="MinLandingFactor"/>, 1].
        /// Mirrors the War Sails auto-resolve cap (NavalDLCCombatSimulationModel.GetShallowShipDeckCrewCapacity): only
        /// hulls that can navigate shallow water land men, and only as many as their main deck seats. The fraction of
        /// the party's fighting men that fits aboard those hulls is the fraction of its strength that can land.
        ///
        /// Fires for ONE case: the <see cref="MapEvent.PowerCalculationContext.PlainBattle"/> land-strength query on a
        /// party that is currently at sea. That query is <c>MobileParty.GetTotalLandStrengthWithFollowers</c>, the
        /// number the raid-decision AI weighs as its own strength. <c>GetContextForPosition</c> never returns
        /// PlainBattle for a party on the water, so the ordinary strength bar and the sea-battle prices (which run
        /// under SeaBattle/OpenSeaBattle, where every embarked man does fight) are untouched -- and so is the tooltip
        /// capture path, which prices through <c>CalculateCurrentStrength</c> under a sea context. A party with no
        /// ships -- every party when War Sails is absent -- is never at sea, so this reads 1 and the feature no-ops
        /// with no DLC present and no reflection needed.
        /// </summary>
        private static float AmphibiousLandingFactor(PartyBase party, MapEvent.PowerCalculationContext context, int healthyMen)
        {
            if (context != MapEvent.PowerCalculationContext.PlainBattle)
            {
                return 1f;
            }

            MobileParty mobile = party.IsMobile ? party.MobileParty : null;
            if (mobile == null || !mobile.IsCurrentlyAtSea)
            {
                return 1f;
            }

            return LandingFactorFromCapacity(ShallowLandingCapacity(party), healthyMen);
        }

        /// <summary>
        /// The landing discount for a party priced by its SHIPS AND MEN alone, without the context or at-sea guards
        /// <see cref="AmphibiousLandingFactor"/> wraps it in -- for a caller that already knows the fight is an
        /// amphibious raid and only wants the number. That caller is the sim log, so the AttackerPower it prints is
        /// the strength the raid-decision AI actually weighed (the landing party) rather than the whole manifest.
        /// Returns 1 for a party with no ships, so it is harmless on any side of any battle and no-ops without the DLC.
        /// </summary>
        internal static float LandingFactor(PartyBase party)
        {
            if (party == null || party.MemberRoster == null)
            {
                return 1f;
            }
            return LandingFactorFromCapacity(ShallowLandingCapacity(party), party.MemberRoster.TotalHealthyCount);
        }

        /// <summary>The men a party can put ashore in one wave: its main-deck crew across every shallow-draft hull it
        /// owns. Mirrors NavalDLC's GetShallowShipDeckCrewCapacity. Zero for a party with no shallow hulls (and so for
        /// every party when War Sails is absent, whose ship list is empty).</summary>
        private static int ShallowLandingCapacity(PartyBase party)
        {
            MBReadOnlyList<Ship> ships = (party != null) ? party.Ships : null;
            if (ships == null || ships.Count == 0)
            {
                return 0;
            }

            int landingCapacity = 0;
            foreach (Ship ship in ships)
            {
                if (ship != null && ship.ShipHull != null && ship.ShipHull.CanNavigateShallowWater)
                {
                    landingCapacity += ship.MainDeckCrewCapacity;
                }
            }
            return landingCapacity;
        }

        /// <summary>The fraction of a party's strength that fits into <paramref name="landingCapacity"/> beach slots,
        /// clamped to [<see cref="MinLandingFactor"/>, 1]. A party that can land everyone -- or has no men to land --
        /// is undiscounted.</summary>
        private static float LandingFactorFromCapacity(int landingCapacity, int healthyMen)
        {
            if (healthyMen <= 0 || landingCapacity >= healthyMen)
            {
                return 1f;
            }
            return MathF.Max(MinLandingFactor, (float)landingCapacity / healthyMen);
        }

        /// <summary>
        /// What a shaken party is worth against a steady one -- vanilla's own map, lifted out of the loop only so it
        /// can be asked for before the men are walked. A party that is not mobile has no morale to be shaken.
        /// </summary>
        private static float MoraleOf(PartyBase party, bool estimated)
        {
            if (!party.IsMobile || party.MobileParty == null)
            {
                return 1f;
            }
            if (estimated)
            {
                return MBMath.Map(party.MobileParty.Morale, 20f, 40f, 0.7f, 1f);
            }
            return (party.MobileParty.Morale < 30f) ? 0.7f : 1f;
        }

        // =========================================================================================================
        // THE MAN
        // =========================================================================================================

        /// <summary>What this troop is worth, averaged over the kits he might turn up in. He does not carry all of
        /// them; he carries one, and which one is not ours to know -- so the expectation is the honest answer.</summary>
        internal static float PowerOf(CharacterObject troop)
        {
            if (troop == null)
            {
                return 0f;
            }

            double today = (Campaign.Current != null) ? CampaignTime.Now.ToDays : 0.0;
            lock (_cacheLock)
            {
                // Once a day, let go of the heroes nobody has asked about since yesterday. A stale hero entry is
                // dead weight by definition -- a live hero's is re-measured on his next pricing anyway -- and it is
                // how the cache would otherwise keep every lord who ever died holding his CharacterObject alive.
                // Templates are never evicted: they are a fixed population, and their entries never go stale.
                if (today - _lastSweepDay >= 1.0)
                {
                    _lastSweepDay = today;
                    List<CharacterObject> stale = null;
                    foreach (KeyValuePair<CharacterObject, PowerEntry> pair in _powerCache)
                    {
                        if (pair.Key.IsHero && (today - pair.Value.Day) >= 1.0)
                        {
                            (stale ?? (stale = new List<CharacterObject>())).Add(pair.Key);
                        }
                    }
                    if (stale != null)
                    {
                        foreach (CharacterObject dead in stale)
                        {
                            _powerCache.Remove(dead);
                        }
                    }
                }

                PowerEntry cached;
                if (_powerCache.TryGetValue(troop, out cached))
                {
                    // A template is fixed for good. A lord is not: he buys harness and trains skills, so his
                    // measurement is only good for the day it was taken.
                    if (!troop.IsHero || (today - cached.Day) < 1.0)
                    {
                        return cached.Power;
                    }
                }
            }

            // Measured outside the lock -- two threads may measure the same troop once each, and the second write
            // simply lands on the first's answer. Cheaper than holding every other pricing up behind one kit walk.
            PowerBreakdown detail;
            float power = Measure(troop, out detail);
            PowerEntry entry;
            entry.Power = power;
            entry.Day = today;
            lock (_cacheLock)
            {
                _powerCache[troop] = entry;
            }
            return power;
        }

        /// <summary>
        /// What one man of this kind is made of, taken apart. Only the log asks for this -- the model itself throws
        /// the parts away -- but it is the SAME walk that produces the number, not a second opinion about it, because
        /// a breakdown that was computed differently from the thing it explains is worse than no breakdown.
        /// </summary>
        internal struct PowerBreakdown
        {
            public float Power;

            public float Offense;

            public float Melee;

            public float Ranged;

            public float ActiveFactor;

            public float PassiveFactor;

            public float WeightedArmor;

            public float ShieldTier;

            public bool HasShield;

            // The raw inputs, carried out for the log. Every one of these has to be visible or the dials above are
            // being turned in the dark -- which is exactly how ranged came to be priced at a fifth of melee.
            public float LauncherTier;

            public float ChargeDamage;

            /// <summary>The mount's contribution to power/man (a share of the rider's own) -- 0 on foot. See MountFractionOf.</summary>
            public float MountBonus;

            /// <summary>Whether the game fields him as an archer -- not whether a bow is in his baggage.</summary>
            public bool IsShooter;

            /// <summary>
            /// His training, as plain levels. Printed because 'melee' is an OFFENSE -- class ceiling x skill -- and
            /// the class dominates it, so the number cannot answer whether one troop is better TRAINED than another.
            /// A crossbowman with a good arm and a short sidearm reads low either way, and the two causes look
            /// identical from outside.
            /// </summary>
            public float MeleeSkill;

            public float RangedSkill;

            /// <summary>What he actually looses with, and its raw MissileSpeed -- the number RBM's ranged tier
            /// formula reads as a DRAW WEIGHT. Printed because a tier of 6.5 tells you nothing about why.</summary>
            public string LauncherName;

            public float LauncherSpeed;

            /// <summary>How many kits he was averaged over. One man, several ways he might turn up.</summary>
            public int Sets;
        }

        private struct SetBreakdown
        {
            public float Offense;

            public float Melee;

            public float Ranged;

            public float ActiveFactor;

            public float PassiveFactor;

            public float WeightedArmor;

            public float ShieldTier;

            public bool HasShield;

            public float LauncherTier;

            public float ChargeDamage;

            public bool IsShooter;

            public string LauncherName;

            public float LauncherSpeed;

            public float MountBonus;
        }

        internal static PowerBreakdown Explain(CharacterObject troop)
        {
            PowerBreakdown detail;
            Measure(troop, out detail);
            return detail;
        }

        private static float Measure(CharacterObject troop, out PowerBreakdown detail)
        {
            detail = default(PowerBreakdown);
            bool rbmCombat = RBMConfig.RBMConfig.rbmCombatEnabled;

            float sum = 0f;
            int sets = 0;
            float offense = 0f, melee = 0f, ranged = 0f, active = 0f, passive = 0f, armor = 0f, shieldTier = 0f;
            float launcherTier = 0f, charge = 0f, mountBonus = 0f;

            foreach (Equipment set in EnumerateBattleEquipments(troop))
            {
                SetBreakdown parts;
                float power = PowerOfSet(troop, set, rbmCombat, out parts);
                if (power <= 0f)
                {
                    continue;
                }
                sum += power;
                sets++;
                mountBonus += parts.MountBonus;
                offense += parts.Offense;
                melee += parts.Melee;
                ranged += parts.Ranged;
                active += parts.ActiveFactor;
                passive += parts.PassiveFactor;
                armor += parts.WeightedArmor;
                shieldTier += parts.ShieldTier;
                launcherTier += parts.LauncherTier;
                charge += parts.ChargeDamage;
                detail.HasShield |= parts.HasShield;
                detail.IsShooter |= parts.IsShooter;
                if (parts.LauncherName != null && detail.LauncherName == null)
                {
                    detail.LauncherName = parts.LauncherName;
                    detail.LauncherSpeed = parts.LauncherSpeed;
                }
            }

            if (sets == 0)
            {
                return 0f;
            }

            detail.Sets = sets;
            // The one place the divide happens, because it is the one walk both PowerOf and Explain come through --
            // so the price the AI reads and the price the log explains cannot drift apart. Note the parts below stay
            // RAW: offense is joules and blows and has a meaning of its own, and dividing it by a number that exists
            // only to talk to vanilla would make the log unreadable to the person calibrating the model. The
            // consequence is that Power is no longer offense x active x passive as printed -- it is that PLUS the mount,
            // all of it over PowerScale -- and the log's header says so. The mount is already inside sum (PowerOfSet
            // adds it raw), so the single divide below carries it too; MountBonus is divided out only for the log, to
            // show the mount in the same power/man units as the column it feeds.
            detail.Power = (sum / sets) / PowerScale;
            detail.MountBonus = (mountBonus / sets) / PowerScale;
            detail.Offense = offense / sets;
            detail.Melee = melee / sets;
            detail.Ranged = ranged / sets;
            detail.ActiveFactor = active / sets;
            detail.PassiveFactor = passive / sets;
            detail.WeightedArmor = armor / sets;
            detail.ShieldTier = shieldTier / sets;
            detail.LauncherTier = launcherTier / sets;
            detail.ChargeDamage = charge / sets;
            detail.MeleeSkill = MeleeSkillOf(troop);
            detail.RangedSkill = RangedSkillOf(troop);
            return detail.Power;
        }

        /// <summary>
        /// One kit's worth: what he does to someone, times how long he lasts.
        ///
        /// The two multiply because they are not two opinions about the same thing -- they are the two halves of what
        /// a soldier IS. A man in plate with a stick and a naked man with a war hammer are both useless, and any sum
        /// of the two terms would call them adequate.
        /// </summary>
        private static float PowerOfSet(CharacterObject troop, Equipment set, bool rbmCombat, out SetBreakdown parts)
        {
            parts = default(SetBreakdown);

            bool hasShield;
            float shieldTier;
            float launcherTier, launcherSpeed;
            string launcherName;
            float melee = MeleeOffense(troop, set, rbmCombat, out hasShield, out shieldTier);
            float ranged = RangedOffense(troop, set, rbmCombat, out launcherTier, out launcherName, out launcherSpeed);

            // WHO SHOOTS is a fact about the soldier, not about whether a bow is in his baggage. This asked only
            // whether he owned one, and so priced Nidir -- a mounted lord with a bow on his back -- as a man who
            // spends seven tenths of a battle shooting it. He came out below his own line troops. A man is an archer
            // if the game fields him as one; anyone else with a bow is a soldier who happens to carry a bow.
            bool shooter = ranged > 0f && SimulationEquipmentPower.IsRangedTroop(troop);

            // And a bow can never make a man WORSE than the steel he already had. The blend is an expectation about
            // how he spends the battle, not a punishment for what he packed: if his bow is the poorer weapon he
            // simply draws his sword, so the blend may lift him and must never drag him under his own melee. The
            // Nord Huntsman -- better sword than a Nord Warrior -- was priced a fifth under him for owning a bow.
            float offense = melee;
            if (shooter)
            {
                float blended = (RangedShare * ranged) + ((1f - RangedShare) * melee);
                if (blended > offense)
                {
                    offense = blended;
                }
                offense *= RangedOffenseWeight;
            }
            if (offense <= 0f)
            {
                return 0f;
            }
            float charge = ChargeDamageOf(set);
            offense *= 1f + (ChargeWeight * charge);

            // ACTIVE -- the blows he turns aside outright, which is a thing he DOES and so is priced on his training.
            // A shield makes it markedly easier, and that is nearly the whole worth of carrying one.
            float skillFrac = MBMath.ClampFloat(MeleeSkillOf(troop) / SkillSaturation, 0f, 1f);
            float active = hasShield
                ? MBMath.ClampFloat(ShieldDefenseBase + (ShieldDefenseSkillCoeff * skillFrac), 0f, DefenseChanceCap)
                : MBMath.ClampFloat(WeaponDefenseFloor + (WeaponDefenseSkillCoeff * skillFrac), 0f, DefenseChanceCap);
            float activeFactor = MathF.Pow(1f / (1f - active), ActiveDefenseDamping);

            // PASSIVE -- what is left of a blow he did NOT turn aside. This is also where the shield answers an arrow:
            // nobody parries one, but a board on your arm is in the way whether you saw it coming or not.
            float head, neck, torso, shoulder, arm, leg;
            SimulationEquipmentPower.GetArmorZones(set, rbmCombat, out head, out neck, out torso, out shoulder,
                out arm, out leg);
            float weighted = (head * ZoneHead) + (neck * ZoneNeck) + (torso * ZoneTorso)
                           + (shoulder * ZoneShoulder) + (arm * ZoneArm) + (leg * ZoneLeg);
            // Barding is NOT here -- it is the horse's armour, and it is priced in the mount term (MountFractionOf).
            weighted += ShieldPassiveWeight * shieldTier;
            // ArmorConstant is the vanilla (armorMultiplier == 1) value. Under RBM Combat the real blow divides
            // armour by 100/(100 + armor*armorMultiplier), so the passive term only says what a real blow says when
            // the divisor tracks 100/armorMultiplier. At the default multiplier of 2 this doubles armour's weight --
            // exactly the "price of protection" RBM's own armour equation charges. See ArmorConstant's own summary.
            float armorConstant = rbmCombat
                ? (ArmorConstant / RBMConfig.RBMConfig.armorMultiplier)
                : ArmorConstant;
            float passiveFactor = 1f + (weighted / armorConstant);

            parts.Offense = offense;
            parts.Melee = melee;
            parts.Ranged = ranged;
            parts.ActiveFactor = activeFactor;
            parts.PassiveFactor = passiveFactor;
            parts.WeightedArmor = weighted;
            parts.ShieldTier = shieldTier;
            parts.HasShield = hasShield;
            parts.LauncherTier = launcherTier;
            parts.ChargeDamage = charge;
            parts.IsShooter = shooter;
            parts.LauncherName = launcherName;
            parts.LauncherSpeed = launcherSpeed;
            // The two factors are stages of one blow -- first it must fail to be turned aside, then it must get through
            // the armour -- so what each buys him in life multiplies rather than adds. That product IS the man. The
            // mount then adds a SHARE of it (MountFractionOf), on the same raw scale, so it rides the /PowerScale
            // divide back in Measure like everything else.
            float product = offense * activeFactor * passiveFactor;
            float mount = product * MountFractionOf(set);
            parts.MountBonus = mount;
            return product + mount;
        }

        /// <summary>
        /// The share of his OWN power a man's mount adds -- 0 for a man on foot. A horse is worth a fraction of the
        /// rider it carries, and how large that fraction is depends on how survivable the animal is: its own health
        /// (Monster hit points plus the item's bonus, the same figure the auto-resolve model wears down) and its
        /// barding, which is the only place barding is priced. Scaled off a barded warhorse as the yardstick (see
        /// ReferenceMountSurvival / MountBonusAtReference), so a lighter mount lands below it and a heavier above --
        /// nothing is clamped. Charge is deliberately absent -- it is the rider's harder blow, priced in offense.
        /// </summary>
        private static float MountFractionOf(Equipment set)
        {
            ItemObject horse = set[EquipmentIndex.Horse].Item;
            if (horse == null || horse.HorseComponent == null)
            {
                return 0f;
            }
            float horseHealth = horse.HorseComponent.HitPoints + horse.HorseComponent.HitPointBonus;
            float survival = horseHealth + (BardingToHealth * BardingOf(set));
            return (survival / ReferenceMountSurvival) * MountBonusAtReference;
        }

        // =========================================================================================================
        // OFFENSE
        // =========================================================================================================

        private static float MeleeOffense(CharacterObject troop, Equipment set, bool rbmCombat,
            out bool hasShield, out float shieldTier)
        {
            hasShield = false;
            shieldTier = 0f;

            float best = 0f;
            float sum = 0f;
            int count = 0;

            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
            {
                ItemObject item = set[i].Item;
                if (item == null)
                {
                    continue;
                }
                WeaponComponentData weapon = item.PrimaryWeapon;
                if (weapon == null)
                {
                    continue;
                }
                if (weapon.IsShield)
                {
                    hasShield = true;
                    float tier = TierfOf(item);
                    if (tier > shieldTier)
                    {
                        shieldTier = tier;
                    }
                    continue;
                }
                // A bow is not a sword, and a javelin's listed ThrustDamage is its MISSILE damage. With RBM Combat
                // on, ClassCeiling happened to drop these at 0; with it off, the listed-damage branch read a Fian's
                // bow as his best "melee" weapon and priced every javelin-carrier's throw as a sword blow. Launchers
                // are RangedOffense's business and thrown are deliberately unpriced -- neither belongs here.
                if (weapon.IsRangedWeapon || weapon.IsAmmo)
                {
                    continue;
                }
                float score = MeleeWeaponScore(troop, item, weapon, rbmCombat);
                if (score <= 0f)
                {
                    continue;
                }
                if (score > best)
                {
                    best = score;
                }
                sum += score;
                count++;
            }

            if (count == 0)
            {
                return 0f;
            }
            return (BestWeaponWeight * best) + ((1f - BestWeaponWeight) * (sum / count));
        }

        /// <summary>
        /// Under RBM Combat a weapon's LISTED damage is not what it does. The blow collapses onto a per-class clamp
        /// (SimulationWeaponModel.GetMeleeClamp) -- a function of what KIND of weapon it is and how well he was taught
        /// -- and the weapon's own quality survives only as penetration. So the class is the number, not the tier.
        ///
        /// That matters most for two-handers, and the tier would get them exactly backwards: RBM's own tier formula
        /// DIVIDES two-handed swords, axes and maces by 1.3 (deliberately -- a two-hander costs you your shield, so it
        /// is priced as comparable WORTH), while the combat model makes them 1.33x as deadly. Price a great axe off
        /// its tier and it reads weaker than a hand axe.
        ///
        /// With RBM Combat off, the game really does use the listed damage, so we do too.
        /// </summary>
        private static float MeleeWeaponScore(CharacterObject troop, ItemObject item, WeaponComponentData weapon,
            bool rbmCombat)
        {
            float skillFrac = SkillFracFor(troop, weapon.RelevantSkill);

            if (!rbmCombat)
            {
                float listed = Math.Max(weapon.SwingDamage, weapon.ThrustDamage);
                if (listed <= 0f)
                {
                    return 0f;
                }
                return listed * (1f + (SkillOffenseSpread * skillFrac));
            }

            bool blunt = BestDamageTypeOf(weapon) == DamageTypes.Blunt;
            float ceiling = ClassCeiling(weapon.WeaponClass, blunt);
            if (ceiling <= 0f)
            {
                return 0f;
            }
            return ceiling * (1f + (SkillOffenseSpread * skillFrac)) * Penetration(DamageFactorOf(weapon));
        }

        /// <summary>
        /// <c>max * scale</c> out of SimulationWeaponModel.GetMeleeClamp -- the hardest a class of weapon can be made
        /// to hit. Duplicated rather than called because that method is private to the auto-resolve model and this is
        /// meant to be liftable out in one piece; KEEP THE TWO IN STEP if that table is ever retuned.
        /// </summary>
        private static float ClassCeiling(WeaponClass weaponClass, bool blunt)
        {
            switch (weaponClass)
            {
                case WeaponClass.Dagger:
                case WeaponClass.OneHandedSword:
                    return blunt ? (20f * 1.6f) : (15f * 4.6f);

                case WeaponClass.TwoHandedSword:
                    return blunt ? (26f * 1.6f) : (20f * 4.6f);

                case WeaponClass.OneHandedAxe:
                    return blunt ? (20f * 1.2f) : (18f * 4.6f);

                case WeaponClass.TwoHandedAxe:
                    return blunt ? (26f * 1.2f) : (24f * 4.6f);

                case WeaponClass.Mace:
                    return 15f * 4.6f;

                case WeaponClass.TwoHandedMace:
                    return 22f * 4.6f;

                case WeaponClass.OneHandedPolearm:
                    return blunt ? (20f * 1.2f) : (24f * 4f);

                case WeaponClass.TwoHandedPolearm:
                case WeaponClass.LowGripPolearm:
                    return blunt ? (26f * 1.2f) : (28f * 4f);

                default:
                    return 0f;
            }
        }

        /// <summary>
        /// The bow and the shaft both throw the shot -- the bow lends it speed, the arrow lends it mass and a head --
        /// so both tiers are in the magnitude. Unlike melee, the tier is a fair proxy here: RBM's missile model is
        /// kinetic, worked from real draw weights and real shaft weights, with no class clamp anywhere in it, so the
        /// numbers the tier is computed from are the numbers the blow is computed from.
        ///
        /// And it needs no rbmCombat branch: RBM patches CalculateRangedWeaponTier and CalculateAmmoTier, so Tierf is
        /// already whichever model is running.
        /// </summary>
        private static float RangedOffense(CharacterObject troop, Equipment set, bool rbmCombat,
            out float launcherTierOut, out string launcherNameOut, out float launcherSpeedOut)
        {
            launcherTierOut = 0f;
            launcherNameOut = null;
            launcherSpeedOut = 0f;

            ItemObject launcher = null;
            WeaponComponentData launcherWeapon = null;
            float launcherTier = 0f;

            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
            {
                ItemObject item = set[i].Item;
                if (item == null)
                {
                    continue;
                }
                WeaponComponentData weapon = item.PrimaryWeapon;
                if (weapon == null || !weapon.IsRangedWeapon || weapon.IsAmmo)
                {
                    continue;
                }
                float energy = LauncherEnergyOf(item, weapon);
                if (launcher == null || energy > launcherTier)
                {
                    launcher = item;
                    launcherWeapon = weapon;
                    launcherTier = energy;
                }
            }
            if (launcher == null)
            {
                return 0f;
            }

            // He still has to have something to loose. The shaft is NOT priced -- the bow is the weapon and the arrow
            // is what it spends, and the two are not comparable things -- but a bow with nothing on the string is a
            // stick, so its presence is checked and nothing more.
            bool foundAmmo = false;
            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
            {
                ItemObject item = set[i].Item;
                if (item == null)
                {
                    continue;
                }
                WeaponComponentData weapon = item.PrimaryWeapon;
                if (weapon == null || !weapon.IsAmmo)
                {
                    continue;
                }
                if (launcherWeapon.AmmoClass == weapon.WeaponClass)
                {
                    foundAmmo = true;
                    break;
                }
            }
            if (!foundAmmo)
            {
                return 0f;
            }

            launcherTierOut = launcherTier;
            launcherNameOut = (launcher.Name != null) ? launcher.Name.ToString() : launcher.StringId;
            launcherSpeedOut = launcherWeapon.MissileSpeed;

            float skillFrac = SkillFracFor(troop, launcherWeapon.RelevantSkill);
            return RangedEnergyScale * launcherTier * (1f + (SkillOffenseSpread * skillFrac));
        }

        /// <summary>
        /// What a launcher actually throws, in joules -- RBM's own physics, not its tier.
        ///
        /// TIER WAS THE WRONG CURRENCY HERE, and slings were only the loudest symptom. RBM's ranged tiers are two
        /// unrelated price curves: a bow is (drawWeight-60)*0.049 and a crossbow is (drawWeight-250)*0.021. Nothing
        /// makes those comparable, and measured against the roster they are not: a Vlandian Sharpshooter's 280lb
        /// crossbow tiers at 0.63 against a Battanian Fian's 163lb bow at 5.06 -- eight times less -- while
        /// carrying HALF AGAIN THE ENERGY (278 J against 192 J). A tier prices what a thing is worth in a shop. It
        /// was never a statement about what it does to a man.
        ///
        /// So the launcher is priced the way SimulationWeaponModel.GetMissileMagnitude prices it, off the same
        /// constants, because MissileSpeed is not a speed at all: RBM overwrites it with the draw weight in pounds
        /// (RangedRework caches the XML value into RangedWeaponStats and writes it back). Bows, crossbows and slings
        /// then sit on ONE scale -- the energy each puts behind the shot -- and no tier is involved anywhere.
        /// </summary>
        private static float LauncherEnergyOf(ItemObject item, WeaponComponentData weapon)
        {
            // For a sling this number is the sling's LENGTH, not a draw weight (see SlingEnergy), so the physics
            // below cannot be run on it at all.
            if (weapon.WeaponClass == WeaponClass.Sling || item.ItemType == ItemObject.ItemTypeEnum.Sling)
            {
                return SlingEnergy;
            }

            float drawWeight = weapon.MissileSpeed;
            if (drawWeight <= 0f)
            {
                return 0f;
            }

            bool crossbow = weapon.WeaponClass == WeaponClass.Crossbow
                || item.ItemType == ItemObject.ItemTypeEnum.Crossbow;

            // RBM branches a longbow on the ITEM USAGE string and not the class -- a yew stave wastes more of the
            // draw than a composite horn-and-sinew bow -- and most of RBM's bows are longbows, so reading only the
            // class would quietly make every one of them a horsebow.
            bool longBow = !crossbow && weapon.ItemUsage == "long_bow";

            float powerstroke = crossbow ? CrossbowPowerstroke : BowPowerstroke;
            float efficiency = crossbow ? CrossbowEfficiency : (longBow ? LongBowEfficiency : BowEfficiency);
            float energy = 0.5f * (drawWeight * PoundsToNewtons) * powerstroke * efficiency;

            // A crossbow buys its power with its reload, and that is the whole of the trade it makes: RBM's
            // crossbows draw 250-300lb against a bow's 60-163, so on energy alone a crossbowman would read as
            // roughly twice a bowman. He does not shoot twice as often -- he shoots far less often. This model has
            // no clock in it (the sim keeps that in its phase/ammo counter), so the trade has to be paid here.
            if (crossbow)
            {
                energy /= CrossbowReloadDivisor;
            }
            return energy;
        }

        /// <summary>A better-made weapon of the same kind bites deeper. Small, and it is meant to be small.</summary>
        private static float Penetration(float damageFactor)
        {
            float quality = MathF.Sqrt(Math.Max(damageFactor, 0.01f));
            return 1f + (PenetrationWeight * (quality - 1f));
        }

        private static float DamageFactorOf(WeaponComponentData weapon)
        {
            float factor = (weapon.SwingDamage >= weapon.ThrustDamage)
                ? weapon.SwingDamageFactor
                : weapon.ThrustDamageFactor;
            return (factor > 0f) ? factor : 1f;
        }

        private static DamageTypes BestDamageTypeOf(WeaponComponentData weapon)
        {
            return (weapon.SwingDamage >= weapon.ThrustDamage) ? weapon.SwingDamageType : weapon.ThrustDamageType;
        }

        private static float ChargeDamageOf(Equipment set)
        {
            ItemObject horse = set[EquipmentIndex.Horse].Item;
            if (horse == null || horse.HorseComponent == null)
            {
                return 0f;
            }
            return horse.HorseComponent.ChargeDamage;
        }

        private static float BardingOf(Equipment set)
        {
            ItemObject harness = set[EquipmentIndex.HorseHarness].Item;
            if (harness == null || harness.ArmorComponent == null)
            {
                return 0f;
            }
            return harness.ArmorComponent.BodyArmor;
        }

        /// <summary>His best hand with a melee weapon -- what he blocks and parries with, whatever else he carries.</summary>
        private static float MeleeSkillOf(CharacterObject troop)
        {
            int oneHanded = troop.GetSkillValue(DefaultSkills.OneHanded);
            int twoHanded = troop.GetSkillValue(DefaultSkills.TwoHanded);
            int polearm = troop.GetSkillValue(DefaultSkills.Polearm);
            return Math.Max(oneHanded, Math.Max(twoHanded, polearm));
        }

        /// <summary>The best of his shooting hands, as a plain level -- for the log, so training can be read apart
        /// from the weapon it is holding.</summary>
        private static float RangedSkillOf(CharacterObject troop)
        {
            int bow = troop.GetSkillValue(DefaultSkills.Bow);
            int crossbow = troop.GetSkillValue(DefaultSkills.Crossbow);
            int throwing = troop.GetSkillValue(DefaultSkills.Throwing);
            return Math.Max(bow, Math.Max(crossbow, throwing));
        }

        private static float SkillFracFor(CharacterObject troop, SkillObject skill)
        {
            if (skill == null)
            {
                return 0f;
            }
            return MBMath.ClampFloat(troop.GetSkillValue(skill) / SkillSaturation, 0f, 1f);
        }

        /// <summary>
        /// Tierf is not a stored number -- it runs the whole tier calculation on every single get (see
        /// ItemObject.Tierf, which calls straight into ItemValueModel.CalculateTier). Reading it in a loop over every
        /// item of every kit of every troop of every party the AI looks at would be ruinous, so it is cached.
        /// </summary>
        private static float TierfOf(ItemObject item)
        {
            float tier;
            lock (_cacheLock)
            {
                if (_tierCache.TryGetValue(item, out tier))
                {
                    return tier;
                }
            }
            try
            {
                tier = item.Tierf;
            }
            catch (Exception)
            {
                tier = 0f;
            }

            // A tier is 0..6, and everything downstream of here assumes it. Tierf does NOT promise that: RBM clamps
            // its melee, ammo and shield tiers at 6.5 and its RANGED one at nothing at all --
            // (MissileSpeed - 60) * 0.049, run free. That is harmless for a bow, whose MissileSpeed is a draw
            // weight in the 60-160 it was fitted to; it is not harmless for a SLING, whose MissileSpeed is the
            // speed of the stone. Slingers came out at tier 12.74 -- twice the best bow in the game -- and a Jawwal
            // Master Slinger priced at 926 against a Battanian Fian's 386, which so poisoned the archer average
            // that every noble bow line in Calradia read as below par.
            //
            // Clamped here rather than in the tier model, because a tier that RBM means to be free is RBM's
            // business; what this file may safely BELIEVE is its own. The game agrees on the range: ItemObject.Tier
            // is ClampInt(Round(Tierf), 0, 6).
            tier = MBMath.ClampFloat(tier, 0f, MaxItemTier);

            lock (_cacheLock)
            {
                _tierCache[item] = tier;
            }
            return tier;
        }

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

        // =========================================================================================================
        // THE COMMANDER
        // =========================================================================================================

        /// <summary>
        /// How much longer this troop lives for having a commander who has learned to keep men alive -- as a
        /// multiple of the hundred a soldier starts with.
        ///
        /// THIS REPLACED A HAND-WRITTEN PERK TALLY, and the reason is worth keeping. That tally tried to decide
        /// which of a commander's perks were "combat" perks by the SKILL they hang off, and no such rule exists:
        /// Athletics carries both Well Built (+hit points to foot troops in your party) and A Good Days Rest
        /// (+regeneration while waiting in settlements); Bow carries both DeadAim and Trainer (daily experience to
        /// your worst archer). Only 13 of the game's 130 PartyLeader perks carry a troop-usage mask, so the mask
        /// cannot sort them either. The tally duly counted a lord's Veterinary -- a perk about his HORSE's health --
        /// as though it made his infantry fight better.
        ///
        /// The right question is not "is this perk about combat" but SimulationPerks' question: does it move a
        /// quantity this model actually prices? For the commander track the answer is hit points, and nothing else:
        /// every troop-HP perk in the game is PartyLeader, his damage perks are hand-coded into vanilla's own blow
        /// (which this model does not touch), and his morale is already applied by GetPowerOfParty's morale factor.
        ///
        /// So it is asked of the one place that already knows: SimulationTroopHitPoints.CommandedHealth, which is
        /// SandboxAgentStatCalculateModel.GetEffectiveMaxHealth transcribed, with the real perks in their real
        /// primary/secondary slots. No list is kept here, so no list here can drift from it.
        ///
        /// dismounted is false because there is no battle: a party on the map is not standing on a wall, and
        /// CommandedHealth only consults it to hand a lancer the foot perks in a siege.
        /// </summary>
        /// <summary>(internal rather than private only so StrategicPowerLog's subtotal can carry the SAME factor
        /// the pricing applied, rather than a second opinion that would stop the rows summing to the total.)</summary>
        internal static float HealthFactorOf(CharacterObject troop, PartyBase party)
        {
            float health = SimulationTroopHitPoints.CommandedHealth(troop, party, dismounted: false);
            if (health <= 0f)
            {
                return 1f;
            }
            return health / BaselineHitPoints;
        }
    }
}
