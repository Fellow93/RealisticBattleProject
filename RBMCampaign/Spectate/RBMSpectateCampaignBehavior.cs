using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;

namespace RBMCampaign
{
    /// <summary>
    /// Offers to show you somebody else's battle.
    ///
    /// When two AI lords meet in the field with real armies behind them, this asks whether you would like to watch --
    /// and if you say yes, opens the fight in real time with both sides under AI command and you as nothing but a
    /// camera. The battle you watch is a copy: the campaign's own MapEvent goes on auto-resolving beside it and
    /// reaches its own verdict, which is the one that counts. Nothing you see is written back, and your party never
    /// touches the event.
    ///
    /// This exists to answer one question that cannot be answered any other way: does RBM's field AI fight a battle
    /// the way RBM's auto-resolve says it would? The simulation log can tell you what the model thinks happened. Only
    /// this can show you the same muster actually fighting.
    ///
    /// Off by default, and gated behind RTSCamera: without a free camera there is nothing to see the battle with,
    /// since there is no player agent to see it through.
    /// </summary>
    public class RBMSpectateCampaignBehavior : CampaignBehaviorBase
    {
        private static bool _rtsCameraChecked;
        private static bool _rtsCameraPresent;

        /// <summary>
        /// Battles we are still willing to ask about.
        ///
        /// A battle enters this set when it starts and leaves it the moment we ask -- whatever the answer. That is the
        /// whole of the once-per-event bookkeeping: an event we have asked about is not in the set, so it cannot be
        /// asked about again, and a decline is as final as an accept.
        ///
        /// It has to be a set of live references because a MapEvent has no id to key on -- it is not an MBObjectBase
        /// and carries no MBGUID, only a start time and its two sides. References are safe here because the set is
        /// drained from both ends: MapEventEnded removes the normal case, and the sweep in OnMapEventStarted catches
        /// any event that was finalized without the event firing. Per session only; nothing here is saved.
        /// </summary>
        private readonly HashSet<MapEvent> _pending = new HashSet<MapEvent>();

        public override void RegisterEvents()
        {
            CampaignEvents.MapEventStarted.AddNonSerializedListener(this, OnMapEventStarted);
            // Reinforcements. A side that was too small to be worth watching when the fight began may be worth watching
            // once the rest of the army has caught up, and this is the only vanilla signal that says a party has been
            // added to a battle already in progress. It fires from MapEvent.AddInvolvedPartyInternal, which is every
            // path into a map event -- army members, allies converging, garrisons pouring out.
            CampaignEvents.OnPartyAddedToMapEventEvent.AddNonSerializedListener(this, OnPartyAddedToMapEvent);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnMapEventStarted(MapEvent mapEvent, PartyBase attackerParty, PartyBase defenderParty)
        {
            // Cheap insurance against the set outliving the battles in it. An event that finalized without ever firing
            // MapEventEnded would otherwise sit here holding a dead battle for the rest of the session.
            _pending.RemoveWhere(IsGone);

            if (mapEvent == null || !IsWatchableKind(mapEvent))
            {
                return;
            }

            // Tracked before we ask, because asking is allowed to fail: a battle that is too small right now is exactly
            // the battle that a joining party may make big enough a moment from now.
            _pending.Add(mapEvent);
            TryOffer(mapEvent);
        }

        /// <summary>
        /// A party has joined a battle that was already running -- reconsider it.
        ///
        /// This fires for the two founding parties too, from inside MapEvent.Initialize and before MapEventStarted, at
        /// a point where the event is still half-built (no State, no BattleState, no next simulation time). Keying off
        /// _pending is what keeps us out of that: an event is only in the set once MapEventStarted has fired for it, so
        /// the founding adds find nothing and return, and every add we do act on is on a fully-built event.
        /// </summary>
        private void OnPartyAddedToMapEvent(PartyBase party)
        {
            MapEvent mapEvent = (party != null) ? party.MapEvent : null;
            if (mapEvent == null || !_pending.Contains(mapEvent))
            {
                return;
            }
            TryOffer(mapEvent);
        }

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            if (mapEvent != null)
            {
                _pending.Remove(mapEvent);
            }
        }

        private static bool IsGone(MapEvent mapEvent)
        {
            return mapEvent == null || mapEvent.IsFinalized;
        }

