using HarmonyLib;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Overlay;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using StackPower = RBMCampaign.StrategicTroopPower.StackPower;

namespace RBMCampaign
{
    /// <summary>
    /// THE STRENGTH BAR, SHOWING ITS WORKING.
    ///
    /// The encounter overlay puts two numbers on a bar and, when hovered, says "Power Levels" -- which the player
    /// already knew. That was survivable while power was tier and nothing else; a tier-5 man was worth what every
    /// other tier-5 man was worth, and there was nothing to explain. <see cref="StrategicTroopPower"/> makes the
    /// number depend on armour, weapons, shield, training, the ground and the commander, so the bar now moves for
    /// reasons no one can see -- and a model no one can see is a model no one can calibrate or trust.
    ///
    /// So the hover is rewritten with the sides' totals, and then a line per KIND of man on each side: how many of
    /// him stood, what tier the game calls him, what ONE of him is worth, and in brackets what they came to
    /// together. The man leads the row because he is what is being calibrated -- the sum is a consequence of him and
    /// of how many turned up, and reads first as a number about the army rather than about the model. The figures
    /// are the pricing's own -- see <see cref="StrategicTroopPower.TryExplainParty"/> -- rather than a second guess
    /// at it, so the rows add up to the side totals and the average is over exactly the men the total was built from.
    ///
    /// The two sides are drawn as two columns, attackers left and defenders right, which is where the overlay and
    /// the bar itself put them. That is also why this cannot simply be a hint: a hint is one string in one widget in
    /// a proportional font, and columns padded with spaces in a font where no two characters are the same width come
    /// out ragged. So the hover shows <see cref="RBMPowerTooltipVM"/> through RBM's own prefab instead -- see
    /// <see cref="ShowInsteadOfHintPatch"/> for how it is substituted for the native hint.
    ///
    /// The plain hint text is still built and still set, as the fallback: if the prefab or its registration ever
    /// fails, the hover degrades to one narrow column rather than to nothing.
    ///
    /// When RBM did not price the sides, this stays out of the way and vanilla's hint stands: with the strategic
    /// power model off, a breakdown here would be describing arithmetic that never ran.
    /// </summary>
    internal static class StrategicPowerTooltip
    {
        // Nothing is folded away. This is a calibration surface before it is a decoration, and a tooltip that
        // silently dropped the tail would hide exactly the rows worth looking at -- the odd troop, the one lord, the
        // stray garrison levy. A siege can therefore run this off the bottom of the screen; that is the price, and
        // it is the one asked for.

        // The shape of the list lives here rather than in the localised rows: a translator has no business deciding
        // how far a row is indented under its heading, and an XML attribute cannot hold a newline anyway -- the parser
        // normalises it to a space.
        private const string RowIndent = "\n  ";

        [HarmonyPatch(typeof(EncounterMenuOverlayVM), "UpdateLists")]
        internal static class UpdateListsPatch
        {
            private static void Postfix(EncounterMenuOverlayVM __instance)
            {
                try
                {
                    Apply(__instance);
                }
                catch (Exception)
                {
                    // A tooltip is not worth a broken encounter screen. Vanilla's hint is already in place.
                }
            }
        }

