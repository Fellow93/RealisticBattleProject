using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection.ArmyManagement;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Hiring mercenary companies from the player's army management screen. Vanilla lists only parties of the
    /// player's own faction, so idle companies are added to the list; their influence cost is 0 (the
    /// CalculatePartyInfluenceCost patch) and their price is in gold, shown on each row and as a cart total by
    /// two injected widgets (<see cref="MercenaryArmyPrefabPatch"/>). The cart's gold total is recomputed every
    /// time vanilla refreshes the cart (its UpdateCanConfirm runs at the end of every add, remove, reset and
    /// boost), so cancel, reset and removal need no bookkeeping. Confirming hires each company in the cart
    /// (gold, then the kingdom contract) just before vanilla assigns the cart's parties to the army; removing a
    /// hired company from the army later ends its contract through the hourly reconcile.
    /// </summary>
    public static class MercenaryArmyScreen
    {
        /// <summary>Gold the companies in the open screen's cart cost. Read by the cart-total widget.</summary>
        public static int CartGold { get; private set; }

        /// <summary>Whether the open screen lists any company to hire. Read by the cart-total widget.</summary>
        public static bool AnyCompanyListed { get; private set; }

        private static readonly MethodInfo OnAddToCartMethod = AccessTools.Method(typeof(ArmyManagementVM), "OnAddToCart");
        private static readonly MethodInfo OnRemoveMethod = AccessTools.Method(typeof(ArmyManagementVM), "OnRemove");
        private static readonly MethodInfo OnFocusMethod = AccessTools.Method(typeof(ArmyManagementVM), "OnFocus");

        /// <summary>A company in the cart that is not hired yet (one already in the player's army costs nothing more).</summary>
        private static bool IsCompanyToHire(ArmyManagementItemVM item)
        {
            MobileParty party = item?.Party;
            return party != null && !item.IsMainHero && !item.IsAlreadyWithPlayer
                && MercenaryCompanies.IsCompanyParty(party) && party.ActualClan.Kingdom == null;
        }

        private static List<ArmyManagementItemVM> CompaniesToHire(ArmyManagementVM vm)
        {
            List<ArmyManagementItemVM> list = new List<ArmyManagementItemVM>();
            foreach (ArmyManagementItemVM item in vm.PartiesInCart)
            {
                if (IsCompanyToHire(item))
                {
                    list.Add(item);
                }
            }
            return list;
        }

        private static int GoldFor(List<ArmyManagementItemVM> items)
        {
            int total = 0;
            foreach (ArmyManagementItemVM item in items)
            {
                total += MercenaryCompanies.HirePrice(item.Party);
            }
            return total;
        }

        [HarmonyPatch(typeof(ArmyManagementVM), MethodType.Constructor, new Type[] { typeof(Action) })]
        private static class ListIdleCompanies
        {
            private static void Postfix(ArmyManagementVM __instance)
            {
                AnyCompanyListed = false;
                CartGold = 0;
                Kingdom kingdom = Hero.MainHero?.MapFaction as Kingdom;
                if (!MercenaryCompanies.IsEnabled || kingdom == null || Clan.PlayerClan.IsUnderMercenaryService
                    || OnAddToCartMethod == null || OnRemoveMethod == null || OnFocusMethod == null)
                {
                    return;
                }
                Action<ArmyManagementItemVM> onAdd = (Action<ArmyManagementItemVM>)Delegate.CreateDelegate(
                    typeof(Action<ArmyManagementItemVM>), __instance, OnAddToCartMethod);
                Action<ArmyManagementItemVM> onRemove = (Action<ArmyManagementItemVM>)Delegate.CreateDelegate(
                    typeof(Action<ArmyManagementItemVM>), __instance, OnRemoveMethod);
                Action<ArmyManagementItemVM> onFocus = (Action<ArmyManagementItemVM>)Delegate.CreateDelegate(
                    typeof(Action<ArmyManagementItemVM>), __instance, OnFocusMethod);
                foreach (Clan clan in MercenaryCompanies.LiveCompanies())
                {
                    MobileParty party = MercenaryCompanies.ActiveParty(clan);
                    // A hired company is a member of a kingdom and already listed by vanilla when it is ours.
                    if (party == null || party.LeaderHero == null || clan.Kingdom != null
                        || FactionManager.IsAtWarAgainstFaction(clan, kingdom))
                    {
                        continue;
                    }
                    __instance.PartyList.Add(new ArmyManagementItemVM(onAdd, onRemove, onFocus, party));
                    AnyCompanyListed = true;
                }
            }
        }

        /// <summary>Recomputes the cart's gold; a cart the player cannot pay for cannot be confirmed.</summary>
        [HarmonyPatch(typeof(ArmyManagementVM), "UpdateCanConfirm")]
        private static class GoldGate
        {
            private static void Postfix(ArmyManagementVM __instance)
            {
                if (!MercenaryCompanies.IsEnabled)
                {
                    CartGold = 0;
                    return;
                }
                CartGold = GoldFor(CompaniesToHire(__instance));
                if (CartGold > 0 && CartGold > Hero.MainHero.Gold)
                {
                    __instance.CanConfirm = false;
                    __instance.DoneHint.HintText = new TextObject("{=RBM_merc_cart_no_gold}You do not have enough gold to hire these mercenary companies.");
                }
            }
        }

        /// <summary>
        /// Hires the cart's companies on Done, before vanilla assigns the cart to the army -- only on the path
        /// where it will (the same conditions ExecuteDone tests). A company that can no longer be hired (it got
        /// into a fight meanwhile) is dropped from the cart so vanilla never puts a foreign party in the army;
        /// if that would leave only the player in the cart, which vanilla reads as "disband", nothing happens.
        /// </summary>
        [HarmonyPatch(typeof(ArmyManagementVM), "ExecuteDone")]
        private static class HireOnDone
        {
            private static bool Prefix(ArmyManagementVM __instance)
            {
                if (!MercenaryCompanies.IsEnabled)
                {
                    return true;
                }
                List<ArmyManagementItemVM> hires = CompaniesToHire(__instance);
                if (hires.Count == 0)
                {
                    return true;
                }
                if (GoldFor(hires) > Hero.MainHero.Gold)
                {
                    return false;
                }
                bool assigns = __instance.CanAffordInfluenceCost
                    && __instance.PartiesInCart.Count > 1
                    && MobileParty.MainParty.MapFaction is Kingdom;
                if (!assigns)
                {
                    return true;
                }
                Kingdom kingdom = (Kingdom)MobileParty.MainParty.MapFaction;
                List<ArmyManagementItemVM> stale = hires.FindAll(item => !MercenaryContracts.CanBeHired(item.Party, kingdom));
                foreach (ArmyManagementItemVM item in stale)
                {
                    MercenaryLog.Log("CONTRACT", item.Party, "PLAYER hire dropped from the cart: "
                        + MercenaryContracts.WhyNotHireable(item.Party, kingdom));
                }
                if (stale.Count > 0)
                {
                    if (__instance.PartiesInCart.Count - stale.Count <= 1)
                    {
                        return false;
                    }
                    foreach (ArmyManagementItemVM item in stale)
                    {
                        __instance.PartiesInCart.Remove(item);
                        hires.Remove(item);
                    }
                }
                foreach (ArmyManagementItemVM item in hires)
                {
                    if (!MercenaryContracts.Hire(item.Party, MobileParty.MainParty, kingdom, null))
                    {
                        __instance.PartiesInCart.Remove(item);
                    }
                }
                return true;
            }
        }
    }
}
