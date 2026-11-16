using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Nightverse.Services;

namespace Nightverse
{
    /// <summary>
    /// Ambient screen lighting: soft, slowly flowing blobs of colour that hug the screen edges and
    /// swell on the beat. The window is transparent, never takes focus and lets every click through,
    /// and it only covers the usable desktop (not the taskbar).
    /// </summary>
    public sealed class GlowWindow : Window
    {
        private sealed class Blob
        {
            public Ellipse Shape = null!;
            public RadialGradientBrush Brush = null!;
            public double U, Drift, Phase, Wobble, Size;
        }

        private const int BlobCount = 10;
        private readonly Canvas _canvas = new();
        private readonly List<Blob> _blobs = new();
        private const int BandStops = 24;
        private readonly Rectangle[] _rims = new Rectangle[4];
        private readonly LinearGradientBrush[] _rimBrushes = new LinearGradientBrush[4];
        private AudioAnalyzer? _audio;

        private List<Color> _palette = new() { Color.FromRgb(255, 42, 85), Color.FromRgb(53, 255, 138), Color.FromRgb(45, 123, 255) };
        private string _style = "ambient";
        private long _stepBeats;
        private double _hueStep;
        private bool _albumMode;           // "album colour" mode
        private Color _album = Color.FromRgb(255, 170, 60);
        private double _react = 0.7, _speed = 0.35, _reach = 0.45, _bright = 0.8;

        private double _w, _h;
        private double _flow, _hue, _bass, _kick;
        private long _seenBeats;
        private DateTime _last = DateTime.UtcNow;
        private readonly DateTime _t0 = DateTime.UtcNow;
        private bool _rendering;

        public GlowWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            IsHitTestVisible = false;
            ResizeMode = ResizeMode.NoResize;
            Title = "Nightverse Glow";
            Opacity = 0;

            // cover the usable desktop only, so the taskbar and its buttons are never touched
            var wa = SystemParameters.WorkArea;
            Left = wa.Left; Top = wa.Top; Width = wa.Width; Height = wa.Height;
            _w = wa.Width; _h = wa.Height;

            _canvas.IsHitTestVisible = false;
            Content = _canvas;

