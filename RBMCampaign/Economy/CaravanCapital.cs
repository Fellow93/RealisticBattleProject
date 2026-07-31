using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;

namespace RBMCampaign
{
    /// <summary>
    /// Scales a caravan's working capital, and the price of buying one, onto the repriced goods list.
    ///
    /// <see cref="CaravanTradeVolume"/> lifted the two caps on how much of a category a caravan may buy
    /// in one visit, and the clamp that stopped it seeing what the dear goods were worth. What was left
    /// holding it back is the money itself. <c>BuyCategory</c> will never commit more than half the
    /// purse to one category, and a caravan is seeded with 10,000 denars -- so 5,000, against a lot of
    /// velvet at 26,500. The purchase is sized <c>RoundRandomized(budget / itemPrice)</c>, so that is
    /// not a small load, it is no load: zero, on every visit, forever.
    ///
    /// Everything here is multiplied rather than replaced, by the ten the price list moved by in
    /// <see cref="TradeGoodValues"/>. Multiplying keeps every branch and modifier the game applies --
    /// the elite premium, the naval premium, the Aserai trader feat's discount on the forming cost --
    /// without this file having to know or restate any of them.
    ///
    /// A caravan's seed and the price of forming one are the same figure in vanilla (15,000 either way,
    /// once the main hero's bonus is counted), so scaling both by the same factor keeps buying one an
    /// even trade rather than a subsidy.
    /// </summary>
    public static class CaravanCapital
    {
        /// <summary>The factor <see cref="TradeGoodValues"/> moved the price list by.</summary>
        private const int PriceScale = 10;

        /// <summary>
        /// The seed capital, and -- through <c>CalculateOwnerIncomeFromCaravan</c> -- the float a
        /// caravan's purse is drained back down to. Both scale together because both read this method,
        /// so a caravan still keeps exactly its starting capital as working money and pays out only what
        /// it earned above it.
        /// </summary>
        [HarmonyPatch(typeof(DefaultCaravanModel), "GetInitialTradeGold")]
        private static class InitialTradeGoldPatch
        {
            private static void Postfix(ref int __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    return;
                }

                __result *= PriceScale;
            }
        }

        /// <summary>
        /// What the player pays a notable to put a caravan on the road. Held level with the seed above:
        /// left at vanilla's 15,000 it would buy a party carrying ten times that in trade gold.
        /// </summary>
        [HarmonyPatch(typeof(DefaultCaravanModel), "GetCaravanFormingCost")]
        private static class FormingCostPatch
        {
            private static void Postfix(ref int __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    return;
                }

