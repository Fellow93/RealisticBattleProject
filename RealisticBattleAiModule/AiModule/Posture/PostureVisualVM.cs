namespace RBMAI
{
    // MBTargetEnemyStatus.TargetEnemyStatusVM
    using TaleWorlds.Library;
    using TaleWorlds.Localization;

    public class PostureVisualVM : ViewModel
    {
        private string enemyName = "";

        private string postureLabel = new TextObject("{=RBM_AI_023}Posture").ToString();
        private string staminaLabel = new TextObject("{=RBM_AI_024}Stamina").ToString();

        private bool showEnemyStatus = false;

        private bool showPlayerPostureStatus = true;

        private int enemyHealth = 100;

        private int enemyHealthMax = 50;

        private int enemyPosture = 100;

        private int enemyPostureMax = 50;

        private int playerPosture = 100;
        private string playerPostureText = "100";

        private int playerPostureMax = 50;
        private string playerPostureMaxText = "50";

        private int playerStamina = 100;
        private string playerStaminaText = "100";

        private int playerStaminaMax = 50;
        private string playerStaminaMaxText = "50";

        [DataSourceProperty]
        public string EnemyName
        {
            get
            {
                return enemyName;
            }
            set
            {
                enemyName = value;
                OnPropertyChanged("EnemyName");
                //OnPropertyChanged("DisplayText");
            }
        }

        [DataSourceProperty]
        public string PostureLabel
        {
            get
            {
                return postureLabel;
            }
            set
            {
                postureLabel = value;
                OnPropertyChanged("PostureLabel");
            }
        }

        [DataSourceProperty]
        public string StaminaLabel
        {
            get
            {
                return staminaLabel;
            }
            set
            {
                staminaLabel = value;
                OnPropertyChanged("StaminaLabel");
            }
        }

        //public string DisplayText => enemyName + $" ({enemyHealth}/{enemyHealthMax})" + $" ({enemyPosture}/{enemyPostureMax})" + $" ({playerPosture}/{playerPostureMax})";

        [DataSourceProperty]
        public bool ShowPlayerPostureStatus
        {
            get
            {
                return showPlayerPostureStatus;
            }
            set
            {
                showPlayerPostureStatus = value;
                OnPropertyChanged("ShowPlayerPostureStatus");
                //OnPropertyChanged("DisplayText");
            }
        }

        [DataSourceProperty]
        public bool ShowEnemyStatus
        {
            get
            {
                return showEnemyStatus;
            }
            set
            {
                showEnemyStatus = value;
                OnPropertyChanged("ShowEnemyStatus");
                //OnPropertyChanged("DisplayText");
            }
        }

        [DataSourceProperty]
        public int EnemyHealth
        {
            get
            {
                return enemyHealth;
            }
            set
            {
                enemyHealth = value;
                OnPropertyChangedWithValue(value, "EnemyHealth");
                //OnPropertyChanged("DisplayText");
            }
        }

        [DataSourceProperty]
        public int EnemyHealthMax
        {
            get
            {
                return enemyHealthMax;
            }
            set
            {
                enemyHealthMax = value;
                OnPropertyChangedWithValue(value, "EnemyHealthMax");
                //OnPropertyChanged("DisplayText");
            }
        }

        [DataSourceProperty]
        public int EnemyPosture
        {
            get
            {
                return enemyPosture;
            }
            set
            {
                enemyPosture = value;
                OnPropertyChangedWithValue(value, "EnemyPosture");
                //OnPropertyChanged("DisplayText");
            }
        }

        [DataSourceProperty]
        public int EnemyPostureMax
        {
            get
            {
                return enemyPostureMax;
            }
            set
            {
                enemyPostureMax = value;
                OnPropertyChangedWithValue(value, "EnemyPostureMax");
                //OnPropertyChanged("DisplayText");
            }
        }

        [DataSourceProperty]
        public int PlayerPosture
        {
            get
            {
                return playerPosture;
            }
            set
            {
                playerPosture = value;
                OnPropertyChangedWithValue(value, "PlayerPosture");
            }
        }

        [DataSourceProperty]
        public int PlayerPostureMax
        {
            get
            {
                return playerPostureMax;
            }
            set
            {
                playerPostureMax = value;
                OnPropertyChangedWithValue(value, "PlayerPostureMax");
            }
        }

        [DataSourceProperty]
        public string PlayerPostureText
        {
            get
            {
                return playerPostureText;
            }
            set
            {
                playerPostureText = value;
                OnPropertyChangedWithValue(value, "PlayerPostureText");
            }
        }

        [DataSourceProperty]
        public string PlayerPostureMaxText
        {
            get
            {
                return playerPostureMaxText;
            }
            set
            {
                playerPostureMaxText = value;
                OnPropertyChangedWithValue(value, "PlayerPostureMaxText");
            }
        }

        [DataSourceProperty]
        public int PlayerStamina
        {
            get
            {
                return playerStamina;
            }
            set
            {
                playerStamina = value;
                OnPropertyChangedWithValue(value, "PlayerStamina");
            }
        }

        [DataSourceProperty]
        public int PlayerStaminaMax
        {
            get
            {
                return playerStaminaMax;
            }
            set
            {
                playerStaminaMax = value;
                OnPropertyChangedWithValue(value, "PlayerStaminaMax");
            }
        }

        [DataSourceProperty]
        public string PlayerStaminaText
        {
            get
            {
                return playerStaminaText;
            }
            set
            {
                playerStaminaText = value;
                OnPropertyChangedWithValue(value, "PlayerStaminaText");
            }
        }

        [DataSourceProperty]
        public string PlayerStaminaMaxText
        {
            get
            {
                return playerStaminaMaxText;
            }
            set
            {
                playerStaminaMaxText = value;
                OnPropertyChangedWithValue(value, "PlayerStaminaMaxText");
            }
        }
    }
}