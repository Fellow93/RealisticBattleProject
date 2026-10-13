using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// The ARMY-CHECK line of <see cref="PartyBudgetLog"/>: why a clan leader could or could not raise an army.
    /// A read-only postfix on vanilla's <c>DefaultArmyManagementCalculationModel.CanLordCreateArmy</c> (the AI
    /// calls it from <c>AiMilitaryBehavior.AiHourlyTick</c> on the main thread); vanilla's rule is unchanged but for
    /// the strength floor <see cref="ArmyStrengthFloorPatch"/> lowers and the call range and kingdom quota
    /// <see cref="ArmyCallReachPatch"/> widens, which this reads. Only
    /// the parties that can lead an army at all -- the clan leader's, or the clan's first war party while the leader
    /// has none -- are logged, once per party per campaign day, with vanilla's gates re-read in vanilla's order. The
    /// member pass is mirrored whenever the leader gates pass (without the quota trim, whose room is printed): how
    /// many qualify and how many free lords were passed over for range, food or size ratio.
    /// </summary>
    internal static class ArmyCheckLog
    {
        // Parties already logged today. Cleared when the day changes, so it never holds more than a day of parties.
        private static readonly HashSet<MobileParty> _loggedToday = new HashSet<MobileParty>();
        private static int _loggedDay = -1;

        // The floor CanLordCreateArmy applies now: RBM's 700 when ArmyStrengthFloorPatch is in, else vanilla's 1000.
        private static float ArmyStrengthFloor
        {
            get { return ArmyStrengthFloorPatch.CurrentFloor(); }
        }
        private const float MinimumInfluence = 100f;

        [HarmonyPatch(typeof(DefaultArmyManagementCalculationModel), nameof(DefaultArmyManagementCalculationModel.CanLordCreateArmy))]
        private static class CanLordCreateArmyPatch
        {
            private static void Postfix(MobileParty mobileParty, ref MBList<MobileParty> possibleArmyMembers, bool __result)
            {
                if (!PartyBudgetLog.IsEnabled || mobileParty?.LeaderHero?.Clan == null || !CanLead(mobileParty))
                {
                    return;
                }
                int day = (int)CampaignTime.Now.ToDays;
                if (day != _loggedDay)
                {
                    _loggedToday.Clear();
                    _loggedDay = day;
                }
                if (!_loggedToday.Add(mobileParty))
                {
                    return;
                }
                try
                {
                    PartyBudgetLog.Log("ARMY-CHECK", mobileParty, Describe(mobileParty, possibleArmyMembers, __result));
                }
                catch (Exception)
                {
                }
            }
        }

        // Vanilla's leader rule: the clan leader's party, or the clan's first war party while the leader is in none.
        private static bool CanLead(MobileParty party)
        {
            Hero hero = party.LeaderHero;
            Clan clan = hero.Clan;
            if (clan.Leader == hero)
            {
                return true;
            }
            return clan.Leader != null && clan.Leader.PartyBelongedTo == null && clan.WarPartyComponents != null
                && clan.WarPartyComponents.FirstOrDefault() == party.WarPartyComponent;
        }

        private static float Strength(MobileParty party)
        {
            return party.Party.GetCustomStrength(BattleSideEnum.Attacker, MapEvent.PowerCalculationContext.Siege);
        }

        private static string Describe(MobileParty leader, MBList<MobileParty> members, bool result)
        {
            Campaign campaign = Campaign.Current;
            float ratioNeeded = campaign.Models.ArmyManagementCalculationModel.AIMobilePartySizeRatioToCallToArmy;
            float leaderFoodNeeded = campaign.Models.MobilePartyAIModel.NeededFoodsInDaysThresholdForSiege;
            float memberFoodNeeded = campaign.Models.ArmyManagementCalculationModel.MinimumNeededFoodInDaysToCallToArmy;
            Clan clan = leader.LeaderHero.Clan;
            Kingdom kingdom = leader.MapFaction as Kingdom;

            float ratio = leader.PartySizeRatio;
            int food = leader.GetNumDaysForFoodToLast();
            bool enemyWithFiefs = kingdom != null && kingdom.FactionsAtWarWith.Any(x => x.Fiefs.Any());
            float leaderStrength = Strength(leader);

            // The first leader gate that fails, in vanilla's order.
            string failed = null;
            if (leader.IsCurrentlyAtSea) failed = "at sea";
            else if (clan.Influence <= MinimumInfluence) failed = "influence";
            else if (clan.IsUnderMercenaryService) failed = "mercenary";
            else if (food <= leaderFoodNeeded) failed = "food";
            else if (!enemyWithFiefs) failed = "no enemy with fiefs";
            else if (ratio <= ratioNeeded) failed = "party size ratio";

            string membersText;
            if (failed == null && kingdom != null)
            {
                MemberCounts counts = QualifyingMembers(leader, kingdom, ratioNeeded, memberFoodNeeded);
                float quota = ArmyCallReachPatch.CurrentQuota();
                int inArmies = kingdom.Armies.Sum(x => x.Parties.Count);
                int quotaRoom = MathF.Ceiling(kingdom.WarPartyComponents.Count * quota - inArmies);
                string passedOver = "passed over: " + counts.OutOfRange + " out of range, " + counts.LowFood
                    + " short of food, " + counts.SmallRatio + " under the size ratio"
                    + "  ·  quota room " + quotaRoom + " (" + (quota * 100f).ToString("0") + "% of "
                    + kingdom.WarPartyComponents.Count + " war parties, " + inArmies + " already in armies)";

                if (result && members != null)
                {
                    float sum = leaderStrength;
                    foreach (MobileParty member in members)
                    {
                        sum += Strength(member);
                    }
                    membersText = "called " + members.Count + " of " + counts.Qualifying + " qualifying, army strength "
                        + sum.ToString("0") + " (floor " + ArmyStrengthFloor.ToString("0") + ")  ·  " + passedOver;
                }
                else
                {
                    float sum = leaderStrength + counts.Strength;
                    membersText = "members qualifying " + counts.Qualifying + ", army strength " + sum.ToString("0")
                        + " (floor " + ArmyStrengthFloor.ToString("0") + ")  ·  " + passedOver;
                    // Vanilla trims the ranked list to the quota room before summing, so a trimmed list can also
                    // fall below the floor; the untrimmed sum is reported.
                    failed = quotaRoom <= 0 ? "army quota full"
                        : counts.Qualifying == 0 ? "no members"
                        : sum < ArmyStrengthFloor ? "strength below " + ArmyStrengthFloor.ToString("0")
                        : counts.Qualifying > quotaRoom ? "army quota (" + quotaRoom + " of " + counts.Qualifying + " members fit)"
                        : "member pass differs from the log's mirror";
                }
            }
            else
            {
                membersText = "members not checked";
            }

            return (result ? "YES" : "NO  (" + failed + ")")
                + "  ·  ratio " + ratio.ToString("0.00") + " (need > " + ratioNeeded.ToString("0.00") + ", "
                + leader.MemberRoster.TotalManCount + "/" + leader.Party.PartySizeLimit + " men)"
                + "  ·  strength " + leaderStrength.ToString("0")
                + "  ·  food " + food + "d (need > " + leaderFoodNeeded.ToString("0") + ")"
                + "  ·  influence " + clan.Influence.ToString("0") + " (need > " + MinimumInfluence.ToString("0") + ")"
                + (clan.IsUnderMercenaryService ? "  ·  mercenary" : "")
                + (leader.IsCurrentlyAtSea ? "  ·  at sea" : "")
                + (enemyWithFiefs ? "" : "  ·  no enemy holds fiefs")
                + "  ·  wage " + leader.TotalWage + "/" + leader.PaymentLimit
                + "  ·  " + membersText;
        }

        private struct MemberCounts
        {
            public int Qualifying;   // pass every gate: the parties vanilla ranks (before its quota trim)
            public float Strength;   // their summed siege strength
            public int OutOfRange;   // free lords farther than the AI call range
            public int LowFood;      // in range, but not more than the member food threshold
            public int SmallRatio;   // in range with food, but not over the size ratio
        }

        // Vanilla's member pass, mirrored for the log (without the quota trim): the parties it would rank, their
        // summed siege strength, and the otherwise free lords it passed over, each counted once under the first of
        // range, food and size ratio that it fails.
        private static MemberCounts QualifyingMembers(MobileParty leader, Kingdom kingdom, float ratioNeeded, float foodNeeded)
        {
            MemberCounts counts = new MemberCounts();
            float maxDistance = Campaign.Current.Models.ArmyManagementCalculationModel.MaximumDistanceToCallToArmy;
            IDisbandPartyCampaignBehavior disband = Campaign.Current.GetCampaignBehavior<IDisbandPartyCampaignBehavior>();
            foreach (var component in kingdom.WarPartyComponents)
            {
                MobileParty party = component.MobileParty;
                Hero hero = party?.LeaderHero;
                if (party == null || !party.IsLordParty || party.Army != null || party == leader || hero == null
                    || party.IsMainParty || hero == hero.MapFaction?.Leader || !hero.CanJoinArmy
                    || party.Ai.DoNotMakeNewDecisions || party.CurrentSettlement?.SiegeEvent != null || party.IsDisbanding
                    || !hero.CanLeadParty() || party.IsInRaftState
                    || party.MapEvent != null || party.BesiegedSettlement != null
                    || (disband != null && disband.IsPartyWaitingForDisband(party)))
                {
                    continue;
                }
                if (!(DistanceHelper.GetDistanceBetweenMobilePartyToMobileParty(party, leader, party.NavigationCapability, out float _) < maxDistance))
                {
                    counts.OutOfRange++;
                    continue;
                }
                if (!(party.GetNumDaysForFoodToLast() > foodNeeded))
                {
                    counts.LowFood++;
                    continue;
                }
                if (!(party.PartySizeRatio > ratioNeeded))
                {
                    counts.SmallRatio++;
                    continue;
                }
                counts.Qualifying++;
                counts.Strength += Strength(party);
            }
            return counts;
        }
    }
}
