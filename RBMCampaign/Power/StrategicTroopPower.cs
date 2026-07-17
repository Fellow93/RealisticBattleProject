using HarmonyLib;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
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
        // TUNING. None of these are derived; they are the dials this model is calibrated on. They deliberately live
        // here rather than in the config screen -- the screen carries the on/off switch and nothing else, the same
        // way the auto-resolve equipment model keeps its weight in the config file (see RBMConfigViewModel).
        // ---------------------------------------------------------------------------------------------------------

        /// <summary>Where a blow lands. The armour a man wears is worth what the blows he takes actually meet, so a
        /// greave is worth less than a cuirass for no reason but that fewer blows go there.</summary>
        private const float ZoneHead = 0.16f;

        private const float ZoneNeck = 0.03f;

        private const float ZoneTorso = 0.44f;

        private const float ZoneShoulder = 0.12f;

        private const float ZoneArm = 0.14f;

        private const float ZoneLeg = 0.11f;

        /// <summary>
        /// How many armour points buy one man's worth of extra life. RBM's own armour equation is
        /// <c>100/(100 + armor*armorMultiplier)</c>, so this tracks 100/armorMultiplier -- pick it that way and the
        /// passive term says the same thing about armour that a real blow does.
        /// </summary>
        private const float ArmorConstant = 100f;

        /// <summary>
        /// What a shield is worth as standing cover, per point of its tier. KEPT SMALL ON PURPOSE. A shield already
        /// moves the ACTIVE term from 1.25x to 4.0x, which is the bulk of what carrying one is worth; this is only the
        /// board's own bulk, answering what he never saw coming. Overfeed it and shielded infantry read as strictly
        /// dominant, and the AI will believe it.
        /// </summary>
        private const float ShieldPassiveWeight = 4f;

        /// <summary>Barding is armour on the thing between the blow and the man. Worth less than his own, not nothing.</summary>
        private const float BardingWeight = 0.35f;

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
        /// Vanilla's own loop, with the tier base taken out of it. Everything else is kept deliberately: the healthy
        /// count (a wounded man fights in no one's line), the terrain modifier and its Estimated exemption, and the
        /// morale map. Those are not this feature's business and a party that disagreed with vanilla about how many
        /// men it has would be a bug wearing a balance change's clothes.
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

                float power = PowerOf(troop);
                if (power <= 0f)
                {
                    // Nothing measurable about him -- a villager with a stick, or an item this model could not read.
                    // He is not worth nothing, he is worth what vanilla always said he was.
                    power = model.GetDefaultTroopPower(troop);
                }

                // What his commander is worth to him: his staying power, and not a percentage. See HealthFactorOf.
                power *= HealthFactorOf(troop, party);

                float contextMod = estimated ? 0f : model.GetContextModifier(troop, side, context);

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

            // The one call into the log, and it asks first: building a block walks the perk table and formats a row
            // per stack, which must not happen on the thousands of prices that will never be written down.
            if (StrategicPowerLog.ShouldWrite(party))
            {
                StrategicPowerLog.WriteParty(party, side, context, morale, result);
            }
            return true;
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
            float launcherTier = 0f, charge = 0f;

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
            detail.Power = sum / sets;
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
            weighted += BardingWeight * BardingOf(set);
            weighted += ShieldPassiveWeight * shieldTier;
            float passiveFactor = 1f + (weighted / ArmorConstant);

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

            // The two are stages of one blow -- first it must fail to be turned aside, then it must get through the
            // armour -- so what each buys him in life multiplies rather than adds.
            return offense * activeFactor * passiveFactor;
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