            // A slim glowing frame hugging all four edges. Colour flows around it along the length (Fill),
            // and an opacity mask fades it to fully transparent towards the centre so lyrics stay crisp.
            for (int i = 0; i < 4; i++)
            {
                var fill = new LinearGradientBrush();
                for (int k = 0; k < BandStops; k++) fill.GradientStops.Add(new GradientStop(Colors.Transparent, k / (double)(BandStops - 1)));
                var mask = new LinearGradientBrush();
                mask.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 0));
                mask.GradientStops.Add(new GradientStop(Color.FromArgb(140, 0, 0, 0), 0.22));
                mask.GradientStops.Add(new GradientStop(Color.FromArgb(45, 0, 0, 0), 0.5));
                mask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
                switch (i)
                {
                    case 0: fill.StartPoint = new Point(0, 0); fill.EndPoint = new Point(1, 0); mask.StartPoint = new Point(0, 0); mask.EndPoint = new Point(0, 1); break; // top
                    case 1: fill.StartPoint = new Point(0, 0); fill.EndPoint = new Point(0, 1); mask.StartPoint = new Point(1, 0); mask.EndPoint = new Point(0, 0); break; // right
                    case 2: fill.StartPoint = new Point(1, 0); fill.EndPoint = new Point(0, 0); mask.StartPoint = new Point(0, 1); mask.EndPoint = new Point(0, 0); break; // bottom
                    default: fill.StartPoint = new Point(0, 1); fill.EndPoint = new Point(0, 0); mask.StartPoint = new Point(0, 0); mask.EndPoint = new Point(1, 0); break; // left
                }
                _rimBrushes[i] = fill;
                _rims[i] = new Rectangle { Fill = fill, OpacityMask = mask, IsHitTestVisible = false };
                _canvas.Children.Add(_rims[i]);
            }

            IsVisibleChanged += (_, _) => SetRendering(IsVisible);
        }

        // ---------- public API ----------
        public void UseAudio(AudioAnalyzer audio) => _audio = audio;

        public void Configure(LightingSettings s)
        {
            _react = Math.Clamp(s.React, 0, 1);
            _speed = Math.Clamp(s.Speed, 0, 1);
            _reach = Math.Clamp(s.Reach, 0.05, 1);
            _bright = Math.Clamp(s.Brightness, 0.05, 1);
            var pal = new List<Color>();
            foreach (var hex in s.Colors)
                if (TryHex(hex, out var c)) pal.Add(c);
            if (pal.Count == 0) pal.Add(Color.FromRgb(255, 170, 60));
            _palette = pal;
            _style = s.Style == "static" || s.Style == "dynamic" ? s.Style : "ambient";
            _albumMode = string.Equals(s.Mode, "album", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Colour from the current cover art; only used in "album" mode.</summary>
        public void SetAlbumColor(Color c) => _album = c;

        public void FadeTo(bool visible)
        {
            if (visible && !IsVisible) Show();
            var anim = new System.Windows.Media.Animation.DoubleAnimation(visible ? 1 : 0, TimeSpan.FromSeconds(visible ? 0.8 : 1.2));
            if (!visible) anim.Completed += (_, _) => { if (Opacity <= 0.001) Hide(); };
            BeginAnimation(OpacityProperty, anim);
        }

        // ---------- rendering ----------
        private void SetRendering(bool on)
        {
            if (on == _rendering) return;
            _rendering = on;
            if (on) { _last = DateTime.UtcNow; CompositionTarget.Rendering += OnRender; }
            else CompositionTarget.Rendering -= OnRender;
        }

        private static Color Lerp(Color a, Color b, double t) =>
            Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        private Color PaletteAt(double p)
        {
            if (_albumMode)
            {
                // gentle shimmer around the album colour
                double k = 0.5 + 0.5 * Math.Sin(p * 1.3);
                var album = _album;
                return Lerp(album, Color.FromRgb((byte)Math.Min(255, album.R + 50), (byte)Math.Min(255, album.G + 50), (byte)Math.Min(255, album.B + 50)), k * 0.5);
            }
            int n = _palette.Count;
            if (n == 1) return _palette[0];
            int a = ((int)Math.Floor(p) % n + n) % n, b = (a + 1) % n;
            double f = p - Math.Floor(p);
            double e = f * f * (3 - 2 * f);
            return Lerp(_palette[a], _palette[b], e);
        }

        private (double x, double y, double nx, double ny) Perimeter(double u)
        {
            u = (u % 1 + 1) % 1;
            double P = 2 * (_w + _h), d = u * P;
            if (d < _w) return (d, 0, 0, 1);
            if (d < _w + _h) return (_w, d - _w, -1, 0);
            if (d < 2 * _w + _h) return (_w - (d - _w - _h), _h, 0, -1);
            return (0, _h - (d - 2 * _w - _h), 1, 0);
        }

        private void OnRender(object? sender, EventArgs e)
        {
            var now = DateTime.UtcNow;
            double dt = Math.Min(0.05, (now - _last).TotalSeconds); _last = now;
            double t = (now - _t0).TotalSeconds;
            if (dt <= 0) return;

            bool isStatic = _style == "static", isDynamic = _style == "dynamic";
            double react = isStatic ? 0 : isDynamic ? Math.Min(1, _react * 1.35 + 0.25) : _react;

            // --- beat / bass ---
            double target;
            bool signal = _audio != null && _audio.HasSignal;
            if (isStatic) { target = 0; _kick = 0; }
            else if (signal)
            {
                long beats = _audio!.BeatCount;
                if (beats != _seenBeats)
                {
                    _seenBeats = beats;
                    _kick = Math.Max(_kick, _audio.BeatStrength);
                    if (isDynamic) _hueStep += 0.5;           // dynamic: the colour jumps to the next shade on every beat
                }
                target = Math.Max(_audio.Bass * (isDynamic ? 0.8 : 0.55), _kick);
            }
            else
            {
                // no audio signal: a slow calm breathing so the light still feels alive
                target = 0.18 + 0.12 * Math.Sin(t * 1.1);
            }
            _kick = Math.Max(0, _kick - dt * (isDynamic ? 5.5 : 4.2));      // beat flash fades out quickly
            _bass += (target - _bass) * Math.Min(1, dt * (target > _bass ? 30 : (isDynamic ? 11 : 7)));

            if (!isStatic)
            {
                _flow += dt * (0.012 + _speed * (isDynamic ? 0.16 : 0.09));
                _hue += dt * (0.05 + _speed * 0.55);
            }
            double hueNow = isStatic ? 0 : _hue + _hueStep;

            double baseSize = Math.Min(_w, _h);
            double P = 2 * (_w + _h);
            double pump = 1 + react * (isDynamic ? 0.7 : 0.45) * _bass;
            double depth = Math.Min(0.16 * baseSize, (0.03 + _reach * 0.09) * baseSize * pump);
            double alpha = isStatic ? 0.35 + 0.45 * _bright
                : isDynamic ? Math.Min(0.95, (0.2 + 0.5 * _bright) * (0.4 + react * 1.6 * _bass))
                : Math.Min(0.9, (0.3 + 0.4 * _bright) * (0.7 + react * 0.8 * _bass));

            // perimeter ranges: top, right, bottom, left (matches the gradient directions set up in the constructor)
            double[] u0 = { 0, _w / P, (_w + _h) / P, (2 * _w + _h) / P };
            double[] u1 = { _w / P, (_w + _h) / P, (2 * _w + _h) / P, 1 };
            for (int i = 0; i < 4; i++)
            {
                var r = _rims[i];
                r.Opacity = alpha;
                var gs = _rimBrushes[i].GradientStops;
                for (int k = 0; k < gs.Count; k++)
                {
                    double u = u0[i] + (u1[i] - u0[i]) * (k / (double)(gs.Count - 1));
                    var c = PaletteAt(hueNow + _flow * 8 + u * 3);
                    gs[k].Color = Color.FromArgb(255, c.R, c.G, c.B);
                }
                if (i == 0) { r.Width = _w; r.Height = depth; Canvas.SetLeft(r, 0); Canvas.SetTop(r, 0); }
                else if (i == 2) { r.Width = _w; r.Height = depth; Canvas.SetLeft(r, 0); Canvas.SetTop(r, _h - depth); }
                else if (i == 3) { r.Width = depth; r.Height = _h; Canvas.SetLeft(r, 0); Canvas.SetTop(r, 0); }
                else { r.Width = depth; r.Height = _h; Canvas.SetLeft(r, _w - depth); Canvas.SetTop(r, 0); }
            }
        }

        private static bool TryHex(string? hex, out Color c)
        {
            c = default;
            if (hex == null || hex.Length != 7 || hex[0] != '#') return false;
            try { c = Color.FromRgb(Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16)); return true; }
            catch { return false; }
        }

        // ---------- make the window completely click-through and never active ----------
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        }

        private const int WM_NCHITTEST = 0x0084, WM_MOUSEACTIVATE = 0x0021, HTTRANSPARENT = -1, MA_NOACTIVATE = 3;
        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCHITTEST) { handled = true; return (IntPtr)HTTRANSPARENT; }
            if (msg == WM_MOUSEACTIVATE) { handled = true; return (IntPtr)MA_NOACTIVATE; }
            return IntPtr.Zero;
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
