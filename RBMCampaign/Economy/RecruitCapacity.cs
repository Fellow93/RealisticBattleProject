using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// How MANY of a notable's six volunteer slots a lord may take -- a separate axis from what each
    /// recruit costs (that is <see cref="RecruitSupply"/>). Vanilla scales capacity off the buyer's
    /// personal relation with the NOTABLE, which makes a fief's own liege no more entitled to its
    /// soldiers than a passing stranger who happens to be liked in town.
    ///
    /// RBM re-roots capacity in who holds the land and who rules the realm:
    ///
    ///   OWNER CLAN -- any member of the clan that owns the fief takes the FULL six. The men of a lord's
    ///   own villages and towns are his to raise, whatever the local headman thinks of him personally.
    ///
    ///   THE RULER -- the kingdom's leader raising men from a vassal's fief is scaled by his relation with
    ///   that VASSAL (the owner clan's leader), on the same ladder vanilla uses for personal relation. A
    ///   well-loved king draws freely on his realm's fiefs; one his lords resent gets little or nothing.
    ///
    ///   EVERYONE ELSE -- fellow vassals, foreigners, mercenaries -- keeps vanilla's notable-relation calc
    ///   untouched.
    ///
    /// This governs the party recruit path (<c>MaximumIndexHeroCanRecruitFromHero</c>) only; garrison
    /// auto-recruit runs through a different method and is suppressed elsewhere in RBMCampaign.
    /// </summary>
    [HarmonyPatch(typeof(DefaultVolunteerModel), "MaximumIndexHeroCanRecruitFromHero")]
    public static class RecruitCapacity
    {
        private static bool Prefix(Hero buyerHero, Hero sellerHero, ref int __result)
        {
            if (buyerHero == null || sellerHero == null)
            {
                return true;
            }

            Settlement settlement = sellerHero.CurrentSettlement;
            if (settlement == null)
            {
                return true;
            }

            Clan ownerClan = settlement.OwnerClan;
            if (ownerClan == null)
            {
                return true;
            }

            // The fief's own clan takes every man it has.
            if (buyerHero.Clan != null && buyerHero.Clan == ownerClan)
            {
                __result = 6;
                return false;
            }

            // The realm's ruler raising men from a vassal's fief -- scaled by his standing with the vassal.
            Kingdom kingdom = ownerClan.Kingdom;
            Hero ownerLeader = ownerClan.Leader;
            if (kingdom != null && ownerLeader != null && kingdom.Leader == buyerHero)
            {
                int relation = buyerHero.GetRelation(ownerLeader);
                __result = MathF.Min(6, MathF.Max(0, 1 + RelationToSlots(relation)));
                return false;
            }

            // Fellow vassals, foreigners, mercenaries: vanilla's notable-relation calc stands.
            return true;
        }

        /// <summary>Vanilla's relation-to-slot ladder, replicated so the ruler branch reads the same as
        /// the personal-relation path it replaces.</summary>
        private static int RelationToSlots(int relation)
        {
            if (relation >= 100) return 7;
            if (relation >= 80) return 6;
            if (relation >= 60) return 5;
            if (relation >= 40) return 4;
            if (relation >= 20) return 3;
            if (relation >= 10) return 2;
            if (relation >= 5) return 1;
            if (relation < 0) return -1;
            return 0;
        }
    }
}
