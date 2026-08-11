using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using SandBox.ViewModelCollection;
using SandBox.ViewModelCollection.Nameplate.NameplateNotifications;
using SandBox.ViewModelCollection.Nameplate.NameplateNotifications.SettlementNotificationTypes;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// RBM's own settlement-nameplate bubbles -- the small floating words that rise above a town when
    /// something happens there. The game raises these for recruiting, caravan trade and the like by
    /// firing a campaign event that the per-settlement <see cref="SettlementNameplateNotificationsVM"/>
    /// listens for; there is no imperative "add a message" call. RBM follows the same pattern with its
    /// own <see cref="MbEvent"/>s: the source (carousing, provisioning) fires one, a Harmony postfix on
    /// the VM's Register/Unload has each in-range nameplate subscribed, and the handler pushes a bubble
    /// only for the settlement it belongs to and only while that settlement is inspected. Every other
    /// nameplate ignores it, and when none is in range the raise is a cheap walk of an empty list.
    /// </summary>
    public static class RBMMapNotifications
    {
        /// <summary>(settlement, carousing party, gold drunk this hour).</summary>
        public static MbEvent<Settlement, MobileParty, int> SpoilsDrunk = new MbEvent<Settlement, MobileParty, int>();

        /// <summary>(settlement, player party, food items bought, gold spent).</summary>
        public static MbEvent<Settlement, MobileParty, List<(ItemObject Item, int Count)>, int> TroopsBoughtFood =
            new MbEvent<Settlement, MobileParty, List<(ItemObject Item, int Count)>, int>();

        /// <summary>(settlement, player party, luxuries bought, gold spent).</summary>
        public static MbEvent<Settlement, MobileParty, List<(ItemObject Item, int Count)>, int> SoldiersBoughtLuxury =
            new MbEvent<Settlement, MobileParty, List<(ItemObject Item, int Count)>, int>();

        /// <summary>Below this a drink is not worth a floating word; the men still spend it.</summary>
        private const int MinDrunkGoldToNotify = 3;

        /// <summary>
        /// Drops every listener by recreating the events. The subscribers are nameplate view-models,
        /// which a static event would otherwise pin in memory across a save reload. RBMCampaignPatcher
        /// calls this on each patch pass (game start / save load), before any new nameplate subscribes,
        /// so a fresh campaign starts with a clean listener list rather than the last one's ghosts.
        /// </summary>
        public static void Reset()
        {
            SpoilsDrunk = new MbEvent<Settlement, MobileParty, int>();
            TroopsBoughtFood = new MbEvent<Settlement, MobileParty, List<(ItemObject Item, int Count)>, int>();
            SoldiersBoughtLuxury = new MbEvent<Settlement, MobileParty, List<(ItemObject Item, int Count)>, int>();
        }

        public static void RaiseSpoilsDrunk(Settlement settlement, MobileParty party, int goldSpent)
        {
            if (settlement != null && party != null && goldSpent >= MinDrunkGoldToNotify)
            {
                SpoilsDrunk.Invoke(settlement, party, goldSpent);
            }
        }

        public static void RaiseTroopsBoughtFood(Settlement settlement, MobileParty party, List<(ItemObject Item, int Count)> items, int goldSpent)
        {
            if (settlement != null && party != null && goldSpent > 0 && items != null && items.Count > 0)
            {
                TroopsBoughtFood.Invoke(settlement, party, items, goldSpent);
            }
        }

        public static void RaiseSoldiersBoughtLuxury(Settlement settlement, MobileParty party, List<(ItemObject Item, int Count)> items, int goldSpent)
        {
            if (settlement != null && party != null && goldSpent > 0 && items != null && items.Count > 0)
            {
                SoldiersBoughtLuxury.Invoke(settlement, party, items, goldSpent);
            }
        }
    }

    /// <summary>
    /// Marks a bubble as one of RBM's, and whether it belongs to the player's own party. A settlement
    /// nameplate shows every player bubble but at most a couple from other parties at once, so the
    /// cap counts the ones flying this flag false.
    /// </summary>
    internal interface IRBMNameplateNotification
    {
        bool IsPlayerParty { get; }
    }

    /// <summary>
    /// "Reik's warband drinks away 240 denars." The party name rides the nameplate's own name line;
    /// this fills the value line beneath it with one of many turns of phrase so a town watched for a
    /// while does not read the same words twice.
    /// </summary>
    public class SpoilsDrunkNotificationItemVM : SettlementNotificationItemBaseVM, IRBMNameplateNotification
    {
        public bool IsPlayerParty { get; }

        private static readonly string[] Flavors =
        {
            "drinks away {AMT} denars",
            "spends {AMT} on ale and dice",
            "empties {AMT} into the tavern",
            "toasts away {AMT} denars",
            "carouses — {AMT} gone",
            "stands a round: {AMT} denars",
            "pours {AMT} down their throats",
            "gambles and drinks: {AMT}",
            "keeps the taverns rich by {AMT}",
            "roars through {AMT} in drink",
            "wets their whistle for {AMT}",
            "leaves {AMT} on the counter",
            "drinks the town's health — {AMT} denars",
            "blows {AMT} on wine and song",
            "sinks {AMT} into their cups",
            "makes merry for {AMT} denars"
        };

        public SpoilsDrunkNotificationItemVM(Action<SettlementNotificationItemBaseVM> onRemove, MobileParty party, int amount, int createdTick)
            : base(onRemove, createdTick)
        {
            IsPlayerParty = (party == MobileParty.MainParty);
            base.Text = Flavors[MBRandom.RandomInt(Flavors.Length)].Replace("{AMT}", amount.ToString());
            base.CharacterName = (party != null) ? party.Name.ToString() : "";
            base.CharacterVisual = new CharacterImageIdentifierVM(RBMMapNotificationHelper.PartyFaceCode(party));
            base.RelationType = RBMMapNotificationHelper.PartyRelation(party);
            base.CreatedTick = createdTick;
        }
    }

    /// <summary>
    /// "Bought: 12 grain, 6 meat, 4 cheese — 240 denars." Only the player's own party raises this,
    /// so it names the fare and the cost outright rather than in the vaguer category terms the game's
    /// caravan bubbles use.
    /// </summary>
    public class TroopFoodNotificationItemVM : SettlementNotificationItemBaseVM, IRBMNameplateNotification
    {
        public bool IsPlayerParty { get; }

        private static readonly string[] Flavors =
        {
            "buys {LIST} — {AMT} denars",
            "lays in {LIST} for {AMT} denars",
            "provisions {LIST} — {AMT} denars",
            "stocks up: {LIST} ({AMT} denars)",
            "fills the packs — {LIST} for {AMT} denars"
        };

        public TroopFoodNotificationItemVM(Action<SettlementNotificationItemBaseVM> onRemove, MobileParty party, List<(ItemObject Item, int Count)> items, int amount, int createdTick)
            : base(onRemove, createdTick)
        {
            IsPlayerParty = (party == MobileParty.MainParty);
            base.Text = Flavors[MBRandom.RandomInt(Flavors.Length)]
                .Replace("{LIST}", RBMMapNotificationHelper.FoodList(items))
                .Replace("{AMT}", amount.ToString());
            base.CharacterName = (party != null) ? party.Name.ToString() : "";
            base.CharacterVisual = new CharacterImageIdentifierVM(RBMMapNotificationHelper.PartyFaceCode(party));
            base.RelationType = 1;
            base.CreatedTick = createdTick;
        }
    }

    /// <summary>
    /// "Vlandia's men indulge in velvet, jewelry — 640 denars." A stack over its ceiling now and then
    /// blows the surplus on a keepsake; only the player's own party names the bauble it bought.
    /// </summary>
    public class TroopLuxuryNotificationItemVM : SettlementNotificationItemBaseVM, IRBMNameplateNotification
    {
        public bool IsPlayerParty { get; }

        private static readonly string[] Flavors =
        {
            "indulge in {LIST} — {AMT} denars",
            "treat themselves to {LIST} for {AMT} denars",
            "splash out on {LIST} — {AMT} denars",
            "fancy {LIST} ({AMT} denars)",
            "buy themselves {LIST} for {AMT} denars"
        };

        public TroopLuxuryNotificationItemVM(Action<SettlementNotificationItemBaseVM> onRemove, MobileParty party, List<(ItemObject Item, int Count)> items, int amount, int createdTick)
            : base(onRemove, createdTick)
        {
            IsPlayerParty = (party == MobileParty.MainParty);
            base.Text = Flavors[MBRandom.RandomInt(Flavors.Length)]
                .Replace("{LIST}", RBMMapNotificationHelper.ItemNameList(items))
                .Replace("{AMT}", amount.ToString());
            base.CharacterName = (party != null) ? party.Name.ToString() : "";
            base.CharacterVisual = new CharacterImageIdentifierVM(RBMMapNotificationHelper.PartyFaceCode(party));
            base.RelationType = 1;
            base.CreatedTick = createdTick;
        }
    }

    /// <summary>Shared formatting for the RBM nameplate bubbles.</summary>
    internal static class RBMMapNotificationHelper
    {
        /// <summary>A face for the bubble's avatar: the party's leader, else whoever stands at its head.</summary>
        public static CharacterCode PartyFaceCode(MobileParty party)
        {
            CharacterObject face = party?.LeaderHero?.CharacterObject;
            if (face == null && party?.Party?.MemberRoster != null && party.Party.MemberRoster.Count > 0)
            {
                face = party.Party.MemberRoster.GetElementCopyAtIndex(0).Character;
            }
            return (face != null) ? SandBoxUIHelper.GetCharacterCode(face) : null;
        }

        /// <summary>Green for our own and our friends, red for those at war, amber for the rest.</summary>
        public static int PartyRelation(MobileParty party)
        {
            if (party == MobileParty.MainParty)
            {
                return 1;
            }
            Clan clan = party?.LeaderHero?.Clan ?? party?.ActualClan;
            if (clan != null && Hero.MainHero?.Clan != null)
            {
                return clan.IsAtWarWith(Hero.MainHero.Clan) ? -1 : 1;
            }
            return 0;
        }

        /// <summary>The three most-bought fares by count, "3 grain, 2 meat", with a tail if there are more.</summary>
        public static string FoodList(List<(ItemObject Item, int Count)> items)
        {
            var totals = new Dictionary<ItemObject, int>();
            var order = new List<ItemObject>();
            foreach (var buy in items)
            {
                if (buy.Item == null)
                {
                    continue;
                }
                if (totals.TryGetValue(buy.Item, out int have))
                {
                    totals[buy.Item] = have + buy.Count;
                }
                else
                {
                    totals[buy.Item] = buy.Count;
                    order.Add(buy.Item);
                }
            }
            order.Sort((a, b) => totals[b].CompareTo(totals[a]));

            var sb = new StringBuilder();
            int shown = MathF.Min(3, order.Count);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(totals[order[i]]).Append(' ').Append(order[i].Name.ToString());
            }
            if (order.Count > shown)
            {
                sb.Append(" …");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Item names, up to three, "velvet, jewelry" -- like <see cref="FoodList"/> but the count is
        /// shown only when more than one was bought, since a keepsake is usually a single piece.
        /// </summary>
        public static string ItemNameList(List<(ItemObject Item, int Count)> items)
        {
            var totals = new Dictionary<ItemObject, int>();
            var order = new List<ItemObject>();
            foreach (var buy in items)
            {
                if (buy.Item == null)
                {
                    continue;
                }
                if (totals.TryGetValue(buy.Item, out int have))
                {
                    totals[buy.Item] = have + buy.Count;
                }
                else
                {
                    totals[buy.Item] = buy.Count;
                    order.Add(buy.Item);
                }
            }
            order.Sort((a, b) => totals[b].CompareTo(totals[a]));

            var sb = new StringBuilder();
            int shown = MathF.Min(3, order.Count);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                if (totals[order[i]] > 1)
                {
                    sb.Append(totals[order[i]]).Append(' ');
                }
                sb.Append(order[i].Name.ToString());
            }
            if (order.Count > shown)
            {
                sb.Append(" …");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Wires RBM's bubble events into each settlement nameplate. The game's VM subscribes its own
    /// listeners in <c>RegisterEvents</c> when the settlement comes into inspection range and drops
    /// them in <c>UnloadEvents</c> when it leaves; these postfixes hang RBM's listeners on the same two
    /// hooks so an RBM bubble appears under exactly the same in-range condition as a native one. The
    /// clear-then-add keeps a single listener even if <c>RegisterEvents</c> is called while already
    /// registered (its body no-ops, but the postfix still runs).
    /// </summary>
    [HarmonyPatch(typeof(SettlementNameplateNotificationsVM), "RegisterEvents")]
    internal static class SettlementNameplateNotificationsRegisterPatch
    {
        private static readonly FieldInfo SettlementField =
            AccessTools.Field(typeof(SettlementNameplateNotificationsVM), "_settlement");
        private static readonly FieldInfo TickField =
            AccessTools.Field(typeof(SettlementNameplateNotificationsVM), "_tickSinceEnabled");

        private static void Postfix(SettlementNameplateNotificationsVM __instance)
        {
            RBMMapNotifications.SpoilsDrunk.ClearListeners(__instance);
            RBMMapNotifications.SpoilsDrunk.AddNonSerializedListener(__instance,
                (settlement, party, amount) => OnSpoilsDrunk(__instance, settlement, party, amount));

            RBMMapNotifications.TroopsBoughtFood.ClearListeners(__instance);
            RBMMapNotifications.TroopsBoughtFood.AddNonSerializedListener(__instance,
                (settlement, party, items, amount) => OnTroopsBoughtFood(__instance, settlement, party, items, amount));

            RBMMapNotifications.SoldiersBoughtLuxury.ClearListeners(__instance);
            RBMMapNotifications.SoldiersBoughtLuxury.AddNonSerializedListener(__instance,
                (settlement, party, items, amount) => OnSoldiersBoughtLuxury(__instance, settlement, party, items, amount));
        }

        /// <summary>At most this many RBM bubbles from other parties crowd a nameplate at once.</summary>
        private const int MaxOtherPartyBubbles = 2;

        private static bool TargetsThisNameplate(SettlementNameplateNotificationsVM vm, Settlement settlement)
        {
            Settlement mine = SettlementField.GetValue(vm) as Settlement;
            return mine != null && mine == settlement && mine.IsInspected;
        }

        /// <summary>
        /// The player's own party always gets its bubble; other parties share a cap of
        /// <see cref="MaxOtherPartyBubbles"/> live RBM bubbles on a nameplate, so a busy town does not
        /// drown its own name under a column of other men's drinking.
        /// </summary>
        private static bool CanAdd(SettlementNameplateNotificationsVM vm, MobileParty party)
        {
            if (party == MobileParty.MainParty)
            {
                return true;
            }
            int others = 0;
            MBBindingList<SettlementNotificationItemBaseVM> list = vm.Notifications;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is IRBMNameplateNotification rbm && !rbm.IsPlayerParty && ++others >= MaxOtherPartyBubbles)
                {
                    return false;
                }
            }
            return true;
        }

        private static void OnSpoilsDrunk(SettlementNameplateNotificationsVM vm, Settlement settlement, MobileParty party, int amount)
        {
            if (!TargetsThisNameplate(vm, settlement) || !CanAdd(vm, party))
            {
                return;
            }
            int tick = (int)TickField.GetValue(vm);
            vm.Notifications.Add(new SpoilsDrunkNotificationItemVM(item => vm.Notifications.Remove(item), party, amount, tick));
        }

        private static void OnTroopsBoughtFood(SettlementNameplateNotificationsVM vm, Settlement settlement, MobileParty party, List<(ItemObject Item, int Count)> items, int amount)
        {
            if (!TargetsThisNameplate(vm, settlement) || !CanAdd(vm, party))
            {
                return;
            }
            int tick = (int)TickField.GetValue(vm);
            vm.Notifications.Add(new TroopFoodNotificationItemVM(item => vm.Notifications.Remove(item), party, items, amount, tick));
        }

        private static void OnSoldiersBoughtLuxury(SettlementNameplateNotificationsVM vm, Settlement settlement, MobileParty party, List<(ItemObject Item, int Count)> items, int amount)
        {
            if (!TargetsThisNameplate(vm, settlement) || !CanAdd(vm, party))
            {
                return;
            }
            int tick = (int)TickField.GetValue(vm);
            vm.Notifications.Add(new TroopLuxuryNotificationItemVM(item => vm.Notifications.Remove(item), party, items, amount, tick));
        }
    }

    [HarmonyPatch(typeof(SettlementNameplateNotificationsVM), "UnloadEvents")]
    internal static class SettlementNameplateNotificationsUnloadPatch
    {
        private static void Postfix(SettlementNameplateNotificationsVM __instance)
        {
            RBMMapNotifications.SpoilsDrunk.ClearListeners(__instance);
            RBMMapNotifications.TroopsBoughtFood.ClearListeners(__instance);
            RBMMapNotifications.SoldiersBoughtLuxury.ClearListeners(__instance);
        }
    }
}
