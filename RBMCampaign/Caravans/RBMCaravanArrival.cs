using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using RC = RBMConfig.RBMConfig;

namespace RBMCampaign
{
    /// <summary>
    /// The road end of the supply-caravan system: it registers our caravans as they are created, keeps
    /// vanilla's caravan trade brain off them, and runs the sale when one reaches its destination.
    ///
    /// The money rule the whole system rests on lives here: a managed caravan never touches a native
    /// trade path. Vanilla <c>OnSettlementEntered</c> and <c>HourlyTickParty</c> are suppressed for our
    /// caravans, and the sale moves money only through <see cref="SettlementWealth"/> -- never
    /// <c>SellItemsAction</c>/<c>ChangeGold</c> -- so the settlement gold funnel is never involved and
    /// money cannot double-move. Mirrors <see cref="VillagerDelivery.SellCargo"/>.
    /// </summary>
    internal static class RBMCaravanArrival
    {
        // The most of a destination's citizen wealth one caravan may take in a single visit, so it cannot
        // strip a market bare and leave nothing for the next caravan (or the town's own consumers).
        private const float MaxSpendFraction = 0.5f;

        /// <summary>
        /// Runs when a managed caravan enters a settlement. A no-op at the source or any waypoint; at the
        /// destination it sells the cargo into the town and marks the caravan for dissolving.
        /// </summary>
        public static void Handle(MobileParty caravan, Settlement settlement)
        {
            if (caravan == null || settlement == null)
            {
                return;
            }
            if (!RBMCaravanRegister.TryGetOrder(caravan.StringId, out RBMCaravanRegister.Order order))
            {
                return;
            }

            if (order.State == RBMCaravanRegister.StateEnRoute)
            {
                HandleDestinationArrival(caravan, settlement, order);
            }
            else if (order.State == RBMCaravanRegister.StateReturning)
            {
                HandleHomecoming(caravan, settlement, order);
            }
            // Spawning (the source-entry during creation) or a waypoint town: nothing to do.
        }

        /// <summary>
        /// The caravan reaches its destination: sell what the town can take and afford, then turn it
        /// around to carry the takings -- and any goods the town could not buy -- home to the source.
        /// </summary>
        private static void HandleDestinationArrival(MobileParty caravan, Settlement settlement, RBMCaravanRegister.Order order)
        {
            Settlement dst = RBMCaravanRegister.FindSettlement(order.DestId);
            if (dst == null || settlement != dst)
            {
                return; // a waypoint, not the destination yet
            }

            Settlement investSrc = RBMCaravanRegister.FindSettlement(order.SourceId);
            int proceeds = 0;
            // Deliver only into a live town of the caravan's own realm. If the destination was captured out
            // of the source's kingdom (or besieged/razed) between dispatch and arrival, move no goods or
            // coin across the new border -- carry the load home instead. This mirrors the hourly divert and
            // catches the case where a caravan reaches the town the same tick it changed hands.
            if (dst.Town != null && dst.IsTown && dst.SiegeEvent == null && SameRealm(investSrc, dst))
            {
                // Prop a struggling destination up with a repayable capital injection -- for a goods
                // caravan this comes BEFORE the sale so a broke town can then afford the goods; for a pure
                // relief caravan (empty manifest) it is the whole point of the trip. ApplyInjection
                // self-checks whether the pairing qualifies, so it is safe to always attempt.
                RBMCaravanInvestment.ApplyInjection(investSrc, dst);

                if (order.Goods != null && order.Goods.Count > 0)
                {
                    proceeds = SellCargo(caravan, dst, order);
                }
            }
            else
            {
                CaravanLog.Log("ABORT", CaravanLog.Name(dst),
                    (SameRealm(investSrc, dst) ? "cannot sell/deliver here" : "destination no longer in the realm")
                    + " — heading home");
            }

            // Carry the takings (and anything unsold) home. The source is paid on arrival there.
            RBMCaravanRegister.SetProceeds(caravan.StringId, proceeds);
            RBMCaravanRegister.SetState(caravan.StringId, RBMCaravanRegister.StateReturning);

            Settlement source = RBMCaravanRegister.FindSettlement(order.SourceId);
            if (source != null)
            {
                // Home the same way it came: over land where there is a road, and by ship (keeping the
                // land legs) where the crossing needs one. A caravan that sailed out already carries its
                // ship, so the return simply reuses it.
                RBMCaravanDispatch.RouteBetween(caravan, dst, source, source.Culture);

                // Re-provision for the whole road home so the guards stay fed on the return leg too. Done
                // after routing so the estimate sees the actual return leg and navigation.
                RBMCaravanDispatch.StockFood(caravan, source, order.Goods);
            }
            else
            {
                // Nowhere to take it back to: finish here.
                RBMCaravanRegister.SetState(caravan.StringId, RBMCaravanRegister.StateDone);
            }
        }

