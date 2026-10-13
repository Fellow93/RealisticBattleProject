using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Producer for <see cref="StuckPartyLog"/>: finds map parties that have somewhere to go but do not move.
    /// Written to diagnose lords, caravans and fleets sitting still for days near some coastal ports on the War
    /// Sails map, where it was unknown whether the engine's path search (MobileParty._aiPathNotFound -- with no
    /// path the party walks straight at its target and grinds against the shore) or an RBM behavior sending it
    /// somewhere unreachable was to blame. Purely an observer: it never changes a party.
    ///
    /// Once per in-game hour it reads a few fields of every party in MobileParty.All. A party is watched while it
    /// has a move target (TargetSettlement / TargetParty, or a movement DefaultBehavior with a TargetPosition)
    /// that it has not reached, and is not in a battle, a siege, someone else's army or a settlement it means to
    /// stay in. A watched party that has moved less than <see cref="MoveThreshold"/> map units in
    /// <see cref="StuckHours"/> in-game hours gets ONE STUCK line with everything readable about it; the only
    /// navigation queries (two PathExistBetweenPoints) are made for that line. Nothing is saved: the behavior is
    /// created anew on every new game / load (RBM/SubModule.OnGameStart), so tracking restarts from zero.
    /// </summary>
    public class StuckPartyLogBehavior : CampaignBehaviorBase
    {
        // A party "has not moved" while it stays within this many map units of where it was first seen still.
        // Parties make several units an hour on the map, so 1.0 is well under one hour's travel.
        internal const float MoveThreshold = 1.0f;

        // How long a watched party may stay within MoveThreshold before it is flagged.
        internal const int StuckHours = 24;

        // A party this close to its target point has arrived (or is waiting there on purpose): not watched.
        private const float ArrivedDistance = 1.0f;

        private sealed class Entry
        {
            public Vec2 Anchor;          // where the party was when this still spell began
            public CampaignTime Since;   // when that was
            public bool Logged;          // STUCK already written for this spell
            public int Pass;             // last hourly pass that saw the party watched
        }

        // Non-persisted. Keyed by party; dropped when a party is destroyed, stops being watched, or is not
        // in MobileParty.All on a pass.
        private readonly Dictionary<MobileParty, Entry> _tracked = new Dictionary<MobileParty, Entry>();
        private readonly List<MobileParty> _removals = new List<MobileParty>();
        private int _pass;

        // Private engine state read for the STUCK line. Null if a game update renamed them; the line then says "?".
        private static readonly AccessTools.FieldRef<MobileParty, bool> AiPathNotFoundRef = FieldRef<bool>("_aiPathNotFound");
        private static readonly AccessTools.FieldRef<MobileParty, bool> PathModeRef = FieldRef<bool>("_pathMode");
        private static readonly AccessTools.FieldRef<MobileParty, MobileParty.NavigationType> LastPathNavRef =
            FieldRef<MobileParty.NavigationType>("_lastComputedPathNavigationType");

        private static AccessTools.FieldRef<MobileParty, T> FieldRef<T>(string name)
        {
            try
            {
                return AccessTools.FieldRefAccess<MobileParty, T>(name);
            }
            catch
            {
                return null;
            }
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnMobilePartyDestroyed);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            _tracked.Clear();
            _pass = 0;
            if (StuckPartyLog.IsEnabled)
            {
                StuckPartyLog.Log("SESSION", "", "campaign session started; tracking restarts, the first STUCK lines can come "
                    + StuckHours + " in-game hours from now");
            }
        }

        private void OnMobilePartyDestroyed(MobileParty party, PartyBase destroyer)
        {
            Entry entry;
            if (party == null || !_tracked.TryGetValue(party, out entry))
            {
                return;
            }
            if (entry.Logged)
            {
                StuckPartyLog.Log("GONE", PartyName(party), Describe(party) + "  destroyed"
                    + (destroyer != null ? " by " + Text(destroyer.Name) : "")
                    + " after " + HoursSince(entry) + "h without moving");
            }
            _tracked.Remove(party);
        }

        private void OnHourlyTick()
        {
            if (!StuckPartyLog.IsEnabled)
            {
                // Switched off mid-session: forget everything, start clean if it is switched on again.
                if (_tracked.Count > 0)
                {
                    _tracked.Clear();
                }
                return;
            }
            if (Campaign.Current == null)
            {
                return;
            }

            _pass++;
            MBReadOnlyList<MobileParty> all = MobileParty.All;
            for (int i = 0; i < all.Count; i++)
            {
                MobileParty party = all[i];
                if (party == null)
                {
                    continue;
                }
                try
                {
                    Check(party);
                }
                catch
                {
                    // A diagnostic must never take the campaign down; skip the party this hour.
                }
            }

            // Parties that left MobileParty.All without a destroy event (removed, disbanded into nothing).
            _removals.Clear();
            foreach (KeyValuePair<MobileParty, Entry> pair in _tracked)
            {
                if (pair.Value.Pass != _pass)
                {
                    _removals.Add(pair.Key);
                }
            }
            for (int i = 0; i < _removals.Count; i++)
            {
                MobileParty party = _removals[i];
                Entry entry = _tracked[party];
                if (entry.Logged)
                {
                    StuckPartyLog.Log("GONE", SafePartyName(party), "left the map after " + HoursSince(entry) + "h without moving");
                }
                _tracked.Remove(party);
            }

            if (CampaignTime.Now.GetHourOfDay == 0)
            {
                int flagged = 0;
                foreach (KeyValuePair<MobileParty, Entry> pair in _tracked)
                {
                    if (pair.Value.Logged)
                    {
                        flagged++;
                    }
                }
                StuckPartyLog.Log("DAILY", "", _tracked.Count + " parties watched (moving with a target), " + flagged + " flagged stuck");
            }
        }

        private void Check(MobileParty party)
        {
            string notWatched = WhyNotWatched(party);
            Entry entry;
            if (notWatched != null)
            {
                if (_tracked.TryGetValue(party, out entry))
                {
                    if (entry.Logged)
                    {
                        StuckPartyLog.Log("CLEARED", PartyName(party), Describe(party) + "  " + notWatched
                            + " after " + HoursSince(entry) + "h without moving");
                    }
                    _tracked.Remove(party);
                }
                return;
            }

            Vec2 position = party.Position.ToVec2();
            if (!_tracked.TryGetValue(party, out entry))
            {
                entry = new Entry { Anchor = position, Since = CampaignTime.Now };
                _tracked[party] = entry;
            }
            entry.Pass = _pass;

            float moved = position.Distance(entry.Anchor);
            if (moved >= MoveThreshold)
            {
                if (entry.Logged)
                {
                    StuckPartyLog.Log("UNSTUCK", PartyName(party), Describe(party) + "  moving again after "
                        + HoursSince(entry) + "h  pos=" + Pos(party.Position)
                        + "  ai=" + party.DefaultBehavior + "/" + party.ShortTermBehavior
                        + "  target=" + TargetText(party));
                }
                entry.Anchor = position;
                entry.Since = CampaignTime.Now;
                entry.Logged = false;
                return;
            }

            if (!entry.Logged && entry.Since.ElapsedHoursUntilNow >= StuckHours)
            {
                entry.Logged = true;
                StuckPartyLog.Log("STUCK", PartyName(party), BuildStuckLine(party, entry, moved));
            }
        }

        /// <summary>Null if the party is trying to go somewhere it has not reached; otherwise why it is not watched.</summary>
        private static string WhyNotWatched(MobileParty party)
        {
            if (!party.IsActive)
            {
                return "inactive";
            }
            if (party.IsMainParty || party.IsGarrison || party.IsMilitia)
            {
                return "not an AI field party";
            }
            if (party.MapEvent != null)
            {
                return "in a battle";
            }
            if (party.BesiegerCamp != null)
            {
                return "besieging " + Name(party.BesiegedSettlement);
            }
            if (party.AttachedTo != null)
            {
                // Army members follow their leader; the leader is the one watched.
                return "attached to " + PartyName(party.AttachedTo) + "'s army";
            }
            Settlement current = party.CurrentSettlement;
            if (current != null && party.TargetParty == null
                && (party.TargetSettlement == null || party.TargetSettlement == current))
            {
                return "in " + Name(current);
            }
            AiBehavior behavior = party.DefaultBehavior;
            if ((behavior == AiBehavior.Hold || behavior == AiBehavior.None) && party.PartyMoveMode == MoveModeType.Hold)
            {
                return "holding (no move target)";
            }
            bool hasTarget = party.TargetSettlement != null || party.TargetParty != null
                || (IsMovementBehavior(behavior) && party.TargetPosition.IsNonZero());
            if (!hasTarget)
            {
                return "no move target (" + behavior + ", " + party.PartyMoveMode + ")";
            }
            CampaignVec2 targetPoint = TargetPoint(party);
            if (targetPoint.IsNonZero() && party.Position.Distance(targetPoint) < ArrivedDistance)
            {
                return "at its target";
            }
            return null;
        }

        private static bool IsMovementBehavior(AiBehavior behavior)
        {
            switch (behavior)
            {
                case AiBehavior.Hold:
                case AiBehavior.None:
                case AiBehavior.DoOperation:
                case AiBehavior.NumAiBehaviors:
                    return false;
                default:
                    return true;
            }
        }

        // Where the party is actually headed: the settlement's port or gate, the target party, or the point.
        private static CampaignVec2 TargetPoint(MobileParty party)
        {
            Settlement settlement = party.TargetSettlement;
            if (settlement != null)
            {
                return party.IsTargetingPort ? settlement.PortPosition : settlement.GatePosition;
            }
            MobileParty targetParty = party.TargetParty;
            if (targetParty != null)
            {
                return targetParty.Position;
            }
            return party.TargetPosition;
        }

        private static string BuildStuckLine(MobileParty party, Entry entry, float moved)
        {
            StringBuilder sb = new StringBuilder(640);
            sb.Append(Describe(party));
            sb.Append("  men=").Append(party.MemberRoster != null ? party.MemberRoster.TotalManCount : 0);
            sb.Append("  still ").Append(HoursSince(entry)).Append("h (moved ").Append(F(moved)).Append(")");

            // Where it is.
            CampaignVec2 position = party.Position;
            sb.Append(" | pos=").Append(Pos(position)).Append(" land=").Append(B(position.IsOnLand));
            float nearestDistance;
            Settlement nearest = NearestSettlement(position, out nearestDistance);
            sb.Append(" near=").Append(Name(nearest));
            if (nearest != null)
            {
                sb.Append(" d=").Append(F(nearestDistance));
                if (nearest.HasPort)
                {
                    sb.Append(" (port)");
                }
            }
            sb.Append(" in=").Append(Name(party.CurrentSettlement));

            // What it wants.
            sb.Append(" | ai=").Append(party.DefaultBehavior).Append("/").Append(party.ShortTermBehavior)
                .Append(" moveMode=").Append(party.PartyMoveMode);
            try
            {
                MobilePartyAi ai = party.Ai;
                if (ai != null)
                {
                    sb.Append(" aiDisabled=").Append(B(ai.IsDisabled)).Append(" noDecisions=").Append(B(ai.DoNotMakeNewDecisions));
                }
            }
            catch
            {
            }
            sb.Append(" disbanding=").Append(B(party.IsDisbanding)).Append(" quest=").Append(B(party.IsCurrentlyUsedByAQuest));
            sb.Append(" | target=").Append(TargetText(party));
            CampaignVec2 targetPoint = TargetPoint(party);
            sb.Append(" dist=").Append(targetPoint.IsNonZero() ? F(position.Distance(targetPoint)) : "-");
            sb.Append(" moveTo=").Append(Pos(party.MoveTargetPoint)).Append(" next=").Append(Pos(party.NextTargetPosition));
            sb.Append(" shortTarget=");
            try
            {
                Settlement shortSettlement = party.ShortTermTargetSettlement;
                MobileParty shortParty = party.ShortTermTargetParty;
                sb.Append(shortSettlement != null ? Name(shortSettlement) : (shortParty != null ? PartyName(shortParty) : "-"));
            }
            catch
            {
                sb.Append("?");
            }

            // How it may travel.
            sb.Append(" | nav desired=").Append(party.DesiredAiNavigationType)
                .Append(" cap=").Append(Try(() => party.NavigationCapability.ToString()))
                .Append(" naval=").Append(Try(() => B(party.HasNavalNavigationCapability)))
                .Append(" atSea=").Append(B(party.IsCurrentlyAtSea))
                .Append(" raft=").Append(B(party.IsInRaftState))
                .Append(" transition=").Append(B(party.IsTransitionInProgress))
                .Append(" exitPort=").Append(B(party.StartTransitionNextFrameToExitFromPort))
                .Append(" anchor=").Append(AnchorText(party.Anchor))
                .Append(" ships=").Append(party.Ships != null ? party.Ships.Count : 0);

            // What the path search made of it.
            sb.Append(" | pathNotFound=").Append(ReadFlag(AiPathNotFoundRef, party))
                .Append(" pathMode=").Append(ReadFlag(PathModeRef, party))
                .Append(" lastPathNav=").Append(Try(() => LastPathNavRef != null ? LastPathNavRef(party).ToString() : "?"));
            if (targetPoint.IsNonZero())
            {
                MobileParty.NavigationType desired = party.DesiredAiNavigationType;
                sb.Append(" exists(desired)=").Append(desired == MobileParty.NavigationType.None ? "n/a" : PathExists(position, targetPoint, desired))
                    .Append(" exists(all)=").Append(PathExists(position, targetPoint, MobileParty.NavigationType.All));
            }
            sb.Append(" speed=").Append(Try(() => F(party.Speed)));
            if (party.StationaryStartTime != CampaignTime.Zero)
            {
                sb.Append(" stationary=").Append(Try(() => ((int)party.StationaryStartTime.ElapsedHoursUntilNow).ToString(CultureInfo.InvariantCulture))).Append("h");
            }
            sb.Append(" army=").Append(ArmyText(party));
            return sb.ToString();
        }

        // "<name> (<id>) kind=.. clan=.. faction=.. leader=.." -- the identity block every line starts with.
        private static string Describe(MobileParty party)
        {
            StringBuilder sb = new StringBuilder(160);
            sb.Append(PartyName(party)).Append(" (").Append(party.StringId).Append(")");
            sb.Append(" kind=").Append(Kind(party));
            sb.Append(" clan=").Append(party.ActualClan != null ? Text(party.ActualClan.Name) : "-");
            sb.Append(" faction=").Append(party.MapFaction != null ? Text(party.MapFaction.Name) : "-");
            sb.Append(" leader=").Append(party.LeaderHero != null ? Text(party.LeaderHero.Name) : "-");
            return sb.ToString();
        }

        private static string Kind(MobileParty party)
        {
            string kind;
            if (MercenaryCompanies.IsCompanyParty(party))
            {
                kind = "merc-company";
            }
            else if (party.IsLordParty)
            {
                kind = "lord";
            }
            else if (party.IsCaravan)
            {
                kind = RBMCaravanRegister.IsManaged(party) ? "rbm-caravan" : "caravan";
            }
            else if (party.IsVillager)
            {
                kind = "villager";
            }
            else if (party.IsBandit)
            {
                kind = (party.ActualClan != null && party.ActualClan.StringId == "deserters") ? "deserter" : "bandit";
            }
            else if (party.IsPatrolParty)
            {
                kind = "patrol";
            }
            else if (party.IsCustomParty)
            {
                kind = "custom";
            }
            else
            {
                kind = "other";
            }
            if (party.Army != null && party.Army.LeaderParty == party)
            {
                kind += "+army-leader";
            }
            return kind;
        }

        private static string TargetText(MobileParty party)
        {
            if (party.TargetSettlement != null)
            {
                return Name(party.TargetSettlement) + "(" + party.TargetSettlement.StringId + ") port=" + B(party.IsTargetingPort);
            }
            if (party.TargetParty != null)
            {
                return "party " + PartyName(party.TargetParty) + "(" + party.TargetParty.StringId + ")";
            }
            return "point " + Pos(party.TargetPosition);
        }

        private static string AnchorText(AnchorPoint anchor)
        {
            if (anchor == null)
            {
                return "-";
            }
            try
            {
                if (anchor.Settlement != null)
                {
                    return Name(anchor.Settlement);
                }
                if (anchor.IsValid)
                {
                    return Pos(anchor.Position) + (anchor.IsDisabled ? "(disabled)" : "");
                }
                return anchor.IsMovingToPoint ? "moving" : "-";
            }
            catch
            {
                return "?";
            }
        }

        private static string ArmyText(MobileParty party)
        {
            Army army = party.Army;
            if (army == null)
            {
                return "-";
            }
            if (army.LeaderParty != party)
            {
                return "member of " + PartyName(army.LeaderParty);
            }
            string gathering = Try(() => B(army.IsWaitingForArmyMembers()));
            return "leader parties=" + (army.Parties != null ? army.Parties.Count : 0) + " gathering=" + gathering;
        }

        private static string PathExists(CampaignVec2 from, CampaignVec2 to, MobileParty.NavigationType type)
        {
            try
            {
                return B(Campaign.Current.Models.MapDistanceModel.PathExistBetweenPoints(from, to, type));
            }
            catch
            {
                return "?";
            }
        }

        // Linear scan; only made for a STUCK line, never on the hourly pass.
        private static Settlement NearestSettlement(CampaignVec2 position, out float distance)
        {
            Settlement best = null;
            float bestSquared = float.MaxValue;
            MBReadOnlyList<Settlement> all = Settlement.All;
            for (int i = 0; i < all.Count; i++)
            {
                Settlement settlement = all[i];
                if (settlement == null || settlement.IsHideout)
                {
                    continue;
                }
                float squared = position.DistanceSquared(settlement.Position);
                if (squared < bestSquared)
                {
                    bestSquared = squared;
                    best = settlement;
                }
            }
            distance = best != null ? (float)Math.Sqrt(bestSquared) : 0f;
            return best;
        }

        private static string ReadFlag(AccessTools.FieldRef<MobileParty, bool> field, MobileParty party)
        {
            if (field == null)
            {
                return "?";
            }
            try
            {
                return B(field(party));
            }
            catch
            {
                return "?";
            }
        }

        private static string Try(Func<string> read)
        {
            try
            {
                return read();
            }
            catch
            {
                return "?";
            }
        }

        private static int HoursSince(Entry entry)
        {
            return (int)entry.Since.ElapsedHoursUntilNow;
        }

        private static string PartyName(MobileParty party)
        {
            return party == null ? "?" : (party.Name != null ? party.Name.ToString() : party.StringId);
        }

        private static string SafePartyName(MobileParty party)
        {
            try
            {
                return PartyName(party) + " (" + party.StringId + ")";
            }
            catch
            {
                return "?";
            }
        }

        private static string Name(Settlement settlement)
        {
            return settlement == null ? "-" : (settlement.Name != null ? settlement.Name.ToString() : settlement.StringId);
        }

        private static string Text(TaleWorlds.Localization.TextObject text)
        {
            return text != null ? text.ToString() : "?";
        }

        private static string Pos(CampaignVec2 position)
        {
            if (!position.IsNonZero())
            {
                return "-";
            }
            return "(" + F(position.X) + "," + F(position.Y) + ")";
        }

        private static string F(float value)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string B(bool value)
        {
            return value ? "1" : "0";
        }
    }
}
