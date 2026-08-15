using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Stops an artisan notable's standing decaying without bound once the bench stops paying him.
    ///
    /// Vanilla gives every artisan a flat <c>-0.1</c> power a day, and offsets it with the income his
    /// hidden shop earns: asset income is minted into his purse, and the 500-gold-to-1-power converter
    /// turns the surplus back into standing. The two are meant to balance somewhere above the Regular
    /// rank.
    ///
    /// Under RBM the income leg is gone. <see cref="WorkshopPurse"/> settles the artisans' bench in kind
    /// rather than in gold -- deliberately, and for good reasons recorded on <c>IsCitizenLabour</c> -- so
    /// the hidden shop's capital never moves, <c>ProfitMade</c> is permanently zero, and an artisan
    /// holding no named shop has no income at all. Nothing then opposes the <c>-0.1</c>.
    ///
    /// That would be survivable if the decay self-limited, but it does not. Vanilla's restoring force is
    /// <c>CalculateDailyPowerChangeForInfluentialNotables</c>, worth <c>-(Power-100)/500</c>, and it is
    /// applied only inside <c>if (hero.Power &gt; RegularNotableMaxPowerLevel)</c>. Below 100 there is no
    /// term pushing back at all, so the artisan falls about 36 power a campaign year, indefinitely, and
    /// eventually goes negative -- <c>AddPower</c> does not clamp.
    ///
    /// Two things break on the way down, and both are self-reinforcing:
    /// <list type="bullet">
    /// <item>volunteer slots stop upgrading, because the daily roll is
    /// <c>log2(Power / Tier) * 0.01</c> and that is non-positive once power falls to the troop's tier;</item>
    /// <item>he can never win a named workshop -- the one thing that would restore his income -- because
    /// the buyer weight is <c>max(Power, 0) / 10^OwnedWorkshops</c>, which reaches zero and stays there.</item>
    /// </list>
    ///
    /// So the occupation penalty is cancelled at and below the Regular rank. An artisan drifts down to
    /// 100 exactly and holds, which is where a man of no particular consequence but no particular trouble
    /// belongs. Above 100 nothing changes: the penalty and vanilla's soft cap both apply as written, and
    /// an artisan who does win a shop still climbs on its income.
    /// </summary>
    /// <remarks>
    /// A postfix that adds the penalty back rather than a transpiler removing it, because the term is a
    /// plain <c>Add</c> on an <c>ExplainedNumber</c> and re-adding it is exact, order-independent, and
    /// leaves every other contribution -- alleys, issues, ruler-clan affiliation, the soft cap -- to be
    /// computed by vanilla. An artisan carrying an issue still takes its <c>IssueOwnerPower</c> drag,
    /// which is intended: a floor on the structural penalty is not immunity from events.
    ///
    /// The threshold is read from the model instance rather than hardcoded to 100, so it tracks
    /// <c>RegularNotableMaxPowerLevel</c> if a future patch or game version moves the rank boundary.
    ///
    /// Guarded on <c>hero.IsActive</c> because vanilla returns an empty number for inactive heroes before
    /// adding anything; without the guard this would hand standing to notables who are dead or otherwise
    /// out of play.
    ///
    /// <c>DefaultNotablePowerModel</c> is safe to reach through <c>PatchAll</c> -- its fields are instance
    /// <c>TextObject</c>s with no static constructor and no <c>Game.Current</c> read, unlike
    /// <c>DefaultClanFinanceModel</c>, whose type initializer trap is documented in
    /// <see cref="WorkshopPurse"/>.
    ///
    /// Patched only when <c>rbmCampaignEnabled</c>: RBMCampaign's <c>PatchAll</c> runs solely under that
    /// toggle, so with the module off the bench pays again and vanilla's balance is restored untouched.
    /// </remarks>
    public static class ArtisanStanding
    {
        // The occupation penalty this cancels, from DefaultNotablePowerModel.CalculateDailyPowerChangeForHero.
        // Kept as a named constant so the sign and magnitude are checkable against the game source rather
        // than being an unexplained 0.1 in the middle of a patch.
        private const float ArtisanOccupationPenalty = -0.1f;

        private static readonly TextObject StandingEffect =
            new TextObject("{=RBMArtisanStanding}Artisan Standing");

        [HarmonyPatch(typeof(DefaultNotablePowerModel), "CalculateDailyPowerChangeForHero")]
        private static class ArtisanDecayFloorPatch
        {
            private static void Postfix(DefaultNotablePowerModel __instance, Hero hero,
                ref ExplainedNumber __result)
            {
                if (hero == null || !hero.IsActive || !hero.IsArtisan)
                {
                    return;
                }
                if (hero.Power > __instance.RegularNotableMaxPowerLevel)
                {
                    return;
                }
                __result.Add(-ArtisanOccupationPenalty, StandingEffect);
            }
        }
    }
}
