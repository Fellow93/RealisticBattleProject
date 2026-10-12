using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade.ViewModelCollection.Inquiries;

namespace RBMCampaign
{
    /// <summary>
    /// The popup setup <c>campaign.rbm_test_battle</c> opens when run without parameters, built from the
    /// vanilla inquiry popups like the Switch Lord picker. Default (every troop type on both sides, the
    /// player attacking) asks only for the men per troop type; Custom first picks the attacking side's
    /// groups, the defending side's groups and the player's side, and ends with an offer to save the picks
    /// as a preset. Last used / Load preset start from saved options (TestBattleCommand.Presets.cs) and
    /// go straight to the men per troop type; Delete presets removes preset files. Every step has Back.
    /// </summary>
    /// <remarks>
    /// Each step opens on the next map tick (<see cref="TickSetup"/>), never from inside the previous
    /// popup's callback: the popup VM runs the callback before it closes itself, and the battle start
    /// refuses to run while a menu or screen is still up.
    ///
    /// A single-choice step goes on as soon as an option is clicked, with no OK: the vanilla popup has no
    /// such mode, so <see cref="AutoConfirmSingleChoicePatch"/> notes the click on this setup's own
    /// single-choice popup and <see cref="TickSetup"/> presses OK for it on the next tick. Every other
    /// popup in the game is untouched.
    /// </remarks>
    public static partial class TestBattleCommand
    {
        private const string SetupTitle = "RBM Test Battle";

        private static Action _pendingSetupStep;

        // The setup's single-choice popup now showing, and that popup's VM once an option in it is clicked.
        private static MultiSelectionInquiryData _autoConfirmData;
        private static MultiSelectionQueryPopUpVM _autoConfirmPopUp;

        private static readonly AccessTools.FieldRef<MultiSelectionQueryPopUpVM, MultiSelectionInquiryData> PopUpData =
            AccessTools.FieldRefAccess<MultiSelectionQueryPopUpVM, MultiSelectionInquiryData>("_data");

        [HarmonyPatch(typeof(MultiSelectionQueryPopUpVM), "OnInquiryElementSelected")]
        private static class AutoConfirmSingleChoicePatch
        {
            private static void Postfix(MultiSelectionQueryPopUpVM __instance, MultiSelectionInquiryData ____data,
                bool isSelected)
            {
                if (isSelected && ____data != null && ____data == _autoConfirmData)
                {
                    _autoConfirmPopUp = __instance;
                }
            }
        }

        private sealed class SetupChoice
        {
            // True only while going through the Custom picks: the count step then leads to the save step.
            internal bool IsCustom;
            // Preset name and file, for a choice read from or saved to a preset file.
            internal string Name;
            internal string FilePath;
            internal int CountPerTroop = DefaultCountPerTroop;
            internal string AttackerGroups = AllGroup;
            internal string DefenderGroups = AllGroup;
            internal bool PlayerIsAttacker = true;
        }

        private static void OpenSetup()
        {
            QueueSetupStep(() => ShowModeStep(new SetupChoice()));
        }

        private static void QueueSetupStep(Action step)
        {
            _pendingSetupStep = step;
        }

        /// <summary>Opens the queued setup step, if any. Called from <see cref="Tick"/>.</summary>
        private static void TickSetup()
        {
            if (_autoConfirmPopUp != null)
            {
                MultiSelectionQueryPopUpVM popUp = _autoConfirmPopUp;
                _autoConfirmPopUp = null;
                // Only if it still shows that step: a closed popup has its data cleared.
                if (_autoConfirmData != null && PopUpData(popUp) == _autoConfirmData)
                {
                    _autoConfirmData = null;
                    popUp.ExecuteAffirmativeAction();
                }
            }
            if (_pendingSetupStep == null)
            {
                return;
            }
            Action step = _pendingSetupStep;
            _pendingSetupStep = null;
            step();
        }

