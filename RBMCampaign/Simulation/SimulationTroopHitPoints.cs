using HarmonyLib;
using Helpers;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// EVERY SOLDIER HAS HIT POINTS NOW, not just the lords.
    ///
    /// Vanilla gives a line trooper no health at all. His life is one coin-flip per blow:
    ///
    ///     else if (MBRandom.RandomInt(_selectedSimulationTroop.MaxHitPoints()) &lt; damage)
    ///
    /// Eight damage against a hundred hit points is not eight points off a bar -- it is an eight per cent chance the
    /// man is simply gone, and a ninety-two per cent chance the blow never happened. Nothing accumulates. A veteran
    /// in plate who has been hacked at for twenty rounds is as fresh as the moment he arrived, and a recruit's lucky
    /// swing can kill a champion outright. Only a HERO gets a real pool (AddHeroDamage), and he gets it four lines
    /// higher up in the same method.
    ///
    /// The game does know who each man is -- for a while. MapEventSide keeps a UniqueTroopDescriptor for the
    /// soldier it has selected, and within one simulated round that descriptor is a man. But a "round" on the map
    /// is a SESSION: every simulation interval MapEvent.SimulateBattleSetup rebuilds each side's muster from the
    /// roster (MapEventParty.Update clears the flattened roster and re-adds every man), and every re-add MINTS A
    /// FRESH DESCRIPTOR. Vanilla never cared -- its coin-flip is memoryless, so it has nothing to lose in the
    /// rebuild. A wound ledger keyed on descriptors alone loses EVERYTHING: each round boundary wiped every man
    /// clean, a man could only die if he was worn through inside a single round, and a battle of thirty ground on
    /// for seventy rounds while nine fields' worth of damage evaporated at the boundaries. (Measured: 31,157
    /// damage dealt onto a field carrying 3,500 hit points, 17 casualties.)
    ///
    /// So the ledger lives on two levels. WITHIN a session, per descriptor -- the man who was hit carries his
    /// wound, exactly as before. When the muster is torn down (SimulateBattleSetup, patched below), the session's
    /// surviving wounds FOLD into a carry per STACK -- (party, character), the only identity that outlives the
    /// rebuild; men of a stack are interchangeable, which is precisely why this is honest. Next session, the first
    /// time a man of that stack is struck, he ADOPTS the worst carried wound as his starting state: the man still
    /// bleeding from the last round is the man who goes down next. Nothing is lost at any boundary, and the log's
    /// hp column finally walks downward across a whole battle instead of resetting with the clock.
    ///
    /// THE TRICK, and the reason this is forty lines instead of a reimplementation: the roll is not replaced, it is
    /// BENT. RandomInt(maxHitPoints) returns 0..maxHitPoints-1, so rewriting `damage` in a prefix makes the outcome
    /// certain in either direction --
    ///
    ///     still standing  -&gt; damage = 0             -&gt; RandomInt(max) &lt; 0    is never true, and he lives
    ///     worn through    -&gt; damage = maxHitPoints  -&gt; RandomInt(max) &lt; max  is always true, and he falls
    ///
    /// -- and everything downstream then runs exactly as it always did: the surgeon's survival roll deciding
    /// whether he is dead or merely wounded, the BattleObserver, the casualty books, the player's kill event.
    /// None of it is reimplemented, so none of it can drift.
    ///
    /// The XP survives too, and that is luck rather than design: MapEvent awards it from its OWN copy of the damage
    /// (`ApplySimulatedHitRewardToSelectedTroop(..., num, flag)`), and a ref prefix only rewrites the callee's. Had
    /// the reward been taken from this method's parameter, zeroing it would have silently stopped every soldier in
    /// Calradia from learning anything in an auto-resolved battle.
    ///
    /// WHAT IT CHANGES IN PLAY: the VARIANCE collapses. Men now die in the order they are worn down; no recruit
    /// fluke-kills a champion, and twenty grazes finally add up to a corpse instead of twenty separate near-misses.
    /// Battles get less swingy and the better army wins more reliably -- and every part of the equipment model bites
    /// harder, because armour that halves a blow now genuinely doubles a man's life instead of halving a lottery ticket.
    ///
    /// The MEAN is no longer untouched, and that is on purpose. The pool is widened by <see cref="LethalityHitPointScale"/>:
    /// a man soaks that many times his native hundred before he falls, so the expected blows to kill him -- maxHP/damage --
    /// rise by the same factor and each blow is proportionally LESS lethal. A single simulated blow was landing far
    /// harder than a real one (the sim compresses a battle into a fraction of its blows, so each carries more), and
    /// widening the pool walks that back toward what a man on the field actually endures. It is the honest knob for it:
    /// it sits downstream of the whole armour-and-kit model and distorts none of it -- it only says how much a man can
    /// take before the last blow tells.
    /// </summary>
    [HarmonyPatch(typeof(MapEventSide), "ApplySimulationDamageToSelectedTroop")]
    internal static class SimulationTroopHitPoints
    {
        /// <summary>
        /// How much of his native hundred a trooper's auto-resolve pool is widened to. 1.0 is native lethality; above
        /// it, a man soaks proportionally more before he falls and each simulated blow lands proportionally softer.
        /// A single sim blow was landing far harder than a real one -- the sim compresses a battle into a fraction of
        /// its blows -- so this walks it back toward the field. Must stay >= 1: the pool trick in
        /// <see cref="MaxHitPoints"/> relies on the pool never dropping below the hundred vanilla rolls against.
        /// </summary>
        internal const float LethalityHitPointScale = 1.25f;

        private static readonly AccessTools.FieldRef<MapEventSide, CharacterObject> SelectedTroop =
            AccessTools.FieldRefAccess<MapEventSide, CharacterObject>("_selectedSimulationTroop");

        private static readonly AccessTools.FieldRef<MapEventSide, UniqueTroopDescriptor> SelectedDescriptor =
            AccessTools.FieldRefAccess<MapEventSide, UniqueTroopDescriptor>("_selectedSimulationTroopDescriptor");

        /// <summary>
        /// One struck man's open wound, and enough to know whose stack he belongs to when his descriptor dies with
        /// the session (see the class note): the fold into the stack carry needs the party and the character, and
        /// they are only cheaply known at the moment he is struck.
        /// </summary>
        private class WoundRecord
        {
            public PartyBase Party;
            public CharacterObject Troop;
            public float Taken;
        }

        /// <summary>
        /// What each man has taken so far THIS SESSION, kept per battle so it can be dropped with the battle.
        /// Several fights run at once across the map, and a campaign that never forgot its wounded would carry
        /// every man it ever scratched. Descriptors are minted fresh each session, so this dictionary is only ever
        /// read within one -- the fold (see <see cref="FoldSessionWounds"/>) is what survives the boundary.
        /// </summary>
        private static readonly Dictionary<MapEvent, Dictionary<UniqueTroopDescriptor, WoundRecord>> _wounds =
            new Dictionary<MapEvent, Dictionary<UniqueTroopDescriptor, WoundRecord>>();

        /// <summary>
        /// The wounds that survived a torn-down session, banked per STACK -- (party, character) is the only
        /// identity the muster rebuild cannot destroy. Each entry is one still-standing man's accumulated wound;
        /// a fresh descriptor of the stack adopts the worst of them the first time he is struck. Entries only exist
        /// for men who were hit and left standing, so the list can never outgrow the stack for long, and whatever
        /// is left when the battle ends is dropped with it.
        /// </summary>
        private static readonly Dictionary<MapEvent, Dictionary<ValueTuple<PartyBase, CharacterObject>, List<float>>> _carried =
            new Dictionary<MapEvent, Dictionary<ValueTuple<PartyBase, CharacterObject>, List<float>>>();

        /// <summary>What the man who was just struck has left. Read by the log, which no longer has to guess.</summary>
        internal static float LastHitPointsLeft = -1f;

        /// <summary>
        /// What a man can take in an auto-resolved battle: his own frame, and what his COMMANDER has made of it.
        ///
        /// A map battle gives every soldier a flat hundred to begin with (DefaultCharacterStatsModel.MaxHitpoints),
        /// and vanilla's auto-resolve leaves it there. A real MISSION does not: SandboxAgentStatCalculateModel.
        /// GetEffectiveMaxHealth hands a well-led foot line up to +28 hit points, and a doctor-lord's men more again.
        /// Fight the battle yourself and your lord's forty years of soldiering keep his men alive; press the button
        /// and they evaporate. What follows is a TRANSCRIPTION of that method -- the same perks, the same
        /// primary/secondary slots, the same conditions -- so that the two agree.
        ///
        /// THIS WAS ONCE REMOVED, AND THE REASON IT IS BACK IS WORTH WRITING DOWN. The stated case for taking it out
        /// was that a soldier's staying power should be his own armour and his own frame, "not a bonus his captain
        /// carries" -- the same principle that took tier and terrain out of the blow. Both halves of that turn out
        /// not to hold:
        ///
        ///   Not his captain's. NONE of these is a captain perk. Every one of them is PartyRole.PartyLeader -- read
        ///   their own descriptions, which say "to troops in your PARTY", where a captain perk always says "in your
        ///   formation". There is, in fact, no hit-point perk anywhere in Bannerlord with a Captain slot. So this was
        ///   never the captain's bonus to carry; it is the COMMANDER's, and the commander's perks were always meant
        ///   to reach every man he brought.
        ///
        ///   Not the same principle. Tier and terrain came out because they were PROXIES -- crude stand-ins for
        ///   things the equipment model could measure directly and better. A perk is not a proxy for anything. It is
        ///   a real effect with a real number on it, and there is nothing else in the model already saying it.
        ///
        /// A HERO is untouched, exactly as in native: GetEffectiveMaxHealth returns a hero's own MaxHitPoints() and
        /// adds no party bonus to him. His personal perks are already inside that number.
        ///
        /// This does not endanger the pool trick below. The trick needs the pool never to fall BELOW the hundred
        /// vanilla rolls RandomInt against, and every perk here is a positive Add on top of that hundred, then
        /// widened again by <see cref="LethalityHitPointScale"/> (>= 1 by construction). Both terms only ever raise
        /// it. "Worn through" hands back a damage of maxHitPoints, still >= 100, and RandomInt(100) &lt; (&gt;=100)
        /// is still always true; "still standing" still hands back zero. Only shrinking the pool under native's
        /// hundred could break it, and nothing here can.
        /// </summary>
        /// <param name="dismounted">Whether this battle has no horses in it. Several of the perks below reach a man
        /// on foot only, and a cavalryman storming a wall IS a man on foot -- in the mission this transcribes he
        /// spawns without a mount and collects them. See SimulationBattleState.IsMountedIn.</param>
        internal static int MaxHitPoints(CharacterObject troop, PartyBase party, bool dismounted)
        {
            if (troop == null)
            {
                return (int)(100 * LethalityHitPointScale);
            }

            // A HERO keeps his OWN pool, UNSCALED. The lethality scale is a trooper knob (see above); a hero's pool is
            // his own, and his Prefix excludes him from the wound trick entirely. But this method also feeds the
            // absolute per-blow cap (SimulationEquipmentPower's simulationAbsoluteBlowCap x maxHitPoints) -- scaling
            // him there would cap his blows against a pool he does not have, and a lord would die faster than the cap
            // dial says. Leave his MaxHitPoints() as it stands.
            if (troop.IsHero)
            {
                int heroPoints = troop.MaxHitPoints();
                return (heroPoints > 1) ? heroPoints : 1;
            }

            // A trooper's own hit points, with his commander's counted in, then widened by the lethality scale --
            // which is a separate lever about how hard a single simulated blow lands, not about whose perks count.
            int hitPoints = (int)(CommandedHealth(troop, party, dismounted) * LethalityHitPointScale);
            return (hitPoints > 1) ? hitPoints : 1;
        }

        /// <summary>
        /// A trooper's hundred, plus whatever his party's commander has learned about keeping men alive.
        ///
        /// SandboxAgentStatCalculateModel.GetEffectiveMaxHealth, transcribed, and deliberately nothing more: the same
        /// perks, the same primary/secondary slots (which matter -- HardyFrontline carries its hit points on its
        /// PRIMARY slot and every other perk here on its secondary, so passing the wrong flag silently reads the
        /// wrong bonus), and the same at-sea, mounted, ranged and infantry conditions. Nothing is invented.
        ///
        /// The one substitution: native asks <c>agent.HasMount</c> of a spawned Agent, and there is no Agent here, so
        /// it asks SimulationBattleState.IsMountedIn -- the model's single answer to "is this man on a horse in THIS
        /// battle", which is the same thing native's Agent answers for free. That is exactly why the battle's own
        /// dismounting IS consulted: on a wall there are no horses, native would spawn the lancer on foot and hand
        /// him the foot perks, and so must we.
        ///
        /// Gated on the perk system, so the toggle that governs whether RBM prices perks at all governs this too.
        /// </summary>
        /// <summary>
        /// (internal rather than private only so StrategicTroopPower can price a man's staying power by the same
        /// commander perks this does, rather than keeping a second list of them beside this one.)
        /// </summary>
        internal static float CommandedHealth(CharacterObject troop, PartyBase party, bool dismounted)
        {
            return BuildCommandedHealth(troop, party, dismounted, false).ResultNumber;
        }

        /// <summary>
        /// The same pool, with its working shown -- for the log, which is the only way anybody can see that a perk
        /// fired at all. Called once per party per battle at write-up, never on a blow.
        ///
        /// It runs the SAME method the battle ran (see <see cref="BuildCommandedHealth"/>), and reads the answer out
        /// of ExplainedNumber's own record of what it did, rather than a second hand-written list of perks beside the
        /// first. A log that keeps its own copy of the rules is a log that will one day disagree with them, and be
        /// believed.
        /// </summary>
        internal static ExplainedNumber ExplainCommandedHealth(CharacterObject troop, PartyBase party, bool dismounted)
        {
            return BuildCommandedHealth(troop, party, dismounted, true);
        }

        /// <summary>
        /// <paramref name="explain"/> makes ExplainedNumber record every perk that fires, by name and number. It is
        /// OFF on the battle path: this runs on every blow, and an explainer allocates a list per call.
        /// </summary>
        private static ExplainedNumber BuildCommandedHealth(CharacterObject troop, PartyBase party, bool dismounted,
            bool explain)
        {
            float baseHealth = (troop != null) ? troop.MaxHitPoints() : 100f;
            ExplainedNumber stat = new ExplainedNumber(baseHealth, explain);
            if (troop == null || !SimulationPerks.Enabled)
            {
                return stat;
            }

            MobileParty mobileParty = (party != null) ? party.MobileParty : null;
            if (mobileParty == null || mobileParty.LeaderHero == null)
            {
                return stat;
            }

            if (!mobileParty.IsCurrentlyAtSea)
            {
                PerkHelper.AddPerkBonusForParty(DefaultPerks.TwoHanded.ThickHides, mobileParty, false, ref stat);
                PerkHelper.AddPerkBonusForParty(DefaultPerks.Polearm.HardyFrontline, mobileParty, true, ref stat);
            }

            if (troop.IsRanged)
            {
                PerkHelper.AddPerkBonusForParty(DefaultPerks.Crossbow.PickedShots, mobileParty, false, ref stat);
            }

            // A man on a horse gets none of these: they are for the men standing in the line. Which means a
            // cavalryman in a SIEGE collects them, and should -- there are no horses on a wall, and in the mission
            // this transcribes he spawns on foot and native's `!agent.HasMount` lets him have them.
            if (!SimulationBattleState.IsMountedIn(troop, dismounted))
            {
                if (!mobileParty.IsCurrentlyAtSea)
                {
                    PerkHelper.AddPerkBonusForParty(DefaultPerks.Athletics.WellBuilt, mobileParty, false, ref stat);
                }

                PerkHelper.AddPerkBonusForParty(DefaultPerks.Polearm.HardKnock, mobileParty, false, ref stat);

                if (!mobileParty.IsCurrentlyAtSea && troop.IsInfantry)
                {
                    PerkHelper.AddPerkBonusForParty(DefaultPerks.OneHanded.UnwaveringDefense, mobileParty, false, ref stat);
                }
            }

            // And the lord's own doctoring. Not a flat bonus at all -- it is his MEDICINE SKILL, every point of it
            // above the threshold at which epic perks begin to pay.
            CharacterObject leader = mobileParty.LeaderHero.CharacterObject;
            if (leader != null && leader.GetPerkValue(DefaultPerks.Medicine.MinisterOfHealth))
            {
                int epicThreshold = Campaign.Current.Models.CharacterDevelopmentModel.MaxSkillRequiredForEpicPerkBonus;
                int bonus = (int)(MathF.Max(leader.GetSkillValue(DefaultSkills.Medicine) - epicThreshold, 0)
                    * DefaultPerks.Medicine.MinisterOfHealth.PrimaryBonus);
                if (bonus > 0)
                {
                    stat.Add(bonus, DefaultPerks.Medicine.MinisterOfHealth.Name);
                }
            }

            return stat;
        }

        /// <summary>
        /// What the ANIMAL under a man can take, with his commander's veterinary counted in.
        ///
        /// This is the other branch of the same native method <see cref="CommandedHealth"/> transcribes. In a live
        /// mission a horse is an Agent of its own, so GetEffectiveMaxHealth runs down its non-human path and asks
        /// the RIDER's party for the mount perks:
        ///
        ///     Medicine.Sledges     (party, secondary)  -- hit points to mounts in your party
        ///     Riding.Veterinary    (rider,  primary)   -- hit points to YOUR mount; Personal, so heroes only
        ///     Riding.Veterinary    (party, secondary)  -- hit points to mounts of troops in your party
        ///
        /// Faithful down to the missing guards: native does NOT check for a leader here the way the human branch
        /// does, and it lets the party be null -- AddPerkBonusForParty tests the party itself, and the Veterinary
        /// PERSONAL bonus is deliberately outside any party check, so a hero's own mount is toughened by his own
        /// perk whether he brought a party or not. Kept exactly that way.
        ///
        /// ONE HONEST APPROXIMATION. <paramref name="baseMountHealth"/> is TroopKit.HorseHealth, which is averaged
        /// over a troop's battle sets -- a man mounted in one set of two carries "half a horse" of health. The bonus
        /// is added flat on top of that average rather than scaled by how much of a horse he has, so a
        /// partly-mounted troop is credited a whole animal's veterinary. It is a small thing and it only touches
        /// troops the model already calls cavalry (a mostly-unmounted troop is not mounted at all here -- see
        /// TroopKit.IsMounted), but it is an approximation and not a transcription, and is worth knowing.
        /// </summary>
        internal static float CommandedMountHealth(CharacterObject rider, PartyBase party, float baseMountHealth)
        {
            return BuildCommandedMountHealth(rider, party, baseMountHealth, false).ResultNumber;
        }

        /// <summary>The same, with its working shown, for the log. See <see cref="ExplainCommandedHealth"/>.</summary>
        internal static ExplainedNumber ExplainCommandedMountHealth(CharacterObject rider, PartyBase party, float baseMountHealth)
        {
            return BuildCommandedMountHealth(rider, party, baseMountHealth, true);
        }

        private static ExplainedNumber BuildCommandedMountHealth(CharacterObject rider, PartyBase party,
            float baseMountHealth, bool explain)
        {
            ExplainedNumber stat = new ExplainedNumber(baseMountHealth, explain);
            if (baseMountHealth <= 0f || !SimulationPerks.Enabled)
            {
                return stat;
            }

            MobileParty mobileParty = (party != null) ? party.MobileParty : null;
            PerkHelper.AddPerkBonusForParty(DefaultPerks.Medicine.Sledges, mobileParty, false, ref stat);
            PerkHelper.AddPerkBonusForCharacter(DefaultPerks.Riding.Veterinary, rider, true, ref stat);
            PerkHelper.AddPerkBonusForParty(DefaultPerks.Riding.Veterinary, mobileParty, false, ref stat);
            return stat;
        }

        private static void Prefix(MapEventSide __instance, ref int damage)
        {
            LastHitPointsLeft = -1f;

            // With the equipment model off the whole overhaul stands down (see SimulationEquipmentPower.
            // SimulationEnabled): no widened wound pools, no lethality scale -- vanilla's flat hundred decides the man.
            if (!SimulationEquipmentPower.SimulationEnabled)
            {
                return;
            }

            if (__instance == null || damage <= 0)
            {
                return;
            }

            CharacterObject troop = SelectedTroop(__instance);
            if (troop == null || troop.IsHero)
            {
                // A hero already has a pool of his own, four lines below this in the original. Leave him to it.
                return;
            }

            UniqueTroopDescriptor selected = SelectedDescriptor(__instance);

            // His own hundred, what his commander's perks add to it, then widened by the lethality scale (see
            // MaxHitPoints -- the party is handed over for those perks, and is why it is read here at all).
            // The pool is >= nativeMax, which keeps the trick below sound: vanilla rolls RandomInt(nativeMax) and we
            // hand it a damage of the widened pool, so RandomInt(nativeMax) < pool stays always true when he is worn
            // through -- the scale can only make the kill MORE certain, never less.
            MapEvent battle = __instance.MapEvent;
            if (battle == null)
            {
                return;
            }

            PartyBase party = __instance.GetAllocatedTroopParty(selected);
            int maxHitPoints = MaxHitPoints(troop, party, SimulationBattleState.IsDismountedBattle(battle));
            if (maxHitPoints <= 0)
            {
                return;
            }

            WoundRecord record = GetOrAdoptWound(battle, selected, party, troop);
            record.Taken += damage;

            if (record.Taken < maxHitPoints)
            {
                // Still on his feet, and carrying it. The blow is real and it is remembered; it simply has not
                // finished him. Zeroing the damage makes vanilla's roll certain to spare him.
                LastHitPointsLeft = maxHitPoints - record.Taken;
                damage = 0;
                return;
            }

            // Worn through. Vanilla decides from here whether he is dead or only wounded, and the surgeon has his
            // say -- exactly as before. His record leaves the books with him: a dead man's wound must never fold
            // into the stack carry and wear down the living twice.
            SessionWounds(battle).Remove(selected);
            LastHitPointsLeft = 0f;
            damage = maxHitPoints;

            // And a man of this stack is out of the fight, so the arrows still on his back leave it with him -- a
            // decimated archer line must not keep shooting from the quivers of its dead. Which side he stood on is
            // which stack loses him. (He is not a hero here; heroes returned far above and carry no stack quiver.)
            bool struckIsAttacker = battle.AttackerSide == __instance;
            SimulationBattleState.SpendAmmoOnDeath(battle, troop, struckIsAttacker);
        }

        /// <summary>This battle's per-descriptor ledger, made if it does not exist yet.</summary>
        private static Dictionary<UniqueTroopDescriptor, WoundRecord> SessionWounds(MapEvent battle)
        {
            Dictionary<UniqueTroopDescriptor, WoundRecord> wounds;
            if (!_wounds.TryGetValue(battle, out wounds))
            {
                wounds = new Dictionary<UniqueTroopDescriptor, WoundRecord>();
                _wounds[battle] = wounds;
            }
            return wounds;
        }

        /// <summary>
        /// The struck man's open wound record -- his existing one if this session already knows him, otherwise a
        /// fresh one that first ADOPTS the worst wound his stack carried over from the torn-down sessions before
        /// (see the class note). Worst first is deliberate: the man nearest death is the next to go down, so no
        /// carried wound is ever stranded behind fresher ones.
        /// </summary>
        private static WoundRecord GetOrAdoptWound(MapEvent battle, UniqueTroopDescriptor selected,
            PartyBase party, CharacterObject troop)
        {
            Dictionary<UniqueTroopDescriptor, WoundRecord> wounds = SessionWounds(battle);

            WoundRecord record;
            if (wounds.TryGetValue(selected, out record))
            {
                return record;
            }

            record = new WoundRecord { Party = party, Troop = troop, Taken = 0f };
            wounds[selected] = record;

            Dictionary<ValueTuple<PartyBase, CharacterObject>, List<float>> stacks;
            if (_carried.TryGetValue(battle, out stacks))
            {
                List<float> carry;
                if (stacks.TryGetValue(new ValueTuple<PartyBase, CharacterObject>(party, troop), out carry)
                    && carry.Count > 0)
                {
                    int worst = 0;
                    for (int i = 1; i < carry.Count; i++)
                    {
                        if (carry[i] > carry[worst])
                        {
                            worst = i;
                        }
                    }
                    record.Taken = carry[worst];
                    carry.RemoveAt(worst);
                }
            }

            return record;
        }

        /// <summary>
        /// The session is being torn down (the muster rebuild that kills every descriptor -- see the class note);
        /// bank every still-open wound with the stack it belongs to before the key dies. Called from the
        /// SimulateBattleSetup prefix below, which runs before EVERY session including the first (when there is
        /// nothing to fold and this is a no-op).
        /// </summary>
        internal static void FoldSessionWounds(MapEvent battle)
        {
            Dictionary<UniqueTroopDescriptor, WoundRecord> wounds;
            if (battle == null || !_wounds.TryGetValue(battle, out wounds) || wounds.Count == 0)
            {
                return;
            }

            Dictionary<ValueTuple<PartyBase, CharacterObject>, List<float>> stacks;
            if (!_carried.TryGetValue(battle, out stacks))
            {
                stacks = new Dictionary<ValueTuple<PartyBase, CharacterObject>, List<float>>();
                _carried[battle] = stacks;
            }

            foreach (WoundRecord record in wounds.Values)
            {
                if (record.Taken <= 0f)
                {
                    continue;
                }
                ValueTuple<PartyBase, CharacterObject> key =
                    new ValueTuple<PartyBase, CharacterObject>(record.Party, record.Troop);
                List<float> carry;
                if (!stacks.TryGetValue(key, out carry))
                {
                    carry = new List<float>();
                    stacks[key] = carry;
                }
                carry.Add(record.Taken);
            }

            wounds.Clear();
        }

        /// <summary>
        /// A RIPOSTE -- the counter-blow a parrying defender lands on the man who struck at him. It is spent on the
        /// ATTACKER's own wound pool, the very pool his next incoming blow reads, so it genuinely wears him toward a
        /// death the game will realise the next time he is touched. The striker is the striker side's
        /// currently-selected soldier, so his descriptor keys the same dictionary <see cref="MaxHitPoints"/> and
        /// <see cref="Prefix"/> already use for him -- no reimplementation, and no reaching into the simulation's own
        /// kill loop from inside a blow.
        ///
        /// It is never itself blocked or parried (there is no recursion here), and it does not down him in this
        /// instant: the wound is left to accumulate, even past the pool, and the ordinary worn-through path finishes
        /// him on his next blow. Realising the kill reentrantly -- calling ApplySimulationDamageToSelectedTroop back
        /// on the striker mid-blow -- would drive the casualty books, the observer and our own downed-marker in the
        /// middle of another blow's bookkeeping, which is exactly the kind of drift this whole file was rebuilt to
        /// avoid. Deepening the wound is the safe, honest spend.
        ///
        /// Returns what the striker has LEFT after the counter (clamped at zero), or -1 when there is no trooper to
        /// wound -- a hero, who keeps his own hero pool, or no live selection.
        /// </summary>
        /// <param name="strikerParty">
        /// The party of the man the counter FALLS ON -- which in a riposte is the STRIKER's, because the roles are
        /// reversed here: the man who was struck is throwing this one. His pool must be the same pool
        /// <see cref="Prefix"/> will read for him on his next blow, and that pool carries his own commander's
        /// hit-point perks, so it has to be his own party and not the defender's. Passing null was a real bug and a
        /// quiet one: it priced a well-led trooper at his bare hundred here and at his commanded pool there, so the
        /// log could report a man at zero who then walked away. It never mis-priced a KILL -- the counter only
        /// deepens the wound, and Prefix recomputes the pool properly -- but a log that lies is the one thing this
        /// file cannot afford.
        /// </param>
        internal static float ApplyRiposte(MapEventSide strikerSide, MapEvent battle, PartyBase strikerParty,
            float damage)
        {
            if (strikerSide == null || battle == null || damage <= 0f)
            {
                return -1f;
            }

            CharacterObject troop = SelectedTroop(strikerSide);
            if (troop == null)
            {
                return -1f;
            }

            int max = MaxHitPoints(troop, strikerParty, SimulationBattleState.IsDismountedBattle(battle));

            // A hero carries his own pool (AddHeroDamage), not this dictionary. The counter is shown in the log but
            // left un-applied rather than reimplement the hero-wounding path from inside a blow.
            if (troop.IsHero || max <= 0)
            {
                return -1f;
            }

            UniqueTroopDescriptor id = SelectedDescriptor(strikerSide);

            WoundRecord record = GetOrAdoptWound(battle, id, strikerParty, troop);
            record.Taken += damage;

            float left = max - record.Taken;
            return (left > 0f) ? left : 0f;
        }

        /// <summary>The battle is over; its wounded are the campaign's problem now, not ours.</summary>
        internal static void Forget(MapEvent battle)
        {
            if (battle != null)
            {
                _wounds.Remove(battle);
                _carried.Remove(battle);
            }
        }

        /// <summary>A fresh session: the wound pools of the torn-down campaign's battles will never be reclaimed by
        /// MapEventEnded, so drop them all. Called from OnSessionLaunched.</summary>
        internal static void ResetForNewSession()
        {
            _wounds.Clear();
            _carried.Clear();
        }
    }

    /// <summary>
    /// The hook that makes the wound ledger survive the map's clock. SimulateBattleSetup is the muster rebuild --
    /// the one act that kills every UniqueTroopDescriptor (see the note on SimulationTroopHitPoints) -- and it runs
    /// at the top of every simulation session. Folding BEFORE it runs banks the dying session's wounds with their
    /// stacks while the descriptors still mean something. The player-encounter path (SimulatePlayerEncounterBattle)
    /// simulates rounds WITHOUT a fresh setup, and correctly gets no fold: its descriptors live on, and the
    /// per-descriptor ledger keeps working across those rounds untouched.
    /// </summary>
    [HarmonyPatch(typeof(MapEvent), "SimulateBattleSetup")]
    internal static class SimulationWoundCarryover
    {
        private static void Prefix(MapEvent __instance)
        {
            // No gate on SimulationEnabled here, deliberately: with the model off the ledgers are simply empty and
            // this is a no-op, and a mid-battle toggle must still fold what the ledgers already hold rather than
            // strand it.
            SimulationTroopHitPoints.FoldSessionWounds(__instance);
        }
    }
}