        /// <summary>
        /// The caravan arrives back at its source: the destination's coin reaches the source citizens (the
        /// source town taking its tariff out of it), any unsold goods return to the source market, and the
        /// caravan is marked for dissolving.
        /// </summary>
        private static void HandleHomecoming(MobileParty caravan, Settlement settlement, RBMCaravanRegister.Order order)
        {
            Settlement source = RBMCaravanRegister.FindSettlement(order.SourceId);
            if (source == null || settlement != source)
            {
                return; // a waypoint, not home yet
            }

            bool sourceIsMarket = source.Town != null && source.IsTown;
            int proceeds = order.Proceeds;

            // Citizens buying from citizens: the destination's payment lands in the source market now, and
            // the source town takes its market fee out of that same money -- never charged on income the
            // town has not received.
            int credited = 0;
            if (proceeds > 0 && sourceIsMarket)
            {
                SettlementWealth.CreditCitizens(source, proceeds, SettlementWealth.Source.Caravan);
                TradeTariff.Levy(source, proceeds);
                credited = proceeds;
            }

            // Every good the destination could not take comes back to the source market rather than lost.
            int returned = 0;
            if (sourceIsMarket && order.Goods != null)
            {
                foreach (RBMCaravanRegister.GoodLot lot in order.Goods)
                {
                    ItemObject good = RBMCaravanRegister.FindItem(lot.GoodId);
                    if (good == null)
                    {
                        continue;
                    }
                    int back = caravan.ItemRoster.GetItemNumber(good);
                    if (back > 0)
                    {
                        source.ItemRoster.AddToCounts(new EquipmentElement(good), back);
                        caravan.ItemRoster.AddToCounts(new EquipmentElement(good), -back);
                        returned += back;
                    }
                }
            }

            CaravanLog.Log("RETURN", CaravanLog.Name(source),
                "home from " + CaravanLog.Name(RBMCaravanRegister.FindSettlement(order.DestId))
                + "  ·  citizens paid " + credited + "d"
                + (returned > 0 ? "  ·  " + returned + " units returned unsold" : "")
                + "  ·  purse now " + SettlementWealth.GetCitizenWealth(source) + "d");

            RBMCaravanRegister.SetState(caravan.StringId, RBMCaravanRegister.StateDone);
        }