        private void TryOffer(MapEvent mapEvent)
        {
            if (!ShouldOffer(mapEvent))
            {
                // Still pending. Not "no" -- just "not yet".
                return;
            }

            // Off the list before the box goes up, not after. Asked is asked: whether he watches or waves it away, this
            // battle is spent, and a party joining while the inquiry is on screen must not stack a second one behind it.
            _pending.Remove(mapEvent);

            List<InquiryElement> sides = new List<InquiryElement>
            {
                new InquiryElement(BattleSideEnum.Attacker, SideLabel(mapEvent, BattleSideEnum.Attacker), null),
                new InquiryElement(BattleSideEnum.Defender, SideLabel(mapEvent, BattleSideEnum.Defender), null)
            };

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("{=RBM_SPECTATE_001}Watch this battle?").ToString(),
                new TextObject("{=RBM_SPECTATE_002}Two armies have met in the field. You may watch the fight from a free camera, with both sides under their own commanders. Choose whose lines to watch it from.{newline}{newline}This is an observation only: the battle on the map resolves on its own, and nothing you see here changes it.").ToString(),
                sides,
                true,
                1,
                1,
                new TextObject("{=RBM_SPECTATE_003}Watch").ToString(),
                new TextObject("{=RBM_SPECTATE_004}Ignore").ToString(),
                OnSideChosen(mapEvent),
                null), true);
        }

        private static Action<List<InquiryElement>> OnSideChosen(MapEvent mapEvent)
        {
            return delegate (List<InquiryElement> selected)
            {
                if (selected == null || selected.Count == 0)
                {
                    return;
                }

                // The world moved while the box was up -- or could have. The inquiry pauses the campaign, so this
                // should never fire, but opening a mission for a battle that has already been decided would be a
                // crash rather than a disappointment. Ask again.
                if (!ShouldOffer(mapEvent))
                {
                    return;
                }

                BattleSideEnum watchSide = (BattleSideEnum)selected[0].Identifier;
                try
                {
                    RBMSpectatorMission.OpenSpectatorBattleMission(mapEvent, watchSide);
                }
                catch (Exception exception)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "RBM: could not open the spectated battle. " + exception.Message));
                }
            };
        }

        /// <summary>
        /// Whether this is the sort of battle we could ever want, ignoring how big it is or what else is going on.
        ///
        /// These are the things about a map event that never change once it has started, which is what makes them the
        /// right test for whether to keep an eye on it at all. Everything that can change with time -- the head count,
        /// whether it has been decided, whether the player is in a mission -- belongs in ShouldOffer.
        /// </summary>
        private static bool IsWatchableKind(MapEvent mapEvent)
        {
            // A field battle, and a real one. Raids, sieges, sally-outs and hideout fights all want a different
            // mission with a different scene and a different set of behaviours; a sea fight wants the naval mission.
            // This clone is the open-field one.
            if (!mapEvent.IsFieldBattle || mapEvent.IsNavalMapEvent)
            {
                return false;
            }
            // Somebody else's fight, always. The player's own battle has a mission of its own, with the player in it.
            if (mapEvent.IsPlayerMapEvent)
            {
                return false;
            }
            return RBMConfig.RBMConfig.rbmCampaignEnabled
                   && RBMConfig.RBMConfig.spectateBattlesEnabled
                   && IsRTSCameraPresent();
        }

        private static bool ShouldOffer(MapEvent mapEvent)
        {
            if (mapEvent == null || mapEvent.IsFinalized || !IsWatchableKind(mapEvent))
            {
                return false;
            }
            // Not while the player is already standing in a battle of his own. The campaign clock is stopped during a
            // mission, but events still start, and stacking a second mission on the first is not a thing to attempt.
            if (Mission.Current != null)
            {
                return false;
            }
            // Already decided. The map event goes on auto-resolving while it waits for reinforcements, and a battle
            // that has found its winner has nothing left to show -- opening it would spawn the losing side's dead.
            if (mapEvent.BattleState != BattleState.None)
            {
                return false;
            }
            if (!mapEvent.HasTroopsOnBothSides())
            {
                return false;
            }
            if (mapEvent.GetLeaderParty(BattleSideEnum.Attacker) == null
                || mapEvent.GetLeaderParty(BattleSideEnum.Defender) == null)
            {
                return false;
            }

            // Worth stopping the campaign for. A skirmish between two patrols tells you nothing about how a line
            // holds, and being asked about every looter band on the map would make the feature unusable.
            //
            // GetNumberOfInvolvedMen routes through MapEventSide.RecalculateMemberCountOfSide, which walks every party
            // on the side and sums NumberOfHealthyMembers -- so this counts the whole muster, army and allies both, and
            // recounts it live. It is the right measure, and it is why re-asking it after a join can change the answer.
            int threshold = RBMConfig.RBMConfig.spectateMinTroopsPerSide;
            return mapEvent.GetNumberOfInvolvedMen(BattleSideEnum.Attacker) >= threshold
                   && mapEvent.GetNumberOfInvolvedMen(BattleSideEnum.Defender) >= threshold;
        }

        private static string SideLabel(MapEvent mapEvent, BattleSideEnum side)
        {
            PartyBase leader = mapEvent.GetLeaderParty(side);
            string name = (leader != null && leader.Name != null) ? leader.Name.ToString() : side.ToString();
            int men = mapEvent.GetNumberOfInvolvedMen(side);

            TextObject label = new TextObject(
                (side == BattleSideEnum.Attacker)
                    ? "{=RBM_SPECTATE_005}Attackers: {NAME} ({COUNT} men)"
                    : "{=RBM_SPECTATE_006}Defenders: {NAME} ({COUNT} men)");
            label.SetTextVariable("NAME", name);
            label.SetTextVariable("COUNT", men);
            return label.ToString();
        }

        /// <summary>
        /// RTSCamera is not optional here, and this is asked once.
        ///
        /// The spectated battle has no player agent at all -- that is the whole design, not an oversight -- so the
        /// game's own camera has nothing to sit behind. RTSCamera's free camera is the only thing that can look at
        /// it. Without the module the mission would open onto a black screen.
        /// </summary>
        private static bool IsRTSCameraPresent()
        {
            if (!_rtsCameraChecked)
            {
                _rtsCameraChecked = true;
                _rtsCameraPresent = ModuleHelper.IsModuleActive("RTSCamera");
            }
            return _rtsCameraPresent;
        }
    }
}
