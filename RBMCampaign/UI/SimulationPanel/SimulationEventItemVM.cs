using TaleWorlds.Library;

namespace RBMCampaign
{
    internal class SimulationEventItemVM : ViewModel
    {
        private string _message;
        private string _eventType;
        private bool _isHeroEvent;
        private bool _isPlayerHero;
        private string _heroName;
        private string _restMessage;
        private string _heroPrefix;

        public SimulationEventItemVM(string message, string eventType)
            : this(message, eventType, null, null, false, null)
        {
        }

        // heroPrefix is whatever the translated sentence puts BEFORE the hero's name. English
        // leaves it empty (the name opens the sentence), but languages that do not front the
        // subject need somewhere to put those words, and the name keeps its own coloured widget.
        public SimulationEventItemVM(string message, string eventType,
            string heroName, string restMessage, bool isPlayerHero, string heroPrefix)
        {
            _message = message;
            _eventType = eventType;
            _isHeroEvent = heroName != null;
            _isPlayerHero = isPlayerHero && heroName != null;
            _heroName = heroName ?? "";
            _restMessage = restMessage ?? "";
            _heroPrefix = heroPrefix ?? "";
        }

        [DataSourceProperty]
        public string Message
        {
            get => _message;
            set
            {
                if (_message != value)
                {
                    _message = value;
                    OnPropertyChangedWithValue(value, "Message");
                }
            }
        }

        [DataSourceProperty]
        public bool IsHeroEvent
        {
            get => _isHeroEvent;
            set
            {
                if (_isHeroEvent != value)
                {
                    _isHeroEvent = value;
                    OnPropertyChangedWithValue(value, "IsHeroEvent");
                }
            }
        }

        [DataSourceProperty]
        public bool IsPlayerHero
        {
            get => _isPlayerHero;
            set
            {
                if (_isPlayerHero != value)
                {
                    _isPlayerHero = value;
                    OnPropertyChangedWithValue(value, "IsPlayerHero");
                }
            }
        }

        [DataSourceProperty]
        public bool IsOtherHero
        {
            get => _isHeroEvent && !_isPlayerHero;
        }

        [DataSourceProperty]
        public string HeroName
        {
            get => _heroName;
            set
            {
                if (_heroName != value)
                {
                    _heroName = value;
                    OnPropertyChangedWithValue(value, "HeroName");
                }
            }
        }

        [DataSourceProperty]
        public string HeroPrefix
        {
            get => _heroPrefix;
            set
            {
                if (_heroPrefix != value)
                {
                    _heroPrefix = value;
                    OnPropertyChangedWithValue(value, "HeroPrefix");
                }
            }
        }

        [DataSourceProperty]
        public string RestMessage
        {
            get => _restMessage;
            set
            {
                if (_restMessage != value)
                {
                    _restMessage = value;
                    OnPropertyChangedWithValue(value, "RestMessage");
                }
            }
        }

        [DataSourceProperty]
        public string EventType
        {
            get => _eventType;
            set
            {
                if (_eventType != value)
                {
                    _eventType = value;
                    OnPropertyChangedWithValue(value, "EventType");
                }
            }
        }
    }
}
