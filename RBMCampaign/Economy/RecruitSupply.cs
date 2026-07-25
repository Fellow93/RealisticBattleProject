using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Where a soldier's equipment comes from and who is paid for him. Two legs, at two different
    /// moments, and deliberately not tied to each other:
    ///
    ///   GEAR, when the man first offers himself. A volunteer appearing in a notable's roster is a
    ///   villager being armed, so his kit comes off the market that supplies the place -- a town's own,
    ///   a village's from the town it trades with. NO MONEY CHANGES HANDS. The community arms its own
    ///   sons; the stock simply goes.
    ///
    ///   MONEY, when a party takes him. What the recruiting lord pays goes into that same town's OWN
    ///   TREASURY -- raising soldiers is the fief's business as a body, not its shopkeepers' -- and so it
    ///   pays no market fee, the coin never having crossed a counter. Vanilla charges him and then
    ///   destroys it, paying it to nobody at all. The price carries an enlistment premium of five days'
    ///   soldier's wage on top of vanilla's flat tier ladder.
    ///
    /// Keeping the two apart is the point. Gear leaves when a man is raised, whether or not anyone ever
    /// comes for him; coin arrives when someone does. So a war-torn region arms volunteers its lords are
    /// too poor to collect, and a rich lord sweeping through a stripped countryside pays full price for
    /// men in rags -- both of which are the right answer, and neither of which survives if one number is
    /// computed from the other.
    ///
    /// The whole feature lives in this file plus its call sites in
    /// <see cref="SpoilsPool.OnTroopRecruited"/> / <see cref="SpoilsPool.OnUnitRecruited"/>, each tagged
    /// with the comment "RecruitSupply pay". Switch it off at runtime with
    /// RBMConfig.recruitDrawsFromSettlementStock = 0; remove it outright by deleting this file, its
    /// csproj entry, and those tagged lines.
    /// </summary>
    public static class RecruitSupply
    {
        /// <summary>On only when the spoils economy is on and the feature is switched on in config.</summary>
        public static bool IsEnabled
        {
            get { return SpoilsPool.IsEnabled && RBMConfig.RBMConfig.recruitDrawsFromSettlementStock; }
        }

        // ------------------------------------------------------------------ pricing

        /// <summary>
        /// Days of the man's own pay that raising him costs over and above vanilla's price -- the bounty
        /// and the trouble of it. Deliberately priced off his wage rather than off his kit: a kit-priced
        /// recruit costs his lord a fortune up front, which is more than mustering a man should ever be.
        /// </summary>
        private const int EnlistmentWageDays = 5;

        /// <summary>
        /// What one man of this troop costs to raise, on top of what vanilla asks: five days of his own
        /// pay. Read through his wage, so RBM's own tier-based pay table drives it and a dearer soldier
        /// is dearer to enlist without anything here needing to know why.
        /// </summary>
        public static int EnlistmentPremium(CharacterObject character)
        {
            if (character == null || character.IsHero)
            {
                return 0;
            }
            int wage = character.TroopWage;
            return (wage > 0) ? wage * EnlistmentWageDays : 0;
        }

        /// <summary>
        /// What a man of this troop wears, by worth. Not a price -- no money is ever charged from this.
        /// It sizes how much gear he takes off the market when he is raised, and picks stock of the right
        /// tier so a helmet is drawn against helmets of about the right quality.
        ///
        /// Mount-less on purpose: vanilla already charges a flat surcharge for a mounted troop's horse
        /// (150 denars, 500 above level 26), so the mount stays abstract -- paid for on vanilla's terms
        /// and not drawn off the market, which is why <see cref="SpoilsPool.GetKitSlots"/> is asked for
        /// the mount-less slot list too.
        /// </summary>
        public static int KitValue(CharacterObject character)
        {
            return (character == null || character.IsHero) ? 0 : SpoilsPool.GetEquipmentValue(character);
        }

        /// <summary>
        /// Adds an enlistment premium to what a lord pays for a man -- five days of the soldier's own
        /// wage, on top of vanilla's flat tier ladder. Vanilla asks ten denars for a peasant, which is
        /// less than the man earns in a week and nothing at all against what raising him is worth.
        ///
        /// This is what the supplying town is then paid, so it is credited money that genuinely changed
        /// hands instead of money conjured for it. Both recruit paths price through this one model -- the
        /// player's screen via <c>RecruitVolunteerTroopVM.Cost</c> and the AI via
        /// <c>RecruitmentCampaignBehavior</c> -- so patching it here covers both.
        /// </summary>
        /// <remarks>
        /// Skipped when the caller asked for a price <paramref name="withoutItemCost"/>, which is that
        /// flag's nearest sense here: a bare quote for the man, without what comes attached to him.
        ///
        /// The addition lands on the ExplainedNumber's base, so vanilla's recruitment perks scale it
        /// along with the rest of the price -- a lord with a recruiting perk pays less of it too. That is
        /// why the payment reads the model's own result rather than <see cref="EnlistmentPremium"/>
        /// directly: what reaches the town must be what he actually paid, perks and all.
        /// </remarks>
        [HarmonyPatch(typeof(DefaultPartyWageModel))]
        [HarmonyPatch("GetTroopRecruitmentCost")]
        private class OverrideGetTroopRecruitmentCost
        {
            private static void Postfix(CharacterObject troop, bool withoutItemCost, ref ExplainedNumber __result)
            {
                if (!IsEnabled || withoutItemCost)
                {
                    return;
                }
                int premium = EnlistmentPremium(troop);
                if (premium > 0)
                {
                    __result.Add(premium, EnlistmentLine);
                }
            }
        }

        private static readonly TextObject EnlistmentLine = new TextObject("{=RBM_CON_111}Enlistment");

        // ------------------------------------------------------------------ the market that supplies a place

        /// <summary>
        /// The market that arms and is paid for a man raised at <paramref name="settlement"/>. A town
        /// serves itself; a village has no armourer worth the name, so it draws on the town it trades
        /// with. Null when nothing can serve him -- anywhere that is neither town nor village, or a
        /// castle-bound village whose trade bound has not been assigned (a faction with no town left to
        /// trade into), in which case he is armed off-screen and no stock or coin moves.
        /// </summary>
        /// <remarks>
        /// Village.TradeBound needs no Bound fallback: its getter already returns the bound settlement
        /// itself when that is a town, and the separately-assigned trade bound only for castle villages.
        /// </remarks>
        public static Settlement GetSupplyMarket(Settlement settlement)
        {
            if (settlement == null)
            {
                return null;
            }
            if (settlement.IsTown)
            {
                return settlement;
            }
            if (settlement.IsVillage && settlement.Village != null)
            {
                return settlement.Village.TradeBound;
            }
            return null;
        }

        // ------------------------------------------------------------------ leg one: gear, at creation

        // The settlement's volunteers as they stood before the day's roll, counted by troop type. Static
        // and reused rather than allocated per settlement: this runs for every settlement every day.
        private static readonly Dictionary<CharacterObject, int> _volunteersBefore = new Dictionary<CharacterObject, int>();
        private static readonly Dictionary<CharacterObject, int> _volunteersAfter = new Dictionary<CharacterObject, int>();

        /// <summary>
        /// Arms the day's new volunteers out of the market that supplies the settlement they offered
        /// themselves in. A man who steps forward has to be equipped from somewhere, and this is that
        /// somewhere: his kit's worth of stock leaves the stalls, and nobody is paid for it.
        /// </summary>
        /// <remarks>
        /// A before/after diff rather than a hook on the assignment, because the native method has no
        /// seam to hook -- it fills empty slots and promotes filled ones in one pass, from two different
        /// branches.
        ///
        /// Counted as a MULTISET over the whole settlement, not slot by slot. The tail of the native
        /// method RE-SORTS each notable's VolunteerTypes array weakest-to-strongest, so a slot-indexed
        /// diff would read the shuffle as a roster full of new men and arm the town's whole militia over
        /// again every day. Troop counts are invariant under that sort; slot positions are not.
        ///
        /// A promoted volunteer reads as one troop leaving and a better one arriving, so he draws his new
        /// kit whole rather than the difference. That over-draws slightly on the ~1%-a-day promotion
        /// roll, which is tolerable precisely because this leg moves no money: it costs a little extra
        /// stock and cannot put the ledger out.
        /// </remarks>
        [HarmonyPatch(typeof(RecruitmentCampaignBehavior))]
        [HarmonyPatch("UpdateVolunteersOfNotablesInSettlement")]
        private class ArmNewVolunteersFromMarket
        {
            private static void Prefix(Settlement settlement)
            {
                _volunteersBefore.Clear();
                if (!IsEnabled)
                {
                    return;
                }
                CountVolunteers(settlement, _volunteersBefore);
            }

            private static void Postfix(Settlement settlement)
            {
                if (!IsEnabled || _volunteersBefore.Count == 0 && settlement == null)
                {
                    return;
                }
                Settlement market = GetSupplyMarket(settlement);
                if (market == null || market.ItemRoster == null)
                {
                    return;
                }
                _volunteersAfter.Clear();
                CountVolunteers(settlement, _volunteersAfter);

                foreach (KeyValuePair<CharacterObject, int> after in _volunteersAfter)
                {
                    int before;
                    _volunteersBefore.TryGetValue(after.Key, out before);
                    int raised = after.Value - before;
                    if (raised > 0)
                    {
                        DrawKitFromMarket(market, settlement, after.Key, raised);
                    }
                }
            }
        }

        /// <summary>Tallies a settlement's standing volunteers by troop type.</summary>
        private static void CountVolunteers(Settlement settlement, Dictionary<CharacterObject, int> into)
        {
            if (settlement == null || settlement.Notables == null)
            {
                return;
            }
            foreach (Hero notable in settlement.Notables)
            {
                CharacterObject[] volunteers = (notable != null) ? notable.VolunteerTypes : null;
                if (volunteers == null)
                {
                    continue;
                }
                for (int i = 0; i < volunteers.Length; i++)
                {
                    CharacterObject troop = volunteers[i];
                    if (troop == null)
                    {
                        continue;
                    }
                    int running;
                    into.TryGetValue(troop, out running);
                    into[troop] = running + 1;
                }
            }
        }

        /// <summary>
        /// Takes <paramref name="count"/> men's worth of kit off <paramref name="market"/>: for every
        /// slot the troop's gear fills, one item of that class and tier per man, up to the full worth of
        /// what he wears. No money moves -- see the class summary.
        ///
        /// Soft on stock: it takes what the market has and never holds anything up for want of it, since
        /// a picked-clean market would otherwise stop a countryside arming itself at all. What it cannot
        /// supply is simply not drawn, and the man is turned out in whatever he had.
        /// </summary>
        public static void DrawKitFromMarket(Settlement market, Settlement raisedAt, CharacterObject character, int count)
        {
            if (!IsEnabled || market == null || market.ItemRoster == null
                || character == null || character.IsHero || count <= 0)
            {
                return;
            }
            int perManValue = KitValue(character);
            if (perManValue <= 0)
            {
                return;
            }

            ItemRoster stock = market.ItemRoster;
            int budget = perManValue * count;
            int drawn = 0;
            int taken = 0;
            int wanted = 0;
            List<SpoilsPool.SlotPurchase> slots = SpoilsPool.GetKitSlots(character);
            if (slots.Count > 0)
            {
                wanted = slots.Count * count;
                foreach (SpoilsPool.SlotPurchase slot in slots)
                {
                    bool exhausted = false;
                    for (int man = 0; man < count; man++)
                    {
                        int index = UpgradeSupply.FindKitInStock(stock, slot.ItemType, slot.Value);
                        if (index < 0)
                        {
                            break; // this class is out of stock; the rest of the kit is found elsewhere
                        }
                        if (!TryDrawFromStock(market, stock, index, budget - drawn, ref drawn))
                        {
                            exhausted = true;
                            break;
                        }
                        taken++;
                    }
                    // His kit's worth is spent; the remaining slots are not walked at all rather than
                    // scavenged for whatever cheap piece might still fit inside the rounding.
                    if (exhausted)
                    {
                        break;
                    }
                }
            }
            else
            {
                // The troop declares no battle equipment to walk, so there is no class to match: fall
                // back to one generic in-band item per man, as the upgrade draw does in the same spot.
                wanted = count;
                for (int man = 0; man < count; man++)
                {
                    int index = UpgradeSupply.FindKitInStock(stock, perManValue);
                    if (index < 0 || !TryDrawFromStock(market, stock, index, budget - drawn, ref drawn))
                    {
                        break;
                    }
                    taken++;
                }
            }

            if (SpoilsLog.IsEnabled && taken > 0)
            {
                SpoilsLog.Log("RECRUIT", (raisedAt != null ? raisedAt.Name.ToString() : "?") + " raised "
                    + count + "x " + SpoilsLog.Describe(character) + "; armed from " + market.Name
                    + " with " + taken + "/" + wanted + " item(s) worth " + drawn + "d of " + budget
                    + "d kit" + (taken < wanted ? " — market short " + (wanted - taken) : ""));
            }
        }

        /// <summary>
        /// Takes one piece off the stall, so long as <paramref name="remaining"/> of the man's kit
        /// allowance covers it. False when it does not, leaving the stock where it is -- the caller reads
        /// that as the kit being complete.
        /// </summary>
        /// <remarks>
        /// Valued through <see cref="TroopMarketFeedback.UnitPrice"/> rather than off the item's base
        /// value, so a town stripped of mail values what it has left the way it would sell it. NO MONEY
        /// MOVES: the figure only meters how much gear the man has taken and drives the demand signal.
        /// </remarks>
        private static bool TryDrawFromStock(Settlement market, ItemRoster stock, int index, int remaining, ref int drawn)
        {
            ItemObject item = stock.GetItemAtIndex(index);
            if (item == null)
            {
                return false;
            }
            int value = TroopMarketFeedback.UnitPrice(market, item, stock, index);
            if (value > remaining)
            {
                return false;
            }
            stock.AddToCounts(item, -1);
            // A price signal, not a payment: the town restocks what its recruits keep walking off with.
            if (market.Town != null && item.ItemCategory != null)
            {
                RBMTownFoodSupply.RegisterPurchaseDemand(market.Town.MarketData, item.ItemCategory, value);
            }
            drawn += value;
            return true;
        }

        // ------------------------------------------------------------------ leg two: money, at recruitment

        /// <summary>
        /// Set while the player is turning prisoners into soldiers, which reaches us down the same event
        /// as a proper muster but is not one.
        /// </summary>
        private static bool _inPrisonerRecruitment;

        /// <summary>
        /// Keeps prisoner recruitment from paying a town for men it never raised. A prisoner talked round
        /// to the ranks costs conformity, not coin -- <c>RecruitPrisonersCampaignBehavior</c> moves no
        /// gold whatsoever -- yet the player's side of it announces itself through
        /// <c>OnUnitRecruited</c>, the same event a paid muster uses. Crediting a settlement there would
        /// hand it the price of a recruit nobody paid, which is precisely the minting this design exists
        /// to avoid.
        /// </summary>
        /// <remarks>
        /// The AI's side of prisoner recruitment needs no guard: it reports through
        /// <c>OnTroopRecruited</c> with a null settlement, which is already turned away.
        ///
        /// A scoped flag rather than an inspection of the troop, because nothing about the character says
        /// how it was obtained -- the same CharacterObject arrives from the recruit screen. Cleared in a
        /// Finalizer so a throw inside the native method cannot leave it stuck on and silently switch the
        /// payment off for the rest of the session.
        /// </remarks>
        [HarmonyPatch(typeof(RecruitPrisonersCampaignBehavior))]
        [HarmonyPatch("OnMainPartyPrisonerRecruited")]
        private class SuppressPrisonerRecruitPay
        {
            private static void Prefix()
            {
                _inPrisonerRecruitment = true;
            }

            private static void Finalizer()
            {
                _inPrisonerRecruitment = false;
            }
        }

        /// <summary>
        /// Hands the recruit price over to the town that supplies the settlement the men were taken from.
        /// Nothing is drawn here and nothing is charged here: the gear went when they were raised, and
        /// vanilla billed <paramref name="payer"/> a moment ago and then destroyed the money, paying it
        /// to nobody. This redirects the payment already made, so charge and credit are the same number
        /// by construction and the ledger can neither mint nor burn.
        /// </summary>
        public static void PayRecruitPrice(Settlement recruitedAt, PartyBase buyer, Hero payer, CharacterObject character, int count)
        {
            if (!IsEnabled || character == null || character.IsHero || count <= 0)
            {
                return;
            }
            // No buyer, no payment. Covers the paths that reach the recruit events with no hero behind
            // them, and prisoners talked round to the ranks, who cost their captor nothing at all.
            if (payer == null || _inPrisonerRecruitment)
            {
                return;
            }
            Settlement market = GetSupplyMarket(recruitedAt);
            if (market == null)
            {
                return;
            }
            int price = RecruitPricePaid(character, payer) * count;
            if (price <= 0)
            {
                return;
            }
            TroopMarketFeedback.RegisterRecruitPay(market, price);

            if (SpoilsLog.IsEnabled)
            {
                SpoilsLog.Log("RECRUIT", buyer, SpoilsLog.Describe(buyer) + " recruited " + count + "x "
                    + SpoilsLog.Describe(character) + " at " + recruitedAt.Name + "; paid " + price
                    + "d to " + market.Name);
            }
        }

        /// <summary>
        /// What one man of this troop cost <paramref name="payer"/> to recruit -- the same model call,
        /// with the same buyer, that both recruit paths bill through, so the figure here is the figure
        /// charged rather than a reconstruction of it.
        /// </summary>
        private static int RecruitPricePaid(CharacterObject character, Hero payer)
        {
            PartyWageModel model = Campaign.Current != null ? Campaign.Current.Models.PartyWageModel : null;
            if (model == null)
            {
                return 0;
            }
            return model.GetTroopRecruitmentCost(character, payer).RoundedResultNumber;
        }
    }
}