        private static void ShowModeStep(SetupChoice choice)
        {
            List<InquiryElement> elements = new List<InquiryElement>
            {
                new InquiryElement("default", "Default", null, true,
                    "Both sides get every troop type: every culture's regular troops, every tavern mercenary and "
                    + "every minor-faction troop. You attack."),
                new InquiryElement("custom", "Custom", null, true,
                    "Pick the troops of each side (cultures, mercenaries, minor factions) and the side you fight on."),
            };
            SetupChoice lastUsed = ReadPreset(LastUsedPresetPath);
            if (lastUsed != null)
            {
                elements.Add(new InquiryElement("last", "Last used", null, true, DescribeChoice(lastUsed)));
            }
            List<SetupChoice> presets = ReadPresets();
            if (presets.Count > 0)
            {
                elements.Add(new InquiryElement("load", "Load preset (" + presets.Count + ")", null, true,
                    string.Join("\n", presets.Select(p => p.Name))));
                elements.Add(new InquiryElement("delete", "Delete presets", null, true,
                    "Presets are files in " + PresetFolderPath));
            }

            ShowSetupSelection("Choose the battle setup:", elements, 1, "Next", "Cancel",
                selected =>
                {
                    switch ((string)selected[0].Identifier)
                    {
                        case "custom":
                            choice.IsCustom = true;
                            QueueSetupStep(() => ShowGroupStep(choice, true));
                            break;
                        case "last":
                            QueueSetupStep(() => ShowCountStep(lastUsed, () => ShowModeStep(choice)));
                            break;
                        case "load":
                            QueueSetupStep(() => ShowLoadPresetStep(choice));
                            break;
                        case "delete":
                            QueueSetupStep(() => ShowDeletePresetStep(choice));
                            break;
                        default:
                            choice.IsCustom = false;
                            choice.AttackerGroups = AllGroup;
                            choice.DefenderGroups = AllGroup;
                            choice.PlayerIsAttacker = true;
                            QueueSetupStep(() => ShowCountStep(choice, () => ShowModeStep(choice)));
                            break;
                    }
                },
                null);
        }

        private static void ShowLoadPresetStep(SetupChoice modeChoice)
        {
            List<InquiryElement> elements = ReadPresets()
                .Select(p => new InquiryElement(p, p.Name, null, true, DescribeChoice(p)))
                .ToList();
            if (elements.Count == 0)
            {
                ShowModeStep(modeChoice);
                return;
            }
            ShowSetupSelection("Saved presets (hover for the options):", elements, 1, "Next", "Back",
                selected =>
                {
                    SetupChoice preset = (SetupChoice)selected[0].Identifier;
                    QueueSetupStep(() => ShowCountStep(preset, () => ShowLoadPresetStep(modeChoice)));
                },
                _ => QueueSetupStep(() => ShowModeStep(modeChoice)));
        }

        private static void ShowDeletePresetStep(SetupChoice modeChoice)
        {
            List<InquiryElement> elements = ReadPresets()
                .Select(p => new InquiryElement(p, p.Name, null, true, DescribeChoice(p)))
                .ToList();
            if (elements.Count == 0)
            {
                ShowModeStep(modeChoice);
                return;
            }
            ShowSetupSelection("Presets to delete:", elements, elements.Count, "Delete", "Back",
                selected =>
                {
                    foreach (InquiryElement element in selected)
                    {
                        DeletePreset((SetupChoice)element.Identifier);
                    }
                    QueueSetupStep(() => ShowModeStep(modeChoice));
                },
                _ => QueueSetupStep(() => ShowModeStep(modeChoice)));
        }

