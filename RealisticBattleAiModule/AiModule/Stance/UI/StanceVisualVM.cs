namespace RBMAI
{
    // MBTargetEnemyStatus.TargetEnemyStatusVM
    using TaleWorlds.Library;
    using TaleWorlds.Localization;

    public class StanceVisualVM : ViewModel
    {
        private string enemyName = "";

        private string postureLabel = new TextObject("{=RBM_AI_023}Posture").ToString();
        private string staminaLabel = new TextObject("{=RBM_AI_024}Stamina").ToString();

        private bool showEnemyStatus = false;

        private bool showPlayerPostureStatus = true;

        private bool showPostureBar = RBMConfig.RBMConfig.postureEnabled;
        private bool showStaminaBar = RBMConfig.RBMConfig.staminaEnabled;

        private int enemyHealth = 100;
        private int enemyHealthMax = 50;

        private int enemyPosture = 100;
        private int enemyPostureMax = 50;

        private int enemyStamina = 100;
        private int enemyStaminaMax = 50;

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
                if (enemyName == value)
                {
                    return;
                }
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
                if (postureLabel == value)
                {
                    return;
                }
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
                if (staminaLabel == value)
                {
                    return;
                }
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
                if (showPlayerPostureStatus == value)
                {
                    return;
                }
                    showPlayerPostureStatus = value;
                    OnPropertyChanged("ShowPlayerPostureStatus");
                    OnPropertyChanged("ShowPlayerPostureBar");
                    OnPropertyChanged("ShowPlayerStaminaBar");
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
                if (showEnemyStatus == value)
                {
                    return;
                }
                    showEnemyStatus = value;
                    OnPropertyChanged("ShowEnemyStatus");
                    OnPropertyChanged("ShowEnemyPostureBar");
                    OnPropertyChanged("ShowEnemyStaminaBar");
            }
        }

        [DataSourceProperty]
        public bool ShowPlayerPostureBar => showPlayerPostureStatus && showPostureBar;

        [DataSourceProperty]
        public bool ShowPlayerStaminaBar => showPlayerPostureStatus && showStaminaBar;

        [DataSourceProperty]
        public bool ShowEnemyPostureBar => showEnemyStatus && showPostureBar;

        [DataSourceProperty]
        public bool ShowEnemyStaminaBar => showEnemyStatus && showStaminaBar;

        [DataSourceProperty]
        public int EnemyHealth
        {
            get
            {
                return enemyHealth;
            }
            set
            {
                if (enemyHealth == value)
                {
                    return;
                }
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
                if (enemyHealthMax == value)
                {
                    return;
                }
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
                if (enemyPosture == value)
                {
                    return;
                }
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
                if (enemyPostureMax == value)
                {
                    return;
                }
                    enemyPostureMax = value;
                    OnPropertyChangedWithValue(value, "EnemyPostureMax");
                    //OnPropertyChanged("DisplayText");
            }
        }

        [DataSourceProperty]
        public int EnemyStamina
        {
            get
            {
                return enemyStamina;
            }
            set
            {
                if (enemyStamina == value)
                {
                    return;
                }
                    enemyStamina = value;
                    OnPropertyChangedWithValue(value, "EnemyStamina");
                    //OnPropertyChanged("DisplayText");
            }
        }

        [DataSourceProperty]
        public int EnemyStaminaMax
        {
            get
            {
                return enemyStaminaMax;
            }
            set
            {
                if (enemyStaminaMax == value)
                {
                    return;
                }
                    enemyStaminaMax = value;
                    OnPropertyChangedWithValue(value, "EnemyStaminaMax");
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
                if (playerPosture == value)
                {
                    return;
                }
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
                if (playerPostureMax == value)
                {
                    return;
                }
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
                if (playerPostureText == value)
                {
                    return;
                }
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
                if (playerPostureMaxText == value)
                {
                    return;
                }
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
                if (playerStamina == value)
                {
                    return;
                }
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
                if (playerStaminaMax == value)
                {
                    return;
                }
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
                if (playerStaminaText == value)
                {
                    return;
                }
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
                if (playerStaminaMaxText == value)
                {
                    return;
                }
                    playerStaminaMaxText = value;
                    OnPropertyChangedWithValue(value, "PlayerStaminaMaxText");
            }
        }
    }
}