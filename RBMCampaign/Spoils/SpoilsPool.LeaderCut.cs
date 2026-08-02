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
        /// Skims the party leader's share off a fresh gather and hands it to him as gold. Called with the
        /// spoils the men actually took (loot, plunder or a sack), so the cut is a share of what reached
        /// the stacks, not of what the field nominally held. The share is drawn back out of those stacks
        /// -- the gather guarantees the purse holds at least it -- so the men net the remainder.
        /// </summary>
        /// <returns>The gold the leader pocketed, which the gather's own announcement can note.</returns>
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
            // The base share scales with clan tier: a greater house commands a heavier cut of its men's
            // spoils. Tier + 1 is the multiplier, so a tier-0 (or clanless) leader still takes the base
            // share once over and a tier-6 dynasty takes seven times it.
            int clanTier = (payee.Clan != null) ? payee.Clan.Tier : 0;
            float fraction = RBMConfig.RBMConfig.troopLeaderSpoilsCutFraction * (clanTier + 1);
            // A captain in a kingdom's pay is running a business, not holding a fief: he skims a heavier
            // share of the spoils his contract wins than a lord fighting his own war would. While the
            // mercenary contract holds, his cut is half again as large. Clamped below so the multiplied
            // share never runs past all of it.
            if (MercenaryContractPay.IsMercenaryClan(payee.Clan))
            {
                fraction *= MercenaryLeaderCutMultiplier;
            }
            fraction = MathF.Clamp(fraction, 0f, 1f);
            if (fraction <= 0f)
            {
                return 0;
            }
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
                    + ") of " + gathered + " gathered");
            }
            return drawn;
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