        private static void Apply(EncounterMenuOverlayVM overlay)
        {
            if (overlay == null || overlay.PowerComparer == null || !StrategicTroopPower.Enabled)
            {
                return;
            }

            _hint = null;
            _data = null;

            float defenders = SideTotal(overlay.DefenderPartyList);
            float attackers = SideTotal(overlay.AttackerPartyList);

            // Only ever explain a bar that agrees with us. The overlay does not always show the model's answer: when
            // a party on the field is one the player cannot see into, it throws the numbers away and forces the bar to
            // 1/0, and it leaves the bar at 1/1 for a settlement with no siege on it. Breaking those down would be
            // inventing a battle -- and in the hidden case, handing over a roster he has not earned sight of. Rather
            // than restate the overlay's reasons for lying (they are its own, and free to change), we check the one
            // thing that matters: that the figures on the bar are the figures we are about to take apart.
            if (!Agrees(overlay.PowerComparer.DefenderBattlePowerValue, defenders)
                || !Agrees(overlay.PowerComparer.AttackerBattlePowerValue, attackers))
            {
                return;
            }

            // Attackers first, everywhere: on the bar their fill is the left half, and on the overlay their portraits
            // are the left block. A breakdown that named the sides in the other order would be quietly asking the
            // reader to swap them back every time they looked.
            TextObject header = new TextObject("{=RBM_POWER_001}Power: {ATTACKER_TOTAL} attacking  vs  {DEFENDER_TOTAL} defending", null);
            header.SetTextVariable("ATTACKER_TOTAL", Round(attackers));
            header.SetTextVariable("DEFENDER_TOTAL", Round(defenders));

            string attackerColumn = BuildSide(new TextObject("{=RBM_POWER_003}ATTACKERS", null), overlay.AttackerPartyList);
            string defenderColumn = BuildSide(new TextObject("{=RBM_POWER_002}DEFENDERS", null), overlay.DefenderPartyList);

            HintViewModel hint = overlay.PowerComparer.Hint;
            if (hint == null)
            {
                hint = new HintViewModel(TextObject.GetEmpty(), null);
                overlay.PowerComparer.Hint = hint;
            }

            // The fallback, and the handle. The text is swapped INTO the hint the comparer already owns rather than
            // the hint being replaced: the prefab hands the HintWidget its DataSource once
            // (<HintWidget DataSource="{Hint}" ...>) and only reads HintText -- a plain field, not a bound property --
            // when the pointer arrives, so writing the field is enough and does not rest on the binding re-resolving a
            // new object underneath the widget. This string is what the player sees if the custom tooltip is not
            // available; when it is, ShowInsteadOfHintPatch never reads it.
            TextObject flat = new TextObject("{=!}{HEADER}\n\n{ATTACKERS}\n\n{DEFENDERS}", null);
            flat.SetTextVariable("HEADER", header.ToString());
            flat.SetTextVariable("ATTACKERS", attackerColumn);
            flat.SetTextVariable("DEFENDERS", defenderColumn);
            hint.HintText = flat;

            // Which hint is ours, and what it should say. Identity, not content: the hint the comparer owns is the
            // only thing the patch below will have to recognise it by.
            _hint = hint;
            _data = new RBMPowerTooltipData(header.ToString(), attackerColumn, defenderColumn);
        }

        // The one hint on the map that is not a sentence, and the columns it should be drawn with. Only ever touched
        // from the UI thread, on the encounter overlay's own refresh and hover.
        private static HintViewModel _hint;

        private static RBMPowerTooltipData _data;

        /// <summary>
        /// Substitutes RBM's two-column tooltip for the native one-string hint, for our hover and nothing else.
        ///
        /// The prefab wires the bar's HintWidget to call ExecuteBeginHint on whatever HintViewModel the comparer is
        /// holding, and that method is not virtual -- so this is where it can be caught. The test is reference
        /// identity against the exact instance <see cref="Apply"/> wrote to, which is why every other hint in the
        /// game passes straight through untouched.
        /// </summary>
        [HarmonyPatch(typeof(HintViewModel), "ExecuteBeginHint")]
        internal static class ShowInsteadOfHintPatch
        {
            private static bool Prefix(HintViewModel __instance)
            {
                try
                {
                    if (!ReferenceEquals(__instance, _hint) || _data == null || !RBMPowerTooltipVM.IsRegistered)
                    {
                        // Not ours, or ours but with nowhere to draw it: let the native hint run. In the second case
                        // that shows the flat text set above -- narrow, but there.
                        return true;
                    }
                    InformationManager.ShowTooltip(typeof(RBMPowerTooltipData), _data);
                    return false;
                }
                catch (Exception)
                {
                    return true;
                }
            }
        }

        /// <summary>
        /// Whether the figure on the bar is the one we just worked out. Both come from the same calls a moment apart,
        /// so this is an identity test wearing a tolerance -- the slack is only there because comparing floats for
        /// equality across two walks of the same roster is a bet, not a check.
        /// </summary>
        private static bool Agrees(double shown, float ours)
        {
            double slack = Math.Max(0.01, Math.Abs(ours) * 0.001);
            return Math.Abs(shown - ours) <= slack;
        }

        private static float SideTotal(MBBindingList<GameMenuPartyItemVM> list)
        {
            float total = 0f;
            if (list == null)
            {
                return 0f;
            }
            foreach (GameMenuPartyItemVM item in list)
            {
                if (item != null && item.Party != null)
                {
                    total += item.Party.CalculateCurrentStrength();
                }
            }
            return total;
        }

        /// <summary>What one kind of man came to, once every party on the side had been counted.</summary>
        private struct TroopTally
        {
            public int Men;

            public float Total;

            /// <summary>
            /// What one of him is worth. An average, not a constant: the same troop is worth a little more under a
            /// lord whose perks reach him, and less in a party whose morale has broken, so this is the mean over
            /// everyone on the side fielding him. Computed rather than stored so the figure the rows are sorted by
            /// and the figure printed on them cannot drift apart.
            /// </summary>
            public float PerMan
            {
                get { return (Men > 0) ? (Total / Men) : 0f; }
            }
        }