        /// <summary>
        /// Sells every good in the caravan's manifest into the destination market and returns the total
        /// coin collected. Goods are sold in manifest order (food first, as <c>ModelledGoods</c> is
        /// ordered), each drawing on the town's remaining citizen wealth in turn.
        /// </summary>
        private static int SellCargo(MobileParty caravan, Settlement dst, RBMCaravanRegister.Order order)
        {
            Town town = dst.Town;
            ItemRoster roster = caravan.ItemRoster;
            Settlement src = RBMCaravanRegister.FindSettlement(order.SourceId);

            int startWealth = SettlementWealth.GetCitizenWealth(dst);
            int budget = (int)(startWealth * MaxSpendFraction); // a caravan takes at most this much coin
            int spent = 0;
            int totalProceeds = 0;
            int totalUnitsSold = 0;
            int totalCarried = 0;
            int goodsSold = 0;

            foreach (RBMCaravanRegister.GoodLot lot in order.Goods)
            {
                ItemObject good = RBMCaravanRegister.FindItem(lot.GoodId);
                if (good == null)
                {
                    continue;
                }
                int carried = roster.GetItemNumber(good);
                if (carried <= 0)
                {
                    continue;
                }
                totalCarried += carried;

                // The town is buying. A managed supply caravan is a real CaravanPartyComponent, not a
                // villager, so the price patch prices this at the live days-of-supply rate, not the flat
                // wholesale floor -- a caravan relieving a shortage is paid the scarcity price the shortage
                // has earned, and the destination pays it out of its own citizen wealth (bounded below).
                int price = town.GetItemPrice(new EquipmentElement(good), caravan, isSelling: true);
                if (price <= 0)
                {
                    continue;
                }

                // Bounded by storage room, by the town's live coin, and by the caravan's remaining share of
                // the visit budget -- so no single good, and no single caravan, empties the market.
                int room = TownStorage.Accept(dst, good, carried);
                int wealth = SettlementWealth.GetCitizenWealth(dst);
                int spendable = Math.Min(wealth, budget - spent);
                int affordable = Math.Min(room, spendable / price);
                if (affordable <= 0)
                {
                    continue;
                }

                int cost = affordable * price;
                int moved = SettlementWealth.DebitCitizens(dst, cost, SettlementWealth.Source.Caravan);
                int units = moved / price;
                if (units <= 0)
                {
                    if (moved > 0)
                    {
                        // Took coin that could not buy a whole unit; give it straight back.
                        SettlementWealth.CreditCitizens(dst, moved, SettlementWealth.Source.Caravan);
                    }
                    continue;
                }

                int actualCost = units * price;
                int refund = moved - actualCost;
                if (refund > 0)
                {
                    SettlementWealth.CreditCitizens(dst, refund, SettlementWealth.Source.Caravan);
                }

                dst.ItemRoster.AddToCounts(new EquipmentElement(good), units);
                roster.AddToCounts(new EquipmentElement(good), -units);
                TradeTariff.Levy(dst, actualCost);

                spent += actualCost;
                totalProceeds += actualCost;
                totalUnitsSold += units;
                goodsSold++;
            }

            CaravanLog.Log("ARRIVE", CaravanLog.Name(dst),
                "from " + CaravanLog.Name(src) + "  ·  sold " + totalUnitsSold + " of " + totalCarried
                + " units across " + goodsSold + " good(s) for " + totalProceeds + "d"
                + "  ·  purse " + startWealth + " → " + SettlementWealth.GetCitizenWealth(dst) + "d");

            // The coin the destination paid; the caravan now carries it home to the source citizens.
            return totalProceeds;
        }

        /// <summary>
        /// Registers every managed caravan as it is created, before its own source-entry event fires.
        /// Runs for every caravan creation but acts only when an errand is waiting in
        /// <see cref="RBMCaravanRegister.Pending"/> -- set moments earlier by the dispatcher.
        /// </summary>
        [HarmonyPatch(typeof(CaravanPartyComponent), "OnMobilePartySetOnCreation")]
        private static class RegisterOnCreationPatch
        {
            private static void Postfix(CaravanPartyComponent __instance)
            {
                if (RBMCaravanRegister.Pending == null || __instance == null || __instance.MobileParty == null)
                {
                    return;
                }
                RBMCaravanRegister.BindPending(__instance.MobileParty.StringId);
            }
        }

        /// <summary>
        /// Runs our arrival handling for a managed caravan and suppresses vanilla's trade brain for it;
        /// every other caravan falls through to vanilla untouched.
        /// </summary>
        [HarmonyPatch(typeof(CaravansCampaignBehavior), "OnSettlementEntered")]
        private static class SettlementEnteredPatch
        {
            private static bool Prefix(MobileParty mobileParty, Settlement settlement, Hero hero)
            {
                if (!RC.rbmCampaignEnabled || !RBMCaravanRegister.IsManaged(mobileParty))
                {
                    return true;
                }
                Handle(mobileParty, settlement);
                return false;
            }
        }

