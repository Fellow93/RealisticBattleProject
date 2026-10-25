namespace RBMAI
{
    using TaleWorlds.Library;

    /// <summary>
    /// One team's block of the tactics panel: title, ranked options, terrain status and flank plan lines. Row VMs are reused.
    /// </summary>
    public class BattleStatsTacticTeamVM : ViewModel
    {
        private string title = "";
        private string terrainLine = "";
        private string flankLine = "";
        private MBBindingList<BattleStatsTacticRowVM> rows = new MBBindingList<BattleStatsTacticRowVM>();

        [DataSourceProperty]
        public string Title
        {
            get
            {
                return title;
            }
            set
            {
                if (title == value)
                {
                    return;
                }
                title = value;
                OnPropertyChangedWithValue(value, "Title");
            }
        }

        [DataSourceProperty]
        public string TerrainLine
        {
            get
            {
                return terrainLine;
            }
            set
            {
                if (terrainLine == value)
                {
                    return;
                }
                terrainLine = value;
                OnPropertyChangedWithValue(value, "TerrainLine");
            }
        }

        [DataSourceProperty]
        public string FlankLine
        {
            get
            {
                return flankLine;
            }
            set
            {
                if (flankLine == value)
                {
                    return;
                }
                flankLine = value;
                OnPropertyChangedWithValue(value, "FlankLine");
            }
        }

        [DataSourceProperty]
        public MBBindingList<BattleStatsTacticRowVM> Rows
        {
            get
            {
                return rows;
            }
            set
            {
                if (rows == value)
                {
                    return;
                }
                rows = value;
                OnPropertyChangedWithValue(value, "Rows");
            }
        }

        // Resizes at the tail only, so existing row views stay.
        public void SetRowCount(int count)
        {
            while (rows.Count > count)
            {
                rows.RemoveAt(rows.Count - 1);
            }
            while (rows.Count < count)
            {
                rows.Add(new BattleStatsTacticRowVM());
            }
        }
    }

    /// <summary>One option row; values are preformatted text.</summary>
    public class BattleStatsTacticRowVM : ViewModel
    {
        private string rank = "";
        private string name = "";
        private string final = "";
        private string fit = "";
        private string multiplier = "";
        private string terrain = "";
        private bool isCurrent;

        [DataSourceProperty]
        public string Rank
        {
            get
            {
                return rank;
            }
            set
            {
                if (rank == value)
                {
                    return;
                }
                rank = value;
                OnPropertyChangedWithValue(value, "Rank");
            }
        }

        [DataSourceProperty]
        public string Name
        {
            get
            {
                return name;
            }
            set
            {
                if (name == value)
                {
                    return;
                }
                name = value;
                OnPropertyChangedWithValue(value, "Name");
            }
        }

        [DataSourceProperty]
        public string Final
        {
            get
            {
                return final;
            }
            set
            {
                if (final == value)
                {
                    return;
                }
                final = value;
                OnPropertyChangedWithValue(value, "Final");
            }
        }

        [DataSourceProperty]
        public string Fit
        {
            get
            {
                return fit;
            }
            set
            {
                if (fit == value)
                {
                    return;
                }
                fit = value;
                OnPropertyChangedWithValue(value, "Fit");
            }
        }

        [DataSourceProperty]
        public string Multiplier
        {
            get
            {
                return multiplier;
            }
            set
            {
                if (multiplier == value)
                {
                    return;
                }
                multiplier = value;
                OnPropertyChangedWithValue(value, "Multiplier");
            }
        }

        [DataSourceProperty]
        public string Terrain
        {
            get
            {
                return terrain;
            }
            set
            {
                if (terrain == value)
                {
                    return;
                }
                terrain = value;
                OnPropertyChangedWithValue(value, "Terrain");
            }
        }

        [DataSourceProperty]
        public bool IsCurrent
        {
            get
            {
                return isCurrent;
            }
            set
            {
                if (isCurrent == value)
                {
                    return;
                }
                isCurrent = value;
                OnPropertyChangedWithValue(value, "IsCurrent");
            }
        }
    }
}
