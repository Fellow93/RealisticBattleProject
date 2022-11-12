namespace RBMCombat
{
	using TaleWorlds.Library;

	public class PlayerArmorStatusVM : ViewModel
    {
        //private string helmet = "Helmet: 100%";
        //private string shoulders = "Shoulders: 100%";
        //private string body = "Body: 100%";
        //private string gloves = "Gloves: 100%";
        //private string legs = "Legs: 100%";
        //private string harness = "Harness: 100%"; 

        public const string green = "#90EE90FF";
        public const string grey = "#EEEEEEFF";
        public const string lightOrange = "#FFD580FF";
        public const string orange = "#FFA500FF";
        public const string darkOrange = "#FF7518FF";
        public const string red = "#CC0000FF";

        private string helmet = grey;
        private string shoulders = grey;
        private string body = grey;
        private string gloves = grey;
        private string legs = grey;
        private string harness = grey;

        [DataSourceProperty]
		public string Helmet
        {
			get
			{
				return helmet;
			}
			set
			{
                helmet = value;
				OnPropertyChanged("Helmet");
			}
		}

        [DataSourceProperty]
        public string Shoulders
        {
            get
            {
                return shoulders;
            }
            set
            {
                shoulders = value;
                OnPropertyChanged("Shoulders");
            }
        }

        [DataSourceProperty]
        public string Body
        {
            get
            {
                return body;
            }
            set
            {
                body = value;
                OnPropertyChanged("Body");
            }
        }

        [DataSourceProperty]
        public string Gloves
        {
            get
            {
                return gloves;
            }
            set
            {
                gloves = value;
                OnPropertyChanged("Gloves");
            }
        }

        [DataSourceProperty]
        public string Legs
        {
            get
            {
                return legs;
            }
            set
            {
                legs = value;
                OnPropertyChanged("Legs");
            }
        }

        [DataSourceProperty]
        public string Harness
        {
            get
            {
                return harness;
            }
            set
            {
                harness = value;
                OnPropertyChanged("Harness");
            }
        }
    }
}