        /// <summary>
        /// Keeps vanilla's caravan retargeting off our caravans, disposes of a delivered one here -- on a
        /// tick, not from inside the settlement-entered event where destroying a party would be unsafe --
        /// and, crucially, is the watchdog that guarantees no managed caravan is ever left stranded.
        ///
        /// Because the vanilla body (which re-drives normal caravans every tick) is suppressed for ours,
        /// nothing else would re-issue their orders. Every hour this checks each managed caravan and:
        ///  * dissolves one that has finished;
        ///  * turns an out-bound one home if its destination is no longer a valid, reachable drop (captured
        ///    out of the realm, razed, or now under siege) -- it must not march into enemy ground or deliver
        ///    across a border;
        ///  * re-homes a home-bound one whose source it can no longer reach (razed, or captured into an enemy
        ///    that would bar the gate) to the nearest friendly town, rather than let it mill outside forever;
        ///  * and otherwise puts any caravan that has stopped moving -- idle after a won battle, stuck in a
        ///    waypoint town, or off-route for any other reason -- back on the road toward its current target.
        /// A caravan that is travelling normally, or actively fleeing a fight it is losing, is left untouched.
        /// </summary>
        [HarmonyPatch(typeof(CaravansCampaignBehavior), "HourlyTickParty")]
        private static class HourlyTickPatch
        {
            private static bool Prefix(MobileParty mobileParty)
            {
                if (!RC.rbmCampaignEnabled || !RBMCaravanRegister.IsManaged(mobileParty))
                {
                    return true;
                }
                if (!RBMCaravanRegister.TryGetOrder(mobileParty.StringId, out RBMCaravanRegister.Order order))
                {
                    return false;
                }

                if (order.State == RBMCaravanRegister.StateDone)
                {
                    RBMCaravanRegister.Remove(mobileParty.StringId);
                    DestroyPartyAction.Apply(null, mobileParty);
                    return false;
                }

                if (order.State == RBMCaravanRegister.StateEnRoute && !DestinationUsable(order))
                {
                    DivertHome(mobileParty, order);
                    return false;
                }

                if (order.State == RBMCaravanRegister.StateReturning && !SourceReachable(mobileParty, order))
                {
                    RerouteHome(mobileParty, order);
                    return false;
                }

                KeepMoving(mobileParty, order);
                return false;
            }
        }

        /// <summary>
        /// Whether an out-bound caravan's destination is still a town it can lawfully and physically deliver
        /// to: a live town, of the source's own kingdom (the system is intra-kingdom only), and not currently
        /// besieged (it could not get in). A destination that fails any of these is a drop we must abandon.
        /// </summary>
        private static bool DestinationUsable(RBMCaravanRegister.Order order)
        {
            Settlement dst = RBMCaravanRegister.FindSettlement(order.DestId);
            if (dst == null || dst.Town == null || !dst.IsTown || dst.SiegeEvent != null)
            {
                return false; // razed, no longer a town, or besieged
            }
            Settlement src = RBMCaravanRegister.FindSettlement(order.SourceId);
            return SameRealm(src, dst);
        }

        /// <summary>
        /// Whether a home-bound caravan can still reach its source to unload: the source is a live town and
        /// the caravan's own faction is not at war with whoever holds it now (a source captured into an enemy
        /// would bar the caravan at the gate). A source merely under siege by an enemy is still counted as
        /// reachable -- the caravan waits it out rather than throwing the trip away, since sieges lift.
        /// </summary>
        private static bool SourceReachable(MobileParty caravan, RBMCaravanRegister.Order order)
        {
            Settlement source = RBMCaravanRegister.FindSettlement(order.SourceId);
            if (source == null || source.Town == null || !source.IsTown)
            {
                return false; // razed or no longer a town
            }
            IFaction home = source.MapFaction;
            IFaction mine = caravan.MapFaction;
            if (home == null || mine == null)
            {
                return false;
            }
            return home == mine || !home.IsAtWarWith(mine);
        }

        /// <summary>
        /// Abandons the delivery and turns a caravan home carrying whatever it was hauling. The goods go back
        /// to the source market on homecoming (fully conserved); no money moves, because none was collected.
        /// If the source is itself gone, the caravan is simply dissolved on the next tick.
        /// </summary>
        private static void DivertHome(MobileParty caravan, RBMCaravanRegister.Order order)
        {
            RBMCaravanRegister.SetProceeds(caravan.StringId, 0);
            RBMCaravanRegister.SetState(caravan.StringId, RBMCaravanRegister.StateReturning);

            Settlement source = RBMCaravanRegister.FindSettlement(order.SourceId);
            if (source == null)
            {
                RBMCaravanRegister.SetState(caravan.StringId, RBMCaravanRegister.StateDone);
                return;
            }
            SendTo(caravan, source);
            CaravanLog.Log("DIVERT", CaravanLog.Name(source),
                "destination " + CaravanLog.Name(RBMCaravanRegister.FindSettlement(order.DestId))
                + " lost — carrying the load home");
        }

