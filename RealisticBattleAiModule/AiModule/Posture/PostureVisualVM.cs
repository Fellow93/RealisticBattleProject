namespace RBMAI
{
	// MBTargetEnemyStatus.TargetEnemyStatusVM
	using TaleWorlds.Library;

	public class PostureVisualVM : ViewModel
	{
		private string enemyName = "";

		private string postureLabel = "Posture";

		private bool showEnemyStatus = false;

		private bool showPlayerPostureStatus = true;

		private int enemyHealth = 100;

		private int enemyHealthMax = 50;

		private int enemyPosture = 100;

		private int enemyPostureMax = 50;

		private int playerPosture = 100;

		private int playerPostureMax = 50;

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
				//OnPropertyChanged("DisplayText");
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
				//OnPropertyChanged("DisplayText");
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
				//OnPropertyChanged("DisplayText");
			}
		}
	}

}
