using HarmonyLib;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;

namespace RBMCampaign
{
    /// <summary>
    /// Scales AI lord parties' wage payment limit up to match RBM's inflated wage table, so a lord can
    /// afford -- and therefore field and muster -- a vanilla-sized army again.
    ///
    /// The problem: an AI party's *affordable* troop count is <c>PaymentLimit / Campaign.AverageWage</c>
    /// (see <c>PartyBaseHelper.FindPartySizeNormalLimit</c> and
    /// <c>GarrisonTroopsCampaignBehavior.CalculateMobilePartySizeLimitWithFoodAndWage</c>). RBM's
    /// <see cref="TierBasedWageModel"/> raises every troop wage ~10x, and <c>Campaign.AverageWage</c> is
    /// derived straight from those wages, so the *denominator* jumps ~10x. But the *numerator*,
    /// <c>PaymentLimit</c>, is set daily by vanilla <c>ClanVariablesCampaignBehavior.MakeClanFinancialEvaluation</c>
    /// from the clan leader's gold at vanilla scale (~200-1200 for most lords, 10000 only for very rich
    /// clans) and RBM never touches it. Numerator vanilla-scale, denominator 10x -> a lord who could afford
    /// ~150 troops in vanilla affords ~15-20 here. That is why AI lords wander at a fraction of strength
    /// while their own wealth-funded garrisons balloon -- and why vanilla's garrison-take math (rightly)
    /// refuses to hand a lord more men than his crushed affordable ceiling.
    ///
    /// RBM already fixed the mirror-image problem on the garrison side (see <see cref="GarrisonWageLimit"/>
    /// lifts each fief's garrison wage cap so garrisons size by strength, not economy). This applies the
    /// same correction to lord field parties: after vanilla sets each war party's limit for the day, we
    /// multiply it by the wage-inflation factor, restoring the vanilla-equivalent affordable party size
    /// while KEEPING vanilla's clan-wealth gradient (poor clans still field smaller armies than rich ones,
    /// just at RBM's wage scale). Idempotent day to day: vanilla resets the limit to an absolute value each
    /// daily clan tick, and we re-scale that fresh value, so the multiplier never compounds.
    ///
    /// Patched at <c>MakeClanFinancialEvaluation</c>, which vanilla calls once per day for every non-player
    /// clan (<c>DailyTickClan</c> gates it on <c>clan != Clan.PlayerClan</c>), so this only ever scales AI
    /// lords; the player's own <c>PaymentLimit</c> is left exactly as vanilla/RBM set it.
    /// </summary>
    [HarmonyPatch(typeof(ClanVariablesCampaignBehavior), "MakeClanFinancialEvaluation")]
    internal static class LordPartyWageLimit
    {
        // RBM's TierBasedWageModel inflates the party-template-weighted average troop wage ~10x over
        // vanilla, which is exactly the factor by which Campaign.AverageWage (the affordability divisor)
        // grows. Scaling PaymentLimit by the same factor cancels the inflation, so affordable party size
        // returns to its vanilla value. Keep this in step with the wage table if it is ever re-based.
        private const int WagePaymentLimitScale = 10;

        private static void Postfix(Clan clan)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || clan == null || clan == Clan.PlayerClan)
            {
                return;
            }

            foreach (WarPartyComponent warPartyComponent in clan.WarPartyComponents)
            {
                MobileParty mobileParty = warPartyComponent?.MobileParty;
                if (mobileParty == null)
                {
                    continue;
                }

                // long-guard the multiply so a max-limit (10000) party can't overflow int on the way up.
                long scaled = (long)mobileParty.PaymentLimit * WagePaymentLimitScale;
                int newLimit = (int)Math.Min(scaled, int.MaxValue);
                if (newLimit != mobileParty.PaymentLimit)
                {
                    mobileParty.SetWagePaymentLimit(newLimit);
                }
            }
        }
    }
}
