using HarmonyLib;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
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
    /// The game does know who each man is: MapEventSide keeps a UniqueTroopDescriptor for the soldier it has
    /// selected. So a pool is possible, and this keeps one.
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
        /// What each man has taken so far, kept per battle so it can be dropped with the battle. Several fights run
        /// at once across the map, and a campaign that never forgot its wounded would carry every man it ever
        /// scratched.
        /// </summary>
        private static readonly Dictionary<MapEvent, Dictionary<UniqueTroopDescriptor, float>> _wounds =
            new Dictionary<MapEvent, Dictionary<UniqueTroopDescriptor, float>>();

        /// <summary>What the man who was just struck has left. Read by the log, which no longer has to guess.</summary>
        internal static float LastHitPointsLeft = -1f;

        /// <summary>
        /// What a man can take in an auto-resolved battle: his OWN hit points, and only his own.
        ///
        /// A map battle gives every soldier a flat hundred to begin with -- DefaultCharacterStatsModel.MaxHitpoints
        /// starts at 100 -- and a regular trooper keeps exactly that. His party's perks are NOT added to it. They
        /// could be: a real MISSION, in SandboxAgentStatCalculateModel, hands a well-led foot line up to +28 hit
        /// points (TwoHanded.ThickHides +5 and Polearm.HardyFrontline +5 to all, Athletics.WellBuilt +5 and
        /// Polearm.HardKnock +3 on foot, OneHanded.UnwaveringDefense +10 for infantry) and a doctor-lord's men more
        /// again through Medicine.MinisterOfHealth. For a while this method transcribed that whole block into the
        /// sim so the two would agree. It no longer does, by design.
        ///
        /// A soldier's staying power in auto-resolve is meant to be his OWN armour and his own frame, not a bonus
        /// his captain carries -- the same principle that took tier and terrain out of the blow. So the auto-resolve
        /// deliberately diverges from the live mission here: a regular unit's pool is native's hundred, and no perk
        /// on top of it -- then widened uniformly by the lethality scale, which is a separate lever about how hard a
        /// blow lands, not about whose perks count.
        /// A HERO is untouched -- his own MaxHitPoints() already carries his personal perks, and no party bonus was
        /// ever added to him -- so he keeps every point he has (the scale is a trooper knob; a hero's pool is his own).
        ///
        /// This also keeps the pool trick below sound. A regular unit's pool is native's hundred SCALED UP by
        /// <see cref="LethalityHitPointScale"/> -- and the scale is >= 1 by construction, so the pool is never less
        /// than the hundred vanilla rolls RandomInt against. "Worn through" hands back a damage of maxHitPoints, now
        /// >= 100, and RandomInt(100) &lt; (&gt;=100) is still always true; "still standing" hands back zero, still
        /// never true. Widening the pool cannot break the trick -- only shrinking it below native's hundred could,
        /// which is why the scale is clamped at 1 and never allowed under it.
        ///
        /// (<paramref name="party"/> is no longer consulted -- a trooper's pool does not depend on his officers now.
        /// It is kept in the signature to mirror the caller and leave the door open if that ever changes again.)
        /// </summary>
        internal static int MaxHitPoints(CharacterObject troop, PartyBase party)
        {
            if (troop == null)
            {
                return (int)(100 * LethalityHitPointScale);
            }

            // Base only, hero or trooper alike: a soldier's own hit points -- his personal perks already in them for
            // a hero, and nothing added for a trooper. Party and leader perks are deliberately left out; see above --
            // then widened by the lethality scale, which lowers how hard a single simulated blow lands.
            int hitPoints = (int)(troop.MaxHitPoints() * LethalityHitPointScale);
            return (hitPoints > 1) ? hitPoints : 1;
        }

        private static void Prefix(MapEventSide __instance, ref int damage)
        {
            LastHitPointsLeft = -1f;

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

            // His own hundred, nothing his officers carry, then widened by the lethality scale (see MaxHitPoints).
            // The pool is >= nativeMax, which keeps the trick below sound: vanilla rolls RandomInt(nativeMax) and we
            // hand it a damage of the widened pool, so RandomInt(nativeMax) < pool stays always true when he is worn
            // through -- the scale can only make the kill MORE certain, never less.
            int maxHitPoints = MaxHitPoints(troop, __instance.GetAllocatedTroopParty(selected));
            if (maxHitPoints <= 0)
            {
                return;
            }

            MapEvent battle = __instance.MapEvent;
            if (battle == null)
            {
                return;
            }

            Dictionary<UniqueTroopDescriptor, float> wounds;
            if (!_wounds.TryGetValue(battle, out wounds))
            {
                wounds = new Dictionary<UniqueTroopDescriptor, float>();
                _wounds[battle] = wounds;
            }

            UniqueTroopDescriptor id = selected;

            float taken;
            wounds.TryGetValue(id, out taken);
            taken += damage;

            if (taken < maxHitPoints)
            {
                // Still on his feet, and carrying it. The blow is real and it is remembered; it simply has not
                // finished him. Zeroing the damage makes vanilla's roll certain to spare him.
                wounds[id] = taken;
                LastHitPointsLeft = maxHitPoints - taken;
                damage = 0;
                return;
            }

            // Worn through. Vanilla decides from here whether he is dead or only wounded, and the surgeon has his
            // say -- exactly as before.
            wounds.Remove(id);
            LastHitPointsLeft = 0f;
            damage = maxHitPoints;
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
        /// wound -- a hero, who keeps his own hero pool, or no live selection. <paramref name="maxHitPoints"/> comes
        /// back with his pool size for the log whatever the outcome.
        /// </summary>
        internal static float ApplyRiposte(MapEventSide strikerSide, MapEvent battle, float damage, out int maxHitPoints)
        {
            maxHitPoints = 0;
            if (strikerSide == null || battle == null || damage <= 0f)
            {
                return -1f;
            }

            CharacterObject troop = SelectedTroop(strikerSide);
            if (troop == null)
            {
                return -1f;
            }

            int max = MaxHitPoints(troop, null);
            maxHitPoints = max;

            // A hero carries his own pool (AddHeroDamage), not this dictionary. The counter is shown in the log but
            // left un-applied rather than reimplement the hero-wounding path from inside a blow.
            if (troop.IsHero || max <= 0)
            {
                return -1f;
            }

            UniqueTroopDescriptor id = SelectedDescriptor(strikerSide);

            Dictionary<UniqueTroopDescriptor, float> wounds;
            if (!_wounds.TryGetValue(battle, out wounds))
            {
                wounds = new Dictionary<UniqueTroopDescriptor, float>();
                _wounds[battle] = wounds;
            }

            float taken;
            wounds.TryGetValue(id, out taken);
            taken += damage;
            wounds[id] = taken;

            float left = max - taken;
            return (left > 0f) ? left : 0f;
        }

        /// <summary>The battle is over; its wounded are the campaign's problem now, not ours.</summary>
        internal static void Forget(MapEvent battle)
        {
            if (battle != null)
            {
                _wounds.Remove(battle);
            }
        }
    }
}