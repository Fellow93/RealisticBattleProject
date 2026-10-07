using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    public static partial class RecruitSupply
    {
        // ------------------------------------------------------------------ a lord's party spawned in play

        // An AI lord respawned in play levies his men from a garrison, already armed (LordRespawn); a
        // rebellion's parties are the one lord party still raised ready-made mid-game, so they are armed here.

        /// <summary>
        /// Arms the rebel clan's parties a town's rebellion raises -- the leader's and his two supporters',
        /// each filled from the culture's rebel template -- off the rebelling town's own market. By the
        /// postfix the town has passed to the rebel leader, so his clan is its owner.
        /// </summary>
        [HarmonyPatch(typeof(RebellionsCampaignBehavior), "CreateRebelPartyAndClan")]
        private class ArmRebelPartiesFromMarket
        {
            private static void Postfix(Settlement settlement)
            {
                Clan clan = settlement?.OwnerClan;
                if (!IsEnabled || clan == null || !clan.IsRebelClan)
                {
                    return;
                }
                foreach (WarPartyComponent component in clan.WarPartyComponents)
                {
                    if (component?.MobileParty != null)
                    {
                        ArmRebelParty(component.MobileParty, settlement);
                    }
                }
            }
        }

        /// <summary>
        /// Arms every man of a rebel party off the rebelling town's market, through the same draw a
        /// volunteer is armed by when he first offers himself (<see cref="ArmNewVolunteersFromMarket"/>).
        /// The rebels take what is on the shelves: the gear that leaves is the town's whole cost, and no
        /// coin moves -- not fronted by the citizens, not bought in for what the shelves lack. What is
        /// missing the rebels find off-screen. No recruit price is paid. Logs one summary line per party.
        /// </summary>
        private static void ArmRebelParty(MobileParty party, Settlement town)
        {
            if (party == null || town == null || !town.IsTown || town.ItemRoster == null)
            {
                return;
            }
            int men = 0;
            int kitValue = 0;
            int drawn = 0;
            int shortfall = 0;
            int returned = 0;
            TroopRoster roster = party.MemberRoster;
            // One priced view of the market for every stack, so the stall is priced once for the party.
            UpgradeSupply.KitStock kitStock = new UpgradeSupply.KitStock();
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (!element.Character.IsHero && element.Number > 0)
                {
                    KitDraw draw = DrawKitFromMarket(town, town, element.Character, element.Number,
                        includeMount: true, chargeOwnTown: false, kitStock: kitStock);
                    men += element.Number;
                    kitValue += draw.KitValue;
                    drawn += draw.Drawn;
                    shortfall += draw.Shortfall;
                    returned += draw.Returned;
                }
            }
            if (SpoilsLog.IsEnabled && men > 0)
            {
                SpoilsLog.Log("RECRUIT", party.Party, SpoilsLog.Describe(party.Party) + " raised in rebellion at "
                    + town.Name + " with " + men + " men; " + kitValue + "d of kit: " + drawn + "d off the shelves"
                    + (shortfall > 0 ? ", " + shortfall + "d missing found off-screen" : "")
                    + (returned > 0 ? "; " + returned + "d over need paid back" : ""));
            }
        }
    }
}
