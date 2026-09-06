using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// The sack of a stormed fief, driven by the fate its conqueror chose for it. Vanilla already asks
    /// the question -- Devastate, Pillage, or Show Mercy -- and then answers it almost entirely in
    /// prosperity and reputation; the coin it minted for the victors came out of nowhere and the goods on
    /// the shelves were never touched at all. Here the choice is what decides how deep the men go: how
    /// much of the fief's money is taken and how much of that they keep rather than burn, and how much of
    /// its market they carry off.
    /// </summary>
    /// <remarks>
    /// Every denar and every crate paid out here was first taken from the settlement, and the share not
    /// paid out is destroyed rather than banked -- a sack impoverishes a fief far more than it enriches
    /// an army, and devastation is the extreme of that: it takes nearly everything and keeps almost none
    /// of it.
    /// </remarks>
    public static partial class SpoilsPool
    {
        /// <summary>
        /// What one aftermath choice does to a fief. Prosperity is a flat fraction of what the place had;
        /// wealth is a fraction of the pot the sack draws on (a town's market wealth, a castle's
        /// treasury); goods are a fraction of the market's stock. Of what is stolen -- coin and crates
        /// alike -- only the spoils share reaches the besiegers; the rest is gone from the world.
        /// </summary>
        private struct SackTier
        {
            public readonly float Prosperity;
            public readonly float Wealth;
            public readonly float WealthSpoils;
            public readonly float Goods;
            public readonly float GoodsSpoils;

            public SackTier(float prosperity, float wealth, float wealthSpoils, float goods, float goodsSpoils)
            {
                Prosperity = prosperity;
                Wealth = wealth;
                WealthSpoils = wealthSpoils;
                Goods = goods;
                GoodsSpoils = goodsSpoils;
            }
        }

        /// <summary>
        /// Mercy spares the place; pillage is the customary right of a storming army, taking half the
        /// money and much of the market but under enough discipline that most of it survives to be
        /// carried home; devastation takes nearly everything and keeps almost none of it, because a
        /// devastated city is burned rather than looted.
        /// </summary>
        private static SackTier TierFor(SiegeAftermathAction.SiegeAftermath aftermath)
        {
            switch (aftermath)
            {
                case SiegeAftermathAction.SiegeAftermath.Devastate:
                    return new SackTier(0.50f, 0.90f, 0.1f, 0.90f, 0.1f);
                case SiegeAftermathAction.SiegeAftermath.Pillage:
                    return new SackTier(0.20f, 0.50f, 0.5f, 0.40f, 0.5f);
                default:
                    return new SackTier(0.10f, 0.10f, 0.5f, 0.00f, 0.0f);
            }
        }

        /// <summary>
        /// Food is taken at half the rate of everything else. Grain and meat are bulk, perishable and the
        /// first thing a fief hides, and an army that stripped a captured city's larder outright would
        /// starve the garrison it is about to install.
        /// </summary>
        private const float FoodTakeFactor = 0.5f;

        /// <summary>
        /// The prosperity fraction the sack takes, as a NEGATIVE number for vanilla's penalty hook.
        /// Read by <see cref="SiegeAftermathPatches"/>, which replaces vanilla's army-size curve with it.
        /// </summary>
        internal static float ProsperityPenaltyFor(Settlement settlement, SiegeAftermathAction.SiegeAftermath aftermath)
        {
            Town town = settlement != null ? settlement.Town : null;
            if (town == null)
            {
                return 0f;
            }
            return -1f * TierFor(aftermath).Prosperity * MathF.Max(0f, town.Prosperity);
        }

        /// <summary>The gate every leg of the sack shares -- the same one the raid and siege drains use.</summary>
        internal static bool SackActive
        {
            get
            {
                return RBMConfig.RBMConfig.rbmCampaignEnabled
                    && IsEnabled
                    && RBMConfig.RBMConfig.troopRaidSpoilsMultiplier > 0f;
            }
        }

        /// <summary>A capture whose aftermath has not been chosen yet, with the men waiting to be paid.</summary>
        private struct PendingCapture
        {
            public readonly List<MobileParty> Besiegers;
            public readonly CampaignTime At;

            public PendingCapture(List<MobileParty> besiegers, CampaignTime at)
            {
                Besiegers = besiegers;
                At = at;
            }
        }

        /// <summary>
        /// Captures still waiting on their aftermath -- the player-led case, where the fief changes hands
        /// before he is asked what to do with it. Transient: consumed by the aftermath, or by the sweep.
        /// </summary>
        private static readonly Dictionary<Settlement, PendingCapture> _pendingCaptures = new Dictionary<Settlement, PendingCapture>();

        /// <summary>
        /// Fiefs the aftermath has already sacked, waiting for the owner change that follows it to notice
        /// and stand down. Its value is the moment of the sack, so an entry whose owner change never
        /// arrives (a defenders' sally-out, which vanilla also routes through the aftermath) is swept
        /// rather than left to pin a settlement forever.
        /// </summary>
        private static readonly Dictionary<Settlement, CampaignTime> _sackedByAftermath = new Dictionary<Settlement, CampaignTime>();

        /// <summary>Drops both handshake tables; called from <see cref="Reset"/>.</summary>
        private static void ResetSackHandshake()
        {
            _pendingCaptures.Clear();
            _sackedByAftermath.Clear();
        }

        /// <summary>
        /// The sack proper. Runs on vanilla's aftermath event, so the fate the conqueror chose -- his own
        /// menu choice, or <c>DetermineAISiegeAftermath</c>'s weighted pick for an AI -- is what decides
        /// how hard the fief is hit. Fires for towns and castles alike, and for AI captures as much as
        /// the player's.
        /// </summary>
        /// <remarks>
        /// Vanilla routes a WON SALLY-OUT through the same event, passing the garrison's own leader as the
        /// "attacker"; that is a fief successfully defending itself, not one taken, so the faction check
        /// below stands the sack down for it.
        /// </remarks>
        public static void OnSiegeAftermathApplied(MobileParty attackerParty, Settlement settlement,
            SiegeAftermathAction.SiegeAftermath aftermathType, Clan previousSettlementOwner,
            Dictionary<MobileParty, float> partyContributions)
        {
            if (!SackActive || settlement == null || !settlement.IsFortification || attackerParty == null)
            {
                return;
            }
            // The fief only pays if it actually fell to someone else. Compared against the PREVIOUS owner
            // because the two events fire in either order: by the time a player-led aftermath runs, the
            // settlement is already his.
            if (previousSettlementOwner == null || attackerParty.MapFaction == null
                || previousSettlementOwner.MapFaction == attackerParty.MapFaction)
            {
                return;
            }
            if (_sackedByAftermath.ContainsKey(settlement))
            {
                return;
            }

            SackTier tier = TierFor(aftermathType);
            List<MobileParty> besiegers = TakePendingBesiegers(settlement, attackerParty);

            ApplySack(settlement, tier, aftermathType.ToString(), besiegers, partyContributions);
            _sackedByAftermath[settlement] = CampaignTime.Now;
        }

        /// <summary>
        /// The men to pay: the snapshot the owner change parked if the aftermath came second, otherwise
        /// the siege camp as it stands (the aftermath runs before the camp is torn down), otherwise the
        /// daily drain's snapshot, and failing all three the party the capture is credited to.
        /// </summary>
        private static List<MobileParty> TakePendingBesiegers(Settlement settlement, MobileParty attackerParty)
        {
            PendingCapture pending;
            if (_pendingCaptures.TryGetValue(settlement, out pending))
            {
                _pendingCaptures.Remove(settlement);
                if (pending.Besiegers != null && pending.Besiegers.Count > 0)
                {
                    return pending.Besiegers;
                }
            }

            List<MobileParty> live = CollectBesiegers(settlement.SiegeEvent);
            if (live.Count > 0)
            {
                _siegeBesiegers.Remove(settlement);
                return live;
            }

            return TakeBesiegerSnapshot(settlement, attackerParty.LeaderHero);
        }

        /// <summary>
        /// Takes the wealth and the goods, pays the men, and writes the whole account to the log.
        /// </summary>
        private static void ApplySack(Settlement settlement, SackTier tier, string tierName,
            List<MobileParty> besiegers, Dictionary<MobileParty, float> partyContributions)
        {
            int held;
            int coinStolen;
            int coinDestroyed;
            int pot = SackWealthOnCapture(settlement, tier.Wealth, tier.WealthSpoils, out held, out coinStolen, out coinDestroyed);

            // Only a town has a market to strip; a castle's stores are its own supply, and there is no
            // priced shelf to value them off.
            MarketSackResult goods = settlement.IsTown
                ? SackMarketGoods(settlement, tier.Goods, tier.GoodsSpoils)
                : default(MarketSackResult);
            pot += goods.Pot;

            if (SpoilsLog.IsEnabled)
            {
                SpoilsLog.Log("SACK", (settlement.IsCastle ? "castle " : "town ")
                    + (settlement.Name != null ? settlement.Name.ToString() : settlement.StringId)
                    + " taken (" + tierName + "):"
                    + " purse " + held + " -> stolen " + coinStolen + " (spoils " + (pot - goods.Pot) + ", destroyed " + coinDestroyed + ")"
                    + "; goods " + goods.Value + "d taken as " + goods.FoodUnits + " food + " + goods.OtherUnits + " other units"
                    + " (spoils " + goods.Pot + ", destroyed " + (goods.Value - goods.Pot) + ")"
                    + "; pot " + pot + " across " + besiegers.Count + " siege party(s)"
                    + (settlement.IsTown ? "; treasury left to the new owner" : ""));
            }

            if (pot >= 1 && besiegers.Count > 0)
            {
                DistributeToParties(besiegers, pot, "SACK", settlement, announceSack: true,
                    goodsTaken: goods.Pot > 0, contributions: partyContributions);
            }
        }

        /// <summary>What one market sack came to.</summary>
        private struct MarketSackResult
        {
            public int Value;
            public int Pot;
            public int FoodUnits;
            public int OtherUnits;
        }

        /// <summary>
        /// Carries off a share of a town's market stock. Everything on the shelves is fair game at
        /// <paramref name="fraction"/>, save food at half that (see <see cref="FoodTakeFactor"/>). What is
        /// taken is valued at the market's own asking price -- an army looting a besieged city's last
        /// grain takes something genuinely precious -- and of that value only
        /// <paramref name="spoilsShare"/> reaches the men; the remainder is spoilage, breakage and
        /// arson. The goods themselves are gone either way, and no coin is minted for the destroyed part.
        /// </summary>
        private static MarketSackResult SackMarketGoods(Settlement town, float fraction, float spoilsShare)
        {
            MarketSackResult result = default(MarketSackResult);
            fraction = MathF.Clamp(fraction, 0f, 1f);
            if (fraction <= 0f || town.Town == null || town.Town.Owner == null)
            {
                return result;
            }

            ItemRoster roster = town.Town.Owner.ItemRoster;
            // Priced and tallied in one pass over a stable index, then removed in a second: AddToCounts
            // reshuffles the roster, so mutating it mid-walk would skip stacks or read the wrong ones.
            List<KeyValuePair<ItemObject, int>> taken = new List<KeyValuePair<ItemObject, int>>();
            for (int i = 0; i < roster.Count; i++)
            {
                ItemRosterElement element = roster.GetElementCopyAtIndex(i);
                ItemObject item = element.EquipmentElement.Item;
                if (item == null || element.Amount <= 0)
                {
                    continue;
                }

                float share = item.IsFood ? fraction * FoodTakeFactor : fraction;
                int n = MathF.Floor(element.Amount * share);
                if (n <= 0)
                {
                    continue;
                }

                result.Value += n * TroopMarketFeedback.UnitPrice(town, item, roster, i);
                if (item.IsFood)
                {
                    result.FoodUnits += n;
                }
                else
                {
                    result.OtherUnits += n;
                }
                taken.Add(new KeyValuePair<ItemObject, int>(item, n));
            }

            foreach (KeyValuePair<ItemObject, int> line in taken)
            {
                roster.AddToCounts(line.Key, -line.Value);
            }

            result.Pot = MathF.Round(result.Value * MathF.Clamp(spoilsShare, 0f, 1f));
            return result;
        }

        /// <summary>
        /// Cleans up after the two-sided handshake, and is the backstop for a capture whose aftermath
        /// never came. Campaign time is frozen while the player's aftermath menu is open, so anything
        /// still pending once an hour has actually passed belongs to a capture path that does not raise
        /// the aftermath event at all; it is sacked at the customary Pillage rate rather than let go.
        /// </summary>
        public static void OnHourlyTickSackSweep()
        {
            if (_pendingCaptures.Count > 0)
            {
                List<Settlement> orphaned = null;
                foreach (KeyValuePair<Settlement, PendingCapture> entry in _pendingCaptures)
                {
                    if (entry.Value.At.ElapsedHoursUntilNow >= 1f)
                    {
                        if (orphaned == null)
                        {
                            orphaned = new List<Settlement>();
                        }
                        orphaned.Add(entry.Key);
                    }
                }
                if (orphaned != null)
                {
                    foreach (Settlement settlement in orphaned)
                    {
                        PendingCapture pending = _pendingCaptures[settlement];
                        _pendingCaptures.Remove(settlement);
                        if (!SackActive || pending.Besiegers == null || pending.Besiegers.Count == 0)
                        {
                            continue;
                        }
                        ApplySack(settlement, TierFor(SiegeAftermathAction.SiegeAftermath.Pillage),
                            "Pillage (no aftermath)", pending.Besiegers, null);
                        _sackedByAftermath[settlement] = CampaignTime.Now;
                    }
                }
            }

            if (_sackedByAftermath.Count == 0)
            {
                return;
            }
            List<Settlement> stale = null;
            foreach (KeyValuePair<Settlement, CampaignTime> entry in _sackedByAftermath)
            {
                if (entry.Value.ElapsedDaysUntilNow >= 1f)
                {
                    if (stale == null)
                    {
                        stale = new List<Settlement>();
                    }
                    stale.Add(entry.Key);
                }
            }
            if (stale != null)
            {
                foreach (Settlement settlement in stale)
                {
                    _sackedByAftermath.Remove(settlement);
                }
            }
        }
    }
}
