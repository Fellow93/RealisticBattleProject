// TroopHealthBar.AgentStatusWidget
using System;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.GauntletUI.ExtraWidgets;
using TaleWorlds.Library;

namespace RBM.AgentStatusBar
{
    public class AgentStatusWidget : Widget
    {
        private Vec2 _position;

        private float _distance;

        private float _screenDistance;

        private static int _distanceCutOff;

        private bool _isAgentFocused;

        private uint _teamColor;

        private FillBar _bar;

        private FillBar _postureBar;

        private FillBar _staminaBar;

        private BrushWidget _healthBar;

        private const float BACKGROUND_WIDTH = 975f;

        private const float BACKGROUND_HEIGHT = 105f;

        public static float BAR_WIDTH = AgentStatusBarConfig.BarWidth;

        public static float BAR_HEIGHT = AgentStatusBarConfig.BarHeight;

        private float _fade;

        private uint _emptyColor;

        private int _screenZ;

        private float _screenXPosition;

        private float _screenYPosition;

        [DataSourceProperty]
        public float Fade
        {
            get
            {
                return _fade;
            }
            set
            {
                if (_fade != value)
                {
                    _fade = value;
                    GauntletExtensions.SetGlobalAlphaRecursively((Widget)(object)this, value);
                }
            }
        }

        [DataSourceProperty]
        public float ScreenDistance
        {
            get
            {
                return _screenDistance;
            }
            set
            {
                if (_screenDistance != value)
                {
                    _screenDistance = value;
                }
            }
        }

        [DataSourceProperty]
        public uint TeamColor
        {
            get
            {
                return _teamColor;
            }
            set
            {
                //IL_002e: Unknown result type (might be due to invalid IL or missing references)
                //IL_0038: Expected O, but got Unknown
                if (_teamColor != value)
                {
                    _teamColor = value;
                    if (_healthBar == null)
                    {
                        _healthBar = (BrushWidget)((Widget)this).GetChild(0);
                    }
                    _healthBar.Brush.GetLayer("ChangeFill").Color = Color.FromUint(value);
                }
            }
        }

        [DataSourceProperty]
        public uint EmptyColor
        {
            get
            {
                return _emptyColor;
            }
            set
            {
                //IL_002e: Unknown result type (might be due to invalid IL or missing references)
                //IL_0038: Expected O, but got Unknown
                if (_emptyColor != value)
                {
                    _emptyColor = value;
                    if (_healthBar == null)
                    {
                        _healthBar = (BrushWidget)((Widget)this).GetChild(0);
                    }
                    _healthBar.Brush.GetLayer("EmptyFill").Color = Color.FromUint(value);
                }
            }
        }

        [DataSourceProperty]
        public int ScreenZ
        {
            get
            {
                return _screenZ;
            }
            set
            {
                if (_screenZ != value)
                {
                    _screenZ = value;
                }
            }
        }

        [DataSourceProperty]
        public bool IsAgentFocused
        {
            get
            {
                return _isAgentFocused;
            }
            set
            {
                if (_isAgentFocused != value)
                {
                    _isAgentFocused = value;
                }
            }
        }

        [DataSourceProperty]
        public Vec2 Position
        {
            get
            {
                return _position;
            }
            set
            {
                if (_position != value)
                {
                    _position = value;
                    if (_bar != null)
                    {
                        _position.x -= ((Widget)_bar).ScaledSuggestedWidth * 0.5f;
                    }
                }
            }
        }

        [DataSourceProperty]
        public float ScreenXPosition
        {
            get
            {
                return _screenXPosition;
            }
            set
            {
                if (_screenXPosition != value)
                {
                    _screenXPosition = value;
                }
            }
        }

        [DataSourceProperty]
        public float ScreenYPosition
        {
            get
            {
                return _screenYPosition;
            }
            set
            {
                if (_screenYPosition != value)
                {
                    _screenYPosition = value;
                }
            }
        }

        [DataSourceProperty]
        public float Distance
        {
            get
            {
                return _distance;
            }
            set
            {
                if (_distance != value)
                {
                    _distance = value;
                }
            }
        }

        [DataSourceProperty]
        public int DistanceCutOff
        {
            get
            {
                return _distanceCutOff;
            }
            set
            {
                if (_distanceCutOff != value)
                {
                    _distanceCutOff = value;
                }
            }
        }

        public AgentStatusWidget(UIContext context)
            : base(context)
        {
            _isAgentFocused = false;
            IsFocusable = true;
            _screenDistance = 1f;
            IsHidden = true;
        }

        public static void UpdateHeightAndWidth()
        {
            BAR_WIDTH = AgentStatusBarConfig.BarWidth;
            BAR_HEIGHT = AgentStatusBarConfig.BarHeight;
        }

        protected override void OnLateUpdate(float dt)
        {
            base.OnLateUpdate(dt);
            if (_bar != null)
            {
                if (IsVisible)
                {
                    float inverse = 1f / ScreenDistance;
                    float scaledWidth, scaledHeight;
                    try
                    {
                        scaledWidth = BAR_WIDTH * inverse;
                        scaledHeight = BAR_HEIGHT * inverse;
                        _bar.ScaledSuggestedWidth = scaledWidth;
                        _bar.ScaledSuggestedHeight = scaledHeight;
                        if (_postureBar != null)
                        {
                            _postureBar.ScaledSuggestedWidth = scaledWidth;
                            _postureBar.ScaledSuggestedHeight = scaledHeight;
                            _postureBar.ScaledPositionYOffset = scaledHeight;
                        }
                        if (_staminaBar != null)
                        {
                            _staminaBar.ScaledSuggestedWidth = scaledWidth;
                            _staminaBar.ScaledSuggestedHeight = scaledHeight;
                            _staminaBar.ScaledPositionYOffset = scaledHeight * 2f;
                        }
                    }
                    catch (OverflowException)
                    {
                        return;
                    }
                    ScaledPositionXOffset = ScreenXPosition - _bar.ScaledSuggestedWidth * 0.5f;
                    ScaledPositionYOffset = ScreenYPosition;
                }
            }
            else
            {
                _bar = (FillBar)GetChild(0);
                _postureBar = GetChild(1) as FillBar;
                _staminaBar = GetChild(2) as FillBar;
            }
        }
    }
}