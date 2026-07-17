using System.Collections.Generic;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// What a formation's CAPTAIN is worth to the men under him, in a battle nobody watched.
    ///
    /// Bannerlord runs two entirely separate perk tracks, and they meet nowhere. A COMMANDER -- the party or army
    /// leader -- carries perks whose role is PartyLeader or ArmyCommander, and those apply to everyone he brought.
    /// A CAPTAIN carries perks whose role is Captain, and those apply only to the one formation he personally
    /// leads. The two are disjoint by construction: MobileParty.HasPerk has no case for PartyRole.Captain and falls
    /// through to false, and PerkHelper.AddPerkBonusFromCaptain silently no-ops on a perk with no Captain slot. So
    /// neither track can ever leak into the other, and that is why this file only has to think about one of them.
    ///
    /// AUTO-RESOLVE GETS THE COMMANDER'S DAMAGE PERKS AND NOT THE CAPTAIN TRACK. DefaultCombatSimulationModel
    /// hand-codes some fifteen party-role perks (Tactics.TightFormations' PartyLeader slot, LawKeeper, Coaching,
    /// Encirclement, the TacticalMastery epic bonus...) straight into the blow, and RBM's correction multiplies that
    /// number rather than rebuilding it, so all of them survive untouched and none of them is this file's business.
    ///
    /// The commander track is not entirely handled, mind -- vanilla's list is damage-only. His HIT-POINT perks reach
    /// no auto-resolved battle at all, and those are restored in SimulationTroopHitPoints.CommandedHealth, gated on
    /// the same <see cref="Enabled"/> as this file. They live there and not here because they are PartyLeader perks
    /// and want the man's PARTY, where everything in this file wants his formation's captain -- two different
    /// questions with two different answers, and the one place they must not be confused.
    ///
    /// The captain track is where it falls apart. The only channel auto-resolve has for it is Hero.PowerModifier,
    /// which is DefaultMilitaryPowerModel.GetPowerModifierOfHero, which COUNTS a hero's captain perks and turns the
    /// count into a flat percentage by skill tier -- 0.01 to 0.06 apiece. The perk's actual bonus, its increment
    /// type and its troop-usage mask are all thrown away, so a +20 Bow captain perk and a -25% enemy-morale captain
    /// perk are worth precisely the same. Worse, it tests PrimaryRole == Captain only, and the great majority of
    /// captain perks -- DeadAim, TightFormations, HeroicLeader, StrongGrip -- declare Captain as their SECONDARY
    /// role, so they count for nothing at all. And it is fed one hero for the whole side: the companions who would
    /// captain a formation in a live mission are worth zero.
    ///
    /// So this asks the real question instead, of the real captain, through the game's own PerkHelper: what does
    /// THIS man's training do for THESE soldiers. See SimulationCommandStructure for who that man is, and
    /// GetVanillaPowerNeutralizingFactor for why the PowerModifier count comes back out once this is on.
    ///
    /// AND BE HONEST ABOUT WHAT THAT LIFT COSTS, because it is not the clean swap it was once written up as. The
    /// proxy counts PrimaryRole == Captain and the table below is Captain-SECONDARY to a man, so the two sets do not
    /// overlap at all: there was never a double-count to prevent. Exactly two perks in the game declare Captain as
    /// their PRIMARY role -- Polearm.StandardBearer and Tactics.Gensdarmes -- and those two are the whole of what the
    /// proxy was ever paying out. Lifting it takes them away and puts nothing in their place, so a commander holding
    /// either is quietly poorer with this system on than off. It is a small sum (0.01 to 0.06 apiece, inside
    /// (1 + leader + context)) and the trade is still worth making -- a real captain's real perks for a flat count of
    /// two -- but it is a trade, not a free replacement, and the day either perk matters this paragraph is where to
    /// start.
    ///
    /// WHAT IS HERE IS A CURATED LIST, NOT ALL OF THEM. The live mission spreads roughly a hundred and nine
    /// AddPerkBonusFromCaptain call sites across four Sandbox models, hand-written at each place the effect
    /// matters; there is no dispatcher to borrow. Only the ones that land on a quantity this simulation actually
    /// prices are worth porting, and for now that means the skill perks -- see the note on the table below.
    /// </summary>
    internal static class SimulationPerks
    {
        /// <summary>
        /// The captain perks that reach a troop's SKILL, in the order their signature bits are numbered.
        ///
        /// This is SandboxAgentStatCalculateModel.GetEffectiveSkill's captain branch, ported. It is a port and not
        /// an invention: the conditions below (which skill, mounted or not, infantry-with-a-bow or archer-with-a-
        /// sword) are native's own, in native's own order, and getting them from anywhere but the decompiled method
        /// would have got them wrong -- the melee perks in particular sit inside an `else` on HasMount, so they
        /// reach a man on foot and never a rider, which is not a thing anyone would guess.
        ///
        /// Deliberately NOT here:
        ///   Riding.NimbleSteed  -- a captain perk on the Riding skill, which this simulation never reads.
        ///   Polearm.Phalanx     -- a PARTY perk (commander track), and gated on the shieldwall arrangement order,
        ///                          which is a mission concept that does not survive into the campaign layer.
        ///   Roguery.OneOfTheFamily -- also a party perk, and also the commander track's business, not ours.
        /// </summary>
        private static PerkObject[] _table;

        /// <summary>
        /// The perks resolved against the running campaign, once. DefaultPerks' statics are filled in at campaign
        /// load, so this cannot be a static initialiser -- and it is dropped between sessions along with everything
        /// else that holds a campaign object (see ResetForNewSession).
        /// </summary>
        private static PerkObject[] Table
        {
            get
            {
                if (_table == null)
                {
                    _table = new PerkObject[]
                    {
                        DefaultPerks.Throwing.FlexibleFighter,
                        DefaultPerks.Bow.DeadAim,
                        DefaultPerks.Bow.HorseMaster,
                        DefaultPerks.Athletics.StrongArms,
                        DefaultPerks.Throwing.RunningThrow,
                        DefaultPerks.Crossbow.DonkeysSwiftness,
                        DefaultPerks.OneHanded.WrappedHandles,
                        DefaultPerks.TwoHanded.StrongGrip,
                        DefaultPerks.Polearm.CleanThrust,
                        DefaultPerks.Polearm.CounterWeight
                    };
                }
                return _table;
            }
        }

        /// <summary>
        /// The system stands down with the equipment model, exactly as every other part of the overhaul does: with
        /// the model off, vanilla's blow (PowerModifier proxy and all) is left alone and there is nothing here to
        /// add to it.
        /// </summary>
        internal static bool Enabled
        {
            get
            {
                return RBMConfig.RBMConfig.simulationPerkSystem && SimulationEquipmentPower.SimulationEnabled;
            }
        }

        /// <summary>
        /// WHICH OF THE PERKS ABOVE THIS CAPTAIN OWNS, as a bitmask -- and the reason the kit cache can survive
        /// having captains in it at all.
        ///
        /// The kit cache is keyed by CharacterObject, on the sound reasoning that a troop template's gear and
        /// training do not change at runtime. A captain breaks that: the same Imperial Legionary fights on both
        /// sides of the same battle and in every party in the campaign, and folding one captain's +20 Bow into the
        /// cached kit would leak it into every other Legionary alive. So the captain joins the key -- but not as
        /// himself. Two captains with the same perks out of this list produce, by construction, byte-identical
        /// kits, so they share a cache entry and the cache stays small: a battle has a handful of distinct
        /// signatures in it, not a handful of distinct heroes.
        ///
        /// A captain with NONE of these perks signs as 0, which is the same as no captain at all -- also by
        /// construction, and also correct: he changes no skill, so the kit really is the uncaptained one.
        /// </summary>
        internal static int SignatureOf(CharacterObject captain)
        {
            if (captain == null || !captain.IsHero || captain.HeroObject == null || !Enabled)
            {
                return 0;
            }

            PerkObject[] table = Table;
            int signature = 0;
            for (int i = 0; i < table.Length; i++)
            {
                if (table[i] != null && captain.GetPerkValue(table[i]))
                {
                    signature |= (1 << i);
                }
            }
            return signature;
        }

        /// <summary>
        /// WHETHER THE CAPTAIN PERKS BELOW SEE A HORSEMAN -- and deliberately NOT the same question as
        /// SimulationBattleState.IsMountedIn, which is the one everything else in this simulation asks.
        ///
        /// The two native methods this module ports disagree with each other, and the disagreement is real rather
        /// than a reading error. GetEffectiveMaxHealth asks the AGENT (`agent.HasMount`), who has genuinely been
        /// spawned onto a horse or not, so its perks follow the battle: a lancer on a wall really is on foot there and
        /// really does collect the foot perks -- which is why SimulationTroopHitPoints passes IsMountedIn and is right
        /// to. GetEffectiveSkill asks the TEMPLATE (`characterObject.HasMount()`, the horse slot of an equipment set),
        /// which is blind to the battle and blind to the agent. So native hands a cavalry-classed archer his Horse
        /// Master on a ladder, and withholds Wrapped Handles from the dismounted lancer beside him.
        ///
        /// That is daft, and it is the rule. This file is a port of native's captain track and its whole worth is
        /// that a lord's perks pay the same whether the battle was fought or skipped; a "fix" here would only make
        /// auto-resolve disagree with the mission it stands in for, which is the one bug it cannot afford. So: battle-
        /// blind, like the method it ports. It reads the formation class rather than the equipment slot for the same
        /// reason IsMountedIn does -- the model has committed to the arm taxonomy everywhere, and a third notion of
        /// "mounted" would be worse than the small disagreement this leaves (see IsMountedIn, which argues it out).
        /// </summary>
        private static bool IsCavalryTemplate(CharacterObject troop)
        {
            return troop != null && troop.IsMounted;
        }

        /// <summary>
        /// A troop's training in one skill, with his captain's teaching folded in -- the number the kit is then
        /// priced on, so the bonus flows through the real damage, miss and defence equations exactly as a genuinely
        /// better-trained man's would, rather than being approximated by a multiplier bolted on afterwards.
        ///
        /// <paramref name="captain"/> is null for a troop with no captain, and null for the captain HIMSELF: a
        /// captain never receives his own captain perks. Native is explicit about this (GetEffectiveSkill opens by
        /// nulling the captain when he is the agent being asked about) and the exclusion is applied at the source,
        /// in SimulationCommandStructure.CaptainFor, so it cannot be forgotten at one call site and honoured at
        /// another.
        ///
        /// NOTE WHAT THIS DOES NOT CHECK: <see cref="Enabled"/>. That is deliberate, and it is what keeps the kit
        /// cache honest. A kit is cached under the captain's SIGNATURE, so if this method could return one answer
        /// with the system on and another with it off, one key would name two different kits and which one you got
        /// would depend on when it was first built -- a config toggle moved mid-battle would leave half the army
        /// priced one way and half the other, with nothing looking broken. So the question "is the system on" is
        /// asked once, upstream, where a captain is APPOINTED (SignatureOf returns 0 and Build returns an empty
        /// command when it is off), and a captain who reaches this method is a captain who counts. Signature X means
        /// exactly one kit, for the life of the session.
        /// </summary>
        internal static int SkillOf(CharacterObject troop, SkillObject skill, CharacterObject captain)
        {
            bool mounted = IsCavalryTemplate(troop);
            int baseSkill = (troop != null && skill != null) ? troop.GetSkillValue(skill) : 0;
            if (troop == null || skill == null || captain == null || captain == troop)
            {
                return baseSkill;
            }

            ExplainedNumber bonuses = new ExplainedNumber(baseSkill);

            // THE CROSS-TRAINING PERK, and the only one that asks what ARM the man belongs to rather than what is in
            // his hand: a captain with Flexible Fighter teaches his infantry to shoot and his archers to fight. Note
            // it is the troop's arm crossed against the skill being asked for -- an infantryman's Bow, an archer's
            // Polearm -- so it never fires for a man using the weapon he was trained for.
            bool rangedSkill = (skill == DefaultSkills.Bow || skill == DefaultSkills.Crossbow || skill == DefaultSkills.Throwing);
            bool meleeSkill = (skill == DefaultSkills.OneHanded || skill == DefaultSkills.TwoHanded || skill == DefaultSkills.Polearm);
            if ((troop.IsInfantry && rangedSkill) || (troop.IsRanged && meleeSkill))
            {
                PerkHelper.AddPerkBonusFromCaptain(DefaultPerks.Throwing.FlexibleFighter, captain, ref bonuses);
            }

            if (skill == DefaultSkills.Bow)
            {
                PerkHelper.AddPerkBonusFromCaptain(DefaultPerks.Bow.DeadAim, captain, ref bonuses);
                if (mounted)
                {
                    PerkHelper.AddPerkBonusFromCaptain(DefaultPerks.Bow.HorseMaster, captain, ref bonuses);
                }
            }
            else if (skill == DefaultSkills.Throwing)
            {
                PerkHelper.AddPerkBonusFromCaptain(DefaultPerks.Athletics.StrongArms, captain, ref bonuses);
                PerkHelper.AddPerkBonusFromCaptain(DefaultPerks.Throwing.RunningThrow, captain, ref bonuses);
            }
            else if (skill == DefaultSkills.Crossbow)
            {
                PerkHelper.AddPerkBonusFromCaptain(DefaultPerks.Crossbow.DonkeysSwiftness, captain, ref bonuses);
            }

            // AND THE MELEE PERKS REACH A FOOT TROOP ONLY. This is not a simplification -- it is where native puts
            // them: GetEffectiveSkill's whole melee-captain block sits in the `else` of `if (HasMount())`, so a
            // captain's Wrapped Handles does nothing whatever for his cavalry. Odd, but it is the game's rule and
            // this is a port of the game's rule. Note that a lancer STORMING A WALL is still a horseman to this test
            // and still collects none of them -- which reads wrong, and is native (the test is his TEMPLATE, which
            // kept its horse even though the wall left it in the camp). See IsCavalryTemplate.
            if (!mounted)
            {
                if (skill == DefaultSkills.OneHanded)
                {
                    PerkHelper.AddPerkBonusFromCaptain(DefaultPerks.OneHanded.WrappedHandles, captain, ref bonuses);
                }
                else if (skill == DefaultSkills.TwoHanded)
                {
                    PerkHelper.AddPerkBonusFromCaptain(DefaultPerks.TwoHanded.StrongGrip, captain, ref bonuses);
                }
                else if (skill == DefaultSkills.Polearm)
                {
                    PerkHelper.AddPerkBonusFromCaptain(DefaultPerks.Polearm.CleanThrust, captain, ref bonuses);
                    PerkHelper.AddPerkBonusFromCaptain(DefaultPerks.Polearm.CounterWeight, captain, ref bonuses);
                }
            }

            return (int)bonuses.ResultNumber;
        }

        /// <summary>
        /// Which of the table's perks this captain actually brought, by name -- for the log alone, so a battle's
        /// write-up can say what his presence was worth rather than merely that he was there. Empty for a captain
        /// with nothing relevant, which is most of them.
        /// </summary>
        internal static List<string> PerkNamesOf(CharacterObject captain)
        {
            List<string> names = new List<string>();
            if (captain == null || !captain.IsHero || captain.HeroObject == null)
            {
                return names;
            }

            PerkObject[] table = Table;
            for (int i = 0; i < table.Length; i++)
            {
                if (table[i] != null && captain.GetPerkValue(table[i]))
                {
                    names.Add(table[i].Name.ToString());
                }
            }
            return names;
        }

        /// <summary>
        /// A fresh session (new game or a loaded save). The table holds PerkObjects belonging to the campaign being
        /// torn down; rebuilt lazily against the new one on first use. Called from OnSessionLaunched.
        /// </summary>
        internal static void ResetForNewSession()
        {
            _table = null;
        }
    }
}
