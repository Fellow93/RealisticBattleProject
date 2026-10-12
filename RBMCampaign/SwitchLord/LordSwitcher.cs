using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.BattleWreckages;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;

namespace RBMCampaign
{
    public static class LordSwitcher
    {
        private static readonly PropertyInfo PlayerDefaultFactionProp =
            typeof(Campaign).GetProperty("PlayerDefaultFaction",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        // The next popup to open. Each popup opens on the tick after the previous one's callback, never from
        // inside it: the popup VM runs the callback before it closes itself.
        private static Action _pendingStep;

        // Observe mode: the map camera follows this lord's party (or his captor while he is a prisoner) and
        // the fog of war is lifted around it (ObserveVisibilityPatch). Lasts until stopped from the picker,
        // until the player moves the party with a map click, until a menu opens while the player is not in an
        // army (ObserveEndingPatches), or until the lord has no party left to follow. Any other camera
        // takeover (joining, leaving or riding in an army, the army menus) is undone on the next tick.
        private static Hero _observedHero;
        private static PartyBase _observedParty;
        private static Campaign _observeCampaign;
        private static bool _observeEndRequested;

        /// <summary>Polled every frame on the map (no mission running) from RBM.SubModule.</summary>
        public static void Tick()
        {
            if (Campaign.Current == null)
                return;

            TickObserve();

            if (_pendingStep != null)
            {
                Action step = _pendingStep;
                _pendingStep = null;
                step();
            }

            CheckHotkey();
        }

        private static void CheckHotkey()
        {
            if (MobileParty.MainParty == null)
                return;

            // In an army the player usually sits in one of its menus (army_wait, or in a settlement with it);
            // the picker opens there too, for observing.
            if (!IsInArmy && (MobileParty.MainParty.CurrentSettlement != null || Campaign.Current.CurrentMenuContext != null))
                return;

            bool ctrl = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
            bool shift = Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift);

            if (ctrl && shift && Input.IsKeyPressed(InputKey.L))
            {
                ShowLordPicker();
            }
        }

        public static void ShowLordPicker()
        {
            if (Campaign.Current == null)
            {
                InformationManager.DisplayMessage(new InformationMessage("Not in a campaign.", Colors.Red));
                return;
            }

            var lordsByKingdom = Hero.AllAliveHeroes
                .Where(h => h.IsLord && h != Hero.MainHero && h.PartyBelongedTo != null)
                .GroupBy(h => h.Clan != null ? h.Clan.Kingdom : null)
                .OrderBy(g => g.Key == null ? "zzz" : g.Key.Name.ToString());

            List<InquiryElement> elements = new List<InquiryElement>();

            if (IsObserving)
            {
                elements.Add(new InquiryElement(
                    StopObservingId,
                    "Stop observing " + _observedHero.Name,
                    null,
                    true,
                    "Return the camera to your own party."));
            }

            foreach (var group in lordsByKingdom)
            {
                string kingdomName = group.Key != null ? group.Key.Name.ToString() : "No Kingdom";
                int count = group.Count();

                elements.Add(new InquiryElement(
                    null,
                    "--- " + kingdomName + " (" + count + ") ---",
                    null,
                    false,
                    kingdomName + ": " + count + " lords with active parties"));

                foreach (Hero h in group.OrderBy(h => h.Name.ToString()))
                {
                    elements.Add(new InquiryElement(
                        h,
                        h.Name + " (" + (h.Clan != null ? h.Clan.Name.ToString() : "?") + ") - "
                            + h.PartyBelongedTo.MemberRoster.TotalManCount + " troops",
                        null,
                        true,
                        BuildHint(h)));
                }
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Switch Lord",
                "All lords with active parties, grouped by kingdom:",
                elements,
                true,
                1,
                1,
                "Select",
                "Cancel",
                selected => OnLordSelected(selected),
                null,
                "",
                true), true);
        }

        private const string StopObservingId = "stop_observing";
        private const string SwitchActionId = "switch";
        private const string ObserveActionId = "observe";

