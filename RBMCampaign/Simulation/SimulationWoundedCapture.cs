using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// A beaten side's WOUNDED are always taken -- even when it fled the field.
    ///
    /// Vanilla's <see cref="MapEvent.CaptureDefeatedPartyMembers"/> hands each defeated party's leftover wounded to the
    /// winner, but with two gaps this fills:
    ///
    ///   1. It hard-returns the instant a side is flagged RETREATING (RetreatingSide != None). So a lord who breaks off
    ///      and runs carries his wounded away with him and leaves NOTHING behind -- no prisoners at all. A man too hurt
    ///      to stand should not be able to keep pace with a fleeing column; he is left where he fell, and taken.
    ///   2. Even when it does run, capture is chance-gated while REMOVAL is not: a wounded man whose draw finds no
    ///      willing captor (an empty chance list, a null draw) is struck off the roster all the same and captured by
    ///      nobody -- he simply vanishes. Pre-battle and during-battle wounded are the same WoundedNumber to that loop,
    ///      so both leak through it identically.
    ///
    /// This prefix closes both: before vanilla runs, it moves every non-hero wounded man of each defeated NPC party
    /// into a winning party's prison roster -- guaranteed, and regardless of whether the side is retreating -- then
    /// strikes exactly those men from the beaten roster. Vanilla then proceeds on what is left: it finds no wounded to
    /// (chance-)capture, so there is no double-take, and it still shares out the healthy remnants and the heroes exactly
    /// as before. Heroes are left entirely to vanilla -- a fleeing lord still gets away, he just cannot carry his
    /// wounded rank-and-file out with him.
    ///
    /// The player's OWN main party is left untouched, so a player who retreats keeps his wounded exactly as vanilla
    /// allows; this only ever strips an NPC (enemy or player-clan) party. Stands down with the auto-resolve overhaul
    /// (SimulationEquipmentPower.SimulationEnabled): if RBM is not deciding the battle, it does not touch the spoils.
    /// </summary>
    [HarmonyPatch(typeof(MapEvent), "CaptureDefeatedPartyMembers")]
    internal static class SimulationWoundedCapture
    {
        private static void Prefix(
            MBReadOnlyList<MapEventParty> winnerParties,
            MBReadOnlyList<MapEventParty> defeatedParties)
        {
            if (!SimulationEquipmentPower.SimulationEnabled || winnerParties == null || defeatedParties == null)
            {
                return;
            }

            try
            {
                // The winners fit to hold prisoners -- the same set vanilla's capture-chance model draws from: a real
                // fighting party that took part, never a village's own militia/garrison or a trade convoy.
                List<MapEventParty> captors = new List<MapEventParty>();
                int contributionTotal = 0;
                foreach (MapEventParty winner in winnerParties)
                {
                    if (winner == null || winner.Party == null
                        || winner.ContributionToBattle <= 0 || winner.Party.MemberRoster.Count <= 0)
                    {
                        continue;
                    }
                    MobileParty mobile = winner.Party.MobileParty;
                    if (mobile != null && (mobile.IsVillager || mobile.IsCaravan || mobile.IsPatrolParty
                        || ((mobile.IsGarrison || mobile.IsMilitia)
                            && mobile.CurrentSettlement != null && mobile.CurrentSettlement.IsVillage)))
                    {
                        continue;
                    }
                    captors.Add(winner);
                    contributionTotal += winner.ContributionToBattle;
                }
                if (captors.Count == 0)
                {
                    // Nobody fit to hold them: leave the roster exactly as it stands for vanilla to resolve.
                    return;
                }

                BattleRewardModel rewardModel = Campaign.Current.Models.BattleRewardModel;
                foreach (MapEventParty defeated in defeatedParties)
                {
                    if (defeated == null || defeated.Party == null || defeated.Party == PartyBase.MainParty)
                    {
                        continue;
                    }
                    TroopRoster roster = defeated.Party.MemberRoster;
                    for (int index = roster.Count - 1; index >= 0; index--)
                    {
                        TroopRosterElement element = roster.GetElementCopyAtIndex(index);
                        CharacterObject character = element.Character;
                        if (character == null || character.IsHero || element.WoundedNumber <= 0)
                        {
                            continue;
                        }
                        if (!rewardModel.CanTroopBeTakenPrisoner(character))
                        {
                            continue;
                        }

                        int wounded = element.WoundedNumber;
                        for (int i = 0; i < wounded; i++)
                        {
                            MapEventParty captor = PickCaptor(captors, contributionTotal);
                            captor?.RosterToReceiveLootPrisoners?.AddToCounts(character, 1, insertAtFront: false, woundedCount: 1);
                        }
                        // Off the beaten roster they go -- both the head and its wounded flag -- so the party leaves
                        // without them and vanilla's own wounded loop below finds nothing left to capture.
                        roster.AddToCountsAtIndex(index, -wounded, -wounded, 0, removeDepleted: false);
                    }
                    roster.RemoveZeroCounts();
                }
            }
            catch (Exception)
            {
                // Battle finalisation is load-bearing; never let this stop vanilla's own capture from running.
            }
        }

        // Weighted by contribution, mirroring the game's own share-out -- but always returning a captor, because the
        // whole point here is that a wounded man is taken by SOMEONE rather than dropped.
        private static MapEventParty PickCaptor(List<MapEventParty> captors, int contributionTotal)
        {
            if (captors.Count == 1 || contributionTotal <= 0)
            {
                return captors[0];
            }
            int roll = MBRandom.RandomInt(contributionTotal);
            for (int i = 0; i < captors.Count; i++)
            {
                roll -= captors[i].ContributionToBattle;
                if (roll < 0)
                {
                    return captors[i];
                }
            }
            return captors[captors.Count - 1];
        }
    }
}