        private static void ShowGroupStep(SetupChoice choice, bool attackerSide)
        {
            Dictionary<CharacterObject, string> sourceOf;
            Dictionary<CharacterObject, string> groupOf;
            List<CharacterObject> troops = CollectTestTroops(out sourceOf, out groupOf);
            HashSet<string> cultureIds = new HashSet<string>(GetMainCultures().Select(c => c.StringId),
                StringComparer.OrdinalIgnoreCase);

            // Groups in collection order: cultures, mercenaries, minor factions.
            List<string> groups = new List<string>();
            Dictionary<string, List<CharacterObject>> troopsOf = new Dictionary<string, List<CharacterObject>>();
            foreach (CharacterObject troop in troops)
            {
                string group = groupOf[troop];
                List<CharacterObject> list;
                if (!troopsOf.TryGetValue(group, out list))
                {
                    list = new List<CharacterObject>();
                    troopsOf[group] = list;
                    groups.Add(group);
                }
                list.Add(troop);
            }

            List<InquiryElement> elements = new List<InquiryElement>
            {
                new InquiryElement(AllGroup, "All troop types (" + troops.Count + ")", null, true,
                    "Every culture's regular troops, every tavern mercenary and every minor-faction troop."),
            };
            bool minorEntryAdded = false;
            foreach (string group in groups)
            {
                bool minor = IsMinorGroup(group, cultureIds);
                if (minor && !minorEntryAdded)
                {
                    int minorCount = troops.Count(t => IsMinorGroup(groupOf[t], cultureIds));
                    elements.Add(new InquiryElement(MinorGroup, "All minor factions (" + minorCount + ")", null, true,
                        "Every minor-faction troop."));
                    minorEntryAdded = true;
                }
                List<CharacterObject> list = troopsOf[group];
                elements.Add(new InquiryElement(group, sourceOf[list[0]] + " (" + list.Count + ")", null, true,
                    string.Join(", ", list.Select(t => t.Name + " (T" + t.Tier + ")"))));
            }

            string side = attackerSide ? "attacking" : "defending";
            ShowSetupSelection("Troops of the " + side + " side. Pick one or more; hover for the troop list.",
                elements, elements.Count, "Next", "Back",
                selected =>
                {
                    string groupList = string.Join(",", selected.Select(e => (string)e.Identifier));
                    if (attackerSide)
                    {
                        choice.AttackerGroups = groupList;
                        QueueSetupStep(() => ShowGroupStep(choice, false));
                    }
                    else
                    {
                        choice.DefenderGroups = groupList;
                        QueueSetupStep(() => ShowSideStep(choice));
                    }
                },
                _ =>
                {
                    if (attackerSide)
                    {
                        QueueSetupStep(() => ShowModeStep(choice));
                    }
                    else
                    {
                        QueueSetupStep(() => ShowGroupStep(choice, true));
                    }
                });
        }

        private static void ShowSideStep(SetupChoice choice)
        {
            List<InquiryElement> elements = new List<InquiryElement>
            {
                new InquiryElement(true, "Attacker", null, true, "You attack the defending side."),
                new InquiryElement(false, "Defender", null, true,
                    "You are attacked. As in vanilla, the defender gets no free Leave: attack, send troops, try to "
                    + "get away or surrender."),
            };
            ShowSetupSelection("Your side:", elements, 1, "Next", "Back",
                selected =>
                {
                    choice.PlayerIsAttacker = (bool)selected[0].Identifier;
                    QueueSetupStep(() => ShowCountStep(choice, () => ShowSideStep(choice)));
                },
                _ => QueueSetupStep(() => ShowGroupStep(choice, false)));
        }

        /// <summary>
        /// Men per troop type. Start runs the battle, except in the Custom flow, where it leads to the step
        /// that offers to save the picks as a preset. <paramref name="back"/> is the step Back returns to.
        /// </summary>
        private static void ShowCountStep(SetupChoice choice, Action back)
        {
            string text = (choice.Name != null ? "Preset: " + choice.Name + ". " : "")
                + "Men per troop type (1-" + MaxCountPerTroop + ")";
            Dictionary<CharacterObject, string> sourceOf;
            Dictionary<CharacterObject, string> groupOf;
            List<CharacterObject> troops = CollectTestTroops(out sourceOf, out groupOf);
            string filterError;
            List<CharacterObject> attackerTroops = FilterTroops(troops, groupOf, choice.AttackerGroups, out filterError);
            List<CharacterObject> defenderTroops = FilterTroops(troops, groupOf, choice.DefenderGroups, out filterError);
            if (attackerTroops != null && defenderTroops != null)
            {
                text += ". Troop types: attacker " + attackerTroops.Count + ", defender " + defenderTroops.Count
                    + ". You are the " + (choice.PlayerIsAttacker ? "attacker" : "defender") + ".";
            }

            InformationManager.ShowTextInquiry(new TextInquiryData(SetupTitle, text, true, true,
                choice.IsCustom ? "Next" : "Start", "Back",
                input =>
                {
                    int count;
                    if (int.TryParse(input.Trim(), out count))
                    {
                        choice.CountPerTroop = Math.Max(1, Math.Min(count, MaxCountPerTroop));
                    }
                    if (choice.IsCustom)
                    {
                        QueueSetupStep(() => ShowFinishStep(choice, back));
                    }
                    else
                    {
                        QueueSetupStep(() => StartFromSetup(choice));
                    }
                },
                () => QueueSetupStep(back),
                false, ValidateCountInput, "", choice.CountPerTroop.ToString()), true);
        }

