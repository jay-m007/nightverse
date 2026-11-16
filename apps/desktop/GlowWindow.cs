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

        private const int BlobCount = 32;
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
        private double _react = 0.7, _speed = 0.35, _reach = 0.45, _bright = 0.8, _grad = 0.85;

        private double _w, _h;
        private double _flow, _hue, _bass, _kick;
        private long _seenBeats;
        private DateTime _last = DateTime.UtcNow;
        private double _dim = 1;                 // 1 = full glow, ~0.15 = faint while the cursor is near an edge / the taskbar
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
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

            // Soft fluid light: overlapping blurry blobs sitting right on the screen edge all the way round.
            // Their size and position wave slowly, so the glow ripples like liquid and fades out softly inwards.
            for (int i = 0; i < BlobCount; i++)
            {
                var brush = new RadialGradientBrush { GradientStops = new GradientStopCollection
                {
                    new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Transparent, 0.2),
                    new GradientStop(Colors.Transparent, 0.4), new GradientStop(Colors.Transparent, 0.6),
                    new GradientStop(Colors.Transparent, 0.8), new GradientStop(Colors.Transparent, 1),
                } };
                var el = new Ellipse { Fill = brush, IsHitTestVisible = false };
                _canvas.Children.Add(el);
                _blobs.Add(new Blob
                {
                    Shape = el, Brush = brush,
                    U = i / (double)BlobCount,
                    Drift = 0.6 + ((i * 37) % 10) / 10.0,
                    Phase = i * 0.22,
                    Wobble = i * 1.7,
                    Size = 0.85 + ((i * 53) % 10) / 30.0,
                });
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
            _grad = Math.Clamp(s.Gradient, 0, 1);
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

            // Stay out of the way: when the cursor goes near the screen edges or the taskbar (using other apps,
            // opening the taskbar, resizing windows) fade the light down to a faint minimum so everything stays easy to see.
            double dimTarget = 1;
            if (GetCursorPos(out var cp))
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                double mx = cp.X / dpi.DpiScaleX - Left, my = cp.Y / dpi.DpiScaleY - Top;       // relative to the glow window
                double edge = Math.Min(Math.Min(mx, _w - mx), Math.Min(my, _h - my));          // negative when over the taskbar
                double zone = Math.Max(90, 0.16 * Math.Min(_w, _h));
                if (edge < zone) dimTarget = 0.15 + 0.85 * Math.Max(0, edge) / zone;
            }
            _dim += (dimTarget - _dim) * Math.Min(1, dt * (dimTarget < _dim ? 8 : 2.5));      // dims fast, returns gently

            double baseSize = Math.Min(_w, _h);
            double reach = (0.07 + _reach * 0.27) * baseSize;
            // gradient softness: 0 = solid colour that only fades at the very end, 1 = long smooth fade from the centre
            double plateau = 0.6 * (1 - _grad);
            double[] fade = new double[6];
            for (int k = 0; k < 6; k++)
            {
                double x = k * 0.2, q = Math.Clamp((x - plateau) / (1 - plateau), 0, 1);
                fade[k] = Math.Pow(1 - q * q * (3 - 2 * q), 1.5);
            }
            double pump = 1 + react * (isDynamic ? 0.9 : 0.55) * _bass;
            double alpha = isStatic ? 0.2 + 0.36 * _bright
                : isDynamic ? Math.Min(0.8, (0.2 + 0.34 * _bright) * (0.5 + react * 1.5 * _bass))
                : Math.Min(0.62, (0.2 + 0.3 * _bright) * (0.75 + react * 0.7 * _bass));

            alpha *= _dim;
            for (int i = 0; i < _blobs.Count; i++)
            {
                var b = _blobs[i];
                double u = b.U + (isStatic ? 0 : _flow * 0.5 * b.Drift + Math.Sin(t * 0.3 + b.Wobble) * 0.012);
                var p = Perimeter(u);
                // wavy: radius and distance from the edge ripple with slow, out-of-phase sine waves
                double wave = isStatic ? 1 : 1 + 0.32 * Math.Sin(t * 0.55 + b.Wobble) + 0.12 * Math.Sin(t * 1.3 + b.Wobble * 2.1);
                double r = reach * b.Size * wave * pump;
                double inset = r * (0.30 + (isStatic ? 0 : 0.22 * Math.Sin(t * 0.45 + b.Wobble * 1.4)));  // centre sits outside the edge
                double cx = p.x - p.nx * inset, cy = p.y - p.ny * inset;
                var c = PaletteAt(b.Phase + (isStatic ? 0 : hueNow + _flow * 2));

                b.Shape.Width = r * 2; b.Shape.Height = r * 2;
                Canvas.SetLeft(b.Shape, cx - r); Canvas.SetTop(b.Shape, cy - r);
                var st = b.Brush.GradientStops;
                for (int k = 0; k < 6; k++)
                    st[k].Color = Color.FromArgb((byte)(255 * Math.Min(1, alpha * fade[k])), c.R, c.G, c.B);
                st[5].Color = Color.FromArgb(0, c.R, c.G, c.B);
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
