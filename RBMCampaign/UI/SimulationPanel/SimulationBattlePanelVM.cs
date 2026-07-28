using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

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

            _phaseName = "DEPLOYING";
            _phaseDescription = "Forces marshal on the field";
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
                RoundText = "Round " + state.Round;

                if (state.Round > 1 && state.Round % RoundDividerInterval == 0)
                {
                    AddEvent("──── Round " + state.Round + " ────", "divider");
                }

                ScanArtillery(state);
            }

            ScanTrace(state);
            CheckRout(state);
            CheckMilestones();
        }

        // ── Phase tracking with flavor ──────────────────────────────────

        private static readonly string[] VolleyFlavor = new[]
        {
            "Arrows darken the sky",
            "Bowstrings sing across the field",
            "The first shafts find their mark",
            "A storm of arrows descends on the enemy",
            "Volleys arc high and fall like rain",
            "The air hums with feathered death",
            "Shafts whistle overhead in thick waves",
            "Archers loose in unison — the sky goes dark",
            "A thousand bowstrings snap as one",
            "The arrow storm begins its grim harvest",
            "Quivers empty into the massed ranks ahead",
            "Flights of arrows blot out the sun",
        };

        private static readonly string[] SkirmishFlavor = new[]
        {
            "Javelins fly as the cavalry rides out",
            "Horsemen clash between the closing lines",
            "Riders spur forward, javelins in hand",
            "The skirmish opens as the gap narrows",
            "Light horse wheel and strike at the flanks",
            "Cavalry thunder across the open ground",
            "Lances dip as the horsemen charge into the fray",
            "Javelins arc through the dust between the lines",
            "Outriders trade blows at the edges of the fight",
            "The ground shakes as mounted warriors collide",
            "Skirmishers dart forward and hurl their darts",
            "Hooves pound and javelins flash in the sun",
        };

        private static readonly string[] MeleeFlavor = new[]
        {
            "Steel meets steel as the lines crash together",
            "The shieldwall buckles under the press",
            "Infantry close to sword's length at last",
            "The lines meet with a thunderous crash",
            "Men hack and shove in the press of bodies",
            "The melee is a heaving mass of iron and flesh",
            "Shields splinter under the weight of the charge",
            "Swords ring out and men fall screaming",
            "The battle becomes a brutal close-quarters brawl",
            "Blades flash and blood slicks the trampled earth",
            "The two sides grind against each other in the mud",
            "Warriors grapple in the dust, fighting for their lives",
            "The front line is a wall of shields, blood and iron",
            "Axes and swords bite through armour and bone",
        };

        private static readonly string[] SiegeApproachFlavor = new[]
        {
            "Men sprint across the killing ground",
            "The besiegers advance under a hail of arrows",
            "Bodies pile before the gates",
            "The open ground before the walls is a death trap",
            "Defenders rain fire on the approaching columns",
            "Arrows hammer down from the battlements",
            "The assault columns push through a storm of bolts",
            "Men fall by the dozen crossing the open ground",
            "Hot sand and stones cascade from the ramparts",
            "The advance is a slow crawl under murderous fire",
            "Siege towers creak forward under a hail of missiles",
        };

        private static readonly string[] SiegeAssaultFlavor = new[]
        {
            "Ladders strike the walls!",
            "The storm begins at the breaches",
            "Men pour through the openings",
            "Fighting is hand-to-hand at the parapets",
            "The besiegers claw their way onto the walls",
            "Defenders shove ladders back — but more come",
            "The gatehouse is a slaughterhouse",
            "Blood runs down the stone steps of the battlements",
            "Swords clash on the narrow walkways of the wall",
            "The breach is choked with the dead of both sides",
            "Attackers flood the parapet despite fearful losses",
        };

        private static readonly string[] RoutFlavor = new[]
        {
            "Their courage breaks!",
            "The line shatters and men flee!",
            "Panic spreads through the ranks!",
            "They throw down their arms and run!",
            "The rout is on — nothing can stop it!",
            "Their nerve fails — men turn and flee!",
            "The retreat becomes a stampede!",
            "Officers shout but no one listens — they run!",
            "The formation dissolves into a fleeing mob!",
            "Banners fall as men scatter in every direction!",
        };

        private static readonly string[] HalfStrengthFlavor = new[]
        {
            "have lost half their strength — the field is littered with their dead",
            "are at half strength and wavering",
            "have taken grievous losses — half their men are down",
            "bleed freely — barely half still stand",
            "have paid a terrible price — half their number lie fallen",
            "thin visibly — the gaps in their line grow wider",
            "stagger under the weight of their casualties",
        };

        private static readonly string[] QuarterStrengthFlavor = new[]
        {
            "are being destroyed — barely a quarter remain",
            "are on the verge of annihilation",
            "cling to the field with a handful of survivors",
            "have lost three quarters of their men",
            "fight on in desperate knots — most of their comrades are fallen",
            "are a broken remnant, still fighting but doomed",
            "barely hold together — the end is near for them",
        };

        private string PickFlavor(string[] pool)
        {
            return pool[_flavorCounter++ % pool.Length];
        }

        private void UpdatePhase(SimulationBattleState.BattleState state)
        {
            string phaseKey;
            string phaseName;
            string[] flavorPool;

            if (state.SiegeAssaultBattle)
            {
                if (SimulationSiege.IsApproach(state))
                {
                    phaseKey = "siege_approach";
                    phaseName = "APPROACH";
                    flavorPool = SiegeApproachFlavor;
                }
                else
                {
                    phaseKey = "siege_assault";
                    phaseName = "ASSAULT";
                    flavorPool = SiegeAssaultFlavor;
                    UpdateSiegeInfo(state);
                }
            }
            else if (SimulationBattleState.IsVolleyPhase(state))
            {
                phaseKey = "volley";
                phaseName = "VOLLEY";
                flavorPool = VolleyFlavor;
            }
            else if (SimulationBattleState.IsSkirmishPhase(state))
            {
                phaseKey = "skirmish";
                phaseName = "SKIRMISH";
                flavorPool = SkirmishFlavor;
            }
            else
            {
                phaseKey = "melee";
                phaseName = "MELEE";
                flavorPool = MeleeFlavor;
            }

            if (state.AttackerRouted > 0 || state.DefenderRouted > 0)
            {
                phaseKey = "rout";
                phaseName = "ROUT";
                flavorPool = RoutFlavor;
            }

            if (phaseKey != _lastPhaseKey && state.Round > 0)
            {
                _lastPhaseKey = phaseKey;

                string flavor = PickFlavor(flavorPool);
                AddEvent(phaseName + " — " + flavor, "phase");
            }

            PhaseName = phaseName;
            PhaseDescription = PickFlavorStable(flavorPool, state.Round / 3);
        }

        private static string PickFlavorStable(string[] pool, int seed)
        {
            return pool[Math.Abs(seed) % pool.Length];
        }

        private void UpdateSiegeInfo(SimulationBattleState.BattleState state)
        {
            if (state.AttackWidth > 0 || state.DefendWidth > 0)
            {
                SiegeInfo = "Frontage: " + state.AttackWidth + " vs " + state.DefendWidth
                    + " · Wall: " + ((int)(state.SiegeWallFactor * 100)) + "%";
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

        private static string VerbForWeapon(string weaponClass, string phase, int counter)
        {
            string[] verbs;
            switch (weaponClass)
            {
                case "OneHandedSword":
                case "TwoHandedSword":
                    verbs = new[] { "cut down", "slashed", "ran through", "slew" };
                    break;
                case "Dagger":
                    verbs = new[] { "stabbed", "knifed", "gutted" };
                    break;
                case "OneHandedAxe":
                case "TwoHandedAxe":
                    verbs = new[] { "cleaved", "hewed down", "hacked apart", "split open" };
                    break;
                case "Mace":
                case "TwoHandedMace":
                case "Pick":
                    verbs = new[] { "battered down", "hammered down", "crushed", "smashed" };
                    break;
                case "OneHandedPolearm":
                case "TwoHandedPolearm":
                case "LowGripPolearm":
                    verbs = new[] { "pierced", "impaled", "speared", "skewered" };
                    break;
                case "Arrow":
                case "Bow":
                    verbs = new[] { "shot down", "pierced with an arrow", "pinned with a shaft", "dropped with a shot" };
                    break;
                case "Bolt":
                case "Crossbow":
                    verbs = new[] { "shot down with a bolt", "pinned with a crossbow bolt", "dropped with a bolt" };
                    break;
                case "Javelin":
                    verbs = new[] { "speared with a javelin", "impaled with a thrown spear", "skewered at range" };
                    break;
                case "ThrowingAxe":
                    verbs = new[] { "hit with a thrown axe", "split open with a hurled axe", "struck down with a thrown axe" };
                    break;
                case "ThrowingKnife":
                    verbs = new[] { "hit with a thrown knife", "struck down with a thrown blade" };
                    break;
                case "Stone":
                case "SlingStone":
                case "Sling":
                    verbs = new[] { "brained with a sling stone", "felled with a stone", "struck down with a sling" };
                    break;
                default:
                    if (phase == "shoot")
                        verbs = new[] { "shot down", "pierced", "struck at range" };
                    else if (phase == "throw")
                        verbs = new[] { "struck at range", "hit with a thrown weapon" };
                    else
                        verbs = new[] { "struck down", "felled", "cut down" };
                    break;
            }
            return verbs[Math.Abs(counter) % verbs.Length];
        }

        private static string SideLabel(bool isAttacker)
        {
            return isAttacker ? "[ATK] " : "[DEF] ";
        }

        private void EmitRoundHardestHit()
        {
            if (_phaseHardestStriker == null || _phaseHardestStruck == null || _phaseHardestDamage < 10f)
            {
                return;
            }

            string verb = VerbForWeapon(_phaseHardestWeapon, _phaseHardestAttack, _flavorCounter++);
            string strikerName = TroopName(_phaseHardestStriker);
            string struckName = TroopName(_phaseHardestStruck);
            string strikerSide = SideLabel(_phaseHardestStrikerIsAttacker);
            string struckSide = SideLabel(!_phaseHardestStrikerIsAttacker);
            int dmg = (int)_phaseHardestDamage;

            AddEvent(strikerSide + strikerName + " " + verb + " " + struckSide + struckName
                + " (" + dmg + " dmg)", "hardhit");
        }

        // ── Trace scanning (heroes + hard hits) ────────────────────────

        private static readonly string[] HeroFellVerbs_Charge = new[]
        {
            "was ridden down by",
            "was trampled under the hooves of",
            "was unhorsed and crushed by",
            "was broken by the lance of",
            "was swept from the saddle by",
            "was smashed aside by the charge of",
        };

        private static readonly string[] HeroFellVerbs_Brace = new[]
        {
            "was impaled on the braced spear of",
            "charged into the waiting lance of",
            "rode onto the set pike of",
            "was skewered on the levelled polearm of",
        };

        private static string HeroVerbForWeapon(string weaponClass, string phase, int counter)
        {
            string[] verbs;
            switch (weaponClass)
            {
                case "OneHandedSword":
                case "TwoHandedSword":
                    verbs = new[] { "was cut down by", "was slain by the blade of", "fell to the sword of", "was run through by" };
                    break;
                case "Dagger":
                    verbs = new[] { "was stabbed down by", "was knifed by", "fell to the dagger of" };
                    break;
                case "OneHandedAxe":
                case "TwoHandedAxe":
                    verbs = new[] { "was cleaved apart by", "was hewn down by", "fell to the axe of", "was split open by" };
                    break;
                case "Mace":
                case "TwoHandedMace":
                case "Pick":
                    verbs = new[] { "was battered down by", "was crushed by", "had their skull caved in by", "was hammered to the ground by" };
                    break;
                case "OneHandedPolearm":
                case "TwoHandedPolearm":
                case "LowGripPolearm":
                    verbs = new[] { "was pierced by the spear of", "was impaled by", "was run through by the lance of", "fell to the polearm of" };
                    break;
                case "Arrow":
                case "Bow":
                    verbs = new[] { "was felled by an arrow from", "was shot down by", "took a fatal shaft from", "was pierced by an arrow from" };
                    break;
                case "Bolt":
                case "Crossbow":
                    verbs = new[] { "was dropped by a bolt from", "was pinned by a crossbow bolt from", "took a killing bolt from" };
                    break;
                case "Javelin":
                    verbs = new[] { "was speared by a javelin from", "was impaled at range by", "took a hurled spear from" };
                    break;
                case "ThrowingAxe":
                    verbs = new[] { "was struck by a thrown axe from", "was split open by a hurled axe from" };
                    break;
                case "ThrowingKnife":
                    verbs = new[] { "was struck down by a thrown knife from", "took a thrown blade from" };
                    break;
                case "Stone":
                case "SlingStone":
                case "Sling":
                    verbs = new[] { "was brained by a sling stone from", "was felled by a stone from" };
                    break;
                default:
                    if (phase == "shoot")
                        verbs = new[] { "was shot down by", "was struck at range by", "was felled by a missile from" };
                    else if (phase == "throw")
                        verbs = new[] { "was struck down at range by", "was felled by a thrown weapon from" };
                    else
                        verbs = new[] { "was struck down by", "was felled by", "fell in combat with" };
                    break;
            }
            return verbs[Math.Abs(counter) % verbs.Length];
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
            " strikes ", " lands a blow on ", " hits ", " connects with ",
        };

        private static readonly string[] PlayerTakeHitVerbs = new[]
        {
            " takes a hit from ", " is struck by ", " is hit by ", " absorbs a blow from ",
        };

        private static readonly string[] PlayerMissVerbs = new[]
        {
            " dodges a shot from ", " evades ", " sidesteps a blow from ",
        };

        private static readonly string[] PlayerMissedShotVerbs = new[]
        {
            " misses a shot at ", " sends a shaft wide of ", " looses at ",
        };

        private void EmitPlayerHitEvent(HitRecord hit)
        {
            bool playerIsStriker = hit.Striker != null && hit.Striker.IsPlayerCharacter;
            bool playerIsStruck = hit.Struck != null && hit.Struck.IsPlayerCharacter;

            string playerName = playerIsStriker
                ? (hit.Striker.Name != null ? hit.Striker.Name.ToString() : "You")
                : (hit.Struck.Name != null ? hit.Struck.Name.ToString() : "You");
            string playerSide = SideLabel(playerIsStriker ? hit.StrikerIsAttacker : !hit.StrikerIsAttacker);
            CharacterObject playerChar = playerIsStriker ? hit.Striker : hit.Struck;

            string otherName = playerIsStriker ? TroopName(hit.Struck) : TroopName(hit.Striker);
            string otherSide = SideLabel(playerIsStriker ? !hit.StrikerIsAttacker : hit.StrikerIsAttacker);

            if (hit.Evaded || hit.Closing)
            {
                if (playerIsStruck)
                {
                    AddHeroEvent(playerSide + playerName,
                        PickFlavor(PlayerMissVerbs) + otherSide + otherName, "hero", playerChar);
                }
                else
                {
                    AddHeroEvent(playerSide + playerName,
                        PickFlavor(PlayerMissedShotVerbs) + otherSide + otherName, "hero", playerChar);
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
                    string defenseDesc;
                    switch (hit.Defense)
                    {
                        case "riposte":     defenseDesc = "'s blow is parried and countered by "; break;
                        case "parry":       defenseDesc = "'s attack is parried by "; break;
                        case "shield-block": defenseDesc = "'s strike is blocked by "; break;
                        case "weapon-block": defenseDesc = "'s blow is deflected by "; break;
                        default:            defenseDesc = "'s attack is defended by "; break;
                    }
                    AddHeroEvent(playerSide + playerName,
                        defenseDesc + otherSide + otherName + DmgSuffix(hit.FinalDamage), "hero", playerChar);
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
                    AddHeroEvent(playerSide + playerName,
                        PickFlavor(PlayerHitVerbs) + otherSide + otherName + DmgSuffix(hit.FinalDamage),
                        "hero", playerChar);
                }
            }
            else
            {
                AddHeroEvent(playerSide + playerName,
                    PickFlavor(PlayerTakeHitVerbs) + otherSide + otherName + DmgSuffix(hit.FinalDamage),
                    "hero", playerChar);
            }
        }

        private void EmitHeroCasualty(HitRecord hit)
        {
            string victimName = hit.Struck.Name != null ? hit.Struck.Name.ToString() : "A lord";
            string killerName = hit.Striker != null
                ? TroopName(hit.Striker)
                : "an unknown assailant";
            string victimSide = SideLabel(!hit.StrikerIsAttacker);
            string killerSide = SideLabel(hit.StrikerIsAttacker);

            string verb;
            if (hit.Braced)
            {
                verb = PickFlavor(HeroFellVerbs_Brace);
            }
            else if (hit.ChargeBonus > 5f)
            {
                verb = PickFlavor(HeroFellVerbs_Charge);
            }
            else
            {
                verb = HeroVerbForWeapon(hit.Weapon, hit.Phase, _flavorCounter++);
            }

            AddHeroEvent(victimSide + victimName,
                " " + verb + " " + killerSide + killerName + DmgSuffix(hit.FinalDamage), "hero",
                hit.Struck);
        }

        private static string DmgSuffix(float damage)
        {
            int dmg = (int)damage;
            return dmg > 0 ? " (" + dmg + " dmg)" : "";
        }

        private static readonly string[] RiposteFlavor = new[]
        {
            " parries a blow from {0} and strikes back",
            " deflects {0}'s attack and counters",
            " turns aside {0}'s blade and ripostes",
            " catches {0}'s strike and drives their own home",
            " sidesteps {0} and delivers a vicious counterstrike",
        };

        private static readonly string[] ParryFlavor = new[]
        {
            " blocks a blow from {0}",
            " catches {0}'s strike on their shield",
            " turns aside a blow from {0}",
            " deflects {0}'s attack",
        };

        private void EmitHeroDefense(HitRecord hit)
        {
            string heroName = hit.Struck.Name != null ? hit.Struck.Name.ToString() : "A lord";
            string attackerName = hit.Striker != null ? TroopName(hit.Striker) : "an attacker";
            string heroSide = SideLabel(!hit.StrikerIsAttacker);
            string attackerSide = SideLabel(hit.StrikerIsAttacker);
            string attackerFull = attackerSide + attackerName;

            if (hit.Defense == "riposte")
            {
                string flavor = string.Format(PickFlavor(RiposteFlavor), attackerFull);
                AddHeroEvent(heroSide + heroName, flavor + DmgSuffix(hit.FinalDamage), "hero", hit.Struck);
            }
            else if (hit.Defense == "parry" || hit.Defense == "weapon-block" || hit.Defense == "shield-block")
            {
                string flavor = string.Format(PickFlavor(ParryFlavor), attackerFull);
                AddHeroEvent(heroSide + heroName, flavor + DmgSuffix(hit.FinalDamage), "hero", hit.Struck);
            }
        }

        private static readonly string[] HeroKillFlavor_Melee = new[]
        {
            " cuts down ",
            " strikes down ",
            " slays ",
            " fells ",
            " sends another to the grave — ",
        };

        private static readonly string[] HeroKillFlavor_Ranged = new[]
        {
            " picks off ",
            " drops ",
            " shoots down ",
            " finds their mark — ",
        };

        private void EmitHeroKill(HitRecord hit)
        {
            string heroName = hit.Striker.Name != null ? hit.Striker.Name.ToString() : "A lord";
            string victimName = TroopName(hit.Struck);
            string heroSide = SideLabel(hit.StrikerIsAttacker);
            string victimSide = SideLabel(!hit.StrikerIsAttacker);

            bool ranged = hit.Phase == "shoot" || hit.Phase == "throw";
            string[] pool = ranged ? HeroKillFlavor_Ranged : HeroKillFlavor_Melee;
            string verb = PickFlavor(pool);

            AddHeroEvent(heroSide + heroName, verb + victimSide + victimName
                + DmgSuffix(hit.FinalDamage), "hero", hit.Striker);
        }

        private static readonly string[] HeadshotFlavor = new[]
        {
            " takes an arrow to the head!",
            " is struck in the face by a missile!",
            " catches a bolt in the skull!",
            " is hit square in the head at range!",
            " takes a shot clean through the helm!",
        };

        private void EmitHeroHeadshot(HitRecord hit)
        {
            string heroName = hit.Struck.Name != null ? hit.Struck.Name.ToString() : "A lord";
            string heroSide = SideLabel(!hit.StrikerIsAttacker);
            int dmg = (int)hit.FinalDamage;
            AddHeroEvent(heroSide + heroName, PickFlavor(HeadshotFlavor) + " (" + dmg + " dmg)", "hero",
                hit.Struck);
        }

        private static readonly string[] HeroSnipeFlavor = new[]
        {
            " lands a perfect headshot on ",
            " puts an arrow clean through the helm of ",
            " nails a shot to the head of ",
            " finds the gap in the visor of ",
            " sends a bolt straight through the skull of ",
        };

        private void EmitHeroSniped(HitRecord hit)
        {
            string heroName = hit.Striker.Name != null ? hit.Striker.Name.ToString() : "A lord";
            string victimName = TroopName(hit.Struck);
            string heroSide = SideLabel(hit.StrikerIsAttacker);
            string victimSide = SideLabel(!hit.StrikerIsAttacker);
            int dmg = (int)hit.FinalDamage;
            AddHeroEvent(heroSide + heroName,
                PickFlavor(HeroSnipeFlavor) + victimSide + victimName + " (" + dmg + " dmg)", "hero",
                hit.Striker);
        }

        // ── Artillery ───────────────────────────────────────────────────

        private static readonly string[] ArtilleryFlavor = new[]
        {
            "Stones crash into the defenders",
            "A boulder smashes through the ranks",
            "The engines hurl death at the walls",
            "Siege stones tear men apart",
            "A catapult stone carves a path through the crowd",
            "Trebuchet fire hammers the fortifications",
            "The ground shakes as heavy stones find their targets",
            "Engine crews heave and another stone arcs skyward",
            "A volley of stones rains down from the siege line",
        };

        private static readonly string[] ArtilleryDestroyFlavor = new[]
        {
            "An enemy engine is shattered to splinters!",
            "A direct hit reduces an engine to kindling!",
            "A well-aimed stone destroys an enemy machine!",
            "An engine erupts into flying timber and rope!",
            "The crew dives clear as their machine is wrecked!",
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
                string flavor = PickFlavor(ArtilleryFlavor);
                string msg = flavor + " — " + killed + " killed";
                if (wounded > 0)
                {
                    msg += ", " + wounded + " wounded";
                }
                AddEvent(msg, "artillery");
            }

            if (destroyed > 0)
            {
                AddEvent(PickFlavor(ArtilleryDestroyFlavor), "artillery");
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
                string flavor = PickFlavor(RoutFlavor);
                AddEvent("The attackers " + flavor.ToLower() + " (" + state.AttackerRouted + " fled)", "rout");
            }
            else if (state.DefenderRouted > 0)
            {
                _hadRout = true;
                string flavor = PickFlavor(RoutFlavor);
                AddEvent("The defenders " + flavor.ToLower() + " (" + state.DefenderRouted + " fled)", "rout");
            }
        }

        private void CheckMilestones()
        {
            if (_attackerStartCount > 0)
            {
                if (!_attackerHalfReported && _attackerTotal <= _attackerStartCount / 2)
                {
                    _attackerHalfReported = true;
                    AddEvent("The attackers " + PickFlavor(HalfStrengthFlavor), "milestone");
                }
                else if (!_attackerQuarterReported && _attackerTotal <= _attackerStartCount / 4)
                {
                    _attackerQuarterReported = true;
                    AddEvent("The attackers " + PickFlavor(QuarterStrengthFlavor), "milestone");
                }
            }
            if (_defenderStartCount > 0)
            {
                if (!_defenderHalfReported && _defenderTotal <= _defenderStartCount / 2)
                {
                    _defenderHalfReported = true;
                    AddEvent("The defenders " + PickFlavor(HalfStrengthFlavor), "milestone");
                }
                else if (!_defenderQuarterReported && _defenderTotal <= _defenderStartCount / 4)
                {
                    _defenderQuarterReported = true;
                    AddEvent("The defenders " + PickFlavor(QuarterStrengthFlavor), "milestone");
                }
            }
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
                return "an unknown soldier";
            }
            if (troop.Name != null)
            {
                return troop.Name.ToString();
            }
            return troop.IsHero ? "a lord" : "a soldier";
        }

        private static string GetSideName(MapEventSide side)
        {
            if (side == null || side.LeaderParty == null)
            {
                return "Unknown";
            }
            PartyBase leader = side.LeaderParty;
            if (leader.MapFaction != null && leader.MapFaction.Name != null)
            {
                return leader.MapFaction.Name.ToString();
            }
            return leader.Name != null ? leader.Name.ToString() : "Unknown";
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

        [DataSourceProperty]
        public string AttackerInfantryText => "Inf: " + _attackerInfantry;

        [DataSourceProperty]
        public string AttackerRangedText => "Ran: " + _attackerRanged;

        [DataSourceProperty]
        public string AttackerCavalryText => "Cav: " + _attackerCavalry;

        [DataSourceProperty]
        public string AttackerTotalText => "Total: " + _attackerTotal + " / " + _attackerStart;

        [DataSourceProperty]
        public string DefenderInfantryText => "Inf: " + _defenderInfantry;

        [DataSourceProperty]
        public string DefenderRangedText => "Ran: " + _defenderRanged;

        [DataSourceProperty]
        public string DefenderCavalryText => "Cav: " + _defenderCavalry;

        [DataSourceProperty]
        public string DefenderTotalText => "Total: " + _defenderTotal + " / " + _defenderStart;

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
