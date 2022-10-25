namespace RBMAI
{
	using TaleWorlds.GauntletUI.BaseTypes;
	// MBTargetEnemyStatus.TargetEnemyStatusVM
	using TaleWorlds.Library;

	public class BattleStatsVM : ViewModel
    {
		private string atkarc = "ATK ARC: 0";
        private string atkha = "ATK HA : 0";
        private string atkcav = "ATK CAV: 0";
        private string atkinf = "ATK INF: 0";
        private string defarc = "DEF ARC: 0";
        private string defha = "DEF HA : 0";
        private string defcav = "DEF CAV: 0";
        private string definf = "DEF INF: 0";

        [DataSourceProperty]
		public string Atkarc
		{
			get
			{
				return atkarc;
			}
			set
			{
                atkarc = value;
				OnPropertyChanged("Atkarc");
			}
		}

        [DataSourceProperty]
        public string Atkha
        {
            get
            {
                return atkha;
            }
            set
            {
                atkha = value;
                OnPropertyChanged("Atkha");
            }
        }
        [DataSourceProperty]
        public string Atkcav
        {
            get
            {
                return atkcav;
            }
            set
            {
                atkcav = value;
                OnPropertyChanged("Atkcav");
            }
        }
        [DataSourceProperty]
        public string Atkinf
        {
            get
            {
                return atkinf;
            }
            set
            {
                atkinf = value;
                OnPropertyChanged("Atkinf");
            }
        }
        [DataSourceProperty]
        public string Defarc
        {
            get
            {
                return defarc;
            }
            set
            {
                defarc = value;
                OnPropertyChanged("Defarc");
            }
        }
        [DataSourceProperty]
        public string Defha
        {
            get
            {
                return defha;
            }
            set
            {
                defha = value;
                OnPropertyChanged("Defha");
            }
        }
        [DataSourceProperty]
        public string Defcav
        {
            get
            {
                return defcav;
            }
            set
            {
                defcav = value;
                OnPropertyChanged("Defcav");
            }
        }
        [DataSourceProperty]
        public string Definf
        {
            get
            {
                return definf;
            }
            set
            {
                definf = value;
                OnPropertyChanged("Definf");
            }
        }
    }

}
