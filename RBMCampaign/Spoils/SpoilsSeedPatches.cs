using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// The recruit seed for the parties vanilla fills without a recruit event RBM can read: a settlement's
    /// militia as it grows, a naval convoy refilled in port, and a caravan's own hires (whose
    /// <c>OnTroopRecruited</c> carries no hero to find the party by). Each seeds only the men it added,
    /// through <see cref="SpoilsPool.SeedNewMen"/>'s top-up. A new caravan or lord's party is seeded off
    /// <c>MobilePartyCreated</c> (<see cref="RBMSpoilsCampaignBehavior"/>), and RBM's own garrison growth
    /// and defence muster seed where they add their men.
    /// </summary>
    public static class SpoilsSeedPatches
    {
        /// <summary>
        /// Every militiaman a settlement raises: its daily growth, a fresh militia party's first men, a
        /// lent escort coming home. The setter adds them through here, picking melee or ranged, green or
        /// veteran at random, so the roster is diffed rather than the request trusted.
        /// </summary>
        [HarmonyPatch(typeof(Settlement), "AddMilitiasToParty")]
        private static class MilitiaSpawnSeedPatch
        {
            private static void Prefix(MobileParty militiaParty, out Dictionary<CharacterObject, int> __state)
            {
                __state = SpoilsPool.IsEnabled ? SpoilsPool.SnapshotStacks(militiaParty?.Party) : null;
            }

            private static void Postfix(MobileParty militiaParty, Dictionary<CharacterObject, int> __state)
            {
                if (__state != null)
                {
                    SpoilsPool.SeedGrowthSince(militiaParty?.Party, __state);
                }
            }
        }

        /// <summary>A naval convoy topped back up to strength from its template on entering a port town.</summary>
        [HarmonyPatch(typeof(CaravansCampaignBehavior), "RefillConvoyTroops")]
        private static class ConvoyRefillSeedPatch
        {
            private static void Prefix(MobileParty convoy, out Dictionary<CharacterObject, int> __state)
            {
                __state = SpoilsPool.IsEnabled ? SpoilsPool.SnapshotStacks(convoy?.Party) : null;
            }

            private static void Postfix(MobileParty convoy, Dictionary<CharacterObject, int> __state)
            {
                if (__state != null)
                {
                    SpoilsPool.SeedGrowthSince(convoy?.Party, __state);
                }
            }
        }

        /// <summary>
        /// A caravan hiring tavern mercenaries. It is led by a caravan master, no hero, so the
        /// <c>OnTroopRecruited</c> it raises names no recruiter and RBM's seed there cannot find the party.
        /// A companion-led caravan does have one and is seeded there instead.
        /// </summary>
        [HarmonyPatch(typeof(RecruitmentCampaignBehavior), "ApplyInternal")]
        private static class CaravanRecruitSeedPatch
        {
            private static void Postfix(MobileParty side1Party, CharacterObject troop, int number)
            {
                if (side1Party == null || side1Party.LeaderHero != null || !side1Party.IsCaravan)
                {
                    return;
                }
                SpoilsPool.SeedNewMen(side1Party.Party, troop, number);
            }
        }
    }
}
