using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Lets a village keep more than one trade convoy on the road at a time.
    ///
    /// Vanilla gives a village exactly one villager party, held in the single
    /// <see cref="Village.VillagerPartyComponent"/> slot: while that party is walking to town the
    /// village cannot send anything else, and any villager party that is not the one in the slot is
    /// destroyed on its next hourly tick. A productive village therefore stockpiles goods it has no
    /// way to move, and everything RBM adds to village production piles up behind that one cart.
    ///
    /// The native slot stays a slot -- it is read all over the campaign system (market data, banner
    /// visuals, prisoner release, settlement handover) and widening it is not an option. Instead this
    /// keeps its own register of a village's convoys, points the native slot at whichever convoy is
    /// currently being worked on, and stops the two places that would otherwise garbage-collect the
    /// convoys the slot does not happen to name.
    ///
    /// The register needs no save data: <c>VillagerPartyComponent.OnInitialize</c> runs both when a
    /// party is created and again for every party during load (<c>MobileParty.PreAfterLoad</c>), so
    /// the register rebuilds itself from the save.
    /// </summary>
    internal static class VillagerConvoys
    {
        /// <summary>
        /// How many trade convoys one village may have out at once. Set to 1: a village keeps a single
        /// convoy (as vanilla does), but that convoy carries proportionally more -- see
        /// <see cref="VillagerCarryCapacityPatch"/>. At 1 the register below degrades to a safe vanilla
        /// passthrough: with no convoy the native first-party path raises one, and a lone convoy on the
        /// road never triggers a second.
        /// </summary>
        public const int MaxConvoysPerVillage = 1;

        private static readonly Dictionary<Village, List<MobileParty>> Convoys = new Dictionary<Village, List<MobileParty>>();

        /// <summary>
        /// The campaign the register belongs to. Villages are per-campaign objects, so a register
        /// built in one session is meaningless in the next; comparing against the live campaign
        /// clears it without needing a load hook (which would run too late anyway -- parties register
        /// during PreAfterLoad).
        /// </summary>
        private static Campaign owningCampaign;

        private static void EnsureCurrentCampaign()
        {
            if (owningCampaign != Campaign.Current)
            {
                Convoys.Clear();
                owningCampaign = Campaign.Current;
            }
        }

        public static void Register(Village village, MobileParty party)
        {
            if (village == null || party == null)
            {
                return;
            }

            EnsureCurrentCampaign();

            List<MobileParty> list;
            if (!Convoys.TryGetValue(village, out list))
            {
                list = new List<MobileParty>(MaxConvoysPerVillage);
                Convoys[village] = list;
            }
            if (!list.Contains(party))
            {
                list.Add(party);
            }
        }

        public static void Deregister(Village village, MobileParty party)
        {
            if (village == null || party == null)
            {
                return;
            }

            EnsureCurrentCampaign();

            List<MobileParty> list;
            if (Convoys.TryGetValue(village, out list))
            {
                list.Remove(party);
            }
        }

        /// <summary>
        /// The village's live convoys. Prunes parties that have gone away without passing through
        /// <see cref="Deregister"/>, so a missed finalize cannot permanently hold a convoy slot.
        /// The returned list is the register's own -- copy it before doing anything that can destroy
        /// a party.
        /// </summary>
        public static List<MobileParty> Get(Village village)
        {
            EnsureCurrentCampaign();

            List<MobileParty> list;
            if (village == null || !Convoys.TryGetValue(village, out list))
            {
                return EmptyConvoys;
            }

            for (int i = list.Count - 1; i >= 0; i--)
            {
                MobileParty party = list[i];
                if (party == null || !party.IsActive || party.VillagerPartyComponent == null
                    || party.VillagerPartyComponent.Village != village)
                {
                    list.RemoveAt(i);
                }
            }
            return list;
        }

        private static readonly List<MobileParty> EmptyConvoys = new List<MobileParty>();

        public static bool IsConvoy(Village village, MobileParty party)
        {
            return Get(village).Contains(party);
        }

        /// <summary>Registers every villager party as it is created and as it is reloaded.</summary>
        [HarmonyPatch(typeof(VillagerPartyComponent), "OnInitialize")]
        private static class OnInitializePatch
        {
            private static void Postfix(VillagerPartyComponent __instance)
            {
                Register(__instance.Village, __instance.MobileParty);
            }
        }

        /// <summary>
        /// Replaces the vanilla finalizer, which clears the village's slot no matter which party is
        /// leaving -- with several convoys that would blank the slot for the survivors and get them
        /// destroyed on their next tick. Here the slot is only cleared when it named the departing
        /// party, and is handed to a surviving convoy where there is one.
        /// </summary>
        [HarmonyPatch(typeof(VillagerPartyComponent), "OnFinalize")]
        private static class OnFinalizePatch
        {
            private static bool Prefix(VillagerPartyComponent __instance)
            {
                Village village = __instance.Village;
                if (village == null)
                {
                    return true;
                }

                Deregister(village, __instance.MobileParty);
                if (village.VillagerPartyComponent == __instance)
                {
                    List<MobileParty> remaining = Get(village);
                    village.VillagerPartyComponent = (remaining.Count > 0) ? remaining[0].VillagerPartyComponent : null;
                }
                return false;
            }
        }

        /// <summary>
        /// Vanilla's hourly party tick destroys any villager party the village's slot does not name,
        /// which is how it enforces one convoy per village. Pointing the slot at the party about to
        /// be ticked keeps a registered convoy alive while leaving the rest of that tick -- the
        /// retarget, raid and siege handling -- to run untouched. Genuinely orphaned villager parties
        /// (a village handed over, a component detached) are not in the register and are still
        /// destroyed.
        /// </summary>
        [HarmonyPatch(typeof(VillagerCampaignBehavior), "HourlyTickParty")]
        private static class HourlyTickPartyPatch
        {
            private static void Prefix(MobileParty villagerParty)
            {
                if (villagerParty == null || !villagerParty.IsVillager)
                {
                    return;
                }

                VillagerPartyComponent component = villagerParty.VillagerPartyComponent;
                if (component == null || component.Village == null || component.Village.VillagerPartyComponent == component)
                {
                    return;
                }

                if (IsConvoy(component.Village, villagerParty))
                {
                    component.Village.VillagerPartyComponent = component;
                }
            }
        }

        /// <summary>
        /// Vanilla only sweeps up the convoy named by the slot when its last man is gone; the others
        /// would linger as empty parties. Same rule, applied to every convoy.
        /// </summary>
        [HarmonyPatch(typeof(VillagerCampaignBehavior), "DestroyVillagerPartyIfMemberCountIsZero")]
        private static class DestroyEmptyConvoysPatch
        {
            private static void Postfix(Settlement settlement)
            {
                Village village = (settlement != null) ? settlement.Village : null;
                if (village == null)
                {
                    return;
                }

                List<MobileParty> convoys = Get(village);
                if (convoys.Count == 0)
                {
                    return;
                }

                // Destroying a party deregisters it, so walk a copy.
                MobileParty[] snapshot = convoys.ToArray();
                for (int i = 0; i < snapshot.Length; i++)
                {
                    MobileParty convoy = snapshot[i];
                    if (convoy.IsActive && convoy.MapEvent == null && convoy.MemberRoster.TotalHealthyCount == 0)
                    {
                        DestroyPartyAction.Apply(null, convoy);
                    }
                }
            }
        }

        /// <summary>
        /// The dispatch decision, taken before vanilla's.
        ///
        /// Vanilla asks "is my one party standing in the village?" and gives up if it is not. With a
        /// register to consult, the question becomes "is ANY of my convoys standing in the village?"
        /// -- if one is, the native slot is pointed at it and vanilla loads and sends it exactly as
        /// it always has. If none is home and the village is still under the convoy cap, a fresh
        /// convoy is raised on the same terms vanilla raises its first one; it walks out with the
        /// next hourly tick, again as vanilla does.
        /// </summary>
        [HarmonyPatch(typeof(VillagerCampaignBehavior), "ThinkAboutSendingItemToTown")]
        private static class ThinkAboutSendingItemToTownPatch
        {
            private static bool Prefix(Village village)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || village == null)
                {
                    return true;
                }

                List<MobileParty> convoys = Get(village);
                if (convoys.Count == 0)
                {
                    // No convoy at all: vanilla's own first-party path handles it.
                    return true;
                }

                for (int i = 0; i < convoys.Count; i++)
                {
                    if (convoys[i].CurrentSettlement == village.Settlement)
                    {
                        village.VillagerPartyComponent = convoys[i].VillagerPartyComponent;
                        return true;
                    }
                }

                if (convoys.Count < MaxConvoysPerVillage)
                {
                    TryRaiseConvoy(village);
                }
                return false;
            }
        }

        /// <summary>
        /// Raises an additional convoy on vanilla's terms: the same hourly chance, the same
        /// undisturbed-village and stocked-warehouse gates as
        /// <c>VillagerCampaignBehavior.ThinkAboutSendingItemToTown</c>, and the same hearth cost as
        /// its <c>CreateVillagerParty</c>. It is left standing in the village; loading and sending it
        /// is the next tick's business, exactly as for a first convoy.
        /// </summary>
        private static void TryRaiseConvoy(Village village)
        {
            if (!(MBRandom.RandomFloat < 0.15f) || village.Owner == null || village.Owner.MapEvent != null)
            {
                return;
            }

            CultureObject culture = village.Settlement.Culture;
            if (culture == null || culture.VillagerPartyTemplate == null)
            {
                return;
            }

            int stored = 0;
            for (int i = 0; i < village.Owner.ItemRoster.Count; i++)
            {
                stored += village.Owner.ItemRoster[i].Amount;
            }
            if (stored < (int)(village.GetWarehouseCapacity() * VillagerDispatchThresholdPatch.DispatchThresholdFraction))
            {
                return;
            }

            if (village.Hearth <= Campaign.Current.Models.PartySizeLimitModel.MinimumNumberOfVillagersAtVillagerParty)
            {
                return;
            }

            MobileParty convoy = VillagerPartyComponent.CreateVillagerParty(culture.VillagerPartyTemplate.StringId + "_1", village);
            village.Hearth = MathF.Max(0f, village.Hearth - (convoy.MemberRoster.TotalManCount + 1) / 2);
            EnterSettlementAction.ApplyForParty(convoy, village.Settlement);

            if (EconomyLog.IsEnabled)
            {
                EconomyLog.Log("DISPATCH", village.Settlement.Name.ToString(),
                    "raised convoy " + Get(village).Count + "/" + MaxConvoysPerVillage
                    + "  ·  " + convoy.MemberRoster.TotalManCount + " men"
                    + "  ·  village store " + stored + "/" + village.GetWarehouseCapacity()
                    + "  hearth left " + EconomyLog.Fmt(village.Hearth));
            }
        }

        /// <summary>
        /// How much more a single villager convoy can haul than its troops and animals would otherwise
        /// allow. With one convoy per village instead of two, each trip has to move roughly twice the
        /// cargo to keep the same goods reaching town, so the factor pairs with
        /// <see cref="MaxConvoysPerVillage"/> above -- raise the convoy count and you would lower this,
        /// and vice versa.
        /// </summary>
        private const float VillagerCarryMultiplier = 2f;

        private static readonly TextObject CarryText = new TextObject("{=rbm_villager_convoy_carry}Trade convoy");

        /// <summary>
        /// Multiplies a villager party's inventory capacity so one convoy can carry the load two used
        /// to. This is the weight budget vanilla's <c>VillagerCampaignBehavior.MoveItemsToVillagerParty</c>
        /// fills from the village warehouse when the party sets out, so a bigger budget means a bigger
        /// load -- and, because capacity also governs the overload speed penalty, the heavier convoy is
        /// not slowed for carrying it. Villager parties only; every other party is left untouched.
        /// </summary>
        [HarmonyPatch(typeof(DefaultInventoryCapacityModel), "CalculateInventoryCapacity")]
        private static class VillagerCarryCapacityPatch
        {
            private static void Postfix(MobileParty mobileParty, ref ExplainedNumber __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || mobileParty == null || !mobileParty.IsVillager)
                {
                    return;
                }

                __result.AddFactor(VillagerCarryMultiplier - 1f, CarryText);
            }
        }
    }
}
