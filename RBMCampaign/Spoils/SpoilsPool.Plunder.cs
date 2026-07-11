using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Plunder from settlements: a sacked village pays its raiders in coin and plate, and a town
    /// taken by storm is sacked outright. Unlike battlefield salvage, plunder is shared by the head
    /// rather than fought over piece by piece, so every man of a raiding party takes an even cut.
    /// </summary>
    public static partial class SpoilsPool
    {
        /// <summary>
        /// A sacked village pays its raiders in more than the goods they cart off: the men pocket coin
        /// and plate as they go. Fires once when a raid finishes, so it pays for a raid actually seen
        /// through rather than one the party broke off. Only a won raid plunders -- a raid the militia
        /// or a relief force turned back left the village its wealth.
        /// </summary>
        /// <remarks>
        /// The pot is the share of the village's hearth the raid actually stripped
        /// (<see cref="RaidEventComponent.RaidDamage"/> is that share, 0 to 1), scaled by
        /// troopRaidSpoilsMultiplier. It is split among the raiding parties by their contribution, the
        /// way battlefield salvage is, and then evenly among each party's men, since plunder is shared
        /// by the head and not fought over piece by piece the way kit off a field is.
        /// </remarks>
        public static void OnRaidCompleted(BattleSideEnum winnerSide, RaidEventComponent raidEvent)
        {
            if (!IsEnabled || RBMConfig.RBMConfig.troopRaidSpoilsMultiplier <= 0f || raidEvent == null)
            {
                return;
            }
            if (winnerSide != BattleSideEnum.Attacker)
            {
                return;
            }
            Settlement settlement = raidEvent.MapEventSettlement;
            Village village = settlement?.Village;
            MapEventSide attackers = raidEvent.AttackerSide;
            if (village == null || attackers == null)
            {
                return;
            }

            float pot = village.Hearth * MathF.Clamp(raidEvent.RaidDamage, 0f, 1f)
                * RBMConfig.RBMConfig.troopRaidSpoilsMultiplier;
            if (pot < 1f)
            {
                return;
            }

            long totalContribution = 0L;
            foreach (MapEventParty raider in attackers.Parties)
            {
                totalContribution += MathF.Max(0, raider.ContributionToBattle);
            }

            if (SpoilsLog.IsEnabled)
            {
                SpoilsLog.Log("RAID", "raid on " + (settlement.Name != null ? settlement.Name.ToString() : settlement.StringId)
                    + " done: hearth " + (int)village.Hearth + " x damage " + raidEvent.RaidDamage.ToString("0.00")
                    + " -> pot " + (int)pot + " across " + attackers.Parties.Count + " raider party(s)"
                    + "; raiders: " + SidePartyNames(attackers));
            }

            foreach (MapEventParty raider in attackers.Parties)
            {
                // Simulated raids can leave every contribution at zero; split evenly rather than
                // paying nobody.
                long weight = (totalContribution > 0L) ? MathF.Max(0, raider.ContributionToBattle) : 1L;
                long divisor = (totalContribution > 0L) ? totalContribution : attackers.Parties.Count;
                int share = MathF.Round(pot * ((float)weight / divisor));
                int granted = GrantFlatSpoilsToParty(raider.Party, share);
                if (raider.Party == PartyBase.MainParty && granted > 0)
                {
                    AnnounceRaidSpoilsToPlayer(settlement, granted);
                }
            }
        }

        /// <summary>
        /// Splits a lump of spoils across a party's stacks by head count, so plunder falls to every man
        /// alike rather than to whoever happened to be highest tier. Heroes take no share. Returns what
        /// was actually handed out, which rounding can leave a hair under the pot.
        /// </summary>
        private static int GrantFlatSpoilsToParty(PartyBase party, int amount)
        {
            TroopRoster roster = party?.MemberRoster;
            if (roster == null || amount <= 0)
            {
                return 0;
            }
            int totalMen = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (!element.Character.IsHero)
                {
                    totalMen += element.Number;
                }
            }
            if (totalMen <= 0)
            {
                return 0;
            }

            int granted = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character.IsHero)
                {
                    continue;
                }
                int points = (int)((long)amount * element.Number / totalMen);
                if (points <= 0)
                {
                    continue;
                }
                if (SpoilsLog.Verbose && party == PartyBase.MainParty)
                {
                    int before = GetSpoils(party, element.Character);
                    SpoilsLog.LogVerbose("RAID", party, "  " + SpoilsLog.Describe(element.Character) + " x" + element.Number
                        + ": +" + points + " (pool " + before + " -> " + (before + points) + ")");
                }
                AddSpoils(party, element.Character, points);
                granted += points;
            }
            return granted;
        }

        private static void AnnounceRaidSpoilsToPlayer(Settlement settlement, int granted)
        {
            TextObject message = new TextObject("{=RBM_SPOILS_013}Your men plunder {SETTLEMENT} and pocket {AMOUNT} in spoils.");
            message.SetTextVariable("SETTLEMENT", settlement.Name);
            message.SetTextVariable("AMOUNT", granted);
            InformationManager.DisplayMessage(new InformationMessage(message.ToString()));
        }

        /// <summary>
        /// A town or castle taken by storm is sacked by the men who took it. Fires on the settlement
        /// changing hands; only a capture by siege sacks it, so a fief handed over by barter, gift or
        /// council vote leaves its wealth alone. The plunder scales with the settlement's prosperity --
        /// a rich town is a far bigger prize than a village -- and goes to the capturing party.
        /// </summary>
        /// <remarks>
        /// Attributed to the one party the capture is credited to, the way the game credits it to a
        /// single hero, rather than split across a besieging army. Prosperity, not hearth, since towns
        /// are measured in the former; the shared troopRaidSpoilsMultiplier tunes both.
        /// </remarks>
        public static void OnSettlementCaptured(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturerHero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (!IsEnabled || RBMConfig.RBMConfig.troopRaidSpoilsMultiplier <= 0f)
            {
                return;
            }
            if (detail != ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege)
            {
                return;
            }
            Town town = settlement?.Town;
            PartyBase captor = (capturerHero ?? newOwner)?.PartyBelongedTo?.Party;
            if (town == null || captor == null)
            {
                return;
            }

            float pot = town.Prosperity * RBMConfig.RBMConfig.troopRaidSpoilsMultiplier;
            if (pot < 1f)
            {
                return;
            }

            if (SpoilsLog.IsEnabled)
            {
                SpoilsLog.Log("RAID", captor, "town " + (settlement.Name != null ? settlement.Name.ToString() : settlement.StringId)
                    + " sacked: prosperity " + (int)town.Prosperity + " -> pot " + (int)pot
                    + " to " + SpoilsLog.Describe(captor)
                    + (oldOwner != null ? " from " + (oldOwner.Name != null ? oldOwner.Name.ToString() : oldOwner.StringId) : ""));
            }

            int granted = GrantFlatSpoilsToParty(captor, MathF.Round(pot));
            if (captor == PartyBase.MainParty && granted > 0)
            {
                AnnounceSackSpoilsToPlayer(settlement, granted);
            }
        }

        private static void AnnounceSackSpoilsToPlayer(Settlement settlement, int granted)
        {
            TextObject message = new TextObject("{=RBM_SPOILS_014}Your men sack {SETTLEMENT} and pocket {AMOUNT} in spoils.");
            message.SetTextVariable("SETTLEMENT", settlement.Name);
            message.SetTextVariable("AMOUNT", granted);
            InformationManager.DisplayMessage(new InformationMessage(message.ToString()));
        }
    }
}
