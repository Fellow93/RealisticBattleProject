using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// Decides whether a workshop runs a production cycle. RBM owns the whole decision rather than
    /// bending vanilla's constants from the outside.
    ///
    /// Vanilla asks three questions in two slightly different orders (the player and notable gates have
    /// already drifted apart), and it asks them about a number nobody ever pays: the cycle's FULL retail
    /// output value. The town-gold test therefore demanded a town hold the retail value of a velvet bolt
    /// in cash before a weavery could weave one, while the payout the shop actually received was capped
    /// far below it. Gate and ledger disagreed by construction, which is why RBM used to need a
    /// ref-argument prefix (<c>WorkshopPayoutCap</c>) purely to fake agreement between them.
    ///
    /// Here the gate tests the same figure <see cref="RBMWorkshopSettlement"/> will actually pay, so the
    /// two cannot disagree. Vanilla's speed-inverted margin floor is replaced by a proportional one, and
    /// RBM's storage-glut skip (formerly <c>WorkshopHeadroomGate</c>) is folded in as the first test.
    /// </summary>
    /// <remarks>
    /// Both gate methods are replaced by skip-prefixes at <see cref="Priority.First"/>, so RBM's decision
    /// is made ahead of any other mod's prefix. That is the explicit trade of owning the rules: a second
    /// workshop-economy mod loaded alongside RBM will conflict here.
    ///
    /// The prefixes are inert when <c>rbmCampaignEnabled</c> is off (they return true and vanilla runs),
    /// which matters because <c>ApplyHarmonyPatches</c> can leave them applied across a toggle.
    /// </remarks>
    public static class RBMWorkshopCycle
    {
        /// <summary>What the batch must clear ON TOP of its materials, as a fraction of them.</summary>
        /// <remarks>
        /// Vanilla's floor was <c>inputCost + 200/ConversionSpeed</c>, which is backwards: it demands the
        /// MOST margin from the SLOWEST recipes -- velvet, wine, oil, the very goods RBM wants running.
        /// A percentage over materials plus the batch's own wage is the actual business test: does this
        /// batch cover what it consumes and what it pays the hands, with something left over?
        /// </remarks>
        public const float MarginRate = 0.15f;

        public enum Reason
        {
            Ran,
            Glutted,
            Margin,
            ShopBroke,
            TownBroke
        }

        public struct Verdict
        {
            public bool Allowed;
            public Reason Why;
            public int Payout;
            public int InputCost;
        }

        // The last decision made about each shop, for the diagnostics to READ rather than recompute.
        // Session-only; a shop that has not been ticked since load simply has no entry.
        private static readonly Dictionary<Workshop, Verdict> _lastVerdict = new Dictionary<Workshop, Verdict>();

        /// <summary>Drops the previous session's verdicts. Diagnostics only, so a session hook is enough.</summary>
        public static void Reset()
        {
            _lastVerdict.Clear();
        }

        /// <summary>The most recent decision about a shop, if one has been made this session.</summary>
        public static bool TryGetLastVerdict(Workshop workshop, out Verdict verdict)
        {
            if (workshop != null && _lastVerdict.TryGetValue(workshop, out verdict))
            {
                return true;
            }
            verdict = default(Verdict);
            return false;
        }

        /// <summary>
        /// Whether a cycle settles in gold at all.
        /// </summary>
        /// <remarks>
        /// <c>effectCapital</c> is vanilla's own switch for "this recipe settles in gold" -- it is set for
        /// any recipe whose goods are all trade goods. RBM adds one condition: the hidden <c>artisans</c>
        /// bench never settles in gold, whatever it is making.
        ///
        /// Every town has that bench in slot 0, and it is not a business in the sense the other twelve
        /// types are. It is the townspeople themselves: the butcher jointing the cow, the smith at his
        /// tier-1 blades. The bench was once made to trade for real -- citizens paying the shop for its
        /// output, the shop paying a wage back for the labour -- and measured over fourteen logged days
        /// that circuit turned out to be almost entirely self-cancelling: the wage credit is the output
        /// debit coming home. It also did real harm, because the shop's float is a claim on the town's
        /// money; in the poorest towns it was measured holding MORE than the townspeople had between
        /// them, and since citizen wealth gated production those towns locked -- exactly the ones with
        /// the worst output.
        ///
        /// So a man working his own stock does not buy from himself or pay himself. Materials come off
        /// the shelf and finished goods go back onto it; what the day added is a better shelf, not a
        /// bigger pile of denars. The one thing that still moves is the market fee on the materials
        /// drawn -- the stall is the town's, not his (see <see cref="RBMWorkshopSettlement"/>).
        ///
        /// This is the single home for what used to be three separate <c>ref bool effectCapital</c>
        /// prefixes in <c>WorkshopPurse</c>.
        /// </remarks>
        public static bool SettlesInGold(Workshop workshop, bool vanillaEffectCapital)
        {
            return vanillaEffectCapital
                && workshop != null
                && workshop.WorkshopType != null
                && !workshop.WorkshopType.IsHidden;
        }

        /// <summary>
        /// True when every one of a recipe's outputs is already at or over the town's storage ceiling.
        /// </summary>
        /// <remarks>
        /// Vanilla dumps a cycle's output straight onto the town roster, past the <see cref="TownStorage"/>
        /// ceiling -- which only ever gated goods arriving from OUTSIDE. A low-demand output whose input
        /// is cheap (pottery above all) therefore piles to many times its cap, because nothing on the
        /// production side reads the glut. This is the quantity stop that closes that hole.
        ///
        /// A multi-output recipe is skipped only when EVERY output is full, so a wanted co-product is
        /// never starved for a glutted one (cow -&gt; meat + hides still runs while hides has room).
        /// </remarks>
        private static bool AllOutputsGlutted(WorkshopType.Production production, Town town)
        {
            if (town == null || production.Outputs == null || production.Outputs.Count == 0)
            {
                return false;
            }
            for (int i = 0; i < production.Outputs.Count; i++)
            {
                ItemCategory category = production.Outputs[i].Item1;
                if (!TownStorage.OutputHasNoRoom(town, category))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>The recipe's first output, for naming a skipped cycle on the SHOPCAP line.</summary>
        internal static string PrimaryOutput(WorkshopType.Production production)
        {
            return (production.Outputs != null && production.Outputs.Count > 0 && production.Outputs[0].Item1 != null)
                ? production.Outputs[0].Item1.StringId
                : "?";
        }

        /// <summary>
        /// True when a PLAYER shop is set to bank its output in the owner's warehouse and that warehouse
        /// still has room. Such a cycle never reaches the town shelf, so a TOWN-market glut is no reason
        /// to skip it -- the whole point of the warehouse is to stockpile a good the market cannot
        /// absorb. Once it fills, vanilla spills the overflow back onto the town market and the glut gate
        /// rightly applies again. Notable shops have no warehouse (the ratio stays 0), so this never
        /// fires for them.
        /// </summary>
        private static bool SendsOutputToWarehouseWithRoom(Workshop workshop)
        {
            if (Campaign.Current == null || workshop == null || workshop.Settlement == null)
            {
                return false;
            }

            IWorkshopWarehouseCampaignBehavior warehouse =
                Campaign.Current.GetCampaignBehavior<IWorkshopWarehouseCampaignBehavior>();
            if (warehouse == null || warehouse.GetStockProductionInWarehouseRatio(workshop) <= 0f)
            {
                return false;
            }

            return warehouse.GetWarehouseItemRosterWeight(workshop.Settlement)
                   < Campaign.Current.Models.WorkshopModel.WarehouseCapacity;
        }

        /// <summary>
        /// Vanilla's <c>IsWarehouseAtLimit</c> (private), reimplemented over the public warehouse seam.
        /// Only used for vanilla's player escape from the town-cash test.
        /// </summary>
        private static bool IsWarehouseAtLimit(Settlement settlement)
        {
            if (Campaign.Current == null || settlement == null)
            {
                return false;
            }
            IWorkshopWarehouseCampaignBehavior warehouse =
                Campaign.Current.GetCampaignBehavior<IWorkshopWarehouseCampaignBehavior>();
            if (warehouse == null)
            {
                return false;
            }
            return warehouse.GetWarehouseItemRosterWeight(settlement)
                   >= Campaign.Current.Models.WorkshopModel.WarehouseCapacity;
        }

        /// <summary>
        /// The whole produce-or-not decision, for both owner paths.
        /// </summary>
        /// <remarks>
        /// The only genuine asymmetries in vanilla are the warehouse and the owner-gold fallback on
        /// expenses; everything else was duplicated code that had already drifted. One decision serves
        /// both, with <paramref name="allOutputsWillBeSentToWarehouse"/> always false on the notable path.
        ///
        /// Vanilla skips its margin test before <c>Campaign.Current.GameStarted</c> (WCB:709, 779) so the
        /// world can be seeded; the town-cash test is likewise meaningless before then, since
        /// <see cref="RBMWorkshopSettlement"/> moves no gold either. Both are skipped here for the same
        /// reason.
        /// </remarks>
        public static Verdict Decide(WorkshopType.Production production, Workshop workshop,
            int inputMaterialCost, int rawOutputIncome, bool effectCapital, bool allOutputsWillBeSentToWarehouse)
        {
            Verdict verdict = default(Verdict);
            verdict.InputCost = inputMaterialCost;

            Town town = (workshop != null && workshop.Settlement != null) ? workshop.Settlement.Town : null;
            bool hidden = workshop != null && workshop.WorkshopType != null && workshop.WorkshopType.IsHidden;
            bool started = Campaign.Current != null && Campaign.Current.GameStarted;
            int wage = hidden ? 0 : RBMWorkshopExpense.WagePerCycle;

            // 1. Glut. Refused before anything is consumed, so a gated cycle wastes nothing -- the clay
            //    stays on the shelf. Counted on its own SHOPCAP line, not among the SHOPBLOCK reasons.
            if (RBMConfig.RBMConfig.workshopHeadroomGateEnabled
                && !SendsOutputToWarehouseWithRoom(workshop)
                && AllOutputsGlutted(production, town))
            {
                verdict.Allowed = false;
                verdict.Why = Reason.Glutted;
                Publish(workshop, verdict);
                WorkshopDiagnostics.CountCapped(workshop.Settlement, PrimaryOutput(production));
                return verdict;
            }

            // 2. Payout -- the figure the settlement step will actually pay, not the retail value.
            verdict.Payout = RBMWorkshopSettlement.ValueOfOutputs(town, production, rawOutputIncome);

            // 3. Margin. The artisans use the bare test: they pay no wage and vanilla gave them no margin
            //    term either, because they are not a business trying to clear one.
            if (started)
            {
                bool clearsMargin = hidden
                    ? (verdict.Payout > inputMaterialCost)
                    : ((float)verdict.Payout >= inputMaterialCost * (1f + MarginRate) + wage);
                if (!clearsMargin)
                {
                    verdict.Allowed = false;
                    verdict.Why = Reason.Margin;
                    Publish(workshop, verdict);
                    return verdict;
                }
            }

            // 4. Shop solvency. Skipped for the artisans: they settle in kind, so there is nothing for
            //    them to be short of.
            if (!hidden && workshop != null && workshop.Capital < inputMaterialCost + wage)
            {
                verdict.Allowed = false;
                verdict.Why = Reason.ShopBroke;
                Publish(workshop, verdict);
                return verdict;
            }

            // 5. Town cash, on the ceilinged payout rather than the retail value -- which is what makes
            //    town-broke rare rather than universal. Vanilla's player escape stands: a run banked
            //    entirely into a FULL warehouse costs the town nothing, so its cash cannot refuse it.
            if (started && SettlesInGold(workshop, effectCapital) && town != null && town.Gold < verdict.Payout)
            {
                if (!(allOutputsWillBeSentToWarehouse && IsWarehouseAtLimit(workshop.Settlement)))
                {
                    verdict.Allowed = false;
                    verdict.Why = Reason.TownBroke;
                    Publish(workshop, verdict);
                    return verdict;
                }
            }

            verdict.Allowed = true;
            verdict.Why = Reason.Ran;
            Publish(workshop, verdict);
            return verdict;
        }

        private static void Publish(Workshop workshop, Verdict verdict)
        {
            if (workshop != null)
            {
                _lastVerdict[workshop] = verdict;
            }
        }

        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "CanNotableWorkshopProduceThisCycle")]
        private static class NotableGate
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(WorkshopType.Production production, Workshop workshop,
                int inputMaterialCost, int outputIncome, bool effectCapital, ref bool __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || workshop == null || workshop.Settlement == null)
                {
                    return true;
                }
                __result = Decide(production, workshop, inputMaterialCost, outputIncome, effectCapital,
                    allOutputsWillBeSentToWarehouse: false).Allowed;
                return false;
            }
        }

        [HarmonyPatch(typeof(WorkshopsCampaignBehavior), "CanPlayerWorkshopProduceThisCycle")]
        private static class PlayerGate
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(WorkshopType.Production production, Workshop workshop,
                int inputMaterialCost, int outputIncome, bool effectCapital,
                bool allOutputsWillBeSentToWarehouse, ref bool __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || workshop == null || workshop.Settlement == null)
                {
                    return true;
                }
                __result = Decide(production, workshop, inputMaterialCost, outputIncome, effectCapital,
                    allOutputsWillBeSentToWarehouse).Allowed;
                return false;
            }
        }
    }
}
