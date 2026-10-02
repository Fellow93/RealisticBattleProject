using System;
using System.Collections.Generic;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMConfig
{
    // The changelog viewer (prefab RBMChangelog.xml): versions on the left, the selected one's notes on the right.
    public class RBMChangelogVM : ViewModel
    {
        private readonly Action _onClose;

        private string _titleText;
        private string _closeText;
        private string _messageText;
        private bool _hasMessage;
        private MBBindingList<RBMChangelogVersionVM> _versions;
        private MBBindingList<RBMChangelogPageVM> _pages;

        // message: shown instead of the notes when the file is missing, unreadable or empty (null otherwise).
        internal RBMChangelogVM(List<ChangelogEntry> entries, string message, Action onClose)
        {
            _onClose = onClose;
            Versions = new MBBindingList<RBMChangelogVersionVM>();
            Pages = new MBBindingList<RBMChangelogPageVM>();
            if (entries != null)
            {
                foreach (ChangelogEntry entry in entries)
                {
                    Versions.Add(new RBMChangelogVersionVM(entry, OnVersionSelected));
                }
            }
            MessageText = message ?? string.Empty;
            HasMessage = !string.IsNullOrEmpty(message);
            RefreshValues();
            if (Versions.Count > 0)
            {
                OnVersionSelected(Versions[0]);
            }
        }

        public override void RefreshValues()
        {
            base.RefreshValues();
            TitleText = new TextObject("{=RBM_CHG_001}RBM Changelog").ToString();
            CloseText = new TextObject("{=RBM_CHG_002}Close").ToString();
        }

        // The page sits in a one-item list so each selection builds a fresh scroll panel, which starts at the top.
        private void OnVersionSelected(RBMChangelogVersionVM selected)
        {
            foreach (RBMChangelogVersionVM version in Versions)
            {
                version.IsSelected = version == selected;
            }
            Pages.Clear();
            Pages.Add(new RBMChangelogPageVM(selected.Entry));
        }

        public void ExecuteClose()
        {
            _onClose?.Invoke();
        }

        [DataSourceProperty]
        public string TitleText
        {
            get => _titleText;
            set { if (value != _titleText) { _titleText = value; OnPropertyChangedWithValue(value, "TitleText"); } }
        }

        [DataSourceProperty]
        public string CloseText
        {
            get => _closeText;
            set { if (value != _closeText) { _closeText = value; OnPropertyChangedWithValue(value, "CloseText"); } }
        }

        [DataSourceProperty]
        public string MessageText
        {
            get => _messageText;
            set { if (value != _messageText) { _messageText = value; OnPropertyChangedWithValue(value, "MessageText"); } }
        }

        [DataSourceProperty]
        public bool HasMessage
        {
            get => _hasMessage;
            set { if (value != _hasMessage) { _hasMessage = value; OnPropertyChangedWithValue(value, "HasMessage"); } }
        }

        [DataSourceProperty]
        public MBBindingList<RBMChangelogVersionVM> Versions
        {
            get => _versions;
            set { if (value != _versions) { _versions = value; OnPropertyChangedWithValue(value, "Versions"); } }
        }

        [DataSourceProperty]
        public MBBindingList<RBMChangelogPageVM> Pages
        {
            get => _pages;
            set { if (value != _pages) { _pages = value; OnPropertyChangedWithValue(value, "Pages"); } }
        }
    }

    // One button in the version list.
    public class RBMChangelogVersionVM : ViewModel
    {
        private readonly Action<RBMChangelogVersionVM> _onSelect;

        private string _name;
        private bool _isSelected;

        internal ChangelogEntry Entry { get; }

        internal RBMChangelogVersionVM(ChangelogEntry entry, Action<RBMChangelogVersionVM> onSelect)
        {
            Entry = entry;
            _onSelect = onSelect;
            Name = entry.Version;
        }

        public void ExecuteSelect()
        {
            _onSelect?.Invoke(this);
        }

        [DataSourceProperty]
        public string Name
        {
            get => _name;
            set { if (value != _name) { _name = value; OnPropertyChangedWithValue(value, "Name"); } }
        }

        [DataSourceProperty]
        public bool IsSelected
        {
            get => _isSelected;
            set { if (value != _isSelected) { _isSelected = value; OnPropertyChangedWithValue(value, "IsSelected"); } }
        }
    }

    // The right-hand pane for one version: its header and its lines.
    public class RBMChangelogPageVM : ViewModel
    {
        private string _versionText;
        private string _subtitleText;
        private bool _hasSubtitle;
        private MBBindingList<RBMChangelogLineVM> _lines;

        internal RBMChangelogPageVM(ChangelogEntry entry)
        {
            VersionText = entry.Version;
            SubtitleText = entry.Subtitle ?? string.Empty;
            HasSubtitle = !string.IsNullOrEmpty(entry.Subtitle);
            Lines = new MBBindingList<RBMChangelogLineVM>();
            foreach (ChangelogLine line in entry.Lines)
            {
                Lines.Add(new RBMChangelogLineVM(line));
            }
        }

        [DataSourceProperty]
        public string VersionText
        {
            get => _versionText;
            set { if (value != _versionText) { _versionText = value; OnPropertyChangedWithValue(value, "VersionText"); } }
        }

        [DataSourceProperty]
        public string SubtitleText
        {
            get => _subtitleText;
            set { if (value != _subtitleText) { _subtitleText = value; OnPropertyChangedWithValue(value, "SubtitleText"); } }
        }

        [DataSourceProperty]
        public bool HasSubtitle
        {
            get => _hasSubtitle;
            set { if (value != _hasSubtitle) { _hasSubtitle = value; OnPropertyChangedWithValue(value, "HasSubtitle"); } }
        }

        [DataSourceProperty]
        public MBBindingList<RBMChangelogLineVM> Lines
        {
            get => _lines;
            set { if (value != _lines) { _lines = value; OnPropertyChangedWithValue(value, "Lines"); } }
        }
    }

    // One line of notes. Exactly one of IsSection / IsBullet / IsParagraph is set; the prefab shows that variant.
    public class RBMChangelogLineVM : ViewModel
    {
        // Left margin per bullet nesting level.
        private const float IndentPerDepth = 28f;

        private string _text;
        private bool _isSection;
        private bool _isBullet;
        private bool _isParagraph;
        private float _indent;

        internal RBMChangelogLineVM(ChangelogLine line)
        {
            IsSection = line.Kind == ChangelogLineKind.Section;
            IsBullet = line.Kind == ChangelogLineKind.Bullet;
            IsParagraph = line.Kind == ChangelogLineKind.Paragraph;
            Indent = line.Depth * IndentPerDepth;
            // Section titles go to a plain TextWidget; bullets and paragraphs to a RichTextWidget.
            Text = IsSection ? ChangelogParser.ToPlainText(line.Text) : ChangelogParser.ToRichText(line.Text);
        }

        [DataSourceProperty]
        public string Text
        {
            get => _text;
            set { if (value != _text) { _text = value; OnPropertyChangedWithValue(value, "Text"); } }
        }

        [DataSourceProperty]
        public bool IsSection
        {
            get => _isSection;
            set { if (value != _isSection) { _isSection = value; OnPropertyChangedWithValue(value, "IsSection"); } }
        }

        [DataSourceProperty]
        public bool IsBullet
        {
            get => _isBullet;
            set { if (value != _isBullet) { _isBullet = value; OnPropertyChangedWithValue(value, "IsBullet"); } }
        }

        [DataSourceProperty]
        public bool IsParagraph
        {
            get => _isParagraph;
            set { if (value != _isParagraph) { _isParagraph = value; OnPropertyChangedWithValue(value, "IsParagraph"); } }
        }

        [DataSourceProperty]
        public float Indent
        {
            get => _indent;
            set { if (value != _indent) { _indent = value; OnPropertyChangedWithValue(value, "Indent"); } }
        }
    }

    // The title-screen badges (prefab RBMChangelogBadge.xml): "RBM Changelog" and "RBM Manual" under it.
    public class RBMChangelogBadgeVM : ViewModel
    {
        private readonly Action _onOpen;
        private readonly Action _onOpenManual;

        private string _badgeText;
        private string _manualText;
        private bool _hasUnseen;

        internal RBMChangelogBadgeVM(Action onOpen, Action onOpenManual)
        {
            _onOpen = onOpen;
            _onOpenManual = onOpenManual;
            RefreshValues();
        }

        public override void RefreshValues()
        {
            base.RefreshValues();
            BadgeText = new TextObject("{=RBM_CHG_001}RBM Changelog").ToString();
            ManualText = new TextObject("{=RBM_CHG_006}RBM Manual").ToString();
        }

        public void ExecuteOpen()
        {
            _onOpen?.Invoke();
        }

        public void ExecuteOpenManual()
        {
            _onOpenManual?.Invoke();
        }

        [DataSourceProperty]
        public string BadgeText
        {
            get => _badgeText;
            set { if (value != _badgeText) { _badgeText = value; OnPropertyChangedWithValue(value, "BadgeText"); } }
        }

        [DataSourceProperty]
        public string ManualText
        {
            get => _manualText;
            set { if (value != _manualText) { _manualText = value; OnPropertyChangedWithValue(value, "ManualText"); } }
        }

        // The newest changelog version has not been opened yet: the badge shows its "new" dot.
        [DataSourceProperty]
        public bool HasUnseen
        {
            get => _hasUnseen;
            set { if (value != _hasUnseen) { _hasUnseen = value; OnPropertyChangedWithValue(value, "HasUnseen"); } }
        }
    }
}