        /// <summary>Second step after picking a lord: take the lord over, or only watch with the camera.</summary>
        private static void ShowActionPicker(Hero h)
        {
            // Switching pulls the player's party out of its army (SwitchToLord), which must not happen under
            // the army's menus; observing works there.
            bool canSwitch = MobileParty.MainParty.CurrentSettlement == null && Campaign.Current.CurrentMenuContext == null;
            List<InquiryElement> elements = new List<InquiryElement>
            {
                new InquiryElement(SwitchActionId, "Switch to " + h.Name, null, canSwitch,
                    canSwitch
                        ? "Play as " + h.Name + ": you take over this lord's party and clan."
                        : "Not while a menu is open: leave the army or the settlement first."),
                new InquiryElement(ObserveActionId, "Observe " + h.Name, null, true,
                    "The camera follows this lord's party and the map around it is revealed. You keep playing "
                    + "your own character. Moving your party with a click on the map, or a menu opening while "
                    + "you are not in an army, returns the camera to you."),
            };

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                h.Name.ToString(),
                BuildHint(h),
                elements,
                true,
                1,
                1,
                "OK",
                "Back",
                selected =>
                {
                    if (selected == null || selected.Count == 0)
                        return;
                    if ((string)selected[0].Identifier == ObserveActionId)
                        _pendingStep = () => StartObserving(h);
                    else
                        _pendingStep = () => SwitchToLord(h);
                },
                _ => _pendingStep = ShowLordPicker,
                "",
                true), true);
        }

        private static string BuildHint(Hero h)
        {
            string clan = h.Clan != null ? h.Clan.Name.ToString() : "None";
            string kingdom = h.Clan != null && h.Clan.Kingdom != null ? h.Clan.Kingdom.Name.ToString() : "None";
            string party = h.PartyBelongedTo != null
                ? h.PartyBelongedTo.Name + " (" + h.PartyBelongedTo.MemberRoster.TotalManCount + " troops)"
                : "No party";
            return "Age: " + (int)h.Age + " | Clan: " + clan + " | Kingdom: " + kingdom + " | " + party;
        }

        private static void OnLordSelected(List<InquiryElement> selected)
        {
            if (selected == null || selected.Count == 0)
                return;

            if (selected[0].Identifier as string == StopObservingId)
            {
                _pendingStep = () => StopObserving(true, null);
                return;
            }

            Hero targetHero = selected[0].Identifier as Hero;
            if (targetHero == null)
                return;

            _pendingStep = () => ShowActionPicker(targetHero);
        }

        private static bool IsObserving =>
            _observedHero != null && _observedParty != null && _observeCampaign == Campaign.Current;

        private static bool IsInArmy => MobileParty.MainParty != null && MobileParty.MainParty.Army != null;

        /// <summary>The party the camera follows for an observed lord: the lord's own, or the captor's while a prisoner.</summary>
        private static PartyBase ObservedPartyOf(Hero hero)
        {
            if (hero == null || !hero.IsAlive)
                return null;
            if (hero.PartyBelongedTo != null)
                return hero.PartyBelongedTo.Party;
            if (hero.IsPrisoner)
                return hero.PartyBelongedToAsPrisoner;
            return null;
        }

        public static void StartObserving(Hero targetHero)
        {
            PartyBase party = ObservedPartyOf(targetHero);
            if (Campaign.Current == null || party == null || MapScreen.Instance == null || Hero.MainHero.IsPrisoner
                || (!IsInArmy && (MobileParty.MainParty.CurrentSettlement != null || Campaign.Current.CurrentMenuContext != null)))
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "Cannot observe " + (targetHero != null ? targetHero.Name.ToString() : "?") + " right now.",
                    Colors.Red));
                return;
            }

            _observedHero = targetHero;
            _observeCampaign = Campaign.Current;
            _observeEndRequested = false;
            FollowWithCamera(party);
            InformationManager.DisplayMessage(new InformationMessage(
                "Observing " + targetHero.Name + ". Open the lord picker (Ctrl+Shift+L) or click on the map to stop.",
                Colors.Green));
        }

        private static void FollowWithCamera(PartyBase party)
        {
            _observedParty = party;
            MapScreen mapScreen = MapScreen.Instance;
            if (mapScreen == null)
                return;
            // Fly the camera over at the fast speed first; plain follow mode crawls across the map. Called on
            // the camera view directly: MapScreen's version does nothing while a menu (the army's) is open.
            mapScreen.MapCameraView.FastMoveCameraToPosition(party.Position, false);
            mapScreen.MapCameraView.SetCameraMode(MapCameraView.CameraFollowMode.FollowParty);
            party.SetAsCameraFollowParty();
        }

        /// <param name="returnCamera">Fly the camera back to the player's party (not needed when vanilla already took it).</param>
        /// <param name="reason">Shown to the player; null for none.</param>
        private static void StopObserving(bool returnCamera, string reason)
        {
            bool wasObserving = IsObserving;
            _observedHero = null;
            _observedParty = null;
            _observeCampaign = null;
            _observeEndRequested = false;
            if (wasObserving && returnCamera && MapScreen.Instance != null && MobileParty.MainParty != null)
            {
                MapScreen.Instance.MapCameraView.FastMoveCameraToMainParty();
            }
            if (wasObserving && reason != null)
            {
                InformationManager.DisplayMessage(new InformationMessage(reason, Colors.Yellow));
            }
        }

        private static void TickObserve()
        {
            if (_observedHero == null)
                return;

            // A different campaign was loaded: the stored hero and party belong to the old one.
            if (_observeCampaign != Campaign.Current)
            {
                StopObserving(false, null);
                return;
            }

            // The player moved the party with a map click, or a menu opened outside an army: vanilla has already
            // pointed the camera back at the player's party.
            if (_observeEndRequested)
            {
                StopObserving(false, "Stopped observing " + _observedHero.Name + ".");
                return;
            }

            // The player's own capture hands the camera to the captor for good.
            if (Hero.MainHero.IsPrisoner)
            {
                StopObserving(false, null);
                return;
            }

            PartyBase party = ObservedPartyOf(_observedHero);
            if (party == null)
            {
                StopObserving(true, "Stopped observing " + _observedHero.Name + ": no party left to follow.");
                return;
            }

            // Taken prisoner, freed, or given a new party: keep following the lord. And any other camera
            // takeover is undone: the army code points the camera at the army leader when the player's party
            // merges into an army and back at the player when it leaves, and the army's menus at the player.
            if (party != _observedParty || Campaign.Current.CameraFollowParty != party)
            {
                FollowWithCamera(party);
            }
        }

        /// <summary>The two camera takeovers that do end observing; Tick reads the flag on its next run.</summary>
        private static class ObserveEndingPatches
        {
            // Called by MapScreen only when a click on the map gave the player's party a move order.
            [HarmonyPatch(typeof(MapCameraView), nameof(MapCameraView.HandleLeftMouseButtonClick))]
            private static class MapClickPatch
            {
                private static void Prefix(bool isMouseActive)
                {
                    if (IsObserving && isMouseActive)
                        _observeEndRequested = true;
                }
            }

            // In an army the player sits in the army's menus, so there a menu does not end observing.
            [HarmonyPatch(typeof(MapScreen), "EnterMenuContext")]
            private static class MenuPatch
            {
                private static void Prefix()
                {
                    if (IsObserving && !IsInArmy)
                        _observeEndRequested = true;
                }
            }
        }

        /// <summary>
        /// While observing, parties and settlements around the observed lord are spotted as if the player's party
        /// were there too. Runs vanilla's own spotting pass with the observed lord's position added to the
        /// player's (the pass takes several points: it already uses two or three during a siege or a coastal
        /// raid), so everything spotted around the player's own party stays as it was.
        /// </summary>
        [HarmonyPatch(typeof(CampaignTickCacheDataStore), "UpdateVisibilitiesAroundMainParty")]
        private static class ObserveVisibilityPatch
        {
            private static readonly Action<CampaignTickCacheDataStore, Vec2[]> UpdateVisibilitiesBasedOnPoint =
                AccessTools.MethodDelegate<Action<CampaignTickCacheDataStore, Vec2[]>>(
                    AccessTools.Method(typeof(CampaignTickCacheDataStore), "UpdateVisibilitiesBasedOnPoint"));

            private static bool Prefix(CampaignTickCacheDataStore __instance)
            {
                if (!IsObserving || UpdateVisibilitiesBasedOnPoint == null)
                    return true;

                MobileParty main = MobileParty.MainParty;
                if (main == null || !main.Position.IsValid() || !_observedParty.Position.IsValid())
                    return true;

                // The same points vanilla's UpdateVisibilitiesAroundMainParty picks for the player's party.
                List<Vec2> points = new List<Vec2>(4);
                MapEvent mapEvent = main.MapEvent;
                if (main.SiegeEvent != null && main.SiegeEvent.BesiegedSettlement.HasPort)
                {
                    Settlement besieged = main.SiegeEvent.BesiegedSettlement;
                    points.Add(besieged.GatePosition.ToVec2());
                    points.Add(besieged.PortPosition.ToVec2());
                    points.Add(main.SiegeEvent.BesiegerCamp.LeaderParty.Position.ToVec2());
                }
                else if (mapEvent != null && mapEvent.IsRaid && mapEvent.MapEventSettlement.HasPort)
                {
                    points.Add(mapEvent.MapEventSettlement.GatePosition.ToVec2());
                    points.Add(mapEvent.MapEventSettlement.PortPosition.ToVec2());
                }
                else
                {
                    points.Add(main.Position.ToVec2());
                }
                points.Add(_observedParty.Position.ToVec2());

                UpdateVisibilitiesBasedOnPoint(__instance, points.ToArray());
                foreach (BattleWreckage wreckage in Campaign.Current.Wreckages)
                {
                    wreckage.UpdateVisibility();
                }
                return false;
            }
        }

        public static void SwitchToLord(Hero targetHero)
        {
            if (Campaign.Current == null || targetHero == null)
                return;

            // The target has to have a party of his own to hand us. Without one, ChangePlayerCharacterAction
            // falls into its heir-with-no-party branch and mints a fresh player party at a guessed position --
            // never what a "switch to this lord" is asking for.
            if (targetHero.PartyBelongedTo == null || targetHero.IsPrisoner)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    targetHero.Name + " has no field party to switch into.", Colors.Red));
                return;
            }

            StopObserving(false, null);

            Hero oldHero = Hero.MainHero;
            Clan oldClan = Clan.PlayerClan;
            Clan newClan = targetHero.Clan;

            // The two parties in play: the one we are leaving (the current main party) and the one we are taking
            // over (the target lord's). Captured before Apply moves MainParty off the first onto the second.
            MobileParty oldParty = MobileParty.MainParty;
            MobileParty newParty = targetHero.PartyBelongedTo;

            // THE FIX FOR THE PERMANENT STUTTER.
            //
            // ChangePlayerCharacterAction was written for heir SUCCESSION -- the old character is dead, the heir
            // usually had no party -- so it never considers that either party might be in an ARMY. Switching to a
            // LIVING lord routinely does: in wartime most lords are armied. If we let the swap run with the target
            // still attached to an AI army, the player party ends up INSIDE an AI-commanded army, and the army's
            // per-tick logic (cohesion decay, gather-around, disband checks, and the leader's move orders to its
            // members) runs every frame against a party that now answers only to the player -- a conflict that
            // never resolves. That is the sudden, extreme, and unending stutter. Pull both parties clear of any
            // army first: setting Army = null removes a member cleanly, or disbands the army if the party led it.
            DetachFromArmy(newParty);
            DetachFromArmy(oldParty);

            if (newClan != null && newClan != oldClan && PlayerDefaultFactionProp != null)
            {
                PlayerDefaultFactionProp.SetValue(Campaign.Current, newClan);
            }

            ChangePlayerCharacterAction.Apply(targetHero);

            // Repair the party we just left. Apply's succession branch handed it to the NEW hero
            // (LordPartyComponent.ChangePartyOwner(Hero.MainHero), which by that point is already the target) while
            // leaving its Leader as the old hero -- an ownerless-feeling, split-brain party owned by the player's
            // new clan but led by an old-clan hero. Reassigning the old hero as leader routes through
            // LordPartyComponent.OnChangePartyLeader, which sets both _leader AND Owner back to him, turning it
            // into a clean AI lord party for the lord we stopped playing. (ActualClan already tracks the old clan,
            // so MapFaction stays correct.)
            if (oldParty != null && oldParty != MobileParty.MainParty && oldParty.IsActive
                && oldParty.MemberRoster.TotalManCount > 0
                && oldHero != null && oldHero.IsAlive && !oldHero.IsPrisoner
                && oldParty.LordPartyComponent != null && oldParty.LeaderHero != oldHero)
            {
                oldParty.ChangePartyLeader(oldHero);
            }

            // Rebuild the map nameplate for the party we just left.
            //
            // PartyNameplatesVM.OnPlayerCharacterChangedEvent only re-points the single PLAYER
            // nameplate onto the new main party. The party we left was, as the former main party,
            // tracked ONLY by that player nameplate -- it never had an entry in the AI-nameplate pool.
            // After the swap the player nameplate belongs to the new party, so the old party is left
            // with no nameplate at all and its floating map label simply vanishes (the "switched-from
            // party name is wrong/stale" symptom). Re-firing a visibility change for it makes
            // PartyNameplatesVM.UpdateMobilePartyVisibility notice a spotted party with no nameplate on
            // the next tick and build a fresh AI nameplate from its now-correct LeaderHero. This is the
            // same event the engine raises whenever a party is spotted, so replaying it once is safe.
            if (oldParty != null && oldParty != MobileParty.MainParty && oldParty.IsActive
                && oldParty.MemberRoster.TotalManCount > 0)
            {
                CampaignEventDispatcher.Instance.OnPartyVisibilityChanged(oldParty.Party);
            }

            string msg = "Switched from " + oldHero.Name + " (" + (oldClan != null ? oldClan.Name.ToString() : "?") +
                         ") to " + targetHero.Name + " (" + (newClan != null ? newClan.Name.ToString() : "?") + ")";
            InformationManager.DisplayMessage(new InformationMessage(msg, Colors.Green));
        }

        /// <summary>
        /// Take a party out of whatever army it is in, if any. A player party must never sit inside an AI army:
        /// the army ticks its members every frame and would fight the player's control indefinitely. Nulling Army
        /// removes a member cleanly (OnRemovePartyInternal) or, if this party was the army's leader, disbands the
        /// army (DisbandArmyAction.ApplyByLeaderPartyRemoved) -- both acceptable outcomes for a debug switch.
        /// </summary>
        private static void DetachFromArmy(MobileParty party)
        {
            if (party != null && party.Army != null)
            {
                party.Army = null;
            }
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("switch_lord", "campaign")]
        public static string ConsoleSwitchLord(List<string> args)
        {
            if (Campaign.Current == null)
                return "Not in a campaign.";

            if (args == null || args.Count == 0)
                return "Usage: campaign.switch_lord <name>\nSearches for alive lords matching the name.";

            string searchText = string.Join(" ", args);

            List<Hero> matches = Hero.AllAliveHeroes
                .Where(h => h.IsLord && h != Hero.MainHero &&
                       h.Name != null &&
                       h.Name.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(h => h.Name.ToString())
                .ToList();

            if (matches.Count == 0)
                return "No lords found matching '" + searchText + "'.";

            if (matches.Count > 1)
            {
                string list = string.Join("\n",
                    matches.Take(25).Select(h =>
                        "  " + h.Name + " (" + (h.Clan != null ? h.Clan.Name.ToString() : "no clan") + ")"));
                if (matches.Count > 25)
                    list += "\n  ... and " + (matches.Count - 25) + " more";
                return "Multiple matches for '" + searchText + "':\n" + list + "\nRefine your search.";
            }

            SwitchToLord(matches[0]);
            return "Switched to " + matches[0].Name + " (" +
                   (matches[0].Clan != null ? matches[0].Clan.Name.ToString() : "no clan") + ").";
        }
    }
}
