using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// ARM-AWARE TARGET SELECTION for auto-resolve. Vanilla <see cref="MapEvent"/>.SimulateSingleTroopHit picks
    /// BOTH the man who strikes and the man he strikes UNIFORMLY AT RANDOM from the whole side -- arm-blind. So a
    /// melee footman is exactly as likely to "hit" an enemy archer three ranks back as the man in front of him,
    /// and the volley is thrown by whoever the dice land on rather than by the bows. The damage model then spends
    /// a great deal of effort papering over that: it NULLIFIES the wrong-arm blows a bad draw produced (a footman
    /// "shooting" in the volley is zeroed) and BOOSTS the archers (VolleyFocus) to hand back the turns their own
    /// infantry ate. This makes the selection itself honest, so those corrections have nothing left to correct.
    ///
    /// It controls two picks, in the two calls SimulateSingleTroopHit makes, in order:
    ///   1. THE STRIKER (who acts), by PHASE. In the volley only the bows act; in the skirmish the horse ride out
    ///      and the javelin-men throw; once the lines meet, everyone. This is a strong preference, not a law -- a
    ///      man of the wrong arm is heavily passed over, never forbidden, so nothing deadlocks when an arm is thin.
    ///   2. THE STRUCK (whom he reaches), by the STRIKER'S ARM and the phase. Melee foot reach the enemy front
    ///      line, then the horse among it, and the enemy's shooters last (they stand behind their own line). The
    ///      bows loose into the massed foot and reach the fewer, faster, further horsemen last. The horse, in the
    ///      skirmish, ride at the other side's horse before the foot are near.
    ///
    /// HOW: a Prefix on SimulateSingleTroopHit brackets the two selection calls and remembers which MapEventSide is
    /// the striker and which the struck; a Postfix on <see cref="MapEventSide"/>.SelectRandomSimulationTroop then
    /// RE-PICKS the man vanilla just chose -- overwriting the three private _selected* fields the downstream kill
    /// and reward dispatch reads (index, descriptor, troop), all three kept consistent -- so that dispatch runs
    /// exactly as it always did. Nothing about the damage, the reward, or the death is reimplemented; only WHO the
    /// blow is between changes. That is the whole reason for the coordinator over a wholesale reimplementation of
    /// SimulateSingleTroopHit: the mod's standing rule is don't reimplement what you can redirect, because a
    /// reimplementation drifts from the thing it copies and then lies about the battle with perfect confidence.
    ///
    /// The pick is a bounded rejection sample: draw a uniform index (as vanilla does), keep it with a probability
    /// read off the striker-arm x target-arm keep-weight tables, and redraw if rejected -- at most a few times,
    /// then take the last uniform draw regardless. So the preferred arms are favoured, a present-but-unfavoured
    /// arm is never excluded, and an ABSENT preferred arm simply loses every roll and the fallback uniform draw
    /// stands. It cannot loop forever and it cannot fail to return a man.
    ///
    /// Gated behind RBMConfig.simulationArmTargeting (default on). When off, the Prefix disarms the coordinator and
    /// every SelectRandomSimulationTroop is left exactly as vanilla picked it, and VolleyFocus resumes.
    /// </summary>
    internal static class SimulationArmTargeting
    {
        // --- Reflection handles into MapEventSide's private selection state -------------------------------------
        // The three fields the downstream dispatch reads (ApplySimulationDamageToSelectedTroop,
        // ApplySimulatedHitRewardToSelectedTroop, RemoveSelectedTroopFromSimulationList) and the list they index.
        // A re-pick MUST set all three to the SAME troop, and the index MUST be that troop's real position in the
        // list, or the man who is damaged is not the man who was chosen.
        private static readonly AccessTools.FieldRef<MapEventSide, List<UniqueTroopDescriptor>> SimulationTroopList =
            AccessTools.FieldRefAccess<MapEventSide, List<UniqueTroopDescriptor>>("_simulationTroopList");

        private static readonly AccessTools.FieldRef<MapEventSide, int> SelectedIndex =
            AccessTools.FieldRefAccess<MapEventSide, int>("_selectedSimulationTroopIndex");

        private static readonly AccessTools.FieldRef<MapEventSide, UniqueTroopDescriptor> SelectedDescriptor =
            AccessTools.FieldRefAccess<MapEventSide, UniqueTroopDescriptor>("_selectedSimulationTroopDescriptor");

        private static readonly AccessTools.FieldRef<MapEventSide, CharacterObject> SelectedTroop =
            AccessTools.FieldRefAccess<MapEventSide, CharacterObject>("_selectedSimulationTroop");

        // --- The per-hit state machine -------------------------------------------------------------------------
        // Campaign simulation is single-threaded, so one set of statics carries the pending hit. The Prefix arms it
        // fresh for every blow; the Postfix reads the stage to tell the striker pick (0) from the struck pick (1).
        // Any anomaly parks the stage past both so the rest of the blow is left to vanilla -- see BeginHit/OnSelected.
        private const int StageStriker = 0;
        private const int StageStruck = 1;
        private const int StageDone = 2;
        private const int StageDisarmed = 3;

        private static MapEvent _pendingEvent;
        private static MapEventSide _pendingStrikerSide;
        private static MapEventSide _pendingStruckSide;
        private static int _stage = StageDisarmed;
        private static int _strikerArm = -1;

        // Arm by troop template. The arm is a fixed property of the template (GetTroopType reads IsMounted/IsRanged
        // off the CharacterObject), so this is cached once per troop and never invalidated -- the list the selector
        // draws from mutates every time a man falls, but a Vlandian Sergeant is infantry in the first round and the
        // last. This is why the pick needs no per-round rebuild of arm buckets: classification does not go stale.
        private static readonly Dictionary<CharacterObject, int> _armCache = new Dictionary<CharacterObject, int>();

        // The striking side's archer share, cached for the round it was computed in (see ArcherShare). One slot is
        // enough: a round's ticks alternate sides but the volley is short and a re-scan on a side flip is cheap.
        private static MapEventSide _shareCachedSide;
        private static int _shareCachedRound = -1;
        private static float _shareCachedValue;

        // The most redraws a rejected pick will make before it gives up and takes the last uniform draw. Small: the
        // common case (contact phase, no preference, weight 1.0) returns on the first draw, and even a thin
        // preferred arm is found quickly. The cap is what guarantees no loop and a graceful degrade to random.
        private const int MaxSelectionAttempts = 8;

        // Phase labels, derived from SimulationBattleState's phase definitions (the single source of truth).
        private const int PhaseVolley = 0;
        private const int PhaseSkirmish = 1;
        private const int PhaseContact = 2;

        // ---------------------------------------------------------------------------------------------------------
        // KEEP-WEIGHTS: the probability of ACCEPTING a drawn candidate of a given arm. 1.0 = always take it; a lower
        // value = usually pass it over and redraw, which biases the final pick toward the higher-weighted arms
        // without ever forbidding a present arm. Every number here is a starting point -- tune vs a paired log.

        // -- STRIKER, by phase (who gets to act) --
        // The volley belongs to the bows -- but WEIGHTED by how many bows a side brought, not handed to them whole.
        // Only share^VolleyArcherScalingExponent of the volley's shots become archer fire; see PickVolleyStriker. The
        // 0.6 is not free: random selection gave archers `share` of the shots and VolleyFocus boosted them by
        // share^-0.4, so their output went as share^0.6 -- and this reproduces that same count-dependence in the pick.
        private const float VolleyArcherScalingExponent = 0.6f;
        // The skirmish belongs to the horse and the javelins. Foot archers keep shooting (they still have ammo), so
        // they act too; plain foot with no javelins are still just walking and are mostly passed over.
        private const float SkirmishStrikerRanged = 1.0f; // arc and HA -- still shooting
        private const float SkirmishStrikerCavalry = 1.0f; // the horse ride out
        private const float SkirmishStrikerFootJavelin = 1.0f; // a foot skirmisher with javelins left throws here
        private const float SkirmishStrikerFootOther = 0.15f;  // everyone else on foot is merely closing
        // Once the lines meet, a horseman lands far more blows than a footman -- he is mobile, rides in, kills, backs
        // out and comes again, engaging many where the foot engage one. So in the contact phase the MOUNTED arms are
        // chosen as striker this many times as often as their headcount alone would give them (the foot keep-weight is
        // its reciprocal, below). No blow is lost to it -- a passed-over footman is redrawn, not dropped -- the side's
        // blows just shift toward the men who would really be landing them, and with them the charges those blows roll.
        // This was the gap that turned a cavalry-won battle backwards: the sim gave the winning horse a footman's rate.
        // Eased from 1.75 to 1.4 -- at 1.75, stacked on the charge buffs, the horse landed too many blows and ran the
        // field on charges where a ranged army should have won.
        private const float CavalryMobilityMultiplier = 1.4f;

        // Once the lines meet, a FOOT archer is overrun: the enemy is on him, and he is drawing a sword or dying, not
        // loosing freely as he did across the open ground. So in the contact phase he is selected to act far less than
        // a footman -- this is his keep-weight there. It was the other half of the forest inversion: the sim let the
        // attacker's bows shoot half the battle's blows where a real forest melee let them shoot a fifth. (Horse
        // archers keep their mounted weight -- they ride clear and shoot, they are not overrun the way the foot are.)
        // Eased from 0.25 to 0.35 -- a little less damped, the bows loose a touch more once the lines meet.
        private const float ContactArcherStrikerWeight = 0.35f;

        // -- STRUCK, by striker arm (whom the blow reaches) --
        // Melee foot: the man in front first, the horse in among the line next, the enemy's shooters -- who stand
        // behind their own line -- hardest of all to get at.
        private const float MeleeInfVsInf = 1.0f;
        private const float MeleeInfVsCavalry = 0.6f;
        private const float MeleeInfVsRanged = 0.2f;
        // Ranged (foot bows and horse archers alike): loose into the massed foot; the mounted are fewer, faster and
        // further off, and are struck last.
        private const float RangedVsFoot = 1.0f;
        private const float RangedVsMounted = 0.35f;
        // Cavalry in the skirmish: horse meets horse out in front of the foot.
        private const float CavSkirmishVsMounted = 1.0f;
        private const float CavSkirmishVsFoot = 0.3f;
        // Cavalry once the lines have met: they break off and ride down the enemy's shooters before grinding at his
        // foot line, so they favour the ranged over the melee infantry (and the other horse) -- but only a little now
        // (ranged eased from 1.0 to 0.9, so the lean over the foot line is gentle rather than a fixation).
        private const float CavContactVsRanged = 0.9f;
        private const float CavContactVsMelee = 0.75f;

        // =========================================================================================================
        // The coordinator entry points.

        /// <summary>
        /// Arm the coordinator for one blow. Called from the Prefix on SimulateSingleTroopHit, BEFORE either of its
        /// two SelectRandomSimulationTroop calls. <paramref name="side"/> is the STRIKING side; the struck side is
        /// the other. Resolving the two sides here (rather than in the Postfix) is what lets the Postfix tell the
        /// striker's pick from the struck's by a plain reference check. When the feature is off, or there is no
        /// battle to read a phase from, the coordinator is disarmed and every pick is left to vanilla.
        /// </summary>
        internal static void BeginHit(MapEvent battle, BattleSideEnum side)
        {
            if (!RBMConfig.RBMConfig.simulationArmTargeting || battle == null)
            {
                _pendingEvent = null;
                _stage = StageDisarmed;
                return;
            }

            _pendingEvent = battle;
            // MapEvent stores _sides[Attacker] as AttackerSide and _sides[Defender] as DefenderSide, and the striking
            // side is _sides[side]; so the striker is the attacker side exactly when side is Attacker.
            bool strikerIsAttacker = side == BattleSideEnum.Attacker;
            _pendingStrikerSide = strikerIsAttacker ? battle.AttackerSide : battle.DefenderSide;
            _pendingStruckSide = strikerIsAttacker ? battle.DefenderSide : battle.AttackerSide;
            _stage = StageStriker;
            _strikerArm = -1;
        }

        /// <summary>
        /// Re-pick the man vanilla just selected, if this is one of our two bracketed picks. Called from the Postfix
        /// on SelectRandomSimulationTroop. The first pick on the pending striker side (stage 0) is re-chosen by
        /// phase and its arm remembered; the second on the pending struck side (stage 1) is re-chosen by that arm
        /// and the phase. Any other call -- a ship hit's troop casualties, a side that is neither, a stale stage --
        /// is left exactly as vanilla picked it. Any exception disarms the rest of this blow rather than propagate
        /// out of a Harmony postfix into the game's simulation loop.
        /// </summary>
        internal static void OnSelected(MapEventSide instance, ref UniqueTroopDescriptor result)
        {
            if (_pendingEvent == null || instance == null)
            {
                return;
            }
            if (_stage != StageStriker && _stage != StageStruck)
            {
                return;
            }

            try
            {
                if (_stage == StageStriker && instance == _pendingStrikerSide)
                {
                    SimulationBattleState.BattleState state = SimulationBattleState.Get(_pendingEvent);
                    int phase = PhaseOf(state);
                    // The volley is share-weighted, not forced: see PickVolleyStriker. Every other phase is a plain
                    // keep-weight pick.
                    int index = (phase == PhaseVolley)
                        ? PickVolleyStriker(instance, state)
                        : PickIndex(instance, phase, StrikerKeepWeight, strikerArm: -1);
                    CharacterObject chosen = ApplySelection(instance, index, ref result);
                    _strikerArm = ArmOf(chosen);
                    _stage = StageStruck;
                }
                else if (_stage == StageStruck && instance == _pendingStruckSide)
                {
                    SimulationBattleState.BattleState state = SimulationBattleState.Get(_pendingEvent);
                    int phase = PhaseOf(state);
                    int index = PickIndex(instance, phase, StruckKeepWeight, strikerArm: _strikerArm);
                    ApplySelection(instance, index, ref result);
                    _stage = StageDone;
                }
            }
            catch
            {
                // Something went wrong reaching into the private state -- stand down for the rest of this blow and
                // leave whatever vanilla picked. The next BeginHit re-arms cleanly.
                _stage = StageDisarmed;
            }
        }

        // =========================================================================================================
        // Selection.

        /// <summary>
        /// THE VOLLEY, SHARE-WEIGHTED. Forcing every volley shot onto an archer (keep-weight 1) would make a side's
        /// arrow output depend on its TOTAL size, not on how many archers it brought -- thirty bowmen would loose as
        /// many shafts as three hundred, because the shot count per round is fixed and all of it went to whoever the
        /// bows were. That is the very failure VolleyFocus was built to prevent, and neutralising VolleyFocus for the
        /// arm-aware path reopened it.
        ///
        /// So the volley is not forced, it is WEIGHTED: only <c>share^0.6</c> of a side's volley shots become archer
        /// fire, where <c>share</c> is the archers' fraction of that side. The rest fall to a foot soldier and are
        /// nullified by the damage model's volley rule (a man not shooting in the volley deals nothing and is not
        /// recorded), so the log still shows only archers loosing. The point is the exponent: random selection gave
        /// archers <c>share</c> of the shots and VolleyFocus multiplied their damage by <c>share^-0.4</c>, netting
        /// output ∝ <c>share^0.6·pow(men,0.6) = pow(archers,0.6)</c>. Weighting the SELECTION by <c>share^0.6</c>
        /// reproduces exactly that count-dependence, in the selection instead of the damage -- so bringing more
        /// archers means more arrows again, sublinearly, as everything else in the sim scales.
        /// </summary>
        private static int PickVolleyStriker(MapEventSide side, SimulationBattleState.BattleState state)
        {
            float share = ArcherShare(side, (state != null) ? state.Round : 0);
            float archerChance = (share <= 0f) ? 0f : MathF.Pow(share, VolleyArcherScalingExponent);
            bool wantArcher = MBRandom.RandomFloat < archerChance;
            // Want an archer -> pick one; else pick a foot man who will be nullified, keeping the archer output at the
            // weighted rate. Either pick degrades to a plain random draw if that arm is somehow absent (no deadlock).
            // Split rather than a ternary: C# 7.3 will not target-type a conditional between two method groups.
            if (wantArcher)
            {
                return PickIndex(side, PhaseVolley, ArcherStrikerWeight, strikerArm: -1);
            }
            return PickIndex(side, PhaseVolley, FootFillStrikerWeight, strikerArm: -1);
        }

        /// <summary>
        /// The archers' fraction of a side, computed once per round and cached (the volley is a few rounds long, so
        /// this is a handful of list scans a battle). Share drifts as men fall within a round, but slowly, and the
        /// round-start figure is more than close enough for a weighting. Counts foot bows AND horse archers.
        /// </summary>
        private static float ArcherShare(MapEventSide side, int round)
        {
            int cachedRound;
            if (side == _shareCachedSide && _shareCachedRound == round)
            {
                return _shareCachedValue;
            }

            List<UniqueTroopDescriptor> list = SimulationTroopList(side);
            int total = (list != null) ? list.Count : 0;
            float share = 0f;
            if (total > 0)
            {
                int archers = 0;
                for (int i = 0; i < total; i++)
                {
                    int arm = ArmOf(side.GetAllocatedTroop(list[i]));
                    if (arm == SimulationEquipmentPower.ArcherType || arm == SimulationEquipmentPower.HorseArcherType)
                    {
                        archers++;
                    }
                }
                share = (float)archers / total;
            }

            _shareCachedSide = side;
            _shareCachedRound = round;
            _shareCachedValue = share;
            return share;
        }

        /// <summary>Keep an archer, pass everyone else -- to pick the archer a weighted volley shot goes to.</summary>
        private static float ArcherStrikerWeight(int unusedStrikerArm, int phase, CharacterObject candidate)
        {
            int arm = ArmOf(candidate);
            return (arm == SimulationEquipmentPower.ArcherType || arm == SimulationEquipmentPower.HorseArcherType) ? 1f : 0f;
        }

        /// <summary>Keep a foot non-archer, pass the bows -- the shot the volley weighting spends on a nullified man.</summary>
        private static float FootFillStrikerWeight(int unusedStrikerArm, int phase, CharacterObject candidate)
        {
            int arm = ArmOf(candidate);
            return (arm == SimulationEquipmentPower.ArcherType || arm == SimulationEquipmentPower.HorseArcherType) ? 0f : 1f;
        }

        /// <summary>
        /// A bounded rejection sample over the side's remaining troops. Draws a uniform index like vanilla, keeps it
        /// with the keep-weight of its arm, and redraws on a reject up to <see cref="MaxSelectionAttempts"/> times,
        /// then takes the last draw regardless. Returns an index into _simulationTroopList, or -1 if the list is
        /// empty (which cannot happen here -- vanilla has just picked from it -- but is handled for safety).
        /// </summary>
        private static int PickIndex(MapEventSide side, int phase,
            Func<int, int, CharacterObject, float> keepWeight, int strikerArm)
        {
            List<UniqueTroopDescriptor> list = SimulationTroopList(side);
            int count = (list != null) ? list.Count : 0;
            if (count <= 0)
            {
                return -1;
            }
            if (count == 1)
            {
                return 0;
            }

            int last = 0;
            for (int attempt = 0; attempt < MaxSelectionAttempts; attempt++)
            {
                int index = MBRandom.RandomInt(count);
                last = index;

                CharacterObject candidate = side.GetAllocatedTroop(list[index]);
                float weight = keepWeight(strikerArm, phase, candidate);

                if (weight >= 1f)
                {
                    return index;
                }
                if (weight > 0f && MBRandom.RandomFloat < weight)
                {
                    return index;
                }
                // Rejected: draw again. If the preferred arm is simply absent, every draw is rejected and the last
                // uniform draw below stands -- a clean degrade to random, never a deadlock.
            }
            return last;
        }

        /// <summary>
        /// Point the three _selected* fields at the troop now at <paramref name="index"/>, keeping index, descriptor
        /// and CharacterObject consistent, and hand the descriptor back through the postfix's ref result. Returns the
        /// chosen CharacterObject, or null if the index is somehow out of range (in which case vanilla's pick stands).
        /// </summary>
        private static CharacterObject ApplySelection(MapEventSide side, int index, ref UniqueTroopDescriptor result)
        {
            List<UniqueTroopDescriptor> list = SimulationTroopList(side);
            if (list == null || index < 0 || index >= list.Count)
            {
                return null;
            }

            UniqueTroopDescriptor descriptor = list[index];
            CharacterObject troop = side.GetAllocatedTroop(descriptor);

            SelectedIndex(side) = index;
            SelectedDescriptor(side) = descriptor;
            SelectedTroop(side) = troop;
            result = descriptor;
            return troop;
        }

        /// <summary>The arm this candidate would strike as -- cached by template, never invalidated. -1 for a null.</summary>
        private static int ArmOf(CharacterObject troop)
        {
            if (troop == null)
            {
                return -1;
            }
            int arm;
            if (!_armCache.TryGetValue(troop, out arm))
            {
                arm = SimulationEquipmentPower.ArmOf(troop);
                _armCache[troop] = arm;
            }
            return arm;
        }

        private static int PhaseOf(SimulationBattleState.BattleState state)
        {
            if (SimulationBattleState.IsVolleyPhase(state))
            {
                return PhaseVolley;
            }
            if (SimulationBattleState.IsSkirmishPhase(state))
            {
                return PhaseSkirmish;
            }
            return PhaseContact;
        }

        // =========================================================================================================
        // The two keep-weight functions, shaped to the Func<strikerArm, phase, candidate> the sampler calls.

        /// <summary>Who is allowed to ACT, by phase. The striker arm parameter is unused (the striker has no striker).</summary>
        private static float StrikerKeepWeight(int unusedStrikerArm, int phase, CharacterObject candidate)
        {
            int arm = ArmOf(candidate);

            switch (phase)
            {
                // The volley does not come through here -- it is share-weighted in PickVolleyStriker, not a plain
                // keep-weight pick. StrikerKeepWeight is only ever asked about the skirmish and the contact phases.
                case PhaseSkirmish:
                    // The bows still shoot; the horse ride out; a footman with javelins hurls them. Plain foot are
                    // merely closing and are mostly passed over.
                    if (arm == SimulationEquipmentPower.ArcherType || arm == SimulationEquipmentPower.HorseArcherType)
                    {
                        return SkirmishStrikerRanged;
                    }
                    if (arm == SimulationEquipmentPower.CavalryType)
                    {
                        return SkirmishStrikerCavalry;
                    }
                    return SimulationEquipmentPower.CarriesThrown(candidate)
                        ? SkirmishStrikerFootJavelin
                        : SkirmishStrikerFootOther;

                default:
                    // The lines have met. The horse land more of the blows (mobility), the melee foot the reciprocal
                    // of that multiplier, and the FOOT BOWS fewer still -- overrun, they loose little now. See
                    // CavalryMobilityMultiplier and ContactArcherStrikerWeight.
                    if (arm == SimulationEquipmentPower.CavalryType
                        || arm == SimulationEquipmentPower.HorseArcherType)
                    {
                        return 1f;
                    }
                    if (arm == SimulationEquipmentPower.ArcherType)
                    {
                        return ContactArcherStrikerWeight;
                    }
                    return 1f / CavalryMobilityMultiplier;
            }
        }

        /// <summary>Whom the blow reaches, by the striker's arm and the phase. The candidate here is the TARGET.</summary>
        private static float StruckKeepWeight(int strikerArm, int phase, CharacterObject candidate)
        {
            int targetArm = ArmOf(candidate);
            if (targetArm < 0)
            {
                return 1f; // an unclassifiable target: no preference, take it as readily as any other
            }

            bool targetMounted = targetArm == SimulationEquipmentPower.CavalryType
                || targetArm == SimulationEquipmentPower.HorseArcherType;

            switch (strikerArm)
            {
                case SimulationEquipmentPower.ArcherType:
                case SimulationEquipmentPower.HorseArcherType:
                    // Ranged: the massed foot first, the mounted last.
                    return targetMounted ? RangedVsMounted : RangedVsFoot;

                case SimulationEquipmentPower.CavalryType:
                    if (phase == PhaseSkirmish)
                    {
                        // Horse meets horse out in front of the foot.
                        return targetMounted ? CavSkirmishVsMounted : CavSkirmishVsFoot;
                    }
                    // Contact: ride down the shooters a bit ahead of the melee line (and the other horse).
                    return (targetArm == SimulationEquipmentPower.ArcherType
                            || targetArm == SimulationEquipmentPower.HorseArcherType)
                        ? CavContactVsRanged
                        : CavContactVsMelee;

                case SimulationEquipmentPower.InfantryType:
                    // Melee foot: the front line, then the horse in it, the enemy's shooters last.
                    if (targetArm == SimulationEquipmentPower.InfantryType)
                    {
                        return MeleeInfVsInf;
                    }
                    if (targetArm == SimulationEquipmentPower.CavalryType)
                    {
                        return MeleeInfVsCavalry;
                    }
                    return MeleeInfVsRanged; // arc or HA -- ranged, behind their own line

                default:
                    return 1f; // striker arm unknown (e.g. the striker re-pick was declined): no preference
            }
        }
    }

    /// <summary>
    /// Brackets the two SelectRandomSimulationTroop calls SimulateSingleTroopHit makes, arming the coordinator with
    /// the striker and struck sides for this blow. It does NOT skip the original -- vanilla still picks, and the
    /// Postfix on the selector re-picks over the top of it.
    /// </summary>
    [HarmonyPatch(typeof(MapEvent), "SimulateSingleTroopHit")]
    internal static class SimulationArmTargeting_HitPrefix
    {
        private static void Prefix(MapEvent __instance, BattleSideEnum side)
        {
            SimulationArmTargeting.BeginHit(__instance, side);
        }
    }

    /// <summary>
    /// Re-picks the selected simulation troop by phase and arm, when the coordinator is armed and this is one of the
    /// two bracketed picks. Every unrelated selection is left exactly as vanilla made it.
    /// </summary>
    [HarmonyPatch(typeof(MapEventSide), "SelectRandomSimulationTroop")]
    internal static class SimulationArmTargeting_SelectPostfix
    {
        private static void Postfix(MapEventSide __instance, ref UniqueTroopDescriptor __result)
        {
            SimulationArmTargeting.OnSelected(__instance, ref __result);
        }
    }
}
