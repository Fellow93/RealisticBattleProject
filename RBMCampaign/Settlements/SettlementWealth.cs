using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// Two stores of wealth every settlement carries: rural wealth, the raw produce and stock a village
    /// pulls off its land, and urban wealth, the money-and-goods a town or castle turns that produce into.
    /// This file is the store only -- the dictionaries, their keys, save/load, and a sensible starting
    /// value for each settlement. No mechanic moves wealth in or out yet; that comes later.
    /// </summary>
    /// <remarks>
    /// Settlement holds no spare serialized field, so wealth cannot ride along on the settlement itself.
    /// Keyed by <see cref="MBObjectBase.StringId"/>, which is stable across a save and unique per settlement.
    /// Both stores exist for every village, castle and town; the one a settlement does not naturally make
    /// (a village makes no urban wealth, a fortification grows no rural wealth of its own) simply starts at
    /// zero until a mechanic feeds it.
    /// </remarks>
    public static class SettlementWealth
    {
        private static Dictionary<string, int> _ruralWealth = new Dictionary<string, int>();
        private static Dictionary<string, int> _urbanWealth = new Dictionary<string, int>();

        public static void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("RBM_settlementRuralWealth", ref _ruralWealth);
            dataStore.SyncData("RBM_settlementUrbanWealth", ref _urbanWealth);
            if (_ruralWealth == null)
            {
                _ruralWealth = new Dictionary<string, int>();
            }
            if (_urbanWealth == null)
            {
                _urbanWealth = new Dictionary<string, int>();
            }
        }

        /// <summary>
        /// Seeds every village, castle and town that has no entry yet. Run once a session is launched, it
        /// covers a fresh game (nothing loaded), a save the mod was just added to (nothing loaded), and any
        /// settlement a loaded save has never seen -- an entry already present keeps its stored value.
        /// </summary>
        public static void InitializeAll()
        {
            foreach (Settlement settlement in Settlement.All)
            {
                EnsureInitialized(settlement);
            }
        }

        /// <summary>Gives <paramref name="settlement"/> a starting value for each store if it lacks one.</summary>
        public static void EnsureInitialized(Settlement settlement)
        {
            if (settlement == null || !(settlement.IsVillage || settlement.IsTown || settlement.IsCastle))
            {
                return;
            }
            string key = settlement.StringId;
            if (!_ruralWealth.ContainsKey(key))
            {
                _ruralWealth[key] = InitialRuralWealth(settlement);
            }
            if (!_urbanWealth.ContainsKey(key))
            {
                _urbanWealth[key] = InitialUrbanWealth(settlement);
            }
        }

        public static int GetRuralWealth(Settlement settlement)
        {
            if (settlement == null)
            {
                return 0;
            }
            EnsureInitialized(settlement);
            int value;
            return _ruralWealth.TryGetValue(settlement.StringId, out value) ? value : 0;
        }

        public static int GetUrbanWealth(Settlement settlement)
        {
            if (settlement == null)
            {
                return 0;
            }
            EnsureInitialized(settlement);
            int value;
            return _urbanWealth.TryGetValue(settlement.StringId, out value) ? value : 0;
        }

        public static void SetRuralWealth(Settlement settlement, int value)
        {
            if (settlement == null)
            {
                return;
            }
            _ruralWealth[settlement.StringId] = value < 0 ? 0 : value;
        }

        public static void SetUrbanWealth(Settlement settlement, int value)
        {
            if (settlement == null)
            {
                return;
            }
            _urbanWealth[settlement.StringId] = value < 0 ? 0 : value;
        }

        // Placeholder for now: every settlement starts both stores at zero. A later mechanic will seed
        // these from the game's economy proxies (a village's hearth, a fortification's prosperity); until
        // then wealth only moves once something explicitly feeds it.
        private static int InitialRuralWealth(Settlement settlement)
        {
            return 0;
        }

        private static int InitialUrbanWealth(Settlement settlement)
        {
            return 0;
        }
    }
}
