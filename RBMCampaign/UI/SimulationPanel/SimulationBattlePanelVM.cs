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
    // The chronicle's sentence templates live in SimulationBattlePanelVM.Chronicle.cs.
    internal partial class SimulationBattlePanelVM : ViewModel
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

            _phaseName = new TextObject(PhaseDeploying).ToString();
            _phaseDescription = new TextObject(DeployDescription).ToString();
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
                RoundText = new TextObject(RoundLine).SetTextVariable("NUM", state.Round).ToString();

                if (state.Round > 1 && state.Round % RoundDividerInterval == 0)
                {
                    AddEvent(new TextObject(RoundDivider)
                        .SetTextVariable("NUM", state.Round).ToString(), "divider");
                }

                ScanArtillery(state);
            }

            ScanTrace(state);
            CheckRout(state);
            CheckMilestones();
        }

        // ── Phase tracking with flavor ──────────────────────────────────

        // Returns the raw {=id}English template so the caller can resolve it; the flavor
        // counter advances exactly as it did when these were plain English strings.
        private string PickFlavor(string[] pool)
        {
            return pool[_flavorCounter++ % pool.Length];
        }

        private void UpdatePhase(SimulationBattleState.BattleState state)
        {
            string phaseKey;
            string phaseTemplate;
            string[] flavorPool;

            if (state.SiegeAssaultBattle)
            {
                if (SimulationSiege.IsApproach(state))
                {
                    phaseKey = "siege_approach";
                    phaseTemplate = PhaseApproach;
                    flavorPool = SiegeApproachFlavor;
                }
                else
                {
                    phaseKey = "siege_assault";
                    phaseTemplate = PhaseAssault;
                    flavorPool = SiegeAssaultFlavor;
                    UpdateSiegeInfo(state);
                }
            }
            else if (SimulationBattleState.IsVolleyPhase(state))
            {
                phaseKey = "volley";
                phaseTemplate = PhaseVolley;
                flavorPool = VolleyFlavor;
            }
            else if (SimulationBattleState.IsSkirmishPhase(state))
            {
                phaseKey = "skirmish";
                phaseTemplate = PhaseSkirmish;
                flavorPool = SkirmishFlavor;
            }
            else
            {
                phaseKey = "melee";
                phaseTemplate = PhaseMelee;
                flavorPool = MeleeFlavor;
            }

            if (state.AttackerRouted > 0 || state.DefenderRouted > 0)
            {
                phaseKey = "rout";
                phaseTemplate = PhaseRout;
                flavorPool = RoutFlavor;
            }

            string phaseName = new TextObject(phaseTemplate).ToString();

            if (phaseKey != _lastPhaseKey && state.Round > 0)
            {
                _lastPhaseKey = phaseKey;

                string flavor = new TextObject(PickFlavor(flavorPool)).ToString();
                AddEvent(new TextObject(PhaseBannerLine)
                    .SetTextVariable("PHASE", phaseName)
                    .SetTextVariable("FLAVOR", flavor).ToString(), "phase");
            }

            PhaseName = phaseName;
            PhaseDescription = new TextObject(PickFlavorStable(flavorPool, state.Round / 3)).ToString();
        }

        private static string PickFlavorStable(string[] pool, int seed)
        {
            return pool[Math.Abs(seed) % pool.Length];
        }

        private void UpdateSiegeInfo(SimulationBattleState.BattleState state)
        {
            if (state.AttackWidth > 0 || state.DefendWidth > 0)
            {
                SiegeInfo = new TextObject(SiegeInfoLine)
                    .SetTextVariable("ATK", state.AttackWidth)
                    .SetTextVariable("DEF", state.DefendWidth)
                    .SetTextVariable("PCT", (int)(state.SiegeWallFactor * 100)).ToString();
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

        private void EmitRoundHardestHit()
        {
            if (_phaseHardestStriker == null || _phaseHardestStruck == null || _phaseHardestDamage < 10f)
            {
                return;
            }

            string template = KillTemplateForWeapon(_phaseHardestWeapon, _phaseHardestAttack,
                _flavorCounter++);

            AddEvent(new TextObject(template)
                .SetTextVariable("ATTACKER",
                    SidedName(TroopName(_phaseHardestStriker), _phaseHardestStrikerIsAttacker))
                .SetTextVariable("VICTIM",
                    SidedName(TroopName(_phaseHardestStruck), !_phaseHardestStrikerIsAttacker))
                .SetTextVariable("DMG", DmgText(_phaseHardestDamage)).ToString(), "hardhit");
        }

        // ── Trace scanning (heroes + hard hits) ────────────────────────

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

        private void EmitPlayerHitEvent(HitRecord hit)
        {
            bool playerIsStriker = hit.Striker != null && hit.Striker.IsPlayerCharacter;
            bool playerIsStruck = hit.Struck != null && hit.Struck.IsPlayerCharacter;

            string rawPlayerName = playerIsStriker
                ? (hit.Striker.Name != null ? hit.Striker.Name.ToString() : new TextObject(NameYou).ToString())
                : (hit.Struck.Name != null ? hit.Struck.Name.ToString() : new TextObject(NameYou).ToString());
            string playerName = SidedName(rawPlayerName,
                playerIsStriker ? hit.StrikerIsAttacker : !hit.StrikerIsAttacker);
            CharacterObject playerChar = playerIsStriker ? hit.Striker : hit.Struck;

            string otherName = SidedName(
                playerIsStriker ? TroopName(hit.Struck) : TroopName(hit.Striker),
                playerIsStriker ? !hit.StrikerIsAttacker : hit.StrikerIsAttacker);

            if (hit.Evaded || hit.Closing)
            {
                string[] pool = playerIsStruck ? PlayerMissFlavor : PlayerMissedShotFlavor;
                AddHeroSentence(new TextObject(PickFlavor(pool)).SetTextVariable("FOE", otherName),
                    playerName, "hero", playerChar);
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
                    string template;
                    switch (hit.Defense)
                    {
                        case "riposte":      template = PlayerAttackRiposted; break;
                        case "parry":        template = PlayerAttackParried; break;
                        case "shield-block": template = PlayerAttackShieldBlocked; break;
                        case "weapon-block": template = PlayerAttackWeaponBlocked; break;
                        default:             template = PlayerAttackDefended; break;
                    }
                    AddHeroSentence(new TextObject(template)
                        .SetTextVariable("FOE", otherName)
                        .SetTextVariable("DMG", DmgText(hit.FinalDamage)),
                        playerName, "hero", playerChar);
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
                    AddHeroSentence(new TextObject(PickFlavor(PlayerHitFlavor))
                        .SetTextVariable("FOE", otherName)
                        .SetTextVariable("DMG", DmgText(hit.FinalDamage)),
                        playerName, "hero", playerChar);
                }
            }
            else
            {
                AddHeroSentence(new TextObject(PickFlavor(PlayerTakeHitFlavor))
                    .SetTextVariable("FOE", otherName)
                    .SetTextVariable("DMG", DmgText(hit.FinalDamage)),
                    playerName, "hero", playerChar);
            }
        }

        private void EmitHeroCasualty(HitRecord hit)
        {
            string victimName = SidedName(
                hit.Struck.Name != null ? hit.Struck.Name.ToString() : new TextObject(NameLordCap).ToString(),
                !hit.StrikerIsAttacker);
            string killerName = SidedName(
                hit.Striker != null ? TroopName(hit.Striker) : new TextObject(NameUnknownAssailant).ToString(),
                hit.StrikerIsAttacker);

            string template;
            if (hit.Braced)
            {
                template = PickFlavor(HeroFell_Brace);
            }
            else if (hit.ChargeBonus > 5f)
            {
                template = PickFlavor(HeroFell_Charge);
            }
            else
            {
                template = HeroFellTemplateForWeapon(hit.Weapon, hit.Phase, _flavorCounter++);
            }

            AddHeroSentence(new TextObject(template)
                .SetTextVariable("FOE", killerName)
                .SetTextVariable("DMG", DmgText(hit.FinalDamage)),
                victimName, "hero", hit.Struck);
        }

        private void EmitHeroDefense(HitRecord hit)
        {
            string heroName = SidedName(
                hit.Struck.Name != null ? hit.Struck.Name.ToString() : new TextObject(NameLordCap).ToString(),
                !hit.StrikerIsAttacker);
            string attackerName = SidedName(
                hit.Striker != null ? TroopName(hit.Striker) : new TextObject(NameAnAttacker).ToString(),
                hit.StrikerIsAttacker);

            string[] pool;
            if (hit.Defense == "riposte")
            {
                pool = RiposteFlavor;
            }
            else if (hit.Defense == "parry" || hit.Defense == "weapon-block" || hit.Defense == "shield-block")
            {
                pool = ParryFlavor;
            }
            else
            {
                return;
            }

            AddHeroSentence(new TextObject(PickFlavor(pool))
                .SetTextVariable("FOE", attackerName)
                .SetTextVariable("DMG", DmgText(hit.FinalDamage)),
                heroName, "hero", hit.Struck);
        }

        private void EmitHeroKill(HitRecord hit)
        {
            string heroName = SidedName(
                hit.Striker.Name != null ? hit.Striker.Name.ToString() : new TextObject(NameLordCap).ToString(),
                hit.StrikerIsAttacker);
            string victimName = SidedName(TroopName(hit.Struck), !hit.StrikerIsAttacker);

            bool ranged = hit.Phase == "shoot" || hit.Phase == "throw";
            string[] pool = ranged ? HeroKillFlavor_Ranged : HeroKillFlavor_Melee;

            AddHeroSentence(new TextObject(PickFlavor(pool))
                .SetTextVariable("VICTIM", victimName)
                .SetTextVariable("DMG", DmgText(hit.FinalDamage)),
                heroName, "hero", hit.Striker);
        }

        private void EmitHeroHeadshot(HitRecord hit)
        {
            string heroName = SidedName(
                hit.Struck.Name != null ? hit.Struck.Name.ToString() : new TextObject(NameLordCap).ToString(),
                !hit.StrikerIsAttacker);

            AddHeroSentence(new TextObject(PickFlavor(HeadshotFlavor))
                .SetTextVariable("DMG", DmgText(hit.FinalDamage)),
                heroName, "hero", hit.Struck);
        }

        private void EmitHeroSniped(HitRecord hit)
        {
            string heroName = SidedName(
                hit.Striker.Name != null ? hit.Striker.Name.ToString() : new TextObject(NameLordCap).ToString(),
                hit.StrikerIsAttacker);
            string victimName = SidedName(TroopName(hit.Struck), !hit.StrikerIsAttacker);

            AddHeroSentence(new TextObject(PickFlavor(HeroSnipeFlavor))
                .SetTextVariable("VICTIM", victimName)
                .SetTextVariable("DMG", DmgText(hit.FinalDamage)),
                heroName, "hero", hit.Striker);
        }

        // ── Artillery ───────────────────────────────────────────────────

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
                string flavor = new TextObject(PickFlavor(ArtilleryFlavor)).ToString();
                TextObject line = new TextObject(wounded > 0 ? ArtilleryTallyWounded : ArtilleryTally)
                    .SetTextVariable("FLAVOR", flavor)
                    .SetTextVariable("KILLED", killed);
                if (wounded > 0)
                {
                    line.SetTextVariable("WOUNDED", wounded);
                }
                AddEvent(line.ToString(), "artillery");
            }

            if (destroyed > 0)
            {
                AddEvent(new TextObject(PickFlavor(ArtilleryDestroyFlavor)).ToString(), "artillery");
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
                EmitRoutLine(true, state.AttackerRouted);
            }
            else if (state.DefenderRouted > 0)
            {
                _hadRout = true;
                EmitRoutLine(false, state.DefenderRouted);
            }
        }

        private void EmitRoutLine(bool attackers, int fled)
        {
            AddEvent(new TextObject(PickFlavor(RoutLineFlavor))
                .SetTextVariable("SIDE", SideNoun(attackers))
                .SetTextVariable("COUNT", fled).ToString(), "rout");
        }

        private void CheckMilestones()
        {
            if (_attackerStartCount > 0)
            {
                if (!_attackerHalfReported && _attackerTotal <= _attackerStartCount / 2)
                {
                    _attackerHalfReported = true;
                    EmitMilestone(HalfStrengthFlavor, true);
                }
                else if (!_attackerQuarterReported && _attackerTotal <= _attackerStartCount / 4)
                {
                    _attackerQuarterReported = true;
                    EmitMilestone(QuarterStrengthFlavor, true);
                }
            }
            if (_defenderStartCount > 0)
            {
                if (!_defenderHalfReported && _defenderTotal <= _defenderStartCount / 2)
                {
                    _defenderHalfReported = true;
                    EmitMilestone(HalfStrengthFlavor, false);
                }
                else if (!_defenderQuarterReported && _defenderTotal <= _defenderStartCount / 4)
                {
                    _defenderQuarterReported = true;
                    EmitMilestone(QuarterStrengthFlavor, false);
                }
            }
        }

        private void EmitMilestone(string[] pool, bool attackers)
        {
            AddEvent(new TextObject(PickFlavor(pool))
                .SetTextVariable("SIDE", SideNoun(attackers)).ToString(), "milestone");
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

        // Resolves a hero sentence and splits it around the hero's name, which the chronicle
        // draws in its own coloured widget: whatever the translation puts before the name
        // becomes the prefix, whatever follows it becomes the rest.
        private void AddHeroSentence(TextObject sentence, string heroName, string eventType,
            CharacterObject heroCharacter = null)
        {
            sentence.SetTextVariable("HERO", HeroMarker);
            string full = sentence.ToString();

            int at = full.IndexOf(HeroMarker, StringComparison.Ordinal);
            string prefix = at >= 0 ? full.Substring(0, at) : string.Empty;
            string rest = at >= 0 ? full.Substring(at + HeroMarker.Length) : full;

            bool isPlayer = heroCharacter != null && heroCharacter.IsPlayerCharacter;
            _events.Add(new SimulationEventItemVM(prefix + heroName + rest, eventType,
                heroName, rest, isPlayer, prefix));
        }

        private static string TroopName(CharacterObject troop)
        {
            if (troop == null)
            {
                return new TextObject(NameUnknownSoldier).ToString();
            }
            if (troop.Name != null)
            {
                return troop.Name.ToString();
            }
            return new TextObject(troop.IsHero ? NameALord : NameASoldier).ToString();
        }

        private static string GetSideName(MapEventSide side)
        {
            if (side == null || side.LeaderParty == null)
            {
                return new TextObject(NameUnknownSide).ToString();
            }
            PartyBase leader = side.LeaderParty;
            if (leader.MapFaction != null && leader.MapFaction.Name != null)
            {
                return leader.MapFaction.Name.ToString();
            }
            return leader.Name != null
                ? leader.Name.ToString()
                : new TextObject(NameUnknownSide).ToString();
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

        // The panel's two fixed captions. Gauntlet cannot resolve a {=id} marker inside a prefab's
        // Text=, so the prefab binds these instead.
        [DataSourceProperty]
        public string PanelTitle =>
            new TextObject("{=RBM_SIMPANEL_TITLE}RBM Campaign — Detailed Auto-Resolve").ToString();

        [DataSourceProperty]
        public string ChronicleTitle => new TextObject("{=RBM_SIMPANEL_CHRONICLE}Battle Chronicle").ToString();

        [DataSourceProperty]
        public string AttackerInfantryText =>
            new TextObject(StatInfantry).SetTextVariable("VALUE", _attackerInfantry).ToString();

        [DataSourceProperty]
        public string AttackerRangedText =>
            new TextObject(StatRanged).SetTextVariable("VALUE", _attackerRanged).ToString();

        [DataSourceProperty]
        public string AttackerCavalryText =>
            new TextObject(StatCavalry).SetTextVariable("VALUE", _attackerCavalry).ToString();

        [DataSourceProperty]
        public string AttackerTotalText =>
            new TextObject(StatTotal)
                .SetTextVariable("VALUE", _attackerTotal)
                .SetTextVariable("START", _attackerStart).ToString();

        [DataSourceProperty]
        public string DefenderInfantryText =>
            new TextObject(StatInfantry).SetTextVariable("VALUE", _defenderInfantry).ToString();

        [DataSourceProperty]
        public string DefenderRangedText =>
            new TextObject(StatRanged).SetTextVariable("VALUE", _defenderRanged).ToString();

        [DataSourceProperty]
        public string DefenderCavalryText =>
            new TextObject(StatCavalry).SetTextVariable("VALUE", _defenderCavalry).ToString();

        [DataSourceProperty]
        public string DefenderTotalText =>
            new TextObject(StatTotal)
                .SetTextVariable("VALUE", _defenderTotal)
                .SetTextVariable("START", _defenderStart).ToString();

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
