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
                int total = GrantSpoilsWeightedByTier(raider.Party, share, "RAID", out int companionGold);
                int troopGranted = total - companionGold;
                int leaderCut = (troopGranted > 0)
                    ? ApplyLeaderCut(raider.Party, troopGranted)
                    : (companionGold > 0 ? 0 : ApplyLeaderCutSolo(raider.Party, share));
                if (raider.Party == PartyBase.MainParty)
                {
                    if (troopGranted > 0)
                    {
                        AnnounceRaidSpoilsToPlayer(settlement, troopGranted);
                    }
                    AnnounceCompanionSpoilsToPlayer(companionGold);
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
        /// Strips a stormed fief's purse by the fraction its conqueror's aftermath choice calls for, and
        /// reports what became of the coin. A castle is drawn from its own treasury; a town from its
        /// market (citizen) wealth -- NOT its treasury, which is left untouched to pass to the new owner,
        /// since draining the coffers you are about to hold would only hand you a bankrupt conquest. Of
        /// what is taken, <paramref name="spoilsShare"/> is carried off by the besiegers and the rest is
        /// destroyed: burned in the streets, buried by the townsfolk, or simply spilled.
        /// </summary>
        /// <returns>The spoils pot the wealth leg contributes.</returns>
        private static int SackWealthOnCapture(Settlement settlement, float stolenFraction, float spoilsShare,
            out int held, out int stolen, out int destroyed)
        {
            bool citizens = !settlement.IsCastle;
            held = citizens ? SettlementWealth.GetCitizenWealth(settlement) : SettlementWealth.GetSettlementWealth(settlement);

            int target = MathF.Round(held * MathF.Clamp(stolenFraction, 0f, 1f));
            stolen = citizens
                ? SettlementWealth.DebitCitizens(settlement, target, SettlementWealth.Source.Sack)
                : SettlementWealth.Debit(settlement, target, SettlementWealth.Source.Sack);

            int pot = MathF.Round(stolen * MathF.Clamp(spoilsShare, 0f, 1f));
            destroyed = stolen - pot;
            return pot;
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
        /// Splits a plunder pot across several parties -- by the battle contributions vanilla worked out
        /// when one is given, otherwise by troop count, so a bigger contingent takes a bigger share --
        /// then within each party by tier weight, the same split captured spoils take, and skims each
        /// party's leader cut. Announces to the player only for a sack, to keep a multi-day siege from
        /// spamming a daily popup.
        /// </summary>
        /// <param name="contributions">
        /// Vanilla's per-party share of the siege, as percentages of the total (0-100). Null, empty or
        /// all-zero falls back to the headcount split.
        /// </param>
        private static void DistributeToParties(List<MobileParty> parties, int pot, string logCategory, Settlement settlement,
            bool announceSack, bool goodsTaken = false, Dictionary<MobileParty, float> contributions = null)
        {
            // Only trust the contribution map when it actually covers the parties being paid and carries
            // some weight; a simulated or instantly-resolved siege can leave every figure at zero.
            float totalContribution = 0f;
            if (contributions != null)
            {
                foreach (MobileParty p in parties)
                {
                    float c;
                    if (contributions.TryGetValue(p, out c) && c > 0f)
                    {
                        totalContribution += c;
                    }
                }
            }
            bool byContribution = totalContribution > 0f;

            long totalMen = 0L;
            foreach (MobileParty p in parties)
            {
                totalMen += MathF.Max(0, p.Party.MemberRoster.TotalManCount);
            }

            foreach (MobileParty p in parties)
            {
                int share;
                if (byContribution)
                {
                    float c;
                    if (!contributions.TryGetValue(p, out c) || c < 0f)
                    {
                        c = 0f;
                    }
                    share = MathF.Round(pot * (c / totalContribution));
                }
                else
                {
                    long weight = (totalMen > 0L) ? MathF.Max(0, p.Party.MemberRoster.TotalManCount) : 1L;
                    long divisor = (totalMen > 0L) ? totalMen : parties.Count;
                    share = MathF.Round(pot * ((float)weight / divisor));
                }
                int total = GrantSpoilsWeightedByTier(p.Party, share, logCategory, out int companionGold);
                int troopGranted = total - companionGold;
                int leaderCut = (troopGranted > 0)
                    ? ApplyLeaderCut(p.Party, troopGranted)
                    : (companionGold > 0 ? 0 : ApplyLeaderCutSolo(p.Party, share));
                if (announceSack && p.Party == PartyBase.MainParty)
                {
                    if (troopGranted > 0)
                    {
                        AnnounceSackSpoilsToPlayer(settlement, troopGranted, goodsTaken);
                    }
                    AnnounceCompanionSpoilsToPlayer(companionGold);
                    AnnounceLeaderCutToPlayer(leaderCut);
                }
            }
        }

        /// <summary>
        /// A fief changing hands. The sack itself no longer happens here -- it hangs off the siege
        /// aftermath the conqueror chooses (see <see cref="OnSiegeAftermathApplied"/>) -- but the two
        /// events fire in either order depending on who took the place, so this is where the besieger
        /// snapshot is parked for the aftermath to consume when the aftermath comes second.
        /// </summary>
        /// <remarks>
        /// Vanilla's ordering, verified in the decompiled sources:
        /// <c>MapEvent.FinalizeEventAux</c> dispatches <c>OnMapEventEnded</c> BEFORE
        /// <c>Component.OnBeforeMapEventFinalize</c>, and it is the latter that runs
        /// <c>SiegeCompleted</c> -> <c>ChangeOwnerOfSettlementAction.ApplyBySiege</c>. So when an AI (or a
        /// player who is only an army member) takes the fief, <c>SiegeAftermathCampaignBehavior</c>
        /// applies the aftermath from inside <c>OnMapEventEnded</c> and the aftermath comes FIRST; when
        /// the PLAYER leads the siege the aftermath waits on his menu choice and comes SECOND, after the
        /// fief has already changed hands.
        ///
        /// Hence the two-sided handshake: an aftermath that already sacked leaves a mark here to consume,
        /// and a capture with no mark parks its snapshot for the aftermath still to come.
        /// </remarks>
        public static void OnSettlementCaptured(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturerHero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (!SackActive || settlement == null)
            {
                return;
            }
            if (detail != ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege)
            {
                return;
            }
            if (!settlement.IsFortification)
            {
                // A village never reaches here by siege -- it is raided, not stormed.
                return;
            }

            if (_sackedByAftermath.Remove(settlement))
            {
                // The aftermath ran first and has already sacked the place; drop the marker and let the
                // snapshot the aftermath consumed stay consumed.
                return;
            }

            // The aftermath is still to come (player-led siege). Park the besiegers for it, and stamp the
            // moment so the fallback sweep can tell a genuinely orphaned capture from one still waiting on
            // a menu -- campaign time does not advance while that menu is open.
            _pendingCaptures[settlement] = new PendingCapture(TakeBesiegerSnapshot(settlement, capturerHero), CampaignTime.Now);
        }

        private static void AnnounceSackSpoilsToPlayer(Settlement settlement, int granted, bool goodsTaken)
        {
            TextObject message = goodsTaken
                ? new TextObject("{=RBM_SPOILS_030}Your men sack {SETTLEMENT}, stripping its markets bare, and pocket {AMOUNT} in spoils.")
                : new TextObject("{=RBM_SPOILS_014}Your men sack {SETTLEMENT} and pocket {AMOUNT} in spoils.");
            message.SetTextVariable("SETTLEMENT", settlement.Name);
            message.SetTextVariable("AMOUNT", granted);
            InformationManager.DisplayMessage(new InformationMessage(message.ToString()));
        }
    }
}