                __result *= PriceScale;
            }
        }

        /// <summary>
        /// War Sails wraps the caravan model rather than extending it, and answers both questions itself
        /// for a convoy -- returning without ever reaching the base methods the two patches above sit on.
        /// Left alone, sea trade would keep vanilla's capital while land trade got ten times it.
        ///
        /// Both are gated on the decorator's own <c>navalCaravan</c> branch, because anything that is not
        /// a convoy was handed to the base model, where the scaling has already been applied -- scaling
        /// again here would give it a hundred.
        ///
        /// Reflected onto the DLC type by name, so with War Sails absent the type does not resolve,
        /// <c>Prepare</c> returns false and neither patch is applied.
        /// </summary>
        [HarmonyPatch]
        private static class NavalInitialTradeGoldPatch
        {
            private static bool Prepare()
            {
                return TargetMethod() != null;
            }

            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(NavalCaravanModelTypeName + ":GetInitialTradeGold");
            }

            private static void Postfix(bool navalCaravan, ref int __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || !navalCaravan)
                {
                    return;
                }

                __result *= PriceScale;
            }
        }

        /// <inheritdoc cref="NavalInitialTradeGoldPatch"/>
        [HarmonyPatch]
        private static class NavalFormingCostPatch
        {
            private static bool Prepare()
            {
                return TargetMethod() != null;
            }

            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(NavalCaravanModelTypeName + ":GetCaravanFormingCost");
            }

            private static void Postfix(bool navalCaravan, ref int __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || !navalCaravan)
                {
                    return;
                }

                __result *= PriceScale;
            }
        }

        private const string NavalCaravanModelTypeName = "NavalDLC.GameComponents.NavalDLCCaravanModel";

        /// <summary>
        /// Moves the payout float for clan-owned caravans onto the model, which is where the rest of the
        /// game already reads it from.
        ///
        /// A caravan's purse is drained to its owner daily, and the game does it down two different
        /// paths. A notable's caravan goes through <c>CalculateOwnerIncomeFromCaravan</c>, which keeps
        /// back <c>GetInitialTradeGold</c> and pays out a fifth of the rest -- so the float tracks the
        /// seed, and scaling one scales the other. A clan's caravan -- the player's, and any a companion
        /// leads -- goes through this method instead, which keeps back a hardcoded 10,000 and pays out a
        /// tenth. That constant does not know the model exists.
        ///
        /// Left alone against a ten-times seed it would be an exploit and a dead end at once: a caravan
        /// formed with 150,000 in its purse would pay 14,000 into clan gold on its first day and go on
        /// bleeding its own capital out until it settled back at 10,000 -- and 10,000 is below the price
        /// of the goods this work exists to let caravans carry, so a player caravan could never trade
        /// them however rich its owner was.
        ///
        /// The replacement is vanilla's arithmetic with the constant read from the model, taking the same
        /// figure for the same caravan that the notable path would. Vanilla's tenth is kept: the two
        /// paths differing is the game's own choice, not something this is trying to correct. Only
        /// caravans are handled -- lord parties and garrisons come through here too and fall straight
        /// through to the original, constant and all.
        /// </summary>
        [HarmonyPatch(typeof(DefaultClanFinanceModel), "AddIncomeFromParty")]
        private static class ClanCaravanPayoutFloatPatch
        {
            /// <summary>Vanilla's share of the surplus, unchanged.</summary>
            private const int PayoutDivisor = 10;

            private static bool Prefix(MobileParty party, Clan clan, bool applyWithdrawals, ref int __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || party == null || clan == null
                    || !party.IsCaravan || party.Owner == null)
                {
                    return true;
                }

                // An RBM supply caravan is not an asset trading for its owner's profit -- its money moves
                // between town purses, not into a clan's gold -- so it pays out nothing and its inflated
                // seed capital is never bled to an owner. See RBMCaravanArrival.
                if (RBMCaravanRegister.IsManaged(party))
                {
                    __result = 0;
                    return false;
                }

                __result = 0;

                // Vanilla's own guard. A caravan the clan leader leads in person is not an asset paying
                // its owner, so it is passed over here exactly as it is there.
                if (!party.IsActive || party.LeaderHero == clan.Leader)
                {
                    return false;
                }

                int floatGold = Campaign.Current.Models.CaravanModel.GetInitialTradeGold(
                    party.Owner, party.HasNavalNavigationCapability, eliteCaravan: false);

                int purse = party.PartyTradeGold;
                if (purse <= floatGold)
                {
                    return false;
                }

                int income = (purse - floatGold) / PayoutDivisor;
                __result = income;

                if (!applyWithdrawals || income <= 0)
                {
                    return false;
                }

                party.PartyTradeGold -= income;

                if (party.LeaderHero != null)
                {
                    SkillLevelingManager.OnTradeProfitMade(party.LeaderHero, income);
                }

                Hero owner = party.Party.Owner;
                if (owner != null && owner.Clan != null && owner.Clan.Leader != null
                    && owner.Clan.Leader.GetPerkValue(DefaultPerks.Trade.GreatInvestor))
                {
                    owner.Clan.AddRenown(DefaultPerks.Trade.GreatInvestor.PrimaryBonus);
                }

                if (clan == Clan.PlayerClan)
                {
                    CampaignEventDispatcher.Instance.OnPlayerEarnedGoldFromAsset(
                        DefaultClanFinanceModel.AssetIncomeType.Caravan, income);
                }

                return false;
            }
        }

        /// <summary>
        /// A managed supply caravan costs its owner clan nothing on the expense side either: its guard
        /// wages are drawn from its own seed trade gold and it is dissolved once its errand is done, so it
        /// should never land on the owner's ledger as a party expense -- not even the top-up a normal
        /// caravan draws once its purse runs low, which is the one path by which a long-lived caravan could
        /// otherwise start bleeding the owner. Forces the expense to zero and skips the original for our
        /// caravans; every other party (real caravans included) falls straight through.
        /// </summary>
        [HarmonyPatch(typeof(DefaultClanFinanceModel), "AddPartyExpense")]
        private static class ManagedCaravanNoExpensePatch
        {
            private static bool Prefix(MobileParty party, ref int __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || party == null || !RBMCaravanRegister.IsManaged(party))
                {
                    return true;
                }
                __result = 0;
                return false;
            }
        }
    }
}
