using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// Money a fief's daily tick books against its owning clan, held until that clan's once-a-day finance
    /// apply pass settles it -- plus the record of which fiefs have already booked their day, so a display
    /// pass can say exactly what the next apply will settle.
    ///
    /// The timing model: <c>CampaignPeriodicEventManager</c> spreads the settlement daily ticks and the clan
    /// daily ticks across the day, each object at its own fixed point in the cycle. Between two of a clan's
    /// apply passes each of its fiefs therefore ticks exactly once, so at any moment the clan's NEXT apply
    /// settles (a) what the fiefs that have ticked since its last apply already booked -- the pending pool --
    /// plus (b) what each fief that has not ticked yet will book when it does. <see cref="ProjectNext"/>
    /// returns (a) plus the caller's projection of (b), worked out by the same formula the tick charges
    /// with, so the finance display and the apply pass agree on the figure.
    ///
    /// Both stores are serialized: the pool because it is money already moved on the settlement side, the
    /// booked marks so a save taken mid-day does not project a fief's day a second time on load.
    /// </summary>
    internal sealed class SettlementAccrualPool
    {
        // clanId -> gold booked by its fiefs' ticks since the clan's last apply pass.
        private Dictionary<string, int> _pendingByClan = new Dictionary<string, int>();

        // settlementId -> what that fief booked on its tick since its owner was last settled. Only the
        // presence of the key is read; the amount is informational.
        private Dictionary<string, int> _bookedBySettlement = new Dictionary<string, int>();

        /// <summary>
        /// Books <paramref name="amount"/> to <paramref name="clan"/>'s pool and marks the fief's day as done.
        /// Called on EVERY tick that assesses the fief -- a zero amount included -- so a fief that owed
        /// nothing today is not projected again before its owner's apply.
        /// </summary>
        public void Accrue(Settlement settlement, Clan clan, int amount)
        {
            if (settlement == null)
            {
                return;
            }
            int booked;
            _bookedBySettlement.TryGetValue(settlement.StringId, out booked);
            _bookedBySettlement[settlement.StringId] = booked + Math.Max(0, amount);
            if (clan == null || amount <= 0)
            {
                return;
            }
            int current;
            _pendingByClan.TryGetValue(clan.StringId, out current);
            _pendingByClan[clan.StringId] = current + amount;
        }

        /// <summary>
        /// Empties the clan's pool and returns it, and clears the booked marks of the fiefs it holds now so
        /// their next tick is projected again. Called once per clan per day from the apply pass -- always,
        /// even when the pool is empty, so the marks are cleared on schedule.
        /// </summary>
        public int Consume(Clan clan)
        {
            if (clan == null)
            {
                return 0;
            }
            foreach (Settlement settlement in clan.Settlements)
            {
                _bookedBySettlement.Remove(settlement.StringId);
            }
            int amount;
            if (_pendingByClan.TryGetValue(clan.StringId, out amount))
            {
                _pendingByClan.Remove(clan.StringId);
                return amount;
            }
            return 0;
        }

        /// <summary>The gold sitting in the clan's pool now, without consuming it.</summary>
        public int Pending(Clan clan)
        {
            int amount;
            return (clan != null && _pendingByClan.TryGetValue(clan.StringId, out amount)) ? amount : 0;
        }

        /// <summary>
        /// What the clan's next apply pass will settle: the pool, plus <paramref name="projectUnbooked"/> for
        /// each fief it holds that has not booked its day yet. Moves nothing.
        /// </summary>
        public int ProjectNext(Clan clan, Func<Settlement, int> projectUnbooked)
        {
            if (clan == null)
            {
                return 0;
            }
            long total = Pending(clan);
            foreach (Settlement settlement in clan.Settlements)
            {
                if (!_bookedBySettlement.ContainsKey(settlement.StringId))
                {
                    total += projectUnbooked(settlement);
                }
            }
            return (total > int.MaxValue) ? int.MaxValue : (int)total;
        }

        /// <summary>Drops the previous campaign's state, before this one's save is read.</summary>
        public void Reset()
        {
            _pendingByClan = new Dictionary<string, int>();
            _bookedBySettlement = new Dictionary<string, int>();
        }

        public void SyncData(IDataStore dataStore, string pendingKey, string bookedKey)
        {
            dataStore.SyncData(pendingKey, ref _pendingByClan);
            dataStore.SyncData(bookedKey, ref _bookedBySettlement);
            if (_pendingByClan == null)
            {
                _pendingByClan = new Dictionary<string, int>();
            }
            if (_bookedBySettlement == null)
            {
                _bookedBySettlement = new Dictionary<string, int>();
            }
        }
    }
}
