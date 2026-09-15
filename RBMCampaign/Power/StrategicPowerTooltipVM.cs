using System;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// What the power tooltip is told to show: a headline, and one column per side, already laid out.
    ///
    /// Deliberately three finished strings rather than the rosters themselves. The reasoning about what a man is
    /// worth belongs to StrategicTroopPower and the formatting to StrategicPowerTooltip; this only has to survive
    /// the trip through InformationManager, which hands its argument across as a bare object.
    /// </summary>
    public class RBMPowerTooltipData
    {
        public readonly string Header;

        /// <summary>The left column. Attackers, because that is the side the bar and the overlay put on the left.</summary>
        public readonly string Attackers;

        /// <summary>The right column.</summary>
        public readonly string Defenders;

        public RBMPowerTooltipData(string header, string attackers, string defenders)
        {
            Header = header ?? string.Empty;
            Attackers = attackers ?? string.Empty;
            Defenders = defenders ?? string.Empty;
        }
    }

    /// <summary>
    /// The power tooltip's own view model, and the reason this feature has a prefab at all.
    ///
    /// The ordinary hover hint is one RichTextWidget holding one string, in a proportional font. That is fine for a
    /// sentence and hopeless for a table: two columns cannot be faked with spaces when no two characters are the
    /// same width, so putting the attackers beside the defenders needs two real widgets, which needs a real prefab,
    /// which needs this.
    ///
    /// Modelled on TaleWorlds' own HintVM -- same shape, same lifecycle -- because it is loaded by the same
    /// machinery: GauntletInformationView builds it with Activator.CreateInstance(type, invokedType, args), so the
    /// (Type, object[]) constructor is not a choice, and InvokeRefreshData is what pulls the data back out of the
    /// registry the args were pushed through.
    /// </summary>
    public class RBMPowerTooltipVM : TooltipBaseVM
    {
        /// <summary>The prefab that draws this. Must match the file name of RBMXML/GUI/Prefabs/RBMPowerTooltip.xml.</summary>
        public const string MovieName = "RBMPowerTooltip";

        private string _headerText = string.Empty;

        private string _attackerText = string.Empty;

        private string _defenderText = string.Empty;

        public RBMPowerTooltipVM(Type invokedType, object[] invokedArgs)
            : base(invokedType, invokedArgs)
        {
            InvokeRefreshData(this);
            base.IsActive = true;
        }

        protected override void OnFinalizeInternal()
        {
            base.IsActive = false;
        }

        [DataSourceProperty]
        public string HeaderText
        {
            get
            {
                return _headerText;
            }
            set
            {
                if (value != _headerText)
                {
                    _headerText = value;
                    OnPropertyChangedWithValue(value, "HeaderText");
                }
            }
        }

        [DataSourceProperty]
        public string AttackerText
        {
            get
            {
                return _attackerText;
            }
            set
            {
                if (value != _attackerText)
                {
                    _attackerText = value;
                    OnPropertyChangedWithValue(value, "AttackerText");
                }
            }
        }

        [DataSourceProperty]
        public string DefenderText
        {
            get
            {
                return _defenderText;
            }
            set
            {
                if (value != _defenderText)
                {
                    _defenderText = value;
                    OnPropertyChangedWithValue(value, "DefenderText");
                }
            }
        }

        /// <summary>The registry's way back to the data. Handed to RegisterTooltip and called by InvokeRefreshData.</summary>
        public static void Refresh(RBMPowerTooltipVM tooltip, object[] args)
        {
            RBMPowerTooltipData data = ((args != null) && (args.Length > 0)) ? (args[0] as RBMPowerTooltipData) : null;
            if (data == null)
            {
                return;
            }
            tooltip.HeaderText = data.Header;
            tooltip.AttackerText = data.Attackers;
            tooltip.DefenderText = data.Defenders;
        }

        /// <summary>
        /// Teaches the tooltip system that an RBMPowerTooltipData is drawn by our prefab. Must run before anything
        /// asks for one, and mirrors where TaleWorlds register theirs (OnBeforeInitialModuleScreenSetAsRoot). Safe
        /// to call twice -- the registry is a dictionary keyed by the data type, and this just overwrites its entry.
        /// </summary>
        public static void Register()
        {
            try
            {
                InformationManager.RegisterTooltip<RBMPowerTooltipData, RBMPowerTooltipVM>(Refresh, MovieName);
                _registered = true;
            }
            catch (Exception)
            {
                _registered = false;
            }
        }

        private static bool _registered;

        /// <summary>
        /// Whether the custom tooltip may be shown at all. If registration never happened -- or threw -- asking for
        /// it would show nothing, so the caller keeps the plain hint text as its fallback and the player still gets
        /// a readable, if narrow, breakdown rather than an empty hover.
        /// </summary>
        public static bool IsRegistered
        {
            get { return _registered; }
        }
    }
}
