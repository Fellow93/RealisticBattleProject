using System;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    // Battle-chronicle text tables.
    //
    // Every entry below is a COMPLETE sentence template carrying a {=RBM_CHR_*} key, so a
    // translator sees the whole line and is free to reorder subject, verb and object (word
    // order differs wildly across the languages RBM ships). The old code composed lines by
    // gluing an English verb fragment between two names; each such verb variant has been
    // multiplied out into its own full-sentence template instead.
    //
    // Templates are resolved with `new TextObject(template)` at pick time, never cached, so
    // switching the game language takes effect immediately -- the same pattern
    // RBMCampaign/UI/RBMMapNotifications.cs uses.
    //
    // Named variables used here:
    //   {HERO}     the hero the chronicle line is "about" (drawn in its own coloured widget)
    //   {FOE}      the other party in a hero line (already carries its [ATK]/[DEF] tag)
    //   {VICTIM}   the man who went down
    //   {ATTACKER} the man who struck him
    //   {SIDE}     "The attackers" / "The defenders"
    //   {DMG}      the damage parenthetical, or empty -- see DmgText()
    internal partial class SimulationBattlePanelVM
    {
        // Substituted for {HERO} while resolving a hero line, then split back out: the
        // chronicle draws the hero's name in its own coloured TextWidget, so we need to know
        // what the translated sentence puts before and after it.
        private const string HeroMarker = "";

        // ── Side tags and stock names ───────────────────────────────────
        // Fragments on purpose: a short bracket tag glued in front of a proper name.
        private const string SidedNameAttacker = "{=RBM_CHR_SIDED_ATK}[ATK] {NAME}";
        private const string SidedNameDefender = "{=RBM_CHR_SIDED_DEF}[DEF] {NAME}";

        private const string SideAttackers = "{=RBM_CHR_SIDE_ATTACKERS}The attackers";
        private const string SideDefenders = "{=RBM_CHR_SIDE_DEFENDERS}The defenders";

        private const string NameUnknownSoldier = "{=RBM_CHR_UNKNOWN_SOLDIER}an unknown soldier";
        private const string NameALord = "{=RBM_CHR_A_LORD}a lord";
        private const string NameASoldier = "{=RBM_CHR_A_SOLDIER}a soldier";
        private const string NameLordCap = "{=RBM_CHR_LORD_CAP}A lord";
        private const string NameYou = "{=RBM_CHR_YOU}You";
        private const string NameUnknownAssailant = "{=RBM_CHR_UNKNOWN_ASSAILANT}an unknown assailant";
        private const string NameAnAttacker = "{=RBM_CHR_AN_ATTACKER}an attacker";
        private const string NameUnknownSide = "{=RBM_CHR_UNKNOWN_SIDE}Unknown";

        // The damage parenthetical. A trailing stat, not part of the grammar, so it stays a
        // separate keyed fragment appended through {DMG}.
        private const string DamageParenthetical = "{=RBM_CHR_DMG} ({DMG} dmg)";

        // ── Header / phase furniture ────────────────────────────────────
        private const string PhaseDeploying = "{=RBM_CHR_PHASE_DEPLOYING}DEPLOYING";
        private const string PhaseApproach = "{=RBM_CHR_PHASE_APPROACH}APPROACH";
        private const string PhaseAssault = "{=RBM_CHR_PHASE_ASSAULT}ASSAULT";
        private const string PhaseVolley = "{=RBM_CHR_PHASE_VOLLEY}VOLLEY";
        private const string PhaseSkirmish = "{=RBM_CHR_PHASE_SKIRMISH}SKIRMISH";
        private const string PhaseMelee = "{=RBM_CHR_PHASE_MELEE}MELEE";
        private const string PhaseRout = "{=RBM_CHR_PHASE_ROUT}ROUT";

        private const string DeployDescription = "{=RBM_CHR_DEPLOY_DESC}Forces marshal on the field";

        // Joiner: a phase banner is "<phase label> — <one of the flavour sentences>". Both
        // halves are translated in their own right, so this only carries the separator.
        private const string PhaseBannerLine = "{=RBM_CHR_PHASE_LINE}{PHASE} — {FLAVOR}";
        private const string RoundLine = "{=RBM_CHR_ROUND}Round {NUM}";
        private const string RoundDivider = "{=RBM_CHR_DIVIDER}──── Round {NUM} ────";
        private const string SiegeInfoLine = "{=RBM_CHR_SIEGE_INFO}Frontage: {ATK} vs {DEF} · Wall: {PCT}%";

        // ── Troop-strength read-outs ────────────────────────────────────
        private const string StatInfantry = "{=RBM_CHR_STAT_INF}Inf: {VALUE}";
        private const string StatRanged = "{=RBM_CHR_STAT_RAN}Ran: {VALUE}";
        private const string StatCavalry = "{=RBM_CHR_STAT_CAV}Cav: {VALUE}";
        private const string StatTotal = "{=RBM_CHR_STAT_TOTAL}Total: {VALUE} / {START}";

        // ── Phase flavour (whole sentences, no variables) ───────────────

        private static readonly string[] VolleyFlavor = new[]
        {
            "{=RBM_CHR_VOLLEY_01}Arrows darken the sky",
            "{=RBM_CHR_VOLLEY_02}Bowstrings sing across the field",
            "{=RBM_CHR_VOLLEY_03}The first shafts find their mark",
            "{=RBM_CHR_VOLLEY_04}A storm of arrows descends on the enemy",
            "{=RBM_CHR_VOLLEY_05}Volleys arc high and fall like rain",
            "{=RBM_CHR_VOLLEY_06}The air hums with feathered death",
            "{=RBM_CHR_VOLLEY_07}Shafts whistle overhead in thick waves",
            "{=RBM_CHR_VOLLEY_08}Archers loose in unison — the sky goes dark",
            "{=RBM_CHR_VOLLEY_09}A thousand bowstrings snap as one",
            "{=RBM_CHR_VOLLEY_10}The arrow storm begins its grim harvest",
            "{=RBM_CHR_VOLLEY_11}Quivers empty into the massed ranks ahead",
            "{=RBM_CHR_VOLLEY_12}Flights of arrows blot out the sun",
        };

        private static readonly string[] SkirmishFlavor = new[]
        {
            "{=RBM_CHR_SKIRM_01}Javelins fly as the cavalry rides out",
            "{=RBM_CHR_SKIRM_02}Horsemen clash between the closing lines",
            "{=RBM_CHR_SKIRM_03}Riders spur forward, javelins in hand",
            "{=RBM_CHR_SKIRM_04}The skirmish opens as the gap narrows",
            "{=RBM_CHR_SKIRM_05}Light horse wheel and strike at the flanks",
            "{=RBM_CHR_SKIRM_06}Cavalry thunder across the open ground",
            "{=RBM_CHR_SKIRM_07}Lances dip as the horsemen charge into the fray",
            "{=RBM_CHR_SKIRM_08}Javelins arc through the dust between the lines",
            "{=RBM_CHR_SKIRM_09}Outriders trade blows at the edges of the fight",
            "{=RBM_CHR_SKIRM_10}The ground shakes as mounted warriors collide",
            "{=RBM_CHR_SKIRM_11}Skirmishers dart forward and hurl their darts",
            "{=RBM_CHR_SKIRM_12}Hooves pound and javelins flash in the sun",
        };

        private static readonly string[] MeleeFlavor = new[]
        {
            "{=RBM_CHR_MELEE_01}Steel meets steel as the lines crash together",
            "{=RBM_CHR_MELEE_02}The shieldwall buckles under the press",
            "{=RBM_CHR_MELEE_03}Infantry close to sword's length at last",
            "{=RBM_CHR_MELEE_04}The lines meet with a thunderous crash",
            "{=RBM_CHR_MELEE_05}Men hack and shove in the press of bodies",
            "{=RBM_CHR_MELEE_06}The melee is a heaving mass of iron and flesh",
            "{=RBM_CHR_MELEE_07}Shields splinter under the weight of the charge",
            "{=RBM_CHR_MELEE_08}Swords ring out and men fall screaming",
            "{=RBM_CHR_MELEE_09}The battle becomes a brutal close-quarters brawl",
            "{=RBM_CHR_MELEE_10}Blades flash and blood slicks the trampled earth",
            "{=RBM_CHR_MELEE_11}The two sides grind against each other in the mud",
            "{=RBM_CHR_MELEE_12}Warriors grapple in the dust, fighting for their lives",
            "{=RBM_CHR_MELEE_13}The front line is a wall of shields, blood and iron",
            "{=RBM_CHR_MELEE_14}Axes and swords bite through armour and bone",
        };

        private static readonly string[] SiegeApproachFlavor = new[]
        {
            "{=RBM_CHR_SAPPR_01}Men sprint across the killing ground",
            "{=RBM_CHR_SAPPR_02}The besiegers advance under a hail of arrows",
            "{=RBM_CHR_SAPPR_03}Bodies pile before the gates",
            "{=RBM_CHR_SAPPR_04}The open ground before the walls is a death trap",
            "{=RBM_CHR_SAPPR_05}Defenders rain fire on the approaching columns",
            "{=RBM_CHR_SAPPR_06}Arrows hammer down from the battlements",
            "{=RBM_CHR_SAPPR_07}The assault columns push through a storm of bolts",
            "{=RBM_CHR_SAPPR_08}Men fall by the dozen crossing the open ground",
            "{=RBM_CHR_SAPPR_09}Hot sand and stones cascade from the ramparts",
            "{=RBM_CHR_SAPPR_10}The advance is a slow crawl under murderous fire",
            "{=RBM_CHR_SAPPR_11}Siege towers creak forward under a hail of missiles",
        };

        private static readonly string[] SiegeAssaultFlavor = new[]
        {
            "{=RBM_CHR_SASLT_01}Ladders strike the walls!",
            "{=RBM_CHR_SASLT_02}The storm begins at the breaches",
            "{=RBM_CHR_SASLT_03}Men pour through the openings",
            "{=RBM_CHR_SASLT_04}Fighting is hand-to-hand at the parapets",
            "{=RBM_CHR_SASLT_05}The besiegers claw their way onto the walls",
            "{=RBM_CHR_SASLT_06}Defenders shove ladders back — but more come",
            "{=RBM_CHR_SASLT_07}The gatehouse is a slaughterhouse",
            "{=RBM_CHR_SASLT_08}Blood runs down the stone steps of the battlements",
            "{=RBM_CHR_SASLT_09}Swords clash on the narrow walkways of the wall",
            "{=RBM_CHR_SASLT_10}The breach is choked with the dead of both sides",
            "{=RBM_CHR_SASLT_11}Attackers flood the parapet despite fearful losses",
        };

        private static readonly string[] RoutFlavor = new[]
        {
            "{=RBM_CHR_ROUT_01}Their courage breaks!",
            "{=RBM_CHR_ROUT_02}The line shatters and men flee!",
            "{=RBM_CHR_ROUT_03}Panic spreads through the ranks!",
            "{=RBM_CHR_ROUT_04}They throw down their arms and run!",
            "{=RBM_CHR_ROUT_05}The rout is on — nothing can stop it!",
            "{=RBM_CHR_ROUT_06}Their nerve fails — men turn and flee!",
            "{=RBM_CHR_ROUT_07}The retreat becomes a stampede!",
            "{=RBM_CHR_ROUT_08}Officers shout but no one listens — they run!",
            "{=RBM_CHR_ROUT_09}The formation dissolves into a fleeing mob!",
            "{=RBM_CHR_ROUT_10}Banners fall as men scatter in every direction!",
        };

        // The chronicle's rout line names the side and the number that fled, so it needs its
        // own templates rather than the bare phase-banner flavour above. Same length as
        // RoutFlavor, so the shared flavour counter advances exactly as it used to.
        private static readonly string[] RoutLineFlavor = new[]
        {
            "{=RBM_CHR_ROUTLINE_01}{SIDE} lose their courage — {COUNT} flee the field",
            "{=RBM_CHR_ROUTLINE_02}{SIDE} shatter and run — {COUNT} flee the field",
            "{=RBM_CHR_ROUTLINE_03}Panic spreads through {SIDE} — {COUNT} flee the field",
            "{=RBM_CHR_ROUTLINE_04}{SIDE} throw down their arms — {COUNT} flee the field",
            "{=RBM_CHR_ROUTLINE_05}The rout of {SIDE} cannot be stopped — {COUNT} flee the field",
            "{=RBM_CHR_ROUTLINE_06}The nerve of {SIDE} fails — {COUNT} flee the field",
            "{=RBM_CHR_ROUTLINE_07}The retreat of {SIDE} becomes a stampede — {COUNT} flee the field",
            "{=RBM_CHR_ROUTLINE_08}No one heeds the officers of {SIDE} — {COUNT} flee the field",
            "{=RBM_CHR_ROUTLINE_09}{SIDE} dissolve into a fleeing mob — {COUNT} flee the field",
            "{=RBM_CHR_ROUTLINE_10}The banners of {SIDE} fall — {COUNT} flee the field",
        };

        // ── Strength milestones ─────────────────────────────────────────

        private static readonly string[] HalfStrengthFlavor = new[]
        {
            "{=RBM_CHR_HALF_01}{SIDE} have lost half their strength — the field is littered with their dead",
            "{=RBM_CHR_HALF_02}{SIDE} are at half strength and wavering",
            "{=RBM_CHR_HALF_03}{SIDE} have taken grievous losses — half their men are down",
            "{=RBM_CHR_HALF_04}{SIDE} bleed freely — barely half still stand",
            "{=RBM_CHR_HALF_05}{SIDE} have paid a terrible price — half their number lie fallen",
            "{=RBM_CHR_HALF_06}{SIDE} thin visibly — the gaps in their line grow wider",
            "{=RBM_CHR_HALF_07}{SIDE} stagger under the weight of their casualties",
        };

        private static readonly string[] QuarterStrengthFlavor = new[]
        {
            "{=RBM_CHR_QUARTER_01}{SIDE} are being destroyed — barely a quarter remain",
            "{=RBM_CHR_QUARTER_02}{SIDE} are on the verge of annihilation",
            "{=RBM_CHR_QUARTER_03}{SIDE} cling to the field with a handful of survivors",
            "{=RBM_CHR_QUARTER_04}{SIDE} have lost three quarters of their men",
            "{=RBM_CHR_QUARTER_05}{SIDE} fight on in desperate knots — most of their comrades are fallen",
            "{=RBM_CHR_QUARTER_06}{SIDE} are a broken remnant, still fighting but doomed",
            "{=RBM_CHR_QUARTER_07}{SIDE} barely hold together — the end is near for them",
        };

        // ── Artillery ───────────────────────────────────────────────────

        private static readonly string[] ArtilleryFlavor = new[]
        {
            "{=RBM_CHR_ARTY_01}Stones crash into the defenders",
            "{=RBM_CHR_ARTY_02}A boulder smashes through the ranks",
            "{=RBM_CHR_ARTY_03}The engines hurl death at the walls",
            "{=RBM_CHR_ARTY_04}Siege stones tear men apart",
            "{=RBM_CHR_ARTY_05}A catapult stone carves a path through the crowd",
            "{=RBM_CHR_ARTY_06}Trebuchet fire hammers the fortifications",
            "{=RBM_CHR_ARTY_07}The ground shakes as heavy stones find their targets",
            "{=RBM_CHR_ARTY_08}Engine crews heave and another stone arcs skyward",
            "{=RBM_CHR_ARTY_09}A volley of stones rains down from the siege line",
        };

        private static readonly string[] ArtilleryDestroyFlavor = new[]
        {
            "{=RBM_CHR_ARTYDEST_01}An enemy engine is shattered to splinters!",
            "{=RBM_CHR_ARTYDEST_02}A direct hit reduces an engine to kindling!",
            "{=RBM_CHR_ARTYDEST_03}A well-aimed stone destroys an enemy machine!",
            "{=RBM_CHR_ARTYDEST_04}An engine erupts into flying timber and rope!",
            "{=RBM_CHR_ARTYDEST_05}The crew dives clear as their machine is wrecked!",
        };

        // Artillery tally line. Singular/plural is not distinguished: the counts are almost
        // always greater than one and English reads the same either way here.
        private const string ArtilleryTally = "{=RBM_CHR_ARTY_TALLY}{FLAVOR} — {KILLED} killed";
        private const string ArtilleryTallyWounded =
            "{=RBM_CHR_ARTY_TALLY_W}{FLAVOR} — {KILLED} killed, {WOUNDED} wounded";

        // ── Round's hardest blow (rank-and-file, no hero involved) ──────
        // Each of the old verb fragments became a full sentence.

        private static readonly string[] KillSword = new[]
        {
            "{=RBM_CHR_KILL_SWORD_01}{ATTACKER} cut down {VICTIM}{DMG}",
            "{=RBM_CHR_KILL_SWORD_02}{ATTACKER} slashed {VICTIM} open{DMG}",
            "{=RBM_CHR_KILL_SWORD_03}{ATTACKER} ran {VICTIM} through{DMG}",
            "{=RBM_CHR_KILL_SWORD_04}{ATTACKER} slew {VICTIM}{DMG}",
        };

        private static readonly string[] KillDagger = new[]
        {
            "{=RBM_CHR_KILL_DAGGER_01}{ATTACKER} stabbed {VICTIM}{DMG}",
            "{=RBM_CHR_KILL_DAGGER_02}{ATTACKER} knifed {VICTIM}{DMG}",
            "{=RBM_CHR_KILL_DAGGER_03}{ATTACKER} gutted {VICTIM}{DMG}",
        };

        private static readonly string[] KillAxe = new[]
        {
            "{=RBM_CHR_KILL_AXE_01}{ATTACKER} cleaved {VICTIM}{DMG}",
            "{=RBM_CHR_KILL_AXE_02}{ATTACKER} hewed down {VICTIM}{DMG}",
            "{=RBM_CHR_KILL_AXE_03}{ATTACKER} hacked {VICTIM} apart{DMG}",
            "{=RBM_CHR_KILL_AXE_04}{ATTACKER} split {VICTIM} open{DMG}",
        };

        private static readonly string[] KillMace = new[]
        {
            "{=RBM_CHR_KILL_MACE_01}{ATTACKER} battered down {VICTIM}{DMG}",
            "{=RBM_CHR_KILL_MACE_02}{ATTACKER} hammered down {VICTIM}{DMG}",
            "{=RBM_CHR_KILL_MACE_03}{ATTACKER} crushed {VICTIM}{DMG}",
            "{=RBM_CHR_KILL_MACE_04}{ATTACKER} smashed {VICTIM}{DMG}",
        };

        private static readonly string[] KillPolearm = new[]
        {
            "{=RBM_CHR_KILL_POLE_01}{ATTACKER} pierced {VICTIM}{DMG}",
            "{=RBM_CHR_KILL_POLE_02}{ATTACKER} impaled {VICTIM}{DMG}",
            "{=RBM_CHR_KILL_POLE_03}{ATTACKER} speared {VICTIM}{DMG}",
            "{=RBM_CHR_KILL_POLE_04}{ATTACKER} skewered {VICTIM}{DMG}",
        };

        private static readonly string[] KillBow = new[]
        {
            "{=RBM_CHR_KILL_BOW_01}{ATTACKER} shot down {VICTIM}{DMG}",
            "{=RBM_CHR_KILL_BOW_02}{ATTACKER} pierced {VICTIM} with an arrow{DMG}",
            "{=RBM_CHR_KILL_BOW_03}{ATTACKER} pinned {VICTIM} with a shaft{DMG}",
            "{=RBM_CHR_KILL_BOW_04}{ATTACKER} dropped {VICTIM} with a shot{DMG}",
        };

        private static readonly string[] KillCrossbow = new[]
        {
            "{=RBM_CHR_KILL_XBOW_01}{ATTACKER} shot down {VICTIM} with a bolt{DMG}",
            "{=RBM_CHR_KILL_XBOW_02}{ATTACKER} pinned {VICTIM} with a crossbow bolt{DMG}",
            "{=RBM_CHR_KILL_XBOW_03}{ATTACKER} dropped {VICTIM} with a bolt{DMG}",
        };

        private static readonly string[] KillJavelin = new[]
        {
            "{=RBM_CHR_KILL_JAV_01}{ATTACKER} speared {VICTIM} with a javelin{DMG}",
            "{=RBM_CHR_KILL_JAV_02}{ATTACKER} impaled {VICTIM} with a thrown spear{DMG}",
            "{=RBM_CHR_KILL_JAV_03}{ATTACKER} skewered {VICTIM} at range{DMG}",
        };

        private static readonly string[] KillThrowingAxe = new[]
        {
            "{=RBM_CHR_KILL_TAXE_01}{ATTACKER} hit {VICTIM} with a thrown axe{DMG}",
            "{=RBM_CHR_KILL_TAXE_02}{ATTACKER} split {VICTIM} open with a hurled axe{DMG}",
            "{=RBM_CHR_KILL_TAXE_03}{ATTACKER} struck down {VICTIM} with a thrown axe{DMG}",
        };

        private static readonly string[] KillThrowingKnife = new[]
        {
            "{=RBM_CHR_KILL_TKNIFE_01}{ATTACKER} hit {VICTIM} with a thrown knife{DMG}",
            "{=RBM_CHR_KILL_TKNIFE_02}{ATTACKER} struck down {VICTIM} with a thrown blade{DMG}",
        };

        private static readonly string[] KillSling = new[]
        {
            "{=RBM_CHR_KILL_SLING_01}{ATTACKER} brained {VICTIM} with a sling stone{DMG}",
            "{=RBM_CHR_KILL_SLING_02}{ATTACKER} felled {VICTIM} with a stone{DMG}",
            "{=RBM_CHR_KILL_SLING_03}{ATTACKER} struck down {VICTIM} with a sling{DMG}",
        };

        private static readonly string[] KillShootGeneric = new[]
        {
            "{=RBM_CHR_KILL_SHOOT_01}{ATTACKER} shot down {VICTIM}{DMG}",
            "{=RBM_CHR_KILL_SHOOT_02}{ATTACKER} pierced {VICTIM}{DMG}",
            "{=RBM_CHR_KILL_SHOOT_03}{ATTACKER} struck {VICTIM} at range{DMG}",
        };

        private static readonly string[] KillThrowGeneric = new[]
        {
            "{=RBM_CHR_KILL_THROW_01}{ATTACKER} struck {VICTIM} at range{DMG}",
            "{=RBM_CHR_KILL_THROW_02}{ATTACKER} hit {VICTIM} with a thrown weapon{DMG}",
        };

        private static readonly string[] KillMeleeGeneric = new[]
        {
            "{=RBM_CHR_KILL_MELEE_01}{ATTACKER} struck down {VICTIM}{DMG}",
            "{=RBM_CHR_KILL_MELEE_02}{ATTACKER} felled {VICTIM}{DMG}",
            "{=RBM_CHR_KILL_MELEE_03}{ATTACKER} cut down {VICTIM}{DMG}",
        };

        // ── A hero falls ────────────────────────────────────────────────

        private static readonly string[] HeroFell_Charge = new[]
        {
            "{=RBM_CHR_HFELL_CHARGE_01}{HERO} was ridden down by {FOE}{DMG}",
            "{=RBM_CHR_HFELL_CHARGE_02}{HERO} was trampled under the hooves of {FOE}{DMG}",
            "{=RBM_CHR_HFELL_CHARGE_03}{HERO} was unhorsed and crushed by {FOE}{DMG}",
            "{=RBM_CHR_HFELL_CHARGE_04}{HERO} was broken by the lance of {FOE}{DMG}",
            "{=RBM_CHR_HFELL_CHARGE_05}{HERO} was swept from the saddle by {FOE}{DMG}",
            "{=RBM_CHR_HFELL_CHARGE_06}{HERO} was smashed aside by the charge of {FOE}{DMG}",
        };

        private static readonly string[] HeroFell_Brace = new[]
        {
            "{=RBM_CHR_HFELL_BRACE_01}{HERO} was impaled on the braced spear of {FOE}{DMG}",
            "{=RBM_CHR_HFELL_BRACE_02}{HERO} charged into the waiting lance of {FOE}{DMG}",
            "{=RBM_CHR_HFELL_BRACE_03}{HERO} rode onto the set pike of {FOE}{DMG}",
            "{=RBM_CHR_HFELL_BRACE_04}{HERO} was skewered on the levelled polearm of {FOE}{DMG}",
        };

        private static readonly string[] HeroFell_Sword = new[]
        {
            "{=RBM_CHR_HFELL_SWORD_01}{HERO} was cut down by {FOE}{DMG}",
            "{=RBM_CHR_HFELL_SWORD_02}{HERO} was slain by the blade of {FOE}{DMG}",
            "{=RBM_CHR_HFELL_SWORD_03}{HERO} fell to the sword of {FOE}{DMG}",
            "{=RBM_CHR_HFELL_SWORD_04}{HERO} was run through by {FOE}{DMG}",
        };

        private static readonly string[] HeroFell_Dagger = new[]
        {
            "{=RBM_CHR_HFELL_DAGGER_01}{HERO} was stabbed down by {FOE}{DMG}",
            "{=RBM_CHR_HFELL_DAGGER_02}{HERO} was knifed by {FOE}{DMG}",
            "{=RBM_CHR_HFELL_DAGGER_03}{HERO} fell to the dagger of {FOE}{DMG}",
        };

        private static readonly string[] HeroFell_Axe = new[]
        {
            "{=RBM_CHR_HFELL_AXE_01}{HERO} was cleaved apart by {FOE}{DMG}",
            "{=RBM_CHR_HFELL_AXE_02}{HERO} was hewn down by {FOE}{DMG}",
            "{=RBM_CHR_HFELL_AXE_03}{HERO} fell to the axe of {FOE}{DMG}",
            "{=RBM_CHR_HFELL_AXE_04}{HERO} was split open by {FOE}{DMG}",
        };

        private static readonly string[] HeroFell_Mace = new[]
        {
            "{=RBM_CHR_HFELL_MACE_01}{HERO} was battered down by {FOE}{DMG}",
            "{=RBM_CHR_HFELL_MACE_02}{HERO} was crushed by {FOE}{DMG}",
            "{=RBM_CHR_HFELL_MACE_03}{HERO} had their skull caved in by {FOE}{DMG}",
            "{=RBM_CHR_HFELL_MACE_04}{HERO} was hammered to the ground by {FOE}{DMG}",
        };

        private static readonly string[] HeroFell_Polearm = new[]
        {
            "{=RBM_CHR_HFELL_POLE_01}{HERO} was pierced by the spear of {FOE}{DMG}",
            "{=RBM_CHR_HFELL_POLE_02}{HERO} was impaled by {FOE}{DMG}",
            "{=RBM_CHR_HFELL_POLE_03}{HERO} was run through by the lance of {FOE}{DMG}",
            "{=RBM_CHR_HFELL_POLE_04}{HERO} fell to the polearm of {FOE}{DMG}",
        };

        private static readonly string[] HeroFell_Bow = new[]
        {
            "{=RBM_CHR_HFELL_BOW_01}{HERO} was felled by an arrow from {FOE}{DMG}",
            "{=RBM_CHR_HFELL_BOW_02}{HERO} was shot down by {FOE}{DMG}",
            "{=RBM_CHR_HFELL_BOW_03}{HERO} took a fatal shaft from {FOE}{DMG}",
            "{=RBM_CHR_HFELL_BOW_04}{HERO} was pierced by an arrow from {FOE}{DMG}",
        };

        private static readonly string[] HeroFell_Crossbow = new[]
        {
            "{=RBM_CHR_HFELL_XBOW_01}{HERO} was dropped by a bolt from {FOE}{DMG}",
            "{=RBM_CHR_HFELL_XBOW_02}{HERO} was pinned by a crossbow bolt from {FOE}{DMG}",
            "{=RBM_CHR_HFELL_XBOW_03}{HERO} took a killing bolt from {FOE}{DMG}",
        };

        private static readonly string[] HeroFell_Javelin = new[]
        {
            "{=RBM_CHR_HFELL_JAV_01}{HERO} was speared by a javelin from {FOE}{DMG}",
            "{=RBM_CHR_HFELL_JAV_02}{HERO} was impaled at range by {FOE}{DMG}",
            "{=RBM_CHR_HFELL_JAV_03}{HERO} took a hurled spear from {FOE}{DMG}",
        };

        private static readonly string[] HeroFell_ThrowingAxe = new[]
        {
            "{=RBM_CHR_HFELL_TAXE_01}{HERO} was struck by a thrown axe from {FOE}{DMG}",
            "{=RBM_CHR_HFELL_TAXE_02}{HERO} was split open by a hurled axe from {FOE}{DMG}",
        };

        private static readonly string[] HeroFell_ThrowingKnife = new[]
        {
            "{=RBM_CHR_HFELL_TKNIFE_01}{HERO} was struck down by a thrown knife from {FOE}{DMG}",
            "{=RBM_CHR_HFELL_TKNIFE_02}{HERO} took a thrown blade from {FOE}{DMG}",
        };

        private static readonly string[] HeroFell_Sling = new[]
        {
            "{=RBM_CHR_HFELL_SLING_01}{HERO} was brained by a sling stone from {FOE}{DMG}",
            "{=RBM_CHR_HFELL_SLING_02}{HERO} was felled by a stone from {FOE}{DMG}",
        };

        private static readonly string[] HeroFell_ShootGeneric = new[]
        {
            "{=RBM_CHR_HFELL_SHOOT_01}{HERO} was shot down by {FOE}{DMG}",
            "{=RBM_CHR_HFELL_SHOOT_02}{HERO} was struck at range by {FOE}{DMG}",
            "{=RBM_CHR_HFELL_SHOOT_03}{HERO} was felled by a missile from {FOE}{DMG}",
        };

        private static readonly string[] HeroFell_ThrowGeneric = new[]
        {
            "{=RBM_CHR_HFELL_THROW_01}{HERO} was struck down at range by {FOE}{DMG}",
            "{=RBM_CHR_HFELL_THROW_02}{HERO} was felled by a thrown weapon from {FOE}{DMG}",
        };

        private static readonly string[] HeroFell_MeleeGeneric = new[]
        {
            "{=RBM_CHR_HFELL_MELEE_01}{HERO} was struck down by {FOE}{DMG}",
            "{=RBM_CHR_HFELL_MELEE_02}{HERO} was felled by {FOE}{DMG}",
            "{=RBM_CHR_HFELL_MELEE_03}{HERO} fell in combat with {FOE}{DMG}",
        };

        // ── The player's own blows ──────────────────────────────────────

        private static readonly string[] PlayerHitFlavor = new[]
        {
            "{=RBM_CHR_PHIT_01}{HERO} strikes {FOE}{DMG}",
            "{=RBM_CHR_PHIT_02}{HERO} lands a blow on {FOE}{DMG}",
            "{=RBM_CHR_PHIT_03}{HERO} hits {FOE}{DMG}",
            "{=RBM_CHR_PHIT_04}{HERO} connects with {FOE}{DMG}",
        };

        private static readonly string[] PlayerTakeHitFlavor = new[]
        {
            "{=RBM_CHR_PTAKE_01}{HERO} takes a hit from {FOE}{DMG}",
            "{=RBM_CHR_PTAKE_02}{HERO} is struck by {FOE}{DMG}",
            "{=RBM_CHR_PTAKE_03}{HERO} is hit by {FOE}{DMG}",
            "{=RBM_CHR_PTAKE_04}{HERO} absorbs a blow from {FOE}{DMG}",
        };

        private static readonly string[] PlayerMissFlavor = new[]
        {
            "{=RBM_CHR_PMISS_01}{HERO} dodges a shot from {FOE}",
            "{=RBM_CHR_PMISS_02}{HERO} evades {FOE}",
            "{=RBM_CHR_PMISS_03}{HERO} sidesteps a blow from {FOE}",
        };

        private static readonly string[] PlayerMissedShotFlavor = new[]
        {
            "{=RBM_CHR_PMISSSHOT_01}{HERO} misses a shot at {FOE}",
            "{=RBM_CHR_PMISSSHOT_02}{HERO} sends a shaft wide of {FOE}",
            "{=RBM_CHR_PMISSSHOT_03}{HERO} looses at {FOE}",
        };

        // The player's attack was stopped by the other man.
        private const string PlayerAttackRiposted =
            "{=RBM_CHR_PDEF_RIPOSTE}{HERO}'s blow is parried and countered by {FOE}{DMG}";
        private const string PlayerAttackParried =
            "{=RBM_CHR_PDEF_PARRY}{HERO}'s attack is parried by {FOE}{DMG}";
        private const string PlayerAttackShieldBlocked =
            "{=RBM_CHR_PDEF_SHIELD}{HERO}'s strike is blocked by {FOE}{DMG}";
        private const string PlayerAttackWeaponBlocked =
            "{=RBM_CHR_PDEF_WEAPON}{HERO}'s blow is deflected by {FOE}{DMG}";
        private const string PlayerAttackDefended =
            "{=RBM_CHR_PDEF_OTHER}{HERO}'s attack is defended by {FOE}{DMG}";

        // ── A hero defends ──────────────────────────────────────────────

        private static readonly string[] RiposteFlavor = new[]
        {
            "{=RBM_CHR_RIPOSTE_01}{HERO} parries a blow from {FOE} and strikes back{DMG}",
            "{=RBM_CHR_RIPOSTE_02}{HERO} deflects {FOE}'s attack and counters{DMG}",
            "{=RBM_CHR_RIPOSTE_03}{HERO} turns aside {FOE}'s blade and ripostes{DMG}",
            "{=RBM_CHR_RIPOSTE_04}{HERO} catches {FOE}'s strike and drives their own home{DMG}",
            "{=RBM_CHR_RIPOSTE_05}{HERO} sidesteps {FOE} and delivers a vicious counterstrike{DMG}",
        };

        private static readonly string[] ParryFlavor = new[]
        {
            "{=RBM_CHR_PARRY_01}{HERO} blocks a blow from {FOE}{DMG}",
            "{=RBM_CHR_PARRY_02}{HERO} catches {FOE}'s strike on their shield{DMG}",
            "{=RBM_CHR_PARRY_03}{HERO} turns aside a blow from {FOE}{DMG}",
            "{=RBM_CHR_PARRY_04}{HERO} deflects {FOE}'s attack{DMG}",
        };

        // ── A hero kills ────────────────────────────────────────────────

        private static readonly string[] HeroKillFlavor_Melee = new[]
        {
            "{=RBM_CHR_HKILL_MELEE_01}{HERO} cuts down {VICTIM}{DMG}",
            "{=RBM_CHR_HKILL_MELEE_02}{HERO} strikes down {VICTIM}{DMG}",
            "{=RBM_CHR_HKILL_MELEE_03}{HERO} slays {VICTIM}{DMG}",
            "{=RBM_CHR_HKILL_MELEE_04}{HERO} fells {VICTIM}{DMG}",
            "{=RBM_CHR_HKILL_MELEE_05}{HERO} sends another to the grave — {VICTIM}{DMG}",
        };

        private static readonly string[] HeroKillFlavor_Ranged = new[]
        {
            "{=RBM_CHR_HKILL_RANGED_01}{HERO} picks off {VICTIM}{DMG}",
            "{=RBM_CHR_HKILL_RANGED_02}{HERO} drops {VICTIM}{DMG}",
            "{=RBM_CHR_HKILL_RANGED_03}{HERO} shoots down {VICTIM}{DMG}",
            "{=RBM_CHR_HKILL_RANGED_04}{HERO} finds their mark — {VICTIM}{DMG}",
        };

        private static readonly string[] HeadshotFlavor = new[]
        {
            "{=RBM_CHR_HEADSHOT_01}{HERO} takes an arrow to the head!{DMG}",
            "{=RBM_CHR_HEADSHOT_02}{HERO} is struck in the face by a missile!{DMG}",
            "{=RBM_CHR_HEADSHOT_03}{HERO} catches a bolt in the skull!{DMG}",
            "{=RBM_CHR_HEADSHOT_04}{HERO} is hit square in the head at range!{DMG}",
            "{=RBM_CHR_HEADSHOT_05}{HERO} takes a shot clean through the helm!{DMG}",
        };

        private static readonly string[] HeroSnipeFlavor = new[]
        {
            "{=RBM_CHR_SNIPE_01}{HERO} lands a perfect headshot on {VICTIM}{DMG}",
            "{=RBM_CHR_SNIPE_02}{HERO} puts an arrow clean through the helm of {VICTIM}{DMG}",
            "{=RBM_CHR_SNIPE_03}{HERO} nails a shot to the head of {VICTIM}{DMG}",
            "{=RBM_CHR_SNIPE_04}{HERO} finds the gap in the visor of {VICTIM}{DMG}",
            "{=RBM_CHR_SNIPE_05}{HERO} sends a bolt straight through the skull of {VICTIM}{DMG}",
        };

        // ── Template selection ──────────────────────────────────────────

        // The round's hardest blow, phrased for the weapon that landed it.
        private static string KillTemplateForWeapon(string weaponClass, string phase, int counter)
        {
            string[] pool;
            switch (weaponClass)
            {
                case "OneHandedSword":
                case "TwoHandedSword":
                    pool = KillSword;
                    break;
                case "Dagger":
                    pool = KillDagger;
                    break;
                case "OneHandedAxe":
                case "TwoHandedAxe":
                    pool = KillAxe;
                    break;
                case "Mace":
                case "TwoHandedMace":
                case "Pick":
                    pool = KillMace;
                    break;
                case "OneHandedPolearm":
                case "TwoHandedPolearm":
                case "LowGripPolearm":
                    pool = KillPolearm;
                    break;
                case "Arrow":
                case "Bow":
                    pool = KillBow;
                    break;
                case "Bolt":
                case "Crossbow":
                    pool = KillCrossbow;
                    break;
                case "Javelin":
                    pool = KillJavelin;
                    break;
                case "ThrowingAxe":
                    pool = KillThrowingAxe;
                    break;
                case "ThrowingKnife":
                    pool = KillThrowingKnife;
                    break;
                case "Stone":
                case "SlingStone":
                case "Sling":
                    pool = KillSling;
                    break;
                default:
                    if (phase == "shoot")
                        pool = KillShootGeneric;
                    else if (phase == "throw")
                        pool = KillThrowGeneric;
                    else
                        pool = KillMeleeGeneric;
                    break;
            }
            return pool[Math.Abs(counter) % pool.Length];
        }

        // The same table for a named hero going down, phrased from the victim's side.
        private static string HeroFellTemplateForWeapon(string weaponClass, string phase, int counter)
        {
            string[] pool;
            switch (weaponClass)
            {
                case "OneHandedSword":
                case "TwoHandedSword":
                    pool = HeroFell_Sword;
                    break;
                case "Dagger":
                    pool = HeroFell_Dagger;
                    break;
                case "OneHandedAxe":
                case "TwoHandedAxe":
                    pool = HeroFell_Axe;
                    break;
                case "Mace":
                case "TwoHandedMace":
                case "Pick":
                    pool = HeroFell_Mace;
                    break;
                case "OneHandedPolearm":
                case "TwoHandedPolearm":
                case "LowGripPolearm":
                    pool = HeroFell_Polearm;
                    break;
                case "Arrow":
                case "Bow":
                    pool = HeroFell_Bow;
                    break;
                case "Bolt":
                case "Crossbow":
                    pool = HeroFell_Crossbow;
                    break;
                case "Javelin":
                    pool = HeroFell_Javelin;
                    break;
                case "ThrowingAxe":
                    pool = HeroFell_ThrowingAxe;
                    break;
                case "ThrowingKnife":
                    pool = HeroFell_ThrowingKnife;
                    break;
                case "Stone":
                case "SlingStone":
                case "Sling":
                    pool = HeroFell_Sling;
                    break;
                default:
                    if (phase == "shoot")
                        pool = HeroFell_ShootGeneric;
                    else if (phase == "throw")
                        pool = HeroFell_ThrowGeneric;
                    else
                        pool = HeroFell_MeleeGeneric;
                    break;
            }
            return pool[Math.Abs(counter) % pool.Length];
        }

        // ── Small text helpers ──────────────────────────────────────────

        // The damage parenthetical, or nothing when the blow did none.
        private static string DmgText(float damage)
        {
            int dmg = (int)damage;
            if (dmg <= 0)
            {
                return string.Empty;
            }
            return new TextObject(DamageParenthetical).SetTextVariable("DMG", dmg).ToString();
        }

        // A combatant's name with its side tag, e.g. "[ATK] Vlandian Sergeant".
        private static string SidedName(string name, bool isAttacker)
        {
            return new TextObject(isAttacker ? SidedNameAttacker : SidedNameDefender)
                .SetTextVariable("NAME", name).ToString();
        }

        private static string SideNoun(bool isAttacker)
        {
            return new TextObject(isAttacker ? SideAttackers : SideDefenders).ToString();
        }
    }
}
