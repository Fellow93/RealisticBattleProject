using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.GauntletUI.PrefabSystem;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace RBMCampaign
{
    /// <summary>
    /// The weight column of the inventory and trade lists. RBM gives the trade goods real per-lot
    /// masses spanning four orders of magnitude, so what a cart can carry is now a trading decision
    /// and not a footnote -- but the native row shows only type, name, count and value.
    ///
    /// The row's view model (<c>SPItemVM</c>) cannot simply be given a Weight property: Gauntlet
    /// builds its bindable property table by reflecting the concrete view model type
    /// (TaleWorlds.Library.ViewModel.GetPropertiesOfType), so a property that is not compiled into
    /// that class does not exist as far as a binding is concerned. What the row DOES already
    /// publish is the item's lock string id, which the native tuple binds as <c>ItemID="@StringId"</c>.
    /// So this widget takes the same binding and looks the item up itself.
    /// </summary>
    public class RBMItemWeightTextWidget : TextWidget
    {
        /// <summary>
        /// Lock string id -> item. The id is <c>item.StringId + itemModifier.StringId</c>
        /// (CampaignUIHelper.GetItemLockStringID), which cannot be split back apart reliably, so the
        /// pairs are recorded as the game mints them rather than parsed. Bounded by the number of
        /// distinct item/modifier pairs the player has ever had a row for.
        /// </summary>
        private static readonly Dictionary<string, ItemObject> _itemsByLockId = new Dictionary<string, ItemObject>();

        /// <summary>
        /// Drops the previous campaign's items. Nothing reclaims this cache during play -- it fills
        /// as ids are minted and is never emptied -- so without this it would hold a departed
        /// campaign's <see cref="ItemObject"/>s for the life of the process, and a lock id that
        /// happened to repeat would resolve against the dead one.
        ///
        /// Benign today, since the only thing read off the item is <c>Weight</c> and a given id means
        /// the same item in any campaign. It is a reset hook because every other per-campaign store
        /// in this module has one, and a cache that is stale-but-harmless by coincidence of what its
        /// one reader happens to want is not a property worth relying on.
        /// </summary>
        internal static void ResetForNewSession()
        {
            _itemsByLockId.Clear();
        }

        private string _itemId;
        private bool _isHeader;

        public RBMItemWeightTextWidget(UIContext context) : base(context)
        {
        }

        /// <summary>Bound to the row view model's StringId, i.e. the item's lock string id.</summary>
        public string ItemId
        {
            get
            {
                return _itemId;
            }
            set
            {
                if (_itemId != value)
                {
                    _itemId = value;
                    RefreshText();
                }
            }
        }

        /// <summary>
        /// Set on the one instance that labels the column in the list header, which has no row of
        /// its own to read an item off. Kept on the same widget type so the header and the cells
        /// cannot drift apart.
        /// </summary>
        public bool IsHeader
        {
            get
            {
                return _isHeader;
            }
            set
            {
                if (_isHeader != value)
                {
                    _isHeader = value;
                    RefreshText();
                }
            }
        }

        private void RefreshText()
        {
            if (_isHeader)
            {
                Text = new TextObject("{=RBM_CON_106}Wt.").ToString();
                return;
            }
            ItemObject item = Resolve(_itemId);
            Text = (item == null) ? "" : FormatWeight(item.Weight);
        }

        /// <summary>
        /// Two decimals at most, and no trailing zeroes: jewellery reads 0.03 and hardwood reads 200
        /// in the same narrow column. Invariant so the separator matches the rest of the numbers the
        /// screen prints.
        /// </summary>
        private static string FormatWeight(float weight)
        {
            return weight.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static ItemObject Resolve(string lockId)
        {
            if (string.IsNullOrEmpty(lockId))
            {
                return null;
            }
            ItemObject item;
            if (_itemsByLockId.TryGetValue(lockId, out item))
            {
                return item;
            }
            // An unmodified item's lock id is just its own string id, so the object manager answers
            // for every row that was built before this widget existed.
            item = MBObjectManager.Instance?.GetObject<ItemObject>(lockId);
            if (item != null)
            {
                _itemsByLockId[lockId] = item;
            }
            return item;
        }

        /// <summary>
        /// Records the item behind every lock id the campaign UI mints. Patching the id builder
        /// rather than the SPItemVM constructor keeps this off an eight-argument signature that a
        /// game update is far likelier to move.
        /// </summary>
        [HarmonyPatch(typeof(CampaignUIHelper))]
        [HarmonyPatch("GetItemLockStringID")]
        private class RecordItemLockId
        {
            private static void Postfix(EquipmentElement equipmentElement, string __result)
            {
                if (!string.IsNullOrEmpty(__result) && equipmentElement.Item != null)
                {
                    _itemsByLockId[__result] = equipmentElement.Item;
                }
            }
        }

        /// <summary>
        /// Both of Gauntlet's type registries scan assemblies once, before a module's assembly is in
        /// the AppDomain, so this type has to be added to each by hand. See
        /// RBMTroopSpoilsBarWidget.RegisterWidgetType for the full reasoning; this is the same dance.
        /// </summary>
        public static void RegisterWidgetType()
        {
            RegisterWidgetInfo();

            WidgetFactory factory = UIResourceManager.WidgetFactory;
            if (factory == null)
            {
                SpoilsLog.Trace("UIResourceManager.WidgetFactory was null; the item weight widget type is not registered.");
                return;
            }
            Dictionary<string, Type> builtinTypes = AccessTools.FieldRefAccess<WidgetFactory, Dictionary<string, Type>>("_builtinTypes")(factory);
            builtinTypes[nameof(RBMItemWeightTextWidget)] = typeof(RBMItemWeightTextWidget);
            SpoilsLog.Trace("registered widget type " + nameof(RBMItemWeightTextWidget));
        }

        [HarmonyPatch(typeof(WidgetInfo))]
        [HarmonyPatch("Refresh")]
        private class ReRegisterAfterWidgetInfoRefresh
        {
            private static void Postfix()
            {
                RegisterWidgetInfo();
            }
        }

        private static void RegisterWidgetInfo()
        {
            Dictionary<Type, WidgetInfo> widgetInfos =
                AccessTools.Field(typeof(WidgetInfo), "_widgetInfos").GetValue(null) as Dictionary<Type, WidgetInfo>;
            if (widgetInfos == null)
            {
                // CollectWidgetTypes has not run yet; it will pick the type up on its own.
                return;
            }
            if (!widgetInfos.ContainsKey(typeof(RBMItemWeightTextWidget)))
            {
                widgetInfos.Add(typeof(RBMItemWeightTextWidget), new WidgetInfo(typeof(RBMItemWeightTextWidget)));
                SpoilsLog.Trace("registered widget info for " + nameof(RBMItemWeightTextWidget));
            }
        }
    }
}