        /// <summary>Last Custom step: start, or save the picks as a preset first.</summary>
        private static void ShowFinishStep(SetupChoice choice, Action countBack)
        {
            string description = DescribeChoice(choice);
            List<InquiryElement> elements = new List<InquiryElement>
            {
                new InquiryElement(false, "Start the battle", null, true, description),
                new InquiryElement(true, "Save as preset, then start", null, true,
                    description + "\nSaved to " + PresetFolderPath),
            };
            ShowSetupSelection(description, elements, 1, "OK", "Back",
                selected =>
                {
                    if ((bool)selected[0].Identifier)
                    {
                        QueueSetupStep(() => ShowSavePresetStep(choice, countBack));
                    }
                    else
                    {
                        QueueSetupStep(() => StartFromSetup(choice));
                    }
                },
                _ => QueueSetupStep(() => ShowCountStep(choice, countBack)));
        }

        private static void ShowSavePresetStep(SetupChoice choice, Action countBack)
        {
            string defaultName = choice.Name ?? SuggestPresetName(choice);
            InformationManager.ShowTextInquiry(new TextInquiryData(SetupTitle,
                "Preset name (a preset with the same name is replaced):", true, true, "Save and start", "Back",
                input =>
                {
                    choice.Name = input.Trim();
                    if (!SavePreset(choice))
                    {
                        QueueSetupStep(() => InformationManager.ShowInquiry(new InquiryData(SetupTitle,
                            "The preset could not be saved to " + PresetFolderPath
                            + " (see the game log). The battle starts without it.",
                            true, false, "OK", null, () => QueueSetupStep(() => StartFromSetup(choice)), null), true));
                        return;
                    }
                    QueueSetupStep(() => StartFromSetup(choice));
                },
                () => QueueSetupStep(() => ShowFinishStep(choice, countBack)),
                false, ValidatePresetName, "", defaultName), true);
        }

        private static Tuple<bool, string> ValidateCountInput(string input)
        {
            int count;
            if (input == null || !int.TryParse(input.Trim(), out count) || count < 1 || count > MaxCountPerTroop)
            {
                return new Tuple<bool, string>(false, "Enter a whole number from 1 to " + MaxCountPerTroop + ".");
            }
            return new Tuple<bool, string>(true, null);
        }

        private static void StartFromSetup(SetupChoice choice)
        {
            bool started;
            string result = StartTestBattle(choice.CountPerTroop, choice.AttackerGroups, choice.DefenderGroups,
                choice.PlayerIsAttacker, out started);
            if (started)
            {
                // The encounter menu is the confirmation; the roster summary goes to the game log.
                Debug.Print("[RBM test battle] " + result);
                return;
            }
            InformationManager.ShowInquiry(new InquiryData(SetupTitle, "The test battle could not start:\n" + result,
                true, false, "OK", null, null, null), true);
        }

        private static void ShowSetupSelection(string description, List<InquiryElement> elements, int maxSelected,
            string affirmativeText, string negativeText, Action<List<InquiryElement>> onAffirmative,
            Action<List<InquiryElement>> onNegative)
        {
            MultiSelectionInquiryData data = new MultiSelectionInquiryData(SetupTitle, description,
                elements, true, 1, maxSelected, affirmativeText, negativeText, onAffirmative, onNegative, "",
                elements.Count > 8);
            // Single choice: clicking an option is the confirmation (see AutoConfirmSingleChoicePatch).
            _autoConfirmData = maxSelected == 1 ? data : null;
            MBInformationManager.ShowMultiSelectionInquiry(data, true);
        }
    }
}
