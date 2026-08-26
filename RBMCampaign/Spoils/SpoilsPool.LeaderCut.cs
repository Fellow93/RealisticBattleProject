using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// A commander takes his cut. Whatever a party's men gather off a field or out of a sacked
    /// settlement, their leader skims a share of it into his own purse as gold before it settles into
    /// the stacks. The men keep the rest as spoils, the way they always have. The share scales with the
    /// leader's clan tier -- a greater house takes a heavier cut -- so a base share is set and multiplied
    /// by the tier plus one, and even a tier-0 or clanless leader still takes the base share.
    /// </summary>
    /// <remarks>
    /// Conserving: the cut is drawn out of the same purses the gather just
    /// filled and handed to the leader as gold one-for-one, so no coin is minted from nothing -- it is
    /// only moved from the stacks' pool into their keeper's treasury. Paid to the party's payee
    /// (see <see cref="GetPartyPayee"/>): the party's owner if one is alive, else the hero
    /// leading it, so an AI lord pays himself and the player's parties pay the player.
    /// </remarks>
    public static partial class SpoilsPool
    {
        /// <summary>
        /// How much heavier a mercenary captain's spoils cut runs than a settled lord's while his contract
        /// holds -- half again as large. A hired company fights for the purse, so its leader keeps a larger
        /// share of what it wins than one whose men fight for his realm.
        /// </summary>
        private const float MercenaryLeaderCutMultiplier = 1.5f;

        /// <summary>
        /// How much each point of the leader's Roguery widens his cut, relative to the share his standing and
        /// contract already earn him: the fraction is multiplied by (1 + Roguery x this), so a sharper eye for
        /// plunder skims a larger slice of the same pot without touching what the tier scaling below hands out.
        /// At 0.003 a hundred points of Roguery is a +30% share -- a tier-1 lord's usual tenth carried up to 13%
        /// -- and the 300-point cap is very nearly double. Stacks on top of the tier and mercenary scaling, and
        /// like them is drawn back out of the men's own purses, so a keener leader mints no coin, he only keeps
        /// more of what his men already took.
        /// </summary>
        private const float LeaderRogueryShareBonusPerPoint = 0.003f;

        /// <summary>
        /// Skims the party leader's share off a fresh gather and hands it to him as gold. Called with the
        /// spoils the men actually took (loot, plunder or a sack), so the cut is a share of what reached
        /// the stacks, not of what the field nominally held. The share is drawn back out of those stacks
        /// -- the gather guarantees the purse holds at least it -- so the men net the remainder.
        /// </summary>
        /// <returns>The gold the leader pocketed, which the gather's own announcement can note.</returns>
        /// <summary>
        /// The share of a fresh gather a party's leader skims as gold before any of it settles into the
        /// stacks: the base cut scaled by clan tier (tier + 1, so even a tier-0 or clanless leader takes
        /// the base share once over), half again as large while a mercenary contract holds, and clamped so
        /// it never runs past all of it. Zero when the party has no living payee to take a cut. Shared by
        /// <see cref="ApplyLeaderCut"/>, which applies it, and the ransom-menu tooltip, which previews it,
        /// so the two can never quote different numbers.
        /// </summary>
        public static float GetLeaderCutFraction(PartyBase party)
        {
            Hero payee = GetPartyPayee(party);
            if (payee == null || !payee.IsAlive)
            {
                return 0f;
            }
            int clanTier = (payee.Clan != null) ? payee.Clan.Tier : 0;
            float fraction = RBMConfig.RBMConfig.troopLeaderSpoilsCutFraction * (clanTier + 1);
            if (MercenaryContractPay.IsMercenaryClan(payee.Clan))
            {
                fraction *= MercenaryLeaderCutMultiplier;
            }
            int roguery = payee.GetSkillValue(DefaultSkills.Roguery);
            if (roguery > 0)
            {
                fraction *= (1f + roguery * LeaderRogueryShareBonusPerPoint);
            }
            return MathF.Clamp(fraction, 0f, 1f);
        }

        /// <summary>
        /// What the leader would skim off a gather of <paramref name="gathered"/> spoils, touching no
        /// purse -- the same figure <see cref="ApplyLeaderCut"/> draws, for a tooltip to quote in advance.
        /// </summary>
        public static int PreviewLeaderCut(PartyBase party, int gathered)
        {
            if (gathered <= 0)
            {
                return 0;
            }
            return MathF.Round(gathered * GetLeaderCutFraction(party));
        }

        public static int ApplyLeaderCut(PartyBase party, int gathered)
        {
            if (party == null || gathered <= 0)
            {
                return 0;
            }
            Hero payee = GetPartyPayee(party);
            if (payee == null || !payee.IsAlive)
            {
                return 0;
            }
            float fraction = GetLeaderCutFraction(party);
            if (fraction <= 0f)
            {
                return 0;
            }
            int clanTier = (payee.Clan != null) ? payee.Clan.Tier : 0;
            int cut = MathF.Round(gathered * fraction);
            if (cut <= 0)
            {
                return 0;
            }
            int drawn = DrawFromPartyPurse(party, cut);
            if (drawn <= 0)
            {
                return 0;
            }
            // Null giver mints the coin into the payee's purse, the mirror of how an upgrade pays gold out
            // to a null receiver.
            GiveGoldAction.ApplyBetweenCharacters(null, payee, drawn, true);
            if (SpoilsLog.IsEnabled)
            {
                SpoilsLog.Log("LEADER", party, SpoilsLog.Describe(party) + " leader " + payee.Name
                    + " took a " + drawn + " gold cut (" + fraction.ToString("0.00") + " = base x (clan tier "
                    + clanTier + " + 1)"
                    + (MercenaryContractPay.IsMercenaryClan(payee.Clan) ? " x " + MercenaryLeaderCutMultiplier.ToString("0.0") + " mercenary" : "")
                    + (payee.GetSkillValue(DefaultSkills.Roguery) > 0 ? " x (1 + Roguery " + payee.GetSkillValue(DefaultSkills.Roguery) + " x " + LeaderRogueryShareBonusPerPoint.ToString("0.000") + ")" : "")
                    + ") of " + gathered + " gathered");
            }
            return drawn;
        }

        /// <summary>
        /// True when a party has at least one non-hero stack that can hold spoils. A party of only heroes --
        /// a lone wandering commander at its extreme -- has none, so an ordinary gather grants it nothing and
        /// the conserving <see cref="ApplyLeaderCut"/> has no purse to draw a cut from; that is the case
        /// <see cref="ApplyLeaderCutSolo"/> covers.
        /// </summary>
        private static bool HasSpoilsBearingStacks(PartyBase party)
        {
            TroopRoster roster = party?.MemberRoster;
            if (roster == null)
            {
                return false;
            }
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (!element.Character.IsHero && element.Number > 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The commander's cut for a party with no men to share the rest. When a gather found
        /// <paramref name="pot"/> worth of spoils but the party holds no non-hero stack to take the soldiers'
        /// share -- a lone hero, or a band of nothing but heroes -- its leader still strips the field himself
        /// and keeps his usual cut as gold, figured off the whole pot and minted to him the way an ordinary
        /// cut's gold is. The men's remainder has no one to fall to and is lost, as spoils always are without
        /// men to carry them. Does nothing once the party holds even one soldier: that party runs the
        /// conserving <see cref="ApplyLeaderCut"/> instead, drawing its leader's cut back out of the spoils it
        /// was just granted rather than minting a fresh one.
        /// </summary>
        /// <returns>The gold the leader pocketed, for the gather's own announcement to note.</returns>
        public static int ApplyLeaderCutSolo(PartyBase party, int pot)
        {
            if (party == null || pot <= 0 || HasSpoilsBearingStacks(party))
            {
                return 0;
            }
            Hero payee = GetPartyPayee(party);
            if (payee == null || !payee.IsAlive)
            {
                return 0;
            }
            float fraction = GetLeaderCutFraction(party);
            if (fraction <= 0f)
            {
                return 0;
            }
            int cut = MathF.Round(pot * fraction);
            if (cut <= 0)
            {
                return 0;
            }
            int clanTier = (payee.Clan != null) ? payee.Clan.Tier : 0;
            // Nothing was minted into the (absent) stacks to draw the cut back out of, so mint it straight to
            // the leader -- the same null-giver mint ApplyLeaderCut ends on, just without the purse round-trip.
            GiveGoldAction.ApplyBetweenCharacters(null, payee, cut, true);
            if (SpoilsLog.IsEnabled)
            {
                SpoilsLog.Log("LEADER", party, SpoilsLog.Describe(party) + " leader " + payee.Name
                    + " took a " + cut + " gold solo cut (" + fraction.ToString("0.00") + " = base x (clan tier "
                    + clanTier + " + 1)"
                    + (MercenaryContractPay.IsMercenaryClan(payee.Clan) ? " x " + MercenaryLeaderCutMultiplier.ToString("0.0") + " mercenary" : "")
                    + (payee.GetSkillValue(DefaultSkills.Roguery) > 0 ? " x (1 + Roguery " + payee.GetSkillValue(DefaultSkills.Roguery) + " x " + LeaderRogueryShareBonusPerPoint.ToString("0.000") + ")" : "")
                    + ") of " + pot + " stripped, no men to share the rest");
            }
            return cut;
        }

        /// <summary>
        /// Draws <paramref name="amount"/> of spoils out of a party's stacks, spread across them in
        /// proportion to what each already holds, and clamped so no stack is taken below empty. A second
        /// pass mops up any rounding shortfall from whatever purse is left. Returns how much came out --
        /// the whole of <paramref name="amount"/> whenever the party's purses between them hold at least it.
        /// </summary>
        private static int DrawFromPartyPurse(PartyBase party, int amount)
        {
            TroopRoster roster = party?.MemberRoster;
            if (roster == null || amount <= 0)
            {
                return 0;
            }
            long total = 0L;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character.IsHero || element.Number <= 0)
                {
                    continue;
                }
                total += MathF.Max(0, GetSpoils(party, element.Character));
            }
            if (total <= 0L)
            {
                return 0;
            }

            int toDraw = (amount < total) ? amount : (int)total;
            int drawn = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character.IsHero || element.Number <= 0)
                {
                    continue;
                }
                int purse = GetSpoils(party, element.Character);
                if (purse <= 0)
                {
                    continue;
                }
                int share = MathF.Min(purse, (int)((long)toDraw * purse / total));
                if (share > 0)
                {
                    AddSpoils(party, element.Character, -share);
                    drawn += share;
                }
            }

            // Rounding down every proportional share can leave a few short of the cut; take the remainder
            // from whatever purse is still standing.
            int remainder = toDraw - drawn;
            for (int i = 0; i < roster.Count && remainder > 0; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character.IsHero || element.Number <= 0)
                {
                    continue;
                }
                int purse = GetSpoils(party, element.Character);
                if (purse <= 0)
                {
                    continue;
                }
                int take = MathF.Min(remainder, purse);
                AddSpoils(party, element.Character, -take);
                drawn += take;
                remainder -= take;
            }
            return drawn;
        }
    }
}
