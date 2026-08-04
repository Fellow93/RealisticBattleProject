using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Makes a re-spawning AI lord party appear at one of its own clan's fortifications when the clan
    /// owns any -- the clan's own towns/castles are the "first pick", and vanilla's faction-wide
    /// <see cref="SettlementHelper.GetBestSettlementToSpawnAround"/> scoring is left as the fallback for
    /// a landless clan (or the player, whom we never touch).
    ///
    /// Vanilla already biases toward clan-owned settlements (a 4x weight), but its distance term is
    /// weighted 0.66 toward <c>hero.LastKnownClosestSettlement</c>, so a lord captured deep in enemy
    /// territory and then released often re-spawns next to a same-faction *ally's* fief instead of his
    /// own. This postfix hard-prefers the clan's own holdings whenever the clan has at least one, and
    /// only among those still keeps the vanilla "nearest to where the lord was last seen" preference so
    /// a multi-fief clan puts him back near his old stomping grounds rather than at a random holding.
    ///
    /// Pure location override -- the party creation, timing, slot/score gates and everything else stay
    /// vanilla's. The method is also used for the player's own fallback spawn position and (via
    /// MobilePartyHelper) other spawns, so we scope the override to non-player heroes only.
    /// </summary>
    [HarmonyPatch(typeof(SettlementHelper), "GetBestSettlementToSpawnAround")]
    internal static class RBMLordSpawnSettlementBehavior
    {
        private static void Postfix(Hero hero, ref Settlement __result)
        {
            if (hero == null || hero == Hero.MainHero || hero.Clan == null)
            {
                return;
            }

            Settlement best = null;
            float bestScore = -1f;
            foreach (Settlement settlement in hero.Clan.Settlements)
            {
                // Towns and castles only -- villages have no gate garrison and are the wrong place to
                // put a landed lord's party; a clan that owns only villages falls through to vanilla.
                if (!settlement.IsFortification)
                {
                    continue;
                }
                // Don't drop a fresh party straight into a raid, siege or ongoing battle.
                if (settlement.Party.MapEvent != null || settlement.IsUnderRaid || settlement.IsUnderSiege)
                {
                    continue;
                }

                // Prefer a town over a castle, then (among the clan's own holdings) the one nearest to
                // where the lord was last known -- the same distance shape vanilla uses, squared.
                float typeWeight = settlement.IsTown ? 1f : 0.9f;
                float distWeight = 1f;
                if (hero.LastKnownClosestSettlement != null)
                {
                    float normalized = Campaign.Current.Models.MapDistanceModel.GetDistance(
                        hero.LastKnownClosestSettlement, settlement, isFromPort: false,
                        isTargetingPort: false, MobileParty.NavigationType.Default) / Campaign.MapDiagonal;
                    distWeight = 1f - MathF.Clamp(normalized, 0f, 1f);
                    distWeight *= distWeight;
                }

                float score = typeWeight * (0.25f + 0.75f * distWeight);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = settlement;
                }
            }

            if (best != null)
            {
                __result = best;
            }
        }
    }
}
