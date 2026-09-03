using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Campaign-layer economy setup that has to run once, at world generation, rather than as a
    /// Harmony patch on a per-tick model.
    /// </summary>
    public class RBMEconomyCampaignBehavior : CampaignBehaviorBase
    {
        /// <summary>
        /// Drops the previous campaign's troop-trade tallies. The constructor is the only safe place:
        /// it runs on OnGameStart, for a new game and a loaded save alike, and BEFORE the save is
        /// read -- so a real save still repopulates through SyncData below, while a new campaign
        /// starts clean instead of inheriting the last one's figures under the same settlement ids.
        /// </summary>
        public RBMEconomyCampaignBehavior()
        {
            TroopMarketFeedback.Reset();
        }

        public override void RegisterEvents()
        {
            // The same hook FoodConsumptionBehavior uses to seed starting food stocks: late enough
            // that every settlement, its bound villages and their hearths are built and linked.
            CampaignEvents.OnNewGameCreatedPartialFollowUpEndEvent.AddNonSerializedListener(this, OnNewGameCreatedFollowUpEnd);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
            CampaignEvents.OnCharacterCreationIsOverEvent.AddNonSerializedListener(this, OnCharacterCreationIsOver);
        }

        /// <summary>
        /// A session launching -- new game or loaded save alike -- rolls the economy log over to a fresh
        /// file, so each play session stands in its own log rather than appending to the last one's.
        /// </summary>
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            EconomyLog.StartCampaignLog();

            // The log file has just rolled over; land the world-gen workshop rolls in it before
            // anything clears their buffer.
            WorkshopVillageBias.FlushPendingStartRolls();

            ArtisanOutput.ResetForNewSession();
            WorkshopVillageBias.ResetForNewSession();
            PartyTradeFlow.Reset();
            TradeTariff.Reset();
            WorkshopPurse.Reset();
            RBMWorkshopCycle.Reset();
            RBMWorkshopExpense.Reset();
            WorkshopDiagnostics.Reset();
            TownStorage.Reset();
        }

        /// <summary>
        /// The end-of-day state of every settlement, which is what the rest of the log's lines add up
        /// to: a fief against the countryside equilibrium it is drifting toward, a village against the
        /// hearths that drive its output. Nothing here changes state -- it only writes down what the
        /// day left behind -- and it costs nothing at all with the log off.
        /// </summary>
        private void OnDailyTickSettlement(Settlement settlement)
        {
            // Ahead of the logging gate: the trade tally has to age whether or not anyone is reading
            // the log, or a town would keep a passing army's custom on its books forever.
            TroopMarketFeedback.DecayDaily(settlement);

            // Ahead of the gate for the same reason: the tally has to be cleared each day whether or
            // not anyone is reading it, or it accumulates for the life of the session.
            PartyTradeFlow.FlushDaily(settlement);

            if (!EconomyLog.IsEnabled || settlement == null)
            {
                return;
            }

            string name = settlement.Name != null ? settlement.Name.ToString() : settlement.StringId;

            if (settlement.IsVillage)
            {
                Village village = settlement.Village;
                EconomyLog.Log("DAILY", name,
                    "village  hearth " + EconomyLog.Fmt(village.Hearth)
                    + "  ·  store " + RBMVillageProduction.StoredUnits(village)
                    + "/" + village.GetWarehouseCapacity()
                    + "  ·  state " + village.VillageState
                    + "  ·  bound to " + (village.TradeBound != null ? village.TradeBound.Name.ToString() : "-")
                    + "  ·  owner " + (settlement.OwnerClan != null ? settlement.OwnerClan.Name.ToString() : "-"));
                return;
            }

            if (!settlement.IsFortification)
            {
                return;
            }

            Town town = settlement.Town;
            float target = RBMProsperityEquilibrium.TargetProsperity(settlement);
            EconomyLog.Log("DAILY", name,
                (town.IsTown ? "town   " : "castle ")
                + " prosperity " + EconomyLog.Fmt(town.Prosperity)
                + (town.IsTown
                    ? (" of countryside " + EconomyLog.Fmt(target) + " (gap " + EconomyLog.Fmt(target - town.Prosperity) + ")")
                    : " (vanilla)")
                + "  ·  food " + EconomyLog.Fmt(town.FoodStocks)
                + " change " + EconomyLog.Fmt(town.FoodChange)
                + (settlement.IsStarving ? "  STARVING" : "")
                + "  ·  loyalty " + EconomyLog.Fmt(town.Loyalty)
                + ", militia " + EconomyLog.Fmt(town.Militia)
                + ", security " + EconomyLog.Fmt(town.Security)
                + "  ·  gold " + town.Gold
                + (town.IsTown && TroopMarketFeedback.RecentSpend(town) > 0
                    ? (" (troop trade " + TroopMarketFeedback.RecentSpend(town) + ")")
                    : ""));

            // Castles are on vanilla prosperity, so there is no equilibrium to report for them.
            if (town.IsTown)
            {
                LogProsperityEquilibrium(name, town, target);
                ArtisanOutput.LogDaily(town);
            }
        }

        /// <summary>
        /// The prosperity equilibrium, term by term. The DAILY line above says where a fief SITS
        /// relative to its countryside; this says what is actually moving it there, and how fast.
        ///
        /// Worth its own line because prosperity is now pulled by forces that fight each other: the
        /// countryside term drags toward the hearth target (having first cancelled vanilla's housing
        /// ladder, which is why both appear), the famine penalty drags down while a town cannot feed
        /// itself, and loyalty, buildings and policies push either way. A fief stuck short of its
        /// target looks identical in the DAILY line whether the pull is being outvoted or is simply
        /// slow, and those want different fixes.
        ///
        /// The projection is deliberately naive -- gap over today's rate, no compounding -- because
        /// its job is to flag "this will never converge", not to predict a date. A negative or absurd
        /// figure is the signal.
        /// </summary>
        private void LogProsperityEquilibrium(string name, Town town, float target)
        {
            float change = town.ProsperityChange;
            float gap = target - town.Prosperity;

            StringBuilder terms = new StringBuilder();
            foreach (var line in town.ProsperityChangeExplanation.GetLines())
            {
                if (terms.Length > 0)
                {
                    terms.Append(", ");
                }
                terms.Append(line.name).Append(" ").Append(line.number >= 0f ? "+" : "").Append(EconomyLog.Fmt(line.number));
            }

            // Converging only if today's change actually points at the gap rather than away from it.
            string closing;
            if (MathF.Abs(gap) < 0.5f)
            {
                closing = "at rest";
            }
            else if (change * gap <= 0f)
            {
                closing = "DIVERGING";
            }
            else
            {
                closing = "~" + EconomyLog.Fmt(MathF.Abs(gap / change)) + "d to close";
            }

            EconomyLog.Log("PROSPER", name,
                (town.IsTown ? "town   " : "castle ")
                + " " + EconomyLog.Fmt(town.Prosperity) + " → " + EconomyLog.Fmt(target)
                + "  (gap " + EconomyLog.Fmt(gap) + ", " + closing + ")"
                + "  ·  change " + (change >= 0f ? "+" : "") + EconomyLog.Fmt(change) + "/day"
                + (terms.Length > 0 ? ("  ·  " + terms) : ""));
        }

        /// <summary>
        /// Sets the player's opening purse to a flat <see cref="RBMConfig.RBMConfig.campaignStartingGold"/>
        /// instead of whatever the backstory choices happened to add up to. RBM reprices most of the campaign --
        /// troop upgrades are paid out of a spoils purse, gear and trade goods cost several times the
        /// vanilla figure -- so vanilla's few hundred denars leaves the player unable to take any of
        /// the opening decisions the economy is built around.
        ///
        /// Fires once, when character creation finalizes, which is after the narrative stages have
        /// applied their own gold; a loaded save never passes through here, so an existing campaign
        /// keeps the gold it was saved with.
        /// </summary>
        private void OnCharacterCreationIsOver()
        {
            Hero player = Hero.MainHero;
            if (player == null)
            {
                return;
            }

            player.ChangeHeroGold(RBMConfig.RBMConfig.campaignStartingGold - player.Gold);
        }

        public override void SyncData(IDataStore dataStore)
        {
            TroopMarketFeedback.SyncData(dataStore);
        }

        /// <summary>
        /// Starts every town ON its countryside equilibrium (see
        /// <see cref="RBMProsperityEquilibrium"/>) rather than on vanilla's hand-authored figure.
        /// Vanilla's starting prosperity bears no fixed relation to the land around a fief, so
        /// without this the campaign would open with every settlement on the map mid-correction --
        /// large towns sliding for years and small ones climbing -- which reads as the mod being
        /// broken rather than as an economy finding its level.
        ///
        /// Towns only, matching the equilibrium term itself: castles are outside the countryside
        /// model and keep whatever prosperity the world was authored with.
        ///
        /// New games only. A loaded save keeps the prosperity it was saved with; this fires on the
        /// new-game path alone, so it cannot rewrite an existing campaign's settlements. Those will
        /// converge on their own through the equilibrium term instead.
        /// </summary>
        private void OnNewGameCreatedFollowUpEnd(CampaignGameStarter starter)
        {
            // Rebuild the countryside sums first. This hook runs after trade bounds are assigned, but
            // the cache may already hold a copy taken mid-worldgen, before castle villages had one.
            RBMProsperityEquilibrium.InvalidateHearthCache();

            // Start every fief on its own equilibrium so the map does not open mid-correction: a town
            // on its trade-bound hearths, a castle on the average hearth of its own bound villages. A
            // castle whose bounds are not readable yet (target 0) keeps its authored prosperity.
            foreach (Settlement settlement in Settlement.All)
            {
                if (settlement.IsTown)
                {
                    settlement.Town.Prosperity = RBMProsperityEquilibrium.TargetProsperity(settlement);
                }
                else if (settlement.IsCastle)
                {
                    float castleTarget = RBMProsperityEquilibrium.CastleTargetProsperity(settlement);
                    if (castleTarget > 0f)
                    {
                        settlement.Town.Prosperity = castleTarget;
                    }
                }
            }
        }
    }
}
