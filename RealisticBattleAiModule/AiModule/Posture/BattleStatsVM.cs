namespace RBMAI
{
    using TaleWorlds.Library;
    using TaleWorlds.Localization;

    public class BattleStatsVM : ViewModel
    {
        private string atkarc = new TextObject("{=RBM_AI_001}ATK ARC:").ToString() + " 0";
        private string atkha = new TextObject("{=RBM_AI_002}ATK HA :").ToString() + " 0";
        private string atkcav = new TextObject("{=RBM_AI_003}ATK CAV:").ToString() + " 0";
        private string atkinf = new TextObject("{=RBM_AI_004}ATK INF:").ToString() + " 0";
        private string defarc = new TextObject("{=RBM_AI_005}DEF ARC:").ToString() + " 0";
        private string defha = new TextObject("{=RBM_AI_006}DEF HA :").ToString() + " 0";
        private string defcav = new TextObject("{=RBM_AI_007}DEF CAV:").ToString() + " 0";
        private string definf = new TextObject("{=RBM_AI_008}DEF INF:").ToString() + " 0";

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