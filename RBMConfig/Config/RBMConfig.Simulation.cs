using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace RBMConfig
{
    public static partial class RBMConfig
    {
        // Equipment-aware auto-resolve: when a map battle is simulated (auto-calc / "send troops"), scale
        // each troop's simulated hitting power by the quality of its actual kit rather than its tier alone,
        // so a well-armoured, well-armed troop resolves stronger than a ragged one of the same tier. The
        // kit is valued by whichever combat model is running -- RBM's own per-item assessment when RBM
        // Combat is on, raw vanilla item stats when it is off -- so auto-resolve tracks a fought battle.
        // False (0) restores the vanilla tier-only simulation.
        public static bool simulationEquipmentEnabled = true;

        // How strongly kit quality bends the simulated outcome. 0 is vanilla (no effect); 1 applies the
        // model at full strength; higher exaggerates the gap between good and poor equipment. In RATIO mode
        // this is the exponent on the equipment ratio; in ABSOLUTE mode (below) it is only the on/off gate.
        public static float simulationEquipmentPowerWeight = 1f;

        // STRATEGIC POWER: what the player and the AI are TOLD a troop is worth, as against what a simulated blow
        // does to him. Vanilla prices a troop for that purpose by his tier alone -- a heavily armoured elite and a
        // ragged levy of the same tier read as the same soldier -- and that one number is party strength, the
        // strength shown in an encounter, and every AI judgement about whether a fight can be won. True prices him
        // on his actual kit and training instead, and credits his party's commander perks.
        //
        // Note this replaces the tier curve outright rather than bending it, so every AI strength threshold in the
        // game moves at once and renown/influence shift with it. See RBMCampaign/Power/StrategicTroopPower.cs.
        // Auto-resolve is untouched either way.
        public static bool strategicPowerEnabled = true;


        // Writes every party out as it was priced -- the perks that reached it, then each stack with what one man of
        // it is worth and what he is made of -- to logs/powerCalculation. None of the model's constants are derived,
        // so this is how they get tuned. One block per party per in-game day; see StrategicPowerLog for why.
        public static bool strategicPowerLoggingEnabled = true;

        // ABSOLUTE DAMAGE. When true, a simulated blow is worth its own real magnitude rather than a ratio to a
        // typical blow of its arm. The model still keeps every one of vanilla's surviving factors -- side
        // advantage, the leader/captain modifier, all the Tactics/Scouting perks, and vanilla's own random
        // spread -- and replaces only vanilla's tier-power CORE with the kit-derived blow. False restores the
        // older ratio-against-baseline behaviour (clamped [0.1,8]). See SimulationEquipmentPower.Explain.
        public static bool simulationAbsoluteDamage = true;

        // The one calibration dial of absolute mode: how a blow's real magnitude maps onto the hit-point pool
        // the casualty stage wears down. Vanilla's fixed 40 base set this scale for free; absolute mode owns it.
        // Raise to make blows bite harder (battles kill faster), lower to soften them. TUNE VS A PAIRED LOG.
        public static float simulationAbsoluteScale = 1f;

        // The absolute per-blow ceiling, as a multiple of the struck man's hit-point pool. With the ratio clamp
        // gone, this is what stops one freak kit pairing landing a blow many times a man's pool; no single blow
        // may exceed this share of it. 0 disables the cap. Only applies in absolute mode.
        public static float simulationAbsoluteBlowCap = 1.5f;

        // The share of blows an ordinary shield turns aside. A shield's worth in a fight is not the armour it
        // adds -- it is the blows it stops outright, and nothing else in a troop's kit does that. A better
        // shield than the common sort stops proportionally more, a poorer one less, so this sets the middle of
        // the range rather than the whole of it. Zero makes shields count for nothing.
        //
        // Unlike almost everything else in the auto-resolve model, this figure is a judgement rather than a
        // number read out of the game: how often a man in a line gets his shield in the way is not something
        // the game records. Treat it as the dial it is.
        public static float simulationShieldBlockChance = 0.4f;

        // The skill-based defense system for auto-resolve melee: a discrete block/parry/riposte roll per blow
        // rather than the old fractional shield-skim. A defender rolls to defend (chance from his own melee skill,
        // easy behind a shield and roughly twice as hard with only a weapon); a successful defence either fully
        // blocks the blow (a shield eats the whole of it; a weapon just deflects it) or -- when he out-skills his
        // attacker -- parries and lands a counter-blow (a riposte) of his own. Ranged blows are answered by the
        // shield alone. This is what makes landed melee lethality depend on training, which pulls the sim's
        // ranged-to-melee kill balance back toward a real field battle. False (0) restores the fractional skim.
        public static bool simulationDefenseSystem = true;

        // Arm-aware target selection for auto-resolve. Vanilla picks both the striker and the man he strikes
        // UNIFORMLY AT RANDOM from the whole side, arm-blind -- a melee footman is as likely to "hit" an enemy
        // archer three ranks back as the man in front of him. This makes selection respect the battle's phase and
        // the arms of service: in the volley the bows act, in the skirmish the horse and the javelins, and every
        // striker reaches for the enemy he could actually reach (foot for the front line, archers for the massed
        // foot, cavalry for cavalry in the open). It is a weighted preference, never a hard filter, and always
        // degrades to random when the preferred arm is absent. When on, the volley's archer compensation
        // (VolleyFocus) stands down, since the bows are now handed their turns directly. False (0) restores
        // vanilla's arm-blind random selection and the VolleyFocus path unchanged.
        public static bool simulationArmTargeting = true;

        // A fired shot can simply MISS. Auto-resolve has never let one: every arrow the sim loosed connected with
        // somebody, and the only thing that could stop one was a shield in the way -- so an archer's shafts all
        // arrived, and the arm was worth what a bowman would be if he never missed. When on, a shot rolls to hit
        // before it is a blow at all (so a missed shaft meets no armour, wears no shield and kills no horse), on the
        // shooter's own bow or crossbow training above all, and then on what he shoots, how far (a volley arcs in and
        // scatters; a closing skirmish is a flat shot at a man he can see), whether he looses from a moving horse and
        // whether he shoots at one. Fired missiles only -- a thrown javelin is a committed throw and is left alone.
        // False (0) restores the shot that always arrives.
        public static bool simulationRangedMissEnabled = true;

        // The base chance a shot goes wide, before any of it is priced: what an UNTRAINED man with a bow does, which
        // every other term then works on (training cuts it hard, a crossbow cuts it, range and movement raise it). The
        // master dial for the whole arm's accuracy: raise it to put more shafts in the dirt, 0 disables the roll.
        //
        // Like simulationShieldBlockChance, this is a judgement and not a number read out of the game -- how often a
        // bowman in a line hits the man he meant to is not something Bannerlord records. Treat it as the dial it is,
        // and note that it interacts with the ranged landing spread: see RangedLandingExponent's calibration note.
        public static float simulationRangedMissChance = 0.35f;

        // How much campaign time a simulated round costs -- how long a battle on the map takes to fight, in the hours
        // the parties stand locked together. This is NOT a balance dial: it changes nothing about who wins, what the
        // casualties are, or how the phases divide. It changes only the CLOCK.
        //
        // Vanilla bills a flat half hour for every round (an hour for a siege assault), and that was the right price
        // for a vanilla round, which was a big undifferentiated chunk of the brawl and resolved a large share of the
        // fight. An RBM round is not that round. It is a PHASE -- a volley in which only the bows act, a skirmish in
        // which only the javelins and the horse do -- and a phase is a thin slice of a battle, worth minutes rather
        // than half an hour. Vanilla's price was never re-examined when the round changed meaning, so a fight that
        // takes more rounds (blows that miss, are blocked, or kill a horse instead of a man all cost a round and no
        // casualty) bills half an hour for each of them, and two warbands of twenty end up locked together for a day.
        //
        // So this is the price of a round in minutes, for a field battle. A siege assault keeps vanilla's own ratio
        // and costs twice this, because a siege round really is the longer business. Raise it to make battles occupy
        // more of the campaign's day, lower it to get them over with. 0 restores vanilla's flat 30/60 exactly.
        public static float simulationRoundMinutes = 10f;

        // A SIEGE gives the man on the wall the edge the wall is. Auto-resolve no longer folds vanilla's siege context
        // into the blow (see SimulationEquipmentPower.GetVanillaPowerNeutralizingFactor), so the defender's advantage
        // is priced here instead, and as better DICE rather than a flat power bonus: he turns aside more blows, and in
        // an exchange of shot the besieger firing UP at the battlement misses more while the defender firing DOWN
        // misses less. Off outside a siege, and off entirely when this is false.
        public static bool simulationSiegeDefenderEnabled = true;

        // How much better the besieged man's defence rolls are: his shield block, weapon block and parry chance are
        // multiplied by this while a besieger is striking him (capped so the wall is an edge, not invulnerability).
        // 1.0 is no edge; 1.3 is a third more often turned aside.
        public static float simulationSiegeDefenderDefenseBonus = 1.3f;

        // The height advantage in an exchange of arrows, as a skew on the miss chance, symmetric about 1: the besieger
        // shooting up misses this much more (x skew), the defender shooting down this much less (x (2 - skew)). 1.0 is
        // no skew; 1.4 is the besieger missing 40% more often and the defender 40% less.
        public static float simulationSiegeRangedMissSkew = 1.4f;

        // A beaten side breaks and runs instead of being fought to the last man. Vanilla's auto-resolve only routs a
        // side when its STANDING campaign morale falls to nearly zero, which never moves during the simulated fight,
        // so every auto-resolved battle grinds on to annihilation. When on, a side that falls far enough behind on
        // the field (below a fraction of the enemy's remaining numbers) may break each round, with a chance that
        // climbs the more lopsided the fight becomes; the break runs through vanilla's own Route(), so the fugitives
        // survive and the pursuit and rewards behave. Sieges are left to vanilla. Off (0) by default -- vanilla's
        // fight-to-the-last-man auto-resolve, which is what the game does without RBM.
        public static bool simulationRoutEnabled = false;

        // Perks in auto-resolve. Bannerlord runs two perk tracks: a COMMANDER's (the party or army leader),
        // which applies to everyone he brought, and a CAPTAIN's, which applies only to the one formation that hero
        // personally leads. Auto-resolve gets the first properly and the second barely at all -- its only channel for
        // captain perks is Hero.PowerModifier, which COUNTS the side commander's captain perks and turns the count
        // into a flat percentage for the whole side, throwing away what each perk actually does, ignoring the great
        // majority of them (it tests only perks whose PRIMARY role is Captain, and most declare Captain as their
        // secondary), and crediting nothing at all to the companions who would be leading formations in a real
        // battle. When on, the sim synthesises the formations auto-resolve lacks (bucketing each side's men by
        // formation class), appoints a captain over each by porting the game's own assignment rule -- so an
        // auto-resolved battle is led by the same men who would lead it if it were fought by hand -- honours the
        // player's own Order of Battle for his side, and asks each captain for his real perks through the game's own
        // PerkHelper. The PowerModifier count is then lifted back out of the blow, since it is the same thing counted
        // twice.
        //
        // It also restores the COMMANDER's hit-point perks to his men -- ThickHides, HardyFrontline, WellBuilt,
        // HardKnock, UnwaveringDefense, PickedShots and a doctor-lord's MinisterOfHealth, up to +28 and more for a
        // well-led line. Those are PartyLeader perks, not captain ones, and every one of them fires in a battle you
        // fight by hand and none in a battle you press the button on. Commander DAMAGE perks need nothing from this
        // toggle either way: vanilla applies them itself and RBM's correction preserves them.
        //
        // False (0) restores vanilla's captain-perk count and drops the commander's hit-point perks again.
        public static bool simulationPerkSystem = true;

        // Writes every auto-resolved battle to its own log under logs/simulation, as it was actually fought: who
        // stood on each side, what they carried, how it ended. Costs nothing while off -- no battle is snapshotted
        // and no blow is recorded.
        public static bool simulationLoggingEnabled = true;

        // And the battle itself, BLOW BY BLOW: every man who swung, what he was doing at the time (shooting,
        // hurling a javelin, charging, setting a spear, or just walking into arrows while the lines closed), what
        // armour he met, what his shield turned aside, what vanilla alone would have hit for, and what the model
        // made of it. The matchup table says what a blow would do in the abstract; only this can tell you the
        // archers ran dry in round fifteen. A large battle runs to several thousand lines. Needs the log above.
        public static bool simulationLogHits = true;

        // Offers to open a battle between two AI lords as a real-time fight you watch and take no part in: both sides
        // under their own commanders, no player agent on the field at all, RTSCamera's free camera the only way to
        // see it. The battle on the map auto-resolves on its own beside it and reaches its own verdict; the fight you
        // watch is a copy and is written back nowhere.
        //
        // This is a measuring instrument, not a feature of the campaign: it is the only way to see whether the field
        // AI fights a muster the way auto-resolve says it would. Off by default, and does nothing without RTSCamera.
        public static bool spectateBattlesEnabled = false;

        // How big both sides must be before the offer is made at all. Two patrols brushing past each other say
        // nothing about how a line holds, and being asked about every looter band on the map would make the thing
        // unusable. Default 100, counted per side.
        public static int spectateMinTroopsPerSide = 100;

        // Writes every blow of a REAL battle -- the one fought on the field -- to logs/battles, in the same columns
        // the auto-resolve trace uses, so what the simulation CLAIMS a battle is can be held against one that
        // actually happened: who was shooting, who had reached anybody yet, what armour a blow met, what it did.
        // Off by default. A real battle lands thousands of blows and each is a line.
        public static bool battleHitLoggingEnabled = false;
    }
}