        /// <summary>
        /// Re-homes a home-bound caravan whose source it can no longer reach: it adopts the nearest reachable
        /// friendly town of its own realm as the new home and unloads its takings and unsold goods there when
        /// it arrives (the normal homecoming flow), so the coin and goods stay in the realm rather than being
        /// lost. Only if there is no friendly town in range at all is the caravan written off.
        /// </summary>
        private static void RerouteHome(MobileParty caravan, RBMCaravanRegister.Order order)
        {
            Settlement fallback = NearestFriendlyTown(caravan);
            if (fallback == null)
            {
                RBMCaravanRegister.SetState(caravan.StringId, RBMCaravanRegister.StateDone);
                CaravanLog.Log("ABANDON", CaravanLog.Name(RBMCaravanRegister.FindSettlement(order.SourceId)),
                    "home unreachable and no friendly town in range — caravan dissolved");
                return;
            }

            // Adopt the fallback as the new home: the homecoming handler pays its citizens the takings and
            // returns any unsold goods to its market. Re-provision for the (possibly longer) road to it.
            RBMCaravanRegister.SetSource(caravan.StringId, fallback.StringId);
            RBMCaravanDispatch.StockFood(caravan, fallback, order.Goods);
            SendTo(caravan, fallback);
            CaravanLog.Log("REROUTE", CaravanLog.Name(fallback),
                "home " + CaravanLog.Name(RBMCaravanRegister.FindSettlement(order.SourceId))
                + " unreachable — unloading at nearest friendly town instead");
        }

        /// <summary>
        /// The nearest town of the caravan's own realm it can actually reach right now -- a live town, of the
        /// caravan's <see cref="IFaction"/>, not under siege, and with a path to it. Null if the caravan has
        /// no faction or no reachable friendly town remains.
        /// </summary>
        private static Settlement NearestFriendlyTown(MobileParty caravan)
        {
            IFaction mine = (caravan != null) ? caravan.MapFaction : null;
            if (mine == null)
            {
                return null;
            }
            var dist = Campaign.Current.Models.MapDistanceModel;
            MobileParty.NavigationType nav = caravan.HasNavalNavigationCapability
                ? MobileParty.NavigationType.All
                : MobileParty.NavigationType.Default;
            float unreachable = Campaign.MapDiagonal * 5f; // the model returns ~this when no path of that type exists

            Settlement best = null;
            float bestDist = float.MaxValue;
            foreach (Town town in Town.AllTowns)
            {
                Settlement s = (town != null) ? town.Settlement : null;
                if (s == null || !s.IsTown || s.MapFaction != mine || s.SiegeEvent != null)
                {
                    continue;
                }
                bool isPort = caravan.HasNavalNavigationCapability && s.HasPort;
                float d = dist.GetDistance(caravan, s, isPort, nav, out float _);
                if (d < bestDist && d < unreachable)
                {
                    bestDist = d;
                    best = s;
                }
            }
            return best;
        }

        /// <summary>Whether two settlements are still in the same kingdom (share a live <see cref="IFaction"/>).</summary>
        private static bool SameRealm(Settlement a, Settlement b)
        {
            return a != null && b != null && a.MapFaction != null && a.MapFaction == b.MapFaction;
        }

        /// <summary>
        /// The settlement the caravan's current state should be moving toward: its destination while carrying
        /// goods out, its source on the way home. Null for any other state (nothing should be moving then).
        /// </summary>
        private static Settlement CurrentLegTarget(RBMCaravanRegister.Order order)
        {
            string id = (order.State == RBMCaravanRegister.StateReturning) ? order.SourceId
                : (order.State == RBMCaravanRegister.StateEnRoute) ? order.DestId : null;
            return RBMCaravanRegister.FindSettlement(id);
        }

