using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;

namespace RBMCampaign
{
    /// <summary>
    /// What a fief's buildings are actually worth, in RBM's terms.
    ///
    /// Vanilla hangs a small table of effects off every building type and reads it through
    /// <c>Town.AddEffectOfBuildings</c>. Those effects stay where they are -- a Barracks still cuts
    /// garrison wages, a Waterworks still eats less food -- but most of them were written for an economy
    /// where nothing had a price. RBM's seams need the one thing the vanilla table cannot give them: the
    /// plain level of a named building, 0 to 3, so a rate or a bill can be scaled by it.
    ///
    /// A town and a castle build different things with the same purpose: a town raises Fortifications, a
    /// castle raises Fortifications of its own, and the two are separate <see cref="BuildingType"/>
    /// objects that mean the same to us. Every lookup here takes both and returns whichever the fief
    /// actually has. Where a castle has no equivalent at all -- it keeps no Marketplace, no Tax Office,
    /// no Waterworks -- the castle argument is null and the tier is simply 0.
    /// </summary>
    public static class BuildingEffects
    {
        /// <summary>
        /// The level of whichever of the two building types this fief owns, 0 when it owns neither or has
        /// not begun it. Safe on a null town, on a fief mid-load, and before the building lists exist.
        /// </summary>
        public static int Tier(Town town, BuildingType townType, BuildingType castleType)
        {
            if (town == null || town.Buildings == null)
            {
                return 0;
            }
            foreach (Building building in town.Buildings)
            {
                BuildingType type = building.BuildingType;
                if (type == null)
                {
                    continue;
                }
                if (type == townType || (castleType != null && type == castleType))
                {
                    int level = building.CurrentLevel;
                    return (level < 0) ? 0 : ((level > 3) ? 3 : level);
                }
            }
            return 0;
        }

        // ------------------------------------------------------------------ the buildings RBM reads

        public static int Fortifications(Town town)
        {
            return Tier(town, DefaultBuildingTypes.SettlementFortifications, DefaultBuildingTypes.CastleFortifications);
        }

        public static int Barracks(Town town)
        {
            return Tier(town, DefaultBuildingTypes.SettlementBarracks, DefaultBuildingTypes.CastleBarracks);
        }

        public static int TrainingFields(Town town)
        {
            return Tier(town, DefaultBuildingTypes.SettlementTrainingFields, DefaultBuildingTypes.CastleTrainingFields);
        }

        public static int GuardHouse(Town town)
        {
            return Tier(town, DefaultBuildingTypes.SettlementGuardHouse, DefaultBuildingTypes.CastleGuardHouse);
        }

        public static int Mason(Town town)
        {
            return Tier(town, DefaultBuildingTypes.SettlementMason, DefaultBuildingTypes.CastleMason);
        }

        /// <summary>Towns only -- a castle keeps no market of its own.</summary>
        public static int Marketplace(Town town)
        {
            return Tier(town, DefaultBuildingTypes.SettlementMarketplace, null);
        }

        /// <summary>Towns only.</summary>
        public static int TaxOffice(Town town)
        {
            return Tier(town, DefaultBuildingTypes.SettlementTaxOffice, null);
        }

        /// <summary>A town's Warehouse and a castle's Granary are the same store of food.</summary>
        public static int FoodStore(Town town)
        {
            return Tier(town, DefaultBuildingTypes.SettlementWarehouse, DefaultBuildingTypes.CastleGranary);
        }

        /// <summary>Towns only.</summary>
        public static int Waterworks(Town town)
        {
            return Tier(town, DefaultBuildingTypes.SettlementWaterworks, null);
        }

        public static int Roads(Town town)
        {
            return Tier(town, DefaultBuildingTypes.SettlementRoadsAndPaths, DefaultBuildingTypes.CastleRoadsAndPaths);
        }

        // ------------------------------------------------------------------ derived rates

        /// <summary>Fortifications: what a fief pays to keep its garrison and watch, 1 / 0.95 / 0.9.</summary>
        public static float MaintenanceFactor(Town town)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return 1f;
            }
            int tier = Fortifications(town);
            return (tier <= 1) ? 1f : (1f - 0.05f * (tier - 1));
        }

        /// <summary>Barracks: what it costs to put a man in the garrison or the watch, −5/10/15%.</summary>
        public static float SpawnCostFactor(Town town)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return 1f;
            }
            return 1f - 0.05f * Barracks(town);
        }

        /// <summary>Barracks: extra men a day, on top of what the fief's money buys.</summary>
        public static int BarracksGrowth(Town town)
        {
            return RBMConfig.RBMConfig.rbmCampaignEnabled ? Barracks(town) : 0;
        }

        /// <summary>Training Fields: what a promotion costs the fief, −5/10/15%.</summary>
        public static float UpgradeCostFactor(Town town)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return 1f;
            }
            return 1f - 0.05f * TrainingFields(town);
        }

        /// <summary>Guard House: the extra fee, in percentage points, on caravan and player trade.</summary>
        public static float GuardHouseTariffBonus(Town town)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return 0f;
            }
            switch (GuardHouse(town))
            {
                case 1: return 0.003f;
                case 2: return 0.006f;
                case 3: return 0.010f;
                default: return 0f;
            }
        }

        /// <summary>Tax Office: vanilla's own TaxPerDay factor, 1 / 1.05 / 1.1 / 1.15.</summary>
        public static float TaxFactor(Town town)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return 1f;
            }
            return 1f + 0.05f * TaxOffice(town);
        }

        /// <summary>Marketplace: vanilla's own TariffIncome factor, 1 / 1.1 / 1.2 / 1.3.</summary>
        public static float TariffFactor(Town town)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return 1f;
            }
            return 1f + 0.1f * Marketplace(town);
        }

        /// <summary>Warehouse / Granary: days of eating the fief can keep in store.</summary>
        public static int FoodStockDays(Town town)
        {
            return 10 + 10 * FoodStore(town);
        }
    }
}
