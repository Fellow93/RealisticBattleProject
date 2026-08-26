using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// What a battle takes out of the purse rather than puts in. A man carries his share of his stack's
    /// spoils on him, so when he falls it falls with him: the dead men's part of the purse is lost. When
    /// a whole stack is wiped its comrades recover what they can carry off its bodies and split it among
    /// themselves, but a battlefield is no place to count coin and much of it is lost in the taking.
    /// </summary>
    public static partial class SpoilsPool
    {
        /// <summary>
        /// The share of a wholly wiped stack's purse its comrades carry off and keep; the rest is lost.
        /// A whole-stack death would otherwise strand its purse entirely, so this hands most of it back
        /// to the men who held the field. Kept alongside the loot salvage fractions it echoes.
        /// </summary>
        private const float FallenPurseRecoveryFraction = 0.5f;

        /// <summary>
        /// Only the winner has its purses settled. The victors hold the field, so their roster is settled
        /// -- the dead struck off, the wounded still counted, and none of them routed -- which is what lets
        /// the stack size before the battle be recovered as survivors + dead. A beaten party's men scatter
        /// as often as they fall, and telling the two apart after the fact is not worth the guess, so the
        /// losing side's fallen are logged for the record but its purses are left untouched (a wiped
        /// party's pool is pruned on its destruction in any case).
        /// </summary>
        public static void ApplyBattleCasualties(MapEventSide winner)
        {
            if (!IsEnabled || winner == null)
            {
                return;
            }
            foreach (MapEventParty mep in winner.Parties)
            {
                ApplyPartyCasualties(mep.Party, mep.DiedInBattle);
            }
            if (winner.OtherSide != null)
            {
                CaptureFallenEnemySpoils(winner);
            }
        }

        /// <summary>
        /// The beaten side's killed and wounded drop their share of their stacks' purses onto the field.
        /// A configured share of it the victors carry off, the rest is trampled and lost; routed men keep
        /// theirs and flee with it, so they are counted in the stack but never stripped. With capture
        /// switched off the fallen are only logged, their purses left where they lie.
        /// </summary>
        private static void CaptureFallenEnemySpoils(MapEventSide winner)
        {
            float captureFraction = MathF.Clamp(RBMConfig.RBMConfig.troopFallenSpoilsCaptureFraction, 0f, 1f);
            if (captureFraction <= 0f)
            {
                foreach (MapEventParty mep in winner.OtherSide.Parties)
                {
                    LogFallenOnly(mep.Party, mep.DiedInBattle);
                }
                return;
            }

            int pot = 0;
            foreach (MapEventParty mep in winner.OtherSide.Parties)
            {
                pot += CaptureFallenSpoilsFromParty(mep.Party, mep.DiedInBattle, mep.WoundedInBattle, mep.RoutedInBattle);
            }
            if (pot <= 0)
            {
                return;
            }

            int toVictors = MathF.Round(pot * captureFraction);
            if (SpoilsLog.IsEnabled)
            {
                SpoilsLog.Log("CAPTURE", "fallen enemy spoils: " + pot + " off the field, "
                    + toVictors + " to the victors, " + (pot - toVictors) + " lost");
            }
            DistributeCapturedToWinners(winner, toVictors);
        }

        /// <summary>
        /// Strips the killed-and-wounded share of each of a beaten party's stack purses. A man's share is
        /// even, so the fallen carry off <c>(killed + wounded) / (survivors + killed + routed)</c> of the
        /// stack's purse -- the pre-battle head count in the denominator, so routed men dilute but keep
        /// their own. Returns the total drawn off, for the victors to split.
        /// </summary>
        private static int CaptureFallenSpoilsFromParty(PartyBase party, TroopRoster died, TroopRoster wounded, TroopRoster routed)
        {
            if (party == null || party.MemberRoster == null)
            {
                return 0;
            }
            Dictionary<CharacterObject, int> killedByChar = CountByCharacter(died);
            Dictionary<CharacterObject, int> woundedByChar = CountByCharacter(wounded);
            Dictionary<CharacterObject, int> routedByChar = CountByCharacter(routed);

            HashSet<CharacterObject> affected = new HashSet<CharacterObject>(killedByChar.Keys);
            foreach (CharacterObject c in woundedByChar.Keys)
            {
                affected.Add(c);
            }

            int captured = 0;
            foreach (CharacterObject character in affected)
            {
                if (character.IsHero)
                {
                    continue;
                }
                int purse = GetSpoils(party, character);
                if (purse <= 0)
                {
                    continue;
                }
                int killed;
                killedByChar.TryGetValue(character, out killed);
                int woundedCount;
                woundedByChar.TryGetValue(character, out woundedCount);
                int routedCount;
                routedByChar.TryGetValue(character, out routedCount);

                int fallenMen = killed + woundedCount;
                if (fallenMen <= 0)
                {
                    continue;
                }
                // Survivors still stand in the roster (wounded among them); the dead and routed have left
                // it, so adding them back recovers the head count the purse was shared across.
                int preBattle = GetStackSize(party, character) + killed + routedCount;
                if (preBattle <= 0)
                {
                    continue;
                }
                int share = (int)((long)purse * MathF.Min(fallenMen, preBattle) / preBattle);
                if (share <= 0)
                {
                    continue;
                }
                AddSpoils(party, character, -share);
                captured += share;
                if (SpoilsLog.Verbose)
                {
                    SpoilsLog.LogVerbose("CAPTURE", party, "  " + fallenMen + " fallen/wounded of " + SpoilsLog.Describe(character)
                        + " in " + SpoilsLog.Describe(party) + " dropped " + share + " spoils (pool " + purse + " -> " + (purse - share) + ")");
                }
            }
            if (SpoilsLog.IsEnabled && captured > 0)
            {
                SpoilsLog.Log("CAPTURE", party, SpoilsLog.Describe(party) + " (defeated) lost " + captured + " spoils off its fallen and wounded");
            }
            return captured;
        }

        /// <summary>Total non-hero troops of each character in a roster, in one pass.</summary>
        private static Dictionary<CharacterObject, int> CountByCharacter(TroopRoster roster)
        {
            Dictionary<CharacterObject, int> counts = new Dictionary<CharacterObject, int>();
            if (roster == null)
            {
                return counts;
            }
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character == null || element.Character.IsHero || element.Number <= 0)
                {
                    continue;
                }
                int existing;
                counts.TryGetValue(element.Character, out existing);
                counts[element.Character] = existing + element.Number;
            }
            return counts;
        }

        /// <summary>
        /// Splits the captured pot across the winning parties by their part in the battle, then hands each
        /// party's cut to its stacks by weight. Rounding leftovers fall to the largest, so the whole of
        /// the pot reaches the men.
        /// </summary>
        private static void DistributeCapturedToWinners(MapEventSide winner, int amount)
        {
            if (amount <= 0)
            {
                return;
            }
            long totalContribution = 0L;
            foreach (MapEventParty victor in winner.Parties)
            {
                totalContribution += MathF.Max(0, victor.ContributionToBattle);
            }

            int distributed = 0;
            int topIndex = -1;
            long topWeight = -1L;
            for (int i = 0; i < winner.Parties.Count; i++)
            {
                MapEventParty victor = winner.Parties[i];
                long weight = (totalContribution > 0L) ? MathF.Max(0, victor.ContributionToBattle) : 1L;
                long divisor = (totalContribution > 0L) ? totalContribution : winner.Parties.Count;
                if (weight > topWeight)
                {
                    topWeight = weight;
                    topIndex = i;
                }
                int partyShare = (int)((long)amount * weight / divisor);
                if (partyShare > 0)
                {
                    int took = GrantSpoilsWeightedByTier(victor.Party, partyShare, "CAPTURE");
                    LogCapturedToParty(victor.Party, took);
                    // Count what was actually delivered, not what was allotted: a hero-only victor party
                    // (no non-hero stack to take a cut) grants nothing, and its unallotted share must fall
                    // to the remainder below rather than vanish.
                    distributed += took;
                }
            }
            int remainder = amount - distributed;
            if (remainder > 0 && topIndex >= 0)
            {
                int took = GrantSpoilsWeightedByTier(winner.Parties[topIndex].Party, remainder, "CAPTURE");
                LogCapturedToParty(winner.Parties[topIndex].Party, took);
            }
        }

        /// <summary>The summary line for a party's cut of captured enemy spoils, split by tier weight.</summary>
        private static void LogCapturedToParty(PartyBase party, int took)
        {
            if (SpoilsLog.IsEnabled && took > 0)
            {
                SpoilsLog.Log("CAPTURE", party, SpoilsLog.Describe(party) + " took " + took + " in fallen enemy spoils, split by tier weight");
            }
        }

        /// <summary>
        /// Hands a lump of spoils to a party's stacks by weight -- head count times troop tier -- so a
        /// veteran stack takes a larger cut than a green one of the same size. Rounding leftovers fall to
        /// the heaviest-weighted stack so none of the lump is lost. Verbose per-stack lines log under
        /// <paramref name="logCategory"/>; the caller writes any summary, since the reason a lump is split
        /// differs (enemy spoils captured off the field vs. a wiped comrade stack's purse recovered).
        /// </summary>
        /// <returns>How much was actually distributed -- the whole of <paramref name="amount"/> unless the
        /// party has no non-hero stacks left to take it, in which case nothing is granted.</returns>
        private static int GrantSpoilsWeightedByTier(PartyBase party, int amount, string logCategory)
        {
            return GrantSpoilsWeightedByTier(party, amount, logCategory, out int _);
        }

        private static int GrantSpoilsWeightedByTier(PartyBase party, int amount, string logCategory, out int companionGold)
        {
            companionGold = 0;
            TroopRoster roster = party?.MemberRoster;
            if (roster == null || amount <= 0)
            {
                return 0;
            }
            Hero payee = GetPartyPayee(party);
            long totalWeight = 0L;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Number > 0 && (!element.Character.IsHero || IsCompanionStack(element.Character, payee)))
                {
                    totalWeight += (long)element.Number * MathF.Max(1, element.Character.Tier);
                }
            }
            if (totalWeight <= 0L)
            {
                return 0;
            }

            int distributed = 0;
            int topIndex = -1;
            long topWeight = -1L;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Number <= 0 || (element.Character.IsHero && !IsCompanionStack(element.Character, payee)))
                {
                    continue;
                }
                bool isCompanion = IsCompanionStack(element.Character, payee);
                long weight = (long)element.Number * MathF.Max(1, element.Character.Tier);
                // Only a troop (non-companion) stack can be the rounding-remainder mop-up target, since the
                // remainder goes to a purse; a companion holds none.
                if (!isCompanion && weight > topWeight)
                {
                    topWeight = weight;
                    topIndex = i;
                }
                int share = (int)((long)amount * weight / totalWeight);
                if (share <= 0)
                {
                    continue;
                }
                if (isCompanion)
                {
                    companionGold += share;
                    if (SpoilsLog.Verbose)
                    {
                        SpoilsLog.LogVerbose(logCategory, party, "  " + SpoilsLog.Describe(element.Character) + " x" + element.Number
                            + " (weight " + weight + ", companion): +" + share + " gold to the party");
                    }
                }
                else
                {
                    AddSpoils(party, element.Character, share);
                    if (SpoilsLog.Verbose)
                    {
                        int after = GetSpoils(party, element.Character);
                        SpoilsLog.LogVerbose(logCategory, party, "  " + SpoilsLog.Describe(element.Character) + " x" + element.Number
                            + " (weight " + weight + "): +" + share + " (pool " + (after - share) + " -> " + after + ")");
                    }
                }
                distributed += share;
            }
            int remainder = amount - distributed;
            if (remainder > 0)
            {
                if (topIndex >= 0)
                {
                    AddSpoils(party, roster.GetElementCopyAtIndex(topIndex).Character, remainder);
                    distributed += remainder;
                }
                else if (payee != null)
                {
                    companionGold += remainder;
                    distributed += remainder;
                }
            }

            if (companionGold > 0 && payee != null && payee.IsAlive)
            {
                GiveGoldAction.ApplyBetweenCharacters(null, payee, companionGold, true);
                if (SpoilsLog.IsEnabled)
                {
                    SpoilsLog.Log(logCategory, party, SpoilsLog.Describe(party) + " companions claimed " + companionGold
                        + " gold in spoils, paid to " + payee.Name);
                }
            }
            return distributed;
        }

        /// <summary>
        /// Records a losing party's fallen without touching its spoils, so both sides of a battle show in
        /// the log. Mirrors the winner's per-stack casualty lines but changes nothing -- see the note on
        /// <see cref="ApplyBattleCasualties"/> for why the loser's purses are left as they are.
        /// </summary>
        private static void LogFallenOnly(PartyBase party, TroopRoster died)
        {
            if (!SpoilsLog.IsEnabled || party == null || died == null)
            {
                return;
            }
            for (int i = 0; i < died.Count; i++)
            {
                TroopRosterElement element = died.GetElementCopyAtIndex(i);
                CharacterObject character = element.Character;
                if (character.IsHero || element.Number <= 0)
                {
                    continue;
                }
                int purse = GetSpoils(party, character);
                SpoilsLog.Log("CASUALTY", party, element.Number + " of " + SpoilsLog.Describe(character)
                    + " fell on the losing side in " + SpoilsLog.Describe(party)
                    + (purse > 0 ? ": held " + purse + " spoils, left untouched" : ": no purse"));
            }
        }

        private static void ApplyPartyCasualties(PartyBase party, TroopRoster died)
        {
            if (party == null || died == null || party.MemberRoster == null)
            {
                return;
            }
            // Two passes so the order stacks appear in the died roster cannot change the outcome. A wiped
            // stack redistributes its recovered purse to the survivors, so if that ran before a surviving
            // stack's own partial-casualty loss, that loss would be computed off a purse already inflated
            // by the inheritance and a slice of the inherited spoils would be destroyed twice. Take every
            // per-man loss from the stacks that held first, then redistribute the wiped stacks' purses.
            for (int i = 0; i < died.Count; i++)
            {
                TroopRosterElement element = died.GetElementCopyAtIndex(i);
                CharacterObject character = element.Character;
                if (character.IsHero || element.Number <= 0)
                {
                    continue;
                }
                int purse = GetSpoils(party, character);
                if (purse <= 0 || GetStackSize(party, character) <= 0)
                {
                    continue;
                }
                // Some fell, some held: the fallen carried their per-man share to the grave.
                int survivors = GetStackSize(party, character);
                int lost = (int)((long)purse * element.Number / (survivors + element.Number));
                if (lost > 0)
                {
                    AddSpoils(party, character, -lost);
                    if (SpoilsLog.IsEnabled)
                    {
                        SpoilsLog.Log("CASUALTY", party, element.Number + " of " + SpoilsLog.Describe(character)
                            + " x" + (survivors + element.Number) + " fell in " + SpoilsLog.Describe(party)
                            + ": lost their share " + lost + " (pool " + purse + " -> " + (purse - lost) + ")");
                    }
                }
            }
            for (int i = 0; i < died.Count; i++)
            {
                TroopRosterElement element = died.GetElementCopyAtIndex(i);
                CharacterObject character = element.Character;
                if (character.IsHero || element.Number <= 0)
                {
                    continue;
                }
                int purse = GetSpoils(party, character);
                if (purse <= 0 || GetStackSize(party, character) > 0)
                {
                    continue;
                }
                // The whole stack is gone: recover what the comrades can and split it, lose the rest.
                DistributeFallenPurse(party, character, purse);
            }
        }

        /// <summary>
        /// A wiped stack's purse, minus what a battlefield swallows, handed to the surviving stacks by
        /// tier weight -- head count times troop tier -- the same split the fallen enemy's captured spoils
        /// take, so a veteran comrade inherits a larger cut than a green one of the same size. With no
        /// survivors to take it -- the whole party fell -- there is no one to carry it and all of it is
        /// lost, which the party's own destruction would have pruned anyway.
        /// </summary>
        private static void DistributeFallenPurse(PartyBase party, CharacterObject fallen, int purse)
        {
            // The fallen stack is already gone from the roster, so this clears its key outright.
            AddSpoils(party, fallen, -purse);

            int recovered = MathF.Round(purse * FallenPurseRecoveryFraction);
            int distributed = (recovered > 0) ? GrantSpoilsWeightedByTier(party, recovered, "CASUALTY") : 0;

            if (SpoilsLog.IsEnabled)
            {
                if (distributed > 0)
                {
                    SpoilsLog.Log("CASUALTY", party, "the whole stack of " + SpoilsLog.Describe(fallen) + " fell in "
                        + SpoilsLog.Describe(party) + ": purse " + purse + ", comrades split " + distributed
                        + " by tier weight, " + (purse - distributed) + " lost");
                }
                else
                {
                    SpoilsLog.Log("CASUALTY", party, "the whole stack of " + SpoilsLog.Describe(fallen) + " fell in "
                        + SpoilsLog.Describe(party) + " with no comrades to take its purse; all " + purse + " lost");
                }
            }
        }
    }
}
