using System;
using TaleWorlds.Library;

namespace RBMCampaign
{
    // One entry in the ledger's left-hand tab strip. Holds its own display name plus the
    // (placeholder, for now) body text it should render when selected, and reports clicks
    // back to the owning RBMLedgerViewModel so it can swap the content pane.
    public class RBMLedgerTabVM : ViewModel
    {
        private readonly Action<RBMLedgerTabVM> _onSelect;
        private string _name;
        private bool _isSelected;

        public RBMLedgerTabVM(string tabId, string name, string title, string content, Action<RBMLedgerTabVM> onSelect)
        {
            _onSelect = onSelect;
            TabId = tabId;
            Name = name;
            Title = title;
            Content = content;
        }

        // Stable id used by the parent to decide which content pane to show (e.g. "villages").
        public string TabId { get; }

        // Header + body shown in the content pane when this tab is active. Not data-bound
        // directly (the parent copies them into CurrentTitle/CurrentContent on select).
        public string Title { get; }

        public string Content { get; }

        [DataSourceProperty]
        public string Name
        {
            get => _name;
            set
            {
                if (value != _name)
                {
                    _name = value;
                    OnPropertyChangedWithValue(value, "Name");
                }
            }
        }

        [DataSourceProperty]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (value != _isSelected)
                {
                    _isSelected = value;
                    OnPropertyChangedWithValue(value, "IsSelected");
                }
            }
        }

        // Bound from the prefab tab button's Command.Click.
        private void ExecuteSelect()
        {
            _onSelect?.Invoke(this);
        }
    }
}