        /// <summary>
        /// The catch-all that keeps a caravan from ever stalling: if it is not already heading to its current
        /// target, it is put back on the road toward it. This re-drives a caravan left idle by a won battle
        /// (<see cref="AiBehavior.Hold"/>, target cleared), one that has wandered into a waypoint town and
        /// stopped, or one in any other off-route state. It leaves alone a caravan that is already travelling
        /// to the right place, one sitting inside its actual target (the arrival flow will handle that), one
        /// caught in a map event, and one actively fleeing -- so normal travel and self-preservation are
        /// never disturbed.
        /// </summary>
        private static void KeepMoving(MobileParty caravan, RBMCaravanRegister.Order order)
        {
            if (caravan.MapEvent != null)
            {
                return; // in a fight -- let it play out
            }
            Settlement target = CurrentLegTarget(order);
            if (target == null)
            {
                return;
            }
            if (caravan.CurrentSettlement == target)
            {
                return; // already at the target -- the settlement-entered arrival flow deals with it
            }
            if (caravan.DefaultBehavior == AiBehavior.GoToSettlement && caravan.TargetSettlement == target)
            {
                return; // already correctly under way
            }
            if (IsFleeing(caravan.DefaultBehavior))
            {
                return; // running from a losing fight -- do not send it back toward danger
            }

            SendTo(caravan, target);
            CaravanLog.Log("RESUME", CaravanLog.Name(target), "was idle/off-route — back on the road");
        }

        /// <summary>Whether a behaviour is one of the flee modes, in which the caravan should be left to run.</summary>
        private static bool IsFleeing(AiBehavior behavior)
        {
            return behavior == AiBehavior.FleeToPoint
                || behavior == AiBehavior.FleeToGate
                || behavior == AiBehavior.FleeToParty;
        }

        /// <summary>
        /// Points a caravan at a settlement using whatever navigation capability it already has -- it keeps
        /// any ship it sailed out with -- rather than re-running the sea/land choice from a mid-map position.
        /// Used to resume or redirect a caravan already on the road, where a full <c>RouteBetween</c> (which
        /// measures from a settlement and plants a fleet anchor) has no valid vantage point.
        /// </summary>
        private static void SendTo(MobileParty caravan, Settlement target)
        {
            if (caravan == null || target == null)
            {
                return;
            }
            MobileParty.NavigationType nav = caravan.HasNavalNavigationCapability
                ? MobileParty.NavigationType.All
                : MobileParty.NavigationType.Default;
            caravan.SetMoveGoToSettlement(target, nav, isTargetingThePort: caravan.HasNavalNavigationCapability && target.HasPort);
        }

        /// <summary>
        /// Names our caravans "⟨source⟩ → ⟨destination⟩ Supply Caravan" on the map, instead of the vanilla
        /// "⟨owner⟩'s Caravan", so they read as what they are and where they run. Managed caravans only;
        /// every other caravan keeps its native name.
        /// </summary>
        [HarmonyPatch(typeof(CaravanPartyComponent), "Name", MethodType.Getter)]
        private static class NamePatch
        {
            private static void Postfix(CaravanPartyComponent __instance, ref TextObject __result)
            {
                MobileParty party = (__instance != null) ? __instance.MobileParty : null;
                if (party == null || !RBMCaravanRegister.IsManaged(party))
                {
                    return;
                }
                if (!RBMCaravanRegister.TryGetOrder(party.StringId, out RBMCaravanRegister.Order order))
                {
                    return;
                }
                Settlement source = RBMCaravanRegister.FindSettlement(order.SourceId);
                Settlement dest = RBMCaravanRegister.FindSettlement(order.DestId);
                string from = (source != null && source.Name != null) ? source.Name.ToString() : null;
                string to = (dest != null && dest.Name != null) ? dest.Name.ToString() : null;

                // A relief caravan (empty manifest) carries only investment capital, so it reads as one.
                string kind = (order.Goods == null || order.Goods.Count == 0) ? " Relief Caravan" : " Supply Caravan";

                string label;
                if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
                {
                    label = from + " → " + to + kind;
                }
                else if (!string.IsNullOrEmpty(from))
                {
                    label = from + kind;
                }
                else
                {
                    label = kind.Trim();
                }
                __result = new TextObject(label);
            }
        }

        /// <summary>
        /// Keeps our caravans from losing men to desertion. Vanilla deserts troops from a party whose
        /// morale falls below a threshold (see <c>DesertionCampaignBehavior</c>), and caravans are included;
        /// a managed supply caravan is a transient economic party, provisioned for its trip, and should not
        /// bleed guards on the road. Skips the desertion check for our caravans; every other party is
        /// checked as normal.
        /// </summary>
        [HarmonyPatch(typeof(DesertionCampaignBehavior), "DailyTickParty")]
        private static class NoDesertionPatch
        {
            private static bool Prefix(MobileParty mobileParty)
            {
                return !(RC.rbmCampaignEnabled && RBMCaravanRegister.IsManaged(mobileParty));
            }
        }
    }
}
