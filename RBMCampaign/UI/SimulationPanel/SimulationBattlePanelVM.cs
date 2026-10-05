using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    internal class SimulationBattlePanelVM : ViewModel
    {
        private const int RoundDividerInterval = 10;

        private readonly MapEvent _mapEvent;

        private string _phaseName;
        private string _phaseDescription;
        private string _roundText;

        private string _attackerName;
        private int _attackerInfantry;
        private int _attackerRanged;
        private int _attackerCavalry;
        private int _attackerTotal;
        private int _attackerStart;

        private string _defenderName;
        private int _defenderInfantry;
        private int _defenderRanged;
        private int _defenderCavalry;
        private int _defenderTotal;
        private int _defenderStart;

        private bool _isSiege;
        private string _siegeInfo;
        private bool _isVisible;

        private MBBindingList<SimulationEventItemVM> _events;

        private int _lastRound;
        private int _lastTraceCount;
        private int _lastArtilleryCount;
        private string _lastPhaseKey;
        private bool _hadRout;
        private bool _attackerHalfReported;
        private bool _defenderHalfReported;
        private bool _attackerQuarterReported;
        private bool _defenderQuarterReported;
        private int _attackerStartCount;
        private int _defenderStartCount;
        private float _countUpdateTimer;
        private int _flavorCounter;

        private float _phaseHardestDamage;
        private CharacterObject _phaseHardestStriker;
        private CharacterObject _phaseHardestStruck;
        private string _phaseHardestWeapon;
        private string _phaseHardestAttack;
        private bool _phaseHardestStrikerIsAttacker;
        private bool _heroEventThisRound;

        public SimulationBattlePanelVM(MapEvent mapEvent)
        {
            _mapEvent = mapEvent;
            _events = new MBBindingList<SimulationEventItemVM>();
            _lastRound = -1;
            _lastTraceCount = 0;
            _lastArtilleryCount = 0;
            _lastPhaseKey = "";
            _isVisible = true;
            _flavorCounter = 0;

            _isSiege = mapEvent.IsSiegeAssault;

            _attackerName = GetSideName(mapEvent.AttackerSide);
            _defenderName = GetSideName(mapEvent.DefenderSide);

            _attackerStartCount = CountSide(mapEvent.AttackerSide);
            _defenderStartCount = CountSide(mapEvent.DefenderSide);
            _attackerStart = _attackerStartCount;
            _defenderStart = _defenderStartCount;

            _phaseName = new TextObject("{=RBM_SIM_PHASE_DEPLOYING}DEPLOYING").ToString();
            _phaseDescription = new TextObject("{=RBM_SIM_PHASE_DEPLOYING_DESC}Forces marshal on the field").ToString();
            _roundText = "";
        }

        internal void Tick(float dt)
        {
            if (_mapEvent == null || !SimulationEquipmentPower.SimulationEnabled)
            {
                return;
            }

            SimulationBattleState.BattleState state = SimulationBattleState.Get(_mapEvent);
            if (state == null)
            {
                return;
            }

            bool roundChanged = state.Round != _lastRound;

            if (state.Round > 0 && _attackerStartCount <= 0)
            {
                _attackerStartCount = CountSide(_mapEvent.AttackerSide)
                    + CasualtiesOnSide(_mapEvent.AttackerSide);
                _defenderStartCount = CountSide(_mapEvent.DefenderSide)
                    + CasualtiesOnSide(_mapEvent.DefenderSide);
                AttackerStart = _attackerStartCount;
                DefenderStart = _defenderStartCount;
            }

            UpdatePhase(state);

            _countUpdateTimer -= dt;
            if (roundChanged || _countUpdateTimer <= 0f)
            {
                _countUpdateTimer = 0.5f;
                UpdateTroopCounts();
            }

            if (roundChanged)
            {
                if (!_heroEventThisRound)
                {
                    EmitRoundHardestHit();
                }
                ResetPhaseHardestHit();
                _heroEventThisRound = false;

                _lastRound = state.Round;
                RoundText = new TextObject("{=RBM_SIM_ROUND}Round {ROUND}")
                    .SetTextVariable("ROUND", state.Round).ToString();

                if (state.Round > 1 && state.Round % RoundDividerInterval == 0)
                {
                    AddEvent(new TextObject("{=RBM_SIM_ROUND_DIVIDER}──── Round {ROUND} ────")
                        .SetTextVariable("ROUND", state.Round).ToString(), "divider");
                }

                ScanArtillery(state);
            }

            ScanTrace(state);
            CheckRout(state);
            CheckMilestones();
        }

        // ── Phase tracking with flavor ──────────────────────────────────

        // Every pool holds raw "{=ID}fallback" text (all ids listed in LOC-eng.xml) and is
        // resolved through a TextObject when a line is produced, so a language switch is
        // followed. Pick order and pool lengths are what drive the flavor rotation, so a
        // translation never changes which line is chosen.

        private static readonly string[] VolleyFlavor = new[]
        {
            "{=RBM_SIM_VOLLEY_01}Arrows darken the sky",
            "{=RBM_SIM_VOLLEY_02}Bowstrings sing across the field",
            "{=RBM_SIM_VOLLEY_03}The first shafts find their mark",
            "{=RBM_SIM_VOLLEY_04}A storm of arrows descends on the enemy",
            "{=RBM_SIM_VOLLEY_05}Volleys arc high and fall like rain",
            "{=RBM_SIM_VOLLEY_06}The air hums with feathered death",
            "{=RBM_SIM_VOLLEY_07}Shafts whistle overhead in thick waves",
            "{=RBM_SIM_VOLLEY_08}Archers loose in unison — the sky goes dark",
            "{=RBM_SIM_VOLLEY_09}A thousand bowstrings snap as one",
            "{=RBM_SIM_VOLLEY_10}The arrow storm begins its grim harvest",
            "{=RBM_SIM_VOLLEY_11}Quivers empty into the massed ranks ahead",
            "{=RBM_SIM_VOLLEY_12}Flights of arrows blot out the sun",
        };

        private static readonly string[] SkirmishFlavor = new[]
        {
            "{=RBM_SIM_SKIRMISH_01}Javelins fly as the cavalry rides out",
            "{=RBM_SIM_SKIRMISH_02}Horsemen clash between the closing lines",
            "{=RBM_SIM_SKIRMISH_03}Riders spur forward, javelins in hand",
            "{=RBM_SIM_SKIRMISH_04}The skirmish opens as the gap narrows",
            "{=RBM_SIM_SKIRMISH_05}Light horse wheel and strike at the flanks",
            "{=RBM_SIM_SKIRMISH_06}Cavalry thunder across the open ground",
            "{=RBM_SIM_SKIRMISH_07}Lances dip as the horsemen charge into the fray",
            "{=RBM_SIM_SKIRMISH_08}Javelins arc through the dust between the lines",
            "{=RBM_SIM_SKIRMISH_09}Outriders trade blows at the edges of the fight",
            "{=RBM_SIM_SKIRMISH_10}The ground shakes as mounted warriors collide",
            "{=RBM_SIM_SKIRMISH_11}Skirmishers dart forward and hurl their darts",
            "{=RBM_SIM_SKIRMISH_12}Hooves pound and javelins flash in the sun",
        };

        private static readonly string[] MeleeFlavor = new[]
        {
            "{=RBM_SIM_MELEE_01}Steel meets steel as the lines crash together",
            "{=RBM_SIM_MELEE_02}The shieldwall buckles under the press",
            "{=RBM_SIM_MELEE_03}Infantry close to sword's length at last",
            "{=RBM_SIM_MELEE_04}The lines meet with a thunderous crash",
            "{=RBM_SIM_MELEE_05}Men hack and shove in the press of bodies",
            "{=RBM_SIM_MELEE_06}The melee is a heaving mass of iron and flesh",
            "{=RBM_SIM_MELEE_07}Shields splinter under the weight of the charge",
            "{=RBM_SIM_MELEE_08}Swords ring out and men fall screaming",
            "{=RBM_SIM_MELEE_09}The battle becomes a brutal close-quarters brawl",
            "{=RBM_SIM_MELEE_10}Blades flash and blood slicks the trampled earth",
            "{=RBM_SIM_MELEE_11}The two sides grind against each other in the mud",
            "{=RBM_SIM_MELEE_12}Warriors grapple in the dust, fighting for their lives",
            "{=RBM_SIM_MELEE_13}The front line is a wall of shields, blood and iron",
            "{=RBM_SIM_MELEE_14}Axes and swords bite through armour and bone",
        };

        private static readonly string[] SiegeApproachFlavor = new[]
        {
            "{=RBM_SIM_APPROACH_01}Men sprint across the killing ground",
            "{=RBM_SIM_APPROACH_02}The besiegers advance under a hail of arrows",
            "{=RBM_SIM_APPROACH_03}Bodies pile before the gates",
            "{=RBM_SIM_APPROACH_04}The open ground before the walls is a death trap",
            "{=RBM_SIM_APPROACH_05}Defenders rain fire on the approaching columns",
            "{=RBM_SIM_APPROACH_06}Arrows hammer down from the battlements",
            "{=RBM_SIM_APPROACH_07}The assault columns push through a storm of bolts",
            "{=RBM_SIM_APPROACH_08}Men fall by the dozen crossing the open ground",
            "{=RBM_SIM_APPROACH_09}Hot sand and stones cascade from the ramparts",
            "{=RBM_SIM_APPROACH_10}The advance is a slow crawl under murderous fire",
            "{=RBM_SIM_APPROACH_11}Siege towers creak forward under a hail of missiles",
        };

        private static readonly string[] SiegeAssaultFlavor = new[]
        {
            "{=RBM_SIM_ASSAULT_01}Ladders strike the walls!",
            "{=RBM_SIM_ASSAULT_02}The storm begins at the breaches",
            "{=RBM_SIM_ASSAULT_03}Men pour through the openings",
            "{=RBM_SIM_ASSAULT_04}Fighting is hand-to-hand at the parapets",
            "{=RBM_SIM_ASSAULT_05}The besiegers claw their way onto the walls",
            "{=RBM_SIM_ASSAULT_06}Defenders shove ladders back — but more come",
            "{=RBM_SIM_ASSAULT_07}The gatehouse is a slaughterhouse",
            "{=RBM_SIM_ASSAULT_08}Blood runs down the stone steps of the battlements",
            "{=RBM_SIM_ASSAULT_09}Swords clash on the narrow walkways of the wall",
            "{=RBM_SIM_ASSAULT_10}The breach is choked with the dead of both sides",
            "{=RBM_SIM_ASSAULT_11}Attackers flood the parapet despite fearful losses",
        };

        private static readonly string[] RoutFlavor = new[]
        {
            "{=RBM_SIM_ROUT_01}Their courage breaks!",
            "{=RBM_SIM_ROUT_02}The line shatters and men flee!",
            "{=RBM_SIM_ROUT_03}Panic spreads through the ranks!",
            "{=RBM_SIM_ROUT_04}They throw down their arms and run!",
            "{=RBM_SIM_ROUT_05}The rout is on — nothing can stop it!",
            "{=RBM_SIM_ROUT_06}Their nerve fails — men turn and flee!",
            "{=RBM_SIM_ROUT_07}The retreat becomes a stampede!",
            "{=RBM_SIM_ROUT_08}Officers shout but no one listens — they run!",
            "{=RBM_SIM_ROUT_09}The formation dissolves into a fleeing mob!",
            "{=RBM_SIM_ROUT_10}Banners fall as men scatter in every direction!",
        };

        // The same lines, lower-cased, for the mid-sentence rout event ("The attackers
        // their courage breaks!"). They used to be RoutFlavor run through ToLower(), which
        // mangles translated text (German nouns, Turkish dotted i), so translators get their
        // own copy. Must stay index-aligned with RoutFlavor.
        private static readonly string[] RoutFlavorInline = new[]
        {
            "{=RBM_SIM_ROUT_INLINE_01}their courage breaks!",
            "{=RBM_SIM_ROUT_INLINE_02}the line shatters and men flee!",
            "{=RBM_SIM_ROUT_INLINE_03}panic spreads through the ranks!",
            "{=RBM_SIM_ROUT_INLINE_04}they throw down their arms and run!",
            "{=RBM_SIM_ROUT_INLINE_05}the rout is on — nothing can stop it!",
            "{=RBM_SIM_ROUT_INLINE_06}their nerve fails — men turn and flee!",
            "{=RBM_SIM_ROUT_INLINE_07}the retreat becomes a stampede!",
            "{=RBM_SIM_ROUT_INLINE_08}officers shout but no one listens — they run!",
            "{=RBM_SIM_ROUT_INLINE_09}the formation dissolves into a fleeing mob!",
            "{=RBM_SIM_ROUT_INLINE_10}banners fall as men scatter in every direction!",
        };

        private static readonly string[] HalfStrengthFlavor = new[]
        {
            "{=RBM_SIM_HALF_01}have lost half their strength — the field is littered with their dead",
            "{=RBM_SIM_HALF_02}are at half strength and wavering",
            "{=RBM_SIM_HALF_03}have taken grievous losses — half their men are down",
            "{=RBM_SIM_HALF_04}bleed freely — barely half still stand",
            "{=RBM_SIM_HALF_05}have paid a terrible price — half their number lie fallen",
            "{=RBM_SIM_HALF_06}thin visibly — the gaps in their line grow wider",
            "{=RBM_SIM_HALF_07}stagger under the weight of their casualties",
        };

        private static readonly string[] QuarterStrengthFlavor = new[]
        {
            "{=RBM_SIM_QUARTER_01}are being destroyed — barely a quarter remain",
            "{=RBM_SIM_QUARTER_02}are on the verge of annihilation",
            "{=RBM_SIM_QUARTER_03}cling to the field with a handful of survivors",
            "{=RBM_SIM_QUARTER_04}have lost three quarters of their men",
            "{=RBM_SIM_QUARTER_05}fight on in desperate knots — most of their comrades are fallen",
            "{=RBM_SIM_QUARTER_06}are a broken remnant, still fighting but doomed",
            "{=RBM_SIM_QUARTER_07}barely hold together — the end is near for them",
        };

        private TextObject PickFlavor(string[] pool)
        {
            return new TextObject(pool[_flavorCounter++ % pool.Length]);
        }

        private void UpdatePhase(SimulationBattleState.BattleState state)
        {
            string phaseKey;
            TextObject phaseName;
            string[] flavorPool;

            if (state.SiegeAssaultBattle)
            {
                if (SimulationSiege.IsApproach(state))
                {
                    phaseKey = "siege_approach";
                    phaseName = new TextObject("{=RBM_SIM_PHASE_APPROACH}APPROACH");
                    flavorPool = SiegeApproachFlavor;
                }
                else
                {
                    phaseKey = "siege_assault";
                    phaseName = new TextObject("{=RBM_SIM_PHASE_ASSAULT}ASSAULT");
                    flavorPool = SiegeAssaultFlavor;
                    UpdateSiegeInfo(state);
                }
            }
            else if (SimulationBattleState.IsVolleyPhase(state))
            {
                phaseKey = "volley";
                phaseName = new TextObject("{=RBM_SIM_PHASE_VOLLEY}VOLLEY");
                flavorPool = VolleyFlavor;
            }
            else if (SimulationBattleState.IsSkirmishPhase(state))
            {
                phaseKey = "skirmish";
                phaseName = new TextObject("{=RBM_SIM_PHASE_SKIRMISH}SKIRMISH");
                flavorPool = SkirmishFlavor;
            }
            else
            {
                phaseKey = "melee";
                phaseName = new TextObject("{=RBM_SIM_PHASE_MELEE}MELEE");
                flavorPool = MeleeFlavor;
            }

            if (state.AttackerRouted > 0 || state.DefenderRouted > 0)
            {
                phaseKey = "rout";
                phaseName = new TextObject("{=RBM_SIM_PHASE_ROUT}ROUT");
                flavorPool = RoutFlavor;
            }

            if (phaseKey != _lastPhaseKey && state.Round > 0)
            {
                _lastPhaseKey = phaseKey;

                TextObject phaseEvent = new TextObject("{=RBM_SIM_PHASE_EVENT}{PHASE} — {FLAVOR}");
                phaseEvent.SetTextVariable("PHASE", phaseName);
                phaseEvent.SetTextVariable("FLAVOR", PickFlavor(flavorPool));
                AddEvent(phaseEvent.ToString(), "phase");
            }

            PhaseName = phaseName.ToString();
            PhaseDescription = PickFlavorStable(flavorPool, state.Round / 3).ToString();
        }

        private static TextObject PickFlavorStable(string[] pool, int seed)
        {
            return new TextObject(pool[Math.Abs(seed) % pool.Length]);
        }

        private void UpdateSiegeInfo(SimulationBattleState.BattleState state)
        {
            if (state.AttackWidth > 0 || state.DefendWidth > 0)
            {
                TextObject info = new TextObject(
                    "{=RBM_SIM_SIEGE_INFO}Frontage: {ATK_WIDTH} vs {DEF_WIDTH} · Wall: {WALL}%");
                info.SetTextVariable("ATK_WIDTH", state.AttackWidth);
                info.SetTextVariable("DEF_WIDTH", state.DefendWidth);
                info.SetTextVariable("WALL", (int)(state.SiegeWallFactor * 100));
                SiegeInfo = info.ToString();
            }
        }

        // ── Hard hit tracking ───────────────────────────────────────────

        private void ResetPhaseHardestHit()
        {
            _phaseHardestDamage = 0f;
            _phaseHardestStriker = null;
            _phaseHardestStruck = null;
            _phaseHardestWeapon = null;
            _phaseHardestAttack = null;
        }

        private void TrackHardHit(HitRecord hit)
        {
            if (!hit.Downed || hit.Striker == null || hit.Struck == null)
            {
                return;
            }
            if (hit.FinalDamage > _phaseHardestDamage)
            {
                _phaseHardestDamage = hit.FinalDamage;
                _phaseHardestStriker = hit.Striker;
                _phaseHardestStruck = hit.Struck;
                _phaseHardestWeapon = hit.Weapon;
                _phaseHardestAttack = hit.Phase;
                _phaseHardestStrikerIsAttacker = hit.StrikerIsAttacker;
            }
        }

        // Round's hardest blow, one whole sentence per verb so a translator can place both
        // names freely. {STRIKER}/{VICTIM} arrive already side-tagged ("[ATK] Name").
        private static readonly string[] KillSword = new[]
        {
            "{=RBM_SIM_KILL_SWORD_01}{STRIKER} cut down {VICTIM}",
            "{=RBM_SIM_KILL_SWORD_02}{STRIKER} slashed {VICTIM}",
            "{=RBM_SIM_KILL_SWORD_03}{STRIKER} ran through {VICTIM}",
            "{=RBM_SIM_KILL_SWORD_04}{STRIKER} slew {VICTIM}",
        };
        private static readonly string[] KillDagger = new[]
        {
            "{=RBM_SIM_KILL_DAGGER_01}{STRIKER} stabbed {VICTIM}",
            "{=RBM_SIM_KILL_DAGGER_02}{STRIKER} knifed {VICTIM}",
            "{=RBM_SIM_KILL_DAGGER_03}{STRIKER} gutted {VICTIM}",
        };
        private static readonly string[] KillAxe = new[]
        {
            "{=RBM_SIM_KILL_AXE_01}{STRIKER} cleaved {VICTIM}",
            "{=RBM_SIM_KILL_AXE_02}{STRIKER} hewed down {VICTIM}",
            "{=RBM_SIM_KILL_AXE_03}{STRIKER} hacked apart {VICTIM}",
            "{=RBM_SIM_KILL_AXE_04}{STRIKER} split open {VICTIM}",
        };
        private static readonly string[] KillMace = new[]
        {
            "{=RBM_SIM_KILL_MACE_01}{STRIKER} battered down {VICTIM}",
            "{=RBM_SIM_KILL_MACE_02}{STRIKER} hammered down {VICTIM}",
            "{=RBM_SIM_KILL_MACE_03}{STRIKER} crushed {VICTIM}",
            "{=RBM_SIM_KILL_MACE_04}{STRIKER} smashed {VICTIM}",
        };
        private static readonly string[] KillPolearm = new[]
        {
            "{=RBM_SIM_KILL_POLE_01}{STRIKER} pierced {VICTIM}",
            "{=RBM_SIM_KILL_POLE_02}{STRIKER} impaled {VICTIM}",
            "{=RBM_SIM_KILL_POLE_03}{STRIKER} speared {VICTIM}",
            "{=RBM_SIM_KILL_POLE_04}{STRIKER} skewered {VICTIM}",
        };
        private static readonly string[] KillBow = new[]
        {
            "{=RBM_SIM_KILL_BOW_01}{STRIKER} shot down {VICTIM}",
            "{=RBM_SIM_KILL_BOW_02}{STRIKER} pierced with an arrow {VICTIM}",
            "{=RBM_SIM_KILL_BOW_03}{STRIKER} pinned with a shaft {VICTIM}",
            "{=RBM_SIM_KILL_BOW_04}{STRIKER} dropped with a shot {VICTIM}",
        };
        private static readonly string[] KillCrossbow = new[]
        {
            "{=RBM_SIM_KILL_XBOW_01}{STRIKER} shot down with a bolt {VICTIM}",
            "{=RBM_SIM_KILL_XBOW_02}{STRIKER} pinned with a crossbow bolt {VICTIM}",
            "{=RBM_SIM_KILL_XBOW_03}{STRIKER} dropped with a bolt {VICTIM}",
        };
        private static readonly string[] KillJavelin = new[]
        {
            "{=RBM_SIM_KILL_JAV_01}{STRIKER} speared with a javelin {VICTIM}",
            "{=RBM_SIM_KILL_JAV_02}{STRIKER} impaled with a thrown spear {VICTIM}",
            "{=RBM_SIM_KILL_JAV_03}{STRIKER} skewered at range {VICTIM}",
        };
        private static readonly string[] KillThrowingAxe = new[]
        {
            "{=RBM_SIM_KILL_TAXE_01}{STRIKER} hit with a thrown axe {VICTIM}",
            "{=RBM_SIM_KILL_TAXE_02}{STRIKER} split open with a hurled axe {VICTIM}",
            "{=RBM_SIM_KILL_TAXE_03}{STRIKER} struck down with a thrown axe {VICTIM}",
        };
        private static readonly string[] KillThrowingKnife = new[]
        {
            "{=RBM_SIM_KILL_TKNIFE_01}{STRIKER} hit with a thrown knife {VICTIM}",
            "{=RBM_SIM_KILL_TKNIFE_02}{STRIKER} struck down with a thrown blade {VICTIM}",
        };
        private static readonly string[] KillSling = new[]
        {
            "{=RBM_SIM_KILL_SLING_01}{STRIKER} brained with a sling stone {VICTIM}",
            "{=RBM_SIM_KILL_SLING_02}{STRIKER} felled with a stone {VICTIM}",
            "{=RBM_SIM_KILL_SLING_03}{STRIKER} struck down with a sling {VICTIM}",
        };
        private static readonly string[] KillShoot = new[]
        {
            "{=RBM_SIM_KILL_SHOOT_01}{STRIKER} shot down {VICTIM}",
            "{=RBM_SIM_KILL_SHOOT_02}{STRIKER} pierced {VICTIM}",
            "{=RBM_SIM_KILL_SHOOT_03}{STRIKER} struck at range {VICTIM}",
        };
        private static readonly string[] KillThrow = new[]
        {
            "{=RBM_SIM_KILL_THROW_01}{STRIKER} struck at range {VICTIM}",
            "{=RBM_SIM_KILL_THROW_02}{STRIKER} hit with a thrown weapon {VICTIM}",
        };
        private static readonly string[] KillMelee = new[]
        {
            "{=RBM_SIM_KILL_MELEE_01}{STRIKER} struck down {VICTIM}",
            "{=RBM_SIM_KILL_MELEE_02}{STRIKER} felled {VICTIM}",
            "{=RBM_SIM_KILL_MELEE_03}{STRIKER} cut down {VICTIM}",
        };

        private static TextObject KillLineForWeapon(string weaponClass, string phase, int counter)
        {
            string[] verbs;
            switch (weaponClass)
            {
                case "OneHandedSword":
                case "TwoHandedSword":
                    verbs = KillSword;
                    break;
                case "Dagger":
                    verbs = KillDagger;
                    break;
                case "OneHandedAxe":
                case "TwoHandedAxe":
                    verbs = KillAxe;
                    break;
                case "Mace":
                case "TwoHandedMace":
                case "Pick":
                    verbs = KillMace;
                    break;
                case "OneHandedPolearm":
                case "TwoHandedPolearm":
                case "LowGripPolearm":
                    verbs = KillPolearm;
                    break;
                case "Arrow":
                case "Bow":
                    verbs = KillBow;
                    break;
                case "Bolt":
                case "Crossbow":
                    verbs = KillCrossbow;
                    break;
                case "Javelin":
                    verbs = KillJavelin;
                    break;
                case "ThrowingAxe":
                    verbs = KillThrowingAxe;
                    break;
                case "ThrowingKnife":
                    verbs = KillThrowingKnife;
                    break;
                case "Stone":
                case "SlingStone":
                case "Sling":
                    verbs = KillSling;
                    break;
                default:
                    if (phase == "shoot")
                        verbs = KillShoot;
                    else if (phase == "throw")
                        verbs = KillThrow;
                    else
                        verbs = KillMelee;
                    break;
            }
            return new TextObject(verbs[Math.Abs(counter) % verbs.Length]);
        }

        // "[ATK] Name" / "[DEF] Name" — the side tag and the name as one translatable unit.
        private static string Tagged(bool isAttacker, string name)
        {
            TextObject text = isAttacker
                ? new TextObject("{=RBM_SIM_TAG_ATK}[ATK] {NAME}")
                : new TextObject("{=RBM_SIM_TAG_DEF}[DEF] {NAME}");
            text.SetTextVariable("NAME", name);
            return text.ToString();
        }

        private void EmitRoundHardestHit()
        {
            if (_phaseHardestStriker == null || _phaseHardestStruck == null || _phaseHardestDamage < 10f)
            {
                return;
            }

            TextObject line = KillLineForWeapon(_phaseHardestWeapon, _phaseHardestAttack, _flavorCounter++);
            line.SetTextVariable("STRIKER", Tagged(_phaseHardestStrikerIsAttacker, TroopName(_phaseHardestStriker)));
            line.SetTextVariable("VICTIM", Tagged(!_phaseHardestStrikerIsAttacker, TroopName(_phaseHardestStruck)));
            int dmg = (int)_phaseHardestDamage;

            AddEvent(line.ToString() + " " + DmgText(dmg), "hardhit");
        }

        // ── Trace scanning (heroes + hard hits) ────────────────────────

        // Hero lines are drawn as two widgets: the side-tagged hero name (coloured), then
        // the rest of the line. So every hero template below is the predicate only and
        // starts exactly as it renders after the name — mostly with a leading space, which
        // a translation must keep. The other party ({KILLER}, {ATTACKER}, ...) arrives
        // already side-tagged and can go anywhere in the predicate.

        private static readonly string[] HeroFellVerbs_Charge = new[]
        {
            "{=RBM_SIM_FELL_CHARGE_01} was ridden down by {KILLER}",
            "{=RBM_SIM_FELL_CHARGE_02} was trampled under the hooves of {KILLER}",
            "{=RBM_SIM_FELL_CHARGE_03} was unhorsed and crushed by {KILLER}",
            "{=RBM_SIM_FELL_CHARGE_04} was broken by the lance of {KILLER}",
            "{=RBM_SIM_FELL_CHARGE_05} was swept from the saddle by {KILLER}",
            "{=RBM_SIM_FELL_CHARGE_06} was smashed aside by the charge of {KILLER}",
        };

        private static readonly string[] HeroFellVerbs_Brace = new[]
        {
            "{=RBM_SIM_FELL_BRACE_01} was impaled on the braced spear of {KILLER}",
            "{=RBM_SIM_FELL_BRACE_02} charged into the waiting lance of {KILLER}",
            "{=RBM_SIM_FELL_BRACE_03} rode onto the set pike of {KILLER}",
            "{=RBM_SIM_FELL_BRACE_04} was skewered on the levelled polearm of {KILLER}",
        };

        private static readonly string[] FellSword = new[]
        {
            "{=RBM_SIM_FELL_SWORD_01} was cut down by {KILLER}",
            "{=RBM_SIM_FELL_SWORD_02} was slain by the blade of {KILLER}",
            "{=RBM_SIM_FELL_SWORD_03} fell to the sword of {KILLER}",
            "{=RBM_SIM_FELL_SWORD_04} was run through by {KILLER}",
        };
        private static readonly string[] FellDagger = new[]
        {
            "{=RBM_SIM_FELL_DAGGER_01} was stabbed down by {KILLER}",
            "{=RBM_SIM_FELL_DAGGER_02} was knifed by {KILLER}",
            "{=RBM_SIM_FELL_DAGGER_03} fell to the dagger of {KILLER}",
        };
        private static readonly string[] FellAxe = new[]
        {
            "{=RBM_SIM_FELL_AXE_01} was cleaved apart by {KILLER}",
            "{=RBM_SIM_FELL_AXE_02} was hewn down by {KILLER}",
            "{=RBM_SIM_FELL_AXE_03} fell to the axe of {KILLER}",
            "{=RBM_SIM_FELL_AXE_04} was split open by {KILLER}",
        };
        private static readonly string[] FellMace = new[]
        {
            "{=RBM_SIM_FELL_MACE_01} was battered down by {KILLER}",
            "{=RBM_SIM_FELL_MACE_02} was crushed by {KILLER}",
            "{=RBM_SIM_FELL_MACE_03} had their skull caved in by {KILLER}",
            "{=RBM_SIM_FELL_MACE_04} was hammered to the ground by {KILLER}",
        };
        private static readonly string[] FellPolearm = new[]
        {
            "{=RBM_SIM_FELL_POLE_01} was pierced by the spear of {KILLER}",
            "{=RBM_SIM_FELL_POLE_02} was impaled by {KILLER}",
            "{=RBM_SIM_FELL_POLE_03} was run through by the lance of {KILLER}",
            "{=RBM_SIM_FELL_POLE_04} fell to the polearm of {KILLER}",
        };
        private static readonly string[] FellBow = new[]
        {
            "{=RBM_SIM_FELL_BOW_01} was felled by an arrow from {KILLER}",
            "{=RBM_SIM_FELL_BOW_02} was shot down by {KILLER}",
            "{=RBM_SIM_FELL_BOW_03} took a fatal shaft from {KILLER}",
            "{=RBM_SIM_FELL_BOW_04} was pierced by an arrow from {KILLER}",
        };
        private static readonly string[] FellCrossbow = new[]
        {
            "{=RBM_SIM_FELL_XBOW_01} was dropped by a bolt from {KILLER}",
            "{=RBM_SIM_FELL_XBOW_02} was pinned by a crossbow bolt from {KILLER}",
            "{=RBM_SIM_FELL_XBOW_03} took a killing bolt from {KILLER}",
        };
        private static readonly string[] FellJavelin = new[]
        {
            "{=RBM_SIM_FELL_JAV_01} was speared by a javelin from {KILLER}",
            "{=RBM_SIM_FELL_JAV_02} was impaled at range by {KILLER}",
            "{=RBM_SIM_FELL_JAV_03} took a hurled spear from {KILLER}",
        };
        private static readonly string[] FellThrowingAxe = new[]
        {
            "{=RBM_SIM_FELL_TAXE_01} was struck by a thrown axe from {KILLER}",
            "{=RBM_SIM_FELL_TAXE_02} was split open by a hurled axe from {KILLER}",
        };
        private static readonly string[] FellThrowingKnife = new[]
        {
            "{=RBM_SIM_FELL_TKNIFE_01} was struck down by a thrown knife from {KILLER}",
            "{=RBM_SIM_FELL_TKNIFE_02} took a thrown blade from {KILLER}",
        };
        private static readonly string[] FellSling = new[]
        {
            "{=RBM_SIM_FELL_SLING_01} was brained by a sling stone from {KILLER}",
            "{=RBM_SIM_FELL_SLING_02} was felled by a stone from {KILLER}",
        };
        private static readonly string[] FellShoot = new[]
        {
            "{=RBM_SIM_FELL_SHOOT_01} was shot down by {KILLER}",
            "{=RBM_SIM_FELL_SHOOT_02} was struck at range by {KILLER}",
            "{=RBM_SIM_FELL_SHOOT_03} was felled by a missile from {KILLER}",
        };
        private static readonly string[] FellThrow = new[]
        {
            "{=RBM_SIM_FELL_THROW_01} was struck down at range by {KILLER}",
            "{=RBM_SIM_FELL_THROW_02} was felled by a thrown weapon from {KILLER}",
        };
        private static readonly string[] FellMelee = new[]
        {
            "{=RBM_SIM_FELL_MELEE_01} was struck down by {KILLER}",
            "{=RBM_SIM_FELL_MELEE_02} was felled by {KILLER}",
            "{=RBM_SIM_FELL_MELEE_03} fell in combat with {KILLER}",
        };

        private static TextObject HeroFellLineForWeapon(string weaponClass, string phase, int counter)
        {
            string[] verbs;
            switch (weaponClass)
            {
                case "OneHandedSword":
                case "TwoHandedSword":
                    verbs = FellSword;
                    break;
                case "Dagger":
                    verbs = FellDagger;
                    break;
                case "OneHandedAxe":
                case "TwoHandedAxe":
                    verbs = FellAxe;
                    break;
                case "Mace":
                case "TwoHandedMace":
                case "Pick":
                    verbs = FellMace;
                    break;
                case "OneHandedPolearm":
                case "TwoHandedPolearm":
                case "LowGripPolearm":
                    verbs = FellPolearm;
                    break;
                case "Arrow":
                case "Bow":
                    verbs = FellBow;
                    break;
                case "Bolt":
                case "Crossbow":
                    verbs = FellCrossbow;
                    break;
                case "Javelin":
                    verbs = FellJavelin;
                    break;
                case "ThrowingAxe":
                    verbs = FellThrowingAxe;
                    break;
                case "ThrowingKnife":
                    verbs = FellThrowingKnife;
                    break;
                case "Stone":
                case "SlingStone":
                case "Sling":
                    verbs = FellSling;
                    break;
                default:
                    if (phase == "shoot")
                        verbs = FellShoot;
                    else if (phase == "throw")
                        verbs = FellThrow;
                    else
                        verbs = FellMelee;
                    break;
            }
            return new TextObject(verbs[Math.Abs(counter) % verbs.Length]);
        }

        private static bool HitInvolvesPlayer(HitRecord hit)
        {
            return (hit.Striker != null && hit.Striker.IsPlayerCharacter)
                || (hit.Struck != null && hit.Struck.IsPlayerCharacter);
        }

        private void ScanTrace(SimulationBattleState.BattleState state)
        {
            if (state.Trace == null)
            {
                return;
            }

            int count = state.Trace.Count;
            for (int i = _lastTraceCount; i < count; i++)
            {
                HitRecord hit = state.Trace[i];

                TrackHardHit(hit);

                if (hit.Downed && hit.Struck != null && hit.Struck.IsHero)
                {
                    EmitHeroCasualty(hit);
                    _heroEventThisRound = true;
                    continue;
                }

                bool isPlayer = HitInvolvesPlayer(hit);

                if (isPlayer)
                {
                    EmitPlayerHitEvent(hit);
                    continue;
                }

                if (_heroEventThisRound)
                {
                    continue;
                }

                if (TryEmitHeroAction(hit))
                {
                    _heroEventThisRound = true;
                }
            }
            _lastTraceCount = count;
        }

        private bool TryEmitHeroAction(HitRecord hit)
        {
            if (hit.Struck != null && hit.Struck.IsHero && hit.Defense == "riposte")
            {
                EmitHeroDefense(hit);
                return true;
            }
            if (hit.Struck != null && hit.Struck.IsHero
                && (hit.Defense == "parry" || hit.Defense == "weapon-block" || hit.Defense == "shield-block"))
            {
                EmitHeroDefense(hit);
                return true;
            }
            if (hit.Striker != null && hit.Striker.IsHero && hit.Downed
                && hit.Struck != null && !hit.Struck.IsHero)
            {
                EmitHeroKill(hit);
                return true;
            }
            if (hit.Struck != null && hit.Struck.IsHero
                && (hit.Phase == "shoot" || hit.Phase == "throw")
                && (hit.BodyPart == "head" || hit.BodyPart == "neck")
                && hit.FinalDamage > 20f)
            {
                EmitHeroHeadshot(hit);
                return true;
            }
            if (hit.Striker != null && hit.Striker.IsHero
                && (hit.Phase == "shoot" || hit.Phase == "throw")
                && (hit.BodyPart == "head" || hit.BodyPart == "neck")
                && hit.FinalDamage > 20f && hit.Struck != null)
            {
                EmitHeroSniped(hit);
                return true;
            }
            return false;
        }

        // ── Player hero: every blow they're involved in ─────────────

        private static readonly string[] PlayerHitVerbs = new[]
        {
            "{=RBM_SIM_PLAYER_HIT_01} strikes {TARGET}",
            "{=RBM_SIM_PLAYER_HIT_02} lands a blow on {TARGET}",
            "{=RBM_SIM_PLAYER_HIT_03} hits {TARGET}",
            "{=RBM_SIM_PLAYER_HIT_04} connects with {TARGET}",
        };

        private static readonly string[] PlayerTakeHitVerbs = new[]
        {
            "{=RBM_SIM_PLAYER_TAKE_01} takes a hit from {ATTACKER}",
            "{=RBM_SIM_PLAYER_TAKE_02} is struck by {ATTACKER}",
            "{=RBM_SIM_PLAYER_TAKE_03} is hit by {ATTACKER}",
            "{=RBM_SIM_PLAYER_TAKE_04} absorbs a blow from {ATTACKER}",
        };

        private static readonly string[] PlayerMissVerbs = new[]
        {
            "{=RBM_SIM_PLAYER_DODGE_01} dodges a shot from {ATTACKER}",
            "{=RBM_SIM_PLAYER_DODGE_02} evades {ATTACKER}",
            "{=RBM_SIM_PLAYER_DODGE_03} sidesteps a blow from {ATTACKER}",
        };

        private static readonly string[] PlayerMissedShotVerbs = new[]
        {
            "{=RBM_SIM_PLAYER_MISS_01} misses a shot at {TARGET}",
            "{=RBM_SIM_PLAYER_MISS_02} sends a shaft wide of {TARGET}",
            "{=RBM_SIM_PLAYER_MISS_03} looses at {TARGET}",
        };

        private void EmitPlayerHitEvent(HitRecord hit)
        {
            bool playerIsStriker = hit.Striker != null && hit.Striker.IsPlayerCharacter;
            bool playerIsStruck = hit.Struck != null && hit.Struck.IsPlayerCharacter;

            string playerName = playerIsStriker
                ? (hit.Striker.Name != null ? hit.Striker.Name.ToString() : YouName())
                : (hit.Struck.Name != null ? hit.Struck.Name.ToString() : YouName());
            string playerTagged = Tagged(playerIsStriker ? hit.StrikerIsAttacker : !hit.StrikerIsAttacker, playerName);
            CharacterObject playerChar = playerIsStriker ? hit.Striker : hit.Struck;

            string otherName = playerIsStriker ? TroopName(hit.Struck) : TroopName(hit.Striker);
            string otherTagged = Tagged(playerIsStriker ? !hit.StrikerIsAttacker : hit.StrikerIsAttacker, otherName);

            if (hit.Evaded || hit.Closing)
            {
                if (playerIsStruck)
                {
                    TextObject rest = PickFlavor(PlayerMissVerbs);
                    rest.SetTextVariable("ATTACKER", otherTagged);
                    AddHeroEvent(playerTagged, rest.ToString(), "hero", playerChar);
                }
                else
                {
                    TextObject rest = PickFlavor(PlayerMissedShotVerbs);
                    rest.SetTextVariable("TARGET", otherTagged);
                    AddHeroEvent(playerTagged, rest.ToString(), "hero", playerChar);
                }
                return;
            }

            if (hit.Defense != null && hit.Defense != "none")
            {
                if (playerIsStruck)
                {
                    EmitHeroDefense(hit);
                }
                else
                {
                    // Possessive on the player's name: the line continues straight after it.
                    TextObject defenseDesc;
                    switch (hit.Defense)
                    {
                        case "riposte":     defenseDesc = new TextObject("{=RBM_SIM_PLAYER_DEF_RIPOSTE}'s blow is parried and countered by {DEFENDER}"); break;
                        case "parry":       defenseDesc = new TextObject("{=RBM_SIM_PLAYER_DEF_PARRY}'s attack is parried by {DEFENDER}"); break;
                        case "shield-block": defenseDesc = new TextObject("{=RBM_SIM_PLAYER_DEF_SHIELD}'s strike is blocked by {DEFENDER}"); break;
                        case "weapon-block": defenseDesc = new TextObject("{=RBM_SIM_PLAYER_DEF_WEAPON}'s blow is deflected by {DEFENDER}"); break;
                        default:            defenseDesc = new TextObject("{=RBM_SIM_PLAYER_DEF_OTHER}'s attack is defended by {DEFENDER}"); break;
                    }
                    defenseDesc.SetTextVariable("DEFENDER", otherTagged);
                    AddHeroEvent(playerTagged,
                        defenseDesc.ToString() + DmgSuffix(hit.FinalDamage), "hero", playerChar);
                }
                return;
            }

            if (playerIsStriker)
            {
                if (hit.Downed)
                {
                    EmitHeroKill(hit);
                }
                else
                {
                    TextObject rest = PickFlavor(PlayerHitVerbs);
                    rest.SetTextVariable("TARGET", otherTagged);
                    AddHeroEvent(playerTagged,
                        rest.ToString() + DmgSuffix(hit.FinalDamage),
                        "hero", playerChar);
                }
            }
            else
            {
                TextObject rest = PickFlavor(PlayerTakeHitVerbs);
                rest.SetTextVariable("ATTACKER", otherTagged);
                AddHeroEvent(playerTagged,
                    rest.ToString() + DmgSuffix(hit.FinalDamage),
                    "hero", playerChar);
            }
        }

        private void EmitHeroCasualty(HitRecord hit)
        {
            string victimName = hit.Struck.Name != null ? hit.Struck.Name.ToString() : ALordName();
            string killerName = hit.Striker != null
                ? TroopName(hit.Striker)
                : new TextObject("{=RBM_SIM_NAME_UNKNOWN_ASSAILANT}an unknown assailant").ToString();

            TextObject rest;
            if (hit.Braced)
            {
                rest = PickFlavor(HeroFellVerbs_Brace);
            }
            else if (hit.ChargeBonus > 5f)
            {
                rest = PickFlavor(HeroFellVerbs_Charge);
            }
            else
            {
                rest = HeroFellLineForWeapon(hit.Weapon, hit.Phase, _flavorCounter++);
            }
            rest.SetTextVariable("KILLER", Tagged(hit.StrikerIsAttacker, killerName));

            AddHeroEvent(Tagged(!hit.StrikerIsAttacker, victimName),
                rest.ToString() + DmgSuffix(hit.FinalDamage), "hero",
                hit.Struck);
        }

        // "(37 dmg)". DmgSuffix is the optional " (37 dmg)" tail, empty when no damage landed.
        private static string DmgText(int dmg)
        {
            return new TextObject("{=RBM_SIM_DMG}({DMG} dmg)").SetTextVariable("DMG", dmg).ToString();
        }

        private static string DmgSuffix(float damage)
        {
            int dmg = (int)damage;
            return dmg > 0 ? " " + DmgText(dmg) : "";
        }

        private static readonly string[] RiposteFlavor = new[]
        {
            "{=RBM_SIM_RIPOSTE_01} parries a blow from {ATTACKER} and strikes back",
            "{=RBM_SIM_RIPOSTE_02} deflects {ATTACKER}'s attack and counters",
            "{=RBM_SIM_RIPOSTE_03} turns aside {ATTACKER}'s blade and ripostes",
            "{=RBM_SIM_RIPOSTE_04} catches {ATTACKER}'s strike and drives their own home",
            "{=RBM_SIM_RIPOSTE_05} sidesteps {ATTACKER} and delivers a vicious counterstrike",
        };

        private static readonly string[] ParryFlavor = new[]
        {
            "{=RBM_SIM_PARRY_01} blocks a blow from {ATTACKER}",
            "{=RBM_SIM_PARRY_02} catches {ATTACKER}'s strike on their shield",
            "{=RBM_SIM_PARRY_03} turns aside a blow from {ATTACKER}",
            "{=RBM_SIM_PARRY_04} deflects {ATTACKER}'s attack",
        };

        private void EmitHeroDefense(HitRecord hit)
        {
            string heroName = hit.Struck.Name != null ? hit.Struck.Name.ToString() : ALordName();
            string attackerName = hit.Striker != null
                ? TroopName(hit.Striker)
                : new TextObject("{=RBM_SIM_NAME_AN_ATTACKER}an attacker").ToString();
            string heroTagged = Tagged(!hit.StrikerIsAttacker, heroName);
            string attackerFull = Tagged(hit.StrikerIsAttacker, attackerName);

            if (hit.Defense == "riposte")
            {
                TextObject flavor = PickFlavor(RiposteFlavor);
                flavor.SetTextVariable("ATTACKER", attackerFull);
                AddHeroEvent(heroTagged, flavor.ToString() + DmgSuffix(hit.FinalDamage), "hero", hit.Struck);
            }
            else if (hit.Defense == "parry" || hit.Defense == "weapon-block" || hit.Defense == "shield-block")
            {
                TextObject flavor = PickFlavor(ParryFlavor);
                flavor.SetTextVariable("ATTACKER", attackerFull);
                AddHeroEvent(heroTagged, flavor.ToString() + DmgSuffix(hit.FinalDamage), "hero", hit.Struck);
            }
        }

        private static readonly string[] HeroKillFlavor_Melee = new[]
        {
            "{=RBM_SIM_HERO_KILL_MELEE_01} cuts down {VICTIM}",
            "{=RBM_SIM_HERO_KILL_MELEE_02} strikes down {VICTIM}",
            "{=RBM_SIM_HERO_KILL_MELEE_03} slays {VICTIM}",
            "{=RBM_SIM_HERO_KILL_MELEE_04} fells {VICTIM}",
            "{=RBM_SIM_HERO_KILL_MELEE_05} sends another to the grave — {VICTIM}",
        };

        private static readonly string[] HeroKillFlavor_Ranged = new[]
        {
            "{=RBM_SIM_HERO_KILL_RANGED_01} picks off {VICTIM}",
            "{=RBM_SIM_HERO_KILL_RANGED_02} drops {VICTIM}",
            "{=RBM_SIM_HERO_KILL_RANGED_03} shoots down {VICTIM}",
            "{=RBM_SIM_HERO_KILL_RANGED_04} finds their mark — {VICTIM}",
        };

        private void EmitHeroKill(HitRecord hit)
        {
            string heroName = hit.Striker.Name != null ? hit.Striker.Name.ToString() : ALordName();
            string victimName = TroopName(hit.Struck);

            bool ranged = hit.Phase == "shoot" || hit.Phase == "throw";
            string[] pool = ranged ? HeroKillFlavor_Ranged : HeroKillFlavor_Melee;
            TextObject rest = PickFlavor(pool);
            rest.SetTextVariable("VICTIM", Tagged(!hit.StrikerIsAttacker, victimName));

            AddHeroEvent(Tagged(hit.StrikerIsAttacker, heroName), rest.ToString()
                + DmgSuffix(hit.FinalDamage), "hero", hit.Striker);
        }

        private static readonly string[] HeadshotFlavor = new[]
        {
            "{=RBM_SIM_HEADSHOT_01} takes an arrow to the head!",
            "{=RBM_SIM_HEADSHOT_02} is struck in the face by a missile!",
            "{=RBM_SIM_HEADSHOT_03} catches a bolt in the skull!",
            "{=RBM_SIM_HEADSHOT_04} is hit square in the head at range!",
            "{=RBM_SIM_HEADSHOT_05} takes a shot clean through the helm!",
        };

        private void EmitHeroHeadshot(HitRecord hit)
        {
            string heroName = hit.Struck.Name != null ? hit.Struck.Name.ToString() : ALordName();
            int dmg = (int)hit.FinalDamage;
            AddHeroEvent(Tagged(!hit.StrikerIsAttacker, heroName),
                PickFlavor(HeadshotFlavor).ToString() + " " + DmgText(dmg), "hero",
                hit.Struck);
        }

        private static readonly string[] HeroSnipeFlavor = new[]
        {
            "{=RBM_SIM_SNIPE_01} lands a perfect headshot on {VICTIM}",
            "{=RBM_SIM_SNIPE_02} puts an arrow clean through the helm of {VICTIM}",
            "{=RBM_SIM_SNIPE_03} nails a shot to the head of {VICTIM}",
            "{=RBM_SIM_SNIPE_04} finds the gap in the visor of {VICTIM}",
            "{=RBM_SIM_SNIPE_05} sends a bolt straight through the skull of {VICTIM}",
        };

        private void EmitHeroSniped(HitRecord hit)
        {
            string heroName = hit.Striker.Name != null ? hit.Striker.Name.ToString() : ALordName();
            string victimName = TroopName(hit.Struck);
            int dmg = (int)hit.FinalDamage;
            TextObject rest = PickFlavor(HeroSnipeFlavor);
            rest.SetTextVariable("VICTIM", Tagged(!hit.StrikerIsAttacker, victimName));
            AddHeroEvent(Tagged(hit.StrikerIsAttacker, heroName),
                rest.ToString() + " " + DmgText(dmg), "hero",
                hit.Striker);
        }

        // ── Artillery ───────────────────────────────────────────────────

        private static readonly string[] ArtilleryFlavor = new[]
        {
            "{=RBM_SIM_ARTY_01}Stones crash into the defenders",
            "{=RBM_SIM_ARTY_02}A boulder smashes through the ranks",
            "{=RBM_SIM_ARTY_03}The engines hurl death at the walls",
            "{=RBM_SIM_ARTY_04}Siege stones tear men apart",
            "{=RBM_SIM_ARTY_05}A catapult stone carves a path through the crowd",
            "{=RBM_SIM_ARTY_06}Trebuchet fire hammers the fortifications",
            "{=RBM_SIM_ARTY_07}The ground shakes as heavy stones find their targets",
            "{=RBM_SIM_ARTY_08}Engine crews heave and another stone arcs skyward",
            "{=RBM_SIM_ARTY_09}A volley of stones rains down from the siege line",
        };

        private static readonly string[] ArtilleryDestroyFlavor = new[]
        {
            "{=RBM_SIM_ARTY_DESTROY_01}An enemy engine is shattered to splinters!",
            "{=RBM_SIM_ARTY_DESTROY_02}A direct hit reduces an engine to kindling!",
            "{=RBM_SIM_ARTY_DESTROY_03}A well-aimed stone destroys an enemy machine!",
            "{=RBM_SIM_ARTY_DESTROY_04}An engine erupts into flying timber and rope!",
            "{=RBM_SIM_ARTY_DESTROY_05}The crew dives clear as their machine is wrecked!",
        };

        private void ScanArtillery(SimulationBattleState.BattleState state)
        {
            if (state.Artillery == null)
            {
                return;
            }

            int count = state.Artillery.Count;
            int killed = 0;
            int wounded = 0;
            int destroyed = 0;

            for (int i = _lastArtilleryCount; i < count; i++)
            {
                ArtilleryRecord shot = state.Artillery[i];
                if (shot.Round != state.Round)
                {
                    continue;
                }
                if (shot.Hit)
                {
                    killed += shot.Killed;
                    wounded += shot.Wounded;
                    if (shot.Destroyed)
                    {
                        destroyed++;
                    }
                }
            }

            if (killed > 0 || wounded > 0)
            {
                TextObject msg = wounded > 0
                    ? new TextObject("{=RBM_SIM_ARTY_KILLED_WOUNDED}{FLAVOR} — {KILLED} killed, {WOUNDED} wounded")
                    : new TextObject("{=RBM_SIM_ARTY_KILLED}{FLAVOR} — {KILLED} killed");
                msg.SetTextVariable("FLAVOR", PickFlavor(ArtilleryFlavor));
                msg.SetTextVariable("KILLED", killed);
                msg.SetTextVariable("WOUNDED", wounded);
                AddEvent(msg.ToString(), "artillery");
            }

            if (destroyed > 0)
            {
                AddEvent(PickFlavor(ArtilleryDestroyFlavor).ToString(), "artillery");
            }

            _lastArtilleryCount = count;
        }

        // ── Rout & milestones ───────────────────────────────────────────

        private void CheckRout(SimulationBattleState.BattleState state)
        {
            if (_hadRout)
            {
                return;
            }
            if (state.AttackerRouted > 0)
            {
                _hadRout = true;
                TextObject msg = new TextObject("{=RBM_SIM_ROUT_EVENT_ATK}The attackers {FLAVOR} ({COUNT} fled)");
                msg.SetTextVariable("FLAVOR", PickFlavor(RoutFlavorInline));
                msg.SetTextVariable("COUNT", state.AttackerRouted);
                AddEvent(msg.ToString(), "rout");
            }
            else if (state.DefenderRouted > 0)
            {
                _hadRout = true;
                TextObject msg = new TextObject("{=RBM_SIM_ROUT_EVENT_DEF}The defenders {FLAVOR} ({COUNT} fled)");
                msg.SetTextVariable("FLAVOR", PickFlavor(RoutFlavorInline));
                msg.SetTextVariable("COUNT", state.DefenderRouted);
                AddEvent(msg.ToString(), "rout");
            }
        }

        private void CheckMilestones()
        {
            if (_attackerStartCount > 0)
            {
                if (!_attackerHalfReported && _attackerTotal <= _attackerStartCount / 2)
                {
                    _attackerHalfReported = true;
                    AddMilestone(true, HalfStrengthFlavor);
                }
                else if (!_attackerQuarterReported && _attackerTotal <= _attackerStartCount / 4)
                {
                    _attackerQuarterReported = true;
                    AddMilestone(true, QuarterStrengthFlavor);
                }
            }
            if (_defenderStartCount > 0)
            {
                if (!_defenderHalfReported && _defenderTotal <= _defenderStartCount / 2)
                {
                    _defenderHalfReported = true;
                    AddMilestone(false, HalfStrengthFlavor);
                }
                else if (!_defenderQuarterReported && _defenderTotal <= _defenderStartCount / 4)
                {
                    _defenderQuarterReported = true;
                    AddMilestone(false, QuarterStrengthFlavor);
                }
            }
        }

        private void AddMilestone(bool attackers, string[] pool)
        {
            TextObject msg = attackers
                ? new TextObject("{=RBM_SIM_MILESTONE_ATK}The attackers {FLAVOR}")
                : new TextObject("{=RBM_SIM_MILESTONE_DEF}The defenders {FLAVOR}");
            msg.SetTextVariable("FLAVOR", PickFlavor(pool));
            AddEvent(msg.ToString(), "milestone");
        }

        // ── Troop counts ────────────────────────────────────────────────

        private void UpdateTroopCounts()
        {
            int atkInf = 0, atkRan = 0, atkCav = 0, atkTotal = 0;
            int defInf = 0, defRan = 0, defCav = 0, defTotal = 0;

            CountSideByArm(_mapEvent.AttackerSide, ref atkInf, ref atkRan, ref atkCav, ref atkTotal);
            CountSideByArm(_mapEvent.DefenderSide, ref defInf, ref defRan, ref defCav, ref defTotal);

            AttackerInfantry = atkInf;
            AttackerRanged = atkRan;
            AttackerCavalry = atkCav;
            AttackerTotal = atkTotal;

            DefenderInfantry = defInf;
            DefenderRanged = defRan;
            DefenderCavalry = defCav;
            DefenderTotal = defTotal;
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private void AddEvent(string message, string eventType)
        {
            _events.Add(new SimulationEventItemVM(message, eventType));
        }

        private void AddHeroEvent(string heroName, string rest, string eventType,
            CharacterObject heroCharacter = null)
        {
            bool isPlayer = heroCharacter != null && heroCharacter.IsPlayerCharacter;
            _events.Add(new SimulationEventItemVM(heroName + rest, eventType, heroName, rest, isPlayer));
        }

        private static string TroopName(CharacterObject troop)
        {
            if (troop == null)
            {
                return new TextObject("{=RBM_SIM_NAME_UNKNOWN_SOLDIER}an unknown soldier").ToString();
            }
            if (troop.Name != null)
            {
                return troop.Name.ToString();
            }
            return troop.IsHero
                ? new TextObject("{=RBM_SIM_NAME_A_LORD}a lord").ToString()
                : new TextObject("{=RBM_SIM_NAME_A_SOLDIER}a soldier").ToString();
        }

        // Fallback names for a nameless hero at the start of a line, and for the player.
        private static string ALordName()
        {
            return new TextObject("{=RBM_SIM_NAME_A_LORD_START}A lord").ToString();
        }

        private static string YouName()
        {
            return new TextObject("{=RBM_SIM_NAME_YOU}You").ToString();
        }

        private static string GetSideName(MapEventSide side)
        {
            if (side == null || side.LeaderParty == null)
            {
                return new TextObject("{=RBM_SIM_NAME_UNKNOWN_SIDE}Unknown").ToString();
            }
            PartyBase leader = side.LeaderParty;
            if (leader.MapFaction != null && leader.MapFaction.Name != null)
            {
                return leader.MapFaction.Name.ToString();
            }
            return leader.Name != null
                ? leader.Name.ToString()
                : new TextObject("{=RBM_SIM_NAME_UNKNOWN_SIDE}Unknown").ToString();
        }

        private static int CountSide(MapEventSide side)
        {
            if (side == null)
            {
                return 0;
            }
            int total = 0;
            foreach (MapEventParty party in side.Parties)
            {
                if (party.Party == null || party.Party.MemberRoster == null)
                {
                    continue;
                }
                for (int i = 0; i < party.Party.MemberRoster.Count; i++)
                {
                    TaleWorlds.CampaignSystem.Roster.TroopRosterElement el =
                        party.Party.MemberRoster.GetElementCopyAtIndex(i);
                    int healthy = el.Number - el.WoundedNumber;
                    if (healthy > 0)
                    {
                        total += healthy;
                    }
                }
            }
            return total;
        }

        private static int CasualtiesOnSide(MapEventSide side)
        {
            return side != null ? side.TroopCasualties : 0;
        }

        private static void CountSideByArm(MapEventSide side,
            ref int infantry, ref int ranged, ref int cavalry, ref int total)
        {
            if (side == null)
            {
                return;
            }
            foreach (MapEventParty party in side.Parties)
            {
                if (party.Party == null || party.Party.MemberRoster == null)
                {
                    continue;
                }
                for (int i = 0; i < party.Party.MemberRoster.Count; i++)
                {
                    TaleWorlds.CampaignSystem.Roster.TroopRosterElement el =
                        party.Party.MemberRoster.GetElementCopyAtIndex(i);
                    int healthy = el.Number - el.WoundedNumber;
                    if (healthy <= 0 || el.Character == null)
                    {
                        continue;
                    }
                    total += healthy;
                    int arm = SimulationEquipmentPower.ArmOf(el.Character);
                    switch (arm)
                    {
                        case SimulationEquipmentPower.ArcherType:
                            ranged += healthy;
                            break;
                        case SimulationEquipmentPower.CavalryType:
                        case SimulationEquipmentPower.HorseArcherType:
                            cavalry += healthy;
                            break;
                        default:
                            infantry += healthy;
                            break;
                    }
                }
            }
        }

        // ── Formatted text properties ───────────────────────────────────

        // Panel headings. They used to be literal Text="..." in SimulationBattlePanel.xml,
        // which no language file can reach.
        [DataSourceProperty]
        public string PanelTitle => new TextObject("{=RBM_SIM_TITLE}RBM Campaign — Detailed Auto-Resolve").ToString();

        [DataSourceProperty]
        public string ChronicleTitle => new TextObject("{=RBM_SIM_CHRONICLE}Battle Chronicle").ToString();

        [DataSourceProperty]
        public string AttackerInfantryText => CountText("{=RBM_SIM_COUNT_INF}Inf: {COUNT}", _attackerInfantry);

        [DataSourceProperty]
        public string AttackerRangedText => CountText("{=RBM_SIM_COUNT_RAN}Ran: {COUNT}", _attackerRanged);

        [DataSourceProperty]
        public string AttackerCavalryText => CountText("{=RBM_SIM_COUNT_CAV}Cav: {COUNT}", _attackerCavalry);

        [DataSourceProperty]
        public string AttackerTotalText => TotalText(_attackerTotal, _attackerStart);

        [DataSourceProperty]
        public string DefenderInfantryText => CountText("{=RBM_SIM_COUNT_INF}Inf: {COUNT}", _defenderInfantry);

        [DataSourceProperty]
        public string DefenderRangedText => CountText("{=RBM_SIM_COUNT_RAN}Ran: {COUNT}", _defenderRanged);

        [DataSourceProperty]
        public string DefenderCavalryText => CountText("{=RBM_SIM_COUNT_CAV}Cav: {COUNT}", _defenderCavalry);

        [DataSourceProperty]
        public string DefenderTotalText => TotalText(_defenderTotal, _defenderStart);

        private static string CountText(string template, int count)
        {
            return new TextObject(template).SetTextVariable("COUNT", count).ToString();
        }

        private static string TotalText(int total, int start)
        {
            TextObject text = new TextObject("{=RBM_SIM_COUNT_TOTAL}Total: {COUNT} / {START}");
            text.SetTextVariable("COUNT", total);
            text.SetTextVariable("START", start);
            return text.ToString();
        }

        // ── DataSource Properties ───────────────────────────────────────

        [DataSourceProperty]
        public string PhaseName
        {
            get => _phaseName;
            set
            {
                if (_phaseName != value)
                {
                    _phaseName = value;
                    OnPropertyChangedWithValue(value, "PhaseName");
                }
            }
        }

        [DataSourceProperty]
        public string PhaseDescription
        {
            get => _phaseDescription;
            set
            {
                if (_phaseDescription != value)
                {
                    _phaseDescription = value;
                    OnPropertyChangedWithValue(value, "PhaseDescription");
                }
            }
        }

        [DataSourceProperty]
        public string RoundText
        {
            get => _roundText;
            set
            {
                if (_roundText != value)
                {
                    _roundText = value;
                    OnPropertyChangedWithValue(value, "RoundText");
                }
            }
        }

        [DataSourceProperty]
        public string AttackerName
        {
            get => _attackerName;
            set
            {
                if (_attackerName != value)
                {
                    _attackerName = value;
                    OnPropertyChangedWithValue(value, "AttackerName");
                }
            }
        }

        [DataSourceProperty]
        public int AttackerInfantry
        {
            get => _attackerInfantry;
            set
            {
                if (_attackerInfantry != value)
                {
                    _attackerInfantry = value;
                    OnPropertyChangedWithValue(value, "AttackerInfantry");
                    OnPropertyChanged("AttackerInfantryText");
                }
            }
        }

        [DataSourceProperty]
        public int AttackerRanged
        {
            get => _attackerRanged;
            set
            {
                if (_attackerRanged != value)
                {
                    _attackerRanged = value;
                    OnPropertyChangedWithValue(value, "AttackerRanged");
                    OnPropertyChanged("AttackerRangedText");
                }
            }
        }

        [DataSourceProperty]
        public int AttackerCavalry
        {
            get => _attackerCavalry;
            set
            {
                if (_attackerCavalry != value)
                {
                    _attackerCavalry = value;
                    OnPropertyChangedWithValue(value, "AttackerCavalry");
                    OnPropertyChanged("AttackerCavalryText");
                }
            }
        }

        [DataSourceProperty]
        public int AttackerTotal
        {
            get => _attackerTotal;
            set
            {
                if (_attackerTotal != value)
                {
                    _attackerTotal = value;
                    OnPropertyChangedWithValue(value, "AttackerTotal");
                    OnPropertyChanged("AttackerTotalText");
                }
            }
        }

        [DataSourceProperty]
        public int AttackerStart
        {
            get => _attackerStart;
            set
            {
                if (_attackerStart != value)
                {
                    _attackerStart = value;
                    OnPropertyChangedWithValue(value, "AttackerStart");
                    OnPropertyChanged("AttackerTotalText");
                }
            }
        }

        [DataSourceProperty]
        public string DefenderName
        {
            get => _defenderName;
            set
            {
                if (_defenderName != value)
                {
                    _defenderName = value;
                    OnPropertyChangedWithValue(value, "DefenderName");
                }
            }
        }

        [DataSourceProperty]
        public int DefenderInfantry
        {
            get => _defenderInfantry;
            set
            {
                if (_defenderInfantry != value)
                {
                    _defenderInfantry = value;
                    OnPropertyChangedWithValue(value, "DefenderInfantry");
                    OnPropertyChanged("DefenderInfantryText");
                }
            }
        }

        [DataSourceProperty]
        public int DefenderRanged
        {
            get => _defenderRanged;
            set
            {
                if (_defenderRanged != value)
                {
                    _defenderRanged = value;
                    OnPropertyChangedWithValue(value, "DefenderRanged");
                    OnPropertyChanged("DefenderRangedText");
                }
            }
        }

        [DataSourceProperty]
        public int DefenderCavalry
        {
            get => _defenderCavalry;
            set
            {
                if (_defenderCavalry != value)
                {
                    _defenderCavalry = value;
                    OnPropertyChangedWithValue(value, "DefenderCavalry");
                    OnPropertyChanged("DefenderCavalryText");
                }
            }
        }

        [DataSourceProperty]
        public int DefenderTotal
        {
            get => _defenderTotal;
            set
            {
                if (_defenderTotal != value)
                {
                    _defenderTotal = value;
                    OnPropertyChangedWithValue(value, "DefenderTotal");
                    OnPropertyChanged("DefenderTotalText");
                }
            }
        }

        [DataSourceProperty]
        public int DefenderStart
        {
            get => _defenderStart;
            set
            {
                if (_defenderStart != value)
                {
                    _defenderStart = value;
                    OnPropertyChangedWithValue(value, "DefenderStart");
                    OnPropertyChanged("DefenderTotalText");
                }
            }
        }

        [DataSourceProperty]
        public bool IsSiege
        {
            get => _isSiege;
            set
            {
                if (_isSiege != value)
                {
                    _isSiege = value;
                    OnPropertyChangedWithValue(value, "IsSiege");
                }
            }
        }

        [DataSourceProperty]
        public string SiegeInfo
        {
            get => _siegeInfo;
            set
            {
                if (_siegeInfo != value)
                {
                    _siegeInfo = value;
                    OnPropertyChangedWithValue(value, "SiegeInfo");
                }
            }
        }

        [DataSourceProperty]
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible != value)
                {
                    _isVisible = value;
                    OnPropertyChangedWithValue(value, "IsVisible");
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<SimulationEventItemVM> Events
        {
            get => _events;
            set
            {
                if (_events != value)
                {
                    _events = value;
                    OnPropertyChangedWithValue(value, "Events");
                }
            }
        }
    }
}
