using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Plunder from settlements: a sacked village pays its raiders in coin and plate, and a town
    /// taken by storm is sacked outright. Unlike battlefield salvage, which is fought over piece by
    /// piece, plunder is a lump the party splits by tier weight -- the same split captured spoils
    /// take -- so a veteran takes a larger cut than a green recruit of the same stack size.
    /// </summary>
    public static partial class SpoilsPool
    {
        /// <summary>
        /// The share of a raided village's drained purse the raiders carry off as spoils; the rest is
        /// burned, spoiled or hidden by the villagers and gone from the economy. The one dial on how much
        /// a raid enriches its raiders versus how much it simply destroys -- kept a constant for now,
        /// promotable to config later. Held below 1 by construction so the spoils leg can never pay out
        /// more than was drawn (which would mint money the settlement never held).
        /// </summary>
        private const float RaidSpoilsShare = 0.5f;

        /// <summary>
        /// The share of a besieged castle's treasury stripped each day the siege holds. The fief bleeds
        /// it slowly rather than all at once: a long siege drains most of the purse before the wall is
        /// even breached, a short one leaves more to be taken in the sack.
        /// </summary>
        private const float SiegeDailyDrainRate = 0.05f;

        /// <summary>
        /// Of each day's siege drain, the share the besiegers carry off as spoils; the rest is destroyed.
        /// Held below 1 by construction so the spoils leg never pays out more than was drawn.
        /// </summary>
        private const float SiegeDrainSpoilsShare = 0.5f;

        /// <summary>
        /// The share of a stormed fief's sacked pot that stays behind rather than being carried off or
        /// destroyed -- a castle's treasury, or a town's market liquidity. A conquered fief is left
        /// something to run on. (A town's TREASURY is a separate pot that is not touched at all: it passes
        /// whole to the new owner. See <see cref="SackTownOnCapture"/>.) Shared by both sacks; split into
        /// castle- and town-specific dials if they ever want to diverge.
        /// </summary>
        private const float SackRetainShare = 0.34f;

        /// <summary>
        /// Of the sacked pot removed on capture (what is not retained), the share carried off as the
        /// besiegers' spoils; the rest is destroyed. With <see cref="SackRetainShare"/> at a third, a
        /// half here splits the remainder into roughly equal thirds: kept, looted, burned. Held below 1
        /// by construction so the spoils leg never pays out more than was drawn.
        /// </summary>
        private const float SackRemovedSpoilsShare = 0.5f;

        /// <summary>
        /// The besieging parties seen on the last daily drain of each castle under siege, kept so the
        /// sack at capture can pay the whole siege rather than only the party the capture is credited to.
        /// The <see cref="SiegeEvent"/> -- and with it the besieger camp -- is already torn down by the
        /// time the owner-changed hook fires, so the list must be captured while the siege still stands.
        /// Transient: rebuilt every daily tick and dropped at capture or when the siege lifts, so it is
        /// not saved. A save reloaded mid-siege simply rebuilds it on the next daily tick.
        /// </summary>
        private static readonly Dictionary<Settlement, List<MobileParty>> _siegeBesiegers = new Dictionary<Settlement, List<MobileParty>>();

        /// <summary>
        /// A raid strips the village's purse and splits the coin: the men carry off their share as
        /// spoils, the rest is destroyed. Fires once when a raid finishes, so it pays for a raid actually
        /// seen through rather than one the party broke off. Only a won raid plunders -- a raid the
        /// militia or a relief force turned back left the village its wealth.
        /// </summary>
        /// <remarks>
        /// The purse is drained in proportion to how thoroughly the village was looted:
        /// <see cref="RaidEventComponent.RaidDamage"/> is exactly that share (0 to 1) -- it and the
        /// settlement's hit points move together by the same per-tick figure, so a raid pressed until the
        /// village is sacked (hit points to zero) saturates it at 1 and empties the purse, while one
        /// broken off early takes its proportion. Of the coin drawn, <see cref="RaidSpoilsShare"/> is
        /// carried off as spoils and the remainder destroyed. The spoils are split among the raiding
        /// parties by their contribution, the way battlefield salvage is, and then among each party's men
        /// by tier weight, the same split captured enemy spoils take.
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

            // Strip the purse in proportion to the looting, and let the ledger's clamp report what the
            // village could actually give -- a broke village pays nothing, however hard it was hit.
            int purse = SettlementWealth.GetSettlementWealth(settlement);
            int drainTarget = MathF.Round(purse * MathF.Clamp(raidEvent.RaidDamage, 0f, 1f));
            int drained = SettlementWealth.Debit(settlement, drainTarget, SettlementWealth.Source.Raid);
            if (drained < 1)
            {
                return;
            }

            // The drawn coin is already out of the village. Part of it becomes spoils in the raiders'
            // purses; the rest -- never re-credited to anyone -- is the destroyed remainder.
            int pot = MathF.Round(drained * RaidSpoilsShare);
            int destroyed = drained - pot;

            long totalContribution = 0L;
            foreach (MapEventParty raider in attackers.Parties)
            {
                totalContribution += MathF.Max(0, raider.ContributionToBattle);
            }

            if (SpoilsLog.IsEnabled)
            {
                SpoilsLog.Log("RAID", "raid on " + (settlement.Name != null ? settlement.Name.ToString() : settlement.StringId)
                    + " done: purse " + purse + " x looted " + MathF.Clamp(raidEvent.RaidDamage, 0f, 1f).ToString("0.00")
                    + " -> drained " + drained + " (spoils " + pot + ", destroyed " + destroyed + ")"
                    + " across " + attackers.Parties.Count + " raider party(s); raiders: " + SidePartyNames(attackers));
            }

            if (pot < 1)
            {
                return;
            }

            foreach (MapEventParty raider in attackers.Parties)
            {
                // Simulated raids can leave every contribution at zero; split evenly rather than
                // paying nobody.
                long weight = (totalContribution > 0L) ? MathF.Max(0, raider.ContributionToBattle) : 1L;
                long divisor = (totalContribution > 0L) ? totalContribution : attackers.Parties.Count;
                int share = MathF.Round(pot * ((float)weight / divisor));
                int granted = GrantSpoilsWeightedByTier(raider.Party, share, "RAID");
                int leaderCut = ApplyLeaderCut(raider.Party, granted);
                if (raider.Party == PartyBase.MainParty && granted > 0)
                {
                    AnnounceRaidSpoilsToPlayer(settlement, granted);
                    AnnounceLeaderCutToPlayer(leaderCut);
                }
            }
        }

        private static void AnnounceRaidSpoilsToPlayer(Settlement settlement, int granted)
        {
            TextObject message = new TextObject("{=RBM_SPOILS_013}Your men plunder {SETTLEMENT} and pocket {AMOUNT} in spoils.");
            message.SetTextVariable("SETTLEMENT", settlement.Name);
            message.SetTextVariable("AMOUNT", granted);
            InformationManager.DisplayMessage(new InformationMessage(message.ToString()));
        }

        /// <summary>
        /// Runs once per settlement per day; does its work only for a fortification actually under siege.
        /// For every besieged town and castle it snapshots the besieging parties, because the siege camp
        /// -- the only place the full roster is reachable -- is already gone by the time the fief changes
        /// hands, and the sack at capture reads that snapshot to pay every party (see
        /// <see cref="_siegeBesiegers"/>). Both bleed a slice of wealth each day, but from different pots:
        /// a castle from its treasury, a town from its market (citizen) wealth -- the same pot each is
        /// sacked from at capture. A town's treasury is left out of the daily bleed for the same reason
        /// the sack spares it: it is what passes intact to the new owner. Part of each day's draw is
        /// carried off as spoils across the besiegers, the rest destroyed. Silent to the player: a
        /// weeks-long siege would spam a daily popup, so the daily income shows only on the party's spoils
        /// bar and in the log, and the sack at the end is what announces.
        /// </summary>
        /// <remarks>
        /// The drain is a flat share of the CURRENT balance, so it tapers as the pot empties rather than
        /// running it negative; the ledger's own clamp is the final backstop.
        /// </remarks>
        public static void OnBesiegedFortificationDailyTick(Settlement settlement)
        {
            if (!IsEnabled || RBMConfig.RBMConfig.troopRaidSpoilsMultiplier <= 0f)
            {
                return;
            }
            if (settlement == null || !settlement.IsFortification)
            {
                return;
            }
            if (!settlement.IsUnderSiege || settlement.SiegeEvent == null || settlement.SiegeEvent.BesiegerCamp == null)
            {
                // Not (or no longer) besieged: drop any snapshot a past siege of this fief left behind.
                _siegeBesiegers.Remove(settlement);
                return;
            }

            // Snapshot the besiegers for both towns and castles so the sack at capture can pay the whole
            // siege.
            List<MobileParty> besiegers = CollectBesiegers(settlement.SiegeEvent);
            _siegeBesiegers[settlement] = besiegers;
            if (besiegers.Count == 0)
            {
                return;
            }

            // A castle bleeds its treasury; a town its market wealth (its treasury is spared, to pass to
            // the new owner). Same rate, same split -- only the pot differs.
            int wealth;
            int drained;
            int drainTarget;
            if (settlement.IsCastle)
            {
                wealth = SettlementWealth.GetSettlementWealth(settlement);
                drainTarget = MathF.Round(wealth * SiegeDailyDrainRate);
                drained = SettlementWealth.Debit(settlement, drainTarget, SettlementWealth.Source.Siege);
            }
            else if (settlement.IsTown)
            {
                wealth = SettlementWealth.GetCitizenWealth(settlement);
                drainTarget = MathF.Round(wealth * SiegeDailyDrainRate);
                drained = SettlementWealth.DebitCitizens(settlement, drainTarget, SettlementWealth.Source.Siege);
            }
            else
            {
                return;
            }
            if (drained < 1)
            {
                return;
            }

            int pot = MathF.Round(drained * SiegeDrainSpoilsShare);
            int destroyed = drained - pot;

            if (SpoilsLog.IsEnabled)
            {
                SpoilsLog.Log("SIEGE", "siege of " + (settlement.Name != null ? settlement.Name.ToString() : settlement.StringId)
                    + ": wealth " + wealth + " -> drained " + drained
                    + " (spoils " + pot + ", destroyed " + destroyed + ")"
                    + " across " + besiegers.Count + " besieging party(s)");
            }

            if (pot >= 1)
            {
                DistributeToParties(besiegers, pot, "SIEGE", settlement, announceSack: false);
            }
        }

        /// <summary>
        /// Sacks a castle taken by storm out of its own treasury. What the siege left is split three ways:
        /// <see cref="SackRetainShare"/> stays with the fief for its new owner (simply never drawn), and
        /// the rest is removed -- <see cref="SackRemovedSpoilsShare"/> of it carried off as spoils across
        /// every party that besieged the place, the remainder destroyed. The besieging parties come from
        /// the snapshot the daily drain kept; a siege short enough to fall before its first daily tick
        /// falls back to the party the capture is credited to.
        /// </summary>
        private static void SackCastleOnCapture(Settlement castle, Hero capturerHero)
        {
            int wealth = SettlementWealth.GetSettlementWealth(castle);
            List<MobileParty> besiegers = TakeBesiegerSnapshot(castle, capturerHero);

            // Retained coin is left in the treasury untouched; only the removable remainder is debited.
            int retained = MathF.Round(wealth * SackRetainShare);
            int removable = wealth - retained;
            int drained = SettlementWealth.Debit(castle, removable, SettlementWealth.Source.Sack);

            int pot = MathF.Round(drained * SackRemovedSpoilsShare);
            int destroyed = drained - pot;

            if (SpoilsLog.IsEnabled)
            {
                SpoilsLog.Log("SACK", "castle " + (castle.Name != null ? castle.Name.ToString() : castle.StringId)
                    + " taken: purse " + wealth + " -> retained " + retained
                    + ", drained " + drained + " (spoils " + pot + ", destroyed " + destroyed + ")"
                    + " across " + besiegers.Count + " siege party(s)");
            }

            if (pot >= 1 && besiegers.Count > 0)
            {
                DistributeToParties(besiegers, pot, "SACK", castle, announceSack: true);
            }
        }

        /// <summary>
        /// Sacks a town taken by storm out of its market (citizen) wealth -- NOT its treasury, which is
        /// left untouched to pass to the new owner; draining the coffers you are about to hold would only
        /// hand you a bankrupt conquest. What the market held is split three ways: <see cref="SackRetainShare"/>
        /// stays as circulating liquidity (never drawn), and the rest is removed -- <see cref="SackRemovedSpoilsShare"/>
        /// of it carried off as spoils across every party that besieged the place, the remainder destroyed.
        /// A sacked market recovers slowly, which is the town's lasting wound from the storm.
        /// </summary>
        private static void SackTownOnCapture(Settlement town, Hero capturerHero)
        {
            int market = SettlementWealth.GetCitizenWealth(town);
            List<MobileParty> besiegers = TakeBesiegerSnapshot(town, capturerHero);

            // Retained liquidity is left in the market untouched; only the removable remainder is debited.
            int retained = MathF.Round(market * SackRetainShare);
            int removable = market - retained;
            int drained = SettlementWealth.DebitCitizens(town, removable, SettlementWealth.Source.Sack);

            int pot = MathF.Round(drained * SackRemovedSpoilsShare);
            int destroyed = drained - pot;

            if (SpoilsLog.IsEnabled)
            {
                SpoilsLog.Log("SACK", "town " + (town.Name != null ? town.Name.ToString() : town.StringId)
                    + " taken: market " + market + " -> retained " + retained
                    + ", drained " + drained + " (spoils " + pot + ", destroyed " + destroyed + ")"
                    + " across " + besiegers.Count + " siege party(s); treasury left to the new owner");
            }

            if (pot >= 1 && besiegers.Count > 0)
            {
                DistributeToParties(besiegers, pot, "SACK", town, announceSack: true);
            }
        }

        /// <summary>Every besieging party currently in the camp, deduplicated, mobile only.</summary>
        private static List<MobileParty> CollectBesiegers(SiegeEvent siege)
        {
            List<MobileParty> list = new List<MobileParty>();
            if (siege == null || siege.BesiegerCamp == null)
            {
                return list;
            }
            foreach (PartyBase pb in siege.BesiegerCamp.GetInvolvedPartiesForEventType())
            {
                if (pb != null && pb.IsMobile && pb.MobileParty != null && !list.Contains(pb.MobileParty))
                {
                    list.Add(pb.MobileParty);
                }
            }
            return list;
        }

        /// <summary>
        /// Consumes the besieger snapshot for a captured fief -- removing it -- and keeps only the parties
        /// still alive to be paid. When no snapshot survived (a siege that fell inside a day, or a save
        /// reloaded mid-siege), falls back to the party the capture is credited to so the sack is never
        /// simply lost.
        /// </summary>
        private static List<MobileParty> TakeBesiegerSnapshot(Settlement settlement, Hero capturerHero)
        {
            List<MobileParty> snapshot;
            List<MobileParty> living = new List<MobileParty>();
            if (_siegeBesiegers.TryGetValue(settlement, out snapshot) && snapshot != null)
            {
                foreach (MobileParty p in snapshot)
                {
                    if (p != null && p.Party != null && p.Party.IsActive && !living.Contains(p))
                    {
                        living.Add(p);
                    }
                }
            }
            _siegeBesiegers.Remove(settlement);

            if (living.Count == 0 && capturerHero != null && capturerHero.PartyBelongedTo != null)
            {
                living.Add(capturerHero.PartyBelongedTo);
            }
            return living;
        }

        /// <summary>
        /// Splits a plunder pot across several parties by their troop count -- a bigger contingent takes
        /// a bigger share -- then within each party by tier weight, the same split captured spoils take,
        /// and skims each party's leader cut. Announces to the player only for a sack, to keep a
        /// multi-day siege from spamming a daily popup.
        /// </summary>
        private static void DistributeToParties(List<MobileParty> parties, int pot, string logCategory, Settlement settlement, bool announceSack)
        {
            long totalMen = 0L;
            foreach (MobileParty p in parties)
            {
                totalMen += MathF.Max(0, p.Party.MemberRoster.TotalManCount);
            }

            foreach (MobileParty p in parties)
            {
                long weight = (totalMen > 0L) ? MathF.Max(0, p.Party.MemberRoster.TotalManCount) : 1L;
                long divisor = (totalMen > 0L) ? totalMen : parties.Count;
                int share = MathF.Round(pot * ((float)weight / divisor));
                int granted = GrantSpoilsWeightedByTier(p.Party, share, logCategory);
                int leaderCut = ApplyLeaderCut(p.Party, granted);
                if (announceSack && p.Party == PartyBase.MainParty && granted > 0)
                {
                    AnnounceSackSpoilsToPlayer(settlement, granted);
                    AnnounceLeaderCutToPlayer(leaderCut);
                }
            }
        }

        /// <summary>
        /// A fief taken by storm is sacked by the men who took it. Fires on the settlement changing
        /// hands; only a capture by siege sacks it, so a fief handed over by barter, gift or council vote
        /// leaves its wealth alone. A castle is sacked out of its own treasury; a town out of its market
        /// (citizen) wealth, its treasury left to pass intact to the new owner -- robbing the treasury
        /// you are about to hold would only bankrupt your own conquest. Either way the pot is split three
        /// ways -- some kept, some spoils, some destroyed -- and the spoils go to every party that
        /// besieged the place, not only the one the capture is credited to.
        /// </summary>
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
            if (settlement == null)
            {
                return;
            }
            if (settlement.IsCastle)
            {
                SackCastleOnCapture(settlement, capturerHero);
            }
            else if (settlement.IsTown)
            {
                SackTownOnCapture(settlement, capturerHero);
            }
            // A village never reaches here by siege -- it is raided, not stormed -- so there is nothing
            // else to sack.
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
