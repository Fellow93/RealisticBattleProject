using System;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBM.AgentStatusBar
{
    public class AgentStatusVM : ViewModel
    {
        private int _health;

        private int _maxHealth;

        private Vec2 _position;

        private float _distance;

        private bool _isEnabled;

        private bool _isAgentFocused;

        private uint _teamColor;

        private float _screenDistance;

        private bool _isHidden;

        public bool _isRemoved;

        public Agent _agent;

        public bool _isHit = false;

        private const float _fadeTime = 2f;

        private bool _isKeyToggled;

        private float _fade = 2f;

        private int _screenZ;

        private uint _emptyColor;

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
                    OnPropertyChanged("Fade");
                }
            }
        }

        [DataSourceProperty]
        public bool IsHidden
        {
            get
            {
                return _isHidden;
            }
            set
            {
                if (_isHidden != value)
                {
                    _isHidden = value;
                    OnPropertyChanged("IsHidden");
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
                    OnPropertyChanged("IsAgentFocused");
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
                    OnPropertyChanged("ScreenZ");
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
                    OnPropertyChanged("ScreenDistance");
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
                if (_teamColor != value)
                {
                    uint fill;
                    uint empty;
                    if (!Mission.Current.IsFieldBattle && Mission.Current.IsFriendlyMission)
                    {
                        AssignLeaderBannerColour(out fill, out empty, in value);
                    }
                    else if (!AgentStatusBarConfig.AllyBarMatchesWithBanner && _agent.Team.IsPlayerAlly)
                    {
                        fill = AgentStatusBarConfig.AllyHealthBarFillColour;
                        empty = AgentStatusBarConfig.AllyHealthBarEmptyColour;
                    }
                    else if (!AgentStatusBarConfig.EnemyBarMatchesWithBanner && !_agent.Team.IsPlayerAlly)
                    {
                        fill = AgentStatusBarConfig.EnemyHealthBarFillColour;
                        empty = AgentStatusBarConfig.EnemyHealthBarEmptyColour;
                    }
                    else
                    {
                        AssignLeaderBannerColour(out fill, out empty, in value);
                    }
                    _teamColor = fill;
                    OnPropertyChanged("TeamColor");
                    EmptyColor = empty;
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
                if (_emptyColor != value)
                {
                    _emptyColor = value;
                    OnPropertyChanged("EmptyColor");
                }
            }
        }

        [DataSourceProperty]
        public bool IsEnabled
        {
            get
            {
                return _isEnabled;
            }
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged("IsEnabled");
                }
            }
        }

        [DataSourceProperty]
        public int Health
        {
            get
            {
                return _health;
            }
            set
            {
                if (_health != value)
                {
                    _health = value;
                    OnPropertyChanged("Health");
                }
            }
        }

        [DataSourceProperty]
        public int MaxHealth
        {
            get
            {
                return _maxHealth;
            }
            set
            {
                if (_maxHealth != value)
                {
                    _maxHealth = value;
                    OnPropertyChanged("MaxHealth");
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
                    OnPropertyChanged("ScreenXPosition");
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
                    OnPropertyChanged("ScreenYPosition");
                }
            }
        }

        [DataSourceProperty]
        public Vec2 ScreenPosition
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
                    OnPropertyChanged("ScreenPosition");
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
                    OnPropertyChanged("Distance");
                }
            }
        }

        public AgentStatusVM(Agent agent)
        {
            _agent = agent;
            _health = Convert.ToInt32(agent.Health);
            _maxHealth = Convert.ToInt32(agent.HealthLimit);
            _position = new Vec2(0f, 0f);
            TeamColor = agent.Team.Color;
            _isEnabled = true;
            _isAgentFocused = false;
            _isHidden = true;
            _isRemoved = false;
        }

        public override void OnFinalize()
        {
            _agent = null;
            base.OnFinalize();
        }

        public void OnAgentHit(bool isMainAgent)
        {
            Health = Convert.ToInt32(_agent.Health);
            if ((AgentStatusBarConfig.HealthOnHitOnlyForMainAgent && isMainAgent) || (!AgentStatusBarConfig.HealthOnHitOnlyForMainAgent && AgentStatusBarConfig.HealthOnHit))
            {
                if (!_isKeyToggled && (IsHidden || _isRemoved))
                {
                    _isHit = true;
                }
                else if (_isHit)
                {
                    _isHit = false;
                }
            }
        }

        public void tick(float dt, bool IsKeyToggled)
        {
            _isKeyToggled = IsKeyToggled;
            if (_isHit && _fade > 0f)
            {
                Fade -= dt;
            }
            else if (_isHit)
            {
                _isHit = false;
                _fade = 2f;
            }
        }

        internal void OnFocusGained()
        {
            IsAgentFocused = true;
        }

        internal void OnFocusLost()
        {
            IsAgentFocused = false;
        }

        internal void OnAgentRemoved(bool isMainAgent)
        {
            if ((AgentStatusBarConfig.HealthOnHitOnlyForMainAgent && isMainAgent) || (!AgentStatusBarConfig.HealthOnHitOnlyForMainAgent && AgentStatusBarConfig.HealthOnHit))
            {
                _isHit = true;
            }
            _isRemoved = true;
        }

        private void AssignLeaderBannerColour(out uint fill, out uint empty, in uint value)
        {
            fill = value;
            Color c = Color.FromUint(value);
            if (c.Red < 128f)
            {
                c.Red *= 1.5f;
            }
            else
            {
                c.Red *= 0.5f;
            }
            if (c.Green < 128f)
            {
                c.Green *= 1.5f;
            }
            else
            {
                c.Green *= 0.5f;
            }
            if (c.Blue < 128f)
            {
                c.Blue *= 1.5f;
            }
            else
            {
                c.Blue *= 0.5f;
            }
            empty = c.ToUnsignedInteger();
        }
    }
}