        /// <summary>
        /// One side, as its own column: a heading, then a line per KIND of man on it.
        ///
        /// Tallied across the parties rather than under them, which is the only grouping that says anything -- a
        /// roster already holds one stack per troop, so a per-party breakdown is just the roster read aloud. What
        /// the model has to be judged on is whether a Vlandian Militia is worth what a Vlandian Militia should be,
        /// and that question is about the man, not about which lord he happens to be marching with.
        ///
        /// Never empty -- a side with no parties left still has to hold its column open, or the other side slides
        /// up under the wrong heading.
        /// </summary>
        private static string BuildSide(TextObject header, MBBindingList<GameMenuPartyItemVM> list)
        {
            string body = header.ToString();
            if (list == null || list.Count == 0)
            {
                return body;
            }

            Dictionary<CharacterObject, TroopTally> byTroop = new Dictionary<CharacterObject, TroopTally>();
            List<StackPower> stacks = new List<StackPower>();

            foreach (GameMenuPartyItemVM item in list)
            {
                if (item == null || item.Party == null)
                {
                    continue;
                }
                float ignored;
                if (!StrategicTroopPower.TryExplainParty(item.Party, stacks, out ignored))
                {
                    // Vanilla priced this one, or it is a garrison worth nothing on the map. Either way there is no
                    // working of ours to show for it.
                    continue;
                }
                for (int i = 0; i < stacks.Count; i++)
                {
                    StackPower stack = stacks[i];
                    if (stack.Troop == null)
                    {
                        continue;
                    }
                    TroopTally tally;
                    byTroop.TryGetValue(stack.Troop, out tally);
                    tally.Men += stack.Healthy;
                    tally.Total += stack.Total;
                    byTroop[stack.Troop] = tally;
                }
            }

            List<KeyValuePair<CharacterObject, TroopTally>> ordered =
                new List<KeyValuePair<CharacterObject, TroopTally>>(byTroop);
            // Best man first, not biggest contribution. The row is a claim about what a KIND of man is worth, and
            // ordering by the total would sort by how many of him turned up instead -- burying a handful of elite
            // horse under a mass of levy, which is the comparison the model most needs to be checked on.
            ordered.Sort(delegate (KeyValuePair<CharacterObject, TroopTally> a, KeyValuePair<CharacterObject, TroopTally> b)
            {
                return b.Value.PerMan.CompareTo(a.Value.PerMan);
            });

            for (int i = 0; i < ordered.Count; i++)
            {
                TroopTally tally = ordered[i].Value;

                TextObject row = new TextObject("{=RBM_POWER_004}{COUNT} {TIER} {NAME}: {PER_MAN} ({TOTAL})", null);
                row.SetTextVariable("COUNT", tally.Men);
                row.SetTextVariable("TIER", TierOf(ordered[i].Key));
                row.SetTextVariable("NAME", ordered[i].Key.Name ?? TextObject.GetEmpty());
                row.SetTextVariable("PER_MAN", PerMan(tally.PerMan));
                row.SetTextVariable("TOTAL", Round(tally.Total));
                body += RowIndent + row.ToString();
            }
            return body;
        }

        /// <summary>
        /// What the game calls this man, which is worth showing precisely BECAUSE this model no longer prices him on
        /// it: vanilla's whole answer was his tier, and the row invites the comparison the model has to survive --
        /// two T5s worth wildly different amounts is either the point or a bug, and there is no telling which
        /// without the tier on the line.
        ///
        /// A hero has no tier -- CharacterObject.Tier reads 0 for him -- but the game still prices him on one taken
        /// from his level, so that is what is shown rather than a 0 that would make every lord look like a peasant.
        /// Derived the same way as <c>SimulationEquipmentPower.VanillaTierPower</c> and the battle log.
        /// </summary>
        private static string TierOf(CharacterObject troop)
        {
            if (troop == null)
            {
                return string.Empty;
            }
            int tier = (troop.IsHero && troop.HeroObject != null)
                ? ((troop.HeroObject.Level / 4) + 1)
                : troop.Tier;
            return "T" + tier;
        }

        /// <summary>
        /// Always one decimal. This is the number the row is now built around, and it is the number the whole model
        /// is judged by -- the difference between 4.2 and 4.8 is the entire argument -- so it does not get rounded
        /// away just because a lord happens to be worth more than ten.
        /// </summary>
        private static string PerMan(float value)
        {
            return value.ToString("0.0");
        }

        /// <summary>
        /// One decimal below ten, none above. Sums run to the hundreds, where a decimal is noise on the row; but a
        /// side of three looters is single digits, and there the decimal is the only thing distinguishing them.
        /// </summary>
        private static string Round(float value)
        {
            if (value < 10f)
            {
                return value.ToString("0.0");
            }
            return TaleWorlds.Library.MathF.Round(value).ToString("0");
        }
    }
}
