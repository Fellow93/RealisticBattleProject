using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// Formation shouts: soldiers of every formation of every team give voice to the orders they get and to carrying
    /// them out. Sound only -- nothing here changes what anybody does, except the separate order-reaction test below.
    ///
    /// 1. Order given. A player order (OrderController, via Team.OnOrderIssued) already makes the commander say the
    ///    line in vanilla; here several soldiers repeat it, the formation answers with a crowd grunt, and a later wave
    ///    repeats it again as it is passed back through the ranks. The AI's
    ///    tactics never go through an OrderController, so its captains are silent in vanilla: a change of a formation's
    ///    movement GROUP (AI behaviors re-issue Move every few frames and RBM turns Charge into ChargeToTarget, so the
    ///    raw enum is useless) or of its arrangement makes the captain -- or a man of the front rank -- call the order,
    ///    and the same echoes and answer follow. A change the player caused in the last PlayerOrderShadow seconds is
    ///    left to the player path, so nothing is shouted twice.
    /// 2. Order carried out. A halt that has settled and a shield wall/square/circle that has closed up with its front
    ///    rank's shields raised get the crowd grunt; a charge gets its men's yells and its culture's war cry and, on the
    ///    first clash, the men's roar and the crowd's battle answer. An infantry formation moving in step keeps up its
    ///    leader's culture's marching cadence. Single men only repeat order lines and yell in a charge (user,
    ///    2026-10-09: no single grunts, no other yells); everything else is a crowd recording: RBM's crowd grunt
    ///    (module sound rbm/march/*), the cultural war cries (alerts/rally) and the crowd acknowledgements (alerts/nods).
    /// 3. Every voice goes through one main-thread queue: per-agent cooldown, a global cap per second, and a lateness
    ///    limit past which a voice deferred by the cap is dropped. There is deliberately no distance limit to the
    ///    player; cooldowns and caps keep it cheap and keep it from turning into noise.
    ///
    /// Staggered order reaction (orderReactionDelayEnabled, experimental): vanilla's AfterSetOrder gives every man of a
    /// formation the engine's default random decide time and only then fires OnOrderIssued, so a call made from the
    /// handler here overrides it per man with a delay that grows with his distance from the commander.
    ///
    /// Threading: everything runs on the main thread. Formation movement/arrangement changes are POLLED in
    /// OnMissionTick (two managed field reads per formation) instead of subscribing to Formation.OnBeforeMovementOrderApplied
    /// / OnAfterArrangementOrderApplied, because those fire on the engine's async AI thread; OnMissionTick runs while no
    /// agent or formation AI tick does. Team.OnOrderIssued and OnMeleeHit are main thread. All state is instance fields
    /// keyed by Agent/Formation and cleared at mission end, so nothing outlives the mission.
    /// </summary>
    public class FormationShoutsLogic : MissionLogic
    {
        // ---- tuning: order echoes ----

        /// <summary>Random part of each echo delay, before the distance part.</summary>
        private const float EchoDelayMin = 0.5f;
        private const float EchoDelayMax = 1.5f;

        /// <summary>Seconds added per metre between the man and whoever gave the order.</summary>
        private const float EchoDelayPerMeter = 0.025f;

        private const float EchoDelayCap = 4f;

        /// <summary>A player order echoes once per formation per this many seconds (one click can fire Move, LookAt and FormCustom).</summary>
        private const float PlayerEchoCooldown = 1f;

        /// <summary>A movement/arrangement change this soon after a player order is the player's, not the AI's.</summary>
        private const float PlayerOrderShadow = 0.5f;

        /// <summary>An AI formation calls a new order at most this often.</summary>
        private const float AiOrderShoutCooldown = 4f;

        /// <summary>Men repeating the order line at once: min, max, and one more per this many men.</summary>
        private const int EchoesMin = 2;
        private const int EchoesMax = 6;
        private const int EchoPerMen = 20;

        /// <summary>A later wave repeats the order again, as it is passed back through the ranks.</summary>
        private const int LateEchoesMin = 1;
        private const int LateEchoesMax = 4;
        private const float LateEchoDelayMin = 1.5f;
        private const float LateEchoDelayMax = 3.5f;

        // ---- tuning: voice queue ----

        /// <summary>One man says nothing again for this long after he was picked to speak.</summary>
        private const float AgentVoiceCooldown = 2f;

        /// <summary>Voices started per rolling second, all formations together.</summary>
        private const int MaxVoicesPerSecond = 60;

        /// <summary>A voice held back by the cap is dropped once it is this late; a reply ten seconds on is not a reply.</summary>
        private const float VoiceLateness = 1.5f;

        /// <summary>A march caller's order line held back by the cap is dropped once it is this late: it would be out of step.</summary>
        private const float CallLateness = 0.3f;

        // ---- tuning: order carried out ----

        private const float PollInterval = 0.3f;

        /// <summary>A halt is looked for this long after a Stop or Move order.</summary>
        private const float HaltWatchSeconds = 20f;

        /// <summary>CachedCurrentVelocity below this counts as standing.</summary>
        private const float HaltSpeed = 0.35f;

        /// <summary>DeviationOfPositionsExcludeFarAgents below this counts as in place.</summary>
        private const float HaltDeviation = 1.5f;

        /// <summary>Consecutive standing polls before the halt grunt.</summary>
        private const int HaltPolls = 2;

        private const float HaltGruntCooldown = 10f;

        /// <summary>A closed shield wall / square / circle is looked for this long after the arrangement change.</summary>
        private const float FormedWatchSeconds = 25f;

        private const float FormedDeviation = 1.5f;

        /// <summary>Share of the front rank's shield-bearers that must be holding the shield up.</summary>
        private const float FormedShieldShare = 0.5f;

        /// <summary>Most front-rank shield-bearers checked per poll.</summary>
        private const int FormedShieldSample = 16;

        /// <summary>The charge yells and the war cry start this long after the charge order.</summary>
        private const float ChargeYellDelayMin = 0.4f;
        private const float ChargeYellDelayMax = 1.2f;

        /// <summary>
        /// Share of the formation that yells as the charge starts, over this many seconds, at most once per cooldown.
        /// Charges are the one time single men yell (user, 2026-10-09).
        /// </summary>
        private const float ChargeYellShare = 0.6f;
        private const float ChargeYellSpread = 1.5f;
        private const float ChargeYellCooldown = 15f;

        /// <summary>Share of the formation that roars when its charge first clashes, over this many seconds.</summary>
        private const float ContactRoarShare = 0.5f;
        private const float ContactRoarSpread = 2f;

        /// <summary>Most men one charge yell / contact roar picks, whatever the formation size.</summary>
        private const int MaxBurst = 40;

        // ---- tuning: marching beat ----

        /// <summary>CachedCurrentVelocity above this counts as marching.</summary>
        private const float MarchSpeed = 0.7f;

        /// <summary>DeviationOfPositionsExcludeFarAgents above this is a crowd, not a formation in step.</summary>
        private const float MarchDeviation = 5f;

        /// <summary>The first cadence starts this long after the formation starts moving.</summary>
        private const float MarchFirstBeatMin = 0.5f;
        private const float MarchFirstBeatMax = 1.2f;

        /// <summary>A marching formation that falls out of step (speed or deviation) for less than this keeps its rhythm.</summary>
        private const float MarchGrace = 1.5f;

        /// <summary>Another infantry formation takes over as the team's main one only when it is this much bigger.</summary>
        private const float MainInfantrySwitchRatio = 1.2f;

        // ---- tuning: reforming call ----

        /// <summary>
        /// Slower than MarchSpeed but with DeviationOfPositionsExcludeFarAgents above this, the formation is not going
        /// anywhere but its men are still finding their places: the caller shouts the formation (line, shield wall...).
        /// </summary>
        private const float ReformDeviation = 2f;

        /// <summary>Seconds between the caller's reforming shouts, random per shout; the first comes after the first delay.</summary>
        private const float ReformCallIntervalMin = 3.5f;
        private const float ReformCallIntervalMax = 6f;
        private const float ReformFirstCallMin = 0.8f;
        private const float ReformFirstCallMax = 1.5f;

        // ---- tuning: crowd sounds ----
        // Single voices cannot sound like hundreds of men: there are only so many recordings and the sound engine
        // limits instances. The game's crowd recordings can: the cultural war cries (event:/alerts/rally/*, used by
        // vanilla only in multiplayer warmup) and the crowd acknowledgements (event:/alerts/nods/*, vanilla plays them
        // only for the player's orders). They are layered on top of the single voices.

        /// <summary>A formation this small gets no war cry and no crowd acknowledgement: its own voices are enough.</summary>
        private const int WarCryMinUnits = 20;
        private const int CrowdMinUnits = 10;

        /// <summary>A war cry lasts 7-15 s; one formation gives one at most this often.</summary>
        private const float WarCryCooldown = 40f;

        /// <summary>One team starts at most one war cry / one crowd acknowledgement per this many seconds.</summary>
        private const float TeamWarCryGap = 4f;
        private const float TeamCrowdGap = 1.5f;

        /// <summary>The crowd answers an AI order this long after the captain called it.</summary>
        private const float CrowdAnswerDelayMin = 0.5f;
        private const float CrowdAnswerDelayMax = 0.9f;

        /// <summary>
        /// Tempo of every cadence: beat offsets and cycle lengths in Cadences are divided by this. 1 = as written;
        /// 1.1 = 10% faster (user, 2026-10-09; 1.2 was tried first).
        /// </summary>
        private const float MarchTempo = 1.1f;

        /// <summary>
        /// Every cadence's Spread (how far apart one beat's crowd grunts may land) is multiplied by this. 1 = as written;
        /// 0.5 = twice as tight (user, 2026-10-09: "a bit tighter").
        /// </summary>
        private const float MarchSpreadScale = 0.5f;

        /// <summary>Each cycle's length is stretched or shrunk by up to this fraction, so formations drift out of lockstep with each other.</summary>
        private const float MarchCycleJitter = 0.05f;

        /// <summary>How often a formation's main culture (which picks its cadence) is looked up again.</summary>
        private const float CultureCheckInterval = 15f;

        /// <summary>A formation counts as in melee (no marching call) for this long after one of its men hit or was hit in melee.</summary>
        private const float MeleeContactWindow = 4f;

        // ---- tuning: staggered order reaction ----

        private const float ReactionDelayMin = 0.3f;
        private const float ReactionDelayMax = 2f;
        private const float ReactionDelayPerMeter = 0.04f;

        /// <summary>Each man's distance part is scaled by a random factor in this range, so neighbours do not move as one.</summary>
        private const float ReactionDistanceJitterMin = 0.5f;
        private const float ReactionDistanceJitterMax = 1.5f;

        /// <summary>This share of men are slow on the uptake and add a further random lag.</summary>
        private const float ReactionStragglerChance = 0.25f;
        private const float ReactionStragglerMin = 0.5f;
        private const float ReactionStragglerMax = 2.5f;

        private const float ReactionDelayCap = 6f;

        // ------------------------------------------------------------------

        /// <summary>Movement orders grouped the way a soldier would hear them. None = not a shoutable order.</summary>
        internal enum MoveGroup
        {
            None,
            Charge,
            Stop,
            Advance,
            FallBack,
            Retreat,
            Move,
            Follow
        }

        private sealed class FormationState
        {
            public MoveGroup Group;
            public ArrangementOrder.ArrangementOrderEnum Arrangement;
            public float LastPlayerOrderTime = -1000f;
            public float NextPlayerEchoTime;
            public float NextAiShoutTime;
            public float HaltWatchUntil = -1f;
            public int HaltStablePolls;
            public float NextHaltGruntTime;
            public float FormedWatchUntil = -1f;
            public float NextChargeYellTime;
            public bool ContactPending;
            public float NextMarchBeat = -1f;
            public float LastInStepTime = -1000f;
            public float NextReformCall = -1f;
            public float LastContactTime = -1000f;
            public MarchCadence Cadence;
            public float NextCultureCheck;
            public float NextWarCryTime;
            public int MarchCrowdEvent = -1;
            public float FemaleShare;
            public Agent BannerBearer;
            public float NextBannerCheck;
        }

        /// <summary>A crowd sound waiting to be played: the formation's crowd, heard from one of its men.</summary>
        private struct PendingCrowd
        {
            public float Time;
            public Formation Formation;
            public int EventId;
            /// <summary>Set for an RBM march sample: played at this man, with no GenderRatio parameter. Null = a vanilla crowd event at the median man.</summary>
            public Agent Source;
        }

        /// <summary>
        /// RBM's own massed march grunt (module sound, RBMXML/module_sounds.xml + ModuleSounds/): one recording of a
        /// whole formation, so it can sound like hundreds of men where single voices cannot (they sounded like a few
        /// men grunting). See MarchCrowdEventFor.
        /// </summary>
        private const string MarchCrowdPrefix = "rbm/march/";

        /// <summary>Instances of the march sample per beat: one, plus one per this many men, at most MarchCrowdMax, each at a different man of the line.</summary>
        private const int MarchCrowdPerMen = 150;
        private const int MarchCrowdMax = 3;

        private static readonly string[] MarchCrowdCultures = { "empire", "vlandia", "sturgia", "battania", "aserai", "khuzait", "nord" };

        /// <summary>Resolved march sample by culture; _marchCrowdGeneric for any other. Filled in AfterStart.</summary>
        private readonly Dictionary<string, int> _marchCrowdIds = new Dictionary<string, int>();
        private int _marchCrowdGeneric = -1;

        private readonly List<PendingCrowd> _pendingCrowd = new List<PendingCrowd>();
        private readonly Dictionary<Team, float> _teamNextWarCry = new Dictionary<Team, float>();

        /// <summary>Each team's current main infantry formation (MainInfantryOf).</summary>
        private readonly Dictionary<Team, Formation> _mainInfantry = new Dictionary<Team, Formation>();
        private readonly Dictionary<Team, float> _teamNextCrowd = new Dictionary<Team, float>();

        /// <summary>War cry event id by culture StringId; _rallyGeneric for any other culture. Filled in AfterStart.</summary>
        private readonly Dictionary<string, int> _rallyIds = new Dictionary<string, int>();
        private int _rallyGeneric = -1;

        private int _nodMove = -1;
        private int _nodAttack = -1;
        private int _nodStop = -1;
        private int _nodFormation = -1;

        private static readonly string[] RallyCultures = { "empire", "vlandia", "sturgia", "battania", "khuzait", "aserai" };

        /// <summary>One short grunt of a cadence: when in the cycle, and by the whole chorus or one caller.</summary>
        private struct Beat
        {
            public float Offset;
            public bool Caller;

            public Beat(float offset, bool caller = false)
            {
                Offset = offset;
                Caller = caller;
            }
        }

        /// <summary>
        /// A culture's marching call: its beats, the cycle length, and how tightly the chorus keeps together. A chorus
        /// beat is the massed crowd grunt (MarchCrowdEventFor) at one to MarchCrowdMax men across the line; a caller
        /// beat is the captain (else banner bearer, else a soldier) calling the formation's order, which the chorus answers.
        /// </summary>
        private sealed class MarchCadence
        {
            public Beat[] Beats;
            public float Cycle;
            public float Spread;
        }

        private static readonly MarchCadence DefaultCadence = new MarchCadence
        {
            // Untrained men: one ragged beat.
            Beats = new[] { new Beat(0f) },
            Cycle = 3f,
            Spread = 0.3f
        };

        // ---- tuning: how far the crowd grunt carries ----
        // Single soldiers never grunt any more (user, 2026-10-09: the crowd recording sounds far better); every grunt
        // is the crowd sample, played as a sound at one of the formation's men.

        /// <summary>
        /// A pulled crowd grunt is never placed further than this from the listener. The module sound's own falloff is
        /// steeper than vanilla's voices (80 m): 40-75 m sounded too distant, then 20-40 m made the far ones too loud
        /// (user, 2026-10-09).
        /// </summary>
        private const float AudibleRange = 55f;

        /// <summary>Within this distance a crowd grunt plays where the man is.</summary>
        private const float PullStart = 20f;

        /// <summary>Crowd grunts further than this from the listener are not played at all.</summary>
        private const float MaxHearDistance = 250f;

        /// <summary>A voice comes out of the head, not the feet.</summary>
        private const float MouthHeight = 1.6f;

        /// <summary>
        /// Cadence by the formation's leader's culture (StringId). Every chorus beat is the same crowd grunt (pitched
        /// per culture in module_sounds.xml), so the cultures differ by rhythm, by a leader calling the order and the
        /// chorus answering, and by how tight the chorus is.
        /// </summary>
        private static readonly Dictionary<string, MarchCadence> Cadences = new Dictionary<string, MarchCadence>
        {
            // Drilled legion: the centurion calls, four even steps answer, then a pause.
            ["empire"] = new MarchCadence
            {
                Beats = new[] { new Beat(0f, caller: true), new Beat(1f), new Beat(2f), new Beat(3f), new Beat(4f) },
                Cycle = 6f,
                Spread = 0.06f
            },
            // Two firm steps.
            ["vlandia"] = new MarchCadence
            {
                Beats = new[] { new Beat(0f), new Beat(1f) },
                Cycle = 3.5f,
                Spread = 0.1f
            },
            // Slow and heavy.
            ["sturgia"] = new MarchCadence
            {
                Beats = new[] { new Beat(0f), new Beat(1.6f), new Beat(3.2f) },
                Cycle = 5.5f,
                Spread = 0.12f
            },
            // Loose: one man leads, the rest follow raggedly.
            ["battania"] = new MarchCadence
            {
                Beats = new[] { new Beat(0f, caller: true), new Beat(0.9f), new Beat(1.6f) },
                Cycle = 4f,
                Spread = 0.25f
            },
            // Call and response, twice.
            ["aserai"] = new MarchCadence
            {
                Beats = new[] { new Beat(0f, caller: true), new Beat(1f), new Beat(2.5f, caller: true), new Beat(3.5f) },
                Cycle = 5.5f,
                Spread = 0.1f
            },
            // Quick triplet.
            ["khuzait"] = new MarchCadence
            {
                Beats = new[] { new Beat(0f), new Beat(0.7f), new Beat(1.4f) },
                Cycle = 4f,
                Spread = 0.08f
            },
            // One plain HUH every 1.5 s (1.65 / MarchTempo 1.1).
            ["nord"] = new MarchCadence
            {
                Beats = new[] { new Beat(0f) },
                Cycle = 1.65f,
                Spread = 0.1f
            }
        };

        /// <summary>Scratch counts for the culture lookup.</summary>
        private readonly Dictionary<string, int> _cultureCounts = new Dictionary<string, int>();

        private struct PendingVoice
        {
            public float Time;
            public float Deadline;
            public Agent Agent;
            public SkinVoiceManager.SkinVoiceType Voice;
        }

        private readonly Dictionary<Formation, FormationState> _states = new Dictionary<Formation, FormationState>();

        private readonly List<PendingVoice> _pending = new List<PendingVoice>();

        /// <summary>When each man may be picked to speak again. Set when he is picked, so one burst never picks him twice.</summary>
        private readonly Dictionary<Agent, float> _agentNextVoice = new Dictionary<Agent, float>();

        /// <summary>
        /// Start times of the last MaxVoicesPerSecond voices, a ring; _recentHead is the oldest. The cap is reached
        /// while the oldest of them is less than a second old.
        /// </summary>
        private readonly float[] _recentVoices = NewRecentVoices();

        private int _recentHead;

        private readonly List<Team> _subscribedTeams = new List<Team>();

        private readonly List<Agent> _picked = new List<Agent>();

        /// <summary>StaggerReaction's men by decide-time step (key = delay / DecideTimeStep); lists reused between orders.</summary>
        private readonly Dictionary<int, List<int>> _decideBuckets = new Dictionary<int, List<int>>();

        /// <summary>Decide times are rounded to this, so a formation needs one engine call per step, not per man.</summary>
        private const float DecideTimeStep = 0.1f;

        private readonly HashSet<string> _reportedFailures = new HashSet<string>();

        private float _nextPoll;

        public override void AfterStart()
        {
            SubscribeTeams();
            try
            {
                ResolveSoundEvents();
            }
            catch (Exception e)
            {
                // No grunt events: every grunt falls back to the grunt voice line.
                ReportOnce("soundEvents", e);
            }
        }

        public override void OnMissionTick(float dt)
        {
            Mission mission = Mission;
            if (mission == null || GameNetwork.IsMultiplayer)
            {
                return;
            }

            if (mission.Teams.Count != _subscribedTeams.Count)
            {
                SubscribeTeams();
            }

            // Present only for the order-reaction test: the subscription above is all it needs.
            if (!RBMConfig.RBMConfig.formationShoutsEnabled)
            {
                _pending.Clear();
                return;
            }

            float now = mission.CurrentTime;
            // Deployment, conversations, cutscenes: keep track of the orders silently, so the first real tick of the
            // battle does not shout every order handed out while setting up.
            bool live = mission.Mode == MissionMode.Battle;
            if (!live)
            {
                _pending.Clear();
                _pendingCrowd.Clear();
            }

            bool poll = now >= _nextPoll;
            if (poll)
            {
                _nextPoll = now + PollInterval;
            }

            foreach (Team team in mission.Teams)
            {
                if (team == null || team.FormationsIncludingSpecialAndEmpty == null)
                {
                    continue;
                }
                Formation mainInfantry = poll ? MainInfantryOf(team) : null;
                foreach (Formation formation in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (formation == null || formation.CountOfUnits <= 0)
                    {
                        continue;
                    }
                    try
                    {
                        TickFormation(formation, now, live, poll, formation == mainInfantry);
                    }
                    catch (Exception e)
                    {
                        ReportOnce("formation", e);
                    }
                }
            }

            if (live)
            {
                try
                {
                    SpeakDueVoices(now);
                }
                catch (Exception e)
                {
                    ReportOnce("speak", e);
                }
                try
                {
                    PlayDueCrowds(now);
                }
                catch (Exception e)
                {
                    ReportOnce("crowd", e);
                }
            }
        }

        public override void OnMeleeHit(Agent attacker, Agent victim, bool isCanceled, AttackCollisionData collisionData)
        {
            if (isCanceled || attacker == null || victim == null || !RBMConfig.RBMConfig.formationShoutsEnabled)
            {
                return;
            }
            Mission mission = Mission;
            if (mission == null || mission.Mode != MissionMode.Battle || !attacker.IsHuman || !victim.IsHuman || !attacker.IsEnemyOf(victim))
            {
                return;
            }
            try
            {
                float now = mission.CurrentTime;
                TryContactRoar(attacker.Formation, now);
                TryContactRoar(victim.Formation, now);
            }
            catch (Exception e)
            {
                ReportOnce("meleeHit", e);
            }
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow blow)
        {
            if (affectedAgent != null)
            {
                _agentNextVoice.Remove(affectedAgent);
            }
        }

        public override void OnRemoveBehavior()
        {
            Clear();
            base.OnRemoveBehavior();
        }

        protected override void OnEndMission()
        {
            Clear();
        }

        private void Clear()
        {
            UnsubscribeTeams();
            _states.Clear();
            _pending.Clear();
            _pendingCrowd.Clear();
            _teamNextWarCry.Clear();
            _teamNextCrowd.Clear();
            _mainInfantry.Clear();
            _agentNextVoice.Clear();
            for (int i = 0; i < _recentVoices.Length; i++)
            {
                _recentVoices[i] = -1000f;
            }
            _picked.Clear();
        }

        private static float[] NewRecentVoices()
        {
            float[] times = new float[MaxVoicesPerSecond];
            for (int i = 0; i < times.Length; i++)
            {
                times[i] = -1000f;
            }
            return times;
        }

        // ---------------- team subscription ----------------

        private void SubscribeTeams()
        {
            Mission mission = Mission;
            if (mission == null)
            {
                return;
            }
            foreach (Team team in mission.Teams)
            {
                if (team != null && !_subscribedTeams.Contains(team))
                {
                    team.OnOrderIssued += OnOrderIssued;
                    _subscribedTeams.Add(team);
                }
            }
        }

        private void UnsubscribeTeams()
        {
            foreach (Team team in _subscribedTeams)
            {
                if (team != null)
                {
                    team.OnOrderIssued -= OnOrderIssued;
                }
            }
            _subscribedTeams.Clear();
        }

        // ---------------- player orders ----------------

        /// <summary>
        /// Main thread, fired by OrderController right after AfterSetOrder (which has just given the formations the
        /// engine's default decide time and made the commander say the order line).
        /// </summary>
        private void OnOrderIssued(OrderType orderType, MBReadOnlyList<Formation> appliedFormations, OrderController orderController, params object[] delegateParams)
        {
            Mission mission = Mission;
            if (appliedFormations == null || mission == null || mission.Mode != MissionMode.Battle || GameNetwork.IsMultiplayer)
            {
                return;
            }

            try
            {
                float now = mission.CurrentTime;
                Agent commander = (orderController != null) ? orderController.Owner : null;
                bool hasSource = commander != null && commander.IsActive();
                Vec2 source = hasSource ? commander.Position.AsVec2 : Vec2.Zero;
                SkinVoiceManager.SkinVoiceType? line = OrderLine(orderType, mission);
                bool shouts = RBMConfig.RBMConfig.formationShoutsEnabled;
                bool delay = RBMConfig.RBMConfig.orderReactionDelayEnabled && IsDelayedOrder(orderType);

                foreach (Formation formation in appliedFormations)
                {
                    if (formation == null || formation.CountOfUnits <= 0)
                    {
                        continue;
                    }

                    if (delay)
                    {
                        StaggerReaction(mission, formation, hasSource, source);
                    }

                    FormationState state = GetState(formation);
                    state.LastPlayerOrderTime = now;
                    if (IsHaltOrder(orderType))
                    {
                        // A repeated Move/Stop leaves the movement group as it was, so the poll would not arm this.
                        state.HaltWatchUntil = now + HaltWatchSeconds;
                        state.HaltStablePolls = 0;
                    }

                    if (!shouts || line == null || now < state.NextPlayerEchoTime)
                    {
                        continue;
                    }
                    state.NextPlayerEchoTime = now + PlayerEchoCooldown;
                    Vec2 from = hasSource ? source : formation.CachedAveragePosition;
                    ScheduleReplies(formation, line.Value, from, now);
                }
            }
            catch (Exception e)
            {
                ReportOnce("orderIssued", e);
            }
        }

        /// <summary>
        /// SetRandomDecideTimeOfAgentsWithIndices with min = max gives each man exactly his decide time, overriding the
        /// default vanilla's AfterSetOrder handed out a moment earlier. Men are bucketed by delay in DecideTimeStep
        /// steps, one engine call per bucket instead of one per man.
        /// </summary>
        private void StaggerReaction(Mission mission, Formation formation, bool hasSource, Vec2 source)
        {
            MBReadOnlyList<IFormationUnit> units = formation.Arrangement?.GetAllUnits();
            if (units == null)
            {
                return;
            }
            foreach (List<int> bucket in _decideBuckets.Values)
            {
                bucket.Clear();
            }
            for (int i = 0; i < units.Count; i++)
            {
                Agent agent = units[i] as Agent;
                if (agent == null || !agent.IsActive() || agent.IsPlayerControlled)
                {
                    continue;
                }
                int step = (int)Math.Round(ReactionDelay(agent, hasSource, source) / DecideTimeStep);
                List<int> bucket;
                if (!_decideBuckets.TryGetValue(step, out bucket))
                {
                    bucket = new List<int>();
                    _decideBuckets[step] = bucket;
                }
                bucket.Add(agent.Index);
            }
            foreach (KeyValuePair<int, List<int>> bucket in _decideBuckets)
            {
                if (bucket.Value.Count == 0)
                {
                    continue;
                }
                float d = bucket.Key * DecideTimeStep;
                mission.SetRandomDecideTimeOfAgentsWithIndices(bucket.Value.ToArray(), d, d);
            }
        }

        /// <summary>
        /// One man's delay before he acts on a player order: a random part, his distance from the commander scaled by
        /// his own random factor, and for some men a straggler's lag on top, capped. Shared with OrderReactionHold,
        /// which holds his old formation frame for this long. Main thread only (MBRandom).
        /// </summary>
        internal static float ReactionDelay(Agent agent, bool hasSource, Vec2 source)
        {
            return ReactionDelayFrom(MBRandom.RandomFloat, MBRandom.RandomFloat, MBRandom.RandomFloat, MBRandom.RandomFloat,
                hasSource ? agent.Position.AsVec2.Distance(source) : -1f);
        }

        /// <summary>
        /// The same delay off another thread (OrderReactionHold's AI-order stamp runs on the engine's AI thread, where
        /// MBRandom must not be used): the caller's own System.Random.
        /// </summary>
        internal static float ReactionDelay(Agent agent, bool hasSource, Vec2 source, Random random)
        {
            return ReactionDelayFrom((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(),
                hasSource ? agent.Position.AsVec2.Distance(source) : -1f);
        }

        /// <summary>The delay from four uniform [0,1) draws and the distance from the order's source (negative: none).</summary>
        private static float ReactionDelayFrom(float rBase, float rDistance, float rStraggler, float rStragglerLag, float distance)
        {
            float d = ReactionDelayMin + rBase * (ReactionDelayMax - ReactionDelayMin);
            if (distance >= 0f)
            {
                d += distance * ReactionDelayPerMeter
                    * (ReactionDistanceJitterMin + rDistance * (ReactionDistanceJitterMax - ReactionDistanceJitterMin));
            }
            if (rStraggler < ReactionStragglerChance)
            {
                d += ReactionStragglerMin + rStragglerLag * (ReactionStragglerMax - ReactionStragglerMin);
            }
            return Math.Min(d, ReactionDelayCap);
        }

        /// <summary>Orders a formation physically reacts to. Fire/mount/cohesion/AI-control/transfer orders are left alone.</summary>
        private static bool IsDelayedOrder(OrderType orderType)
        {
            switch (orderType)
            {
                case OrderType.Move:
                case OrderType.MoveToLineSegment:
                case OrderType.MoveToLineSegmentWithHorizontalLayout:
                case OrderType.Charge:
                case OrderType.ChargeWithTarget:
                case OrderType.StandYourGround:
                case OrderType.FollowMe:
                case OrderType.FollowEntity:
                case OrderType.Retreat:
                case OrderType.AdvanceTenPaces:
                case OrderType.FallBackTenPaces:
                case OrderType.Advance:
                case OrderType.FallBack:
                case OrderType.LookAtEnemy:
                case OrderType.LookAtDirection:
                case OrderType.ArrangementLine:
                case OrderType.ArrangementCloseOrder:
                case OrderType.ArrangementLoose:
                case OrderType.ArrangementCircular:
                case OrderType.ArrangementSchiltron:
                case OrderType.ArrangementVee:
                case OrderType.ArrangementColumn:
                case OrderType.ArrangementScatter:
                case OrderType.FormDeep:
                case OrderType.FormWide:
                case OrderType.FormWider:
                case OrderType.AttackEntity:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsHaltOrder(OrderType orderType)
        {
            return orderType == OrderType.Move || orderType == OrderType.MoveToLineSegment
                || orderType == OrderType.MoveToLineSegmentWithHorizontalLayout || orderType == OrderType.StandYourGround;
        }

        /// <summary>The line the commander says for this order: the same mapping as vanilla OrderController.PlayOrderGestures.</summary>
        private static SkinVoiceManager.SkinVoiceType? OrderLine(OrderType orderType, Mission mission)
        {
            switch (orderType)
            {
                case OrderType.FireAtWill: return SkinVoiceManager.VoiceType.FireAtWill;
                case OrderType.HoldFire: return SkinVoiceManager.VoiceType.HoldFire;
                case OrderType.Mount: return SkinVoiceManager.VoiceType.Mount;
                case OrderType.Dismount: return SkinVoiceManager.VoiceType.Dismount;
                case OrderType.Move:
                case OrderType.MoveToLineSegment:
                case OrderType.MoveToLineSegmentWithHorizontalLayout: return SkinVoiceManager.VoiceType.Move;
                case OrderType.Charge:
                case OrderType.ChargeWithTarget: return SkinVoiceManager.VoiceType.Charge;
                case OrderType.FollowMe: return SkinVoiceManager.VoiceType.Follow;
                case OrderType.Retreat: return SkinVoiceManager.VoiceType.Retreat;
                case OrderType.AdvanceTenPaces:
                case OrderType.Advance: return SkinVoiceManager.VoiceType.Advance;
                case OrderType.FallBackTenPaces:
                case OrderType.FallBack: return SkinVoiceManager.VoiceType.FallBack;
                case OrderType.StandYourGround: return SkinVoiceManager.VoiceType.Stop;
                case OrderType.ArrangementLine: return SkinVoiceManager.VoiceType.FormLine;
                case OrderType.ArrangementCloseOrder: return SkinVoiceManager.VoiceType.FormShieldWall;
                case OrderType.ArrangementLoose: return SkinVoiceManager.VoiceType.FormLoose;
                case OrderType.ArrangementCircular: return SkinVoiceManager.VoiceType.FormCircle;
                case OrderType.ArrangementSchiltron: return SkinVoiceManager.VoiceType.FormSquare;
                case OrderType.ArrangementVee: return SkinVoiceManager.VoiceType.FormSkein;
                case OrderType.ArrangementColumn: return SkinVoiceManager.VoiceType.FormColumn;
                case OrderType.ArrangementScatter: return SkinVoiceManager.VoiceType.FormScatter;
                case OrderType.LookAtEnemy:
                    return mission.IsNavalBattle ? SkinVoiceManager.VoiceType.BoardAtWill : SkinVoiceManager.VoiceType.FaceEnemy;
                case OrderType.LookAtDirection:
                    return mission.IsNavalBattle ? SkinVoiceManager.VoiceType.AvoidBoarding : SkinVoiceManager.VoiceType.FaceDirection;
                default:
                    return null;
            }
        }

        // ---------------- per-formation tick ----------------

        /// <summary>
        /// The team's main infantry: its biggest infantry formation (ties go to the lower formation index, so the
        /// Infantry slot wins). Only it keeps up the marching call; other infantry formations march silently. Sticky:
        /// the current main one stays main until another is MainInfantrySwitchRatio bigger, so two near-equal
        /// formations do not trade the march back and forth as men fall.
        /// </summary>
        private Formation MainInfantryOf(Team team)
        {
            Formation biggest = null;
            foreach (Formation formation in team.FormationsIncludingSpecialAndEmpty)
            {
                if (formation == null || formation.CountOfUnits <= 0 || !formation.QuerySystem.IsInfantryFormationReadOnly)
                {
                    continue;
                }
                if (biggest == null || formation.CountOfUnits > biggest.CountOfUnits)
                {
                    biggest = formation;
                }
            }
            Formation current;
            if (biggest != null && _mainInfantry.TryGetValue(team, out current) && current != null && current != biggest
                && current.CountOfUnits > 0 && current.QuerySystem.IsInfantryFormationReadOnly
                && biggest.CountOfUnits < current.CountOfUnits * MainInfantrySwitchRatio)
            {
                return current;
            }
            _mainInfantry[team] = biggest;
            return biggest;
        }

        private void TickFormation(Formation formation, float now, bool live, bool poll, bool isMainInfantry)
        {
            MoveGroup group = GroupOf(formation.GetReadonlyMovementOrderReference().OrderEnum);
            ArrangementOrder.ArrangementOrderEnum arrangement = formation.ArrangementOrder.OrderEnum;

            FormationState state;
            if (!_states.TryGetValue(formation, out state))
            {
                // First sighting: what the formation is doing now is not a change.
                state = new FormationState { Group = group, Arrangement = arrangement };
                _states[formation] = state;
                return;
            }

            bool groupChanged = group != state.Group;
            bool arrangementChanged = arrangement != state.Arrangement;
            state.Group = group;
            state.Arrangement = arrangement;

            if (!live)
            {
                state.HaltWatchUntil = -1f;
                state.FormedWatchUntil = -1f;
                state.ContactPending = false;
                state.NextMarchBeat = -1f;
                return;
            }

            if (groupChanged)
            {
                OnGroupChanged(formation, state, group, now);
            }
            if (arrangementChanged)
            {
                OnArrangementChanged(formation, state, arrangement, now);
            }

            if (poll)
            {
                PollHalt(formation, state, now);
                PollFormed(formation, state, now);
                PollMarch(formation, state, now, isMainInfantry);
                PollReform(formation, state, now, isMainInfantry);
            }
        }

        private void OnGroupChanged(Formation formation, FormationState state, MoveGroup group, float now)
        {
            if (group == MoveGroup.Stop || group == MoveGroup.Move)
            {
                state.HaltWatchUntil = now + HaltWatchSeconds;
                state.HaltStablePolls = 0;
            }
            else
            {
                state.HaltWatchUntil = -1f;
            }

            if (group == MoveGroup.Charge)
            {
                state.ContactPending = true;
                if (now >= state.NextChargeYellTime)
                {
                    state.NextChargeYellTime = now + ChargeYellCooldown;
                    float yellStart = now + MBRandom.RandomFloatRanged(ChargeYellDelayMin, ChargeYellDelayMax);
                    ScheduleBurst(formation, SkinVoiceManager.VoiceType.Yell, ChargeYellShare, yellStart, ChargeYellSpread, now);
                    // The whole formation's war cry under the single yells; player charges too (this is polled).
                    TryWarCry(formation, state, yellStart, now);
                }
            }
            else
            {
                state.ContactPending = false;
            }

            AiOrderShout(formation, state, GroupLine(group), NodFor(group), now);
        }

        private void OnArrangementChanged(Formation formation, FormationState state, ArrangementOrder.ArrangementOrderEnum arrangement, float now)
        {
            bool closed = arrangement == ArrangementOrder.ArrangementOrderEnum.ShieldWall
                || arrangement == ArrangementOrder.ArrangementOrderEnum.Square
                || arrangement == ArrangementOrder.ArrangementOrderEnum.Circle;
            state.FormedWatchUntil = closed ? now + FormedWatchSeconds : -1f;

            AiOrderShout(formation, state, ArrangementLine(arrangement), _nodFormation, now);
        }

        /// <summary>
        /// An order the AI gave: the captain (or a man of the front rank) calls it, the rest echo and answer, and the
        /// crowd acknowledges it as vanilla's does the player's orders. Skipped if the player gave this formation an
        /// order a moment ago -- that one is already being echoed (and vanilla already played its crowd answer).
        /// </summary>
        private void AiOrderShout(Formation formation, FormationState state, SkinVoiceManager.SkinVoiceType? line, int crowdEvent, float now)
        {
            if (line == null || now - state.LastPlayerOrderTime < PlayerOrderShadow || now < state.NextAiShoutTime)
            {
                return;
            }
            Agent speaker = PickSpeaker(formation, now);
            if (speaker == null)
            {
                return;
            }
            state.NextAiShoutTime = now + AiOrderShoutCooldown;
            float start = now + MBRandom.RandomFloatRanged(0.05f, 0.25f);
            Schedule(speaker, line.Value, start);
            ScheduleReplies(formation, line.Value, speaker.Position.AsVec2, start);
            ScheduleCrowd(formation, crowdEvent, start + MBRandom.RandomFloatRanged(CrowdAnswerDelayMin, CrowdAnswerDelayMax), now);
        }

        private void PollHalt(Formation formation, FormationState state, float now)
        {
            if (state.HaltWatchUntil < 0f)
            {
                return;
            }
            if (now > state.HaltWatchUntil || (state.Group != MoveGroup.Stop && state.Group != MoveGroup.Move))
            {
                state.HaltWatchUntil = -1f;
                return;
            }
            bool standing = formation.CachedCurrentVelocity.Length < HaltSpeed
                && formation.CachedFormationIntegrityData.DeviationOfPositionsExcludeFarAgents < HaltDeviation;
            state.HaltStablePolls = standing ? state.HaltStablePolls + 1 : 0;
            if (state.HaltStablePolls < HaltPolls)
            {
                return;
            }
            state.HaltWatchUntil = -1f;
            if (now < state.NextHaltGruntTime)
            {
                return;
            }
            state.NextHaltGruntTime = now + HaltGruntCooldown;
            ScheduleCrowd(formation, _nodStop, now + MBRandom.RandomFloatRanged(0.1f, 0.4f), now);
            ScheduleCrowdGrunts(formation, CrowdGruntFor(formation, state, now), MBRandom.RandomInt(1, 3), now + 0.1f, 0.4f, now);
        }

        private void PollFormed(Formation formation, FormationState state, float now)
        {
            if (state.FormedWatchUntil < 0f)
            {
                return;
            }
            if (now > state.FormedWatchUntil)
            {
                state.FormedWatchUntil = -1f;
                return;
            }
            if (formation.CachedCurrentVelocity.Length >= HaltSpeed
                || formation.CachedFormationIntegrityData.DeviationOfPositionsExcludeFarAgents >= FormedDeviation
                || !FrontRankShieldsUp(formation))
            {
                return;
            }
            state.FormedWatchUntil = -1f;
            ScheduleCrowd(formation, _nodFormation, now + MBRandom.RandomFloatRanged(0.1f, 0.4f), now);
            ScheduleCrowdGrunts(formation, CrowdGruntFor(formation, state, now), 2, now + 0.1f, 0.3f, now);
        }

        /// <summary>
        /// At least FormedShieldShare of the front rank's shield-bearers hold the shield up. Only shield-bearers count:
        /// RBM keeps archers' and two-handers' shields down in these arrangements. A front rank with no shields at all
        /// passes on position and speed alone.
        /// </summary>
        private static bool FrontRankShieldsUp(Formation formation)
        {
            MBReadOnlyList<IFormationUnit> units = formation.Arrangement?.GetAllUnits();
            if (units == null)
            {
                return false;
            }
            int bearers = 0;
            int raised = 0;
            // A sample of the front rank is enough, and keeps the engine queries (action type) few.
            for (int i = 0; i < units.Count && bearers < FormedShieldSample; i++)
            {
                Agent agent = units[i] as Agent;
                if (agent == null || !agent.IsActive() || ((IFormationUnit)agent).FormationRankIndex != 0)
                {
                    continue;
                }
                MissionWeapon offhand = agent.WieldedOffhandWeapon;
                if (offhand.IsEmpty || offhand.CurrentUsageItem == null || !offhand.CurrentUsageItem.IsShield)
                {
                    continue;
                }
                bearers++;
                if (agent.GetCurrentActionType(1) == Agent.ActionCodeType.DefendShield)
                {
                    raised++;
                }
            }
            return bearers == 0 || raised >= bearers * FormedShieldShare;
        }

        /// <summary>
        /// The team's main infantry formation (MainInfantryOf), moving in step (not charging, not in melee), keeps up its
        /// leader's culture's marching call: one cadence cycle after another. Other formations march silently.
        /// </summary>
        private void PollMarch(Formation formation, FormationState state, float now, bool isMainInfantry)
        {
            bool ordered = isMainInfantry
                && (state.Group == MoveGroup.Move || state.Group == MoveGroup.Advance
                    || state.Group == MoveGroup.FallBack || state.Group == MoveGroup.Follow)
                && now - state.LastContactTime > MeleeContactWindow;
            bool inStep = formation.CachedCurrentVelocity.Length > MarchSpeed
                && formation.CachedFormationIntegrityData.DeviationOfPositionsExcludeFarAgents < MarchDeviation;
            if (ordered && inStep)
            {
                state.LastInStepTime = now;
            }
            // A moment out of step (a turn, a squeeze, a slow-down) keeps the rhythm going: restarting it would wait a
            // first-beat delay and sound like a missed beat (user, 2026-10-09). A new order, melee or losing the main
            // infantry still stop it at once.
            bool marching = ordered && (inStep || (state.NextMarchBeat >= 0f && now - state.LastInStepTime < MarchGrace));
            if (!marching)
            {
                state.NextMarchBeat = -1f;
                return;
            }
            RefreshCulture(formation, state, now);
            if (state.NextMarchBeat < 0f)
            {
                state.NextMarchBeat = now + MBRandom.RandomFloatRanged(MarchFirstBeatMin, MarchFirstBeatMax);
                return;
            }
            if (now < state.NextMarchBeat)
            {
                return;
            }
            MarchCadence cadence = state.Cadence;
            // The poll is coarser than the cycle: start from the due time, not from now, so the rhythm does not drift.
            float start = Math.Max(state.NextMarchBeat, now - PollInterval);
            state.NextMarchBeat = start + cadence.Cycle / MarchTempo * (1f + MBRandom.RandomFloatRanged(-MarchCycleJitter, MarchCycleJitter));
            ScheduleCadence(formation, cadence, state.MarchCrowdEvent, GroupLine(state.Group) ?? SkinVoiceManager.VoiceType.Advance,
                ArrangementLine(state.Arrangement), start, now);
        }

        /// <summary>
        /// The main infantry is not going anywhere (slower than MarchSpeed) but its men are still finding their places
        /// (deviation above ReformDeviation): the caller shouts the formation -- "Form line!", "Shield wall!" -- every
        /// few seconds until it has closed up.
        /// </summary>
        private void PollReform(Formation formation, FormationState state, float now, bool isMainInfantry)
        {
            SkinVoiceManager.SkinVoiceType? formLine = ArrangementLine(state.Arrangement);
            bool reforming = isMainInfantry
                && formLine != null
                && state.Group != MoveGroup.Charge && state.Group != MoveGroup.Retreat
                && formation.CachedCurrentVelocity.Length <= MarchSpeed
                && formation.CachedFormationIntegrityData.DeviationOfPositionsExcludeFarAgents > ReformDeviation
                && now - state.LastContactTime > MeleeContactWindow;
            if (!reforming)
            {
                state.NextReformCall = -1f;
                return;
            }
            if (state.NextReformCall < 0f)
            {
                state.NextReformCall = now + MBRandom.RandomFloatRanged(ReformFirstCallMin, ReformFirstCallMax);
                return;
            }
            if (now < state.NextReformCall)
            {
                return;
            }
            state.NextReformCall = now + MBRandom.RandomFloatRanged(ReformCallIntervalMin, ReformCallIntervalMax);
            Agent caller = PickCaller(formation, now);
            if (caller != null)
            {
                Schedule(caller, formLine.Value, now);
            }
        }

        /// <summary>
        /// One cycle. A chorus beat is the formation's crowd grunt (crowdEvent) at one to MarchCrowdMax men across the
        /// line; with no sample the chorus beats are silent. A caller beat is one man calling, at random per call, the
        /// movement ordered (moveLine, e.g. "Advance!") or the formation held (formLine, e.g. "Shield wall!"), and the
        /// chorus answers.
        /// </summary>
        private void ScheduleCadence(Formation formation, MarchCadence cadence, int crowdEvent, SkinVoiceManager.SkinVoiceType moveLine,
            SkinVoiceManager.SkinVoiceType? formLine, float start, float now)
        {
            int crowdCount = Math.Min(MarchCrowdMax, 1 + formation.CountOfUnits / MarchCrowdPerMen);
            Agent caller = null;
            for (int b = 0; b < cadence.Beats.Length; b++)
            {
                Beat beat = cadence.Beats[b];
                float t = start + beat.Offset / MarchTempo;
                if (!beat.Caller)
                {
                    ScheduleCrowdGrunts(formation, crowdEvent, crowdCount, t, cadence.Spread * MarchSpreadScale, now, footOnly: true);
                    continue;
                }
                if (caller == null)
                {
                    caller = PickCaller(formation, now);
                }
                if (caller != null)
                {
                    SkinVoiceManager.SkinVoiceType callLine = (formLine != null && MBRandom.RandomFloat < 0.5f) ? formLine.Value : moveLine;
                    // A late call is out of step: dropped rather than played late.
                    Schedule(caller, callLine, t, CallLateness);
                }
            }
        }

        /// <summary>
        /// Who calls the step: the formation's captain, else its banner bearer, else one random soldier. Picked once per
        /// cycle; the voice queue's per-man cooldown is not checked, the call belongs to him.
        /// </summary>
        private Agent PickCaller(Formation formation, float now)
        {
            Agent captain = formation.Captain;
            if (captain != null && captain.IsActive() && captain.IsHuman && !captain.IsPlayerControlled)
            {
                return captain;
            }
            // The banner bearer is looked up once per CultureCheckInterval, not on every call.
            FormationState state = GetState(formation);
            if (now >= state.NextBannerCheck)
            {
                state.BannerBearer = FindBannerBearer(formation);
                state.NextBannerCheck = now + CultureCheckInterval;
            }
            Agent banner = state.BannerBearer;
            if (banner != null && banner.IsActive() && !banner.IsPlayerControlled && banner.Formation == formation)
            {
                return banner;
            }
            PickUnits(formation, 1, now, footOnly: true);
            return (_picked.Count > 0) ? _picked[0] : null;
        }

        private static Agent FindBannerBearer(Formation formation)
        {
            MBReadOnlyList<IFormationUnit> units = formation.Arrangement?.GetAllUnits();
            if (units == null)
            {
                return null;
            }
            for (int i = 0; i < units.Count; i++)
            {
                Agent agent = units[i] as Agent;
                if (agent != null && agent.IsActive() && !agent.IsPlayerControlled && Utilities.IsBannerBearer(agent))
                {
                    return agent;
                }
            }
            return null;
        }

        /// <summary>
        /// `count` instances of the crowd grunt, each from a different man of the formation (so a big formation's
        /// grunt has width), starting within `spread` seconds of `start`. Not subject to the voice cooldown or cap:
        /// each instance stands for many men.
        /// </summary>
        private void ScheduleCrowdGrunts(Formation formation, int crowdEvent, int count, float start, float spread, float now, bool footOnly = false)
        {
            if (crowdEvent < 0 || count <= 0)
            {
                return;
            }
            PickUnits(formation, count, now, footOnly);
            for (int i = 0; i < _picked.Count; i++)
            {
                _pendingCrowd.Add(new PendingCrowd
                {
                    Time = start + MBRandom.RandomFloat * spread,
                    Formation = formation,
                    EventId = crowdEvent,
                    Source = _picked[i]
                });
            }
        }

        /// <summary>The formation's crowd grunt, in its culture (MarchCultureOf).</summary>
        private int CrowdGruntFor(Formation formation, FormationState state, float now)
        {
            RefreshCulture(formation, state, now);
            return state.MarchCrowdEvent;
        }

        /// <summary>Looks up the formation's culture again (cadence and crowd grunt) when it has never been or every CultureCheckInterval.</summary>
        private void RefreshCulture(Formation formation, FormationState state, float now)
        {
            if (state.Cadence != null && now < state.NextCultureCheck)
            {
                return;
            }
            string culture = MarchCultureOf(formation);
            MarchCadence found;
            state.Cadence = (culture != null && Cadences.TryGetValue(culture, out found)) ? found : DefaultCadence;
            state.MarchCrowdEvent = MarchCrowdEventFor(culture);
            state.FemaleShare = FemaleShare(formation);
            state.NextCultureCheck = now + CultureCheckInterval;
        }

        /// <summary>
        /// The formation's culture, for its march and war cry: its captain's, else the culture most of its men belong
        /// to. Null if none is known.
        /// </summary>
        private string MarchCultureOf(Formation formation)
        {
            // The team's general is deliberately not used: in custom battle he is the chosen commander, whose culture
            // has nothing to do with the troops picked (and no commander is Nord).
            Agent leader = formation.Captain;
            if (leader != null && leader.IsActive())
            {
                string leaderCulture = leader.Character?.Culture?.StringId;
                if (leaderCulture != null)
                {
                    return leaderCulture;
                }
            }

            MBReadOnlyList<IFormationUnit> units = formation.Arrangement?.GetAllUnits();
            if (units == null)
            {
                return null;
            }
            _cultureCounts.Clear();
            string best = null;
            int bestCount = 0;
            for (int i = 0; i < units.Count; i++)
            {
                Agent agent = units[i] as Agent;
                string culture = agent?.Character?.Culture?.StringId;
                if (culture == null)
                {
                    continue;
                }
                int n;
                _cultureCounts.TryGetValue(culture, out n);
                _cultureCounts[culture] = ++n;
                if (n > bestCount)
                {
                    bestCount = n;
                    best = culture;
                }
            }
            return best;
        }

        /// <summary>
        /// A melee hit involving the formation: keeps its in-melee clock (no marching call meanwhile) and, on the first
        /// clash of a charge, the men's roar and the crowd's battle answer.
        /// </summary>
        private void TryContactRoar(Formation formation, float now)
        {
            if (formation == null)
            {
                return;
            }
            FormationState state;
            if (!_states.TryGetValue(formation, out state))
            {
                return;
            }
            state.LastContactTime = now;
            if (!state.ContactPending || state.Group != MoveGroup.Charge)
            {
                return;
            }
            state.ContactPending = false;
            ScheduleBurst(formation, SkinVoiceManager.VoiceType.Yell, ContactRoarShare, now, ContactRoarSpread, now);
            ScheduleCrowd(formation, _nodAttack, now + MBRandom.RandomFloatRanged(0.1f, 0.4f), now);
        }

        /// <summary>A share of the formation (capped at MaxBurst) gives the same voice, spread over `spread` seconds from `start`.</summary>
        private void ScheduleBurst(Formation formation, SkinVoiceManager.SkinVoiceType voice, float share, float start, float spread, float now)
        {
            int count = Math.Min(MaxBurst, Math.Max(1, (int)Math.Round(formation.CountOfUnits * share)));
            PickUnits(formation, count, now);
            for (int i = 0; i < _picked.Count; i++)
            {
                Schedule(_picked[i], voice, start + MBRandom.RandomFloat * spread);
            }
        }

        // ---------------- scheduling ----------------

        /// <summary>
        /// Several men (more in a bigger formation) repeat the order line, the formation acknowledges with a crowd
        /// grunt, and a later wave repeats the line again. Each echo is delayed by its own random part plus its
        /// distance from where the order came from. No yells: single men only repeat the order.
        /// </summary>
        private void ScheduleReplies(Formation formation, SkinVoiceManager.SkinVoiceType line, Vec2 source, float start)
        {
            float now = Mission.CurrentTime;
            int units = formation.CountOfUnits;
            int echoCap = Math.Min(EchoesMax, EchoesMin + units / EchoPerMen);
            int echoes = (units >= 4) ? MBRandom.RandomInt(EchoesMin, echoCap + 1) : 1;

            PickUnits(formation, echoes, now);
            for (int i = 0; i < _picked.Count; i++)
            {
                Agent agent = _picked[i];
                float delay = MBRandom.RandomFloatRanged(EchoDelayMin, EchoDelayMax)
                    + agent.Position.AsVec2.Distance(source) * EchoDelayPerMeter;
                Schedule(agent, line, start + Math.Min(delay, EchoDelayCap));
            }

            // The formation acknowledges as one: a crowd grunt after the echoes.
            ScheduleCrowdGrunts(formation, CrowdGruntFor(formation, GetState(formation), now), 1,
                start + EchoDelayMax + 0.3f, 0.4f, now);

            if (units < 8)
            {
                return;
            }
            int lateCap = Math.Min(LateEchoesMax, LateEchoesMin + units / EchoPerMen);
            PickUnits(formation, MBRandom.RandomInt(LateEchoesMin, lateCap + 1), now);
            for (int i = 0; i < _picked.Count; i++)
            {
                Schedule(_picked[i], line, start + MBRandom.RandomFloatRanged(LateEchoDelayMin, LateEchoDelayMax));
            }
        }

        /// <summary>Queues one man's voice: an order line, or a yell in a charge (single men never grunt).</summary>
        private void Schedule(Agent agent, SkinVoiceManager.SkinVoiceType voice, float time, float lateness = VoiceLateness)
        {
            _agentNextVoice[agent] = time + AgentVoiceCooldown;
            _pending.Add(new PendingVoice { Time = time, Deadline = time + lateness, Agent = agent, Voice = voice });
        }

        private void ResolveSoundEvents()
        {
            _rallyIds.Clear();
            for (int i = 0; i < RallyCultures.Length; i++)
            {
                int id = SoundEvent.GetEventIdFromString("event:/alerts/rally/" + RallyCultures[i]);
                if (id >= 0)
                {
                    _rallyIds[RallyCultures[i]] = id;
                }
            }
            _rallyGeneric = SoundEvent.GetEventIdFromString("event:/alerts/rally/generic");
            _nodMove = SoundEvent.GetEventIdFromString("event:/alerts/nods/move");
            _nodAttack = SoundEvent.GetEventIdFromString("event:/alerts/nods/attack");
            _nodStop = SoundEvent.GetEventIdFromString("event:/alerts/nods/stop");
            _nodFormation = SoundEvent.GetEventIdFromString("event:/alerts/nods/formation");

            _marchCrowdIds.Clear();
            for (int i = 0; i < MarchCrowdCultures.Length; i++)
            {
                int id = SoundEvent.GetEventIdFromString(MarchCrowdPrefix + MarchCrowdCultures[i]);
                if (id >= 0)
                {
                    _marchCrowdIds[MarchCrowdCultures[i]] = id;
                }
            }
            _marchCrowdGeneric = SoundEvent.GetEventIdFromString(MarchCrowdPrefix + "generic");
        }

        /// <summary>
        /// The march sample of a culture with its own cadence (rbm/march/{culture}), or rbm/march/generic for any
        /// other culture. No fallback between them: a missing sample means a silent march.
        /// </summary>
        private int MarchCrowdEventFor(string culture)
        {
            if (culture != null && Array.IndexOf(MarchCrowdCultures, culture) >= 0)
            {
                int id;
                return _marchCrowdIds.TryGetValue(culture, out id) ? id : -1;
            }
            return _marchCrowdGeneric;
        }

        // ---------------- crowd sounds ----------------

        /// <summary>
        /// The formation's war cry, in its culture (MarchCultureOf), as its charge starts. Only a formation of WarCryMinUnits
        /// or more, at most once per WarCryCooldown, and one per team per TeamWarCryGap (a whole line charging gives a
        /// few cries, not one per formation on top of each other).
        /// </summary>
        private void TryWarCry(Formation formation, FormationState state, float time, float now)
        {
            if (formation.CountOfUnits < WarCryMinUnits || now < state.NextWarCryTime || formation.Team == null)
            {
                return;
            }
            float teamNext;
            if (_teamNextWarCry.TryGetValue(formation.Team, out teamNext) && now < teamNext)
            {
                return;
            }
            int id = WarCryFor(formation);
            if (id < 0)
            {
                return;
            }
            state.NextWarCryTime = now + WarCryCooldown;
            _teamNextWarCry[formation.Team] = now + TeamWarCryGap;
            _pendingCrowd.Add(new PendingCrowd { Time = time, Formation = formation, EventId = id });
        }

        private int WarCryFor(Formation formation)
        {
            string culture = MarchCultureOf(formation);
            int id;
            return (culture != null && _rallyIds.TryGetValue(culture, out id)) ? id : _rallyGeneric;
        }

        /// <summary>A crowd acknowledgement (alerts/nods) from the formation, for formations of CrowdMinUnits or more, one per team per TeamCrowdGap.</summary>
        private void ScheduleCrowd(Formation formation, int eventId, float time, float now)
        {
            if (eventId < 0 || formation.CountOfUnits < CrowdMinUnits || formation.Team == null)
            {
                return;
            }
            float teamNext;
            if (_teamNextCrowd.TryGetValue(formation.Team, out teamNext) && now < teamNext)
            {
                return;
            }
            _teamNextCrowd[formation.Team] = now + TeamCrowdGap;
            _pendingCrowd.Add(new PendingCrowd { Time = time, Formation = formation, EventId = eventId });
        }

        /// <summary>
        /// Plays the crowd sounds that are due, from the formation's median man as vanilla does for its own crowd
        /// acknowledgement, with the GenderRatio parameter the nods events take (war cries ignore it). Not counted
        /// against the voice cap: there are few of them and each stands for the whole formation.
        /// </summary>
        private void PlayDueCrowds(float now)
        {
            int write = 0;
            // The listener is looked up once per tick, and only if a crowd grunt is due.
            Vec3 listener = Vec3.Zero;
            bool haveListener = false;
            for (int i = 0; i < _pendingCrowd.Count; i++)
            {
                PendingCrowd crowd = _pendingCrowd[i];
                if (crowd.Time > now)
                {
                    _pendingCrowd[write++] = crowd;
                    continue;
                }
                Formation formation = crowd.Formation;
                if (formation == null || formation.CountOfUnits <= 0)
                {
                    continue;
                }
                if (crowd.Source != null)
                {
                    if (!crowd.Source.IsActive())
                    {
                        continue;
                    }
                    if (!haveListener)
                    {
                        listener = SoundManager.GetListenerFrame().origin;
                        haveListener = true;
                    }
                    Vec3 mouth = crowd.Source.Position;
                    mouth.z += MouthHeight;
                    Vec3 position;
                    if (CarryPosition(mouth, listener, out position))
                    {
                        MBSoundEvent.PlaySound(crowd.EventId, position);
                    }
                    continue;
                }
                // A vanilla crowd event (alerts/nods, war cries) from one man of the formation, with its cached female
                // share: no full scan of the formation per sound.
                FormationState crowdState = GetState(formation);
                RefreshCulture(formation, crowdState, now);
                PickUnits(formation, 1, now);
                if (_picked.Count == 0)
                {
                    continue;
                }
                SoundEventParameter parameter = new SoundEventParameter("GenderRatio", crowdState.FemaleShare);
                MBSoundEvent.PlaySound(crowd.EventId, ref parameter, _picked[0].Position);
            }
            _pendingCrowd.RemoveRange(write, _pendingCrowd.Count - write);
        }

        private static float FemaleShare(Formation formation)
        {
            MBReadOnlyList<IFormationUnit> units = formation.Arrangement?.GetAllUnits();
            if (units == null || units.Count == 0)
            {
                return 0f;
            }
            int female = 0;
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i] is Agent agent && agent.IsFemale)
                {
                    female++;
                }
            }
            return (float)female / units.Count;
        }

        /// <summary>The crowd acknowledgement vanilla would play for this movement group (none for fall back / retreat, as vanilla).</summary>
        private int NodFor(MoveGroup group)
        {
            switch (group)
            {
                case MoveGroup.Charge: return _nodAttack;
                case MoveGroup.Stop: return _nodStop;
                case MoveGroup.Advance:
                case MoveGroup.Move:
                case MoveGroup.Follow: return _nodMove;
                default: return -1;
            }
        }

        /// <summary>
        /// Where to play a crowd grunt so it carries further than a voice does (vanilla's fade out by 80 m): within PullStart of
        /// the listener at the man himself, further out pulled toward the listener along the same line, the distance
        /// squeezed from PullStart..MaxHearDistance into PullStart..AudibleRange. It still comes from the right side
        /// and fades with distance. False beyond MaxHearDistance.
        /// </summary>
        private static bool CarryPosition(Vec3 mouth, Vec3 listener, out Vec3 position)
        {
            position = mouth;
            Vec3 offset = mouth - listener;
            float distance = offset.Length;
            if (distance <= PullStart)
            {
                return true;
            }
            if (distance > MaxHearDistance)
            {
                return false;
            }
            float carried = PullStart + (distance - PullStart) * (AudibleRange - PullStart) / (MaxHearDistance - PullStart);
            position = listener + offset * (carried / distance);
            return true;
        }

        /// <summary>
        /// Up to `count` distinct men of the formation who may speak, picked at random into _picked. Random probes
        /// rather than a full scan: a few failed probes on a nearly exhausted formation just mean a quieter reply.
        /// </summary>
        private void PickUnits(Formation formation, int count, float now, bool footOnly = false)
        {
            _picked.Clear();
            MBReadOnlyList<IFormationUnit> units = formation.Arrangement?.GetAllUnits();
            if (units == null || units.Count == 0 || count <= 0)
            {
                return;
            }
            int probes = count * 4 + 4;
            while (_picked.Count < count && probes-- > 0)
            {
                Agent agent = units[MBRandom.RandomInt(units.Count)] as Agent;
                if (agent == null || (footOnly && agent.MountAgent != null) || !CanSpeak(agent, now) || _picked.Contains(agent))
                {
                    continue;
                }
                _picked.Add(agent);
            }
        }

        /// <summary>The captain if he can speak, otherwise a random man of the front rank, otherwise anybody.</summary>
        private Agent PickSpeaker(Formation formation, float now)
        {
            Agent captain = formation.Captain;
            if (captain != null && CanSpeak(captain, now))
            {
                return captain;
            }

            MBReadOnlyList<IFormationUnit> units = formation.Arrangement?.GetAllUnits();
            if (units == null)
            {
                return null;
            }
            // Reservoir pick over the front rank, one pass, no allocation.
            Agent chosen = null;
            int seen = 0;
            for (int i = 0; i < units.Count; i++)
            {
                Agent agent = units[i] as Agent;
                if (agent == null || ((IFormationUnit)agent).FormationRankIndex != 0 || !CanSpeak(agent, now))
                {
                    continue;
                }
                seen++;
                if (MBRandom.RandomInt(seen) == 0)
                {
                    chosen = agent;
                }
            }
            if (chosen != null)
            {
                return chosen;
            }
            PickUnits(formation, 1, now);
            return (_picked.Count > 0) ? _picked[0] : null;
        }

        private bool CanSpeak(Agent agent, float now)
        {
            if (!agent.IsActive() || !agent.IsHuman || agent.IsPlayerControlled || agent.IsDetachedFromFormation)
            {
                return false;
            }
            float next;
            return !_agentNextVoice.TryGetValue(agent, out next) || now >= next;
        }

        /// <summary>
        /// Starts every voice that is due, within the global cap. A voice the cap holds back is retried a moment
        /// later until it is VoiceLateness late, then dropped.
        /// </summary>
        private void SpeakDueVoices(float now)
        {
            int write = 0;
            for (int i = 0; i < _pending.Count; i++)
            {
                PendingVoice voice = _pending[i];
                if (voice.Time > now)
                {
                    _pending[write++] = voice;
                    continue;
                }
                if (now > voice.Deadline || voice.Agent == null || !voice.Agent.IsActive() || voice.Agent.IsPlayerControlled)
                {
                    continue;
                }
                if (now - _recentVoices[_recentHead] < 1f)
                {
                    voice.Time = now + MBRandom.RandomFloatRanged(0.1f, 0.3f);
                    _pending[write++] = voice;
                    continue;
                }
                SkinVoiceManager.SkinVoiceType type = voice.Voice;
                if (type.Index == SkinVoiceManager.VoiceType.Yell.Index && voice.Agent.MountAgent != null)
                {
                    // As vanilla's own yell (Agent.TickParallel): a rider rallies his horse instead.
                    type = SkinVoiceManager.VoiceType.HorseRally;
                }
                voice.Agent.MakeVoice(type, SkinVoiceManager.CombatVoiceNetworkPredictionType.NoPrediction);
                _recentVoices[_recentHead] = now;
                _recentHead = (_recentHead + 1) % _recentVoices.Length;
            }
            _pending.RemoveRange(write, _pending.Count - write);
        }

        // ---------------- helpers ----------------

        private FormationState GetState(Formation formation)
        {
            FormationState state;
            if (!_states.TryGetValue(formation, out state))
            {
                state = new FormationState
                {
                    Group = GroupOf(formation.GetReadonlyMovementOrderReference().OrderEnum),
                    Arrangement = formation.ArrangementOrder.OrderEnum
                };
                _states[formation] = state;
            }
            return state;
        }

        internal static MoveGroup GroupOf(MovementOrder.MovementOrderEnum order)
        {
            switch (order)
            {
                case MovementOrder.MovementOrderEnum.Charge:
                case MovementOrder.MovementOrderEnum.ChargeToTarget:
                    return MoveGroup.Charge;
                case MovementOrder.MovementOrderEnum.Stop:
                    return MoveGroup.Stop;
                case MovementOrder.MovementOrderEnum.Advance:
                    return MoveGroup.Advance;
                case MovementOrder.MovementOrderEnum.FallBack:
                    return MoveGroup.FallBack;
                case MovementOrder.MovementOrderEnum.Retreat:
                    return MoveGroup.Retreat;
                case MovementOrder.MovementOrderEnum.Move:
                    return MoveGroup.Move;
                case MovementOrder.MovementOrderEnum.Follow:
                case MovementOrder.MovementOrderEnum.FollowEntity:
                    return MoveGroup.Follow;
                default:
                    return MoveGroup.None;
            }
        }

        private static SkinVoiceManager.SkinVoiceType? GroupLine(MoveGroup group)
        {
            switch (group)
            {
                case MoveGroup.Charge: return SkinVoiceManager.VoiceType.Charge;
                case MoveGroup.Stop: return SkinVoiceManager.VoiceType.Stop;
                case MoveGroup.Advance: return SkinVoiceManager.VoiceType.Advance;
                case MoveGroup.FallBack: return SkinVoiceManager.VoiceType.FallBack;
                case MoveGroup.Retreat: return SkinVoiceManager.VoiceType.Retreat;
                case MoveGroup.Move: return SkinVoiceManager.VoiceType.Move;
                case MoveGroup.Follow: return SkinVoiceManager.VoiceType.Follow;
                default: return null;
            }
        }

        private static SkinVoiceManager.SkinVoiceType? ArrangementLine(ArrangementOrder.ArrangementOrderEnum arrangement)
        {
            switch (arrangement)
            {
                case ArrangementOrder.ArrangementOrderEnum.Line: return SkinVoiceManager.VoiceType.FormLine;
                case ArrangementOrder.ArrangementOrderEnum.ShieldWall: return SkinVoiceManager.VoiceType.FormShieldWall;
                case ArrangementOrder.ArrangementOrderEnum.Loose: return SkinVoiceManager.VoiceType.FormLoose;
                case ArrangementOrder.ArrangementOrderEnum.Circle: return SkinVoiceManager.VoiceType.FormCircle;
                case ArrangementOrder.ArrangementOrderEnum.Square: return SkinVoiceManager.VoiceType.FormSquare;
                case ArrangementOrder.ArrangementOrderEnum.Skein: return SkinVoiceManager.VoiceType.FormSkein;
                case ArrangementOrder.ArrangementOrderEnum.Column: return SkinVoiceManager.VoiceType.FormColumn;
                case ArrangementOrder.ArrangementOrderEnum.Scatter: return SkinVoiceManager.VoiceType.FormScatter;
                default: return null;
            }
        }

        /// <summary>Silent by design: a failure goes to the game's log once per kind, never to the screen.</summary>
        private void ReportOnce(string key, Exception e)
        {
            if (_reportedFailures.Add(key))
            {
                TaleWorlds.Library.Debug.Print("[RBM FormationShouts] FAILED [" + key + "] " + e.GetType().Name + ": " + e.Message);
            }
        }
    }
}
