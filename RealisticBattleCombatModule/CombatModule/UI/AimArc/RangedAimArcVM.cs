using TaleWorlds.Library;

namespace RBMCombat
{
    /// <summary>
    /// Fixed pool of dots (the arc and the accuracy ring share it) plus one impact marker, bound by RBMRangedAimArc.xml. The pool is filled once in the
    /// constructor and never resized, so no widget is created or destroyed per frame; unused dots are hidden.
    /// </summary>
    public class RangedAimArcVM : ViewModel
    {
        private readonly MBBindingList<RangedAimArcDotVM> _dots = new MBBindingList<RangedAimArcDotVM>();
        private readonly RangedAimArcDotVM _impact = new RangedAimArcDotVM();
        private int _cursor;
        private int _lastUsed;

        public RangedAimArcVM(int dotCount)
        {
            for (int i = 0; i < dotCount; i++)
            {
                _dots.Add(new RangedAimArcDotVM());
            }
        }

        [DataSourceProperty]
        public MBBindingList<RangedAimArcDotVM> Dots => _dots;

        [DataSourceProperty]
        public RangedAimArcDotVM Impact => _impact;

        public void BeginFrame()
        {
            _cursor = 0;
        }

        public void PushDot(float x, float y, float size, float alpha, uint color)
        {
            if (_cursor >= _dots.Count)
            {
                return;
            }
            _dots[_cursor++].Set(x, y, size, alpha, color);
        }

        public void EndFrame()
        {
            for (int i = _cursor; i < _lastUsed; i++)
            {
                _dots[i].IsHidden = true;
            }
            _lastUsed = _cursor;
        }

        public void SetImpact(float x, float y, float size, float alpha, uint color)
        {
            _impact.Set(x, y, size, alpha, color);
        }

        public void HideImpact()
        {
            _impact.IsHidden = true;
        }

        public void HideAll()
        {
            for (int i = 0; i < _lastUsed; i++)
            {
                _dots[i].IsHidden = true;
            }
            _cursor = 0;
            _lastUsed = 0;
            _impact.IsHidden = true;
        }
    }

    public class RangedAimArcDotVM : ViewModel
    {
        private float _screenX;
        private float _screenY;
        private float _size = 6f;
        private float _alpha = 1f;
        private Color _dotColor = Color.FromUint(0xFFFFFFFFu);
        private bool _isHidden = true;

        // Widgets are placed by their top-left corner, so the centre goes half a size back.
        public void Set(float x, float y, float size, float alpha, uint color)
        {
            ScreenX = x - size * 0.5f;
            ScreenY = y - size * 0.5f;
            Size = size;
            Alpha = alpha;
            DotColor = Color.FromUint(color);
            IsHidden = false;
        }

        [DataSourceProperty]
        public float ScreenX
        {
            get { return _screenX; }
            set { if (_screenX != value) { _screenX = value; OnPropertyChangedWithValue(value, "ScreenX"); } }
        }

        [DataSourceProperty]
        public float ScreenY
        {
            get { return _screenY; }
            set { if (_screenY != value) { _screenY = value; OnPropertyChangedWithValue(value, "ScreenY"); } }
        }

        [DataSourceProperty]
        public float Size
        {
            get { return _size; }
            set { if (_size != value) { _size = value; OnPropertyChangedWithValue(value, "Size"); } }
        }

        [DataSourceProperty]
        public float Alpha
        {
            get { return _alpha; }
            set { if (_alpha != value) { _alpha = value; OnPropertyChangedWithValue(value, "Alpha"); } }
        }

        [DataSourceProperty]
        public Color DotColor
        {
            get { return _dotColor; }
            set { if (_dotColor != value) { _dotColor = value; OnPropertyChangedWithValue(value, "DotColor"); } }
        }

        [DataSourceProperty]
        public bool IsHidden
        {
            get { return _isHidden; }
            set { if (_isHidden != value) { _isHidden = value; OnPropertyChangedWithValue(value, "IsHidden"); } }
        }
    }
}
