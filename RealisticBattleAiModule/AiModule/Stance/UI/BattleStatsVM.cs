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
                if (atkarc == value)
                {
                    return;
                }
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
                if (atkha == value)
                {
                    return;
                }
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
                if (atkcav == value)
                {
                    return;
                }
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
                if (atkinf == value)
                {
                    return;
                }
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
                if (defarc == value)
                {
                    return;
                }
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
                if (defha == value)
                {
                    return;
                }
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
                if (defcav == value)
                {
                    return;
                }
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
                if (definf == value)
                {
                    return;
                }
                    definf = value;
                    OnPropertyChanged("Definf");
            }
        }
    }
}