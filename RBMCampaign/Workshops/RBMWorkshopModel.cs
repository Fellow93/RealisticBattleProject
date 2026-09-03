using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// RBM's workshop constants, owned outright as a game model rather than bent from the outside by
    /// Harmony postfixes on <c>DefaultWorkshopModel</c>'s getters.
    ///
    /// Every number here exists because RBM's prices are roughly an order of magnitude above vanilla's.
    /// A shop's Capital is its own purse -- it buys inputs from it and pays its overhead out of it -- and
    /// a single RBM input draw (cotton for a velvet weavery, say) can run to thousands, so vanilla's
    /// 10,000 float would be spent in a couple of busy days. The founding capital is raised to 60,000 and
    /// the low-capital warning line is expressed as half of it, so the two can never drift apart the way
    /// they did while both were separate patched constants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why subclass <c>WorkshopModel</c> and not <c>DefaultWorkshopModel</c>.</b>
    /// <c>GameModelsManager.GetGameModel&lt;T&gt;</c> walks the registered list backwards, so the model
    /// added last wins, and <c>MBGameModel&lt;T&gt;.Initialize</c> hands it the model it displaced as
    /// <c>BaseModel</c>. That makes the correct shape a decorator: override only what RBM owns and
    /// delegate the rest to <c>BaseModel</c>. NavalDLC registers <c>NavalDLCWorkshopModel</c> in exactly
    /// this way and it too only decorates -- its two naval policy factors live inside
    /// <c>GetEffectiveConversionSpeedOfProduction</c> and are reached solely through the
    /// <c>BaseModel</c> chain. Subclassing <c>DefaultWorkshopModel</c> here would silently drop them
    /// whenever RBM happened to be registered after Naval.
    /// </para>
    /// <para>
    /// Because the constants are read straight off the model, three consumers RBM never patches now
    /// agree for free: <c>Workshop.Initialize</c> (a new shop's starting Capital),
    /// <c>ChangeOwnerOfWorkshopAction</c> (which resets Capital to <c>InitialCapital</c> when a shop
    /// changes hands, so existing shops climb to the new float as they turn over rather than all at
    /// once), and <c>DefaultClanFinanceModel.AddPlayerExpenseForWorkshops</c> plus
    /// <c>ClanFinanceWorkshopItemVM</c>, which both test <c>CapitalLowLimit</c>. The last of those is
    /// notable: <c>DefaultClanFinanceModel</c> has a static-constructor trap that makes patching it
    /// hazardous, and reading it through a model avoids the problem entirely.
    /// </para>
    /// <para>
    /// The <c>rbmCampaignEnabled</c> guard on the three constants is belt-and-braces: the model is only
    /// registered under that toggle (<c>RBM/SubModule.cs</c>, <c>OnGameStart</c>), but the toggle can be
    /// flipped in the settings screen mid-session while the model stays in the chain, and the constants
    /// are the members a live toggle would visibly disagree about.
    /// </para>
    /// </remarks>
    public class RBMWorkshopModel : WorkshopModel
    {
        /// <summary>The purse a workshop is founded with. Vanilla is 10,000.</summary>
        private const int FoundingCapital = 60000;

        /// <summary>
        /// The standing overhead only -- rent, upkeep, the bench itself. Vanilla is 100. The per-batch
        /// payroll is a separate charge and stays in <see cref="WorkshopPurse"/> until phase 4 of the
        /// workshop rework moves it into a dedicated expense step.
        /// </summary>
        private const int StandingOverhead = 250;

        public override int DaysForPlayerSaveWorkshopFromBankruptcy => base.BaseModel.DaysForPlayerSaveWorkshopFromBankruptcy;

        /// <summary>
        /// Half the founding float, as vanilla's 5,000 is half of its 10,000. Below this line the daily
        /// overhead is billed to the owner's treasury rather than the shop, and the clan-screen capital
        /// row turns into a warning -- a signal that is only meaningful at a sensible fraction of the
        /// purse. A shop carried over from an older save at 10,000 sits under this line and climbs out
        /// through production; nothing is minted to top it up.
        /// </summary>
        public override int CapitalLowLimit
        {
            get
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled) return base.BaseModel.CapitalLowLimit;
                return FoundingCapital / 2;
            }
        }

        public override int InitialCapital
        {
            get
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled) return base.BaseModel.InitialCapital;
                return FoundingCapital;
            }
        }

        public override int DailyExpense
        {
            get
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled) return base.BaseModel.DailyExpense;
                return StandingOverhead;
            }
        }

        public override int WarehouseCapacity => base.BaseModel.WarehouseCapacity;

        public override int DefaultWorkshopCountInSettlement => base.BaseModel.DefaultWorkshopCountInSettlement;

        public override int MaximumWorkshopsPlayerCanHave => base.BaseModel.MaximumWorkshopsPlayerCanHave;

        public override int GetMaxWorkshopCountForClanTier(int tier)
        {
            return base.BaseModel.GetMaxWorkshopCountForClanTier(tier);
        }

        /// <summary>
        /// Delegated: vanilla's formula already adds <c>InitialCapital / 5</c>, and <c>InitialCapital</c>
        /// resolves through the model chain to RBM's figure, so the raised founding float flows into the
        /// purchase price on its own.
        /// </summary>
        public override int GetCostForPlayer(Workshop workshop)
        {
            return base.BaseModel.GetCostForPlayer(workshop);
        }

        public override int GetCostForNotable(Workshop workshop)
        {
            return base.BaseModel.GetCostForNotable(workshop);
        }

        public override Hero GetNotableOwnerForWorkshop(Workshop workshop)
        {
            return base.BaseModel.GetNotableOwnerForWorkshop(workshop);
        }

        /// <summary>
        /// Layers RBM's town-sized artisan bench onto whatever the rest of the chain produced.
        /// </summary>
        /// <remarks>
        /// Taken first from <c>BaseModel</c> so vanilla's policies, building effects, governor trait and
        /// two perks -- and NavalDLC's two policy factors, if it is loaded below us -- all survive, then
        /// scaled with <c>AddFactor</c> rather than overwritten so each of those keeps its proportional
        /// effect. The scale itself, its per-day cache and its <c>SHOPSCALE</c> log line stay in
        /// <see cref="ArtisanOutput"/>; this is only the seam it is applied at, replacing the Harmony
        /// postfix that used to do the same job on <c>DefaultWorkshopModel</c>.
        /// </remarks>
        public override ExplainedNumber GetEffectiveConversionSpeedOfProduction(Workshop workshop, float speed, bool includeDescriptions)
        {
            ExplainedNumber result = base.BaseModel.GetEffectiveConversionSpeedOfProduction(workshop, speed, includeDescriptions);

            if (!RBMConfig.RBMConfig.rbmCampaignEnabled
                || workshop == null
                || workshop.WorkshopType == null)
            {
                return result;
            }

            float scale = ArtisanOutput.Scale(workshop);
            if (scale == 1f) return result;

            TextObject text = workshop.WorkshopType.IsHidden ? ArtisanOutput.ScaleText : ArtisanOutput.OwnedScaleText;
            result.AddFactor(scale - 1f, text);
            return result;
        }

        public override int GetConvertProductionCost(WorkshopType workshopType)
        {
            return base.BaseModel.GetConvertProductionCost(workshopType);
        }

        public override bool CanPlayerSellWorkshop(Workshop workshop, out TextObject explanation)
        {
            return base.BaseModel.CanPlayerSellWorkshop(workshop, out explanation);
        }

        public override float GetTradeXpPerWarehouseProduction(EquipmentElement production)
        {
            return base.BaseModel.GetTradeXpPerWarehouseProduction(production);
        }
    }
}
