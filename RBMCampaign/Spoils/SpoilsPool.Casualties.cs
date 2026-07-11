using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
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
        /// Only the winner is processed. The victors hold the field, so their roster is settled -- the
        /// dead struck off, the wounded still counted, and none of them routed -- which is what lets the
        /// stack size before the battle be recovered as survivors + dead. A beaten party's men scatter as
        /// often as they fall, and telling the two apart after the fact is not worth the guess.
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
        }

        private static void ApplyPartyCasualties(PartyBase party, TroopRoster died)
        {
            if (party == null || died == null || party.MemberRoster == null)
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
                if (purse <= 0)
                {
                    continue;
                }
                int survivors = GetStackSize(party, character);
                if (survivors <= 0)
                {
                    // The whole stack is gone: recover what the comrades can and split it, lose the rest.
                    DistributeFallenPurse(party, character, purse);
                }
                else
                {
                    // Some fell, some held: the fallen carried their per-man share to the grave.
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
            }
        }

        /// <summary>
        /// A wiped stack's purse, minus what a battlefield swallows, handed to the surviving stacks by
        /// head count so every man left standing takes an even cut. With no survivors to take it -- the
        /// whole party fell -- there is no one to carry it and all of it is lost, which the party's own
        /// destruction would have pruned anyway.
        /// </summary>
        private static void DistributeFallenPurse(PartyBase party, CharacterObject fallen, int purse)
        {
            // The fallen stack is already gone from the roster, so this clears its key outright.
            AddSpoils(party, fallen, -purse);

            int recovered = MathF.Round(purse * FallenPurseRecoveryFraction);
            List<TroopRosterElement> heirs = new List<TroopRosterElement>();
            int totalMen = 0;
            if (recovered > 0)
            {
                TroopRoster roster = party.MemberRoster;
                for (int i = 0; i < roster.Count; i++)
                {
                    TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                    if (!element.Character.IsHero && element.Number > 0)
                    {
                        heirs.Add(element);
                        totalMen += element.Number;
                    }
                }
            }

            if (totalMen <= 0)
            {
                if (SpoilsLog.IsEnabled)
                {
                    SpoilsLog.Log("CASUALTY", party, "the whole stack of " + SpoilsLog.Describe(fallen) + " fell in "
                        + SpoilsLog.Describe(party) + " with no comrades to take its purse; all " + purse + " lost");
                }
                return;
            }

            // Floor each stack's cut, then hand the rounding remainder to the largest so the whole of the
            // recovered sum reaches the men and only the intended share is lost.
            int distributed = 0;
            int largest = 0;
            for (int i = 0; i < heirs.Count; i++)
            {
                int share = (int)((long)recovered * heirs[i].Number / totalMen);
                AddSpoils(party, heirs[i].Character, share);
                distributed += share;
                if (heirs[i].Number > heirs[largest].Number)
                {
                    largest = i;
                }
            }
            int remainder = recovered - distributed;
            if (remainder > 0)
            {
                AddSpoils(party, heirs[largest].Character, remainder);
            }

            if (SpoilsLog.IsEnabled)
            {
                SpoilsLog.Log("CASUALTY", party, "the whole stack of " + SpoilsLog.Describe(fallen) + " fell in "
                    + SpoilsLog.Describe(party) + ": purse " + purse + ", comrades split " + recovered
                    + " among " + totalMen + " men, " + (purse - recovered) + " lost");
            }
        }
    }
}
