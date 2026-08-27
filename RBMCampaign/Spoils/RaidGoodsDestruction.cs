using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Raids waste as much as they carry off. A raiding party can only strip and haul away so much before
    /// the rest -- barns fired, stores trampled, whatever cannot be loaded in time -- is simply destroyed.
    /// So the goods a raid yields are scaled down to a "taken fraction": by default only about half of
    /// what vanilla would hand over actually reaches the raiders' baggage, the other half lost with the
    /// village. A better raider takes more of it: the Nord, a raiding people, and a leader schooled in
    /// Roguery both push the fraction back up toward vanilla's full haul, so a master reiver at the head
    /// of a Nord host loots nearly everything while a green lord's raid mostly goes up in smoke.
    /// </summary>
    /// <remarks>
    /// This is the goods analogue of the coin-side <see cref="SpoilsPool.OnRaidCompleted"/> drain, which
    /// already splits a raided village's purse into a carried-off share and a destroyed remainder. Coin is
    /// settled once when the raid finishes; goods are looted tick by tick during the looting phase, so they
    /// are governed at the model instead -- <see cref="DefaultRaidModel.GetRaidLootMultiplier"/> is the one
    /// lever every goods funnel in <c>RaidEventComponent.Update</c> reads (village stockpile keep-chance,
    /// production rewards, common loot) plus the per-lost-hearth gold reward. Multiplying its result by the
    /// taken fraction therefore scales the whole haul at once.
    ///
    /// The fraction is read RELATIVE to vanilla's baseline: 1.0 is vanilla's own yield (which itself already
    /// destroys about half the stockpile -- that is vanilla, not us), and the default 0.5 halves it again.
    /// The ceiling is clamped to 1.0, so the most skilled raider reaches vanilla's full haul but never
    /// exceeds it. Naval-DLC safe: the live <c>NavalDLCRaidModel</c> is a decorator that routes back through
    /// <see cref="DefaultRaidModel.GetRaidLootMultiplier"/>, so this fires with or without the DLC and does
    /// not double-apply.
    ///
    /// Kept as code constants for now -- the same footing the raid/siege coin splits sit on -- promotable to
    /// config sliders later. Switch the whole thing off by disabling the campaign module.
    /// </remarks>
    public static class RaidGoodsDestruction
    {
        /// <summary>On whenever the campaign module is on. Independent of the spoils-purse economy.</summary>
        public static bool IsEnabled
        {
            get { return RBMConfig.RBMConfig.rbmCampaignEnabled; }
        }

        /// <summary>
        /// The share of a raid's goods an unremarkable raider carries off; the rest is destroyed. Read
        /// against vanilla's own yield (see the class remarks), so 0.5 means "half of what vanilla would
        /// give". The one dial on how wasteful a base-case raid is, before culture and skill lift it.
        /// </summary>
        private const float BaseTakenFraction = 0.5f;

        /// <summary>
        /// How much each point of the leader's Roguery lifts the taken fraction. At 0.001 the 300-point cap
        /// adds +0.30 -- a plain lord's half carried up to four-fifths -- so a plunderer's eye means less of
        /// the village burns unhauled and more of it rides home.
        /// </summary>
        private const float RogueryBonusPerPoint = 0.001f;

        /// <summary>
        /// The flat lift a Nord leader adds to the taken fraction. A raiding people load and haul faster than
        /// they waste, so a Nord host leaves less behind: +0.20 on its own, and stacked with a full Roguery
        /// score it reaches the 1.0 ceiling -- a master Nord reiver who destroys nothing and takes it all.
        /// </summary>
        private const float NordCultureBonus = 0.2f;

        /// <summary>
        /// The fraction of vanilla's raid haul the <paramref name="receivingParty"/> keeps, given its
        /// leader's culture and Roguery. Resolves the leader the way the model does -- the army leader for a
        /// party riding in an army, else the party's own -- so a follower party in a Nord lord's host shares
        /// his raiding prowess. Clamped to [0, 1]: never negative, never past vanilla's full haul.
        /// </summary>
        private static float TakenFraction(PartyBase receivingParty)
        {
            float fraction = BaseTakenFraction;
            MobileParty mobileParty = receivingParty?.MobileParty;
            Hero hero = mobileParty?.Army?.LeaderParty?.LeaderHero ?? mobileParty?.LeaderHero;
            if (hero != null)
            {
                int roguery = hero.GetSkillValue(DefaultSkills.Roguery);
                if (roguery > 0)
                {
                    fraction += roguery * RogueryBonusPerPoint;
                }
                if (hero.Culture != null && hero.Culture.StringId == "nord")
                {
                    fraction += NordCultureBonus;
                }
            }
            return MathF.Clamp(fraction, 0f, 1f);
        }

        [HarmonyPatch(typeof(DefaultRaidModel), "GetRaidLootMultiplier")]
        private class ScaleRaidLootByTakenFraction
        {
            private static void Postfix(PartyBase receivingParty, ref float __result)
            {
                if (IsEnabled)
                {
                    __result *= TakenFraction(receivingParty);
                }
            }
        }
    }
}
