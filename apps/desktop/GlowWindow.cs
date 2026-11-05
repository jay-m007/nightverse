using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Nightverse
{
    /// <summary>
    /// A transparent, click-through, always-on-top window that paints a soft glow
    /// along the screen edges in the album's accent colour (the "Lumn" effect).
    /// </summary>
    public sealed class GlowWindow : Window
    {
        private readonly GradientStop[] _stops;
        private readonly Grid _root = new();
        private double _intensity = 0.8;
        private Color _color = Color.FromRgb(255, 170, 60);

        public GlowWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Title = "Nightverse Glow";
            // primary screen only for v1 (multi-monitor: one GlowWindow per screen)
            Left = 0; Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
            Opacity = 0;

            var stops = new System.Collections.Generic.List<GradientStop>();
            Rectangle Edge(double angle, HorizontalAlignment h, VerticalAlignment v, bool horizontal)
            {
                var a = new GradientStop(_color, 0);
                var b = new GradientStop(Color.FromArgb(0, _color.R, _color.G, _color.B), 1);
                stops.Add(a); stops.Add(b);
                var brush = new LinearGradientBrush(new GradientStopCollection { a, new GradientStop(Color.FromArgb(0x55, _color.R, _color.G, _color.B), 0.35), b }, angle);
                stops.Add(brush.GradientStops[1]);
                var r = new Rectangle { Fill = brush, HorizontalAlignment = h, VerticalAlignment = v, IsHitTestVisible = false };
                if (horizontal) r.Height = 10; else r.Width = 10;
                _root.Children.Add(r);
                return r;
            }
            Edge(90, HorizontalAlignment.Stretch, VerticalAlignment.Top, true);      // top: colour at top, fades down
            Edge(270, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, true);  // bottom
            Edge(0, HorizontalAlignment.Left, VerticalAlignment.Stretch, false);     // left
            Edge(180, HorizontalAlignment.Right, VerticalAlignment.Stretch, false);  // right
            _stops = stops.ToArray();
            Content = _root;
            ApplyThickness();

            // slow "breathing" so it feels alive
            var breathe = new DoubleAnimation(1.0, 0.72, TimeSpan.FromSeconds(3.2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            _root.BeginAnimation(OpacityProperty, breathe);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        public double Intensity
        {
            get => _intensity;
            set { _intensity = Math.Clamp(value, 0.1, 1.0); ApplyThickness(); }
        }

        private void ApplyThickness()
        {
            double t = Math.Min(Width, Height) * (0.05 + 0.09 * _intensity);
            foreach (var child in _root.Children)
            {
                if (child is Rectangle r)
                {
                    if (r.VerticalAlignment == VerticalAlignment.Stretch) r.Width = t; else r.Height = t;
                }
            }
        }

        public void SetColor(Color c)
        {
            _color = c;
            var dur = TimeSpan.FromSeconds(1.2);
            foreach (var s in _stops)
            {
                byte alpha = s.Offset == 0 ? (byte)(200 + 55 * _intensity) : s.Offset >= 1 ? (byte)0 : (byte)(70 * _intensity + 20);
                s.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(Color.FromArgb(alpha, c.R, c.G, c.B), dur));
            }
        }

        public void FadeTo(bool visible)
        {
            if (visible && !IsVisible) Show();
            var anim = new DoubleAnimation(visible ? 1 : 0, TimeSpan.FromSeconds(visible ? 0.8 : 1.2));
            if (!visible) anim.Completed += (_, _) => { if (Opacity == 0) Hide(); };
            BeginAnimation(OpacityProperty, anim);
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
