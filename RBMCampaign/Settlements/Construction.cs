using System.Collections.Generic;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Building work as something a fief pays for, out of a budget, day by day.
    ///
    /// Vanilla construction is a number that arrives from nowhere. A town turns a hundredth of its
    /// prosperity into "construction points" every day, spends them on whatever project is at the head
    /// of its queue, and no purse anywhere is touched: a wall goes up because the town is populous, not
    /// because anyone bought the stone. The one place gold enters is the owner's reserve, and vanilla
    /// burns exactly 500 of it a day whether or not the work needs it.
    ///
    /// Here a point of construction is a coin. One point, one denar -- so a project's cost, multiplied
    /// up by <see cref="RBMConfig.RBMConfig.buildingCostMultiplier"/>, reads as the price of the work in
    /// money, and the fief has to find that money before a stone is laid. Three things pay for a day's
    /// points, in order of what they cost the treasury:
    ///
    ///   FREE LABOUR -- prisoners in the settlement's pit, and (phase 2) the men a Guard House keeps at
    ///   work. They are already fed and housed by the fief; the work they do costs nothing more.
    ///
    ///   MATERIALS -- clay and hardwood bought off the settlement's own market. A load of clay is worth
    ///   a great deal of building and costs what the market asks for it, so a town with a full yard
    ///   builds far more cheaply per coin than one buying every day's work in wages.
    ///
    ///   WAGES -- the rest, a coin a point, of which half reaches the townsmen who swing the hammers and
    ///   half is simply spent: rope, scaffolding, spoilage, the work itself.
    ///
    /// The budget is <see cref="Town.BoostBuildingProcess"/>, vanilla's own reserve field -- already
    /// saved, already shown in the town management screen, already fillable by the owner. Each day the
    /// fief tips <see cref="RBMConfig.RBMConfig.constructionBudgetShare"/> of its treasury into it, and
    /// the player may top it up by hand. Nothing else funds building, so a poor fief builds slowly and a
    /// rich one quickly, and a project is a standing claim on the treasury rather than a free background
    /// process.
    ///
    /// The owner's own boost needs no patch of its own any more: vanilla's
    /// <c>BuildingHelper.BoostBuildingProcessWithGold</c> already moves his gold into the reserve and
    /// nowhere else, and the reserve is now a real pool that this file spends down. (The old
    /// <c>ConstructionLabour</c> paid the townsmen the instant the money was pledged, which was right
    /// when nothing ever spent the reserve and is double payment now.)
    /// </summary>
    public static class Construction
    {
        /// <summary>Points a point of prosperity is worth per day: 90 men-equivalent x 0.4.</summary>
        public const float PointsPerProsperity = 36f;

        /// <summary>Points one prisoner adds to the day's ceiling.</summary>
        public const float PointsPerPrisoner = 60f;

        /// <summary>How much of a prisoner's work costs the treasury nothing.</summary>
        public const float FreePointsPerPrisoner = 30f;

        /// <summary>The largest share of a day's work that bought materials may cover.</summary>
        public const float MaterialShare = 0.5f;

        /// <summary>Of a wage-paid point, the half that reaches the townsmen. The rest is spent.</summary>
        public const float SalaryShare = 0.5f;

        /// <summary>Points of work that wear out one load of tools.</summary>
        public const float PointsPerTool = 50000f;

        /// <summary>What a day is worth when there is no queued project and the fief builds on its own.</summary>
        public const float IdleProjectShare = 0.25f;

        // The tool wear each settlement is carrying, in loads. Keyed by settlement id, persisted with the
        // wealth behaviour: a fief that has worn out a load of tools it could not buy owes it until the
        // market has one, and that debt must survive a save or it is cleared by reloading.
        private static Dictionary<string, float> _toolDebt = new Dictionary<string, float>();

        // What each settlement's last building day cost its reserve, for the map tooltip. Not persisted:
        // it is a display figure and refills on the first tick after a load.
        private static Dictionary<string, int> _lastSpend = new Dictionary<string, int>();

        /// <summary>Denars the settlement's reserve paid out on its most recent building day.</summary>
        public static int LastDailySpend(Settlement settlement)
        {
            int value;
            return (settlement != null && _lastSpend.TryGetValue(settlement.StringId, out value)) ? value : 0;
        }

        /// <summary>Drops the previous campaign's tool debts. Called from the behaviour CONSTRUCTOR.</summary>
        public static void Reset()
        {
            _toolDebt = new Dictionary<string, float>();
            _marketCache = new Dictionary<Settlement, MarketCache>();
            _lastSpend = new Dictionary<string, int>();
        }

        public static void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("RBM_constructionToolDebt", ref _toolDebt);
            if (_toolDebt == null)
            {
                _toolDebt = new Dictionary<string, float>();
            }
        }

        private static float GetToolDebt(Settlement settlement)
        {
            float value;
            return _toolDebt.TryGetValue(settlement.StringId, out value) ? value : 0f;
        }

        private static void SetToolDebt(Settlement settlement, float value)
        {
            _toolDebt[settlement.StringId] = (value < 0f) ? 0f : value;
        }

        // ---------------------------------------------------------------- phase 2 seams

        /// <summary>
        /// The Guard House tier. Its gaolers keep a standing body of convicts at work on the fief's
        /// projects -- men who are housed and fed whatever they do, so half of what they build costs
        /// nothing (see <see cref="DailyCapacity"/> and <see cref="FreeLabour"/>).
        /// </summary>
        private static int GuardHouseTier(Town town)
        {
            return BuildingEffects.GuardHouse(town);
        }

        /// <summary>
        /// The Mason tier, which raises both the day's ceiling and the work each coin buys. This REPLACES
        /// vanilla's flat ConstructionPerDay 3/6/9, which was worth nothing at RBM's scale.
        /// </summary>
        private static int MasonTier(Town town)
        {
            return BuildingEffects.Mason(town);
        }

        private static float MasonCapacityFactor(Town town)
        {
            return 1f + 0.1f * MasonTier(town);
        }

        private static float MasonEfficiency(Town town)
        {
            return 1f + 0.05f * MasonTier(town);
        }

        // ---------------------------------------------------------------- perks

        /// <summary>The most the governor, perks, feat and market goods together may multiply a day's work by.</summary>
        public const float MaxPerkFactor = 2f;

        /// <summary>
        /// Vanilla's construction bonuses -- the governor's Engineering skill, Forced Labor, Carpenters /
        /// Military Planner, Stonecutters, Confidence, Self-Made Man, the Battanian feat and the "construction
        /// from market" goods -- turned from free points into a multiplier on FUNDED work.
        /// </summary>
        /// <remarks>
        /// Vanilla adds each of these on top of a baseline of prosperity/100 points. Running exactly the
        /// same helpers against exactly that baseline and taking the ratio keeps every perk's relative
        /// weight as TaleWorlds tuned it, while the absolute figure scales with whatever the fief actually
        /// pays for. Loyalty and the Mason are deliberately excluded here: they have their own terms.
        /// </remarks>
        public static float PerkFactor(Town town)
        {
            if (town == null || town.Settlement == null)
            {
                return 1f;
            }
            float baseline = town.Prosperity * 0.01f;
            if (baseline < 1f)
            {
                baseline = 1f;
            }
            ExplainedNumber en = new ExplainedNumber(baseline);

            Hero governor = town.Governor;
            bool governorHere = governor != null && governor.CurrentSettlement != null && governor.CurrentSettlement.Town == town;
            bool queued = !town.BuildingsInProgress.IsEmpty();
            BuildingType queuedType = queued ? town.BuildingsInProgress.Peek().BuildingType : null;

            if (governorHere)
            {
                SkillHelper.AddSkillBonusForTown(DefaultSkillEffects.TownProjectBuildingBonus, town, ref en);
                PerkHelper.AddPerkBonusForTown(DefaultPerks.Steward.ForcedLabor, town, ref en);
                if (queued)
                {
                    int prisoners = Prisoners(town);
                    if (governor.GetPerkValue(DefaultPerks.Steward.ForcedLabor) && prisoners > 0)
                    {
                        en.AddFactor(MathF.Min(0.3f, prisoners / 3f * DefaultPerks.Steward.ForcedLabor.SecondaryBonus));
                    }
                    if (town.IsCastle)
                    {
                        PerkHelper.AddPerkBonusForTown(DefaultPerks.Engineering.MilitaryPlanner, town, ref en);
                    }
                    else if (town.IsTown)
                    {
                        PerkHelper.AddPerkBonusForTown(DefaultPerks.Engineering.Carpenters, town, ref en);
                    }
                    if (queuedType == DefaultBuildingTypes.SettlementFortifications
                        || queuedType == DefaultBuildingTypes.CastleBarracks
                        || queuedType == DefaultBuildingTypes.SettlementBarracks)
                    {
                        PerkHelper.AddPerkBonusForTown(DefaultPerks.Engineering.Stonecutters, town, ref en);
                    }
                }
            }

            int productionGoods = 0;
            foreach (Town.SellLog log in town.SoldItems)
            {
                if (log.Category != null && log.Category.Properties == ItemCategory.Property.BonusToProduction)
                {
                    productionGoods += log.Number;
                }
            }
            if (productionGoods > 0)
            {
                en.Add(0.25f * productionGoods);
            }

            if (queuedType != null && queuedType.IsMilitaryProject)
            {
                PerkHelper.AddPerkBonusForTown(DefaultPerks.TwoHanded.Confidence, town, ref en);
            }
            if (queuedType == DefaultBuildingTypes.SettlementMarketplace)
            {
                PerkHelper.AddPerkBonusForTown(DefaultPerks.Trade.SelfMadeMan, town, ref en);
            }
            // The game build RBM compiles against has no FeatHelper; the feat is applied by hand the way
            // MilitiaUpkeep applies the Battanian militia feat.
            if (town.OwnerClan != null && town.OwnerClan.Culture != null
                && town.OwnerClan.Culture.HasFeat(DefaultCulturalFeats.BattanianConstructionFeat))
            {
                en.AddFactor(DefaultCulturalFeats.BattanianConstructionFeat.EffectBonus);
            }

            float factor = en.ResultNumber / baseline;
            return MBMath.ClampFloat(factor, 1f, MaxPerkFactor);
        }

        // ---------------------------------------------------------------- capacity

        /// <summary>
        /// Vanilla's loyalty curve, kept as it stands: a devoted town works a fifth harder, a sullen one
        /// drags, and one on the edge of revolt does not build at all.
        /// </summary>
        public static float LoyaltyFactor(Town town)
        {
            float loyalty = town.Loyalty;
            if (loyalty <= 25f)
            {
                return 0f;
            }
            if (loyalty >= 75f)
            {
                return 1f + MBMath.Map(loyalty, 75f, 100f, 0f, 0.2f);
            }
            if (loyalty <= 50f)
            {
                return 1f - MBMath.Map(loyalty, 25f, 50f, 0.5f, 0f);
            }
            return 1f;
        }

        private static int Prisoners(Town town)
        {
            if (town.Settlement == null || town.Settlement.Party == null || town.Settlement.Party.PrisonRoster == null)
            {
                return 0;
            }
            return town.Settlement.Party.PrisonRoster.TotalManCount;
        }

        /// <summary>
        /// The most work the fief could get done in a day if money were no object -- hands, not coin.
        /// </summary>
        public static float DailyCapacity(Town town)
        {
            if (town == null)
            {
                return 0f;
            }
            float prosperity = (town.Prosperity > 0f) ? town.Prosperity : 0f;
            float cap = prosperity * PointsPerProsperity
                        + Prisoners(town) * PointsPerPrisoner
                        + GuardHouseTier(town) * 0.6f * prosperity;
            cap *= MasonCapacityFactor(town);
            cap *= LoyaltyFactor(town);
            return (cap > 0f) ? cap : 0f;
        }

        /// <summary>The part of the day's ceiling that costs the treasury nothing at all.</summary>
        public static float FreeLabour(Town town)
        {
            if (town == null)
            {
                return 0f;
            }
            float prosperity = (town.Prosperity > 0f) ? town.Prosperity : 0f;
            float free = Prisoners(town) * FreePointsPerPrisoner + GuardHouseTier(town) * 0.3f * prosperity;
            free *= LoyaltyFactor(town);
            return (free > 0f) ? free : 0f;
        }

        // ---------------------------------------------------------------- who is paid, and from whose shelves

        /// <summary>
        /// The market a fief's building work is transacted in: where the wages land, where the clay and
        /// the tools are bought, and where the fee on that trade is levied.
        ///
        /// A town is its own labour market. A CASTLE IS NOT: it holds a single pool and has no citizen
        /// purse at all (see <see cref="SettlementWealth.HasCitizenPurse"/>), so every credit to "its
        /// citizens" is silently dropped and the money it spends building would simply vanish. A castle
        /// hires its masons, buys its brick and replaces its tools through the nearest town it is not at
        /// war with, exactly as its watch is armed (<c>MilitiaUpkeep.ResolveCastleSupplyMarket</c>) and its
        /// garrison's promotions are paid for.
        ///
        /// Null for a castle that can reach no such town -- a faction reduced to that one keep. Its
        /// building then has no market at all: no materials are bought, and the wage coin it spends leaves
        /// the ledger for good, which is the same treatment every other unreachable-market case gets.
        /// </summary>
        public static Settlement LabourMarket(Settlement settlement)
        {
            if (settlement == null)
            {
                return null;
            }
            if (SettlementWealth.HasCitizenPurse(settlement))
            {
                return settlement;
            }
            // The sweep is O(all towns), and this is not only asked once a day: the town management screen
            // reads the construction model on every refresh, and that reads the projection, and that needs
            // to know who would be paid. Cached for a day, which is far shorter than the war, peace or
            // capture that could change the answer.
            MarketCache cached;
            double now = CampaignTime.Now.ToDays;
            if (_marketCache.TryGetValue(settlement, out cached) && now < cached.ExpiryDay)
            {
                return cached.Market;
            }

            IFaction faction = settlement.MapFaction;
            Town town = SettlementHelper.FindNearestTownToSettlement(settlement, MobileParty.NavigationType.Default,
                s => s.MapFaction != null && faction != null
                    && (s.MapFaction == faction || !faction.IsAtWarWith(s.MapFaction)));
            Settlement market = (town != null) ? town.Settlement : null;
            _marketCache[settlement] = new MarketCache { Market = market, ExpiryDay = now + 1.0 };
            return market;
        }

        private struct MarketCache
        {
            public Settlement Market;
            public double ExpiryDay;
        }

        // Keyed on live Settlement objects, so it must be dropped with the rest of the campaign's state --
        // see Reset.
        private static Dictionary<Settlement, MarketCache> _marketCache = new Dictionary<Settlement, MarketCache>();

        // ---------------------------------------------------------------- the day's plan

        /// <summary>
        /// A day's construction worked out but not yet carried out: how much of the ceiling each source
        /// of labour covers, what it costs the reserve, and what the total comes to. The tick executes
        /// one of these; the UI projection reads one and throws it away.
        /// </summary>
        internal struct DayPlan
        {
            public float Capacity;
            public float Free;
            public float MaterialPoints;
            public float CashPoints;
            public int MaterialSpend;
            public int CashSpend;
            /// <summary>The day's wage for the men working bought materials -- all of it reaches them.</summary>
            public int MaterialSalary;
            public float MasonFactor;
            /// <summary>Governor skill/perks, culture feat and market goods, as one multiplier (see <see cref="PerkFactor"/>).</summary>
            public float PerkFactor;
            public float Points;
            public List<ConstructionMaterials.Purchase> Purchases;
        }

        /// <summary>
        /// Works out what a settlement can build today, given its hands, its reserve and its market. Does
        /// not spend anything, take anything off the shelves or touch the tool debt.
        /// </summary>
        internal static DayPlan PlanDay(Town town, float capacityShare)
        {
            return PlanDay(town, capacityShare, LabourMarket(town.Settlement));
        }

        internal static DayPlan PlanDay(Town town, float capacityShare, Settlement market)
        {
            DayPlan plan = default(DayPlan);
            plan.MasonFactor = MasonEfficiency(town);
            plan.PerkFactor = PerkFactor(town);
            plan.Capacity = DailyCapacity(town) * capacityShare;
            if (plan.Capacity < 1f)
            {
                return plan;
            }

            int budget = town.BoostBuildingProcess;
            if (budget < 0)
            {
                budget = 0;
            }

            plan.Free = MBMath.ClampFloat(FreeLabour(town) * capacityShare, 0f, plan.Capacity);
            float paid = plan.Capacity - plan.Free;

            if (paid >= 1f && budget > 0 && market != null)
            {
                // Materials first: a coin spent on clay buys far more work than a coin spent in wages, so
                // the fief uses its market before it opens its purse to labourers -- but never for more
                // than half a day's work, because a building is not made of clay alone.
                //
                // Only when there is a market to pay: a castle cut off from every friendly town has no
                // merchants to buy from, and buying anyway would take the goods for nothing.
                float materialRoom = MBMath.ClampFloat(plan.Capacity * MaterialShare, 0f, paid);
                int spent;
                plan.Purchases = ConstructionMaterials.Plan(town, market.Town, materialRoom, budget, out plan.MaterialPoints, out spent);
                plan.MaterialSpend = spent;
                budget -= spent;
                paid -= plan.MaterialPoints;
            }

            // Wages. Half of every coin reaches the townsmen; the whole coin buys a point of work.
            if (paid >= 1f && budget > 0)
            {
                float cash = (paid < budget) ? paid : budget;
                plan.CashPoints = (cash > 0f) ? cash : 0f;
                plan.CashSpend = (int)plan.CashPoints;
            }

            budget -= plan.CashSpend;

            // The wage bill also covers the men working with bought materials -- they are paid for their
            // day like anyone else, on top of what the clay cost -- but only as far as the reserve
            // stretches, so the day can never spend more than the fief actually has banked.
            int materialSalary = (int)(plan.MaterialPoints * SalaryShare);
            if (materialSalary > budget)
            {
                materialSalary = (budget > 0) ? budget : 0;
            }
            plan.MaterialSalary = materialSalary;

            plan.Points = (plan.Free + plan.MaterialPoints + plan.CashPoints) * plan.MasonFactor * plan.PerkFactor;
            return plan;
        }

        // ---------------------------------------------------------------- the daily tick

        /// <summary>
        /// The fief's building day: fund the reserve out of the treasury, then spend it on the project.
        /// Runs from the RBM settlement pass after the day's other bills, so building is paid for out of
        /// what is left once the garrison, the watch and the administration have been kept.
        /// </summary>
        public static void OnDailyTick(Settlement settlement)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null)
            {
                return;
            }
            Town town = settlement.Town;
            if (town == null)
            {
                return;
            }

            DepositBudget(settlement, town);
            _lastSpend[settlement.StringId] = 0;

            // Vanilla's rule, kept: nothing is built while an army is at the gate.
            if (settlement.IsUnderSiege)
            {
                return;
            }

            // Vanilla dequeued a finished project at the head of the tick; that tick is ours now.
            if (!town.BuildingsInProgress.IsEmpty() && town.BuildingsInProgress.Peek().CurrentLevel == 3)
            {
                town.BuildingsInProgress.Dequeue();
            }

            float share;
            Building target = SelectTarget(town, out share);
            if (target == null)
            {
                return;
            }

            // Resolved once for the whole tick: a castle's sweep for the nearest friendly town is not
            // something to repeat per purchase.
            Advance(settlement, town, target, share, LabourMarket(settlement));
        }

        /// <summary>
        /// Tips the day's share of the fief's treasury into the construction reserve.
        /// </summary>
        /// <remarks>
        /// Runs whether or not there is anything to build: an idle fief banks its building money, which
        /// is what lets a castle save up for a wall it could never fund out of one day's income.
        /// </remarks>
        private static void DepositBudget(Settlement settlement, Town town)
        {
            float share = RBMConfig.RBMConfig.constructionBudgetShare;
            if (share <= 0f)
            {
                return;
            }
            int wanted = (int)(SettlementWealth.GetSettlementWealth(settlement) * share);
            if (wanted <= 0)
            {
                return;
            }
            int taken = SettlementWealth.Debit(settlement, wanted, SettlementWealth.Source.Construction);
            if (taken > 0)
            {
                town.BoostBuildingProcess += taken;
            }
        }

        /// <summary>
        /// What the fief works on today: the head of its queue, or -- with nothing queued -- a quarter
        /// day spent shoring up whatever is least built.
        /// </summary>
        private static Building SelectTarget(Town town, out float capacityShare)
        {
            capacityShare = 1f;
            if (!town.BuildingsInProgress.IsEmpty())
            {
                return town.BuildingsInProgress.Peek();
            }

            capacityShare = IdleProjectShare;
            Building best = null;
            int count = 0;
            foreach (Building building in town.Buildings)
            {
                if (building.BuildingType == null || building.BuildingType.IsDailyProject || building.CurrentLevel >= 3)
                {
                    continue;
                }
                if (best == null || building.CurrentLevel < best.CurrentLevel)
                {
                    best = building;
                    count = 1;
                }
                else if (building.CurrentLevel == best.CurrentLevel)
                {
                    // Reservoir pick, so the fief does not always favour whichever building the roster
                    // happens to list first.
                    count++;
                    if (MBRandom.RandomInt(count) == 0)
                    {
                        best = building;
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// Buys and books a day of work on one project: materials off the shelves, wages to the
        /// townsmen, tools worn through, and the progress that came of it.
        /// </summary>
        private static void Advance(Settlement settlement, Town town, Building target, float capacityShare, Settlement market)
        {
            DayPlan plan = PlanDay(town, capacityShare, market);
            if (plan.Points < 1f && plan.MaterialSpend == 0)
            {
                return;
            }
            int reserveBefore = town.BoostBuildingProcess;

            // Materials leave the shelves and their price leaves the reserve for the merchants' purses.
            if (plan.Purchases != null)
            {
                ConstructionMaterials.Execute(market, plan.Purchases);
            }

            // Wages: the whole coin leaves the reserve, half of it reaching the townsmen and the rest
            // going the way of every consumable a building site eats.
            int wageCoin = plan.CashSpend + plan.MaterialSalary;
            int toTownsmen = (int)(plan.CashSpend * SalaryShare) + plan.MaterialSalary;
            if (toTownsmen > 0 && market != null)
            {
                // The labour market, which for a castle is the town its masons and carters come from --
                // crediting the castle itself would drop the money on the floor, it having no such purse.
                SettlementWealth.CreditCitizens(market, toTownsmen, SettlementWealth.Source.Construction);
            }

            int spend = plan.MaterialSpend + wageCoin;
            town.BoostBuildingProcess -= spend;
            if (town.BoostBuildingProcess < 0)
            {
                town.BoostBuildingProcess = 0;
            }

            float points = plan.Points;

            // Tools. A day's work wears through picks, saws and barrows; the fief replaces them off its
            // market. A site that has worn out a load it cannot replace works at half speed until it can.
            bool short_ = WearTools(settlement, town, points, market);
            if (short_)
            {
                points *= 0.5f;
            }

            if (points > 0f)
            {
                target.BuildingProgress += points;
                BuildingHelper.CheckIfBuildingIsComplete(target);
            }

            // Materials, wages and any tools bought, as one figure for the map tooltip.
            _lastSpend[settlement.StringId] = reserveBefore - town.BoostBuildingProcess;

            if (EconomyLog.IsEnabled)
            {
                EconomyLog.Log("BUILD", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                    target.Name + " lvl " + target.CurrentLevel
                    + "  ·  cap " + (int)plan.Capacity
                    + "  ·  points " + (int)points
                    + " (free " + (int)plan.Free + ", materials " + (int)plan.MaterialPoints + ", wages " + (int)plan.CashPoints + ")"
                    + "  ·  spent " + spend + "d (materials " + plan.MaterialSpend + "d)"
                    + (short_ ? "  ·  TOOLS SHORT" : "")
                    + ((plan.MasonFactor > 1f || plan.PerkFactor > 1f)
                        ? "  ·  x" + EconomyLog.Fmt(plan.MasonFactor) + " mason x" + EconomyLog.Fmt(plan.PerkFactor) + " perks" : "")
                    + "  ·  market " + ((market == null) ? "none"
                        : (market == settlement ? "own" : (market.Name != null ? market.Name.ToString() : market.StringId)))
                    + "  ·  progress " + (int)target.BuildingProgress + "/" + target.GetConstructionCost()
                    + "  ·  reserve " + town.BoostBuildingProcess + "d");
            }
        }

        /// <summary>
        /// Adds the day's wear to the fief's tool debt and buys what it can off the market to clear it.
        /// Returns true when a whole load is still owed and the market has none -- the site is short of
        /// tools and works at half speed.
        /// </summary>
        private static bool WearTools(Settlement settlement, Town town, float points, Settlement market)
        {
            float debt = GetToolDebt(settlement) + points / PointsPerTool;
            while (debt >= 1f)
            {
                if (!ConstructionMaterials.BuyOneTool(town, market))
                {
                    SetToolDebt(settlement, debt);
                    return true;
                }
                debt -= 1f;
            }
            SetToolDebt(settlement, debt);
            return false;
        }

        // ---------------------------------------------------------------- projection for the UI

        private static readonly TextObject CapText = new TextObject("{=!}Labour available");
        private static readonly TextObject FreeText = new TextObject("{=!}Prisoners");
        private static readonly TextObject MaterialText = new TextObject("{=!}Materials");
        private static readonly TextObject WageText = new TextObject("{=!}Reserve");
        private static readonly TextObject MasonText = new TextObject("{=!}Masons");
        private static readonly TextObject ToolsText = new TextObject("{=!}Tools shortage");
        private static readonly TextObject LoyaltyText = new TextObject("{=!}Loyalty");
        private static readonly TextObject PerkText = new TextObject("{=!}Governor, perks and market");

        /// <summary>
        /// What the town screen and the days-to-complete estimate should read: today's funded work, by
        /// where it comes from. Read-only -- nothing here spends, buys or wears anything.
        /// </summary>
        public static ExplainedNumber Project(Town town, bool includeDescriptions)
        {
            ExplainedNumber result = new ExplainedNumber(0f, includeDescriptions);
            if (town == null || town.Settlement == null || town.Settlement.IsUnderSiege)
            {
                return result;
            }

            float share = town.BuildingsInProgress.IsEmpty() ? IdleProjectShare : 1f;
            DayPlan plan = PlanDay(town, share);
            if (plan.Capacity < 1f)
            {
                if (LoyaltyFactor(town) <= 0f)
                {
                    result.LimitMax(0f, LoyaltyText);
                }
                return result;
            }

            if (plan.Free > 0f)
            {
                result.Add(plan.Free, FreeText);
            }
            if (plan.MaterialPoints > 0f)
            {
                result.Add(plan.MaterialPoints, MaterialText);
            }
            if (plan.CashPoints > 0f)
            {
                result.Add(plan.CashPoints, WageText);
            }
            if (plan.MasonFactor > 1f)
            {
                result.AddFactor(plan.MasonFactor - 1f, MasonText);
            }
            if (plan.PerkFactor > 1f)
            {
                result.AddFactor(plan.PerkFactor - 1f, PerkText);
            }
            // The ceiling is only worth showing when it is what binds -- an empty reserve is the usual
            // limit, and reporting the hands available then would read as a bonus that is not there.
            if (plan.Free + plan.MaterialPoints + plan.CashPoints >= plan.Capacity - 1f)
            {
                result.Add(0f, CapText);
            }
            if (GetToolDebt(town.Settlement) >= 1f)
            {
                result.AddFactor(-0.5f, ToolsText);
            }
            result.LimitMin(0f);
            return result;
        }
    }
}
