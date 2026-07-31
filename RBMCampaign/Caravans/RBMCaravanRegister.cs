using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// The register of RBM-managed supply caravans: the parties this module spawned to carry a bundle of
    /// surplus goods from one town to a short town of the same kingdom. It is what tells our Harmony
    /// patches "this caravan is ours" so they can drive it instead of vanilla's trade brain, and it
    /// remembers each caravan's errand -- where from, where to, and the manifest of goods it carries --
    /// across a save.
    ///
    /// Unlike villager convoys, these carry no intrinsic marker (a native caravan and one of ours are the
    /// same type), so the managed flag itself is saved state. The errand is spread across parallel
    /// primitive dictionaries keyed by the caravan's <see cref="MobileParty.StringId"/>, because the save
    /// system serializes those directly where it cannot serialize a struct or a party reference. The
    /// cargo is a single <c>"goodId:qty|goodId:qty"</c> manifest string per caravan (item ids never
    /// contain <c>:</c> or <c>|</c>), so a variable-length bundle still fits the flat dictionary shape.
    /// </summary>
    internal static class RBMCaravanRegister
    {
        /// <summary>Just created and still standing in its source town; the buy leg has not run yet.</summary>
        public const int StateSpawning = 0;

        /// <summary>Bought its cargo and walking to the destination town; sells on arrival there.</summary>
        public const int StateEnRoute = 1;

        /// <summary>
        /// Sold at the destination and carrying the takings (and any unsold goods) back to the source,
        /// where the coin is paid to the source citizens.
        /// </summary>
        public const int StateReturning = 2;

        /// <summary>
        /// Home, or otherwise finished, and waiting to be dissolved. The dissolve is deferred to the next
        /// hourly tick so the party is not destroyed from inside the settlement-entered event.
        /// </summary>
        public const int StateDone = 3;

        private static Dictionary<string, string> _src = new Dictionary<string, string>();
        private static Dictionary<string, string> _dst = new Dictionary<string, string>();
        private static Dictionary<string, string> _manifest = new Dictionary<string, string>();
        private static Dictionary<string, int> _state = new Dictionary<string, int>();
        private static Dictionary<string, int> _proceeds = new Dictionary<string, int>();

        /// <summary>One good and how many units of it a caravan carries.</summary>
        public struct GoodLot
        {
            public string GoodId;
            public int Qty;

            public GoodLot(string goodId, int qty)
            {
                GoodId = goodId;
                Qty = qty;
            }
        }

        /// <summary>One caravan's errand, reconstructed from the dictionaries for callers to read.</summary>
        public class Order
        {
            public string CaravanId;
            public string SourceId;
            public string DestId;
            public List<GoodLot> Goods;
            public int State;

            /// <summary>Coin collected at the destination and being carried home to the source citizens.</summary>
            public int Proceeds;
        }

        /// <summary>
        /// The order about to be bound to the next caravan created. Set by <see cref="RBMCaravanDispatch"/>
        /// immediately before it calls <c>CreateCaravanParty</c>, consumed by the creation postfix in
        /// <see cref="RBMCaravanArrival"/> -- which runs while the party is being built, before the
        /// synchronous source-entry event -- so the caravan is on the register before anything can ask
        /// whether it is ours.
        /// </summary>
        public static Order Pending;

        /// <summary>Commits <see cref="Pending"/> to the store under <paramref name="caravanId"/> and clears it.</summary>
        public static void BindPending(string caravanId)
        {
            if (Pending == null || string.IsNullOrEmpty(caravanId))
            {
                return;
            }
            _src[caravanId] = Pending.SourceId;
            _dst[caravanId] = Pending.DestId;
            _manifest[caravanId] = EncodeManifest(Pending.Goods);
            _state[caravanId] = Pending.State;
            _proceeds[caravanId] = Pending.Proceeds;
            Pending = null;
        }

        public static bool IsManaged(MobileParty party)
        {
            return party != null && _src.ContainsKey(party.StringId);
        }

        public static bool TryGetOrder(string caravanId, out Order order)
        {
            order = null;
            if (caravanId == null || !_src.ContainsKey(caravanId))
            {
                return false;
            }
            order = new Order
            {
                CaravanId = caravanId,
                SourceId = _src[caravanId],
                DestId = _dst.TryGetValue(caravanId, out string d) ? d : null,
                Goods = DecodeManifest(_manifest.TryGetValue(caravanId, out string m) ? m : null),
                State = _state.TryGetValue(caravanId, out int s) ? s : StateSpawning,
                Proceeds = _proceeds.TryGetValue(caravanId, out int pr) ? pr : 0
            };
            return true;
        }

        public static void SetState(string caravanId, int state)
        {
            if (caravanId != null && _src.ContainsKey(caravanId))
            {
                _state[caravanId] = state;
            }
        }

        public static void SetProceeds(string caravanId, int proceeds)
        {
            if (caravanId != null && _src.ContainsKey(caravanId))
            {
                _proceeds[caravanId] = proceeds;
            }
        }

        public static void Remove(string caravanId)
        {
            if (caravanId == null)
            {
                return;
            }
            _src.Remove(caravanId);
            _dst.Remove(caravanId);
            _manifest.Remove(caravanId);
            _state.Remove(caravanId);
            _proceeds.Remove(caravanId);
        }

        /// <summary>How many RBM caravans are currently on the road.</summary>
        public static int ActiveCount
        {
            get { return _src.Count; }
        }

        /// <summary>
        /// Units of <paramref name="goodId"/> already promised to <paramref name="destId"/> by caravans
        /// still in flight. Subtracted from the town's headroom at dispatch so two caravans never race to
        /// fill the same shortage.
        /// </summary>
        public static int InFlightQty(string destId, string goodId)
        {
            if (destId == null || goodId == null)
            {
                return 0;
            }
            int total = 0;
            foreach (KeyValuePair<string, string> pair in _dst)
            {
                if (pair.Value != destId || !_manifest.TryGetValue(pair.Key, out string m))
                {
                    continue;
                }
                foreach (GoodLot lot in DecodeManifest(m))
                {
                    if (lot.GoodId == goodId)
                    {
                        total += lot.Qty;
                    }
                }
            }
            return total;
        }

        public static Settlement FindSettlement(string id)
        {
            return string.IsNullOrEmpty(id) ? null : Settlement.Find(id);
        }

        public static ItemObject FindItem(string id)
        {
            if (string.IsNullOrEmpty(id) || Game.Current == null || Game.Current.ObjectManager == null)
            {
                return null;
            }
            return Game.Current.ObjectManager.GetObject<ItemObject>(id);
        }

        /// <summary>A short "120 Grain, 40 Wool" summary of a manifest for the logs.</summary>
        public static string DescribeGoods(List<GoodLot> goods)
        {
            if (goods == null || goods.Count == 0)
            {
                return "(nothing)";
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < goods.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                ItemObject item = FindItem(goods[i].GoodId);
                sb.Append(goods[i].Qty).Append(' ').Append(item != null ? item.Name.ToString() : goods[i].GoodId);
            }
            return sb.ToString();
        }

        private static string EncodeManifest(List<GoodLot> goods)
        {
            if (goods == null || goods.Count == 0)
            {
                return "";
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < goods.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('|');
                }
                sb.Append(goods[i].GoodId).Append(':').Append(goods[i].Qty);
            }
            return sb.ToString();
        }

        private static List<GoodLot> DecodeManifest(string manifest)
        {
            List<GoodLot> list = new List<GoodLot>();
            if (string.IsNullOrEmpty(manifest))
            {
                return list;
            }
            foreach (string part in manifest.Split('|'))
            {
                int colon = part.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }
                string id = part.Substring(0, colon);
                if (int.TryParse(part.Substring(colon + 1), out int qty) && qty > 0)
                {
                    list.Add(new GoodLot(id, qty));
                }
            }
            return list;
        }

        /// <summary>
        /// A managed caravan destroyed on the road before it could finish. Its cargo/coin is gone with it,
        /// which is intended and costs no one: the sale (or the payment home) simply never fires. We drop
        /// the errand and note the loss. A clean delivery removes the order before dissolving, so it never
        /// reaches this as a "loss".
        /// </summary>
        public static void OnMobilePartyDestroyed(MobileParty party, PartyBase destroyer)
        {
            if (party == null)
            {
                return;
            }
            string id = party.StringId;
            if (!_src.ContainsKey(id))
            {
                return;
            }
            if (TryGetOrder(id, out Order order)
                && (order.State == StateEnRoute || order.State == StateReturning))
            {
                CaravanLog.Lost(order);
            }
            Remove(id);
        }

        /// <summary>Drops the previous campaign's caravans before this one's save is read. See <see cref="SpoilsPool.Reset"/>.</summary>
        public static void Reset()
        {
            _src.Clear();
            _dst.Clear();
            _manifest.Clear();
            _state.Clear();
            _proceeds.Clear();
            Pending = null;
        }

        public static void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("RBM_caravanSource", ref _src);
            dataStore.SyncData("RBM_caravanDest", ref _dst);
            dataStore.SyncData("RBM_caravanManifest", ref _manifest);
            dataStore.SyncData("RBM_caravanState", ref _state);
            dataStore.SyncData("RBM_caravanProceeds", ref _proceeds);

            if (_src == null) _src = new Dictionary<string, string>();
            if (_dst == null) _dst = new Dictionary<string, string>();
            if (_manifest == null) _manifest = new Dictionary<string, string>();
            if (_state == null) _state = new Dictionary<string, int>();
            if (_proceeds == null) _proceeds = new Dictionary<string, int>();
        }
    }
}
