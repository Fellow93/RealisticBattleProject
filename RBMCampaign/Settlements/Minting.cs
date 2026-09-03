using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace RBMCampaign
{
    /// <summary>
    /// A town's mint. Each day it strikes coin from the silver ore standing in its market: the ore is
    /// consumed off the shelves and its value turned into fresh money. Most of the coin lands in the
    /// citizens' purse -- the market it was struck in -- while the fief's owner, the realm's ruler and
    /// the settlement's own strongbox each take a cut off the top.
    ///
    /// Town-only, matching the citizen purse the bulk of the coin lands in: a village has no market to
    /// hold circulating money and a castle no citizen purse (see <see cref="SettlementWealth.HasCitizenPurse"/>),
    /// and neither trades silver the way a town's market does. The silver read and consumed is the town
    /// market's own stock in <c>Town.Owner.ItemRoster</c>, the same roster the storage and demand systems
    /// meter.
    /// </summary>
    public static class Minting
    {
        /// <summary>The item StringId of silver ore -- see <c>RBMEconomy_trade_goods.xml</c>.</summary>
        private const string SilverOreId = "silver";

        /// <summary>
        /// Coin struck from one unit of silver ore, before any cut. Matches silver ore's own trade value
        /// (<see cref="TradeGoodValues"/>): minting realises the ore's worth as money rather than minting
        /// value from nothing.
        /// </summary>
        public const int CoinsPerOre = 85;

        /// <summary>
        /// Silver ore the mint never touches: a standing seed kept on the market's shelves so the good
        /// itself never fully disappears from trade. Minting only ever draws stock down to this floor.
        /// </summary>
        public const int ReserveOre = 10;

        /// <summary>Fraction of the day's ore, up to the surplus line, that is struck into coin.</summary>
        public const float BaseRate = 0.10f;

        /// <summary>
        /// The stock line above which the mint runs hot: ore held over this is struck at
        /// <see cref="SurplusRate"/> rather than <see cref="BaseRate"/>, so a glut of silver is worked off
        /// faster than a town's ordinary standing stock.
        /// </summary>
        public const int SurplusThreshold = 100;

        /// <summary>Fraction of the day's ore ABOVE the surplus line that is struck into coin.</summary>
        public const float SurplusRate = 0.30f;

        /// <summary>The ruler of the realm's cut of the coin struck, off the top.</summary>
        public const float RulerCutRate = 0.20f;

        /// <summary>The holding lord's cut of the coin struck, off the top.</summary>
        public const float OwnerCutRate = 0.10f;

        /// <summary>The fief's own strongbox cut of the coin struck, off the top.</summary>
        public const float SettlementCutRate = 0.01f;

        /// <summary>
        /// Strikes the day's coin from a town's silver stock and hands out the shares. Ore is consumed off
        /// the market's shelves; its value is split so that the ruler, the owner and the fief's strongbox
        /// take their cuts first and the remainder -- the great bulk of it -- lands in the citizens' purse.
        /// A recipient the town lacks (an unowned town has no lord or ruler) drops its cut back into the
        /// citizens' share rather than losing the coin.
        /// </summary>
        public static void OnDailyTick(Settlement settlement)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null
                || !settlement.IsTown || settlement.Town == null)
            {
                return;
            }

            ItemObject silver = MBObjectManager.Instance?.GetObject<ItemObject>(SilverOreId);
            ItemRoster roster = settlement.Town.Owner?.ItemRoster;
            if (silver == null || roster == null)
            {
                return;
            }

            int stock = roster.GetItemNumber(silver);
            // Nothing is struck until the stock stands above the reserve floor -- the seed ore below it
            // is never touched.
            int mintable = stock - ReserveOre;
            if (mintable <= 0)
            {
                return;
            }

            // Tiered off the day's standing stock: a base slice of the ore up to the surplus line, and a
            // steeper slice of anything held above it, so a glut is worked off faster than ordinary stock.
            int baseOre = (stock < SurplusThreshold) ? stock : SurplusThreshold;
            int surplusOre = (stock > SurplusThreshold) ? (stock - SurplusThreshold) : 0;
            int minted = (int)(baseOre * BaseRate) + (int)(surplusOre * SurplusRate);
            if (minted <= 0)
            {
                return;
            }
            // Never draw stock below the reserve, however hot the day's rate.
            if (minted > mintable)
            {
                minted = mintable;
            }

            roster.AddToCounts(silver, -minted);

            int totalValue = minted * CoinsPerOre;
            // Tax Office: the same clerks who assess the wealth tax weigh the mint's output, so all three
            // cuts off the top are found more completely -- +5/10/15%. The citizens' share is the remainder,
            // so it shrinks by exactly what the cuts grow by and nothing is minted or destroyed.
            float taxOffice = BuildingEffects.TaxFactor(settlement.Town);
            int rulerCut = (int)(totalValue * RulerCutRate * taxOffice);
            int ownerCut = (int)(totalValue * OwnerCutRate * taxOffice);
            int settlementCut = (int)(totalValue * SettlementCutRate * taxOffice);

            Hero owner = settlement.OwnerClan?.Leader;
            Hero ruler = settlement.OwnerClan?.Kingdom?.Leader;
            // A cut with no one to collect it is not destroyed: it stays in the coin struck and so falls
            // through to the citizens' share below (which is the total less whatever the cuts actually take).
            if (owner == null)
            {
                ownerCut = 0;
            }
            if (ruler == null)
            {
                rulerCut = 0;
            }

            int citizenShare = totalValue - rulerCut - ownerCut - settlementCut;

            if (citizenShare > 0)
            {
                SettlementWealth.CreditCitizens(settlement, citizenShare, SettlementWealth.Source.Minting);
            }
            if (settlementCut > 0)
            {
                SettlementWealth.Credit(settlement, settlementCut, SettlementWealth.Source.Minting);
            }
            if (ownerCut > 0)
            {
                GiveGoldAction.ApplyBetweenCharacters(null, owner, ownerCut, true);
            }
            if (rulerCut > 0)
            {
                GiveGoldAction.ApplyBetweenCharacters(null, ruler, rulerCut, true);
            }

            if (EconomyLog.IsEnabled)
            {
                EconomyLog.Log("MINTING", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                    "struck " + minted + " of " + stock + " ore -> " + totalValue + "d"
                    + "  ·  citizens +" + citizenShare + "d, owner +" + ownerCut + "d, ruler +" + rulerCut
                    + "d, treasury +" + settlementCut + "d"
                    + "  ·  silver left " + roster.GetItemNumber(silver)
                    + "  ·  citizen wealth now " + SettlementWealth.GetCitizenWealth(settlement) + "d");
            }
        }
    }
}
